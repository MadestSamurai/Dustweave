namespace Dustweave;

public sealed record DailyProgress(string State, string Message, int Completed = 0, int Total = 0);
public sealed record DailyRunStatus(DateTimeOffset AtUtc, string RunId, DailyProgress Progress);
public sealed class DailyOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

// A heartbeat is not login proof. Require two fresh, advancing main-thread snapshots.
public sealed class DailyIdentityGuard
{
    private DailySnapshot? previous;
    public static bool Ready(DailySnapshot? s, GameInstance game, DateTimeOffset now)
        => s != null && s.Runtime == DailyIdentity.RuntimeName && s.ProcessId == game.ProcessId && s.ProcessStartTicks == game.StartTicks
            && s.FrameUtcTicks >= now.UtcTicks - TimeSpan.FromSeconds(5).Ticks && s.FrameUtcTicks <= now.UtcTicks + TimeSpan.FromSeconds(2).Ticks
            && s.State == "identified" && s.Startup?.Visible != true && DailyProfiles.ValidKey(s.AccountKey) && DailyProfiles.ValidKey(s.PlayerKey)
            && !string.IsNullOrEmpty(s.InstanceId) && s.Sequence > 0 && s.Capabilities?.Contains("account.identity") == true;
    public bool Observe(DailySnapshot? s, GameInstance game, string account, string expectedPlayer, DateTimeOffset now)
    {
        if (!Ready(s, game, now))
        {
            previous = null;
            return false;
        }
        if (s!.AccountKey != account)
            throw new InvalidOperationException("游戏登录的账号与目标账号不一致，已停止；请检查登录状态后重新连接。");
        if (expectedPlayer.Length > 0 && s.PlayerKey != expectedPlayer)
            throw new InvalidOperationException("该账号的游戏角色身份发生变化，已停止；请先核对登录区服和角色。");
        bool stable = previous != null && previous.InstanceId == s.InstanceId && previous.AccountKey == s.AccountKey && previous.PlayerKey == s.PlayerKey
            && s.Sequence > previous.Sequence && s.FrameUtcTicks > previous.FrameUtcTicks;
        previous = s;
        return stable;
    }
}

// Each run has one owner. A stop revokes its lease; restarting the desktop never resumes a run.
public sealed class DailyCoordinator
{
    private readonly IAccountSessions sessions; private readonly IGameHost game; private readonly DailyProfiles profiles;
    private readonly DailyTaskLedger ledger; private readonly DailyOptions options; private readonly string leasePath; private readonly string startupPath; private readonly object ownership = new(); private readonly string runPath; private string runId = "";
    private readonly Func<CancellationToken, Task>? onVerified;
    private readonly SemaphoreSlim gate = new(1, 1); private CancellationTokenSource? active;
    public event Action<DailyProgress>? Progress;
    public bool IsRunning => active != null;
    public DailyCoordinator(IAccountSessions sessions, IGameHost game, string root, DailyOptions? options = null, Func<CancellationToken, Task>? onVerified = null)
    {
        this.sessions = sessions;
        this.game = game;
        this.options = options ?? new();
        this.onVerified = onVerified;
        profiles = new(root);
        ledger = new(root);
        leasePath = Path.Combine(root, "lease.json");
        runPath = Path.Combine(root, "run.json");
        startupPath = Path.Combine(root, "startup-permit.json");
    }
    public void Stop()
    {
        lock (ownership)
        {
            active?.Cancel();
            Revoke();
        }
    }
    private void Revoke()
    {
        lock (ownership)
        {
            string root = Path.GetDirectoryName(leasePath)!;
            bool ipc = BD2.LocalIpc.DesktopFiles.Handles(leasePath);
            var current = ipc ? game.Find() : null;
            if (ipc && (!DailyTransport.Refresh(root, current) || !BD2.LocalIpc.DesktopFiles.HasLease(root)))
                return;
            try
            {
                DailyJson.Write(leasePath, new DailyLease());
                DailyJson.Write(startupPath, new StartupPermit());
            }
            catch (BD2.LocalIpc.LeaseRevokedException) { } // A newer controller already revoked this owner.
            catch (Exception e) when (ipc && (e is IOException or TimeoutException) && game.Find() != current) { } // The exact endpoint died during revocation.
        }
    }
    private void PermitStartup(DailySnapshot s, GameInstance instance, string account, string owner, CancellationToken token)
    {
        lock (ownership)
        {
            token.ThrowIfCancellationRequested();
            DailyJson.Write(startupPath, new StartupPermit { Owner = owner, ProcessId = instance.ProcessId, ProcessStartTicks = instance.StartTicks, AccountKey = account, InstanceId = s.InstanceId, ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks });
        }
    }
    private void Report(string state, string message, int done = 0, int total = 0)
    {
        var update = new DailyProgress(state, message, done, total);
        DailyJson.Write(runPath, new DailyRunStatus(DateTimeOffset.UtcNow, runId, update));
        Progress?.Invoke(update);
    }
    public Task ConnectCurrentAsync() => RunAsync(async token =>
    {
        sessions.EnsureControl();
        var catalog = sessions.Read();
        var instance = game.Find() ?? throw new InvalidOperationException("游戏没有运行，请先启动并登录。");
        bool connected = false;
        try
        {
            DailyLoginDiagnostics.Capture(Path.GetDirectoryName(runPath)!, "request", catalog, game, sessions);
            string key = catalog.CurrentKey;
            if (!DailyProfiles.ValidKey(key))
            {
                await game.ConnectAsync(instance, m => Report("connecting", m), token);
                connected = true;
                Report("waiting_login", "正在读取游戏中的登录身份；请保持游戏打开，若停在登录页请完成登录。");
                key = await CurrentGameObservation.WaitForIdentityAsync(sessions, game, instance, token, options.LoginTimeout, options.PollInterval);
                catalog = sessions.Read();
            }
            DailySandbox.RequireBoundAccount(key);
            var target = catalog.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == key)
                ?? new DailyAccount(0, "当前未保存账号", key, "", true, true, "");
            await VerifyAsync(target, instance, token, connected);
            DailyLoginDiagnostics.Capture(Path.GetDirectoryName(runPath)!, "verified", catalog, game, sessions);
        }
        catch
        {
            DailyLoginDiagnostics.Capture(Path.GetDirectoryName(runPath)!, "failed", catalog, game, sessions);
            throw;
        }
        Report("completed", "当前账号身份已核对。可继续保存账号，或选择多个账号进行登录检查。", 1, 1);
    });
    public Task InspectAsync(IEnumerable<DailyAccount> selected) => RunAsync(async token =>
    {
        var targets = selected.ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("请至少勾选一个账号。");
        if (targets.Any(a => !a.Valid || !DailyProfiles.ValidKey(a.AccountKey)))
            throw new InvalidOperationException("所选账号中有无法读取的登录会话，请先处理该账号。");
        targets = targets.DistinctBy(a => a.AccountKey).ToArray();
        sessions.EnsureControl();
        Preflight(targets);
        int done = 0;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            Preflight([target]);
            Revoke();
            var catalog = sessions.Read();
            var instance = game.Find();
            if (instance != null && catalog.CurrentKey != target.AccountKey)
            {
                Report("closing", $"{done + 1}/{targets.Length} · 正常关闭游戏，准备切换到 {target.Name}", done, targets.Length);
                await game.CloseAsync(instance, token);
                instance = null;
            }
            if (instance == null)
            {
                token.ThrowIfCancellationRequested();
                Preflight([target]);
                Report("launching", $"{done + 1}/{targets.Length} · 切换并启动 {target.Name}", done, targets.Length);
                sessions.ActivateAndLaunch(target.SlotNumber, target.AccountKey);
                var deadline = DateTimeOffset.UtcNow + options.LoginTimeout;
                while ((instance = game.Find()) == null)
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        throw new TimeoutException("启动后没有检测到游戏进程。");
                    await Task.Delay(options.PollInterval, token);
                }
            }
            await VerifyAsync(target, instance, token);
            done++;
            Report("verified", $"{target.Name} · 登录检查通过", done, targets.Length);
        }
        Report("completed", $"{done} 个账号登录检查完成。游戏停留在最后一个账号。", done, targets.Length);
    });
    private void Preflight(IEnumerable<DailyAccount> targets)
    {
        var catalog = sessions.Read();
        if (catalog.StarterRunning)
            throw new InvalidOperationException("请先关闭游戏启动器，再进行账号切换。");
        if (catalog.GameRunning && !catalog.SessionComplete && targets.Any(a => a.AccountKey != catalog.CurrentKey))
            throw new InvalidOperationException("当前游戏的登录会话尚不完整，请先完成登录并保存账号，或手动关闭游戏。");
        foreach (var target in targets)
            if (!catalog.Accounts.Any(a => a.SlotNumber == target.SlotNumber && a.AccountKey == target.AccountKey && a.Valid))
                throw new InvalidOperationException("所选槽位的账号已改变，请刷新后重新选择。");
        if (catalog.SessionComplete && catalog.Accounts.All(a => !a.Valid || a.AccountKey != catalog.CurrentKey))
            throw new InvalidOperationException("当前登录账号尚未保存。请先正常关闭游戏并保存当前账号，再开始切换。");
    }
    private async Task VerifyAsync(DailyAccount target, GameInstance instance, CancellationToken token, bool connected = false)
    {
        token.ThrowIfCancellationRequested();
        var profile = DailyAccountOrder.ProfileFor(target, profiles.Read());
        if (!connected) await game.ConnectAsync(instance, m => Report("connecting", $"{target.Name} · {m}"), token);
        var guard = new DailyIdentityGuard();
        var deadline = DateTimeOffset.UtcNow + options.LoginTimeout;
        var owner = Guid.NewGuid().ToString("N");
        Report("waiting_login", $"{target.Name} · 等待游戏登录和实际角色身份（最长 {options.LoginTimeout.TotalSeconds:0} 秒）");
        string startupMessage = "";
        bool waitingDownload = false;
        bool waitingAgreement = false;
        bool recoveryChecked = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (game.Find() != instance)
                throw new InvalidOperationException("游戏进程已退出或改变，本次检查停止。");
            var report = game.ReadStatus();
            if (report?.ProcessId == instance.ProcessId && report.ProcessStartTicks == instance.StartTicks && report.State == "error")
                throw new InvalidOperationException("组件启动失败：" + report.Error);
            var now = DateTimeOffset.UtcNow;
            var snapshot = game.ReadSnapshot();
            DailyLoginReadiness.Check(snapshot, instance, now.UtcTicks);
            if (snapshot?.State == "waiting_sdk" && startupMessage != "waiting_sdk") { startupMessage = "waiting_sdk"; Report("waiting_sdk", target.Name + " · 等待登录 SDK 初始化"); }
            if (snapshot != null && snapshot.ProcessId == instance.ProcessId && snapshot.ProcessStartTicks == instance.StartTicks && StartupPolicy.Fresh(snapshot, now.UtcTicks) && snapshot.Startup?.Visible == true)
            {
                if (snapshot.AccountKey.Length > 0 && snapshot.AccountKey != target.AccountKey)
                    throw DailyLoginFailure.Mismatch(Path.GetDirectoryName(runPath)!, runId, target, snapshot, sessions.Read(), startup: true);
                if (DailyLoginReadiness.NeedsAgreement(snapshot))
                {
                    // A human decision has no authentication timeout. Do not
                    // click terms, retry recovery, or reinterpret this as logout.
                    Revoke();
                    waitingAgreement = true;
                    deadline = now + options.LoginTimeout;
                    if (startupMessage != DailyLoginReadiness.AgreementMessage)
                    {
                        startupMessage = DailyLoginReadiness.AgreementMessage;
                        Report("waiting_start", target.Name + " · " + startupMessage);
                    }
                    await Task.Delay(options.PollInterval, token);
                    continue;
                }                var startupCatalog = sessions.Read();
                bool waitingCredentials = !startupCatalog.SessionComplete;
                if (waitingCredentials)
                {
                    // Registry flags can be transient while the SDK refreshes its session.
                    // Revoke input permission; keep observing within the existing deadline.
                    Revoke();
                    const string waitingMessage = "等待游戏完成登录或刷新凭据；恢复后会自动继续检查。";
                    if (startupMessage != waitingMessage)
                    {
                        startupMessage = waitingMessage;
                        Report("waiting_credentials", target.Name + " · " + waitingMessage);
                    }
                    if (now >= deadline)
                        throw new TimeoutException("等待登录超时：本机登录信息仍不完整或自动登录未开启。请在游戏内完成登录后重试；已保存的账号未删除。此检查不能判定服务器令牌是否过期。");
                    await Task.Delay(options.PollInterval, token);
                    continue;
                }
                if (startupCatalog.CurrentKey != target.AccountKey)
                    throw new InvalidOperationException("本机登录会话发生变化，已停止自动进入。");
                bool downloading = snapshot.Startup.DownloadVisible || snapshot.Startup.DownloadInProgress;
                if (downloading && !waitingDownload)
                {
                    deadline = now + options.DownloadTimeout;
                    waitingDownload = true;
                }
                else if (!downloading && waitingDownload)
                {
                    deadline = now + options.LoginTimeout;
                    waitingDownload = false;
                }
                if (snapshot.Startup.BlockReason.Length > 0 && !recoveryChecked && DailyLoginReadiness.CanRecover(snapshot, target.AccountKey))
                {
                    recoveryChecked = true;
                    Revoke();
                    bool recovered = await game.RecoverStartupAsync(target.AccountKey, m => Report("recovering_start", target.Name + " · " + m), token);
                    token.ThrowIfCancellationRequested();
                    if (recovered)
                    {
                        deadline = DateTimeOffset.UtcNow + options.LoginTimeout;
                        waitingDownload = false;
                        continue;
                    }
                }
                PermitStartup(snapshot, instance, target.AccountKey, owner, token);
                string message = snapshot.Startup.BlockReason.Length > 0 ? snapshot.Startup.BlockReason : snapshot.Startup.Action.Length > 0 ? snapshot.Startup.Action : snapshot.Startup.Ready ? "检测到 TOUCH TO START，准备自动进入" : "等待启动页加载 · " + snapshot.Startup.Stage;
                if (message != startupMessage)
                {
                    startupMessage = message;
                    Report("waiting_start", target.Name + " · " + message);
                }
            }
            if (waitingAgreement)
            {
                // Start a fresh bounded login wait only after the modal is gone.
                deadline = now + options.LoginTimeout;
                waitingAgreement = false;
                startupMessage = "";
            }            bool verified;
            try { verified = guard.Observe(snapshot, instance, target.AccountKey, profile.PlayerKey, now); }
            catch (InvalidOperationException) when (snapshot != null && snapshot.AccountKey != target.AccountKey)
            { throw DailyLoginFailure.Mismatch(Path.GetDirectoryName(runPath)!, runId, target, snapshot, sessions.Read(), startup: false); }
            if (verified)
            {
                var verifiedCatalog = sessions.Read();
                if (DailyProfiles.ValidKey(verifiedCatalog.CurrentKey) && verifiedCatalog.CurrentKey != target.AccountKey)
                    throw DailyLoginFailure.Mismatch(Path.GetDirectoryName(runPath)!, runId, target, snapshot!, verifiedCatalog, startup: false);
                lock (ownership)
                {
                    token.ThrowIfCancellationRequested();
                    DailyJson.Write(startupPath, new StartupPermit());
                    DailyJson.Write(leasePath, new DailyLease { Owner = owner, ProcessId = instance.ProcessId, ProcessStartTicks = instance.StartTicks, AccountKey = target.AccountKey, ExpiresUtcTicks = now.AddSeconds(10).UtcTicks });
                }
                // The runtime acknowledges the exact owner before the task is committed.
                if (snapshot!.LeaseOwner == owner)
                {
                    token.ThrowIfCancellationRequested();
                    if (onVerified != null) await onVerified(token);
                    token.ThrowIfCancellationRequested();
                    profile.SlotNumber = target.SlotNumber;
                    profile.PlayerKey = snapshot.PlayerKey;
                    profile.PlayerName = snapshot.PlayerName;
                    profile.LastVerifiedUtc = now;
                    profiles.Update(profile);
                    ledger.Record(new(target.AccountKey, snapshot.PlayerKey, now.ToString("yyyy-MM-dd"), "account.verify", "completed", now, "游戏登录身份已核对；不表示日常任务完成"));
                    return;
                }
            }
            if (now >= deadline)
                throw new TimeoutException("未能确认游戏登录身份。请检查启动页、登录弹窗、网络连接或组件状态；已停止队列，不会跳到其他账号。");
            await Task.Delay(options.PollInterval, token);
        }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!await gate.WaitAsync(0))
            throw new InvalidOperationException("已有账号操作正在进行，请先停止或等待完成。");
        active = new();
        runId = Guid.NewGuid().ToString("N");
        try
        {
            Revoke();
            Report("starting", "开始本次账号检查");
            await action(active.Token);
        }
        catch (OperationCanceledException) { Report("stopped", "已停止。保留游戏当前账号和已完成的检查记录。"); }
        catch (Exception e) { DailyJson.Write(Path.Combine(Path.GetDirectoryName(runPath)!, "run-error.json"), new { atUtc = DateTimeOffset.UtcNow, runId, error = e.ToString() }); Report("error", e.Message); throw; }
        finally { try { Revoke(); } finally { active.Dispose(); active = null; gate.Release(); } }
    }
}

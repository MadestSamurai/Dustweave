using System.Text.Json;
namespace BD2Daily;

public sealed class GuildSession
{
    private readonly IAccountSessions sessions; private readonly IGameHost host; private readonly GuildStore store; private readonly DailyProfiles profiles;
    private readonly TimeSpan poll; private CancellationTokenSource? active; private readonly object control = new(); private bool observing;
    public event Action<string>? Progress;
    public bool IsRunning => active != null;
    public string LastTrace { get; private set; } = "";
    public GuildSession(IAccountSessions sessions, IGameHost host, string root, TimeSpan? poll = null)
    {
        this.sessions = sessions;
        this.host = host;
        store = new(root);
        profiles = new(root);
        this.poll = poll ?? TimeSpan.FromMilliseconds(500);
    }
    public void Stop()
    {
        lock (control)
        {
            active?.Cancel();
            if (IsRunning && !observing)
                DailyJson.Write(Path.Combine(store.Root, "lease.json"), new DailyLease());
        }
    }
    private void RenewLease(DailyLease lease)
    {
        lock (control)
        {
            if (active == null || active.IsCancellationRequested)
                return;
            DailyJson.Write(Path.Combine(store.Root, "lease.json"), lease);
        }
    }
    private void Report(string message) => Progress?.Invoke(message);
    public string Describe(DailySnapshot? s)
    {
        if (s == null)
            return "先连接当前游戏，核对账号身份。";
        if (s.Guild != null && !s.Guild.Supported && !string.IsNullOrEmpty(s.Guild.Error))
            return s.Guild.Error;
        try
        {
            var reason = GuildPolicy.Gate(s, s.Guild == null ? null : store.Prior(s), DateTime.UtcNow.Ticks);
            return string.IsNullOrEmpty(reason) ? "可单步进入公会；进入时可能直接完成签到。" : reason;
        }
        catch (Exception e) { return "操作记录不可读，暂停提交：" + e.Message; }
    }
    private async Task<DailySnapshot> Verify(CancellationToken token)
    {
        sessions.EnsureControl();
        var c = sessions.Read();
        var instance = host.Find() ?? throw new InvalidOperationException("游戏尚未运行。");
        if (!DailyProfiles.ValidKey(c.CurrentKey))
            throw new InvalidOperationException("当前账号尚未登录。");
        var profile = profiles.Read().FirstOrDefault(p => p.AccountKey == c.CurrentKey);
        if (profile == null || string.IsNullOrEmpty(profile.PlayerKey))
            throw new InvalidOperationException("请先点击连接当前游戏，完成当前账号的身份核对。");
        var guard = new DailyIdentityGuard();
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            if (host.Find() != instance || sessions.Read().CurrentKey != c.CurrentKey)
                throw new InvalidOperationException("当前账号或游戏进程发生变化。");
            var s = host.ReadSnapshot();
            if (guard.Observe(s, instance, c.CurrentKey, profile.PlayerKey, DateTimeOffset.UtcNow))
                return s!;
            await Task.Delay(poll, token);
        }
        throw new TimeoutException("没有取得连续的新鲜身份快照，请先连接日常组件。");
    }
    public async Task ObserveAsync()
    {
        if (active != null)
            throw new InvalidOperationException("已有观察或单步操作正在运行。");
        active = new();
        observing = true;
        try
        {
            var s = await Verify(active.Token);
            var id = Guid.NewGuid().ToString("N");
            LastTrace = store.TracePath(id);
            Report("只观察中：记录已加载状态。你可以手动操作游戏；观察器不会进入公会或刷新请求。");
            long seq = -1;
            while (true)
            {
                active.Token.ThrowIfCancellationRequested();
                var current = host.ReadSnapshot();
                if (current != null && current.Sequence != seq)
                {
                    if (current.AccountKey != s.AccountKey || current.PlayerKey != s.PlayerKey || current.InstanceId != s.InstanceId)
                        throw new InvalidOperationException("观察期间账号或组件发生变化，已结束本段记录。");
                    seq = current.Sequence;
                    store.Append(id, new GuildTrace { Origin = "desktop_observer", Event = "snapshot", Snapshot = current, NowUtcTicks = DateTime.UtcNow.Ticks, Decision = Describe(current) });
                }
                await Task.Delay(poll, active.Token);
            }
        }
        catch (OperationCanceledException) { Report("观察已停止，记录已保留。没有向游戏发送任务操作。"); }
        finally { lock (control) { active.Dispose(); active = null; observing = false; } }
    }
    public async Task SingleAsync()
    {
        if (active != null)
            throw new InvalidOperationException("已有观察或单步操作正在运行。");
        active = new();
        observing = false;
        string id = "";
        bool queued = false;
        try
        {
            var s = await Verify(active.Token);
            var reason = GuildPolicy.Gate(s, store.Prior(s), DateTime.UtcNow.Ticks);
            if (reason.Length > 0)
                throw new InvalidOperationException(reason);
            if (BD2.LocalIpc.DesktopFiles.Exists(Path.Combine(store.Root, "guild-command.json")))
                throw new InvalidOperationException("已有待处理公会指令，先查看操作记录；不会覆盖。");
            var owner = Guid.NewGuid().ToString("N");
            id = Guid.NewGuid().ToString("N");
            var c = new GuildCommand { Id = id, Owner = owner, AccountKey = s.AccountKey, PlayerKey = s.PlayerKey, ServerKey = s.Guild.ServerKey, GuildKey = s.Guild.GuildKey, CycleKey = s.Guild.CycleKey, ProcessId = s.ProcessId, ProcessStartTicks = s.ProcessStartTicks, InstanceId = s.InstanceId, ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks };
            void Renew() => RenewLease(new DailyLease { Owner = owner, ProcessId = s.ProcessId, ProcessStartTicks = s.ProcessStartTicks, AccountKey = s.AccountKey, ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks });
            active.Token.ThrowIfCancellationRequested();
            store.Save(GuildStore.Intent(id, s));
            LastTrace = store.TracePath(id);
            Renew();
            GuildStore.Write(Path.Combine(store.Root, "guild-command.json"), c, false);
            queued = true;
            Report("已准备单次公会操作；等待组件接收。不会切换账号或继续其他日常。");
            var until = DateTime.UtcNow.AddSeconds(50);
            string state = "";
            while (DateTime.UtcNow < until)
            {
                active.Token.ThrowIfCancellationRequested();
                var r = store.Receipt(id) ?? throw new InvalidDataException("操作记录消失，已停止控制。");
                if (r.State != state)
                {
                    state = r.State;
                    Report(r.Message.Length > 0 ? r.Message : r.State);
                }
                if (GuildPolicy.Terminal(r.State))
                    return;
                var current = host.ReadSnapshot();
                if (current == null || host.Find()?.ProcessId != s.ProcessId || current.ProcessStartTicks != s.ProcessStartTicks || current.InstanceId != s.InstanceId || current.AccountKey != s.AccountKey || current.PlayerKey != s.PlayerKey || current.FrameUtcTicks < DateTime.UtcNow.AddSeconds(-5).Ticks)
                    throw new InvalidOperationException("游戏或身份快照改变，停止控制。已发送的操作保留待确认记录，禁止自动重试。");
                Renew();
                await Task.Delay(poll, active.Token);
            }
            throw new TimeoutException("未取得单步最终结果，已释放控制。请查看操作记录；不会重发请求。");
        }
        catch (OperationCanceledException) { Report(queued ? "已停止点击。组件会继续记录当前请求的响应；请查看操作记录并手动处理游戏界面。" : "已停止，本次没有发出公会操作。"); }
        finally { try { DailyJson.Write(Path.Combine(store.Root, "lease.json"), new DailyLease()); } finally { lock (control) { active.Dispose(); active = null; observing = false; } } }
    }
}
public sealed record GuildReplayRow(int Line, string Event, string Expected, string Actual, bool Match);
public static class GuildReplay
{
    public static IReadOnlyList<GuildReplayRow> Run(string path)
    {
        var rows = new List<GuildReplayRow>();
        int line = 0;
        foreach (var text in File.ReadLines(path))
        {
            line++;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var t = JsonSerializer.Deserialize<GuildTrace>(text, DailyJson.Options) ?? throw new InvalidDataException("Empty trace line " + line);
            if (t.Event != "decision")
                continue;
            if (t.Snapshot == null || t.Receipt == null || t.NowUtcTicks <= 0)
                throw new InvalidDataException("Incomplete decision line " + line);
            var actual = GuildPolicy.Next(t);
            rows.Add(new(line, t.Event, t.Decision, actual, t.Decision == actual));
        }
        return rows;
    }
}

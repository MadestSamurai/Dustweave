using System.Diagnostics;
using System.Text.Json;

using Dustweave.Accounts;
using Dustweave.Compatibility;
using Microsoft.Win32;
namespace Dustweave;

public sealed record DailySandboxRequest(string Box, int Slot, string Account, string Name, string GameExecutable, DateTimeOffset CreatedUtc) { public DailyParallelJob? Worker { get; init; } public string ClientKey { get; init; } = ""; public string CaptureSessionId { get; init; } = ""; public DailySandboxFileMode FileMode { get; init; } }
public sealed record DailySandboxError(DateTimeOffset AtUtc,string Request,string Message,string Error);
public sealed record DailySandboxBinding(string Box, string Account, string Name, int Slot)
{
    public DateTimeOffset ImportedSessionUtc { get; init; }
}

// Each instance keeps the established single-game pipeline inside one OS sandbox.
// Session bytes never enter the launch request or command line.
public static partial class DailySandbox
{
    public const string LaunchSwitch = "--sandbox-account";
    public static void RequireHost()
    {
        if (SandboxProcessScope.CurrentBox.Length != 0) throw new InvalidOperationException("请在普通主窗口管理账号、定时执行和软件更新。");
    }
    public static void RequireBoundAccount(string account)
    {
        if(SandboxProcessScope.CurrentBox.Length==0)return;
        var binding=Current??throw new InvalidOperationException("隔离窗口的账号绑定缺失，请从普通主窗口重新打开。");
        if(binding.Account!=account)throw new InvalidOperationException("此窗口只操作已绑定的隔离账号。");
    }
    public static void RequireAccountAvailable(string account)
    {
        RequireBoundAccount(account);
        string wanted=BoxName(account),current=SandboxProcessScope.CurrentBox;
        var games=Process.GetProcessesByName("BrownDust II");
        try
        {
            foreach(var game in games)
            {
                string box;
                try{box=SandboxProcessScope.BoxOf(game.Id);}catch(IOException)when(game.HasExited){continue;}
                bool sameAccount = box.Equals(wanted,StringComparison.OrdinalIgnoreCase);
                if (!sameAccount && box.Length != 0 && Installation() is {} installed)
                    sameAccount = SandboxConfiguration.SameRoot(SandboxConfiguration.Read(installed,box,"FileRootPath"),Path.Combine(StateDirectory(account),"root"));
                if(sameAccount&&!box.Equals(current,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("这个账号已在隔离窗口运行，请先关闭对应游戏，避免同一账号重复登录。");
            }
        }
        finally{foreach(var game in games)game.Dispose();}
    }
    public static bool HasIsolatedWindows()
    {
        RequireHost();
        var processes = Process.GetProcessesByName("Dustweave");
        try
        {
            foreach (var process in processes)
            {
                try { if (SandboxProcessScope.BoxOf(process.Id).Length != 0) return true; }
                catch (IOException) when (process.HasExited) { }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static string Store => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dustweave", "instances");
    public static string BoxName(string account)
    {
        string canonical = CanonicalBoxName(account);
        var binding = Current;
        if (binding?.Account == account) return binding.Box;
        var registered = DailyJson.TryRead<DailySandboxRequest>(Path.Combine(StateDirectory(account), "instance.json"));
        if(registered==null) {
            var removing=DailyJson.TryRead<RemovalIntent>(Path.Combine(StateDirectory(account),"removal.json"));
            if(removing?.Schema==1 && removing.Account==account && removing.Owner?.Account==account
                && removing.Owner.Box==removing.Box && SandboxConfiguration.SameRoot(removing.Root,Path.Combine(StateDirectory(account),"root")))registered=removing.Owner;
        }
        return registered?.Account == account && ValidBoxName(registered.Box) ? registered.Box : canonical;
    }
    // The data directory belongs to the account and never follows a display/configuration rename.
    public static string StateDirectory(string account) => Path.Combine(Store, CanonicalBoxName(account));
    public static string CanonicalBoxName(string account)
    {
        if (!DailyProfiles.ValidKey(account)) throw new ArgumentException("账号标识无效。");
        return "Dustweave_" + account[..20];
    }
    internal static bool ValidBoxName(string box) => !string.IsNullOrWhiteSpace(box) && box.Length <= 32 && box.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    public static string? Installation() => new SandboxInstallation().ResolveOptional();
    private static string BindingPath => Path.Combine(DailyIdentity.DataRoot, "sandbox-binding.json");
    public static DailySandboxBinding? Current
    {
        get
        {
            string box = SandboxProcessScope.CurrentBox;
            if (box.Length == 0) return null;
            var binding = DailyJson.TryRead<DailySandboxBinding>(BindingPath);
            return binding != null && string.Equals(binding.Box, box, StringComparison.OrdinalIgnoreCase) && DailyProfiles.ValidKey(binding.Account) && ValidBoxName(box) ? binding : null;
        }
    }
    internal static void Validate(DailySandboxRequest request)
    {
        if(!Enum.IsDefined(request.FileMode))throw new InvalidDataException("sandbox.manage.mode_invalid");
        if (!string.Equals(request.Box, BoxName(request.Account), StringComparison.OrdinalIgnoreCase) || request.Slot is < 1 or > 100 || string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidDataException("隔离实例请求不完整。");
        if (!Path.IsPathFullyQualified(request.GameExecutable) || !Path.GetFileName(request.GameExecutable).Equals("BrownDust II.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("游戏路径不符合预期。");
    }
    internal static readonly SemaphoreSlim ConfigurationGate = new(1, 1);
    public static async Task<string> LaunchAsync(DailyAccount account, string executable, Action<string> progress, CancellationToken token, DailyParallelJob? worker = null,string captureSessionId="")
    {
        await ConfigurationGate.WaitAsync(token);
        try { return await LaunchCoreAsync(account, executable, progress, token, worker,captureSessionId); }
        finally { ConfigurationGate.Release(); }
    }
    // Open the existing account workspace only. No credential import, bootstrap or game launch.
    public static async Task OpenTasksAsync(string account, string executable, CancellationToken token)
    {
        RequireHost();
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !DailyApplication.IsExecutable(executable)) throw new InvalidOperationException("run.account_unavailable");
        string installation = Installation() ?? throw new InvalidOperationException("parallel.install_required");
        SandboxProcessScope.RequireAvailable();
        await ConfigurationGate.WaitAsync(token);
        try
        {
            await DailySandboxRegistration.EnsureAsync(account, token);
            string box = BoxName(account), state = StateDirectory(account);
            var owned = DailyJson.TryRead<DailySandboxRequest>(Path.Combine(state, "instance.json"));
            string actual = (await Run(installation, "SbieIni.exe", ["query", box, "FileRootPath"], token)).Trim();
            if (owned?.Account != account || !string.Equals(owned.Box, box, StringComparison.OrdinalIgnoreCase) || !SandboxConfiguration.SameRoot(actual, Path.Combine(state, "root")))
                throw new InvalidOperationException("parallel.identity_changed");
            foreach (string setting in new[] { "OpenKeyPath", "OpenIpcPath", "OpenPipePath", "OpenFilePath", "BreakoutProcess", "BreakoutFolder", "BreakoutDocument" })
                if (!string.IsNullOrWhiteSpace(await Run(installation, "SbieIni.exe", ["query", box, setting], token)))
                    throw new InvalidOperationException("parallel.identity_changed");
            var windows = Process.GetProcessesByName("Dustweave");
            try
            {
                foreach (var process in windows)
                {
                    if (process.HasExited || !string.Equals(SandboxProcessScope.BoxOf(process.Id), box, StringComparison.OrdinalIgnoreCase)) continue;
                    if (process.MainWindowHandle == 0) throw new InvalidOperationException("parallel.tasks_busy");
                    DailyParallelRuntime.ShowWindow(process.MainWindowHandle);
                    return;
                }
            }
            finally { foreach (var process in windows) process.Dispose(); }
            // A persistent UI must not inherit captured stdout/stderr handles from the launcher.
            var start = new ProcessStartInfo(Path.Combine(installation, "Start.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Directory.GetCurrentDirectory() };
            start.Environment["DUSTWEAVE_PLUGIN"] = DailyPlugin.Current.Available ? DailyPlugin.Current.Root : "none";
            foreach (string argument in new[] { "/box:" + box, "/silent", executable, "--view-account", account }) start.ArgumentList.Add(argument);
            using var launcher = Process.Start(start) ?? throw new IOException("parallel.window_open");
            await launcher.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
            if (launcher.ExitCode != 0) throw new IOException("parallel.window_open");
        }
        finally { ConfigurationGate.Release(); }
    }
    private static async Task<string> LaunchCoreAsync(DailyAccount account, string executable, Action<string> progress, CancellationToken token, DailyParallelJob? worker,string captureSessionId)
    {
        RequireHost();
        DailyLoadingExperimentLaunch.RequireIsolatedSession(captureSessionId);
        RequireNotRemoving(StateDirectory(account.AccountKey));
        string installation = Installation() ?? throw new InvalidOperationException("parallel.install_required");
        SandboxProcessScope.RequireAvailable();
        var vault = new SessionVault();
        var saved = SessionService.LatestSameAccount(vault.LoadFixedSlot(account.SlotNumber), vault.TryLoadFixedSlot);
        if (!account.Valid || DailyIdentity.MemberKey(SessionIdentity.GetMemberId(saved) ?? "") != account.AccountKey)
            throw new InvalidOperationException("所选账号已经改变，请刷新后重试。");
        using var sessions = new AccountSessions();
        var status = sessions.Read();
        if (status.GameRunning && (!status.SessionComplete || status.CurrentKey == account.AccountKey))
            throw new InvalidOperationException("这个账号正在普通游戏实例中运行，请先关闭该实例，避免同一账号重复登录。");
        if (status.StarterRunning) throw new InvalidOperationException("请先关闭游戏启动器，再进行账号交接。");
        new SessionService(vault).SynchronizeCurrentSlot();
        await DailySandboxSessions.CollectAsync(account.AccountKey,vault,executable,token);
        saved = SessionService.LatestSameAccount(vault.LoadFixedSlot(account.SlotNumber),vault.TryLoadFixedSlot);
        string box = BoxName(account.AccountKey);
        string state = StateDirectory(account.AccountKey); Directory.CreateDirectory(state);
        string root = Path.Combine(state,"root");
        string marker = Path.Combine(state,"instance.json");
        var request = new DailySandboxRequest(box,account.SlotNumber,account.AccountKey,account.Name,GameLauncher.ResolveExecutable(),DateTimeOffset.UtcNow) { Worker = worker,CaptureSessionId=captureSessionId,FileMode=DailySandboxPreferences.Read().Mode };
        request = request with { ClientKey = await Task.Run(() => ClientInputs.ReadGame(request.GameExecutable), token) };
        Validate(request);
        if (worker != null) { worker.Validate(); if (worker.Account != account.AccountKey) throw new InvalidDataException("parallel.identity_changed"); }
        await PrepareSpaceAsync(request,executable,progress,token);
        token.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            DailyAccountProgress.ImportSandbox(DailyIdentity.DataRoot, account.AccountKey, force: true);
            DailyAccountProgress.ExportSandbox(DailyIdentity.DataRoot, account.AccountKey);
        }, token);
        string requests=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","instance-requests"); Directory.CreateDirectory(requests);
        string requestPath=Path.Combine(requests,"launch-"+Guid.NewGuid().ToString("N")+".json"); DailyJson.Write(requestPath,request);
        string sessionPath = Path.ChangeExtension(requestPath, ".bd2slot");

        progress("正在打开独立窗口…");
        try { DailyLoadingExperimentLaunch.RequireIsolatedSession(captureSessionId);vault.WriteLaunchSnapshot(saved, sessionPath); await Run(installation,"Start.exe",["/box:"+box,"/silent","/wait",Path.GetFullPath(executable),LaunchSwitch,requestPath],token); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            string errorPath=Path.Combine(root,"user","current","AppData","Local","BD2DailyAssistant","sandbox-error.json");
            var failure=DailyJson.TryRead<DailySandboxError>(errorPath);
            if(failure?.Request==Path.GetFileName(requestPath)&&failure.AtUtc>=request.CreatedUtc)
                throw new InvalidOperationException(failure.Message);
            throw;
        }
        finally { File.Delete(requestPath); File.Delete(sessionPath); }
        return box;
    }
    public static DailySandboxBinding Bootstrap(string requestPath)
    {
        using var gate = new Mutex(false, @"Local\Dustweave-SandboxBootstrap");
        bool held;
        try { held = gate.WaitOne(TimeSpan.Zero); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new InvalidOperationException("此隔离实例正在启动，请稍候。");
        try { return BootstrapCore(requestPath); } finally { gate.ReleaseMutex(); }
    }
    private static DailySandboxBinding BootstrapCore(string requestPath)
    {
        var request=JsonSerializer.Deserialize<DailySandboxRequest>(File.ReadAllText(requestPath))??throw new InvalidDataException("隔离启动请求无法读取。"); Validate(request);
        DailyLoadingExperimentLaunch.RequireIsolatedSession(request.CaptureSessionId);
        SandboxProcessScope.Require(request.Box);
        if(DateTimeOffset.UtcNow-request.CreatedUtc>TimeSpan.FromMinutes(10)||request.CreatedUtc>DateTimeOffset.UtcNow.AddMinutes(1))throw new InvalidDataException("隔离启动请求已过期，请从主窗口重新打开。");
        if (request.Worker != null)
        {
            request.Worker.Validate();
            if (request.Worker.Account != request.Account) throw new InvalidDataException("parallel.identity_changed");
            using var available = new Mutex(false, DailyApplication.InstanceMutex);
            bool held; try { held = available.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("parallel.window_open");
            available.ReleaseMutex();
        }
        string actualClient = ClientInputs.ReadGame(request.GameExecutable);
        DailyJson.Write(Path.Combine(DailyIdentity.DataRoot,"client-update-check.json"),new { atUtc=DateTimeOffset.UtcNow, expected=request.ClientKey, actual=actualClient, matches=request.ClientKey==actualClient });
        ClientInputs.RequireSame(request.ClientKey, actualClient);
        // Refresh the sandbox's copy from the validated host request, never an old virtualized choice.
        new GameInstallation().Select(request.GameExecutable);
        var old=Current;
        if(old!=null&&old.Account!=request.Account)throw new InvalidOperationException("隔离空间已绑定其他账号。");
        var vault=new SessionVault(); var saved=vault.ReadLaunchSnapshot(Path.ChangeExtension(requestPath,".bd2slot"));
        if(DailyIdentity.MemberKey(SessionIdentity.GetMemberId(saved)??"")!=request.Account)throw new InvalidOperationException("账号槽位已变化，未写入登录信息。");
        // Refresh the sandbox's copy only after validating the host request, client and account identity.
        new GameInstallation().Select(request.GameExecutable);
        DateTimeOffset imported = old?.ImportedSessionUtc ?? default;
        var games=SandboxProcessScope.Find("BrownDust II");
        try
        {
            if(games.Length>1)throw new InvalidOperationException("同一隔离空间中存在多个游戏，请保留一个。");
            var state=SessionRegistry.GetStatus();
            string current=state.Complete?DailyIdentity.MemberKey(SessionIdentity.GetMemberId(SessionRegistry.ReadCurrent("sandbox-identity"))??""):"";
            if(games.Length>0&&current!=request.Account)throw new InvalidOperationException("运行中游戏的账号不一致，未改写会话。");
            if(games.Length==0)
            {
                // Only a verified bootstrap in the driver-reported sandbox can write.
                using(var key=Registry.CurrentUser.CreateSubKey(SessionConstants.RegistrySubKey)) { }
                new SessionService(vault).SynchronizeCurrentSlot();
                SessionSlot? local;
                try { local = vault.TryLoadFixedSlot(request.Slot); }
                catch(Exception error) when(error is System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or SessionManagerException or System.Text.Json.JsonException)
                { local = null; } // The already verified host request replaces an unreadable old Windows copy.
                if (local != null && SessionIdentity.GetMemberId(local) == SessionIdentity.GetMemberId(saved))
                {
                    local = SessionService.LatestSameAccount(local,vault.TryLoadFixedSlot);
                    if (state.Complete && current == request.Account && local.CapturedAtUtc > saved.CapturedAtUtc) saved = local;
                }
                if (old != null && !state.Complete)
                {
                    var identity = SessionRegistry.ReadAuthenticationIdentity();
                    var observed = vault.ReadObserved();
                    if (identity.Member.Length > 0 && DailyIdentity.MemberKey(identity.Member) != request.Account)
                        throw new InvalidOperationException("隔离窗口已登录其他账号，未导出凭据。");
                    if (observed != null && DailyIdentity.MemberKey(SessionIdentity.GetMemberId(observed) ?? "") != request.Account) observed = null;
                    DailySandboxSessions.RequireFreshReplacement(saved,
                        identity.Stamp.Length > 0 ? identity.Stamp : observed == null ? "" : SessionRegistry.AuthenticationStamp(observed),
                        observed?.CapturedAtUtc ?? old.ImportedSessionUtc);
                }
                if(ShouldImportSession(old, saved.CapturedAtUtc, state.Complete, current, request.Account))
                {
                    SessionRegistry.WriteAndVerify(saved);
                    vault.SaveFixedSlot(request.Slot, saved, request.Name, replace:true);
                    vault.RememberObserved(saved);
                    imported = saved.CapturedAtUtc;
                }
                // The agreement can change without a token rotation. Restore only
                // an actual same-client record bound to the selected account.
                SessionRegistry.RestoreAgreement(saved);
                if(SessionRegistry.HasPendingLauncherToken())throw new InvalidOperationException("仍有启动器登录请求，已停止隔离启动。");
                DailyLoadingExperimentLaunch.RequireIsolatedSession(request.CaptureSessionId);
                DailySandboxSessions.WriteAudit(request.Account, "bootstrap", SessionRegistry.ReadAuthenticationIdentity().Stamp, SessionRegistry.AuthenticationStamp(saved));
                GameLauncher.ValidateLaunchContext(request.GameExecutable);
                // Updates can restore the Google Play channel. Prepare the
                // direct-PC channel inside this box, with its own exact backup.
                int launchedPid=GameLauncher.LaunchDirect(request.GameExecutable);
                if(request.CaptureSessionId.Length>0){
                    using var launched=Process.GetProcessById(launchedPid);
                    DailyJson.Write(Path.Combine(DailyIdentity.DataRoot,"loading-experiment-game.json"),new{id=request.CaptureSessionId,pid=launchedPid,start=launched.StartTime.ToUniversalTime().Ticks,box=request.Box,executable=request.GameExecutable});
                }
            }
        }
        finally { foreach(var game in games)game.Dispose(); }
        var binding=new DailySandboxBinding(request.Box,request.Account,request.Name,request.Slot) { ImportedSessionUtc=imported };
        DailyJson.Write(BindingPath,binding);
        if (request.Worker != null)
            DailyJson.Write(Path.Combine(DailyParallelRuntime.WorkerDirectory(DailyIdentity.DataRoot, request.Worker.Id), "job.json"), request.Worker with { StartedGame = games.Length == 0 });
        return binding;
    }
    internal static bool ShouldImportSession(DailySandboxBinding? binding, DateTimeOffset incomingUtc, bool complete, string current, string expected)
        => binding == null || !complete || current != expected || incomingUtc > binding.ImportedSessionUtc;

    internal static async Task<string> Run(string installation,string file,IEnumerable<string> args,CancellationToken token,TimeSpan? timeout=null)
    {
        string[] arguments = args.ToArray();
        if (file.Equals("SbieIni.exe", StringComparison.OrdinalIgnoreCase) && arguments is ["query", var section, var setting])
        {
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => SandboxConfiguration.Read(installation, section, setting), token).ConfigureAwait(false);
        }
        var start=new ProcessStartInfo(Path.Combine(installation,file)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Directory.GetCurrentDirectory(),RedirectStandardOutput=true,RedirectStandardError=true};
        // Pin the parent's immutable plugin version for isolated account workers too.
        start.Environment["DUSTWEAVE_PLUGIN"]=DailyPlugin.Current.Available?DailyPlugin.Current.Root:"none";
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Sandboxie 未启动。");
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token).WaitAsync(timeout??TimeSpan.FromSeconds(20),token).ConfigureAwait(false); }
        catch { if(!process.HasExited)process.Kill();throw; }
        string text=await output.ConfigureAwait(false), detail=await error.ConfigureAwait(false);
        if(process.ExitCode!=0)throw new IOException($"Sandboxie 操作失败（{process.ExitCode}）：{detail.Trim()}");
        return text;
    }
}

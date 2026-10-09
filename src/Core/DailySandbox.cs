using System.Diagnostics;
using System.Text.Json;

using Dustweave.Accounts;
using Dustweave.Compatibility;
using Microsoft.Win32;
namespace Dustweave;

public sealed record DailySandboxRequest(string Box, int Slot, string Account, string Name, string GameExecutable, DateTimeOffset CreatedUtc) { public DailyParallelJob? Worker { get; init; } public string ClientKey { get; init; } = ""; }
public sealed record DailySandboxError(DateTimeOffset AtUtc,string Request,string Message,string Error);
public sealed record DailySandboxBinding(string Box, string Account, string Name, int Slot)
{
    public DateTimeOffset ImportedSessionUtc { get; init; }
}

// Each instance keeps the established single-game pipeline inside one OS sandbox.
// Session bytes never enter the launch request or command line.
public static class DailySandbox
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
                if(box==wanted&&box!=current)throw new InvalidOperationException("这个账号已在隔离窗口运行，请先关闭对应游戏，避免同一账号重复登录。");
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
        if (!DailyProfiles.ValidKey(account)) throw new ArgumentException("账号标识无效。");
        return "Dustweave_" + account[..20];
    }
    public static string? Installation()
    {
        foreach (var name in new[] { "Sandboxie-Plus", "Sandboxie" })
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name);
            if (File.Exists(Path.Combine(path,"Start.exe")) && File.Exists(Path.Combine(path,"SbieIni.exe"))) return path;
        }
        return null;
    }
    private static string BindingPath => Path.Combine(DailyIdentity.DataRoot, "sandbox-binding.json");
    public static DailySandboxBinding? Current
    {
        get
        {
            string box = SandboxProcessScope.CurrentBox;
            if (box.Length == 0) return null;
            var binding = DailyJson.TryRead<DailySandboxBinding>(BindingPath);
            return binding != null && binding.Box == box && DailyProfiles.ValidKey(binding.Account) && BoxName(binding.Account) == box ? binding : null;
        }
    }
    internal static void Validate(DailySandboxRequest request)
    {
        if (request.Box != BoxName(request.Account) || request.Slot is < 1 or > 100 || string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidDataException("隔离实例请求不完整。");
        if (!Path.IsPathFullyQualified(request.GameExecutable) || !Path.GetFileName(request.GameExecutable).Equals("BrownDust II.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("游戏路径不符合预期。");
    }
    private static readonly SemaphoreSlim configurationGate = new(1, 1);
    public static async Task<string> LaunchAsync(DailyAccount account, string executable, Action<string> progress, CancellationToken token, DailyParallelJob? worker = null)
    {
        await configurationGate.WaitAsync(token);
        try { return await LaunchCoreAsync(account, executable, progress, token, worker); }
        finally { configurationGate.Release(); }
    }
    // Open the existing account workspace only. No credential import, bootstrap or game launch.
    public static async Task OpenTasksAsync(string account, string executable, CancellationToken token)
    {
        RequireHost();
        string box = BoxName(account);
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !DailyApplication.IsExecutable(executable)) throw new InvalidOperationException("run.account_unavailable");
        string installation = Installation() ?? throw new InvalidOperationException("parallel.install_required");
        await configurationGate.WaitAsync(token);
        try
        {
            string state = Path.Combine(Store, box);
            var owned = DailyJson.TryRead<DailySandboxRequest>(Path.Combine(state, "instance.json"));
            string actual = (await Run(installation, "SbieIni.exe", ["query", box, "FileRootPath"], token)).Trim();
            if (owned?.Account != account || owned.Box != box || !string.Equals(actual, Path.Combine(state, "root"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("parallel.identity_changed");
            foreach (string setting in new[] { "OpenKeyPath", "OpenIpcPath", "OpenPipePath", "OpenFilePath", "BreakoutProcess", "BreakoutFolder", "BreakoutDocument" })
                if (!string.IsNullOrWhiteSpace(await Run(installation, "SbieIni.exe", ["query", box, setting], token)))
                    throw new InvalidOperationException("parallel.identity_changed");
            var windows = Process.GetProcessesByName("Dustweave");
            try
            {
                foreach (var process in windows)
                {
                    if (process.HasExited || SandboxProcessScope.BoxOf(process.Id) != box) continue;
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
        finally { configurationGate.Release(); }
    }
    private static async Task<string> LaunchCoreAsync(DailyAccount account, string executable, Action<string> progress, CancellationToken token, DailyParallelJob? worker)
    {
        RequireHost();
        string installation = Installation() ?? throw new InvalidOperationException("尚未安装 Sandboxie-Plus，请先完成它的服务和驱动安装。");
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
        string state = Path.Combine(Store,box); Directory.CreateDirectory(state);
        string root = Path.Combine(state,"root");
        string marker = Path.Combine(state,"instance.json");
        var request = new DailySandboxRequest(box,account.SlotNumber,account.AccountKey,account.Name,GameLauncher.ResolveExecutable(),DateTimeOffset.UtcNow) { Worker = worker };
        request = request with { ClientKey = await Task.Run(() => ClientInputs.ReadGame(request.GameExecutable), token) };
        Validate(request);
        if (worker != null) { worker.Validate(); if (worker.Account != account.AccountKey) throw new InvalidDataException("parallel.identity_changed"); }
        // A previously registered box is never silently reconfigured while running.
        var existing = await Run(installation,"SbieIni.exe",["query","*"],token);
        bool configured = existing.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Any(x=>x.Trim()==box);
        if (configured)
        {
            var owned = DailyJson.TryRead<DailySandboxRequest>(marker);

            string actual = (await Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token)).Trim();
            if(actual.Length==0)
            {
                var pids=(await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
                if(pids.Length!=1||pids[0].Trim()!="0")throw new InvalidOperationException("隔离空间尚未配置完整，但仍有程序运行，请先关闭该空间中的程序。");
                configured=false;
            }
            else
            {
                if (owned == null || owned.Account != request.Account || owned.Box != box) throw new InvalidOperationException("同名沙箱不是由此账号登记的，未修改它。");
                if (!string.Equals(actual,root,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("隔离目录已被修改，请先检查 Sandboxie 设置。");
            }
        }
        if(!configured)
        {
            // Record ownership before configuration so an interrupted setup is repairable.
            DailyJson.Write(marker,request);
            progress("准备独立账号空间…");
            var settings = new Dictionary<string,string> {
                ["Enabled"]="y", ["ConfigLevel"]="10", ["FileRootPath"]=root,
                ["AutoDelete"]="n", ["BoxNameTitle"]="y", ["HideOtherBoxes"]="y", ["HideNonSystemProcesses"]="y", ["BorderColor"]="#748FA5,ttl"
            };
            foreach (var pair in settings) await Run(installation,"SbieIni.exe",["set",box,pair.Key,pair.Value],token);
            // Keep normal game preferences through copy-on-write. The bootstrap writes
            // and verifies all account fields before any game is allowed to start.
            string low=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Gamfs","BrownDust II");
            foreach(var child in new[]{"Vuplex.WebView","Neo","args.info"})
                await Run(installation,"SbieIni.exe",["append",box,"WriteFilePath",Path.Combine(low,child)],token);
            DailyJson.Write(marker,request);
        }
        foreach(string setting in new[]{"OpenKeyPath","OpenIpcPath","OpenPipePath","OpenFilePath","BreakoutProcess","BreakoutFolder","BreakoutDocument"})
            if(!string.IsNullOrWhiteSpace(await Run(installation,"SbieIni.exe",["query",box,setting],token)))
                throw new InvalidOperationException("隔离空间包含额外的共享或退出隔离设置，请先移除它们："+setting);
        // Legacy integrated tools enumerate by game name before checking the shared
        // connection descriptor. Keep unrelated host/box games out of that view.
        foreach(string setting in new[]{"HideOtherBoxes","HideNonSystemProcesses"})
            if((await Run(installation,"SbieIni.exe",["query",box,setting],token)).Trim()!="y")
                await Run(installation,"SbieIni.exe",["set",box,setting,"y"],token);
        // MacType also hooks CreateProcessInternalW; its delayed injection can retain
        // a stale Sandboxie trampoline. Restrict this compatibility exclusion to our
        // executable in this box, preserving the user's global font settings.
        var closed=(await Run(installation,"SbieIni.exe",["query",box,"ClosedFilePath"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        foreach(var dll in new[]{"MacType64.dll","MacType.dll"})
        {
            string rule=Path.GetFileName(executable)+",*\\"+dll;
            if(!closed.Contains(rule,StringComparer.OrdinalIgnoreCase))await Run(installation,"SbieIni.exe",["append",box,"ClosedFilePath",rule],token);
        }
        // The host owns its automation lock while scheduling. A fresh sandbox must
        // create its own lock instead of attempting to copy the host's open file.
        string controlLock = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant", "tools", "control.lock");
        var privateFiles = (await Run(installation,"SbieIni.exe",["query",box,"WriteFilePath"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if (!privateFiles.Contains(controlLock,StringComparer.OrdinalIgnoreCase))
            await Run(installation,"SbieIni.exe",["append",box,"WriteFilePath",controlLock],token);
        token.ThrowIfCancellationRequested();
        string requests=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","instance-requests"); Directory.CreateDirectory(requests);
        string requestPath=Path.Combine(requests,"launch-"+Guid.NewGuid().ToString("N")+".json"); DailyJson.Write(requestPath,request);
        string sessionPath = Path.ChangeExtension(requestPath, ".bd2slot");

        progress("正在打开独立窗口…");
        try { vault.WriteLaunchSnapshot(saved, sessionPath); await Run(installation,"Start.exe",["/box:"+box,"/silent","/wait",Path.GetFullPath(executable),LaunchSwitch,requestPath],token); }
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
                var local = vault.TryLoadFixedSlot(request.Slot);
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
                DailySandboxSessions.WriteAudit(request.Account, "bootstrap", SessionRegistry.ReadAuthenticationIdentity().Stamp, SessionRegistry.AuthenticationStamp(saved));
                GameLauncher.ValidateLaunchContext(request.GameExecutable);
                // Updates can restore the Google Play channel. Prepare the
                // direct-PC channel inside this box, with its own exact backup.
                GameLauncher.LaunchDirect(request.GameExecutable);
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

    internal static async Task<string> Run(string installation,string file,IEnumerable<string> args,CancellationToken token)
    {
        var start=new ProcessStartInfo(Path.Combine(installation,file)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Directory.GetCurrentDirectory(),RedirectStandardOutput=true,RedirectStandardError=true};
        // Pin the parent's immutable plugin version for isolated account workers too.
        start.Environment["DUSTWEAVE_PLUGIN"]=DailyPlugin.Current.Available?DailyPlugin.Current.Root:"none";
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Sandboxie 未启动。");
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(20),token).ConfigureAwait(false); }
        catch { if(!process.HasExited)process.Kill();throw; }
        string text=await output.ConfigureAwait(false), detail=await error.ConfigureAwait(false);
        if(process.ExitCode!=0)throw new IOException($"Sandboxie 操作失败（{process.ExitCode}）：{detail.Trim()}");
        return text;
    }
}

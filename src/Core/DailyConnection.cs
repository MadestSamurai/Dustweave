using System.Diagnostics;
using System.Security.Cryptography;
using BD2Daily.Compatibility;
using SharpMonoInjector;
namespace BD2Daily;

public sealed record GameInstance(int ProcessId, long StartTicks, string Executable);
public interface IGameHost
{
    GameInstance? Find();
    Task CloseAsync(GameInstance instance, CancellationToken cancellation);
    Task ConnectAsync(GameInstance instance, Action<string> progress, CancellationToken cancellation);
    Task<bool> RecoverStartupAsync(string account, Action<string> progress, CancellationToken cancellation) => Task.FromResult(false);
    DailySnapshot? ReadSnapshot();
    DailyRuntimeStatus? ReadStatus();
}
public sealed class DailyConnectionState
{
    public int ProcessId
    {
        get; set;
    }
    public long StartTicks
    {
        get; set;
    }
    public string Fingerprint { get; set; } = ""; public string PayloadSha { get; set; } = ""; public long Address
    {
        get; set;
    }
}
public sealed class DailyGameHost : IGameHost
{
    private readonly string root;
    public DailyGameHost(string? root = null)
    {
        this.root = root ?? DailyIdentity.DataRoot;
        DailyTransport.Reader(this.root);
    }
    public GameInstance? Find()
    {
        var games = Process.GetProcessesByName("BrownDust II");
        try
        {
            if (games.Length > 1)
                throw new InvalidOperationException("检测到多个游戏进程，请只保留一个。");
            if (games.Length == 0)
                return null;
            var p = games[0];
            return DailyConnectionAccess.Describe(p.Id);
        }
        finally { foreach (var p in games) p.Dispose(); }
    }
    public async Task CloseAsync(GameInstance instance, CancellationToken cancellation)
    {
        using var p = Process.GetProcessById(instance.ProcessId);
        if (p.StartTime.ToUniversalTime().Ticks != instance.StartTicks)
            throw new InvalidOperationException("游戏进程已变化，请重试。");
        cancellation.ThrowIfCancellationRequested();
        if (!p.CloseMainWindow())
            throw new InvalidOperationException("游戏暂时无法正常关闭，请手动关闭后重试；不会强制结束进程。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("游戏尚未退出，请处理游戏中的确认窗口后重试。账号会话尚未改写。"); }
    }
    private bool ReadReady()
    {
        var current = Find();
        return DailyTransport.Refresh(root, current) && DailyTransport.EndpointReady(current!);
    }
    public DailySnapshot? ReadSnapshot() => ReadReady() ? DailyJson.TryRead<DailySnapshot>(Path.Combine(root, "snapshot.json")) : null;
    public DailyRuntimeStatus? ReadStatus() => ReadReady() ? DailyJson.TryRead<DailyRuntimeStatus>(Path.Combine(root, "runtime.json")) : null;
    public async Task ConnectAsync(GameInstance instance, Action<string> progress, CancellationToken cancellation)
    {
        var path = Path.Combine(root, "connection.json");
        var pipe = DailyTransport.Bind(root, instance);
        try
        {
            if (pipe.Fingerprint() == DailyHookCompiler.Fingerprint)
            {
                pipe.Open(DailyHookCompiler.Fingerprint);
                progress("组件已连接，等待主线程与登录状态");
                return;
            }
        }
        catch (IOException) { }
        catch (TimeoutException) { }
        var managed = Path.Combine(Path.GetDirectoryName(instance.Executable)!, Path.GetFileNameWithoutExtension(instance.Executable) + "_Data", "Managed");
        progress("检查本机接口并准备账户识别组件");
        var prepared = DailySuite.Prepare!=null ? await DailySuite.Prepare(managed,progress,cancellation) : await Task.Run(() => DailyHookCompiler.Prepare(managed), cancellation);
        DailyJson.Write(Path.Combine(root, "compatibility.json"), prepared.Report);
        DailyJson.Write(Path.Combine(root, "guild-compatibility.json"), prepared.GuildReport);
        DailyJson.Write(Path.Combine(root, "startup-compatibility.json"), prepared.StartupReport);
        cancellation.ThrowIfCancellationRequested();
        if (Find() != instance)
            throw new InvalidOperationException("准备组件期间游戏进程已变化。");
        var state = new DailyConnectionState { ProcessId = instance.ProcessId, StartTicks = instance.StartTicks, Fingerprint = DailyHookCompiler.Fingerprint, PayloadSha = Convert.ToHexString(SHA256.HashData(prepared.Payload)) };
        DailyJson.Write(path, state);
        state.Address = await DailyConnectionAccess.InjectAsync(instance, prepared.Payload, progress, cancellation);
        DailyJson.Write(path, state);
        var ready = DateTimeOffset.UtcNow.AddSeconds(35);
        while (DateTimeOffset.UtcNow < ready)
        {
            cancellation.ThrowIfCancellationRequested();
            var status = ReadStatus();
            if (status?.State == "error")
                throw new InvalidOperationException(status.Error);
            try
            {
                if (status?.State == "active" && pipe.Fingerprint() == DailyHookCompiler.Fingerprint)
                {
                    pipe.Open(DailyHookCompiler.Fingerprint);
                    return;
                }
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            await Task.Delay(200, cancellation);
        }
        throw new TimeoutException("等待日常组件交接超时，请完成待结算操作后重新连接。");
    }
    public async Task<bool> RecoverStartupAsync(string account, Action<string> progress, CancellationToken cancellation)
    {
        if (!DailyProfiles.ValidKey(account))
            throw new InvalidOperationException("启动恢复缺少目标账号。");
        var directory = ResolvePackageDirectory(AppContext.BaseDirectory);
        string live = Path.Combine(root, "live");

        async Task<int> Invoke(string relative, IEnumerable<string> arguments)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = DailyTools.StartInfo(directory, relative);
            start.Environment["BD2_DAILY_DATA_ROOT"] = root;
            start.Environment["DUSTWEAVE_PLUGIN"] = DailyPlugin.Current.Available ? DailyPlugin.Current.Root : "none";
            foreach (var arg in arguments)
                start.ArgumentList.Add(arg);
            using var p = Process.Start(start) ?? throw new IOException("启动恢复组件未启动。");
            var normal = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            // Bound the helper lifetime even if the game exits during an IPC handoff.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                await p.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("启动连接准备超时；原游戏进程与操作记录保留。"); }
            finally { if (!p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
            string stdout = await normal, stderr = await error;
            cancellation.ThrowIfCancellationRequested();
            if (p.ExitCode != 0)
                throw new InvalidOperationException("启动恢复已停止：" + (stderr.Length > 0 ? stderr : stdout));
            return p.ExitCode;
        }
        progress("检查资源下载错误并准备有限重试");
        await Invoke("live", ["ready"]);
        var current = Find() ?? throw new IOException("启动恢复前游戏已退出。");
        try
        {
            return await DailyStartupRecovery.Run(root, current, account, progress, cancellation);
        }
        finally { if (Find() == current) { var resumed = DailyTransport.Bind(root, current); resumed.Open(DailyHookCompiler.Fingerprint); } }
    }
    internal static string ResolvePackageDirectory(string directory)
    {
        // The GUI lives at package root; the CLI lives in its connection folder.
        var current = DailyTools.PackageDirectory(directory);
        if (File.Exists(Path.Combine(current, "connection", "BD2Daily.Live.exe")))
            return current;
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(current));
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(current)).Equals("connection", StringComparison.OrdinalIgnoreCase) && parent != null && File.Exists(Path.Combine(parent.FullName, "BD2DailyAssistant.exe")))
            return parent.FullName;
        return current;
    }
}


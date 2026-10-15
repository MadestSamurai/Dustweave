extern alias apostle;
using A = apostle::BD2ApostleDefense;
using System.IO;
using System.Text.Json;

namespace Dustweave.Desktop;

internal interface IApostleToolPort : A.IClientPort
{
    bool Alive { get; }
    bool Ready { get; }
    bool Pending { get; }
}

// Uses the host's selected process and owner. It never discovers or injects an independent hook.
internal sealed class ApostleToolPort(IGameHost host, string root, GameInstance game, string account, string player) : IApostleToolPort
{
    readonly apostle::BD2.LocalIpc.PipeClient pipe = new(A.Identity.Root, game.ProcessId, game.StartTicks);
    public bool Alive => host.Find() == game;
    public bool Ready { get; private set; }
    public bool Pending { get; private set; }
    public A.GameProcess? Find() => Alive ? new(game.ProcessId, game.StartTicks, game.Executable) : null;
    void RequireIdentity()
    {
        var identity = Alive ? host.ReadSnapshot(game) : null;
        if (identity == null || identity.AccountKey != account || identity.PlayerKey != player || identity.FrameUtcTicks < DateTime.UtcNow.AddSeconds(-5).Ticks)
            throw new InvalidOperationException("游戏身份已变化或连接已过期，请返回日常助手。");
    }
    bool Matches(SuiteStatus state) => state.Tool == "apostle-defense" && state.State == "ready" && state.Owner == DailySuite.Owner
        && state.Account == account && state.Player == player && state.At >= DateTime.UtcNow.AddSeconds(-5).Ticks;
    public Task ConnectAsync(Action<string> progress, CancellationToken cancellation) => Task.Run(() => {
        cancellation.ThrowIfCancellationRequested();
        RequireIdentity();
        var state = DailySuite.Read(root, game) ?? throw new IOException("统一会话未就绪。");
        if (!Matches(state)) throw new InvalidOperationException("此工具尚未恢复就绪，请等待日常助手完成交接。");
        var fingerprint = state.Fingerprints.Single(x => x.StartsWith("apostle-defense|", StringComparison.Ordinal)).Split('|')[1];
        if (pipe.Fingerprint() != fingerprint) throw new IOException("功能组件与统一会话不一致，请返回日常助手。");
        pipe.Open(fingerprint); Ready = true;
    }, cancellation);
    public A.Snapshot? Read()
    {
        Ready = false; Pending = true;
        if (!Alive) { Pending = false; return null; }
        var state = DailySuite.Read(root, game) ?? throw new IOException("统一会话未就绪。");
        Pending = DailyActivityRules.ExecutionPending(state.Pending);
        Ready = state.At >= DateTime.UtcNow.AddSeconds(-5).Ticks;
        RequireIdentity();
        if (!Matches(state)) return null;
        var bytes = pipe.Read("snapshot.json");
        return bytes == null ? null : JsonSerializer.Deserialize<A.Snapshot>(bytes, A.JsonFiles.Options);
    }
    public void Write(A.Control command)
    {
        if (!Alive) { if (command.Enabled) throw new IOException("游戏已退出。"); return; }
        // Cancellation is safe even if the account changed; it never renews an enabled lease.
        if(command.Enabled)RequireIdentity();
        var state = DailySuite.Read(root, game) ?? throw new IOException("统一会话未就绪。");
        if (!Matches(state)) { if (command.Enabled) throw new IOException("此功能的统一会话已停止，请返回日常助手。"); return; }
        pipe.Write("control.json", JsonSerializer.SerializeToUtf8Bytes(command, A.JsonFiles.Options));
    }
}

internal sealed class ApostleToolSession(string root, Func<Action<string>, CancellationToken, Task<IApostleToolPort>> prepare) : IDisposable
{
    readonly DailyActivityLease activity = new(root);
    readonly SemaphoreSlim work = new(1, 1);
    readonly object logSync = new();
    IApostleToolPort? port;
    A.Controller? controller;
    CancellationTokenSource? startup;
    bool disposed;
    bool stopConfirmed = true;
    string lastError = "";
    public bool Starting => startup != null;
    public bool Connected { get; private set; }
    public bool Running => controller?.Running == true;
    public bool Occupied => activity.Held;
    public A.Snapshot? Snapshot { get; private set; }
    public A.Decision? Decision => controller?.PendingDecision;
    public string Message { get; private set; } = "apostle.ready";
    public event Action<string>? Diagnostic;
    internal static string SettingsPath(string root) => Path.Combine(root, "tools", "apostle-defense", "settings.json");
    public A.Settings LoadSettings()
    {
        // Migrate preferences only. Standalone evidence and account state never become host state.
        var value = A.JsonFiles.Read<A.Settings>(SettingsPath(root)) ?? A.JsonFiles.Read<A.Settings>(Path.Combine(A.Identity.Root, "settings.json"));
        return value != null && value.Validate() == "" ? value : new();
    }
    public void SaveSettings(A.Settings settings)
    {
        if (settings.Validate() is { Length: > 0 } error) throw new ArgumentException(error);
        A.JsonFiles.Write(SettingsPath(root), settings); controller?.Update(settings);
    }
    public Task ConnectAsync() => PrepareAsync(null);
    public Task StartAsync(A.Settings settings) => PrepareAsync(settings);
    async Task PrepareAsync(A.Settings? settings)
    {
        if (Starting || Running || Occupied || disposed) return;
        using var cancel = new CancellationTokenSource(); startup = cancel;
        try
        {
            activity.Enter(); controller = null; stopConfirmed=true;
            if (settings != null) SaveSettings(settings);
            Message = "apostle.connecting";
            await work.WaitAsync(cancel.Token);
            try
            {
                // Revalidate a prepared connection before enabling it. Daily may have switched
                // modules or accounts while this window was idle. Never enable an old port.
                bool fresh = false;
                if (Connected && port != null)
                {
                    try { fresh = await ReadFreshAsync(cancel.Token); }
                    catch (Exception) when (!cancel.IsCancellationRequested) { fresh = false; }
                }
                if (!fresh)
                {
                    Connected = false; port = null; Snapshot = null;
                    // Setup and native reads stay off the dispatcher so the window can paint,
                    // animate, cancel and close even before the first IPC await.
                    port = await Task.Run(() => prepare(ReportProgress, cancel.Token), cancel.Token);
                    cancel.Token.ThrowIfCancellationRequested();
                    await port.ConnectAsync(ReportProgress, cancel.Token);
                    Message = "apostle.reading";
                    for (int i = 0; i < 25; i++)
                    {
                        if (await ReadFreshAsync(cancel.Token)) { fresh = true; break; }
                        await Task.Delay(200, cancel.Token);
                    }
                }
                cancel.Token.ThrowIfCancellationRequested();
                var currentPort=port;var currentSnapshot=Snapshot;
                if (!fresh || currentPort==null || currentSnapshot==null) throw new IOException("apostle.no_snapshot");
                Connected = true; lastError = "";
                if (settings == null)
                {
                    // Connecting is read-only. A visible, idle window must not reserve the game.
                    Message = "apostle.ready"; activity.Dispose(); return;
                }
                controller = new(currentPort); controller.Diagnostic += Record;
                var startingController=controller;long stopVersion=startingController.StopVersion;
                stopConfirmed=false;
                await Task.Run(() => startingController.Start(currentSnapshot, settings, DateTime.UtcNow.Ticks,stopVersion), cancel.Token);
                Message = controller.Message; lastError = "";
            }
            finally { work.Release(); }
        }
        catch (OperationCanceledException) { Connected=false; Message = "apostle.connection_cancelled"; if(controller==null)activity.Dispose();else controller.RequestStop(); }
        catch (Exception error) { Connected=false; Message = error.GetBaseException().Message; Record(error.ToString()); if(controller==null)activity.Dispose();else controller.RequestStop(); }
        finally { startup = null; }
    }
    void ReportProgress(string message) { if (startup?.IsCancellationRequested == false) Message = message; }
    Task<bool> ReadFreshAsync(CancellationToken cancellation) => Task.Run(() => {
        Snapshot = port!.Read();
        return port.Ready && Snapshot != null && port.Find() is { } game && A.Guards.Fresh(Snapshot, game.Id, game.Start, DateTime.UtcNow.Ticks);
    }, cancellation);
    public async Task PollAsync()
    {
        if (disposed || Starting || port == null || !await work.WaitAsync(0)) return;
        try
        {
            // A stopped view does not continuously probe an exited game or another tool's module.
            if (!Running && !Occupied) return;
            await Task.Run(() => {
                Snapshot = port.Read(); controller?.Poll(Snapshot, DateTime.UtcNow.Ticks);
                if(!Running && !stopConfirmed && controller!=null){controller.Flush();stopConfirmed=true;}
                activity.Observe(Running, port.Alive, port.Ready, port.Pending || !Running&&!stopConfirmed);
                if (!port.Alive) Connected = false;
            });
            Message = controller?.Message ?? Message; lastError = "";
        }
        catch (Exception error)
        {
            Connected = false;
            Message = error.GetBaseException().Message;
            if(Running)controller?.RequestStop(Message);
            if (lastError != Message) { Record(error.ToString()); lastError = Message; }
            try
            {
                await Task.Run(()=>{
                    if(controller!=null && !stopConfirmed)try{controller.Flush();stopConfirmed=true;}catch(Exception){ }
                    activity.Observe(false,port.Alive,port.Ready,port.Pending || !stopConfirmed);
                });
            }
            catch(Exception) { /* An unavailable process query cannot confirm settlement. Keep pause responsive and retry observations. */ }
        }
        finally { work.Release(); }
    }
    public async Task<bool> StopAsync()
    {
        startup?.Cancel(); controller?.RequestStop(); Message = "apostle.stopping";
        if (!await work.WaitAsync(TimeSpan.FromSeconds(12))) { Message = "apostle.settling"; return false; }
        try { if (controller != null) {await Task.Run(controller.Flush);stopConfirmed=true;} }
        catch (Exception error) { Record(error.ToString()); }
        finally { work.Release(); }
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (Occupied && DateTime.UtcNow < deadline) { await PollAsync(); if (Occupied) await Task.Delay(100); }
        Message = Occupied ? "apostle.settling" : Connected ? "apostle.paused" : "apostle.connection_cancelled";
        return !Occupied;
    }
    void Record(string message)
    {
        Diagnostic?.Invoke(message);
        try
        {
            lock (logSync)
            {
                string folder = Path.Combine(root, "live", "diagnostics", "apostle-defense"); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "decisions-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose() { disposed = true; startup?.Cancel(); controller?.RequestStop(); activity.Dispose(); }
}

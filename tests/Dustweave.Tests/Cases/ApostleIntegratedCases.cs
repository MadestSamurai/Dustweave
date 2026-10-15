extern alias apostle;
using A = apostle::BD2ApostleDefense;
using Dustweave;
using Dustweave.Desktop;

internal static class ApostleIntegratedCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool valid, string text) { if (!valid) throw new Exception(text); cases.Add("apostle-integrated: " + text); }
        string data = Path.Combine(root, "apostle-hosted");
        var port = new FakePort(); int prepared = 0;
        using var session = new ApostleToolSession(data, (_,_) => { prepared++; return Task.FromResult<IApostleToolPort>(port); });
        session.SaveSettings(new() { IntervalMs = 350, Clear50 = true, Rare = false });
        Check(session.LoadSettings().IntervalMs == 350 && !session.LoadSettings().Rare, "host preferences preserved without importing evidence");
        await session.PollAsync(); Check(prepared == 0 && port.Reads == 0 && !session.Occupied, "opening idle settings does not prepare, poll or own game");
        using (DailyToolControl.Acquire(data)) { await session.StartAsync(new()); Check(!session.Running && prepared == 0, "daily ownership blocks start before preparation"); }
        await session.ConnectAsync();
        Check(session.Connected && !session.Running && !session.Occupied && prepared==1 && port.Commands.Count==0,"connection prepares a snapshot without enabling automation or retaining control");
        await session.StartAsync(new()); Check(session.Running && session.Occupied && prepared == 1, "explicit start prepares once and owns control");
        Check(port.Commands.Count == 1 && port.Commands[0].Enabled, "start uses existing controller transport");
        await session.StartAsync(new()); Check(prepared == 1, "double start cannot create another controller");
        bool blocked = false; try { using var lease = DailyToolControl.Acquire(data); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "automation excludes daily and other tools");
        port.Pending = true;
        var stop = session.StopAsync(); await Task.Delay(150);
        Check(!session.Running && session.Occupied && !stop.IsCompleted, "pause stops decisions immediately but waits for settlement");
        Check(!port.Commands.Last().Enabled, "pause flushes disabled command");
        port.Pending = false;
        Check(await stop && !session.Occupied, "settlement releases control without closing view");
        int reads = port.Reads; await session.PollAsync(); Check(port.Reads == reads, "paused view does not repeatedly poll IPC");
        await session.StartAsync(new()); port.Alive = false; await session.PollAsync();
        Check(!session.Running && !session.Occupied, "game exit stops and releases ownership");
        reads = port.Reads; for (int i = 0; i < 4; i++) await session.PollAsync();
        Check(port.Reads == reads && prepared == 1 && !session.Connected, "game exit never becomes a reconnect loop");
        Check(Directory.GetFiles(Path.Combine(data, "live", "diagnostics", "apostle-defense"), "*.log").Length == 1, "decisions use host diagnostic retention and export tree");
        Check(DailyDiagnosticExport.Allowed("live/diagnostics/apostle-defense/decisions-20261015.log"), "unified diagnostic export includes decisions");
        using var failed = new ApostleToolSession(Path.Combine(data,"failure"), (_,_) => throw new IOException("fixture prepare failure"));
        await failed.StartAsync(new()); Check(!failed.Occupied && failed.Message == "fixture prepare failure", "preparation failure is actionable and releases unused lease");
        var preparationEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new ApostleToolSession(Path.Combine(data,"cancel"), async (progress,token) => { progress("fixture preparing");preparationEntered.TrySetResult();await Task.Delay(Timeout.Infinite, token); return port; });
        var starting = cancel.ConnectAsync(); await preparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancel.Starting && cancel.Occupied && cancel.Message=="fixture preparing","connection exposes progress and owns the shared lease while preparing");
        await cancel.ConnectAsync();await cancel.StartAsync(new());
        Check(cancel.Starting && !cancel.Running,"repeated connect and start are ignored during preparation");
        var stopping = cancel.StopAsync(); await starting;
        Check(await stopping && !cancel.Occupied && !cancel.Starting, "pause cancels preparation without starting a match");
        var racingPort=new FakePort();int finds=0;
        using var releaseFind=new ManualResetEventSlim();
        var finding=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var racing=new ApostleToolSession(Path.Combine(data,"stop-start"),(_,_)=>Task.FromResult<IApostleToolPort>(racingPort));
        await racing.ConnectAsync();
        racingPort.FindHook=()=>{if(Interlocked.Increment(ref finds)==2){finding.TrySetResult();if(!releaseFind.Wait(5000))throw new TimeoutException("fixture wait");}};
        var raceStart=racing.StartAsync(new());await finding.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var raceStop=racing.StopAsync();releaseFind.Set();await raceStart;
        Check(await raceStop && racingPort.Commands.All(c=>!c.Enabled),"cancel during final start validation cannot re-enable automation");
        var changedPort=new FakePort();
        using var changed=new ApostleToolSession(Path.Combine(data,"changed"),(_,_)=>Task.FromResult<IApostleToolPort>(changedPort));
        await changed.StartAsync(new());changedPort.ReadFailure=true;changedPort.FailStopOnce=true;
        await changed.PollAsync();
        Check(!changed.Running&&changed.Occupied,"failed read stops decisions and retains ownership until cancellation is confirmed");
        await changed.PollAsync();await Task.Delay(800);await changed.PollAsync();
        Check(!changed.Occupied&&!changedPort.Commands.Last().Enabled,"safe cancellation retries after transient failure then releases ownership");
        Check(changed.Message=="fixture identity changed","failure explanation survives cancellation and subsequent polls");
        var replacementPort=new FakePort();var switchedPort=new FakePort();int switchedPrepares=0;
        using var switched=new ApostleToolSession(Path.Combine(data,"switched"),(_,_)=>Task.FromResult<IApostleToolPort>(++switchedPrepares==1?switchedPort:replacementPort));
        await switched.ConnectAsync();switchedPort.ReadFailure=true;await switched.StartAsync(new());
        Check(switched.Running && switchedPrepares==2 && switchedPort.Commands.Count==0 && replacementPort.Commands.Single().Enabled,"start revalidates and replaces a connection invalidated while the window was idle");
        await switched.StopAsync();
        var stalePort=new FakePort { Stale=true };
        using var stale=new ApostleToolSession(Path.Combine(data,"stale"),(_,_)=>Task.FromResult<IApostleToolPort>(stalePort));
        await stale.ConnectAsync();
        Check(!stale.Connected && !stale.Occupied && stalePort.Commands.Count==0 && stale.Message=="apostle.no_snapshot","non-null stale snapshots never enable automation");
        port.Alive = true;
        var host = new DemoEnvironment(data); host.Game = new(42, 123, "fixture.exe");
        var wrong = new ApostleToolPort(host, data, host.Game, new('f',64), new('e',64));
        blocked = false; try { wrong.Write(new() { Enabled = true }); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "account mismatch rejected before opening IPC");
        host.Game = null;
        Check(wrong.Read() == null && !wrong.Alive, "missing selected process never falls back to another game");
        wrong.Write(new());
    }
    sealed class FakePort : IApostleToolPort
    {
        public bool Alive { get; set; } = true;
        public bool Ready => Alive;
        public bool Pending { get; set; }
        public int Reads;
        public bool ReadFailure,FailStopOnce,Stale;
        public List<A.Control> Commands = new();
        public Action? FindHook;
        public A.GameProcess? Find() {FindHook?.Invoke();return Alive ? new(42,123,"fixture.exe") : null;}
        public A.Snapshot? Read() { Reads++;if(ReadFailure)throw new IOException("fixture identity changed"); return Alive ? new() { ProcessId=42, ProcessStart=123, At=Stale?1:DateTime.UtcNow.Ticks, Account=new('a',64), Stage="waiting" } : null; }
        public void Write(A.Control c) {if(!c.Enabled&&FailStopOnce){FailStopOnce=false;throw new IOException("fixture stop timeout");}Commands.Add(A.JsonFiles.Clone(c));}
        public Task ConnectAsync(Action<string> progress, CancellationToken cancellation) => Task.CompletedTask;
    }
}

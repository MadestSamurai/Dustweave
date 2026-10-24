using System.Diagnostics;
using Dustweave;
using Dustweave.Desktop;

internal static class GamePreparationCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool value, string description)
        {
            if (!value) throw new Exception(description);
            cases.Add("game preparation: " + description);
        }
        async Task<Exception> Failure(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (Exception error) { return error; }
            throw new Exception("Expected a stopped preparation");
        }
        TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        {
            var host = new Host(); var selected = host.Current!; int reads = 0;
            host.Mutate = value => { value.Scene = ++reads < 3 ? "Empty" : "Map3001_001"; value.Loading = reads < 3; };
            var messages = new List<string>();
            var ready = await DailyGamePreparation.WaitForIdentityAsync(host, selected, null, messages.Add, CancellationToken.None);
            Check(ready.Scene == "Map3001_001" && reads == 3 && messages.Count == 1, "loading identity waits between polls without repeatedly preparing a module");
            Check(host.Discoveries == 0 && host.TargetedReads == 3, "preparation observes only the selected PID instead of enumerating all processes every poll");
            var expected = ready;
            host.Mutate = value => { value.Scene = "Empty"; value.MainMenuReady = true; };
            var home = await DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None);
            Check(home.Scene == "Empty" && home.MainMenuReady, "native Empty home menu prepares modules without entering a map");
            host.Mutate = value => value.Scene = "Empty";
            var child = await DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None);
            Check(child.Scene == "Empty" && !child.MainMenuReady, "authenticated child pages prepare modules without requiring MenuUI");
            host.Mutate = value => { value.Scene = "Empty"; value.Loading = true; };
            Check(await Failure(DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None, timeout:TimeSpan.Zero)) is TimeoutException,
                "actual loading remains blocked independently of the scene name");
            host.Mutate = value => value.Startup = new() { Visible = true };
            Check(await Failure(DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None, timeout:TimeSpan.Zero)) is TimeoutException,
                "visible login page cannot prepare components even if cached identity remains identified");
            host.Mutate = value => { value.Loading = true; value.AccountKey = "changed"; };
            Check(await Failure(DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None)) is InvalidOperationException,
                "an actual account change during loading is still rejected immediately");
            host.Mutate = value => value.FrameUtcTicks = DateTime.UtcNow.AddSeconds(-13).Ticks;
            using var cancel = new CancellationTokenSource();
            var pending = DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, cancel.Token);cancel.Cancel();
            Check(await Failure(pending) is OperationCanceledException, "stale frame recovery remains immediately cancellable");
            pending = DailyGamePreparation.WaitForIdentityAsync(host, selected, expected, _ => {}, CancellationToken.None);host.Current = null;
            Check(await Failure(pending) is DailyGameExitedException, "game exit ends a loading wait without rediscovering or launching a different game");
        }
        foreach (string reason in new[] { "closed", "restarted", "pid-reused", "user-stop" })
        {
            var host = new Host(); var selected = host.Current!;
            var work = Signal<int>(); using var cancel = new CancellationTokenSource();
            Action<string>? lateReport = null; CancellationToken compilerToken = default;
            var messages = new List<string>();
            var pending = DailyGamePreparation.RunAsync(host, selected, (report, token) =>
            {
                compilerToken = token; lateReport = report; report("started"); return work.Task;
            }, messages.Add, cancel.Token);
            if (reason == "user-stop") cancel.Cancel();
            else host.Current = reason switch
            {
                "restarted" => selected with { ProcessId = selected.ProcessId + 1 },
                "pid-reused" => selected with { StartTicks = selected.StartTicks + 1 },
                _ => null
            };
            var elapsed = Stopwatch.StartNew();
            var failure = await Failure(pending);
            Check(reason == "user-stop" ? failure is OperationCanceledException and not DailyGameExitedException
                : failure is DailyGameExitedException, reason + " has an explicit stopped outcome");
            Check(elapsed.Elapsed < TimeSpan.FromSeconds(2) && !work.Task.IsCompleted && compilerToken.IsCancellationRequested,
                reason + " releases its waiter without waiting for a non-cooperative compiler");
            lateReport!("late result");
            Check(messages.SequenceEqual(new[] { "started" }), reason + " cannot publish late progress");
            work.SetException(new InvalidDataException("late compiler failure"));
            await Task.Delay(10);
        }
        {
            var host = new Host(); var selected = host.Current!; int called = 0;
            host.Current = null;
            var failure = await Failure(DailyGamePreparation.RunAsync(host, selected,
                (_, _) => { called++; return Task.FromResult(1); }, _ => { }, CancellationToken.None));
            Check(failure is DailyGameExitedException && called == 0, "already closed game never starts preparation");
        }
        {
            var host = new Host(); var selected = host.Current!;
            var value = await DailyGamePreparation.RunAsync(host, selected, (_, _) => Task.FromResult(7), _ => { }, CancellationToken.None);
            Check(value == 7, "successful preparation returns its original result");
            var failure = await Failure(DailyGamePreparation.RunAsync<int>(host, selected,
                (_, _) => Task.FromException<int>(new InvalidDataException("compiler failure")), _ => { }, CancellationToken.None));
            Check(failure is InvalidDataException && failure.Message == "compiler failure", "real compiler errors are not converted to cancellation");
            failure = await Failure(DailyGamePreparation.RunAsync(host, selected,
                (_, _) => { host.Current = null; return Task.FromResult(7); }, _ => { }, CancellationToken.None));
            Check(failure is DailyGameExitedException, "exit concurrent with completion cannot activate stale output");
            host.Current = selected;
            var work = Signal<int>(); var pending = DailyGamePreparation.RunAsync(host, selected, (_, _) => work.Task, _ => { }, CancellationToken.None);
            host.ProbeError = new UnauthorizedAccessException("scope denied");
            failure = await Failure(pending); work.SetResult(7);
            Check(failure is UnauthorizedAccessException, "failed process inspection is not claimed to be a normal exit");
        }
        {
            var host = new Host(); var original = DailySuite.PrepareModule;
            var work = Signal<byte[]>(); var reached = Signal<bool>();
            var directory = Path.Combine(root, "suite-preparation-stop");
            using var self = Process.GetCurrentProcess();
            host.Current = new(self.Id, self.StartTime.ToUniversalTime().Ticks, "fixture.exe");
            using var server = new System.IO.Pipes.NamedPipeServerStream(BD2.LocalIpc.Wire.Endpoint(self.Id, host.Current.StartTicks),
                System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
            var response = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                BD2.LocalIpc.Wire.ReadFrame(server);
                BD2.LocalIpc.Wire.WriteFrame(server, BD2.LocalIpc.Wire.Encode(w =>
                { w.Write("empty"); w.Write(""); w.Write(""); BD2.LocalIpc.Wire.Bytes(w, []); }));
            });
            try
            {
                DailySuite.PrepareModule = (_, _, _, _) => { reached.SetResult(true); return work.Task; };
                var pending = DailySuite.ActivateAsync(host, directory, "daily", _ => { }, CancellationToken.None);
                await Task.WhenAny(reached.Task, pending).WaitAsync(TimeSpan.FromSeconds(3));
                if (pending.IsCompleted) await pending;
                await response;
                host.Current = null;
                Check(await Failure(pending) is DailyGameExitedException, "actual module activation abandons preparation on game exit");
                work.SetResult([1, 2, 3]); await Task.Delay(30);
                Check(!File.Exists(Path.Combine(directory, "suite", "diagnostics", "ownership.jsonl")), "stopped activation cannot submit a late command");
            }
            finally { work.TrySetResult([]); DailySuite.PrepareModule = original; }
        }
        {
            var directory = Path.Combine(root, "coordinator-preparation-stop");
            var host = new DemoEnvironment(directory);
            var reached = Signal<bool>(); var work = Signal<int>(); bool first = true;
            var coordinator = new DailyCoordinator(host, host, directory, new() { PollInterval = TimeSpan.FromMilliseconds(2) }, async token =>
            {
                if (!first) return;
                await DailyGamePreparation.RunAsync(host, host.Game!, (_, _) => { reached.TrySetResult(true); return work.Task; }, _ => { }, token);
            });
            bool continuedIntoQueue = false;
            async Task Start()
            {
                using var control = DailyToolControl.Acquire(directory);
                await coordinator.InspectAsync([host.Accounts[0]]);
                continuedIntoQueue = true;
            }
            var pending = Start();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(3)); host.Game = null;
            Check(await Failure(pending) is DailyGameExitedException, "coordinator propagates game exit to prevent starting the queue");
            Check(!coordinator.IsRunning && !DailyToolControl.IsOccupied(directory) && !continuedIntoQueue,
                "connection and tool control locks are released before compiler completion");
            Check(DailyJson.TryRead<DailyRunStatus>(Path.Combine(directory, "run.json"))?.Progress.State == "stopped"
                && !File.Exists(Path.Combine(directory, "run-error.json")), "normal game exit records stopped instead of an error");
            first = false;
            await coordinator.InspectAsync([host.Accounts[0]]).WaitAsync(TimeSpan.FromSeconds(3));
            Check(host.Game != null && !coordinator.IsRunning && !work.Task.IsCompleted, "next launch is allowed while old pure work winds down");
            work.SetResult(1);
        }
        {
            using var current = Process.GetCurrentProcess();
            var selected = new GameInstance(current.Id, current.StartTime.ToUniversalTime().Ticks, "fixture");
            Check(DailyConnectionAccess.IsRunning(selected), "native liveness probe recognizes a live exact generation");
            Check(!DailyConnectionAccess.IsRunning(selected with { StartTicks = selected.StartTicks + 1 }), "native liveness probe rejects PID reuse");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory };
            start.ArgumentList.Add("--watchdog-child");
            using var child = Process.Start(start)!;
            var owned = new GameInstance(child.Id, child.StartTime.ToUniversalTime().Ticks, "fixture");
            try
            {
                Check(DailyConnectionAccess.IsRunning(owned), "native probe sees synthetic child before exit");
                child.Kill(); await child.WaitForExitAsync();
                Check(!DailyConnectionAccess.IsRunning(owned), "native probe detects exited synthetic child even with its handle retained");
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
        Check(DailyIssues.Classify(new DailyGameExitedException()) == new DailyIssue("game-ended", "stopped"), "diagnostics distinguishes game closure from compiler failure");
        {
            var host = new Host { ReadFailure = "transport-timeout" };
            string directory = Path.Combine(root, "identity-read-failure");
            var error = await Failure(DailyGamePreparation.WaitForIdentityAsync(host, host.Current!, null, _ => {}, CancellationToken.None,
                directory, TimeSpan.Zero));
            Check(error is TimeoutException && error.Message.Contains("身份数据连接暂时不可用"), "module preparation explains transport failure instead of implying lost login");
            string relative = "live/diagnostics/login-preparation-failed.json";
            using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, relative)));
            Check(report.RootElement.GetProperty("readError").GetString() == "transport-timeout", "failed preparation preserves its actual read cause");
            Check(DailyDiagnosticExport.Allowed(relative), "failed identity preparation is included in diagnostic export");
            host.ReadFailure = null; host.Mutate = snapshot => snapshot.Loading = true;
            error = await Failure(DailyGamePreparation.WaitForIdentityAsync(host, host.Current!, null, _ => {}, CancellationToken.None, directory, TimeSpan.Zero));
            Check(error.Message.Contains("仍在加载场景"), "a fresh loading frame is distinguished from a missing transport");
            using var loading = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, relative)));
            Check(loading.RootElement.GetProperty("observed").GetProperty("Loading").GetBoolean(), "native loading flag survives bounded failure capture");
        }
        {
            string directory = Path.Combine(root, "parallel-login-diagnostics");
            var environment = new DemoEnvironment(directory);
            var host = new MissingIdentityHost(environment);
            var coordinator = new DailyCoordinator(environment, host, directory, new() { LoginTimeout = TimeSpan.FromMilliseconds(20), PollInterval = TimeSpan.FromMilliseconds(1) });
            var error = await Failure(coordinator.InspectAsync([environment.Accounts[0]]));
            Check(error is TimeoutException && error.Message.Contains("尚未收到游戏身份数据"), "batch login distinguishes active heartbeat from main-thread identity");
            using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "live/diagnostics/login-failed.json")));
            Check(report.RootElement.GetProperty("readError").GetString() == "snapshot-missing" && report.RootElement.GetProperty("status").GetProperty("State").GetString() == "active",
                "batch verification now records missing snapshot alongside healthy heartbeat");
            Check(!environment.Calls.Any(c => c.StartsWith("save:") || c.StartsWith("launch:") || c == "close") && new DailyProfiles(directory).Read().Count == 0,
                "missing identity never changes login data or binds the saved account as live proof");
        }
    }

    sealed class MissingIdentityHost(DemoEnvironment inner) : IGameHost
    {
        public GameInstance? Find() => inner.Find();
        public Task CloseAsync(GameInstance game, CancellationToken token) => inner.CloseAsync(game, token);
        public Task ConnectAsync(GameInstance game, Action<string> progress, CancellationToken token) => inner.ConnectAsync(game, progress, token);
        public DailySnapshot? ReadSnapshot() => null;
        public DailyRuntimeStatus? ReadStatus() => new() { ProcessId = inner.Game!.ProcessId, ProcessStartTicks = inner.Game.StartTicks, State = "active" };
    }

    sealed class Host : IGameHost
    {
        public GameInstance? Current = new(999999, 1234567, @"C:\fixture\BrownDust II.exe");
        public Exception? ProbeError;
        public Action<DailySnapshot>? Mutate;
        public int Discoveries, TargetedReads;
        public string? ReadFailure;
        public DailyIdentityRead ReadIdentity(GameInstance expected) => ReadFailure == null ? new(ReadSnapshot(expected)) : new(null, ReadFailure, "fixture");
        public GameInstance? Find() { Discoveries++; return Current; }
        public bool IsRunning(GameInstance expected) => ProbeError == null ? Current == expected : throw ProbeError;
        public Task CloseAsync(GameInstance instance, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ConnectAsync(GameInstance instance, Action<string> progress, CancellationToken cancellation) => throw new NotSupportedException();
        public DailyRuntimeStatus? ReadStatus() => null;
        public DailySnapshot? ReadSnapshot(GameInstance? expected) { TargetedReads++; return expected == Current ? ReadSnapshot() : null; }
        public DailySnapshot? ReadSnapshot()
        {
            if (Current is not {} game) return null;
            var value = new DailySnapshot { ProcessId = game.ProcessId, ProcessStartTicks = game.StartTicks,
                FrameUtcTicks = DateTime.UtcNow.Ticks, InstanceId = "fixture", Sequence = 1, State = "identified", AccountKey = "fixture", PlayerKey = "fixture" };
            Mutate?.Invoke(value); return value;
        }
    }
}

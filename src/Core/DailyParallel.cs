using System.Collections.Concurrent;

namespace Dustweave;

public sealed record DailyParallelOptions(bool Enabled = false, int Maximum = 2, bool CloseGame = true)
{
    public void Validate() { if (Maximum is < 1 or > 4) throw new InvalidDataException("parallel.invalid_limit"); }
}
public sealed record DailyParallelJob(string Id, string Account, string Name, string Preferences, bool CloseGame)
{
    public bool StartedGame { get; init; }
    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || !DailyProfiles.ValidKey(Account) || string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("parallel.invalid_job");
        DailyPreferences.Parse(Preferences).Validate();
    }
}
public sealed record DailyParallelCommand(string Id, string Account, string Action, long Sequence, DateTimeOffset AtUtc);
public sealed record DailyParallelStatus(string Id, string Account, string State, string Detail, DateTimeOffset AtUtc,
    int ProcessId = 0, long ProcessStartTicks = 0, int GameId = 0, long GameStartTicks = 0, QueueView? Queue = null);
public sealed record DailyParallelItem(DailyAccount Account, DailyParallelJob Job, string State = "waiting", string Detail = "", DailyParallelStatus? Status = null, long Sequence = 0, string Action = "run");
public sealed record DailyParallelRun(string Id, DailyParallelOptions Options, DateTimeOffset CreatedUtc, IReadOnlyList<DailyParallelItem> Items);
public interface IDailyParallelRuntime
{
    Task StartAsync(DailyAccount account, DailyParallelJob job, CancellationToken token);
    DailyParallelStatus? Read(DailyParallelJob job);
    bool Alive(DailyParallelStatus status);
    bool Busy(DailyParallelJob job);
    void Send(DailyParallelJob job, string action, long sequence);
}

// The ordinary desktop owns scheduling only. Each worker owns one account, one
// connection and the existing queue. Persist before starting any external work.
public sealed class DailyParallelSession
{
    private readonly string path;
    private readonly IDailyParallelRuntime runtime;
    private readonly Func<DateTimeOffset> now;
    private readonly Dictionary<string, Task> starting = new();
    private readonly Dictionary<string, DateTimeOffset> launched = new();
    private readonly ConcurrentQueue<(string account, string action)> commands = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool holdWaiting;
    public DailyParallelRun? Current { get; private set; }
    public bool Occupied => Current?.Items.Any(x => Occupies(x.State)) == true;
    public bool HasWork => Current?.Items.Any(x => Occupies(x.State) || x.State is "waiting" or "held") == true;
    public static bool Occupies(string state) => state is "starting" or "connecting" or "running" or "pausing" or "paused" or "stopping" or "closing";
    public DailyParallelSession(string root, IDailyParallelRuntime runtime, Func<DateTimeOffset>? now = null)
    {
        path = Path.Combine(root, "parallel-queue.json"); this.runtime = runtime; this.now = now ?? (() => DateTimeOffset.UtcNow);
        var prior = DailyJson.TryRead<DailyParallelRun>(path);
        if (prior != null)
        {
            // Reopening never starts a game or silently replays an uncertain command.
            Current = prior with { Items = prior.Items.Select(x => x with { State = Occupies(x.State) || x.State is "waiting" or "held" ? "interrupted" : x.State, Detail = Occupies(x.State) || x.State is "waiting" or "held" ? "parallel.reopen" : x.Detail }).ToArray() };
        }
    }
    public async Task StartAsync(IEnumerable<DailyAccount> accounts, DailyParallelOptions options, Func<string, DailyPreferences> preferences)
    {
        await gate.WaitAsync();
        try
        {
            if (HasWork) throw new InvalidOperationException("parallel.busy");
            options.Validate();
            var targets = accounts.ToArray();
            if (targets.Length == 0 || targets.Any(x => !x.Valid || !DailyProfiles.ValidKey(x.AccountKey)) || targets.Select(x => x.AccountKey).Distinct().Count() != targets.Length) throw new InvalidDataException("parallel.invalid_accounts");
            var items = targets.Select(a => new DailyParallelItem(a, new(Guid.NewGuid().ToString("N"), a.AccountKey, a.Name, System.Text.Json.JsonSerializer.Serialize(preferences(a.AccountKey)), options.CloseGame))).ToArray();
            foreach (var item in items) item.Job.Validate();
            starting.Clear(); launched.Clear(); while (commands.TryDequeue(out _)) { } holdWaiting = false;
            Current = new(Guid.NewGuid().ToString("N"), options, now(), items); Save();
        }
        finally { gate.Release(); }
    }
    public void Control(string account, string action)
    {
        if (action is not ("pause" or "resume" or "stop")) throw new ArgumentException("parallel.invalid_action");
        commands.Enqueue((account, action));
    }
    public async Task TickAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (Current == null || !HasWork && commands.IsEmpty) return;
            var items = Current.Items.ToArray();
            while (commands.TryDequeue(out var command))
            {
                if (command.account.Length == 0) holdWaiting = command.action != "resume";
                else if (command.action == "resume") holdWaiting = false; // Other held accounts stay held.
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i]; if (command.account.Length != 0 && item.Account.AccountKey != command.account) continue;
                    if (item.State is "waiting" or "held")
                    {
                        items[i] = item with { State = command.action == "stop" ? "stopped" : command.action == "resume" ? "waiting" : "held" }; continue;
                    }
                    if (!Occupies(item.State)) continue;
                    if (command.action == "resume" && item.State != "paused") continue;
                    items[i] = item with { Sequence = item.Sequence + 1, Action = command.action, State = command.action == "stop" ? "stopping" : command.action == "pause" ? "pausing" : "running" };
                }
            }
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i]; if (!Occupies(item.State)) continue;
                try
                {
                    // Periodic lease continues even when one account takes time to launch.
                    runtime.Send(item.Job, item.Action, item.Sequence);
                    if (starting.TryGetValue(item.Job.Id, out var task) && task.IsCompleted)
                    {
                        starting.Remove(item.Job.Id); await task;
                    }
                    var status = runtime.Read(item.Job);
                    if (status != null)
                    {
                        if (status.Id != item.Job.Id || status.Account != item.Account.AccountKey) throw new InvalidDataException("parallel.identity_changed");
                        if (status.AtUtc > now().AddSeconds(5)) throw new InvalidDataException("parallel.invalid_status");
                        bool live = runtime.Alive(status);
                        bool stale = now() - status.AtUtc > TimeSpan.FromSeconds(20);
                        if (Occupies(status.State) && (!live || stale))
                        {
                            // Never free this capacity while the actual worker still runs.
                            items[i] = item with { State = live ? "stopping" : "interrupted", Action = "stop", Sequence = item.Sequence + 1, Status = status, Detail = live ? "parallel.no_heartbeat" : "parallel.worker_lost" }; continue;
                        }
                        if (!KnownStatus(status.State)) throw new InvalidDataException("parallel.invalid_status");
                        string state = item.State is "pausing" or "stopping" && Occupies(status.State) && status.State != "paused" ? item.State : status.State;
                        if (item.State == "stopping" && status.State == "paused") state = "stopping";
                        items[i] = item with { State = state, Status = status, Detail = status.Detail };
                    }
                    else if (now() - launched[item.Job.Id] > TimeSpan.FromMinutes(2))
                    {
                        // The launch task's bounded startup owns failure cleanup.
                        items[i] = item with { State = runtime.Busy(item.Job) ? "stopping" : "failed", Detail = "parallel.start_timeout", Action = "stop", Sequence = item.Sequence + 1 };
                        runtime.Send(item.Job, "stop", item.Sequence + 1);
                    }
                }
                catch (Exception error)
                {
                    bool occupied=true; try { occupied=runtime.Busy(item.Job); } catch { }
                    items[i] = item with { State = occupied ? "stopping" : "failed", Detail = error.Message, Action = "stop", Sequence = item.Sequence + 1 };
                    try { runtime.Send(item.Job, "stop", item.Sequence + 1); } catch { }
                }
            }
            if (!holdWaiting)
            {
                int available = Current.Options.Maximum - items.Count(x => Occupies(x.State));
                for (int i = 0; i < items.Length && available > 0; i++)
                {
                    if (items[i].State != "waiting") continue;
                    var item = items[i] with { State = "starting" }; items[i] = item; available--;
                    launched[item.Job.Id] = now();
                    Current = Current with { Items = items.ToArray() }; Save();
                    starting[item.Job.Id] = Task.Run(() => runtime.StartAsync(item.Account, item.Job, token), token);
                }
            }
            Current = Current with { Items = items }; Save();
        }
        finally { gate.Release(); }
    }
    private static bool KnownStatus(string state) => Occupies(state) || state is "completed" or "partial" or "failed" or "stopped" or "interrupted";
    private void Save() => DailyJson.Write(path, Current);
}

using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

/// <summary>Exercises the production managed host using deterministic native observations and mailboxes.</summary>
sealed class ManagedHostFixture : IDisposable
{
    public DailyStageHost Host
    {
        get;
    }
    public DailyCommandDriver Driver
    {
        get;
    }
    public DailyManagedBusiness Business
    {
        get;
    }
    public DailyWorkflowRegistry Registry
    {
        get;
    }
    private readonly bool ownsDriver;
    public ManagedHostFixture(string root, Func<Task<DailyStageFrame>> read, DailyCommandDriver? driver = null, DailyManagedBusiness? business = null, DailyStageObservation? observation = null, DailyStageNavigation? navigation = null, DailyManagedBootstrap? bootstrap = null, IReadOnlyDictionary<string, IDailyManagedStage>? stages = null, DailyBusinessProof[]? proofs = null, Func<bool>? stopped = null)
    {
        stopped ??= () => false;
        var initial = read().GetAwaiter().GetResult();
        var box = new CommandDriverCases.Mailbox();
        ownsDriver = driver == null;
        Driver = driver ?? new(root, box, read, stopped, () => N(read().GetAwaiter().GetResult().Frame["AtUtcTicks"]));
        observation ??= new DailyStageObservation(S(initial.Context["actor"]![3]), () => { var f = read().GetAwaiter().GetResult().Frame.DeepClone().AsObject(); f["Protocol"] = 1; f["BridgeVersion"] = DailyStageObservation.BridgeVersion; return f; }, () => { var f = read().GetAwaiter().GetResult(); return new DailySnapshot { ProcessId = I(f.Frame["ProcessId"]), ProcessStartTicks = N(f.Frame["ProcessStartTicks"]), InstanceId = S(f.Frame["Instance"]), AccountKey = S(f.Context["actor"]![3]), PlayerKey = S(f.Context["actor"]![4]), FrameUtcTicks = N(f.Frame["AtUtcTicks"]), State = "identified", Guild = new() { ServerKey = S(f.Context["server"]), CycleKey = S(f.Context["cycle"]) } }; }, stopped, () => N(read().GetAwaiter().GetResult().Frame["AtUtcTicks"]), timeout: TimeSpan.Zero);
        proofs ??= [];
        Business = business ?? new(root, Driver, proofs, stopped);
        navigation ??= new(read, stopped);
        bootstrap ??= new(root, Driver, read, stopped);
        var reconciliation = new DailyManagedReconciliation(root, Driver, ["room.info", "mail.collect"], stopped);
        Registry = new(root, TestPaths.SourceRoot, Driver, Business, navigation, proofs, stopped, _ => { }, () => null);
        Host = new(root, observation, navigation, Driver, Business, bootstrap, reconciliation, Registry, stages ?? new Dictionary<string, IDailyManagedStage>(), proofs);
    }
    public void Dispose()
    {
        Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (ownsDriver)
            Driver.Dispose();
    }
    public sealed class Stage(Func<JsonObject, Task<JsonObject>> execute, bool resume = false) : IDailyManagedStage
    {
        public bool CanResume(DailyStageFrame frame) => resume; public Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay) => execute(context);
    }
}

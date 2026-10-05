using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

/// <summary>Runs production workflows against a deterministic native mailbox and virtual monotonic clock.</summary>
sealed class WorkflowHarness : IDisposable
{
    public readonly string Root;
    public readonly CommandDriverCases.Mailbox Box = new() { Error = "" };
    public readonly JsonObject Context = CommandDriverCases.Context();
    public JsonObject Frame = CommandDriverCases.Frame();
    public Func<JsonObject[]> Readings = () => [];
    public Action<JsonObject>? OnCommand;
    public Action? OnDelay, OnRead;
    public DailyCommandDriver Driver
    {
        get;
    }
    public DailyManagedBusiness Business
    {
        get;
    }
    public DailyWorkflow Workflow
    {
        get;
    }
    public bool Stopped;
    public double Time;
    public long Ticks => 638948160000000000L + (long)(Time * TimeSpan.TicksPerSecond);
    private long sequence;
    private readonly string[] taps;
    public WorkflowHarness(string root, DailyBusinessProof[] proofs, string[]? extraTaps = null)
    {
        Root = root;
        Frame["BridgeVersion"] = DailyStageObservation.BridgeVersion;
        taps = proofs.SelectMany(p => p.EventRoles).Concat(extraTaps ?? []).Distinct().ToArray();
        Box.AfterWrite = (_, name, _) => { if (name == "observation-request.json") Publish(); };
        Box.AfterCommand = command => { OnCommand?.Invoke(command); Publish(); };
        Task<DailyStageFrame> Read()
        {
            OnRead?.Invoke();
            Frame["AtUtcTicks"] = Ticks;
            Publish();
            return Task.FromResult(new DailyStageFrame(Frame.DeepClone().AsObject(), Context.DeepClone().AsObject()));
        }
        Driver = new(root, Box, Read, () => Stopped, () => Ticks, () => Time, delay => { Time += delay.TotalSeconds; OnDelay?.Invoke(); Publish(); return Task.CompletedTask; });
        Driver.Bind(Context);
        Business = new(root, Driver, proofs, () => Stopped);
        var navigation = new DailyStageNavigation(Read, () => Stopped);
        Workflow = new(root, TestPaths.SourceRoot, Context, Driver, Business, navigation, proofs, () => Stopped, (_, _, _) => throw new Exception("Unexpected workflow relay"));
    }
    public void Page(string ui, params string[] fields)
    {
        if (ui == "MenuUI" && !fields.Contains("_objBackButton")) fields = fields.Append("_objBackButton").ToArray();
        Frame["Surfaces"] = new JsonArray(O(("Id", 1), ("Type", ui), ("Popup", ui.EndsWith("PopupUI", StringComparison.Ordinal)), ("Order", 0), ("InputReady", true), ("Targets", Array(fields.Select((field, i) => O(("Id", i + 10), ("Field", field), ("Enabled", true), ("Route", "ui")))))));
        Frame["UiToken"] = ui;
        Publish();
    }
    public JsonObject Evidence() => O(("Config", "workflow-fixture"), ("Frame", Frame), ("AtUtcTicks", Ticks), ("Readings", Array(Readings())), ("Taps", taps), ("Error", ""));
    public void Publish()
    {
        Frame["AtUtcTicks"] = Ticks;
        if (!Box.Values.TryGetValue("live:observation-request.json", out var bytes))
            return;
        var request = JsonNode.Parse(bytes)!;
        var evidence = Evidence();
        evidence["ObservationRequest"] = Copy(request["Id"]);
        Box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
    }
    public void Event(string role, string kind, params (string Key, object? Value)[] values)
    {
        var item = WorkflowCases.Event(role, kind, checked((int)++sequence), values);
        item["Frame"] = Frame.DeepClone();
        item["AtUtcTicks"] = Ticks;
        Box.Values[$"live:events~{Ticks}-{sequence}.json"] = JsonSerializer.SerializeToUtf8Bytes(item);
    }
    public void Dispose() => Driver.Dispose();
}

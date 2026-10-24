using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

// Desktop-only observation journal. No extra game reads, hooks or packet collection.
// Bounded history complements the full before/receipt/final evidence in live/steps.
public sealed class DailyDiagnosticTrail(string root, Func<double> clock, Func<long> now, long maxBytes = 2 * 1024 * 1024, Func<JsonObject?>? readPerformance = null)
{
    readonly string path = Path.Combine(root, "live", "diagnostics", "timeline.jsonl");
    readonly string session = Guid.NewGuid().ToString("N");
    readonly object sync = new();
    string scope = "", key = "";
    double observedAt = double.NegativeInfinity;
    double performanceReadAt = double.NegativeInfinity;
    long performanceTicks;
    long sequence;
    JsonObject? latestFrame;
    readonly Dictionary<string, double> savedFailures = new();
    public string LastError { get; private set; } = "";
    public IDisposable Scope(string name, JsonObject? detail = null)
    {
        string parent = scope;
        scope = parent.Length == 0 ? name : parent + "/" + name;
        Event("begin", detail);
        return new Exit(() => { Event("end"); scope = parent; key = ""; });
    }
    public void Event(string kind, JsonObject? detail = null) => Write(O(("kind", kind), ("detail", detail)));
    public void Failure(string kind, JsonObject detail)
    {
        detail = detail.DeepClone().AsObject();
        if (detail["last_observed"] == null) detail["last_observed"] = latestFrame?.DeepClone();
        Event(kind, detail);
        // Failure-only snapshots survive rotation of the busy observation timeline.
        // Existing live-log age/size retention still applies to this directory.
        string failureKey = scope + "|" + kind + "|" + S(detail["error"] ?? detail["message"]) + "|" + S(detail["last_observed"]?["Scene"]);
        double at = clock();
        if (savedFailures.TryGetValue(failureKey, out var saved) && at - saved < 30) return;
        if (savedFailures.Count >= 64) savedFailures.Remove(savedFailures.MinBy(p => p.Value).Key);
        savedFailures[failureKey] = at;
        try { DailyJson.Write(Path.Combine(root, "live", "diagnostics", "failures", Guid.NewGuid().ToString("N") + ".json"),
            O(("kind", kind), ("detail", detail), ("session", session), ("utc_ticks", now()), ("scope", scope))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; }
    }
    public void Observe(JsonObject frame, string phase = "observe", JsonObject? detail = null)
    {
        PreservePerformance(frame);
        latestFrame = frame.DeepClone().AsObject();
        double at = clock();
        if (at - observedAt < .25) return;
        var rows = DailyNavigationDecision.Rows(frame).OrderByDescending(r => N(r["Order"])).Take(24).ToArray();
        JsonArray Targets(JsonObject r) => Array((r["Targets"] as JsonArray ?? []).OfType<JsonObject>().Take(160)
            .Select(t => O(("id", t["Id"]), ("field", t["Field"]), ("enabled", t["Enabled"]), ("route", t["Route"]))));
        var shapes = Array(rows.Select(r => O(("id", r["Id"]), ("type", r["Type"]), ("ready", r["InputReady"]), ("popup", r["Popup"]), ("targets", Targets(r)))));
        string signature = scope + "|" + S(frame["Scene"]) + "|" + phase + "|" + shapes.ToJsonString();
        if (signature == key && at - observedAt < 5) return;
        if (at - observedAt < .25) return;
        key = signature; observedAt = at;
        var snapshot = O(("kind", "observation"), ("phase", phase), ("detail", detail), ("frame_at", frame["AtUtcTicks"]),
            ("frame_sequence", frame["Sequence"]), ("scene", frame["Scene"]), ("ui_token", frame["UiToken"]), ("process", frame["ProcessId"]),
            ("process_start", frame["ProcessStartTicks"]), ("instance", frame["Instance"]), ("account", frame["AccountKey"]),
            ("player", frame["PlayerKey"]), ("bridge", frame["BridgeVersion"]));
        snapshot["surfaces"] = Array(rows.Select(r =>
        {
            var text = Array((r["Text"] as JsonArray ?? []).Take(12).Select(t => JsonValue.Create(S(t).Length > 256 ? S(t)[..256] : S(t))));
            return O(("id", r["Id"]), ("type", r["Type"]), ("ready", r["InputReady"]), ("popup", r["Popup"]),
                ("order", r["Order"]), ("native_context", r["NativeContext"]), ("text", text), ("targets", Targets(r)));
        }));
        Write(snapshot);
    }
    void PreservePerformance(JsonObject frame)
    {
        // The bridge already measures these values. Reading its existing IPC value
        // does not request another game observation or run work on the game thread.
        if (readPerformance == null || clock() - performanceReadAt < 10) return;
        performanceReadAt = clock();
        var row = O(("kind", "performance"), ("scene", frame["Scene"]),
            ("frame_at", frame["AtUtcTicks"]), ("frame_sequence", frame["Sequence"]),
            ("process", frame["ProcessId"]), ("process_start", frame["ProcessStartTicks"]),
            ("instance", frame["Instance"]),
            ("loading", DailyNavigationDecision.Types(frame).Contains("LoadingUI")));
        try
        {
            var sample = readPerformance();
            long ticks = sample?["AtUtcTicks"]?.GetValue<long>() ?? 0;
            if (sample == null) row["state"] = "unavailable";
            else if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks || ticks - now() > TimeSpan.FromSeconds(2).Ticks)
                row["state"] = "invalid_timestamp";
            else
            {
                if (ticks == performanceTicks) return;
                row["state"] = "sampled";
                // Deliberately allowlist metrics: never persist arbitrary new game
                // payloads or account fields through this small diagnostic stream.
                var metrics = O(("AtUtcTicks", ticks));
                foreach (string name in new[] { "Count", "TargetFps", "RenderFrames", "SlowFrames", "MaxFrameMs", "ObserveMs", "EvidenceMs", "WriteMs", "MaxMs", "FrameDeltaMs", "TimeScale", "CommonMaxMs", "CommonIdentityMs", "CommonControlMs", "CommonStartupMs", "CommonGuildMs", "CommonWriteMs" })
                    if (sample[name] is JsonValue value && value.TryGetValue<double>(out double number) && double.IsFinite(number)) metrics[name] = number;
                foreach (string name in new[] { "Legacy", "Focused" })
                    if (sample[name] is JsonValue value && value.TryGetValue<bool>(out bool flag)) metrics[name] = flag;
                row["metrics"] = metrics;
                performanceTicks = ticks;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or FormatException or TimeoutException or StageHostException)
        {
            // Loss of optional timing evidence must never interrupt navigation.
            row["state"] = "read_failed"; row["error_type"] = error.GetType().Name;
        }
        Write(row, Path.Combine(root, "live", "diagnostics", "performance.jsonl"), 256 * 1024);
    }
    void Write(JsonObject row) => Write(row, path, maxBytes);
    void Write(JsonObject row, string destination, long limit)
    {
        lock (sync)
        {
            try
            {
                row["session"] = session; row["sequence"] = ++sequence; row["utc_ticks"] = now(); row["scope"] = scope; row["version"] = typeof(DailyDiagnosticTrail).Assembly.GetName().Version?.ToString();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination) && new FileInfo(destination).Length >= limit)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        string source = i == 1 ? destination : destination + "." + (i - 1);
                        if (File.Exists(source)) File.Move(source, destination + "." + i, true);
                    }
                }
                File.AppendAllText(destination, row.ToJsonString() + "\n", new UTF8Encoding(false));
                LastError = "";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (LastError != e.Message) Console.Error.WriteLine("诊断记录写入失败，执行记录仍按原流程保存：" + e.Message);
                LastError = e.Message;
            }
        }
    }
    sealed class Exit(Action close) : IDisposable { bool done; public void Dispose() { if (!done) { done = true; close(); } } }
}

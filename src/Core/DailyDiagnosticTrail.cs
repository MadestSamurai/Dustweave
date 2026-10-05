using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

// Desktop-only observation journal. No extra game reads, hooks or packet collection.
// Bounded history complements the full before/receipt/final evidence in live/steps.
public sealed class DailyDiagnosticTrail(string root, Func<double> clock, Func<long> now, long maxBytes = 2 * 1024 * 1024)
{
    readonly string path = Path.Combine(root, "live", "diagnostics", "timeline.jsonl");
    readonly string session = Guid.NewGuid().ToString("N");
    readonly object sync = new();
    string scope = "", key = "";
    double observedAt = double.NegativeInfinity;
    long sequence;
    public string LastError { get; private set; } = "";
    public IDisposable Scope(string name, JsonObject? detail = null)
    {
        string parent = scope;
        scope = parent.Length == 0 ? name : parent + "/" + name;
        Event("begin", detail);
        return new Exit(() => { Event("end"); scope = parent; key = ""; });
    }
    public void Event(string kind, JsonObject? detail = null) => Write(O(("kind", kind), ("detail", detail)));
    public void Observe(JsonObject frame, string phase = "observe", JsonObject? detail = null)
    {
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
    void Write(JsonObject row)
    {
        lock (sync)
        {
            try
            {
                row["session"] = session; row["sequence"] = ++sequence; row["utc_ticks"] = now(); row["scope"] = scope; row["version"] = typeof(DailyDiagnosticTrail).Assembly.GetName().Version?.ToString();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        string source = i == 1 ? path : path + "." + (i - 1);
                        if (File.Exists(source)) File.Move(source, path + "." + i, true);
                    }
                }
                File.AppendAllText(path, row.ToJsonString() + "\n", new UTF8Encoding(false));
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

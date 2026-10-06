using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Read-only recovery of migrated business records; never submits an input.</summary>
public sealed class DailyManagedReconciliation
{
    private readonly string root; private readonly DailyCommandDriver driver; private readonly HashSet<string> roles; private readonly Func<bool> stopped;
    private static readonly HashSet<string> Supported = ["room.info", "mail.collect"];
    public DailyManagedReconciliation(string root, DailyCommandDriver driver, IEnumerable<string> roles, Func<bool> stopped)
    {
        this.root = root;
        this.driver = driver;
        this.roles = roles.ToHashSet(StringComparer.Ordinal);
        this.stopped = stopped;
        if (!this.roles.IsSubsetOf(Supported))
            throw new ArgumentException("Unsupported managed recovery role");
    }
    public string[] Roles => roles.Order(StringComparer.Ordinal).ToArray();
    public static void ValidateRecord(string path, JsonObject op)
    {
        string id = op["id"]?.GetValue<string>() ?? "";
        if (!GuildStore.ValidId(id) || Path.GetFileNameWithoutExtension(path) != id || op["account"] is not JsonValue || op["player"] is not JsonValue || op["server"] is not JsonValue || op["cycle"] is not JsonValue || op["state"] is not JsonValue)
            throw new InvalidDataException("Incomplete or invalid managed business record");
    }
    private static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after) => op["role"]!.GetValue<string>() switch
    {
        "room.info" => DailyEvidence.VerifyRoom(op["before"]!.AsObject(), events, after),
        "mail.collect" => DailyMailProof.Verify(op["before"]!.AsObject(), events, after, op["scope"]!["tab"]!.GetValue<string>()),
        _ => throw new InvalidDataException("Unsupported managed proof")
    };
    public async Task<JsonObject> RunAsync(JsonObject context)
    {
        if (stopped())
            throw new StageHostException("stopped", "Stopped before read-only business reconciliation");
        var current = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(current.Context, context))
            throw new StageHostException("identity", "Recovery queue identity changed");
        var completed = new JsonArray();
        var unresolved = new JsonArray();
        var report = new JsonObject { ["completed"] = completed, ["unresolved"] = unresolved, ["actions"] = 0, ["engine"] = "dotnet-reconciliation-v1" };
        string directory = Path.Combine(root, "live", "business");
        if (!Directory.Exists(directory))
            return report;
        var rows = new List<(string Path, JsonObject Record)>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
            if (!roles.Contains(op["role"]?.GetValue<string>() ?? ""))
                continue;
            ValidateRecord(path, op);
            if (op["state"]!.GetValue<string>() is not ("prepared" or "dispatching" or "unknown"))
                continue;
            if (!JsonNode.DeepEquals(op["account"], context["actor"]![3]) || !JsonNode.DeepEquals(op["player"], context["actor"]![4]) || !JsonNode.DeepEquals(op["server"], context["server"]))
                continue;
            rows.Add((path, op));
        }
        JsonObject? fresh = null;
        bool attempted = false;
        foreach (var (path, op) in rows)
        {
            if (stopped())
                throw new StageHostException("stopped", "Stopped during read-only business reconciliation");
            string role = op["role"]!.GetValue<string>();
            var entry = new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = role, ["stages"] = new JsonArray(op["stage"]?.GetValue<string>() ?? (role == "room.info" ? "room" : "mail")) };
            if (op["state"]!.GetValue<string>() == "prepared")
            {
                op["state"] = "rejected";
                op["reconciliation"] = new JsonObject { ["at"] = driver.UtcTicks, ["method"] = "never_dispatched", ["actions"] = 0 };
                DailyJson.Write(path, op);
                completed.Add(entry);
                continue;
            }
            var original = op["before"]!.AsObject();
            var events = new Dictionary<(string, long), JsonObject>();
            foreach (var node in (op["events"] as JsonArray ?? new JsonArray()).Concat(driver.CollectEvents(role, DailyEvidence.Integer(op["at"]))))
            {
                var evt = node!.AsObject();
                if (evt["Role"]?.GetValue<string>() == role && DailyEvidence.SameActor(evt["Frame"]!.AsObject(), original["Frame"]!.AsObject()))
                    events[(role, DailyEvidence.Integer(evt["Sequence"]))] = evt;
            }
            var selected = new JsonArray(events.Values.OrderBy(e => DailyEvidence.Integer(e["Sequence"])).Select(e => (JsonNode)e.DeepClone()).ToArray());
            string last = "No matching post-operation observation";
            bool ok = false;
            bool Try(JsonObject candidate)
            {
                JsonObject result;
                try
                {
                    result = Verify(op, selected, candidate);
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException) { last = error.Message; return false; }
                string previous = op["state"]!.GetValue<string>();
                op["state"] = "completed";
                op["events"] = selected.DeepClone();
                op["after"] = candidate.DeepClone();
                op["result"] = result;
                op["reconciliation"] = new JsonObject { ["previous_state"] = previous, ["at"] = driver.UtcTicks, ["method"] = "original_proof_and_native_evidence", ["actions"] = 0 };
                op.Remove("error");
                DailyJson.Write(path, op);
                return true;
            }
            foreach (string key in new[] { "after", "last_observation" })
                if (op[key] is JsonObject saved && Try(saved))
                {
                    ok = true;
                    break;
                }
            if (!ok)
            {
                if (!attempted)
                {
                    attempted = true;
                    try
                    {
                        fresh = await driver.EvidenceAsync(rows.Select(r => r.Record["role"]!.GetValue<string>() == "room.info" ? "room" : "mail"));
                    }
                    catch (Exception error) when (error is StageHostException or DailyStepException or IOException or System.Text.Json.JsonException) { report["observation_error"] = error.Message; }
                }
                if (fresh != null && JsonNode.DeepEquals(op["day_cycle"] ?? op["cycle"], context["cycle"]) && DailyEvidence.SameActor(fresh["Frame"]!.AsObject(), original["Frame"]!.AsObject()) && JsonNode.DeepEquals(fresh["Config"], original["Config"]))
                    ok = Try(fresh);
            }
            if (ok)
                completed.Add(entry);
            else
            {
                op["events"] = selected;
                op["recovery_error"] = last;
                DailyJson.Write(path, op);
                entry["reason"] = last;
                entry["recover_in_stage"] = false;
                unresolved.Add(entry);
            }
        }
        return report;
    }
}

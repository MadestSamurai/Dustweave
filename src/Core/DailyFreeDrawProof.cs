using System.Globalization;
using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyNativeProof
{
    public static JsonObject Response(string role, JsonObject before, JsonArray events, JsonObject after, int requests = 1)
    {
        if (before["Config"] == null || !JsonNode.DeepEquals(before["Config"], after["Config"]))
            throw new InvalidDataException("Evidence configuration changed");
        var rows = events.Select(e => e!.AsObject()).Where(e => e["Role"]?.GetValue<string>() == role).ToArray();
        var req = rows.Where(e => e["Kind"]?.GetValue<string>() == "request").ToArray();
        var res = rows.Where(e => e["Kind"]?.GetValue<string>() == "response").ToArray();
        if (req.Length != requests || res.Length != 1 || rows.Length != requests + 1)
            throw new InvalidDataException("Native request/response count mismatch");
        var response = res[0];
        if (response["Error"]?.GetValue<string>() != "" || DailyEvidence.Integer(response["ErrorCode"]) != 0 || response["Accepted"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Native response rejected");
        if (req.Max(e => DailyEvidence.Integer(e["Sequence"])) >= DailyEvidence.Integer(response["Sequence"]) || rows.Any(e => !DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())) || !DailyEvidence.SameActor(after["Frame"]!.AsObject(), before["Frame"]!.AsObject()))
            throw new InvalidDataException("Native response ordering or identity mismatch");
        return DailyEvidence.Values(response);
    }
}
public sealed class DailyFreeDrawRules
{
    public JsonObject Projection
    {
        get;
    }
    private readonly Dictionary<long, (long Group, long Limit)> free = new();
    public DailyFreeDrawRules(JsonObject projection)
    {
        Projection = projection.DeepClone().AsObject();
        if (projection["schema"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Unsupported free draw rules");
        foreach (var node in projection["free"]!.AsArray())
        {
            var row = node!.AsObject();
            long id = DailyEvidence.Integer(row["id"]), group = DailyEvidence.Integer(row["group"]), limit = DailyEvidence.Integer(row["limit"]);
            if (id <= 0 || group <= 0 || limit <= 0 || !free.TryAdd(id, (group, limit)))
                throw new InvalidDataException("Invalid or duplicate daily free draw definition");
        }
        if (free.Count == 0)
            throw new InvalidDataException("Empty daily free draw rules");
    }
    public static DailyFreeDrawRules Load()
    {
        JsonArray Read(string name)
        {
            using var stream = typeof(DailyFreeDrawRules).Assembly.GetManifestResourceStream("Dustweave." + name) ?? throw new IOException("Missing free draw rules");
            return JsonNode.Parse(stream)!.AsArray();
        }
        return Build(Read("GachaTable.json"), Read("GachaGroupTable.json"));
    }
    public static DailyFreeDrawRules Build(JsonArray tableRows, JsonArray groupRows)
    {
        var tables = tableRows.Select(r => r!.AsObject()).ToDictionary(r => DailyEvidence.Integer(r["id"]));
        var groups = groupRows.Select(r => r!.AsObject()).ToArray();
        var rows = new JsonArray();
        var ids = new HashSet<long>();
        foreach (var group in groups)
        {
            long id = DailyEvidence.Integer(group["oneTimeGachaId"]);
            if (id == 0)
                continue;
            if (tables.TryGetValue(id, out var table) && DailyEvidence.Integer(table["freeCountDay"]) > 0 && DailyEvidence.Integer(table["gachaCount"]) == 1)
            {
                if (!ids.Add(id))
                    throw new InvalidDataException("Ambiguous free draw group");
                rows.Add(new JsonObject { ["id"] = id, ["group"] = group["id"]!.DeepClone(), ["limit"] = table["freeCountDay"]!.DeepClone() });
            }
        }
        return new(new()
        {
            ["schema"] = 1,
            ["free"] = rows
        });
    }
    public (long Group, long Limit) Require(long id) => free.TryGetValue(id, out var value) ? value : throw new InvalidDataException("Response includes unsupported or paid draw");
}
public static class DailyFreeDrawProof
{
    public const string Role = "gacha.free_all";
    public static Dictionary<long, JsonObject> Users(JsonObject evidence)
    {
        var keys = DailyEvidence.Reading(evidence, "gacha.users", "Keys")!.AsArray();
        var values = DailyEvidence.Reading(evidence, "gacha.users", "Values")!.AsArray();
        long count = DailyEvidence.Integer(DailyEvidence.Reading(evidence, "gacha.users", "Count"));
        if (count != keys.Count || count != values.Count)
            throw new InvalidDataException("Incomplete gacha cache");
        var result = new Dictionary<long, JsonObject>();
        for (int i = 0; i < keys.Count; i++)
        {
            long id = DailyEvidence.Integer(keys[i]);
            var row = values[i]!.AsObject();
            if (id <= 0 || !result.TryAdd(id, row) || row["groupId"] != null && DailyEvidence.Integer(row["groupId"]) != id)
                throw new InvalidDataException("Invalid or duplicate gacha cache group");
        }
        return result;
    }
    private static long Counter(JsonObject? row, string key)
    {
        long count = row?[key] == null ? 0 : DailyEvidence.Integer(row[key]);
        if (count < 0)
            throw new InvalidDataException("Negative gacha counter");
        return count;
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, DailyFreeDrawRules rules)
    {
        var data = DailyNativeProof.Response(Role, before, events, after);
        var results = data["Results"]!.AsArray();
        if (results.Count == 0)
            throw new InvalidDataException("Empty free draw response");
        var expected = new Dictionary<long, long>();
        foreach (var node in results)
        {
            var row = node!.AsObject();
            var definition = rules.Require(DailyEvidence.Integer(row["id"]));
            if (row["rewardInfoBundle"] is not JsonObject bundle || bundle.Count == 0)
                throw new InvalidDataException("Missing free draw reward");
            expected[definition.Group] = expected.GetValueOrDefault(definition.Group) + 1;
            if (expected[definition.Group] > definition.Limit)
                throw new InvalidDataException("Free draw response exceeds daily definition");
        }
        var old = Users(before);
        var current = Users(after);
        if (old.Keys.Except(current.Keys).Any())
            throw new InvalidDataException("Gacha cache lost an original group");
        foreach (long group in old.Keys.Union(current.Keys))
        {
            old.TryGetValue(group, out var prior);
            current.TryGetValue(group, out var next);
            if (Counter(next, "oneFreePickCount") - Counter(prior, "oneFreePickCount") != expected.GetValueOrDefault(group))
                throw new InvalidDataException("Free draw counter mismatch");
            foreach (string key in new[] { "oneCashPickCount", "tenCashPickCount", "totalBuyCount", "tenFreePickCount" })
                if (Counter(next, key) != Counter(prior, key))
                    throw new InvalidDataException("Unexpected paid or ten-draw counter change");
        }
        var counts = new JsonObject();
        foreach (var pair in expected)
            counts[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
        return new()
        {
            ["draws"] = results.Count,
            ["groups"] = counts,
            ["results"] = results.DeepClone(),
            ["free_cache_matched"] = true
        };
    }
    public static DailyBusinessProof Definition() => new(Role, "free_draws", ["gacha", "missions.cache"], (op, events, after) => Verify(op["before"]!.AsObject(), events, after, new DailyFreeDrawRules(op["rules"]!.AsObject())), PreviewOwner: DailyFreeDrawStage.OwnsPreview);
}



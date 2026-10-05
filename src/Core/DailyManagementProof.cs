using System.Globalization;
using System.Text.Json.Nodes;
namespace BD2Daily;

public static class DailyManagementProof
{
    public const string Role = "management.collect", Helpers = "life.helpers", Cafeteria = "cafeteria.collect", Fishing = "fishing.trap";
    public static bool IsFreeClaim(string role) => role is Role or Helpers or Cafeteria or Fishing;
    public static readonly (string Role, string Ui)[] Categories = [(Cafeteria, "cafeteria.ui"), (Fishing, "fishing.ui"), (Helpers, "life.helpers_ui")];
    public static readonly string[] Prefixes = ["management", "cafeteria.cache", "cafeteria.ui", "fishing", "life"];
    public static long Timestamp(JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
            return parsed;
        long number = DailyEvidence.Integer(value);
        return number >= 0 ? number : throw new InvalidDataException("Negative settlement timestamp");
    }
    public static bool Claimable(JsonObject evidence, string id) => DailyEvidence.Reading(evidence, id, "IsDisable()")?.GetValue<bool>() == false && DailyEvidence.Reading(evidence, id, "IsCanSettlement()")?.GetValue<bool>() == true;
    public static string[] Eligible(JsonObject evidence) => Categories.Where(c => Claimable(evidence, c.Ui)).Select(c => c.Role).ToArray();
    private static long Number(JsonObject row, string key) => row[key] == null ? 0 : DailyEvidence.Integer(row[key]);
    public static Dictionary<long, JsonObject> HelperCache(JsonObject evidence)
    {
        long count = DailyEvidence.Integer(DailyEvidence.Reading(evidence, "life.helpers_cache", "Count"));
        var values = DailyEvidence.Reading(evidence, "life.helpers_cache", "Values")!.AsArray();
        if (count != values.Count)
            throw new InvalidDataException("Incomplete helper cache");
        var rows = new Dictionary<long, JsonObject>();
        foreach (var node in values)
        {
            var row = node!.AsObject();
            long slot = Number(row, "helperSlotId");
            if (slot <= 0 || !rows.TryAdd(slot, row))
                throw new InvalidDataException("Invalid or duplicate helper slot");
        }
        return rows;
    }
    public static JsonObject VerifyHelpers(JsonObject before, JsonArray events, JsonObject after)
    {
        var data = DailyNativeProof.Response(Helpers, before, events, after);
        if (!Claimable(before, "life.helpers_ui"))
            throw new InvalidDataException("Helper rewards were not claimable");
        if (DailyEvidence.Integer(DailyEvidence.Reading(before, "life.helpers_pending", "Count")) <= 0 || DailyEvidence.Integer(DailyEvidence.Reading(after, "life.helpers_pending", "Count")) != 0)
            throw new InvalidDataException("Helper pending rewards did not clear");
        var old = HelperCache(before);
        var fresh = HelperCache(after);
        var returned = new Dictionary<long, JsonObject>();
        foreach (var node in data["HelperInfo"]!.AsArray())
        {
            var row = node!.AsObject();
            long slot = Number(row, "helperSlotId");
            if (!returned.TryAdd(slot, row) || !old.ContainsKey(slot))
                throw new InvalidDataException("Unexpected helper response slot");
        }
        if (returned.Count == 0 || !old.Keys.ToHashSet().SetEquals(fresh.Keys))
            throw new InvalidDataException("Helper response or cache lost slots");
        int advanced = 0;
        foreach (var (slot, row) in returned)
        {
            if (!JsonNode.DeepEquals(fresh[slot], row))
                throw new InvalidDataException("Helper response/cache mismatch");
            foreach (string key in new[] { "helperIndex", "helperId", "workType", "workId" })
                if (Number(row, key) != Number(old[slot], key))
                    throw new InvalidDataException("Helper assignment changed during settlement");
            long previous = Timestamp(old[slot]["assignDate"]), current = Timestamp(row["assignDate"]);
            if (current < previous)
                throw new InvalidDataException("Helper accrual timestamp moved backwards");
            if (current == previous && !JsonNode.DeepEquals(row, old[slot]))
                throw new InvalidDataException("Unsettled helper changed during settlement");
            if (current > previous)
                advanced++;
        }
        foreach (long slot in old.Keys.Except(returned.Keys))
            if (!JsonNode.DeepEquals(old[slot], fresh[slot]))
                throw new InvalidDataException("Unreturned helper assignment changed");
        if (advanced == 0)
            throw new InvalidDataException("No helper accrual timestamp advanced");
        var rewards = data["RewardBundle"]!["itemInfo"]!.AsArray();
        if (rewards.Count == 0)
            throw new InvalidDataException("Empty helper reward bundle");
        return new()
        {
            ["helper_slots"] = new JsonArray(returned.Keys.Order().Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
            ["rewards"] = rewards.DeepClone(),
            ["settled_slots"] = advanced,
            ["unchanged_slots"] = returned.Count - advanced
        };
    }
    public static JsonObject VerifyCafeteria(JsonObject before, JsonArray events, JsonObject after)
    {
        var data = DailyNativeProof.Response(Cafeteria, before, events, after);
        long stamp = Timestamp(data["RewardReceiptTime"]);
        if (stamp <= Timestamp(DailyEvidence.Reading(before, "cafeteria.cache", "RewardReceiptTime")) || stamp != Timestamp(DailyEvidence.Reading(after, "cafeteria.cache", "RewardReceiptTime")))
            throw new InvalidDataException("Cafeteria settlement timestamp/cache mismatch");
        return new()
        {
            ["outcome"] = "collected",
            ["cache_matched"] = true,
            ["reward_info"] = data["CumulativeRewardInfo"]?.DeepClone() ?? throw new InvalidDataException("Missing cafeteria reward"),
            ["receipt_time"] = data["RewardReceiptTime"]!.DeepClone()
        };
    }
    public static JsonObject VerifyFishing(JsonObject before, JsonArray events, JsonObject after)
    {
        var data = DailyNativeProof.Response(Fishing, before, events, after);
        if (Timestamp(data["TrapRewardReceiptTime"]) <= Timestamp(DailyEvidence.Reading(before, "fishing.trap", "TrapRewardReceiptTime")))
            throw new InvalidDataException("Fishing settlement timestamp did not advance");
        foreach (string key in new[] { "Level", "Exp" })
            if (!JsonNode.DeepEquals(DailyEvidence.Reading(after, "fishing.player", key), data[key]))
                throw new InvalidDataException("Fishing response/cache mismatch");
        if (DailyEvidence.Reading(after, "fishing.ui", "IsCanSettlement()")?.GetValue<bool>() != false)
            throw new InvalidDataException("Fishing trap settlement remains pending");
        return data;
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after)
    {
        var eligible = Eligible(before);
        if (eligible.Length == 0)
            throw new InvalidDataException("Nothing was eligible before management settlement");
        var results = new JsonObject();
        foreach (var (role, ui) in Categories)
        {
            if (!eligible.Contains(role, StringComparer.Ordinal))
            {
                if (events.Any(e => e?["Role"]?.GetValue<string>() == role))
                    throw new InvalidDataException("Unexpected settlement in an ineligible category");
                continue;
            }
            results[role] = role switch
            {
                Cafeteria => VerifyCafeteria(before, events, after),
                Fishing => VerifyFishing(before, events, after),
                Helpers => VerifyHelpers(before, events, after),
                _ => throw new InvalidDataException("Unknown management category")
            };
        }
        return new()
        {
            ["categories"] = results,
            ["ui_submissions"] = 1
        };
    }
    public static DailyBusinessProof[] Definitions() => [
        new(Role,"management",Prefixes,(op,e,a)=>Verify(op["before"]!.AsObject(),e,a),Categories.Select(c=>c.Role).ToArray(),["management","cafeteria_income","life_helpers"]),
        new(Helpers,"life_helpers",["life"],(op,e,a)=>VerifyHelpers(op["before"]!.AsObject(),e,a),AffectedStages:["management","cafeteria_income","life_helpers"]),
        new(Cafeteria,"cafeteria_income",["cafeteria"],(op,e,a)=>VerifyCafeteria(op["before"]!.AsObject(),e,a),AffectedStages:["management","cafeteria_income"]),
        new(Fishing,"cafeteria_income",["fishing"],(op,e,a)=>VerifyFishing(op["before"]!.AsObject(),e,a),AffectedStages:["management","cafeteria_income"])
    ];
}

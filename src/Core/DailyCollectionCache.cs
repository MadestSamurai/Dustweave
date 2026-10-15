using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

// Server observations are scoped by map AND category. A collection-only reply
// must not erase theft cooldowns, and a local recovery must not erase other maps.
internal static class DailyCollectionCache
{
    internal static readonly object Gate = new();
    private static double At(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : 0;
    private static bool Newer(JsonNode incoming, JsonNode prior)
    {
        // Export/write time is not observation time. An older server reply arriving
        // through another launch mode cannot regress a more recent map state.
        long next = N(incoming["network"]?["Now"]), previous = N(prior["network"]?["Now"]);
        if (next != previous) return next > previous;
        long nextAt = N(incoming["network"]?["At"]), previousAt = N(prior["network"]?["At"]);
        if (nextAt > 0 && previousAt > 0 && nextAt != previousAt) return nextAt > previousAt;
        return At(incoming["at"]) > At(prior["at"]);
    }
    private static bool SameOwner(JsonObject a, JsonObject b) => new[] { "account", "player", "server" }.All(k => JsonNode.DeepEquals(a[k], b[k]))
        && S(a["network"]?["Week"]) == S(b["network"]?["Week"]) && N(a["network"]?["Pack"]) == N(b["network"]?["Pack"]);
    private static JsonObject Project(JsonObject network, long map)
    {
        var n = network.DeepClone().AsObject();
        n["Maps"] = new JsonArray(map);
        foreach (string key in new[] { "Drops", "Targets", "DeferredTargets", "StealTargets" })
            n[key] = Array(Rows(network[key] ?? new JsonArray()).Where(r => N(r["Map"]) == map));
        var drops = Rows(n["Drops"]).Select(r => N(r["Id"])).ToHashSet();
        var monsters = Rows(n["Targets"]).Select(r => N(r["Id"])).ToHashSet();
        n["Rewards"] = Array(Rows(JsonNode.Parse(S(network["Rewards"]))).Where(r => drops.Contains(N(r["id"])))).ToJsonString();
        n["Monsters"] = Array(Rows(JsonNode.Parse(S(network["Monsters"]))).Where(r => monsters.Contains(N(r["monsterId"])))).ToJsonString();
        if (B(n["StealReady"]))
        {
            var targets = Rows(n["StealTargets"]).Select(r => (N(r["Npc"]), N(r["Group"]))).ToHashSet();
            n["Steals"] = Array(Rows(JsonNode.Parse(S(network["Steals"]))).Where(r => targets.Contains((N(r["npcId"]), N(r["groupId"]))))).ToJsonString();
        }
        return n;
    }
    private static JsonObject Index(JsonObject record)
    {
        var network = record["network"] as JsonObject ?? throw new InvalidDataException("Collection network missing");
        if (record["map_progress"] is JsonObject saved)
        {
            foreach (var map in saved)
            {
                Require(long.TryParse(map.Key, out long id) && id > 0, "Collection map key invalid");
                var categories = map.Value as JsonObject ?? throw new InvalidDataException("Collection map categories missing");
                foreach (var category in categories)
                {
                    Require(category.Key is "collection" or "steal", "Collection category invalid");
                    var observation = category.Value as JsonObject ?? throw new InvalidDataException("Collection observation missing");
                    var n = observation["network"] as JsonObject ?? throw new InvalidDataException("Collection network missing");
                    Require(S(n["Week"]) == S(network["Week"]), "Collection cache cycle mismatch");
                    DailyCollectionProgress.Classify(n, N(network["Pack"]), [id], category.Key == "steal", category.Key == "steal");
                }
            }
            return saved.DeepClone().AsObject();
        }
        var index = new JsonObject();
        var maps = network["Maps"] as JsonArray ?? throw new InvalidDataException("Collection maps missing");
        DailyCollectionProgress.Classify(network, N(network["Pack"]), maps.Select(N).ToArray(), B(network["StealReady"]));
        foreach (long map in network["Maps"]!.AsArray().Select(N))
        {
            var observation = O(("at", record["at"]), ("network", Project(network, map)));
            var categories = O(("collection", observation));
            if (B(network["StealReady"])) categories["steal"] = Copy(observation);
            index[map.ToString()] = categories;
        }
        return index;
    }
    public static JsonObject Merge(JsonObject? old, JsonObject incoming)
    {
        if (old != null && (N(old["schema"]) != 1 || S(old["source"]) != "FieldObjectInfoResponse+MonsterInfoResponse")) old = null;
        if (old == null || !SameOwner(old, incoming))
        {
            var first = incoming.DeepClone().AsObject(); first["map_progress"] = Index(incoming); return first;
        }
        var result = (At(old["at"]) > At(incoming["at"]) ? old : incoming).DeepClone().AsObject();
        var index = Index(old);
        foreach (var map in Index(incoming))
        {
            if (index[map.Key] is not JsonObject categories) index[map.Key] = categories = new();
            foreach (var category in map.Value!.AsObject())
                if (categories[category.Key] == null || Newer(category.Value!, categories[category.Key]!))
                    categories[category.Key] = Copy(category.Value);
        }
        result["map_progress"] = index;
        return result;
    }
    public static JsonObject Read(JsonObject record, long pack, long[] maps, bool steal, bool onlySteal, bool refreshStoryLocks)
    {
        Require(!onlySteal || steal, "Steal-only requires server cooldown");
        var index = Index(record); var states = new JsonObject(); var complete = new JsonObject();
        foreach (long map in maps)
        {
            JsonObject Category(string key)
            {
                if (index[map.ToString()]?[key]?["network"] is not JsonObject n)
                    throw new DailyCollectionSyncRequired(key == "steal" ? "需要补读本卡带偷窃服务器记录" : "需要补读本卡带新增地图进度");
                Require(S(n["Week"]) == S(record["network"]!["Week"]), "Collection cache cycle mismatch");
                if (key == "collection" && refreshStoryLocks && Rows(n["DeferredTargets"] ?? new JsonArray()).Length > 0)
                    throw new DailyCollectionSyncRequired("需要重新核对本地图的剧情解锁进度");
                return Classify(n, key == "steal");
            }
            JsonObject Classify(JsonObject n, bool theft) => DailyCollectionProgress.Classify(n, pack, [map], theft, theft)["maps"]![map.ToString()]!.AsObject();
            var state = onlySteal ? O(("drops", new JsonArray()), ("monsters", new JsonArray())) : Category("collection").DeepClone().AsObject();
            if (steal) state["steals"] = Copy(Category("steal")["steals"]);
            states[map.ToString()] = state;
            if (state.All(p => p.Value is JsonArray { Count: 0 })) complete[map.ToString()] = Copy(state);
        }
        return O(("source", "game_network"), ("weekly_reset", S(record["network"]!["Week"])), ("maps", states), ("completed", complete));
    }
    public static IEnumerable<JsonObject> CollectionNetworks(JsonObject record) => Index(record).Select(p => p.Value?["collection"]?["network"]).OfType<JsonObject>();
}

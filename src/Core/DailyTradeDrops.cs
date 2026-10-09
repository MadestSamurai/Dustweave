using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Weighted field reward expectations from the client's static tables. No game input.</summary>
public static class DailyTradeDrops
{
    public static JsonObject Build(string folder, JsonObject catalog)
    {
        JsonObject[] Read(string path) => Rows(JsonNode.Parse(File.ReadAllText(path).TrimStart('\uFEFF')));
        string common = Path.Combine(folder, "common");
        var manifest = DailyTradeCatalog.Read(Path.Combine(common, "manifest.json"));
        Require(string.Equals(S(manifest["databaseSha256"]), S(catalog["database_sha256"]), StringComparison.OrdinalIgnoreCase), "Drop and trade databases differ");
        var rewards = Read(Path.Combine(common, "RewardGroupTable.json")).ToDictionary(r => N(r["id"]));
        var boxes = Read(Path.Combine(common, "RandomBoxTable.json")).ToDictionary(r => N(r["id"]));

        var valued = Rows(catalog["items"]).ToDictionary(r => N(r["id"]), r => N(r["sale"]));
        JsonObject Reward(long type, long id, long count, HashSet<long>? ancestors = null)
        {
            Require(count >= 0, "Negative drop quantity");
            if (type == 5) return O(("items", valued.ContainsKey(id) ? O((id.ToString(), count)) : new JsonObject()));
            if (type != 9 || count == 0) return O(("items", new JsonObject()));
            Require(boxes.TryGetValue(id, out var box), "Missing field random box");
            return O(("repeat", count), ("child", Group(N(box!["rewardGroupId"]), ancestors ?? [])));
        }
        JsonObject Group(long id, HashSet<long> ancestors)
        {
            Require(!ancestors.Contains(id) && rewards.TryGetValue(id, out _), "Missing or recursive reward group");
            var path = new HashSet<long>(ancestors) { id }; var r = rewards[id];
            var types = r["itemType"]!.AsArray(); var ids = r["itemId"]!.AsArray(); var counts = r["itemCount"]!.AsArray(); var weights = r["ratio"]!.AsArray();
            Require(types.Count == ids.Count && types.Count == counts.Count && types.Count == weights.Count && types.Count > 0, "Incomplete reward group");
            Require(N(r["dropType"]) is 0 or 1 && N(r["dropCount"]) >= 0 && weights.All(w => N(w) >= 0), "Unknown field drop rule");
            var branches = new JsonArray();
            for (int i = 0; i < types.Count; i++) branches.Add(O(("weight", weights[i]), ("child", Reward(N(types[i]), N(ids[i]), N(counts[i]), path))));
            Require(N(r["dropType"]) == 1 || weights.Sum(N) > 0, "Empty drop weights");
            return O(("draws", r["dropCount"]), ("all", N(r["dropType"]) == 1), ("choices", branches));
        }
        var entries = new JsonArray(); var sources = new JsonArray();
        foreach (var dir in Directory.GetDirectories(folder).Where(p => long.TryParse(Path.GetFileName(p), out _)).Order())
        {
            long pack = long.Parse(Path.GetFileName(dir)); var source = DailyTradeCatalog.Read(Path.Combine(dir, "manifest.json"));
            Require(string.Equals(S(source["assemblySha256"]), S(manifest["assemblySha256"]), StringComparison.OrdinalIgnoreCase), "Mixed drop assemblies");
            sources.Add(O(("pack", pack), ("database", source["databaseFile"]), ("sha256", source["databaseSha256"])));
            var groups = Read(Path.Combine(dir, "FieldRewardObjectGroupTable.json")).ToDictionary(r => N(r["id"]));
            var decks = Read(Path.Combine(dir, "BattleDeckTable.json")).ToDictionary(r => N(r["id"]));
            void Add(string kind, long id, long map, JsonObject distribution)
            {
                if (Mean(distribution).Values.Sum() > 0) entries.Add(O(("pack", pack), ("kind", kind), ("id", id), ("map", map), ("reward", distribution)));
            }
            foreach (var o in Read(Path.Combine(dir, "FieldRewardObjectTable.json")))
            {
                var g = groups[N(o["fieldObjectGroupId"])];
                if (N(o["mapId"]) > 0 && N(g["type"]) is 1 or 3 && N(g["resetType"]) == 3)
                    Add("pickup", N(o["id"]), N(o["mapId"]), Group(N(g["rewardGroupId"]), []));
            }
            var regens = Read(Path.Combine(dir, "FieldMonsterRegenTable.json")).ToDictionary(r => N(r["id"]));
            foreach (var m in Read(Path.Combine(dir, "FieldMonsterTable.json")))
            {
                if (!regens.ContainsKey(N(m["regenId"])) || N(m["type"]) == 3 || !(N(m["useBattleSkip"]) == 1 || N(m["type"]) == 2)) continue;
                if (N(m["type"]) == 2) Add("monster", N(m["id"]), 0, Reward(N(m["rewardType"]), N(m["rewardId"]), N(m["rewardCount"])));
                else
                {
                    var variants = new List<JsonObject>();
                    foreach (long id in m["battleDeckId"]!.AsArray().Select(N))
                    {
                        if (!decks.TryGetValue(id, out var d)) continue;
                        var types = d["rewardType"]!.AsArray();var ids = d["rewardId"]!.AsArray();var counts = d["rewardCount"]!.AsArray();
                        Require(types.Count == ids.Count && ids.Count == counts.Count, "Incomplete monster reward");
                        variants.Add(O(("all", true), ("choices", Array(types.Select((t, i) => O(("weight", 1), ("child", Reward(N(t), N(ids[i]), N(counts[i])))))))));
                    }
                    if (variants.Count > 0) Add("monster", N(m["id"]), 0, variants.OrderBy(v => Mean(v).Sum(p => p.Value * valued[p.Key])).First());
                }
            }
        }
        return O(("schema", 1), ("database_sha256", catalog["database_sha256"]), ("assembly_sha256", manifest["assemblySha256"]),
            ("model", "weighted_reward_draws"), ("monster_difficulty", "minimum_available_reward"), ("sources", sources), ("entries", entries));
    }

    public static Dictionary<long, double> Mean(JsonObject node)
    {
        if (node["items"] is JsonObject items) return items.ToDictionary(p => long.Parse(p.Key), p => (double)N(p.Value));
        if (node["child"] is JsonObject child) return Mean(child).ToDictionary(p => p.Key, p => p.Value * N(node["repeat"]));
        var branches = Rows(node["choices"]); double total = branches.Sum(r => (double)N(r["weight"]));
        var result = new Dictionary<long, double>(); bool all = B(node["all"]);
        foreach (var branch in branches)
            foreach (var item in Mean(branch["child"]!.AsObject()))
                result[item.Key] = result.GetValueOrDefault(item.Key) + item.Value * (all ? 1 : N(node["draws"]) * N(branch["weight"]) / total);
        return result;
    }

    public static Dictionary<long, long> Sample(JsonObject node, Random rng)
    {
        var result = new Dictionary<long, long>();
        void Add(JsonObject part) { foreach (var p in Sample(part, rng)) result[p.Key] = checked(result.GetValueOrDefault(p.Key) + p.Value); }
        if (node["items"] is JsonObject items) return items.ToDictionary(p => long.Parse(p.Key), p => N(p.Value));
        if (node["child"] is JsonObject child) { for (int i = 0; i < N(node["repeat"]); i++) Add(child); return result; }
        var branches = Rows(node["choices"]);
        if (B(node["all"])) foreach (var branch in branches) Add(branch["child"]!.AsObject());
        else for (int i = 0; i < N(node["draws"]); i++)
        {
            long target = rng.NextInt64(branches.Sum(b => N(b["weight"])));
            foreach (var branch in branches) { target -= N(branch["weight"]); if (target < 0) { Add(branch["child"]!.AsObject()); break; } }
        }
        return result;
    }
}

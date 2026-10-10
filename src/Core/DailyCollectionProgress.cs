using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyCollectionCatalog(DailyWorkflow workflow)
{
    private readonly Dictionary<long, JsonObject> cartridges = Rows(workflow.Asset("collection-catalog.json")["cartridges"]).ToDictionary(r => N(r["id"]));
    private readonly Dictionary<long, JsonObject> steals = Rows(workflow.Asset("steal-catalog.json")["packs"]).ToDictionary(r => N(r["id"]));
    public bool Supported(long pack) => pack is >= 1 and <= 19 || cartridges.ContainsKey(pack);
    public long[] Packs() => Enumerable.Range(1, 19).Select(id => (long)id)
        .Concat(cartridges.Where(p => (S(p.Value["kind"]) is "character" or "event") && p.Value["maps"] is JsonArray { Count: > 0 }).Select(p => p.Key).Order())
        .Distinct().ToArray();
    public long[] AvailablePacks(JsonObject evidence)
    {
        var owned = Rows(R(evidence, "mainline.owned_packs", "$items"));
        var definitions = Rows(R(evidence, "mainline.packs", "$items"));
        Require(owned.Length > 0 && owned.Length == N(R(evidence, "mainline.owned_packs", "Count")) &&
            definitions.Length > 0 && definitions.Length == N(R(evidence, "mainline.packs", "Count")), "collection_pack_list_incomplete");
        Require(owned.All(p => N(p["Key"]) > 0 && N(p["Key"]) == N(p["Value.Id"])), "collection_pack_list_incomplete");
        var ids = owned.Select(p => N(p["Value.Id"])).ToHashSet();
        var fields = definitions.Where(p => N(p["PackType"]) is 0 or 1 or 6).Select(p => N(p["Id"])).ToHashSet();
        return Packs().Where(id => ids.Contains(id) && fields.Contains(id)).ToArray();
    }
    public HashSet<long> Transit(long pack) => cartridges[pack]["transitMaps"]!.AsArray().Select(N).ToHashSet();
    public long[] Maps(long pack) => pack switch { 6 => [601, 602, 606, 605, 608, 607], 11 => [1101, 1102, 1103], 14 => [141, 143, 142, 144], 15 => [151, 153, 154], 18 => [181, 186, 183], 1001 => [10011, 10014, 10015], 1002 => [10021, 10022, 10023], 1003 => [10031, 10032, 10033], _ => cartridges.TryGetValue(pack, out var c) ? c["maps"]!.AsArray().Select(N).ToArray() : pack is >= 1 and <= 19 ? new long[] { 1, 2, 3 }.Select(offset => (pack == 1 ? 0 : pack * 10) + offset).ToArray() : throw new InvalidDataException("Unsupported collection cartridge") };
    public long[] StealMaps(long pack)
    {
        Require(steals.TryGetValue(pack, out var p), "卡带尚无偷窃静态资料：" + pack);
        if (S(p!["status"]) is "empty" or "hidden")
            return [];
        Require(S(p["status"]) == "targets" && p["maps"] is JsonArray a && a.Count > 0, "Incomplete steal catalog");
        return p["maps"]!.AsArray().Select(N).ToArray();
    }
}
public sealed class DailyCollectionProgress(DailyWorkflow workflow)
{
    public bool Steal
    {
        get; set;
    }
    public bool OnlySteal
    {
        get; set;
    }
    public static JsonObject Classify(JsonObject data, long pack, long[] maps, bool steal = false, bool onlySteal = false)
    {
        Require(S(data["State"]) == "ready" && S(data["Error"]) == "" && N(data["Pack"]) == pack && data["Maps"]!.AsArray().Select(N).SequenceEqual(maps), "Collection response incomplete or for another cartridge");
        Require(N(data["Week"]) > 0 && N(data["Now"]) > 0, "Collection server clock missing");
        var result = new JsonObject(maps.Select(id => new KeyValuePair<string, JsonNode?>(id.ToString(), O(("drops", new JsonArray()), ("monsters", new JsonArray())))));
        var obtained = Rows(JsonNode.Parse(S(data["Rewards"])));
        var monsters = Rows(JsonNode.Parse(S(data["Monsters"])));
        var taken = obtained.Select(r => N(r["id"])).ToHashSet();
        var lookup = monsters.ToDictionary(r => N(r["monsterId"]));
        var seen = new HashSet<(long, long)>();
        foreach (var target in Rows(data["Drops"]))
        {
            long map = N(target["Map"]), id = N(target["Id"]);
            Require(result.ContainsKey(map.ToString()) && seen.Add((map, id)), "Invalid drop projection");
            if (!taken.Contains(id))
                result[map.ToString()]!["drops"]!.AsArray().Add(id);
        }
        seen.Clear();
        foreach (var target in Rows(data["Targets"]))
        {
            long map = N(target["Map"]), id = N(target["Id"]);
            Require(result.ContainsKey(map.ToString()) && seen.Add((map, id)), "Invalid monster projection");
            Require(lookup.TryGetValue(id, out var value) && N(value["groupId"]) == N(target["Group"]), "Server monster target missing");
            if (B(value!["activeFlag"]) && N(value["respawnTime"]) <= N(data["Now"]))
                result[map.ToString()]!["monsters"]!.AsArray().Add(id);
        }
        if (steal)
        {
            Require(B(data["StealReady"]), "NPC server cooldown not read");
            var records = Rows(JsonNode.Parse(S(data["Steals"])));
            var ends = records.ToDictionary(r => (N(r["npcId"]), N(r["groupId"])), r => N(r["endTime"]));
            foreach (var v in result)
                v.Value!["steals"] = new JsonArray();
            seen.Clear();
            foreach (var t in Rows(data["StealTargets"]))
            {
                long npc = N(t["Npc"]), group = N(t["Group"]), map = N(t["Map"]);
                Require(result.ContainsKey(map.ToString()) && npc > 0 && group > 0 && seen.Add((npc, group)), "Invalid steal projection");
                if (ends.GetValueOrDefault((npc, group)) <= N(data["Now"]))
                    result[map.ToString()]!["steals"]!.AsArray().Add(t.DeepClone());
            }
        }
        if (onlySteal)
        {
            Require(steal, "Steal-only requires server cooldown");
            foreach (var p in result)
            {
                p.Value!["drops"] = new JsonArray();
                p.Value["monsters"] = new JsonArray();
            }
        }
        var complete = new JsonObject(result.Where(p => p.Value!.AsObject().All(v => v.Value!.AsArray().Count == 0)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value!.DeepClone())));
        return O(("source", "game_network"), ("weekly_reset", S(data["Week"])), ("maps", result), ("completed", complete));
    }
    private string PathFor(string week, long pack) => Path.Combine(workflow.Root, "live", "server-collection", S(workflow.Context["actor"]![3]), week, pack + ".json");
    public JsonObject? Cached(long pack, long[] maps, string week, bool missingAllowed = false)
    {
        Require(week.Length > 0 && week.All(char.IsDigit), "Invalid weekly scope");
        string path = PathFor(week, pack);
        if (!File.Exists(path))
        {
            if (missingAllowed)
                return null;
            throw new DailyCollectionSyncRequired("尚无本周服务器地图记录");
        }
        try
        {
            var stored = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Require(N(stored["schema"]) == 1 && S(stored["source"]) == "FieldObjectInfoResponse+MonsterInfoResponse" && JsonNode.DeepEquals(stored["account"], workflow.Context["actor"]![3]) && JsonNode.DeepEquals(stored["player"], workflow.Context["actor"]![4]) && S(stored["network"]!["Week"]) == week, "Collection cache identity/cycle mismatch");
            var network = stored["network"]!.AsObject();
            if (Steal && !B(network["StealReady"]))
            {
                if (missingAllowed)
                    return null;
                throw new DailyCollectionSyncRequired("需要补读本卡带偷窃服务器记录");
            }
            Classify(network, pack, network["Maps"]!.AsArray().Select(N).ToArray(), Steal, OnlySteal);
            if (!maps.All(id => network["Maps"]!.AsArray().Any(m => N(m) == id)))
            {
                if (missingAllowed)
                    return null;
                throw new DailyCollectionSyncRequired("需要补读本卡带新增地图进度");
            }
            var projected = network.DeepClone().AsObject();
            projected["Maps"] = new JsonArray(maps.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
            foreach (string key in new[] { "Drops", "Targets", "StealTargets" })
                if (projected[key] is JsonArray rows)
                    projected[key] = Array(Rows(rows).Where(r => maps.Contains(N(r["Map"]))));
            return Classify(projected, pack, maps, Steal, OnlySteal);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { throw new DailyCollectionSyncRequired("服务器收集记录异常，请按需手动检查；日常不自动全图扫描：" + e.Message); }
    }
    public async Task<JsonObject> Query(long pack, long[] maps)
    {
        var command = await workflow.Step(O(("ui", "GameFieldDefaultUI"), ("operation", Steal ? "collection_query_steal" : "collection_query"), ("value", pack), ("items", maps), ("reason", "读取本卡带服务器收集进度")));
        JsonObject? data = null;
        await workflow.WaitEvidence(["mainline.network"], e => { var n = State(e, "mainline.network", "$self"); if (S(n["Query"]) != S(command["id"])) return false; if (S(n["State"]) == "failed") throw new StageHostException("adapter", S(n["Error"])); if (S(n["State"]) != "ready") return false; data = n; Classify(n, pack, maps, Steal, OnlySteal); return true; }, 35, "服务器收集进度未返回；未使用本地执行记录代替");
        var result = Classify(data!, pack, maps, Steal, OnlySteal);
        DailyJson.Write(PathFor(S(data!["Week"]), pack), O(("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", workflow.Context["actor"]![3]), ("player", workflow.Context["actor"]![4]), ("network", data), ("at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d)));
        DailyJson.Write(Path.Combine(workflow.Root, "live", "mainline", S(workflow.Context["actor"]![3]), "network-" + pack + ".json"), O(("result", result), ("network", data), ("actor", workflow.Context["actor"]), ("at", workflow.Driver.UtcTicks)));
        return result;
    }
}

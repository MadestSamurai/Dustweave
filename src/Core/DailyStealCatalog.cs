using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

/// <summary>Rebuilds the weekly NPC index from explicit client exports, without a live game.</summary>
public static class DailyStealCatalog
{
    public static JsonObject Build(string folder)
    {
        string common = Path.Combine(folder, "common");
        var manifest = DailyTradeCatalog.Read(Path.Combine(common, "manifest.json"));
        JsonObject[] Read(string directory, JsonObject provenance, string name)
        {
            var rows = Rows(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")).TrimStart('\uFEFF')));
            Require(provenance["tables"]?[name] != null && rows.Length == N(provenance["tables"]![name]), "Incomplete " + name);
            return rows;
        }
        var packs = Read(common, manifest, "PackTable");
        var maps = Read(common, manifest, "MapTable").ToDictionary(r => N(r["id"]));
        var names = Read(common, manifest, "NameTextTable").ToDictionary(r => N(r["id"]));
        var groups = Read(common, manifest, "TalentSkillTable").Where(r => N(r["classType"]) == 1 && N(r["resetType"]) == 2).Select(r => N(r["groupId"])).ToHashSet();
        Require(groups.Count > 0, "No weekly stealing talent in export");
        string Name(JsonNode? key) => names.TryGetValue(N(key), out var row) ? S(row["textCn"]) : throw new InvalidDataException("Missing NPC name row");
        var result = new JsonArray();
        foreach (var pack in packs.OrderBy(r => N(r["id"])))
        {
            long id = N(pack["id"]);
            string directory = Path.Combine(folder, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var source = DailyTradeCatalog.Read(Path.Combine(directory, "manifest.json"));
            Require(string.Equals(S(source["assemblySha256"]), S(manifest["assemblySha256"]), StringComparison.OrdinalIgnoreCase), "Mixed client assemblies in NPC export");
            var seen = new HashSet<long>();
            var targets = new JsonArray();
            foreach (var npc in Read(directory, source, "FieldNpcTable"))
            {
                long[] skills = RowsOrIds(npc["talentSkillGroupList"]).Where(groups.Contains).Distinct().Order().ToArray();
                if (skills.Length == 0)
                    continue;
                long map = N(npc["mapId"]);
                Require(maps.TryGetValue(map, out var definition) && N(npc["packId"]) == id && N(definition["packId"]) == id && seen.Add(N(npc["id"])), "Invalid NPC/map identity");
                targets.Add(O(("npc", npc["id"]), ("map", map), ("name", Name(npc["npcNameTextId"])), ("map_name", Name(definition!["mapNameTextId"])), ("groups", skills)));
            }
            bool hidden = Flag(pack["packHide"]);
            result.Add(O(("id", id), ("name", Name(pack["packNameTextId"])), ("type", pack["packType"]), ("hidden", hidden), ("status", hidden ? "hidden" : targets.Count > 0 ? "targets" : "empty"), ("maps", Rows(targets).Select(t => N(t["map"])).Distinct().Order().ToArray()), ("targets", targets), ("database", source["databaseFile"]), ("sha256", source["databaseSha256"])));
        }
        Require(Rows(result).Select(r => N(r["id"])).Distinct().Count() == packs.Length, "Duplicate cartridge identity");
        return O(("schema", 1), ("source", "PackTable + FieldNpcTable + TalentSkillTable + MapTable"), ("assembly_sha256", manifest["assemblySha256"]), ("common_sha256", manifest["databaseSha256"]), ("exported_at", manifest["exportedAtUtc"]), ("weekly_groups", groups.Order().ToArray()), ("packs", result));
    }
    private static IEnumerable<long> RowsOrIds(JsonNode? value) => value is JsonArray rows ? rows.Select(N) : throw new InvalidDataException("NPC talent list missing");
}

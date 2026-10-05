using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class StealCatalogCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);
            cases.Add("steal catalog: " + name);
        }
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try
            {
                action();
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or FileNotFoundException) { rejected = true; }
            Check(rejected, name);
        }
        string folder = Path.Combine(root, "steal-catalog");
        var packs = new[]
        {
            O(("id", 2), ("packNameTextId", 1), ("packType", 0), ("packHide", true)),
            O(("id", 1), ("packNameTextId", 1), ("packType", 0), ("packHide", false)),
            O(("id", 3), ("packNameTextId", 1), ("packType", 0), ("packHide", false))
        };
        var names = new[] { O(("id", 1), ("textCn", "卡带")), O(("id", 2), ("textCn", "目标")), O(("id", 3), ("textCn", "室内")) };
        var maps = new[] { O(("id", 11), ("packId", 1), ("mapNameTextId", 3)), O(("id", 22), ("packId", 2), ("mapNameTextId", 3)) };
        var talents = new[] { O(("id", 1), ("groupId", 8), ("classType", 1), ("resetType", 2)), O(("id", 2), ("groupId", 9), ("classType", 1), ("resetType", 1)) };
        JsonObject Npc(int id, int pack, int map, params int[] skills) => O(("id", id), ("packId", pack), ("mapId", map), ("npcNameTextId", 2), ("talentSkillGroupList", skills));
        void WriteTables(string part, params (string Name, JsonObject[] Rows)[] tables)
        {
            string directory = Path.Combine(folder, part);
            Directory.CreateDirectory(directory);
            var counts = new JsonObject();
            foreach (var table in tables)
            {
                counts[table.Name] = table.Rows.Length;
                File.WriteAllText(Path.Combine(directory, table.Name + ".json"), Array(table.Rows).ToJsonString());
            }
            File.WriteAllText(Path.Combine(directory, "manifest.json"), O(("assemblySha256", new string('a', 64)), ("databaseSha256", new string('b', 64)), ("databaseFile", part + ".bytes"), ("exportedAtUtc", "2026-10-02T00:00:00Z"), ("tables", counts)).ToJsonString());
        }
        void Seed()
        {
            WriteTables("common", ("PackTable", packs), ("MapTable", maps), ("NameTextTable", names), ("TalentSkillTable", talents));
            WriteTables("1", ("FieldNpcTable", new[] { Npc(10, 1, 11, 8, 8, 9), Npc(11, 1, 11, 9) }));
            WriteTables("2", ("FieldNpcTable", new[] { Npc(20, 2, 22, 8) }));
            WriteTables("3", ("FieldNpcTable", System.Array.Empty<JsonObject>()));
        }
        Seed();
        var catalog = DailyStealCatalog.Build(folder);
        var rows = Rows(catalog["packs"]);
        Check(rows.Select(p => N(p["id"])).SequenceEqual(new long[] { 1, 2, 3 }), "cartridges have deterministic order");
        Check(S(rows[0]["status"]) == "targets" && Rows(rows[0]["targets"]).Length == 1 && rows[0]["targets"]![0]!["groups"]!.ToJsonString() == "[8]", "only weekly stealing talents are selected and duplicates are removed");
        Check(B(rows[1]["hidden"]) && S(rows[1]["status"]) == "hidden" && Rows(rows[1]["targets"]).Length == 1, "hidden cartridge targets remain distinguishable from accessible targets");
        Check(S(rows[2]["status"]) == "empty" && S(rows[0]["targets"]![0]!["map_name"]) == "室内", "verified empty maps and localized names survive");
        WriteTables("1", ("FieldNpcTable", new[] { Npc(10, 1, 22, 8) }));
        Reject(() => DailyStealCatalog.Build(folder), "foreign cartridge map cannot create a route");
        Seed();
        WriteTables("1", ("FieldNpcTable", new[] { Npc(10, 1, 11, 8), Npc(10, 1, 11, 8) }));
        Reject(() => DailyStealCatalog.Build(folder), "duplicate NPC identity is refused");
        Seed();
        File.WriteAllText(Path.Combine(folder, "1", "FieldNpcTable.json"), "[]");
        Reject(() => DailyStealCatalog.Build(folder), "incomplete export never becomes an empty route");
        Seed();
        var manifestFile = Path.Combine(folder, "1", "manifest.json");
        var manifest = DailyTradeCatalog.Read(manifestFile);
        manifest["assemblySha256"] = new string('c', 64);
        File.WriteAllText(manifestFile, manifest.ToJsonString());
        Reject(() => DailyStealCatalog.Build(folder), "mixed client exports are refused");
        Seed();
        File.Delete(Path.Combine(folder, "3", "FieldNpcTable.json"));
        Reject(() => DailyStealCatalog.Build(folder), "an omitted cartridge cannot be assumed empty");
    }
}

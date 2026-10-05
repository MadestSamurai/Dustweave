using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class RuleDataCases
{
    public static async Task Run(string output, List<string> cases)
    {
        var root = Path.Combine(output, "rule-preparation");
        var package = Path.Combine(root, "package");
        var gameRoot = Path.Combine(root, "game");
        var dataRoot = Path.Combine(root, "game-data");
        var assembly = Path.Combine(gameRoot, "BrownDust II_Data", "Managed", "Assembly-CSharp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        Directory.CreateDirectory(dataRoot);
        File.Copy(typeof(RuleDataCases).Assembly.Location, assembly);
        var database = Path.Combine(dataRoot, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("common_v1"))));
        File.WriteAllText(database, "offline-test-database");
        string a = DailyTradeCatalog.Hash(assembly), d = DailyTradeCatalog.Hash(database), mvid = DailyTradeCatalog.Mvid(assembly);
        var groups = new Dictionary<string, string[]> { ["rules"] = ["OneTable", "TwoTable"] };
        var game = new GameInstance(123, 456, Path.Combine(gameRoot, "BrownDust II.exe"));
        JsonObject context = O(("actor", new JsonArray(123, 456, "bridge", new string('a', 64))), ("cycle", "1"));
        int reads = 0;
        bool changeIdentity = false;
        Task<DailyStageFrame> Read()
        {
            var current = context.DeepClone().AsObject();
            if (++reads >= 2 && changeIdentity)
                current["cycle"] = "2";
            return Task.FromResult(new DailyStageFrame(O(("ProcessId", 123), ("ProcessStartTicks", 456)), current, new DailySnapshot { Guild = new() { ClientMvid = mvid } }));
        }
        void WriteTables(string directory)
        {
            DailyJson.Write(Path.Combine(directory, "manifest.json"), O(("assemblySha256", a), ("databaseSha256", d), ("tables", O(("OneTable", 2), ("TwoTable", 1)))));
            DailyJson.Write(Path.Combine(directory, "OneTable.json"), new JsonArray(O(("groupId", 1), ("id", 1)), O(("groupId", 2), ("id", 1))));
            DailyJson.Write(Path.Combine(directory, "TwoTable.json"), new JsonArray(O(("id", 1))));
        }
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            cases.Add(label);
        }
        async Task Reject(Func<Task> action, string label)
        {
            bool rejected = false;
            try
            {
                await action();
            }
            catch (Exception e) when (e is InvalidDataException or StageHostException) { rejected = true; }
            Check(rejected, label);
        }
        string? prior = Environment.GetEnvironmentVariable("BD2_DATA_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BD2_DATA_ROOT", dataRoot);
            string source = Path.Combine(package, "data", "sample");
            WriteTables(Path.Combine(source, "rules"));
            Task<DailyRuleData> Prepare(string folder, bool stop = false) => DailyRuleData.PrepareSetAsync(package, root, game, Read, () => stop, _ => throw new Exception("Unexpected exporter invocation"), "sample", folder, groups);
            var current = await Prepare(source);
            Check(S(current.Proof["state"]) == "packaged_current" && current.PathFor("rules/OneTable.json").Contains("package"), "rules prepare complete current-client package without exporter or game input");
            Check(current.Proof["hashes"]!["rules"]!.AsObject().Count == 2, "packaged rules preserve compound row IDs and complete hashes");
            await Reject(() => Prepare(source, true), "stopped rule preparation cannot read or start exporters");
            Check(reads == 2, "stop is checked before initial observation");
            changeIdentity = true;
            reads = 0;
            await Reject(() => Prepare(source), "account or cycle change during rule preparation prevents publication");
            changeIdentity = false;
            reads = 0;
            string cache = Path.Combine(root, "rules", "sample", a + "-" + d), tables = Path.Combine(cache, "export-fixture");
            WriteTables(tables);
            var hashes = DailyRuleData.ValidateExport(tables, a, d, groups.Values.SelectMany(x => x));
            DailyJson.Write(Path.Combine(cache, "verified.json"), O(("directory", tables), ("hashes", hashes)));
            Directory.CreateDirectory(Path.Combine(package, "trade-data"));
            File.WriteAllText(Path.Combine(package, "trade-data", "BD2TableExporter.exe"), "must-not-run");
            var cached = await Prepare(Path.Combine(package, "not-installed"));
            Check(S(cached.Proof["state"]) == "cached_current" && cached.PathFor("rules/TwoTable.json") == Path.Combine(tables, "TwoTable.json"), "same-client verified cache reuses managed tables without export");
            string gacha = Path.Combine(package, "data", "daily", "gacha");
            DailyJson.Write(Path.Combine(gacha, "manifest.json"), O(("assemblySha256", a), ("databaseSha256", d), ("tables", O(("GachaTable", 1), ("GachaGroupTable", 1)))));
            DailyJson.Write(Path.Combine(gacha, "GachaTable.json"), new JsonArray(O(("id", 50), ("freeCountDay", 1), ("gachaCount", 1))));
            DailyJson.Write(Path.Combine(gacha, "GachaGroupTable.json"), new JsonArray(O(("id", 5), ("oneTimeGachaId", 50))));
            var draw = await DailyFreeDrawData.PrepareAsync(package, root, game, Read, () => false, _ => throw new Exception("Unexpected draw exporter"));
            Check(draw.Rules.Require(50).Group == 5 && S(draw.Proof["state"]) == "packaged_current", "free draw consumes current packaged tables through the shared rule loader without repeated exports");
            File.WriteAllText(database, "changed-client-database");
            await Reject(() => { draw.AssertReady(); return Task.CompletedTask; }, "free draw rechecks shared provenance before consuming confirmation");
            await Reject(() => { cached.AssertReady(); return Task.CompletedTask; }, "database changes invalidate already prepared rules before gameplay");
            File.WriteAllText(database, "offline-test-database");
            cached.AssertReady();
            File.SetLastWriteTimeUtc(assembly, DateTime.UtcNow.AddSeconds(3));
            current.AssertReady();
            Check(true, "unchanged content accepts timestamp refresh after rehash");
            var rows = JsonNode.Parse(File.ReadAllText(Path.Combine(tables, "OneTable.json")))!.AsArray();
            rows.RemoveAt(1);
            DailyJson.Write(Path.Combine(tables, "OneTable.json"), rows);
            await Reject(() => { DailyRuleData.ValidateExport(tables, a, d, groups.Values.SelectMany(x => x)); return Task.CompletedTask; }, "truncated cached rules cannot satisfy the advertised complete table count");
            await Reject(() => DailyRuleData.PrepareSetAsync(package, root, game, Read, () => false, _ => { }, "../escape", source, groups), "rule cache namespaces cannot escape the account data root");
        }
        finally { Environment.SetEnvironmentVariable("BD2_DATA_ROOT", prior); }
    }
}

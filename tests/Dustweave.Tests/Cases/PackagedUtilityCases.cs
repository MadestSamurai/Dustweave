using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class PackagedUtilityCases
{
    public static async Task Run(string output, List<string> cases)
    {
        string package = Path.Combine(output, "tool-layout");
        Directory.CreateDirectory(package);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name)
        {
            bool failed = false;
            try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or FileNotFoundException) { failed = true; }
            Check(failed, name);
        }
        Check(!DailyTools.Available(package, "live"), "absent utility is detected before process start");
        Reject(() => DailyTools.StartInfo(package, "live"), "missing development helper cannot fall through to PATH");
        File.WriteAllText(Path.Combine(package, "Dustweave.exe"), "offline-not-executable");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol)));
        foreach (string name in new[] { "live", "minigame", "exporter", "diagnostics" })
        {
            var start = DailyTools.StartInfo(package, name);
            start.ArgumentList.Add("argument with spaces 中文");
            Check(DailyTools.Available(package, name) && start.FileName == Path.Combine(package, "Dustweave.exe") && start.WorkingDirectory == package && start.ArgumentList.SequenceEqual(new[] { "--utility", name, "argument with spaces 中文" }) && start.CreateNoWindow && !start.UseShellExecute && start.RedirectStandardError && start.RedirectStandardOutput,
                "unified utility keeps exact arguments and uses isolated hidden process: " + name);
        }
        Check(DailyTools.PackageDirectory(Path.Combine(package, "connection")) == package && DailyTools.EvidenceSpec(package, "test.json") == Path.Combine(package, "connection/specs", "test.json"), "nested connection utility resolves packaged evidence without developer paths");
        Reject(() => DailyTools.StartInfo(package, "unknown"), "unknown utility never launches the desktop UI");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol + 1)));
        Reject(() => DailyTools.Available(package, "live"), "incompatible unified layout fails before connecting");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol)));
        File.Delete(Path.Combine(package, "Dustweave.exe"));
        Reject(() => DailyTools.Available(package, "live"), "incomplete unified distribution cannot report available");
        await Resources(output, cases);
    }

    // Exercise the public readers, not only the layout resolver. A host at the
    // install root must never read stale files left beside it by an old package.
    private static async Task Resources(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        string previous = Environment.GetEnvironmentVariable("BD2_DATA_ROOT") ?? "";
        string root = Path.Combine(output, "resource-readers"), gameRoot = Path.Combine(root, "game");
        string assembly = Path.Combine(gameRoot, "BrownDust II_Data", "Managed", "Assembly-CSharp.dll"), data = Path.Combine(root, "database");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        Directory.CreateDirectory(data);
        File.Copy(typeof(PackagedUtilityCases).Assembly.Location, assembly);
        string database = Path.Combine(data, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("common_v1"))));
        File.WriteAllText(database, "synthetic package resource database");
        string dllHash = DailyTradeCatalog.Hash(assembly), dbHash = DailyTradeCatalog.Hash(database), mvid = DailyTradeCatalog.Mvid(assembly);
        var game = new GameInstance(123, 456, Path.Combine(gameRoot, "BrownDust II.exe"));
        var context = O(("actor", new JsonArray(123, 456L, "fixture", new string('a', 64))), ("cycle", "1"));
        Task<DailyStageFrame> Read() => Task.FromResult(new DailyStageFrame(O(("ProcessId", 123), ("ProcessStartTicks", 456L)), context.DeepClone().AsObject(), new DailySnapshot { Guild = new() { ClientMvid = mvid } }));
        try
        {
            Environment.SetEnvironmentVariable("BD2_DATA_ROOT", data);
            foreach (bool nested in new[] { false, true })
            {
                string install = Path.Combine(root, nested ? "nested 中文" : "flat"), resources = nested ? Path.Combine(install, "app") : install;
                Directory.CreateDirectory(resources);
                File.WriteAllText(Path.Combine(install, "Dustweave.exe"), "offline-not-executable");
                DailyJson.Write(Path.Combine(resources, DailyTools.Marker), O(("protocol", DailyTools.Protocol)));
                DailyJson.Write(Path.Combine(resources, "flows", "fixture.json"), O(("loaded", "current")));
                var groups = DailyRuleData.Groups.ToDictionary(p => p.Key, p => p.Value);
                groups["draw-report"] = ["CostumeTable", "CharTable", "EquipmentTable", "NameTextTable"];
                foreach (var group in groups)
                {
                    var counts = new JsonObject();
                    foreach (string name in group.Value)
                    {
                        var rows = name switch {
                            "GachaTable" => new JsonArray(O(("id", 50), ("freeCountDay", 1), ("gachaCount", 1))),
                            "GachaGroupTable" => new JsonArray(O(("id", 5), ("oneTimeGachaId", 50))),
                            _ => new JsonArray()
                        };
                        counts[name] = rows.Count;
                        DailyJson.Write(Path.Combine(resources, "data", "daily", group.Key, name + ".json"), rows);
                    }
                    DailyJson.Write(Path.Combine(resources, "data", "daily", group.Key, "manifest.json"), O(("assemblySha256", dllHash), ("databaseSha256", dbHash), ("tables", counts)));
                }
                DailyJson.Write(Path.Combine(resources, "data", "trade-catalog.json"), O(("assembly_sha256", dllHash), ("database_sha256", dbHash), ("client", mvid)));
                if (nested)
                {
                    DailyJson.Write(Path.Combine(install, "flows", "fixture.json"), O(("loaded", "stale")));
                    DailyJson.Write(Path.Combine(install, "data", "trade-catalog.json"), O(("client", "stale")));
                }
                foreach (string entry in new[] { install, resources, Path.Combine(resources, "connection") }.Distinct())
                {
                    string session = Path.Combine(root, Guid.NewGuid().ToString("N"));
                    using var f = new WorkflowHarness(session, [], assetDirectory: entry);
                    Check(S(f.Workflow.Asset("fixture.json")["loaded"]) == "current" && N(f.Workflow.Table("gacha/GachaTable.json")[0]["id"]) == 50,
                        "workflow reads current flows and tables from " + entry);
                    var rules = await DailyRuleData.PrepareAsync(entry, session, game, Read, () => false, _ => throw new Exception("Unexpected exporter"));
                    Check(S(rules.Proof["state"]) == "packaged_current" && rules.PathFor("policy/CharTable.json").StartsWith(resources, StringComparison.OrdinalIgnoreCase), "daily rules use shipped app data without regeneration: " + entry);
                    var draws = await DailyFreeDrawData.PrepareAsync(entry, session, game, Read, () => false, _ => throw new Exception("Unexpected exporter"));
                    Check(draws.Rules.Require(50).Group == 5 && S(draws.Proof["state"]) == "packaged_current", "free draw rules load from the same package layout: " + entry);
                    var trade = await DailyTradeData.PrepareAsync(entry, session, game, await Read(), () => false, _ => throw new Exception("Unexpected exporter"));
                    Check(trade.Error == "" && S(trade.Proof["state"]) == "current" && trade.Directory == resources, "trade preparation reads current catalog without falling back to an exporter: " + entry);
                    Check(f.Box.Commands.Count == 0, "resource preparation sends no game inputs: " + entry);
                }
            }
        }
        finally { Environment.SetEnvironmentVariable("BD2_DATA_ROOT", previous.Length == 0 ? null : previous); }
    }

    public static async Task Travel(string output, List<string> cases)
    {
        foreach (string mode in new[] { "delay", "never", "stop" })
        {
            using var f = new WorkflowHarness(Path.Combine(output, "travel-readiness-" + mode), []);
            f.Page("GameFieldDefaultUI");
            f.Frame["Surfaces"]![0]!["InputReady"] = false;
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3), (DailyTravel.CurrentPack + ".PackType", 1))];
            f.OnDelay = () =>
            {
                if (mode == "delay" && f.Time >= 1) f.Frame["Surfaces"]![0]!["InputReady"] = true;
                if (mode == "stop" && f.Time >= .4) f.Stopped = true;
            };
            string outcome = "";
            try { if (await DailyTravel.Reuse(f.Workflow, packId: 3, seconds: 3)) outcome = "ready"; }
            catch (StageHostException e) { outcome = e.Kind; }
            if (outcome != (mode == "delay" ? "ready" : mode == "never" ? "adapter" : "stopped") || f.Box.Commands.Count != 0 || mode == "delay" && f.Time < 1.3)
                throw new Exception("Cartridge readiness regression: " + mode + " => " + outcome);
            cases.Add("current cartridge waits for actual input readiness without re-entering: " + mode);
        }
    }
}

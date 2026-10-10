using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
static class PreferenceCases
{
    public static void Run(string output, List<string> cases)
    {
        string root = Path.Combine(output, "policy-" + Guid.NewGuid().ToString("N"));
        var store = new DailyPreferenceStore(root);
        string a = new('a', 64), b = new('b', 64);
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            cases.Add(name);
        }
        void Reject(Action act, string name)
        {
            try
            {
                act();
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or IOException) { cases.Add(name); return; }
            throw new Exception(name);
        }
        var p = store.Read(a);
        Check(!p.Weekly.WalkCollect, "weekly collection defaults to Absorb, not walking");
        foreach (bool walk in new[] { false, true })
        {
            var oldRange = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
            oldRange["Schema"] = 15;
            var weekly = oldRange["Weekly"]!.AsObject();
            weekly["FirstChapter"] = 8;
            weekly["LastChapter"] = 8;
            weekly["CharacterCartridges"] = 0;
            weekly["EventCartridges"] = false;
            weekly["WalkCollect"] = walk;
            weekly["Mainline"] = false;
            weekly["Steal"] = true;
            weekly["Npc"] = true;
            var migratedRoute = DailyPreferences.Parse(oldRange.ToJsonString());
            Check(migratedRoute.Schema == 16 && migratedRoute.Weekly.WalkCollect == walk &&
                !migratedRoute.Weekly.Mainline && migratedRoute.Weekly.Steal && migratedRoute.Weekly.Npc,
                "range migration preserves independent tasks and walking choice " + walk);
            store.Save(b, migratedRoute);
            var saved = JsonNode.Parse(File.ReadAllText(store.PathFor(b)))!["Weekly"]!.AsObject();
            Check(new[] { "FirstChapter", "LastChapter", "CharacterCartridges", "EventCartridges" }.All(key => !saved.ContainsKey(key)) && store.Read(b).Weekly.WalkCollect == walk,
                "retired route bounds do not survive save and reload " + walk);
        }
        using (var harness = new WorkflowHarness(Path.Combine(output, "fixed-weekly-route"), []))
        {
            var workflow = new DailyWorkflow(harness.Root, Path.Combine(TestPaths.SourceRoot, "assets"), harness.Context,
                harness.Driver, harness.Business, harness.Workflow.Navigation, [], () => false,
                (_, _, _) => throw new Exception("Catalog validation must not send game commands"));
            var routeCatalog = new DailyCollectionCatalog(workflow);
            var expected = Enumerable.Range(1, 19).Select(n => (long)n)
                .Concat(Enumerable.Range(1001, 7).Select(n => (long)n)).Concat(new long[] { 2001, 2002, 2005, 2007 }).ToArray();
            Check(routeCatalog.Packs().SequenceEqual(expected), "fixed weekly catalog covers all supported story, character and event packs");
            Check(routeCatalog.Packs().All(id => routeCatalog.Maps(id).Length > 0 && routeCatalog.StealMaps(id) != null), "shared weekly route has collection and theft coverage for every pack");
            Check(routeCatalog.Maps(6).SequenceEqual(new long[] { 601, 602, 606, 605, 608, 607 }), "fixed route retains chapter six floor exclusions");
            var owned = new long[] { 2007, 1007, 19, 1, 2003, 1008, 9999 };
            var evidence = WorkflowCases.Evidence(
                WorkflowCases.Reading("mainline.owned_packs", ("Count", owned.Length), ("$items", DailyData.Array(owned.Select(id => DailyData.O(("Key", id), ("Value.Id", id)))))),
                WorkflowCases.Reading("mainline.packs", ("Count", expected.Length + 3), ("$items", DailyData.Array(expected.Concat(new long[] { 2003, 1008, 9999 }).Select(id => DailyData.O(("Id", id), ("PackType", id >= 2000 ? 6 : id >= 1000 ? 1 : 0)))))));
            Check(routeCatalog.AvailablePacks(evidence).SequenceEqual(new long[] { 1, 19, 1007, 2007 }), "automatic route excludes unowned and noncollectible cartridges while retaining latest supported packs");
            evidence["Readings"]![0]!["Values"]![0]!["Json"] = "100";
            Reject(() => routeCatalog.AvailablePacks(evidence), "incomplete cartridge ownership cannot silently report collection complete");
        }
        var priorPuzzle = JsonSerializer.SerializeToNode(p)!.AsObject();
        priorPuzzle["Schema"] = 14;
        priorPuzzle["Events"]!.AsObject().Remove("Puzzle");
        priorPuzzle["Events"]!["Enabled"] = false;
        var migratedPuzzle = DailyPreferences.Parse(priorPuzzle.ToJsonString());
        Check(migratedPuzzle.Schema == 16 && migratedPuzzle.Events.Puzzle && !migratedPuzzle.Events.Enabled, "puzzle migration preserves disabled parent stage");
        migratedPuzzle.Events.Puzzle = false;
        Check(!DailyPreferences.Parse(JsonSerializer.Serialize(migratedPuzzle)).Events.Puzzle, "puzzle opt out survives preference roundtrip");
        Check(p.Hunt.OrdinaryChapter == 9 && p.Hunt.FarmGold && !p.Hunt.FarmSlime && p.Hunt.StoneElement == "least" && p.Hunt.TorchLimit == 60, "daily defaults match requested policy");
        Check(p.Mirror.Enabled && p.Mirror.Multiplier == 40 && p.Stages.FreeDraws, "mirror and stage defaults");
        p.Mirror.Enabled = false;
        p.Mirror.Multiplier = 15;
        p.Stages.Guild = false;
        p.Hunt.OrdinaryChapter = 3;
        store.Save(a, p);
        Check(store.Read(a).Hunt.OrdinaryChapter == 3 && store.Read(b).Hunt.OrdinaryChapter == 9, "daily policy isolated by account");
        Check(!store.Read(a).Mirror.Enabled && store.Read(a).Mirror.Multiplier == 15 && !store.Read(a).Stages.Guild, "disabled stage settings retained");
        var legacy = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        legacy.Remove("Friendship");
        legacy.Remove("Tactics");
        legacy.Remove("Events");
        legacy.Remove("EventBattle");
        legacy.Remove("Trade");
        legacy.Remove("Weekly");
        legacy.Remove("MonsterHunt");
        legacy.Remove("Mirror");
        legacy.Remove("Stages");
        legacy["Schema"] = 1;
        legacy["Equipment"]!["EnhanceLevel"] = 3;
        Check(DailyPreferences.Parse(legacy.ToJsonString()).Equipment.EnhanceLevel == 3, "legacy preference values migrated");
        legacy["Hunt"]!.AsObject().Remove("Enabled");
        Reject(() => DailyPreferences.Parse(legacy.ToJsonString()), "incomplete legacy preferences rejected");
        var v2 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        v2.Remove("Friendship");
        v2.Remove("Tactics");
        v2.Remove("Events");
        v2.Remove("EventBattle");
        v2.Remove("Trade");
        v2.Remove("Weekly");
        v2.Remove("MonsterHunt");
        v2["Schema"] = 2;
        v2["Stages"]!.AsObject().Remove("Mail");
        v2["Stages"]!["Guild"] = false;
        var migrated = DailyPreferences.Parse(v2.ToJsonString());
        Check(migrated.Schema == 16 && migrated.Stages.Mail && !migrated.Stages.Guild, "v2 mail migration preserves disabled daily stages");
        v2["Stages"]!.AsObject().Remove("FreeDraws");
        Reject(() => DailyPreferences.Parse(v2.ToJsonString()), "incomplete v2 cannot implicitly reenable draw");
        var v5 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        v5.Remove("Friendship");
        v5.Remove("Tactics");
        v5.Remove("Events");
        v5.Remove("EventBattle");
        v5["Schema"] = 5;
        v5["Mirror"]!["Multiplier"] = 15;
        var v6 = DailyPreferences.Parse(v5.ToJsonString());
        Check(v6.Schema == 16 && !v6.EventBattle.Enabled && v6.EventBattle.Challenge && v6.Mirror.Multiplier == 15, "event migration opt in and retains prior settings");
        v6.EventBattle.SearchSeconds = 0;
        Reject(v6.Validate, "event invalid search bound rejected");
        var old6 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        old6.Remove("Friendship");
        old6.Remove("Tactics");
        old6.Remove("Events");
        old6["Schema"] = 6;
        old6["Mirror"]!["Enabled"] = false;
        var now7 = DailyPreferences.Parse(old6.ToJsonString());
        Check(now7.Schema == 16 && now7.Events.Enabled && now7.Events.Missions && now7.Events.Roulette && now7.Events.Exchange && !now7.Mirror.Enabled, "event settings migration preserves disabled mirror");
        var incompleteEvents = JsonSerializer.SerializeToNode(now7)!.AsObject();
        incompleteEvents["Events"]!.AsObject().Remove("Exchange");
        Reject(() => DailyPreferences.Parse(incompleteEvents.ToJsonString()), "incomplete event spending preference rejected");
        now7.Events.Enabled = false;
        store.Save(b, now7);
        Check(!store.Read(b).Events.Enabled && store.Read(a).Events.Enabled, "event reward settings isolated by account");
        var old10 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        old10["Schema"] = 10;
        old10["Weekly"]!.AsObject().Remove("Book");
        old10["Weekly"]!.AsObject().Remove("EquipmentCraft");
        old10["Equipment"]!["Enabled"] = false;
        var new11 = DailyPreferences.Parse(old10.ToJsonString());
        Check(new11.Schema == 16 && new11.Weekly.Book && new11.Weekly.EquipmentCraft && !new11.Equipment.Enabled, "weekly migration preserves recycle choice");
        new11.Weekly.Book = false;
        store.Save(b, new11);
        Check(!store.Read(b).Weekly.Book && store.Read(b).Weekly.EquipmentCraft, "weekly task switches independent");
        var old12 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        old12["Schema"] = 12;
        old12["Weekly"]!.AsObject().Remove("Steal");
        old12["Weekly"]!["Mainline"] = false;
        var steal = DailyPreferences.Parse(old12.ToJsonString());
        Check(steal.Schema == 16 && !steal.Weekly.Steal && !steal.Weekly.Mainline, "steal migration preserves disabled collection");
        steal.Weekly.Steal = true;
        store.Save(b, steal);
        Check(store.Read(b).Weekly.Steal && !store.Read(a).Weekly.Steal, "steal switch isolated per account");
        var missing13 = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        missing13["Weekly"]!.AsObject().Remove("Steal");
        Reject(() => DailyPreferences.Parse(missing13.ToJsonString()), "missing current steal preference rejected");
        var written = JsonNode.Parse(File.ReadAllText(store.PathFor(a)))!;
        Check(written["Equipment"]!["EnhanceLevel"]!.GetValue<int>() == 7, "native preference JSON uses shared schema");
        DailyJson.Write(Path.Combine(output, "daily-defaults.json"), new DailyPreferences());
        Reject(() => store.PathFor("../escape"), "daily preference path traversal rejected");
        foreach (var change in new Action<DailyPreferences>[] { v => v.Mirror.Multiplier = 0, v => v.Mirror.Multiplier = 41, v => v.Hunt.OrdinaryChapter = 0, v => v.Hunt.OrdinaryChapter = 11, v => v.Equipment.EnhanceLevel = -1, v => v.Equipment.EnhanceLevel = 10, v => v.Hunt.TorchLimit = 61, v => v.Hunt.Priority = ["gold", "gold", "ordinary"], v => v.Hunt.StoneElement = "none", v => v.Equipment.RefineInstance = "9223372036854775808" })
        {
            var invalid = new DailyPreferences();
            change(invalid);
            Reject(invalid.Validate, "invalid preference range rejected " + cases.Count);
        }
        string before = File.ReadAllText(store.PathFor(a));
        var incomplete = JsonNode.Parse(before)!;
        incomplete["Equipment"]!.AsObject().Remove("KeepFiveStarSr");
        File.WriteAllText(store.PathFor(a), incomplete.ToJsonString());
        Reject(() => store.Read(a), "incomplete preferences never silently reenable actions");
        string broken = File.ReadAllText(store.PathFor(a));
        Reject(() => store.Save(a, new()), "corrupt policy cannot be overwritten by save");
        Check(File.ReadAllText(store.PathFor(a)) == broken, "corrupt policy bytes retained");
        File.WriteAllText(store.PathFor(a), before);
        string inventoryPath = Path.Combine(Path.GetDirectoryName(store.PathFor(a))!, "equipment-choices.json");
        var catalog = new RefinementChoices { Schema = 1, AccountKey = a, CapturedUtc = DateTimeOffset.UtcNow.ToString("O"), Complete = true, Items = [new("123", "UR 专武 +9")] };
        DailyJson.Write(inventoryPath, catalog);
        Check(RefinementChoices.Read(root, a)!.Items[0].Instance == "123", "refinement catalog reads exact instance");
        catalog.Items.Add(null!);
        DailyJson.Write(inventoryPath, catalog);
        Reject(() => RefinementChoices.Read(root, a), "null refinement catalog row rejected without null reference");
        catalog.Items = [new("123", "A"), new("123", "B")];
        DailyJson.Write(inventoryPath, catalog);
        Reject(() => RefinementChoices.Read(root, a), "duplicate refinement instance rejected");
        catalog.Items = [new("123", "A")];
        catalog.AccountKey = b;
        DailyJson.Write(inventoryPath, catalog);
        Reject(() => RefinementChoices.Read(root, a), "other account refinement catalog rejected");
        using (var held = new FileStream(store.PathFor(a), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Reject(() => store.Read(a), "locked daily policy never treated as defaults");
    }
}

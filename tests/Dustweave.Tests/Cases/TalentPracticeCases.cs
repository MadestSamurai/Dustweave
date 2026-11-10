using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class TalentPracticeCases
{
    sealed class Fixture : IDisposable
    {
        public WorkflowHarness H;
        public DailyBargainCharacter Character;
        public DailyTalentPractice Trainer;
        public long Potions = 200, Pack = 1;
        public int Casts, Switches;
        public bool Stop, MissingResponse, Reset = true, Locked, Unsafe;
        public int? Gain;
        public Action? AfterCast;
        public Fixture(string root, int kind = 12)
        {
            Character = new(10, 44, "Gray", 1, 5, kind * 100 + 5, 1, 0, 0, 3, 1, 5) { Kind = kind };
            H = new(root, [DailyTalentPracticeProof.Definition]);
            H.Frame["Scene"] = "Map0001_001"; H.Page("GameFieldDefaultUI", "_buttonPackList");
            Trainer = new(H.Workflow, () => Stop, _ => { }, _ => { });
            H.Readings = () => [
                WorkflowCases.Reading(DailyTalentPracticeProof.ReadId, ("$self", O(("Characters", new[] { Character }), ("Catalyst", Potions)))),
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", Pack), (DailyTravel.CurrentPack + ".PackType", 0)),
                WorkflowCases.Reading("mainline.map", (DailyFieldRoute.MapPath, O(("id", Pack == 1 ? 1 : 21), ("packId", Pack)))),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 10000)),
                WorkflowCases.Reading("mainline.travel", ("$self", O(("Pack", Pack), ("Map", Pack == 1 ? 1 : 21), ("Maps", new[] { O(("Id", Pack == 1 ? 1 : 21), ("MapType", Unsafe ? 1 : 0), ("Inside", false), ("Hunting", false), ("EnvironmentalHazard", false)) })))),
                WorkflowCases.Reading("route.quick", ("$self", O(("Targets", new JsonArray())))),
                WorkflowCases.Reading("mainline.owned_packs", ("$items", new[] { O(("Key", 1), ("Value.Id", 1)), O(("Key", Locked ? 9 : 2), ("Value.Id", Locked ? 9 : 2)) })),
                WorkflowCases.Reading("mainline.packs", ("$items", new[] { O(("Id", 1), ("PackType", 0)), O(("Id", 2), ("PackType", 0)) })),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", new[] {
                    O(("Kind", kind), ("Character", 11), ("Group", Character.Group), ("Instance", 21), ("Gate", ""), ("Reason", ""), ("PackRestricted", false)),
                    O(("Kind", kind), ("Character", Character.Character), ("Group", Character.Group), ("Instance", 20), ("Gate", Character.Active ? "talent_wait:duration_active" : ""), ("Reason", ""), ("PackRestricted", false)) }))
            ];
            H.OnCommand = command =>
            {
                H.Time += .01;
                switch (S(command["Kind"]))
                {
                    case "mainline_menu": H.Page("QuickMenuUI"); break;
                    case "mainline_pack": Pack = N(command["Value"]); Switches++; if (Reset) Character = Character with { Active = false }; H.Frame["Scene"] = "Map000" + Pack + "_001"; H.Page("GameFieldDefaultUI", "_buttonPackList"); break;
                    case "mainline_talent":
                        if (N(command["Value"]) != 20 || Character.Active) throw new Exception("Wrong character or active talent cast");
                        Casts++; H.Event("dispatch.start", "request");
                        int gain = Gain ?? Character.PerUse;
                        Character = Character with { Experience = Math.Min(Character.Target, Character.Experience + gain), Active = true }; Potions -= Character.Cost;
                        if (!MissingResponse) H.Event("dispatch.start", "response", ("TalentSkillInfo", O(("groupId", Character.Group))), ("AddTalentExp", gain), ("IsSuccess", true));
                        H.Page("GameFieldDefaultUI", "_buttonPackList"); AfterCast?.Invoke(); break;
                    default:
                        if (S(command["UiToken"]) == "GameFieldDefaultUI") H.Page("PackListUI");
                        else throw new Exception("Unexpected practice command: " + command);
                        break;
                }
            };
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        // Display language is metadata, not identity or an execution prerequisite.
        using (var f = new Fixture(Path.Combine(root, "practice-game-names"), 17))
        {
            var row = JsonSerializerNode(f.Character);
            row["Names"] = O(("Cn", "游戏简体名"), ("Tw", "遊戲繁體名"), ("En", "Client English name"));
            var parsed = DailyBargainCharacter.Parse(row);
            Check(parsed.NameIn("zh-CN") == "游戏简体名" && parsed.NameIn("zh-TW") == "遊戲繁體名" && parsed.NameIn("en-US") == "Client English name",
                "practice names select the game's corresponding language columns");
            parsed.AssertSameTier(f.Character);
            row.Remove("Names");
            Check(DailyBargainCharacter.Parse(row).NameIn("en-US") == "Gray", "missing display metadata preserves the game's readable name");
            row["Name"] = "";
            Check(DailyBargainCharacter.Parse(row).NameIn("en-US") == "#44", "missing names keep identity readable without blocking practice");
            var title = O(("Kind", 17), ("Names", O(("Cn", "游戏藏身"), ("Tw", "遊戲藏身"), ("En", "Client stealth"))));
            var reading = WorkflowCases.Reading(DailyTalentPracticeProof.ReadId, ("$self", O(("Talents", new[] { title }))));
            var evidence = O(("Readings", new[] { reading }));
            Check(DailyTalentPracticeProof.Talents(evidence).Single() is { Kind: 17, Names.En: "Client stealth" }, "talent titles read game metadata rather than translated class labels");
            Check(DailyTalentPracticeProof.Talents(await f.H.Workflow.Evidence(DailyTalentPracticeProof.ReadId)).Length == 0, "missing optional talent titles do not block reading XP");
        }
        foreach (int kind in new[] { 2, 6, 12, 13, 17 })
        using (var f = new Fixture(Path.Combine(root, "practice-kind-" + kind), kind))
        {
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade", "field practice fills tier: " + kind);
            Check(f.Casts == 3 && f.Switches == 2 && f.Potions == 185, "one cast per reset, no extra final switch: " + kind);
            Check(f.Character.Experience == 3 && f.H.Business.Records(f.H.Context).All(o => !DailyManagedBusiness.Pending(o)), "settled XP and journal: " + kind);
        }
        using (var f = new Fixture(Path.Combine(root, "practice-stop")))
        {
            f.AfterCast = () => f.Stop = true;
            Check(await f.Trainer.Run(f.Character) == "bargain.stopped" && f.Casts == 1 && f.Switches == 0, "stop settles cast without further travel");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-limited")))
            Check(await f.Trainer.Run(f.Character, 2) == "practice.test_complete" && f.Casts == 2 && f.Switches == 1, "live verification round limit");
        using (var f = new Fixture(Path.Combine(root, "practice-partial-final-gain"), 13))
        {
            f.Character = f.Character with { PerUse = 2 };
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 2 && f.Character.Experience == 3 && f.Potions == 190,
                "final partial XP consumes exactly one normal cast then stops");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-no-xp")) { Gain = 0 })
            Check(await f.Trainer.Run(f.Character) == "bargain.no_growth" && f.Casts == 1 && f.Switches == 0, "no XP stops before repeating travel");
        using (var f = new Fixture(Path.Combine(root, "practice-no-reset")) { Reset = false })
        {
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected reset failure"); }
            catch (DailyBargainFault e) { Check(e.Code == "practice.effect_not_reset" && f.Casts == 1 && f.Switches <= 2, "missing reset has bounded exit without repeat cast"); }
            f.Reset = true;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3, "reset recovery continues current XP");
        }
        foreach (string scenario in new[] { "locked", "unsafe", "short", "full" })
        using (var f = new Fixture(Path.Combine(root, "practice-" + scenario)))
        {
            f.Locked = scenario == "locked"; f.Unsafe = scenario == "unsafe";
            if (scenario == "short") f.Potions = 1;
            if (scenario == "full") f.Character = f.Character with { Experience = 3 };
            try { await f.Trainer.Run(f.Character); }
            catch (DailyBargainFault) { }
            Check(f.Casts == 0, "practice no waste: " + scenario);
        }
        foreach (string recovery in new[] { "same-session", "component-reloaded", "game-restarted" })
        using (var f = new Fixture(Path.Combine(root, "practice-lost-receipt-" + recovery)) { MissingResponse = true })
        {
            try { await f.Trainer.Run(f.Character); } catch (StageHostException) { }
            Check(f.Casts == 1, "lost receipt is not permission to repeat cast");
            f.MissingResponse = false;
            var pending = f.H.Business.Records(f.H.Context).Single(DailyManagedBusiness.Pending);
            if (recovery == "game-restarted") pending["before"]!["Frame"]!["ProcessStartTicks"] = 99;
            if (recovery == "component-reloaded") pending["before"]!["Frame"]!["Instance"] = "old-instance";
            f.H.Business.Save(pending);
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3, "current XP resumes without replaying lost request: " + recovery);
        }
    }
    static JsonObject JsonSerializerNode(DailyBargainCharacter c) => System.Text.Json.JsonSerializer.SerializeToNode(c)!.AsObject();
}

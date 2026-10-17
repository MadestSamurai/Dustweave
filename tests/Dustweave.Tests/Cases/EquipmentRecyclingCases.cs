using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class EquipmentRecyclingCases
{
    static JsonObject Gear(string id, int grade, string? protection = null) => O(
        ("instance", id), ("grade", grade), ("owner_grade", 0),
        ("locked", protection == "locked"), ("kept", protection == "kept"), ("equipped", protection == "equipped"));

    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks.Add(name); return; }
            throw new Exception(name);
        }
        foreach (int legacy in new[] { 0, 3, 7, 9 })
        {
            var original = new DailyPreferences(); original.Equipment.EnhanceLevel = legacy;
            var json = JsonSerializer.SerializeToNode(original)!.AsObject();
            Check(!json["Equipment"]!.AsObject().ContainsKey("EnhanceLevelUr"), "legacy optional rarity targets remain absent: " + legacy);
            var read = DailyPreferences.Parse(json.ToJsonString());
            Check(Enumerable.Range(1, 4).All(g => read.Equipment.EnhancementTarget(g) == legacy), "legacy level inherited by every rarity: " + legacy);
        }
        var store = new DailyPreferenceStore(Path.Combine(root, "equipment-rarity-profiles"));
        string a = new('a', 64), b = new('b', 64);
        var settings = store.Read(a);
        settings.Equipment.EnhanceLevelN = 0;
        settings.Equipment.EnhanceLevelR = 7;
        settings.Equipment.EnhanceLevelSr = 7;
        settings.Equipment.EnhanceLevelUr = 9;
        store.Save(a, settings);
        var saved = store.Read(a).Equipment;
        Check(Enumerable.Range(1, 4).Select(saved.EnhancementTarget).SequenceEqual(new[] { 0, 7, 7, 9 }), "rarity targets and explicit zero survive account save/reload");
        Check(Enumerable.Range(1, 4).All(g => store.Read(b).Equipment.EnhancementTarget(g) == 7), "another account keeps its existing defaults");
        foreach (string field in new[] { "EnhanceLevelN", "EnhanceLevelR", "EnhanceLevelSr", "EnhanceLevelUr" })
        foreach (int bad in new[] { -1, 10 })
        {
            var invalid = JsonSerializer.SerializeToNode(settings)!;
            invalid["Equipment"]![field] = bad;
            Reject(() => DailyPreferences.Parse(invalid.ToJsonString()), $"invalid {field} {bad} rejected");
        }
        Reject(() => saved.EnhancementTarget(5), "unknown rarity cannot inherit a spending target");

        var rows = new[] { Gear("1", 1), Gear("2", 2), Gear("3", 3), Gear("4", 4), Gear("5", 4),
            Gear("6", 4, "locked"), Gear("7", 2, "kept"), Gear("8", 3, "equipped"), Gear("9", 4), Gear("10", 2) };
        rows[8]["owner_grade"] = 5;
        string[] drawn = Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray();
        var plan = DailyEquipment.Plan(saved, rows, drawn, true, false, false);
        var actions = Rows(plan["actions"]);
        Check(actions.Length == 3 && actions.Select(r => I(r["enhance_level"])).SequenceEqual(new[] { 0, 7, 9 }), "different levels split into three native batches");
        Check(actions[1]["instances"]!.AsArray().Select(S).SequenceEqual(new[] { "2", "3" }), "R and SR at same level share one batch");
        Check(actions[2]["instances"]!.AsArray().Select(S).SequenceEqual(new[] { "4", "5" }), "both eligible UR items use level nine together");
        Check(actions.SelectMany(r => r["instances"]!.AsArray().Select(S)).Order().SequenceEqual(new[] { "1", "2", "3", "4", "5" }), "only exact eligible draw instances selected once; protected and unrelated gear excluded");
        Check(plan["retained"]!.AsArray().Select(S).ToHashSet().SetEquals(new[] { "6", "7", "8", "9" }), "all prior equipment protections retained");
        var same = DailyEquipment.Plan(new EquipmentPreferences(), rows, drawn, true, false, false);
        Check(Rows(same["actions"]).Length == 1 && I(same["actions"]![0]!["enhance_level"]) == 7, "legacy equal targets still use a single batch");
        foreach (var source in new string[]?[] { null, [] })
        {
            var fallback = Rows(DailyEquipment.Plan(saved, [], source, true, false, false)["actions"]);
            Check(fallback.Length == 1 && S(fallback[0]["task"]) == "equipment.craft_recycle" && I(fallback[0]["enhance_level"]) == 0,
                "N task craft uses N target for " + (source == null ? "unobserved draws" : "empty draws"));
        }
        var unknown = new[] { Gear("1", 5) };
        Reject(() => DailyEquipment.Plan(saved, unknown, ["1"], true, false, false), "unknown item rarity cannot create a recycle batch");
        using var fixture = new Fixture(Path.Combine(root, "equipment-rarity-execution"));
        foreach (var action in actions) await DailyEquipment.ExecuteAction(fixture.H.Workflow, action);
        Check(fixture.Submitted.Select(x => x.Level).SequenceEqual(new[] { 0, 7, 9 }), "production executor submits each planned native level instead of shared default");
        Check(fixture.Submitted.Select(x => string.Join(',', x.Ids)).SequenceEqual(new[] { "1", "2,3", "4,5" }), "production executor submits exact per-level batches");
        Check(fixture.Inventory.Keys.Order().SequenceEqual(new[] { "10", "6", "7", "8", "9" }), "native transaction proof preserves all unselected gear");
        var records = fixture.H.Business.Records(fixture.H.Context, "equipment.recycle").Concat(fixture.H.Business.Records(fixture.H.Context, "equipment.break")).ToArray();
        Check(records.Length == 3 && records.All(r => S(r["state"]) == "completed" && B(r["result"]?["inventory_matched"])), "each rarity batch has independently confirmed inventory evidence");
        int count = fixture.H.Box.Commands.Count;
        try { await DailyEquipment.ExecuteAction(fixture.H.Workflow, O(("task", "equipment.craft_recycle"))); throw new Exception("Missing target accepted"); }
        catch (InvalidDataException) { }
        Check(fixture.H.Box.Commands.Count == count, "invalid target fails before any crafting or game command");
    }

    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly Dictionary<string, JsonObject> Inventory = Enumerable.Range(1, 10).ToDictionary(i => i.ToString(), i => O(
            ("InvenIndex", i), ("LockFlag", i == 6), ("KeepFlag", i == 7), ("UseChar", i == 8 ? 1 : 0)));
        public readonly List<(int Level, string[] Ids)> Submitted = [];
        int level = 9;
        bool auto;
        string[] selected = [];
        public Fixture(string root)
        {
            H = new(root, DailyEquipment.Proofs().ToArray());
            H.Page("InventoryManageUI");
            H.Readings = () => [
                WorkflowCases.Reading("policy.equipment", ("Count", Inventory.Count), ("$items", Inventory.Values.ToArray())),
                WorkflowCases.Reading("equipment.upgrade_ui", ("ὬὠὬὡὫὤὮὨὦὤὥ", level), ("ὬὭὪὣὣὨὭὭὩὯὨ", auto), ("ὢὧὣὨὨὦὠὭὬὠὫ", selected)),
                WorkflowCases.Reading("equipment.break_ui", ("ὫὮὪὪὬὩὤὯὦὠὬ", selected))
            ];
            H.OnCommand = c =>
            {
                H.Time += .01;
                string kind = S(c["Kind"]), ui = S(c["UiToken"]);
                if (kind is "equipment_upgrade_preview" or "equipment_break_preview")
                {
                    selected = c["Items"]!.AsArray().Select(S).ToArray();
                    if (kind == "equipment_upgrade_preview") H.Page("EquipmentUpgradePopupUI", "_buttonUpgradePlus", "_buttonUpgradeMinus", "_autoBreakToggleButton", "_buttonAccept");
                    else H.Page("EquipmentBreakPopupUI", "_objBreakButton");
                }
                else if (kind == "back" && ui == "EquipmentBatchUpgradeResultPopupUI") H.Page("InventoryManageUI");
                else if (ui == "EquipmentUpgradePopupUI" && I(c["TargetId"]) == 10) level++;
                else if (ui == "EquipmentUpgradePopupUI" && I(c["TargetId"]) == 11) level--;
                else if (ui == "EquipmentUpgradePopupUI" && I(c["TargetId"]) == 12) auto = !auto;
                else if (ui == "EquipmentBreakPopupUI" || ui == "EquipmentUpgradePopupUI" && I(c["TargetId"]) == 13)
                {
                    bool enhance = ui == "EquipmentUpgradePopupUI";
                    string role = enhance ? "equipment.recycle" : "equipment.break";
                    H.Event(role, "request");
                    Submitted.Add((enhance ? level : 0, selected));
                    foreach (string id in selected) if (!Inventory.Remove(id)) throw new Exception("Item recycled twice");
                    H.Event(role, "response");
                    H.Page(enhance ? "EquipmentBatchUpgradeResultPopupUI" : "InventoryManageUI");
                }
                else throw new Exception("Unexpected equipment command: " + c.ToJsonString());
            };
        }
        public void Dispose() => H.Dispose();
    }
}

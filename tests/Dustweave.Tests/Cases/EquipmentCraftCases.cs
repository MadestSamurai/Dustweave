using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class EquipmentCraftCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        foreach (int usable in new[] { 1, 2, 3 })
        {
            using var f = new Fixture(Path.Combine(root, "craft-option-" + usable), usable);
            var result = await DailyEquipment.Run(f.H.Workflow, "weekly_equipment");
            Check(S(result["state"]) == "completed" && B(result["mission"]?["complete"]), "weekly mission completed using N recipe " + usable);
            Check(f.Previews.SequenceEqual(Enumerable.Range(1, usable)) && f.Submitted == 1, "disabled recipes skipped once; craft stops after one success: " + usable);
            Check(f.Inventory.Count == 2 && f.Inventory.ContainsKey("41") && S(result["instances"]?[0]) == "42", "only the newly crafted instance is selected: " + usable);
            Check(await f.H.Workflow.Has("MenuUI"), "successful fallback cleans up native crafting pages: " + usable);
            var record = f.H.Business.Records(f.H.Context, "equipment.craft").Single();
            Check(N(record["scope"]?["recipe"]) == usable && S(record["state"]) == "completed", "receipt bound to selected N recipe: " + usable);
        }
        using (var f = new Fixture(Path.Combine(root, "craft-all-unavailable"), 0))
        {
            var result = await DailyEquipment.Run(f.H.Workflow, "weekly_equipment");
            Check(S(result["state"]) == "partial" && S(result["reason"]) == DailyEquipment.CraftUnavailable, "exhausted recipes report an unfinished task with actionable reason");
            Check(f.Previews.SequenceEqual(new[] { 1, 2, 3 }) && f.Submitted == 0, "all N recipes tried once without material consumption");
            Check(!f.H.Business.Records(f.H.Context, "equipment.craft").Any(), "unavailable previews never create unresolved consumption journals");
            Check(await f.H.Workflow.Has("MenuUI") && !B(result["mission"]?["complete"]), "exhaustion returns home without falsely completing weekly mission");
            await f.H.Workflow.Step("MenuUI", "_buttonInventory", expect: "InventoryManageUI");
            Check(await f.H.Workflow.Has("InventoryManageUI"), "the next daily operation can start after all recipes are unavailable");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-recycle-unavailable"), 0))
        {
            var result = await DailyEquipment.ExecuteAction(f.H.Workflow, O(("task", "equipment.craft_recycle"), ("enhance_level", 7)));
            Check(S(result["state"]) == "partial" && f.Submitted == 0 && await f.H.Workflow.Has("MenuUI"), "shared craft/recycle fallback exits without recycling an empty or existing selection");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-already-complete"), 1) { Complete = true })
        {
            var result = await DailyEquipment.Run(f.H.Workflow, "weekly_equipment");
            Check(S(result["state"]) == "skipped" && f.Previews.Count == 0 && f.Submitted == 0, "server-complete weekly mission never crafts again");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-no-recipes"), 0) { Recipes = [] })
        {
            var result = await DailyEquipment.Craft(f.H.Workflow);
            Check(S(result["state"]) == "partial" && f.Previews.Count == 0 && await f.H.Workflow.Has("MenuUI"), "no available N recipes leaves a recoverable partial result");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-non-normal"), 1) { Recipes = [O(("Id", 4), ("Equipment", new[] { 104 }))] })
        {
            bool rejected = false;
            try { await DailyEquipment.Craft(f.H.Workflow); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && f.Previews.Count == 0 && f.Submitted == 0, "higher rarity output is rejected before preview or spending");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-potion-reserve"), 1))
        {
            f.H.Driver.PotionPolicy = () => new() { TalentPotionReserve = 100 };
            DailyStepException? failure = null;
            try { await DailyEquipment.Craft(f.H.Workflow); } catch (DailyStepException e) { failure = e; }
            Check(failure?.PotionLimit != null && !failure.Submitted && f.Submitted == 0, "alternate N recipe selection preserves the daily potion reserve");
        }
        using (var f = new Fixture(Path.Combine(root, "craft-uncertain"), 2) { SuppressResponse = true })
        {
            Exception? failure = null;
            try { await DailyEquipment.Craft(f.H.Workflow); } catch (Exception e) { failure = e; }
            Check(failure != null && f.Submitted == 1 && f.Previews.SequenceEqual(new[] { 1, 2 }), "lost craft receipt never falls through to another consuming recipe");
        }
        // The receipt must match the chosen recipe, while old journal scopes remain readable.
        var before = WorkflowCases.Evidence(WorkflowCases.Reading("policy.equipment", ("Count", 0), ("$items", new JsonArray())));
        var after = WorkflowCases.Evidence(WorkflowCases.Reading("policy.equipment", ("Count", 1), ("$items", new JsonArray(O(("InvenIndex", 42))))));
        var events = new JsonArray(WorkflowCases.Event("equipment.craft", "request", 1), WorkflowCases.Event("equipment.craft", "response", 2, ("EquipInfo", new[] { O(("invenIndex", 42), ("baseInfo", O(("id", 102)))) })));
        var op = O(("before", before), ("scope", O(("recipe_outputs", new[] { 101 }), ("equipment_definitions", new[] { O(("id", 102), ("grade", 1), ("slotType", 3)) }))));
        bool mismatch = false;
        try { DailyEquipment.VerifyCraft(op, events, after); } catch (InvalidDataException) { mismatch = true; }
        Check(mismatch, "another N recipe's receipt cannot confirm this craft");
        op["scope"]!["recipe_outputs"] = new JsonArray(102);
        Check(B(DailyEquipment.VerifyCraft(op, events, after)["inventory_matched"]), "non-sword N output accepted with exact recipe and inventory evidence");
        op["scope"]!.AsObject().Remove("recipe_outputs");
        Check(B(DailyEquipment.VerifyCraft(op, events, after)["inventory_matched"]), "older craft journal without output list remains verifiable");
    }

    sealed class Fixture : IDisposable
    {
        const string RecipePath = "ὤὭὠὥὤὠὡὣὣὠὣ.ὫὮὩὬὤὫὮὨὢὯὬ.Id";
        public readonly WorkflowHarness H;
        public readonly List<int> Previews = [];
        public readonly Dictionary<string, JsonObject> Inventory = new() { ["41"] = O(("InvenIndex", 41), ("LockFlag", true)) };
        public JsonObject[] Recipes = Enumerable.Range(1, 3).Select(id => O(("Id", id), ("Equipment", new[] { 100 + id }))).ToArray();
        public bool Complete, SuppressResponse;
        public int Submitted;
        int selected;
        public Fixture(string root, int usable)
        {
            string assets = Path.Combine(root, "assets");
            DailyJson.Write(Path.Combine(assets, "data/daily/policy/EquipmentTable.json"), Enumerable.Range(1, 4).Select(id => O(("id", 100 + id), ("grade", id == 4 ? 2 : 1), ("slotType", id - 1))).ToArray());
            DailyJson.Write(Path.Combine(assets, "data/daily/policy/MissionTable.json"), new[] { O(("id", 208), ("conditionType", 6), ("conditionSubType", 9), ("groupType", 1), ("conditionValue", 1)) });
            H = new(root, DailyEquipment.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: assets);
            H.Workflow.Settings.Weekly.EquipmentCraft = true;
            H.Workflow.Settings.Equipment.Enabled = false;
            Menu();
            H.Readings = () => [
                WorkflowCases.Reading("equipment.craft_recipes", ("$self", Recipes)),
                WorkflowCases.Reading("equipment.craft_ui", (RecipePath, selected), ("_craftMaterial.craftCount", 1), ("_craftButton.UseResourceCount", 1), ("_craftButton.objDisableButton.activeSelf", selected != usable)),
                WorkflowCases.Reading("policy.equipment", ("Count", Inventory.Count), ("$items", Inventory.Values.ToArray())),
                WorkflowCases.Reading("missions.cache", ("Count", 1), ("_items", new[] { O(("id", 208), ("value", Complete ? 1 : 0)) })),
                WorkflowCases.Reading("dispatch.currency", ("Catalyst", 100))
            ];
            H.OnCommand = c =>
            {
                H.Time += .01;
                string kind = S(c["Kind"]), ui = S(c["UiToken"]);
                if (kind == "reward_refresh")
                {
                    foreach (string role in new[] { "missions.query", "missions.event_query" }) { H.Event(role, "request"); H.Event(role, "response"); }
                    H.Time += .01;
                }
                else if (kind == "equipment_craft_menu") H.Page("EquipmentMakingSelectUI");
                else if (kind == "equipment_craft_preview")
                {
                    selected = I(c["Value"]); Previews.Add(selected);
                    H.Page("EquipmentMakingUI", "$pointer/UIRoot/Mask/EquipInfoParent/Button - Make");
                }
                else if (kind == "back" && ui == "EquipmentMakingUI") H.Page("EquipmentMakingSelectUI");
                else if (kind == "back" && ui == "EquipmentMakingSelectUI") Menu();
                else if (ui == "ItemGetPopupUI") H.Page("EquipmentMakingUI");
                else if (ui == "MenuUI") H.Page("InventoryManageUI");
                else if (ui == "EquipmentMakingUI" && I(c["TargetId"]) == 10)
                {
                    if (selected != usable || Submitted != 0) throw new Exception("Unavailable or duplicate craft submitted");
                    Submitted++;
                    H.Event("equipment.craft", "request");
                    if (SuppressResponse) return;
                    Inventory.Add("42", O(("InvenIndex", 42), ("LockFlag", false)));
                    Complete = true;
                    H.Event("equipment.craft", "response", ("EquipInfo", new[] { O(("invenIndex", 42), ("baseInfo", O(("id", 100 + selected)))) }));
                    H.Page("ItemGetPopupUI", "_objBackButton");
                }
                else throw new Exception("Unexpected craft command: " + c.ToJsonString());
            };
        }
        void Menu() => H.Page("MenuUI", "_buttonInventory");
        public void Dispose() => H.Dispose();
    }
}

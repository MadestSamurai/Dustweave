using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailyResults
{
    public static readonly HashSet<string> Results = ["EquipmentBatchUpgradeResultPopupUI", "EquipmentUpgradeResultPopupUI", "ItemGetPopupUI", "RewardReceivePopupUI"];
    public static async Task Cleanup(DailyWorkflow w, string destination = "MenuUI", string? expectedResult = null)
    {
        double end = w.Time + 45;
        bool seen = expectedResult == null;
        var allowed = new HashSet<string> { "EquipmentUpgradeUI", "EquipmentMakingUI", "EquipmentMakingSelectUI", "InventoryManageUI", "MailUI" };
        var policy = DailyNavigationPolicy.Load();
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            var types = DailyNavigationDecision.Types(frame);
            if (expectedResult != null && types.Contains(expectedResult))
                seen = true;
            if (await DailyTravel.RecoverPresentation(w, frame))
                continue;
            var results = Results.Append("NewsPopupEventUI").Concat(seen ? ["EquipmentUpgradePopupUI"] : System.Array.Empty<string>()).ToHashSet();
            var candidate = DailyNavigationDecision.Rows(frame).Where(r => results.Contains(S(r["Type"])) && S(r["Type"]) != destination && DailyNavigationDecision.Blockers(frame, S(r["Type"]), policy).Length == 0).OrderByDescending(r => N(r["Order"])).FirstOrDefault();
            if (candidate != null)
            {
                if (DailyNavigationDecision.ReadyInput(candidate))
                {
                    string ui = S(candidate["Type"]);
                    if (ui == "RewardReceivePopupUI")
                        await w.Dismiss(destination, true);
                    else
                        await w.Step(ui, ui == "ItemGetPopupUI" ? "_objBackButton" : ui == "EquipmentUpgradeResultPopupUI" ? "_objOKButton" : null, back: ui is not ("ItemGetPopupUI" or "EquipmentUpgradeResultPopupUI"), absent: ui, reason: "关闭已核账的结果展示");
                }
                else
                    await w.Delay(100);
                continue;
            }
            if (!seen)
            {
                await w.Delay(100);
                continue;
            }
            var blocked = DailyNavigationDecision.Blockers(frame, destination, policy);
            Require(!blocked.Except(results).Except(allowed).Any(), "Confirmed result blocked by unknown popup");
            if (types.Contains(destination) && blocked.Length == 0 && DailyNavigationDecision.Rows(frame).Any(r => S(r["Type"]) == destination && DailyNavigationDecision.ReadyInput(r)))
                return;
            if (destination == "MenuUI" && !types.Contains(destination) && types.Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(frame, "GameFieldDefaultUI", policy).Length == 0)
            {
                await w.Step("GameFieldDefaultUI", "_buttonMenu", expect: "MenuUI");
                continue;
            }
            var page = DailyNavigationDecision.Rows(frame).Where(r => allowed.Contains(S(r["Type"])) && S(r["Type"]) != destination && DailyNavigationDecision.ReadyInput(r) && DailyNavigationDecision.Blockers(frame, S(r["Type"]), policy).Length == 0).OrderByDescending(r => N(r["Order"])).FirstOrDefault();
            if (page != null)
                await w.Step(S(page["Type"]), back: true, absent: S(page["Type"]), reason: "结束已确认的操作页面");
            else
                await w.Delay(100);
        }
        throw new StageHostException("adapter", "结果展示尚未结束，不重复执行已确认操作。");
    }
}
public static class DailyEquipment
{
    private static readonly string[] Prefixes = ["equipment", "policy", "missions.cache"];
    private const string Level = "ὬὠὬὡὫὤὮὨὦὤὥ", Auto = "ὬὭὪὣὣὨὭὭὩὯὨ", Ids = "ὢὧὣὨὨὦὠὭὬὠὫ";
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("equipment.recycle", "equipment", Prefixes, (op, events, after) => VerifyRecycle(op, events, after), ["equipment.recycle", "equipment.recycle.single"], ["equipment", "equipment_recycle", "weekly_equipment"]);
        yield return new("equipment.break", "equipment", Prefixes, (op, events, after) => VerifyRecycle(op, events, after), AffectedStages: ["equipment", "equipment_recycle", "weekly_equipment"]);
        yield return new("equipment.refine", "equipment", Prefixes, (op, events, after) => VerifyRefine(op, events, after), AffectedStages: ["equipment", "equipment_refine"]);
        yield return new("equipment.craft", "weekly_equipment", Prefixes, (op, events, after) => VerifyCraft(op, events, after), AffectedStages: ["equipment", "equipment_recycle", "weekly_equipment"]);
    }
    public static Dictionary<string, JsonObject> Gear(JsonObject e)
    {
        var rows = Rows(R(e, "policy.equipment", "$items"));
        Require(rows.Length == N(R(e, "policy.equipment", "Count")) && rows.Select(r => S(r["InvenIndex"])).Distinct().Count() == rows.Length && rows.All(r => N(r["InvenIndex"]) > 0), "Equipment inventory incomplete or duplicated");
        return rows.ToDictionary(r => S(r["InvenIndex"]));
    }
    public static Dictionary<string, JsonObject> Recyclable(JsonObject e, string[] ids)
    {
        var gear = Gear(e);
        Require(ids.Length > 0 && ids.Distinct().Count() == ids.Length, "Empty or duplicate recycle selection");
        foreach (string id in ids)
            Require(gear.TryGetValue(id, out var r) && !Flag(r["LockFlag"]) && !Flag(r["KeepFlag"]) && N(r["UseChar"]) == 0, "Equipment protected/unavailable: " + id);
        return gear;
    }
    private static JsonObject[] Accepted(JsonObject op, JsonArray events, JsonObject after)
    {
        var roles = op["roles"]!.AsArray().Select(S).ToArray();
        var replies = roles.SelectMany(role => Responses(role, events, op["before"]!.AsObject(), after, 0)).ToArray();
        Require(replies.Length > 0, "No equipment response");
        return replies;
    }
    public static JsonObject VerifyRecycle(JsonObject op, JsonArray events, JsonObject after)
    {
        var ids = op["scope"]!["instances"]!.AsArray().Select(S).ToArray();
        var old = Recyclable(op["before"]!.AsObject(), ids);
        var current = Gear(after);
        var receipts = Accepted(op, events, after);
        Require(old.Keys.Except(current.Keys).ToHashSet().SetEquals(ids) && !current.Keys.Except(old.Keys).Any() && current.All(p => JsonNode.DeepEquals(p.Value, old[p.Key])), "Equipment removal differs from exact selection");
        return O(("instances", ids), ("responses", receipts.Length), ("inventory_matched", true));
    }
    public static JsonObject VerifyRefine(JsonObject op, JsonArray events, JsonObject after)
    {
        var response = Response("equipment.refine", events, op["before"]!.AsObject(), after)!;
        var info = response["EquipInfo"]!.AsObject();
        var old = Gear(op["before"]!.AsObject());
        var current = Gear(after);
        string id = S(op["scope"]!["instance"]);
        Require(S(info["invenIndex"]) == id && current.ContainsKey(id) && old.Keys.ToHashSet().SetEquals(current.Keys), "Refinement instance differs");
        Require(JsonNode.DeepEquals(current[id]["BaseInfo.Rank"], info["baseInfo"]?["rank"]) && old.Where(p => p.Key != id).All(p => JsonNode.DeepEquals(p.Value, current[p.Key])), "Refinement cache differs");
        return O(("instance", id), ("attempts", 1), ("inventory_matched", true));
    }
    public static JsonObject VerifyCraft(JsonObject op, JsonArray events, JsonObject after)
    {
        var response = Response("equipment.craft", events, op["before"]!.AsObject(), after)!;
        var infos = response["EquipInfo"] is JsonArray array ? Rows(array) : [response["EquipInfo"]!.AsObject()];
        Require(infos.Length == 1, "Expected exactly one crafted item");
        var info = infos[0];
        string id = S(info["invenIndex"]);
        var definition = Rows(op["scope"]!["equipment_definitions"]).Single(r => N(r["id"]) == N(info["baseInfo"]!["id"]));
        Require(N(definition["grade"]) == 1 && N(definition["slotType"]) == 0, "Crafted item is not an N sword");
        var old = Gear(op["before"]!.AsObject());
        var current = Gear(after);
        Require(current.Keys.Except(old.Keys).SequenceEqual([id]) && !old.Keys.Except(current.Keys).Any() && old.All(p => JsonNode.DeepEquals(p.Value, current[p.Key])), "Craft inventory delta differs");
        var result = O(("instances", new[] { id }), ("inventory_matched", true));
        if (N(op["scope"]?["mission"]) is long mission && mission > 0)
        {
            var tables = Rows(op["scope"]!["mission_definitions"]);
            var prior = DailyWeeklyMission.Progress(op["before"]!.AsObject(), mission, tables);
            var now = DailyWeeklyMission.Progress(after, mission, tables);
            Require(!B(prior["complete"]) && B(now["complete"]), "Weekly craft progress did not advance");
            result["mission"] = now;
        }
        return result;
    }
    public static async Task<string[]> Craft(DailyWorkflow w, long? mission = null)
    {
        await w.Home("weekly_equipment");
        await w.Step("MenuUI", operation: "equipment_craft_menu", expect: "EquipmentMakingSelectUI", reason: "打开原生装备制作");
        await w.Step("EquipmentMakingSelectUI", operation: "equipment_craft_preview", expect: "EquipmentMakingUI", reason: "预览任务需要的一把N剑");
        var e = await w.WaitEvidence(Prefixes, x => N(R(x, "equipment.craft_ui", "_craftMaterial.craftCount")) == 1);
        Require(N(R(e, "equipment.craft_ui", "ὤὭὠὥὤὠὡὣὣὠὣ.ὫὮὩὬὤὫὮὨὢὯὬ.Id")) == 1 && !B(R(e, "equipment.craft_ui", "_craftButton.objDisableButton.activeSelf")), "N sword recipe unavailable");
        var scope = O(("recipe", 1), ("count", 1), ("mission", mission), ("equipment_definitions", w.Table("policy/EquipmentTable.json").Where(r => N(r["grade"]) == 1 && N(r["slotType"]) == 0).ToArray()));
        if (mission.HasValue)
            scope["mission_definitions"] = Array(w.Table("policy/MissionTable.json").Where(r => N(r["id"]) == mission));
        var op = await w.Transact("equipment.craft", scope, O(("ui", "EquipmentMakingUI"), ("field", "$pointer/UIRoot/Mask/EquipInfoParent/Button - Make"), ("reason", "仅为未完成任务制作一把N剑")));
        await DailyResults.Cleanup(w, expectedResult: "ItemGetPopupUI");
        return op["result"]!["instances"]!.AsArray().Select(S).ToArray();
    }
    public static async Task<JsonObject> Recycle(DailyWorkflow w, string[] ids, int level, string source)
    {
        Recyclable(await w.Evidence(Prefixes), ids);
        Require(level is >= 0 and <= 9, "Invalid enhancement target");
        await w.Step(O(("ui", "InventoryManageUI"), ("operation", level > 0 ? "equipment_upgrade_preview" : "equipment_break_preview"), ("items", ids.Select(long.Parse).ToArray()), ("expect", level > 0 ? "EquipmentUpgradePopupUI" : "EquipmentBreakPopupUI"), ("reason", "仅预览本次明确选中的装备")));
        string role, ui, field;
        if (level > 0)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int current = I(R(await w.Evidence(Prefixes), "equipment.upgrade_ui", Level));
                if (current == level)
                    break;
                await w.Step("EquipmentUpgradePopupUI", current < level ? "_buttonUpgradePlus" : "_buttonUpgradeMinus");
                await w.WaitEvidence(Prefixes, e => N(R(e, "equipment.upgrade_ui", Level)) != current);
            }
            if (!B(R(await w.Evidence(Prefixes), "equipment.upgrade_ui", Auto)))
                await w.Step("EquipmentUpgradePopupUI", "_autoBreakToggleButton");
            var e = await w.WaitEvidence(Prefixes, x => B(R(x, "equipment.upgrade_ui", Auto)));
            Require(R(e, "equipment.upgrade_ui", Ids)!.AsArray().Select(S).ToHashSet().SetEquals(ids) && N(R(e, "equipment.upgrade_ui", Level)) == level, "Native recycle preview differs");
            role = "equipment.recycle";
            ui = "EquipmentUpgradePopupUI";
            field = "_buttonAccept";
        }
        else
        {
            var e = await w.Evidence(Prefixes);
            Require(R(e, "equipment.break_ui", "ὫὮὪὪὬὩὤὯὦὠὬ")!.AsArray().Select(S).ToHashSet().SetEquals(ids), "Native dismantle preview differs");
            role = "equipment.break";
            ui = "EquipmentBreakPopupUI";
            field = "_objBreakButton";
        }
        Recyclable(await w.Evidence(Prefixes), ids);
        var op = await w.Transact(role, O(("instances", ids), ("enhance_level", level), ("source", source)), O(("ui", ui), ("field", field), ("reason", "本次指定装备强化后分解")), 60);
        if (level == 0)
            await w.Dismiss("InventoryManageUI", true);
        await DailyResults.Cleanup(w, "InventoryManageUI", level > 0 ? "EquipmentBatchUpgradeResultPopupUI" : null);
        return O(("id", op["id"]), ("result", op["result"]));
    }
    private static async Task<JsonObject> Refine(DailyWorkflow w, string id)
    {
        await w.Step(O(("ui", "InventoryManageUI"), ("operation", "equipment_refine_preview"), ("items", new[] { long.Parse(id) }), ("expect", "EquipmentUpgradeUI"), ("reason", "预览任务需要的一次精炼")));
        var e = await w.WaitEvidence(Prefixes, x => S(R(x, "equipment.refine_ui", "ὯὢὪὬὠὧὥὠὤὩὦ")) == "SMELT");
        Require(S(R(e, "equipment.refine_ui", "ὢὠὡὡὫὤὮὫὢὢὪ.InvenIndex")) == id && !B(R(e, "equipment.refine_ui", "ὢὮὫὪὩὨὥὥὬὪὪ")) && B(R(e, "equipment.refine_ui", "ὠὠὡὭὧὫὫὭὥὣὯ")), "Single refinement not ready");
        var op = await w.Transact("equipment.refine", O(("instance", id), ("attempts", 1)), O(("ui", "EquipmentUpgradeUI"), ("field", "_objUpgradeButton"), ("reason", "仅为未完成任务精炼一次")));
        await w.WaitEvidence(Prefixes, x => !B(R(x, "equipment.refine_ui", "ὡὩὮὤὬὪὧὭὭὪὢ")));
        await DailyResults.Cleanup(w, "InventoryManageUI");
        return O(("id", op["id"]), ("result", op["result"]));
    }
    public static JsonObject[] Normalize(JsonObject e, JsonObject[] tables, JsonObject[] characters)
    {
        var definitions = tables.ToDictionary(r => N(r["id"]));
        var grades = characters.Where(r => N(r["type"]) == 0 && !Flag(r["usePackTemporary"])).GroupBy(r => N(r["uniqueCharId"])).ToDictionary(g => g.Key, g => g.Min(r => N(r["grade"])));
        return Gear(e).Select(p => { var r = p.Value; Require(definitions.TryGetValue(N(r["BaseInfo.Id"]), out var t), "Equipment definition missing"); long uid = N(t!["privateUniqueCharId"]); Require(uid == 0 || grades.ContainsKey(uid), "Exclusive character grade missing"); var rank = r["BaseInfo.Rank"] as JsonArray ?? throw new InvalidDataException("Missing equipment ranks"); Require((rank.Count == 3 || rank.Count == 0 && N(r["BaseInfo.Level"]) < 9) && rank.All(n => N(n) is >= 0 and <= 4), "Invalid refinement ranks"); return O(("instance", p.Key), ("equipment_id", r["BaseInfo.Id"]), ("family", new[] { uid, N(t["uniqueEquipId"]), N(t["grade"]) }), ("owner_grade", uid == 0 ? 0 : grades[uid]), ("grade", N(t["grade"])), ("exclusive", uid > 0), ("level", N(r["BaseInfo.Level"])), ("max_refine", rank.Count == 3 && rank.All(n => N(n) == 4)), ("locked", Flag(r["LockFlag"])), ("kept", Flag(r["KeepFlag"])), ("equipped", N(r["UseChar"]) != 0)); }).ToArray();
    }
    public static RefinementChoices PublishChoices(string root, string account, JsonObject evidence, JsonObject[] rows, JsonObject[] definitions, JsonObject[] names)
    {
        Require(S(evidence["Frame"]?["AccountKey"]) == account, "Equipment catalog account differs");
        var tables = definitions.ToDictionary(r => N(r["id"]));
        var texts = names.ToDictionary(r => N(r["id"]));
        string Label(JsonObject row)
        {
            var table = tables[N(row["equipment_id"])];
            texts.TryGetValue(N(table["itemNameTextId"]), out var text);
            string name = S(text?["textCn"]);
            if (name.Length == 0)
                name = S(row["equipment_id"]);
            string rarity = N(row["grade"]) switch
            {
                1 => "N",
                2 => "R",
                3 => "SR",
                4 => "UR",
                _ => S(row["grade"])
            };
            return $"{rarity} {name} +{N(row["level"])} · #{S(row["instance"])}";
        }
        var value = new RefinementChoices { Schema = 1, AccountKey = account, Complete = true, CapturedUtc = new DateTimeOffset(N(evidence["AtUtcTicks"]), TimeSpan.Zero).ToString("O"), Items = rows.Where(r => B(r["exclusive"]) && N(r["owner_grade"]) == 5 && N(r["level"]) == 9 && !B(r["max_refine"])).Select(r => new RefinementChoice(S(r["instance"]), Label(r))).ToList() };
        string path = Path.Combine(Path.GetDirectoryName(new DailyPreferenceStore(root).PathFor(account))!, "equipment-choices.json");
        DailyJson.Write(path, value);
        return value;
    }
    public static JsonObject Plan(EquipmentPreferences settings, JsonObject[] rows, string[]? drawn, bool breakNeeded, bool refineNeeded, bool refined)
    {
        var actions = new JsonArray();
        var pending = new JsonArray();
        var retained = new JsonArray();
        var byId = rows.ToDictionary(r => S(r["instance"]));
        if (settings.Enabled)
        {
            if (drawn == null)
            {
                // Unobserved draws never authorize selecting existing inventory. A new
                // cheap craft has its own receipt and can independently satisfy a mission.
                if (breakNeeded && settings.CraftFallback)
                    actions.Add(O(("task", "equipment.craft_recycle")));
                else if (breakNeeded)
                    pending.Add("free_draw_receipt_unobserved");
            }
            else
            {
                Require(drawn.Distinct().Count() == drawn.Length, "Duplicate source equipment");
                var selected = new List<string>();
                foreach (string id in drawn)
                {
                    if (!byId.TryGetValue(id, out var r))
                    {
                        pending.Add("drawn_equipment_missing:" + id);
                        continue;
                    }
                    bool five = N(r["owner_grade"]) == 5, protect = five && (N(r["grade"]) != 3 || settings.KeepFiveStarSr);
                    if (protect || B(r["locked"]) || B(r["kept"]) || B(r["equipped"]))
                        retained.Add(id);
                    else
                        selected.Add(id);
                }
                if (pending.Count == 0 && selected.Count > 0)
                    actions.Add(O(("task", "equipment.recycle"), ("instances", selected.ToArray()), ("enhance_level", settings.EnhanceLevel)));
                else if (pending.Count == 0 && breakNeeded && settings.CraftFallback)
                    actions.Add(O(("task", "equipment.craft_recycle")));
            }
        }
        if (settings.RefineEnabled && refineNeeded)
        {
            if (refined)
                pending.Add("refinement_daily_limit_reached");
            else
            {
                bool Usable(JsonObject r) => B(r["exclusive"]) && N(r["owner_grade"]) == 5 && !B(r["max_refine"]) && N(r["level"]) == 9;
                JsonObject? chosen = null;
                if (settings.RefineInstance.Length > 0)
                {
                    byId.TryGetValue(settings.RefineInstance, out chosen);
                    if (chosen == null || !Usable(chosen))
                    {
                        chosen = null;
                        pending.Add("selected_refine_equipment_unavailable");
                    }
                }
                else
                {
                    var full = rows.Where(r => B(r["max_refine"])).Select(r => r["family"]!.ToJsonString()).ToHashSet();
                    chosen = rows.Where(r => Usable(r) && !full.Contains(r["family"]!.ToJsonString())).OrderByDescending(r => N(r["grade"])).ThenBy(r => N(r["equipment_id"])).ThenBy(r => long.Parse(S(r["instance"]))).FirstOrDefault();
                    if (chosen == null)
                        pending.Add("no_useful_refinement_candidate");
                }
                if (chosen != null)
                    actions.Add(O(("task", "equipment.refine"), ("instance", chosen["instance"])));
            }
        }
        return O(("actions", actions), ("pending", pending), ("retained", retained));
    }
    private static string[]? Drawn(DailyWorkflow w)
    {
        var ops = w.Business.Records(w.Context).Where(r => JsonNode.DeepEquals(r["cycle"], w.Context["cycle"])).ToArray();
        bool found = false;
        var ids = new List<string>();
        foreach (var op in ops.Where(r => S(r["role"]) == "gacha.free_all" && S(r["state"]) != "rejected"))
        {
            Require(S(op["state"]) == "completed" && B(op["result"]?["free_cache_matched"]), "Unresolved free draw receipt");
            foreach (var reward in Rows(op["result"]!["results"]))
                if (reward["rewardInfoBundle"]?["equipInfo"] is JsonArray gear && gear.Count > 0)
                {
                    found = true;
                    ids.AddRange(Rows(gear).Select(r => S(r["invenIndex"])));
                }
        }
        foreach (var op in ops.Where(r => S(r["role"]) == "equipment.craft"))
        {
            Require(!DailyManagedBusiness.Pending(op), "Unresolved craft receipt");
            if (S(op["state"]) == "completed" && B(op["result"]?["inventory_matched"]))
            {
                found = true;
                ids.AddRange(op["result"]!["instances"]!.AsArray().Select(S));
            }
        }
        Require(ids.Distinct().Count() == ids.Count, "Duplicate equipment source receipt");
        var consumed = new HashSet<string>();
        foreach (var op in ops.Where(r => S(r["role"]) is "equipment.recycle" or "equipment.break"))
        {
            Require(!DailyManagedBusiness.Pending(op), "Unresolved recycle receipt");
            if (S(op["state"]) == "completed" && B(op["result"]?["inventory_matched"]))
                consumed.UnionWith(op["result"]!["instances"]!.AsArray().Select(S));
        }
        return found ? ids.Where(id => !consumed.Contains(id)).ToArray() : null;
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, string stage)
    {
        if (stage == "weekly_equipment")
        {
            if (!w.Settings.Weekly.EquipmentCraft)
                return DailyWorkflow.Skipped("disabled");
            await w.Refresh();
            var progress = await DailyWeeklyMission.Read(w, DailyWeeklyMission.Equipment);
            if (B(progress["complete"]))
                return DailyWeeklyMission.Skipped("本周制作装备任务已完成", progress);
            var ids = await Craft(w, N(progress["id"]));
            var mission = await DailyWeeklyMission.Confirm(w, DailyWeeklyMission.Equipment);
            var result = DailyWorkflow.Completed(mission);
            result["mission"] = mission.DeepClone();
            result["instances"] = Array(ids.Select(id => (JsonNode?)JsonValue.Create(id)));
            if (w.Settings.Equipment.Enabled)
            {
                await w.Step("MenuUI", "_buttonInventory", expect: "InventoryManageUI");
                result["recycle"] = await Recycle(w, ids, w.Settings.Equipment.EnhanceLevel, "weekly_craft");
                await DailyResults.Cleanup(w);
            }
            return result;
        }
        await w.Home("equipment");
        await w.Refresh();
        var reports = await DailyMissionInspection.Read(w);
        var settings = new EquipmentPreferences { Enabled = stage != "equipment_refine" && w.Settings.Equipment.Enabled, RefineEnabled = stage != "equipment_recycle" && w.Settings.Equipment.RefineEnabled, KeepFiveStarSr = w.Settings.Equipment.KeepFiveStarSr, EnhanceLevel = w.Settings.Equipment.EnhanceLevel, CraftFallback = w.Settings.Equipment.CraftFallback, RefineInstance = w.Settings.Equipment.RefineInstance };
        bool Needed(int type) => reports.Any(r => S(r["status"]) == "pending" && N(r["condition_type"]) == type && N(r["condition_subtype"]) == 0 && (r["params"] as JsonArray)?.Count == 0);
        var inventoryEvidence = await w.Evidence(Prefixes);
        var inventory = Normalize(inventoryEvidence, w.Table("policy/EquipmentTable.json"), w.Table("policy/CharTable.json"));
        PublishChoices(w.Root, S(w.Context["actor"]![3]), inventoryEvidence, inventory, w.Table("policy/EquipmentTable.json"), w.Table("names/NameTextTable.json"));
        w.Business.RequireResolved(w.Context, "equipment.refine");
        bool refined = w.Business.Records(w.Context, "equipment.refine").Any(r => JsonNode.DeepEquals(r["cycle"], w.Context["cycle"]) && S(r["state"]) == "completed");
        var plan = Plan(settings, inventory, Drawn(w), Needed(216), Needed(217), refined);
        var operations = new JsonArray();
        foreach (var action in Rows(plan["actions"]))
        {
            string task = S(action["task"]);
            string[]? ids = null;
            if (task == "equipment.craft_recycle")
                ids = await Craft(w);
            if (!await w.Has("InventoryManageUI"))
                await w.Step("MenuUI", "_buttonInventory", expect: "InventoryManageUI");
            operations.Add(task == "equipment.refine" ? await Refine(w, S(action["instance"])) : await Recycle(w, ids ?? action["instances"]!.AsArray().Select(S).ToArray(), settings.EnhanceLevel, task));
        }
        if (operations.Count > 0)
            await DailyResults.Cleanup(w);
        return O(("state", plan["pending"]!.AsArray().Count > 0 ? "partial" : operations.Count > 0 ? "completed" : "skipped"), ("operations", operations), ("plan", plan), ("engine", "dotnet-equipment-v1"));
    }
}
public static class DailyMissionInspection
{
    public static async Task<JsonObject[]> Read(DailyWorkflow w)
    {
        await w.Refresh();
        var reports = new List<JsonObject>();
        await w.Step("MenuUI", "_buttonMission", expect: "MissionUI");
        foreach (string group in new[] { "daily", "weekly" })
        {
            await w.Step("MissionUI", "_tabButton" + char.ToUpperInvariant(group[0]) + group[1..] + "Mission._objButton");
            var tables = w.Index("policy/MissionTable.json");
            var e = await w.WaitEvidence(["missions"], e => S(DailyMissions.Report(e, tables)["tab"]) == "MG_" + group.ToUpperInvariant());
            reports.AddRange(Rows(DailyMissions.Report(e, tables, w.Index("missions/LocalTextTable.json"))["missions"]));
        }
        await w.Step("MissionUI", "_objBackButton", expect: "MenuUI");
        var passes = await DailyPasses.Run(w, false);
        reports.AddRange(Rows(passes["reports"]).SelectMany(r => Rows(r["missions"])));
        return reports.ToArray();
    }
}

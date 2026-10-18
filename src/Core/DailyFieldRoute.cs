using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed partial class DailyFieldRoute
{
    public const string MapPath = "ὮὬὬὮὠὮὪὠὧὩὪ", SkillPath = "ὣὡὪὭὤὨὭὣὪὧὩ", ObjectPath = "ὪὬὣὧὥὨὦὯὨὮὬ", DestinationPath = "ὤὦὨὨὭὮὯὡὧὤὯ", WaypointNear = "ὩὤὨὮὥὦὫὭὫὭὨ";
    public static readonly HashSet<long> Excluded = [609, 610, 611, 612, 613, 619, 620, 621, 622, 623, 624, 625, 626, 627, 628, 630, 631];
    public static readonly Dictionary<int, string> Kinds = new() { { 2, "FastDash" }, { 17, "Stealth" }, { 3, "Overwhelm" }, { 4, "FieldRewardAbsorb" }, { 6, "FieldRewardResearch" }, { 15, "WaypointMake" }, { 20, "FieldMonsterSummon" } };
    public DailyWorkflow W
    {
        get;
    }
    public DailyCollectionCatalog Catalog
    {
        get;
    }
    public DailyCollectionProgress Progress
    {
        get;
    }
    public bool RecoverEncounters
    {
        get; set;
    }
    public bool NpcActing
    {
        get; set;
    }
    public Func<long, bool> CanSuppress { get; set; } = _ => true;
    private bool recovering; private readonly Dictionary<string, int> encounters = new();
    public DailyFieldRoute(DailyWorkflow workflow)
    {
        W = workflow;
        Catalog = new(workflow);
        Progress = new(workflow);
    }
    public Task<JsonObject> Evidence() => W.Evidence("mainline", "dispatch.talent", "weekly_npc");
    public static JsonObject Map(JsonObject e) => State(e, "mainline.map", MapPath);
    public static JsonObject Travel(JsonObject e) => State(e, "mainline.travel", "$self");
    public static JsonObject[] FieldRows(JsonObject e, string name) => Rows(e["Readings"]).Where(r => S(r["Id"]) == "mainline." + name && S(r["Error"]) == "").Select(r => { var v = DailyEvidence.Values(r); v["instance"] = Copy(r["InstanceId"]); return v; }).ToArray();
    public static JsonObject[] Counts(JsonObject e) => Rows(R(e, "mainline.talent_counts", "Values"));
    public static long Used(JsonObject e, int kind) => Counts(e).Where(r => N(r["groupId"]) / 100 == kind).Sum(r => N(r["useCount"]));
    public static JsonObject[] Talents(JsonObject e) => Rows(R(e, "mainline.talent_rows", "$self"));
    public void Quota(JsonObject e)
    {
        if (!Progress.OnlySteal && Used(e, 4) >= 21)
            throw new DailyQuotaExhausted("吸收次数已用完，次日接续");
    }
    public static double Remaining(JsonObject e, int kind)
    {
        foreach (var row in FieldRows(e, "durations"))
            foreach (var field in row.Where(p => p.Value is JsonArray))
                foreach (var x in Rows(field.Value))
                    if ((S(x["Key"]) == kind.ToString() || S(x["Key"]) == Kinds[kind]) && B(x["Value.ὫὢὢὬὫὫὡὩὪὩὮ"]))
                        return Math.Max(0, DailyRegionRouter.D(x["Value.ὠὨὠὭὬὤὪὢὫὨὮ"]));
        return 0;
    }
    public static bool DurationBusy(JsonObject e, int kind) => FieldRows(e, "durations").SelectMany(r => r.Where(p => p.Value is JsonArray).SelectMany(p => Rows(p.Value))).Any(x => (S(x["Key"]) == kind.ToString() || S(x["Key"]) == Kinds[kind]) && B(x["Value.ὫὢὢὬὫὫὡὩὪὩὮ"]));
    private static bool Cooling(JsonObject e, int kind)
    {
        var eligible = Talents(e).Where(r => N(r["Group"]) / 100 == kind && S(r["Reason"]) == "").ToArray();
        return eligible.Length > 0 && eligible.All(r => DailyRegionRouter.D(r["Cooldown"]) > 0);
    }
    public static bool RequiresStealth(JsonObject e) => N(Map(e)["packId"]) is 14 or 1003;
    // Nightmare Winter's native map type distinguishes patrol areas from the
    // safe hub/interiors. Do not infer danger from cartridge or map number alone.
    public static bool PrefersStealth(JsonObject e) => N(Map(e)["packId"]) == 2002 && N(Map(e)["mapType"]) == 1;
    public static bool NeedsStealth(JsonObject e) => RequiresStealth(e) || PrefersStealth(e) && Remaining(e, 17) > 0;
    private long unavailableStealthMap;
    internal static bool FieldInputReady(JsonObject frame) => DailyNavigationDecision.MapSceneReady(frame)
        && DailyNavigationDecision.Rows(frame).Any(r => S(r["Type"]) == "GameFieldDefaultUI" && DailyNavigationDecision.ReadyInput(r))
        && DailyNavigationDecision.Types(frame).All(t => t == "GameFieldDefaultUI" || DailyNavigationDecision.PassiveSurface(t));
    public async Task FieldReady(double timeout = 45, double settle = 1)
    {
        double end = W.Time + timeout;
        double? stable = null;
        string? storySeen = null;
        var policy = DailyNavigationPolicy.Load();
        while (W.Time < end)
        {
            var frame = (await W.Observe()).Frame;
            DailyGameVisibility.EnsureVisible(I(frame["ProcessId"]));
            var types = DailyNavigationDecision.Types(frame);
            var popup = DailyHomeDecision.BackgroundErrorAction(frame, policy) ?? DailyNavigationDecision.TalentAction(frame, policy) ?? DailyNavigationDecision.Notice(frame, policy, out _) ?? DailyNavigationDecision.WeeklyResult(frame, policy);
            if (popup != null)
            {
                await DailyTravel.RecoverPresentation(W, frame);
                stable = null;
                continue;
            }
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            {
                if (await Encounter(frame))
                {
                    stable = null;
                    continue;
                }
                throw new StageHostException("adapter", "地图途中进入战斗准备，保留现场；未擅自开始战斗。");
            }
            var story = DailyNavigationDecision.StoryAction(frame, policy);
            if (story != null)
            {
                string signature = S(frame["Scene"]) + "|" + S(frame["UiToken"]) + "|" + story.ToJsonString();
                if (signature != storySeen)
                {
                    await DailyTravel.RecoverPresentation(W, frame);
                    storySeen = signature;
                }
                stable = null;
                await W.Delay(200);
                continue;
            }
            storySeen = null;
            if (types.Contains("QuickMenuUI") && types.All(t => t is "QuickMenuUI" or "GameFieldDefaultUI" || DailyNavigationDecision.PassiveSurface(t)) && DailyNavigationDecision.Rows(frame).Any(r => S(r["Type"]) == "QuickMenuUI" && DailyNavigationDecision.ReadyInput(r)))
            {
                await W.Step("QuickMenuUI", back: true, absent: "QuickMenuUI");
                continue;
            }
            bool ready = FieldInputReady(frame);
            if (ready)
            {
                stable ??= W.Time;
                if (W.Time - stable >= settle)
                    return;
            }
            else
                stable = null;
            await W.Delay(200);
        }
        throw new StageHostException("adapter", "field.not-ready: 地图操作界面仍未恢复，已保留收集进度；请回到可移动的地图界面后接续任务。");
    }
    public async Task PrepareNpcMap(long map)
    {
        await FieldReady(settle: 0);
        var e = await Evidence();
        Require(N(Map(e)["id"]) == map, "周任务准备期间地图已改变");
        if (!B(R(e, "mainline.map", "HasCanOverwhelmMonster()")))
            return;
        bool suppress = CanSuppress(map);
        var result = await Skill(suppress ? 3 : 17, 5);
        if (suppress && S(result["reason"]) == "native_disabled")
            result = await Skill(17, 5);
        Require(S(result["reason"]) != "native_disabled", "当前地图无法压制或藏身，保留周任务，避免反复遇敌");
    }
    public async Task<bool> Encounter(JsonObject frame)
    {
        if (!RecoverEncounters || recovering || NpcActing)
            return false;
        var types = DailyNavigationDecision.Types(frame);
        if (!types.Contains("BattleUI_FieldBattle") || types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal) && t != "BattleUI_FieldBattle"))
            return false;
        string key = S(frame["Scene"]);
        int count = encounters.GetValueOrDefault(key);
        if (count >= 2)
            throw new StageHostException("adapter", "同一地图连续遇敌，保留进度，避免反复撤退。");
        encounters[key] = count + 1;
        recovering = true;
        try
        {
            await ExitEncounter(frame);
            await FieldReady(settle: 0);
            var e = await Evidence();
            bool allowed = !Progress.OnlySteal && CanSuppress(N(Map(e)["id"]));
            if (allowed && B(R(e, "mainline.map", "HasCanOverwhelmMonster()")))
            {
                var result = await Skill(3);
                if (S(result["reason"]) == "native_disabled")
                    result = await Skill(17, 5);
                Require(S(result["reason"]) != "native_disabled", "退出遇敌后压制和藏身均不可用");
            }
            else
            {
                var result = await Skill(17, 5);
                Require(S(result["reason"]) != "native_disabled", "任务目标不可压制且藏身不可用");
            }
            return true;
        }
        finally { recovering = false; }
    }
    // This retry policy belongs only to retreating from an already observed field
    // encounter. It must not make uncertain business/network commands replayable.
    internal async Task ExitEncounter(JsonObject original)
    {
        const string battleUi = "BattleUI_FieldBattle";
        const string skillPresentation = "SubSkillCutSceneUI";
        const string pauseButton = "$pointer/HUD/ButtonLayout/Button - Pause/Button - Pause";
        var origin = original.DeepClone().AsObject();
        var battle = DailyNavigationDecision.Rows(origin).Single(r => S(r["Type"]) == battleUi);
        string[] pages = [battleUi, "BattlePauseUI", "BattlePopupUI"];
        string[] labels = ["打开暂停菜单", "选择撤退", "确认退出战斗"];
        var policy = DailyNavigationPolicy.Load();
        int[] attempts = [0, 0, 0];
        string?[] targets = [null, null, null];
        double[] retryAt = [0, 0, 0];
        int furthest = -1;
        bool exitSubmitted = false;
        DailyStepException? uncertain = null;
        int uncertainStep = -1;
        double end = W.Time + 45;
        while (W.Time < end)
        {
            var current = (await W.Observe()).Frame;
            Require(DailyEvidence.SameActor(origin, current), "野外战斗账号或连接已经改变");
            var types = DailyNavigationDecision.Types(current);
            bool loading = types.Overlaps(DailyNavigationDecision.Transitions);
            Require(S(current["Scene"]) == S(origin["Scene"]) || exitSubmitted && loading &&
                (DailyNavigationDecision.SceneLoading(current) || S(current["Scene"]) == "Empty"), "野外战斗地图已经改变");
            Require(types.All(t => pages.Contains(t) || t is "GameFieldDefaultUI" or skillPresentation ||
                DailyNavigationDecision.PassiveSurface(t) || DailyNavigationDecision.Transitions.Contains(t)), "野外战斗或弹窗已经改变");
            if (!types.Contains(battleUi) && FieldInputReady(current))
                return;
            if (loading || !types.Contains(battleUi))
            {
                await W.Delay(200);
                continue;
            }
            var battles = DailyNavigationDecision.Rows(current).Where(r => S(r["Type"]) == battleUi).ToArray();
            Require(battles.Length == 1 && JsonNode.DeepEquals(battles[0]["Id"], battle["Id"]), "野外战斗已被另一场战斗替换");
            if (types.Contains(skillPresentation))
            {
                // A skill cutscene belongs to this same encounter. Wait for it
                // to release input without clicking behind it or restarting the
                // retreat budget. Unrelated dialogs are still rejected above.
                await W.Delay(200);
                continue;
            }
            int step = types.Contains(pages[2]) ? 2 : types.Contains(pages[1]) ? 1 : 0;
            // Native give-up closes its dialogs before the return-to-field
            // coroutine finishes. That is progress, not permission to reopen Pause.
            if (exitSubmitted && step < 2)
            {
                await W.Delay(200);
                continue;
            }
            Require(step >= furthest, "野外战斗退出页面已经改变");
            furthest = step;
            if (uncertain != null)
            {
                if (step > uncertainStep) uncertain = null;
                else
                {
                    if (W.Time >= retryAt[step]) throw uncertain;
                    await W.Delay(200);
                    continue;
                }
            }
            var surfaces = DailyNavigationDecision.Rows(current).Where(r => S(r["Type"]) == pages[step]).ToArray();
            Require(surfaces.Length == 1 && DailyNavigationDecision.Blockers(current, pages[step], policy).Length == 0, "野外战斗或弹窗已经改变");
            var surface = surfaces[0];
            string field = step == 0 ? pauseButton : step == 1 ? "_objectRun" : "_objGiveUpButton";
            var choices = Rows(surface["Targets"]).Where(t => S(t["Field"]) == field && (step != 0 || S(t["Route"]) == "pointer")).ToArray();
            Require(choices.Length <= 1, "野外战斗退出按钮不唯一");
            // Back remains a bounded compatibility fallback only when there is
            // no pause button; a disabled observed button is never bypassed.
            bool back = step == 0 && choices.Length == 0;
            var target = choices.SingleOrDefault();
            string signature = S(surface["Id"]) + "|" + (back ? "back" : S(target?["Id"]) + "|" + S(target?["Route"]));
            Require(targets[step] == null || targets[step] == signature, "野外战斗退出按钮已被替换");
            if (!DailyNavigationDecision.ReadyInput(surface) || !back && (target == null || !B(target["Enabled"])) || W.Time < retryAt[step])
            {
                await W.Delay(200);
                continue;
            }
            if (attempts[step] >= 3)
                throw new StageHostException("adapter", "野外遇敌退出未完成：" + labels[step] + "已尝试3次，收集进度已保留。");
            targets[step] = signature;
            attempts[step]++;
            var action = O(("ui", pages[step]), ("timeout", 5), ("reason", "野外遇敌：" + labels[step]));
            if (back) action["back"] = true;
            else { action["field"] = field; action["target_id"] = Copy(target!["Id"]); }
            W.Driver.Diagnostics.Event("field_retreat", O(("step", step), ("attempt", attempts[step]), ("scene", current["Scene"]), ("surface", surface["Id"])));
            try
            {
                // Own the exact observed page through final preflight and native
                // dispatch. A receipt confirms input delivery; the next frame
                // decides progress, including late/already completed transitions.
                await W.Driver.SubmitOwnedAsync(action, current);
                if (step == 2) exitSubmitted = true;
            }
            catch (DailyStepException error) when (error.DefinitelyNotDispatched &&
                (error.PresentationChanged || error.Kind == "rejected" && error.Message.StartsWith("Owned confirmation changed", StringComparison.Ordinal)))
            {
                W.Driver.Diagnostics.Event("field_retreat_reobserve", O(("step", step), ("reason", error.Message)));
            }
            catch (DailyStepException error) when (error.Kind == "pending" && error.MayHaveDispatched)
            {
                // Lost receipts may still lead to a visible next page. Observe
                // that progress, but never replay an input that may still be queued.
                uncertain = error;
                uncertainStep = step;
                if (step == 2) exitSubmitted = true;
                W.Driver.Diagnostics.Event("field_retreat_await_progress", O(("step", step), ("reason", error.Message)));
            }
            // Retreat confirmation can wait on a server response. Do not hammer
            // it while the game is processing; unknown receipts still propagate.
            retryAt[step] = W.Time + (uncertain != null ? 10 : step == 2 ? 5 : 1.5);
        }
        if (uncertain != null) throw uncertain;
        throw new StageHostException("adapter", "野外遇敌退出未完成，收集进度已保留；请检查当前战斗画面后接续任务。");
    }
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("dispatch.start", "weekly_mainline", ["mainline", "dispatch.talent"], (op, es, a) => VerifyTalent(op, es, a), AffectedStages: ["weekly_mainline", "weekly_npc", "weekly_steal"],
            ScopeStages: scope => N(scope["kind"]) == 1 ? ["weekly_steal"] : ["weekly_mainline", "weekly_npc", "weekly_steal"],
            CanResume: (op, _) => DailyFieldTalentProof.AbsorptionLocation(op) != null);
        yield return new("mainline.waypoint_use", "weekly_mainline", ["mainline"], (op, es, a) => Response("mainline.waypoint_use", es, op["before"]!.AsObject(), a)!, AffectedStages: ["weekly_mainline", "weekly_npc", "weekly_steal", "trade"]);
        yield return new("mainline.pickup", "weekly_mainline", ["mainline"], (op, es, a) => VerifyPickup(op, es, a), ["mainline.pickup", "mainline.pickup_list"],
            CanResume: (op, _) => DailyPickupRecovery.Location(op) != null);
    }
    public static JsonObject VerifyTalent(JsonObject op, JsonArray es, JsonObject after)
    {
        int kind = I(op["scope"]!["kind"]);
        if (kind == 1)
        {
            var scope = op["scope"]!;
            var before = op["before"]!.AsObject();
            var result = Response("dispatch.start", es, before, after)!;
            var info = result["TalentNpcInfo"]!;
            Require(N(info["npcId"]) == N(scope["npc"]) && N(info["groupId"]) == N(scope["group"]) && N(info["endTime"]) > N(scope["server_now"]), "偷窃回执与NPC、技能或冷却不一致");
            foreach (var e in new[] { before, after })
                Require(N(Map(e)["id"]) == N(scope["map"]) && S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks")) == S(scope["week"]), "偷窃地图或周周期改变");
            return O(("npc", scope["npc"]), ("group", scope["group"]), ("success", B(result["IsSuccess"])), ("end_time", info["endTime"]), ("response", result));
        }
        if (kind == 15)
            return Response("dispatch.start", es, op["before"]!.AsObject(), after)!;
        return DailyFieldTalentProof.Verify(op, es, after);
    }
    private static JsonObject VerifyPickup(JsonObject op, JsonArray es, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        long target = N(op["scope"]!["object"]);
        Identity(before, after);
        Require(N(Map(before)["id"]) == N(op["scope"]!["map"]) && N(Map(before)["id"]) == N(Map(after)["id"]), "拾取期间地图发生变化，需重新核对目标。");
        Require(Used(before, 4) == Used(after, 4), "拾取期间吸收次数发生变化，需重新核对目标。");
        var original = RewardObjects(before).Where(r => N(r[ObjectPath]) == target).ToArray();
        Require(original.Length == 1 && DropAvailable(original[0]), "原拾取目标不唯一或已不再可拾取。");
        var remaining = RewardObjects(after).Where(r => N(r[ObjectPath]) == target).ToArray();
        Require(remaining.Length <= 1 && (remaining.Length == 0 || !DropAvailable(remaining[0])), "目标尚未确认拾取；途中其他物品的回执不代表目标完成。");
        var replies = new List<JsonObject>();
        foreach (string role in new[] { "mainline.pickup", "mainline.pickup_list" })
        {
            var pending = new JsonArray();
            foreach (var e in Rows(es).Where(e => S(e["Role"]) == role).OrderBy(e => N(e["Sequence"])))
            {
                pending.Add(e.DeepClone());
                if (S(e["Kind"]) == "response")
                {
                    replies.Add(Response(role, pending, before, after)!);
                    pending = new();
                }
            }
            Require(pending.Count == 0, "拾取仍有未确认回执");
        }
        Require(replies.Count > 0, "缺少拾取回执");
        return O(("target", target), ("rewards", Array(replies)));
    }
    public async Task<JsonObject> Skill(int kind, double minRemaining = 3, int attempts = 0, bool optional = false)
    {
        Require(!(Progress.OnlySteal && kind is 3 or 4 or 6 or 20), "单独偷窃不允许使用收集天赋");
        Require(attempts < 3, "遇敌使天赋菜单反复失效");
        await FieldReady(settle: 0);
        var e = await Evidence();
        if (kind == 3 && !B(R(e, "mainline.map", "HasCanOverwhelmMonster()")))
            return DailyWorkflow.Skipped("no_suppressible_monsters");
        double remaining = 0;
        if (kind is 2 or 17)
        {
            remaining = Remaining(e, kind);
            if (remaining > minRemaining)
                return DailyWorkflow.Skipped("native_duration_active");
        }
        if (kind == 6)
        {
            var research = State(e, "mainline.research", "$self");
            remaining = B(research["Active"]) ? DailyRegionRouter.D(research["Remaining"]) : 0;
            if (remaining > minRemaining)
                return DailyWorkflow.Skipped("research_active");
        }
        if (kind is 4 or 20 && Used(e, kind) >= 21)
            return DailyWorkflow.Skipped("daily_limit");
        JsonObject opened;
        try
        {
            opened = (await W.Step("GameFieldDefaultUI", operation: "mainline_menu", value: kind, expect: "QuickMenuUI"))["after"]!.AsObject();
        }
        catch (Exception ex) when (ex is DailyStepException or StageHostException) { if (!await Encounter((await W.Observe()).Frame)) throw; return await Skill(kind, minRemaining, attempts + 1, optional); }
        if (kind == 6 && remaining > 0)
        {
            double end = W.Time + remaining + 2;
            while (true)
            {
                var research = State(await Evidence(), "mainline.research", "$self");
                if (!B(research["Active"]) || DailyRegionRouter.D(research["Remaining"]) <= 0)
                    break;
                Require(W.Time < end, "探查效果到期没有推进");
                await W.Delay(100);
            }
        }
        if (kind is 2 or 17)
        {
            double end = W.Time + Math.Max(0, remaining) + 5;
            while (true)
            {
                e = await Evidence();
                if (Remaining(e, kind) <= 0 && !Cooling(e, kind))
                    break;
                Require(W.Time < end, "移动天赋到期没有推进");
                await W.Delay(100);
            }
        }
        var menu = DailyNavigationDecision.Rows(opened).Single(r => S(r["Type"]) == "QuickMenuUI");
        JsonObject[] candidates = [];
        double menuStarted = W.Time;
        e = await W.WaitEvidence(["mainline", "dispatch.talent"], state => { if (N(state["Frame"]!["Sequence"]) < N(opened["Sequence"]) || !DailyNavigationDecision.Rows(state["Frame"]!.AsObject()).Any(r => S(r["Type"]) == "QuickMenuUI" && JsonNode.DeepEquals(r["Id"], menu["Id"]) && DailyNavigationDecision.ReadyInput(r))) return false; candidates = Rows(state["Readings"]).Where(r => S(r["Id"]) == "dispatch.talent" && S(r["Error"]) == "").Select(r => { var v = DailyEvidence.Values(r); v["instance"] = Copy(r["InstanceId"]); return v; }).Where(r => S(r["ὬὩὬὡὯὮὨὣὩὦὡ"]) == Kinds[kind]).ToArray(); return candidates.Length > 0 || optional && W.Time - menuStarted >= 3; }, 4, "原生天赋列表尚未就绪");
        var status = Talents(e).ToDictionary(r => N(r["Instance"]));
        var used = Counts(e).ToDictionary(r => N(r["groupId"]), r => N(r["useCount"]));
        var selected = candidates.Where(r => status.TryGetValue(N(r["instance"]), out var st) && S(st["Reason"]) == "" && DailyRegionRouter.D(st["Cooldown"]) <= 0 && !B(r["_objDisableImage.activeSelf"]) && (!(kind is 3 or 4 or 20) || used.GetValueOrDefault(N(r[SkillPath]!["groupId"])) < N(r[SkillPath]!["valueList"]![0]))).OrderByDescending(r => kind is 2 or 17 ? DailyRegionRouter.D(r[SkillPath]!["valueList"]![1]) : 0).ThenByDescending(r => N(r[SkillPath]!["id"])).ThenBy(r => N(r[SkillPath]!["catalystValue"])).FirstOrDefault();
        if (selected == null)
        {
            var shortage = candidates.Where(r => status.TryGetValue(N(r["instance"]), out var st) && S(st["Reason"]) == "catalyst_insufficient")
                .OrderBy(r => N(r[SkillPath]?["catalystValue"])).FirstOrDefault();
            if (shortage != null && !optional) await W.Driver.RequirePotionsAsync(N(shortage[SkillPath]?["catalystValue"]));
            await W.Step("QuickMenuUI", back: true, absent: "QuickMenuUI");
            return DailyWorkflow.Skipped("native_disabled");
        }
        if (kind == 6)
        {
            var research = State(e, "mainline.research", "$self");
            if (B(research["Active"]) && DailyRegionRouter.D(research["Remaining"]) > 1)
            {
                await W.Step("QuickMenuUI", back: true, absent: "QuickMenuUI");
                return DailyWorkflow.Skipped("native_research_active");
            }
        }
        try { await W.Driver.RequirePotionsAsync(N(selected[SkillPath]?["catalystValue"])); }
        catch (DailyStepException error) when (optional && error.PotionLimit != null)
        {
            await W.Step("QuickMenuUI", back: true, absent: "QuickMenuUI");
            return DailyWorkflow.Skipped("native_disabled");
        }
        if (kind == 15)
        {
            await W.Step("QuickMenuUI", operation: "mainline_talent", value: I(selected["instance"]), expect: "WayPointUI");
            return O(("state", "waypoint_selection"));
        }
        if (kind is 2 or 17)
            await RecoverExpired(e);
        var op = await W.Transact("dispatch.start", O(("kind", kind), ("pack", Map(e)["packId"]), ("map", Map(e)["id"]), ("group", selected[SkillPath]!["groupId"])), O(("ui", "QuickMenuUI"), ("operation", "mainline_talent"), ("value", selected["instance"])), 25);
        await FieldReady(settle: 0);
        if (kind is 2 or 17)
            Require(Remaining(await Evidence(), kind) > 0, "移动天赋未确认生效");
        return O(("state", "completed"), ("id", op["id"]), ("result", op["result"]));
    }
    internal async Task RecoverExpired(JsonObject current)
    {
        foreach (var op in W.Business.MatchingRecords(W.Context, "dispatch.start", DailyManagedBusiness.Pending).Where(op => N(op["scope"]?["kind"]) is 2 or 17).ToArray())
        {
            if (!JsonNode.DeepEquals(op["cycle"], W.Context["cycle"]) || !DailyHomeProof.SameGameAccount(op["before"]!["Frame"]!.AsObject(), current["Frame"]!.AsObject()) || N(current["AtUtcTicks"]) - N(op["at"]) < 600_000_000 || W.Business.Events(op).Count > 0)
                continue;
            var receipt = DailyJson.TryRead<JsonObject>(Path.Combine(W.Root, "live", "steps", S(op["command_id"]), "result.json"))?["receipt"] as JsonObject
                ?? DailyJson.TryRead<JsonObject>(Path.Combine(W.Root, "live", "step-recovery", S(op["command_id"]) + ".json"))?["receipt"] as JsonObject;
            if (receipt == null || S(receipt["State"]) != "observed_after_dispatch" || S(receipt["Error"]) != "" || S(receipt["Command"]?["Id"]) != S(op["command_id"]) || S(receipt["Command"]?["Kind"]) != "mainline_talent" || !JsonNode.DeepEquals(receipt["Command"]?["Value"], op["action"]?["value"]) || !DailyEvidence.SameActor(receipt["Before"]!.AsObject(), op["before"]!["Frame"]!.AsObject()) || !DailyNavigationDecision.Types(receipt["After"]!.AsObject()).Contains("QuickMenuUI"))
                continue;
            var rows = Talents(current).Where(r => N(r["Group"]) == N(op["scope"]!["group"])).ToArray();
            if (rows.Length != 1 || DailyRegionRouter.D(rows[0]["Cooldown"]) > 0 || DurationBusy(current, I(op["scope"]!["kind"])))
                continue;
            op["state"] = "superseded";
            op["reconciliation"] = O(("at", W.Driver.UtcTicks), ("method", "returned_click_no_request_and_expired_travel_effect"), ("actions", 0));
            W.Business.Save(op);
        }
    }
    public async Task Protection(JsonObject e)
    {
        bool required = RequiresStealth(e), preferred = PrefersStealth(e);
        long map = N(Map(e)["id"]);
        if (map != unavailableStealthMap) unavailableStealthMap = 0;
        if ((required || preferred && unavailableStealthMap == 0) && Remaining(e, 17) <= 5)
        {
            var result = await Skill(17, 5, optional: !required);
            if (!required && S(result["reason"]) == "native_disabled")
            {
                unavailableStealthMap = map;
                W.Driver.Diagnostics.Event("optional_field_stealth_unavailable", O(("pack", Map(e)["packId"]), ("map", map)));
                return;
            }
            Require(Remaining(await Evidence(), 17) > 5, "巡逻路线藏身不足，已停止移动");
        }
    }
    public async Task Patrol()
    {
        if (!Progress.OnlySteal)
            await Skill(6);
        await Skill(17, 12);
        await Skill(2, 5);
        var e = await Evidence();
        Require(Remaining(e, 17) > 5 && Remaining(e, 2) > 1, "巡逻路线藏身和飞奔未就绪");
    }
    public async Task WaitMap(long map, double timeout = 60)
    {
        await W.WaitEvidence(["mainline"], e => N(Map(e)["id"]) == map, timeout, "未到达预期地图：" + map);
        await FieldReady(timeout: Math.Min(20, timeout));
    }
    public static JsonObject[] Reachable(JsonObject travel, JsonObject[] rows)
    {
        var paths = Rows(travel["Approaches"]).Where(r => B(r["Reachable"])).ToDictionary(r => N(r["Instance"]));
        return rows.Where(r => paths.ContainsKey(N(r["instance"]))).OrderBy(r => DailyRegionRouter.D(paths[N(r["instance"])]["Distance"])).ThenBy(r => N(r["instance"])).ToArray();
    }
    public static string Region(JsonObject e)
    {
        var logical = FieldRows(e, "gate").Select(r => (Id: N(r["instance"]), Kind: "gate", Obj: N(r[ObjectPath]))).Concat(FieldRows(e, "waypoint").Select(r => (Id: N(r["instance"]), Kind: "waypoint", Obj: N(r[ObjectPath])))).ToDictionary(r => r.Id);
        var objects = Rows(Travel(e)["Approaches"]).Where(r => B(r["Reachable"]) && logical.ContainsKey(N(r["Instance"]))).Select(r => logical[N(r["Instance"])]).OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Obj);
        return new JsonArray(Copy(Travel(e)["Map"]), Array(objects.Select(r => (JsonNode)new JsonArray(r.Kind, r.Obj)))).ToJsonString();
    }
    private async Task Failure(JsonObject e, int target, string reason, JsonObject? detail = null)
    {
        var report = O(("reason", reason), ("target", target), ("frame", e["Frame"]), ("map", Map(e)), ("travel", Travel(e)), ("gates", Array(FieldRows(e, "gate"))), ("waypoints", Array(FieldRows(e, "waypoint"))), ("details", detail));
        try
        {
            var fresh = await W.Evidence("mainline", "route.gates", "route.navigation");
            report["latest_frame"] = Copy(fresh["Frame"]);
            if (N(Map(fresh)["id"]) == N(Map(e)["id"]))
            {
                report["contacts"] = Copy(R(fresh, "route.gates", "$self"));
                report["path"] = Copy(R(fresh, "route.navigation", "$items"));
            }
        }
        catch (Exception ex) { report["diagnostic_error"] = ex.Message; }
        try { DailyJson.Write(Path.Combine(W.Root, "live", "travel-diagnostics", W.Driver.UtcTicks + ".json"), report); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Preserve the navigation failure if diagnostics cannot be saved. */ }
    }
    public const string DropAvailablePath = "ὪὤὦὬὭὪὯὫὦὠὫ";
    private static JsonObject[] RewardObjects(JsonObject e)
    {
        Require(!Rows(e["Readings"]).Any(r => S(r["Id"]) == "mainline.reward" && S(r["Error"]) != ""), "地图物品状态读取失败，不能视为已经收集。");
        return FieldRows(e, "reward");
    }
    internal static bool DropAvailable(JsonObject row)
    {
        // Normal/hidden are immutable type flags. The native callback updates this
        // separate property from the server's obtained-object cache after pickup.
        Require(row[DropAvailablePath] is JsonValue value && value.TryGetValue<bool>(out _), "拾取状态尚未读取，请重新连接以更新观察组件。");
        return B(row[DropAvailablePath]);
    }
    public static JsonObject[] Drops(JsonObject e) => RewardObjects(e).Where(r =>
        (B(r["ὦὫὧὯὥὫὦὪὣὦὪ"]) || B(r["ὯὩὮὩὮὣὯὫὮὡὧ"])) && DropAvailable(r)).ToArray();
}

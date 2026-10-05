using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static BD2Daily.DailyData;
using static BD2Daily.DailyFieldRoute;
namespace BD2Daily;

public sealed class DailyNpcUnavailable(string message) : Exception(message);
public sealed record DailyNpcData(JsonObject Native, Dictionary<long, JsonObject> Rows, JsonObject[] Active)
{
    public static DailyNpcData Decode(JsonObject n)
    {
        if (S(n["State"]) != "ready" || S(n["Error"]) != "")
            throw new DailyNpcUnavailable(S(n["Error"]) is { Length: > 0 } reason ? reason : "周NPC任务尚无服务器状态");
        var rows = DailyData.Rows(JsonNode.Parse(S(n["Rows"]))).ToDictionary(r => N(r["id"]));
        var active = DailyData.Rows(JsonNode.Parse(S(n["Active"])));
        if (active.Any(a => !rows.ContainsKey(N(a["id"]))) || n["Posted"]!.AsArray().Any(i => !rows.ContainsKey(N(i))) || N(n["Limit"]) < 1 || N(n["Week"]) <= 0)
            throw new DailyNpcUnavailable("周NPC任务表、次数或周期缺失");
        return new(n, rows, active);
    }
    public JsonObject[] Chain(long id)
    {
        var seen = new HashSet<long>();
        var result = new List<JsonObject>();
        while (id != 0)
        {
            if (!seen.Add(id) || !Rows.TryGetValue(id, out var row))
                throw new DailyNpcUnavailable("周NPC任务链缺失或循环");
            result.Add(row);
            id = N(row["nextQuestId"]);
        }
        return result.ToArray();
    }
    public long Root(long id)
    {
        var seen = new HashSet<long>();
        while (true)
        {
            if (!seen.Add(id) || !Rows.TryGetValue(id, out var row))
                throw new DailyNpcUnavailable("周NPC任务前置链缺失或循环");
            long prior = N(row["priorQuestId"]);
            if (prior == 0)
                return id;
            id = prior;
        }
    }
    public JsonObject[] Wild(JsonObject row) => DailyData.Rows(Native["Wild"]).Where(x => N(x["Step"]) == N(row["id"]) && B(x["Available"])).ToArray();
    public bool RequiresHunting(JsonObject row) => N(row["conditionType"]) == 1 && Wild(row).GroupBy(x => N(x["Monster"])).Sum(g => N(g.First()["Count"])) < N(row["conditionCount"] ?? JsonValue.Create(1));
    public JsonObject? Current(long pack) => Active.Select(a => Rows[N(a["id"])]).FirstOrDefault(r => N(r["packId"]) == pack);
    public JsonObject[] Candidates(long pack, bool allowHunting, IEnumerable<long> excluded)
    {
        if (Native["CanAccept"] != null && !B(Native["CanAccept"]) || N(Native["Remaining"]) <= 0 || Current(pack) != null)
            return [];
        var done = Native["Cleared"]!.AsArray().Select(N).Where(Rows.ContainsKey).Select(Root).ToHashSet();
        var excludedMaps = excluded.ToHashSet();
        return Native["Posted"]!.AsArray().Select(N).Where(Rows.ContainsKey).Where(id => N(Rows[id]["priorQuestId"]) == 0 && N(Rows[id]["packId"]) == pack && !done.Contains(id))
            .Select(id => (Id: id, Steps: Chain(id))).Where(x => x.Steps.All(r => N(r["conditionType"]) is 1 or 2 or 9 or 18 or 19) && !x.Steps.Any(r => excludedMaps.Contains(N(r["mapId"])) && N(r["conditionType"]) != 1))
            .Select(x => (x.Id, x.Steps, Hunting: x.Steps.Any(RequiresHunting))).Where(x => allowHunting || !x.Hunting).OrderBy(x => x.Hunting).ThenBy(x => x.Steps.Any(r => N(r["conditionType"]) == 1)).ThenBy(x => x.Steps.Select(r => N(r["mapId"])).Distinct().Count()).ThenBy(x => x.Steps.Length).ThenBy(x => x.Id)
            .Select(x => O(("id", x.Id), ("hunting", x.Hunting), ("steps", Array(x.Steps)))).ToArray();
    }
    public bool Protects(long map) => Active.Any(a => Chain(N(a["id"])).Skip(1).Any(r => N(r["conditionType"]) == 1 && Wild(r).Any(x => N(x["Map"]) == map)));
    public JsonObject? CarrySource(long id)
    {
        var sources = DailyData.Rows(Native["CarryObjects"] ?? new JsonArray()).Where(x => N(x["Quest"]) == id).ToArray();
        long held = N(Native["CarryId"]);
        if (held != 0)
        {
            if (!sources.Any(x => N(x["Id"]) == held))
                throw new DailyNpcUnavailable("手持物品不属于当前搬运任务，保留现场");
            return null;
        }
        return sources.Where(x => B(x["Available"])).OrderBy(x => DailyRegionRouter.D(x["Distance"])).ThenBy(x => N(x["Id"])).FirstOrDefault() ?? throw new DailyNpcUnavailable("搬运任务没有可用物品来源");
    }
}
public sealed class DailyWeeklyNpc(DailyFieldRoute route, bool allowHunting)
{
    private DailyWorkflow W => route.W;
    public DailyNpcData? Data
    {
        get; private set;
    }
    public HashSet<long> ExcludedRoots { get; } = [];
    public HashSet<long> ExhaustedPacks { get; } = [];
    public async Task<DailyNpcData> Observe() => DailyNpcData.Decode(State(await route.Evidence(), "weekly_npc.native", "$self"));
    public async Task<DailyNpcData> Query(long pack)
    {
        var receipt = await W.Step(await W.Has("QuestBoardUI") ? "QuestBoardUI" : "GameFieldDefaultUI", operation: "weekly_npc_query", value: checked((int)pack));
        string id = S(receipt["id"]);
        double end = W.Time + 40;
        while (W.Time < end)
        {
            var n = State(await route.Evidence(), "weekly_npc.native", "$self");
            if (S(n["Query"]) == id)
            {
                if (S(n["State"]) == "ready")
                    return Data = DailyNpcData.Decode(n);
                if (S(n["State"]) is "invalid" or "failed")
                    throw new DailyNpcUnavailable(S(n["Error"]));
            }
            await W.Delay(250);
        }
        throw new DailyNpcUnavailable("周NPC任务查询超时，未接取新任务");
    }
    public async Task Prepare(long pack)
    {
        Data = await Query(pack);
        if (Data.Current(pack) != null) { await DailyWeeklyNpcBoard.Close(W); return; }
        var chosen = Data.Candidates(pack, allowHunting, Excluded).FirstOrDefault(c => !ExcludedRoots.Contains(N(c["id"])));
        if (chosen == null)
        {
            ExhaustedPacks.Add(pack);
            await DailyWeeklyNpcBoard.Close(W);
            return;
        }
        await DailyWeeklyNpcBoard.Open(route, Data, pack);
        // Walking can take time: re-read quota and offered quests on the open board.
        Data = await Query(pack);
        chosen = Data.Candidates(pack, allowHunting, Excluded).FirstOrDefault(c => !ExcludedRoots.Contains(N(c["id"])));
        if (chosen == null) { if (Data.Current(pack) == null) ExhaustedPacks.Add(pack); await DailyWeeklyNpcBoard.Close(W); return; }
        long id = N(chosen["id"]);
        await DailyWeeklyNpcBoard.Select(W, Observe, id);
        // A lost confirmation is never replayed. A new run queries the accepted server state.
        await W.Step(O(("ui", "QuestPopupUI"), ("operation", "weekly_npc_accept"), ("value", id), ("items", new[] { allowHunting ? 1 : 0 }), ("absent", "QuestPopupUI")));
        await DailyWeeklyNpcBoard.Close(W);
        await route.FieldReady();
        Data = await Query(pack);
        if (!Data.Active.Any(a => Data.Root(N(a["id"])) == id) && !Data.Native["Cleared"]!.AsArray().Select(N).Where(Data.Rows.ContainsKey).Any(i => Data.Root(i) == id))
        {
            ExcludedRoots.Add(id);
            throw new DailyNpcUnavailable("接取未获服务器确认，不重复提交");
        }
    }
    public async Task<long[]> Targets(long pack)
    {
        var row = Data?.Current(pack);
        if (row == null)
            return [];
        if (N(row["conditionType"]) != 1)
            return [N(row["mapId"])];
        var wild = Data!.Wild(row);
        if (wild.Length > 0)
            return wild.Select(x => N(x["Map"])).Distinct().ToArray();
        if (!allowHunting)
            throw new DailyNpcUnavailable("已接任务的野外目标消失；未开启狩猎场执行");
        return [N(Map(await route.Evidence())["id"])];
    }
    public string Summary() => Data == null ? "等待读取本周任务" : $"本周完成 {N(Data.Native["Completed"])}/{N(Data.Native["Limit"])}" + (Data.Current(N(Data.Native["Pack"])) == null ? "" : " · " + Regex.Replace(S(Data.Native["CurrentName"]), "<[^>]+>", ""));
    public async Task<bool> AtMap(long pack, long map)
    {
        var row = Data?.Current(pack);
        if (row == null || !(await Targets(pack)).Contains(map))
            return false;
        bool prior = route.NpcActing;
        route.NpcActing = true;
        try
        {
            if (N(row["conditionType"]) == 1 && Data!.Wild(row).Any(x => N(x["Map"]) == map))
            {
                if (!B(R(await route.Evidence(), "mainline.map", "HasCanOverwhelmMonster()")))
                    throw new DailyNpcUnavailable("服务器目标存在，但当前地图没有可压制对象");
                var before = Array(Data.Active);
                await route.Skill(3);
                await route.FieldReady();
                Data = await Query(pack);
                if (JsonNode.DeepEquals(before, Array(Data.Active)))
                    throw new DailyNpcUnavailable("压制后击杀进度未变化，保留任务");
                return true;
            }
            if (N(row["conditionType"]) == 18)
                await route.Skill(6);
            await Advance(row);
            Data = await Query(pack);
            if (Data.Active.Any(a => N(a["id"]) == N(row["id"])))
                throw new DailyNpcUnavailable("交互后任务步骤尚未获得确认，保留游戏进度，不重复提交");
            return true;
        }
        finally { route.NpcActing = prior; }
    }
    private Task<JsonObject> Navigate(long id) => W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "weekly_npc_nav"), ("value", id), ("items", new[] { allowHunting ? 1 : 0 })));
    private async Task Advance(JsonObject row)
    {
        long id = N(row["id"]);
        var before = Data!.Active.Single(a => N(a["id"]) == id).DeepClone().AsObject();
        JsonObject? carry = N(row["conditionType"]) == 9 ? (await Observe()).CarrySource(id) : null;
        if (carry != null)
            await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "weekly_npc_carry_nav"), ("value", id), ("items", new[] { N(carry["Instance"]) })));
        else
            await Navigate(id);
        bool liftSent = false, interactionSent = false;
        long pendingObject = 0;
        bool objectStep = N(row["conditionType"]) is 2 or 9 or 18;
        static HashSet<long> Collected(JsonObject step) => (step["objectId"] as JsonArray ?? new()).Select(N).ToHashSet();
        double end = W.Time + 180, lastProgress = W.Time;
        string last = "", recoveryRecorded = "";
        var policy = DailyNavigationPolicy.Load();
        while (W.Time < end)
        {
            var f = (await W.Observe()).Frame;
            var types = DailyNavigationDecision.Types(f);
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            {
                await Battle(row);
                lastProgress = W.Time;
                continue;
            }
            var presentation = DailyNavigationDecision.WeeklyResult(f, policy) ?? DailyNavigationDecision.StoryAction(f, policy) ?? DailyNavigationDecision.TalentAction(f, policy) ?? DailyNavigationDecision.Notice(f, policy, out _);
            if (presentation != null)
            {
                await W.Driver.NavigationAsync(presentation);
                continue;
            }
            if (types.Contains("RewardReceivePopupUI"))
            {
                await W.Dismiss("GameFieldDefaultUI");
                continue;
            }
            if (types.Contains("BalloonScriptUI") && DailyNavigationDecision.Rows(f).Any(r => S(r["Type"]) == "BalloonScriptUI" && DailyNavigationDecision.ReadyInput(r) && Rows(r["Targets"]).Any(t => S(t["Field"]) == "_objTouchButton" && B(t["Enabled"]))))
            {
                await W.Step("BalloonScriptUI", operation: "weekly_npc_talk", value: checked((int)id));
                await W.Delay(200);
                continue;
            }
            var data = await Observe();
            Data = data;
            string recovery = S(data.Native["NavigationRecovery"]) + "|" + data.Native["NavigationRemoved"]?.ToJsonString();
            if (S(data.Native["NavigationRecovery"]) == "repaired_stale_collection_cache" && recoveryRecorded != recovery) {
                recoveryRecorded = recovery;
                SaveRecovery("navigation_cache_repaired", row, data);
            }
            var current = data.Active.SingleOrDefault(a => N(a["id"]) == id);
            // ObjectId changes confirm ONE native pickup, not completion of this
            // step. TodayQuestInfo can omit these local, acknowledged objects;
            // querying between pickups used to wipe them before QuestClear ran.
            if (current == null && !types.Contains("BalloonScriptUI"))
            {
                await route.FieldReady();
                await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                return;
            }
            if (current == null)
            {
                await W.Delay(300);
                continue;
            }
            var collected = Collected(current);
            var priorObjects = Collected(before);
            if (!priorObjects.IsSubsetOf(collected) || N(current["value"]) < N(before["value"]))
                throw new DailyNpcUnavailable("NPC任务局部进度发生回退，保留现场；未重新拾取或提交");
            bool advanced = !collected.SetEquals(priorObjects) || N(current["value"]) > N(before["value"]);
            if (advanced)
            {
                before = current.DeepClone().AsObject();
                lastProgress = W.Time;
            }
            bool pickupConfirmed = pendingObject != 0 && collected.Contains(pendingObject);
            if ((advanced && pendingObject == 0 || pickupConfirmed) && !types.Contains("BalloonScriptUI"))
            {
                interactionSent = false;
                pendingObject = 0;
                if (objectStep && collected.Count < N(row["conditionCount"]))
                {
                    await route.FieldReady();
                    // Continue the same step through the game's own navigation,
                    // whose collected-object list excludes successful pickups.
                    carry = N(row["conditionType"]) == 9 ? (await Observe()).CarrySource(id) : null;
                    liftSent = false;
                    if (carry != null)
                        await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "weekly_npc_carry_nav"), ("value", id), ("items", new[] { N(carry["Instance"]) })));
                    else
                        await Navigate(id);
                    end = W.Time + 180;
                    continue;
                }
                // All objects have been acknowledged. Wait for the game's own
                // completion callback / next step; do not synthesize a clear.
            }
            if (carry != null && types.Contains("GameFieldDefaultUI"))
            {
                long held = N(data.Native["CarryId"]);
                if (held != 0 && held != N(carry["Id"]))
                    throw new DailyNpcUnavailable("搬运物品发生变化，保留任务");
                if (held == N(carry["Id"]))
                {
                    await Navigate(id);
                    carry = null;
                    lastProgress = W.Time;
                    continue;
                }
                if (!liftSent && !B(data.Native["Navigating"]) && Rows(data.Native["CarryObjects"] ?? new JsonArray()).Any(x => N(x["Instance"]) == N(carry["Instance"]) && N(x["Quest"]) == id && B(x["Near"]) && B(x["Ready"])))
                {
                    await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "weekly_npc_lift"), ("value", id), ("items", new[] { N(carry["Instance"]) })));
                    liftSent = true;
                    lastProgress = W.Time;
                    continue;
                }
            }
            if (carry == null && !interactionSent && objectStep && collected.Count < N(row["conditionCount"]) && types.Contains("GameFieldDefaultUI") && !B(data.Native["Navigating"]))
            {
                var target = Rows(data.Native["Objects"] ?? new JsonArray()).Where(x => N(x["Quest"]) == id && !collected.Contains(N(x["Id"])) && (row["magicValue"] as JsonArray ?? new()).Select(N).Contains(N(x["Id"])) && B(x["Near"]) && B(x["Ready"])).OrderBy(x => N(x["Id"])).FirstOrDefault();
                if (target != null)
                {
                    await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "weekly_npc_interact"), ("value", id), ("items", new[] { N(target["Instance"]) })));
                    interactionSent = true;
                    pendingObject = N(target["Id"]);
                    lastProgress = W.Time;
                    continue;
                }
            }
            string signature = current.ToJsonString() + "|" + S(data.Native["Map"]) + "|" + S(data.Native["Navigating"]) + "|" + S(f["UiToken"]);
            if (signature != last)
            {
                last = signature;
                lastProgress = W.Time;
            }
            if (!B(data.Native["Navigating"]) && W.Time - lastProgress > 12)
                break;
            await W.Delay(300);
        }
        if (await W.Has("GameFieldDefaultUI"))
            await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
        SaveRecovery("step_not_advanced", row, Data!);
        throw new DailyNpcUnavailable(objectStep
            ? $"任务物件已确认 {Collected(before).Count}/{N(row["conditionCount"])}，游戏尚未推进下一步；保留进度，不重复点击未确认物件"
            : "NPC任务步骤未推进，保留服务器进度");
    }
    private void SaveRecovery(string state, JsonObject row, DailyNpcData data)
    {
        var snapshot = data.Native.DeepClone().AsObject();
        snapshot.Remove("Rows"); snapshot.Remove("Posted"); snapshot.Remove("Wild");
        DailyJson.Write(Path.Combine(W.Root, "live", "weekly-npc-recovery", DateTime.UtcNow.Ticks + "-" + N(row["id"]) + ".json"),
            O(("state", state), ("step", row), ("native", snapshot)));
    }
    private async Task Battle(JsonObject row)
    {
        double end = W.Time + 600;
        bool started = false, seenResult = false;
        var policy = DailyNavigationPolicy.Load();
        while (W.Time < end)
        {
            var f = (await W.Observe()).Frame;
            var types = DailyNavigationDecision.Types(f);
            var n = (await Observe()).Native;
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal) && t != "BattleUI_FieldBattle"))
                throw new DailyNpcUnavailable("当前战斗不属于周NPC任务");
            if (types.Contains("BattleResultUI"))
            {
                seenResult = true;
                var result = DailyNavigationDecision.Rows(f).Single(r => S(r["Type"]) == "BattleResultUI");
                if (DailyNavigationDecision.ReadyInput(result) && DailyNavigationDecision.Blockers(f, "BattleResultUI", policy).Length == 0)
                {
                    var fields = Rows(result["Targets"]).Where(t => B(t["Enabled"])).Select(t => S(t["Field"])).ToHashSet();
                    string? field = new[] { "_objectWinExitButton", "_objectLoseExitButton", "_goWinSafeArea", "_goLoseSafeArea" }.FirstOrDefault(fields.Contains);
                    if (field != null)
                    {
                        await W.Step("BattleResultUI", field);
                        continue;
                    }
                }
            }
            else if (types.Contains("BattleUI_FieldBattle") && B(n["BattleReady"]) && !B(n["BattleAuto"]) && !started)
            {
                await W.Step(O(("ui", "BattleUI_FieldBattle"), ("operation", "weekly_npc_auto"), ("value", row["id"]), ("items", new[] { 1 })));
                started = true;
            }
            else if (types.Contains("GameFieldDefaultUI") && (started || seenResult))
                return;
            await W.Delay(350);
        }
        throw new DailyNpcUnavailable("NPC狩猎结算超时，保留现场，不重复挑战");
    }
}

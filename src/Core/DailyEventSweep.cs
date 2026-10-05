using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

/// <summary>Native free-AP sweep. Public policy is exactly cleared Challenge 15, at most five per server day.</summary>
public static class DailyEventSweep
{
    public const string PublicPolicy = "challenge15.daily.v1";
    public static readonly string[] Prefixes = ["event.lobby", "event.currency", "event.sweep"];
    public const string Count = "_sliderAutoCount.ὮὢὥὯὥὧὤὭὨὨὪ";
    private const string Popup = "event.sweep.popup";
    public static JsonObject Lobby(JsonObject e) => State(e, "event.lobby", "$self");
    public static JsonObject? Highest(JsonObject lobby)
    {
        if (!B(lobby["Eligible"]))
            return null;
        var rows = Rows(lobby["Stages"]);
        Require(rows.Select(r => N(r["Id"])).Distinct().Count() == rows.Length, "Duplicate event stages");
        var candidates = new List<JsonObject>();
        foreach (var row in rows)
        {
            var (table, deck) = DailyEventStageData.Tables(lobby, row);
            var progress = DailyEventStageData.Progress(lobby, row);
            if (N(table["quickBattlePossible"]) != 1 || progress == null)
                continue;
            Require(N(progress["eventUid"]) == N(lobby["Event"]) && N(progress["groupId"]) == N(lobby["Group"]) && N(progress["id"]) == N(row["Id"]), "Event progress belongs to another stage");
            Require(N(table["id"]) == N(row["Id"]) && N(table["groupId"]) == N(lobby["Group"]) && N(table["battleDeckId"]) == N(deck["id"]), "Event table identity differs");
            var indices = (progress["battleChallengeIndex"] as JsonArray ?? new()).Select(N).Order().ToArray();
            if (!indices.SequenceEqual(Enumerable.Range(0, (deck["bonusRewardId"] as JsonArray ?? new()).Count).Select(x => (long)x)))
                continue;
            Require(N(table["eventApCount"]) > 0, "Unknown event AP cost");
            candidates.Add(O(("event", lobby["Event"]), ("group", lobby["Group"]), ("stage", row["Id"]), ("deck", deck["id"]), ("cost", table["eventApCount"]), ("category", lobby["Category"])));
        }
        return candidates.OrderByDescending(c => N(c["stage"])).FirstOrDefault();
    }
    public static JsonObject? Challenge15(JsonObject lobby)
    {
        if (S(lobby["Category"]) != "Challenge" || !B(lobby["Eligible"]))
            return null;
        var rows = Rows(lobby["Stages"]).Where(r => N(r["Id"]) == 15).ToArray();
        if (rows.Length != 1)
            return null;
        var reduced = lobby.DeepClone().AsObject();
        reduced["Stages"] = Array(rows);
        var t = Highest(reduced);
        if (t != null)
            t["policy"] = PublicPolicy;
        return t;
    }
    public static JsonObject PopupPlan(JsonObject e, JsonObject target)
    {
        JsonNode? P(string path) => R(e, Popup, path);
        Require(B(P("_buttonFreeOnly.IsEnable")), "Sweep must use only free AP");
        Require(S(P("ὩὥὭὬὬὥὡὠὥὧὭ")) is "PackEventBattle" or "17" && S(P("ὥὩὢὦὫὩὭὬὬὦὥ")) is "Skip" or "0", "Not native event sweep mode");
        string[] keys = ["group", "stage", "deck", "cost"], paths = ["ὠὬὦὫὩὨὣὫὤὢὨ", "ὦὠὠὧὩὩὮὥὫὧὡ", "ὪὪὥὧὡὪὥὡὡὣὡ", "ὩὤὦὯὯὨὫὧὥὡὯ"];
        for (int i = 0; i < keys.Length; i++)
            Require(N(P(paths[i])) == N(target[keys[i]]), "Sweep popup changed: " + keys[i]);
        var lobby = Lobby(e);
        var expected = S(target["policy"]) == PublicPolicy ? Challenge15(lobby) : Highest(lobby);
        var canonical = target.DeepClone().AsObject();
        canonical.Remove("budget");
        Require(JsonNode.DeepEquals(expected, canonical) && N(lobby["Selected"]) == N(target["stage"]), "Sweep target changed");
        long count = N(P(Count)), cost = N(target["cost"]);
        Require(cost > 0 && count > 0 && count <= N(R(e, "event.currency", "EventApFree")) / cost, "Sweep exceeds free AP");
        if (S(target["policy"]) == PublicPolicy)
            Require(count <= N(target["budget"] ?? JsonValue.Create(5)) && N(target["budget"] ?? JsonValue.Create(5)) <= 5, "Daily sweep budget exceeded");
        var result = target.DeepClone().AsObject();
        result["count"] = count;
        return result;
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var plan = op["scope"]!.AsObject();
        var target = plan.DeepClone().AsObject();
        target.Remove("count");
        Require(JsonNode.DeepEquals(PopupPlan(before, target), plan), "Sweep confirmation plan differs");
        int packets = checked((int)((N(plan["count"]) + 99) / 100));
        var responses = Responses("event.sweep", events, before, after, packets, packets);
        Require(responses.All(r => r["RewardBundle"] is JsonObject b && b.Count > 0), "Sweep rewards missing");
        long spent = checked(N(plan["count"]) * N(plan["cost"]));
        Require(N(R(before, "event.currency", "EventApFree")) - N(R(after, "event.currency", "EventApFree")) == spent, "Sweep free AP debit differs");
        Require(JsonNode.DeepEquals(R(before, "event.currency", "EventApStack"), R(after, "event.currency", "EventApStack")), "Stored AP was changed");
        var result = plan.DeepClone().AsObject();
        result["spent"] = spent;
        result["rewards"] = Array(responses);
        return result;
    }
    public static DailyBusinessProof Definition() => new("event.sweep", "event_battle", Prefixes, Verify);
    public static int Remaining(DailyWorkflow w, long eventId)
    {
        long used = 0;
        foreach (var op in w.Business.Records(w.Context, "event.sweep"))
        {
            if (!JsonNode.DeepEquals(op["cycle"], w.Context["cycle"]) || N(op["scope"]?["event"]) != eventId || N(op["scope"]?["stage"]) != 15 || S(op["scope"]?["category"]) != "Challenge")
                continue;
            if (DailyManagedBusiness.Pending(op))
                throw new StageHostException("pending", "挑战15上次扫荡尚未确认，未重新提交。");
            if (S(op["state"]) == "completed")
            {
                Require(N(op["result"]?["count"]) == N(op["scope"]?["count"]) && N(op["scope"]?["count"]) > 0, "Invalid completed sweep count");
                used += N(op["scope"]?["count"]);
            }
        }
        return checked((int)Math.Max(0, 5 - used));
    }
    public static async Task EnterMain(DailyWorkflow w)
    {
        const string banner = "$pointer/UIRoot/Mask/Object - Right/BannerLayout/EventBanner/ScrollRect/Viewport/Content/EventTitle";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await w.Home("event_battle");
            await w.Step("MenuUI", banner, reason: "进入活动大厅");
            double end = w.Time + 40, menuSince = 0;
            bool dismissed = false;
            while (w.Time < end)
            {
                var frame = (await w.Observe()).Frame;
                var types = DailyNavigationDecision.Types(frame);
                if (types.Contains("EventMainUI") && !types.Contains("NewsPopupEventUI"))
                    return;
                var action = DailyNavigationDecision.Notice(frame, DailyNavigationPolicy.Load(), out _);
                if (action != null)
                {
                    await w.Step(action);
                    dismissed = true;
                    menuSince = 0;
                    continue;
                }
                if (dismissed && types.Contains("MenuUI") && DailyNavigationDecision.Blockers(frame, "MenuUI", DailyNavigationPolicy.Load()).Length == 0)
                {
                    if (menuSince == 0)
                        menuSince = w.Time;
                    if (w.Time - menuSince >= 2)
                        break;
                }
                else
                    menuSince = 0;
                await w.Delay(200);
            }
        }
        throw new StageHostException("adapter", "活动大厅没有进入，未开始战斗。");
    }
    public static async Task Leave(DailyWorkflow w)
    {
        for (int i = 0; i < 3; i++)
        {
            if (await w.Has("BattleUI_EventBattle"))
                throw new StageHostException("pending", "活动战斗尚未完成，未退出。");
            if (await w.Has("EventBattleUI"))
                await w.Step("EventBattleUI", back: true, absent: "EventBattleUI");
            else if (await w.Has("EventMainUI"))
                await w.Step("EventMainUI", back: true, absent: "EventMainUI");
            else
                break;
        }
        await w.Home("event_battle");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, JsonObject lobby, JsonObject? fixedTarget = null, int? budget = null)
    {
        var target = budget != null ? fixedTarget?.DeepClone().AsObject() : Highest(lobby);
        if (budget != null)
        {
            Require(target != null && S(target["policy"]) == PublicPolicy && budget is > 0 and <= 5, "Invalid daily challenge plan");
            target!["budget"] = budget.Value;
        }
        if (budget == null && target == null && S(lobby["Category"]) != "Battle")
        {
            await w.Step("EventBattleUI", back: true, expect: "EventMainUI");
            await w.Target("EventMainUI", "_buttonBattle._button");
            await w.Step("EventMainUI", "_buttonBattle._button", expect: "EventBattleUI");
            lobby = Lobby(await w.Evidence(Prefixes));
            target = Highest(lobby);
        }
        if (target == null)
            return DailyWorkflow.Partial("no_fully_cleared_sweep_stage");
        long free = N(R(await w.Evidence(Prefixes), "event.currency", "EventApFree"));
        if (free < N(target["cost"]))
            return O(("state", "completed"), ("reason", "free_attempts_exhausted"), ("count", 0), ("remaining", free), ("target", target));
        if (N(lobby["Selected"]) != N(target["stage"]))
        {
            foreach (int value in new[] { 0, 1 })
                await w.Step(O(("ui", "EventBattleUI"), ("operation", "event_sweep_select"), ("items", new JsonArray(Copy(target["event"]), Copy(target["group"]), Copy(target["stage"]))), ("value", value)));
            await w.WaitEvidence(Prefixes, e => N(Lobby(e)["Selected"]) == N(target["stage"]));
        }
        await w.Target("EventBattleUI", "_buttonSkipBattle._button");
        await w.Step("EventBattleUI", "_buttonSkipBattle._button", expect: "BattleSkipPopupUI");
        if (!B(R(await w.Evidence(Prefixes), Popup, "_buttonFreeOnly.IsEnable")))
        {
            await w.Step("BattleSkipPopupUI", "_buttonFreeOnly._objectRoot");
            await w.WaitEvidence(Prefixes, e => B(R(e, Popup, "_buttonFreeOnly.IsEnable")));
        }
        if (budget != null)
        {
            int count = checked((int)Math.Min(budget.Value, free / N(target["cost"])));
            await w.Step("BattleSkipPopupUI", "_sliderAutoCount._objMinusMaxButton");
            await w.WaitEvidence(Prefixes, e => N(R(e, Popup, Count)) == 1);
            for (int value = 2; value <= count; value++)
            {
                await w.Step("BattleSkipPopupUI", "_sliderAutoCount._objPlusButton");
                await w.WaitEvidence(Prefixes, e => N(R(e, Popup, Count)) == value);
            }
        }
        else
        {
            await w.Step("BattleSkipPopupUI", "_sliderAutoCount._objPlusMaxButton");
            await w.WaitEvidence(Prefixes, e => N(R(e, Popup, Count)) == N(R(e, "event.currency", "EventApFree")) / N(target["cost"]));
        }
        var plan = PopupPlan(await w.Evidence(Prefixes), target);
        if (budget != null)
            Require(Remaining(w, N(target["event"])) == budget.Value, "Daily challenge budget changed");
        var op = await w.Transact("event.sweep", plan, O(("ui", "BattleSkipPopupUI"), ("operation", "event_sweep_confirm"), ("items", new JsonArray(Copy(target["event"]), Copy(target["group"]), Copy(target["stage"]), Copy(target["deck"]))), ("value", plan["count"])), 45);
        await w.Dismiss("EventBattleUI", true);
        return O(("state", "completed"), ("operation", op["id"]), ("result", op["result"]));
    }
    public static async Task<JsonObject> PublicRun(DailyWorkflow w)
    {
        if (!w.Settings.EventBattle.Enabled)
            return DailyWorkflow.Skipped("disabled");
        var frame = (await w.Observe()).Frame;
        if (DailyNavigationDecision.Types(frame).Overlaps(new[] { "BattleUI_EventBattle", "BattleResultUI" }))
            return DailyWorkflow.Skipped("extension_in_progress");
        if (!await w.Has("EventBattleUI") || S(Lobby(await w.Evidence(Prefixes))["Category"]) != "Challenge")
        {
            if (await w.Has("EventBattleUI"))
                await w.Step("EventBattleUI", back: true, expect: "EventMainUI");
            if (!await w.Has("EventMainUI"))
                await EnterMain(w);
            await w.Target("EventMainUI", "_buttonChallenge._button");
            await w.Step("EventMainUI", "_buttonChallenge._button", expect: "EventBattleUI");
        }
        var lobby = Lobby(await w.Evidence(Prefixes));
        var target = Challenge15(lobby);
        JsonObject result;
        if (target == null)
            result = DailyWorkflow.Skipped("challenge15_not_sweepable");
        else
        {
            int remaining = Remaining(w, N(target["event"]));
            result = remaining == 0 ? DailyWorkflow.Skipped("daily_limit_reached") : await Run(w, lobby, target, remaining);
        }
        await Leave(w);
        return result;
    }
}



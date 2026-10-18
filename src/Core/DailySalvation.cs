using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailySalvation
{
    public const string Stage = "salvation";
    public static readonly string[] Prefixes = ["salvation.native"];
    public static JsonObject StateOf(JsonObject e) => State(e, "salvation.native", "$self");
    public static JsonObject Plan(JsonObject s, int reserve, int fixedCount = 0)
    {
        Require(reserve is >= 0 and <= 999, "Invalid salvation key reserve");
        Require(fixedCount is >= 0 and <= 999, "Invalid salvation sweep count");
        string reason = !B(s["Unlocked"]) ? "salvation_locked" : !B(s["Season"]) ? "salvation_closed" :
            !B(s["Ready"]) ? "salvation_not_ready" : !B(s["Idle"]) ? "salvation_run_in_progress" :
            N(s["Level"]) <= 0 ? "salvation_no_cleared_level" : "";
        if (reason.Length > 0) return Skipped(reason);
        long free = N(s["Free"]), stones = N(s["Stones"]), remaining = N(s["RemainingCost"]), reward = N(s["Reward"]), cost = N(s["Cost"]);
        Require(free >= 0 && stones >= 0 && remaining >= 0 && reward > 0 && cost > 0, "Salvation resource data unavailable");
        if (fixedCount == 0 && stones >= remaining) return Skipped("salvation_growth_funded");
        long wanted = fixedCount > 0 ? fixedCount : (remaining - stones - 1) / reward + 1;
        long count = Math.Min(Math.Max(0, free - reserve) / cost, wanted);
        if (count == 0) return Skipped("salvation_keys_reserved");
        var plan = O(("state", "ready"), ("level", s["Level"]), ("count", count), ("reserve", reserve), ("free", free), ("paid", s["Paid"]),
            ("stones", stones), ("remainingCost", remaining), ("reward", reward), ("cost", cost));
        // Preserve the original growth-mode scope for interrupted older operations.
        if (fixedCount > 0) plan["fixedCount"] = fixedCount;
        return plan;
    }
    private static JsonObject Skipped(string reason) => DailyWorkflow.Skipped(reason);
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject(); Identity(before, after);
        var reply = Response("salvation.sweep", events, before, after)!;
        var scope = op["scope"]!.AsObject(); var first = StateOf(before); var last = StateOf(after);
        Require(JsonNode.DeepEquals(Plan(first, I(scope["reserve"]), I(scope["fixedCount"])), scope), "Salvation plan changed before confirmation");
        Require(B(first["Popup"]) && B(first["FreeOnly"]) && N(first["PopupLevel"]) == N(scope["level"]) && N(first["PopupCost"]) == N(scope["cost"]) && N(first["Count"]) == N(scope["count"]), "Salvation popup differs");
        Require(N(first["Free"]) - N(last["Free"]) == checked(N(scope["count"]) * N(scope["cost"])) && N(last["Free"]) >= N(scope["reserve"]), "Salvation free key debit differs");
        Require(N(first["Paid"]) == N(last["Paid"]) && N(last["Stones"]) - N(first["Stones"]) == checked(N(scope["count"]) * N(scope["reward"])), "Salvation reward or paid keys changed");
        Require(B(last["Idle"]) && N(first["RemainingCost"]) == N(last["RemainingCost"]), "Salvation growth or run state changed");
        Require(N(reply["ScoreInfo"]?["obsidian"] ?? reply["ScoreInfo"]?["Obsidian"]) == N(scope["count"]) * N(scope["reward"]), "Salvation response reward differs");
        return O(("count", scope["count"]), ("level", scope["level"]), ("stonesGained", N(last["Stones"]) - N(first["Stones"])), ("keysRemaining", last["Free"]));
    }
    public static DailyBusinessProof Definition() => new("salvation.sweep", Stage, Prefixes, Verify, CanResume: DailyNativeBattleRecovery.CanResume);
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Salvation.Enabled) return Skipped("disabled");
        if (await w.Has("RogueLikeTotalScoreUI"))
            await w.Step("RogueLikeTotalScoreUI", "_objBackButton", absent: "RogueLikeTotalScoreUI");
        if (await w.Has("RogueLikeSkipPopupUI"))
            await w.Step("RogueLikeSkipPopupUI", back: true, absent: "RogueLikeSkipPopupUI");
        if (!await w.Has("RogueLikeLobbyUI"))
        {
            if (!await w.Has("EvilCastleEnterUI"))
            {
                // Opening the real list resets the native selection latch in
                // PackListLoopScrollItem.OnEnable. Direct selection from MenuUI
                // can otherwise be silently ignored after an earlier pack switch.
                if (!await DailyTravel.Enter(w, 1, "salvation.pack", packId: 3003, surfaces: ["GameFieldDefaultUI", "EvilCastleEnterUI", "RogueLikeLobbyUI"]))
                    return Skipped("salvation_locked");
                if (!await w.Has("EvilCastleEnterUI") && !await w.Has("RogueLikeLobbyUI"))
                {
                    await w.Step("GameFieldDefaultUI", operation: "salvation_walk", value: 1, reason: "走入恶魔城入口");
                    try { await w.Wait(f => DailyNavigationDecision.Types(f.Frame).Contains("EvilCastleEnterUI"), 35, "救赎之塔入口未触发"); }
                    finally { if (await w.Has("GameFieldDefaultUI") && !await w.Has("EvilCastleEnterUI")) await w.Step("GameFieldDefaultUI", operation: "salvation_walk", value: 0); }
                }
            }
            if (!await w.Has("RogueLikeLobbyUI"))
            {
                var eligibility = StateOf(await w.Evidence(Prefixes));
                if (!B(eligibility["Unlocked"]) || !B(eligibility["Season"])) return await Finish(w, Skipped(!B(eligibility["Unlocked"]) ? "salvation_locked" : "salvation_closed"));
                await w.Step("EvilCastleEnterUI", "_objRogueLikeButton", expect: "RogueLikeLobbyUI");
            }
        }
        var state = StateOf(await w.Evidence(Prefixes));
        await DailyNativeBattleRecovery.Replan(w, "salvation.sweep");
        var plan = Plan(state, w.Settings.Salvation.ReserveKeys, w.Settings.Salvation.UseFixedCount ? w.Settings.Salvation.SweepCount : 0);
        if (S(plan["state"]) != "ready") return await Finish(w, plan);
        foreach (int value in new[] { 0, 1 })
            await w.Step(O(("ui", "RogueLikeLobbyUI"), ("operation", "salvation_select"), ("items", new JsonArray(Copy(plan["level"]))), ("value", value)));
        await w.WaitEvidence(Prefixes, e => N(StateOf(e)["Selected"]) == N(plan["level"]));
        await w.Step(O(("ui", "RogueLikeLobbyUI"), ("operation", "salvation_popup"), ("items", new JsonArray(Copy(plan["level"]))), ("value", 0), ("expect", "RogueLikeSkipPopupUI")));
        if (!B(StateOf(await w.Evidence(Prefixes))["FreeOnly"]))
            await w.Step("RogueLikeSkipPopupUI", "_buttonFreeOnly._objectRoot");
        await w.WaitEvidence(Prefixes, e => B(StateOf(e)["FreeOnly"]));
        await w.Step("RogueLikeSkipPopupUI", "_sliderAutoCount._objMinusMaxButton");
        await w.WaitEvidence(Prefixes, e => N(StateOf(e)["Count"]) == 1);
        for (int count = 2; count <= N(plan["count"]); count++)
        {
            await w.Step("RogueLikeSkipPopupUI", "_sliderAutoCount._objPlusButton");
            await w.WaitEvidence(Prefixes, e => N(StateOf(e)["Count"]) == count);
        }
        var items = new JsonArray(Copy(plan["level"]), Copy(plan["reserve"]), Copy(plan["free"]), Copy(plan["stones"]), Copy(plan["remainingCost"]));
        if (I(plan["fixedCount"]) > 0) items.Add(Copy(plan["fixedCount"]));
        var op = await w.Transact("salvation.sweep", plan, O(("ui", "RogueLikeSkipPopupUI"), ("operation", "salvation_confirm"),
            ("items", items), ("value", plan["count"])), 60);
        await w.Wait(f => DailyNavigationDecision.Types(f.Frame).Contains("RogueLikeTotalScoreUI"), 30);
        await w.Step("RogueLikeTotalScoreUI", "_objBackButton", absent: "RogueLikeTotalScoreUI");
        return await Finish(w, O(("state", "completed"), ("result", op["result"])));
    }
    private static async Task<JsonObject> Finish(DailyWorkflow w, JsonObject result)
    {
        foreach (string ui in new[] { "RogueLikeLobbyUI", "EvilCastleEnterUI" })
            if (await w.Has(ui)) await w.Step(ui, back: true, absent: ui);
        return result;
    }
}

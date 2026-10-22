using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Run the game's native consecutive-stage mode; the game owns all combat decisions.</summary>
public static class DailyEventNative
{
    public static readonly string[] Prefixes = ["event.native", "event.currency", "event.sweep"];
    public static JsonObject Native(JsonObject e) => State(e, "event.native", "$self");
    public static JsonObject? Plan(JsonObject lobby, long free, int maximum = 5)
    {
        Require(maximum is >= 1 and <= 5, "Invalid event auto budget");
        Require(free >= 0, "Event free attempts unavailable");
        if (free == 0) return null;
        if (!B(lobby["Eligible"])) return null;
        var rows = Rows(lobby["Stages"]).OrderBy(r => N(r["Id"])).ToArray();
        var frontier = rows.FirstOrDefault(r => DailyEventStageData.Progress(lobby, r) == null);
        if (frontier == null) return null;
        Require(N(frontier["Id"]) == N(lobby["Latest"]), "Event native frontier differs");
        long cost = N(DailyEventStageData.Tables(lobby, frontier).Table["eventApCount"]);
        Require(cost > 0, "Event auto AP cost unavailable");
        var tail = rows.SkipWhile(r => r != frontier).Take(maximum).ToArray();
        int count = 0;
        foreach (var row in tail)
        {
            // The native count selector assumes one price for its batch.
            if (N(row["Id"]) != N(frontier["Id"]) + count || N(DailyEventStageData.Tables(lobby, row).Table["eventApCount"]) != cost || (count + 1) * cost > free) break;
            count++;
        }
        return count == 0 ? null : O(("event", lobby["Event"]), ("group", lobby["Group"]), ("stage", frontier["Id"]), ("count", count), ("cost", cost), ("category", lobby["Category"]));
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject(); var scope = op["scope"]!.AsObject(); var state = Native(after);
        Identity(before, after);
        bool exhausted = N(R(after, "event.currency", "EventApFree")) == 0;
        Require(B(state["Result"]) && !B(state["Continuing"]) && (exhausted || N(state["Total"]) == N(scope["count"])), "Event auto batch has not finished");
        // The game's terminal batch state is authoritative. A long native batch
        // can outlive the bounded receipt buffer; missing receipts must not keep
        // an already finished fight (especially zero remaining free AP) pending.
        int count = B(state["Lost"]) ? Math.Clamp(I(state["Current"]), 1, I(scope["count"])) : I(scope["count"]);
        long spent = N(R(before, "event.currency", "EventApFree")) - N(R(after, "event.currency", "EventApFree"));
        return O(("count", count), ("lost", state["Lost"]), ("spent", spent), ("completion_source", "current_game_result"),
            ("free_exhausted", exhausted),
            ("currency_changed_unexpectedly", spent < 0 || spent > count * N(scope["cost"]) ||
                !JsonNode.DeepEquals(R(before, "event.currency", "EventApStack"), R(after, "event.currency", "EventApStack"))));
    }
    public static DailyBusinessProof Definition() => new("event.native_batch", "event_battle", Prefixes, Verify, [], CanResume: DailyNativeBattleRecovery.CanResume);
    public static async Task WaitBatch(DailyWorkflow w)
    {
        await w.WaitEvidence(Prefixes, e => B(Native(e)["Result"]) && !B(Native(e)["Continuing"]), 900, "活动自动战斗尚未结束；保留当前战斗，未重复开始。");
    }
    private static async Task<JsonObject> Lobby(DailyWorkflow w, bool challenge)
    {
        string category = challenge ? "Challenge" : "Battle";
        if (await w.Has("EventBattleUI"))
        {
            var lobby = DailyEventSweep.Lobby(await w.Evidence(DailyEventSweep.Prefixes));
            if (S(lobby["Category"]) == category) return lobby;
            await w.Step("EventBattleUI", back: true, expect: "EventMainUI");
        }
        if (!await w.Has("EventMainUI")) await DailyEventSweep.EnterMain(w);
        string field = challenge ? "_buttonChallenge._button" : "_buttonBattle._button";
        await w.Target("EventMainUI", field);
        await w.Step("EventMainUI", field, expect: "EventBattleUI");
        return DailyEventSweep.Lobby(await w.Evidence(DailyEventSweep.Prefixes));
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.EventBattle.Enabled) return DailyWorkflow.Skipped("disabled");
        var results = new JsonArray(); int used = 0; bool failed = false;
        var frame = (await w.Observe()).Frame;
        if (DailyNavigationDecision.Types(frame).Overlaps(new[] { "BattleUI_EventBattle", "BattleResultUI" }))
        {
            var owned = w.Business.Records(w.Context, "event.native_batch").LastOrDefault(op =>
                JsonNode.DeepEquals(op["cycle"], w.Context["cycle"]) && DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), frame) && (DailyManagedBusiness.Pending(op) || S(op["state"]) == "completed"));
            if (owned == null)
            {
                var current = await w.Evidence(Prefixes); var native = Native(current);
                // A finished event with no free attempts has nothing left to
                // dispatch. Returning from its native result needs no old log.
                if (!B(native["Battle"]) || !B(native["Result"]) || B(native["Continuing"]) || N(R(current, "event.currency", "EventApFree")) != 0)
                    return DailyWorkflow.Skipped("extension_in_progress");
                used = 5; failed = B(native["Lost"]);
            }
            else
            {
                await WaitBatch(w); await w.Business.ReconcileAsync(w.Context);
                owned = w.Business.Records(w.Context, "event.native_batch").Single(x => S(x["id"]) == S(owned["id"]));
                if (DailyManagedBusiness.Pending(owned)) throw new StageHostException("pending", "event_native_result_pending");
                failed = B(Native(await w.Evidence(Prefixes))["Lost"]); used = I(owned["scope"]?["count"]);
            }
            await w.Step("BattleResultUI", operation: "event_native_exit", expect: "EventBattleUI");
        }
        var lobby = await Lobby(w, false);
        await DailyNativeBattleRecovery.Replan(w, "event.native_batch");
        used = Math.Max(used, BudgetUsed(w.Business.Records(w.Context, "event.native_batch"), w.Context, N(lobby["Event"])));
        if (N(R(await w.Evidence(Prefixes), "event.currency", "EventApFree")) == 0)
            return await FinishExhausted(w, results, failed);
        // Normal stages must finish before challenge stages can be attempted.
        if (Rows(lobby["Stages"]).All(r => DailyEventStageData.Progress(lobby, r) != null) && w.Settings.EventBattle.Challenge)
            lobby = await Lobby(w, true);
        for (int batch = 0; batch < 2 && used < 5 && !failed; batch++)
        {
            var e = await w.Evidence(DailyEventSweep.Prefixes);
            var plan = Plan(lobby, N(R(e, "event.currency", "EventApFree")), 5 - used);
            if (plan == null) break;
            foreach (int value in new[] { 0, 1 })
                await w.Step(O(("ui", "EventBattleUI"), ("operation", "event_native_select"), ("items", new JsonArray(Copy(plan["event"]), Copy(plan["group"]), Copy(plan["stage"]))), ("value", value)));
            await w.WaitEvidence(DailyEventSweep.Prefixes, x => N(DailyEventSweep.Lobby(x)["Selected"]) == N(plan["stage"]));
            var op = await DailyEventPreview.Run(w, "_buttonAutoBattle._button", () => SetCount(w, I(plan["count"])),
                evidence => w.TransactObserved("event.native_batch", plan, O(("ui", "BattleSkipPopupUI"), ("operation", "event_native_confirm"),
                    ("items", new JsonArray(Copy(plan["event"]), Copy(plan["group"]), Copy(plan["stage"]))), ("value", plan["count"])), evidence, 30, drive: () => WaitBatch(w)));
            results.Add(Copy(op["result"])); used += I(op["result"]?["count"]); failed = B(op["result"]?["lost"]);
            await w.Step("BattleResultUI", operation: "event_native_exit", expect: "EventBattleUI");
            var settled = await w.Evidence(DailyEventSweep.Prefixes);
            if (N(R(settled, "event.currency", "EventApFree")) == 0)
                return await FinishExhausted(w, results, failed);
            lobby = DailyEventSweep.Lobby(settled);
            if (!failed && S(lobby["Category"]) == "Battle" && Rows(lobby["Stages"]).All(r => DailyEventStageData.Progress(lobby, r) != null) && w.Settings.EventBattle.Challenge)
                lobby = await Lobby(w, true);
            else break;
        }
        if (failed && !w.Settings.EventBattle.SweepAfterFailure)
        {
            await DailyEventSweep.Leave(w);
            return O(("state", "partial"), ("reason", "event_auto_failed_stop"), ("batches", results));
        }
        // Sweep the highest fully cleared stage; a failed fight does not authorize a retry.
        var target = DailyEventSweep.Highest(lobby);
        if (target == null && S(lobby["Category"]) == "Challenge") { lobby = await Lobby(w, false); target = DailyEventSweep.Highest(lobby); }
        JsonObject sweep;
        if (target != null)
        {
            target["policy"] = DailyEventSweep.NativePolicy;
            sweep = await DailyEventSweep.Run(w, lobby, target, 5);
        }
        else sweep = DailyWorkflow.Skipped("no_fully_cleared_sweep_stage");
        await DailyEventSweep.Leave(w);
        bool swept = S(sweep["state"]) == "completed";
        return O(("state", failed && !swept ? "partial" : results.Count > 0 || swept ? "completed" : "skipped"), ("reason", failed ? swept ? "event_auto_failed_swept" : "event_auto_failed_no_sweep" : "event_native_finished"), ("batches", results), ("sweep", sweep));
    }
    private static async Task<JsonObject> FinishExhausted(DailyWorkflow w, JsonArray results, bool failed)
    {
        await DailyEventSweep.Leave(w);
        // No further target lookup or challenge navigation can produce free work.
        // Keep a failed battle visible in the summary without blocking the queue.
        return O(("state", "completed"), ("reason", failed ? "event_auto_failed_no_sweep" : "free_attempts_exhausted"), ("batches", results));
    }
    internal static int BudgetUsed(IEnumerable<JsonObject> records, JsonObject context, long eventId) => (int)Math.Min(5, records.Where(op =>
        JsonNode.DeepEquals(op["cycle"], context["cycle"]) && N(op["scope"]?["event"]) == eventId && S(op["state"]) is "completed" or "superseded")
        .Sum(op => S(op["state"]) == "completed" ? N(op["result"]?["count"]) : N(op["scope"]?["count"])));
    private static async Task SetCount(DailyWorkflow w, int count)
    {
        if (!B(R(await w.Evidence(Prefixes), "event.sweep.popup", "_buttonFreeOnly.IsEnable")))
            await w.Step("BattleSkipPopupUI", "_buttonFreeOnly._objectRoot");
        await DailyEventPreview.WaitValue(w, Prefixes, e => B(R(e, "event.sweep.popup", "_buttonFreeOnly.IsEnable")));
        await w.Step("BattleSkipPopupUI", "_sliderAutoCount._objMinusMaxButton");
        await DailyEventPreview.WaitValue(w, Prefixes, e => N(R(e, "event.sweep.popup", DailyEventSweep.Count)) == 1);
        for (int i = 2; i <= count; i++)
        {
            await w.Step("BattleSkipPopupUI", "_sliderAutoCount._objPlusButton");
            await DailyEventPreview.WaitValue(w, Prefixes, e => N(R(e, "event.sweep.popup", DailyEventSweep.Count)) == i);
        }
    }
}

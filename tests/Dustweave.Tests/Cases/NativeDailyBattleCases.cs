using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class NativeDailyBattleCases
{
    public static void Run(List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        void Reject(Action run, string name) { try { run(); } catch (InvalidDataException) { checks.Add(name); return; } throw new Exception(name); }
        string Exit(bool inEvent, bool continuing, params string[] visible) => BD2Daily.Live.EventBattleResultRules.ExitField(inEvent, continuing, visible.Contains);
        Check(Exit(true, false, "_winObject", "_goWinSafeArea") == "_goWinSafeArea", "Event victory returns through its actual safe-area control without WinExit");
        Check(Exit(true, false, "_winObject", "_goWinSafeArea", "_objectWinExitButton") == "_goWinSafeArea", "Event return control takes priority over generic win layout");
        Check(Exit(true, false, "_winObject", "_objectWinExitButton") == "_objectWinExitButton", "Generic event result layout still supported");
        Check(Exit(true, false, "_loseObject", "_objectLoseExitButton") == "_objectLoseExitButton", "Defeat uses loss exit instead of retry");
        Check(Exit(true, false, "_loseObject", "_goLoseSafeArea") == "_goLoseSafeArea", "Loss safe-area variant supported");
        Check(Exit(true, true, "_winObject", "_goWinSafeArea") == "", "Visible intermediate win cannot interrupt native continuation");
        Check(Exit(false, false, "_winObject", "_goWinSafeArea") == "", "Foreign battle result is never claimed as event completion");
        Check(Exit(true, false, "_winObject", "_objEventBattleNextBattleButton") == "", "Next-stage button alone never authorizes exiting or advancing");
        Check(Exit(true, false, "_winObject", "_loseObject", "_goWinSafeArea") == "", "Overlapping result panes wait for native transition");
        Check(Exit(true, false, "_goWinSafeArea") == "", "An exit outside a visible result pane does not finish battle");
        var p = new DailyPreferences();
        Check(!p.Salvation.Enabled && p.Salvation.ReserveKeys == 0 && p.EventBattle.SweepAfterFailure, "New battle defaults are opt-in tower and sweep after defeat");
        var legacy = JsonSerializer.SerializeToNode(p)!.AsObject(); legacy.Remove("Salvation"); legacy["EventBattle"]!.AsObject().Remove("SweepAfterFailure");
        var restored = DailyPreferences.Parse(legacy.ToJsonString());
        Check(!restored.Salvation.Enabled && restored.EventBattle.SweepAfterFailure, "Existing settings receive safe defaults without changing enabled stages");
        p.Salvation.ReserveKeys = -1; Reject(p.Validate, "Negative key reserve rejected");
        p.Salvation.ReserveKeys = 1000; Reject(p.Validate, "Oversized key reserve rejected");
        p.Salvation.ReserveKeys = 3; p.Salvation.Enabled = true; p.EventBattle.SweepAfterFailure = false;
        restored = DailyPreferences.Parse(JsonSerializer.Serialize(p));
        Check(restored.Salvation.Enabled && restored.Salvation.ReserveKeys == 3 && !restored.EventBattle.SweepAfterFailure, "Account settings round trip both options");
        Check(!restored.Salvation.UseFixedCount && restored.Salvation.SweepCount == 1, "Existing tower settings keep growth mode and a one-run fixed default");
        var previousTower = JsonSerializer.SerializeToNode(p)!.AsObject();
        previousTower["Salvation"]!.AsObject().Remove("UseFixedCount"); previousTower["Salvation"]!.AsObject().Remove("SweepCount");
        restored = DailyPreferences.Parse(previousTower.ToJsonString());
        Check(restored.Salvation.ReserveKeys == 3 && !restored.Salvation.UseFixedCount && restored.Salvation.SweepCount == 1, "Tower mode migration preserves the existing key reserve");
        p.Salvation.UseFixedCount = true; p.Salvation.SweepCount = 4;
        restored = DailyPreferences.Parse(JsonSerializer.Serialize(p));
        Check(restored.Salvation.UseFixedCount && restored.Salvation.SweepCount == 4, "Fixed tower policy persists for the account");
        p.Salvation.SweepCount = 0; Reject(p.Validate, "Zero fixed tower count rejected");
        p.Salvation.SweepCount = 1000; Reject(p.Validate, "Oversized fixed tower count rejected"); p.Salvation.SweepCount = 4;
        Check(DailyStageCatalog.Enabled("salvation", restored) && DailyStageCatalog.Name("event_battle") == "活动战斗", "Both native battle stages are selectable without a plugin");

        var s = O(("Ready", true), ("Idle", true), ("Unlocked", true), ("Season", true), ("Level", 12), ("Free", 6), ("Paid", 99), ("Stones", 80), ("RemainingCost", 100), ("Reward", 9), ("Cost", 1));
        Check(N(DailySalvation.Plan(s, 0)["count"]) == 3, "Tower rounds up only the remaining stone deficit");
        Check(N(DailySalvation.Plan(s, 4)["count"]) == 2, "Tower preserves requested free keys");
        Check(S(DailySalvation.Plan(s, 8)["reason"]) == "salvation_keys_reserved", "Reserve above inventory skips without paid tickets");
        s["Cost"] = 2; Check(N(DailySalvation.Plan(s, 1)["count"]) == 2, "Tower count accounts for multi-key sweep cost"); s["Cost"] = 1;
        s["Stones"] = 100; Check(S(DailySalvation.Plan(s, 0)["reason"]) == "salvation_growth_funded", "Unspent stones already sufficient prevents farming");
        Check(N(DailySalvation.Plan(s, 0, 5)["count"]) == 5, "Fixed tower count ignores sufficient saved growth stones");
        s["RemainingCost"] = 0;
        Check(N(DailySalvation.Plan(s, 0, 5)["count"]) == 5, "Fixed tower count runs after all upgrades are maxed");
        Check(N(DailySalvation.Plan(s, 4, 5)["count"]) == 2, "Fixed tower count still respects retained daily keys");
        Check(S(DailySalvation.Plan(s, 6, 5)["reason"]) == "salvation_keys_reserved", "No daily keys never falls back to stored paid keys");
        s["Cost"] = 2;
        Check(N(DailySalvation.Plan(s, 1, 5)["count"]) == 2, "Fixed tower count is clamped to multi-key costs"); s["Cost"] = 1;
        s["RemainingCost"] = 100; s["Stones"] = 99;
        Check(N(DailySalvation.Plan(s, 0, 5)["count"]) == 5 && N(DailySalvation.Plan(s, 0)["count"]) == 1, "Fixed count is not shortened by a one-run growth deficit");
        Reject(() => DailySalvation.Plan(s, 0, -1), "Negative fixed count rejected");
        Reject(() => DailySalvation.Plan(s, 0, 1000), "Unbounded fixed count rejected");
        s["RemainingCost"] = 0; Check(S(DailySalvation.Plan(s, 0)["reason"]) == "salvation_growth_funded", "Maxed growth skips"); s["RemainingCost"] = 100; s["Stones"] = 80;
        foreach (var pair in new[] { ("Idle", "salvation_run_in_progress"), ("Ready", "salvation_not_ready"), ("Unlocked", "salvation_locked"), ("Season", "salvation_closed") })
        {
            s[pair.Item1] = false; Check(S(DailySalvation.Plan(s, 0)["reason"]) == pair.Item2, "Tower protects " + pair.Item1); s[pair.Item1] = true;
        }
        s["Level"] = 0; Check(S(DailySalvation.Plan(s, 0)["reason"]) == "salvation_no_cleared_level", "Never selects an uncleared tower level"); s["Level"] = 12;
        s["Reward"] = 0; Reject(() => DailySalvation.Plan(s, 0), "Unknown tower reward cannot consume keys");

        var lobby = EventTradeBoundaryCases.Lobby();
        var plan = DailyEventNative.Plan(lobby, 5)!;
        Check(N(plan["stage"]) == 2 && N(plan["count"]) == 1, "Native battle starts at the first uncleared stage and caps at the final stage");
        Check(DailyEventNative.Plan(lobby, 0) == null, "No free AP never starts a native batch");
        lobby["Stages"] = new JsonArray(Enumerable.Range(1, 10).Select(i => (JsonNode)EventTradeBoundaryCases.Stage(i, i == 1)).ToArray());
        Check(N(DailyEventNative.Plan(lobby, 20)!["count"]) == 5, "Native batch cannot exceed five even with stored AP");
        Check(N(DailyEventNative.Plan(lobby, 20, 2)!["count"]) == 2, "Second difficulty receives only remaining batch budget");
        Check(N(DailyEventNative.Plan(lobby, 3)!["count"]) == 3, "Native batch respects free AP");
        lobby["Latest"] = 3; Reject(() => DailyEventNative.Plan(lobby, 5), "Stale native frontier is rejected before consuming AP"); lobby["Latest"] = 2;
        var table = JsonNode.Parse(S(lobby["Stages"]![3]!["Table"]))!.AsObject(); table["eventApCount"] = 2; lobby["Stages"]![3]!["Table"] = table.ToJsonString();
        Check(N(DailyEventNative.Plan(lobby, 10)!["count"]) == 2, "A changed price breaks the batch before a higher-priced stage");
        lobby["Eligible"] = false; Check(DailyEventNative.Plan(lobby, 5) == null, "Locked event group never starts");
        Reject(() => DailyEventNative.Plan(lobby, 5, 6), "Oversized native batch request rejected");

        JsonObject frame = O(("ProcessId", 7), ("ProcessStartTicks", 8), ("Instance", "one"), ("AccountKey", "a"), ("PlayerKey", "p"));
        JsonObject Value(string path, JsonNode? value) => O(("Path", path), ("Error", ""), ("Json", value?.ToJsonString() ?? "null"));
        JsonObject Read(string id, params (string Key, object? Value)[] fields) => O(("Id", id), ("Error", ""), ("Values", new JsonArray(O(fields).Select(p => (JsonNode)Value(p.Key, p.Value)).ToArray())));
        JsonObject Evidence(params JsonObject[] readings) => O(("Frame", frame), ("Config", "test"), ("Readings", new JsonArray(readings.Cast<JsonNode>().ToArray())));
        JsonObject Event(string role, string kind, int seq, params (string Key, object? Value)[] fields) => O(("Role", role), ("Kind", kind), ("Sequence", seq), ("Frame", frame), ("Error", ""), ("ErrorCode", 0), ("Accepted", true),
            ("Values", new JsonArray(O(fields).Select(p => (JsonNode)Value(p.Key, p.Value)).ToArray())));
        var nativePlan = O(("event", 100), ("group", 10), ("stage", 2), ("count", 2), ("cost", 1), ("category", "Battle"));
        var eventBefore = Evidence(Read("event.currency", ("EventApFree", 5), ("EventApStack", 10)));
        JsonObject EventAfter(int free, int paid, bool lost = false, bool continuing = false) => Evidence(Read("event.currency", ("EventApFree", free), ("EventApStack", paid)),
            Read("event.native", ("$self", O(("Result", true), ("Lost", lost), ("Continuing", continuing), ("Total", 2)))));
        var eventOp = O(("before", eventBefore), ("scope", nativePlan));
        var eventReplies = new JsonArray(Event("event.end", "request", 1), Event("event.end", "response", 2, ("PackEventBattleInfo", O(("eventUid", 100), ("groupId", 10), ("id", 2)))),
            Event("event.end", "request", 3), Event("event.end", "response", 4, ("PackEventBattleInfo", O(("eventUid", 100), ("groupId", 10), ("id", 3)))));
        Check(N(DailyEventNative.Verify(eventOp, eventReplies, EventAfter(3, 10))["count"]) == 2, "Native auto settles ordered server progress and free AP debit");
        Reject(() => DailyEventNative.Verify(eventOp, eventReplies, EventAfter(3, 9)), "Stored AP change cannot be confirmed as free auto play");
        Reject(() => DailyEventNative.Verify(eventOp, eventReplies, EventAfter(5, 10)), "Winning receipts without their AP debit are not accepted");
        Reject(() => DailyEventNative.Verify(eventOp, eventReplies, EventAfter(3, 10, continuing: true)), "Intermediate auto result cannot finish batch");
        Check(DailyEventNative.Definition().EventRoles.Contains("event.end"), "Native batch subscribes to actual game result role");
        var defeat = new JsonArray(Event("event.end", "request", 1), Event("event.end", "response", 2, ("PackEventBattleInfo", null)));
        Check(B(DailyEventNative.Verify(eventOp, defeat, EventAfter(5, 10, lost: true))["lost"]), "Native defeat can finish early without fabricated clear progress");

        s["Reward"] = 9; s["Popup"] = true; s["FreeOnly"] = true; s["Count"] = 3; s["PopupLevel"] = 12; s["PopupCost"] = 1;
        var towerPlan = DailySalvation.Plan(s, 0);
        var towerBefore = Evidence(Read("salvation.native", ("$self", s)));
        var towerAfterState = s.DeepClone().AsObject(); towerAfterState["Free"] = 3; towerAfterState["Stones"] = 107; towerAfterState["Popup"] = false;
        var towerOp = O(("before", towerBefore), ("scope", towerPlan));
        var towerEvents = new JsonArray(Event("salvation.sweep", "request", 1), Event("salvation.sweep", "response", 2, ("ScoreInfo", O(("obsidian", 27)))));
        Check(N(DailySalvation.Verify(towerOp, towerEvents, Evidence(Read("salvation.native", ("$self", towerAfterState))))["count"]) == 3, "Tower settles native score and free key changes");
        towerAfterState["Paid"] = 98; Reject(() => DailySalvation.Verify(towerOp, towerEvents, Evidence(Read("salvation.native", ("$self", towerAfterState)))), "Tower refuses any paid key debit");
        s["RemainingCost"] = 0; s["Stones"] = 100;
        var fixedPlan = DailySalvation.Plan(s, 3, 5);
        var fixedOp = O(("before", Evidence(Read("salvation.native", ("$self", s)))), ("scope", fixedPlan));
        var fixedAfter = s.DeepClone().AsObject(); fixedAfter["Free"] = 3; fixedAfter["Stones"] = 127; fixedAfter["Popup"] = false;
        Check(N(DailySalvation.Verify(fixedOp, towerEvents, Evidence(Read("salvation.native", ("$self", fixedAfter))))["count"]) == 3, "Fixed sweep confirms actual limited count with growth already maxed");
        fixedOp["scope"]!["fixedCount"] = 2;
        Reject(() => DailySalvation.Verify(fixedOp, towerEvents, Evidence(Read("salvation.native", ("$self", fixedAfter)))), "Fixed mode receipt cannot confirm more runs than requested");
        var cycle = O(("day", "today")); var context = O(("cycle", cycle));
        var history = new[] { O(("cycle", cycle), ("scope", nativePlan), ("state", "superseded")) };
        Check(DailyEventNative.BudgetUsed(history, context, 100) == 2 && DailyEventNative.BudgetUsed(history, context, 101) == 0, "Uncertain interrupted auto reserves its budget without blocking a fresh lobby");
        var resumeContext = O(("actor", new JsonArray(7, 8, "one", "a", "p")), ("server", "server"), ("cycle", cycle));
        var resumeOp = O(("role", "event.native_batch"), ("account", "a"), ("player", "p"), ("server", "server"), ("cycle", cycle), ("before", eventBefore), ("state", "completed"));
        var liveFrame = frame.DeepClone().AsObject();
        liveFrame["Surfaces"] = new JsonArray(HomeNavigationCases.Surface("BattleUI_EventBattle"), HomeNavigationCases.Surface("BattleResultUI"));
        Check(DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "Owned native result can be exited after restart of the controller");
        liveFrame["Surfaces"] = new JsonArray(HomeNavigationCases.Surface("BattleResultUI"));
        Check(DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "Owned terminal result resumes after the event HUD disappears");
        liveFrame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI_PVP"));
        Check(!DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "A later foreign battle cannot inherit the event result record");
        liveFrame["Surfaces"] = new JsonArray(HomeNavigationCases.Surface("BattleResultUI"));
        liveFrame["ProcessId"] = 99;
        Check(!DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "A different game process cannot inherit an active native battle");
        liveFrame["Surfaces"] = new JsonArray(HomeNavigationCases.Surface("EventBattleUI"));
        Check(DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "Fresh idle lobby permits account-scoped replanning after a game restart");
        liveFrame["PlayerKey"] = "other";
        Check(!DailyNativeBattleRecovery.CanResume(resumeOp, new(liveFrame, resumeContext)), "Replanning never crosses player identity");
    }
}

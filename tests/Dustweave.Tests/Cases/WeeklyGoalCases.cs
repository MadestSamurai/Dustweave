using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
static class WeeklyGoalCases
{
    // The parent suite still validates the current game table; the independent suite uses explicit synthetic definitions.
    public static void Run(List<string> cases) => Run(cases,
        Rows(JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.SourceRoot, "assets/rules/daily/policy/MissionTable.json")))));

    public static void RunSynthetic(List<string> cases)
    {
        JsonObject Definition(long id, int type, int subtype, int required) => O(
            ("id", id), ("conditionType", type), ("conditionSubType", subtype), ("conditionValue", required),
            ("groupType", 1), ("unlockQuestId", 0), ("unlockPackId", 0));
        Run(cases, [Definition(226, 999, 0, 1), Definition(229, 343, 0, 1),
            Definition(208, 6, 9, 1), Definition(218, 280, 0, 1),
            Definition(225, 36, 0, 3), Definition(60041, 341, 0, 1)]);
    }

    private static void Run(List<string> cases, JsonObject[] tables)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        JsonObject E(params JsonObject[] rows) => WorkflowCases.Evidence(WorkflowCases.Reading("missions.cache", ("Count", rows.Length), ("_items", Array(rows))));
        JsonObject Row(long id, long progress, bool claimed = false) => O(("id", id), ("value", progress), ("isComplete", claimed));
        var evidence = E(Row(226, 1, true), Row(229, 0));
        var pending = DailyWeeklyMission.Progress(evidence, DailyWeeklyMission.MiniGame, tables);
        Check(N(pending["id"]) == 229 && !B(pending["complete"]), "arcade menu claimed does not complete playing a minigame");
        Check(!B(DailyWeeklyMission.Progress(E(Row(226, 1)), DailyWeeklyMission.MiniGame, tables)["complete"]), "missing play row is unfinished even when menu mission is done");
        Check(B(DailyWeeklyMission.Progress(E(Row(229, 1)), DailyWeeklyMission.MiniGame, tables)["complete"]), "unclaimed but achieved game mission is complete");
        Check(B(DailyWeeklyMission.Progress(E(Row(229, 0, true)), DailyWeeklyMission.MiniGame, tables)["complete"]), "claimed mission remains complete after counter disappears");
        foreach (var (goal, id, required) in new[] { (DailyWeeklyMission.Equipment, 208L, 1), (DailyWeeklyMission.Book, 218L, 1), (DailyWeeklyMission.Likes, 225L, 3), (DailyWeeklyMission.Fishing, 60041L, 1), (DailyWeeklyMission.MiniGame, 229L, 1) })
        {
            var progress = DailyWeeklyMission.Progress(E(Row(id, required - 1)), goal, tables);
            Check(N(progress["id"]) == id && N(progress["required"]) == required && !B(progress["complete"]), "supplied mission definitions resolve weekly goal: " + goal);
        }
        var changed = tables.Select(r => r.DeepClone().AsObject()).ToArray();
        changed.Single(r => N(r["id"]) == 229)["id"] = 990229;
        Check(B(DailyWeeklyMission.Progress(E(Row(990229, 1)), DailyWeeklyMission.MiniGame, changed)["complete"]), "mission renumbering follows semantic goal rather than old id");
        Reject(() => DailyWeeklyMission.Progress(evidence, 226, tables), "menu mission cannot be passed as a weekly play goal");
        Reject(() => DailyWeeklyMission.Progress(E(Row(229, 1), Row(229, 0)), DailyWeeklyMission.MiniGame, tables), "duplicate cache cannot produce completion");
        Reject(() => DailyWeeklyMission.Progress(E(Row(229, -1)), DailyWeeklyMission.MiniGame, tables), "invalid progress cannot produce completion");
        Reject(() => DailyWeeklyMission.Progress(evidence, DailyWeeklyMission.MiniGame, tables.Concat([changed.Single(r => N(r["id"]) == 990229)]).ToArray()), "ambiguous play definitions require diagnosis");
        Reject(() => DailyWeeklyMission.Skipped("done", pending), "unfinished task cannot be recorded as skipped complete");
        JsonObject Item(string state, JsonObject result) => O(("task", DailyWeeklyMission.MiniGame), ("state", state), ("result", result), ("carried_forward", true));
        var oldResult = O(("state", "skipped"), ("reason", "weekly_minigame_complete"));
        var stale = Item("skipped", oldResult);
        var record = O(("items", new JsonArray(stale)));
        DailyWeeklyMission.RecheckLegacy(record);
        stale = record["items"]![0]!.AsObject();
        Check(S(stale["state"]) == "partial" && !B(stale["carried_forward"]) && JsonNode.DeepEquals(stale["result"], oldResult), "legacy false skip becomes retryable without discarding historical proof");
        var wrong = Item("completed", O(("mission", O(("id", 226), ("complete", true)))));
        Check(DailyWeeklyMission.NeedsRecheck(wrong), "legacy completion using arcade browsing proof is invalidated");
        var proof = DailyWeeklyMission.Progress(E(Row(229, 1)), DailyWeeklyMission.MiniGame, tables);
        Check(!DailyWeeklyMission.NeedsRecheck(Item("skipped", DailyWeeklyMission.Skipped("done", proof))), "correct proof remains terminal");
        Check(!DailyWeeklyMission.NeedsRecheck(Item("skipped", O(("reason", "disabled")))), "disabled mini game is not re-enabled by migration");
        Check(!DailyWeeklyMission.NeedsRecheck(Item("recovery_required", oldResult)), "unresolved operation is not converted into replayable skip");
    }
}

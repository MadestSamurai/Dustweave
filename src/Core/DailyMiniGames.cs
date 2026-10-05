using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailyMiniGames
{
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("minigames.quiz_clear", "event_rewards", ["minigames", "reward.presentation"], (op, events, after) => DailyQuizRecovery.Verify(op["before"]!.AsObject(), events, after, new JsonArray(Copy(op["scope"]!["event"]), Copy(op["scope"]!["group"]), Copy(op["scope"]!["quiz"]))), AffectedStages: ["event_rewards", "rewards"]);
        yield return new("rewards.dice", "event_rewards", DailyEventRewards.Prefixes, (op, events, after) => VerifyDice(op["before"]!.AsObject(), events, after), AffectedStages: ["event_rewards", "rewards"]);
    }
    public static JsonObject[] Pending(JsonObject p) => Rows(p["Quizzes"]).Where(q => B(p["Available"]) && B(q["Open"]) && !B(q["Complete"])).ToArray();
    private static async Task DriveQuiz(DailyWorkflow w, JsonArray key)
    {
        double end = w.Time + 180, progress = w.Time;
        string? previous = null, sent = null;
        int actions = 0;
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            var page = DailyQuizRecovery.Page(await w.Evidence("minigames", "reward.presentation"));
            Require(JsonNode.DeepEquals(page["EventUid"], key[0]), "Quiz event changed");
            if (Rows(page["Quizzes"]).Any(q => JsonNode.DeepEquals(q["Group"], key[1]) && JsonNode.DeepEquals(q["Id"], key[2]) && B(q["Complete"])))
                return;
            string signature = DailyQuizRecovery.Progress(page);
            if (previous != signature)
            {
                previous = signature;
                progress = w.Time;
            }
            if (B(page["Playing"]))
            {
                Require(JsonNode.DeepEquals(page["Group"], key[1]) && JsonNode.DeepEquals(page["QuizId"], key[2]), "Another quiz active");
                var ui = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == "BalloonScriptUI");
                if (ui != null && DailyNavigationDecision.ReadyInput(ui, false) && DailyNavigationDecision.Blockers(frame, "BalloonScriptUI", DailyNavigationPolicy.Load()).Length == 0 && sent != signature && (B(page["Choosing"]) || B(page["Touch"])))
                {
                    Require(actions < 80, "Quiz step budget exceeded");
                    await w.Step(O(("ui", "BalloonScriptUI"), ("operation", "quiz_talk"), ("items", key), ("value", B(page["Choosing"]) ? DailyQuizRecovery.AnswerId(page) : 0)));
                    sent = signature;
                    actions++;
                }
            }
            w.Save("quiz-progress.json", O(("state", "running"), ("quiz", key), ("actions", actions), ("cursor", page["Cursor"]), ("at", w.Driver.UtcTicks)));
            Require(w.Time - progress < 45, "Quiz did not progress; original operation retained");
            await w.Delay(200);
        }
        throw new StageHostException("pending", "问答未结束，保留原操作。");
    }
    public static async Task<JsonObject> Quizzes(DailyWorkflow w)
    {
        var p = DailyQuizRecovery.Page(await w.Evidence("minigames"));
        var operations = new JsonArray();
        if (B(p["Playing"]))
        {
            var r = await w.Driver.HomeRecoveryAsync("quiz");
            operations.Add(Copy(r["operation"]));
            p = DailyQuizRecovery.Page(await w.Evidence("minigames"));
        }
        if (Pending(p).Length == 0)
        {
            if (await w.Has("MiniEventQuizUI"))
                await FinishQuiz(w);
            return O(("state", "completed"), ("actions", operations.Count), ("operations", operations));
        }
        if (!await w.Has("MiniEventQuizUI"))
        {
            if (!await w.Has("MiniEventMainUI"))
            {
                await w.Home("event_rewards");
                await w.Step("MenuUI", operation: "quiz_hub", value: I(p["HubUid"]), expect: "MiniEventMainUI");
            }
            await w.Step("MiniEventMainUI", operation: "quiz_list", value: I(p["HubUid"]), expect: "MiniEventQuizUI");
        }
        bool done = false;
        for (int i = 0; i < 100; i++)
        {
            p = DailyQuizRecovery.Page(await w.Evidence("minigames"));
            var q = Pending(p).OrderBy(q => N(q["OpenDay"])).ThenBy(q => N(q["Group"])).ThenBy(q => N(q["Id"])).FirstOrDefault();
            if (q == null)
            {
                done = true;
                break;
            }
            var key = new JsonArray(Copy(p["EventUid"]), Copy(q["Group"]), Copy(q["Id"]));
            var op = await w.Transact("minigames.quiz_clear", O(("event", key[0]), ("group", key[1]), ("quiz", key[2])), O(("ui", "MiniEventQuizUI"), ("operation", "quiz_play"), ("items", key)), 40, () => DriveQuiz(w, key));
            operations.Add(Copy(op["id"]));
            await w.Dismiss("MiniEventQuizUI", true);
        }
        Require(done, "Quiz budget exceeded");
        await FinishQuiz(w);
        var result = O(("state", "completed"), ("actions", operations.Count), ("operations", operations), ("engine", "dotnet"));
        w.Save("quiz-latest.json", result);
        return result;
    }
    private static async Task FinishQuiz(DailyWorkflow w)
    {
        await w.Dismiss("MiniEventQuizUI");
        await w.Step("MiniEventQuizUI", back: true, absent: "MiniEventQuizUI");
        await w.Step("MiniEventMainUI", operation: "quiz_leave", absent: "MiniEventMainUI");
        await w.Home("event_rewards");
    }
    public static JsonObject VerifyDice(JsonObject before, JsonArray events, JsonObject after)
    {
        Identity(before, after);
        var b = DailyEventRewards.Page(before);
        var a = DailyEventRewards.Page(after);
        Require(JsonNode.DeepEquals(b["TableId"], a["TableId"]) && JsonNode.DeepEquals(b["Schedule"], a["Schedule"]) && !B(a["Auto"]) && B(a["Ready"]), "Dice still active or scope changed");
        var es = Rows(events).Where(e => S(e["Role"]) == "rewards.dice").OrderBy(e => N(e["Sequence"])).ToArray();
        var requests = es.Where(e => S(e["Kind"]) == "request").ToArray();
        var replies = es.Where(e => S(e["Kind"]) == "response").ToArray();
        Require(requests.Length > 0 && requests.Length == replies.Length && requests.Length <= 10000 && es.All(e => DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())), "Dice response chain incomplete");
        for (int i = 0; i < replies.Length; i++)
            Require(N(requests[i]["Sequence"]) < N(replies[i]["Sequence"]) && S(replies[i]["Error"]) == "" && (B(replies[i]["Accepted"]) || N(replies[i]["ErrorCode"]) != 0), "Dice response unreadable");
        var accepted = replies.Where(e => B(e["Accepted"]) && N(e["ErrorCode"]) == 0).Select(DailyEvidence.Values).ToArray();
        var failures = replies.Where(e => N(e["ErrorCode"]) != 0).Select(e => N(e["ErrorCode"])).ToArray();
        long schedule = N(DailyEventRewards.Parse(b["Schedule"])["id"]);
        Require(accepted.All(r => N(r["MiniGameBoardInfo"]?["eventScheduleId"]) == schedule && r["MiniGameControllerInfo"] is JsonNode n && n.ToJsonString() is not ("null" or "[]" or "{}")), "Dice result scope differs");
        long spent = checked(accepted.Length * N(b["Cost"]));
        Require(N(b["Balance"]) - N(a["Balance"]) == spent, "Dice balance differs");
        return O(("throws", accepted.Length), ("spent", spent), ("responses", replies.Length), ("failures", failures), ("rewards", Array(accepted)));
    }
    public sealed class DiceGuard
    {
        private string? prior; private double changed; public void Observe(JsonObject page, JsonArray events, double now)
        {
            string signature = O(("Balance", page["Balance"]), ("Auto", page["Auto"]), ("Ready", page["Ready"]), ("events", Array(Rows(events).Select(e => O(("Kind", e["Kind"]), ("Sequence", e["Sequence"])))))).ToJsonString();
            if (signature != prior)
            {
                prior = signature;
                changed = now;
            }
            double idle = now - changed;
            if (idle >= 45 || idle >= 10 && !B(page["Auto"]) && B(page["Ready"]))
                throw new StageHostException("pending", "骰子没有状态或网络进展；已停止投掷并保留原记录。");
        }
    }
    private static async Task Lease(DailyWorkflow w)
    {
        var frame = (await w.Observe()).Frame;
        await w.MailWrite("dice-lease.json", O(("Instance", frame["Instance"]), ("AccountKey", frame["AccountKey"]), ("PlayerKey", frame["PlayerKey"]), ("ExpiresUtcTicks", w.Driver.UtcTicks + 150_000_000)));
    }
    public static async Task<JsonObject> Dice(DailyWorkflow w, JsonObject p)
    {
        long start = w.Driver.UtcTicks;
        var guard = new DiceGuard();
        await Lease(w);
        try
        {
            var op = await w.Transact("rewards.dice", O(("event", p["TableId"]), ("schedule", DailyEventRewards.Parse(p["Schedule"])), ("balance", p["Balance"])), O(("ui", "EventUI"), ("operation", "reward_action"), ("items", new JsonArray(Copy(p["TableId"]))), ("value", 6)), 1800, heartbeat: async () => { var page = DailyEventRewards.Page(await w.Evidence(DailyEventRewards.Prefixes)); Require(new[] { "TableId", "Schedule" }.All(k => JsonNode.DeepEquals(page[k], p[k])), "Dice activity changed"); var events = w.Driver.CollectEvents("rewards.dice", start); guard.Observe(page, events, w.Time); await Lease(w); w.Save("dice-progress.json", O(("state", "running"), ("initial_balance", p["Balance"]), ("balance", page["Balance"]), ("auto", page["Auto"]), ("throws", Rows(events).Count(e => S(e["Kind"]) == "response" && B(e["Accepted"]) && N(e["ErrorCode"]) == 0)), ("at", w.Driver.UtcTicks))); });
            Require(op["result"]!["failures"]!.AsArray().Count == 0, "Dice request rejected; confirmed progress retained");
            var result = op["result"]!.DeepClone().AsObject();
            result.Remove("rewards");
            result["id"] = Copy(op["id"]);
            result["action"] = 6;
            w.Save("dice-progress.json", O(("state", "completed"), ("result", result)));
            return result;
        }
        catch (Exception e) { w.Save("dice-progress.json", O(("state", "needs_review"), ("error", e.Message), ("initial_balance", p["Balance"]), ("at", w.Driver.UtcTicks))); throw; }
        finally { await w.MailDelete("dice-lease.json"); }
    }
    public static bool Exhausted(JsonObject op, JsonObject now, JsonArray rows)
    {
        var old = op["scope"]!;
        var schedule = DailyEventRewards.Parse(now["Schedule"]);
        return S(now["Kind"]) == "MiniGameDiceUI" && B(now["Ready"]) && !B(now["Auto"]) && N(now["Cost"]) > 0 && N(now["Balance"]) >= 0 && N(now["Balance"]) < N(now["Cost"]) && N(old["event"]) == N(now["TableId"]) && N(old["schedule"]?["id"]) == N(schedule["id"]) && Rows(rows).Any(r => N(r["eventScheduleId"]) == N(schedule["id"]));
    }
    public static async Task ReconcileDice(DailyWorkflow w, JsonObject p)
    {
        foreach (var op in w.Business.Records(w.Context, "rewards.dice").Where(DailyManagedBusiness.Pending).Where(op => N(op["scope"]?["event"]) == N(p["TableId"])).ToArray())
        {
            await w.Business.ReconcileAsync(w.Context);
            var current = w.Business.Records(w.Context, "rewards.dice").Single(r => S(r["id"]) == S(op["id"]));
            if (!DailyManagedBusiness.Pending(current))
                continue;
            long at = w.Driver.UtcTicks;
            await w.Refresh("EventUI", true, true);
            var after = await w.Evidence(DailyEventRewards.Prefixes);
            var response = w.Driver.CollectEvents("rewards.dice_query", at);
            var values = Rows(response).Where(e => S(e["Kind"]) == "response" && B(e["Accepted"]) && N(e["ErrorCode"]) == 0).Select(DailyEvidence.Values).Single();
            Require(Exhausted(current, DailyEventRewards.Page(after), values["MiniGameBoardInfo"]!.AsArray()), "骰子旧操作需核对，服务器未确认无剩余次数，未重复投掷");
            current["state"] = "settled_without_receipts";
            current["recovery_observation"] = after.DeepClone();
            current["reconciliation"] = O(("at", w.Driver.UtcTicks), ("method", "server_board_queried_no_remaining_tokens"), ("actions", 0), ("historical_rewards_verified", false));
            w.Business.Save(current);
        }
    }
}

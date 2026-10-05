using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailySquare
{
    private const string Object = "ὪὬὣὧὥὨὦὯὨὮὬ", Busy = "ὡὯὧὯὨὢὭὤὡὮὭ";
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("square.reward", "square", ["square"], (op, events, after) => { var response = Response("square.reward", events, op["before"]!.AsObject(), after); Require(!B(R(after, "square.goddess", "$self")), "Goddess availability did not clear"); return O(("response", response), ("availability_matched", true)); }, AffectedStages: ["square", "goddess"]);
        yield return new("square.ranking_reward", "square", ["square"], (op, events, after) => { var before = op["before"]!.AsObject(); var response = Response("square.ranking_reward", events, before, after); long id = N(op["scope"]!["object_id"]); var old = R(before, "square.ranking_claimed", "$self")!.AsArray().Select(N).ToHashSet(); var latest = R(after, "square.ranking_claimed", "$self")!.AsArray().Select(N).ToHashSet(); Require(!old.Contains(id) && latest.Except(old).SequenceEqual([id]) && old.IsSubsetOf(latest), "Ranking reward cache differs"); return O(("object_id", id), ("response", response), ("claimed_cache_matched", true)); }, AffectedStages: ["square", "square_ranking"]);
    }
    public static JsonObject[] Rankings(JsonObject e)
    {
        var rows = Rows(e["Readings"]).Where(r => S(r["Id"]) is "square.ranking" or "square.guild_ranking").Select(r => { Require(S(r["Error"]) == "", "Square ranking observer failed"); return DailyEvidence.Values(r); }).ToArray();
        Require(rows.Select(r => N(r[Object])).Distinct().Count() == rows.Length, "Ambiguous square ranking objects");
        return rows;
    }
    public static long[] Targets(JsonObject e)
    {
        var claimed = R(e, "square.ranking_claimed", "$self")!.AsArray().Select(N).ToHashSet();
        return Rankings(e).Where(r => B(r["_objStatueReward.activeInHierarchy"]) && r[Busy] != null && !B(r[Busy]) && !claimed.Contains(N(r[Object]))).Select(r => N(r[Object])).ToArray();
    }
    public static async Task Recover(DailyWorkflow w)
    {
        var pending = w.Business.Records(w.Context, "square.ranking_reward").Where(DailyManagedBusiness.Pending).ToArray();
        if (pending.Length == 0)
            return;
        await w.Step("GameFieldDefaultUI", operation: "square_cancel_nav", reason: "停止上次排行领奖移动，核对游戏现状");
        var first = await w.Evidence("square");
        await w.Delay(250);
        var current = await w.Evidence("square");
        foreach (var op in pending)
        {
            string? State(JsonObject e)
            {
                if (S(op["action"]?["operation"]) != "square_ranking_nav")
                    return null;
                long id = N(op["scope"]?["object_id"]);
                var row = Rankings(e).FirstOrDefault(r => N(r[Object]) == id);
                if (row == null || row[Busy] == null || B(row[Busy]))
                    return null;
                if (R(e, "square.ranking_claimed", "$self")!.AsArray().Select(N).Contains(id))
                    return "already_claimed";
                return B(row["_objStatueReward.activeInHierarchy"]) ? "available_unclaimed" : null;
            }
            string? state = State(current);
            if (state == null || State(first) != state)
                throw new StageHostException("pending", "排行榜奖励仍在结算，未继续领奖。");
            op["reconciliation"] = O(("previous_state", op["state"]), ("at", w.Driver.UtcTicks), ("method", "current_native_square_state"), ("native_state", state), ("actions", 0));
            op["state"] = "superseded";
            op["recovery_observation"] = current.DeepClone();
            w.Business.Save(op);
        }
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, string stage)
    {
        if (!await DailyTravel.Enter(w, 2, "square.pack", packType: 11))
            return DailyWorkflow.Skipped("square_not_available_in_native_travel");
        bool goddess = stage != "square_ranking" && w.Settings.Tasks.Goddess, ranking = stage != "goddess" && w.Settings.Tasks.SquareRanking;
        var operations = new JsonArray();
        if (ranking)
            await Recover(w);
        if (goddess && B(R(await w.Evidence("square"), "square.goddess", "$self")))
        {
            try
            {
                await w.Step("GameFieldDefaultUI", operation: "square_goddess_nav", reason: "使用 A* 移动到女神像");
                await DailySquareNavigation.Wait(w,"square_goddess_nav",async()=>B(R(await w.Evidence("square"),"square.statue","ὩὤὨὮὥὦὫὭὫὭὨ")));
            }
            finally { await DailySquareNavigation.Stop(w); }
            var op = await w.Transact("square.reward", O(("kind", "goddess")), O(("ui", "GameFieldDefaultUI"), ("operation", "square_goddess_interact"), ("reason", "领取女神像每日奖励")), 35);
            operations.Add(O(("id", op["id"]), ("result", op["result"])));
            await w.Dismiss("GameFieldDefaultUI");
        }
        if (ranking)
            foreach (long target in Targets(await w.Evidence("square")))
            {
                if (!Targets(await w.Evidence("square")).Contains(target))
                    continue;
                JsonObject op;
                try
                {
                    op = await w.Transact("square.ranking_reward", O(("object_id", target)), O(("ui", "GameFieldDefaultUI"), ("operation", "square_ranking_nav"), ("value", checked((int)target)), ("reason", "领取排行榜周期金币")), 245, heartbeat:async()=>DailySquareNavigation.Inspect((await w.Observe()).Frame,"square_ranking_nav"));
                }
                finally { var f = (await w.Observe()).Frame; if (DailyNavigationDecision.Types(f).Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(f, "GameFieldDefaultUI", DailyNavigationPolicy.Load()).Length == 0) await w.Step("GameFieldDefaultUI", operation: "square_cancel_nav", reason: "结束排行领奖移动，保留结算记录"); }
                operations.Add(O(("id", op["id"]), ("result", op["result"])));
                await w.Dismiss("GameFieldDefaultUI");
            }
        await DailyTravel.Ready(w, "GameFieldDefaultUI");
        return O(("state", "completed"), ("reason", "square_rewards_checked"), ("operations", operations), ("engine", "dotnet-square-v1"));
    }
}
public static class DailyCafeteria
{
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        foreach (string role in new[] { "cafeteria.regular_all", "cafeteria.event" })
            yield return new(role, "cafeteria_guests", ["cafeteria"], (op, events, after) => Verify(S(op["role"]), op["before"]!.AsObject(), events, after), AffectedStages: ["management", "cafeteria_guests"]);
    }
    private static Dictionary<long, bool> Done(JsonObject e)
    {
        var keys = R(e, "cafeteria.regular_done", "Keys")!.AsArray();
        var flags = R(e, "cafeteria.regular_done", "Values")!.AsArray();
        Require(keys.Count == flags.Count && keys.Select(N).Distinct().Count() == keys.Count, "Guest cache incomplete");
        return keys.Select((k, i) => (Id: N(k), Flag: B(flags[i]))).ToDictionary(p => p.Id, p => p.Flag);
    }
    public static JsonObject Verify(string role, JsonObject before, JsonArray events, JsonObject after)
    {
        var data = Response(role, events, before, after)!;
        if (role == "cafeteria.regular_all")
        {
            var old = Done(before);
            var latest = Done(after);
            var daily = R(before, "cafeteria.cache", "DailyRegularCostumeId")!.AsArray().Select(N).ToHashSet();
            var rewarded = data["RewardedCostumeId"]!.AsArray().Select(N).ToArray();
            Require(rewarded.Length > 0 && rewarded.Distinct().Count() == rewarded.Length && rewarded.All(daily.Contains), "Unexpected guest rewards");
            Require(rewarded.All(i => old.TryGetValue(i, out bool was) && !was && latest.TryGetValue(i, out bool now) && now), "Guest claimed state did not advance");
            var cached = Rows(R(after, "cafeteria.regular_cache", "Values")).ToDictionary(r => N(r["costumeId"]));
            var returned = Rows(data["RegularCostumeInfo"]).ToDictionary(r => N(r["costumeId"]));
            Require(rewarded.All(i => returned.ContainsKey(i) && cached.ContainsKey(i) && JsonNode.DeepEquals(cached[i], returned[i])), "Guest response/cache differs");
            return O(("outcome", "collected_guests"), ("costume_ids", rewarded), ("reward_info", data["RewardInfo"]), ("cache_matched", true));
        }
        long previous = N(R(before, "cafeteria.cache", "DailyNpcRewardCurrencyCount")), count = N(data["DailyNpcRewardCurrencyCount"]), limit = N(R(before, "cafeteria.rules", "DailyShopCurrencyLimit"));
        Require(count > previous && count <= limit && N(R(after, "cafeteria.cache", "DailyNpcRewardCurrencyCount")) == count, "Bubble reward count did not advance");
        long currency = N(R(before, "cafeteria.rules", "EventRewardType")), gained = Rows(data["RewardInfo"]!["itemInfo"]).Where(r => N(r["type"]) == currency).Sum(r => N(r["count"]));
        Require(gained == count - previous, "Bubble currency reward differs");
        return O(("outcome", "collected_bubble"), ("daily_count", count), ("daily_limit", limit), ("gained", gained), ("reward_info", data["RewardInfo"]), ("cache_matched", true));
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Stages.CafeteriaGuests)
            return DailyWorkflow.Skipped("disabled");
        if (!await DailyTravel.Reuse(w, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], extra: ["ManagementRewardPopupUI"]))
        {
            if (!await w.Has("ManagementRewardPopupUI"))
            {
                await w.Home("cafeteria_guests");
                await DailyManagementEntry.OpenAsync(w.Driver, () => { w.Check(); return false; });
            }
            await w.Step("ManagementRewardPopupUI", "$pointer/Button - background/Parent/Image - Backgrond/CafeteriaSettlementInfo/EnableRoot/Button - ShortCut", reason: "进入餐厅领取熟客与限时气泡");
            await DailyTravel.Reuse(w, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], waitForTarget: true);
        }
        await DailyTravel.Ready(w, "CafeteriaFieldDefaultUI");
        var operations = new JsonArray();
        double end = w.Time + 120, last = w.Time;
        string state = "time_budget_reached";
        while (w.Time < end)
        {
            var f = (await w.Observe()).Frame;
            Require(S(f["Scene"]) == "Map3007_001", "Not on cafeteria field");
            if (await DailyTravel.RecoverPresentation(w, f))
                continue;
            Require(DailyNavigationDecision.Blockers(f, "CafeteriaFieldDefaultUI", DailyNavigationPolicy.Load()).Length == 0, "Unknown popup on cafeteria field");
            var e = await w.Evidence("cafeteria");
            f = e["Frame"]!.AsObject(); // Select short-lived bubbles from the same fresh observation.
            Require(S(f["Scene"] ) == "Map3007_001", "Cafeteria changed during observation" );
            long count = N(R(e, "cafeteria.cache", "DailyNpcRewardCurrencyCount")), limit = N(R(e, "cafeteria.rules", "DailyShopCurrencyLimit"));
            JsonObject? action = null;
            string? role = null;
            if (count < limit)
            {
                var bubble = DailyNavigationDecision.Rows(f).Where(r => S(r["Type"]) == "OverheadManageUI").SelectMany(r => Rows(r["Targets"])).FirstOrDefault(t => S(t["Route"]) == "pointer" && B(t["Enabled"]) && S(t["Field"]).StartsWith("$pointer/Parent/Cafeteria(Clone)/", StringComparison.Ordinal) && S(t["Field"]).EndsWith("/Button - EventBubble", StringComparison.Ordinal));
                if (bubble != null)
                {
                    role = "cafeteria.event";
                    action = O(("ui", "OverheadManageUI"), ("field", bubble["Field"]), ("target_id", bubble["Id"]), ("reason", "领取当前限时装饰币气泡"));
                }
            }
            var done = Done(e);
            var daily = R(e, "cafeteria.cache", "DailyRegularCostumeId")!.AsArray().Select(N).ToArray();
            Require(daily.All(done.ContainsKey), "Guest cache missing daily visitor");
            bool pending = daily.Any(i => !done[i]);
            if (action == null && pending)
            {
                role = "cafeteria.regular_all";
                action = O(("ui", "CafeteriaFieldDefaultUI"), ("field", "_buttonTrackingRegular.button"), ("reason", "一键领取当前熟客奖励"));
            }
            if (action != null)
            {
                try
                {
                    var op = await w.Transact(role!, O(("daily_count", count), ("kind", role)), action, 20);
                    operations.Add(op["id"]!.DeepClone());
                    last = w.Time;
                }
                catch (DailyStepException ex) when (ex.Kind == "rejected" && !ex.Submitted && (ex.Message.Contains("screen_changed", StringComparison.Ordinal) || ex.Message.Contains("target_unavailable", StringComparison.Ordinal) || role == "cafeteria.event" && ex.RejectionCode == "target_missing")) { await w.Delay(200); continue; }
            }
            else if (count >= limit && !pending)
            {
                state = "completed";
                break;
            }
            else if (w.Time - last >= 25)
            {
                state = "idle_no_visible_bubble";
                break;
            }
            await w.Delay(300);
        }
        var result = O(("state", state == "completed" ? "completed" : "partial"), ("reason", state), ("operations", operations), ("engine", "dotnet-cafeteria-v1"));
        w.Save("cafeteria-guests-latest.json", result);
        return result;
    }
}

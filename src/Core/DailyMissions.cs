using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyMissionResetException(JsonObject proof) : Exception("服务器已拒绝过期任务领取（112003），需要刷新任务进度。")
{
    public JsonObject Proof { get; } = proof;
}
public static class DailyMissions
{
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("missions.clear", "rewards", ["missions"], (op, events, after) => Verify(op, events, after), ["missions.clear", "missions.section"], ["rewards", "mission_rewards", "daily_rewards", "weekly_rewards"]);
        yield return new("pass.claim_all", "rewards", ["pass", "missions"], (op, events, after) => DailyPasses.Verify(op, events, after), ["missions.clear", "pass.reward"], ["rewards", "pass_rewards"]);
    }
    public static JsonObject Report(JsonObject e, Dictionary<long, JsonObject> tables, Dictionary<long, JsonObject>? texts = null)
    {
        Require(S(e["Error"]) == "", "Mission observation failed");
        string kind = S(R(e, "missions.tab", "_missionGroupType"));
        int group = kind switch
        {
            "MG_DAILY" => 0,
            "MG_WEEKLY" => 1,
            _ => throw new InvalidDataException("Unknown mission tab")
        };
        var ids = R(e, "missions.tab", "ὣὥὧὣὠὫὠὨὣὦὤ")!.AsArray().Select(N).ToArray();
        Require(ids.Distinct().Count() == ids.Length, "Duplicate mission IDs");
        var cached = Cached(e, "missions.cache").ToDictionary(r => N(r["id"]));
        var visible = Readings(e, "missions.row").Where(r => r.ContainsKey("ὫὫὩὭὣὡὤὮὤὭὥ")).ToDictionary(r => N(r["ὫὫὩὭὣὡὤὮὤὭὥ"]));
        var rows = new JsonArray();
        foreach (long id in ids)
        {
            Require(tables.TryGetValue(id, out var table) && N(table["groupType"]) == group, "Mission table/tab differs");
            cached.TryGetValue(id, out var stored);
            visible.TryGetValue(id, out var ui);
            long value = N(stored?["value"]), required = N(table!["conditionValue"]);
            string status = B(stored?["isComplete"]) ? "claimed" : B(ui?["_objLockCondition.activeInHierarchy"]) ? "locked" : value >= required ? "claimable" : "pending";
            string title = texts != null && texts.TryGetValue(N(table["titleLocalTextId"]), out var text) && S(text["textCn"]) != "" ? S(text["textCn"]) : S(ui?["_textTitle.text"]);
            rows.Add(O(("key", kind + ":" + id), ("id", id), ("title", title.Length > 0 ? title : id.ToString()), ("progress", value), ("required", required), ("status", status), ("condition_type", N(table["conditionType"])), ("condition_subtype", N(table["conditionSubType"])), ("params", table["conditionSubTypeParams"] ?? new JsonArray())));
        }
        return O(("tab", kind), ("missions", rows), ("total", rows.Count), ("claimable", Rows(rows).Count(r => S(r["status"]) == "claimable")), ("claimed", Rows(rows).Count(r => S(r["status"]) == "claimed")), ("pending", Rows(rows).Count(r => S(r["status"]) == "pending")));
    }
    private static (long Group, long Id)[] Sections(JsonObject e, JsonObject report, JsonObject[] tables)
    {
        int group = S(report["tab"]) == "MG_DAILY" ? 0 : 1;
        int count = Rows(report["missions"]).Count(r => S(r["status"]) is "claimed" or "claimable");
        var old = Cached(e, "missions.sections").Select(r => (Group: N(r["groupType"]), Id: N(r["id"]))).ToHashSet();
        return tables.Where(r => N(r["groupType"]) == group && N(r["sectionValue"]) <= count && !old.Contains((group, N(r["id"])))).Select(r => (Group: (long)group, Id: N(r["id"]))).OrderBy(r => r.Group).ThenBy(r => r.Id).ToArray();
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var scope = op["scope"]!.AsObject();
        var tables = Rows(scope["definitions"]).ToDictionary(r => N(r["id"]));
        var report = Report(before, tables);
        var ids = Rows(report["missions"]).Where(r => S(r["status"]) == "claimable").Select(r => N(r["id"])).ToArray();
        var expected = Sections(before, report, Rows(scope["section_definitions"]));
        var reward = Response("missions.clear", events, before, after);
        Require(ids.Length > 0 || expected.Length > 0, "No eligible missions/sections before claim");
        var cache = Cached(after, "missions.cache").ToDictionary(r => N(r["id"]));
        Require(ids.All(id => cache.TryGetValue(id, out var r) && B(r["isComplete"])), "Claimed mission cache did not advance");
        var sections = Response("missions.section", events, before, after, true);
        var old = Cached(before, "missions.sections").Select(r => (Group: N(r["groupType"]), Id: N(r["id"]))).ToHashSet();
        var current = Cached(after, "missions.sections").Select(r => (Group: N(r["groupType"]), Id: N(r["id"]))).ToHashSet();
        var added = current.Except(old).OrderBy(p => p.Group).ThenBy(p => p.Id).ToArray();
        Require((sections != null) == (added.Length > 0) && added.SequenceEqual(expected) && old.IsSubsetOf(current), "Mission section cache/response differs");
        Require(B(R(after, "missions.ui", "_tabAllRecvButton._objDisable.activeInHierarchy")), "Native mission All chain is unfinished");
        return O(("claimed_missions", ids), ("sections", added.Select(p => new[] { p.Group, p.Id }).ToArray()), ("reward", reward), ("section_reward", sections));
    }
    public static async Task<JsonObject> Claims(DailyWorkflow w, IEnumerable<string> groups, bool returnMenu = true)
    {
        await w.Refresh();
        var definitions = w.Index("missions/MissionTable.json");
        var texts = w.Index("missions/LocalTextTable.json");
        var sections = w.Table("gacha/MissionSectionRewardTable.json");
        var results = new JsonArray();
        if (await w.Has("MenuUI"))
            await w.Step("MenuUI", "_buttonMission", expect: "MissionUI", reason: "领取当前可领取的日常与周常奖励");
        foreach (string group in groups)
        {
            Require(group is "daily" or "weekly", "Invalid mission tab");
            await w.Step("MissionUI", "_tabButton" + char.ToUpperInvariant(group[0]) + group[1..] + "Mission._objButton", reason: "切换任务分类");
            var e = await w.WaitEvidence(["missions"], x => S(Report(x, definitions)["tab"]) == "MG_" + group.ToUpperInvariant());
            var report = Report(e, definitions, texts);
            bool eligible = N(report["claimable"]) > 0 || Sections(e, report, sections).Length > 0;
            Require(B(R(e, "missions.ui", "_tabAllRecvButton._objDisable.activeInHierarchy")) != eligible, "Mission button/eligibility disagrees");
            var result = O(("state", "completed"), ("group", group), ("actions", 0), ("report", report));
            if (eligible)
            {
                var scope = O(("group", group), ("definitions", Rows(report["missions"]).Select(r => definitions[N(r["id"])]).ToArray()), ("section_definitions", sections));
                var op = await w.Transact("missions.clear", scope, O(("ui", "MissionUI"), ("field", "_tabAllRecvButton._objButton"), ("expect", "RewardReceivePopupUI"), ("reason", "原生一键领取本页任务奖励")));
                await w.Dismiss("MissionUI", true);
                result["actions"] = 1;
                result["id"] = op["id"]!.DeepClone();
                result["result"] = op["result"]!.DeepClone();
                result["report"] = Report(await w.Evidence("missions"), definitions, texts);
            }
            results.Add(result);
        }
        if (returnMenu)
            await w.Step("MissionUI", "_objBackButton", expect: "MenuUI");
        var pending = Rows(results).SelectMany(r => Rows(r["report"]!["missions"]).Where(m => S(m["status"]) == "pending").Select(m => { var copy = m.DeepClone().AsObject(); copy["group"] = r["group"]!.DeepClone(); return copy; })).ToArray();
        return O(("state", Rows(results).Any(r => N(r["report"]?["claimable"]) > 0) ? "partial" : "completed"), ("groups", results), ("pending_missions", pending));
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, string stage)
    {
        var groups = new List<string>();
        if ((stage is "rewards" or "mission_rewards" or "daily_rewards") && w.Settings.Stages.DailyRewards)
            groups.Add("daily");
        if ((stage is "rewards" or "mission_rewards" or "weekly_rewards") && w.Settings.Stages.WeeklyRewards)
            groups.Add("weekly");
        bool passes = stage is "rewards" or "pass_rewards" && w.Settings.Tasks.Pass;
        var results = new JsonObject();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (groups.Count > 0)
                    results["missions"] = await Claims(w, groups, !passes);
                if (passes)
                    results["passes"] = await DailyPasses.Run(w);
                break;
            }
            catch (DailyMissionResetException) { await w.Driver.HomeRecoveryAsync("mission_reset"); foreach (string ui in new[] { "PassUI", "EventUI", "MissionUI" }) if (await w.Has(ui)) await w.Step(ui, "_objBackButton", expect: "MenuUI", reason: "关闭服务器拒绝的旧任务页面"); if (attempt >= 1) throw new StageHostException("adapter", "刷新后服务器仍拒绝领取，原结果已保留。"); await w.Refresh(force: true); }
        }
        var pending = new JsonArray();
        if (results["missions"]?["pending_missions"] is JsonArray missions)
            foreach (var row in missions)
                pending.Add(row!.DeepClone());
        if (results["passes"]?["reports"] is JsonArray reports)
            foreach (var row in Rows(reports).SelectMany(r => Rows(r["missions"])).Where(r => S(r["status"]) is "pending" or "claimable"))
                pending.Add(row.DeepClone());
        return O(("state", results.Any(r => S(r.Value?["state"]) is not ("completed" or "skipped")) ? "partial" : "completed"), ("stages", results), ("pending_tasks", pending), ("reason", pending.Count > 0 ? "奖励检查完成；仍有未完成任务" : "奖励检查完成"), ("engine", "dotnet-rewards-v1"));
    }
}

public static class DailyPasses
{
    public const string Enabled = "_btnAllRecvButton.ὢὪὯὧὠὥὬὪὪὮὤ";
    public static JsonObject Report(JsonObject e, JsonNode cycle, Dictionary<long, JsonObject>? texts = null)
    {
        Require(S(e["Error"]) == "", "Pass observation failed");
        int count = I(R(e, "pass.active", "ὠὤὭὯὯὪὯὧὮὪὬ.Count"));
        var active = R(e, "pass.active", "ὠὤὭὯὯὪὯὧὮὪὬ._items")!.AsArray();
        Require(count >= 0 && active.Count >= count && active.Take(count).All(r => r is JsonObject), "Active pass list incomplete");
        var activeIds = active.Take(count).Select(r => N(r!["ὥὬὭὪὪὦὬὩὦὧὠ"])).ToArray();
        Require(activeIds.Distinct().Count() == count, "Duplicate active passes");
        long pid = N(R(e, "pass.scope", "ὢὡὧὮὫὫὬὥὪὪὢ"));
        Require(activeIds.Contains(pid), "Selected pass is not active");
        var native = State(e, "pass.definition", "$self");
        Require(N(native["PassId"]) == pid, "Pass selection has not settled");
        var table = JsonNode.Parse(S(native["Table"]))!.AsObject();
        var group = JsonNode.Parse(S(native["Group"]))!.AsObject();
        Require(N(table["id"]) == pid && N(table["expEventMissionGroupId"]) == N(group["id"]) && N(group["usePass"]) != 0, "Native pass definition mismatch");
        var definitions = native["Missions"]!.AsArray().Select(raw => JsonNode.Parse(S(raw))!.AsObject()).ToDictionary(r => N(r["id"]));
        const string prefix = "ὨὤὭὯὣὥὡὯὯὭὬ";
        var keys = R(e, "pass.scope", prefix + ".Keys")!.AsArray().Select(I).ToArray();
        var values = R(e, "pass.scope", prefix + ".Values")!.AsArray();
        var groupIds = group["missionGroupId"]!.AsArray().Select(N).ToArray();
        Require(keys.Length == N(R(e, "pass.scope", prefix + ".Count")) && values.Count == keys.Length && keys.Order().SequenceEqual(Enumerable.Range(0, groupIds.Length)), "Native pass groups incomplete");
        int unlocked = I(R(e, "pass.scope", "ὥὠὣὪὮὭὧὣὩὩὧ"));
        var ids = new List<long>();
        var locked = new HashSet<long>();
        for (int i = 0; i < keys.Length; i++)
        {
            var listed = values[i]!.AsArray().Select(N).ToArray();
            var expected = definitions.Where(p => N(p.Value["groupType"]) == 2 && N(p.Value["groupId"]) == groupIds[keys[i]]).Select(p => p.Key).ToHashSet();
            Require(listed.Distinct().Count() == listed.Length && expected.SetEquals(listed), "Pass tasks differ from dated native definitions");
            ids.AddRange(listed);
            locked.UnionWith(listed.Where(id => keys[i] > unlocked || N(definitions[id]["unlockPackId"]) != 0 || N(definitions[id]["unlockQuestId"]) != 0));
        }
        var projected = Rows(R(e, "pass.cache", "$items"));
        Require(projected.Length == N(R(e, "pass.cache", "Count")), "Pass cache incomplete");
        var cache = projected.ToDictionary(r => (Event: S(r["EventId"]), Id: N(r["Id"])));
        string eventId = S(group["id"]);
        var missions = new JsonArray();
        foreach (long id in ids)
        {
            var def = definitions[id];
            cache.TryGetValue((eventId, id), out var stored);
            Require(stored == null || N(stored["GroupId"]) == N(def["groupId"]), "Pass cache group differs");
            long progress = N(stored?["Value"]), required = N(def["conditionValue"]);
            string status = B(stored?["IsComplete"]) ? "claimed" : locked.Contains(id) ? "locked" : progress >= required ? "claimable" : "pending";
            string title = texts != null && texts.TryGetValue(N(def["titleLocalTextId"]), out var text) ? S(text["textCn"]) : id.ToString();
            missions.Add(O(("key", "pass:" + pid + ":" + eventId + ":" + id), ("id", id), ("pass_id", pid), ("event_id", eventId), ("cycle", cycle), ("progress", progress), ("required", required), ("status", status), ("condition_type", N(def["conditionType"])), ("condition_subtype", N(def["conditionSubType"])), ("params", def["conditionSubTypeParams"] ?? new JsonArray()), ("title", title)));
        }
        Require(missions.Count == missions.Select(m => S(m!["key"])).Distinct().Count(), "Duplicate pass mission identity");
        return O(("missions", missions), ("total", missions.Count), ("claimable", Rows(missions).Count(r => S(r["status"]) == "claimable")), ("pending", Rows(missions).Count(r => S(r["status"]) == "pending")), ("active_pass_ids", activeIds), ("selected_pass_id", pid), ("pass_title", native["Title"]), ("schedule", native["Schedule"]), ("cycle", cycle));
    }
    private static HashSet<(long Id, string Kind)> Rewards(JsonObject e, long pid)
    {
        var projected = Rows(R(e, "pass.rewards", "$items"));
        Require(projected.Length == N(R(e, "pass.rewards", "Count")) && projected.Select(r => N(r["Key"])).Distinct().Count() == projected.Length, "Pass rewards cache incomplete");
        var row = projected.FirstOrDefault(r => N(r["Key"]) == pid);
        var result = new HashSet<(long, string)>();
        if (row == null)
            return result;
        var rows = Rows(row["Value"]);
        Require(rows.Length == N(row["Value.Count"]) && rows.Select(r => N(r["id"])).Distinct().Count() == rows.Length && rows.All(r => N(r["passId"]) == pid), "Pass reward identity mismatch");
        foreach (var r in rows)
            foreach (string kind in new[] { "basic", "premium1" })
                if (B(r[kind]))
                    result.Add((N(r["id"]), kind));
        return result;
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        Identity(before, after);
        var old = Report(before, op["cycle"]!);
        var current = Report(after, op["cycle"]!);
        Require(new[] { "selected_pass_id", "cycle", "active_pass_ids" }.All(k => JsonNode.DeepEquals(old[k], current[k])), "Pass/reset changed during claim");
        var missionReplies = Responses("missions.clear", events, before, after, 0, 2 * I(old["total"]));
        var levelReplies = Responses("pass.reward", events, before, after, 1, 2);
        var prior = Rows(old["missions"]).ToDictionary(r => S(r["key"]));
        var latest = Rows(current["missions"]).ToDictionary(r => S(r["key"]));
        Require(prior.Keys.ToHashSet().SetEquals(latest.Keys), "Pass mission set changed");
        var ready = prior.Where(p => S(p.Value["status"]) == "claimable").Select(p => p.Key).ToArray();
        Require((ready.Length == 0 || missionReplies.Length > 0) && ready.All(k => S(latest[k]["status"]) == "claimed") && latest.Values.All(r => S(r["status"]) != "claimable") && !B(R(after, "pass.scope", Enabled)), "Pass native All chain incomplete");
        long pid = N(old["selected_pass_id"]);
        var previous = Rewards(before, pid);
        var now = Rewards(after, pid);
        Require(previous.IsSubsetOf(now), "Pass reward cache regressed");
        var added = now.Except(previous).OrderBy(p => p.Id).ThenBy(p => p.Kind).ToArray();
        Require(ready.Length > 0 || added.Length > 0, "No pass reward change confirmed");
        var rewards = missionReplies.Select(r => O(("role", "missions.clear"), ("data", r))).Concat(levelReplies.Select(r => O(("role", "pass.reward"), ("data", r))));
        return O(("pass_id", pid), ("claimed_missions", ready), ("new_level_rewards", added.Select(p => O(("id", p.Id), ("kind", p.Kind))).ToArray()), ("response_counts", O(("missions.clear", missionReplies.Length), ("pass.reward", levelReplies.Length))), ("reward_info", rewards.ToArray()), ("cache_matched", true), ("report", current));
    }
    private static async Task<JsonObject> Read(DailyWorkflow w, long? expected = null)
    {
        JsonObject? report = null;
        await w.WaitEvidence(["pass", "missions"], e => { report = Report(e, w.Context["cycle"]!, w.Index("missions/LocalTextTable.json")); return expected == null || N(report["selected_pass_id"]) == expected; }, 15, "通行证定义尚未载入");
        return report!;
    }
    private static async Task Prepare(DailyWorkflow w)
    {
        double end = w.Time + 12;
        while (w.Time < end)
        {
            var rows = DailyNavigationDecision.Rows((await w.Observe()).Frame).Where(r => S(r["Type"]) == "PassUI").SelectMany(r => Rows(r["Targets"])).Where(t => S(t["Route"]) == "pointer" && S(t["Field"]).EndsWith("/Button - Tab - Mission", StringComparison.Ordinal)).ToArray();
            if (rows.Length == 1)
            {
                if (B(rows[0]["Enabled"]))
                    await w.Step(O(("ui", "PassUI"), ("field", rows[0]["Field"]), ("target_id", rows[0]["Id"]), ("reason", "初始化所有已解锁通行证任务组")));
                return;
            }
            await w.Delay(200);
        }
        throw new StageHostException("adapter", "通行证任务页尚未载入。");
    }
    private static async Task Finish(DailyWorkflow w, bool popup)
    {
        double end = w.Time + 40;
        double? stable = null;
        while (w.Time < end)
        {
            if (await w.Has("RewardReceivePopupUI"))
            {
                var e = await w.Evidence("pass", "reward.presentation");
                bool ready = new[] { "ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ", "ὮὬὧὦὣὠὠὤὮὦὧ" }.All(p => B(R(e, "reward.presentation", p))) && !B(R(e, "reward.presentation", "ὦὡὮὫὧὠὮὡὭὭὬ")) && !B(R(e, "pass.presentation", "ὦὡὮὫὧὠὮὡὭὭὬ"));
                if (ready)
                {
                    await w.Dismiss("PassUI");
                    popup = false;
                }
                stable = null;
            }
            else
            {
                Require(await w.Has("PassUI"), "Pass page disappeared during cleanup");
                if (!popup)
                {
                    stable ??= w.Time;
                    if (w.Time - stable >= .6)
                        return;
                }
            }
            await w.Delay(100);
        }
        throw new StageHostException("adapter", "通行证奖励展示尚未结束，已领取结果保留。");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, bool claim = true)
    {
        await w.Refresh();
        if (!await w.Has("PassUI"))
        {
            if (await w.Has("MissionUI"))
                await w.Step("MissionUI", "_objPassButton", expect: "PassUI");
            else
                await w.Step("MenuUI", "_buttonPass", expect: "PassUI");
        }
        var e = await w.Evidence("pass", "missions");
        if (N(R(e, "pass.active", "ὠὤὭὯὯὪὯὧὮὪὬ.Count")) == 0 && N(R(e, "pass.active", "ὦὩὪὣὣὤὣὯὩὠὢ")) == 0)
            await w.Step(O(("ui", "PassUI"), ("pass_init", true), ("reason", "初始化游戏通行证入口")));
        var first = await Read(w);
        var active = first["active_pass_ids"]!.AsArray().Select(N).ToArray();
        long selected = N(first["selected_pass_id"]);
        var order = new[] { selected }.Concat(active.Where(id => id != selected)).ToArray();
        var reports = new JsonArray();
        var operations = new JsonArray();
        foreach (long id in order)
        {
            if (id != selected)
                await w.Step("PassUI", operation: "pass_select", value: checked((int)id), reason: "检查当前有效通行证");
            await Read(w, id);
            await Prepare(w);
            var report = await Read(w, id);
            Require(report["active_pass_ids"]!.AsArray().Select(N).SequenceEqual(active), "Active pass list changed");
            e = await w.Evidence("pass", "missions");
            if (!claim || !B(R(e, "pass.scope", Enabled)))
            {
                Require(!claim || N(report["claimable"]) == 0, "Pass native button/eligibility differs");
                operations.Add(O(("state", "completed"), ("actions", 0), ("report", report)));
            }
            else
            {
                var target = await w.Pointer("PassUI", t => S(t["Route"]) == "pointer" && S(t["Field"]).EndsWith("/Button - AllRecv", StringComparison.Ordinal));
                var op = await w.Transact("pass.claim_all", O(("pass_id", id)), O(("ui", "PassUI"), ("field", target["Field"]), ("target_id", target["Id"]), ("reason", "领取全部已解锁通行证奖励")));
                bool popup = Rows(op["result"]!["reward_info"]).Any(r => S(r["role"]) == "missions.clear" && r["data"]?["RewardInfoBundle"] != null);
                await Finish(w, popup);
                operations.Add(O(("state", "completed"), ("actions", 1), ("id", op["id"]), ("result", op["result"])));
            }
            reports.Add(await Read(w, id));
        }
        await w.Step("PassUI", "_objBackButton", expect: "MenuUI");
        var result = O(("state", "completed"), ("reports", reports), ("operations", operations), ("engine", "dotnet-passes-v1"));
        w.Save("pass-claims-latest.json", result);
        return result;
    }
}

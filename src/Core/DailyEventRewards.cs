using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyEventRewards
{
    public static readonly string[] Prefixes = ["rewards", "pass.cache", "minigames", "trade.inventory"];
    private static readonly Dictionary<int, string> Kinds = new() { { 4, "EventMissionUI" }, { 19, "MiniGameRouletteUI" }, { 7, "EventExchangeUI" }, { 12, "MiniGameDiceUI" } };
    public static JsonObject Parse(JsonNode? raw) => JsonNode.Parse(string.IsNullOrWhiteSpace(S(raw)) ? "{}" : S(raw))?.AsObject() ?? new();
    public static JsonObject Page(JsonObject state) => State(state, "rewards.native", "$self");
    public static JsonObject[] Select(JsonObject page, EventRewardPreferences settings)
    {
        var enabled = new Dictionary<int, bool> { { 4, settings.Missions }, { 12, settings.Dice }, { 19, settings.Roulette }, { 7, settings.Exchange } };
        var rows = page["Catalog"]!.AsArray().Select(Parse).ToArray();
        Require(rows.Select(r => N(r["id"])).Distinct().Count() == rows.Length, "Duplicate active event identity");
        return rows.Where(r => enabled.GetValueOrDefault(I(r["eventType"])) && !(I(r["eventType"]) == 7 && N(r["eventSubType"]) != 0)).OrderBy(r => I(r["eventType"]) switch { 4 => 0, 12 => 1, 19 => 2, _ => 3 }).ThenBy(r => N(r["sortId"])).ThenBy(r => N(r["id"])).ToArray();
    }
    public static async Task<JsonObject> WaitPage(DailyWorkflow w, long? tid = null, long? tab = null)
    {
        string? previous = null;
        double stable = 0;
        return await w.WaitEvidence(Prefixes, e => { var p = Page(e); if (!B(p["Ready"]) || tid.HasValue && N(p["TableId"]) != tid || tab.HasValue && N(p["Tab"]) != tab) { previous = null; return false; } string key = O(new[] { "TableId", "Tab", "Page", "Received", "Total", "Batch", "Balance", "Claim", "Free", "Renew", "Cache" }.Select(k => (k, (object?)p[k])).ToArray()).ToJsonString(); if (key != previous) { previous = key; stable = w.Time; } return w.Time - stable >= .8; }, 40, "活动原生页面未就绪");
    }
    public static JsonObject[] Missions(JsonObject evidence)
    {
        var p = Page(evidence);
        var cache = Rows(R(evidence, "pass.cache", "$items"));
        Require(cache.Length == N(R(evidence, "pass.cache", "Count")), "Incomplete event mission cache");
        var selected = cache.Where(r => N(r["EventId"]) == N(p["EventId"])).ToDictionary(r => N(r["Id"]));
        return p["Missions"]!.AsArray().Select(Parse).Select(t => { selected.TryGetValue(N(t["id"]), out var saved); Require(saved == null || N(saved["GroupId"]) == N(t["groupId"]), "Event mission group changed"); long v = N(saved?["Value"]), required = N(t["conditionValue"]); return O(("id", t["id"]), ("group", t["groupId"]), ("title_id", t["titleLocalTextId"]), ("progress", v), ("required", required), ("status", B(saved?["IsComplete"]) ? "claimed" : v >= required ? "claimable" : "pending")); }).ToArray();
    }
    public static (Dictionary<(long Event, long Id), JsonObject> Missions, Dictionary<string, long> Balances) Data(JsonObject e)
    {
        var missions = Rows(R(e, "pass.cache", "$items"));
        Require(missions.Length == N(R(e, "pass.cache", "Count")), "Incomplete event mission cache");
        var groups = Rows(R(e, "trade.inventory", "$items"));
        Require(groups.Length == N(R(e, "trade.inventory", "Count")), "Incomplete event inventory");
        var balances = new Dictionary<string, long>();
        foreach (var g in groups)
        {
            var rows = Rows(g["Value.Values"]);
            Require(rows.Length == N(g["Value.Count"]), "Incomplete event token group");
            foreach (var r in rows)
            {
                string key = N(r["type"]) + ":" + N(r["id"]);
                balances[key] = checked(balances.GetValueOrDefault(key) + N(r["count"]));
            }
        }
        return (missions.ToDictionary(r => (N(r["EventId"]), N(r["Id"]))), balances);
    }
    public static bool Needed(JsonObject? entry, string cycle, Dictionary<(long Event, long Id), JsonObject> missions, Dictionary<string, long> balances)
    {
        if (entry == null || S(entry["state"]) != "completed")
            return true;
        if (S(entry["kind"]) == "EventMissionUI")
        {
            var old = Rows(entry["missions"] ?? new JsonArray());
            if (old.Length > 0 && B(entry["all_tabs_seen"]) && old.All(m => missions.TryGetValue((N(entry["event_id"]), N(m["id"])), out var c) && B(c["IsComplete"])))
                return false;
            if (S(entry["cycle"]) != cycle)
                return true;
            foreach (var m in old)
            {
                if (!missions.TryGetValue((N(entry["event_id"]), N(m["id"])), out var c))
                {
                    if (S(m["status"]) == "pending" && N(m["progress"]) == 0)
                        continue;
                    return true;
                }
                if (!B(c["IsComplete"]) && (N(c["Value"]) >= N(m["required"]) || S(m["status"]) == "claimed"))
                    return true;
            }
            return false;
        }
        if (S(entry["cycle"]) != cycle)
            return true;
        string key = S(entry["currency_key"]);
        return key.Length > 0 && N(entry["cost"]) > 0 && balances.GetValueOrDefault(key) >= N(entry["cost"]);
    }
    public static int Action(JsonObject p)
    {
        if (!B(p["Ready"]))
            return 0;
        string kind = S(p["Kind"]);
        bool enough = B(p["AllowedCurrency"]) && B(p["Claim"]) && N(p["Cost"]) > 0 && N(p["Batch"]) > 0 && N(p["Balance"]) >= checked(N(p["Cost"]) * N(p["Batch"]));
        return kind switch
        {
            "MiniGameDiceUI" => !B(p["Auto"]) && B(p["AllowedCurrency"]) && B(p["Claim"]) && N(p["Cost"]) > 0 && N(p["Balance"]) >= N(p["Cost"]) ? 6 : 0,
            "EventMissionUI" => B(p["Claim"]) ? 1 : 0,
            "MiniGameRouletteUI" => B(p["Free"]) && N(Parse(p["Cache"])["freeApCount"]) > 0 ? 2 : enough ? 3 : 0,
            "EventExchangeUI" => B(p["AllowedCurrency"]) && N(p["Total"]) > 0 && N(p["Received"]) >= N(p["Total"]) ? B(p["Renew"]) ? 5 : 0 : enough ? 4 : 0,
            _ => 0
        };
    }
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        foreach (int i in new[] { 1, 2, 3, 4, 5, 8 })
        {
            int action = i;
            string role = action == 1 ? "missions.clear" : action is 2 or 3 or 8 ? "rewards.roulette" : action == 4 ? "rewards.exchange" : "rewards.exchange_page";
            yield return new("event_rewards." + action, "event_rewards", Prefixes, (op, es, after) => Verify(op["before"]!.AsObject(), es, after, action), [role], ["event_rewards", "rewards"], OwnsPreview);
        }
    }
    public static bool OwnsPreview(JsonObject op, DailyStageFrame current)
    {
        var preview = op["preview_frame"] as JsonObject;
        if (preview == null || !JsonNode.DeepEquals(op["cycle"], current.Context["cycle"]) || !DailyEvidence.SameActor(preview, current.Frame) || S(preview["Scene"]) != S(current.Frame["Scene"]) || !JsonNode.DeepEquals(preview["UiToken"], current.Frame["UiToken"]))
            return false;
        var old = DailyNavigationDecision.Rows(preview).Where(r => S(r["Type"]) == "MessagePopupUI").ToArray();
        var now = DailyNavigationDecision.Rows(current.Frame).Where(r => S(r["Type"]) == "MessagePopupUI").ToArray();
        return old.Length == 1 && now.Length == 1 && JsonNode.DeepEquals(old[0]["Id"], now[0]["Id"]) && DailyNavigationDecision.ReadyInput(now[0]);
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, int action)
    {
        var b = Page(before);
        var a = Page(after);
        Require(JsonNode.DeepEquals(b["TableId"], a["TableId"]) && JsonNode.DeepEquals(b["Schedule"], a["Schedule"]), "Event identity changed");
        string role = action == 1 ? "missions.clear" : action is 2 or 3 or 8 ? "rewards.roulette" : action == 4 ? "rewards.exchange" : "rewards.exchange_page";
        var responses = Responses(role, events, before, after, 1, 200);
        if (action == 1)
        {
            Require(JsonNode.DeepEquals(b["Tab"], a["Tab"]), "Event tab changed");
            var ready = Missions(before).Where(r => S(r["status"]) == "claimable").Select(r => N(r["id"])).ToArray();
            var latest = Missions(after).ToDictionary(r => N(r["id"]));
            Require(ready.Length > 0 && ready.All(id => latest.TryGetValue(id, out var m) && S(m["status"]) == "claimed") && !B(a["Claim"]), "Event claim chain incomplete");
            return O(("claimed", ready), ("responses", responses.Length), ("rewards", Array(responses)));
        }
        if (action is 2 or 3 or 8)
        {
            var info = responses[^1]["RouletteInfo"]!;
            Require(JsonNode.DeepEquals(Parse(a["Cache"]), info), "Roulette server cache differs");
            int draws = responses.Sum(r => r["RouletteRewardInfo"]!.AsArray().Count);
            long expected = action is 2 or 8 ? 1 : N(b["Batch"]);
            Require(draws == expected, "Roulette count differs");
            long spent = action == 2 ? 0 : checked(expected * N(b["Cost"]));
            Require(action == 2 ? N(info["freeApCount"]) == N(Parse(b["Cache"])["freeApCount"]) - 1 : N(b["Balance"]) - N(a["Balance"]) == spent, "Roulette token debit differs");
            return O(("draws", draws), ("spent", spent), ("responses", responses.Length), ("rewards", Array(responses)));
        }
        if (action == 4)
        {
            long spent = N(b["Balance"]) - N(a["Balance"]);
            Require(spent == checked(N(b["Batch"]) * N(b["Cost"])) && responses[^1]["ChangeExchangeRewardInfo"] is JsonArray r && r.Count > 0, "Exchange debit or rewards differ");
            return O(("exchanges", b["Batch"]), ("spent", spent), ("responses", responses.Length), ("rewards", Array(responses)));
        }
        Require(N(a["Page"]) > N(b["Page"]), "Exchange page did not advance");
        return O(("page", a["Page"]), ("responses", responses.Length));
    }
    public static Task<JsonObject> Perform(DailyWorkflow w, JsonObject p) => PerformCore(w, p, Action(p));
    public static Task<JsonObject> SingleRoulette(DailyWorkflow w, JsonObject p)
    {
        Require(S(p["Kind"]) == "MiniGameRouletteUI" && B(p["Ready"]) && B(p["Single"]) && B(p["AllowedCurrency"]) && N(p["Cost"]) > 0 && N(p["Balance"]) >= N(p["Cost"]) && N(Parse(p["Cache"])["freeApCount"]) == 0, "当前转盘左侧付费单抽不可用");
        return PerformCore(w, p, 8);
    }
    private static async Task<JsonObject> PerformCore(DailyWorkflow w, JsonObject p, int action)
    {
        Require(action > 0, "No valid event action");
        if (action == 6)
            return await DailyMiniGames.Dice(w, p);
        var command = O(("ui", "EventUI"), ("operation", "reward_action"), ("items", new JsonArray(Copy(p["TableId"]))), ("value", action));
        var scope = O(("event", p["TableId"]), ("schedule", Parse(p["Schedule"])), ("page", p["Page"]), ("tab", p["Tab"]), ("batch", p["Batch"]), ("balance", p["Balance"]));
        JsonObject op;
        if (action is 4 or 5)
        {
            var before = await w.Evidence(Prefixes);
            op = w.Business.Create(w.Context, "event_rewards." + action, before, scope, O(("ui", "MessagePopupUI"), ("field", "_buttonOK")));
            var preview = command.DeepClone().AsObject();
            preview["expect"] = "MessagePopupUI";
            await w.Business.PreviewAsync(op, w.Context, preview);
            var current = Page(await w.Evidence(Prefixes));
            Require(new[] { "TableId", "Page", "Balance", "Batch", "Schedule" }.All(k => JsonNode.DeepEquals(current[k], p[k])), "Exchange preview changed; no confirmation sent");
            await w.Business.CommitAsync(op, w.Context, 90);
        }
        else
            op = await w.Transact("event_rewards." + action, scope, command, 90);
        op["presentation"] = "pending";
        w.Business.Save(op); // Reward/debit is already proven even if animation cleanup fails.
        if (action != 5)
            await FinishPresentation(w, p);
        await WaitPage(w, N(p["TableId"]));
        op["presentation"] = "settled";
        w.Business.Save(op);
        var result = op["result"]!.DeepClone().AsObject();
        result.Remove("rewards");
        result["id"] = Copy(op["id"]);
        result["action"] = action;
        return result;
    }
    // The EventUI container stays interactive while its child roulette is spinning.
    // Only the child state proves presentation is settled; otherwise the delayed
    // RewardReceivePopupUI must still be serviced before waiting for the next draw.
    public static async Task FinishPresentation(DailyWorkflow w, JsonObject expected)
    {
        var progress = O(("state", "presentation_pending"), ("event", expected["TableId"]), ("kind", expected["Kind"]));
        w.Save("event-presentation-latest.json", progress);
        try
        {
            await w.Driver.DismissRewardAsync(["EventUI"], true, async () =>
            {
                var p = Page(await w.Evidence(Prefixes));
                Require(new[] { "TableId", "Kind", "Schedule" }.All(k => JsonNode.DeepEquals(p[k], expected[k])), "活动领奖期间页面或活动周期改变");
                progress["page"] = p.DeepClone();
                return B(p["Ready"]);
            });
            progress["state"] = "settled";
        }
        catch (Exception error) { progress["error"] = error.Message; throw; }
        finally { w.Save("event-presentation-latest.json", progress); }
    }
    public static void RequireExhausted(JsonObject p)
    {
        string kind = S(p["Kind"]);
        if (kind is not ("MiniGameRouletteUI" or "EventExchangeUI" or "MiniGameDiceUI")) return;
        Require(B(p["Ready"]), "活动仍在动画或结算中，不能记为完成");
        if (kind == "MiniGameRouletteUI")
            Require(N(Parse(p["Cache"])["freeApCount"]) == 0, "转盘仍有免费次数，按钮未就绪，不能记为完成");
        if (kind == "EventExchangeUI" && N(p["Total"]) > 0 && N(p["Received"]) >= N(p["Total"]) && !B(p["Renew"])) return;
        Require(N(p["Cost"]) > 0 && N(p["Balance"]) < N(p["Cost"]), "活动仍有可用代币，但原生按钮不可用；保留剩余次数，不能记为完成");
    }
    public static JsonArray Pending(IEnumerable<JsonObject> entries, Dictionary<(long Event, long Id), JsonObject>? cache = null)
    {
        var latest = new Dictionary<(long, long), JsonObject>();
        foreach (var entry in entries)
            foreach (var row in Rows(entry["missions"] ?? new JsonArray()))
            {
                var m = row.DeepClone().AsObject();
                if (cache != null)
                {
                    cache.TryGetValue((N(entry["event_id"]), N(m["id"])), out var current);
                    m["progress"] = N(current?["Value"]);
                    m["status"] = B(current?["IsComplete"]) ? "claimed" : N(m["progress"]) >= N(m["required"]) ? "claimable" : "pending";
                }
                m["group"] = "event";
                m["event_id"] = Copy(entry["event_id"]);
                m["key"] = "event:" + N(entry["event"]) + ":" + N(m["id"]);
                latest[(N(entry["event"]), N(m["id"]))] = m;
            }
        return Array(latest.Values.Where(m => S(m["status"]) is "pending" or "claimable"));
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Events.Enabled)
            return DailyWorkflow.Skipped("disabled");
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await RunOnce(w);
            }
            catch (DailyMissionResetException) when (attempt == 0) { await w.Driver.HomeRecoveryAsync("mission_reset"); await w.Refresh(force: true); }
        }
    }
    public static bool HasPendingPresentation(DailyManagedBusiness business, DailyStageFrame frame) =>
        DailyNavigationDecision.Types(frame.Frame).Contains("EventUI") && business.MatchingRecords(frame.Context, op =>
            S(op["role"]).StartsWith("event_rewards.", StringComparison.Ordinal) && S(op["state"]) == "completed" && S(op["presentation"]) == "pending" && JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"])).Any(op => DailyWorkflowRegistry.Owned(op, frame));
    private static async Task ResumePresentation(DailyWorkflow w)
    {
        var frame = await w.Observe();
        if (!HasPendingPresentation(w.Business, frame)) return;
        var p = Page(await w.Evidence(Prefixes));
        var pending = w.Business.MatchingRecords(w.Context, op => S(op["role"]).StartsWith("event_rewards.", StringComparison.Ordinal) && S(op["state"]) == "completed" && S(op["presentation"]) == "pending" && JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"])).Where(op => DailyWorkflowRegistry.Owned(op, frame))
            .Where(op => new[] { "TableId", "Kind", "Schedule" }.All(k => JsonNode.DeepEquals(Page(op["after"]!.AsObject())[k], p[k]))).OrderByDescending(op => N(op["confirmed_at"])).FirstOrDefault();
        if (pending == null) return;
        await FinishPresentation(w, p);
        pending["presentation"] = "settled";
        w.Business.Save(pending);
    }
    private static async Task ResumePreview(DailyWorkflow w)
    {
        var pending = w.Business.MatchingRecords(w.Context, op => S(op["role"]).StartsWith("event_rewards.", StringComparison.Ordinal) && S(op["state"]) == "preview_ready").ToArray();
        if (pending.Length == 0)
            return;
        Require(pending.Length == 1, "多个活动预览尚未核对");
        var op = pending[0];
        Require(OwnsPreview(op, await w.Observe()), "活动确认窗口已改变，未重复预览或确认");
        var before = Page(op["before"]!.AsObject());
        var current = Page(await w.Evidence(Prefixes));
        Require(new[] { "TableId", "Page", "Balance", "Batch", "Schedule" }.All(k => JsonNode.DeepEquals(current[k], before[k])), "活动原预览报价已改变");
        await w.Business.CommitAsync(op, w.Context, 90);
        if (S(op["role"]) != "event_rewards.5")
            await FinishPresentation(w, before);
        await WaitPage(w, N(before["TableId"]));
    }
    private static async Task<JsonObject> RunOnce(DailyWorkflow w)
    {
        await ResumePresentation(w);
        await ResumePreview(w);
        await w.Refresh();
        var settings = w.Settings.Events;
        string cycle = S(w.Context["cycle"]), settingsKey = Hash(JsonSerializer.Serialize(settings)), path = Path.Combine(w.Root, "live", "event-visits", S(w.Context["actor"]![3]), Hash(S(w.Context["server"])) + ".json");
        var visits = DailyJson.TryRead<JsonObject>(path) ?? O(("entries", new JsonObject()));
        if (S(visits["settings"]) != settingsKey || N(visits["policy"]) != 2)
            visits = O(("entries", new JsonObject()));
        var entries = visits["entries"]!.AsObject();
        var state = await w.Evidence(Prefixes);
        var data = Data(state);
        var rows = Select(Page(state), settings);
        var pendingDice = w.Business.Records(w.Context, "rewards.dice").Where(DailyManagedBusiness.Pending).Select(op => N(op["scope"]?["event"])).ToHashSet();
        bool quiz = settings.Quiz && DailyMiniGames.Pending(DailyQuizRecovery.Page(state)).Length > 0;
        if (pendingDice.Count == 0 && JsonNode.DeepEquals(visits["catalog"], Array(rows)) && S(visits["cycle"]) == cycle && S(visits["settings"]) == settingsKey && !quiz && rows.All(r => !Needed(entries[S(r["id"])] as JsonObject, cycle, data.Missions, data.Balances)))
            return O(("state", "skipped"), ("reason", "server_event_progress_unchanged"), ("actions", 0), ("pending_tasks", Pending(rows.Select(r => entries[S(r["id"])]!.AsObject()), data.Missions)), ("engine", "dotnet"));
        if (!await w.Has("EventUI"))
        {
            await w.Home("event_rewards");
            await w.Step("MenuUI", "_buttonEvent", expect: "EventUI");
        }
        rows = Select(Page(await WaitPage(w)), settings);
        visits["catalog"] = Array(rows);
        visits["settings"] = settingsKey;
        visits["policy"] = 2;
        DailyJson.Write(path, visits);
        var results = new JsonArray();
        var output = O(("state", "running"), ("events", results), ("actions", 0), ("engine", "dotnet"));
        results = output["events"]!.AsArray();
        try
        {
            bool settled = false;
            for (int sweep = 0; sweep < 4; sweep++)
            {
                bool changed = false, quizChecked = false;
                foreach (var row in rows.Cast<JsonObject?>().Append(null))
                {
                    if (!quizChecked && (row == null || N(row["eventType"]) != 4))
                    {
                        quizChecked = true;
                        if (settings.Quiz && DailyMiniGames.Pending(DailyQuizRecovery.Page(await w.Evidence(Prefixes))).Length > 0)
                        {
                            await w.Step("EventUI", "_objBackButton", expect: "MenuUI");
                            var q = await DailyMiniGames.Quizzes(w);
                            var qs = output["quizzes"] as JsonArray ?? new();
                            if (output["quizzes"] == null)
                                output["quizzes"] = qs;
                            qs.Add(q);
                            output["actions"] = N(output["actions"]) + N(q["actions"]);
                            changed |= N(q["actions"]) > 0;
                            await w.Step("MenuUI", "_buttonEvent", expect: "EventUI");
                            await WaitPage(w);
                        }
                    }
                    if (row == null)
                        continue;
                    long id = N(row["id"]);
                    data = Data(await w.Evidence(Prefixes));
                    if (!pendingDice.Contains(id) && !Needed(entries[id.ToString()] as JsonObject, cycle, data.Missions, data.Balances))
                        continue;
                    await w.Step("EventUI", operation: "reward_select", value: checked((int)id));
                    var p = Page(await WaitPage(w, id));
                    if (S(p["Kind"]) == "MiniGameDiceUI")
                    {
                        await DailyMiniGames.ReconcileDice(w, p);
                        pendingDice.Remove(id);
                    }
                    var entry = O(("event", id), ("event_id", row["eventId"]), ("kind", p["Kind"]), ("sweep", sweep), ("operations", new JsonArray()), ("missions", new JsonArray()));
                    results.Add(entry);
                    if (S(p["Kind"]) != Kinds[I(row["eventType"])])
                    {
                        entry["state"] = "skipped";
                        entry["reason"] = "Unsupported event variant";
                        continue;
                    }
                    long[] tabs = S(p["Kind"]) == "EventMissionUI" ? Enumerable.Range(0, checked(I(p["Unlocked"]) + 1)).Select(n => (long)n).ToArray() : [-1];
                    foreach (long tab in tabs)
                    {
                        if (tab >= 0 && N(p["Tab"]) != tab)
                            await w.Step(O(("ui", "EventUI"), ("operation", "reward_tab"), ("value", tab), ("items", new JsonArray(id))));
                        bool tabDone = false;
                        for (int iteration = 0; iteration < 100; iteration++)
                        {
                            var e = await WaitPage(w, id, tab < 0 ? null : tab);
                            p = Page(e);
                            if (Action(p) == 0)
                            {
                                if (S(p["Kind"]) == "EventMissionUI")
                                    foreach (var mission in Missions(e))
                                    {
                                        var texts = w.Index("missions/LocalTextTable.json");
                                        texts.TryGetValue(N(mission["title_id"]), out var text);
                                        mission["title"] = S(text?["textCn"]).Length > 0 ? S(text!["textCn"]) : S(text?["textEn"]);
                                        entry["missions"]!.AsArray().Add(mission);
                                    }
                                Require(!(S(p["Kind"]) is "MiniGameRouletteUI" or "EventExchangeUI" or "MiniGameDiceUI") || N(p["Cost"]) <= 0 || N(p["Balance"]) < N(p["Cost"]) || B(p["AllowedCurrency"]), "Event currency unsupported; balance retained");
                                RequireExhausted(p);
                                tabDone = true;
                                break;
                            }
                            entry["operations"]!.AsArray().Add(await Perform(w, p));
                            output["actions"] = N(output["actions"]) + 1;
                            changed = true;
                            w.Save("event-rewards-latest.json", output);
                        }
                        Require(tabDone, "Event action budget exceeded");
                    }
                    var group = Parse(p["Group"]);
                    entry["state"] = "completed";
                    entry["all_tabs_seen"] = S(p["Kind"]) == "EventMissionUI" && N(p["Unlocked"]) + 1 >= (group["missionGroupId"] as JsonArray)?.Count;
                    entry["cycle"] = cycle;
                    entry["balance"] = Copy(p["Balance"]);
                    entry["cost"] = Copy(p["Cost"]);
                    entry["schedule"] = Copy(p["Schedule"]);
                    entry["currency_key"] = N(group["itemType"]) + ":" + N(group["itemId"]);
                    entries[id.ToString()] = entry.DeepClone();
                    DailyJson.Write(path, visits);
                    w.Save("event-rewards-latest.json", output);
                }
                if (!changed)
                {
                    settled = true;
                    break;
                }
            }
            Require(settled, "Event rewards still changing after four sweeps; progress retained");
            visits["cycle"] = cycle;
            DailyJson.Write(path, visits);
            output["state"] = Rows(results).Any(e => S(e["state"]) == "skipped") ? "partial" : "completed";
            output["pending_tasks"] = Pending(entries.Select(p => p.Value!.AsObject()), Data(await w.Evidence(Prefixes)).Missions);
            w.Save("event-rewards-latest.json", output);
            await w.Step("EventUI", "_objBackButton", expect: "MenuUI");
            return output;
        }
        catch (Exception e) { output["state"] = "blocked"; output["error"] = e.Message; w.Save("event-rewards-latest.json", output); throw; }
    }
}


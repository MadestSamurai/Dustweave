using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;
namespace Dustweave;

public sealed class DailyWeeklySteal(DailyFieldRoute route)
{
    private DailyWorkflow W => route.W;
    public static JsonObject? Choose(JsonObject[] rows, JsonObject[] status, JsonObject[] pending)
    {
        var states = status.ToDictionary(r => N(r["Instance"]));
        var groups = pending.Select(p => N(p["Group"])).ToHashSet();
        return rows.Where(r => r["skill"] is JsonObject t && states.TryGetValue(N(r["instance"]), out var s) && S(s["Reason"]) == "" && S(s["Gate"]) == "" && DailyRegionRouter.D(s["Cooldown"]) <= 0 && !B(r["disabled"]) && N(t["classType"]) == 1 && N(t["resetType"]) == 2 && groups.Contains(N(t["groupId"])))
            .OrderByDescending(r => DailyRegionRouter.D((r["skill"]!["valueList"] as JsonArray)?.FirstOrDefault())).ThenBy(r => N(r["skill"]!["catalystValue"])).ThenByDescending(r => N(r["skill"]!["id"])).FirstOrDefault();
    }
    public static bool WaitingForTalent(JsonObject[] status, JsonObject[] pending) => status.Length == 0 || status.Any(s => pending.Any(p => N(p["Group"]) == N(s["Group"])) && S(s["Gate"]).StartsWith("talent_wait:", StringComparison.Ordinal) && DailyRegionRouter.D(s["Cooldown"]) <= 15);
    private async Task<(JsonObject Evidence, JsonObject Talent)> SelectTalent(long map, string week, JsonObject[] remaining)
    {
        double end = W.Time + 15;
        while (true)
        {
            var e = await route.Evidence();
            Require(N(Map(e)["id"]) == map && S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "连续偷窃期间地图或周周期变化");
            var rows = Rows(e["Readings"]).Where(r => S(r["Id"]) == "dispatch.talent" && S(r["Error"]) == "").Select(r => { var v = DailyEvidence.Values(r); return O(("instance", r["InstanceId"]), ("skill", v[SkillPath]), ("disabled", v["_objDisableImage.activeSelf"])); }).ToArray();
            var status = Talents(e);
            var selected = Choose(rows, status, remaining);
            if (selected != null)
            {
                await W.Driver.RequirePotionsAsync(N(selected["skill"]?["catalystValue"]));
                return (e, selected);
            }
            var shortage = rows.Where(r => status.Any(s => N(s["Instance"]) == N(r["instance"]) && S(s["Reason"]) == "catalyst_insufficient") &&
                remaining.Any(p => N(p["Group"]) == N(r["skill"]?["groupId"]))).OrderBy(r => N(r["skill"]?["catalystValue"])).FirstOrDefault();
            if (shortage != null) await W.Driver.RequirePotionsAsync(N(shortage["skill"]?["catalystValue"]));
            if (W.Time >= end || rows.Length > 0 && !WaitingForTalent(status, remaining))
            {
                W.Driver.Diagnostics.Failure("weekly_steal_unavailable", O(("error", "steal_talent_unavailable"), ("map", map), ("pending", remaining), ("talents", status), ("rows", rows)));
                await W.Step("QuickMenuUI", back: true, absent: "QuickMenuUI");
                throw new StageHostException("adapter", "当前偷窃天赋尚不可用，已保留待办。请检查角色状态、冷却和天赋药；不会自动购买或升级。");
            }
            await W.Delay(200);
        }
    }
    private async Task<bool> Popup(JsonObject frame)
    {
        var policy = DailyNavigationPolicy.Load();
        var a = DailyHomeDecision.BackgroundErrorAction(frame, policy) ?? DailyNavigationDecision.TalentAction(frame, policy) ?? DailyNavigationDecision.Notice(frame, policy, out _);
        if (a == null)
            return false;
        try { await W.Driver.NavigationAsync(a); }
        catch (StageHostException e) when (e.Kind == "navigation_changed") { await W.Delay(150); }
        return true;
    }
    private async Task<JsonObject> Approach(JsonObject target, long map)
    {
        bool started = false;
        double end = W.Time + Math.Clamp(DailyRegionRouter.D(target["Distance"]) / 2 + 15, 15, 90);
        while (W.Time < end)
        {
            var e = await route.Evidence();
            Require(N(Map(e)["id"]) == map, "前往偷窃目标时地图改变");
            if (await Popup(e["Frame"]!.AsObject()))
            {
                started = false;
                continue;
            }
            var current = Rows(R(e, "mainline.steal_npcs", "$self")).SingleOrDefault(n => N(n["Npc"]) == N(target["Npc"]));
            Require(current != null, "偷窃NPC不在当前场景，保留待办");
            if (B(current!["Near"]))
            {
                await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                return current;
            }
            if (!started)
            {
                Require(B(current["Reachable"]), "偷窃NPC不可达：" + S(current["Reason"]));
                await route.ProtectWeeklyMovement();
                await W.Step("GameFieldDefaultUI", operation: "mainline_walk", value: I(current["Instance"]));
                started = true;
            }
            await W.Delay(200);
        }
        await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
        throw new DailyTravelBlocked("前往偷窃NPC超时，保留未完成状态");
    }
    private async Task<bool> Presentation(JsonObject frame, string ui, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (DailyStepException e) when (e.Kind == "rejected" && (e.Message == $"Need one observed {ui}; found 0" || new[] { "rejected: screen_changed", "rejected: ui_not_ready", "rejected: surface_missing", "rejected: foreground_popup" }.Contains(e.Message) || e.Message.StartsWith("Foreground popup needs handling:", StringComparison.Ordinal)))
        {
            var fresh = (await W.Observe()).Frame;
            Require(DailyEvidence.SameActor(frame, fresh) && S(frame["Scene"]) == S(fresh["Scene"]), "偷窃结算身份或场景改变");
            var old = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == ui);
            var next = DailyNavigationDecision.Rows(fresh).SingleOrDefault(r => S(r["Type"]) == ui);
            var p = DailyNavigationPolicy.Load();
            bool changed = next == null || old != null && !JsonNode.DeepEquals(next["Id"], old["Id"]) || !DailyNavigationDecision.ReadyInput(next) || !DailyNavigationDecision.Blockers(frame, ui, p).SequenceEqual(DailyNavigationDecision.Blockers(fresh, ui, p));
            if (!changed)
                throw;
            return false;
        }
    }
    private async Task<bool> Settle(long npc, long group)
    {
        double end = W.Time + 45;
        int advances = 0;
        var p = DailyNavigationPolicy.Load();
        while (W.Time < end)
        {
            var f = (await W.Observe()).Frame;
            if (await Popup(f))
                continue;
            var types = DailyNavigationDecision.Types(f);
            var balloon = DailyNavigationDecision.Rows(f).SingleOrDefault(r => S(r["Type"]) == "BalloonScriptUI");
            if (types.Contains("RewardReceivePopupUI") && DailyNavigationDecision.Blockers(f, "RewardReceivePopupUI", p).Length == 0)
                await Presentation(f, "RewardReceivePopupUI", () => W.Driver.DismissRewardAsync(["QuickMenuUI", "GameFieldDefaultUI", "BalloonScriptUI"], false));
            else if (balloon != null)
            {
                if (DailyNavigationDecision.ReadyInput(balloon) && DailyNavigationDecision.Blockers(f, "BalloonScriptUI", p).Length == 0 && Rows(balloon["Targets"]).Any(t => S(t["Field"]) == "_objTouchButton" && B(t["Enabled"])))
                {
                    if (await Presentation(f, "BalloonScriptUI", async () => { await W.Step("BalloonScriptUI", operation: "weekly_steal_talk", value: checked((int)npc)); }))
                        advances++;
                    Require(advances <= 80, "偷窃对话未推进，已确认回执保留");
                }
            }
            else if (types.Contains("QuickMenuUI") && DailyNavigationDecision.Blockers(f, "QuickMenuUI", p).Length == 0)
            {
                if (Talents(await route.Evidence()).Any(r => N(r["Group"]) == group && DailyRegionRouter.D(r["Cooldown"]) > 0))
                    return true;
            }
            else if (types.Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(f, "GameFieldDefaultUI", p).Length == 0)
            {
                await route.FieldReady();
                return false;
            }
            await W.Delay(200);
        }
        throw new StageHostException("adapter", "偷窃已收到回执，结果展示尚未结束；不会重复消耗。");
    }
    private async Task<JsonObject[]> CollectNpc(JsonObject target, long map, string week, JsonObject[] pending)
    {
        var remaining = pending.Where(t => N(t["Npc"]) == N(target["Npc"])).ToList();
        var results = new List<JsonObject>();
        bool menu = false;
        while (remaining.Count > 0)
        {
            if (!menu)
            {
                target = await Approach(target, map);
                await W.Step("GameFieldDefaultUI", operation: "weekly_steal_menu", value: I(target["Instance"]), expect: "QuickMenuUI");
            }
            var (e, selected) = await SelectTalent(map, week, remaining.ToArray());
            long group = N(selected["skill"]!["groupId"]), npc = N(target["Npc"]);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await W.Step(O(("ui", "QuickMenuUI"), ("operation", "weekly_steal_talent"), ("value", selected["instance"]), ("items", new[] { npc, group }), ("expect", "StealInfoPopupUI")));
                    break;
                }
                catch (DailyStepException error) when (attempt < 2 && error.Kind == "rejected" && !error.MayHaveDispatched && error.Message.StartsWith("rejected: talent_wait:", StringComparison.Ordinal))
                {
                    await W.Delay(200);
                    (e, selected) = await SelectTalent(map, week, remaining.ToArray());
                    group = N(selected["skill"]!["groupId"]);
                }
            }
            var op = await W.Transact("dispatch.start", O(("kind", 1), ("map", map), ("npc", npc), ("group", group), ("week", week), ("server_now", State(e, "mainline.network", "$self")["Now"])), O(("ui", "StealInfoPopupUI"), ("operation", "weekly_steal_confirm"), ("value", npc), ("items", new[] { group })), 30);
            results.Add(O(("state", "completed"), ("id", op["id"]), ("result", op["result"])));
            remaining.RemoveAll(t => N(t["Group"]) == group);
            menu = await Settle(npc, group);
        }
        return results.ToArray();
    }
    public async Task<JsonArray> CollectMap(JsonObject checkpoint)
    {
        if (!route.Progress.Steal)
            return new();
        var e = await route.Evidence();
        long map = N(Map(e)["id"]);
        string week = S(checkpoint["weekly_reset"]);
        var results = new JsonArray();
        int limit = Rows(checkpoint["maps"]![map.ToString()]!["steals"]).Length + 1;
        for (int i = 0; i < limit; i++)
        {
            await route.FieldReady();
            e = await route.Evidence();
            Require(N(Map(e)["id"]) == map && S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "偷窃地图或周周期变化");
            var pending = Rows(checkpoint["maps"]![map.ToString()]!["steals"]);
            if (pending.Length == 0)
                return results;
            var target = Rows(R(e, "mainline.steal_npcs", "$self")).Where(n => pending.Any(t => N(t["Npc"]) == N(n["Npc"])) && (B(n["Near"]) || B(n["Reachable"]))).OrderByDescending(n => B(n["Near"])).ThenBy(n => DailyRegionRouter.D(n["Distance"])).ThenBy(n => N(n["Npc"])).FirstOrDefault();
            Require(target != null, "本图偷窃NPC尚未出现或不可达，保留服务器待办");
            var done = await CollectNpc(target!, map, week, pending);
            foreach (var r in done)
                results.Add(r.DeepClone());
            await route.FieldReady();
            checkpoint = await route.Checkpoint();
            var acknowledged = done.Select(r => (N(r["result"]!["npc"]), N(r["result"]!["group"]))).ToHashSet();
            Require(!Rows(checkpoint["maps"]![map.ToString()]!["steals"]).Any(t => acknowledged.Contains((N(t["Npc"]), N(t["Group"])))), "服务器仍显示刚才的偷窃未完成，停止而非重复消耗");
        }
        throw new StageHostException("adapter", "偷窃目标未收敛，保留待办");
    }
}

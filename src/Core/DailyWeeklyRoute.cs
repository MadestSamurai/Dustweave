using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;
namespace Dustweave;

public sealed class DailyWeeklyCategoryStop(string stage, string reason, JsonObject? metadata = null) : Exception(reason)
{
    public string Stage { get; } = stage; public JsonObject Metadata { get; } = metadata ?? new();
}

/// <summary>One journey, three independently selectable and independently recoverable categories.</summary>
public sealed class DailyWeeklyRoute
{
    public static readonly string[] Stages = ["weekly_mainline", "weekly_npc", "weekly_steal"];
    private static readonly Dictionary<string, string> Names = new() { { "weekly_mainline", "周收集" }, { "weekly_npc", "周NPC任务" }, { "weekly_steal", "每周偷窃" } };
    private readonly DailyWorkflow w; private readonly DailyFieldRoute route; private readonly Action<string, JsonObject> progress;
    private readonly Dictionary<string, HashSet<(long Pack, long Map)>> known = Stages.ToDictionary(s => s, _ => new HashSet<(long, long)>()), done = Stages.ToDictionary(s => s, _ => new HashSet<(long, long)>());
    private readonly HashSet<(long, long)> deferred = [], reentered = []; private HashSet<string> active = []; private DailyWeeklyNpc? npc; private DailyCollectionNavigator? nav; private JsonObject? checkpoint; private string week = ""; private long pack; private bool entered;
    public DailyWeeklyRoute(DailyWorkflow workflow, Action<string, JsonObject>? progress = null)
    {
        w = workflow;
        route = new(w)
        {
            RecoverEncounters = true
        };
        this.progress = progress ?? ((_, _) => { });
        route.CanSuppress = map => active.Contains("weekly_mainline") && CanRun("weekly_mainline", map);
    }
    public static string[] Select(IEnumerable<string> stages, WeeklyPreferences settings)
    {
        var requested = stages.ToArray();
        Require(requested.Length > 0 && requested.Distinct().Count() == requested.Length && requested.All(Stages.Contains), "无效的合并周任务选择");
        return Stages.Where(s => requested.Contains(s) && (s switch { "weekly_mainline" => settings.Mainline, "weekly_npc" => settings.Npc, "weekly_steal" => settings.Steal, _ => false })).ToArray();
    }
    private async Task Guard()
    {
        var e = await route.Evidence();
        Require(S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "合并周任务期间周期改变");
    }
    private JsonObject Progress(string stage)
    {
        if (stage == "weekly_npc" && npc?.Data != null)
            return O(("completed", npc.Data.Native["Completed"]), ("total", npc.Data.Native["Limit"]));
        return O(("completed", done[stage].Count), ("total", known[stage].Count));
    }
    private string Summary(string stage) => stage == "weekly_npc" ? npc?.Summary() ?? "等待读取周NPC任务" : $"{Names[stage]}已核对 {done[stage].Count}/{known[stage].Count} 张地图";
    private Dictionary<string, long[]> Maps() => new() { { "weekly_mainline", active.Contains("weekly_mainline") ? route.Catalog.Maps(pack) : [] }, { "weekly_steal", active.Contains("weekly_steal") ? route.Catalog.StealMaps(pack) : [] } };
    private Dictionary<string, long[]> MapJobs() => Maps().Where(p => active.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Where(id => !done[p.Key].Contains((pack, id))).ToArray());
    private async Task Refresh(bool cache = false)
    {
        var maps = Maps();
        var all = maps.Values.SelectMany(v => v).Distinct().ToArray();
        if (all.Length == 0)
        {
            checkpoint = null;
            return;
        }
        bool oldSteal = route.Progress.Steal, oldOnly = route.Progress.OnlySteal;
        route.Progress.Steal = active.Contains("weekly_steal");
        route.Progress.OnlySteal = false;
        try
        {
            checkpoint = cache ? route.Progress.Cached(pack, all, week, true) : await route.Progress.Query(pack, all);
        }
        finally { route.Progress.Steal = oldSteal; route.Progress.OnlySteal = oldOnly; }
        foreach (var pair in maps)
        {
            known[pair.Key].UnionWith(pair.Value.Select(id => (pack, id)));
            if (checkpoint == null)
                continue;
            var keys = pair.Key == "weekly_steal" ? new[] { "steals" } : new[] { "drops", "monsters" };
            done[pair.Key].ExceptWith(pair.Value.Select(id => (pack, id)));
            done[pair.Key].UnionWith(pair.Value.Where(id => keys.All(key => (checkpoint["maps"]![id.ToString()]![key] as JsonArray)?.Count == 0)).Select(id => (pack, id)));
        }
    }
    private async Task<bool> Prepare(long currentPack)
    {
        pack = currentPack;
        nav = null;
        entered = false;
        await Refresh(true);
        if (MapJobs().GetValueOrDefault("weekly_mainline", []).Length > 0 && !w.Settings.Weekly.WalkCollect)
            try
            {
                route.Quota(await route.Evidence());
            }
            catch (DailyQuotaExhausted) { throw new DailyWeeklyCategoryStop("weekly_mainline", "吸收次数已用完，次日接续", O(("waiting_daily_reset", true))); }
        bool needed = active.Contains("weekly_npc");
        if (npc?.Data is DailyNpcData d)
        {
            needed = needed && (d.Native["Posted"]!.AsArray().Select(N).Any(id => d.Rows.TryGetValue(id, out var row) && N(row["packId"]) == pack) || d.Current(pack) != null);
            if (N(d.Native["Completed"]) >= N(d.Native["Limit"]))
                needed = false;
        }
        if (!needed && (checkpoint != null && !MapJobs().Values.Any(v => v.Length > 0) || !Maps().Values.Any(v => v.Length > 0)))
            return false;
        await route.Enter(pack);
        entered = true;
        if (checkpoint == null || MapJobs().Values.Any(v => v.Length > 0))
            await Refresh();
        nav = new(route, pack, await route.Evidence());
        if (needed)
        {
            npc ??= new(route, w.Settings.Weekly.NpcHunting);
            try
            {
                await npc.Prepare(pack);
            }
            catch (DailyNpcUnavailable e) { throw new DailyWeeklyCategoryStop("weekly_npc", e.Message); }
        }
        return true;
    }
    private async Task<Dictionary<string, long[]>> Jobs()
    {
        var jobs = MapJobs();
        if (!active.Contains("weekly_npc") && npc?.Data is DailyNpcData d)
        {
            foreach (long id in jobs.GetValueOrDefault("weekly_mainline", []))
                if (d.Protects(id))
                    deferred.Add((pack, id));
            if (jobs.ContainsKey("weekly_mainline"))
                jobs["weekly_mainline"] = jobs["weekly_mainline"].Where(id => !deferred.Contains((pack, id))).ToArray();
        }
        if (active.Contains("weekly_npc") && npc?.Data != null && N(npc.Data.Native["Pack"]) == pack)
        {
            try
            {
                if (npc.Data.Current(pack) == null && !npc.ExhaustedPacks.Contains(pack))
                    await npc.Prepare(pack);
                jobs["weekly_npc"] = await npc.Targets(pack);
            }
            catch (DailyNpcUnavailable e) { throw new DailyWeeklyCategoryStop("weekly_npc", e.Message); }
        }
        return jobs;
    }
    private bool CanRun(string stage, long map) => stage != "weekly_mainline" || npc?.Data == null || !npc.Data.Protects(map);
    private async Task Perform(string stage, long map)
    {
        if (stage == "weekly_npc")
        {
            try
            {
                await npc!.AtMap(pack, map);
            }
            catch (DailyNpcUnavailable e) { throw new DailyWeeklyCategoryStop(stage, e.Message); }
            return;
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Guard();
            bool oldSteal = route.Progress.Steal, oldOnly = route.Progress.OnlySteal;
            route.Progress.Steal = route.Progress.OnlySteal = stage == "weekly_steal";
            try
            {
                if (stage == "weekly_steal")
                    await new DailyWeeklySteal(route).CollectMap(await route.Checkpoint());
                else
                    await route.Gather(w.Settings.Weekly.WalkCollect);
            }
            catch (DailyQuotaExhausted) { throw new DailyWeeklyCategoryStop(stage, "吸收次数已用完，次日接续", O(("waiting_daily_reset", true))); }
            catch (DailyCollectionTargetsRemain) { }
            finally { route.Progress.Steal = oldSteal; route.Progress.OnlySteal = oldOnly; }
            await Refresh();
            if (!MapJobs().GetValueOrDefault(stage, []).Contains(map))
                break;
            if (attempt > 0 || !reentered.Add((pack, map)))
                throw new DailyWeeklyCategoryStop(stage, $"卡带 {pack} 地图 {map} 重进后仍有待办，保留服务器进度", O(("remaining", checkpoint!["maps"]![map.ToString()]), ("reentry_attempted", true)));
            Emit(stage, $"地图 {map} 收集状态不一致，切换卡带重进核对", true);
            nav = await route.ReloadMap(pack, map, week);
            await Guard();
            await Refresh();
            if (!MapJobs().GetValueOrDefault(stage, []).Contains(map))
                break;
        }
        if (npc != null && active.Contains("weekly_npc"))
            await npc.Query(pack);
    }
    private void Emit(string stage, string detail, bool running)
    {
        var value = Progress(stage);
        value["detail"] = detail;
        value["active"] = running;
        progress(stage, value);
    }
    private async Task FinishPack()
    {
        long target = pack switch
        {
            6 => 601,
            14 => 141,
            1003 => 10031,
            _ => 0
        };
        if (!entered || nav == null || target == 0)
            return;
        for (int i = 0; i < 32; i++)
        {
            await Guard();
            if (await nav.Observe() == target)
                return;
            var p = await nav.Plan([], target);
            var actions = Rows(p["actions"]);
            Require(actions.Length > 0, "周路线未能安全返回入口");
            await nav.Step(actions[0]);
        }
        throw new DailyTravelBlocked("周路线返回入口超时");
    }
    private JsonObject Finish(string stage)
    {
        if (stage == "weekly_npc")
        {
            if (npc?.Data == null)
                return O(("state", "partial"), ("reason", "no_npc_observation"), ("detail", "所选范围没有可读取的周NPC任务"));
            bool complete = N(npc.Data.Native["Completed"]) >= N(npc.Data.Native["Limit"]);
            return O(("state", complete ? "completed" : "partial"), ("detail", npc.Summary() + (complete ? "" : " · 范围内暂无符合设置的可执行任务")), ("reason", complete ? "weekly_npc_verified" : "no_eligible_quests"), ("source", "TodayQuestInfoResponse"));
        }
        var pending = known[stage].Except(done[stage]).OrderBy(p => p.Pack).ThenBy(p => p.Map).ToArray();
        return O(("state", pending.Length > 0 ? "partial" : "completed"), ("detail", Summary(stage) + (pending.Length > 0 && deferred.Count > 0 ? " · NPC前置未完成，暂缓相关压制" : "")), ("reason", pending.Length > 0 && deferred.Count > 0 ? "protected_npc_targets" : "weekly_route_verified"), ("source", "game_network"), ("pending_maps", Array(pending.Select(p => (JsonNode)new JsonArray(p.Pack, p.Map)))));
    }
    public async Task<JsonObject> Run(string[] requested)
    {
        var selected = Select(requested, w.Settings.Weekly);
        active = selected.ToHashSet();
        if (active.Count > 0) await DailyWeeklyNpcBoard.Close(w);
        var result = new JsonObject();
        foreach (string s in requested.Except(selected))
            result[s] = O(("state", "skipped"), ("reason", "disabled"), ("detail", "已关闭"));
        week = S(R(await route.Evidence(), "mainline.reset", "GetWeeklyResetTime().Ticks"));
        Require(week.Length > 0, "缺少周周期");
        var settings = w.Settings.Weekly;
        var packs = Enumerable.Range(settings.FirstChapter, settings.LastChapter - settings.FirstChapter + 1).Select(i => (long)i).Concat(route.Catalog.Extra(settings.CharacterCartridges, settings.EventCartridges));
        void EmitAll(string place)
        {
            foreach (string s in selected)
                Emit(s, place + " · " + (result[s] == null ? Summary(s) : S(result[s]!["detail"] ?? result[s]!["reason"])), active.Contains(s));
        }
        void Stop(DailyWeeklyCategoryStop e)
        {
            Require(active.Remove(e.Stage), "未知周任务停止项");
            var r = O(("state", "partial"), ("reason", e.Message), ("detail", e.Message));
            foreach (var pair in e.Metadata)
                r[pair.Key] = Copy(pair.Value);
            result[e.Stage] = r;
        }
        foreach (long current in packs)
        {
            await Guard();
            if (active.Count == 0)
                break;
            EmitAll("规划卡带 " + current);
            bool prepared = false;
            while (active.Count > 0)
            {
                try
                {
                    prepared = await Prepare(current);
                    break;
                }
                catch (DailyWeeklyCategoryStop e) { Stop(e); EmitAll("卡带 " + current); }
            }
            if (!prepared)
                continue;
            bool finished = false;
            for (int i = 0; i < 256; i++)
            {
                await Guard();
                try
                {
                    var jobs = await Jobs();
                    if (!jobs.Values.Any(v => v.Length > 0))
                    {
                        finished = true;
                        break;
                    }
                    long map = await nav!.Observe();
                    EmitAll($"卡带 {pack} · 地图 {map}");
                    bool acted = false;
                    foreach (string stage in new[] { "weekly_npc", "weekly_steal", "weekly_mainline" })
                    {
                        if (!active.Contains(stage) || !jobs.GetValueOrDefault(stage, []).Contains(map) || !CanRun(stage, map))
                            continue;
                        await Guard();
                        Emit(stage, $"卡带 {pack} · 地图 {map} · 正在{Names[stage]}", true);
                        await Perform(stage, map);
                        acted = true;
                        EmitAll($"卡带 {pack} · 地图 {map}");
                        if (stage == "weekly_npc")
                            break;
                    }
                    if (acted)
                        continue;
                    var pending = jobs.Values.SelectMany(v => v).Where(id => id != map).Distinct().ToArray();
                    Require(pending.Length > 0, "共享周路线没有可执行动作，保留未完成项目");
                    var plan = await nav.Plan(pending);
                    var actions = Rows(plan["actions"]);
                    Require(actions.Length > 0, "所选周任务没有可用地图路径");
                    await nav.Step(actions[0]);
                }
                catch (DailyWeeklyCategoryStop e) { Stop(e); EmitAll("继续其他已选项目"); if (active.Count == 0) { finished = true; break; } }
            }
            Require(finished, "共享周路线超过恢复次数，已保留分项进度");
            await FinishPack();
        }
        foreach (string s in selected)
        {
            result[s] ??= Finish(s);
            Emit(s, S(result[s]!["detail"] ?? result[s]!["reason"]), false);
        }
        return O(("state", result.All(p => S(p.Value!["state"]) is "completed" or "skipped") ? "completed" : "partial"), ("stages", result), ("engine", "dotnet-weekly-route-v1"));
    }
    public async Task<JsonObject> SyncProgress()
    {
        var settings = w.Settings.Weekly;
        var completed = new JsonArray();
        route.Progress.Steal = settings.Steal;
        foreach (long p in Enumerable.Range(settings.FirstChapter, settings.LastChapter - settings.FirstChapter + 1).Select(i => (long)i).Concat(route.Catalog.Extra(settings.CharacterCartridges, settings.EventCartridges)))
        {
            await route.Enter(p);
            var checkpoint = await route.Checkpoint();
            completed.Add(O(("pack", p), ("result", checkpoint)));
        }
        return DailyWorkflow.Completed(completed);
    }
}

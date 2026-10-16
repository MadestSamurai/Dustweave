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
    private bool catalogReady;
    private Dictionary<string, DailyDisplayName> locationLabels = [];
    private JsonNode Labels(string detail)
    {
        var ids = System.Text.RegularExpressions.Regex.Matches(detail, @"\d+").Select(m => m.Value).ToHashSet();
        return System.Text.Json.JsonSerializer.SerializeToNode(locationLabels.Where(p => ids.Contains(p.Key[1..])).ToDictionary(p => p.Key, p => p.Value))!;
    }
    private readonly HashSet<long> recoveredPacks = [];
    private readonly HashSet<(long Pack, long Map)> storyLocked = [];
    private readonly HashSet<(long Pack, long Map)> waitingAbsorption = [];
    // Deferrals belong to this run only. A retry starts from fresh server progress,
    // never from a persistent blacklist of maps or cartridges.
    private readonly Dictionary<string, Dictionary<(long Pack, long Map), JsonObject>> failures = Stages.ToDictionary(s => s, _ => new Dictionary<(long, long), JsonObject>());
    public JsonObject VerifiedResults()
    {
        var result = new JsonObject();
        if (!catalogReady) return result;
        foreach (string stage in active)
        {
            bool complete = stage == "weekly_npc"
                ? npc?.Data != null && N(npc.Data.Native["Limit"]) > 0 && N(npc.Data.Native["Completed"]) == N(npc.Data.Native["Limit"])
                : known[stage].Count > 0 && known[stage].SetEquals(done[stage]);
            if (complete)
            {
                result[stage] = Finish(stage);
                result[stage]!["week"] = week;
                result[stage]!["verified_maps"] = Array(done[stage].OrderBy(p => p.Pack).ThenBy(p => p.Map).Select(p => (JsonNode)new JsonArray(p.Pack, p.Map)));
            }
        }
        return result;
    }
    public DailyWeeklyRoute(DailyWorkflow workflow, Action<string, JsonObject>? progress = null)
    {
        w = workflow;
        route = new(w)
        {
            RecoverEncounters = true
        };
        this.progress = progress ?? ((_, _) => { });
        route.CanSuppress = map => (active.Contains("weekly_mainline") || active.Contains("weekly_npc")) && CanRun("weekly_mainline", map);
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
    private Dictionary<string, long[]> MapJobs() => Maps().Where(p => active.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Where(id => !done[p.Key].Contains((pack, id)) && !failures[p.Key].ContainsKey((pack, id)) && (p.Key != "weekly_mainline" || !storyLocked.Contains((pack, id)) && !waitingAbsorption.Contains((pack, id)))).ToArray());
    private void Defer(string stage, long map, string reason, JsonObject? metadata = null)
    {
        var failure = O(("pack", pack), ("map", map), ("reason", reason), ("at", w.Driver.UtcTicks), ("metadata", metadata));
        failures[stage][(pack, map)] = failure;
        w.Driver.Diagnostics.Failure("weekly_location_deferred", O(("stage", stage), ("message", reason), ("location", failure)));
        Emit(stage, $"卡带 {pack}" + (map > 0 ? $" · 地图 {map}" : "") + " 暂缓：" + reason + "；继续后续地点", true);
    }
    private void DeferPack(string reason, JsonObject? metadata = null)
    {
        w.Driver.Diagnostics.Event("weekly_cartridge_deferred", O(("pack", pack), ("reason", reason), ("metadata", metadata)));
        foreach (string stage in active)
        {
            if (stage == "weekly_npc")
            {
                if (npc?.Data == null || N(npc.Data.Native["Completed"]) < N(npc.Data.Native["Limit"]))
                    Defer(stage, 0, reason, metadata);
                continue;
            }
            foreach (var location in known[stage].Except(done[stage]).Where(p => p.Pack == pack).OrderBy(p => p.Map))
                if (!failures[stage].ContainsKey(location)) Defer(stage, location.Map, reason, metadata);
        }
    }
    private JsonObject IncludeDeferrals(string stage, JsonObject result)
    {
        var unresolved = failures[stage].Where(p => !done[stage].Contains(p.Key)).OrderBy(p => p.Key.Pack).ThenBy(p => p.Key.Map).Select(p => p.Value).ToArray();
        if (unresolved.Length == 0) return result;
        result["deferred_locations"] = Array(unresolved);
        if (S(result["state"]) == "partial")
        {
            string locations = string.Join("、", unresolved.Select(p => "卡带 " + N(p["pack"]) + (N(p["map"]) > 0 ? " · 地图 " + N(p["map"]) : "")).Distinct());
            result["detail"] = S(result["detail"]) + $" · {locations} 有暂缓项目，原因已保留，可之后补跑";
            if (!B(result["waiting_daily_reset"])) result["reason"] = "weekly_locations_deferred";
        }
        return result;
    }
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
        route.Progress.Steal = maps["weekly_steal"].Length > 0;
        route.Progress.OnlySteal = false;
        try
        {
            checkpoint = cache ? null : await route.Progress.Query(pack, all);
            var cachedMaps = new JsonObject(); bool missing = false;
            foreach (var pair in maps.Where(p => p.Value.Length > 0))
            {
                JsonObject? observed = checkpoint;
                if (cache)
                {
                    route.Progress.Steal = route.Progress.OnlySteal = pair.Key == "weekly_steal";
                    observed = route.Progress.Cached(pack, pair.Value, week, true);
                }
                known[pair.Key].UnionWith(pair.Value.Select(id => (pack, id)));
                if (observed == null) { missing = true; continue; }
                var keys = pair.Key == "weekly_steal" ? new[] { "steals" } : new[] { "drops", "monsters" };
                if (pair.Key == "weekly_mainline")
                    foreach (long id in pair.Value)
                        if (observed["maps"]![id.ToString()]!["story_locked"] is JsonArray { Count: > 0 }) storyLocked.Add((pack, id));
                        else storyLocked.Remove((pack, id));
                done[pair.Key].ExceptWith(pair.Value.Select(id => (pack, id)));
                done[pair.Key].UnionWith(pair.Value.Where(id => (pair.Key != "weekly_mainline" || !storyLocked.Contains((pack, id))) && keys.All(key => (observed["maps"]![id.ToString()]![key] as JsonArray)?.Count == 0)).Select(id => (pack, id)));
                foreach (long id in pair.Value)
                {
                    if (cachedMaps[id.ToString()] is not JsonObject state) cachedMaps[id.ToString()] = state = new();
                    foreach (string key in keys.Append("story_locked"))
                        if (observed["maps"]![id.ToString()]![key] is JsonNode value) state[key] = Copy(value);
                }
            }
            if (cache && !missing) checkpoint = O(("maps", cachedMaps));
        }
        finally { route.Progress.Steal = oldSteal; route.Progress.OnlySteal = oldOnly; }
    }
    private async Task<bool> Prepare(long currentPack)
    {
        pack = currentPack;
        nav = null;
        entered = false;
        await Refresh(true);
        // A prior interrupted absorption must never be hidden by a completed cache.
        // The recovery pass has already queried only the affected maps; refresh the
        // rest of this cartridge once when normal routing reaches it.
        if (recoveredPacks.Contains(pack)) checkpoint = null;
        await DeferAbsorptionOnlyMaps();
        bool needed = active.Contains("weekly_npc") && !failures["weekly_npc"].Keys.Any(p => p.Pack == pack);
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
            await RunNpc(() => npc.Prepare(pack));
        }
        return true;
    }
    private async Task<Dictionary<string, long[]>> Jobs()
    {
        await DeferAbsorptionOnlyMaps();
        var jobs = MapJobs();
        bool npcDeferred = failures["weekly_npc"].Keys.Any(p => p.Pack == pack);
        if ((!active.Contains("weekly_npc") || npcDeferred) && npc?.Data is DailyNpcData d)
        {
            foreach (long id in jobs.GetValueOrDefault("weekly_mainline", []))
                if (d.Protects(id))
                    deferred.Add((pack, id));
            if (jobs.ContainsKey("weekly_mainline"))
                jobs["weekly_mainline"] = jobs["weekly_mainline"].Where(id => !deferred.Contains((pack, id))).ToArray();
        }
        if (active.Contains("weekly_npc") && !npcDeferred && npc?.Data != null && N(npc.Data.Native["Pack"]) == pack)
        {
            await RunNpc(async () =>
            {
                if (npc.Data.Current(pack) == null && !npc.ExhaustedPacks.Contains(pack))
                    await npc.Prepare(pack);
                jobs["weekly_npc"] = await npc.Targets(pack);
            });
        }
        return jobs;
    }
    private async Task DeferAbsorptionOnlyMaps()
    {
        if (!active.Contains("weekly_mainline") || w.Settings.Weekly.WalkCollect || checkpoint == null || Used(await route.Evidence(), 4) < 21) return;
        foreach (long map in route.Catalog.Maps(pack))
            if (checkpoint["maps"]?[map.ToString()] is JsonObject state && state["drops"] is JsonArray { Count: > 0 }
                && state["monsters"] is JsonArray { Count: 0 }) waitingAbsorption.Add((pack, map));
    }
    public static readonly string[] StageOrder = ["weekly_mainline", "weekly_npc", "weekly_steal"];
    private async Task RecoverInterruptedMaps(long[] availablePacks)
    {
        var locations = route.Progress.PendingMaps().Select(op => DailyCollectionProgress.RecoveryLocation(op)!.Value).Distinct().ToArray();
        foreach (var group in locations.GroupBy(p => p.Pack))
        {
            await Guard();
            pack = group.Key;
            var maps = group.Select(p => p.Map).ToArray();
            foreach (string stage in active) Emit(stage, "正在恢复" + string.Join("、", maps.Select(id => $"卡带 {pack} · 地图 {id}")) + " 的当前进度", true);
            try
            {
                if (!availablePacks.Contains(pack)) throw new DailyTravelBlocked("对应卡带当前不可进入");
                await route.Enter(pack);
                await route.FieldReady(settle: 0);
                await route.Progress.Query(pack, maps, cache: false);
                recoveredPacks.Add(pack);
            }
            catch (Exception e) when (e is DailyTravelBlocked or DailyCollectionReadUnavailable)
            {
                await Guard();
                foreach (long map in maps)
                    foreach (string stage in active) Defer(stage, map, "当前无法读取此地图的收集进度，已保留待办：" + e.Message);
            }
        }
    }
    private bool CanRun(string stage, long map) => stage != "weekly_mainline" || npc?.Data == null || !npc.Data.Protects(map);
    private async Task RunNpc(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is DailyNpcUnavailable or DailyTravelBlocked || e is DailyStepException step && DailyWeeklyNpc.RecoverableRejection(step))
        {
            // Only bounded quest/navigation failures are local. Identity changes,
            // unknown dispatched operations and unrelated confirmations still stop.
            await Guard();
            await DailyWeeklyNpcBoard.Close(w);
            await route.FieldReady(15);
            await w.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
            npc!.ExhaustedPacks.Add(pack);
            Defer("weekly_npc", N(Map(await route.Evidence())["id"]), e.Message);
        }
    }
    private async Task Perform(string stage, long map)
    {
        if (stage == "weekly_npc")
        {
            await RunNpc(async () => { await npc!.AtMap(pack, map); });
            return;
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            JsonArray? deferredPickups = null;
            bool recoveredAbsorption = false;
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
            catch (DailyQuotaExhausted)
            {
                // Suppression may already have succeeded. Re-read it and defer only
                // the remaining drops, never stop other maps or weekly categories.
                await Refresh();
                if (checkpoint?["maps"]?[map.ToString()]?["monsters"] is JsonArray { Count: > 0 })
                    Defer(stage, map, "吸收次数用完，本地图压制仍未确认；继续其他地图");
                else waitingAbsorption.Add((pack, map));
                break;
            }
            catch (DailyCollectionTargetsRemain error) { deferredPickups = error.DeferredPickups; }
            catch (StageHostException error) when (error.Kind == "pending" && route.Progress.PendingMaps()
                .Any(op => DailyCollectionProgress.RecoveryLocation(op) == (pack, map)))
            {
                // A live timeout follows the same local recovery path as a restart.
                // Never resend the old command: query current targets, then replan.
                await route.FieldReady(settle: 0);
                await route.Progress.Query(pack, [map], cache: false);
                recoveredAbsorption = true;
            }
            finally { route.Progress.Steal = oldSteal; route.Progress.OnlySteal = oldOnly; }
            await Refresh();
            if (!MapJobs().GetValueOrDefault(stage, []).Contains(map))
                break;
            if (recoveredAbsorption)
            {
                if (attempt == 0) continue;
                Defer(stage, map, "收集后已重新读取进度，本次不再重复尝试；继续其他地图，之后可补跑");
                break;
            }
            if (deferredPickups is { Count: > 0 })
            {
                Defer(stage, map, "可达物品已处理，部分拾取点当前不可达；继续其他地图", O(("targets", deferredPickups), ("remaining", checkpoint!["maps"]![map.ToString()])));
                break;
            }
            if (attempt > 0 || !reentered.Add((pack, map)))
            {
                Defer(stage, map, "重进后仍有待办，保留服务器进度", O(("remaining", checkpoint!["maps"]![map.ToString()]), ("reentry_attempted", true)));
                break;
            }
            Emit(stage, $"地图 {map} 收集状态不一致，切换卡带重进核对", true);
            nav = await route.ReloadMap(pack, map, week);
            await Guard();
            await Refresh();
            if (!MapJobs().GetValueOrDefault(stage, []).Contains(map))
                break;
        }
        if (npc != null && active.Contains("weekly_npc") && !failures["weekly_npc"].Keys.Any(p => p.Pack == pack))
            await RunNpc(async () => { await npc.Query(pack); });
    }
    private void Emit(string stage, string detail, bool running)
    {
        var value = Progress(stage);
        value["detail"] = detail;
        value["location_labels"] = Labels(detail);
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
            if (actions.Length == 0) throw new DailyTravelBlocked("周路线未能安全返回入口");
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
        if (stage == "weekly_mainline" && pending.Any(waitingAbsorption.Contains))
            return O(("state", "partial"), ("detail", Summary(stage) + " · 吸收次数用完，已继续处理可执行的压制；剩余收集次日接续"),
                ("reason", "absorption_exhausted"), ("waiting_daily_reset", true), ("source", "game_network"),
                ("pending_maps", Array(pending.Select(p => (JsonNode)new JsonArray(p.Pack, p.Map)))));
        if (stage == "weekly_mainline" && storyLocked.Count > 0)
            return O(("state", "partial"), ("detail", Summary(stage) + " · 部分地图尚未推进到敌人出现阶段，完成对应卡带剧情后再收集"),
                ("reason", "collection_story_locked"), ("source", "game_network"), ("pending_maps", Array(pending.Select(p => (JsonNode)new JsonArray(p.Pack, p.Map)))));
        return O(("state", pending.Length > 0 ? "partial" : "completed"), ("detail", Summary(stage) + (pending.Length > 0 && deferred.Count > 0 ? " · NPC前置未完成，暂缓相关压制" : "")), ("reason", pending.Length > 0 && deferred.Count > 0 ? "protected_npc_targets" : "weekly_route_verified"), ("source", "game_network"), ("collection_coverage", DailyCollectionCatalog.CoverageRevision), ("pending_maps", Array(pending.Select(p => (JsonNode)new JsonArray(p.Pack, p.Map)))));
    }
    public async Task<JsonObject> Run(string[] requested)
    {
        var selected = Select(requested, w.Settings.Weekly);
        active = selected.ToHashSet();
        try
        {
            var display = await w.PrepareTables("location-labels", Path.Combine(w.Directory, "data", "locations"),
                new Dictionary<string, string[]> { ["locations"] = ["PackTable", "MapTable", "NameTextTable"] });
            JsonObject[] Table(string name) => Rows(JsonNode.Parse(File.ReadAllText(display.PathFor("locations/" + name + ".json"))));
            locationLabels = DailyLocationLabels.Build(Table("PackTable"), Table("MapTable"), Table("NameTextTable"));
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or StageHostException { Kind: "adapter" })
        { w.Driver.Diagnostics.Event("location_names_unavailable", O(("message", error.Message))); }
        if (active.Count > 0) await DailyWeeklyNpcBoard.Close(w);
        var result = new JsonObject();
        foreach (string s in requested.Except(selected))
            result[s] = O(("state", "skipped"), ("reason", "disabled"), ("detail", "已关闭"));
        var evidence = await route.Evidence();
        week = S(R(evidence, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        Require(week.Length > 0, "缺少周周期");
        var packs = route.Catalog.AvailablePacks(evidence);
        var unavailableEvents = route.Catalog.UnavailableEventPacks(evidence);
        if (unavailableEvents.Length > 0)
            w.Driver.Diagnostics.Event("weekly_event_cartridges_unavailable", O(("packs", unavailableEvents), ("reason", "native_event_schedule_closed")));
        if (active.Contains("weekly_npc")) npc = new(route, w.Settings.Weekly.NpcHunting) { AvailablePacks = packs };
        foreach (long p in packs)
        {
            if (active.Contains("weekly_mainline")) known["weekly_mainline"].UnionWith(route.Catalog.Maps(p).Select(m => (p, m)));
            if (active.Contains("weekly_steal")) known["weekly_steal"].UnionWith(route.Catalog.StealMaps(p).Select(m => (p, m)));
        }
        catalogReady = true;
        // Restore each selected category before reporting progress. Missing theft
        // observations must not make verified collection appear to start from zero.
        foreach (long p in packs) { pack = p; await Refresh(true); }
        if (active.Count > 0) await RecoverInterruptedMaps(packs);
        void EmitAll(string place)
        {
            foreach (string s in selected)
                Emit(s, place + " · " + (result[s] == null ? Summary(s) : S(result[s]!["detail"] ?? result[s]!["reason"])), active.Contains(s));
        }
        void Stop(DailyWeeklyCategoryStop e)
        {
            Require(active.Remove(e.Stage), "未知周任务停止项");
            var r = Finish(e.Stage);
            r["state"] = "partial";
            r["reason"] = e.Message;
            r["detail"] = e.Message;
            foreach (var pair in e.Metadata)
                r[pair.Key] = Copy(pair.Value);
            result[e.Stage] = r;
        }
        var pendingPacks = packs.ToList();
        while (pendingPacks.Count > 0)
        {
            long current = npc != null && active.Contains("weekly_npc") ? npc.OrderPacks(pendingPacks)[0] : pendingPacks[0];
            pendingPacks.Remove(current);
            await Guard();
            if (active.Count == 0)
                break;
            EmitAll("规划卡带 " + current);
            try
            {
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
                        foreach (string stage in StageOrder)
                        {
                            if (!active.Contains(stage) || !jobs.GetValueOrDefault(stage, []).Contains(map) || !CanRun(stage, map))
                                continue;
                            await Guard();
                            Emit(stage, $"卡带 {pack} · 地图 {map} · 正在{Names[stage]}", true);
                            await Perform(stage, map);
                            acted = true;
                            EmitAll($"卡带 {pack} · 地图 {map}");
                            // Collection/suppression can advance NPC objectives. Re-read jobs before the next category.
                            break;
                        }
                        if (acted)
                            continue;
                        var pending = jobs.Values.SelectMany(v => v).Where(id => id != map).Distinct().ToArray();
                        if (pending.Length == 0) throw new DailyTravelBlocked("共享周路线没有可执行动作，保留未完成项目");
                        var plan = await nav.Plan(pending);
                        var actions = Rows(plan["actions"]);
                        if (actions.Length == 0) throw new DailyTravelBlocked("所选周任务没有可用地图路径");
                        await nav.Step(actions[0]);
                    }
                    catch (DailyWeeklyCategoryStop e) { Stop(e); EmitAll("继续其他已选项目"); if (active.Count == 0) { finished = true; break; } }
                }
                if (!finished) throw new DailyTravelBlocked("共享周路线超过恢复次数，已保留分项进度");
                await FinishPack();
                // A lower-paying quest in this pack can wait while another pack
                // offers more gold. Revisit only for NPC work; completed collection
                // and theft retain their server-backed per-map progress.
                if (active.Contains("weekly_npc") && npc?.HasOffers(current) == true && !failures["weekly_npc"].Keys.Any(p => p.Pack == current))
                    pendingPacks.Add(current);
            }
            catch (Exception e) when (e is DailyTravelBlocked or DailyCollectionReadUnavailable)
            {
                // Only a bounded, non-consuming navigation failure is local.
                // Identity loss, uncertain talent receipts and cancellation propagate.
                await Guard();
                npc?.ExhaustedPacks.Add(pack);
                DeferPack(e.Message);
            }
            catch (DailyStepException e) when (e.Kind == "deferred" && e.DefinitelyNotDispatched)
            {
                var proof = await w.Driver.TakeCollectionDeferralAsync();
                if (proof == null || N(proof["pack"]) != pack || S(proof["weekly_reset"]) != week) throw;
                await Guard();
                npc?.ExhaustedPacks.Add(pack);
                DeferPack(S(proof["detail"]), proof);
            }
        }
        foreach (string s in selected)
        {
            result[s] ??= Finish(s);
            IncludeDeferrals(s, result[s]!.AsObject());
            if (unavailableEvents.Length > 0)
            {
                result[s]!["unavailable_event_packs"] = Array(unavailableEvents.Select(id => (JsonNode)JsonValue.Create(id)!));
                result[s]!["detail"] = S(result[s]!["detail"]) + " · 未开放活动卡带已略过：" + string.Join("、", unavailableEvents.Select(id => "卡带 " + id));
            }
            result[s]!["location_labels"] = Labels(S(result[s]!["detail"]));
            Emit(s, S(result[s]!["detail"] ?? result[s]!["reason"]), false);
        }
        return O(("state", result.All(p => S(p.Value!["state"]) is "completed" or "skipped") ? "completed" : "partial"), ("stages", result), ("engine", "dotnet-weekly-route-v1"));
    }
    public async Task<JsonObject> SyncProgress()
    {
        var settings = w.Settings.Weekly;
        var completed = new JsonArray();
        route.Progress.Steal = settings.Steal;
        foreach (long p in route.Catalog.AvailablePacks(await route.Evidence()))
        {
            await route.Enter(p);
            var checkpoint = await route.Checkpoint();
            completed.Add(O(("pack", p), ("result", checkpoint)));
        }
        return DailyWorkflow.Completed(completed);
    }
}

using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed partial class DailyFieldRoute
{
    public async Task<JsonObject?> Walk(int target, long? destination = null, int storyRetries = 0)
    {
        await FieldReady();
        var before = await Evidence();
        await Protection(before);
        before = await Evidence();
        long origin = N(Map(before)["id"]);
        bool protectedRoute = NeedsStealth(before);
        double? initial = Rows(Travel(before)["Approaches"]).Where(p => N(p["Instance"]) == target && B(p["Reachable"])).Select(p => (double?)DailyRegionRouter.D(p["Distance"])).FirstOrDefault();
        double start = W.Time, budget = DailyTravelProgress.Budget(initial), end = start + budget, hard = end + 120;
        bool nudged = false;
        var progress = new DailyTravelProgress(start);
        JsonObject current = before;
        async Task<JsonObject?> Resume(string reason)
        {
            var logical = new[] { "gate", "waypoint" }.SelectMany(kind => FieldRows(before, kind).Where(r => N(r["instance"]) == target).Select(r => (Kind: kind, Object: N(r[ObjectPath])))).ToArray();
            try { await FieldReady(); }
            catch (StageHostException ex) when (ex.Kind == "adapter")
            {
                await Failure(current, target, "field_transition_not_ready", O(("destination", destination), ("trigger", reason), ("attempt", storyRetries), ("error", ex.Message)));
                throw;
            }
            var fresh = await Evidence();
            Identity(before, fresh);
            long map = N(Map(fresh)["id"]);
            if (destination.HasValue && map == destination)
                return null;
            if (storyRetries >= 3)
            {
                await Failure(fresh, target, "field_transition_repeated", O(("destination", destination), ("trigger", reason), ("attempt", storyRetries)));
                throw new DailyTravelBlocked("field.route-interrupted: 地图操作界面反复切换，重新选择路线。");
            }
            if (map != origin || logical.Length != 1)
                throw new DailyTravelBlocked("field.route-interrupted: 地图或入口已经改变，需要重新观察路线。");
            var matches = FieldRows(fresh, logical[0].Kind).Where(r => N(r[ObjectPath]) == logical[0].Object).ToArray();
            if (matches.Length != 1 || logical[0].Kind == "gate" && destination.HasValue && N(matches[0][DestinationPath]) != destination)
                throw new DailyTravelBlocked("field.route-interrupted: 原入口已不可用，需要重新选择路线。");
            // A transition can replace the Unity instance without changing the logical
            // gate. Resolve it again; never send an old-scene instance back to the game.
            return await Walk(I(matches[0]["instance"]), destination, storyRetries + 1);
        }
        try
        {
            try
            {
                await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", target), ("require_stealth", protectedRoute)));
            }
            catch (Exception ex) when (ex.Message.Contains("travel_unreachable:", StringComparison.Ordinal)) { await Failure(before, target, ex.Message); throw new DailyTravelBlocked(ex.Message); }
            while (W.Time < end)
            {
                current = await Evidence();
                Identity(before, current);
                var frame = current["Frame"]!.AsObject();
                if (await Encounter(frame) || DailyNavigationDecision.StoryAction(frame, DailyNavigationPolicy.Load()) != null)
                    return await Resume("encounter_or_story");
                // Hidden/disabled field controls are a presentation transition, not
                // evidence of a stuck character. Do not sample stall timers or cancel
                // navigation until the actual field is controllable again.
                if (!FieldInputReady(frame))
                    return await Resume("field_input_unavailable");
                long map = N(Map(current)["id"]);
                if (destination.HasValue && map == destination)
                {
                    await FieldReady();
                    return null;
                }
                Require(map == origin, "导航期间地图改变，未重发旧场景操作");
                if (destination == null)
                {
                    var arrived = FieldRows(current, "waypoint").SingleOrDefault(r => N(r["instance"]) == target && B(r[WaypointNear]));
                    if (arrived != null)
                    {
                        await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                        return arrived;
                    }
                }
                if (protectedRoute && Remaining(current, 17) <= 4)
                {
                    await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                    double pause = W.Time;
                    await Skill(17, 4);
                    var renewed = await Evidence();
                    Identity(before, renewed);
                    Require(Remaining(renewed, 17) > 4, "藏身续用未生效");
                    long arrived = N(Map(renewed)["id"]);
                    if (destination.HasValue && arrived == destination)
                    {
                        await FieldReady();
                        return null;
                    }
                    Require(arrived == origin, "补藏身期间地图改变");
                    await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", target), ("require_stealth", true)));
                    double elapsed = W.Time - pause;
                    end = Math.Min(hard, end + elapsed);
                    progress.Suspend(elapsed);
                    continue;
                }
                var point = new[] { "x", "y", "z" }.Select(axis => DailyRegionRouter.D(R(current, "mainline.map", "ὪὨὯὢὫὮὨὩὮὡὬ.transform.position." + axis))).ToArray();
                double? remaining = Rows(Travel(current)["Approaches"]).Where(p => N(p["Instance"]) == target && B(p["Reachable"])).Select(p => (double?)DailyRegionRouter.D(p["Distance"])).FirstOrDefault();
                if (progress.Sample(point, remaining, W.Time))
                {
                    if (nudged)
                        break;
                    nudged = true;
                    await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                    try
                    {
                        await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_approach"), ("value", target), ("require_stealth", protectedRoute)));
                    }
                    catch (Exception ex) when (ex.Message.Contains("travel_unreachable:", StringComparison.Ordinal)) { break; }
                    progress = new(W.Time);
                }
                await W.Delay(250);
            }
            await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
        }
        catch (DailyStepException ex) when (UnsentFieldTransition(ex))
        {
            // The UI may disappear between the last evidence frame and cancellation.
            // Re-observe both same-map recovery and arrival; waiting only for the
            // destination used to time out after the field had already recovered.
            return await Resume("unsent_field_action: " + ex.Message);
        }
        string reason = W.Time >= end ? "navigation_deadline" : "navigation_no_progress";
        await Failure(current, target, reason, O(("elapsed", W.Time - start), ("initial_distance", initial), ("best_remaining", progress.Best), ("budget", budget), ("extended", end - start - budget), ("ordinary_approach", nudged)));
        throw new DailyTravelBlocked(reason == "navigation_deadline" ? "导航到期仍未到达交互范围" : "导航没有净进展，停止后重新选择路线");
    }
    private static bool UnsentFieldTransition(DailyStepException ex) => !ex.Submitted && ex.Kind == "rejected"
        && (ex.Message.Contains("Scene changed", StringComparison.OrdinalIgnoreCase)
            || ex.Message == "Need one observed GameFieldDefaultUI; found 0"
            || new[] { "rejected: screen_changed", "rejected: ui_not_ready", "rejected: surface_missing", "rejected: foreground_popup" }.Contains(ex.Message)
            || ex.Message.StartsWith("Foreground popup needs handling:", StringComparison.Ordinal));
    public async Task<JsonObject?> PrepareTeleport(bool allowSummon = true, long? destination = null)
    {
        var router = new DailyRegionRouter();
        var failedPorts = new HashSet<(string, long)>();
        var repositioned = new HashSet<string>();
        for (int attempt = 0; attempt < 64; attempt++)
        {
            await FieldReady();
            var e = await Evidence();
            // Preparing a teleport can traverse doors. Stop before walking to a portal
            // or casting a talent if that traversal already reached the requested map.
            if (destination.HasValue && N(Map(e)["id"]) == destination)
                return null;
            await Protection(e);
            e = await Evidence();
            var map = Map(e);
            var travel = Travel(e);
            string node = Region(e);
            Require(N(travel["Pack"]) == N(map["packId"]) && N(travel["Map"]) == N(map["id"]), "Travel rules belong to another map");
            var gates = FieldRows(e, "gate").Where(r => S(r["gameObject.name"]).StartsWith("Gate_", StringComparison.Ordinal)).ToArray();
            var distances = Rows(travel["Approaches"]).Where(p => B(p["Reachable"])).ToDictionary(p => N(p["Instance"]), p => DailyRegionRouter.D(p["Distance"]));
            router.Observe(node, gates.Where(g => distances.ContainsKey(N(g["instance"]))).Select(g => O(("object", g[ObjectPath]), ("destination", g[DestinationPath]), ("distance", distances[N(g["instance"])]))).ToArray());
            var ports = FieldRows(e, "waypoint");
            var near = ports.FirstOrDefault(p => B(p[WaypointNear]));
            if (near != null)
                return near;
            var here = Rows(travel["Maps"]).Single(r => N(r["Id"]) == N(map["id"]));
            var reachable = Reachable(travel, ports);
            double closest = reachable.Length == 0 ? double.PositiveInfinity : reachable.Min(p => distances[N(p["instance"])]);
            if (allowSummon && B(here["CanSummon"]) && B(travel["Ground"]) && !DailyRouteAtlas.PreferPortal(closest))
                return null;
            foreach (var port in reachable)
            {
                var key = (node, N(port[ObjectPath]));
                if (failedPorts.Contains(key))
                    continue;
                try
                {
                    return await Walk(I(port["instance"]));
                }
                catch (DailyTravelBlocked) { failedPorts.Add(key); }
            }
            if (allowSummon && B(here["CanSummon"]))
            {
                if (B(travel["Ground"]))
                    return null;
                if (repositioned.Add(node))
                {
                    bool sent = true;
                    try
                    {
                        await W.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_reposition"), ("require_stealth", NeedsStealth(e))));
                    }
                    catch (Exception ex) when (ex.Message.Contains("travel_unreachable:", StringComparison.Ordinal)) { sent = false; }
                    double end = W.Time + (sent ? 12 : 0);
                    while (W.Time < end)
                    {
                        var current = Travel(await Evidence());
                        Require(N(current["Map"]) == N(map["id"]), "Reposition changed map");
                        if (B(current["Ground"]))
                        {
                            await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                            return null;
                        }
                        await W.Delay(200);
                    }
                    await W.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                }
            }
            long[] preferred;
            try
            {
                preferred = DailyRegionRouter.ExitRoute(travel, gates.Select(g => N(g[DestinationPath])).ToArray(), !allowSummon).Take(1).ToArray();
            }
            catch (DailyTravelBlocked) { preferred = []; }
            JsonObject choice;
            try
            {
                choice = router.Choose(node, preferred);
            }
            catch (DailyTravelBlocked) { await Failure(e, 0, "all_physical_regions_exhausted"); throw; }
            var target = gates.Single(g => N(g[ObjectPath]) == N(choice["object"]));
            if (NeedsStealth(e))
                await Patrol();
            try
            {
                await Walk(I(target["instance"]), N(choice["destination"]));
            }
            catch (DailyTravelBlocked) { router.Reject(); }
        }
        throw new DailyTravelBlocked("传送恢复超过物理区域探索次数");
    }
    private async Task OpenWaypoint(JsonObject port)
    {
        var before = await Evidence();
        long origin = N(Map(before)["id"]);
        int instance = I(port["instance"]);
        var current = FieldRows(before, "waypoint").SingleOrDefault(r => N(r["instance"]) == instance);
        Require(current != null && B(current[WaypointNear]), "传送阵不在交互范围");
        const string status = "ὡὩὫὦὧὢὡὬὡὥὥ";
        Require(S(current![status]) is "WayPoint_Enable" or "WayPoint_Disable", "传送阵启用状态缺失");
        if (S(current[status]) == "WayPoint_Disable")
        {
            await W.Step("GameFieldDefaultUI", operation: "mainline_interact", value: instance);
            double end = W.Time + 15;
            while (true)
            {
                var e = await Evidence();
                Require(N(Map(e)["id"]) == origin, "传送阵激活期间地图改变");
                if (await W.Has("WayPointUI"))
                    return;
                current = FieldRows(e, "waypoint").SingleOrDefault(r => N(r["instance"]) == instance);
                if (current != null && S(current[status]) == "WayPoint_Enable")
                    break;
                Require(W.Time < end, "传送阵激活未确认，未重复发送请求");
                await W.Delay(200);
            }
            Require(B(current[WaypointNear]), "已激活传送阵离开交互范围");
        }
        await W.Step("GameFieldDefaultUI", operation: "mainline_interact", value: instance, expect: "WayPointUI");
    }
    private async Task<(bool Arrived, JsonObject? Port)> OpenTeleport(long map, bool nativeOnly, bool force)
    {
        async Task<bool> Arrived() => !force && N(Map(await Evidence())["id"]) == map;
        if (await Arrived()) return (true, null);
        JsonObject? port;
        if (nativeOnly)
        {
            var e = await Evidence();
            var ports = Reachable(Travel(e), FieldRows(e, "waypoint"));
            var near = FieldRows(e, "waypoint").FirstOrDefault(p => B(p[WaypointNear]));
            if (near == null && ports.Length == 0)
                throw new DailyTravelBlocked("规划中的实体传送阵已不可达");
            port = near ?? await Walk(I(ports[0]["instance"]));
            if (await Arrived()) return (true, null);
            Require(port != null, "实体传送阵到达未确认");
            await OpenWaypoint(port!);
            return (false, port);
        }
        port = await PrepareTeleport(destination: force ? null : map);
        if (await Arrived()) return (true, null);
        if (port == null)
        {
            var result = await Skill(15);
            if (S(result["state"]) == "waypoint_selection")
            {
                var ui = FieldRows(await Evidence(), "waypoint_ui").Single();
                var blocked = ui["ὭὡὢὬὯὦὪὯὮὤὤ"] as JsonArray ?? throw new InvalidDataException("传送天赋限制地图资料不可用");
                if (blocked.Count == 0 || N(ui["_fieldMiniMap.CurrentMap"]!["id"]) == map)
                    return (false, null);
                await W.Step("WayPointUI", back: true, absent: "WayPointUI");
                await FieldReady();
            }
            else
                Require(S(result["state"]) == "skipped" && S(result["reason"]) == "native_disabled", "传送天赋结果未知，未重复尝试");
            port = await PrepareTeleport(false, destination: force ? null : map);
            if (await Arrived()) return (true, null);
            Require(port != null, "实体传送恢复没有返回传送阵");
        }
        await OpenWaypoint(port!);
        return (false, port);
    }
    public async Task<string?> Teleport(long map, long? waypoint = null, bool force = false, bool recovery = false, bool nativeOnly = false)
    {
        Require(!Excluded.Contains(map) || recovery, "地图不属于日常收集范围");
        var e = await Evidence();
        if (!force && N(Map(e)["id"]) == map)
            return null;
        var prepared = await OpenTeleport(map, nativeOnly, force);
        if (prepared.Arrived) return null;
        var native = prepared.Port;
        var seen = new HashSet<long>();
        int pages = Rows(Travel(await Evidence())["Maps"]).Length + 1;
        bool reached = false;
        for (int i = 0; i < pages; i++)
        {
            var u = FieldRows(await Evidence(), "waypoint_ui").Single();
            long current = N(u["_fieldMiniMap.CurrentMap"]!["id"]);
            if (current == map)
            {
                reached = true;
                break;
            }
            if (!seen.Add(current))
                throw new DailyTravelBlocked("Waypoint page cycle");
            await W.Step("WayPointUI", current < map ? "_objMapNextButton" : "_objMapPrevButton");
            await W.WaitEvidence(["mainline"], state => N(FieldRows(state, "waypoint_ui").Single()["_fieldMiniMap.CurrentMap"]!["id"]) != current, 5, "Waypoint map not available: page did not change");
        }
        if (!reached)
            throw new DailyTravelBlocked("Waypoint map not available");
        var icons = FieldRows(await Evidence(), "waypoint_icon").Where(r => B(r["_objWayPointClickable.activeSelf"]) && B(r["_objWayPointEnable.activeSelf"]) && (!waypoint.HasValue || N(r["ὦὠὣὧὥὯὤὮὧὫὬ"]) == waypoint)).OrderBy(r => N(r["ὦὠὣὧὥὯὤὮὧὫὬ"])).ToArray();
        if (icons.Length == 0)
            throw new DailyTravelBlocked("No unlocked waypoint on selected map");
        var icon = icons[0];
        var target = await W.Pointer("WayPointUI", r => N(r["Id"]) == N(icon["gameObject.GetInstanceID()"]));
        var action = O(("ui", "WayPointUI"), ("field", target["Field"]), ("target_id", target["Id"]));
        JsonObject op;
        if (native != null)
            op = await W.Transact("mainline.waypoint_use", O(("destination", map)), action, 30);
        else
        {
            action["expect"] = "WayPointMakePopupUI";
            await W.Step(action);
            op = await W.Transact("dispatch.start", O(("kind", 15), ("destination", map)), O(("ui", "WayPointMakePopupUI"), ("field", "_objOkButton")), 30);
        }
        await WaitMap(map);
        return S(op["id"]);
    }
    public bool Owned(JsonObject e, long pack)
    {
        var owned = Rows(R(e, "mainline.owned_packs", "$items")).Where(r => N(r["Key"]) == N(r["Value.Id"])).Select(r => N(r["Value.Id"])).ToHashSet();
        return owned.Contains(pack) && Rows(R(e, "mainline.packs", "$items")).Any(r => N(r["Id"]) == pack && N(r["PackType"]) is 0 or 1);
    }
    public async Task Enter(long chapter, bool leaveInterior = false)
    {
        Require(Catalog.Supported(chapter) || Owned(await Evidence(), chapter), "卡带不支持或未拥有");
        if (await DailyTravel.Reuse(W, packId: checked((int)chapter), surfaces: ["GameFieldDefaultUI"], extra: ["WayPointUI"]))
            return;
        await DailyTravel.PackList(W);
        await W.Step("PackListUI", operation: "mainline_pack", value: checked((int)chapter));
        Require(await DailyTravel.Reuse(W, packId: checked((int)chapter), surfaces: ["GameFieldDefaultUI"], waitForTarget: true), "卡带切换未完成");
        await FieldReady();
        if (leaveInterior && N(Map(await Evidence())["isInsideMap"]) != 0)
            await PrepareTeleport();
    }
    public async Task WalkGate(long destination, bool recovery = false)
    {
        Require(!Excluded.Contains(destination) || recovery, "目的地图不在日常范围");
        var e = await Evidence();
        long pack = N(Map(e)["packId"]);
        Require(Catalog.Supported(pack) || Owned(e, pack), "卡带不支持门区移动");
        Require(!NeedsStealth(e) || Remaining(e, 17) > 3, "进入巡逻路线前藏身未确认");
        var targets = Reachable(Travel(e), FieldRows(e, "gate").Where(r => S(r["gameObject.name"]).StartsWith("Gate_", StringComparison.Ordinal) && N(r[DestinationPath]) == destination).ToArray());
        foreach (var t in targets)
            try
            {
                await Walk(I(t["instance"]), destination);
                return;
            }
            catch (DailyTravelBlocked) { }
        throw new DailyTravelBlocked("所有可见入口均未到达目标地图");
    }
    public async Task<JsonArray> WalkDrops()
    {
        var result = new JsonArray();
        for (int i = 0; i < 100; i++)
        {
            await FieldReady();
            var e = await Evidence();
            var drops = Drops(e);
            if (drops.Length == 0)
                return result;
            var origin = FieldRows(e, "map").Single();
            var target = drops.OrderBy(r => new[] { "x", "y", "z" }.Sum(axis => Math.Pow(DailyRegionRouter.D(r["transform.position." + axis]) - DailyRegionRouter.D(origin["ὪὨὯὢὫὮὨὩὮὡὬ.transform.position." + axis]), 2))).First();
            var op = await W.Transact("mainline.pickup", O(("map", Map(e)["id"]), ("object", target[ObjectPath])), O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", target["instance"])), 65);
            result.Add(O(("state", "completed"), ("id", op["id"]), ("result", op["result"])));
            await W.Delay(300);
        }
        throw new DailyTravelBlocked("步行拾取超过本图目标预算");
    }
}

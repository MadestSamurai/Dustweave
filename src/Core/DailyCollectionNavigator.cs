using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;
namespace Dustweave;

/// <summary>Routes are reusable geometry; completion always comes from the server projection.</summary>
public sealed class DailyCollectionNavigator
{
    private readonly DailyFieldRoute route;
    private readonly long pack;
    private readonly string path;
    private readonly string tracePath;
    private readonly JsonArray trace = [];
    private readonly DailyRouteAttempts attempts = new();
    private JsonNode? progress;
    private string goals = "";
    public void TrackProgress(JsonObject current)
    {
        if (!JsonNode.DeepEquals(progress, current)) attempts.Clear();
        progress = current.DeepClone();
    }
    private void Trace(string state, JsonObject detail)
    {
        trace.Add(O(("at", route.W.Driver.UtcTicks), ("state", state), ("region", node), ("detail", detail)));
        while (trace.Count > 48) trace.RemoveAt(0);
        try { DailyJson.Write(tracePath, O(("pack", pack), ("account", route.W.Context["actor"]![3]),
            ("cycle", route.W.Context["cycle"]), ("events", trace))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { route.W.Driver.Diagnostics.Event("route_trace_unavailable", O(("message", e.Message))); }
    }
    public DailyRouteAtlas Atlas
    {
        get;
    }
    public JsonObject Evidence
    {
        get; private set;
    }
    private JsonObject[] gates = [];
    private string node = "";
    public DailyCollectionNavigator(DailyFieldRoute route, long pack, JsonObject evidence)
    {
        this.route = route;
        this.pack = pack;
        Evidence = evidence;
        Require(N(Travel(evidence)["Pack"]) == pack, "路线卡带发生变化");
        path = Path.Combine(route.W.Root, "live", "route-atlas", S(route.W.Context["actor"]![3]), pack + ".json");
        tracePath = Path.Combine(route.W.Root, "live", "travel-diagnostics", "route-" + S(route.W.Context["actor"]![3]) + "-" + pack + ".json");
        var seed = route.W.Asset("route-atlas.json")["packs"]?[pack.ToString()] as JsonObject;
        var saved = DailyJson.TryRead<JsonObject>(path);
        var candidate = S(saved?["account"]) == S(route.W.Context["actor"]![3]) ? saved?["graph"] as JsonObject : seed;
        try
        {
            Atlas = new(Rows(Travel(evidence)["Maps"]), candidate);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException) { Atlas = new(Rows(Travel(evidence)["Maps"]), seed); }
        Observe(evidence);
    }
    public long Observe(JsonObject e)
    {
        var map = Map(e);
        var travel = Travel(e);
        Require(N(map["packId"]) == pack && N(travel["Pack"]) == pack && N(travel["Map"]) == N(map["id"]), "原生地图与路线卡带不一致");
        var paths = Rows(travel["Approaches"]).Where(r => B(r["Reachable"])).ToDictionary(r => N(r["Instance"]));
        gates = FieldRows(e, "gate");
        var exits = gates.Where(g => S(g["gameObject.name"]).StartsWith("Gate_", StringComparison.Ordinal) && paths.ContainsKey(N(g["instance"]))).Select(g => O(("object", g[ObjectPath]), ("destination", g[DestinationPath]), ("distance", paths[N(g["instance"])]["Distance"]))).ToArray();
        var ports = FieldRows(e, "waypoint").Where(p => paths.ContainsKey(N(p["instance"]))).Select(p => DailyRegionRouter.D(paths[N(p["instance"])]["Distance"])).ToArray();
        Evidence = e;
        node = Region(e);
        Atlas.Observe(node, exits, ports, B(travel["Ground"]));
        return N(map["id"]);
    }
    public Task<long> Observe() => Observe(settle: 1);
    private async Task<long> Observe(double settle)
    {
        await route.FieldReady(settle: settle);
        return Observe(await route.Evidence());
    }
    public void Save() => DailyJson.Write(path, O(("schema", 1), ("account", route.W.Context["actor"]![3]), ("graph", Atlas.Export())));
    public async Task<JsonObject> Plan(IEnumerable<long> pending, long? returnMap = null, bool teleports = true, IEnumerable<long>? allowed = null)
    {
        // Planning does not move the character. Walk and teleport retain their
        // own stable-field gate immediately before executing the route.
        long current = await Observe(settle: 0);
        var forbidden = allowed == null ? new HashSet<long>() : Atlas.Maps.Keys.Except(allowed).ToHashSet();
        if (!Excluded.Contains(current))
            forbidden.UnionWith(Excluded);
        var plan = Atlas.Plan(pending, returnMap, teleports, forbidden);
        goals = O(("pending", plan["pending"]), ("return", returnMap)).ToJsonString();
        plan["chapter"] = pack;
        plan["current"] = current;
        plan["account"] = Copy(route.W.Context["actor"]![3]);
        plan["at"] = route.W.Driver.UtcTicks;
        Save();
        DailyJson.Write(Path.ChangeExtension(path, "-plan.json"), plan);
        Trace("planned", plan);
        return plan;
    }
    public async Task<bool> Step(JsonObject action)
    {
        route.W.StepProgress("travel");
        // Refresh the planned origin now; the execution branch still waits for stability.
        long current = await Observe(settle: 0);
        string origin = node;
        string planned = S(action["origin"]);
        if (DailyRouteAtlas.MapOf(planned) != current || !planned.StartsWith('?') && planned != origin)
        {
            Trace("origin_changed", action);
            return false;
        }
        double started = route.W.Time;
        JsonObject? gate = null;
        string kind = S(action["kind"]);
        long destination = N(action["destination"]);
        if (!attempts.TryStep(goals, origin, kind, destination, action["object"]))
        {
            Trace("cycle_deferred", action);
            throw new DailyTravelBlocked("地图路线反复经过同一入口且待办未变化，已保留路线记录，暂缓此卡带");
        }
        Trace("moving", action);
        try
        {
            if (kind == "walk")
            {
                gate = Reachable(Travel(Evidence), gates.Where(g => N(g[DestinationPath]) == destination && (action["object"] == null || N(g[ObjectPath]) == N(action["object"]))).ToArray()).FirstOrDefault();
                if (gate == null)
                    throw new DailyTravelBlocked("规划中的入口已经不可达");
                await route.Walk(I(gate["instance"]), destination);
            }
            else
            {
                Require(kind == "teleport", "未知路线动作");
                await route.FieldReady();
                await route.Teleport(destination, recovery: true, nativeOnly: S(action["mode"]) == "native");
            }
        }
        catch (Exception ex) when (kind == "teleport" && new[] { "Waypoint page cycle", "Waypoint map not available", "No unlocked waypoint", "Requested waypoint is not unlocked" }.Any(s => ex.Message.Contains(s, StringComparison.Ordinal)))
        {
            if (await route.W.Has("WayPointUI"))
                await route.W.Step("WayPointUI", back: true, absent: "WayPointUI", reason: "目标传送阵未开放，重新选择已观察路线");
            Atlas.WarpBlocked.Add(destination);
            Trace("waypoint_unavailable", action);
            return false;
        }
        catch (DailyTravelBlocked)
        {
            if (kind == "walk")
                Atlas.Blocked.Add((origin, gate == null ? (action["object"] == null ? null : N(action["object"])) : N(gate[ObjectPath])));
            else
                Atlas.BlockedPorts.Add(origin);
            Trace("unreachable", action);
            return false;
        }
        catch (Exception error)
        {
            Trace("interrupted", O(("action", action), ("error", error.Message)));
            throw;
        }
        long arrived = await Observe();
        Require(arrived == destination, "路线没有到达预期地图");
        // Do not spend stealth merely for arriving. The next concrete action
        // may be immediate suppression, absorption at the waypoint, or another
        // direct teleport. Every actual walking segment protects itself.
        if (kind == "walk")
            Atlas.Reached(origin, N(gate![ObjectPath]), node, route.W.Time - started);
        else
            Atlas.Landing[arrived] = node;
        Save();
        Trace("arrived", O(("action", action), ("map", arrived)));
        return true;
    }
    public Task WalkTo(long destination, IEnumerable<long>? allowed = null) => GoTo(destination, teleports: false, allowed: allowed);
    public async Task GoTo(long destination, bool teleports = true, IEnumerable<long>? allowed = null)
    {
        attempts.Clear();
        for (int i = 0; i < 64; i++)
        {
            if (await Observe() == destination)
                return;
            var p = await Plan([destination], teleports: teleports, allowed: allowed);
            var actions = Rows(p["actions"]);
            if (actions.Length == 0)
                throw new DailyTravelBlocked("目的地图没有可用路线");
            await Step(actions[0]);
        }
        throw new DailyTravelBlocked("地图路线恢复超过上限");
    }
}

public sealed partial class DailyFieldRoute
{
    public Func<long, long[]>? CollectionMapScope { get; set; }
    public long[] StandardMaps(long pack)
    {
        var collection = CollectionMapScope?.Invoke(pack) ?? Catalog.Maps(pack);
        return Progress.Steal ? (Progress.OnlySteal ? Catalog.StealMaps(pack) : collection.Concat(Catalog.StealMaps(pack)).Distinct().ToArray()) : collection;
    }
    public async Task<JsonObject> Checkpoint(bool fresh = true)
    {
        var e = await Evidence();
        long pack = N(Map(e)["packId"]);
        var maps = StandardMaps(pack);
        string week = S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        return fresh ? await Progress.Query(pack, maps) : Progress.Cached(pack, maps, week) ?? throw new DailyCollectionSyncRequired("缺少服务器地图记录");
    }
    public async Task<JsonObject> Gather(bool manual = false, long? expectedMap = null)
    {
        JsonArray? walking = null;
        await FieldReady();
        var before = await Evidence();
        long map = N(Map(before)["id"]), pack = N(Map(before)["packId"]);
        if (expectedMap.HasValue && map != expectedMap.Value) throw new DailyCollectionLocationChanged();
        string week = S(R(before, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        Require(Catalog.Supported(pack) && !Excluded.Contains(map), "地图不在收集范围");
        var checkpoint = await Checkpoint();
        var current = await Evidence();
        if (N(Map(current)["id"]) != map || N(Map(current)["packId"]) != pack) throw new DailyCollectionLocationChanged();
        Require(checkpoint["maps"]?[map.ToString()] is JsonObject, "当前地图不在本次服务器收集范围，需重新规划路线。");
        if (checkpoint["completed"]!.AsObject().ContainsKey(map.ToString()))
            return DailyWorkflow.Skipped("server_complete");
        if (Progress.Steal && Rows(checkpoint["maps"]![map.ToString()]!["steals"]).Length > 0)
            await new DailyWeeklySteal(this).CollectMap(checkpoint);
        if (!Progress.OnlySteal)
        {
            var e = await Evidence();
            var remainingDrops = checkpoint["maps"]![map.ToString()]!["drops"]!.AsArray().Select(N).ToHashSet();
            // A mixed map must wait with its drops when absorption is exhausted;
            // suppressing first would revisit the same route on every daily reset.
            if (!manual && remainingDrops.Count > 0) Quota(e);
            if (B(R(e, "mainline.map", "HasCanOverwhelmMonster()")) && CanSuppress(map))
                await Skill(3);
            e = await Evidence();
            if (FieldRows(e, "monster").Any(r => N(r["ὭὫὠὯὯὭὧὬὣὨὨ.ὫὣὬὥὪὫὪὤὫὮὧ"]) == 2))
                await Skill(20);
            // The live object cache can lag after reconnecting. Already-collected
            // server targets must not spend another absorption use.
            if (remainingDrops.Count > 0)
            {
                if (!manual) Quota(e);
                await Protection(e);
                await Skill(6);
                e = await Evidence();
            }
            if (Drops(e).Any(drop => remainingDrops.Contains(N(drop[ObjectPath]))))
            {
                if (manual)
                    walking = await WalkDrops();
                else
                {
                    Quota(e);
                    await Skill(4);
                }
            }
        }
        await W.Delay(1000);
        var after = await Evidence();
        Require(N(Map(after)["id"]) == map && S(R(after, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "收集过程中地图或周周期变化");
        var result = await Checkpoint();
        if (!result["completed"]!.AsObject().ContainsKey(map.ToString()))
        {
            if (Progress.OnlyCaptureRemains(result, map) && TalentAllowance(after, 20).Remaining == 0)
                throw new DailyCollectionTargetsRemain("今日捕获次数已用完；本图物品已收完，剩余捕获目标留待次日，不再重进地图。", captureLimit: true);
            if (!manual && result["maps"]![map.ToString()]!["drops"] is JsonArray { Count: > 0 })
                Quota(after);
            var deferred = Rows(walking ?? new JsonArray()).Where(r => S(r["state"]) == "deferred").ToArray();
            throw new DailyCollectionTargetsRemain("服务器仍有待处理目标：" + map, deferred.Length == 0 ? null : Array(deferred));
        }
        return DailyWorkflow.Completed(result);
    }
    public async Task<DailyCollectionNavigator> ReloadMap(long pack, long destination, string week)
    {
        async Task Guard()
        {
            var e = await Evidence();
            Require(S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "重进期间周周期改变");
        }
        await Guard();
        var state = await Evidence();
        var owned = Rows(R(state, "mainline.owned_packs", "$items")).Where(r => N(r["Key"]) == N(r["Value.Id"])).Select(r => N(r["Value.Id"])).ToHashSet();
        var alternate = Rows(R(state, "mainline.packs", "$items")).Where(r => N(r["PackType"]) == 0 && N(r["Id"]) != pack && owned.Contains(N(r["Id"]))).OrderBy(r => N(r["Id"])).FirstOrDefault();
        Require(alternate != null, "没有可安全切换的已拥有卡带，保留当前地图");
        await Enter(N(alternate!["Id"]));
        await Guard();
        await Enter(pack);
        await Guard();
        var nav = new DailyCollectionNavigator(this, pack, await Evidence());
        for (int i = 0; i < 32; i++)
        {
            if (await nav.Observe() == destination)
                return nav;
            var plan = await nav.Plan([destination]);
            var actions = Rows(plan["actions"]);
            if (actions.Length == 0)
                break;
            await nav.Step(actions[0]);
            await Guard();
        }
        throw new DailyTravelBlocked("重进卡带后未能回到待收集地图");
    }
    public async Task Collect(long pack, bool walkCollect = false, long? returnMap = null, bool teleports = true, JsonObject? checkpoint = null)
    {
        bool prior = RecoverEncounters;
        RecoverEncounters = true;
        try
        {
            await Enter(pack);
            checkpoint ??= await Checkpoint();
            string week = S(checkpoint["weekly_reset"]);
            var nav = new DailyCollectionNavigator(this, pack, await Evidence());
            var reentered = new HashSet<long>();
            var targets = StandardMaps(pack);
            for (int i = 0; i < 128; i++)
            {
                long current = await nav.Observe();
                Require(S(R(nav.Evidence, "mainline.reset", "GetWeeklyResetTime().Ticks")) == week, "周周期改变，停止原路线");
                var pending = targets.Where(id => !checkpoint["completed"]!.AsObject().ContainsKey(id.ToString())).ToArray();
                if (pending.Length == 0 && (returnMap == null || current == returnMap))
                    return;
                if (pending.Length > 0 && !walkCollect && TalentAllowance(nav.Evidence, 4).Remaining == 0)
                {
                    if (targets.Any(id => checkpoint["maps"]![id.ToString()]!["drops"] is JsonArray { Count: > 0 })) Quota(nav.Evidence);
                }
                if (pending.Contains(current))
                {
                    try
                    {
                        await Gather(walkCollect, expectedMap: current);
                    }
                    catch (DailyCollectionLocationChanged) { continue; }
                    catch (DailyCollectionTargetsRemain error) when (!error.CaptureLimit) { }
                    checkpoint = Progress.Cached(pack, targets, week)!;
                    if (!checkpoint["completed"]!.AsObject().ContainsKey(current.ToString()))
                    {
                        if (!reentered.Add(current))
                            throw new DailyCollectionTargetsRemain("地图重进后仍有未完成目标：" + current);
                        nav = await ReloadMap(pack, current, week);
                        checkpoint = await Checkpoint();
                    }
                    continue;
                }
                var plan = await nav.Plan(pending, returnMap, teleports);
                var actions = Rows(plan["actions"]);
                if (actions.Length == 0)
                    throw new DailyTravelBlocked("无法前往剩余收集地图");
                await nav.Step(actions[0]);
            }
            throw new DailyTravelBlocked("周收集路线超过恢复预算");
        }
        finally { RecoverEncounters = prior; }
    }
}

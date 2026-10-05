using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
using static BD2Daily.DailyFieldRoute;
namespace BD2Daily;

/// <summary>Routes are reusable geometry; completion always comes from the server projection.</summary>
public sealed class DailyCollectionNavigator
{
    private readonly DailyFieldRoute route;
    private readonly long pack;
    private readonly string path;
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
    public async Task<long> Observe()
    {
        await route.FieldReady();
        return Observe(await route.Evidence());
    }
    public void Save() => DailyJson.Write(path, O(("schema", 1), ("account", route.W.Context["actor"]![3]), ("graph", Atlas.Export())));
    public async Task<JsonObject> Plan(IEnumerable<long> pending, long? returnMap = null, bool teleports = true, IEnumerable<long>? allowed = null)
    {
        long current = await Observe();
        var forbidden = allowed == null ? new HashSet<long>() : Atlas.Maps.Keys.Except(allowed).ToHashSet();
        if (!Excluded.Contains(current))
            forbidden.UnionWith(Excluded);
        var plan = Atlas.Plan(pending, returnMap, teleports, forbidden);
        plan["chapter"] = pack;
        plan["current"] = current;
        plan["account"] = Copy(route.W.Context["actor"]![3]);
        plan["at"] = route.W.Driver.UtcTicks;
        Save();
        DailyJson.Write(Path.ChangeExtension(path, "-plan.json"), plan);
        return plan;
    }
    public async Task<bool> Step(JsonObject action)
    {
        long current = await Observe();
        string origin = node;
        string planned = S(action["origin"]);
        if (DailyRouteAtlas.MapOf(planned) != current || !planned.StartsWith('?') && planned != origin)
            return false;
        double started = route.W.Time;
        JsonObject? gate = null;
        string kind = S(action["kind"]);
        long destination = N(action["destination"]);
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
                await route.Teleport(destination, recovery: true, nativeOnly: S(action["mode"]) == "native");
            }
        }
        catch (Exception ex) when (kind == "teleport" && new[] { "Waypoint page cycle", "Waypoint map not available", "No unlocked waypoint", "Requested waypoint is not unlocked" }.Any(s => ex.Message.Contains(s, StringComparison.Ordinal)))
        {
            if (await route.W.Has("WayPointUI"))
                await route.W.Step("WayPointUI", back: true, absent: "WayPointUI", reason: "目标传送阵未开放，重新选择已观察路线");
            Atlas.WarpBlocked.Add(destination);
            return false;
        }
        catch (DailyTravelBlocked)
        {
            if (kind == "walk")
                Atlas.Blocked.Add((origin, gate == null ? (action["object"] == null ? null : N(action["object"])) : N(gate[ObjectPath])));
            else
                Atlas.BlockedPorts.Add(origin);
            return false;
        }
        long arrived = await Observe();
        Require(arrived == destination, "路线没有到达预期地图");
        if (kind == "walk")
            Atlas.Reached(origin, N(gate![ObjectPath]), node, route.W.Time - started);
        else
            Atlas.Landing[arrived] = node;
        Save();
        return true;
    }
    public Task WalkTo(long destination, IEnumerable<long>? allowed = null) => GoTo(destination, teleports: false, allowed: allowed);
    public async Task GoTo(long destination, bool teleports = true, IEnumerable<long>? allowed = null)
    {
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
    public long[] StandardMaps(long pack) => Progress.Steal ? (Progress.OnlySteal ? Catalog.StealMaps(pack) : Catalog.Maps(pack).Concat(Catalog.StealMaps(pack)).Distinct().ToArray()) : Catalog.Maps(pack);
    public async Task<JsonObject> Checkpoint(bool fresh = true)
    {
        var e = await Evidence();
        long pack = N(Map(e)["packId"]);
        var maps = StandardMaps(pack);
        string week = S(R(e, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        return fresh ? await Progress.Query(pack, maps) : Progress.Cached(pack, maps, week) ?? throw new DailyCollectionSyncRequired("缺少服务器地图记录");
    }
    public async Task<JsonObject> Gather(bool manual = false)
    {
        await FieldReady();
        var before = await Evidence();
        long map = N(Map(before)["id"]), pack = N(Map(before)["packId"]);
        string week = S(R(before, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        Require(Catalog.Supported(pack) && !Excluded.Contains(map), "地图不在收集范围");
        var checkpoint = await Checkpoint();
        if (checkpoint["completed"]!.AsObject().ContainsKey(map.ToString()))
            return DailyWorkflow.Skipped("server_complete");
        if (!manual)
            Quota(before);
        if (Progress.Steal && Rows(checkpoint["maps"]![map.ToString()]!["steals"]).Length > 0)
            await new DailyWeeklySteal(this).CollectMap(checkpoint);
        if (!Progress.OnlySteal)
        {
            var e = await Evidence();
            if (B(R(e, "mainline.map", "HasCanOverwhelmMonster()")) && CanSuppress(map))
                await Skill(3);
            e = await Evidence();
            if (FieldRows(e, "monster").Any(r => N(r["ὭὫὠὯὯὭὧὬὣὨὨ.ὫὣὬὥὪὫὪὤὫὮὧ"]) == 2))
                await Skill(20);
            await Skill(6);
            e = await Evidence();
            if (Drops(e).Length > 0)
            {
                if (manual)
                    await WalkDrops();
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
            if (!manual)
                Quota(after);
            throw new DailyCollectionTargetsRemain("服务器仍有待处理目标：" + map);
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
                if (pending.Length > 0 && !walkCollect)
                    Quota(nav.Evidence);
                if (pending.Contains(current))
                {
                    try
                    {
                        await Gather(walkCollect);
                    }
                    catch (DailyCollectionTargetsRemain) { }
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

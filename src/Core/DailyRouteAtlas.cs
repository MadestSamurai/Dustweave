using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyTravelBlocked(string message) : Exception(message);
public sealed class DailyQuotaExhausted(string message) : Exception(message);
public sealed class DailyCollectionTargetsRemain(string message, JsonArray? deferredPickups = null, bool captureLimit = false) : Exception(message)
{
    public JsonArray? DeferredPickups { get; } = deferredPickups;
    public bool CaptureLimit { get; } = captureLimit;
}
internal sealed class DailyCollectionLocationChanged() : Exception("收集准备期间地图已变化，重新规划当前地点。");
public sealed class DailyCollectionSyncRequired(string message) : Exception(message);

// A map can contain disconnected regions, so A -> B -> A is not itself a cycle.
// Bound repeated identical departures only while the actual work is unchanged.
internal sealed class DailyRouteAttempts
{
    private readonly Dictionary<string, int> departures = new(StringComparer.Ordinal);
    public void Clear() => departures.Clear();
    public bool TryStep(string goals, string origin, string kind, long destination, JsonNode? gate)
    {
        string key = new JsonArray(goals, origin, kind, destination, gate?.DeepClone()).ToJsonString();
        if (departures.Count >= 256 && !departures.ContainsKey(key)) return false;
        int count = departures.GetValueOrDefault(key) + 1;
        departures[key] = count;
        return count <= 3;
    }
}

public sealed class DailyTravelProgress(double now, double timeout = 4)
{
    private double changed = now; private double[]? anchor;
    public double? Best
    {
        get; private set;
    }
    public bool Sample(double[] point, double? remaining, double at)
    {
        if (remaining.HasValue && double.IsFinite(remaining.Value) && remaining >= 0)
        {
            if (Best == null || remaining < Best - .35)
            {
                Best = remaining;
                changed = at;
                anchor = point;
            }
        }
        else if (anchor == null || Math.Sqrt(point.Zip(anchor, (a, b) => (a - b) * (a - b)).Sum()) > 1)
        {
            anchor = point;
            changed = at;
        }
        return at - changed >= timeout;
    }
    public void Suspend(double seconds) => changed += seconds;
    public static double Budget(double? distance) => distance.HasValue && double.IsFinite(distance.Value) && distance >= 0 ? Math.Clamp(distance.Value / 1.2 + 20, 60, 300) : 60;
}
public sealed class DailyRegionRouter
{
    private readonly Dictionary<string, Dictionary<long, JsonObject>> options = new(); private readonly Dictionary<(string, long), string> links = new(); private readonly HashSet<(string, long)> failed = new(); private (string, long)? pending;
    public void Observe(string node, JsonObject[] exits)
    {
        if (pending.HasValue)
        {
            links[pending.Value] = node;
            pending = null;
        }
        options[node] = exits.ToDictionary(r => N(r["object"]));
    }
    private JsonObject[] Untried(string node) => options.TryGetValue(node, out var rows) ? rows.Where(p => !links.ContainsKey((node, p.Key)) && !failed.Contains((node, p.Key))).Select(p => p.Value).ToArray() : [];
    public JsonObject Choose(string node, long[]? preferred = null)
    {
        preferred ??= [];
        var fresh = Untried(node);
        JsonObject? result = null;
        if (fresh.Length > 0)
        {
            var visited = options.Keys.Select(DailyRouteAtlas.MapOf).ToHashSet();
            result = fresh.OrderBy(e => visited.Contains(N(e["destination"]))).ThenBy(e => System.Array.IndexOf(preferred, N(e["destination"])) is int p && p >= 0 ? p : preferred.Length).ThenBy(e => D(e["distance"])).ThenBy(e => N(e["object"])).First();
        }
        else
        {
            var queue = new Queue<(string Node, List<JsonObject> Path)>();
            queue.Enqueue((node, []));
            var seen = new HashSet<string> { node };
            while (queue.TryDequeue(out var item))
            {
                if (item.Path.Count > 0 && Untried(item.Node).Length > 0)
                {
                    result = item.Path[0];
                    break;
                }
                foreach (var pair in options.GetValueOrDefault(item.Node) ?? [])
                {
                    if (links.TryGetValue((item.Node, pair.Key), out var target) && !failed.Contains((item.Node, pair.Key)) && seen.Add(target))
                        queue.Enqueue((target, [.. item.Path, pair.Value]));
                }
            }
        }
        if (result == null)
            throw new DailyTravelBlocked("所有已观察的门区已尝试，未找到可用传送入口");
        pending = (node, N(result["object"]));
        return result;
    }
    public void Reject()
    {
        if (pending.HasValue)
            failed.Add(pending.Value);
        pending = null;
    }
    public static double D(JsonNode? n) => n == null ? 0 : double.TryParse(S(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : throw new InvalidDataException("Invalid native distance");
    public static long[] ExitRoute(JsonObject travel, long[] observed, bool disableSummon = false)
    {
        var maps = Rows(travel["Maps"]).ToDictionary(r => N(r["Id"]));
        long start = N(travel["Map"]);
        Require(maps.ContainsKey(start), "Current map missing from native graph");
        var allowed = observed.ToHashSet();
        if (travel["Approaches"] is JsonArray approaches)
            allowed.IntersectWith(Rows(approaches).Where(r => S(r["Kind"]) == "gate" && B(r["Reachable"])).Select(r => N(r["Destination"])));
        var queue = new Queue<(long Map, long[] Path)>();
        queue.Enqueue((start, []));
        var seen = new HashSet<long> { start };
        while (queue.TryDequeue(out var next))
        {
            var row = maps[next.Map];
            if (next.Path.Length > 0 && (!disableSummon && B(row["CanSummon"]) || B(row["HasWaypoint"])))
                return next.Path;
            foreach (long dest in row["Exits"]!.AsArray().Select(N).Distinct().Order())
            {
                if (next.Path.Length == 0 && !allowed.Contains(dest))
                    continue;
                if (maps.ContainsKey(dest) && seen.Add(dest))
                    queue.Enqueue((dest, [.. next.Path, dest]));
            }
        }
        throw new DailyTravelBlocked("未观察到通往合法传送区域的路线");
    }
}
public sealed class DailyRouteAtlas
{
    public const double WalkSpeed = 3, SummonPenalty = 45;
    public Dictionary<long, JsonObject> Maps
    {
        get;
    }
    public Dictionary<string, JsonObject> Regions { get; } = new(); public Dictionary<long, string> Landing { get; } = new();
    public HashSet<(string Origin, long? Object)> Blocked { get; } = []; public HashSet<long> WarpBlocked { get; } = []; public HashSet<string> BlockedPorts { get; } = [];
    private JsonObject[] quickTargets = [];
    private double speedRemaining, speedBonus;
    // Per-observation effect, never persisted into reusable map geometry.
    public void ObserveSpeed(double remaining, double bonus)
    {
        speedRemaining = double.IsFinite(remaining) && remaining > 0 ? remaining : 0;
        speedBonus = double.IsFinite(bonus) && bonus > 0 ? bonus : 0;
    }
    internal static double WalkingSeconds(double ordinarySeconds, double remaining, double bonus)
    {
        if (remaining <= 0 || bonus <= 0) return ordinarySeconds;
        double accelerated = Math.Min(ordinarySeconds, remaining * (1 + bonus));
        return accelerated / (1 + bonus) + ordinarySeconds - accelerated;
    }
    public HashSet<(string Origin, long Npc)> BlockedQuick { get; } = [];
    public static string QuickNode(long map, long npc) => new JsonArray(map, "quick", npc).ToJsonString();
    public void ObserveQuick(IEnumerable<JsonObject> targets)
    {
        // Availability comes from this account's live list, never a saved atlas.
        quickTargets = targets.Where(t => N(t["Npc"]) > 0 && Maps.ContainsKey(N(t["Map"])))
            .GroupBy(t => N(t["Npc"])).Select(g => g.First().DeepClone().AsObject()).ToArray();
        foreach (var target in quickTargets)
        {
            string key = QuickNode(N(target["Map"]), N(target["Npc"]));
            if (Regions.TryGetValue(key, out var old) && !JsonNode.DeepEquals(old["anchor"], Anchor(target))) Regions.Remove(key);
        }
    }
    private static JsonObject Anchor(JsonObject target) => O(("npc", target["Npc"]), ("map", target["Map"]), ("x", target["X"]), ("y", target["Y"]), ("z", target["Z"]));
    public void ReachedQuick(long npc, string region, double seconds, bool sameMap = false)
    {
        var target = quickTargets.SingleOrDefault(t => N(t["Npc"]) == npc);
        // The landing can hide the quick list. Arrival is already verified;
        // losing an optional learning sample must not interrupt the route.
        if (target == null) return;
        Require(MapOf(region) == N(target["Map"]), "Quick travel arrived on another map");
        var row = Regions[region].DeepClone().AsObject();
        string key = QuickNode(MapOf(region), npc);
        var prior = Regions.GetValueOrDefault(key);
        foreach (string cost in new[] { "sameMapSeconds", "crossMapSeconds" })
            if (prior?[cost] != null) row[cost] = prior[cost]!.DeepClone();
        row["anchor"] = Anchor(target);
        row[sameMap ? "sameMapSeconds" : "crossMapSeconds"] = Math.Clamp(seconds, 1, 60);
        Regions[key] = row;
    }
    public string? Current
    {
        get; private set;
    }
    public bool CurrentGround
    {
        get; private set;
    }
    private static string Signature(JsonObject row) => O(("Id", row["Id"]), ("Exits", row["Exits"]!.AsArray().Select(N).Order().ToArray()), ("HasWaypoint", B(row["HasWaypoint"])), ("CanSummon", B(row["CanSummon"])), ("Hunting", B(row["Hunting"]))).ToJsonString();
    public static long MapOf(string key) => key.StartsWith("?:", StringComparison.Ordinal) ? long.Parse(key[2..], System.Globalization.CultureInfo.InvariantCulture) : N(JsonNode.Parse(key)![0]);
    public static string Abstract(long id) => "?:" + id;
    public static bool PreferPortal(double distance) => distance / WalkSpeed <= SummonPenalty;
    public DailyRouteAtlas(JsonObject[] maps, JsonObject? seed = null)
    {
        Maps = maps.ToDictionary(r => N(r["Id"]), r => r.DeepClone().AsObject());
        if (seed == null)
            return;
        var old = Rows(seed["maps"] ?? new JsonArray()).ToDictionary(r => N(r["Id"]));
        var valid = Maps.Where(p => old.TryGetValue(p.Key, out var row) && Signature(p.Value) == Signature(row)).Select(p => p.Key).ToHashSet();
        foreach (var pair in seed["regions"] as JsonObject ?? new())
        {
            var r = pair.Value!.AsObject();
            if (!valid.Contains(N(r["map"])))
                continue;
            var row = r.DeepClone().AsObject();
            row["exits"] = Array(Rows(row["exits"]).Where(e => valid.Contains(N(e["destination"])) && Maps[N(row["map"])]["Exits"]!.AsArray().Any(id => N(id) == N(e["destination"]))));
            Regions[pair.Key] = row;
        }
        foreach (var pair in seed["landing"] as JsonObject ?? new())
            if (Regions.ContainsKey(S(pair.Value)))
                Landing[long.Parse(pair.Key, System.Globalization.CultureInfo.InvariantCulture)] = S(pair.Value);
    }
    public string Observe(string node, JsonObject[] exits, double[] ports, bool ground, double walkingSpeed = WalkSpeed)
    {
        // Store ordinary speed; active talents are applied at planning time with
        // their remaining duration, including time spent loading between maps.
        if (!double.IsFinite(walkingSpeed) || walkingSpeed <= 0) walkingSpeed = WalkSpeed;
        long map = MapOf(node);
        Require(Maps.ContainsKey(map), "Physical region outside map graph");
        var old = Regions.GetValueOrDefault(node);
        var previous = old == null ? new Dictionary<long, JsonObject>() : Rows(old["exits"]).ToDictionary(e => N(e["object"]));
        var edges = new JsonArray();
        foreach (var e in exits)
        {
            long dest = N(e["destination"]);
            if (!Maps.ContainsKey(dest) || !Maps[map]["Exits"]!.AsArray().Any(id => N(id) == dest))
                continue;
            var row = previous.TryGetValue(N(e["object"]), out var p) ? p.DeepClone().AsObject() : new();
            if (N(row["destination"]) != dest)
                row.Remove("target");
            foreach (var pair in e)
                row[pair.Key] = Copy(pair.Value);
            row["seconds"] = Math.Max(.25, DailyRegionRouter.D(e["distance"])) / walkingSpeed + 4;
            edges.Add(row);
        }
        Regions[node] = O(("map", map), ("exits", edges), ("portal", ports.Length > 0), ("portalDistance", ports.Length == 0 ? 0 : ports.Min()), ("walkingSpeed", walkingSpeed));
        Current = node;
        CurrentGround = ground;
        return node;
    }
    public void Reached(string origin, long obj, string target, double seconds)
    {
        var edge = Rows(Regions[origin]["exits"]).Single(r => N(r["object"]) == obj);
        edge["target"] = target;
        edge["seconds"] = Math.Clamp(seconds, 1, 300);
        Blocked.Remove((origin, obj));
    }
    public JsonObject Export() => O(("maps", Array(Maps.Values)), ("regions", new JsonObject(Regions.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value.DeepClone())))), ("landing", new JsonObject(Landing.Select(p => new KeyValuePair<string, JsonNode?>(p.Key.ToString(), JsonValue.Create(p.Value))))));
    public string Destination(long id) => Landing.TryGetValue(id, out var landed) ? landed : Regions.Where(p => N(p.Value["map"]) == id && B(p.Value["portal"])).Select(p => p.Key).Order(StringComparer.Ordinal).FirstOrDefault() ?? Abstract(id);
    private IEnumerable<(string Target, JsonObject Action)> Edges(string node, bool teleports, HashSet<long> forbidden)
    {
        long mid = MapOf(node);
        var row = Maps[mid];
        var region = Regions.GetValueOrDefault(node);
        var exits = region != null ? Rows(region["exits"]) : row["Exits"]!.AsArray().Select(id => O(("object", null), ("destination", id), ("seconds", 18))).ToArray();
        foreach (var e in exits)
        {
            long dest = N(e["destination"]);
            long? obj = e["object"] == null ? null : N(e["object"]);
            if (!Maps.ContainsKey(dest) || forbidden.Contains(dest) || B(Maps[dest]["Hunting"]) || Blocked.Contains((node, obj)))
                continue;
            string target = S(e["target"]);
            if (!Regions.ContainsKey(target) || MapOf(target) != dest)
                target = Abstract(dest);
            double ordinary = region == null ? 0 : DailyRegionRouter.D(region["walkingSpeed"]);
            double walking = ordinary > 0 && e["distance"] != null ? Math.Max(.25, DailyRegionRouter.D(e["distance"])) / ordinary : 0;
            // Arrival time may include a temporary buff. Do not bake that into
            // the next unbuffed route; use observed geometry whenever available.
            double seconds = walking > 0 ? walking + 4 : Math.Max(1, DailyRegionRouter.D(e["seconds"]));
            yield return (target, O(("kind", "walk"), ("origin", node), ("target", target), ("map", mid), ("destination", dest), ("object", obj), ("seconds", seconds), ("walking_seconds", walking), ("casts", 0)));
        }
        if (!teleports) yield break;
        // Separate landing nodes reconnect regions even within the SAME map.
        foreach (var shortcut in quickTargets)
        {
            long dest = N(shortcut["Map"]), npc = N(shortcut["Npc"]);
            string target = QuickNode(dest, npc);
            if (target == node || forbidden.Contains(dest) || B(Maps[dest]["Hunting"]) || B(row["Hunting"])
                || BlockedQuick.Contains((node, npc)) || node == Current && B(shortcut["AtTarget"])) continue;
            string timeKey = mid == dest ? "sameMapSeconds" : "crossMapSeconds";
            double seconds = Regions.TryGetValue(target, out var landing) && landing[timeKey] != null
                ? DailyRegionRouter.D(landing[timeKey]) : mid == dest ? DailyQuickTravel.SameMapTransitionSeconds : DailyQuickTravel.TransitionSeconds;
            yield return (target, O(("kind", "quick"), ("origin", node), ("target", target), ("map", mid),
                ("destination", dest), ("object", npc), ("seconds", seconds), ("casts", 0), ("walking_distance", 0)));
        }
        if (BlockedPorts.Contains(node))
            yield break;
        bool portal = region != null ? B(region["portal"]) : B(row["HasWaypoint"]), summon = B(row["CanSummon"]) && (node != Current || CurrentGround);
        if (!portal && !summon)
            yield break;
        double speed = region == null ? WalkSpeed : DailyRegionRouter.D(region["walkingSpeed"]);
        if (!double.IsFinite(speed) || speed <= 0) speed = WalkSpeed;
        double free = portal ? (region == null ? 20 : DailyRegionRouter.D(region["portalDistance"]) / speed + 10) : double.PositiveInfinity, paid = summon ? 55 : double.PositiveInfinity;
        string mode = free <= paid ? "native" : "talent";
        double cost = mode == "native" ? free : 10;
        foreach (var p in Maps)
        {
            long dest = p.Key;
            if (dest == mid || forbidden.Contains(dest) || WarpBlocked.Contains(dest) || B(p.Value["Hunting"]) || !B(p.Value["HasWaypoint"]))
                continue;
            string target = Destination(dest);
            double walking = region != null && portal ? DailyRegionRouter.D(region["portalDistance"]) / speed : 0;
            yield return (target, O(("kind", "teleport"), ("mode", mode), ("origin", node), ("target", target), ("map", mid), ("destination", dest), ("seconds", cost), ("walking_seconds", mode == "native" ? walking : 0), ("casts", mode == "talent" ? 1 : 0)));
            // An active speed effect can make walking to a portal preferable
            // even when its ordinary cost would favour summoning one.
            if (mode == "talent" && portal && speedRemaining > 0 && speedBonus > 0)
                yield return (target, O(("kind", "teleport"), ("mode", "native"), ("origin", node), ("target", target), ("map", mid), ("destination", dest), ("seconds", free), ("walking_seconds", walking), ("casts", 0)));
        }
    }
    public JsonObject Plan(IEnumerable<long> targets, long? returnMap = null, bool teleports = true, IEnumerable<long>? forbiddenMaps = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var pending = targets.Distinct().Order().ToArray();
        var forbidden = (forbiddenMaps ?? []).ToHashSet();
        Require(pending.All(id => Maps.ContainsKey(id) && !forbidden.Contains(id)), "Collection target outside map graph");
        Require(pending.Length <= 12 && Current != null, "Route needs current region and at most twelve destinations");
        var bits = pending.Select((id, i) => (id, bit: 1 << i)).ToDictionary(p => p.id, p => p.bit);
        int all = (1 << bits.Count) - 1;
        bool timedSpeed = speedRemaining > 0 && speedBonus > 0;
        var start = (Node: Current!, Mask: bits.GetValueOrDefault(MapOf(Current!)), SpeedCasts: 0);
        var queue = new PriorityQueue<(string Node, int Mask, int SpeedCasts), (double Cost, int Casts, int Hops, long Serial)>();
        long serial = 0;
        queue.Enqueue(start, (0, 0, 0, serial++));
        var distance = new Dictionary<(string, int, int), (double Cost, int Casts, int Hops)> { { start, (0, 0, 0) } };
        var parents = new Dictionary<(string, int, int), ((string, int, int) Parent, JsonObject Edge)>();
        // Geometry is identical for every visited-map mask; build edges only once.
        var mapIds = new Dictionary<string, long>();
        long MapId(string node)
        {
            if (!mapIds.TryGetValue(node, out var id)) mapIds[node] = id = MapOf(node);
            return id;
        }
        var adjacency = new Dictionary<string, (string Target, JsonObject Action, double Seconds, int Casts, int Mask)[]>();
        (string Target, JsonObject Action, double Seconds, int Casts, int Mask)[] Outgoing(string node)
        {
            if (!adjacency.TryGetValue(node, out var edges)) adjacency[node] = edges = Edges(node, teleports, forbidden)
                .Select(e => (e.Target, e.Action, DailyRegionRouter.D(e.Action["seconds"]),
                    I(e.Action["casts"]), bits.GetValueOrDefault(MapId(e.Target)))).ToArray();
            return edges;
        }
        (string Node, int Mask, int SpeedCasts)? finish = null;
        while (queue.TryDequeue(out var state, out var weight))
        {
            if (distance[state] != (weight.Cost, weight.Casts, weight.Hops))
                continue;
            if (state.Mask == all && (returnMap == null || MapId(state.Node) == returnMap))
            {
                finish = state;
                break;
            }
            if (distance.Count > 100000)
                throw new DailyTravelBlocked("地图路线规划超出状态预算");
            foreach (var (target, edge, seconds, casts, mask) in Outgoing(state.Node))
            {
                double effective = seconds;
                if (timedSpeed)
                {
                    double elapsed = weight.Cost - weight.Casts * SummonPenalty;
                    double walking = DailyRegionRouter.D(edge["walking_seconds"]);
                    effective -= walking - WalkingSeconds(walking, Math.Max(0, speedRemaining - elapsed), speedBonus);
                }
                // Equal summoning counts have equal penalties, so earliest
                // arrival dominates. Keep differing counts separate while an
                // effect is active: its expiry follows elapsed time, not cost.
                var next = (target, state.Mask | mask, timedSpeed ? weight.Casts + casts : 0);
                var score = (Cost: weight.Cost + effective + casts * SummonPenalty, Casts: weight.Casts + casts, Hops: weight.Hops + 1);
                if (!distance.TryGetValue(next, out var old) || score.CompareTo(old) < 0)
                {
                    distance[next] = score;
                    var timedEdge = edge;
                    if (timedSpeed) { timedEdge = edge.DeepClone().AsObject(); timedEdge["seconds"] = effective; }
                    parents[next] = (state, timedEdge);
                    queue.Enqueue(next, (score.Cost, score.Casts, score.Hops, serial++));
                }
            }
        }
        if (finish == null)
            throw new DailyTravelBlocked("观察到的路线无法覆盖剩余地图");
        var actions = new List<JsonObject>();
        var cursor = finish.Value;
        while (cursor != start)
        {
            var p = parents[cursor];
            actions.Add(p.Edge);
            cursor = p.Parent;
        }
        actions.Reverse();
        return O(("pending", pending), ("return_map", returnMap), ("actions", Array(actions)), ("estimated_seconds", Math.Round(actions.Sum(a => DailyRegionRouter.D(a["seconds"])), 2)), ("weighted_cost", Math.Round(distance[finish.Value].Cost, 2)), ("summon_casts", distance[finish.Value].Casts), ("states", distance.Count), ("expanded_regions", adjacency.Count), ("planning_ms", watch.Elapsed.TotalMilliseconds));
    }
}

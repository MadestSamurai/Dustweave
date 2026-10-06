using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyTravelBlocked(string message) : Exception(message);
public sealed class DailyQuotaExhausted(string message) : Exception(message);
public sealed class DailyCollectionTargetsRemain(string message) : Exception(message);
public sealed class DailyCollectionSyncRequired(string message) : Exception(message);

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
    public string Observe(string node, JsonObject[] exits, double[] ports, bool ground)
    {
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
            row["seconds"] = Math.Max(.25, DailyRegionRouter.D(e["distance"])) / WalkSpeed + 4;
            edges.Add(row);
        }
        Regions[node] = O(("map", map), ("exits", edges), ("portal", ports.Length > 0), ("portalDistance", ports.Length == 0 ? 0 : ports.Min()));
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
            yield return (target, O(("kind", "walk"), ("origin", node), ("target", target), ("map", mid), ("destination", dest), ("object", obj), ("seconds", Math.Max(1, DailyRegionRouter.D(e["seconds"]))), ("casts", 0)));
        }
        if (!teleports || BlockedPorts.Contains(node))
            yield break;
        bool portal = region != null ? B(region["portal"]) : B(row["HasWaypoint"]), summon = B(row["CanSummon"]) && (node != Current || CurrentGround);
        if (!portal && !summon)
            yield break;
        double free = portal ? (region == null ? 20 : DailyRegionRouter.D(region["portalDistance"]) / WalkSpeed + 10) : double.PositiveInfinity, paid = summon ? 55 : double.PositiveInfinity;
        string mode = free <= paid ? "native" : "talent";
        double cost = mode == "native" ? free : 10;
        foreach (var p in Maps)
        {
            long dest = p.Key;
            if (dest == mid || forbidden.Contains(dest) || WarpBlocked.Contains(dest) || B(p.Value["Hunting"]) || !B(p.Value["HasWaypoint"]))
                continue;
            string target = Destination(dest);
            yield return (target, O(("kind", "teleport"), ("mode", mode), ("origin", node), ("target", target), ("map", mid), ("destination", dest), ("seconds", cost), ("casts", mode == "talent" ? 1 : 0)));
        }
    }
    public JsonObject Plan(IEnumerable<long> targets, long? returnMap = null, bool teleports = true, IEnumerable<long>? forbiddenMaps = null)
    {
        var pending = targets.Distinct().Order().ToArray();
        var forbidden = (forbiddenMaps ?? []).ToHashSet();
        Require(pending.All(id => Maps.ContainsKey(id) && !forbidden.Contains(id)), "Collection target outside map graph");
        Require(pending.Length <= 12 && Current != null, "Route needs current region and at most twelve destinations");
        var bits = pending.Select((id, i) => (id, bit: 1 << i)).ToDictionary(p => p.id, p => p.bit);
        int all = (1 << bits.Count) - 1;
        var start = (Node: Current!, Mask: bits.GetValueOrDefault(MapOf(Current!)));
        var queue = new PriorityQueue<(string Node, int Mask), (double Cost, int Casts, int Hops, long Serial)>();
        long serial = 0;
        queue.Enqueue(start, (0, 0, 0, serial++));
        var distance = new Dictionary<(string, int), (double Cost, int Casts, int Hops)> { { start, (0, 0, 0) } };
        var parents = new Dictionary<(string, int), ((string, int) Parent, JsonObject Edge)>();
        (string Node, int Mask)? finish = null;
        while (queue.TryDequeue(out var state, out var weight))
        {
            if (distance[state] != (weight.Cost, weight.Casts, weight.Hops))
                continue;
            if (state.Mask == all && (returnMap == null || MapOf(state.Node) == returnMap))
            {
                finish = state;
                break;
            }
            if (distance.Count > 100000)
                throw new DailyTravelBlocked("地图路线规划超出状态预算");
            foreach (var (target, edge) in Edges(state.Node, teleports, forbidden))
            {
                var next = (target, state.Mask | bits.GetValueOrDefault(MapOf(target)));
                var score = (Cost: weight.Cost + DailyRegionRouter.D(edge["seconds"]) + N(edge["casts"]) * SummonPenalty, Casts: weight.Casts + I(edge["casts"]), Hops: weight.Hops + 1);
                if (!distance.TryGetValue(next, out var old) || score.CompareTo(old) < 0)
                {
                    distance[next] = score;
                    parents[next] = (state, edge);
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
        return O(("pending", pending), ("return_map", returnMap), ("actions", Array(actions)), ("estimated_seconds", Math.Round(actions.Sum(a => DailyRegionRouter.D(a["seconds"])), 2)), ("weighted_cost", Math.Round(distance[finish.Value].Cost, 2)), ("summon_casts", distance[finish.Value].Casts), ("states", distance.Count));
    }
}

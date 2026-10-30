using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class QuickTravelCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("QuickTravel: " + message); }
        RecoveryGraph(checks);
        RecoveryStress(root, checks);
        JsonObject Map(long id, params long[] exits) => O(("Id", id), ("Exits", exits));
        JsonObject Exit(long obj, long map, double distance) => O(("object", obj), ("destination", map), ("distance", distance));
        JsonObject Target(long npc, long map) => O(("Npc", npc), ("Map", map), ("X", 50), ("Y", 0), ("Z", 0), ("AtTarget", false));
        var maps = new[] { Map(1, 2), Map(2, 1, 3), Map(3, 2) };
        foreach (double portalDistance in new[] { 3d, 300d })
        {
            var dual = new DailyRouteAtlas([O(("Id", 1), ("Exits", new JsonArray()), ("CanSummon", true)),
                O(("Id", 2), ("Exits", new JsonArray()), ("HasWaypoint", true))]);
            dual.Observe("[1,[]]", [], [portalDistance], true);
            var preferred = Rows(dual.Plan([2])["actions"])[0];
            dual.RejectDeparture(preferred);
            var alternate = Rows(dual.Plan([2])["actions"])[0];
            Check(S(preferred["mode"]) != S(alternate["mode"]) && N(alternate["destination"]) == 2,
                "excluding one teleport departure preserves the independently usable alternative at distance " + portalDistance);
        }
        var atlas = new DailyRouteAtlas(maps);
        atlas.Observe("[1,[]]", [Exit(12, 2, 300)], [], false);
        atlas.ObserveQuick([Target(102, 2)]);
        var plan = atlas.Plan([2]);
        Check(S(Rows(plan["actions"])[0]["kind"]) == "quick" && N(plan["summon_casts"]) == 0, "free native shortcut beats a long walk without a waypoint");
        Check(S(Rows(atlas.Plan([2], teleports: false)["actions"])[0]["kind"]) == "walk", "explicit walking-only route stays walking-only");
        atlas.BlockedQuick.Add(("[1,[]]", 102));
        Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "walk", "unavailable shortcut falls back to walking");
        atlas.BlockedQuick.Clear();
        atlas.Observe("[1,[]]", [Exit(12, 2, 1)], [], false);
        Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "walk", "nearby gate beats shortcut loading time");
        atlas.Observe("[1,[]]", [Exit(12, 2, 4)], [], false);
        Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "quick", "legacy speed estimate biases a four-unit gate toward a shortcut");
        atlas.Observe("[1,[]]", [Exit(12, 2, 4)], [], false, walkingSpeed: 5);
        Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "walk", "observed native speed keeps a short gate walk instead of an unnecessary shortcut");
        Check(Math.Abs(DailyRegionRouter.D(atlas.Plan([2])["estimated_seconds"]) - 4.8) < .001, "walking estimate uses map speed without assuming a speed talent");
        foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            atlas.Observe("[1,[]]", [Exit(12, 2, 4)], [], false, invalid);
            Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "quick", "missing or invalid walking speed keeps the legacy fallback");
        }
        atlas.Observe("[1,[]]", [Exit(12, 2, 300)], [], false);
        var copy = new DailyRouteAtlas(maps, atlas.Export()); copy.Observe("[1,[]]", [Exit(12, 2, 300)], [], false);
        Check(S(Rows(copy.Plan([2])["actions"])[0]["kind"]) == "walk", "saved geometry never grants another account shortcut availability");

        var speedRoute = new DailyRouteAtlas(maps);
        speedRoute.Observe("[1,[]]", [Exit(12, 2, 50)], [], false, 5);
        speedRoute.Observe("[2,[]]", [Exit(23, 3, 50)], [], false, 5);
        speedRoute.Reached("[1,[]]", 12, "[2,[]]", 9);
        speedRoute.Observe("[1,[]]", [Exit(12, 2, 50)], [], false, 5);
        speedRoute.ObserveSpeed(15, .75);
        var sped = speedRoute.Plan([3], teleports: false);
        Check(Math.Abs(DailyRegionRouter.D(sped["estimated_seconds"]) - 19.75) < .001,
            "speed survives a gate but expires part-way through the next walk; map loading consumes its remaining duration");
        var ordinaryCopy = new DailyRouteAtlas(maps, speedRoute.Export());
        ordinaryCopy.Observe("[1,[]]", [Exit(12, 2, 50)], [], false, 5);
        Check(DailyRegionRouter.D(ordinaryCopy.Plan([3], teleports: false)["estimated_seconds"]) == 28,
            "persisted route geometry cannot retain yesterday's speed effect or accelerated arrival time");
        speedRoute.ObserveSpeed(0, .75);
        Check(DailyRegionRouter.D(speedRoute.Plan([3], teleports: false)["estimated_seconds"]) == 28,
            "expired speed immediately restores ordinary planning costs");
        speedRoute.ObserveSpeed(40, double.NaN);
        Check(DailyRegionRouter.D(speedRoute.Plan([3], teleports: false)["estimated_seconds"]) == 28,
            "unknown speed magnitude never invents a 75-percent bonus");
        var quickSpeed = new DailyRouteAtlas(maps);
        quickSpeed.Observe("[2,[]]", [Exit(23, 3, 50)], [], false, 5);
        quickSpeed.ObserveQuick([Target(102, 2)]);
        quickSpeed.ReachedQuick(102, "[2,[]]", 5);
        quickSpeed.Observe("[1,[]]", [], [], false, 5);
        quickSpeed.ObserveSpeed(8, .75);
        Check(Math.Abs(DailyRegionRouter.D(quickSpeed.Plan([3])["estimated_seconds"]) - 16.75) < .001,
            "quick travel consumes buff time and preserves only the remaining speed for the landing walk");
        Check(DailyRouteAtlas.WalkingSeconds(10, 40, .75) < DailyRouteAtlas.WalkingSeconds(10, 40, .25),
            "known slower native speed bonuses are costed separately");
        var speedReading = O(("Readings", new JsonArray(O(("Id", "mainline.movement"), ("Error", ""),
            ("Values", new JsonArray(O(("Path", "GetTalentMoveSpeedUpValue()"), ("Error", ""), ("Json", "0.75"))))))));
        Check(DailyCollectionNavigator.MovementSpeedBonus(speedReading) == .75, "native speed magnitude is read without assuming a particular talent level");
        speedReading["Readings"]![0]!["Values"]![0]!["Error"] = "Only vetted read-only predicates allowed";
        Check(DailyCollectionNavigator.MovementSpeedBonus(speedReading) == 0, "optional speed getter rejection falls back to ordinary routing");
        speedReading["Readings"]![0]!["Values"]![0]!["Error"] = "";
        speedReading["Readings"]![0]!["Values"]![0]!["Json"] = "unavailable";
        Check(DailyCollectionNavigator.MovementSpeedBonus(speedReading) == 0, "invalid optional speed value cannot stop walking");
        Check(DailyCollectionNavigator.MovementSpeedBonus(O(("Readings", new JsonArray()))) == 0, "older observations without speed magnitude remain navigable");

        // Exhaustive paths on small graphs provide an independent oracle for
        // expiring speed plus summoning penalties (which are not elapsed time).
        var random = new Random(20261026);
        for (int sample = 0; sample < 128; sample++)
        {
            var lengths = new double[4, 4];
            var summon = Enumerable.Range(0, 4).Select(_ => random.Next(2) == 0).ToArray();
            var portals = Enumerable.Range(0, 4).Select(i => (sample + i) % 3 == 0 ? (double?)random.Next(1, 501) : null).ToArray();
            var smallMaps = Enumerable.Range(0, 4).Select(i => O(("Id", i + 1),
                ("Exits", Enumerable.Range(1, 4).Where(j => j != i + 1).ToArray()),
                ("HasWaypoint", true), ("CanSummon", summon[i]))).ToArray();
            var candidate = new DailyRouteAtlas(smallMaps);
            string Node(int i) => "[" + (i + 1) + ",[]]";
            for (int i = 0; i < 4; i++)
            {
                var paths = new List<JsonObject>();
                for (int j = 0; j < 4; j++) if (i != j)
                {
                    lengths[i, j] = random.Next(1, 701);
                    paths.Add(Exit((i + 1) * 10 + j + 1, j + 1, lengths[i, j]));
                }
                candidate.Observe(Node(i), paths.ToArray(), portals[i].HasValue ? [portals[i]!.Value] : [], true, 5);
                candidate.Landing[i + 1] = Node(i);
            }
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (i != j)
                candidate.Reached(Node(i), (i + 1) * 10 + j + 1, Node(j), 4 + lengths[i, j] / 5);
            candidate.Observe(Node(0), Enumerable.Range(1, 3).Select(j => Exit(11 + j, j + 1, lengths[0, j])).ToArray(), portals[0].HasValue ? [portals[0]!.Value] : [], true, 5);
            double duration = random.Next(1, 81), bonus = sample % 2 == 0 ? .75 : .25;
            candidate.ObserveSpeed(duration, bonus);
            double best = double.PositiveInfinity;
            var visited = new HashSet<(int, int)>();
            void Enumerate(int position, int mask, double elapsed, int casts)
            {
                double cost = elapsed + casts * 45;
                if (cost >= best) return;
                if (position == 0 && mask == 3) { best = cost; return; }
                if (!visited.Add((position, mask))) return;
                for (int next = 0; next < 4; next++) if (next != position)
                {
                    int updated = mask | (next == 2 ? 1 : next == 3 ? 2 : 0);
                    double left = Math.Max(0, duration - elapsed), acceleratedDistance = Math.Min(lengths[position, next], left * 5 * (1 + bonus));
                    double travel = acceleratedDistance / (5 * (1 + bonus)) + (lengths[position, next] - acceleratedDistance) / 5 + 4;
                    Enumerate(next, updated, elapsed + travel, casts);
                    if (summon[position]) Enumerate(next, updated, elapsed + 10, casts + 1);
                    if (portals[position] is double distance)
                    {
                        double accelerated = Math.Min(distance, left * 5 * (1 + bonus));
                        Enumerate(next, updated, elapsed + accelerated / (5 * (1 + bonus)) + (distance - accelerated) / 5 + 10, casts);
                    }
                }
                visited.Remove((position, mask));
            }
            Enumerate(0, 0, 0, 0);
            Check(Math.Abs(DailyRegionRouter.D(candidate.Plan([3, 4], returnMap: 1)["weighted_cost"]) - best) <= .0051,
                "expiring-speed itinerary with independent portal and summon choices agrees with exhaustive route search " + sample);
        }

        // One map has disconnected regions. The shop landing is not the current
        // position, and its short exit must survive later movement on that map.
        atlas.Observe("[1,[12]]", [Exit(12, 2, 3)], [], false);
        atlas.ObserveQuick([Target(101, 1)]);
        atlas.ReachedQuick(101, "[1,[12]]", 3, sameMap: true);
        atlas.Observe("[1,[]]", [], [], false);
        plan = atlas.Plan([2]);
        Check(Rows(plan["actions"]).Select(a => S(a["kind"])).SequenceEqual(new[] { "quick", "walk" }), "same-map shortcut reconnects a disconnected region");
        Check(DailyRegionRouter.D(plan["weighted_cost"]) == 8, "shortcut landing retains actual exit distance");
        atlas.Observe("[1,[12]]", [Exit(12, 2, 300)], [], false);
        Check(S(Rows(atlas.Plan([2])["actions"])[0]["kind"]) == "quick", "landing costs are independent of current position on the same map");
        string oldAnchor = DailyRouteAtlas.QuickNode(1, 101);
        atlas.BlockedQuick.Add((atlas.Current!, 101)); atlas.BlockedNativePorts.Add(oldAnchor); atlas.Landing[1] = oldAnchor;
        var moved = Target(101, 1); moved["X"] = 999; atlas.ObserveQuick([moved]);
        Check(!atlas.Regions.ContainsKey(DailyRouteAtlas.QuickNode(1, 101)), "changed native destination invalidates old landing geometry");
        Check(!atlas.BlockedQuick.Any(p => p.Npc == 101) && !atlas.BlockedNativePorts.Contains(oldAnchor) && !atlas.Landing.ContainsKey(1),
            "a moved shortcut anchor invalidates only its obsolete failures and teleport landing references");
        atlas.ObserveQuick([]);
        atlas.ReachedQuick(101, "[1,[12]]", 3);
        Check(!atlas.Regions.ContainsKey(DailyRouteAtlas.QuickNode(1, 101)), "landing without a visible shortcut list does not block travel or invent geometry");
        atlas.ObserveQuick([Target(103, 3)]);
        plan = atlas.Plan([2], forbiddenMaps: [3]);
        Check(Rows(plan["actions"]).All(a => N(a["destination"]) != 3), "excluded map cannot become a shortcut transit hub");
        var huntingMaps = new[] { Map(1, 2), Map(2, 1) }; huntingMaps[1]["Hunting"] = true;
        var hunting = new DailyRouteAtlas(huntingMaps); hunting.Observe("[1,[]]", [], [], false); hunting.ObserveQuick([Target(102, 2)]);
        bool blocked = false; try { hunting.Plan([2]); } catch (DailyTravelBlocked) { blocked = true; }
        Check(blocked, "shortcut does not enter hunting grounds");

        foreach (string mode in new[] { "arrives", "unavailable", "native-unavailable", "settled-failure", "identity-changed" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "quick-" + mode), []);
            h.Page("GameFieldDefaultUI");
            string state = "idle"; bool changed = false;
            h.Readings = () => [WorkflowCases.Reading("route.quick", ("$self", O(("Pack", 1), ("Map", 1), ("Npc", 101), ("State", state),
                ("Targets", mode == "unavailable" ? new JsonArray() : new JsonArray(Target(101, 1))))))];
            h.OnCommand = c => { Check(S(c["Kind"]) == "field_quick_travel", "executor uses native shortcut only"); state = "moving"; h.Page("LoadingUI"); };
            if (mode == "native-unavailable") { h.Box.State = "rejected"; h.Box.Error = "quick_travel_unavailable"; h.Box.Dispatched = false; }
            h.OnDelay = () =>
            {
                if (h.Time < 1 || changed) return;
                changed = true; h.Page("GameFieldDefaultUI");
                if (mode == "arrives") state = "arrived";
                if (mode == "identity-changed") h.Frame["AccountKey"] = "different-account";
            };
            bool result = false, identityRejected = false;
            try { result = await DailyQuickTravel.TryMove(h.Workflow, 101, 1); }
            catch (InvalidDataException) when (mode == "identity-changed") { identityRejected = true; }
            Check(mode == "arrives" ? result && h.Time >= 1 : mode == "identity-changed" ? identityRejected : !result,
                "same-map transition handles " + mode);
            Check(h.Box.Commands.Count == (mode == "unavailable" ? 0 : 1), "no shortcut replay for " + mode);
        }

        // Twelve destinations exercise the actual multi-goal search, not a
        // single straight-line toy path. Record timing without a flaky wall-clock gate.
        var stress = new DailyRouteAtlas(Enumerable.Range(1, 12).Select(i => Map(i, Enumerable.Range(1, 12).Where(j => j != i).Select(j => (long)j).ToArray())).ToArray());
        stress.Observe("[1,[]]", Enumerable.Range(2, 11).Select(i => Exit(i, i, 30)).ToArray(), [], false);
        plan = stress.Plan(Enumerable.Range(2, 11).Select(i => (long)i));
        Check(Rows(plan["actions"]).Length == 11 && N(plan["expanded_regions"]) <= 13, "multi-goal search builds adjacency once per region");
        DailyJson.Write(Path.Combine(root, "quick-route-benchmark.json"), plan);
        var alternativesBenchmark = new JsonArray();
        foreach (int variant in Enumerable.Range(0, 4))
        {
            var allMaps = Enumerable.Range(1, 12).Select(i => O(("Id", i),
                ("Exits", new[] { i % 12 + 1 }), ("HasWaypoint", true), ("CanSummon", true))).ToArray();
            var complete = new DailyRouteAtlas(allMaps);
            string Node(int id) => "[" + id + ",[]]";
            double Portal(int id) => variant == 0 ? 0 : 20 + id * 19;
            for (int id = 1; id <= 12; id++)
            {
                complete.Observe(Node(id), [Exit(id, id % 12 + 1, 80)], [Portal(id)], true, 5);
                complete.Landing[id] = Node(id);
                if (variant == 3 && id % 2 == 0) complete.BlockedNativePorts.Add(Node(id));
            }
            for (int id = 1; id <= 12; id++) complete.Reached(Node(id), id, Node(id % 12 + 1), 20);
            complete.Observe(Node(1), [Exit(1, 2, 80)], [Portal(1)], true, 5);
            if (variant >= 2) complete.ObserveSpeed(60, .75);
            var itinerary = complete.Plan(Enumerable.Range(2, 11).Select(id => (long)id), returnMap: 1);
            Check(Enumerable.Range(2, 11).All(id => Rows(itinerary["actions"]).Any(a => N(a["destination"]) == id))
                && N(Rows(itinerary["actions"])[^1]["destination"]) == 1,
                "twelve-map itinerary with independent teleport alternatives completes and returns, variant " + variant);
            alternativesBenchmark.Add(O(("variant", variant), ("planning_ms", itinerary["planning_ms"]),
                ("expanded_regions", itinerary["expanded_regions"]), ("states", itinerary["states"]), ("actions", Rows(itinerary["actions"]).Length)));
        }
        DailyJson.Write(Path.Combine(root, "alternative-route-benchmark.json"), alternativesBenchmark);
    }
    private static void RecoveryStress(string root, List<string> checks)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("Route fault exploration: " + message); }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int totalPlans = 0, maximumSteps = 0; double maximumPlanMs = 0;
        // Both historical cartridges have ordinary door routes. Disabling every
        // portal and summon must never remove those independent connections.
        var seeds = DailyJson.TryRead<JsonObject>(Path.Combine(TestPaths.SourceRoot, "assets", "flows", "route-atlas.json"))!;
        foreach (long pack in new long[] { 15, 16 })
        {
            var seed = seeds["packs"]![pack.ToString()]!.AsObject();
            var maps = Rows(seed["maps"]).Select(m => m.DeepClone().AsObject()).ToArray();
            foreach (var map in maps) map["CanSummon"] = false;
            var graph = new DailyRouteAtlas(maps, seed, pack);
            // The capability change deliberately invalidates seeds; load only
            // the public geometry, with the adjusted capability fingerprint.
            var adjusted = seed.DeepClone().AsObject(); adjusted["maps"] = Array(maps);
            graph = new DailyRouteAtlas(maps, adjusted, pack);
            long current = pack * 10 + 1;
            var todo = (pack == 15 ? new long[] { 153, 154 } : [162, 163]).ToHashSet();
            for (int step = 0; step < 24 && todo.Count > 0; step++)
            {
                string physical = graph.Regions.First(p => N(p.Value["map"]) == current).Key;
                var geometry = graph.Regions[physical];
                var exits = Rows(geometry["exits"]).Select(e => O(("object", e["object"]), ("destination", e["destination"]),
                    ("distance", Math.Max(.25, DailyRegionRouter.D(e["seconds"]) - 4) * 3))).ToArray();
                graph.Observe(physical, exits, B(geometry["portal"]) ? [DailyRegionRouter.D(geometry["portalDistance"])] : [], true);
                graph.BlockedNativePorts.Add(physical);
                var action = Rows(graph.Plan(todo)["actions"])[0];
                Check(S(action["kind"]) == "walk", "cartridge " + pack + " preserves an ordinary door with all portal departures disabled, step " + step);
                current = N(action["destination"]); todo.Remove(current);
            }
            Check(todo.Count == 0, "cartridge " + pack + " visits every remaining collection map without teleports");
        }
        // Strongly connected directed maps provide an independent reachability
        // oracle: the ring remains walkable, while optional connections fail.
        // Execute the real planner one edge at a time, learning actual landings.
        for (int sample = 0; sample < 240; sample++)
        {
            var random = new Random(91703 + sample); int count = 4 + sample % 4;
            var maps = Enumerable.Range(1, count).Select(id => O(("Id", id), ("Exits", Enumerable.Range(1, count)
                .Where(next => next != id && (next == id % count + 1 || random.Next(3) == 0)).ToArray()),
                ("HasWaypoint", random.Next(3) != 0), ("CanSummon", random.Next(2) == 0), ("MapType", 0))).ToArray();
            var distances = maps.ToDictionary(m => N(m["Id"]), m => m["Exits"]!.AsArray().Select(N).ToDictionary(id => id, _ => (double)random.Next(1, 240)));
            var quick = Enumerable.Range(1, 1 + sample % 3).Select(npc => O(("Npc", npc), ("Map", random.Next(1, count + 1)), ("X", npc * 9), ("Y", 0), ("Z", 0))).ToArray();
            var graph = new DailyRouteAtlas(maps);
            long current = 1, quickNpc = 0; double elapsed = 0;
            string Physical(long id) => new JsonArray(id, new JsonArray("physical")).ToJsonString();
            void Observe()
            {
                graph.RefreshMaps(maps);
                graph.Observe(Physical(current), distances[current].Select(p => O(("object", current * 100 + p.Key),
                    ("destination", p.Key), ("distance", p.Value))).ToArray(), B(maps[current - 1]["HasWaypoint"]) ? [8] : [], true);
                graph.ObserveQuick(quick.Select(q => { var target = q.DeepClone().AsObject(); target["AtTarget"] = current == N(q["Map"]) && quickNpc == N(q["Npc"]); return target; }));
                graph.ObserveSpeed(sample % 2 == 0 ? Math.Max(0, 60 - elapsed) : 0, .75);
            }
            if (sample % 3 == 0) { for (int i = 1; i <= count; i++) { current = i; Observe(); } current = 1; }
            var todo = Enumerable.Range(2, count - 1).Select(id => (long)id).ToHashSet();
            var trace = new JsonArray(); int step;
            for (step = 0; step < 100 && todo.Count > 0; step++)
            {
                Observe(); var plan = graph.Plan(todo); totalPlans++;
                maximumPlanMs = Math.Max(maximumPlanMs, DailyRegionRouter.D(plan["planning_ms"]));
                var action = Rows(plan["actions"])[0]; trace.Add(action.DeepClone());
                string origin = graph.Current!; long destination = N(action["destination"]); bool moved = false;
                switch (S(action["kind"]))
                {
                    case "walk":
                        if (sample % 3 == 1 && destination != current % count + 1) graph.Blocked.Add((origin, N(action["object"])));
                        else { current = destination; quickNpc = 0; Observe(); graph.Reached(origin, N(action["object"]), graph.Current!, DailyRegionRouter.D(action["seconds"])); moved = true; }
                        break;
                    case "quick":
                        current = destination; quickNpc = N(action["object"]); Observe(); graph.ReachedQuick(quickNpc, graph.Current!, 5); moved = true; break;
                    case "teleport":
                        if (sample % 4 != 0)
                        {
                            if (S(action["mode"]) == "native") graph.BlockedNativePorts.Add(origin); else graph.BlockedSummons.Add(origin);
                        }
                        else { current = destination; quickNpc = 0; Observe(); graph.Landing[current] = graph.Current!; moved = true; }
                        break;
                    default: throw new Exception("Unexpected planner operation");
                }
                elapsed += DailyRegionRouter.D(action["seconds"]);
                if (moved) todo.Remove(current);
            }
            maximumSteps = Math.Max(maximumSteps, step);
            if (todo.Count > 0) DailyJson.Write(Path.Combine(root, "failed-route-exploration-" + sample + ".json"), O(("maps", Array(maps)), ("trace", trace), ("graph", graph.Export())));
            Check(todo.Count == 0, "connected map scenario " + sample + " completes despite failed optional portals and doors");
        }
        DailyJson.Write(Path.Combine(root, "route-recovery-benchmark.json"), O(("scenarios", 240), ("plans", totalPlans),
            ("maximum_steps", maximumSteps), ("maximum_plan_ms", maximumPlanMs), ("elapsed_ms", watch.Elapsed.TotalMilliseconds)));
    }
    private static void RecoveryGraph(List<string> checks)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("QuickTravel recovery: " + message); }
        JsonObject Map(long id, params long[] exits) => O(("Id", id), ("Exits", exits), ("HasWaypoint", true), ("CanSummon", true), ("MapType", 0));
        JsonObject Target(long npc, long map, bool at = false) => O(("Npc", npc), ("Map", map), ("X", npc * 10), ("Y", 0), ("Z", 0), ("AtTarget", at));
        JsonObject Exit(long obj, long map, double distance) => O(("object", obj), ("destination", map), ("distance", distance));
        foreach (var (sameMap, summonAtStart) in new[] { (true, true), (false, true), (false, false) })
        {
            long start = sameMap ? 151 : 161, hub = sameMap ? 151 : 164;
            long[] goals = sameMap ? [153, 154] : [162, 163];
            var maps = sameMap ? new[] { Map(start), Map(goals[0]), Map(goals[1]) }
                : new[] { Map(start, hub), Map(hub, start), Map(goals[0]), Map(goals[1]) };
            maps[0]["CanSummon"] = summonAtStart;
            var graph = new DailyRouteAtlas(maps, pack: sameMap ? 15 : 16);
            string Physical(long id) => new JsonArray(id, new JsonArray(new JsonArray("waypoint", 1))).ToJsonString();
            long current = start, npc = 0;
            void Observe()
            {
                graph.RefreshMaps(maps);
                graph.Observe(Physical(current), sameMap || goals.Contains(current) ? [] : [Exit(current, current == start ? hub : start, current == start ? 300 : 3)], [3], true);
                graph.ObserveQuick(sameMap ? [Target(1, hub, npc == 1), Target(2, hub, npc == 2)] : [Target(1, hub, npc == 1 && current == hub)]);
            }
            Observe();
            // Learn the same cached landing copies that appear in the report.
            current = hub; Observe(); graph.ReachedQuick(1, Physical(hub), 5);
            if (sameMap) graph.ReachedQuick(2, Physical(hub), 3.5, true);
            current = start; Observe();
            var remaining = goals.ToHashSet();
            var decisions = new HashSet<string>();
            int quickCount = 0, forcedSummons = 0;
            for (int i = 0; i < 16 && remaining.Count > 0; i++)
            {
                Observe();
                var action = Rows(graph.Plan(remaining)["actions"])[0];
                string decision = new JsonArray(string.Join(",", remaining.Order()), graph.Current, S(action["kind"]), S(action["mode"]), N(action["destination"]), action["object"]?.DeepClone()).ToJsonString();
                string knownLandings = string.Join(";", graph.Regions.OrderBy(p => p.Key).SelectMany(p => Rows(p.Value["exits"])
                    .Where(e => e["target"] != null).Select(e => p.Key + "/" + N(e["object"]) + "=" + S(e["target"]))));
                Check(decisions.Add(decision + knownLandings), "a departure is not repeated with unchanged work and geometry: " + sameMap + "/" + summonAtStart + "/" + i);
                switch (S(action["kind"]))
                {
                    case "quick":
                        quickCount++; current = N(action["destination"]); npc = N(action["object"]); Observe();
                        graph.ReachedQuick(npc, graph.Current!, 4, sameMap); break;
                    case "walk":
                        string origin = graph.Current!; current = N(action["destination"]); npc = 0; Observe();
                        graph.Reached(origin, N(action["object"]), graph.Current!, 5); break;
                    case "teleport":
                        if (!B(action["summon_only"]) && (current == start || current == hub)) graph.BlockedNativePorts.Add(graph.Current!);
                        else { if (B(action["summon_only"])) forcedSummons++; current = N(action["destination"]); npc = 0; }
                        break;
                    default: throw new Exception("Unexpected recovery decision");
                }
                remaining.Remove(current);
            }
            Check(remaining.Count == 0 && quickCount <= (sameMap || !summonAtStart ? 2 : 1) && forcedSummons == 1,
                "chapter " + (sameMap ? 15 : 16) + " reaches every target without repeated shortcuts or a map ban");
            var saved = graph.Export();
            var loaded = new DailyRouteAtlas(maps, saved);
            Check(loaded.BlockedNativePorts.Count == 0 && loaded.BlockedSummons.Count == 0, "temporary failures are not persisted into future runs");
            foreach (var row in saved["regions"]!.AsObject().Select(p => p.Value!.AsObject())) row.Remove("physicalRegion");
            var legacy = new DailyRouteAtlas(maps, saved);
            Check(!legacy.Regions.ContainsKey(DailyRouteAtlas.QuickNode(hub, 1)) && legacy.Regions.ContainsKey(Physical(hub)),
                "legacy unbound shortcut copies are relearned while physical geometry is retained");
        }
        var topology = new DailyRouteAtlas([Map(1, 2), Map(2)]);
        topology.Observe("[1,[]]", [Exit(12, 2, 1)], [0], true);
        topology.Blocked.Add(("[1,[]]", 12)); topology.BlockedNativePorts.Add("[1,[]]");
        topology.RefreshMaps([Map(1, 2), Map(2)]);
        Check(topology.Blocked.Count == 1 && topology.BlockedNativePorts.Count == 1, "identical observations cannot reset failed departures");
        topology.RefreshMaps([Map(1, 3), Map(3)]);
        topology.Observe("[1,[]]", [Exit(13, 3, 1)], [0], true);
        Check(!topology.Maps.ContainsKey(2) && topology.Blocked.Count == 0 && N(Rows(topology.Plan([3])["actions"])[0]["destination"]) == 3,
            "changed native topology replaces stale exits and reopens genuinely changed connections");
        topology.ObserveQuick([Target(1, 3)]);
        var probe = topology.SafeHubProbe([], []);
        Check(probe != null && N(probe["destination"]) == 3, "a safe native shortcut can refresh otherwise missing routes");
        Check(topology.SafeHubProbe([3], []) == null && topology.SafeHubProbe([], [1]) == null,
            "safe-hub discovery respects map scope and cannot retry the same hub indefinitely");
        topology.Maps[3]["MapType"] = 1;
        Check(topology.SafeHubProbe([], []) == null, "recovery never treats an unverified battle map as a safe hub");
        var unreachable = new DailyRouteAtlas([Map(1), Map(2), O(("Id", 3), ("Exits", new JsonArray()))]);
        unreachable.Observe("[1,[]]", [], [1], true); unreachable.ObserveSpeed(60, .75);
        bool finite = false;
        try { unreachable.Plan([3]); }
        catch (DailyTravelBlocked error) { finite = !error.Message.Contains("状态预算", StringComparison.Ordinal); }
        Check(finite, "expired speed states merge so disconnected observations cannot exhaust the planner through casting cycles");
    }
}

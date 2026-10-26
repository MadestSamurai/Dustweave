using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class QuickTravelCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("QuickTravel: " + message); }
        JsonObject Map(long id, params long[] exits) => O(("Id", id), ("Exits", exits));
        JsonObject Exit(long obj, long map, double distance) => O(("object", obj), ("destination", map), ("distance", distance));
        JsonObject Target(long npc, long map) => O(("Npc", npc), ("Map", map), ("X", 50), ("Y", 0), ("Z", 0), ("AtTarget", false));
        var maps = new[] { Map(1, 2), Map(2, 1, 3), Map(3, 2) };
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
                candidate.Observe(Node(i), paths.ToArray(), [], true, 5);
                candidate.Landing[i + 1] = Node(i);
            }
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (i != j)
                candidate.Reached(Node(i), (i + 1) * 10 + j + 1, Node(j), 4 + lengths[i, j] / 5);
            candidate.Observe(Node(0), Enumerable.Range(1, 3).Select(j => Exit(11 + j, j + 1, lengths[0, j])).ToArray(), [], true, 5);
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
                }
                visited.Remove((position, mask));
            }
            Enumerate(0, 0, 0, 0);
            Check(Math.Abs(DailyRegionRouter.D(candidate.Plan([3, 4], returnMap: 1)["weighted_cost"]) - best) <= .0051,
                "expiring-speed itinerary agrees with exhaustive route search " + sample);
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
        var moved = Target(101, 1); moved["X"] = 999; atlas.ObserveQuick([moved]);
        Check(!atlas.Regions.ContainsKey(DailyRouteAtlas.QuickNode(1, 101)), "changed native destination invalidates old landing geometry");
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
    }
}

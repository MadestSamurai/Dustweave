using Dustweave;
using Dustweave.Mansion;

internal static class MansionCases
{
    public static void Run(List<string> cases)
    {
        void Check(bool value, string text)
        {
            if (!value)
                throw new Exception(text);
            cases.Add("mansion: " + text);
        }

        Check(RunGoal.Decide(false, true, 90, true, false, 80) == ResultAction.Wait, "old or unacknowledged results cannot finish the current goal");
        Check(RunGoal.Decide(true, true, 79, false, false, 80) == ResultAction.ExitAndRetry, "successful sub-target result exits and starts a new round even with ordinary retry disabled");
        Check(RunGoal.Decide(true, false, 79, false, true, 80) == ResultAction.Retry, "defeat before target retries in goal mode");
        Check(RunGoal.Decide(true, true, 80, true, false, 80) == ResultAction.GoalCompleted, "exactly eighty in the native result finishes the goal");
        Check(RunGoal.Decide(true, false, 81, true, true, 80) == ResultAction.GoalCompleted, "acknowledged maximum still counts if the chain breaks or the round ends in defeat");
        Check(RunGoal.Decide(true, true, 40, true, false, 80) == ResultAction.ExitAndRetry && RunGoal.Decide(true, true, 40, true, false, 80) == ResultAction.ExitAndRetry, "two rounds of forty do not accumulate into eighty");
        Check(RunGoal.Decide(true, true, 90, true, false, 0) == ResultAction.Completed, "ordinary clear behavior remains a single completed run");
        Check(RunGoal.Decide(true, false, 90, false, true, 0) == ResultAction.Defeated, "ordinary retry-off remains stopped on defeat");
        Check(RunGoal.Decide(true, false, 2, true, true, 0) == ResultAction.Retry, "ordinary retry-on still retries defeat");
        Check(RunGoal.Normalize(81) == 0 && RunGoal.Normalize(-1) == 0, "unsupported target values are not silently accepted");
        Check(RunGoal.AwaitSettlement(80, 80) && !RunGoal.AwaitSettlement(80, 79) && !RunGoal.AwaitSettlement(0, 90), "only a reached goal stops further clearing before native settlement");
        var entry = new EntryIdentity("MiniGameHub", 41001, 3001);
        var retryPolicy = new ChainRetryPolicy();
        Check(retryPolicy.Observe(10, 10, 80, 68, 0, 41, 3, 1, 29, 0) == ChainRetryReason.None, "quantity shortage is debounced across fresh game frames");
        Check(retryPolicy.Observe(11, 11, 80, 68, 0, 41, 3, 1, 29, 0) == ChainRetryReason.InsufficientCandy, "broken chain with insufficient candy retries before the timer ends");
        retryPolicy.Reset();
        retryPolicy.Observe(10, 10, 80, 30, 30, 48, 3, 1, 78, 0);
        Check(retryPolicy.Observe(25, 25, 80, 30, 30, 48, 3, 2, 78, 0) == ChainRetryReason.None, "future native chain rewards protect near-eighty opportunities");
        retryPolicy.Reset();
        retryPolicy.Observe(0, 0, 80, 5, 5, 100, 3, 1, 45, 0);
        Check(retryPolicy.Observe(30, 30, 80, 5, 5, 100, 3, 1, 45, 0) == ChainRetryReason.None, "re-reading one bad route is not multiple searches");
        retryPolicy.Observe(32, 32, 80, 8, 8, 97, 3, 2, 47, 0);
        Check(retryPolicy.Observe(34, 34, 80, 10, 10, 95, 3, 3, 48, 0) == ChainRetryReason.UnpromisingRoute, "independent poor routes can abandon a map that still has enough candy");
        Check(retryPolicy.Observe(35, 35, 80, 10, 10, 95, 3, 4, 80, 0) == ChainRetryReason.None, "a viable refreshed route clears the retry suspicion");
        Check(retryPolicy.Observe(40, 40, 80, 80, 0, 2, 3, 5, 2, 0) == ChainRetryReason.None, "native eighty is never abandoned even after its chain breaks");
        retryPolicy.Reset();
        retryPolicy.Observe(1, 1, 80, 4, 4, 90, 0, 1, 77, 0);
        retryPolicy.Observe(4, 4, 80, 5, 5, 89, 0, 2, 77, 0);
        Check(retryPolicy.Observe(9, 9, 80, 7, 7, 87, 0, 3, 77, 0) == ChainRetryReason.None, "a near miss receives longer observation");
        Check(retryPolicy.Observe(14, 14, 80, 9, 9, 85, 0, 4, 77, 0) == ChainRetryReason.UnpromisingRoute, "persistently near-miss maps do not waste the full game timer");
        Check(retryPolicy.Observe(20, 20, 0, 0, 0, 1, 0, 5, 1, 0) == ChainRetryReason.None, "balanced play never activates chain retries");
        retryPolicy.Reset();
        retryPolicy.Observe(0, 0, 80, 0, 0, 100, 0, 1, 40, 0);
        retryPolicy.Observe(4, 4, 80, 0, 0, 100, 0, 2, 40, 0);
        Check(retryPolicy.Observe(10, 10, 80, 0, 0, 100, 0, 3, 40, 4) == ChainRetryReason.None, "stale search evidence cannot trigger a retry");
        var emptyEntry = new EntryIdentity("None", 0, 0);
        var recovery = new EntryRecovery();
        recovery.Observe(entry, false);
        Check(recovery.Decide(entry, false, "None", true, false) == EntryAction.Missing, "an unverified schedule cannot seed recovery");
        recovery.Observe(entry, true);
        Check(recovery.Decide(entry, true, "InGame", false, true) == EntryAction.Ready, "valid native retries preserve the current run");
        foreach (var phase in new[] { "Entering", "ReloadingStage", "Exiting" })
            Check(recovery.Decide(emptyEntry, false, phase, true, true) == EntryAction.Wait, "a visible lobby or old result cannot override transition " + phase);
        Check(recovery.Decide(emptyEntry, true, "None", true, false) == EntryAction.Reopen, "returning to the territory with a cleared entry reopens the verified minigame");
        Check(recovery.Decide(emptyEntry, true, "InGame", false, true) == EntryAction.ExitToRestore, "retry with a cleared schedule first exits through the native result");
        Check(recovery.Decide(emptyEntry, true, "InGame", false, false) == EntryAction.Missing, "an unfinished run cannot be silently replaced");
        Check(recovery.Decide(emptyEntry, true, "None", false, false) == EntryAction.Missing, "no reopening after the user leaves the minigame lobby");
        Check(recovery.Decide(emptyEntry, false, "None", true, false) == EntryAction.Unavailable, "expired or removed schedules cannot be restored");
        foreach (var changed in new[] { new EntryIdentity("MiniGameHub", 41002, 3001), new EntryIdentity("MiniGameHub", 41001, 3002), new EntryIdentity("EventHub", 41001, 3001), new EntryIdentity("None", 41001, 0) })
            Check(recovery.Decide(changed, true, "None", true, false) == EntryAction.Changed, "conflicting or partially cleared context is not overwritten: " + changed.Origin + changed.ScheduleId + changed.MiniGameId);
        recovery.Reopening();
        Check(recovery.Decide(emptyEntry, true, "None", true, false) == EntryAction.Missing && recovery.Recoveries == 1, "failed entry recovery cannot loop indefinitely");
        Check(recovery.Decide(entry, true, "None", true, false) == EntryAction.Ready, "confirmed native context resumes after recovery");
        recovery.NewRound();
        Check(recovery.Decide(emptyEntry, true, "None", true, false) == EntryAction.Reopen, "another completed round has its own bounded recovery");
        recovery = new EntryRecovery();
        Check(recovery.Decide(emptyEntry, true, "None", true, false) == EntryAction.Missing, "a new control run never borrows an old activity identity");
        var diagnostic = MansionClient.DiagnosticState(System.Text.Json.Nodes.JsonNode.Parse("""
            {"At":"2026-10-11T00:00:00Z","Reason":"entry-missing","ManagerPhase":"None","Entry":{"ScheduleId":0},"SavedEntry":{"ScheduleId":41001},"EntryRecoveries":1,"Board":{"Tiles":[1,2,3]},"UIs":["private text"]}
            """)!.AsObject());
        Check(diagnostic["Entry"]?["ScheduleId"]?.GetValue<int>() == 0 && diagnostic["SavedEntry"]?["ScheduleId"]?.GetValue<int>() == 41001 && diagnostic["ManagerPhase"]?.ToString() == "None", "exportable diagnostic retains cleared and original activity context");
        Check(!diagnostic.ContainsKey("Board") && !diagnostic.ContainsKey("UIs") && DailyDiagnosticExport.Allowed("tools/mansion-runaway-runtime-error.json"), "bounded restart diagnostic excludes board frames and is included in diagnostic exports");
        Check(PickupPolicy.Benefit("SpeedUp", 1, .4f, 0, 50, 5, 25) > 0, "adjacent speed pickup pays for a round trip");
        Check(PickupPolicy.Benefit("SpeedUp", 2, .4f, 0, 50, 5, 25) == 0, "far speed pickup is not worth its travel");
        Check(PickupPolicy.Benefit("SpeedUp", 1, .4f, 0, 5, 5, 25) == 0, "finish the last candy instead of collecting another item");
        Check(PickupPolicy.Benefit("Magnet", 2, .4f, 14, 50, 0, 0) > 0, "nearby dense magnet pickup is useful");
        Check(PickupPolicy.Benefit("Magnet", 2, .4f, 5, 50, 0, 0) == 0, "sparse magnet pickup is skipped");
        Check(PickupPolicy.Benefit("NightVision", 1, .4f, 14, 50, 5, 0) == 0, "visibility-only item does not divert the route");
        var normalOptions = PlannerOptions.ForGoal(0);
        var chainOptions = PlannerOptions.ForGoal(80, new PlannerOptions(1, 180, .4f, true));
        Check(chainOptions.ChainTarget == 80 && chainOptions.CollisionHorizon < normalOptions.CollisionHorizon && chainOptions.WaitSeconds == 0 && !chainOptions.CollectNearbyItems, "eighty-chain goal has its own objective and shorter hard collision horizon without item detours");
        Check(normalOptions.CollectNearbyItems && normalOptions.CollisionHorizon == 3 && normalOptions.GuideWeight == 110 && normalOptions.WaitSeconds == 0, "ordinary mode keeps the current balanced planner");
        Check(PlannerOptions.ForGoal(0).CollectNearbyItems && normalOptions.CollectNearbyItems, "leaving goal mode restores pickups without contaminating the normal profile");
        var researchOptions = new PlannerOptions(2, 180, .2f, false);
        Check(ReferenceEquals(PlannerOptions.ForGoal(0, researchOptions), researchOptions), "ordinary research overrides remain available");
        Check(ReferenceEquals(PlannerOptions.ForGoal(79, researchOptions), researchOptions), "an unsupported goal cannot activate the chain planner");
        var chainProgress = new ChainProgress(79, .3f);
        chainProgress.Advance(.2f, true, .9f);
        Check(chainProgress.Current == 80 && Math.Abs(chainProgress.Remaining - .9f) < .001, "pickup before the live deadline reaches eighty and resets to the native window");
        chainProgress.Advance(1, true, .9f);
        Check(chainProgress.Current == 1 && chainProgress.Peak == 80, "an empty gap breaks the live chain without erasing its real peak");
        var narrowWindow = new ChainProgress(50, .3f);
        narrowWindow.Advance(.4f, true, .9f);
        Check(narrowWindow.Current == 1, "the first pickup uses remaining time rather than a fresh full timer");
        var heading = Heading.At(350);
        heading.Aim(10, 5f / 60);
        float previous = 350;
        for (int frame = 1; frame <= 5; frame++)
        {
            heading.Advance(1f / 60);
            Check(heading.Value >= previous && heading.Value <= 370.001, "wrapped turn moves forward frame " + frame);
            if (frame < 5)
                Check(heading.Value < 370, "turn remains visible before final frame " + frame);
            previous = heading.Value;
        }

        Check(Math.Abs(heading.Value - 370) < .001, "turn completes in five frames");
        Check(Math.Abs(Motion.TurnDuration(1f / 60) - 5f / 60) < .001, "turn budget follows frame duration");
        var line = new MazeGraph([0, 2, 4, 6], [0, 0, 0, 0], [[1], [0, 2], [1, 3], [2]]);
        line.MovingEdge(1.4f, 0, 4, 0, out int from, out int toward);
        Check(from == 0 && toward == 1, "enemy before a junction must reach it before turning");
        line.MovingEdge(2.6f, 0, 4, 0, out from, out toward);
        Check(from == 1 && toward == 2, "enemy after a junction continues on its outgoing edge");
        var plan = new Planner(line).Choose(2, 0, 5, [], [false, false, false, false ], 3, 1, false);
        Check(plan.Next == 2 && !plan.Unsafe, "exit in a dead end is terminal, no fictitious return required");
        plan = new Planner(line).Choose(5.3f, 0, 5, [], [false, false, false, false ], 3, 2, false);
        Check(plan.Next == 3, "enter exit center when already its nearest node");
        var loop = new MazeGraph([0, 2, 4, 4, 2, 0], [0, 0, 0, 2, 2, 2], [[1, 5], [0, 2], [1, 3], [2, 4], [3, 5], [4, 0]]);
        var goalFork = new MazeGraph([0, 1, -2], [0, 0, 0], [[1, 2], [0], [0]]);
        plan = new Planner(goalFork, chainOptions).Choose(0, 0, 5, [], [false, true, true], 2, 1, false,
            currentChain: 79, chainRemaining: .3f, chainWindow: .9f);
        Check(plan.Next == 1 && !plan.Unsafe, "live deadline and eighty-chain target beat an open exit and clearing both branches");
        plan = new Planner(loop, chainOptions).Choose(0, 0, 5,
            [new Threat { X = 2f, Z = 0, Node = 1, Next = 0, Destination = 0, Speed = 4, Radius = 1.05f, State = 1, StateRemaining = 10 }],
            [false, true, true, true, true, true], -1, 1, false, currentChain: 79, chainRemaining: .9f, chainWindow: .9f);
        Check(plan.Next == 5 && !plan.Unsafe, "aggressive goal still rejects an immediate head-on catch");
        var chainEdges = Enumerable.Range(0, 92).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < 90; i++) { chainEdges[i].Add(i + 1); chainEdges[i + 1].Add(i); }
        chainEdges[0].Insert(0, 91); chainEdges[91].Add(0);
        var chainMaze = new MazeGraph(Enumerable.Range(0, 92).Select(i => i == 91 ? -2f : i * 2f).ToArray(), new float[92], chainEdges.Select(e => e.ToArray()).ToArray());
        var chainCandy = Enumerable.Range(0, 92).Select(i => i != 0).ToArray();
        var goalGuide = new RouteGuide(chainMaze);
        var goalRoute = goalGuide.Search(0, chainCandy, 5, true, 80, 70, .5f, .5f);
        Check(goalRoute.Length == 11 && goalRoute[1] == 1 && !goalRoute.Contains(91), "chain guide skips an isolated candy and stops its route at eighty instead of clearing the maze");
        Check(goalGuide.LastSearch?.ProjectedChain == 80 && goalGuide.LastSearch.TargetChain == 80, "route evidence identifies the goal and projected chain");
        Check(goalRoute.Zip(goalRoute.Skip(1)).All(p => chainMaze.Edges[p.First].Contains(p.Second)), "chain-first route retains real corridor connectivity");
        plan = new Planner(loop).Choose(0, 0, 5, [new Threat { X = 1.1f, Z = 0, Node = 1, Next = 0, Destination = 0, Speed = 4, Radius = 1.05f, State = 1, StateRemaining = 10 }], [false, true, true, true, true, true ], -1, 1, false);
        Check(plan.Next == 5, "choose escape branch instead of a head-on collision");
        plan = new Planner(loop).Choose(4, 2, 5, [new Threat { X = 4, Z = .2f, Node = 2, Next = 1, Destination = 3, Speed = 4, Radius = 1.05f, State = 0, StateRemaining = 5, NativeX = [4, 4], NativeZ = [.2f, 2], NativeCursor = 1, NativeSeconds = 1 }], [true, true, true, false, true, true ], -1, 1, false);
        Check(plan.Next == 4, "native corner path takes priority over stale enemy velocity at a turn");
        var crossing = new MazeGraph([-2, 0, 2, 0, 0], [0, 0, 0, -2, 2], [[1], [0, 2, 3, 4], [1], [1], [1]]);
        var crossingThreat = new Threat
        {
            X = 0,
            Z = -.9f,
            Node = 3,
            Next = 1,
            Destination = 4,
            Speed = 4,
            Radius = 1.05f,
            State = 0,
            StateRemaining = 5,
            NativeX = [0, 0],
            NativeZ = [-.9f, 2],
            NativeCursor = 1,
            NativeSeconds = 5
        };
        plan = new Planner(crossing, new PlannerOptions(3, 110, .4f)).Choose(-2, 0, 5, [crossingThreat], [false, false, false, false, false ], 2, 1, false, 90);
        Check(plan.Wait > 0 && plan.Wait <= .4f && !plan.Unsafe, "yield briefly to let a crossing enemy pass instead of a forced collision");
        plan = new Planner(line, new PlannerOptions(3, 110, .4f)).Choose(0, 0, 5, [], [false, false, false, false ], 3, 1, false);
        Check(plan.Wait == 0, "no waiting cost is added to an unobstructed exit route");
        var route = new RouteGuide(loop).Search(0, [false, true, true, true, true, true ], 5, true);
        Check(route.Distinct().Count() == 6, "whole-maze guide covers a loop without splitting untouched candy");
        Check(route.Zip(route.Skip(1)).All(p => loop.Edges[p.First].Contains(p.Second)), "guide uses connected native corridors only");
        Check(new Planner(line).PredictTarget(1, 90) == 3, "predict pursuer projects a straight corridor");
        Check(new Planner(loop).PredictTarget(2, 90) == 2, "predict pursuer does not project around a corner");
        var branch = new MazeGraph([0, 2, 4, 2, 2], [0, 0, 0, 2, -2], [[1], [0, 2, 3, 4], [1], [1], [1]]);
        Check(new Planner(branch).PredictTarget(1, 90) == 1, "predict pursuer falls back at a junction");
        route = new RouteGuide(branch).Search(0, [false, true, true, true, true ], 5, false);
        Check(route.Distinct().Count() == 5, "complete route returns from dead ends to collect other branches");
        Check(route.Zip(route.Skip(1)).All(p => branch.Edges[p.First].Contains(p.Second)), "2-opt expands through real connected corridors");
        var rng = new Random(54321);
        for (int trial = 0; trial < 12; trial++)
        {
            const int size = 8;
            var adjacency = Enumerable.Range(0, size * size).Select(_ => new List<int>()).ToArray();
            void Link(int a, int b)
            {
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int n = y * size + x;
                    if (x > 0)
                        Link(n, n - 1);
                    if (y > 0 && (x == 0 || rng.Next(3) == 0))
                        Link(n, n - size);
                }

            var maze = new MazeGraph(Enumerable.Range(0, size * size).Select(n => (float)(n % size * 2)).ToArray(), Enumerable.Range(0, size * size).Select(n => (float)(n / size * 2)).ToArray(), adjacency.Select(v => v.ToArray()).ToArray());
            var candy = Enumerable.Range(0, size * size).Select(_ => rng.Next(3) > 0).ToArray();
            route = new RouteGuide(maze).Search(0, candy, 5, true);
            Check(Enumerable.Range(0, candy.Length).All(n => !candy[n] || route.Contains(n)), "required pickups survive tour reordering " + trial);
            Check(route.Zip(route.Skip(1)).All(p => maze.Edges[p.First].Contains(p.Second)), "generated route stays connected " + trial);
        }

        var clearedDetour = new RouteGuide(branch);
        // This is a valid earlier coverage route whose dead-end coin has since
        // been collected. Remaining candy on another branch must not require
        // visiting the now empty dead end again.
        typeof(RouteGuide).GetField("route", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(clearedDetour, new[] { 0, 1, 2, 1, 3, 1, 4 });
        Check(clearedDetour.Next(1, [false, false, false, true, true ], 5, 1, false) == 3, "an already collected dead end is skipped while remaining branches stay planned");
        var changing = new RouteGuide(loop);
        changing.Next(0, [false, true, true, false, false, false ], 5, 1, false);
        changing.Invalidate();
        int refreshed = -1;
        var refreshWatch = System.Diagnostics.Stopwatch.StartNew();
        while (refreshed < 0 && refreshWatch.ElapsedMilliseconds < 2000)
        {
            refreshed = changing.Next(0, [false, false, false, false, false, true ], 5, 2 + (float)refreshWatch.Elapsed.TotalSeconds, false);
            Thread.Sleep(1);
        }

        Check(refreshed == 5, "magnet invalidates the old route even while its search is still running");
        var disconnected = new MazeGraph([0, 2], [0, 0], [[], []]);
        plan = new Planner(disconnected).Choose(0, 0, 5, [], [false, true ], -1, 1, false);
        Check(plan.Next == -1, "unreachable target does not invent an edge");
        var tool = DailyToolCatalog.Find("mansion-runaway");
        Check(tool.AssemblyName == "Dustweave", "new tool is hosted by the product");
        var old = Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL");
        try
        {
            Environment.SetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL", null);
            bool rejected = false;
            try
            {
                _ = new MansionClient(Path.GetTempPath(), 42, 10, "fixture");
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "client cannot open a standalone unowned game connection");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL", old);
        }
    }
}

using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;

// Exercise production routing, native commands and observations, not just graph costs.
static class FieldRouteArrivalCases
{
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly DailyFieldRoute Route;
        public long MapId = 9, Destination = 1;
        public long? DisplayedWaypointMap;
        public bool Near, PortNear, AlternativePort, AlternativePortNear, BlockExit, FailCommand;
        public bool QuickEnabled, QuickOutsideOnly, QuickNear, QuickLandNear = true, QuickLandNearby = true, QuickFails;
        public long QuickNpc = 999, QuickPack = 1;
        private string quickState = "idle";
        public long? BattleMap;
        public double StealthSeconds;
        public double GateDistance = 2; public double? FirstCommandAt;
        public int GateInstance = 91;
        public Func<JsonObject[], JsonObject[]>? ChangeMaps;
        public Fixture(string root)
        {
            H = new(root, [new DailyBusinessProof("dispatch.start", "collection", [], (_, _, _) => new())]);
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "flows"));
            foreach (string name in new[] { "collection-catalog.json", "steal-catalog.json", "route-atlas.json" })
                File.Copy(Path.Combine(TestPaths.SourceRoot, "assets", "flows", name), Path.Combine(assets, "flows", name), true);
            var w = new DailyWorkflow(root, assets, H.Context, H.Driver, H.Business, H.Workflow.Navigation, [], () => H.Stopped, H.Workflow.Relay);
            // Route fixtures opt out of talents unless a case explicitly tests them.
            w.Settings.General.FastTravel = false;
            Route = new(w);
            Scene();
            H.Readings = Readings;
            H.OnCommand = c =>
            {
                FirstCommandAt ??= H.Time;
                if (FailCommand) throw new DailyStepException("uncertain", "submitted navigation lost its reply");
                switch (S(c["Kind"]))
                {
                    case "mainline_walk":
                        if (N(c["Value"]) == 91) MapId = 1;
                        else if (N(c["Value"]) == 12) MapId = 2;
                        else if (N(c["Value"]) == 10) PortNear = true;
                        else if (N(c["Value"]) == 20 && AlternativePort) AlternativePortNear = true;
                        else throw new Exception("Unexpected navigation target");
                        Scene();
                        break;
                    case "mainline_cancel_nav": break;
                    case "mainline_interact": H.Page("WayPointUI", "_objMapNextButton", "_objMapPrevButton"); break;
                    case "back": Scene(); break;
                    case "field_quick_travel":
                        if (N(c["Value"]) != 999 || N(c["Items"]?[0]) != Destination) throw new Exception("Shortcut targets another NPC");
                        if (QuickFails) { quickState = "failed"; break; }
                        MapId = Destination; Near = QuickLandNear; QuickNear = QuickLandNearby; quickState = "arrived";
                        Scene();
                        break;
                    case "weekly_npc_board_nav":
                        if (MapId != Destination) throw new Exception("Board navigation on wrong map");
                        Near = true;
                        break;
                    case "weekly_npc_board_stop": break;
                    case "weekly_npc_board_open": H.Page("QuestBoardUI"); break;
                    default: throw new Exception("Unexpected field-route command " + S(c["Kind"]));
                }
            };
        }
        public void Scene()
        {
            H.Frame["Scene"] = $"Map0001_{MapId:000}";
            H.Page("GameFieldDefaultUI");
        }
        public JsonObject Native() => O(("Boards", new[] { new { Map = Destination, Npc = 999, Instance = MapId == Destination ? 71 : 0, Near } }), ("Navigating", false));
        public string[] Steps => H.Box.Commands.Select(c => S(c["Kind"])).ToArray();
        JsonObject Read(string id, int instance, params (string Key, object? Value)[] values) =>
            O(("Id", id), ("InstanceId", instance), ("Error", ""), ("Values", Array(values.Select(v => O(("Path", v.Key), ("Error", ""), ("Json", O(("v", v.Value))["v"]?.ToJsonString() ?? "null"))))));
        JsonObject[] Readings()
        {
            var maps = new[] {
                O(("Id",9),("Exits",new[]{1}),("HasWaypoint",false),("CanSummon",false)),
                O(("Id",1),("Exits",new[]{9,2}),("HasWaypoint",true),("CanSummon",false)),
                O(("Id",2),("Exits",new[]{1}),("HasWaypoint",true),("CanSummon",false))
            };
            foreach (var map in maps) map["MapType"] = N(map["Id"]) == BattleMap ? 1 : 0;
            maps = ChangeMaps?.Invoke(maps) ?? maps;
            var approaches = new List<JsonObject>();
            var rows = new List<JsonObject> {
                Read("mainline.map",0,(MapPath,O(("id",MapId),("packId",1),("mapType", MapId == BattleMap ? 1 : 0))),
                    ("HasCanOverwhelmMonster()",MapId == BattleMap),
                    ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.x",0), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.y",0), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.z",0)),
                Read("weekly_npc.native",0,("$self",Native())),
                Read("navigation.pack",0,(DailyTravel.CurrentPack+".Id",1),(DailyTravel.CurrentPack+".PackType",0))
            };
            rows.Add(Read("mainline.durations",0,("effects",new JsonArray(O(("Key",17),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",StealthSeconds>0),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",StealthSeconds))))));
            rows.Add(Read("route.quick",0,("$self",O(("Pack",QuickPack),("Map",MapId),("Npc",QuickNpc),("State",quickState),
                ("Targets", QuickEnabled && (!QuickOutsideOnly || MapId != 9)
                    ? new JsonArray(O(("Npc",QuickNpc),("Map",Destination),("Name","Board"),("AtTarget",QuickNear))) : new JsonArray())))));
            if (MapId is 9 or 1)
            {
                int instance = MapId == 9 ? GateInstance : 12;
                long dest = MapId == 9 ? 1 : 2;
                rows.Add(Read("mainline.gate",instance,("gameObject.name","Gate_test"),(ObjectPath,MapId == 9 ? 91 : 12),(DestinationPath,dest)));
                approaches.Add(O(("Instance",instance),("Reachable",!BlockExit),("Distance",MapId==9?2:GateDistance),("Kind","gate"),("Destination",dest)));
            }
            if (MapId == 1)
            {
                rows.Add(Read("mainline.waypoint",10,(ObjectPath,1),(WaypointNear,PortNear),("ὡὩὫὦὧὢὡὬὡὥὥ","WayPoint_Enable")));
                approaches.Add(O(("Instance",10),("Reachable",true),("Distance",PortNear?0:5),("Kind","waypoint")));
                if (AlternativePort)
                {
                    rows.Add(Read("mainline.waypoint",20,(ObjectPath,2),(WaypointNear,AlternativePortNear),("ὡὩὫὦὧὢὡὬὡὥὥ","WayPoint_Enable")));
                    approaches.Add(O(("Instance",20),("Reachable",true),("Distance",AlternativePortNear?0:10),("Kind","waypoint")));
                }
            }
            if (S(H.Frame["UiToken"]) == "WayPointUI")
                rows.Add(Read("mainline.waypoint_ui",20,("_fieldMiniMap.CurrentMap",O(("id",DisplayedWaypointMap ?? Destination)))));
            rows.Add(Read("mainline.travel",0,("$self",O(("Pack",1),("Map",MapId),("Maps",Array(maps)),("Approaches",Array(approaches)),("Ground",true),
                ("QuickTargets", QuickEnabled ? new JsonArray(O(("Npc",QuickNpc),("Map",Destination),("AtTarget",MapId == Destination && QuickNear))) : new JsonArray())))));
            return rows.ToArray();
        }
        public Task Board() => DailyWeeklyNpcBoard.Open(Route, new(Native(), [], []), 1);
        public void Dispose() => H.Dispose();
    }
    private static async Task Recovery(string root, List<string> checks)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("Route recovery: " + message); }
        Check(typeof(DailyTravelBlocked).GetConstructor([typeof(string)]) != null
            && typeof(DailyFieldRoute).GetMethod("Teleport", [typeof(long), typeof(long?), typeof(bool), typeof(bool), typeof(bool)]) != null,
            "existing binary signatures remain available to independently built plugins");
        void Reject(Fixture f, JsonObject command) => f.H.Box.Values["live:receipts~" + S(command["Id"]) + ".json"] =
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(O(("Command", command), ("State", "rejected"),
                ("Error", "travel_unreachable:path_incomplete"), ("MayHaveDispatched", false)));
        foreach (long arrival in new long[] { 1, 2 })
        using (var f = new Fixture(Path.Combine(root, "unexpected-arrival-" + arrival)))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            f.H.OnCommand = _ => { f.MapId = arrival; f.Scene(); };
            bool result = await nav.Step(Rows(plan["actions"])[0]);
            Check(result == (arrival == 1) && N(Map(nav.Evidence)["id"]) == arrival
                && nav.State == DailyCollectionNavigator.NavigationState.ReadyForWork && f.Steps.Length == 1,
                "arrival on map " + arrival + " returns to job selection without a second movement or talent");
            Check(nav.Atlas.Blocked.Count == 0 && nav.Atlas.WarpBlocked.Count == 0
                && Rows((await nav.Plan([arrival]))["actions"]).Length == 0,
                "a changed arrival neither bans the old map nor continues from stale coordinates");
        }
        foreach (long arrival in new long[] { 9, 2 })
        using (var f = new Fixture(Path.Combine(root, "standalone-teleport-preparation-" + arrival)) { MapId = 1 })
        {
            f.Scene(); f.H.OnCommand = command =>
            {
                Check(S(command["Kind"]) == "mainline_walk" && N(command["Value"]) == 10,
                    "standalone preparation never sends an old-map gate or talent after relocation");
                f.MapId = arrival; f.Scene();
            };
            bool blocked = false; JsonObject? result = null;
            try { result = await f.Route.PrepareTeleport(false, destination: 9); } catch (DailyTravelBlocked) { blocked = true; }
            Check(f.Steps.Length == 1 && (arrival == 9 ? !blocked && result == null : blocked),
                "non-planner teleport callers also refresh all movement decisions after an unexpected map change: " + arrival);
        }
        using (var f = new Fixture(Path.Combine(root, "rejected-door-alternate-shortcut")) { QuickEnabled = true })
        {
            var original = f.H.OnCommand;
            f.H.OnCommand = c => { if (S(c["Kind"]) == "mainline_walk") Reject(f, c); else original!(c); };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var first = Rows((await nav.Plan([1]))["actions"])[0];
            Check(S(first["kind"]) == "walk" && !await nav.Step(first), "known rejected door is observed again before replanning");
            var next = Rows((await nav.Plan([1]))["actions"])[0];
            Check(S(next["kind"]) == "quick" && await nav.Step(next) && f.MapId == 1
                && f.Steps.SequenceEqual(new[] { "mainline_walk", "field_quick_travel" }),
                "a failed door preserves the native shortcut to the same destination");
        }
        using (var f = new Fixture(Path.Combine(root, "rejected-portal-alternate-door")) { MapId = 1, Destination = 2, GateDistance = 200 })
        {
            f.Scene(); var original = f.H.OnCommand;
            f.H.OnCommand = c => { if (S(c["Kind"]) == "mainline_walk" && N(c["Value"]) == 10) Reject(f, c); else original!(c); };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var first = Rows((await nav.Plan([2]))["actions"])[0];
            Check(S(first["mode"]) == "native" && !await nav.Step(first)
                && nav.Atlas.BlockedNativePorts.Count == 1 && nav.Atlas.WarpBlocked.Count == 0,
                "an unsuccessful physical-portal approach rejects the approach, not its destination");
            var next = Rows((await nav.Plan([2]))["actions"])[0];
            Check(S(next["kind"]) == "walk" && await nav.Step(next) && f.MapId == 2,
                "the map remains reachable through its door after the portal approach fails");
        }
        using (var f = new Fixture(Path.Combine(root, "arrival-topology-changed")))
        {
            f.ChangeMaps = maps => { if (f.MapId == 1) maps.Single(m => N(m["Id"]) == 9)["Exits"] = new JsonArray(); return maps; };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            Check(await nav.Step(Rows((await nav.Plan([1], teleports: false))["actions"])[0]) && f.MapId == 1,
                "invalidating origin geometry during arrival cannot turn successful movement into a cache exception");
        }
        foreach (string failure in new[] { "page-cycle", "page-timeout", "locked-destination" })
        using (var f = new Fixture(Path.Combine(root, "waypoint-" + failure)) { MapId = 1, Destination = 2, GateDistance = 200 })
        {
            f.ChangeMaps = maps => { maps.Single(m => N(m["Id"]) == 9)["HasWaypoint"] = true; return maps; };
            f.DisplayedWaypointMap = failure == "locked-destination" ? 2 : 1;
            f.Scene(); var original = f.H.OnCommand;
            f.H.OnCommand = command =>
            {
                if (S(command["Kind"]) == "click")
                { if (failure == "page-cycle") f.DisplayedWaypointMap = f.DisplayedWaypointMap == 1 ? 3 : 1; return; }
                original!(command);
            };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            Check(!await nav.Step(Rows((await nav.Plan([2]))["actions"])[0])
                && nav.Atlas.WarpBlocked.Contains(2) == (failure == "locked-destination")
                && nav.Atlas.BlockedNativePorts.Count == 0 && S(f.H.Frame["UiToken"]) == "GameFieldDefaultUI",
                failure + " closes only the owned picker and does not label unrelated portal destinations unavailable");
            Check(S(Rows((await nav.Plan([9]))["actions"])[0]["mode"]) == "native",
                failure + " preserves native portal access to another map");
            var walk = Rows((await nav.Plan([2], teleports: false))["actions"])[0];
            Check(S(walk["kind"]) == "walk" && await nav.Step(walk) && f.MapId == 2,
                failure + " leaves an ordinary walking route to the same map usable");
        }
        using (var f = new Fixture(Path.Combine(root, "strict-summon-does-not-retry-native")) { MapId = 1, Destination = 2, GateDistance = 200 })
        {
            f.ChangeMaps = maps => { maps.Single(m => N(m["Id"]) == 1)["CanSummon"] = true; return maps; };
            f.Scene(); bool reachedSummon = false; var original = f.H.OnCommand;
            f.H.OnCommand = c =>
            {
                if (S(c["Kind"]) == "mainline_walk" && N(c["Value"]) == 10) { Reject(f, c); return; }
                if (S(c["Kind"]) == "mainline_menu" && N(c["Value"]) == 15)
                { reachedSummon = true; throw new InvalidOperationException("test: reached native waypoint talent"); }
                original!(c);
            };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            await nav.Step(Rows((await nav.Plan([2]))["actions"])[0]);
            var summon = Rows((await nav.Plan([2]))["actions"])[0];
            Check(S(summon["mode"]) == "talent" && B(summon["summon_only"]), "failed physical approach leaves the independent summoning option");
            try { await nav.Step(summon); }
            catch (DailyStepException error) when (reachedSummon && error.Message.Contains("test: reached native waypoint talent", StringComparison.Ordinal)) { }
            Check(reachedSummon && f.Steps.Count(s => s == "mainline_walk") == 1 && !f.Steps.Contains("mainline_interact"),
                "planned summoning invokes the talent without silently walking back to the failed native portal");
        }
        using (var f = new Fixture(Path.Combine(root, "alternate-physical-portal")) { MapId = 1, Destination = 2, GateDistance = 200, AlternativePort = true })
        {
            f.Scene(); var original = f.H.OnCommand;
            f.H.OnCommand = c => { if (S(c["Kind"]) == "mainline_walk" && N(c["Value"]) == 10) Reject(f, c); else original!(c); };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            await nav.Step(Rows((await nav.Plan([2]))["actions"])[0]);
            Check(f.H.Box.Commands.Where(c => S(c["Kind"]) == "mainline_walk").Select(c => N(c["Value"])).SequenceEqual(new long[] { 10, 20 })
                && f.Steps.Contains("mainline_interact") && nav.Atlas.BlockedNativePorts.Count == 0,
                "one blocked physical portal does not suppress another reachable portal on the same map");
        }
        using (var f = new Fixture(Path.Combine(root, "partial-route-keeps-work")))
        {
            f.ChangeMaps = maps => { foreach (var m in maps.Where(m => N(m["Id"]) != 9)) { m["Exits"] = new JsonArray(); m["HasWaypoint"] = false; } return maps; };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1, 2]);
            Check(B(plan["partial_route"]) && plan["pending"]!.AsArray().Select(N).SequenceEqual(new long[] { 1, 2 })
                && await nav.Step(Rows(plan["actions"])[0]) && f.MapId == 1,
                "missing route evidence for one goal does not discard another reachable goal or erase outstanding work");
        }
        using (var f = new Fixture(Path.Combine(root, "safe-hub-fresh-doors")) { BlockExit = true, QuickEnabled = true })
        {
            f.ChangeMaps = maps =>
            {
                foreach (var m in maps) { m["HasWaypoint"] = false; m["Exits"] = new JsonArray(); }
                if (f.MapId == 1) { maps.Single(m => N(m["Id"]) == 1)["Exits"] = new JsonArray(2); f.BlockExit = false; }
                return maps;
            };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var probe = await nav.Plan([2]);
            Check(B(probe["recovery_probe"]) && N(probe["pending"]![0]) == 2 && await nav.Step(Rows(probe["actions"])[0]),
                "a native safe hub is visited to refresh missing routes without marking the original goal complete");
            var route = await nav.Plan([2]);
            Check(!B(route["recovery_probe"]) && S(Rows(route["actions"])[0]["kind"]) == "walk"
                && await nav.Step(Rows(route["actions"])[0]) && f.MapId == 2,
                "fresh safe-hub doors replace incomplete map observations and finish the journey");
        }
        using (var f = new Fixture(Path.Combine(root, "safe-hub-bounded")) { BlockExit = true, QuickEnabled = true, QuickFails = true })
        {
            f.ChangeMaps = maps => { foreach (var m in maps) { m["Exits"] = new JsonArray(); m["HasWaypoint"] = false; } return maps; };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            Check(!await nav.Step(Rows((await nav.Plan([2]))["actions"])[0]), "failed safe-hub discovery returns to planning");
            bool stopped = false;
            try { await nav.Plan([2]); } catch (DailyTravelBlocked) { stopped = true; }
            Check(stopped && f.Steps.Length == 1 && nav.Atlas.WarpBlocked.Count == 0
                && nav.State == DailyCollectionNavigator.NavigationState.Paused,
                "a failed discovery does not loop, ban destination maps, or invent collection completion");
        }
        foreach (string interruption in new[] { "loading-arrival", "identity", "unknown-popup", "stop" })
        using (var f = new Fixture(Path.Combine(root, "rejected-door-" + interruption)))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var first = Rows((await nav.Plan([1], teleports: false))["actions"])[0];
            f.H.OnCommand = c =>
            {
                Reject(f, c); f.H.Page(interruption == "unknown-popup" ? "UnknownPaymentPopupUI" : "LoadingUI");
                if (interruption == "stop") f.H.Stopped = true;
            };
            f.H.OnDelay = () =>
            {
                if (f.H.Time < .8 || interruption is "unknown-popup" or "stop") return;
                if (interruption == "identity") f.H.Frame["AccountKey"] = "different-account";
                f.MapId = 1; f.Scene();
            };
            bool paused = false, arrived = false;
            try { arrived = await nav.Step(first); }
            catch (Exception error) when (error is InvalidDataException or StageHostException or DailyStepException) { paused = true; }
            Check(f.Steps.Length == 1 && nav.Atlas.Blocked.Count == 0 && nav.Atlas.WarpBlocked.Count == 0,
                "recovery does not submit another command or mutate route failures during " + interruption);
            Check(interruption == "loading-arrival" ? arrived && !paused && f.H.Time >= .8
                && nav.State == DailyCollectionNavigator.NavigationState.ReadyForWork
                : paused && nav.State == DailyCollectionNavigator.NavigationState.Paused,
                "recovery honors live loading, identity, popup and stop boundaries: " + interruption);
        }
        using (var f = new Fixture(Path.Combine(root, "unknown-movement-receipt")) { FailCommand = true })
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence()); bool paused = false;
            try { await nav.Step(Rows((await nav.Plan([1]))["actions"])[0]); } catch (DailyStepException) { paused = true; }
            Check(paused && f.Steps.Length == 1 && nav.Atlas.Blocked.Count == 0
                && nav.State == DailyCollectionNavigator.NavigationState.Paused,
                "an unanswered command cannot become a local route failure or a retry");
        }
        using (var f = new Fixture(Path.Combine(root, "successful-cycle-alternate")) { QuickEnabled = true })
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            for (int i = 0; i < 3; i++) { f.MapId = 9; f.Scene(); await nav.Step(Rows((await nav.Plan([1]))["actions"])[0]); }
            f.MapId = 9; f.Scene();
            Check(!await nav.Step(Rows((await nav.Plan([1]))["actions"])[0]) && f.Steps.Length == 3,
                "a fourth unchanged departure is reconsidered before input");
            var alternative = Rows((await nav.Plan([1]))["actions"])[0];
            Check(S(alternative["kind"]) == "quick" && await nav.Step(alternative) && f.MapId == 1,
                "loop rejection leaves alternative entry methods to the same map available");
        }
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks.Add(message); }
        await Recovery(Path.Combine(root, "recovery"), checks);
        using (var f = new Fixture(Path.Combine(root, "ready-walk-no-fixed-settle")))
        {
            await f.Route.Walk(91, 1);
            Check(f.MapId == 1 && f.FirstCommandAt < .2 && f.H.Time < .5,
                "ready field starts walking without a fixed settle before departure or after arrival");
        }
        using (var f = new Fixture(Path.Combine(root, "loading-walk-native-readiness")))
        {
            f.H.Frame["Surfaces"]![0]!["InputReady"] = false;
            f.H.OnDelay = () => { if (f.H.Time >= .8) f.Scene(); };
            await f.Route.Walk(91, 1);
            Check(f.MapId == 1 && f.FirstCommandAt >= .8 && f.FirstCommandAt < 1.1,
                "walking still waits for actual input readiness, then departs without an extra second");
        }
        using (var f = new Fixture(Path.Combine(root, "field-settle-adjacent")))
        {
            await f.Route.FieldReady();
            double settled = f.H.Time;
            Check(settled >= 1, "first field readiness check retains its full settle");
            int observed = 0;
            f.H.OnRead = () => observed++;
            await f.Route.FieldReady();
            await f.Route.FieldReady(settle: 0);
            await f.Route.FieldReady();
            Check(f.H.Time == settled && observed >= 3,
                "adjacent readiness calls inspect live frames without repeating a completed settle");
            f.H.Time += 1.5;
            await f.Route.FieldReady();
            f.H.Time += .6;
            double expired = f.H.Time;
            await f.Route.FieldReady();
            Check(f.H.Time - expired >= 1,
                "reuse never extends the original readiness proof beyond its two-second lifetime");
            double longer = f.H.Time;
            await f.Route.FieldReady(settle: 2);
            Check(f.H.Time - longer >= 2, "a short completed settle cannot replace a longer requested settle");
        }
        foreach (string change in new[] { "command", "scene", "surface", "not-ready", "popup", "timeout" })
        using (var f = new Fixture(Path.Combine(root, "field-settle-invalidated-" + change)))
        {
            await f.Route.FieldReady();
            switch (change)
            {
                case "command":
                    await f.H.Workflow.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
                    break;
                case "scene": f.MapId = 1; f.Scene(); break;
                case "surface": f.H.Frame["UiToken"] = "changed-field-surface"; break;
                case "not-ready":
                    f.H.Frame["Surfaces"]![0]!["InputReady"] = false;
                    f.H.OnDelay = () => { f.H.OnDelay = null; f.Scene(); };
                    break;
                case "popup": f.H.Page("QuickMenuUI"); break;
                case "timeout":
                    f.H.Page("UnrelatedPopupUI");
                    bool failed = false;
                    try { await f.Route.FieldReady(timeout: .2); }
                    catch (StageHostException) { failed = true; }
                    Check(failed && f.H.Box.Commands.Count == 0,
                        "unknown popup still stops without confirmation despite a recent field settle");
                    f.Scene();
                    break;
            }
            double start = f.H.Time;
            await f.Route.FieldReady();
            Check(f.H.Time - start >= 1 - .000001,
                "field readiness repeats its full settle after " + change);
        }
        var attempts = new DailyRouteAttempts();
        for (int i = 0; i < 3; i++)
        {
            Check(attempts.TryStep("81,82,83", "[81,1]", "teleport", 82, null), "bounded first-to-second departure " + i);
            Check(attempts.TryStep("81,82,83", "[82,1]", "teleport", 81, null), "bounded second-to-first departure " + i);
        }
        Check(!attempts.TryStep("81,82,83", "[81,1]", "teleport", 82, null), "unchanged round trips stop before a fourth identical departure");
        Check(attempts.TryStep("81,82,83", "[81,2]", "walk", 82, JsonValue.Create(7)), "returning to another region of the same map is not a cycle");
        attempts.Clear();
        Check(attempts.TryStep("81,82,83", "[81,1]", "teleport", 82, null), "verified work progress allows legitimate revisits");
        using (var f = new Fixture(Path.Combine(root, "weekly-risk-per-segment")) { BattleMap = 1 })
        {
            f.Route.ProtectWeeklyTravel = true;
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            Check(await nav.Step(Rows(plan["actions"])[0]), "safe segment reaches its battle-map work destination");
            Check(f.Steps.SequenceEqual(new[] { "mainline_walk" }) && !B(f.H.Box.Commands[0]["RequireStealth"]),
                "safe approach and arrival leave immediate suppression to the work stage, without premature stealth");
            f.StealthSeconds = 30;
            await f.Route.Walk(12, 2);
            Check(B(f.H.Box.Commands.Last(c => S(c["Kind"]) == "mainline_walk")["RequireStealth"]),
                "walking out through live enemies reuses active protection for that physical segment");
        }
        foreach (bool appearing in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "weekly-risk-changes-" + appearing)) { BattleMap = appearing ? null : 9, StealthSeconds = 30 })
        {
            f.Route.ProtectWeeklyTravel = true;
            int walks = 0;
            f.H.OnCommand = c =>
            {
                if (S(c["Kind"]) == "mainline_cancel_nav") return;
                Check(S(c["Kind"]) == "mainline_walk", "changed threat reuses live duration instead of casting again");
                walks++;
                if (walks == 1) f.BattleMap = appearing ? 9 : null;
                else { f.MapId = 1; f.Scene(); }
            };
            await f.Route.Walk(91, 1);
            var moves = f.H.Box.Commands.Where(c => S(c["Kind"]) == "mainline_walk").ToArray();
            Check(moves.Length == 2 && B(moves[0]["RequireStealth"]) == !appearing && B(moves[1]["RequireStealth"]) == appearing,
                "walking updates the native duration guard when threats appear or disappear: " + appearing);
        }
        foreach (bool sameMap in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "walk-hidden-field-" + sameMap)))
        {
            bool hidden = false, restored = false;
            f.H.OnCommand = c =>
            {
                Check(S(c["Kind"]) == "mainline_walk", "field transition does not emit stop or nudge commands");
                if (f.Steps.Length == 1) { f.H.Page("OverheadManageUI"); hidden = true; }
                else { Check(N(c["Value"]) == 191, "resumed walk resolves replacement gate instance"); f.MapId = 1; f.Scene(); }
            };
            f.H.OnDelay = () =>
            {
                if (hidden && !restored && f.H.Time >= 8)
                {
                    restored = true; f.GateInstance = 191;
                    if (!sameMap) f.MapId = 1;
                    f.Scene();
                }
            };
            await f.Route.Walk(91, 1);
            Check(restored && f.MapId == 1 && f.Steps.Length == (sameMap ? 2 : 1),
                "hidden controls beyond stall timeout recover by real map state: " + sameMap);
        }
        using (var f = new Fixture(Path.Combine(root, "walk-cancel-ui-race")))
        {
            bool changed = false; double changedAt = 0, departedAt = 0;
            f.H.OnCommand = c => { if (S(c["Kind"]) != "mainline_walk") throw new Exception("Unexpected movement during transition"); departedAt = f.H.Time; };
            f.H.Driver.SubmissionGuard = () =>
            {
                if (!changed && f.Steps.Length == 1 && f.H.Time >= departedAt + 4)
                { changed = true; changedAt = f.H.Time; f.H.Page("OverheadManageUI"); }
            };
            f.H.OnDelay = () => { if (changed && f.H.Time >= changedAt + .6) { f.MapId = 1; f.Scene(); } };
            await f.Route.Walk(91, 1);
            Check(changed && f.Steps.Length == 1 && f.MapId == 1 && f.H.Time < 12,
                "unsent cancellation racing HUD hide recovers without old 30-second destination wait");
        }
        using (var f = new Fixture(Path.Combine(root, "walk-persistent-hidden-field")))
        {
            f.H.OnCommand = _ => f.H.Page("OverheadManageUI");
            bool blocked = false;
            try { await f.Route.Walk(91, 1); }
            catch (StageHostException ex) { blocked = ex.Message.StartsWith("field.not-ready:", StringComparison.Ordinal); }
            Check(blocked && f.Steps.Length == 1 && f.H.Time < 50,
                "persistent field absence has bounded actionable failure without blind movement");
            var report = DailyJson.TryRead<JsonObject>(Directory.GetFiles(Path.Combine(f.H.Root, "live", "travel-diagnostics")).Single())!;
            Check(S(report["reason"]) == "field_transition_not_ready" && N(report["details"]!["destination"]) == 1
                && report["frame"] != null && report["latest_frame"] != null,
                "failed transition retains destination, old and current UI, gates and travel state");
        }
        using (var f = new Fixture(Path.Combine(root, "walk-repeated-interruption")))
        {
            double hiddenAt = 0;
            f.H.OnCommand = _ => { hiddenAt = f.H.Time; f.H.Page("OverheadManageUI"); };
            f.H.OnDelay = () => { if (f.H.Time >= hiddenAt + .6) f.Scene(); };
            bool blocked = false;
            try { await f.Route.Walk(91, 1); }
            catch (DailyTravelBlocked ex) { blocked = ex.Message.StartsWith("field.route-interrupted:", StringComparison.Ordinal); }
            Check(blocked && f.Steps.Length == 4, "repeated same-map interruptions return to route planner after bounded retries");
        }
        using (var f = new Fixture(Path.Combine(root, "walk-hidden-user-stop")))
        {
            f.H.OnCommand = _ => f.H.Page("OverheadManageUI");
            f.H.OnDelay = () => { if (f.Steps.Length > 0) f.H.Stopped = true; };
            bool stopped = false;
            try { await f.Route.Walk(91, 1); }
            catch (StageHostException ex) { stopped = ex.Kind == "stopped"; }
            Check(stopped && f.Steps.Length == 1, "field transition never masks cancellation or restarts stopped movement");
        }
        using (var f = new Fixture(Path.Combine(root, "walk-unknown-popup")))
        {
            f.H.OnCommand = _ => f.H.Page("UnknownPaymentPopupUI");
            bool blocked = false;
            try { await f.Route.Walk(91, 1); }
            catch (StageHostException ex) { blocked = ex.Kind == "adapter"; }
            Check(blocked && f.Steps.Length == 1, "field recovery leaves an unknown confirmation untouched");
        }
        using (var f = new Fixture(Path.Combine(root, "walk-hidden-account-change")))
        {
            f.H.OnCommand = _ => f.H.Page("OverheadManageUI");
            f.H.OnDelay = () => { if (f.Steps.Length > 0) { f.H.Frame["AccountKey"] = "another-account"; f.Scene(); } };
            bool blocked = false;
            try { await f.Route.Walk(91, 1); }
            catch (InvalidDataException ex) { blocked = ex.Message.Contains("account/process changed", StringComparison.Ordinal); }
            Check(blocked && f.Steps.Length == 1, "account change during field recovery cannot replay navigation on the new account");
        }
        using (var f = new Fixture(Path.Combine(root, "route-planning-no-settle")))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            Check(f.H.Time == 0 && f.Steps.Length == 0, "ready-map route planning performs no fixed settle or movement");
            Check(await nav.Step(Rows(plan["actions"])[0]) && f.MapId == 1
                && f.FirstCommandAt < .2,
                "planned walking starts from current ready input without another fixed settle");
        }
        using (var f = new Fixture(Path.Combine(root, "route-planning-stale-origin")))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            f.MapId = 2; f.Scene();
            Check(!await nav.Step(Rows(plan["actions"])[0]) && f.Steps.Length == 0,
                "route decision is refreshed and discarded after map changes");
        }
        using (var f = new Fixture(Path.Combine(root, "route-no-error-export")))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            for (int i = 0; i < 3; i++)
            {
                f.MapId = 9; f.Scene();
                var p = await nav.Plan([1], teleports: false);
                Check(await nav.Step(Rows(p["actions"])[0]), "unchanged route attempt completes " + i);
            }
            string traceFile = Directory.GetFiles(Path.Combine(f.H.Root, "live", "travel-diagnostics"), "route-*.json").Single();
            var trace = DailyJson.TryRead<JsonObject>(traceFile)!;
            Check(Rows(trace["events"]).Any(e => S(e["state"]) == "arrived"), "successful travel retains a compact route trace without an exception");
            var export = await DailyDiagnosticExport.CreateAsync(f.H.Root, Path.Combine(root, "route-no-error.zip"), "manually stopped", "test");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(export.Path))
                Check(zip.GetEntry("logs/main/live/travel-diagnostics/" + Path.GetFileName(traceFile)) != null,
                    "manual diagnostic export includes normal route decisions without requiring an error");
            f.MapId = 9; f.Scene();
            var repeat = await nav.Plan([1], teleports: false);
            int commands = f.Steps.Length;
            Check(!await nav.Step(Rows(repeat["actions"])[0]) && f.Steps.Length == commands
                && nav.State == DailyCollectionNavigator.NavigationState.ReadyForWork,
                "route repetition excludes only that departure and returns to work selection without another command");
            nav.TrackProgress(O(("remaining", 1)));
            Check(await nav.Step(Rows(repeat["actions"])[0]), "verified changed goals clear the route guard");
        }
        using (var f = new Fixture(Path.Combine(root, "route-planning-loading")))
        {
            f.H.Frame["Surfaces"]![0]!["InputReady"] = false;
            f.H.OnDelay = () => { if (f.H.Time >= .6) f.Scene(); };
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            Check(f.H.Time >= .6 && f.H.Time < .9 && f.Steps.Length == 0,
                "route planning still waits for actual field input readiness");
            f.H.Stopped = true;
            bool stopped = false;
            try { await nav.Step(Rows(plan["actions"])[0]); }
            catch (StageHostException ex) { stopped = ex.Kind == "stopped"; }
            Check(stopped && f.Steps.Length == 0, "route execution cannot bypass cancellation after fast planning");
        }
        using (var f = new Fixture(Path.Combine(root, "route-native-teleport-stability")))
        {
            f.MapId = 1; f.Destination = 2; f.PortNear = true; f.Scene();
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            Check(!await nav.Step(O(("kind", "teleport"), ("origin", Region(nav.Evidence)), ("destination", 2), ("mode", "native")))
                && f.FirstCommandAt >= 1 && f.Steps.Contains("mainline_interact"),
                "nearby native teleport retains stable-field gate before opening the portal");
        }
        using (var f = new Fixture(Path.Combine(root, "movement-talent-history")))
        {
            var op = f.H.Business.Create(f.H.Context, "dispatch.start", f.H.Evidence(), O(("kind", 2)), new());
            op["state"] = "completed"; f.H.Business.Save(op);
            string path = Path.Combine(f.H.Root, "live", "managed-business", S(op["id"]) + ".json");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await f.Route.RecoverExpired(f.H.Evidence());
            Check(f.Steps.Length == 0, "travel-talent recovery skips completed capture bodies before reading them");
            op["state"] = "unknown"; DailyJson.Write(path, op);
            await f.Route.RecoverExpired(f.H.Evidence());
            Check(S(f.H.Business.Records(f.H.Context, "dispatch.start").Single()["state"]) == "unknown" && f.Steps.Length == 0,
                "externally unresolved travel talent remains pending without enough recovery proof");
        }
        using (var f = new Fixture(Path.Combine(root, "board-tavern-exit")))
        {
            await f.Board();
            Check(f.MapId == 1 && f.Steps.SequenceEqual(new[] { "mainline_walk", "weekly_npc_board_nav", "weekly_npc_board_stop", "weekly_npc_board_open" }),
                "board visit walks out of tavern and opens town board without waypoint detour");
            Check(Directory.GetFiles(Path.Combine(f.H.Root, "live", "route-atlas"), "*json", SearchOption.AllDirectories).Length > 0,
                "board travel persists geometry through shared route atlas");
        }
        foreach (bool sameMap in new[] { false, true })
        foreach (bool interactionNear in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, $"board-shortcut-{sameMap}-{interactionNear}"))
            { MapId = sameMap ? 1 : 9, BattleMap = 1, QuickEnabled = true, QuickLandNear = interactionNear })
        {
            f.Route.ProtectWeeklyTravel = true;
            f.Route.W.Settings.General.FastTravel = true;
            f.Scene();
            await f.Board();
            string[] expected = interactionNear ? ["field_quick_travel", "weekly_npc_board_open"]
                : ["field_quick_travel", "weekly_npc_board_nav", "weekly_npc_board_stop", "weekly_npc_board_open"];
            Check(f.MapId == 1 && f.Steps.SequenceEqual(expected),
                $"board shortcut avoids stealth and speed talents, including native nearby final approach: sameMap={sameMap}, near={interactionNear}");
        }
        using (var f = new Fixture(Path.Combine(root, "board-shortcut-leaves-threats")) { BattleMap = 9, QuickEnabled = true })
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Board();
            Check(f.Steps.SequenceEqual(new[] { "field_quick_travel", "weekly_npc_board_open" }),
                "direct board shortcut leaves a threatening origin without first casting stealth");
        }
        foreach (bool near in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "board-already-near-" + near))
            { MapId = 1, BattleMap = 1, Near = near, QuickEnabled = !near, QuickNear = true })
        {
            f.Route.ProtectWeeklyTravel = true;
            f.Route.W.Settings.General.FastTravel = true;
            f.Scene();
            await f.Board();
            Check(f.Steps.SequenceEqual(near ? ["weekly_npc_board_open"]
                : new[] { "weekly_npc_board_nav", "weekly_npc_board_stop", "weekly_npc_board_open" }),
                "existing board proximity skips redundant teleport and talent preparation: " + near);
        }
        using (var f = new Fixture(Path.Combine(root, "board-shortcut-after-interior"))
            { QuickEnabled = true, QuickOutsideOnly = true, BattleMap = 1 })
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Board();
            Check(f.Steps.SequenceEqual(new[] { "mainline_walk", "field_quick_travel", "weekly_npc_board_open" }),
                "board shortcut is reconsidered after leaving an interior, before walking across the destination");
        }
        foreach (string mode in new[] { "unavailable", "other-npc", "other-pack", "failed", "outside-nearby-area" })
        using (var f = new Fixture(Path.Combine(root, "board-walking-protection-" + mode))
            { MapId = 1, BattleMap = 1, QuickEnabled = mode != "unavailable", QuickNpc = mode == "other-npc" ? 4 : 999,
                QuickPack = mode == "other-pack" ? 2 : 1, QuickNear = mode == "other-npc", QuickFails = mode == "failed",
                QuickLandNear = false, QuickLandNearby = false })
        {
            f.Route.ProtectWeeklyTravel = true;
            f.Scene();
            bool protectedWalk = false;
            var onCommand = f.H.OnCommand;
            f.H.OnCommand = c =>
            {
                if (S(c["Kind"]) == "mainline_menu" && N(c["Value"]) == 17)
                { protectedWalk = true; throw new InvalidOperationException("test: reached real stealth preparation"); }
                onCommand!(c);
            };
            try { await f.Board(); }
            catch (DailyStepException error) when (protectedWalk && error.Message.Contains("test: reached real stealth preparation", StringComparison.Ordinal)) { }
            Check(protectedWalk && !f.Steps.Contains("weekly_npc_board_nav") && !f.Steps.Contains("weekly_npc_board_open"),
                "ordinary threatened board walk still prepares stealth when shortcut is " + mode);
        }
        using (var f = new Fixture(Path.Combine(root, "teleport-arrived-during-preparation")))
        {
            Check(await f.Route.Teleport(1) == null && f.MapId == 1 && f.Steps.SequenceEqual(new[] { "mainline_walk" }),
                "teleport preparation stops on destination reached through door before approaching portal");
        }
        using (var f = new Fixture(Path.Combine(root, "native-preparation-arrival")))
        {
            Check(await f.Route.PrepareTeleport(false, destination: 1) == null && f.Steps.SequenceEqual(new[] { "mainline_walk" }),
                "physical-portal fallback also stops when walking already reaches destination");
        }
        foreach (bool nativeOnly in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "same-map-" + nativeOnly)))
        {
            f.MapId = 1; f.Scene();
            Check(await f.Route.Teleport(1, nativeOnly: nativeOnly) == null && f.Steps.Length == 0,
                "ordinary same-map travel sends no commands: " + nativeOnly);
        }
        using (var f = new Fixture(Path.Combine(root, "explicit-same-map-warp")))
        {
            f.MapId = 1; f.PortNear = true; f.Scene();
            bool blocked = false;
            try { await f.Route.Teleport(1, waypoint: 2, force: true); }
            catch (DailyTravelBlocked ex) { blocked = ex.Message.Contains("No unlocked waypoint", StringComparison.Ordinal); }
            Check(blocked && f.Steps.SequenceEqual(new[] { "mainline_interact" }),
                "explicit forced same-map warp still opens native waypoint selection");
        }
        using (var f = new Fixture(Path.Combine(root, "other-destination")))
        {
            f.Destination = 2;
            bool blocked = false;
            try { await f.Route.Teleport(2); }
            catch (DailyTravelBlocked ex) { blocked = ex.Message.Contains("No unlocked waypoint", StringComparison.Ordinal); }
            Check(blocked && f.MapId == 1 && f.Steps.Contains("mainline_interact"),
                "intermediate map is not mistaken for final destination");
        }
        using (var f = new Fixture(Path.Combine(root, "board-locked-waypoint")))
        {
            f.MapId = 1; f.Destination = 2; f.GateDistance = 200; f.PortNear = true; f.Scene();
            await f.Board();
            Check(f.MapId == 2 && f.Steps.SequenceEqual(new[] { "mainline_interact", "back", "mainline_walk", "weekly_npc_board_nav", "weekly_npc_board_stop", "weekly_npc_board_open" }),
                "board routing closes unavailable waypoint selector and replans through observed door");
        }
        using (var f = new Fixture(Path.Combine(root, "resume-leftover-selector")))
        {
            f.MapId = 1; f.Scene(); f.H.Page("WayPointUI");
            await f.Route.Enter(1);
            await f.Board();
            Check(f.Steps.SequenceEqual(new[] { "back", "weekly_npc_board_nav", "weekly_npc_board_stop", "weekly_npc_board_open" }),
                "weekly rerun dismisses leftover waypoint selector and continues in current cartridge");
        }
        using (var f = new Fixture(Path.Combine(root, "selector-unknown-popup")))
        {
            f.MapId = 1; f.Scene(); f.H.Page("WayPointUI");
            f.H.Frame["Surfaces"]!.AsArray().Add(O(("Id",99),("Type","UnknownPaymentPopupUI"),("Popup",true),("Order",999),("InputReady",true),("Targets",new JsonArray())));
            bool blocked = false;
            try { await f.Route.Enter(1); } catch (StageHostException) { blocked = true; }
            Check(blocked && f.Steps.Length == 0, "leftover-selector recovery never closes or confirms a foreign popup");
        }
        using (var f = new Fixture(Path.Combine(root, "board-disconnected")))
        {
            f.BlockExit = true;
            bool blocked = false;
            try { await f.Board(); } catch (DailyNpcUnavailable) { blocked = true; }
            Check(blocked && f.Steps.Length == 0, "known board route failure stays NPC-unavailable without unknown-consumption recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "board-uncertain-command")))
        {
            f.FailCommand = true;
            bool blocked = false;
            try { await f.Board(); } catch (DailyStepException) { blocked = true; }
            Check(blocked && f.Steps.Length == 1, "uncertain submitted navigation is neither replayed nor downgraded to route failure");
        }
        using (var f = new Fixture(Path.Combine(root, "board-cancelled")))
        {
            f.H.Stopped = true;
            bool stopped = false;
            try { await f.Board(); } catch (StageHostException ex) { stopped = ex.Kind == "stopped"; }
            Check(stopped && f.Steps.Length == 0, "board map routing respects user cancellation");
        }
    }
}

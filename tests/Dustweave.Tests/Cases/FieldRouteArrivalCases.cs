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
        public bool Near, PortNear, BlockExit, FailCommand;
        public double GateDistance = 2; public double? FirstCommandAt;
        public int GateInstance = 91;
        public Fixture(string root)
        {
            H = new(root, [new DailyBusinessProof("dispatch.start", "collection", [], (_, _, _) => new())]);
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "flows"));
            foreach (string name in new[] { "collection-catalog.json", "steal-catalog.json", "route-atlas.json" })
                File.Copy(Path.Combine(TestPaths.SourceRoot, "assets", "flows", name), Path.Combine(assets, "flows", name), true);
            var w = new DailyWorkflow(root, assets, H.Context, H.Driver, H.Business, H.Workflow.Navigation, [], () => H.Stopped, H.Workflow.Relay);
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
                        else throw new Exception("Unexpected navigation target");
                        Scene();
                        break;
                    case "mainline_cancel_nav": break;
                    case "mainline_interact": H.Page("WayPointUI", "_objMapNextButton"); break;
                    case "back": Scene(); break;
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
            var approaches = new List<JsonObject>();
            var rows = new List<JsonObject> {
                Read("mainline.map",0,(MapPath,O(("id",MapId),("packId",1))),
                    ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.x",0), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.y",0), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.z",0)),
                Read("weekly_npc.native",0,("$self",Native())),
                Read("navigation.pack",0,(DailyTravel.CurrentPack+".Id",1),(DailyTravel.CurrentPack+".PackType",0))
            };
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
            }
            if (S(H.Frame["UiToken"]) == "WayPointUI")
                rows.Add(Read("mainline.waypoint_ui",20,("_fieldMiniMap.CurrentMap",O(("id",Destination)))));
            rows.Add(Read("mainline.travel",0,("$self",O(("Pack",1),("Map",MapId),("Maps",Array(maps)),("Approaches",Array(approaches)),("Ground",true)))));
            return rows.ToArray();
        }
        public Task Board() => DailyWeeklyNpcBoard.Open(Route, new(Native(), [], []), 1);
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks.Add(message); }
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
            bool changed = false; double changedAt = 0;
            f.H.OnCommand = c => { if (S(c["Kind"]) != "mainline_walk") throw new Exception("Unexpected movement during transition"); };
            f.H.Driver.SubmissionGuard = () =>
            {
                if (!changed && f.Steps.Length == 1 && f.H.Time >= 5)
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
                && f.FirstCommandAt >= 1 && f.FirstCommandAt < 1.3,
                "planned walking keeps one full stable-field gate before movement");
        }
        using (var f = new Fixture(Path.Combine(root, "route-planning-stale-origin")))
        {
            var nav = new DailyCollectionNavigator(f.Route, 1, f.H.Evidence());
            var plan = await nav.Plan([1], teleports: false);
            f.MapId = 2; f.Scene();
            Check(!await nav.Step(Rows(plan["actions"])[0]) && f.Steps.Length == 0,
                "route decision is refreshed and discarded after map changes");
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

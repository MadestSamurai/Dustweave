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
        public double GateDistance = 2;
        public Fixture(string root)
        {
            H = new(root, []);
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
                Read("mainline.map",0,(MapPath,O(("id",MapId),("packId",1)))),
                Read("weekly_npc.native",0,("$self",Native())),
                Read("navigation.pack",0,(DailyTravel.CurrentPack+".Id",1),(DailyTravel.CurrentPack+".PackType",0))
            };
            if (MapId is 9 or 1)
            {
                int instance = MapId == 9 ? 91 : 12;
                long dest = MapId == 9 ? 1 : 2;
                rows.Add(Read("mainline.gate",instance,("gameObject.name","Gate_test"),(ObjectPath,instance),(DestinationPath,dest)));
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

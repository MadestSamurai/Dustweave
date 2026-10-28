using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;

static class HazardTravelCases
{
    static JsonObject[] Maps() => [
        O(("Id", 20021), ("MapType", 1), ("Exits", new[] { 20022, 20023 }), ("HasWaypoint", true), ("CanSummon", true), ("EnvironmentalHazard", true)),
        O(("Id", 20022), ("MapType", 1), ("Exits", new[] { 20021, 20023 }), ("HasWaypoint", true), ("CanSummon", true), ("EnvironmentalHazard", true)),
        O(("Id", 20023), ("MapType", 0), ("Exits", new[] { 20021, 20022 }), ("HasWaypoint", true), ("CanSummon", true))];
    static JsonObject Quick() => O(("Npc", 900), ("Map", 20023), ("Name", "Shop"), ("AtTarget", false));
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly DailyFieldRoute Route;
        public long MapId = 20021, PageMap;
        public bool Near, QuickAvailable = true, QuickFails, Disabled, Locked, LoseProtectionInPicker;
        public double Until, LoadSeconds;
        public readonly List<string> Actions = [];
        public int Casts;
        int menu;
        string quickState = "idle";
        public Fixture(string root)
        {
            H = new(root, DailyFieldRoute.Proofs().ToArray());
            H.Workflow.Settings.General.FastTravel = false;
            Route = new(H.Workflow);
            H.Readings = Readings;
            Field();
            H.OnCommand = c =>
            {
                string kind = S(c["Kind"]);
                Actions.Add(MapId + ":" + kind);
                switch (kind)
                {
                    case "field_quick_travel":
                        if (N(c["Value"]) != 900 || N(c["Items"]?[0]) != 20023) throw new Exception("wrong safe hub");
                        quickState = QuickFails ? "failed" : "arrived";
                        if (!QuickFails) { MapId = 20023; Near = false; Field(); }
                        break;
                    case "mainline_walk":
                        if (MapId != 20023 || N(c["Value"]) != 700) throw new Exception("walked through a damage field");
                        Near = true; break;
                    case "mainline_cancel_nav": break;
                    case "mainline_menu":
                        if (MapId != 20023 || !Near) throw new Exception("stealth was prepared outside the safe portal");
                        menu = I(c["Value"]); H.Page("QuickMenuUI"); break;
                    case "mainline_talent":
                        if (MapId != 20023 || N(c["Value"]) != 1705) throw new Exception("unsafe or unrelated talent");
                        Casts++; Until = H.Time + 30;
                        H.Event("dispatch.start", "request");
                        H.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", 1705), ("useCount", Casts))));
                        H.Time += .01; Field(); break;
                    case "back": Field(); break;
                    case "mainline_interact":
                        if (!Near || MapId != 20023) throw new Exception("unsafe departure portal");
                        PageMap = MapId;
                        H.Page("WayPointUI", "_objMapPrevButton", "_objMapNextButton", "portal");
                        break;
                    case "click":
                        if (N(c["TargetId"]) == 10) { PageMap--; if (LoseProtectionInPicker) Until = H.Time; }
                        else if (N(c["TargetId"]) == 11) PageMap++;
                        else if (N(c["TargetId"]) == 12)
                        {
                            if (Until - H.Time <= 5) throw new Exception("departed without stealth");
                            H.Event("mainline.waypoint_use", "request");
                            MapId = PageMap; Near = true; H.Time += LoadSeconds; Field();
                            H.Event("mainline.waypoint_use", "response", ("IsSuccess", true));
                        }
                        else throw new Exception("unexpected pointer " + c);
                        break;
                    default: throw new Exception("unexpected hazard travel command " + c);
                }
            };
        }
        void Field() { H.Frame["Scene"] = "Map2002_" + MapId; H.Page("GameFieldDefaultUI"); }
        JsonObject Read(string id, int instance, params (string Key, object? Value)[] values)
        { var row = WorkflowCases.Reading(id, values); row["InstanceId"] = instance; return row; }
        JsonObject[] Readings()
        {
            var rows = new List<JsonObject> {
                Read("mainline.map", 0, (MapPath, O(("id", MapId), ("packId", 2002), ("mapType", MapId == 20023 ? 0 : 1)))),
                Read("mainline.travel", 0, ("$self", O(("Pack", 2002), ("Map", MapId), ("Ground", true), ("Maps", Array(Maps())),
                    ("ThreatsKnown", true), ("ThreatsPresent", false), ("QuickTargets", QuickAvailable ? new JsonArray(Quick()) : new JsonArray()),
                    ("Approaches", new JsonArray(O(("Instance", 700), ("Reachable", true), ("Distance", Near ? 0 : 4), ("Kind", "waypoint"))))))),
                Read("route.quick", 0, ("$self", O(("Pack", 2002), ("Map", MapId), ("Npc", 900), ("State", quickState),
                    ("Targets", QuickAvailable ? new JsonArray(Quick()) : new JsonArray())))),
                Read("mainline.waypoint", 700, (ObjectPath, 42), (WaypointNear, Near), ("ὡὩὫὦὧὢὡὬὡὥὥ", "WayPoint_Enable")),
                Read("mainline.durations", 0, ("effects", new JsonArray(O(("Key", 17), ("Value.ὫὢὢὬὫὫὡὩὪὩὮ", Until > H.Time), ("Value.ὠὨὠὭὬὤὪὢὫὨὮ", Math.Max(0, Until - H.Time)))))),
                Read("mainline.talent_counts", 0, ("Values", new JsonArray(O(("groupId", 1705), ("useCount", Casts))))),
                Read("mainline.talent_rows", 0, ("$self", new JsonArray(O(("Instance", 1705), ("Kind", 17), ("Group", 1705), ("Cooldown", 0), ("Gate", ""), ("Reason", Disabled ? "character_dead" : "")))))
            };
            if (menu == 17) rows.Add(Read("dispatch.talent", 1705, ("ὬὩὬὡὯὮὨὣὩὦὡ", Kinds[17]),
                (SkillPath, O(("id", 17050), ("groupId", 1705), ("classType", 17), ("valueList", new JsonArray(21, 30)), ("catalystValue", 0))), ("_objDisableImage.activeSelf", Disabled)));
            if (S(H.Frame["UiToken"]) == "WayPointUI")
            {
                rows.Add(Read("mainline.waypoint_ui", 800, ("_fieldMiniMap.CurrentMap", O(("id", PageMap)))));
                rows.Add(Read("mainline.waypoint_icon", 12, ("gameObject.GetInstanceID()", 12), ("ὦὠὣὧὥὯὤὮὧὫὬ", 42),
                    ("_objWayPointClickable.activeSelf", !Locked), ("_objWayPointEnable.activeSelf", !Locked)));
            }
            return rows.ToArray();
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks.Add("HazardTravel: " + message); }
        var atlas = new DailyRouteAtlas(Maps());
        atlas.Observe("[20021,[]]", [O(("object", 1), ("destination", 20022), ("distance", .1))], [0], true);
        atlas.ObserveQuick([Quick()]);
        var plan = Rows(atlas.Plan([20022])["actions"]);
        Check(plan.Select(a => S(a["kind"])).SequenceEqual(new[] { "quick", "teleport" }) && N(plan[0]["destination"]) == 20023,
            "even an adjacent gate and a nearby portal use the safe hub instead of crossing a damage field");
        var old = atlas.Export();
        foreach (bool quick in new[] { false, true })
        {
            var copy = new DailyRouteAtlas(Maps(), old);
            copy.Observe("[20021,[]]", [O(("object", 1), ("destination", 20022), ("distance", .1))], [0], true);
            copy.ObserveQuick(quick ? [Quick()] : []);
            if (quick) copy.BlockedQuick.Add(("[20021,[]]", 900));
            bool blocked = false; try { copy.Plan([20022]); } catch (DailyTravelBlocked) { blocked = true; }
            Check(blocked, "unavailable shortcut cannot fall back to a cached walk, nearby portal or summon: " + quick);
        }
        Check(!DailyHazardTravelPolicy.SafeHub(O(("HasWaypoint", true))), "missing map type cannot be used as a safe hub");
        Check(DailyHazardTravelPolicy.Hazardous(O(("EnvironmentalHazard", true)), 99), "native damage trap metadata works outside Nightmare Winter");
        Check(!DailyHazardTravelPolicy.Hazardous(O(("MapType", 1)), 99), "ordinary combat maps are not environmental hazards");
        foreach (string mode in new[] { "arbitrary-origin", "already-portal", "same-map", "expired", "already-safe", "active", "no-quick", "failed-quick", "disabled", "locked", "slow-load", "stale-walk", "stale-walk-same", "collection-origin", "expire-picker" })
        using (var f = new Fixture(Path.Combine(root, "hazard-" + mode)))
        {
            if (mode == "already-portal") f.Near = true;
            if (mode == "expired") f.Until = f.H.Time + 2;
            if (mode == "already-safe") f.MapId = 20023;
            if (mode == "active") f.Until = 100;
            f.QuickAvailable = mode != "no-quick"; f.QuickFails = mode == "failed-quick";
            f.Disabled = mode == "disabled"; f.Locked = mode == "locked";
            f.LoadSeconds = mode == "slow-load" ? 31 : 0;
            f.LoseProtectionInPicker = mode == "expire-picker";
            bool blocked = false;
            try
            {
                if (mode == "collection-origin") await f.Route.Protection(f.H.Evidence());
                else if (mode is "stale-walk" or "stale-walk-same") await f.Route.Walk(999, mode == "stale-walk-same" ? 20021 : 20022);
                else await f.Route.Teleport(mode == "same-map" ? 20021 : 20022, force: true);
            }
            catch (DailyTravelBlocked) { blocked = true; }
            bool failure = mode is "no-quick" or "failed-quick" or "disabled" or "locked" or "slow-load" or "expire-picker";
            Check(blocked == failure, "production transfer outcome: " + mode);
            Check(f.Actions.All(a => !a.EndsWith(":mainline_walk") && !a.EndsWith(":mainline_talent") || a.StartsWith("20023:")),
                "all walking and stealth casts stay inside the safe area: " + mode);
            if (!failure) Check(f.MapId == (mode is "same-map" or "stale-walk-same" or "collection-origin" ? 20021 : 20022) && f.Near && f.Until - f.H.Time > 5,
                "arrives at the requested physical portal with protection: " + mode);
            if (mode is "no-quick" or "failed-quick") Check(f.Casts == 0, "failed exit never renews stealth in place: " + mode);
            if (mode is "disabled" or "locked" or "slow-load" or "expire-picker") Check(f.MapId == 20023, "unavailable destination stays/returns to safety: " + mode);
            if (mode == "active") Check(f.Casts == 0, "active stealth is inherited through quick travel and portals");
            if (mode is "slow-load" or "expire-picker") Check(f.Casts == 2, "lost protection recovery is bounded and does not loop in traffic: " + mode);
        }
    }
}

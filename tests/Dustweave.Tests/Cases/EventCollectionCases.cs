using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;

static class EventCollectionCases
{
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly DailyFieldRoute Route;
        public long Pack, MapId;
        public int MapType, MenuKind;
        public bool DropsPending, MonstersPending, Research, Disabled, Missing;
        public double StealthUntil;
        public readonly List<int> Casts = [];
        readonly Dictionary<int, int> counts = [];
        JsonObject network = new();
        public Fixture(string root, long pack, long map, int type = 0)
        {
            Pack = pack; MapId = map; MapType = type;
            H = new(root, DailyFieldRoute.Proofs().ToArray());
            Route = new(H.Workflow);
            H.Readings = Readings;
            Field();
            H.OnCommand = c =>
            {
                switch (S(c["Kind"]))
                {
                    case "collection_query":
                        network = O(("Query", c["Id"]), ("State", "ready"), ("Error", ""), ("Pack", Pack), ("Maps", c["Items"]), ("Week", 500), ("Now", 100),
                            ("Drops", new JsonArray(O(("Id", 1001), ("Map", Pack * 10 + 1)))),
                            ("Rewards", DropsPending ? "[]" : "[{\"id\":1001}]"),
                            ("Targets", new JsonArray(O(("Id", 101), ("Map", Pack * 10 + 2), ("Group", 1)))),
                            ("Monsters", new JsonArray(O(("monsterId", 101), ("groupId", 1), ("activeFlag", true), ("respawnTime", MonstersPending ? 0 : 1000))).ToJsonString()),
                            ("DeferredTargets", new JsonArray()));
                        break;
                    case "mainline_menu": MenuKind = I(c["Value"]); H.Page("QuickMenuUI"); break;
                    case "back": Field(); break;
                    case "mainline_talent":
                        int kind = I(c["Value"]) / 100;
                        Casts.Add(kind); counts[kind] = counts.GetValueOrDefault(kind) + 1;
                        if (kind == 3) MonstersPending = false;
                        if (kind == 4) DropsPending = false;
                        if (kind == 6) Research = true;
                        if (kind == 17) StealthUntil = H.Time + 30;
                        H.Event("dispatch.start", "request");
                        H.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", kind * 100 + 5), ("useCount", counts[kind]))));
                        H.Time += .01; Field(); break;
                    default: throw new Exception("Unexpected event collection command: " + c);
                }
            };
        }
        void Field() { H.Frame["Scene"] = "Map" + Pack + "_" + MapId; H.Page("GameFieldDefaultUI"); }
        JsonObject[] Readings()
        {
            int group = MenuKind * 100 + 5;
            var rows = new List<JsonObject> {
                WorkflowCases.Reading("mainline.map", (MapPath, O(("id", MapId), ("packId", Pack), ("mapType", MapType))), ("HasCanOverwhelmMonster()", MonstersPending)),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500)),
                WorkflowCases.Reading("mainline.network", ("$self", network)),
                WorkflowCases.Reading("mainline.talent_counts", ("Values", Array(counts.Select(c => O(("groupId", c.Key * 100 + 5), ("useCount", c.Value)))))),
                WorkflowCases.Reading("mainline.research", ("$self", O(("Active", Research), ("Remaining", Research ? 100 : 0)))),
                WorkflowCases.Reading("mainline.durations", ("effects", new JsonArray(O(("Key", 17), ("Value.ὫὢὢὬὫὫὡὩὪὩὮ", StealthUntil > H.Time), ("Value.ὠὨὠὭὬὤὪὢὫὨὮ", Math.Max(0, StealthUntil - H.Time)))))),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", Missing ? new JsonArray() : new JsonArray(O(("Instance", group), ("Kind", MenuKind), ("Group", group), ("Cooldown", 0), ("Gate", ""), ("PackRestricted", false), ("Reason", Disabled ? "character_dead" : "")))))
            };
            if (MenuKind > 0 && !Missing)
            {
                var talent = WorkflowCases.Reading("dispatch.talent", ("ὬὩὬὡὯὮὨὣὩὦὡ", Kinds[MenuKind]),
                    (SkillPath, O(("id", group * 10), ("groupId", group), ("classType", MenuKind), ("valueList", new JsonArray(21, 30)), ("catalystValue", 0))), ("_objDisableImage.activeSelf", Disabled));
                talent["InstanceId"] = group; rows.Add(talent);
            }
            if (DropsPending)
            {
                var reward = WorkflowCases.Reading("mainline.reward", (ObjectPath, 1001), (DropAvailablePath, true), ("ὯὩὮὩὮὣὯὫὮὡὧ", true), ("ὦὫὧὯὥὫὦὪὣὦὪ", false));
                // Field visibility is independent of map-list completion.
                reward["InstanceId"] = 1001; rows.Add(reward);
            }
            return rows.ToArray();
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        using (var f = new Fixture(Path.Combine(root, "event-coverage"), 2003, 20031))
        {
            Check(f.Route.Catalog.Maps(2003).SequenceEqual(new long[] { 20031, 20032 }), "event three includes material-independent drops and its battle area");
            Check(f.Route.Catalog.Maps(2004).SequenceEqual(new long[] { 20041, 20042 }), "owned hidden event four is not excluded from collection");
            Check(f.Route.Catalog.Maps(2005).SequenceEqual(new long[] { 20051, 20052 }), "event five includes its suppression-only area");
            Check(!f.Route.Catalog.Packs().Contains(2006) && f.Route.Catalog.Maps(2007).SequenceEqual(new long[] { 20071 }), "unconfirmed event six and seven routes remain unchanged");
            Check(Enumerable.Range(2001, 7).Where(BD2Daily.Live.LiveProtocol.CollectionEventPack).Select(x => (long)x).SequenceEqual(f.Route.Catalog.Packs().Where(p => p >= 2000)), "host collection catalog and native entry/walking scope agree");
            var old = O(("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", f.H.Context["actor"]![3]), ("player", f.H.Context["actor"]![4]),
                ("network", O(("State", "ready"), ("Error", ""), ("Pack", 2005), ("Week", 500), ("Now", 100), ("Maps", new JsonArray(20051)), ("Drops", new JsonArray()), ("Targets", new JsonArray()), ("Rewards", "[]"), ("Monsters", "[]"))));
            DailyJson.Write(Path.Combine(f.H.Root, "live", "server-collection", S(f.H.Context["actor"]![3]), "500", "2005.json"), old);
            Check(f.Route.Progress.Cached(2005, [20051, 20052], "500", true) == null, "old event-five cache cannot certify an unqueried battle map");
            var item = O(("task", "weekly_mainline"), ("state", "completed"), ("carried_forward", true), ("result", O(("reason", "weekly_route_verified"))));
            var queue = O(("items", new JsonArray(item)));
            DailyWeeklyMission.RecheckLegacy(queue);
            Check(S(queue["items"]![0]!["state"]) == "partial" && queue["items"]![0]!["carried_forward"] == null, "old all-complete result becomes selectable for new coverage");
            queue["items"]![0]!["state"] = "completed";
            DailyWeeklyMission.RecheckLegacy(queue, resume: true);
            Check(S(queue["items"]![0]!["state"]) == "pending", "resume rechecks omitted maps without manual completion overrides");
            queue["items"]![0]!["state"] = "completed";
            queue["items"]![0]!["result"]!["collection_coverage"] = DailyCollectionCatalog.CoverageRevision;
            DailyWeeklyMission.RecheckLegacy(queue, resume: true);
            Check(S(queue["items"]![0]!["state"]) == "completed", "current coverage completion is not reset repeatedly");
        }
        foreach (long pack in new long[] { 2003, 2004, 2005 })
        using (var f = new Fixture(Path.Combine(root, "event-suppression-" + pack), pack, pack * 10 + 2, 1) { MonstersPending = true })
        {
            var result = await f.Route.Gather();
            Check(S(result["state"]) == "completed" && f.Casts.SequenceEqual(new[] { 3 }), "suppression-only area is handled without spending absorption or research: " + pack);
            await f.Route.Gather();
            Check(f.Casts.Count == 1, "fresh server completion prevents repeat suppression: " + pack);
        }
        using (var f = new Fixture(Path.Combine(root, "event-three-absorb"), 2003, 20031) { DropsPending = true })
        {
            var result = await f.Route.Gather();
            Check(S(result["state"]) == "completed" && f.Casts.SequenceEqual(new[] { 6, 4 }), "event three researches then absorbs its real pending drops");
            await f.Route.Gather();
            Check(f.Casts.Count == 2, "already collected event-three drops do not consume another absorption");
        }
        foreach (string scenario in new[] { "safe", "patrol", "disabled", "missing" })
        using (var f = new Fixture(Path.Combine(root, "event-stealth-" + scenario), 2002, scenario == "safe" ? 20023 : 20021, scenario == "safe" ? 0 : 1)
            { Disabled = scenario == "disabled", Missing = scenario == "missing" })
        {
            await f.Route.Protection(f.H.Evidence());
            await f.Route.Protection(f.H.Evidence());
            Check(scenario == "patrol" ? f.Casts.SequenceEqual(new[] { 17 }) && NeedsStealth(f.H.Evidence()) : f.Casts.Count == 0 && !NeedsStealth(f.H.Evidence()), "event-two stealth respects safe area, active duration and native availability: " + scenario);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_menu") <= 1, "unavailable or active stealth does not repeatedly reopen its menu: " + scenario);
            Check(S(f.H.Frame["UiToken"]) == "GameFieldDefaultUI", "stealth probe leaves the field usable: " + scenario);
        }
    }
}

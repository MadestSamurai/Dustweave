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
        public long? DropMap;
        public int MapType, MenuKind;
        public bool DropsPending, MonstersPending, Research, Disabled, Missing, NearWaypoint;
        public bool ThreatsKnown = true;
        public int OtherMonsters;
        public double StealthUntil;
        public void ExhaustAbsorption() => counts[4] = 21;
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
                            ("Drops", new JsonArray(O(("Id", 1001), ("Map", DropMap ?? Pack * 10 + 1)))),
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
                WorkflowCases.Reading("mainline.travel", ("$self", O(("Pack", Pack), ("Map", MapId), ("Ground", true),
                    ("Maps", new JsonArray(O(("Id", MapId), ("Exits", new JsonArray()), ("CanSummon", true)))), ("Approaches", new JsonArray()),
                    ("ThreatsKnown", ThreatsKnown), ("ThreatsPresent", OtherMonsters > 0 || MonstersPending)))),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500)),
                WorkflowCases.Reading("mainline.waypoint", (WaypointNear, NearWaypoint)),
                WorkflowCases.Reading("mainline.network", ("$self", network)),
                WorkflowCases.Reading("mainline.talent_counts", ("Values", Array(counts.Select(c => O(("groupId", c.Key * 100 + 5), ("useCount", c.Value)))))),
                WorkflowCases.Reading("mainline.talent_limits", ("$self", new JsonArray(O(("Kind", 4), ("Group", 405), ("Limit", 21)), O(("Kind", 20), ("Group", 2005), ("Limit", 21))))),
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
            Check(!f.Route.Catalog.Supported(2004) && !f.Route.Catalog.Packs().Contains(2006), "hidden collaboration IDs are not the fourth and fifth visible cartridges");
            Check(f.Route.Catalog.Maps(2005).SequenceEqual(new long[] { 20051, 20052 }), "visible event four (2005) includes its suppression-only area");
            Check(f.Route.Catalog.Maps(2007).SequenceEqual(new long[] { 20071, 20072 }), "visible event five (2007) includes its suppression-only area");
            Check(Enumerable.Range(2001, 7).Where(BD2Daily.Live.LiveProtocol.CollectionEventPack).Select(x => (long)x).SequenceEqual(f.Route.Catalog.Packs().Where(p => p >= 2000)), "host collection catalog and native entry/walking scope agree");
            var old = O(("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", f.H.Context["actor"]![3]), ("player", f.H.Context["actor"]![4]),
                ("network", O(("State", "ready"), ("Error", ""), ("Pack", 2005), ("Week", 500), ("Now", 100), ("Maps", new JsonArray(20051)), ("Drops", new JsonArray()), ("Targets", new JsonArray()), ("Rewards", "[]"), ("Monsters", "[]"))));
            DailyJson.Write(Path.Combine(f.H.Root, "live", "server-collection", S(f.H.Context["actor"]![3]), "500", "2005.json"), old);
            Check(f.Route.Progress.Cached(2005, [20051], "500", true) != null, "catalog expansion preserves already verified maps in the same cartridge");
            Check(f.Route.Progress.Cached(2005, [20051, 20052], "500", true) == null, "old event-four cache cannot certify an unqueried battle map");
            old["server"] = "different-server";
            DailyJson.Write(Path.Combine(f.H.Root, "live", "server-collection", S(f.H.Context["actor"]![3]), "500", "2005.json"), old);
            Check(f.Route.Progress.Cached(2005, [20051], "500", true) == null, "shared snapshot must also match the connected server");
            var item = O(("task", "weekly_mainline"), ("state", "completed"), ("carried_forward", true), ("result", O(("reason", "weekly_route_verified"))));
            var queue = O(("items", new JsonArray(item)));
            DailyWeeklyMission.RecheckLegacy(queue);
            Check(S(queue["items"]![0]!["state"]) == "completed" && B(queue["items"]![0]!["carried_forward"]), "catalog revision preserves historical completion rather than resetting every account");
            queue["items"]![0]!["state"] = "completed";
            DailyWeeklyMission.RecheckLegacy(queue, resume: true);
            Check(S(queue["items"]![0]!["state"]) == "completed", "resume does not reopen unrelated completed weekly work");
            queue["items"]![0]!["state"] = "completed";
            queue["items"]![0]!["result"]!["collection_coverage"] = DailyCollectionCatalog.CoverageRevision;
            DailyWeeklyMission.RecheckLegacy(queue, resume: true);
            Check(S(queue["items"]![0]!["state"]) == "completed", "current coverage completion is not reset repeatedly");
        }
        using (var f = new Fixture(Path.Combine(root, "event-access-catalog"), 2003, 20031))
        {
            JsonObject Catalog(bool open) => WorkflowCases.Evidence(
                WorkflowCases.Reading("mainline.owned_packs", ("Count", 4), ("$items", Array(new long[] { 2003, 2004, 2005, 2007 }.Select(id => O(("Key", id), ("Value.Id", id)))))),
                WorkflowCases.Reading("mainline.packs", ("Count", 4), ("$items", Array(new long[] { 2003, 2004, 2005, 2007 }.Select(id => O(("Id", id), ("PackType", 6), ("UseSchedule", id == 2005 ? 1 : 0)))))),
                WorkflowCases.Reading("mainline.pack_access", ("$self", new JsonArray(O(("Id", 2005), ("Available", open))))));
            Check(f.Route.Catalog.AvailablePacks(Catalog(false)).SequenceEqual(new long[] { 2003, 2007 }), "historical ownership of a closed event cannot block later permanent events");
            Check(f.Route.Catalog.UnavailableEventPacks(Catalog(false)).SequenceEqual(new long[] { 2005 }), "closed scheduled cartridge is explained separately from completed maps");
            Check(f.Route.Catalog.AvailablePacks(Catalog(true)).SequenceEqual(new long[] { 2003, 2005, 2007 }), "native opening restores a supported event while hidden collaboration IDs remain excluded");
            var missing = Catalog(true); missing["Readings"]!.AsArray().RemoveAt(2);
            bool rejected = false;
            try { f.Route.Catalog.AvailablePacks(missing); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "missing event availability is not interpreted as permission to enter");
        }
        foreach (long target in new long[] { 2005, 2007 })
        foreach (string mode in new[] { "closed", "open", "expired-before-dispatch", "uncertain", "already-loaded" })
        using (var f = new Fixture(Path.Combine(root, "event-entry-" + target + "-" + mode), mode == "already-loaded" ? target : 2003, mode == "already-loaded" ? target * 10 + 1 : 20031))
        {
            var baseReadings = f.H.Readings;
            f.H.Readings = () => baseReadings().Concat(new[] {
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", f.Pack), (DailyTravel.CurrentPack + ".PackType", 6)),
                WorkflowCases.Reading("mainline.pack_access", ("$self", new JsonArray(O(("Id", target), ("Available", mode != "closed")))))
            }).ToArray();
            f.H.Page("GameFieldDefaultUI", "_buttonPackList");
            f.H.OnCommand = c => {
                if (S(c["Kind"]) == "click") f.H.Page("PackListUI");
                else if (S(c["Kind"]) == "mainline_pack")
                {
                    Check(N(c["Value"]) == target, "visible fourth/fifth entry uses the cartridge ID rather than the list ordinal: " + target);
                    if (mode == "expired-before-dispatch")
                        throw new DailyStepException("rejected", "event entry closed", true) { MayHaveDispatched = false, RejectionCode = "pack_entry_unavailable:event_closed" };
                    if (mode == "uncertain") throw new DailyStepException("uncertain", "entry reply unavailable", true);
                    f.Pack = target; f.MapId = target * 10 + 1; f.H.Frame["Scene"] = "Map" + target + "_001"; f.H.Page("GameFieldDefaultUI");
                }
                else throw new Exception("Unexpected event entry command");
            };
            string outcome = "entered";
            try { await f.Route.Enter(target); }
            catch (DailyTravelBlocked) { outcome = "skipped"; }
            catch (DailyStepException e) { outcome = e.Kind; }
            Check(outcome == (mode is "closed" or "expired-before-dispatch" ? "skipped" : mode == "uncertain" ? "uncertain" : "entered"), "event entry follows live availability and preserves uncertain dispatch: " + mode);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack") == (mode is "closed" or "already-loaded" ? 0 : 1), "event selection does not wait or repeat a refused entry: " + mode);
        }
        foreach (long pack in new long[] { 2003, 2005, 2007 })
        using (var f = new Fixture(Path.Combine(root, "event-suppression-" + pack), pack, pack * 10 + 2, 1) { MonstersPending = true })
        {
            var result = await f.Route.Gather();
            Check(S(result["state"]) == "completed" && f.Casts.SequenceEqual(new[] { 3 }), "suppression-only area is handled without spending absorption or research: " + pack);
            await f.Route.Gather();
            Check(f.Casts.Count == 1, "fresh server completion prevents repeat suppression: " + pack);
        }
        using (var f = new Fixture(Path.Combine(root, "event-suppression-without-absorption"), 2005, 20052, 1) { MonstersPending = true })
        {
            f.ExhaustAbsorption();
            var result = await f.Route.Gather();
            Check(S(result["state"]) == "completed" && f.Casts.SequenceEqual(new[] { 3 }), "exhausted absorption still allows suppression-only maps to finish");
        }
        using (var f = new Fixture(Path.Combine(root, "event-mixed-without-absorption"), 2003, 20031, 1) { MonstersPending=true,DropsPending=true })
        {
            f.ExhaustAbsorption();bool stopped=false;
            try{await f.Route.Gather();}catch(DailyQuotaExhausted){stopped=true;}
            Check(stopped&&f.Casts.Count==0,"mixed maps check absorption before spending suppression or research");
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
            { Disabled = scenario == "disabled", Missing = scenario == "missing", OtherMonsters = scenario == "safe" ? 0 : 1 })
        {
            await f.Route.Protection(f.H.Evidence());
            await f.Route.Protection(f.H.Evidence());
            Check(scenario == "patrol" ? f.Casts.SequenceEqual(new[] { 17 }) && NeedsStealth(f.H.Evidence()) : f.Casts.Count == 0 && !NeedsStealth(f.H.Evidence()), "event-two stealth respects safe area, active duration and native availability: " + scenario);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_menu") <= 1, "unavailable or active stealth does not repeatedly reopen its menu: " + scenario);
            Check(S(f.H.Frame["UiToken"]) == "GameFieldDefaultUI", "stealth probe leaves the field usable: " + scenario);
        }
        foreach (string scenario in new[] { "away", "waypoint", "active", "disabled", "missing" })
        using (var f = new Fixture(Path.Combine(root, "weekly-stealth-" + scenario), 8, 82, 1)
            { NearWaypoint = scenario == "waypoint", StealthUntil = scenario == "active" ? 100 : 0,
              Disabled = scenario == "disabled", Missing = scenario == "missing", MonstersPending = true })
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Route.Protection(f.H.Evidence());
            await f.Route.Protection(f.H.Evidence());
            Check(f.Casts.Count == (scenario == "away" ? 1 : 0), "weekly travel protects actual monsters away from an interactable waypoint: " + scenario);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_menu") <= 1, "weekly protection reuses active duration and caches unavailable stealth: " + scenario);
            if (scenario == "waypoint")
            {
                Check(f.H.Box.Commands.Count == 0, "a ready waypoint can be used directly without opening a talent menu");
                await f.Route.ProtectWeeklyMovement();
                Check(f.Casts.SequenceEqual(new[] { 17 }), "leaving a waypoint into monsters activates stealth before movement");
            }
            if (scenario == "away")
            {
                f.H.Time = f.StealthUntil + 1;
                await f.Route.Protection(f.H.Evidence(), walking: true);
                Check(f.Casts.SequenceEqual(new[] { 17, 17 }), "expired weekly protection is renewed instead of walking exposed");
            }
            Check(S(f.H.Frame["UiToken"]) == "GameFieldDefaultUI", "weekly stealth check leaves no talent popup behind: " + scenario);
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-stealth-open-waypoint"), 8, 82))
        {
            f.Route.ProtectWeeklyTravel = true;
            f.H.Page("WayPointUI");
            await f.Route.Protection(f.H.Evidence());
            Check(f.H.Box.Commands.Count == 0 && f.H.Time == 0, "an open teleport picker never waits for a field page to cast stealth");
        }
        foreach (long pack in new long[] { 8, 14, 1003, 2002 })
        using (var f = new Fixture(Path.Combine(root, "weekly-safe-walk-" + pack), pack, pack * 10 + 1))
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Route.Protection(f.H.Evidence());
            await f.Route.ProtectWeeklyMovement();
            Check(f.H.Box.Commands.Count == 0, "confirmed safe areas do not inherit cartridge-wide patrol protection: " + pack);
            f.MapType = 1;
            await f.Route.ProtectWeeklyMovement();
            Check(f.H.Box.Commands.Count == 0, "native-confirmed empty battle areas do not inherit cartridge-wide patrol protection: " + pack);
        }
        foreach (string risk in new[] { "cleared", "ordinary", "non-suppressible", "unknown", "missing-threats", "wrong-map", "wrong-pack" })
        using (var f = new Fixture(Path.Combine(root, "weekly-risk-" + risk), 8, 82, 1)
            { MonstersPending = risk == "ordinary", OtherMonsters = risk == "non-suppressible" ? 1 : 0, ThreatsKnown = risk != "unknown" })
        {
            f.Route.ProtectWeeklyTravel = true;
            var readings = f.H.Readings;
            f.H.Readings = () => readings().Select(r =>
            {
                if (S(r["Id"]) != "mainline.travel") return r;
                var travel = DailyEvidence.Values(r)["$self"]!.AsObject();
                if (risk == "missing-threats") travel.Remove("ThreatsPresent");
                if (risk == "wrong-map") travel["Map"] = 83;
                if (risk == "wrong-pack") travel["Pack"] = 9;
                return WorkflowCases.Reading("mainline.travel", ("$self", travel));
            }).ToArray();
            await f.Route.ProtectWeeklyMovement();
            await f.Route.ProtectWeeklyMovement();
            Check(f.Casts.Count == (risk == "cleared" ? 0 : 1), "fresh scene risk distinguishes cleared fields, non-suppressible enemies and incomplete observations: " + risk);
            Check(!f.H.Box.Commands.Any(c => S(c["Kind"]) == "collection_query"), "travel risk needs no extra network query: " + risk);
        }
        foreach (long pack in new long[] { 8, 14, 1003, 2002 })
        using (var f = new Fixture(Path.Combine(root, "weekly-waypoint-exit-" + pack), pack, pack * 10 + 2, 1) { NearWaypoint = true, OtherMonsters = 1 })
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Route.Protection(f.H.Evidence());
            await f.Route.PrepareTeleport(destination: pack * 10 + 1);
            Check(f.Casts.Count == 0 && f.H.Box.Commands.Count == 0, "battle-map waypoint exit to a safe destination never opens stealth: " + pack);
        }
        foreach (bool drops in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "weekly-suppress-first-" + drops), 8, 82, 1) { MonstersPending = true, DropsPending = drops, DropMap = 82 })
        {
            f.Route.ProtectWeeklyTravel = true;
            var result = await f.Route.Gather();
            await f.Route.ProtectWeeklyMovement();
            Check(S(result["state"]) == "completed" && f.Casts.SequenceEqual(drops ? new[] { 3, 6, 4 } : new[] { 3 }),
                "entry for suppression clears enemies before any stealth or collection action: " + drops);
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-protected-quest-target"), 8, 82, 1) { MonstersPending = true })
        {
            f.Route.ProtectWeeklyTravel = true;
            f.Route.CanSuppress = _ => false;
            await f.Route.ProtectWeeklyMovement();
            Check(f.Casts.SequenceEqual(new[] { 17 }) && f.MonstersPending, "walking protection preserves future NPC kill targets instead of suppressing them");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-npc-suppress-first"), 8, 82, 1) { MonstersPending = true })
        {
            f.Route.ProtectWeeklyTravel = true;
            await f.Route.PrepareNpcMap(82);
            await f.Route.ProtectWeeklyMovement();
            Check(f.Casts.SequenceEqual(new[] { 3 }), "NPC arrival also suppresses first and does not add stealth after the field is clear");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-cleared-no-renewal"), 8, 82, 1) { StealthUntil = 1 })
        {
            f.Route.ProtectWeeklyTravel = true;
            f.H.Time = 2;
            await f.Route.ProtectWeeklyMovement();
            Check(f.Casts.Count == 0 && f.H.Box.Commands.Count == 0, "expired stealth is not renewed for a field confirmed clear");
        }
    }
}

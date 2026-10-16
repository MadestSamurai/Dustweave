using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
using static Dustweave.DailyFieldRoute;

// Real weekly orchestration, cartridge navigation and checkpoint classification;
// only the game's mailbox and clock are synthetic.
static class WeeklyIsolationCases
{
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public long Pack = 1, MapId = 1;
        public int Used;
        public bool Pending = true, Restricted, Reachable = true, MapOnePending = true, MapTwoPending = true, LaterDrop;
        public readonly List<long> Queries = [], Visits = [];
        public Action? BeforeQuery;
        public bool NpcEnabled, RejectNpc, HuntingOffer, QueryFailsFirst;
        public bool WalkingBlocked;
        public bool AbsorbDrops, LoseAbsorbReply, RecoveryQueryFails, StaleDrop;
        public long NetworkWeek = 500;
        public readonly List<long[]> QueryMaps = [];
        public readonly HashSet<long> Picked = [];
        public readonly List<long> Accepted = [], Cleared = [];
        public readonly HashSet<long> MonstersPending = [];
        public readonly List<long> Suppressed = [];
        readonly Dictionary<long, long> npcActive = [];
        string npcQuery = "prior"; long popup; bool navigating;
        JsonObject network = new();
        public Fixture(string root)
        {
            H = new(root, DailyFieldRoute.Proofs().ToArray());
            H.Workflow.Settings.Weekly.Mainline = true;
            H.Readings = Readings;
            Scene();
            H.OnCommand = c =>
            {
                switch (S(c["Kind"]))
                {
                    case "collection_query":
                    case "collection_query_steal":
                        BeforeQuery?.Invoke();
                        Queries.Add(Pack);
                        QueryMaps.Add(c["Items"]!.AsArray().Select(N).ToArray());
                        network = Network(c);
                        if (RecoveryQueryFails && QueryMaps.Last().Length == 1) network["State"] = "failed";
                        break;
                    case "weekly_npc_query": npcQuery = S(c["Id"]); break;
                    case "weekly_npc_nav": navigating = true; break;
                    case "mainline_cancel_nav": navigating = false; break;
                    case "weekly_npc_board_nav": break;
                    case "weekly_npc_board_stop": break;
                    case "weekly_npc_board_open": H.Page("QuestBoardUI"); break;
                    case "weekly_npc_board_select": popup = N(c["Value"]); H.Page("QuestPopupUI"); break;
                    case "weekly_npc_accept":
                        Accepted.Add(popup); npcActive[Pack] = popup; popup = 0; H.Page("QuestBoardUI"); break;
                    case "weekly_npc_interact":
                        if (RejectNpc && Pack == 1)
                            H.Box.Values["live:receipts~" + S(c["Id"]) + ".json"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                                O(("Command", c), ("State", "rejected"), ("Error", "npc_lease_expired"), ("MayHaveDispatched", false)));
                        else { Cleared.Add(npcActive[Pack]); npcActive.Remove(Pack); }
                        break;
                    case "click":
                        if (N(c["TargetId"]) != 10 || S(H.Frame["UiToken"]) != "GameFieldDefaultUI") throw new Exception("Unexpected click: " + c);
                        H.Page("PackListUI");
                        break;
                    case "mainline_pack":
                        Pack = N(c["Value"]); MapId = Pack == 1 ? 1 : 21; Scene();
                        break;
                    case "mainline_walk":
                        if (WalkingBlocked && N(c["Value"]) >= 1000)
                        {
                            long id = N(c["Value"]) - 1000;
                            if (id == 101)
                                H.Box.Values["live:receipts~" + S(c["Id"]) + ".json"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                                    O(("Command", c), ("State", "rejected"), ("Error", "travel_unreachable:path_incomplete"), ("MayHaveDispatched", false)));
                            else { Picked.Add(id); H.Event("mainline.pickup", "request"); H.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject())); }
                            break;
                        }
                        MapId = N(c["Value"]); Visits.Add(MapId); Scene();
                        break;
                    case "mainline_menu":
                        if (!(Restricted && N(c["Value"]) == 6) && !(AbsorbDrops && N(c["Value"]) == 4) && N(c["Value"]) != 3) throw new Exception("Unexpected talent activation");
                        H.Page("QuickMenuUI");
                        break;
                    case "mainline_talent":
                        if (N(c["Value"]) == 305)
                        {
                            Suppressed.Add(MapId); MonstersPending.Remove(MapId);
                            H.Event("dispatch.start", "request"); H.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", 305), ("useCount", Suppressed.Count))));
                            Scene(); break;
                        }
                        if (!AbsorbDrops || N(c["Value"]) != 405) throw new Exception("Unexpected absorption");
                        Used++; MapOnePending = false;
                        if (!LoseAbsorbReply)
                        {
                            H.Event("dispatch.start", "request");
                            H.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", 405), ("useCount", Used))));
                        }
                        Scene(); break;
                    case "back": Scene(); break;
                    default: throw new Exception("Unexpected weekly command: " + c);
                }
            };
        }
        void Scene()
        {
            H.Frame["Scene"] = $"Map{Pack:0000}_{MapId:000}";
            H.Page("GameFieldDefaultUI", "_buttonPackList");
        }
        JsonObject Network(JsonObject command)
        {
            var targets = new JsonArray();
            if (Pending && Pack == 1)
            {
                if (MapOnePending) targets.Add(O(("Id", 101), ("Map", 1)));
                if (WalkingBlocked && !Picked.Contains(103)) targets.Add(O(("Id", 103), ("Map", 1)));
                if (MapTwoPending && !Visits.Contains(2)) targets.Add(O(("Id", 102), ("Map", 2)));
            }
            if (LaterDrop && Pack == 2) targets.Add(O(("Id", 122), ("Map", 22)));
            return O(("State", "ready"), ("Error", ""), ("Query", command["Id"]), ("Pack", Pack),
                ("Maps", command["Items"]), ("Week", NetworkWeek), ("Now", 100), ("Rewards", "[]"),
                ("Monsters", Array(MonstersPending.Select(m => O(("monsterId", 9000 + m), ("groupId", 1), ("activeFlag", true), ("respawnTime", 0)))).ToJsonString()),
                ("Drops", Array(Rows(targets).Where(t => command["Items"]!.AsArray().Any(m => N(m) == N(t["Map"]))))),
                ("Targets", Array(MonstersPending.Where(m => command["Items"]!.AsArray().Any(id => N(id) == m)).Select(m => O(("Id", 9000 + m), ("Map", m), ("Group", 1))))),
                ("StealReady", S(command["Kind"]) == "collection_query_steal"), ("Steals", "[]"), ("StealTargets", new JsonArray()));
        }
        JsonObject[] Readings()
        {
            var maps = Pack == 1 ? new long[] { 1, 2, 3 } : [21, 22, 23];
            var rows = new List<JsonObject>
            {
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", Pack), (DailyTravel.CurrentPack + ".PackType", 0)),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500)),
                WorkflowCases.Reading("mainline.owned_packs", ("Count", 2), ("$items", new JsonArray(O(("Key", 1), ("Value.Id", 1)), O(("Key", 2), ("Value.Id", 2))))),
                WorkflowCases.Reading("mainline.packs", ("Count", 2), ("$items", new JsonArray(O(("Id", 1), ("PackType", 0)), O(("Id", 2), ("PackType", 0))))),
                WorkflowCases.Reading("mainline.map", (MapPath, O(("id", MapId), ("packId", Pack))), ("HasCanOverwhelmMonster()", MonstersPending.Contains(MapId))),
                WorkflowCases.Reading("mainline.talent_counts", ("Values", new JsonArray(O(("groupId", 405), ("useCount", Used)), O(("groupId", 305), ("useCount", Suppressed.Count))))),
                WorkflowCases.Reading("mainline.research", ("$self", O(("Active", !Restricted), ("Remaining", 100)))),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", Restricted ? new JsonArray(O(("Kind", 6), ("Group", 605), ("Instance", 605), ("PackRestricted", true), ("Reason", "character_pack_restricted"))) : AbsorbDrops ? new JsonArray(O(("Kind", 4), ("Group", 405), ("Instance", 405), ("PackRestricted", false), ("Reason", ""), ("Cooldown", 0), ("Gate", ""))) : new JsonArray())),
                WorkflowCases.Reading("mainline.network", ("$self", network)),
                WorkflowCases.Reading("mainline.travel", ("$self", O(("Pack", Pack), ("Map", MapId), ("Ground", true),
                    ("Maps", Array(maps.Select(id => O(("Id", id), ("Exits", Reachable && Pack == 1 && id == 1 ? new[] { 2 } : []), ("HasWaypoint", false), ("CanSummon", false))))),
                    ("Approaches", Reachable && Pack == 1 && MapId == 1 ? new JsonArray(O(("Instance", 2), ("Reachable", true), ("Distance", 1), ("Kind", "gate"), ("Destination", 2))) : new JsonArray()))))
            };
            if (Reachable && Pack == 1 && MapId == 1)
            {
                var gate = WorkflowCases.Reading("mainline.gate", ("gameObject.name", "Gate_test"), (ObjectPath, 2), (DestinationPath, 2));
                gate["InstanceId"] = 2;
                rows.Add(gate);
            }
            if (NpcEnabled) rows.Add(WorkflowCases.Reading("weekly_npc.native", ("$self", NpcState())));
            if (MonstersPending.Contains(MapId))
            {
                rows.RemoveAll(r => S(r["Id"]) == "mainline.talent_rows");
                rows.Add(WorkflowCases.Reading("mainline.talent_rows", ("$self", new JsonArray(O(("Kind", 3), ("Group", 305), ("Instance", 305), ("PackRestricted", false), ("Reason", ""), ("Cooldown", 0), ("Gate", ""))))));
                var suppress = WorkflowCases.Reading("dispatch.talent", ("ὬὩὬὡὯὮὨὣὩὦὡ", "Overwhelm"),
                    (SkillPath, O(("id", 3055), ("groupId", 305), ("valueList", new JsonArray(21, 0)), ("catalystValue", 0))), ("_objDisableImage.activeSelf", false));
                suppress["InstanceId"] = 305; rows.Add(suppress);
            }
            if (AbsorbDrops)
            {
                var talent = WorkflowCases.Reading("dispatch.talent", ("ὬὩὬὡὯὮὨὣὩὦὡ", "FieldRewardAbsorb"),
                    (SkillPath, O(("id", 4055), ("groupId", 405), ("valueList", new JsonArray(21, 0)), ("catalystValue", 0))), ("_objDisableImage.activeSelf", false));
                talent["InstanceId"] = 405; rows.Add(talent);
                if (MapId == 1 && (MapOnePending || StaleDrop))
                {
                    var reward = WorkflowCases.Reading("mainline.reward", (ObjectPath, 101), (DropAvailablePath, true),
                        ("ὯὩὮὩὮὣὯὫὮὡὧ", true), ("ὦὫὧὯὥὫὦὪὣὦὪ", false));
                    reward["InstanceId"] = 1101; rows.Add(reward);
                }
            }
            if (WalkingBlocked && Pack == 1 && MapId == 1)
                foreach (long id in new[] { 101L, 103L })
                {
                    var reward = WorkflowCases.Reading("mainline.reward", (ObjectPath, id), (DropAvailablePath, !Picked.Contains(id)),
                        ("ὯὩὮὩὮὣὯὫὮὡὧ", true), ("ὦὫὧὯὥὫὦὪὣὦὪ", false),
                        ("transform.position.x", id - 100), ("transform.position.y", 0), ("transform.position.z", 0));
                    reward["InstanceId"] = id + 1000; rows.Add(reward);
                }
            return rows.ToArray();
        }
        JsonObject NpcState()
        {
            JsonObject Quest(long id, long pack, long gold, int kind = 2) => O(("id", id), ("packId", pack), ("mapId", pack == 1 ? 1 : 21),
                ("conditionType", kind), ("conditionCount", 1), ("magicValue", new[] { 100 }),
                ("rewardType", new[] { 4 }), ("rewardId", new[] { 0 }), ("rewardCount", new[] { gold }));
            var quests = new[] { Quest(10, 1, 100), Quest(20, 1, 20), Quest(30, 2, 90), Quest(40, 2, 80) }.ToList();
            if (HuntingOffer) quests.Add(Quest(50, 2, 1000, 1));
            long current = npcActive.GetValueOrDefault(Pack);
            return O(("State", QueryFailsFirst && Pack == 1 ? "failed" : "ready"), ("Error", ""), ("Query", npcQuery), ("Pack", Pack), ("Map", MapId), ("Week", 500),
                ("Limit", 3), ("Remaining", 3 - Cleared.Count - npcActive.Count), ("Completed", Cleared.Count), ("ActionsSupported", true), ("CanAccept", true),
                ("Rows", Array(quests).ToJsonString()), ("Active", Array(npcActive.Values.Select(id => O(("id", id), ("objectId", new JsonArray())))).ToJsonString()),
                ("Posted", Array(quests.Select(q => q["id"]!))), ("Cleared", Cleared.ToArray()), ("Wild", new JsonArray()),
                ("Navigating", navigating), ("CarryId", 0), ("CarryObjects", new JsonArray()),
                ("Objects", current == 0 ? new JsonArray() : new JsonArray(O(("Quest", current), ("Instance", 300), ("Id", 100), ("Near", true), ("Ready", true)))),
                ("Boards", new JsonArray(O(("Map", MapId), ("Near", true), ("Instance", 71)))),
                ("Board", O(("Ui", S(H.Frame["UiToken"]) == "QuestBoardUI" ? 2 : 0), ("VisibleQuests", quests.Select(q => N(q["id"])).ToArray()),
                    ("PopupQuest", popup), ("PopupCanAccept", popup != 0))));
        }
        public Task<JsonObject> RunNpc()
        {
            NpcEnabled = true; H.Workflow.Settings.Weekly.Npc = true; H.Workflow.Settings.Weekly.Steal = true;
            return new DailyWeeklyRoute(H.Workflow).Run(["weekly_mainline", "weekly_npc", "weekly_steal"]);
        }
        public Task<JsonObject> Run() => new DailyWeeklyRoute(H.Workflow).Run(["weekly_mainline"]);
        public JsonObject InterruptedAbsorption(bool newProcess = true)
        {
            var op = H.Business.Create(H.Context, "dispatch.start", H.Evidence(), O(("kind", 4), ("pack", 1), ("map", 1), ("group", 405)),
                O(("ui", "QuickMenuUI"), ("operation", "mainline_talent"), ("value", 405)));
            op["state"] = "unknown";
            if (newProcess) op["before"]!["Frame"]!["ProcessId"] = 9876;
            H.Business.Save(op);
            return op;
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks.Add(message); }
        using (var f = new Fixture(Path.Combine(root, "weekly-cache-no-global-recheck")) { Pending = false, Used = 21 })
        {
            await f.Run();
            int queries = f.Queries.Count, switches = f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack");
            var again = await f.Run();
            Check(S(again["state"]) == "completed" && f.Queries.Count == queries
                && f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack") == switches,
                "completed same-week server maps skip all visits and queries even without absorption quota");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-theft-rerun")) { Pending = false })
        {
            f.H.Workflow.Settings.Weekly.Steal = true;
            await new DailyWeeklyRoute(f.H.Workflow).Run(["weekly_mainline", "weekly_steal"]);
            int queries = f.Queries.Count;
            // Force a later collection-only server read as if the user ran just
            // that selection, then restart the combined weekly queue.
            f.Pack = 1;
            f.H.Frame["Scene"] = "Map0001_001"; f.H.Page("GameFieldDefaultUI", "_buttonPackList");
            await new DailyCollectionProgress(f.H.Workflow).Query(1, [1, 2, 3], cache: false);
            var again = await new DailyWeeklyRoute(f.H.Workflow).Run(["weekly_mainline", "weekly_steal"]);
            Check(S(again["state"]) == "completed" && f.Queries.Count == queries + 1,
                "combined weekly rerun reuses theft and collection after a collection-only refresh");
            Check(S(f.H.Box.Commands.Last()["Kind"]) == "collection_query", "completed combined rerun issues no navigation or extra network input");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-adjacent-query")))
        {
            var route = new DailyFieldRoute(f.H.Workflow);
            await route.Progress.Query(1, [1, 2, 3]);
            await route.Progress.Query(1, [1, 2, 3]);
            Check(f.Queries.Count == 1, "adjacent route checks share the same fresh server reply");
            await f.H.Workflow.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
            await route.Progress.Query(1, [1, 2, 3]);
            Check(f.Queries.Count == 2, "any intervening input invalidates collection reply reuse");
            await route.Progress.Query(1, [1, 2, 3], cache: false);
            Check(f.Queries.Count == 3, "explicit recovery always sends a fresh server query");
            f.H.Time += 3;
            await route.Progress.Query(1, [1, 2, 3]);
            Check(f.Queries.Count == 4, "reply reuse expires without turning historical cache into current proof");
            route.Progress.Steal = true;
            await route.Progress.Query(1, [1, 2, 3]);
            Check(f.Queries.Count == 5, "collection-only reply cannot replace missing theft cooldown evidence");
        }
        foreach (bool remaining in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "walk-no-old-proof-"+remaining)) { AbsorbDrops=true, MapOnePending=remaining, Used=6 })
        {
            var op=f.H.Business.Create(f.H.Context,"mainline.pickup",f.H.Evidence(),O(("map",1),("object",101)),
                O(("ui","GameFieldDefaultUI"),("operation","mainline_walk"),("value",-999)));
            op["state"]="unknown"; op["events"]=new JsonArray();
            op["before"]!["Frame"]!["ProcessId"]=9876; op["cycle"]="four-days-ago";
            op["last_observation"]=op["before"]!.DeepClone(); op["last_observation"]!["Frame"]!["Scene"]="Map0001_002";
            f.H.Business.Save(op);
            var preflight=await f.H.Business.ReconcileAsync(f.H.Context);
            Check(B(preflight["unresolved"]![0]!["recover_in_stage"]),"old pickup without replies reaches map recovery after restart");
            var result=await f.Run();
            var saved=f.H.Business.Records(f.H.Context).Single(o=>S(o["id"])==S(op["id"]));
            Check(S(saved["state"])=="superseded" && !B(saved["reconciliation"]!["original_success_claimed"]) && saved["result"]==null,
                "server targets replace four-day-old changed-map pickup without fabricating its outcome");
            Check(f.QueryMaps[0].SequenceEqual([1L]) && f.Used==(remaining?7:6) && S(result["state"])=="completed"
                && f.H.Box.Commands.All(c=>N(c["Value"])!=-999),"recovery uses current remaining drops and never replays stale Unity pointer: "+remaining);
            Check(S(f.H.Box.Commands[0]["Kind"])=="mainline_cancel_nav","interrupted pickup movement is stopped before querying targets");
        }
        foreach (bool remaining in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "absorption-no-old-proof-" + remaining)) { AbsorbDrops = true, MapOnePending = remaining, Used = 6 })
        {
            var op = f.InterruptedAbsorption();
            op["scope"]!.AsObject().Remove("pack"); // Older releases retained this only in the before-map metadata.
            f.H.Business.Save(op);
            var preflight = await f.H.Business.ReconcileAsync(f.H.Context);
            Check(B(preflight["unresolved"]![0]!["recover_in_stage"]), "missing absorption reply delegates recovery to current map instead of blocking queue");
            var r = await f.Run();
            var saved = f.H.Business.Records(f.H.Context).Single(o => S(o["id"]) == S(op["id"]));
            Check(S(saved["state"]) == "superseded" && S(saved["reconciliation"]!["method"]) == "fresh_map_server_progress"
                && !B(saved["reconciliation"]!["original_success_claimed"]) && saved["after"] == null,
                "fresh map state replaces unknown absorption intent without inventing original success: " + remaining);
            Check(f.QueryMaps[0].SequenceEqual([1L]) && f.Used == (remaining ? 7 : 6) && S(r["state"]) == "completed",
                "automatic local recovery reads only affected map first and absorbs only remaining targets: " + remaining);
        }
        using (var f = new Fixture(Path.Combine(root, "absorption-live-lost-reply")) { AbsorbDrops = true, LoseAbsorbReply = true })
        {
            var r = await f.Run();
            Check(f.Used == 1 && S(r["state"]) == "completed" && f.H.Business.Records(f.H.Context).All(o => !DailyManagedBusiness.Pending(o)),
                "live absorption reply loss recovers from current map and finishes without a second cast or manual check");
        }
        using (var f = new Fixture(Path.Combine(root, "absorption-recovery-read-unavailable")) { AbsorbDrops = true, RecoveryQueryFails = true })
        {
            f.InterruptedAbsorption();
            var r = await f.Run();
            Check(f.Used == 0 && f.Visits.Contains(2) && f.Queries.Last() == 2 && S(r["state"]) == "partial",
                "unavailable local recovery skips that map and continues other maps and cartridges");
            Check(r["stages"]!["weekly_mainline"]!["deferred_locations"]!.AsArray().Count == 1,
                "unavailable affected map retains a concrete recoverable reason");
        }
        using (var f = new Fixture(Path.Combine(root, "absorption-recovery-stale-cache")) { Pending = false, Used = 21 })
        {
            await new DailyCollectionProgress(f.H.Workflow).Query(1, [1, 2, 3]);
            f.InterruptedAbsorption(); f.Queries.Clear(); f.QueryMaps.Clear();
            var r = await f.Run();
            Check(f.QueryMaps[0].SequenceEqual([1L]) && f.H.Business.Records(f.H.Context).All(o => !DailyManagedBusiness.Pending(o)),
                "completed local cache and exhausted quota do not suppress automatic fresh map recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "absorption-wrong-week")) { Pending = false, NetworkWeek = 501 })
        {
            f.InterruptedAbsorption();
            Exception? failure = null;
            try { await new DailyCollectionProgress(f.H.Workflow).Query(1, [1]); } catch (Exception e) { failure = e; }
            Check(failure is InvalidDataException && f.H.Business.Records(f.H.Context).All(DailyManagedBusiness.Pending),
                "fresh map recovery rejects a changed weekly cycle and leaves old intent intact");
        }
        using (var f = new Fixture(Path.Combine(root, "absorption-map-scope")))
        {
            f.InterruptedAbsorption();
            bool blocked = false;
            try { f.H.Business.Create(f.H.Context, "dispatch.start", f.H.Evidence(), O(("kind", 4), ("pack", 1), ("map", 1)), O(("operation", "mainline_talent"))); }
            catch (StageHostException e) when (e.Kind == "pending") { blocked = true; }
            Check(blocked, "pending absorption cannot be blindly repeated on its map before a fresh read");
            var other = f.H.Business.Create(f.H.Context, "dispatch.start", f.H.Evidence(), O(("kind", 4), ("pack", 1), ("map", 2)), O(("operation", "mainline_talent")));
            other["state"] = "rejected"; f.H.Business.Save(other);
            var movement = f.H.Business.Create(f.H.Context, "dispatch.start", f.H.Evidence(), O(("kind", 2), ("pack", 1), ("map", 1)), O(("operation", "mainline_talent")));
            Check(S(movement["state"]) == "prepared", "pending absorption does not lock unrelated map actions or movement talents");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-walk-one-unreachable")) { WalkingBlocked = true })
        {
            f.H.Workflow.Settings.Weekly.WalkCollect = true;
            var r = await f.Run(); var stage = r["stages"]!["weekly_mainline"]!;
            Check(f.Picked.SetEquals([103L]) && f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_walk" && N(c["Value"]) == 1101) == 1,
                "unreachable drop is attempted once while reachable drops on the same map still collect");
            Check(f.Visits.SequenceEqual([2L]) && f.Queries.Last() == 2 && S(stage["state"]) == "partial" && stage["pending_maps"]!.ToJsonString() == "[[1,1]]",
                "walking local path refusal continues the next map and cartridge without marking the blocked drop complete");
            Check(stage["deferred_locations"]![0]!["metadata"]!["targets"]![0]!["object"]!.GetValue<long>() == 101 &&
                f.H.Business.Records(f.H.Context).All(op => S(op["state"]) is "completed" or "rejected"),
                "weekly result retains the exact deferred target without an unknown transaction");
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack") == 1,
                "known unreachable pickup does not force cartridge reload or repeat 65 second waits");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-npc-local-failure")) { RejectNpc = true })
        {
            var r = await f.RunNpc(); var npc = r["stages"]!["weekly_npc"]!;
            Check(f.Visits.Contains(2) && f.Accepted.SequenceEqual([10L, 30L, 40L]), "stalled NPC pickup continues collection and NPC quests in another cartridge");
            Check(S(npc["state"]) == "partial" && npc["deferred_locations"]!.AsArray().Count == 1 && S(r["stages"]!["weekly_steal"]!["state"]) == "completed",
                "NPC deferral reports unfinished work independently from completed theft");
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_interact" && N(c["Value"]) == 10) == 3,
                "merged weekly route never restarts an exhausted NPC recovery loop");
        }
        foreach (bool hunting in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "weekly-npc-reward-order-" + hunting)) { Pending = false, HuntingOffer = hunting })
        {
            var r = await f.RunNpc();
            Check(f.Accepted.SequenceEqual([10L, 30L, 40L]) && S(r["state"]) == "completed", "global reward ordering crosses cartridges before accepting a cheaper quest; hunting offer=" + hunting);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_accept") == 3, "reward reranking respects three-quest quota; hunting offer=" + hunting);
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-npc-read-failure")) { Pending = false, QueryFailsFirst = true })
        {
            var r = await f.RunNpc();
            Check(f.Accepted.SequenceEqual([30L, 40L]) && S(r["stages"]!["weekly_mainline"]!["state"]) == "completed", "unavailable read-only NPC query defers its cartridge and continues other work");
            Check(r["stages"]!["weekly_npc"]!["deferred_locations"]!.AsArray().Count == 1, "read-only query failure remains visible as unfinished NPC work");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-map-isolation")))
        {
            var r = await f.Run(); var stage = r["stages"]!["weekly_mainline"]!;
            Check(f.Visits.SequenceEqual([2L]) && f.Queries.Last() == 2, "unresolved map continues another map and the next cartridge");
            Check(S(r["state"]) == "partial" && stage["pending_maps"]!.ToJsonString() == "[[1,1]]", "deferred map is never counted as collected");
            Check(stage["deferred_locations"]!.AsArray().Count == 1 && B(stage["deferred_locations"]![0]!["metadata"]!["reentry_attempted"]), "final weekly result retains location and reentry evidence");
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack") == 3, "one bounded map reentry never loops on a deferred target");
            f.Pending = false;
            var retry = await f.Run();
            Check(S(retry["state"]) == "completed" && retry["stages"]!["weekly_mainline"]!["deferred_locations"] == null, "a new invocation rechecks server progress without a persistent skip list");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-unreachable-pack")) { Reachable = false, MapOnePending = false })
        {
            var r = await f.Run(); var stage = r["stages"]!["weekly_mainline"]!;
            Check(f.Queries.Last() == 2 && S(r["state"]) == "partial", "unreachable cartridge does not stop remaining weekly collection");
            Check(stage["pending_maps"]!.ToJsonString() == "[[1,2]]" && stage["deferred_locations"]!.AsArray().Count == 1, "cartridge deferral preserves already completed maps");
            Check(Directory.GetFiles(Path.Combine(f.H.Root, "live", "diagnostics", "failures"), "*.json").Length > 0, "recoverable weekly failures remain in exported diagnostic evidence");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-restricted-pack")) { Restricted = true })
        {
            var r = await f.Run();
            Check(f.Queries.Last() == 2 && S(r["state"]) == "partial", "proven cartridge talent restrictions continue the next cartridge");
            Check(!f.H.Box.Commands.Any(c => S(c["Kind"]) == "mainline_talent") && r["stages"]!["weekly_mainline"]!["deferred_locations"]!.AsArray().Count == 2, "restricted cartridge defers without talent consumption or false completion");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-quota-stop")) { Used = 21 })
        {
            var r = await f.Run();
            Check(B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"]) && !f.Queries.Contains(2)
                && !f.H.Box.Commands.Any(c => S(c["Kind"]) == "mainline_talent"), "unfinished absorption stops collection without surveying later cartridges for suppression");
            int queries=f.Queries.Count;
            await f.Run();
            Check(f.Queries.Count==queries+1 && f.Queries.Last()==1 && f.Suppressed.Count==0, "same-day retry rechecks only the unfinished cartridge without starting a suppression sweep");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-quota-suppression")) { Used = 21 })
        {
            f.MonstersPending.UnionWith([1L, 2L, 21L]);
            var r = await f.Run();
            Check(f.Suppressed.Count==0 && !f.Queries.Contains(2), "zero absorption leaves mixed maps and later suppression maps for the next collection run");
            Check(B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"]) && f.Used == 21, "unfinished collection waits for tomorrow without spending more talents");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-last-absorption")) { Used = 20, AbsorbDrops = true, MapTwoPending = false })
        {
            f.MonstersPending.UnionWith([2L, 21L]);
            var r = await f.Run();
            Check(f.Used == 21 && f.Suppressed.Order().SequenceEqual(new long[] { 2, 21 }) && S(r["state"]) == "completed", "last absorption does not interrupt remaining suppression-only maps");
            int casts=f.Suppressed.Count,queries=f.Queries.Count;
            f.Used=0;f.MonstersPending.UnionWith([2L,21L]);
            var tomorrow=await f.Run();
            Check(S(tomorrow["state"])=="completed"&&f.Suppressed.Count==casts&&f.Queries.Count==queries,
                "daily talent reset does not restart completed same-week suppression");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-quota-later-drop")) { Used=21,Pending=false,LaterDrop=true })
        {
            f.MonstersPending.UnionWith([1L,21L]);
            var r=await f.Run();
            Check(f.Suppressed.Count==0&&f.Queries.Contains(2)&&B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"]),
                "an empty current cartridge is not proof that all weekly absorption is complete");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-quota-manual-completion")) { Used=21 })
        {
            await f.Run();
            f.Pending=false;f.MonstersPending.UnionWith([1L,21L]);
            var r=await f.Run();
            Check(S(r["state"])=="completed"&&f.Suppressed.Order().SequenceEqual(new long[]{1,21}),
                "fresh game progress overrides a previously unfinished cache and permits the verified suppression tail");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-quota-other-categories")) { Used=21 })
        {
            var r=await f.RunNpc();
            Check(B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"])&&S(r["stages"]!["weekly_npc"]!["state"])=="completed"
                &&S(r["stages"]!["weekly_steal"]!["state"])=="completed", "absorption stop does not cancel NPC quests or theft in the shared route");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-deferred-then-quota")))
        {
            f.BeforeQuery = () => { if (f.Visits.Contains(2)) f.Used = 21; };
            var r = await f.Run(); var stage = r["stages"]!["weekly_mainline"]!;
            Check(B(stage["waiting_daily_reset"]) && stage["deferred_locations"]!.AsArray().Count == 1 && stage["pending_maps"]!.AsArray().Count > 0,
                "quota stop retains earlier local failures and the unvisited map list");
            Check(!f.Queries.Contains(2) && !f.H.Box.Commands.Any(c => S(c["Kind"]) == "mainline_talent" && N(c["Value"]) / 100 == 4),
                "a local failure and exhausted absorption retain pending maps without a later suppression sweep");
        }
        foreach (Exception error in new Exception[] { new OperationCanceledException(), new StageHostException("identity", "account changed"), new DailyStepException("pending", "unknown talent receipt", true), new DailyStepException("deferred", "no matching cartridge proof"), new InvalidDataException("unrelated invalid state") })
        using (var f = new Fixture(Path.Combine(root, "weekly-fatal-" + error.GetType().Name + "-" + (error is DailyStepException step ? step.Kind : ""))))
        {
            f.BeforeQuery = () => throw error;
            Exception? failure = null;
            try { await f.Run(); } catch (Exception e) { failure = e; }
            Check(ReferenceEquals(failure, error) && !f.H.Box.Commands.Any(c => S(c["Kind"]) == "mainline_pack"), "weekly location isolation preserves global stop: " + error.GetType().Name + " / " + error.Message);
        }
    }
}

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
        public int AbsorptionLimit = 21, CaptureLimit = 21;
        public bool Pending = true, Restricted, Reachable = true, MapOnePending = true, MapTwoPending = true, LaterDrop;
        public readonly List<long> Queries = [], Visits = [];
        public readonly List<(string Stage, JsonObject Value)> Progress = [];
        private DailyWeeklyRoute Runner() => new(H.Workflow, (stage, value) => Progress.Add((stage, value.DeepClone().AsObject())));
        public Action? BeforeQuery;
        public Action<List<JsonObject>>? ChangeReadings;
        public bool NpcEnabled, RejectNpc, HuntingOffer, QueryFailsFirst;
        public bool WildQuestChain;
        public bool WalkingBlocked;
        public bool AbsorbDrops, LoseAbsorbReply, RecoveryQueryFails, StaleDrop;
        public long NetworkWeek = 500;
        public readonly List<long[]> QueryMaps = [];
        public readonly HashSet<long> Picked = [];
        public readonly List<long> Accepted = [], Cleared = [];
        public readonly HashSet<long> MonstersPending = [];
        public readonly HashSet<long> CapturesPending = [];
        public int CaptureUsed = 21;
        public readonly List<long> Suppressed = [];
        readonly Dictionary<long, long> npcActive = [];
        string npcQuery = "prior"; long popup, plannedQuest; bool navigating;
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
                    case "weekly_npc_plan": plannedQuest = N(c["Value"]); navigating = false; break;
                    case "weekly_npc_nav":
                        navigating = true;
                        if(WildQuestChain){
                            if(N(c["Value"])==11||MapId!=1)throw new Exception("Native guide attempted hunting/cross-map movement");
                            H.Page("BalloonScriptUI","_objTouchButton");
                        }
                        break;
                    case "weekly_npc_talk":
                        if(!WildQuestChain)throw new Exception("Unexpected NPC dialogue");
                        if(npcActive[Pack]==10)npcActive[Pack]=11;
                        else {Cleared.Add(12);npcActive.Remove(Pack);}
                        Scene();break;
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
                            if(WildQuestChain&&MapId==2&&npcActive.GetValueOrDefault(Pack)==11)npcActive[Pack]=12;
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
        public void Scene()
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
                ("Monsters", Array(MonstersPending.Select(m => O(("monsterId", 9000 + m), ("groupId", 1), ("activeFlag", true), ("respawnTime", 0)))
                    .Concat(CapturesPending.Select(m => O(("monsterId", 4000 + m), ("groupId", 4), ("activeFlag", true), ("respawnTime", 0))))).ToJsonString()),
                ("Drops", Array(Rows(targets).Where(t => command["Items"]!.AsArray().Any(m => N(m) == N(t["Map"]))))),
                ("Targets", Array(MonstersPending.Where(m => command["Items"]!.AsArray().Any(id => N(id) == m)).Select(m => O(("Id", 9000 + m), ("Map", m), ("Group", 1)))
                    .Concat(CapturesPending.Where(m => command["Items"]!.AsArray().Any(id => N(id) == m)).Select(m => O(("Id", 4000 + m), ("Map", m), ("Group", 4), ("Type", 2)))))),
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
                WorkflowCases.Reading("mainline.talent_counts", ("Values", new JsonArray(O(("groupId", 405), ("useCount", Used)), O(("groupId", 305), ("useCount", Suppressed.Count)), O(("groupId", 2005), ("useCount", CaptureUsed))))),
                WorkflowCases.Reading("mainline.talent_limits", ("$self", new JsonArray(O(("Kind", 4), ("Group", 405), ("Limit", AbsorptionLimit)), O(("Kind", 20), ("Group", 2005), ("Limit", CaptureLimit))))),
                // Route isolation cases run with an existing protection effect;
                // activation/expiry/waypoint rules are covered by EventCollection.
                WorkflowCases.Reading("mainline.durations", ("effects", new JsonArray(O(("Key",17),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",true),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",100000-H.Time))))),
                WorkflowCases.Reading("mainline.research", ("$self", O(("Active", !Restricted), ("Remaining", 100)))),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", Restricted ? new JsonArray(O(("Kind", 6), ("Group", 605), ("Instance", 605), ("PackRestricted", true), ("Reason", "character_pack_restricted"))) : AbsorbDrops ? new JsonArray(O(("Kind", 4), ("Group", 405), ("Instance", 405), ("PackRestricted", false), ("Reason", ""), ("Cooldown", 0), ("Gate", ""))) : new JsonArray())),
                WorkflowCases.Reading("mainline.network", ("$self", network)),
                WorkflowCases.Reading("mainline.travel", ("$self", O(("Pack", Pack), ("Map", MapId), ("Ground", true),
                    ("Maps", Array(maps.Select(id => O(("Id", id), ("Exits", Reachable && Pack == 1 ? id == 1 ? new[] { 2 } : WildQuestChain && id == 2 ? new[] { 1 } : [] : []), ("HasWaypoint", false), ("CanSummon", false))))),
                    ("Approaches", Reachable && Pack == 1 && (MapId == 1 || WildQuestChain && MapId == 2) ? new JsonArray(O(("Instance", MapId == 1 ? 2 : 1), ("Reachable", true), ("Distance", 1), ("Kind", "gate"), ("Destination", MapId == 1 ? 2 : 1))) : new JsonArray()))))
            };
            if (Reachable && Pack == 1 && (MapId == 1 || WildQuestChain && MapId == 2))
            {
                int destination = MapId == 1 ? 2 : 1;
                var gate = WorkflowCases.Reading("mainline.gate", ("gameObject.name", "Gate_test"), (ObjectPath, destination), (DestinationPath, destination));
                gate["InstanceId"] = destination;
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
                    (SkillPath, O(("id", 4055), ("groupId", 405), ("valueList", new JsonArray(AbsorptionLimit, 0)), ("catalystValue", 0))), ("_objDisableImage.activeSelf", false));
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
            ChangeReadings?.Invoke(rows);
            return rows.ToArray();
        }
        JsonObject NpcState()
        {
            JsonObject Quest(long id, long pack, long gold, int kind = 2) => O(("id", id), ("packId", pack), ("mapId", pack == 1 ? 1 : 21),
                ("conditionType", kind), ("conditionCount", 1), ("magicValue", new[] { 100 }),
                ("rewardType", new[] { 4 }), ("rewardId", new[] { 0 }), ("rewardCount", new[] { gold }));
            var quests = new[] { Quest(10, 1, 100), Quest(20, 1, 20), Quest(30, 2, 90), Quest(40, 2, 80) }.ToList();
            if(WildQuestChain){
                quests=[Quest(10,1,0,19),Quest(11,1,0,1),Quest(12,1,100,19)];
                quests[0]["nextQuestId"]=11;
                quests[1]["priorQuestId"]=10;quests[1]["nextQuestId"]=12;quests[1]["mapId"]=9;
                quests[2]["priorQuestId"]=11;
            }
            if (HuntingOffer) quests.Add(Quest(50, 2, 1000, 1));
            long current = npcActive.GetValueOrDefault(Pack);
            return O(("State", QueryFailsFirst && Pack == 1 ? "failed" : "ready"), ("Error", ""), ("Query", npcQuery), ("Pack", Pack), ("Map", MapId), ("Week", 500),
                ("Limit", WildQuestChain?1:3), ("Remaining", (WildQuestChain?1:3) - Cleared.Count - npcActive.Count), ("Completed", Cleared.Count), ("ActionsSupported", true), ("CanAccept", true),
                ("Rows", Array(quests).ToJsonString()), ("Active", Array(npcActive.Values.Select(id => O(("id", id), ("objectId", new JsonArray())))).ToJsonString()),
                ("Posted", Array(quests.Where(q=>N(q["priorQuestId"])==0).Select(q => q["id"]!))), ("Cleared", Cleared.ToArray()),
                ("Wild", WildQuestChain?new JsonArray(O(("Step",11),("Map",2),("Group",1),("Monster",101),("Count",1),("Available",MonstersPending.Contains(2)))):new JsonArray()),
                ("Navigating", navigating), ("NavigationPlannedQuest", plannedQuest), ("CarryId", 0), ("CarryObjects", new JsonArray()),
                ("Objects", current == 0 ? new JsonArray() : new JsonArray(O(("Quest", current), ("Instance", 300), ("Id", 100), ("Near", true), ("Ready", true)))),
                ("Boards", new JsonArray(O(("Map", MapId), ("Near", true), ("Instance", 71)))),
                ("Board", O(("Ui", S(H.Frame["UiToken"]) == "QuestBoardUI" ? 2 : 0), ("VisibleQuests", quests.Select(q => N(q["id"])).ToArray()),
                    ("PopupQuest", popup), ("PopupCanAccept", popup != 0))));
        }
        public Task<JsonObject> RunNpc()
        {
            NpcEnabled = true; H.Workflow.Settings.Weekly.Npc = true; H.Workflow.Settings.Weekly.Steal = true;
            return Runner().Run(["weekly_mainline", "weekly_npc", "weekly_steal"]);
        }
        public Task<JsonObject> RunWildQuest(){
            NpcEnabled=WildQuestChain=true;Pending=false;npcActive[1]=10;MonstersPending.Add(2);
            H.Workflow.Settings.Weekly.Npc=true;
            return new DailyWeeklyRoute(H.Workflow).Run(["weekly_npc"]);
        }
        public Task<JsonObject> Run() => Runner().Run(["weekly_mainline"]);
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
        using (var f = new Fixture(Path.Combine(root, "collection-map-empty")))
        {
            f.H.Workflow.Settings.Weekly.CollectionMaps = [];
            f.InterruptedAbsorption();
            var result = await f.Run();
            Check(S(result["stages"]?["weekly_mainline"]?["state"]) == "skipped" && f.H.Box.Commands.Count == 0,
                "empty selection skips collection without queries, travel or pending-operation recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-excluded-recovery")))
        {
            f.H.Workflow.Settings.Weekly.CollectionMaps = [21];
            f.InterruptedAbsorption();
            var result = await f.Run();
            Check(S(result["stages"]?["weekly_mainline"]?["state"]) == "completed" && f.Used == 0 && !f.Queries.Contains(1)
                && f.QueryMaps.All(m => m.SequenceEqual([21L])) && !f.Visits.Contains(1),
                "unchecked pending map cannot pull selection back into another cartridge");
            Check(S(result["stages"]?["weekly_mainline"]?["detail"]).Contains("1/1"),
                "collection progress counts only selected accessible maps");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-unowned")))
        {
            f.H.Workflow.Settings.Weekly.CollectionMaps = [10011];
            var result = await f.Run();
            Check(S(result["stages"]?["weekly_mainline"]?["state"]) == "skipped" && f.Queries.Count == 0 && f.Visits.Count == 0,
                "unowned map selection does not expand to owned unselected cartridges");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-absorb")) { AbsorbDrops = true })
        {
            f.H.Workflow.Settings.Weekly.CollectionMaps = [1];
            var result = await f.Run();
            Check(f.Used == 1 && !f.Visits.Contains(2) && !f.Queries.Contains(2)
                && f.QueryMaps.All(m => m.SequenceEqual([1L])) && S(result["stages"]?["weekly_mainline"]?["state"]) == "completed",
                "absorption and post-action queries stay within selected maps");
            f.H.Workflow.Settings.Weekly.CollectionMaps = [1, 2];
            f.MapTwoPending = false;
            await f.Run();
            Check(f.QueryMaps.Any(m => m.Contains(2)), "expanding map selection reads the newly included map after a completed run");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-walk")) { WalkingBlocked = true, MapOnePending = false })
        {
            f.H.Workflow.Settings.Weekly.WalkCollect = true;
            f.H.Workflow.Settings.Weekly.CollectionMaps = [1];
            var result = await f.Run();
            Check(f.Picked.Contains(103) && !f.Visits.Contains(2) && !f.Queries.Contains(2) && f.Used == 0
                && S(result["stages"]?["weekly_mainline"]?["state"]) == "completed",
                "walking collection collects selected targets without spending absorption or visiting unchecked maps");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-independent-weeklies")) { Pending = false })
        {
            f.H.Workflow.Settings.Weekly.CollectionMaps = [];
            var result = await f.RunNpc();
            Check(S(result["stages"]?["weekly_mainline"]?["state"]) == "skipped" && f.Accepted.Count > 0
                && S(result["stages"]?["weekly_steal"]?["state"]) == "completed",
                "empty collection selection preserves independent NPC and theft execution");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-completed-categories")) { Pending = false })
        {
            f.Cleared.AddRange([10L, 20L, 30L]);
            f.MonstersPending.UnionWith([1L, 2L, 21L]);
            var result = await f.RunNpc();
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_query") == 1
                && f.Accepted.Count == 0 && S(result["stages"]?["weekly_npc"]?["state"]) == "completed",
                "live full NPC quota stops per-map queries without accepting more quests");
            Check(f.Suppressed.SequenceEqual([1L, 2L, 21L])
                && S(result["stages"]?["weekly_mainline"]?["state"]) == "completed",
                "completed NPC quota still allows all remaining collection maps to run");
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "collection_query_steal") == 2
                && S(result["stages"]?["weekly_steal"]?["state"]) == "completed",
                "live completed theft is checked once per cartridge instead of after every collection action");
            var commands = f.H.Box.Commands.ToArray();
            var suppression = commands.Select((c, i) => (c, i)).Where(x => S(x.c["Kind"]) == "mainline_talent" && N(x.c["Value"]) == 305);
            Check(suppression.All(x => commands.Skip(x.i + 1)
                .TakeWhile(c => S(c["Kind"]) is not ("mainline_walk" or "mainline_pack"))
                .Count(c => S(c["Kind"]).StartsWith("collection_query", StringComparison.Ordinal)) == 1),
                "combined route reuses each collection post-action reply instead of querying it again");
        }
        foreach (string page in new[] { "MapRewardInfoPopupUI", "PackListUI", "MenuUI" })
        {
            using var f = new Fixture(Path.Combine(root, "collection-overlay-" + page)) { Pending = false };
            f.H.Page(page, "_objBackButton");
            var original = f.H.OnCommand;
            f.H.OnCommand = c => { if (S(f.H.Frame["UiToken"]) == page && S(c["Kind"]) is "back" or "click") f.Scene(); else original!(c); };
            await new DailyFieldRoute(f.H.Workflow).Progress.Query(1, [1, 2, 3]);
            Check(f.H.Box.Commands.Count == 2 && S(f.H.Box.Commands[0]["Kind"]) is "back" or "click" && f.Queries.Count == 1,
                "collection returns from known overlay before its read-only server query: " + page);
        }
        using (var f = new Fixture(Path.Combine(root, "collection-map-changed")) { Pending = false })
        {
            f.BeforeQuery = () => { f.MapId = 4; f.Scene(); };
            bool replanned = false;
            try { await new DailyFieldRoute(f.H.Workflow).Gather(expectedMap: 1); }
            catch (DailyCollectionLocationChanged) { replanned = true; }
            Check(replanned && f.H.Box.Commands.All(c => S(c["Kind"]) == "collection_query"),
                "map change during collection query replans before null lookup or talent use");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-unknown-popup")))
        {
            f.H.Page("MessagePopupUI", "_buttonOK");
            bool stopped = false;
            try { await new DailyFieldRoute(f.H.Workflow).FieldReady(timeout: 1, settle: 0); }
            catch (StageHostException) { stopped = true; }
            Check(stopped && f.H.Box.Commands.Count == 0, "collection overlay recovery never confirms an unrelated message");
        }
        using (var f = new Fixture(Path.Combine(root, "collection-capture-limit")) { Pending = false })
        {
            f.CapturesPending.UnionWith([1L, 2L]); f.MonstersPending.Add(21);
            var result = await f.Run();
            var deferred = Rows(result["stages"]?["weekly_mainline"]?["deferred_locations"]);
            Check(deferred.Length == 2 && deferred.All(d => S(d["metadata"]?["reason"]) == "capture_daily_limit"),
                "spent capture quota keeps actual targets pending with an explicit next-day reason");
            Check(f.Suppressed.SequenceEqual([21L]) && !f.Visits.Contains(2) && f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_pack") == 1,
                "spent capture quota skips capture-only travel without preventing later cartridge suppression");
        }
        using(var f=new Fixture(Path.Combine(root,"weekly-wild-quest-chain"))){
            var result=await f.RunWildQuest();
            Check(S(result["stages"]?["weekly_npc"]?["state"])=="completed"&&f.Cleared.SequenceEqual([12L]),
                "dialogue -> regular-map suppression -> return dialogue completes the NPC chain");
            Check(f.Suppressed.SequenceEqual([2L])&&!f.Visits.Contains(9),"hunting guide destination is never visited or used for suppression");
            Check(f.H.Box.Commands.Where(c=>S(c["Kind"])=="weekly_npc_nav").Select(c=>N(c["Value"])).SequenceEqual([10L,12L]),
                "native navigation handles only the two local dialogue steps, never the intervening kill guide");
        }
        foreach (bool arrivedDuringFailure in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "weekly-recovery-quest-priority-" + arrivedDuringFailure)))
        {
            bool rejected = false; string quickState = "idle";
            JsonArray Shortcuts() => new(O(("Npc", 700), ("Map", 2), ("Name", "test safe landing"), ("AtTarget", f.MapId == 2)));
            f.ChangeReadings = rows =>
            {
                var travelValue = Rows(rows.Single(r => S(r["Id"]) == "mainline.travel")["Values"]).Single(v => S(v["Path"]) == "$self");
                var travel = JsonNode.Parse(S(travelValue["Json"]))!.AsObject(); travel["QuickTargets"] = Shortcuts();
                travelValue["Json"] = travel.ToJsonString();
                rows.Add(WorkflowCases.Reading("route.quick", ("$self", O(("Pack", f.Pack), ("Map", f.MapId),
                    ("Npc", 700), ("State", quickState), ("Targets", Shortcuts())))));
            };
            var normal = f.H.OnCommand;
            f.H.OnCommand = command =>
            {
                if (S(command["Kind"]) == "mainline_walk" && N(command["Value"]) == 2 && !rejected)
                {
                    rejected = true;
                    if (arrivedDuringFailure) { f.MapId = 2; f.Visits.Add(2); f.Scene(); }
                    f.H.Box.Values["live:receipts~" + S(command["Id"]) + ".json"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        O(("Command", command), ("State", "rejected"), ("Error", "travel_unreachable:path_incomplete"), ("MayHaveDispatched", false)));
                    return;
                }
                if (S(command["Kind"]) == "field_quick_travel")
                { f.MapId = 2; f.Visits.Add(2); quickState = "arrived"; f.Scene(); return; }
                normal!(command);
            };
            var result = await f.RunWildQuest();
            var commands = f.H.Box.Commands;
            int dialogue = commands.FindIndex(c => S(c["Kind"]) == "weekly_npc_talk");
            int suppression = commands.FindIndex(c => S(c["Kind"]) == "mainline_talent" && N(c["Value"]) == 305);
            Check(rejected && S(result["stages"]?["weekly_npc"]?["state"]) == "completed" && f.Cleared.SequenceEqual([12L])
                && f.Suppressed.SequenceEqual([2L]) && dialogue >= 0 && suppression > dialogue,
                "failed travel still completes dialogue prerequisite, permitted suppression and return dialogue in order: " + arrivedDuringFailure);
            Check(commands.Count(c => S(c["Kind"]) == "field_quick_travel") == (arrivedDuringFailure ? 0 : 1)
                && !commands.Any(c => S(c["Kind"]) == "mainline_menu" && N(c["Value"]) == 17),
                "recovery hands arrival back to the NPC job before considering unnecessary movement or stealth: " + arrivedDuringFailure);
        }
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
            f.H.Workflow.Settings.Weekly.NpcHunting = true; // A stale in-memory preference must not accept hunting-only offers.
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
        foreach (int limit in new[] { 7, 14, 21, 28 })
        {
            using (var f = new Fixture(Path.Combine(root, $"weekly-account-quota-{limit}")) { Used = limit, AbsorptionLimit = limit })
            {
                var r = await f.RunNpc();
                var stage = r["stages"]!["weekly_mainline"]!;
                Check(B(stage["waiting_daily_reset"]) && N(stage["talent_used"]) == limit && N(stage["talent_limit"]) == limit
                    && f.Suppressed.Count == 0 && f.Visits.Count == 0,
                    $"account cap {limit} stops unfinished absorption without visiting collection maps");
                Check(S(r["stages"]!["weekly_npc"]!["state"]) == "completed" && S(r["stages"]!["weekly_steal"]!["state"]) == "completed",
                    $"account cap {limit} does not block other weekly categories");
            }
            using (var f = new Fixture(Path.Combine(root, $"weekly-last-account-use-{limit}")) { Used = limit - 1, AbsorptionLimit = limit, AbsorbDrops = true, MapTwoPending = false })
            {
                f.MonstersPending.UnionWith([2L, 21L]);
                var r = await f.Run();
                Check(f.Used == limit && S(r["state"]) == "completed" && f.Suppressed.Order().SequenceEqual(new long[] { 2, 21 }),
                    $"last use at cap {limit} finishes the proven suppression tail");
                var counts = f.Progress.Where(p => p.Stage == "weekly_mainline" && S(p.Value["unit"]) == "absorptions").Select(p => p.Value).ToArray();
                Check(counts.Length > 1 && counts.All(p => N(p["total"]) == 1) && N(counts[^1]["completed"]) == 1,
                    $"account cap {limit} drives progress from the one remaining absorption");
            }
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-account-quota-reached")) { Used = 13, AbsorptionLimit = 14, AbsorbDrops = true })
        {
            var r = await f.Run();
            Check(f.Used == 14 && B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"]) && f.Visits.Count == 0 && !f.Queries.Contains(2),
                "14th absorption leaves pending maps for tomorrow without moving, reentering or surveying another cartridge");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-account-capture-cap")) { Pending = false, CaptureUsed = 14, CaptureLimit = 14 })
        {
            f.CapturesPending.UnionWith([1L, 2L, 21L]);
            var r = await f.Run();
            Check(f.Visits.Count == 0 && !f.H.Box.Commands.Any(c => S(c["Kind"]) == "mainline_talent")
                && r["stages"]!["weekly_mainline"]!["deferred_locations"]!.AsArray().Count == 3,
                "capture also respects a 14-use account cap without visiting unavailable targets");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-no-absorption")) { AbsorptionLimit = 0 })
        {
            var r = await f.Run();
            Check(S(r["stages"]!["weekly_mainline"]!["reason"]) == "absorption_unavailable"
                && !B(r["stages"]!["weekly_mainline"]!["waiting_daily_reset"]) && f.Visits.Count == 0,
                "an account without absorption gets an actionable reason instead of a false next-day reset");
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
            var counts = f.Progress.Where(p => p.Stage == "weekly_mainline" && S(p.Value["unit"]) == "absorptions").Select(p => p.Value).ToArray();
            Check(counts.Length > 1 && N(counts[0]["completed"]) == 0 && counts.All(p => N(p["total"]) == 1)
                && N(counts[^1]["completed"]) == 1, "weekly progress budgets only the last available use, retaining it through suppression tail");
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
        foreach (string failure in new[] { "missing-limits", "missing-counts", "native-error", "invalid-json", "invalid-limit", "late-counts", "after-absorption" })
        using (var f = new Fixture(Path.Combine(root, "weekly-talent-" + failure)))
        {
            void BreakReadings(List<JsonObject> readings)
            {
                if (failure == "after-absorption" && f.Used == 0) return;
                var limits = readings.Single(r => S(r["Id"]) == "mainline.talent_limits");
                switch (failure)
                {
                    case "missing-limits": readings.Remove(limits); break;
                    case "missing-counts": case "late-counts": readings.RemoveAll(r => S(r["Id"]) == "mainline.talent_counts"); break;
                    case "native-error": case "after-absorption": limits["Error"] = "TalentSkill unavailable: fixture"; break;
                    case "invalid-json": limits["Values"]![0]!["Json"] = "{"; break;
                    case "invalid-limit": limits["Values"]![0]!["Json"] = "[{\"Kind\":4,\"Group\":405}]"; break;
                }
            }
            if (failure == "after-absorption") f.AbsorbDrops = true;
            if (failure == "late-counts") f.BeforeQuery = () => f.ChangeReadings = BreakReadings;
            else f.ChangeReadings = BreakReadings;
            var result = await f.RunNpc();
            var collection = result["stages"]!["weekly_mainline"]!;
            Check(S(collection["state"]) == "partial" && S(collection["reason"]) == "talent_data_unavailable"
                && !B(collection["waiting_daily_reset"]) && collection["pending_maps"]!.AsArray().Count > 0
                && collection["talent_observation"] is JsonObject,
                "unavailable talent data retains collection pending state and specific observations: " + failure);
            Check(S(result["stages"]!["weekly_npc"]!["state"]) == "completed" && f.Cleared.Count == 3
                && S(result["stages"]!["weekly_steal"]!["state"]) == "completed"
                && f.H.Box.Commands.Count(c => S(c["Kind"]) == "mainline_talent" && N(c["Value"]) / 100 is 4 or 20) == (failure == "after-absorption" ? 1 : 0),
                "talent read failure does not cancel NPC/theft or spend unknown allowance: " + failure);
            f.ChangeReadings = null; f.BeforeQuery = null; f.AbsorbDrops = true; f.MapTwoPending = false;
            var retry = await f.Run();
            Check(S(retry["stages"]!["weekly_mainline"]!["state"]) == "completed" && f.Used == 1,
                "restored readings allow collection to run without clearing the previous failure: " + failure);
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-walking-no-talent-data")) { Pending = false })
        {
            f.H.Workflow.Settings.Weekly.WalkCollect = true;
            f.ChangeReadings = rows => rows.RemoveAll(r => S(r["Id"]) is "mainline.talent_counts" or "mainline.talent_limits");
            var r = await f.Run();
            Check(S(r["state"]) == "completed", "walking collection without capture targets does not require unused talent allowance");
        }
        using (var f = new Fixture(Path.Combine(root, "weekly-capture-data-unavailable")) { Pending = false })
        {
            f.H.Workflow.Settings.Weekly.WalkCollect = true; f.CapturesPending.Add(1);
            f.ChangeReadings = rows => rows.RemoveAll(r => S(r["Id"]) == "mainline.talent_limits");
            var r = await f.RunNpc();
            Check(S(r["stages"]!["weekly_mainline"]!["reason"]) == "talent_data_unavailable"
                && N(r["stages"]!["weekly_mainline"]!["talent_observation"]!["kind"]) == 20
                && S(r["stages"]!["weekly_npc"]!["state"]) == "completed" && S(r["stages"]!["weekly_steal"]!["state"]) == "completed",
                "walking capture quota failure remains local to collection");
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

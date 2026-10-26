using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class WeeklyNpcInteractionCases
{
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly DailyWeeklyNpc Npc;
        public JsonObject Row;
        public JsonArray Wild = new();
        public Func<JsonArray>? SceneObjects;
        public long Planned;
        public JsonArray Active = new(O(("id", 10), ("objectId", new JsonArray())));
        public bool Navigating, Ready = true, Unknown;
        public bool NavigationReady = true;
        public double X;
        public long TriggerQuest = 10;
        public int Rejections, Interactions, Lifts, Navs, Held;
        public double AckAt = -1, AckDelay;
        string query = "prior";
        public Fixture(string path, int kind = 2, int count = 1)
        {
            Row = O(("id", 10), ("packId", 1), ("mapId", 1), ("conditionType", kind), ("conditionCount", count), ("magicValue", Enumerable.Range(100, count).ToArray()));
            H = new(path, []);
            H.Frame["Scene"] = "Map0001_001";
            H.Page("GameFieldDefaultUI");
            H.Readings = () => [WorkflowCases.Reading("weekly_npc.native", ("$self", Native())),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500))];
            Npc = new(new DailyFieldRoute(H.Workflow), false);
            H.OnDelay = () => { if (AckAt >= 0 && H.Time >= AckAt) { Active.Clear(); AckAt = -1; } };
            H.OnCommand = c =>
            {
                string command = S(c["Kind"]);
                if (command == "weekly_npc_query") { query = S(c["Id"]); return; }
                if (command == "weekly_npc_plan") { Planned = N(c["Value"]); Navigating = false; return; }
                if (command is "weekly_npc_nav" or "weekly_npc_carry_nav") { Navigating = true; Navs++; return; }
                if (command == "mainline_cancel_nav") { Navigating = false; return; }
                if (command is not ("weekly_npc_interact" or "weekly_npc_lift")) throw new Exception("Unexpected NPC command " + command);
                if (Rejections-- > 0 || Unknown)
                {
                    H.Box.Values["live:receipts~" + S(c["Id"]) + ".json"] = JsonSerializer.SerializeToUtf8Bytes(
                        O(("Command", c), ("State", Unknown ? "unknown" : "rejected"), ("Error", Unknown ? "native callback lost" : "npc_lease_expired"), ("MayHaveDispatched", Unknown)));
                    return;
                }
                if (!Ready) throw new Exception("Interaction sent before native object ready");
                if (command == "weekly_npc_lift") { Lifts++; Held = 200; return; }
                if (kind == 9 && Held != 200) throw new Exception("Delivery before lifting");
                Interactions++;
                if (Interactions < count) { Active[0]!["objectId"]!.AsArray().Add(N(c["Items"]![0]) - 200); Held = 0; return; }
                if (AckDelay > 0) AckAt = H.Time + AckDelay; else Active.Clear();
            };
        }
        JsonObject Native() => O(("State", "ready"), ("Error", ""), ("Query", query), ("Pack", 1), ("Map", 1),
            ("Week", 500), ("Limit", 3), ("Remaining", 2), ("Completed", 0), ("ActionsSupported", true), ("CanAccept", false),
            ("Rows", new JsonArray(Row.DeepClone()).ToJsonString()), ("Active", Active.ToJsonString()), ("Posted", new JsonArray()), ("Cleared", new JsonArray()), ("Wild", Wild), ("NavigationPlannedQuest", Planned),
            ("Navigating", Navigating), ("NavigationLeaseQuest", 10), ("NavigationLeaseUntil", H.Ticks + TimeSpan.TicksPerMinute), ("CarryId", Held),
            ("Position", new[] { X, 0d, 0d }), ("NavigationReady", NavigationReady), ("NavQuest", Navigating ? 10 : 0),
            ("Objects", SceneObjects?.Invoke() ?? Array((Row["magicValue"] as JsonArray)!.Select(value => O(("Quest", TriggerQuest), ("Instance", 200 + N(value)), ("Id", N(value)), ("Available", true), ("Eligible", Ready), ("Near", true), ("Ready", Ready))))),
            ("CarryObjects", new JsonArray(O(("Quest", 10), ("Instance", 400), ("Id", 200), ("Available", true), ("Near", true), ("Ready", Ready), ("Distance", 0)))));
        public async Task Run() { await Npc.Query(1); await Npc.Advance(Row); }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        using (var f = new Fixture(Path.Combine(root, "npc-hunt-guide-blocked"), 1))
        {
            bool deferred = false; try { await f.Run(); } catch (DailyNpcUnavailable) { deferred = true; }
            Check(deferred && f.Navs == 0 && f.Interactions == 0, "kill steps cannot fall through to native hunting navigation or trigger battles");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-wild-route"), 1, 3))
        {
            f.Row["mapId"] = 87; f.Navigating = true; f.Active[0]!["value"] = 1;
            f.Wild.Add(O(("Step",10),("Map",82),("Group",1),("Monster",101),("Count",2),("Available",true)));
            await f.Npc.Query(1);
            Check((await f.Npc.Targets(1)).SequenceEqual([82L]) && !f.Navigating && f.Planned == 10,
                "hunting MapId is replaced by live wild map after clearing pre-existing quest navigation; partial kills count");
            await f.Npc.Query(1); await f.Npc.Targets(1);
            Check(f.H.Box.Commands.Count(c=>S(c["Kind"])=="weekly_npc_plan")==1,"unchanged active plan does not repeat cancellation");
            f.Planned=0; await f.Npc.Query(1); await f.Npc.Targets(1);
            Check(f.H.Box.Commands.Count(c=>S(c["Kind"])=="weekly_npc_plan")==2,"expired native plan is re-established before further routing");
            f.Wild[0]!["Available"]=false;
            await f.Npc.Query(1);
            bool deferred=false; try { await f.Npc.Targets(1); } catch(DailyNpcUnavailable) { deferred=true; }
            Check(deferred&&f.Navs==0&&f.Interactions==0,"disappearing wild targets defer locally without switching to the hunting guide");
            await f.Npc.ReleasePlan();
            Check(f.Planned==0,"deferring an NPC task releases its guide ownership");
            f.Wild[0]!["Available"]=true; await f.Npc.Query(1); await f.Npc.Targets(1);
            f.Active.Clear(); await f.Npc.Query(1);
            Check((await f.Npc.Targets(1)).Length==0&&f.Planned==0,"finished quest releases navigation before other daily stages");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-insufficient-wild-route"), 1, 3))
        {
            f.Row["mapId"]=87;
            f.Wild.Add(O(("Step",10),("Map",82),("Group",1),("Monster",101),("Count",2),("Available",true)));
            // One monster listed in two regions must not count twice.
            f.Wild.Add(O(("Step",10),("Map",83),("Group",1),("Monster",101),("Count",2),("Available",true)));
            await f.Npc.Query(1);
            bool deferred=false; try { await f.Npc.Targets(1); } catch(DailyNpcUnavailable) { deferred=true; }
            Check(deferred&&f.Navs==0,"remaining wild count is checked, rather than treating any matching spawn as sufficient");
        }
        foreach (int kind in new[] { 2, 9, 18 })
        using (var f = new Fixture(Path.Combine(root, "npc-arrival-" + kind), kind))
        {
            await f.Run();
            Check(f.Interactions == 1 && f.H.Time < 10, "NPC interacts in range while navigation mode remains active: " + kind);
            Check(f.Lifts == (kind == 9 ? 1 : 0), "carry source is lifted exactly once before delivery: " + kind);
        }
        foreach (int kind in new[] { 2, 9, 18 })
        using (var f = new Fixture(Path.Combine(root, "npc-runtime-object-" + kind), kind, 2))
        {
            JsonObject Obj(int id, bool ready, int quest = 10) => O(("Id", id), ("Instance", 200 + id),
                ("Quest", quest), ("Available", true), ("Eligible", ready), ("Near", true), ("Ready", ready));
            // The game's current scene offers a third target, outside the static
            // MagicValue list. After its acknowledgement another target becomes
            // eligible. Unrelated and not-yet-eligible props remain untouched.
            f.SceneObjects = () => new JsonArray(Obj(99, true, 11), Obj(100, f.Interactions > 0),
                Obj(101, false), Obj(102, true));
            await f.Run();
            Check(f.Interactions == 2 && f.H.Box.Commands.Where(c => S(c["Kind"]) == "weekly_npc_interact")
                    .Select(c => N(c["Items"]![0])).SequenceEqual([302L, 300L]),
                "scene-owned ready targets outside MagicValue finish in-step without picking unrelated or ineligible objects: " + kind);
            Check(f.Lifts == (kind == 9 ? 2 : 0) && f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_query") == 1,
                "runtime target selection preserves carry ownership and confirmed pickup progress: " + kind);
        }
        foreach (int kind in new[] { 2, 9, 18 })
        using (var f = new Fixture(Path.Combine(root, "npc-area-marker-" + kind), kind, 2))
        {
            // Two real pickups hide their quest icons; the visible area guide
            // has IsDisableInteraction=1. This is the 101352 failure topology.
            JsonObject Prop(int id, bool marker) => O(("Id", id), ("Instance", 200 + id), ("Quest", 10),
                ("Available", true), ("Eligible", !marker), ("QuestMark", marker), ("Near", true), ("Ready", !marker));
            f.SceneObjects = () => new JsonArray(Prop(99, true), Prop(100, false), Prop(101, false));
            await f.Run();
            Check(f.Interactions == 2 && f.H.Box.Commands.Where(c => S(c["Kind"]) == "weekly_npc_interact")
                    .Select(c => N(c["Items"]![0])).SequenceEqual([300L, 301L]),
                "hidden-mark props complete without interacting with the visible non-interactive area guide: " + kind);
            Check(f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_query") == 1,
                "area-guide fix retains acknowledged progress without intermediate server rereads: " + kind);
        }
        using (var f = new Fixture(Path.Combine(root, "npc-server-reject")))
        {
            int acknowledgements = 0;
            var original = f.H.OnCommand;
            f.H.OnCommand = c =>
            {
                if (S(c["Kind"]) == "weekly_npc_interact")
                {
                    f.Interactions++;
                    f.H.Page("MessagePopupUI", "_buttonOK");
                    f.H.Frame["Surfaces"]![0]!["NativeContext"] = "weekly_npc_rejected_ack_only";
                    f.H.Frame["Surfaces"]![0]!["Text"] = new JsonArray("無法在伺服器找到任務資訊。, error:100011");
                    return;
                }
                if (S(c["Kind"]) == "background_error_ack") { acknowledgements++; f.H.Page("GameFieldDefaultUI"); return; }
                original!(c);
            };
            string reason = ""; try { await f.Run(); } catch (DailyNpcUnavailable e) { reason = e.Message; }
            Check(reason.Contains("100011") && acknowledgements == 1 && f.Interactions == 1 && f.H.Time < 10
                && DailyNavigationDecision.Types(f.H.Frame).Contains("GameFieldDefaultUI") && f.Active.Count == 1,
                "explicit server rejection closes safely and defers without waiting three minutes, replaying or inventing completion");
            f.H.Page("MessagePopupUI", "_buttonOK");
            Check(DailyWeeklyNpc.RejectedQuestAction(f.H.Frame, DailyNavigationPolicy.Load()) == null,
                "unowned popups are not dismissed as NPC server rejection");
            f.H.Frame["Surfaces"]![0]!["NativeContext"] = "weekly_npc_rejected_ack_only";
            Check(DailyHomeDecision.BackgroundErrorAction(f.H.Frame, DailyNavigationPolicy.Load()) == null,
                "generic navigation cannot swallow an NPC rejection and lose its failure reason");
        }
        foreach (bool activeFlag in new[] { true, false })
        using (var f = new Fixture(Path.Combine(root, "npc-hazard-" + activeFlag)))
        {
            f.Ready = false;
            f.H.OnDelay = () => {
                // Passive UI changes used to keep the old timeout alive forever.
                f.H.Frame["UiToken"] = "frame-" + f.H.Time;
                if (f.Navs < 2) f.Navigating = activeFlag; else f.Ready = true;
            };
            await f.Run();
            var commands = f.H.Box.Commands.Select(c => S(c["Kind"])).ToArray();
            Check(f.Navs == 2 && f.Interactions == 1 && commands.Contains("mainline_cancel_nav") && f.H.Time < 15,
                "stationary interrupted task resumes via cancel then start despite UI churn, navigation flag=" + activeFlag);
        }
        using (var f = new Fixture(Path.Combine(root, "npc-moving")))
        {
            f.Ready = false; f.H.OnDelay = () => { f.X += .5; if (f.H.Time > 15) f.Ready = true; };
            await f.Run();
            Check(f.Navs == 1, "long moving route is not restarted by elapsed time alone");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-damage-animation")))
        {
            f.Ready = false; f.NavigationReady = false;
            f.H.OnDelay = () => { if (f.H.Time > 10) { f.NavigationReady = true; f.Ready = true; } };
            await f.Run();
            Check(f.Navs == 1, "locked movement during an animation is not cancelled");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-persistent-obstacle")))
        {
            f.Ready = false;
            bool deferred = false; try { await f.Run(); } catch (DailyNpcUnavailable) { deferred = true; }
            Check(deferred && f.Navs == 4 && f.H.Time < 40 && f.Interactions == 0, "three unsuccessful route restarts defer the NPC task without an endless daily loop");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-carry-hazard"), 9))
        {
            f.Ready = false;
            f.H.OnDelay = () => { if (f.Navs >= 2) f.Ready = true; };
            await f.Run();
            Check(f.Navs == 3 && f.Lifts == 1 && f.Interactions == 1, "carry approach re-resolves its source after a hazard and lifts only once");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-delayed-ready")))
        {
            f.Ready = false; f.H.OnDelay = () => { if (f.H.Time > 3) f.Ready = true; };
            await f.Run();
            Check(f.Interactions == 1 && f.H.Time < 12, "busy object waits for readiness instead of waiting for navigation expiry");
        }
        foreach (int kind in new[] { 2, 9, 18 })
        using (var f = new Fixture(Path.Combine(root, "npc-multiple-objects-" + kind), kind, 3))
        {
            await f.Run();
            Check(f.Interactions == 3 && f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_query") == 1,
                "multiple acknowledged objects finish without server rereads wiping partial progress: " + kind);
            Check(f.H.Box.Commands.Where(c => S(c["Kind"]) == "weekly_npc_interact").Select(c => N(c["Items"]![0])).SequenceEqual([300L, 301L, 302L]),
                "confirmed objects are excluded from subsequent navigation and pickup: " + kind);
        }
        using (var f = new Fixture(Path.Combine(root, "npc-delayed-ack")) { AckDelay = 10 })
        {
            await f.Run();
            Check(f.Interactions == 1 && f.Navs == 1, "unconfirmed pickup is neither replayed nor interrupted by stalled-route recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-expired-lease")) { Rejections = 1 })
        {
            await f.Run();
            Check(f.Navs == 2 && f.Interactions == 1, "definite lease rejection reobserves and renews navigation before one pickup");
            Check(Directory.GetFiles(Path.Combine(f.H.Root, "live", "weekly-npc-recovery")).Length == 1, "lease recovery records compact native evidence");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-repeated-rejection")) { Rejections = 10 })
        {
            bool deferred = false; try { await f.Run(); } catch (DailyNpcUnavailable) { deferred = true; }
            Check(deferred && f.Navs == 3 && f.Interactions == 0, "repeated definite NPC rejection has two retries then defers locally");
        }
        using (var f = new Fixture(Path.Combine(root, "npc-unknown-receipt")) { Unknown = true })
        {
            DailyStepException? failure = null; try { await f.Run(); } catch (DailyStepException e) { failure = e; }
            Check(failure?.MayHaveDispatched == true && f.Navs == 1 && f.H.Box.Commands.Count(c => S(c["Kind"]) == "weekly_npc_interact") == 1,
                "unknown actual interaction is never retried as a definite lease rejection");
        }
        Check(DailyDiagnosticExport.Allowed("live/weekly-npc-recovery/test.json") && DailyDiagnosticExport.Allowed("live/weekly-npc-queries/test.json"), "NPC query and failure evidence are included in diagnostic export");
        JsonObject Step(int id, int pack, int next, int prior, int gold, int condition = 2) => O(("id", id), ("packId", pack), ("mapId", pack * 10 + 1),
            ("priorQuestId", prior), ("nextQuestId", next), ("conditionType", condition), ("conditionCount", 1),
            ("rewardType", gold > 0 ? new[] { 4 } : []), ("rewardId", gold > 0 ? new[] { 0 } : []), ("rewardCount", gold > 0 ? new[] { gold } : []));
        var rows = new[] { Step(10, 1, 11, 0, 0), Step(11, 1, 0, 10, 100), Step(20, 1, 0, 0, 20), Step(30, 2, 0, 0, 90), Step(40, 2, 0, 0, 80), Step(50, 2, 0, 0, 1000, 1) };
        var native = O(("Remaining", 3), ("Completed", 0), ("Limit", 3), ("CanAccept", true), ("Posted", new[] { 10, 20, 30, 40, 50 }), ("Cleared", new JsonArray()), ("Wild", new JsonArray()));
        var d = new DailyNpcData(native, rows.ToDictionary(r => N(r["id"])), []);
        Check(d.Candidates(1, false, []).Select(c => N(c["id"])).SequenceEqual([10L, 20L]) && N(d.Candidates(1, false, [])[0]["reward_gold"]) == 100,
            "reward ordering uses the terminal reward of the entire quest chain");
        Check(d.Candidates(2, false, []).Select(c => N(c["id"])).SequenceEqual([30L, 40L]) && N(d.Candidates(2, true, [])[0]["id"]) == 50,
            "higher rewards do not bypass disabled hunting-only quests");
        rows[0]["displayRewardCount"] = new JsonArray(999999);
        Check(DailyNpcData.GoldReward(rows.Take(2)) == 100, "display reward preview is not added to actual payouts");
        native["Cleared"] = new JsonArray(11);
        Check(d.Candidates(1, false, []).Select(c => N(c["id"])).SequenceEqual([20L]), "completed chains are removed before reward ranking");
    }
}

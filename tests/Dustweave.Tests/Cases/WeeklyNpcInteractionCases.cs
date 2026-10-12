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
        public JsonArray Active = new(O(("id", 10), ("objectId", new JsonArray())));
        public bool Navigating, Ready = true, Unknown;
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
                if (Interactions < count) { Active[0]!["objectId"]!.AsArray().Add(99 + Interactions); Held = 0; return; }
                if (AckDelay > 0) AckAt = H.Time + AckDelay; else Active.Clear();
            };
        }
        JsonObject Native() => O(("State", "ready"), ("Error", ""), ("Query", query), ("Pack", 1), ("Map", 1),
            ("Week", 500), ("Limit", 3), ("Remaining", 2), ("Completed", 0), ("ActionsSupported", true), ("CanAccept", false),
            ("Rows", new JsonArray(Row.DeepClone()).ToJsonString()), ("Active", Active.ToJsonString()), ("Posted", new JsonArray()), ("Cleared", new JsonArray()),
            ("Navigating", Navigating), ("NavigationLeaseQuest", 10), ("NavigationLeaseUntil", H.Ticks + TimeSpan.TicksPerMinute), ("CarryId", Held),
            ("Objects", Array((Row["magicValue"] as JsonArray)!.Select(value => O(("Quest", 10), ("Instance", 200 + N(value)), ("Id", value), ("Near", true), ("Ready", Ready))))),
            ("CarryObjects", new JsonArray(O(("Quest", 10), ("Instance", 400), ("Id", 200), ("Available", true), ("Near", true), ("Ready", Ready), ("Distance", 0)))));
        public async Task Run() { await Npc.Query(1); await Npc.Advance(Row); }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        foreach (int kind in new[] { 2, 9, 18 })
        using (var f = new Fixture(Path.Combine(root, "npc-arrival-" + kind), kind))
        {
            await f.Run();
            Check(f.Interactions == 1 && f.H.Time < 10, "NPC interacts in range while navigation mode remains active: " + kind);
            Check(f.Lifts == (kind == 9 ? 1 : 0), "carry source is lifted exactly once before delivery: " + kind);
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
        using (var f = new Fixture(Path.Combine(root, "npc-delayed-ack")) { AckDelay = 4 })
        {
            await f.Run();
            Check(f.Interactions == 1, "unconfirmed pickup is not replayed while native acknowledgement is delayed");
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

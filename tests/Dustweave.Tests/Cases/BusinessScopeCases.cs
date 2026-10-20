using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class BusinessScopeCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        foreach (JsonNode? scope in new JsonNode?[] { JsonValue.Create("regular"), null, new JsonArray(1), O(("kind", "regular")) })
        {
            var proof = new DailyBusinessProof("cafeteria.regular_all", "cafeteria_guests", ["cafeteria"], (_, _, _) => O(("verified", true)));
            using var f = new WorkflowHarness(Path.Combine(output, "scope-" + Guid.NewGuid().ToString("N")), [proof]);
            var op = f.Business.Create(f.Context, proof.Role, f.Evidence(), new(), O(("ui", "GameFieldDefaultUI")));
            op["scope"] = Copy(scope); op["state"] = "unknown"; op["last_observation"] = f.Evidence(); f.Business.Save(op);
            var report = await f.Business.ReconcileAsync(f.Context);
            Check(report["completed"]!.AsArray().Count == 1 && f.Box.Commands.Count == 0, "role without scope-dependent rules can reconcile heterogeneous scope: " + (scope?.ToJsonString() ?? "null"));
        }
        // A proof that actually depends on scope still requires its object contract.
        var dependent = new DailyBusinessProof("scope.dependent", "test", ["test"], (_, _, _) => new(), ScopeStages: s => [S(s["stage"])]);
        using var other = new WorkflowHarness(Path.Combine(output, "scope-dependent"), [dependent]);
        var pending = other.Business.Create(other.Context, dependent.Role, other.Evidence(), O(("stage", "test")), O(("ui", "MenuUI")));
        pending["scope"] = "invalid"; pending["state"] = "unknown"; other.Business.Save(pending);
        bool rejected = false;
        try { await other.Business.ReconcileAsync(other.Context); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && other.Box.Commands.Count == 0, "scope-dependent proof rejects a scalar explicitly without inventing defaults");
        var talentProof = DailyWorkflowRegistry.Proofs().Single(p => p.Role == "dispatch.start");
        using var talents = new WorkflowHarness(Path.Combine(output, "scope-stolen-talent"), [talentProof]);
        var steal = talents.Business.Create(talents.Context, "dispatch.start", talents.Evidence(), O(("kind", 1), ("npc", 4), ("group", 114)), O(("ui", "StealInfoPopupUI")));
        steal["state"] = "unknown"; talents.Business.Save(steal);
        var recovery = await talents.Business.ReconcileAsync(talents.Context);
        Check(JsonNode.DeepEquals(recovery["unresolved"]![0]!["stages"], new JsonArray("weekly_steal")),
            "an interrupted theft blocks only theft in the composed production registry");
        var research = talents.Business.Create(talents.Context, "dispatch.start", talents.Evidence(), O(("kind", 6)), O(("ui", "QuickMenuUI")));
        research["state"] = "rejected"; talents.Business.Save(research);
        bool theftBlocked = false;
        try { talents.Business.Create(talents.Context, "dispatch.start", talents.Evidence(), O(("kind", 1), ("npc", 5)), O(("ui", "StealInfoPopupUI"))); }
        catch (StageHostException e) { theftBlocked = e.Kind == "pending"; }
        Check(theftBlocked && talents.Box.Commands.Count == 0 && S(talents.Business.Records(talents.Context).Single(r => S(r["id"]) == S(steal["id"]))["state"]) == "unknown",
            "uncertain theft is preserved without stopping independent research or being replayed on another NPC");
        await TalentReceipts(output, cases);
    }
    private static async Task TalentReceipts(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var definition = DailyWorkflowRegistry.Proofs().Single(p => p.Role == "dispatch.start");
        using var f = new WorkflowHarness(Path.Combine(output, "theft-shared-talent-response"), [definition]);
        f.Page("StealInfoPopupUI", "_objButtonOk");
        f.Readings = () => [
            WorkflowCases.Reading("mainline.map", ("ὮὬὬὮὠὮὪὠὧὩὪ", O(("id", 1), ("packId", 1)))),
            WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500L))
        ];
        var op = f.Business.Create(f.Context, definition.Role, f.Evidence(),
            O(("kind", 1), ("map", 1), ("npc", 5), ("group", 114), ("week", "500"), ("server_now", 1000L)),
            O(("ui", "StealInfoPopupUI"), ("field", "_objButtonOk")));
        JsonObject Event(string kind, int sequence, int group, JsonNode? npc) {
            var e = WorkflowCases.Event("dispatch.start", kind, sequence, ("IsSuccess", true),
                ("TalentSkillInfo", O(("groupId", group))), ("TalentNpcInfo", npc));
            e["Frame"] = Copy(f.Frame); e["AtUtcTicks"] = f.Ticks;
            return e;
        }
        JsonObject Npc(long npc = 5, long group = 114) => O(("npcId", npc), ("groupId", group), ("endTime", "2000"));
        JsonArray Pair(int group, JsonNode? npc, int start = 1) => new(Event("request", start, group, npc), Event("response", start + 1, group, npc));
        void Reject(JsonArray events, string name) {
            bool invalid = false;
            try { definition.Verify(op, events, f.Evidence()); }
            catch (InvalidDataException) { invalid = true; }
            Check(invalid, name);
        }
        Reject(Pair(114, null), "missing NPC receipt is an explicit incomplete proof, not a null-reference crash");
        var dispatch = Pair(1805, null);
        Reject(dispatch, "dispatch reply cannot confirm a theft even though both use TalentSkillUseResponse");
        Check(N(definition.Verify(op, Pair(114, Npc()), f.Evidence())["npc"]) == 5,
            "matching theft keeps its NPC, group, map and week proof");
        var mixed = dispatch.DeepClone().AsArray();
        foreach (var e in Pair(114, Npc(), 3)) mixed.Add(Copy(e));
        Check(N(definition.Verify(op, mixed, f.Evidence())["npc"]) == 5,
            "settled unrelated talent pair does not hide a later matching theft reply");
        foreach (string mutation in new[] { "npc", "npc-group", "missing-group", "rejected-other", "foreign-actor", "duplicate", "overlap", "orphan" })
        {
            var es = mixed.DeepClone().AsArray();
            switch (mutation)
            {
                case "npc": es[3]!["Values"]![2]!["Json"] = Npc(npc: 6).ToJsonString(); break;
                case "npc-group": es[3]!["Values"]![2]!["Json"] = Npc(group: 125).ToJsonString(); break;
                case "missing-group": es[1]!["Values"]![1]!["Json"] = "null"; break;
                case "rejected-other": es[1]!["Accepted"] = false; break;
                case "foreign-actor": es[1]!["Frame"]!["Instance"] = "foreign"; break;
                case "duplicate": foreach (var e in Pair(114, Npc(), 5)) es.Add(Copy(e)); break;
                case "overlap": es[1]!["Sequence"] = 3; es[2]!["Sequence"] = 2; break;
                case "orphan": es.Add(Event("request", 5, 1805, null)); break;
            }
            Reject(es, "theft correlation preserves ambiguity and rejects " + mutation);
        }
        // The reported failure remained in the journal across process and day changes.
        op["state"] = "unknown"; op["events"] = Copy(dispatch); op["last_observation"] = f.Evidence();
        op["error"] = "Object reference not set to an instance of an object."; f.Business.Save(op);
        using var resumed = new WorkflowHarness(Path.Combine(output, "theft-restarted-game"), [definition],
            configureIdentity: (frame, context) => { context["cycle"] = "next-day"; context["actor"]![0] = 999; frame["ProcessId"] = 999; });
        resumed.Business.Save(op);
        var report = await resumed.Business.ReconcileAsync(resumed.Context);
        Check(report["completed"]!.AsArray().Count == 0 && report["unresolved"]!.AsArray().Count == 1
            && JsonNode.DeepEquals(report["unresolved"]![0]!["stages"], new JsonArray("weekly_steal"))
            && resumed.Box.Commands.Count == 0, "old foreign talent receipt no longer pauses preflight or claims theft success");
        resumed.Business.BeginAttempt();
        resumed.Page("GameFieldDefaultUI");
        await resumed.Business.ReplanInterruptedAsync(resumed.Context, "weekly_steal");
        var saved = resumed.Business.Records(resumed.Context).Single();
        Check(S(saved["state"]) == "superseded" && saved["result"] == null && resumed.Box.Commands.Count == 0
            && !B(saved["reconciliation"]?["saved_action_replayed"]),
            "ready weekly route retires the old intent for fresh server-target planning, with no replay");
        Check((await resumed.Business.ReconcileAsync(resumed.Context))["unresolved"]!.AsArray().Count == 0,
            "recovered theft does not return as a permanent pause on the next run");

        using var live = new WorkflowHarness(Path.Combine(output, "theft-delayed-own-response"), [definition]);
        live.Page("StealInfoPopupUI", "_objButtonOk");
        live.Readings = f.Readings;
        var current = live.Business.Create(live.Context, definition.Role, live.Evidence(), op["scope"]!.AsObject(), op["action"]!.AsObject());
        live.OnCommand = _ => {
            live.Event("dispatch.start", "request");
            live.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", 1805))), ("TalentNpcInfo", null));
        };
        bool delivered = false;
        live.OnDelay = () => {
            if (delivered) return;
            delivered = true;
            live.Event("dispatch.start", "request");
            live.Event("dispatch.start", "response", ("IsSuccess", true), ("TalentSkillInfo", O(("groupId", 114))), ("TalentNpcInfo", Npc()));
        };
        var committed = await live.Business.CommitAsync(current, live.Context, 2);
        Check(S(committed["state"]) == "completed" && live.Box.Commands.Count == 1 && delivered
            && committed["events"]!.AsArray().Count == 4,
            "live theft waits for its own response without replay and retains all raw diagnostic events");

        var broken = new DailyBusinessProof("legacy.proof", "weekly_steal", ["test"], (_, _, _) => throw new NullReferenceException("legacy missing field"));
        var valid = new DailyBusinessProof("valid.proof", "room", ["test"], (_, _, _) => O(("verified", true)));
        using var isolated = new WorkflowHarness(Path.Combine(output, "proof-failure-isolation"), [broken, valid]);
        foreach (var proof in new[] { broken, valid })
        {
            var item = isolated.Business.Create(isolated.Context, proof.Role, isolated.Evidence(), new(), new());
            item["state"] = "unknown"; item["last_observation"] = isolated.Evidence(); isolated.Business.Save(item);
        }
        var isolatedReport = await isolated.Business.ReconcileAsync(isolated.Context);
        Check(isolatedReport["completed"]!.AsArray().Count == 1 && isolatedReport["unresolved"]!.AsArray().Count == 1
            && isolated.Box.Commands.Count == 0,
            "a malformed legacy verifier is isolated to its stage while independent reconciliation continues");
    }
}

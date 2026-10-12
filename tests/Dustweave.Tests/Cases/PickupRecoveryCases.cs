using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class PickupRecoveryCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        using var f = new WorkflowHarness(Path.Combine(root, "pickup-recovery"), DailyFieldRoute.Proofs().ToArray());
        f.Page("GameFieldDefaultUI", "pickup");
        var op = f.Business.Create(f.Context, "mainline.pickup", f.Evidence(), O(("map", 3), ("object", 725)), O(("ui", "GameFieldDefaultUI"), ("field", "pickup"), ("native", true)));
        f.OnCommand = _ => {
            f.Event("mainline.pickup", "request");
            f.Event("mainline.pickup", "response", ("RewardInfoBundle", null));
            string key = f.Box.Values.Keys.Single(k => k.StartsWith("live:events~", StringComparison.Ordinal) && k.EndsWith("-2.json", StringComparison.Ordinal));
            var reply = JsonNode.Parse(f.Box.Values[key])!.AsObject(); reply["Accepted"] = false; reply["ErrorCode"] = 5;
            f.Box.Values[key] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reply);
            f.Page("MessagePopupUI", "_buttonOK"); f.Frame["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
        };
        bool rejected = false;
        try { await f.Business.CommitAsync(op, f.Context, 65); }
        catch (DailyPickupRejectedException e) { rejected = e.TalentInactive; }
        Check(rejected && S(op["state"]) == "server_rejected" && f.Time < 10 && f.Box.Commands.Count == 1,
            "known inactive-research pickup is rejected promptly, preserving its reply without repeating input");
        f.Business.RequireResolved(f.Context, "mainline.pickup");
        var events = op["events"]!.AsArray();
        foreach (string mutation in new[] { "request-only", "success", "missing-reward", "reward", "actor", "order", "orphan", "read-error", "old-event", "wrong-role" })
        {
            var bad = events.DeepClone().AsArray();
            switch (mutation) {
                case "request-only": bad.RemoveAt(1); break;
                case "success": bad[1]!["Accepted"] = true; bad[1]!["ErrorCode"] = 0; break;
                case "missing-reward": bad[1]!["Values"] = new JsonArray(); break;
                case "reward": bad[1]!["Values"]![0]!["Json"] = "{}"; break;
                case "actor": bad[1]!["Frame"]!["Instance"] = "other"; break;
                case "order": bad[0]!["Sequence"] = 3; break;
                case "orphan": bad.Add(bad[0]!.DeepClone()); break;
                case "read-error": bad[1]!["Values"]![0]!["Error"] = "unreadable"; break;
                case "old-event": bad[0]!["AtUtcTicks"] = N(op["at"]) - 1; break;
                case "wrong-role": bad[1]!["Role"] = "mainline.pickup_list"; break;
            }
            Check(DailyPickupRecovery.Rejection(op, bad) == null, "pickup recovery retains uncertainty for " + mutation);
        }
        // Older versions waited for after-state equality even when the server rejected
        // pickup. A changed map/absorption count cannot turn that rejection into success.
        var historical = op.DeepClone().AsObject(); historical["state"] = "unknown"; historical["cycle"] = "yesterday";
        historical["last_observation"] = O(("Frame", f.Frame), ("Readings", new JsonArray()));
        f.Business.Save(historical);
        f.Page("GameFieldDefaultUI");
        int commands = f.Box.Commands.Count;
        var report = await f.Business.ReconcileAsync(f.Context);
        var recovered = f.Business.Records(f.Context).Single();
        Check(S(recovered["state"]) == "server_rejected" && report["unresolved"]!.AsArray().Count == 0 && f.Box.Commands.Count == commands &&
            JsonNode.DeepEquals(recovered["events"], events) && recovered["result"] == null,
            "legacy failed pickup stops blocking later queues without claiming collection or altering evidence");
        f.Business.RequireResolved(f.Context, "mainline.pickup");
        await SuccessfulAndInterruptedWalk(root, cases);
        await PickupQueueRecoveryCases.Run(root, cases);
        await UnstartedWalk(root, cases);
    }

    static async Task UnstartedWalk(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        foreach (string reason in new[] { "target_missing", "target_off_navigation", "path_incomplete", "pickup_area_unreachable" })
        {
            string code = "travel_unreachable:" + reason;
            Check(DailyPickupRecovery.PathRejected(new("rejected", code) { RejectionCode = code }), "unsent target rejection may defer: " + reason);
            Check(!DailyPickupRecovery.PathRejected(new("unknown", code, true) { RejectionCode = code }), "missing dispatch proof cannot defer: " + reason);
        }
        foreach (string code in new[] { "travel_unreachable:player_missing", "travel_unreachable:player_off_navigation", "travel_preflight_unavailable:TypeLoadException" })
            Check(!DailyPickupRecovery.PathRejected(new("rejected", code) { RejectionCode = code }), "player or adapter failure cannot blacklist every pickup: " + code);
        using var f = new WorkflowHarness(Path.Combine(root, "pickup-unstarted"), DailyFieldRoute.Proofs().ToArray());
        f.Page("GameFieldDefaultUI");
        f.Readings = () => [WorkflowCases.Reading("mainline.map", (DailyFieldRoute.MapPath, O(("id", 31)))),
            WorkflowCases.Reading("mainline.talent_counts", ("Values", new JsonArray()))];
        var op = f.Business.Create(f.Context, "mainline.pickup", f.Evidence(), O(("map", 31), ("object", 15)),
            O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", 1015)));
        op["state"] = "unknown"; op["command_id"] = new string('c', 32);
        op["before"]!["Frame"]!["BridgeVersion"] = 115;
        var command = op["before"]!["Frame"]!.DeepClone().AsObject();
        command["Id"] = Copy(op["command_id"]); command["Kind"] = "mainline_walk"; command["Value"] = 1015;
        command["Reason"] = "business:" + S(op["id"]) + "|mainline.pickup";
        var receipt = O(("Command", command), ("Before", op["before"]!["Frame"]), ("State", "unknown"), ("MayHaveDispatched", true),
            ("Error", "System.InvalidOperationException: travel_unreachable:path_incomplete\r\n  at BD2Daily.Live.Bridge.Tick ()"));
        Check(DailyPickupRecovery.CanRetireUnstartedWalk(op, new(), receipt, new(f.Frame, f.Context)), "audited v115 path preflight rejection is not an unknown pickup");
        foreach (string mutation in new[] { "no-receipt", "actor", "account", "command", "target", "reason", "events", "version", "native-check", "future", "missing-marker" })
        {
            var bad = receipt.DeepClone().AsObject(); var current = new DailyStageFrame(f.Frame, f.Context.DeepClone().AsObject()); var events = new JsonArray();
            switch (mutation)
            {
                case "actor": bad["Before"]!["Instance"] = "other"; break;
                case "account": current.Context["server"] = "other"; break;
                case "command": bad["Command"]!["Kind"] = "mainline_talent"; break;
                case "target": bad["Command"]!["Value"] = 9999; break;
                case "reason": bad["Command"]!["Reason"] = "another operation"; break;
                case "events": events.Add(WorkflowCases.Event("mainline.pickup", "request", 1)); break;
                case "version": bad["Before"]!["BridgeVersion"] = 114; break;
                case "native-check": bad["Error"] = "System.InvalidOperationException: travel_unreachable:native_check"; break;
                case "future": bad["Before"]!["AtUtcTicks"] = f.Ticks + 1; break;
                case "missing-marker": bad.Remove("MayHaveDispatched"); break;
            }
            Check(!DailyPickupRecovery.CanRetireUnstartedWalk(op, events, mutation == "no-receipt" ? null : bad, current), "unsent walk recovery retains guard: " + mutation);
        }
        f.Business.Save(op);
        DailyJson.Write(Path.Combine(f.Root, "live", "steps", S(op["command_id"]), "result.json"), O(("receipt", receipt)));
        var report = await f.Business.ReconcileAsync(f.Context);
        Check(report["unresolved"]!.AsArray().Count == 0 && f.Box.Commands.Count == 0 && S(f.Business.Records(f.Context).Single()["state"]) == "superseded",
            "historical path refusal retires from durable original receipt without sending a command");
        f.Box.State = "rejected"; f.Box.Dispatched = false; f.Box.Error = "travel_unreachable:path_incomplete";
        var route = new DailyFieldRoute(f.Workflow);
        bool blocked = false;
        try { await route.WalkPickup(f.Evidence(), O((DailyFieldRoute.ObjectPath, 15), ("instance", 1015))); }
        catch (DailyTravelBlocked) { blocked = true; }
        Check(blocked && f.Time < 2 && f.Business.Records(f.Context).All(r => S(r["state"]) is "rejected" or "superseded"),
            "new explicitly unsent path refusal becomes local navigation failure immediately, without a 65 second reward wait");
    }

    static async Task SuccessfulAndInterruptedWalk(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        using var f = new WorkflowHarness(Path.Combine(root, "pickup-walk"), DailyFieldRoute.Proofs().ToArray());
        f.Page("GameFieldDefaultUI");
        bool available = true, legacy = false;
        JsonObject Drop(long id, bool normal, bool hidden, bool pending)
        {
            var row = WorkflowCases.Reading("mainline.reward", (DailyFieldRoute.ObjectPath, id),
                ("ὯὩὮὩὮὣὯὫὮὡὧ", normal), ("ὦὫὧὯὥὫὦὪὣὦὪ", hidden), (DailyFieldRoute.DropAvailablePath, pending));
            row["InstanceId"] = id + 1000;
            if (legacy) row["Values"]!.AsArray().RemoveAt(3);
            return row;
        }
        f.Readings = () => [
            WorkflowCases.Reading("mainline.map", (DailyFieldRoute.MapPath, O(("id", 23))),
                ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.x", 1), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.y", 0), ("ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.z", 2)),
            WorkflowCases.Reading("mainline.talent_counts", ("Values", new JsonArray(O(("groupId", 405), ("useCount", 4))))),
            Drop(201, true, false, available), Drop(226, false, true, true), Drop(227, false, true, false), Drop(33008, false, false, true)
        ];
        Check(DailyFieldRoute.Drops(f.Evidence()).Select(r => N(r[DailyFieldRoute.ObjectPath])).SequenceEqual(new long[] { 201, 226 }),
            "walking targets use native remaining state, exclude collected hidden drops and healing objects");
        JsonObject Create() => f.Business.Create(f.Context, "mainline.pickup", f.Evidence(), O(("map", 23), ("object", 201)),
            O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", 1201)));
        var op = Create();
        bool checkedIncidental = false;
        f.OnCommand = _ => { f.Event("mainline.pickup", "request"); f.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject())); };
        f.OnDelay = () => {
            if (!checkedIncidental)
            {
                checkedIncidental = true;
                bool rejected = false;
                try { f.Business.Verify(op, f.Business.Events(op), f.Evidence()); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "incidental accepted healing pickup does not confirm a still-pending selected target");
            }
            if (f.Time >= 1 && available)
            {
                available = false;
                f.Event("mainline.pickup", "request"); f.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject()));
            }
        };
        await f.Business.CommitAsync(op, f.Context, 5);
        Check(S(op["state"]) == "completed" && f.Time < 3 && f.Box.Commands.Count == 1 &&
            DailyFieldRoute.FieldRows(f.Evidence(), "reward").Any(r => N(r[DailyFieldRoute.ObjectPath]) == 201) &&
            !DailyFieldRoute.Drops(f.Evidence()).Any(r => N(r[DailyFieldRoute.ObjectPath]) == 201),
            "successful target remains in Unity with unchanged type flags but no longer blocks completion or gets selected again");

        f.OnDelay = null; available = true; legacy = true;
        var old = Create(); old["state"] = "unknown"; old["at"] = f.Ticks;
        old["events"] = op["events"]!.DeepClone();
        foreach (var e in Rows(old["events"])) e["AtUtcTicks"] = f.Ticks;
        old["last_observation"] = f.Evidence(); f.Business.Save(old);
        bool missingRejected = false;
        try { DailyFieldRoute.Drops(f.Evidence()); } catch (InvalidDataException) { missingRejected = true; }
        Check(missingRejected, "missing native availability cannot silently turn the entire map into collected");
        var settled = old["events"]!.AsArray();
        foreach (string mutation in new[] { "missing-response", "missing-frame", "actor", "popup", "other-command", "future-response", "rejection" })
        {
            var bad = old.DeepClone().AsObject(); var frame = f.Frame.DeepClone().AsObject();
            switch (mutation) {
                case "missing-response": bad["events"]!.AsArray().RemoveAt(3); break;
                case "missing-frame": bad["events"]![3]!["Frame"] = null; break;
                case "actor": frame["Instance"] = "other"; break;
                case "popup": frame["Surfaces"]![0]!["Type"] = "MessagePopupUI"; break;
                case "other-command": bad["action"]!["operation"] = "mainline_talent"; break;
                case "future-response": bad["events"]![3]!["AtUtcTicks"] = f.Ticks + 1; break;
                case "rejection": bad["events"]![3]!["Accepted"] = false; break;
            }
            Check(!DailyPickupRecovery.SettledWalk(bad, bad["events"]!.AsArray(), frame), "walk recovery retains guard for " + mutation);
        }
        int commands = f.Box.Commands.Count;
        // Updating the observation component does not change the live game or account.
        // The old journal must be settled using its original replies, even at MenuUI.
        old["before"]!["Frame"]!["Instance"] = "previous-observer";
        old["last_observation"]!["Frame"]!["Instance"] = "previous-observer";
        foreach (var e in Rows(old["events"])) e["Frame"]!["Instance"] = "previous-observer";
        f.Business.Save(old);
        f.Page("MenuUI");
        var report = await f.Business.ReconcileAsync(f.Context);
        var saved = f.Business.Records(f.Context).Single(r => S(r["id"]) == S(old["id"]));
        Check(S(saved["state"]) == "superseded" && report["unresolved"]!.AsArray().Count == 0 && f.Box.Commands.Count == commands &&
            saved["result"] == null && !B(saved["reconciliation"]?["target_collected"]) && JsonNode.DeepEquals(saved["events"], settled),
            "legacy settled walking journal permits reselecting current drops without replaying input or claiming collection");
        f.Business.RequireResolved(f.Context, "mainline.pickup");

        legacy = false;
        f.Page("GameFieldDefaultUI");
        int walks = 0;
        f.OnCommand = command => {
            if (S(command["Kind"]) != "mainline_walk") return;
            ++walks;
            f.Event("mainline.pickup", "request"); f.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject()));
            if (walks == 2) available = false;
        };
        var route = new DailyFieldRoute(f.Workflow);
        var walked = await route.WalkPickup(f.Evidence(), DailyFieldRoute.Drops(f.Evidence()).Single(r => N(r[DailyFieldRoute.ObjectPath]) == 201));
        Check(S(walked["state"]) == "completed" && walks == 2 && N(walked["navigation_resumes"]) == 1,
            "stalled walk after an incidental settled pickup resumes navigation and confirms the real target");

        available = true; walks = 0;
        f.OnCommand = command => {
            if (S(command["Kind"]) != "mainline_walk") return;
            ++walks;
            f.Event("mainline.pickup", "request"); f.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject()));
        };
        bool blocked = false;
        try { await route.WalkPickup(f.Evidence(), DailyFieldRoute.Drops(f.Evidence()).Single(r => N(r[DailyFieldRoute.ObjectPath]) == 201)); }
        catch (DailyTravelBlocked) { blocked = true; }
        Check(blocked && walks == 3 && f.Business.Records(f.Context).All(r => S(r["state"]) != "unknown"),
            "repeated stalled pickup is bounded and leaves the target pending without blocking the entire weekly queue");
        f.Business.RequireResolved(f.Context, "mainline.pickup");
    }
}

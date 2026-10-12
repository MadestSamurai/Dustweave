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
    }
}

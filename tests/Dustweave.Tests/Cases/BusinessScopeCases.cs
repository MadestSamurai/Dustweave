using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
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
    }
}
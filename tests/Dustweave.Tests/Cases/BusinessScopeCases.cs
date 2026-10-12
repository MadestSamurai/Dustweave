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
    }
}

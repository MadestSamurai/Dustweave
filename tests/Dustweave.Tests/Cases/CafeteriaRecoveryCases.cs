using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class CafeteriaRecoveryCases
{
    public static async Task Run(string output, List<string> cases)
    {
        foreach (string mode in new[] { "expired", "ambiguous", "unknown" })
        {
            using var f = new WorkflowHarness(Path.Combine(output, "cafeteria-bubble-" + mode), DailyCafeteria.Proofs().ToArray());
            f.Frame["Scene"] = "Map3007_001";
            f.Page("CafeteriaFieldDefaultUI", "_buttonTrackingRegular.button");
            const string field = "$pointer/Parent/Cafeteria(Clone)/Button - EventBubble";
            var target = O(("Id", 99), ("Enabled", true), ("Field", field), ("Route", "pointer"));
            f.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "OverheadManageUI"), ("Popup", false), ("Order", 0), ("InputReady", true), ("Targets", new JsonArray(target))));
            bool expired = false;
            f.Readings = () => [
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3007), (DailyTravel.CurrentPack + ".PackType", 9)),
                WorkflowCases.Reading("cafeteria.cache", ("DailyRegularCostumeId", new JsonArray()), ("DailyNpcRewardCurrencyCount", expired ? 20 : 0)),
                WorkflowCases.Reading("cafeteria.regular_done", ("Keys", new JsonArray()), ("Values", new JsonArray())),
                WorkflowCases.Reading("cafeteria.rules", ("DailyShopCurrencyLimit", 20), ("EventRewardType", 44))];
            f.Driver.SubmissionGuard = () =>
            {
                var targets = f.Frame["Surfaces"]![1]!["Targets"]!.AsArray();
                if (mode == "expired") { expired = true; targets.Clear(); }
                if (mode == "ambiguous") targets.Add(target.DeepClone());
            };
            if (mode == "unknown") f.Box.ThrowCreate = true;
            string outcome = "";
            try { outcome = S((await DailyCafeteria.Run(f.Workflow))["state"]); }
            catch (DailyStepException e) { outcome = e.RejectionCode; }
            catch (StageHostException e) { outcome = e.Kind; }
            bool ok = mode switch {
                "expired" => expired && outcome == "completed" && f.Box.Commands.Count == 0,
                "ambiguous" => outcome == "target_ambiguous" && f.Box.Commands.Count == 0,
                _ => outcome == "pending" && f.Box.Commands.Count == 1
            };
            if (!ok) throw new Exception("Cafeteria bubble recovery failed: " + mode + " -> " + outcome);
            cases.Add("cafeteria bubble lifecycle preserves transaction safety: " + mode);
        }
    }
}

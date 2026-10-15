using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static WorkflowCases;

static class EventPreviewCases
{
    public static async Task Run(string output, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        foreach (string opener in new[] { "_buttonSkipBattle._button", "_buttonAutoBattle._button" })
        foreach (string failure in new[] { "none", "configure", "confirm", "always", "spent", "stored", "stage", "scene", "actor", "unknown", "timeout", "blocked" })
        {
            using var h = new WorkflowHarness(Path.Combine(output, "event-preview-" + Guid.NewGuid().ToString("N")),
                [new DailyBusinessProof("fixture.event", "event_battle", ["event.currency"], (_, _, _) => O(("ok", true)))]);
            var lobby = EventTradeBoundaryCases.Lobby(); lobby["Selected"] = 1;
            int free = 5, stored = 2, opens = 0, confirms = 0, configured = 0;
            h.Readings = () => [Reading("event.lobby", ("$self", lobby)), Reading("event.currency", ("EventApFree", free), ("EventApStack", stored))];
            void LobbyPage() => h.Page("EventBattleUI", opener);
            LobbyPage();
            h.OnCommand = c =>
            {
                if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 10) { opens++; h.Page("BattleSkipPopupUI"); }
                else { Check(S(c["Kind"]) == "event_sweep_confirm", "Recovery preserves the consuming action"); confirms++; LobbyPage(); }
            };
            async Task Configure()
            {
                configured++;
                if (failure == "none" || configured > 1 && failure != "always") return;
                if (failure is "confirm" or "unknown" or "timeout") return;
                LobbyPage();
                if (failure == "spent") free--;
                if (failure == "stored") stored--;
                if (failure == "stage") lobby["Selected"] = 2;
                if (failure == "scene") h.Frame["Scene"] = "another-map";
                if (failure == "actor") h.Frame["PlayerKey"] = "different-player";
                if (failure == "blocked") h.Frame["Surfaces"]!.AsArray().Add(O(("Id", 20), ("Type", "UnknownPopupUI"), ("Popup", true), ("Order", 10), ("InputReady", true), ("Targets", new JsonArray())));
                await DailyEventPreview.WaitValue(h.Workflow, ["event.currency"], _ => true);
            }
            async Task<JsonObject> Confirm(JsonObject e)
            {
                if (configured == 1 && failure is "unknown" or "timeout")
                    throw new DailyStepException(failure == "unknown" ? "rejected" : "pending", "Uncertain native outcome", true) { RejectionCode = "surface_missing" };
                if (configured == 1 && failure == "confirm") LobbyPage();
                return await h.Workflow.TransactObserved("fixture.event", O(("count", 5)),
                    O(("ui", "BattleSkipPopupUI"), ("operation", "event_sweep_confirm")), e);
            }
            bool success = false;
            try { await DailyEventPreview.Run(h.Workflow, opener, Configure, Confirm); success = true; }
            catch (Exception ex) when (ex is DailyStepException or StageHostException or InvalidDataException) { }
            bool recoverable = failure is "none" or "configure" or "confirm";
            Check(success == recoverable, "Preview result is bounded and evidence based: " + failure + " / " + opener);
            Check(confirms == (recoverable ? 1 : 0), "Exactly one native confirmation on recovery, none for unsafe states: " + failure);
            Check(opens == (failure == "always" ? 3 : failure is "configure" or "confirm" ? 2 : 1), "No unlimited preview reopening: " + failure);
            if (failure == "confirm")
            {
                var records = h.Business.Records(h.Context, "fixture.event").ToArray();
                Check(records.Count(r => S(r["state"]) == "rejected" && B(r["confirmation_not_submitted"])) == 1
                    && records.Count(r => S(r["state"]) == "completed") == 1, "Actual transaction rejection is retained and cannot become a pending lock");
            }
        }
    }
}

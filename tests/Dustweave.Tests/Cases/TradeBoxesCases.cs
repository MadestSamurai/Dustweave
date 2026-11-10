using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class TradeBoxesCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks.Add(name); }
        var old = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        old["Trade"]!.AsObject().Remove("OpenBoxesBeforeTrade");
        Check(!DailyPreferences.Parse(old.ToJsonString()).Trade.OpenBoxesBeforeTrade, "Existing accounts keep box opening off");
        JsonObject Box(long instance, int keep, int count = 30, int type = 9) => O(("Instance", instance), ("Id", 100 + instance), ("Type", type), ("Keep", keep), ("Count", count));
        var unknown = Box(6, 0); unknown.Remove("Keep");
        var initial = O(("Revision", 0), ("Boxes", new JsonArray(Box(1, 0), Box(2, 1), Box(3, 2), Box(4, 0, 0), Box(5, 0, 10, 5), unknown, Box(7, 0))));
        Check(Available(initial).Select(x => N(x["Instance"])).SequenceEqual([1L, 7L]), "Locked, unknown-lock, empty and non-box inventory are excluded");
        foreach (string scenario in new[] { "disabled", "trade-off", "empty", "full", "batch", "reward", "active-empty", "timeout", "cancelled" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "boxes-" + scenario), []);
            h.Workflow.Settings.Trade.Enabled = scenario != "trade-off";
            h.Workflow.Settings.Trade.OpenBoxesBeforeTrade = scenario != "disabled";
            var state = initial.DeepClone().AsObject();
            if (scenario == "full") state["Full"] = true;
            if (scenario == "empty") state["Boxes"] = new JsonArray(Box(2, 1));
            if (scenario == "active-empty")
            {
                state["Boxes"] = new JsonArray(Box(2, 1)); state["Pending"] = true;
                h.OnDelay = () => state["Pending"] = false;
            }
            h.Page("InventoryManageUI");
            h.Readings = () => [WorkflowCases.Reading("trade.boxes", ("$self", state)),
                WorkflowCases.Reading("reward.presentation", ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true), ("ὮὬὧὦὣὠὠὤὮὦὧ", true), ("ὦὡὮὫὧὠὮὡὭὭὬ", false))];
            int batches = 0;
            h.OnCommand = c =>
            {
                if (S(c["Kind"]) == "back") { h.Page(N(c["SurfaceId"]) == 2 ? "InventoryManageUI" : "MenuUI"); return; }
                if (S(c["Kind"]) != "trade_boxes_open") throw new Exception("Unexpected input " + c);
                batches++;
                var ids = c["Items"]!.AsArray().Where((_, i) => i % 3 == 0).Select(N).ToArray();
                Check(ids.SequenceEqual([1L, 7L]), "One native batch contains every unlocked stack: " + scenario);
                state["Revision"] = 1;
                if (scenario == "timeout") { state["Pending"] = true; return; }
                if (scenario == "cancelled") h.Stopped = true;
                foreach (var box in Available(state)) box["Count"] = 0;
                if (scenario == "reward") h.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "RewardReceivePopupUI"), ("Popup", true), ("Order", 20), ("InputReady", true), ("Targets", new JsonArray())));
            };
            bool failed = false;
            try { await DailyTradeBoxes.Open(h.Workflow); }
            catch (Exception e) when ((scenario is "timeout" or "cancelled") && (e is StageHostException or DailyStepException)) { failed = true; }
            Check(batches == (scenario is "disabled" or "trade-off" or "empty" or "full" or "active-empty" ? 0 : 1), "Only one batch, or no work when not applicable: " + scenario);
            Check(N(Rows(state["Boxes"]).Single(x => N(x["Instance"]) == 2)["Count"]) == 30, "Locked stock is unchanged: " + scenario);
            if (scenario == "timeout")
            {
                Check(failed && h.Time < 70, "No-progress wait is bounded");
                // Resume completion of the existing native batch; never issue another.
                h.OnDelay = () => { state["Pending"] = false; foreach (var box in Available(state)) box["Count"] = 0; };
                await DailyTradeBoxes.Open(h.Workflow);
                Check(batches == 1 && await h.Workflow.Has("MenuUI"), "A stalled batch resumes and returns to planning without resubmission");
            }
            if (scenario == "cancelled") Check(failed, "Cancellation remains responsive during opening");
            if (scenario == "reward") Check(h.Time < 10 && await h.Workflow.Has("MenuUI"), "Reward returns through inventory without waiting for the wrong destination");
            if (scenario == "active-empty") Check(h.Time > 0 && !B(state["Pending"]) && await h.Workflow.Has("MenuUI"), "Last box consumed still waits for native batch completion");
        }
        var reviewRoot = Path.Combine(root, "review");
        var store = new DailyPreferenceStore(reviewRoot);
        string a = new('a', 64), b = new('b', 64);
        var p = new DailyPreferences(); p.Trade.FavoritesOnly = true; p.General.TalentPotionReserve = 10; store.Save(a, p); store.Save(b, new());
        var review = new DailyResourceReview(reviewRoot);
        Check(DailyDiagnosticExport.Allowed("live/trade-boxes/" + a + ".json"), "The latest native batch journal is included in normal diagnostics");
        Check(review.Needed, "One-time resource review starts pending");
        review.Save(new Dictionary<string, DailyResourceReview.Options> { [a] = new(true, true, 6000, true) });
        Check(store.Read(a).Trade.OpenBoxesBeforeTrade && store.Read(a).Trade.FavoritesOnly && store.Read(a).General.TalentPotionReserve == 10 && store.Read(a).Stages.PreserveBloodCocktailMail, "Review updates only its four fields");
        Check(!store.Read(b).Trade.OpenBoxesBeforeTrade, "Review does not overwrite other accounts");
        bool invalid = false;
        try { review.Save(new Dictionary<string, DailyResourceReview.Options> { [b] = new(true, true, 6000, true), [a] = new(true, true, 5, true) }); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid && !store.Read(b).Trade.OpenBoxesBeforeTrade, "All edits validate before writing any account");
        review.Dismiss(); Check(!new DailyResourceReview(reviewRoot).Needed, "Reminder dismissal persists across restarts");
    }
    private static JsonObject[] Available(JsonObject state) => DailyTradeBoxes.Available(state);
}

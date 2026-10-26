using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class TradeEntryCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        foreach (string scenario in new[] { "already-near", "walk", "walk-error" })
        using (var h = new WorkflowHarness(Path.Combine(root, "merchant-approach-" + scenario), []))
        {
            h.Frame["Scene"] = "Map3009_003";
            h.Page("GameFieldDefaultUI");
            bool near = scenario == "already-near";
            h.Readings = () => [WorkflowCases.Reading("trade.navigation", ("$self", O(("Npc", 2), ("Near", near), ("Reachable", true), ("Distance", 1))))];
            h.OnCommand = c => {
                if (S(c["Kind"]) == "square_shop_nav")
                {
                    if (scenario == "walk-error") throw new DailyStepException("uncertain", "lost navigation receipt", true);
                    near = true;
                    h.Frame["SquareNavigation"] = O(("Kind", "square_shop_nav"), ("State", "arrived")); h.Publish();
                }
            };
            bool failed = false;
            try { await new DailyTradeExecution(h.Workflow, new()).Approach(2); }
            catch (DailyStepException) when (scenario == "walk-error") { failed = true; }
            var steps = h.Box.Commands.Select(c => S(c["Kind"])).ToArray();
            Check(scenario == "already-near" ? steps.Length == 0 : steps.SequenceEqual(new[] { "square_shop_nav", "square_cancel_nav" }),
                "merchant only cancels a walk it attempted, including an uncertain reply: " + scenario);
            Check(failed == (scenario == "walk-error"), "merchant cleanup preserves the original walking failure: " + scenario);
        }
        foreach (string scenario in new[] { "preview", "settlement", "wrong-merchant", "pending", "old-pending", "unknown-popup" })
        using (var h = new WorkflowHarness(Path.Combine(root, "trade-entry-" + scenario), DailyWorkflowRegistry.Proofs().Where(p => p.Role == "dispatch.start").ToArray()))
        {
            h.Frame["Scene"] = "Map3009_003";
            var shop = HomeNavigationCases.Surface("ShopUI", field: "_objBackButton", id: 303);
            var popup = HomeNavigationCases.Surface("DiscountPopupUI", true, "_objButtonCancel", 1000);
            void Page(bool showPreview)
            {
                h.Frame["Surfaces"] = showPreview ? Array(new[] { shop, popup }) : Array(new[] { shop });
                h.Frame["UiToken"] = showPreview ? "shop-discount" : "shop";
                h.Publish();
            }
            h.Readings = () => [
                WorkflowCases.Reading("trade.shop_ui", ("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", scenario == "wrong-merchant" ? 99 : 12)),
                WorkflowCases.Reading("trade.merchant", ("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", 12),
                    ("$items", new[] { O(("ὫὠὫὯὠὢὥὧὢὥὥ", "Shop"), ("ὭὩὪὯὯὬὧὠὠὬὠ", 1)) })),
                WorkflowCases.Reading("trade.discount_popup", (DailyFieldRoute.SkillPath, O(("classType", 16))))
            ];
            Page(true);
            if (scenario is "pending" or "old-pending")
            {
                var op = h.Business.Create(h.Context, "dispatch.start", h.Evidence(), O(("_proof", O(("kind", "bargain")))), O(("ui", "DiscountPopupUI")));
                op["state"] = "unknown";
                if (scenario == "old-pending") op["before"]!["Frame"]!["ProcessId"] = 987;
                h.Business.Save(op);
            }
            if (scenario == "unknown-popup") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPurchaseUI", true, "_buttonOK", 1100));
            int cancelled = 0;
            h.OnCommand = c => {
                if (N(c["SurfaceId"]) != N(popup["Id"]) || N(c["TargetId"]) != N(popup["Targets"]![0]!["Id"])) throw new Exception("Unexpected trade confirmation");
                cancelled++; Page(false);
            };
            var trade = new DailyTradeExecution(h.Workflow, new());
            if (scenario == "settlement")
            {
                h.OnDelay = () => { if (h.Time >= .6) Page(false); };
                await trade.PurchasePageReady();
                Check(h.Time >= .6 && h.Time < 2 && h.Box.Commands.Count == 0, "confirmed bargain waits for its covering popup to disappear, not just for ShopUI underneath");
            }
            else if (scenario is "preview" or "old-pending")
            {
                await trade.PrepareQuotePage();
                await trade.PrepareQuotePage();
                Check(cancelled == 1 && h.Business.Records(h.Context).Count() == (scenario == "old-pending" ? 1 : 0), "unsubmitted preview cancels once without spending, and stale records cannot block it: " + scenario);
            }
            else
            {
                bool blocked = false;
                try { await trade.PrepareQuotePage(); } catch (Exception e) when (e is InvalidDataException or StageHostException) { blocked = true; }
                Check(blocked && cancelled == 0, "trade preview recovery preserves unrelated or unsettled state: " + scenario);
            }
        }
        var enormousInventory = new string('x', 9 * 1024 * 1024);
        var record = O(("state", "failed"), ("error", "preview still visible"), ("waits", 0), ("gameplay_actions", 0),
            ("last", O(("at", 123), ("evidence", O(("Frame", O(("Scene", "Map3009_003"), ("BridgeVersion", 121),
                ("Surfaces", new[] { O(("Type", "ShopUI"), ("Order", 303)), O(("Type", "DiscountPopupUI"), ("Popup", true), ("Order", 1000)) }))),
                ("Readings", new[] { WorkflowCases.Reading("trade.native", ("$self", O(("BargainActive", false), ("BargainPercent", 0), ("Offers", enormousInventory)))) }))))));
        var summary = DailyTradeQuoteReadiness.Summary(record);
        Check(summary.ToJsonString().Length < 4096 && S(summary["last"]!["surfaces"]![1]!["type"]) == "DiscountPopupUI" && !B(summary["last"]!["bargain_active"]),
            "quote diagnostic summary retains window and bargain state even when full evidence exceeds export limit");
        Check(DailyDiagnosticExport.Allowed("live/trade-quote-reads/fixture.summary.json"), "compact quote diagnostic is eligible for the standard export");
        string exportRoot = Path.Combine(root, "trade-summary-export"), exportPath = Path.Combine(root, "trade-summary.zip");
        DailyJson.Write(Path.Combine(exportRoot, "live", "trade-quote-reads", "fixture.json"), record);
        DailyJson.Write(Path.Combine(exportRoot, "live", "trade-quote-reads", "fixture.summary.json"), summary);
        DailyDiagnosticExport.Create(exportRoot, exportPath, "fixture", "test");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(exportPath))
            Check(zip.GetEntry("logs/main/live/trade-quote-reads/fixture.summary.json") != null && zip.GetEntry("logs/main/live/trade-quote-reads/fixture.json") == null,
                "real diagnostic export includes the compact failure summary when the full quote exceeds eight MiB");
        Check(DailyTradeQuoteReadiness.Summary(O(("state", "failed"), ("last", new JsonObject()))).ToJsonString().Length < 4096,
            "incomplete first-read diagnostics do not mask the original error");
    }
}

using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class BattleArrivalRecoveryCases
{
    static JsonObject Presented(string ui) => WorkflowCases.Reading("mirror.presentation." + ui,
        ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true), ("ὮὬὧὦὣὠὠὤὮὦὧ", true),
        ("ὫὥὧὬὭὫὡὠὠὪὠ", false), ("ὦὡὮὫὧὠὮὡὭὭὬ", false));
    static void Page(WorkflowHarness h, params JsonObject[] rows)
    {
        h.Frame["Surfaces"] = Array(rows); h.Frame["UiToken"] = string.Join("/", rows.Select(r => S(r["Type"]))); h.Publish();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        foreach (string timing in new[] { "simultaneous", "delayed-summary", "summary-before-result", "reconnected" })
        using (var h = new WorkflowHarness(Path.Combine(root, "mirror-overlap-" + timing), [DailyMirror.Proof()]))
        {
            h.Frame["Scene"] = "Map3001_001";
            h.Readings = () => new[] { WorkflowCases.Reading("mirror.currency", ("PvpTicket", 0), ("PvpTicketStack", 7)),
                WorkflowCases.Reading("mirror.period", ("$self", O(("Ready", true), ("State", "ScheduleSeason")))) }
                .Concat(new[] { "BattleResultUI", "PVPClassUpUI", "PVPAutoHistoryPopupUI" }.Select(Presented)).ToArray();
            var journal = h.Business.Create(h.Context, "mirror.end", h.Evidence(), O(("multiplier", 1), ("repetitions", 40)), O(("ui", "BattleAutoSettingPopupUI")));
            journal["state"] = "completed"; journal["result"] = O(("remaining", DailyMirror.Tickets(h.Evidence())));
            if (timing == "reconnected") journal["before"]!["Frame"]!["Instance"] = "previous-connector";
            h.Business.Save(journal);
            var field = HomeNavigationCases.Surface("GameFieldDefaultUI", field: "_buttonMenu", id: 302);
            var battle = HomeNavigationCases.Surface("BattleUI_PVP", id: 50);
            var rank = HomeNavigationCases.Surface("PVPClassUpUI", field: "_objCancelButton", id: 800);
            var summary = HomeNavigationCases.Surface("PVPAutoHistoryPopupUI", true, "_objBackButton", 1000);
            var result = HomeNavigationCases.Surface("BattleResultUI", field: "_objectWinExitButton", id: 700);
            const string exit = "$pointer/UIRoot/BattleResult/Object - Win/Parent/ButtonLayout/Button - WinExit";
            var pointer = result["Targets"]![0]!.DeepClone().AsObject(); pointer["Field"] = exit; pointer["Route"] = "pointer"; result["Targets"]!.AsArray().Add(pointer);
            bool exited = false; double summaryAt = -1;
            var closed = new List<string>();
            if (timing == "summary-before-result") Page(h, summary, rank, result, battle, field);
            else Page(h, result, battle, field);
            h.OnDelay = () => { if (summaryAt >= 0 && h.Time >= summaryAt) { summaryAt = -1; Page(h, summary, rank, battle, field); } };
            h.OnCommand = c =>
            {
                var row = Rows(h.Frame["Surfaces"]).Single(r => N(r["Id"]) == N(c["SurfaceId"]));
                string ui = S(row["Type"]);
                if (ui == "BattleResultUI")
                {
                    if (exited || N(c["TargetId"]) != N(pointer["Id"])) throw new Exception("Duplicate or incorrect result exit");
                    exited = true;
                    if (timing == "summary-before-result") Page(h, field);
                    else if (timing == "delayed-summary") { Page(h, field); summaryAt = h.Time + .2; }
                    else Page(h, summary, rank, battle, field);
                    return;
                }
                if (ui == "PVPAutoHistoryPopupUI") { closed.Add(ui); Page(h, timing == "summary-before-result" ? [rank, result, battle, field] : [rank, battle, field]); return; }
                if (ui == "PVPClassUpUI") { closed.Add(ui); Page(h, timing == "summary-before-result" ? [result, battle, field] : [field]); return; }
                if (ui == "GameFieldDefaultUI") { h.Page("MenuUI"); return; }
                throw new Exception("Unexpected settlement input: " + ui);
            };
            var finished = await DailyMirror.Run(h.Workflow);
            Check(S(finished["state"]) == "completed" && exited && closed.SequenceEqual(new[] { "PVPAutoHistoryPopupUI", "PVPClassUpUI" }), "Mirror drains overlapping summary and rank in visible order: " + timing);
            Check(N(finished["free_spent"]) == 0 && h.Business.Records(h.Context).Count() == 1 && h.Time < 10,
                "Settled Mirror recovery returns to daily tasks without another battle or long wait: " + timing);
        }
        foreach (string guard in new[] { "unowned", "pending", "currency-changed", "other-process", "other-account", "other-cycle", "unknown-popup" })
        using (var h = new WorkflowHarness(Path.Combine(root, "mirror-summary-guard-" + guard), [DailyMirror.Proof()]))
        {
            h.Frame["Scene"] = "Map3001_001";
            h.Readings = () => [WorkflowCases.Reading("mirror.currency", ("PvpTicket", 0), ("PvpTicketStack", 7)), Presented("PVPAutoHistoryPopupUI")];
            Page(h, HomeNavigationCases.Surface("PVPAutoHistoryPopupUI", true, "_objBackButton", 1000), HomeNavigationCases.Surface("PVPClassUpUI", field: "_objCancelButton", id: 800));
            if (guard != "unowned")
            {
                var op = h.Business.Create(h.Context, "mirror.end", h.Evidence(), O(("multiplier", 1)), O(("ui", "BattleAutoSettingPopupUI")));
                op["state"] = guard == "pending" ? "unknown" : "completed";
                op["result"] = O(("remaining", O(("free", guard == "currency-changed" ? 1 : 0), ("paid", 7))));
                if (guard == "other-process") op["before"]!["Frame"]!["ProcessId"] = 999;
                if (guard == "other-account") op["before"]!["Frame"]!["AccountKey"] = "different-account";
                if (guard == "other-cycle") op["cycle"] = "previous-day";
                h.Business.Save(op);
            }
            if (guard == "unknown-popup") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPaymentPopupUI", true, "_buttonOK", 1100));
            bool recovered = false, blocked = false;
            try { recovered = await DailyTravel.RecoverPresentation(h.Workflow, h.Frame); } catch (StageHostException ex) { blocked = ex.Kind == "adapter"; }
            Check(!recovered && (guard != "unknown-popup" || blocked) && h.Box.Commands.Count == 0, "Summary recovery preserves ownership and unknown confirmations: " + guard);
        }
        foreach (string start in new[] { "menu", "field", "castle", "unowned" })
        using (var h = new WorkflowHarness(Path.Combine(root, "salvation-entry-" + start), [DailySalvation.Definition()]))
        {
            h.Workflow.Settings.Salvation.Enabled = true;
            int pack = start == "castle" ? 3003 : 3009, selected = 0, listOpened = 0, walked = 0;
            // A prior list selection leaves the game's latch set; OnEnable on the
            // real list clears it. Calling the shortcut from MenuUI cannot do so.
            bool latch = true;
            const string tab = "$pointer/Parent/PackParent/TabButton/UIScrollView - Tap/Viewport/Content/Tab - Content - 1/Button - Tab - Content";
            const string item = "$pointer/Parent/PackParent/UIScrollView/UIViewport/UIContent/Castle/PackItem";
            void Field() { h.Frame["Scene"] = pack == 3003 ? "Map3003_001" : "Map3009_003"; h.Page("GameFieldDefaultUI", "_buttonPackList"); }
            Field(); if (start == "menu") h.Page("MenuUI");
            h.Readings = () => new[] {
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", pack), (DailyTravel.CurrentPack + ".PackType", pack == 3003 ? 4 : 9)),
                WorkflowCases.Reading("salvation.native", ("$self", O(("Ready", DailyNavigationDecision.Types(h.Frame).Contains("RogueLikeLobbyUI")), ("Unlocked", true), ("Season", true), ("Idle", true),
                    ("Level", 12), ("Cost", 1), ("Free", 6), ("Paid", 10), ("Stones", 100), ("RemainingCost", 100), ("Reward", 9))))
            }.Concat(new[] { WorkflowCases.Reading("salvation.pack", ("gameObject.name", start == "unowned" ? "Other" : "Castle"), (DailyTravel.EntryPack + ".Id", start == "unowned" ? 3001 : 3003)) }).ToArray();
            h.OnCommand = c =>
            {
                var row = Rows(h.Frame["Surfaces"]).Single(r => N(r["Id"]) == N(c["SurfaceId"]));
                string ui = S(row["Type"]), kind = S(c["Kind"]), fieldName = Rows(row["Targets"]).Where(t => N(t["Id"]) == N(c["TargetId"])).Select(t => S(t["Field"])).FirstOrDefault() ?? "";
                if (kind == "salvation_open") throw new Exception("Direct shortcut bypasses native list initialization");
                if (ui == "MenuUI" && (kind == "back" || fieldName == "_objBackButton")) { Field(); return; }
                if (ui == "GameFieldDefaultUI" && fieldName == "_buttonPackList") { listOpened++; latch = false; h.Page("PackListUI", tab, item); return; }
                if (ui == "PackListUI" && fieldName == tab) return;
                if (ui == "PackListUI" && fieldName == item) { if (latch) throw new Exception("Native selection latch still set"); latch = true; selected++; pack = 3003; Field(); return; }
                if (ui == "PackListUI" && kind == "back") { Field(); return; }
                if (kind == "salvation_walk") { walked++; h.Page("EvilCastleEnterUI", "_objRogueLikeButton"); return; }
                if (ui == "EvilCastleEnterUI" && fieldName == "_objRogueLikeButton") { h.Page("RogueLikeLobbyUI"); return; }
                if (ui == "RogueLikeLobbyUI" && kind == "back") { h.Page("EvilCastleEnterUI"); return; }
                if (ui == "EvilCastleEnterUI" && kind == "back") { Field(); return; }
                throw new Exception("Unexpected castle input: " + ui + "/" + kind + "/" + fieldName);
            };
            var result = await DailySalvation.Run(h.Workflow);
            Check(S(result["reason"]) == (start == "unowned" ? "salvation_locked" : "salvation_growth_funded"), "Salvation reaches verified eligibility through native pack navigation: " + start);
            Check(selected == (start is "menu" or "field" ? 1 : 0) && walked == (start == "unowned" ? 0 : 1) && listOpened == (start == "castle" ? 0 : 1) && h.Time < 15,
                "Castle arrival resets native latch once and reuses an already loaded castle: " + start);
            Check(!h.Box.Commands.Any(c => S(c["Kind"]) is "salvation_confirm" or "salvation_open"), "Entry recovery cannot spend keys or invoke the stale shortcut: " + start);
        }
    }
}

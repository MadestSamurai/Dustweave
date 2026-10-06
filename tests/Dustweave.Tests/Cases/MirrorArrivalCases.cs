using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

// The weekly reset chain appears inside cartridge arrival, before DailyMirror.Enter returns.
static class MirrorArrivalCases
{
    sealed class Scenario : IDisposable
    {
        public readonly WorkflowHarness H;
        public int Pack = 8, Selected;
        public string FinalUi = "GameFieldDefaultUI";
        public bool DelayedFollowUp;
        public double AnimationSeconds, OpenedAt;
        public readonly List<string> Closed = [];
        private int next;
        private double delayedAt = -1;
        private readonly string[] chain = ["PVPSeasonRewardPopupUI", "PVPClassUpUI", "PVPHistoryUI"];
        private const string Tab = "$pointer/Parent/PackParent/TabButton/UIScrollView - Tap/Viewport/Content/Tab - Content - 1/Button - Tab - Content";
        private const string Item = "$pointer/Parent/PackParent/UIScrollView/UIViewport/UIContent/Mirror/PackItem";
        public Scenario(string root)
        {
            H = new(root, []);
            H.Frame["Scene"] = "Map0008_001";
            H.Page("GameFieldDefaultUI", "_buttonPackList");
            H.Readings = Readings;
            H.OnCommand = c =>
            {
                string ui = S(H.Frame["Surfaces"]![0]!["Type"]), kind = S(c["Kind"]);
                string field = Rows(H.Frame["Surfaces"]![0]!["Targets"]).Where(t => N(t["Id"]) == N(c["TargetId"])).Select(t => S(t["Field"])).SingleOrDefault() ?? "";
                if (ui == "GameFieldDefaultUI" && field == "_buttonPackList") { H.Page("PackListUI", Tab, Item); return; }
                if (ui == "PackListUI" && field == Tab) return;
                if (ui == "PackListUI" && field == Item) { Selected++; Pack = 3001; H.Frame["Scene"] = "Map3001_001"; ShowNext(); return; }
                if (!chain.Contains(ui) || (ui == "PVPClassUpUI" ? field != "_objCancelButton" : kind != "back"))
                    throw new Exception("Unexpected arrival input: " + ui + "/" + kind + "/" + field);
                if (H.Time - OpenedAt < AnimationSeconds) throw new Exception("Dismissed before native presentation ready");
                Closed.Add(ui);
                if (DelayedFollowUp && ui == "PVPSeasonRewardPopupUI")
                {
                    H.Page(FinalUi); delayedAt = H.Time + .2;
                }
                else ShowNext();
            };
            H.OnDelay = () => { if (delayedAt >= 0 && H.Time >= delayedAt) { delayedAt = -1; ShowNext(); } };
        }
        public void ShowNext()
        {
            OpenedAt = H.Time;
            if (next == chain.Length) H.Page(FinalUi);
            else { string ui = chain[next++]; H.Page(ui, ui == "PVPClassUpUI" ? ["_objCancelButton"] : []); }
        }
        public void StartChain()
        {
            Pack = 3001; H.Frame["Scene"] = "Map3001_001"; ShowNext();
        }
        private JsonObject[] Readings() =>
            new[] {
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", Pack), (DailyTravel.CurrentPack + ".PackType", Pack == 3001 ? 2 : 0)),
                WorkflowCases.Reading("mirror.pack", ("gameObject.name", "Mirror"), (DailyTravel.EntryPack + ".Id", 3001)),
                WorkflowCases.Reading("mirror.currency", ("PvpTicket", 40), ("PvpTicketStack", 7))
            }.Concat(chain.Where(ui => DailyNavigationDecision.Types(H.Frame).Contains(ui)).Select(ui =>
                WorkflowCases.Reading("mirror.presentation." + ui, ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true),
                    ("ὮὬὧὦὣὠὠὤὮὦὧ", H.Time - OpenedAt >= AnimationSeconds), ("ὫὥὧὬὭὫὡὠὠὪὠ", false), ("ὦὡὮὫὧὠὮὡὭὭὬ", false)))).ToArray();
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        foreach (bool delayed in new[] { false, true })
        using (var s = new Scenario(Path.Combine(root, "weekly-entry-" + delayed)) { DelayedFollowUp = delayed })
        {
            bool entered = await DailyTravel.Enter(s.H.Workflow, 1, "mirror.pack", packId: 3001);
            Check(entered && s.Selected == 1 && s.Closed.SequenceEqual(new[] { "PVPSeasonRewardPopupUI", "PVPClassUpUI", "PVPHistoryUI" }),
                "Mirror cartridge arrival drains weekly reward/rank/history chain with one selection: " + delayed);
            Check(s.H.Time < 10 && DailyNavigationDecision.Types(s.H.Frame).SetEquals(["GameFieldDefaultUI"]),
                "Mirror weekly entry reaches native lobby without old sixty-second deadlock: " + delayed);
            Check(!s.H.Business.Records(s.H.Context).Any() && !s.H.Box.Commands.Any(c => S(c["Kind"]) is "mirror_ready" or "native_click"),
                "weekly arrival does not start a battle or consume tickets: " + delayed);
        }
        using (var s = new Scenario(Path.Combine(root, "entry-animation")) { AnimationSeconds = 2 })
        {
            bool entered = await DailyTravel.Enter(s.H.Workflow, 1, "mirror.pack", packId: 3001);
            Check(entered && s.Closed.Count == 3 && s.H.Time >= 6, "weekly entry uses native animation readiness without repeated dismissal");
        }
        using (var s = new Scenario(Path.Combine(root, "already-in-rank")))
        {
            s.StartChain();
            bool entered = await DailyTravel.Enter(s.H.Workflow, 1, "mirror.pack", packId: 3001);
            Check(entered && s.Selected == 0 && s.Closed.Count == 3, "rerun from weekly rank chain reuses current cartridge instead of switching again");
        }
        using (var s = new Scenario(Path.Combine(root, "ready-late-rank")) { FinalUi = "BattleUI_PVP", DelayedFollowUp = true })
        {
            s.StartChain();
            await DailyTravel.Ready(s.H.Workflow, "BattleUI_PVP");
            Check(s.Closed.Count == 3 && s.Selected == 0, "battle readiness handles a late rank page after weekly reward closes");
        }
        using (var s = new Scenario(Path.Combine(root, "foreign-popup")))
        {
            s.StartChain();
            s.H.Frame["Surfaces"]!.AsArray().Add(O(("Id",99),("Type","UnknownPaymentPopupUI"),("Popup",true),("Order",999),("InputReady",true),("Targets",new JsonArray())));
            bool blocked = false;
            try { await DailyTravel.Ready(s.H.Workflow, "GameFieldDefaultUI"); } catch (StageHostException ex) { blocked = ex.Kind == "adapter"; }
            Check(blocked && s.H.Box.Commands.Count == 0, "weekly presentation preserves an unknown covering confirmation");
        }
        using (var s = new Scenario(Path.Combine(root, "wrong-scene")))
        {
            s.StartChain(); s.H.Frame["Scene"] = "Map0008_001";
            Check(!await DailyMirror.RecoverRankPresentation(s.H.Workflow, s.H.Frame) && s.H.Box.Commands.Count == 0,
                "Mirror-specific presentation handler refuses another cartridge scene");
        }
        using (var s = new Scenario(Path.Combine(root, "rank-disappeared")))
        {
            s.StartChain(); var stale = s.H.Frame.DeepClone().AsObject(); s.H.Page("GameFieldDefaultUI");
            Check(await DailyMirror.RecoverRankPresentation(s.H.Workflow, stale) && s.H.Box.Commands.Count == 0,
                "rank page disappearing during observation causes re-observation without stale input");
        }
        using (var s = new Scenario(Path.Combine(root, "rank-stop")))
        {
            s.StartChain(); s.H.Stopped = true;
            bool stopped = false;
            try { await DailyTravel.Ready(s.H.Workflow, "GameFieldDefaultUI"); } catch (StageHostException ex) { stopped = ex.Kind == "stopped"; }
            Check(stopped && s.H.Box.Commands.Count == 0, "weekly settlement cancellation never sends a dismiss or match");
        }
        using (var s = new Scenario(Path.Combine(root, "rank-unknown-receipt")))
        {
            s.StartChain(); s.H.Box.ThrowCreate = true;
            bool blocked = false;
            try { await DailyTravel.Ready(s.H.Workflow, "GameFieldDefaultUI"); } catch (DailyStepException ex) { blocked = ex.Submitted; }
            Check(blocked && s.H.Box.Commands.Count == 1, "unknown weekly dismissal receipt is not automatically replayed");
        }
    }
}

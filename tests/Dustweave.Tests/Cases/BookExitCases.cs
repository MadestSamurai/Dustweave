using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class BookExitCases
{
    private static DailyWorkflow Workflow(WorkflowHarness h)
    {
        Task<DailyStageFrame> Read()
        {
            h.OnRead?.Invoke();
            h.Publish();
            return Task.FromResult(new DailyStageFrame(h.Frame.DeepClone().AsObject(), h.Context.DeepClone().AsObject()));
        }
        var navigation = new DailyStageNavigation(Read, () => h.Stopped, clock: () => h.Time,
            delay: t => { h.Time += t.TotalSeconds; h.OnDelay?.Invoke(); h.Publish(); return Task.CompletedTask; });
        return new(h.Root, Path.Combine(TestPaths.SourceRoot, "assets"), h.Context, h.Driver, h.Business,
            navigation, [], () => h.Stopped, (op, _, request) => op == "navigation_step"
                ? h.Driver.NavigationAsync(request!["action"]!.AsObject()) : throw new Exception("Unexpected relay: " + op));
    }
    private static void Result(WorkflowHarness h)
    {
        h.Frame["Scene"] = "Map3005_002";
        h.Page("BattleResultUI", "_objectWinExitButton");
        h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI_TotalWar", id: 2));
    }
    private static void Page(WorkflowHarness h, string type)
    {
        h.Frame["Scene"] = "Map3005_001";
        h.Page(type, type == "GameFieldDefaultUI" ? "_buttonMenu" : "_objBackButton");
    }
    private static void Route(WorkflowHarness h, JsonObject command)
    {
        switch (S(command["UiToken"]))
        {
            case "TotalWarUI": Page(h, "GameFieldDefaultUI"); break;
            case "GameFieldDefaultUI": Page(h, "MenuUI"); break;
            case "TotalWarNewcordPopupUI": Page(h, "TotalWarUI"); break;
            default: throw new Exception("Unexpected exit input: " + command.ToJsonString());
        }
    }
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); cases.Add(label); }
        int n = 0;
        WorkflowHarness New() => new(Path.Combine(output, "book-exit-" + ++n), []);
        foreach (string destination in new[] { "TotalWarUI", "GameFieldDefaultUI", "MenuUI", "TotalWarNewcordPopupUI" })
        {
            using var h = New(); Result(h);
            h.OnCommand = c => { if (S(c["UiToken"]) == "BattleResultUI") Page(h, destination); else Route(h, c); };
            await DailyWeeklyBook.Finish(Workflow(h));
            Check(DailyNavigationDecision.Types(h.Frame).Contains("MenuUI") && h.Time < 10, "Book exit reaches menu from native destination: " + destination);
            Check(h.Box.Commands.Count(c => S(c["UiToken"]) == "BattleResultUI") == 1, "Book result dismissed once: " + destination);
            int sent = h.Box.Commands.Count;
            await DailyWeeklyBook.Finish(Workflow(h));
            Check(h.Box.Commands.Count == sent, "Repeated completed exit submits no new battle or input: " + destination);
        }
        using (var h = New())
        {
            Result(h);
            h.OnCommand = c =>
            {
                if (S(c["UiToken"]) != "BattleResultUI") { Route(h, c); return; }
                h.Page("LoadingUI");
                h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI_TotalWar", id: 2));
            };
            h.OnDelay = () =>
            {
                if (h.Time >= 22 && DailyNavigationDecision.Types(h.Frame).Contains("LoadingUI")) Page(h, "TotalWarUI");
                else if (h.Time >= 12 && DailyNavigationDecision.Types(h.Frame).Contains("BattleUI_TotalWar")) h.Page("LoadingUI");
            };
            await DailyWeeklyBook.Finish(Workflow(h));
            Check(h.Time >= 22 && h.Time < 30 && DailyNavigationDecision.Types(h.Frame).Contains("MenuUI"), "Book exit waits for lingering battle and delayed loading before using actual lobby");
            Check(h.Box.Commands.Count == 3, "Delayed Book exit submits only result, lobby back and menu inputs");
        }
        foreach (string failure in new[] { "loading", "battle-teardown", "unknown-popup", "cancel", "identity" })
        {
            using var h = New(); Result(h);
            var originalContext = h.Context.DeepClone().AsObject();
            h.OnCommand = c =>
            {
                if (S(c["UiToken"]) != "BattleResultUI") { Route(h, c); return; }
                if (failure == "unknown-popup")
                {
                    Page(h, "TotalWarUI");
                    h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("PurchaseConfirmationUI", true, "_buttonOK", 9));
                }
                else h.Page(failure == "battle-teardown" ? "BattleUI_TotalWar" : "LoadingUI");
            };
            h.OnDelay = () =>
            {
                if (failure == "cancel" && h.Time >= 2) h.Stopped = true;
                if (failure == "identity" && h.Time >= 2) h.Context["cycle"] = "changed";
            };
            bool failed = false;
            try { await DailyWeeklyBook.Finish(Workflow(h)); }
            catch (Exception e) when (e is StageHostException or DailyStepException or OperationCanceledException) { failed = true; }
            Check(failed && h.Time < 70, "Book exit pauses within bound for " + failure);
            Check(h.Box.Commands.Count == 1, "Book exit preserves unrelated/unknown state without further inputs: " + failure);
            h.Stopped = false; h.OnDelay = null;
            h.Context["cycle"] = originalContext["cycle"]!.DeepClone();
            Page(h, "TotalWarUI");
            await DailyWeeklyBook.Finish(Workflow(h));
            Check(DailyNavigationDecision.Types(h.Frame).Contains("MenuUI") && h.Box.Commands.Count == 3,
                "Fresh recovery continues from Book lobby without repeating settlement or battle: " + failure);
        }
        using (var h = New())
        {
            h.Page("BattleResultUI", "_objectWinExitButton");
            h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI_PVP", id: 2));
            bool blocked = false;
            try { await DailyWeeklyBook.Finish(Workflow(h)); } catch (StageHostException) { blocked = true; }
            Check(blocked && h.Box.Commands.Count == 0, "Book cleanup never exits another mode's result or battle");
            Page(h, "TotalWarUI"); h.OnCommand = c => Route(h, c);
            await DailyWeeklyBook.Finish(Workflow(h));
            Check(DailyNavigationDecision.Types(h.Frame).Contains("MenuUI"), "Book cleanup can resume after unrelated battle is resolved manually");
        }
    }
}

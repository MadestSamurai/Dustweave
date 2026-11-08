using BD2Daily.Live;
using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static WorkflowCases;

static class EventStageSelectionCases
{
    private sealed class Row(int id)
    {
        public int Id = id;
        public bool Active;
    }

    // Reproduce the current client's SetSelectItem: assigning Selected precedes
    // the same-object early return. The list recycles Row without refreshing Detail.
    private sealed class Lobby
    {
        private Row? renamedSelectedRow;
        public int Selected, Detail, Refreshes;
        public bool FailRefresh;
        public void SetSelectItem(Row row, bool refresh)
        {
            Selected = row.Id;
            if (ReferenceEquals(renamedSelectedRow, row)) return;
            if (renamedSelectedRow != null) renamedSelectedRow.Active = false;
            renamedSelectedRow = row;
            row.Active = true;
            if (refresh)
            {
                if (FailRefresh) throw new InvalidOperationException("Synthetic native refresh interrupted");
                Detail = row.Id;
                Refreshes++;
            }
        }
    }

    public static async Task Run(string output, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        var native = new Lobby(); var recycled = new Row(15);
        native.SetSelectItem(recycled, true);
        recycled.Id = 5;
        native.SetSelectItem(recycled, true);
        Check(native.Selected == 5 && native.Detail == 15, "Reproduces pooled row ID changing while uncleared stage 15 detail remains");
        EventStageSelection.Select(native, recycled, native.SetSelectItem);
        Check(native.Selected == 5 && native.Detail == 5 && native.Refreshes == 2 && recycled.Active, "Same-row selection refreshes detail and buttons through native code");
        var other = new Row(6);
        EventStageSelection.Select(native, other, native.SetSelectItem);
        Check(!recycled.Active && other.Active && native.Detail == 6, "Different-row selection retains native deselection and refresh");
        native.FailRefresh = true; other.Id = 7;
        try { EventStageSelection.Select(native, other, native.SetSelectItem); throw new Exception("Refresh failure hidden"); }
        catch (InvalidOperationException) { }
        native.FailRefresh = false;
        EventStageSelection.Select(native, other, native.SetSelectItem);
        EventStageSelection.Select(native, other, native.SetSelectItem);
        Check(native.Detail == 7 && native.Refreshes == 5, "Interrupted native refresh and repeated continuation can refresh the same row again");

        foreach (int initialSelected in new[] { 15, 5 })
        foreach (bool fixedBudget in new[] { false, true })
        {
            using var h = new WorkflowHarness(Path.Combine(output, $"event-pooled-row-{initialSelected}-{fixedBudget}"), [DailyEventSweep.Definition()]);
            var ui = new Lobby(); var row = new Row(15); ui.SetSelectItem(row, true);
            if (initialSelected == 5) { row.Id = 5; ui.SetSelectItem(row, false); }
            var lobby = EventTradeBoundaryCases.Lobby();
            lobby["Latest"] = 15;
            lobby["Stages"] = new JsonArray(EventTradeBoundaryCases.Stage(5, true), EventTradeBoundaryCases.Stage(15, false));
            int free = 4, count = 1, selections = 0, scrolls = 0, confirmations = 0, clicks = 0;
            h.Readings = () =>
            {
                lobby["Selected"] = ui.Selected;
                return [Reading("event.lobby", ("$self", lobby)), Reading("event.currency", ("EventApFree", free), ("EventApStack", 123)),
                    Reading("event.sweep.popup", ("_buttonFreeOnly.IsEnable", true), (DailyEventSweep.Count, count),
                        ("ὩὥὭὬὬὥὡὠὥὧὭ", "PackEventBattle"), ("ὥὩὢὦὫὩὭὬὬὦὥ", "Skip"),
                        ("ὠὬὦὫὩὨὣὫὤὢὨ", 10), ("ὦὠὠὧὩὩὮὥὫὧὡ", ui.Detail), ("ὪὪὥὧὡὪὥὡὡὣὡ", 1000 + ui.Detail), ("ὩὤὦὯὯὨὫὧὥὡὯ", 1))];
            };
            void LobbyPage() => h.Page("EventBattleUI", "_buttonSkipBattle._button");
            LobbyPage();
            h.OnCommand = c =>
            {
                switch (S(c["Kind"]))
                {
                    case "event_sweep_select":
                        Check(N(c["Items"]![2]) == 5, "Sweep selects cleared stage 5, never the unpassed frontier 15");
                        if (N(c["Value"]) == 0)
                        {
                            scrolls++; row.Id = 5; ui.SetSelectItem(row, false);
                            Check(ui.Detail == 15, "Scroll alone does not imply detail readiness");
                        }
                        else { selections++; EventStageSelection.Select(ui, row, ui.SetSelectItem); }
                        break;
                    case "click":
                        Check(ui.Detail == 5, "Sweep preview opens only after the correct native stage detail refresh");
                        clicks++; h.Page("BattleSkipPopupUI"); break;
                    case "event_sweep_count": count = I(c["Value"]); break;
                    case "event_sweep_confirm":
                        Check(N(c["Items"]![2]) == 5 && N(c["Items"]![3]) == 1005 && ui.Detail == 5, "Confirmation uses the cleared stage and matching native popup");
                        confirmations++; free -= count; LobbyPage(); break;
                    default: throw new Exception("Unexpected action: " + S(c["Kind"]));
                }
            };
            var target = DailyEventSweep.Highest(lobby)!;
            target["policy"] = DailyEventSweep.NativePolicy;
            lobby["Selected"] = initialSelected;
            var result = await DailyEventSweep.Run(h.Workflow, lobby, fixedBudget ? target : null, fixedBudget ? 5 : null);
            Check(S(result["state"]) == "completed" && confirmations == 1 && free == 0 && clicks == 1,
                $"Stale stage 15 detail recovers and sweeps remaining free attempts once: selected={initialSelected}, budget={fixedBudget}");
            Check(selections == 1 && scrolls == (initialSelected == 5 ? 0 : 1), "Always refresh detail; avoid unnecessary scroll");
            result = await DailyEventSweep.Run(h.Workflow, lobby);
            Check(S(result["state"]) == "completed" && confirmations == 1 && selections == 1, "Repeated continuation after exhausted AP cannot select or sweep again");
        }

        using var uncleared = new WorkflowHarness(Path.Combine(output, "event-no-clear-then-manual-clear"), [DailyEventSweep.Definition()]);
        var current = EventTradeBoundaryCases.Lobby(); current["Stages"] = new JsonArray(EventTradeBoundaryCases.Stage(15, false));
        uncleared.Page("EventBattleUI");
        uncleared.Readings = () => [Reading("event.currency", ("EventApFree", 4), ("EventApStack", 123))];
        int commands = 0; uncleared.OnCommand = _ => commands++;
        var partial = await DailyEventSweep.Run(uncleared.Workflow, current);
        Check(S(partial["state"]) == "partial" && commands == 0, "No cleared stage leaves a recoverable partial result without trying stage 15");
        current["Stages"] = new JsonArray(EventTradeBoundaryCases.Stage(15, true));
        Check(N(DailyEventSweep.Highest(current)!["stage"]) == 15, "Fresh manual completion makes stage 15 eligible without clearing local history");
    }
}

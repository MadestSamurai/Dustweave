using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class EventSweepCountCases
{
    private static JsonObject Read(string id, params (string Key, object? Value)[] fields) => O(("Id", id), ("Error", ""), ("Values", new JsonArray(O(fields).Select(p => (JsonNode)O(("Path", p.Key), ("Error", ""), ("Json", p.Value?.ToJsonString() ?? "null"))).ToArray())));
    public static async Task Run(string output, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        foreach (var scenario in new (int Free, int Cost, int? Budget, int Expected)[] { (5, 1, 5, 5), (3, 1, (int?)5, 3), (9, 2, (int?)5, 4),
            (99, 1, (int?)2, 2), (5, 1, (int?)1, 1), (500, 1, (int?)null, 500) })
        {
            using var h = new WorkflowHarness(Path.Combine(output, "sweep-count-" + Guid.NewGuid().ToString("N")), []);
            long selected = 1; int calls = 0;
            h.Readings = () => [Read("event.currency", ("EventApFree", scenario.Free), ("EventApStack", 9999)),
                Read("event.sweep.popup", (DailyEventSweep.Count, selected))];
            h.Page("BattleSkipPopupUI");
            h.OnCommand = c =>
            {
                Check(S(c["Kind"]) == "event_sweep_count", "Sweep selection does not use incremental clicks or consume AP");
                Check(c["Items"]!.AsArray().Select(N).SequenceEqual(new long[] { 100, 10, 1, 1001 }), "Direct quantity selection retains exact event scope");
                calls++; selected = N(c["Value"]);
            };
            var target = O(("event", 100), ("group", 10), ("stage", 1), ("deck", 1001), ("cost", scenario.Cost));
            await DailyEventSweep.SelectCount(h.Workflow, target, scenario.Budget);
            Check(calls == 1 && selected == scenario.Expected, $"One command selects {scenario.Expected} free sweeps within budget");
        }
        Check(DailyEventSweep.SweepCount(0, 1, 5) == 0 && DailyEventSweep.SweepCount(1, 2, 5) == 0, "No free AP never implies stored AP spending");
        foreach (var test in new (long Free, long Cost, int Budget)[] { (-1L, 1L, 5), (5L, 0L, 5), (5L, 1L, 0), (5L, 1L, 6) })
        {
            try { DailyEventSweep.SweepCount(test.Free, test.Cost, test.Budget); throw new Exception("Invalid sweep budget accepted"); }
            catch (InvalidDataException) { checks.Add("Malformed sweep budget is rejected"); }
        }
        using var clamped = new WorkflowHarness(Path.Combine(output, "sweep-clamped"), []);
        clamped.Page("BattleSkipPopupUI");
        clamped.Readings = () => [Read("event.currency", ("EventApFree", 5)), Read("event.sweep.popup", (DailyEventSweep.Count, 1))];
        int commands = 0; clamped.OnCommand = _ => commands++;
        try { await DailyEventSweep.SelectCount(clamped.Workflow, O(("event", 100), ("group", 10), ("stage", 1), ("deck", 1001), ("cost", 1)), 5); throw new Exception("Unapplied count was accepted"); }
        catch (StageHostException) { Check(commands == 1, "Unapplied or clamped selection does not submit a sweep or loop clicks"); }
    }
}

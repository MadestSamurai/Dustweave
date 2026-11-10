using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>One native batch; locked boxes stay intact and uncertain batches are never replayed.</summary>
public static class DailyTradeBoxes
{
    private static readonly string[] Prefixes = ["trade.boxes"];
    private static JsonObject StateOf(JsonObject e) => State(e, "trade.boxes", "$self");
    public static JsonObject[] Available(JsonObject state) => Rows(state["Boxes"])
        .Where(x => x["Keep"] != null && N(x["Keep"]) == 0 && N(x["Type"]) == 9 && N(x["Count"]) > 0)
        .OrderBy(x => N(x["Instance"])).ToArray();
    public static async Task Open(DailyWorkflow w)
    {
        if (!w.Settings.Trade.Enabled || !w.Settings.Trade.OpenBoxesBeforeTrade) return;
        string path = Path.Combine(w.Root, "live", "trade-boxes", S(w.Context["actor"]![3]) + ".json");
        async Task<JsonObject> Read() => StateOf(await w.Evidence(Prefixes));
        var state = await Read();
        var previous = DailyJson.TryRead<JsonObject>(path);
        var frame = (await w.Observe()).Frame;
        bool sameProcess = previous != null && S(previous["state"]) == "pending" &&
            N(previous["process"]) == N(frame["ProcessId"]) && N(previous["started"]) == N(frame["ProcessStartTicks"]);
        if (sameProcess)
        {
            if (B(state["Pending"]) || B(state["Busy"])) { await WaitBatch(); state = await Read(); }
            if (previous!["boxes"] is JsonArray selection && Rows(selection).Any(old =>
                Rows(state["Boxes"]).Any(x => N(x["Instance"]) == N(old["Instance"]) && N(x["Count"]) > 0)))
                w.Report("tradeboxes.unconfirmed");
            await Finish();
            previous!["state"] = "completed"; DailyJson.Write(path, previous); return;
        }
        if (B(state["Pending"])) { await WaitBatch(); await Finish(); return; }
        if (B(state["Busy"])) { await WaitBatch(); state = await Read(); }
        var selected = Available(state);
        if (selected.Length == 0) { if (await w.Has("InventoryManageUI")) await Finish(); return; }
        if (B(state["Full"])) { w.Report("tradeboxes.full"); return; }
        if (!await w.Has("InventoryManageUI"))
        {
            await DailyResults.Cleanup(w, "MenuUI");
            await w.Step("MenuUI", "_buttonInventory", expect: "InventoryManageUI");
        }
        state = await Read(); selected = Available(state);
        if (selected.Length == 0 || B(state["Full"])) { await DailyResults.Cleanup(w, "MenuUI"); return; }
        frame = (await w.Observe()).Frame;
        var journal = O(("state", "pending"), ("process", frame["ProcessId"]), ("started", frame["ProcessStartTicks"]),
            ("boxes", Array(selected)), ("at", w.Driver.UtcTicks));
        DailyJson.Write(path, journal);
        w.StepProgress("trade_boxes");
        try
        {
            await w.Step(O(("ui", "InventoryManageUI"), ("operation", "trade_boxes_open"), ("value", 1),
                ("items", selected.SelectMany(x => new[] { N(x["Instance"]), N(x["Id"]), N(x["Count"]) }).ToArray())));
        }
        catch (DailyStepException error) when (!error.Submitted && error.Kind == "rejected")
        { journal["state"] = "not_sent"; DailyJson.Write(path, journal); throw; }
        await WaitBatch(N(state["Revision"]) + 1);
        journal["state"] = "completed"; journal["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, journal);
        await Finish();

        async Task Finish()
        {
            // The reward closes onto inventory first. Waiting for MenuUI while
            // inventory is still open would delay an already completed batch.
            if (await w.Has("InventoryManageUI")) await DailyResults.Cleanup(w, "InventoryManageUI");
            await DailyResults.Cleanup(w, "MenuUI");
        }

        async Task WaitBatch(long minimumRevision = 0)
        {
            double end = w.Time + 60, absolute = w.Time + 1800;
            long count = long.MaxValue;
            while (w.Time < end && w.Time < absolute)
            {
                var current = await Read();
                long left = Rows(current["Boxes"]).Sum(x => N(x["Count"]));
                if (left < count) { count = left; end = w.Time + 60; }
                if (N(current["Revision"]) >= minimumRevision && !B(current["Pending"]) && !B(current["Busy"])) return;
                await w.Delay(150);
            }
            throw new StageHostException("adapter", "tradeboxes.waiting");
        }
    }
}

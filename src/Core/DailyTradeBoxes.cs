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
        bool sameProcess = previous != null &&
            N(previous["process"]) == N(frame["ProcessId"]) && N(previous["started"]) == N(frame["ProcessStartTicks"]);
        bool resume = previous != null && (S(previous["state"]) == "presentation_pending" ||
            (sameProcess && S(previous["state"]) == "pending"));
        if (resume)
        {
            bool activeBatch = B(state["Pending"]);
            if (activeBatch || B(state["Busy"])) { await WaitBatch(); state = await Read(); }
            if (previous!["boxes"] is JsonArray selection && Rows(selection).Any(old =>
                Rows(state["Boxes"]).Any(x => N(x["Instance"]) == N(old["Instance"]) && N(x["Count"]) > 0)))
                w.Report("tradeboxes.unconfirmed");
            PreparePresentation(previous!);
            await Finish(previous, awaitAppearance: activeBatch, sameProcess: sameProcess);
            previous!["state"] = "completed"; previous["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, previous); return;
        }
        if (B(state["Pending"]))
        {
            await WaitBatch();
            var existing = O(("process", frame["ProcessId"]), ("started", frame["ProcessStartTicks"]));
            PreparePresentation(existing);
            await Finish(existing, awaitAppearance: true);
            existing["state"] = "completed"; existing["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, existing); return;
        }
        if (B(state["Busy"])) { await WaitBatch(); state = await Read(); }
        // Older builds could record the batch as completed before the delayed
        // reward appeared. Recover that visible presentation before any new batch.
        if (await w.Has("RewardReceivePopupUI")) { await Finish(); state = await Read(); }
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
        PreparePresentation(journal);
        await Finish(journal, awaitAppearance: true);
        journal["state"] = "completed"; journal["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, journal);

        void PreparePresentation(JsonObject batch)
        {
            batch["state"] = "presentation_pending";
            if (N(batch["settled"]) <= 0) batch["settled"] = w.Driver.UtcTicks;
            DailyJson.Write(path, batch);
        }

        async Task Finish(JsonObject? batch = null, bool awaitAppearance = false, bool sameProcess = true)
        {
            // OpenBoxItem finishes the network batch after requesting an ASYNC
            // reward window. Inventory and the underlying menu can still be ready
            // before that window arrives. Fresh batches must observe it first;
            // A cancelled wait keeps its original deadline. After that deadline,
            // an already-dismissed result can resume from a stable destination.
            // A restarted game cannot retain the old asynchronous UI request.
            double remaining = awaitAppearance ? 40 : batch != null && sameProcess && !B(batch["reward_seen"]) && await w.Has("InventoryManageUI")
                ? Math.Clamp(40 - (w.Driver.UtcTicks - N(batch["settled"])) / (double)TimeSpan.TicksPerSecond, 0, 40) : 0;
            if (remaining > 0)
                await w.Wait(f => DailyNavigationDecision.Types(f.Frame).Contains("RewardReceivePopupUI") ||
                    (!awaitAppearance && !DailyNavigationDecision.Types(f.Frame).Contains("InventoryManageUI")), remaining, "tradeboxes.reward_waiting");
            if (batch != null && await w.Has("RewardReceivePopupUI"))
            { batch["reward_seen"] = true; DailyJson.Write(path, batch); }
            await w.Driver.DismissRewardAsync(["InventoryManageUI", "MenuUI", "GameFieldDefaultUI"], true);
            // The reward returns to inventory, not directly to MenuUI. Only after
            // presentation cleanup may the inventory back action be submitted.
            if (await w.Has("InventoryManageUI"))
                await w.Step("InventoryManageUI", back: true, absent: "InventoryManageUI", reason: "结束已确认的操作页面");
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

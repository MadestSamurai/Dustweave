using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Reapply the selected ordinary preset before starting a new event batch.</summary>
public static class DailyEventPreset
{
    public static readonly string[] Prefixes = ["event.preset"];
    private static JsonObject StateOf(JsonObject e) => State(e, "event.preset", "$self");
    public static async Task Apply(DailyWorkflow w)
    {
        int slot = w.Settings.EventBattle.PresetSlot;
        if (!w.Settings.EventBattle.Enabled || slot == 0) return;
        Require(slot is >= 1 and <= 999, "prefs.event_preset_invalid");
        w.StepProgress("event_preset_read");
        var before = await w.Evidence(Prefixes);
        long revision = N(StateOf(before)["Revision"]);
        await w.Step("EventBattleUI", operation: "event_preset_read", value: slot);
        var read = await w.WaitEvidence(Prefixes, e => N(StateOf(e)["Revision"]) > revision &&
            I(StateOf(e)["Slot"]) == slot && S(StateOf(e)["State"]) is "ready" or "empty" or "failed",
            45, "event_preset_read_timeout");
        Identity(before, read);
        var state = StateOf(read);
        if (S(state["State"]) == "failed") throw new StageHostException("adapter", "event_preset_read_failed");
        if (S(state["State"]) == "empty") { w.StepProgress("event_preset_empty"); return; }
        revision = N(state["Revision"]);
        w.StepProgress("event_preset_apply");
        await w.Step(O(("ui", "EventBattleUI"), ("operation", "event_preset_apply"),
            ("value", slot), ("items", new JsonArray(JsonValue.Create(revision)))));
        var applied = await w.WaitEvidence(Prefixes, e => N(StateOf(e)["Revision"]) == revision &&
            I(StateOf(e)["Slot"]) == slot && S(StateOf(e)["State"]) is "applied" or "empty" or "failed",
            45, "event_preset_apply_timeout");
        Identity(read, applied);
        if (S(StateOf(applied)["State"]) == "failed") throw new StageHostException("adapter", "event_preset_apply_failed");
        if (S(StateOf(applied)["State"]) == "empty") w.StepProgress("event_preset_empty");
    }

    public static async Task<JsonObject> RunExtension(DailyWorkflow w, Func<Task<JsonObject>> run)
    {
        if (w.Settings.EventBattle.Enabled && w.Settings.EventBattle.PresetSlot != 0 &&
            !DailyNavigationDecision.Types((await w.Observe()).Frame).Overlaps(["BattleUI_EventBattle", "BattleResultUI"]))
        {
            await DailyEventNative.Lobby(w, false);
            if (N(R(await w.Evidence(DailyEventNative.Prefixes), "event.currency", "EventApFree")) > 0)
                await Apply(w);
        }
        return await run();
    }
}

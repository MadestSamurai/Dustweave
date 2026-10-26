using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Free native minimap destinations. Walking distance is zero; loading time is not.</summary>
public static class DailyQuickTravel
{
    public const double TransitionSeconds = 5, SameMapTransitionSeconds = 3.5;
    public static JsonObject State(JsonObject evidence) => Readings(evidence, "route.quick").FirstOrDefault()?["$self"] as JsonObject ?? new();
    public static JsonObject[] Targets(JsonObject evidence) => Rows(State(evidence)["Targets"] ?? new JsonArray());

    public static async Task<bool> TryMove(DailyWorkflow w, long npc, long destination)
    {
        var before = await w.Evidence("route.quick");
        var target = Targets(before).SingleOrDefault(t => N(t["Npc"]) == npc && N(t["Map"]) == destination);
        if (target == null || B(target["AtTarget"])) return false;
        try
        {
            await w.Step(O(("ui", "GameFieldDefaultUI"), ("operation", "field_quick_travel"),
                ("value", npc), ("items", new[] { destination }), ("reason", "通过快速移动前往" + S(target["Name"]))));
        }
        catch (DailyStepException error) when (error.DefinitelyNotDispatched && error.RejectionCode == "quick_travel_unavailable") { return false; }
        double end = w.Time + 60;
        double? readySince = null;
        while (w.Time < end)
        {
            var evidence = await w.Evidence("route.quick");
            DailyData.Identity(before, evidence);
            var state = State(evidence);
            bool ready = DailyFieldRoute.FieldInputReady(evidence["Frame"]!.AsObject());
            if (ready && N(state["Npc"]) == npc && S(state["State"]) == "arrived" && N(state["Map"]) == destination) return true;
            // Native landing recovery may return to a safe map instead of its target.
            // Fall back only after the field has actually settled, never mid-loading.
            if (ready) readySince ??= w.Time; else readySince = null;
            if (readySince.HasValue && w.Time - readySince.Value >= 5)
            {
                w.Driver.Diagnostics.Event("quick_travel_not_arrived", O(("npc", npc), ("destination", destination), ("state", state)));
                return false;
            }
            await w.Delay(200);
        }
        throw new DailyTravelBlocked("快速移动后地图仍在加载，已保留当前路线。");
    }
}

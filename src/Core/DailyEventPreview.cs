using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Reopen a lost, unsubmitted event preview only while its native budget and stage are unchanged.</summary>
internal static class DailyEventPreview
{
    private static readonly string[] Prefixes = ["event.lobby", "event.currency", "event.sweep", "event.native"];
    internal static async Task<JsonObject> Run(DailyWorkflow w, string opener, Func<Task> configure, Func<JsonObject, Task<JsonObject>> confirm)
    {
        var baseline = await w.Evidence(Prefixes);
        for (int attempt = 0; ; attempt++)
        {
            bool confirming = false;
            try
            {
                await w.Target("EventBattleUI", opener);
                await w.Step("EventBattleUI", opener, expect: "BattleSkipPopupUI");
                await w.Wait(f =>
                {
                    var rows = DailyNavigationDecision.Rows(f.Frame).Where(r => S(r["Type"]) == "BattleSkipPopupUI").ToArray();
                    if (rows.Length == 0)
                        throw new DailyStepException("rejected", "Event preview closed before confirmation") { RejectionCode = "surface_missing" };
                    return rows.Length == 1 && DailyNavigationDecision.ReadyInput(rows[0])
                        && DailyNavigationDecision.Blockers(f.Frame, "BattleSkipPopupUI", DailyNavigationPolicy.Load()).Length == 0;
                }, error: "活动确认窗口尚未就绪，未开始战斗或扫荡。");
                await configure();
                var evidence = await w.Evidence(Prefixes);
                Present(evidence);
                Require(Unchanged(baseline, evidence), "Event stage or AP changed before confirmation");
                confirming = true;
                return await confirm(evidence);
            }
            catch (Exception error) when (error is DailyStepException { PresentationChanged: true, RejectionCode: not "scene_changed" }
                || !confirming && error is InvalidDataException && error.Message.StartsWith("Reading unavailable: event.sweep.popup", StringComparison.Ordinal))
            {
                // An ambiguous dispatch/timeout never enters here. Do not close another
                // popup or replay a consuming command just because its window vanished.
                var after = await w.Evidence(Prefixes);
                var frame = (await w.Observe()).Frame;
                bool lobby = DailyNavigationDecision.Rows(frame).Count(r => S(r["Type"]) == "EventBattleUI" && DailyNavigationDecision.ReadyInput(r)) == 1
                    && !DailyNavigationDecision.Types(frame).Contains("BattleSkipPopupUI")
                    && DailyNavigationDecision.Blockers(frame, "EventBattleUI", DailyNavigationPolicy.Load()).Length == 0;
                bool unchanged = Unchanged(baseline, after);
                w.Save("event-preview-recovery.json", O(("at", w.Driver.UtcTicks), ("attempt", attempt + 1), ("opener", opener),
                    ("error", error.Message), ("confirmation_not_submitted", true), ("lobby_ready", lobby), ("unchanged", unchanged),
                    ("before", Snapshot(baseline)), ("after", Snapshot(after)), ("frame", frame)));
                if (!lobby || !unchanged) throw;
                if (attempt >= 2)
                    throw new StageHostException("adapter", "活动确认窗口反复关闭，本环节已暂缓；未重复消耗次数，可稍后重跑。");
            }
        }
    }
    internal static void Present(JsonObject evidence)
    {
        if (!DailyNavigationDecision.Types(evidence["Frame"]!.AsObject()).Contains("BattleSkipPopupUI"))
            throw new DailyStepException("rejected", "Event preview closed before confirmation") { RejectionCode = "surface_missing" };
    }
    internal static Task<JsonObject> WaitValue(DailyWorkflow w, string[] prefixes, Func<JsonObject, bool> ready) =>
        w.WaitEvidence(prefixes, e => { Present(e); return ready(e); });
    internal static JsonObject Snapshot(JsonObject e)
    {
        var lobby = DailyEventSweep.Lobby(e);
        return O(("event", lobby["Event"]), ("group", lobby["Group"]), ("category", lobby["Category"]),
            ("selected", lobby["Selected"]), ("eligible", lobby["Eligible"]), ("stages", lobby["Stages"]),
            ("free", R(e, "event.currency", "EventApFree")), ("stored", R(e, "event.currency", "EventApStack")));
    }
    internal static bool Unchanged(JsonObject before, JsonObject after)
    {
        Identity(before, after);
        Require(S(before["Frame"]?["Scene"]) == S(after["Frame"]?["Scene"]), "Event preview scene changed");
        return JsonNode.DeepEquals(Snapshot(before), Snapshot(after));
    }
}

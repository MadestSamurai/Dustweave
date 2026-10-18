using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

internal static class DailyNativeBattleRecovery
{
    internal static bool SameAccount(JsonObject op, DailyStageFrame f) => op["before"]?["Frame"] is JsonObject prior &&
        new[] { "AccountKey", "PlayerKey" }.All(k => prior[k] != null && JsonNode.DeepEquals(prior[k], f.Frame[k])) &&
        JsonNode.DeepEquals(op["account"], f.Context["actor"]?[3]) && JsonNode.DeepEquals(op["player"], f.Context["actor"]?[4]) &&
        op["server"] != null && JsonNode.DeepEquals(op["server"], f.Context["server"]);

    internal static bool CanResume(JsonObject op, DailyStageFrame f)
    {
        if (!SameAccount(op, f)) return false;
        var types = DailyNavigationDecision.Types(f.Frame);
        if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)) || types.Contains("BattleResultUI"))
            return S(op["role"]) == "event.native_batch" && DailyWorkflowRegistry.Owned(op, f) &&
                !types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal) && t != "BattleUI_EventBattle") &&
                (types.Contains("BattleUI_EventBattle") || types.Contains("BattleResultUI")) &&
                (DailyManagedBusiness.Pending(op) || S(op["state"]) == "completed");
        return types.Overlaps(S(op["role"]) == "salvation.sweep"
            ? ["MenuUI", "GameFieldDefaultUI", "EvilCastleEnterUI", "RogueLikeLobbyUI", "RogueLikeSkipPopupUI", "RogueLikeTotalScoreUI"]
            : new[] { "MenuUI", "GameFieldDefaultUI", "EventMainUI", "EventBattleUI" });
    }

    internal static async Task Replan(DailyWorkflow w, string role)
    {
        var pending = w.Business.Records(w.Context, role).Where(DailyManagedBusiness.Pending).ToArray();
        if (pending.Length == 0) return;
        var e = await w.Evidence(role == "salvation.sweep" ? DailySalvation.Prefixes : ["event.lobby", "event.currency", "event.native"]);
        var f = new DailyStageFrame(e["Frame"]!.AsObject(), w.Context);
        string lobby = role == "salvation.sweep" ? "RogueLikeLobbyUI" : "EventBattleUI";
        Require(!DailyNavigationDecision.Types(f.Frame).Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)) &&
            DailyNavigationDecision.Rows(f.Frame).Any(r => S(r["Type"]) == lobby && DailyNavigationDecision.ReadyInput(r, false)) &&
            DailyNavigationDecision.Blockers(f.Frame, lobby, DailyNavigationPolicy.Load()).Length == 0, "Native battle recovery requires an idle lobby");
        if (role == "salvation.sweep")
        {
            var s = DailySalvation.StateOf(e); Require(B(s["Ready"]) && B(s["Idle"]) && !B(s["Popup"]), "Tower is not idle");
            _ = DailySalvation.Plan(s, w.Settings.Salvation.ReserveKeys, w.Settings.Salvation.UseFixedCount ? w.Settings.Salvation.SweepCount : 0);
        }
        else
        {
            Require(!B(DailyEventNative.Native(e)["Battle"]), "Event battle is still active");
            _ = DailyEventNative.Plan(DailyEventSweep.Lobby(e), N(R(e, "event.currency", "EventApFree")));
            Require(N(R(e, "event.currency", "EventApStack")) >= 0, "Event AP data unavailable");
        }
        foreach (var op in pending)
        {
            Require(SameAccount(op, f), "Native battle recovery account changed");
            op["state"] = "superseded";
            op["reconciliation"] = O(("method", "fresh_native_lobby_budget"), ("actions", 0), ("completion_claimed", false), ("at", w.Driver.UtcTicks));
            op["last_observation"] = Copy(e); op.Remove("error"); w.Business.Save(op);
        }
    }
}

using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyMenuNavigation
{
    private static readonly IReadOnlyDictionary<string, string> FieldMenus = DailyNavigationPolicy.Load().FieldMenu;
    // Only these reviewed open-page/preview inputs are repeatable. In particular, guild
    // entry can claim attendance; back, talent, reward and battle inputs are not
    // menu retries, even when they also name an expected page.
    internal static bool CanRepeatOpen(JsonObject action) =>
        action.All(p => p.Key is "ui" or "field" or "expect" or "reason" or "timeout") &&
        (DailyData.S(action["ui"]), DailyData.S(action["field"]), DailyData.S(action["expect"])) is
            ("GameFieldDefaultUI", "_buttonPackList", "PackListUI") or
            ("GameFieldDefaultUI", "_buttonMenu", "MenuUI") or
            ("MenuUI", "_buttonInventory", "InventoryManageUI") or
            ("MenuUI", "_buttonMission", "MissionUI") or
            ("MenuUI", "_buttonEvent", "EventUI") or
            ("MenuUI", "_buttonPass", "PassUI") or
            ("MenuUI", "_buttonMiniGame", "MiniGameHubUI") or
            ("MenuUI", "_goQuickHuntDispatchButton", "HuntOrAirwayUI") or
            ("HuntOrAirwayUI", DailyHunting.SelectField, "HuntDispatchPopupUI") or
            ("EventBattleUI", "_buttonSkipBattle._button", "BattleSkipPopupUI") or
            ("EventBattleUI", "_buttonAutoBattle._button", "BattleSkipPopupUI");

    internal static string OpenRoute(JsonObject action) => DailyData.S(action["field"]).StartsWith("$pointer/", StringComparison.Ordinal) ? "pointer" : "ui";

    internal static bool InputRouteReady(JsonObject action, JsonObject frame)
    {
        // Hunt buttons are forwarded through WorldMapUI. The child can report
        // ready before that owner finishes changing tabs. Check this only after
        // an opener failed to appear, not on every normal click. Do not impose this
        // owner's contract on independent children elsewhere in the game.
        if (DailyData.S(action["ui"]) != "HuntOrAirwayUI" || OpenRoute(action) != "pointer") return true;
        return DailyNavigationDecision.Rows(frame).Where(r => DailyData.S(r["Type"]) == "WorldMapUI")
            .All(r => DailyNavigationDecision.ReadyInput(r));
    }

    internal static bool RepeatOpenReady(JsonObject action, JsonObject frame, JsonObject original)
    {
        if (!CanRepeatOpen(action) || !DailyEvidence.SameActor(frame, original)
            || !JsonNode.DeepEquals(frame["Scene"], original["Scene"])) return false;
        string ui = DailyData.S(action["ui"]);
        if (!DailyNavigationDecision.SurfaceSceneReady(frame, ui) || DailyNavigationDecision.SceneLoading(frame) || !InputRouteReady(action, frame)) return false;
        var rows = DailyNavigationDecision.Rows(frame).ToArray();
        var sources = rows.Where(r => DailyData.S(r["Type"]) == ui).ToArray();
        if (sources.Length != 1 || !DailyNavigationDecision.ReadyInput(sources[0], false)) return false;
        bool Parent(JsonObject row) => !DailyData.B(row["Popup"]) &&
            (ui == "HuntOrAirwayUI" && DailyData.S(row["Type"]) == "WorldMapUI"
            || DailyData.S(row["Path"]).Length > 0 && DailyData.S(sources[0]["Path"]).StartsWith(DailyData.S(row["Path"]) + "/", StringComparison.Ordinal));
        // A live loading/battle/new page or modal is observed, never clicked
        // through. Passive HUD rows (including empty NoticeUI) remain harmless.
        if (rows.Any(r => DailyData.S(r["Type"]) != ui && !DailyNavigationDecision.PassiveSurface(DailyData.S(r["Type"]))
            && !Parent(r) && !(ui == "MenuUI" && !DailyData.B(r["Popup"]) && FieldMenus.ContainsKey(DailyData.S(r["Type"]))))) return false;
        var targets = (sources[0]["Targets"] as JsonArray)?.OfType<JsonObject>()
            .Where(t => DailyData.S(t["Field"]) == DailyData.S(action["field"]) && DailyData.B(t["Enabled"])).ToArray();
        return targets?.Length == 1 && DailyData.S(targets[0]["Route"]) == OpenRoute(action);
    }

    // MenuUI.OnClickBackButton means Quit on PC; UIBase.OnClickUI(_objBackButton)
    // closes the menu and returns to the loaded cartridge. Never fall back to ESC.
    public static JsonObject Normalize(JsonObject action)
    {
        if (action["ui"]?.GetValue<string>() != "MenuUI" || action["back"]?.GetValue<bool>() != true)
            return action;
        var result = action.DeepClone().AsObject();
        result.Remove("back");
        result.Remove("target_id");
        result["field"] = "_objBackButton";
        return result;
    }
}

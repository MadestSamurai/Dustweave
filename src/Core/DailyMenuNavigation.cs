using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyMenuNavigation
{
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

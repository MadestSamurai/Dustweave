using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>The rotating native settlement banners all open the same management popup.</summary>
public static class DailyManagementEntry
{
    private const string Prefix = "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/";
    private static bool SettlementBanner(JsonObject target) => DailyNavigationDecision.Text(target, "Field") is
        Prefix + "ScrollRect/Viewport/Content1" or Prefix + "ScrollRect/Viewport/Content2";

    public static JsonObject? Select(JsonObject frame)
    {
        var menus = DailyNavigationDecision.Rows(frame).Where(r => DailyNavigationDecision.Text(r, "Type") == "MenuUI").ToArray();
        if (menus.Length != 1 || DailyNavigationDecision.Blockers(frame, "MenuUI", DailyNavigationPolicy.Load()).Length > 0)
            throw new StageHostException("adapter", "经营入口需要已确认的主菜单，保留当前页面。");
        if (!DailyNavigationDecision.ReadyInput(menus[0])) return null;
        var targets = menus[0]["Targets"]!.AsArray().OfType<JsonObject>()
            .Where(t => t["Enabled"]?.GetValue<bool>() == true && DailyNavigationDecision.Text(t, "Field").StartsWith(Prefix, StringComparison.Ordinal))
            .OrderBy(t => DailyNavigationDecision.Text(t, "Field"), StringComparer.Ordinal).ToArray();
        // MenuUI_ManagementLayout.OnClickItem sends every SettlementButtonBase to OpenPopup.
        // Only these known rotating item roots are equivalent; unrelated children remain ambiguous.
        if (targets.Length > 1 && !targets.All(SettlementBanner))
            throw new StageHostException("adapter", "经营入口出现未识别的点击区域，保留现场。");
        return targets.FirstOrDefault();
    }

    public static async Task OpenAsync(DailyCommandDriver driver, Func<bool> stopped)
    {
        double end = driver.MonotonicTime + 25;
        while (driver.MonotonicTime < end)
        {
            if (stopped()) throw new StageHostException("stopped", "经营入口检查已停止。");
            var frame = (await driver.ObserveAsync()).Frame;
            if (DailyNavigationDecision.Types(frame).Contains("ManagementRewardPopupUI")) return;
            var target = Select(frame);
            if (target != null)
            {
                await driver.SendObservedAsync(new() { ["ui"] = "MenuUI", ["field"] = target["Field"]!.DeepClone(), ["target_id"] = target["Id"]!.DeepClone(), ["expect"] = "ManagementRewardPopupUI", ["reason"] = "检查已累积的经营收益" });
                return;
            }
            await driver.DelayAsync(TimeSpan.FromMilliseconds(200));
        }
        throw new StageHostException("adapter", "经营入口未就绪，未提交领取。");
    }
}

using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;
public static class DailySquareNavigation
{
    public static bool MerchantArrived(JsonObject frame,bool nativeNear) => nativeNear
        && S(frame["SquareNavigation"]?["Kind"])=="square_shop_nav"
        && S(frame["SquareNavigation"]?["State"])=="arrived";
    public static void Inspect(JsonObject frame, string kind)
    {
        if (!S(frame["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal)) throw new DailyTravelBlocked("广场移动期间场景改变");
        if (frame["SquareNavigation"] is not JsonObject route || S(route["Kind"]) != kind) return;
        if (S(route["State"]) is "failed" or "cancelled") throw new DailyTravelBlocked("广场 A* 移动未完成：" + S(route["Reason"]));
    }
    public static async Task Wait(DailyWorkflow w, string kind, Func<Task<bool>> arrived)
    {
        double until = w.Time + 245;
        while (w.Time < until)
        {
            if (await arrived()) return;
            Inspect((await w.Observe()).Frame, kind);
            await w.Delay(200);
        }
        throw new DailyTravelBlocked("广场移动超出时间预算，已保留原目标");
    }
    public static async Task Stop(DailyWorkflow w)
    {
        var frame = (await w.Observe()).Frame;
        // The pause channel also cancels native movement when an overlay blocks this explicit command.
        if (S(frame["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && DailyNavigationDecision.Types(frame).Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(frame,"GameFieldDefaultUI",DailyNavigationPolicy.Load()).Length==0)
            await w.Step("GameFieldDefaultUI",operation:"square_cancel_nav",reason:"结束广场移动，保留原奖励和交易记录");
    }
}
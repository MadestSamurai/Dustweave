using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

internal static class DailyTradePreview
{
    private static readonly DailyNavigationPolicy Policy = DailyNavigationPolicy.Load();
    public static bool IsConfirmation(JsonObject action) => (S(action["operation"]), S(action["ui"])) is
        ("trade_sell_confirm" or "trade_buy_confirm", "ShopPopupUI") or ("trade_favorite_confirm", "BuyFavoritePopupUI");

    public static bool Matches(JsonObject action, JsonObject owned, JsonObject current)
    {
        if (!DailyEvidence.SameActor(owned, current) || !JsonNode.DeepEquals(owned["Scene"], current["Scene"])) return false;
        if (!IsConfirmation(action)) return JsonNode.DeepEquals(owned["UiToken"], current["UiToken"]);

        // The global token also hashes the pooled rows behind the popup. Refreshing
        // those rows must not replace an otherwise identical transaction preview.
        // Native dispatch still gates on the latest global token and validates the
        // actual merchant, product, stack, quantity, price and balance before input.
        string ui = S(action["ui"]);
        var before = DailyNavigationDecision.Rows(owned).Where(s=>!Policy.Background.Contains(S(s["Type"]))).ToArray();
        var after = DailyNavigationDecision.Rows(current).Where(s=>!Policy.Background.Contains(S(s["Type"]))).ToArray();
        if (before.Length != after.Length || before.Count(s => S(s["Type"]) == ui) != 1 || after.Count(s => S(s["Type"]) == ui) != 1
            || before.Count(s => S(s["Type"]) == "ShopUI") != 1 || after.Count(s => S(s["Type"]) == "ShopUI") != 1) return false;
        foreach (var previous in before)
        {
            var matches = after.Where(s => JsonNode.DeepEquals(s["Id"], previous["Id"]) && S(s["Type"]) == S(previous["Type"])).ToArray();
            if (matches.Length != 1) return false;
            var next = matches[0];
            string type = S(previous["Type"]);
            if (type == ui)
            {
                if (!DailyNavigationDecision.ReadyInput(previous, fallback: false) || !SameSurface(previous, next, "InputReady")) return false;
            }
            else if (type == "ShopUI")
            {
                if (!SameSurface(previous, next, "Targets", "Text")) return false;
            }
            else if (!JsonNode.DeepEquals(previous, next)) return false;
        }
        return DailyNavigationDecision.Blockers(current, ui, Policy).Length == 0;
    }

    public static bool CanReplan(JsonObject op, JsonObject current, Exception error)
    {
        // Only a local preflight rejection proves that no purchase/sale was sent.
        if (error is not DailyStepException {Kind:"rejected",Submitted:false,Command:null} || !error.Message.StartsWith("Owned confirmation changed",StringComparison.Ordinal)
            || S(op["state"])!="rejected" || op["command_id"]!=null || op["preview_frame"] is not JsonObject owned || op["action"] is not JsonObject action || !IsConfirmation(action)
            || !DailyEvidence.SameActor(owned,current) || !JsonNode.DeepEquals(owned["Scene"],current["Scene"])) return false;
        string ui=S(action["ui"]);
        foreach(string type in new[]{ui,"ShopUI"}){
            var a=DailyNavigationDecision.Rows(owned).Where(s=>S(s["Type"])==type).ToArray();
            var b=DailyNavigationDecision.Rows(current).Where(s=>S(s["Type"])==type).ToArray();
            if(a.Length!=1||b.Length!=1||!JsonNode.DeepEquals(a[0]["Id"],b[0]["Id"]))return false;
        }
        return DailyNavigationDecision.Blockers(current,ui,Policy).Length==0;
    }
    private static bool SameSurface(JsonObject before, JsonObject after, params string[] volatileKeys) =>
        before.Select(p => p.Key).Union(after.Select(p => p.Key)).Except(volatileKeys).All(k => JsonNode.DeepEquals(before[k], after[k]));
}

internal sealed class DailyTradeReplanRequired(string message) : Exception(message);

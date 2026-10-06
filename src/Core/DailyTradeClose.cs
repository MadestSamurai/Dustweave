using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyTradeClose
{
    private static readonly HashSet<string> Background = DailyNavigationPolicy.Load().Background;
    public static string Next(JsonObject frame, bool bargain, bool sent)
    {
        var rows = DailyNavigationDecision.Rows(frame).ToArray();
        var popup = rows.Where(r => r["Type"]?.GetValue<string>() == "DiscountCancelPopupUI").ToArray();
        if (rows.Any(r => r["Popup"]?.GetValue<bool>() == true && !Background.Contains(r["Type"]!.GetValue<string>()) && r["Type"]?.GetValue<string>() is not ("ShopUI" or "DiscountCancelPopupUI")))
            throw new DailyStepException("pending", "Unrelated popup appeared during trade close; preserved", true);
        if (popup.Length > 1)
            throw new DailyStepException("pending", "Ambiguous bargain cancel popup; preserved", true);
        if (popup.Length == 1)
        {
            if (!bargain)
                throw new DailyStepException("pending", "Unexpected bargain cancel popup; preserved", true);
            return !sent && popup[0]["InputReady"]?.GetValue<bool>() == true && popup[0]["Targets"] is JsonArray targets && targets.Count(t => t?["Field"]?.GetValue<string>() == "_objButtonOk" && t["Enabled"]?.GetValue<bool>() == true) == 1 ? "confirm" : "wait";
        }
        return rows.Any(r => r["Type"]?.GetValue<string>() == "ShopUI") ? "wait" : "settling";
    }
    public static JsonObject Owner(string root, JsonObject context, bool endingBargain = true)
    {
        string account = context["actor"]![3]!.GetValue<string>(), cycle = context["cycle"]!.GetValue<string>();
        if (!DailyProfiles.ValidKey(account) || !cycle.All(char.IsAsciiDigit) || cycle.Length == 0)
            throw new DailyStepException("rejected", "Invalid trade close identity");
        var job = DailyTradeCatalog.Read(Path.Combine(root, "trade", "executions", account, cycle, "execution.json"));
        string state = job["state"]?.GetValue<string>() ?? "", phase = job["phase"]?.GetValue<string>() ?? "";
        // Activating bargain starts by leaving the ordinary shop. That navigation
        // belongs to the running purchase phase; ending an active bargain still
        // requires the original post-purchase state and confirmed receipts.
        bool ownedPhase = phase is "cooking" or "sale" or "audit" || (!endingBargain && phase == "purchase" && state == "running");
        if (state is not ("running" or "completed") || !ownedPhase || job["id"]?.GetValue<string>() is not { Length: 32 } id || !id.All(char.IsAsciiHexDigit)
            || job["account"]?.GetValue<string>() != account || !JsonNode.DeepEquals(job["context"]?["account"], context["actor"]![3]) || !JsonNode.DeepEquals(job["context"]?["player"], context["actor"]![4]) || !JsonNode.DeepEquals(job["context"]?["server"], context["server"]) || !JsonNode.DeepEquals(job["context"]?["cycle"], context["cycle"]))
            throw new DailyStepException("rejected", "商店结束确认不属于当前已完成采购的交易；保留现场。");
        // Historical unknown trades do not block a fresh current-state plan.
        return job;
    }
    public static void SameResources(JsonObject before, JsonObject after)
    {
        if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), after["Frame"]!.AsObject()))
            throw new InvalidDataException("Trade close identity changed");
        foreach (var (id, path) in new[] { ("trade.currency", "Gold"), ("trade.currency", "Catalyst"), ("trade.inventory", "$items") })
            if (!JsonNode.DeepEquals(DailyEvidence.Reading(before, id, path), DailyEvidence.Reading(after, id, path)))
                throw new InvalidDataException("商店收尾期间资源发生变化；保留原交易，未继续。");
    }
    public static void Unchanged(JsonObject before, JsonObject after)
    {
        SameResources(before, after);
        if (DailyEvidence.Reading(after, "trade.bargain", "$self")!.GetValue<bool>())
            throw new InvalidDataException("原生砍价状态尚未结束。");
    }
    public static void VerifyExit(JsonObject before, JsonObject after, bool ownedCancelConfirmed)
    {
        SameResources(before, after);
        if (DailyNavigationDecision.Types(after["Frame"]!.AsObject()).Any(t => t is "ShopUI" or "DiscountCancelPopupUI"))
            throw new InvalidDataException("商店或原砍价确认尚未退场。");
        bool bargain = DailyEvidence.Reading(before, "trade.bargain", "$self")!.GetValue<bool>();
        if (Next(after["Frame"]!.AsObject(), bargain, ownedCancelConfirmed) != "settling")
            throw new InvalidDataException("商店收尾观察仍在等待；保留现场。");
        if (bargain && !ownedCancelConfirmed)
            throw new InvalidDataException("缺少本交易的砍价结束确认；保留现场。");
        // This property is assigned only by ShopOpenResponse. The native owned
        // cancel callback closes ShopUI without clearing that cached flag.
        // A completed owned confirmation plus both absent surfaces and unchanged
        // resources proves exit; require a fresh open response for later pricing.
        if (!bargain && DailyEvidence.Reading(after, "trade.bargain", "$self")!.GetValue<bool>())
            throw new InvalidDataException("普通商店退场期间砍价缓存意外变化。");
    }
}

public sealed partial class DailyCommandDriver
{
    private async Task<JsonObject> TradeCloseAsync(JsonObject action)
    {
        var current = await ReadBound();
        var before = await EvidenceAsync(["trade.currency", "trade.inventory", "trade.bargain"]);
        bool bargain = DailyEvidence.Reading(before, "trade.bargain", "$self")!.GetValue<bool>();
        var job = DailyTradeClose.Owner(root, current.Context, endingBargain: bargain);
        string path = Path.Combine(root, "live", "trade-closes", Guid.NewGuid().ToString("N") + ".json");
        var proof = new JsonObject { ["engine"] = "dotnet-trade-close-v2", ["purpose"] = bargain ? "end_bargain" : "ordinary_shop_exit", ["context"] = current.Context.DeepClone(), ["trade_session"] = job["id"]!.DeepClone(), ["before"] = before.DeepClone(), ["state"] = "prepared", ["resources_spent"] = false };
        DailyJson.Write(path, proof);
        try
        {
            var close = await SubmitRawAsync(action);
            proof["close"] = close.DeepClone();
            proof["state"] = "closing";
            DailyJson.Write(path, proof);
            bool sent = false;
            double until = clock() + 15;
            double? absentAt = null;
            while (clock() < until)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new DailyStepException("pending", "Trade close stopped; no replay", true);
                SubmissionGuard?.Invoke();
                var observed = await ReadBound();
                string next = DailyTradeClose.Next(observed.Frame, bargain, sent);
                if (next == "confirm")
                {
                    absentAt = null;
                    if (!sent)
                    {
                        var result = await SubmitRawAsync(new()
                        {
                            ["ui"] = "DiscountCancelPopupUI",
                            ["field"] = "_objButtonOk",
                            ["reason"] = "End only the owned, fully confirmed trade purchase session"
                        });
                        sent = true;
                        proof["confirm"] = result.DeepClone();
                        proof["state"] = "settling";
                        DailyJson.Write(path, proof);
                    }
                }
                else if (next == "settling")
                {
                    absentAt ??= clock();
                    if (clock() - absentAt >= .5)
                    {
                        var after = await EvidenceAsync(["trade.currency", "trade.inventory", "trade.bargain"]);
                        proof["after"] = after.DeepClone();
                        proof["owned_cancel_confirmed"] = sent;
                        proof["bargain_flag_after"] = DailyEvidence.Reading(after, "trade.bargain", "$self")!.DeepClone();
                        proof["bargain_flag_semantics"] = "last_shop_open_response";
                        DailyJson.Write(path, proof);
                        DailyTradeClose.VerifyExit(before, after, sent);
                        proof["state"] = "completed";
                        DailyJson.Write(path, proof);
                        close["managed_trade_close"] = path;
                        return close;
                    }
                }
                else
                    absentAt = null;
                await delay(TimeSpan.FromMilliseconds(150));
            }
            throw new DailyStepException("pending", "砍价结束确认尚未完全关闭；保存收尾记录，不重发。", true);
        }
        catch (Exception error) { proof["state"] = "unknown"; proof["error"] = error.Message; DailyJson.Write(path, proof); if (error is InvalidDataException) throw new DailyStepException("pending", error.Message, true); throw; }
    }
}

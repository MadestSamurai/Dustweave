using System.Text.Json.Nodes;
namespace Dustweave;

public sealed partial class DailyCommandDriver
{
    private async Task<JsonObject> TradeTalentPreviewAsync(JsonObject action)
    {
        // These native handlers open a preview; spending is a separate business
        // confirmation. Keep the original group's RPC/proof ownership intact.
        var before = await EvidenceAsync(["mainline.talent_rows", "trade.currency", "trade.inventory", "trade.bargain"]);
        int instance = (int)Number(action, "value");
        var row = DailyFieldTalentProof.Rows(before).Single(r => DailyFieldTalentProof.Numeric(r["Instance"]) == instance);
        int kind = (int)DailyFieldTalentProof.Numeric(row["Kind"]);
        if (kind is not (7 or 16))
            throw new DailyStepException("rejected", "Only cooking/bargain native previews belong to trade");
        string target = kind == 7 ? "CookingSelectUI" : "DiscountPopupUI";
        string path = Path.Combine(root, "live", "trade-previews", Guid.NewGuid().ToString("N") + ".json");
        var log = new JsonObject { ["engine"] = "dotnet-trade-preview-v1", ["before"] = before.DeepClone(), ["kind"] = kind, ["group"] = row["Group"]!.DeepClone(), ["state"] = "prepared", ["resources_spent"] = false };
        DailyJson.Write(path, log);
        try
        {
            var sent = await FieldTalentAsync(action);
            log["transport"] = sent.DeepClone();
            log["state"] = "submitted";
            DailyJson.Write(path, log);
            double end = clock() + 20;
            while (clock() < end)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new DailyStepException("pending", "Trade preview stopped; no replay", true);
                SubmissionGuard?.Invoke();
                var observed = await ReadBound();
                if (DailyNavigationDecision.Rows(observed.Frame).Count(r => Text(r, "Type") == target && DailyNavigationDecision.ReadyInput(r)) == 1)
                {
                    var after = await EvidenceAsync(["trade.currency", "trade.inventory", "trade.bargain"]);
                    DailyTradeClose.SameResources(before, after);
                    if (!JsonNode.DeepEquals(DailyEvidence.Reading(before, "trade.bargain", "$self"), DailyEvidence.Reading(after, "trade.bargain", "$self")))
                        throw new InvalidDataException("Native trade preview unexpectedly changed the bargain state");
                    log["after"] = after.DeepClone();
                    log["state"] = "completed";
                    DailyJson.Write(path, log);
                    sent["managed_trade_preview"] = path;
                    return sent;
                }
                await delay(TimeSpan.FromMilliseconds(200));
            }
            throw new DailyStepException("pending", "原生交易天赋预览未确认打开；保留现场，没有重发或消耗确认。", true);
        }
        catch (Exception error) { log["state"] = "unknown"; log["error"] = error.Message; DailyJson.Write(path, log); throw; }
    }
}

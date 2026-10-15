using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyTradeQuotePendingException(string reason, JsonObject detail) : IOException(reason)
{
    public JsonObject Detail { get; } = detail.DeepClone().AsObject();
}
public sealed record DailyTradeQuoteObservation(JsonObject Evidence, JsonObject Daily, long UtcTicks);

// ShopUI can accept input while its product models still contain the preceding
// ordinary prices. Only a matching native quote may become a purchase plan.
public static class DailyTradeQuoteReadiness
{
    private static JsonObject Binding(DailyTradeQuoteObservation observation)
    {
        var e = observation.Evidence;
        var frame = e["Frame"]!.AsObject();
        var shop = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == "ShopUI")
            ?? throw new StageHostException("adapter", "商店已关闭，请打开广场商店购买页后重新读取。");
        if (DailyNavigationDecision.Blockers(frame, "ShopUI", DailyNavigationPolicy.Load()).Length > 0
            || DailyNavigationDecision.Types(frame).Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            throw new StageHostException("adapter", DailyNavigationDecision.Types(frame).Contains("DiscountPopupUI")
                ? "砍价预览尚未关闭，请先取消预览后重新读取；本次未购买或使用天赋药。"
                : "商店被其他窗口或战斗遮挡，请返回商店购买页后继续跑商。");
        JsonObject native;
        try { native = State(e, "trade.native", "$self"); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
        {
            var readings = (e["Readings"] as JsonArray ?? []).OfType<JsonObject>().Where(r => S(r["Id"]) == "trade.native").ToArray();
            if (readings.Length == 0)
                throw new InvalidDataException("trade.read.configuration: missing trade.native; config=" + S(e["Config"]));
            var detail = readings.Length == 1 ? S(readings[0]["Error"]) : "reading count=" + readings.Length;
            if (detail.Length == 0 && readings.Length == 1)
                detail = string.Join("; ", (readings[0]["Values"] as JsonArray ?? []).OfType<JsonObject>().Select(v => S(v["Error"])).Where(s => s.Length > 0));
            throw new InvalidDataException("trade.read.native: " + (detail.Length > 0 ? detail : error.Message));
        }
        var guild = observation.Daily["Guild"]!.AsObject();
        return O(("config", e["Config"]), ("actor", Array(new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey", "Scene" }.Select(k => frame[k]))),
            ("surface", shop["Id"]), ("shop", R(e, "trade.shop_ui", "ὬὥὫὥὮὤὭὥὪὥὣ")), ("mode", R(e, "trade.shop_ui", "ὯὣὡὫὨὫὡὧὩὬὠ")),
            ("merchant", R(e, "trade.shop_ui", "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ")), ("bargain", native["BargainActive"]), ("discount", native["BargainPercent"]),
            ("server", guild["ServerKey"]), ("cycle", guild["CycleKey"]), ("client", guild["ClientMvid"]));
    }
    public static JsonObject Summary(JsonObject record)
    {
        JsonObject Snapshot(JsonNode? value)
        {
            var e = value?["evidence"] as JsonObject;
            var frame = e?["Frame"] as JsonObject;
            JsonObject? native = null;
            try { native = e == null ? null : Readings(e, "trade.native").FirstOrDefault()?["$self"] as JsonObject; }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { }
            return O(("at", value?["at"]), ("scene", frame?["Scene"]), ("bridge", frame?["BridgeVersion"]),
                ("surfaces", Array((frame?["Surfaces"] as JsonArray ?? []).OfType<JsonObject>().Select(r => O(("type", r["Type"]), ("order", r["Order"]), ("input_ready", r["InputReady"]), ("popup", r["Popup"]))))),
                ("bargain_active", native?["BargainActive"]), ("bargain_percent", native?["BargainPercent"]),
                ("reading_errors", Array((e?["Readings"] as JsonArray ?? []).OfType<JsonObject>().Where(r => S(r["Error"]).Length > 0).Take(12).Select(r => O(("id", r["Id"]), ("error", r["Error"]))))));
        }
        return O(("engine", "dotnet-trade-quote-summary-v1"), ("state", record["state"]), ("error", record["error"]),
            ("waits", record["waits"]), ("elapsed_seconds", record["elapsed_seconds"]), ("binding", record["binding"]),
            ("first", Snapshot(record["first"])), ("last", Snapshot(record["last"])), ("gameplay_actions", record["gameplay_actions"]));
    }
    public static async Task<(JsonObject State, JsonObject Proof)> ReadAsync(JsonObject catalog,
        Func<Task<DailyTradeQuoteObservation>> read, Func<double> clock, Func<int, Task> delay,
        Action<JsonObject> record, double seconds = 10)
    {
        double started = clock(), deadline = started + seconds;
        JsonObject? binding = null, first = null, last = null;
        int waits = 0;
        string outcome = "failed", error = "";
        DailyTradeQuoteObservation? latest = null;
        try
        {
            while (true)
            {
                var observation = latest = await read();
                var currentBinding = Binding(observation);
                if (binding != null && !JsonNode.DeepEquals(binding, currentBinding))
                    throw new StageHostException("identity", "等待报价期间账号、商人、商店、折扣或周期已改变，保留现场。");
                try
                {
                    if (!DailyNavigationDecision.Rows(observation.Evidence["Frame"]!.AsObject()).Any(r => S(r["Type"]) == "ShopUI" && DailyNavigationDecision.ReadyInput(r, false)))
                        throw new DailyTradeQuotePendingException("商店仍在展开", O(("reason", "input_not_ready")));
                    var result = DailyTradeSnapshot.Convert(catalog, observation.Evidence, observation.Daily, observation.UtcTicks);
                    outcome = "ready";
                    return result;
                }
                catch (DailyTradeQuotePendingException pending)
                {
                    binding ??= currentBinding;
                    last = O(("at", observation.UtcTicks), ("detail", pending.Detail), ("evidence", observation.Evidence), ("daily", observation.Daily));
                    first ??= last.DeepClone().AsObject();
                    waits++;
                    if (clock() >= deadline)
                        // This is a known unresolved shop, not a closeable idle page.
                        // Pause before generic navigation can end its discount session.
                        throw new InvalidOperationException("商店报价刷新超时，未继续采购或重新砍价；保留现场：" + pending.Message);
                    await delay(300);
                }
            }
        }
        catch (Exception failure) { error = failure.ToString(); throw; }
        finally
        {
            // Binding can fail on the very first native read. Preserve that
            // observation, including each reading's error, before any retry.
            if (outcome == "failed" && latest != null)
                last = O(("at", latest.UtcTicks), ("evidence", latest.Evidence), ("daily", latest.Daily));
            if (waits > 0 || outcome == "failed")
                try { record(O(("engine", "dotnet-trade-quote-readiness-v1"), ("state", outcome), ("error", error), ("waits", waits),
                    ("elapsed_seconds", clock() - started), ("binding", binding), ("first", first), ("last", last), ("gameplay_actions", 0))); }
                catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { /* Preserve the original failure. */ }
        }
    }
}

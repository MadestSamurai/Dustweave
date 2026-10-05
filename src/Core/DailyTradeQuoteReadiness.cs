using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

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
            ?? throw new StageHostException("identity", "报价读取期间商店已关闭，保留现场。");
        if (DailyNavigationDecision.Blockers(frame, "ShopUI", DailyNavigationPolicy.Load()).Length > 0
            || DailyNavigationDecision.Types(frame).Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            throw new StageHostException("identity", "报价读取期间出现其他弹窗或战斗，保留现场。");
        var native = State(e, "trade.native", "$self");
        var guild = observation.Daily["Guild"]!.AsObject();
        return O(("config", e["Config"]), ("actor", Array(new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey", "Scene" }.Select(k => frame[k]))),
            ("surface", shop["Id"]), ("shop", R(e, "trade.shop_ui", "ὬὥὫὥὮὤὭὥὪὥὣ")), ("mode", R(e, "trade.shop_ui", "ὯὣὡὫὨὫὡὧὩὬὠ")),
            ("merchant", R(e, "trade.shop_ui", "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ")), ("bargain", native["BargainActive"]), ("discount", native["BargainPercent"]),
            ("server", guild["ServerKey"]), ("cycle", guild["CycleKey"]), ("client", guild["ClientMvid"]));
    }
    public static async Task<(JsonObject State, JsonObject Proof)> ReadAsync(JsonObject catalog,
        Func<Task<DailyTradeQuoteObservation>> read, Func<double> clock, Func<int, Task> delay,
        Action<JsonObject> record, double seconds = 10)
    {
        double started = clock(), deadline = started + seconds;
        JsonObject? binding = null, first = null, last = null;
        int waits = 0;
        string outcome = "failed", error = "";
        try
        {
            while (true)
            {
                var observation = await read();
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
        catch (Exception failure) { error = failure.Message; throw; }
        finally
        {
            if (waits > 0)
                record(O(("engine", "dotnet-trade-quote-readiness-v1"), ("state", outcome), ("error", error), ("waits", waits),
                    ("elapsed_seconds", clock() - started), ("binding", binding), ("first", first), ("last", last), ("gameplay_actions", 0)));
        }
    }
}
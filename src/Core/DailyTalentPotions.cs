using System.Globalization;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Queue-only resource policy. Tool sessions deliberately have no policy delegate.</summary>
public sealed record DailyPotionLimit(long Available, long Required, long Reserve)
{
    public string Message => $"天赋药不足：现有 {Available}，本次需 {Required}，须保留 {Reserve}。本次操作未执行。";
    public JsonObject Json() => O(("available", Available), ("required", Required), ("reserve", Reserve));
    public static DailyPotionLimit? Parse(string code)
    {
        var parts = code.Split(':');
        if (parts.Length != 4 || parts[0] != "potion_limit") return null;
        var n = new long[3];
        for (int i = 0; i < n.Length; i++)
            if (!long.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        return new(n[0], n[1], n[2]);
    }
}

public static class DailyTalentPotions
{
    public const string Stage = "talent_potions";
    public static DailyPotionLimit? Check(long available, long required, long reserve)
    {
        if (available < 0 || required < 0 || reserve < 0) throw new InvalidDataException("天赋药数量或消耗数据无效。");
        // Free actions remain available even when the balance is below the reserve.
        return required > 0 && (available < reserve || required > available - reserve) ? new(available, required, reserve) : null;
    }
    public static long PurchaseCount(GeneralPreferences policy, long available, long required)
    {
        policy.Validate();
        if (!policy.RefillTalentPotions) throw new InvalidDataException("自动补药未开启；请补足天赋药或调整整体设置后补跑此项。");
        if (required > policy.TalentPotionTarget - policy.TalentPotionReserve)
            throw new InvalidDataException($"补药目标 {policy.TalentPotionTarget} 无法同时满足本次消耗 {required} 和保留量 {policy.TalentPotionReserve}，请提高补药目标。");
        long count = Math.Max(0, policy.TalentPotionTarget - available);
        if (count == 0) throw new InvalidDataException("当前药量已达到补药目标；请检查本次消耗或提高补药目标。");
        return count;
    }
    public static JsonObject DiscountedOffer(JsonObject native)
    {
        // ReputationDiscount is the game's weekly NPC-task town discount, not bargaining.
        var offer = Rows(native["Offers"]).Where(o => N(o["Type"]) == 12 && N(o["PriceType"]) == 4 &&
            N(o["ReputationDiscount"]) == 10 && N(o["BasePrice"]) > 0 && N(o["Price"]) > 0 &&
            N(o["Rate"]) == 100 && N(o["Price"]) == N(o["BasePrice"]) * 90 / 100 && N(o["Remaining"]) > 0)
            .OrderBy(o => N(o["Price"])).FirstOrDefault();
        return offer ?? throw new InvalidDataException("尚未取得周NPC任务的九折天赋药报价；请先完成本周至少一条NPC任务，再补跑受限环节。没有购买原价天赋药。");
    }
    public static JsonObject PlanningState(JsonObject state, GeneralPreferences policy, JsonObject? quote = null)
    {
        var result = state.DeepClone().AsObject();
        result["potions"] = Math.Max(0, N(state["potions"]) - policy.TalentPotionReserve);
        // Refill is a queue operation, never an implicit optimizer purchase.
        long buy = 0;
        if (policy.RefillTalentPotions && quote != null)
        {
            try { buy = Math.Min(N(DiscountedOffer(quote)["Remaining"]), Math.Max(0, policy.TalentPotionTarget - policy.TalentPotionReserve - N(result["potions"]))); }
            catch (InvalidDataException) { }
        }
        result["potion_buy_limit"] = Math.Min(N(state["potion_buy_limit"]), buy);
        return result;
    }
}

public sealed partial class DailyCommandDriver
{
    public Func<GeneralPreferences>? PotionPolicy { get; set; }
    public async Task RequirePotionsAsync(long required)
    {
        if (PotionPolicy == null || required == 0) return;
        var e = await EvidenceAsync(["dispatch.currency"]);
        long available = N(R(e, "dispatch.currency", "Catalyst"));
        var limit = DailyTalentPotions.Check(available, required, PotionPolicy().TalentPotionReserve);
        if (limit != null) throw new DailyStepException("rejected", limit.Message) { PotionLimit = limit };
    }
}

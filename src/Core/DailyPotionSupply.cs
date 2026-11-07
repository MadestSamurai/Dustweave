using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public interface IDailyPotionSupplyHost
{
    Task<JsonObject> SupplyPotionsAsync();
}

/// <summary>A definite prerequisite failure, never an uncertain purchase.</summary>
public sealed class DailyPotionSupplyException(string code, DailyText display) : Exception(display.Fallback)
{
    public string Code { get; } = code;
    public DailyText Display { get; } = display;
}

public static class DailyPotionSupply
{
    public static DailyText Text(string key, string fallback, params string[] args) => DailyText.Of("potion." + key, fallback, args);
    public static readonly DailyText NoDiscount = Text("no_discount", "尚无45金币的天赋药折扣报价，本次未补药。缺药任务需手动补充后重新执行；其他任务继续。");
    public static readonly DailyText NoGold = Text("no_gold", "金币不足，或补药会触及保留金币，本次未补药。补足金币或调整目标后重新执行。");
    public static readonly DailyText Shortage = Text("shortage", "本任务天赋药不足，已暂缓。请手动补充天赋药后重新执行此任务；其他任务继续。");
    public static JsonObject Result(string state, DailyText text, string code = "") => O(("state", state), ("reason", code), ("detail", text.Fallback), ("display", JsonSerializer.SerializeToNode(text)));
    public static DailyText? ReadDisplay(JsonElement item)
    {
        // Display metadata can never become a prerequisite for loading history.
        try
        {
            if (!item.TryGetProperty("display", out var value)) return null;
            var text = value.Deserialize<DailyText>();
            return text is { Key.Length: > 0, Fallback: not null, Arguments: not null } ? text : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException) { return null; }
    }
    public static GeneralPreferences CookingPolicy(GeneralPreferences general, long required) => new()
    {
        RefillTalentPotions = true,
        TalentPotionReserve = general.TalentPotionReserve,
        TalentPotionTarget = checked(general.TalentPotionReserve + required)
    };
}

using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyDisplayName(string Cn, string Tw, string En)
{
    public string In(string language) => (language switch { "en-US" => En, "zh-TW" => Tw, _ => Cn }) is { Length: > 0 } name ? name : Cn;
    public static DailyDisplayName From(JsonObject? row) => new(S(row?["textCn"]), S(row?["textTw"]), S(row?["textEn"]));
}
public sealed record DailyDrawItem(string Category, long Id, long Count, int Stars, string Rarity, DailyDisplayName Name, bool Converted, bool Known);
public sealed record DailyDrawSummary(int Draws, IReadOnlyList<DailyDrawItem> Items)
{
    public long FiveStarCostumes => Items.Where(i => i.Category == "Costume" && i.Stars == 5 && i.Known).Sum(i => i.Count);
    public long FiveStarUrEquipment => Items.Where(i => i.Category == "Equipment" && i.Stars == 5 && i.Rarity == "UR" && i.Known).Sum(i => i.Count);
    public long Unknown => Items.Where(i => !i.Known).Sum(i => i.Count) + Math.Max(0, Draws - Items.Sum(i => i.Count));
    public static DailyDrawSummary? Read(JsonElement item)
    {
        if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("draw_summary", out var value) && value.ValueKind == JsonValueKind.Object)
            try { return value.Deserialize<DailyDrawSummary>(); } catch (JsonException) { }
        return null;
    }
}

// Projection of confirmed rewards only, not inventory differences. viewItemInfo
// retains a duplicate costume before its automatic conversion into currency.
public sealed class DailyDrawCatalog(JsonObject[] costumes, JsonObject[] equipment, JsonObject[] characters, JsonObject[] names)
{
    private readonly Dictionary<long, JsonObject> costumes = costumes.ToDictionary(r => N(r["id"]));
    private readonly Dictionary<long, JsonObject> equipment = equipment.ToDictionary(r => N(r["id"]));
    private readonly Dictionary<long, JsonObject> characters = characters.Where(r => N(r["type"]) == 0 && !Flag(r["usePackTemporary"]))
        .GroupBy(r => N(r["uniqueCharId"])).ToDictionary(g => g.Key, g => g.OrderBy(r => N(r["grade"])).First());
    private readonly Dictionary<long, JsonObject> names = names.ToDictionary(r => N(r["id"]));
    public static DailyDrawCatalog Empty { get; } = new([], [], [], []);
    private DailyDisplayName Name(long id) => DailyDisplayName.From(names.GetValueOrDefault(id));
    public DailyDrawSummary Describe(JsonObject proof, string category)
    {
        var items = new List<DailyDrawItem>();
        foreach (var result in (proof["results"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var bundle = result["rewardInfoBundle"] as JsonObject ?? new();
            int type = category == "Costume" ? 11 : 10;
            var rewards = (bundle["viewItemInfo"] as JsonArray ?? []).OfType<JsonObject>().Where(r => N(r["type"]) == type).ToArray();
            var converted = (bundle["itemAutoExchangeInfo"] as JsonArray ?? []).OfType<JsonObject>().Where(r => N(r["originalItemType"]) == type).ToArray();
            if (rewards.Length == 0)
            {
                rewards = (bundle["itemInfo"] as JsonArray ?? []).OfType<JsonObject>().Where(r => N(r["type"]) == type)
                    .Concat(converted.Select(r => O(("id", N(r["originalItemId"])), ("count", N(r["originalItemCount"]))))).ToArray();
                if (rewards.Length == 0 && type == 10)
                    rewards = (bundle["equipInfo"] as JsonArray ?? []).OfType<JsonObject>().Select(r => O(("id", N(r["baseInfo"]?["id"])), ("count", 1))).ToArray();
            }
            foreach (var reward in rewards)
            {
                long id = N(reward["id"]), count = N(reward["count"]);
                if (count <= 0) continue;
                var table = (type == 11 ? costumes : equipment).GetValueOrDefault(id);
                var character = characters.GetValueOrDefault(N(table?[type == 11 ? "useUniqueCharId" : "privateUniqueCharId"]));
                int stars = (int)N(character?["grade"]);
                string rarity = type == 11 ? "" : N(table?["grade"]) switch { 1 => "N", 2 => "R", 3 => "SR", 4 => "UR", _ => "" };
                var name = Name(N(table?[type == 11 ? "costumeNameTextId" : "itemNameTextId"]));
                if (type == 11 && character != null)
                {
                    var owner = Name(N(character["charNameTextId"]));
                    name = new(owner.Cn + " · " + name.Cn, owner.Tw + " · " + name.Tw, owner.En + " · " + name.En);
                }
                items.Add(new(category, id, count, stars, rarity, name, converted.Any(r => N(r["originalItemId"]) == id),
                    table != null && stars is >= 1 and <= 5 && (type == 11 || rarity.Length > 0)));
            }
        }
        return new((int)N(proof["draws"]), items);
    }
}

public static class DailyRunReport
{
    private static readonly HashSet<string> RoutineSkips = ["disabled", "already_completed", "already_claimed", "no_free_draws",
        "weekly_likes_complete", "weekly_fishing_complete", "weekly_minigame_complete", "本周制作装备任务已完成", "本周末日之书任务已完成",
        "服装免费抽取：no_free_draws\n装备免费抽取：no_free_draws"];
    public static bool HasNotice(JsonElement item)
    {
        if (!item.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return false;
        bool Notice(JsonElement row)
        {
            if (row.ValueKind != JsonValueKind.Object) return false;
            foreach (string key in new[] { "reason", "detail" })
                if (row.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is {} s
                    && (s.StartsWith("event_auto_failed", StringComparison.Ordinal) || s.Contains("回退") || s.Contains("未找到完整奖励方案"))) return true;
            if (row.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array && issues.GetArrayLength() > 0) return true;
            // Semantic child results only; never walk observations or historical receipts.
            return row.TryGetProperty("stages", out var children) && children.ValueKind == JsonValueKind.Object && children.EnumerateObject().Any(p => Notice(p.Value));
        }
        return Notice(result);
    }
    public static IReadOnlyList<QueueStage> Relevant(QueueView view) => view.Stages.Where(s => !s.Carried &&
        (s.Draws is { Draws: > 0 } || s.HasNotice || s.PendingTasks?.Count > 0 || s.State != "completed" && !(s.State == "skipped" && RoutineSkips.Contains(s.Detail)))).ToArray();
}

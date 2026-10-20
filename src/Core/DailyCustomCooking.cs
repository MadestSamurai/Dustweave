using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyCookingChoice(long Id, string Name, string Materials, string SearchText);

/// <summary>Ordered, inventory-only cooking. No future purchases or sale assumptions.</summary>
public static class DailyCustomCooking
{
    public static JsonObject[] Recipes(JsonObject catalog) => Rows(catalog["cooking_recipes"] ?? catalog["recipes"]);
    public static JsonObject[] Items(JsonObject catalog) => Rows(catalog["cooking_items"] ?? catalog["items"]);
    // Display projection only: recipe IDs and ingredient quantities stay independent
    // of language, filtering and the user's chosen cooking order.
    public static DailyCookingChoice[] Choices(JsonObject catalog, string language)
    {
        var names = Items(catalog).ToDictionary(r => N(r["id"]),
            r => new DailyDisplayName(S(r["name"]), S(r["name_tw"]), S(r["name_en"])));
        DailyDisplayName Name(long id) => names.GetValueOrDefault(id) ?? new("#" + id, "", "");
        return Recipes(catalog).Select(r => {
            var name = Name(N(r["output"]));
            var materials = r["materials"]!.AsObject().Select(p => (Name: Name(long.Parse(p.Key)), Count: N(p.Value))).ToArray();
            return new DailyCookingChoice(N(r["id"]), name.In(language),
                string.Join(" · ", materials.Select(p => p.Name.In(language) + " ×" + p.Count)),
                string.Join(" ", materials.Select(p => p.Name).Prepend(name).SelectMany(n => new[] { n.Cn, n.Tw, n.En })));
        }).ToArray();
    }
    public static long[] Available(JsonObject catalog, JsonObject evidence)
    {
        var learned = R(evidence, "trade.recipes", "$self") as JsonArray ?? throw new InvalidDataException("料理解锁数据未就绪。");
        Require(learned.Count == N(R(evidence, "trade.recipes", "Count")), "料理解锁数据不完整。");
        var characters = R(evidence, "trade.characters", "$items") as JsonArray ?? throw new InvalidDataException("料理天赋数据未就绪。");
        Require(characters.Count == N(R(evidence, "trade.characters", "Count")), "料理天赋数据不完整。");
        long level = Rows(characters).Where(c => !DailyTradeSnapshot.Flag(c["Temporary"]) && N(catalog["characters"]?[S(c["Id"])]?["kind"]) == 7)
            .Select(c => N(c["TalentLevel"])).DefaultIfEmpty().Max();
        var ids = learned.Select(N).ToHashSet();
        return Recipes(catalog).Where(r => ids.Contains(N(r["id"])) && N(r["talent_level"]) <= level).Select(r => N(r["id"])).ToArray();
    }
    public static JsonObject? Next(JsonObject catalog, long id, JsonObject wallet, long reserve, IEnumerable<long> available)
    {
        var recipe = Recipes(catalog).SingleOrDefault(r => N(r["id"]) == id);
        if (recipe == null || !available.Contains(id)) return null;
        var materials = recipe["materials"]!.AsObject();
        Require(materials.Count > 0 && materials.All(p => N(p.Value) > 0) && N(recipe["count"]) > 0 && N(recipe["potions"]) >= 0, "料理配方数据无效。");
        var protectedItems = (wallet["protected"] as JsonObject ?? new()).Select(p => p.Value!)
            .GroupBy(p => S(p["id"])).ToDictionary(g => g.Key, g => g.Sum(p => N(p["count"])));
        long amount = materials.Min(p => Math.Max(0, N(wallet["items"]?[p.Key]) - protectedItems.GetValueOrDefault(p.Key)) / N(p.Value));
        long output = N(recipe["output"]), stack = N(Items(catalog).Single(r => N(r["id"]) == output)["stack"]);
        amount = Math.Min(amount, Math.Max(0, stack - N(wallet["items"]?[output.ToString()])) / N(recipe["count"]));
        if (N(recipe["potions"]) > 0) amount = Math.Min(amount, Math.Max(0, N(wallet["potions"]) - reserve) / N(recipe["potions"]));
        amount = Math.Min(amount, int.MaxValue);
        if (amount <= 0) return null;
        return O(("recipe", id), ("item", output), ("count", amount), ("potions", checked(amount * N(recipe["potions"]))),
            ("materials", new JsonObject(materials.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(checked(amount * N(p.Value))))))));
    }
}

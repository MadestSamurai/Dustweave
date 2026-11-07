using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Dustweave.DailyData;
namespace Dustweave;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DailySalePreference(long Item, bool Enabled = false, long Reserve = 0);
public sealed record DailySaleChoice(long Id, string Name, bool Cooked, string SearchText);

/// <summary>Opt-in sales after custom cooking. Live high-price quotes only; no optimizer forecasts.</summary>
public static class DailyCustomSales
{
    public static void Validate(DailySalePreference[]? rules)
    {
        if (rules == null || rules.Length > 1000 || rules.Any(r => r == null || r.Item <= 0 || r.Reserve is < 0 or > int.MaxValue)
            || rules.Select(r => r.Item).Distinct().Count() != rules.Length)
            throw new InvalidDataException("customsales.invalid");
    }
    public static DailySaleChoice[] Choices(JsonObject catalog, string language)
    {
        var outputs = DailyCustomCooking.Recipes(catalog).Select(r => N(r["output"])).ToHashSet();
        return DailyCustomCooking.Items(catalog).Concat(Rows(catalog["items"] ?? new JsonArray())).DistinctBy(r => N(r["id"]))
            .Where(r => N(r["id"]) > 0).Select(r => {
                var name = new DailyDisplayName(S(r["name"]), S(r["name_tw"]), S(r["name_en"]));
                return new DailySaleChoice(N(r["id"]), name.In(language), outputs.Contains(N(r["id"])),
                    string.Join(" ", name.Cn, name.Tw, name.En));
            }).ToArray();
    }
    public static JsonObject[] Plan(JsonObject catalog, DailySalePreference[] rules, JsonObject wallet, JsonObject native)
    {
        Validate(rules);
        var known = Choices(catalog, "zh-CN").Select(c => c.Id).ToHashSet();
        var shops = (native["AvailableShops"] as JsonArray ?? throw new InvalidDataException("customsales.quote_missing")).Select(N).ToHashSet();
        var quotes = Rows(native["Quotes"] as JsonArray ?? throw new InvalidDataException("customsales.quote_missing"));
        var result = new List<JsonObject>();
        foreach (var rule in rules.Where(r => r.Enabled && known.Contains(r.Item)).OrderBy(r => r.Item))
        {
            var quote = quotes.Where(q => N(q["Item"]) == rule.Item && shops.Contains(N(q["Shop"])) && N(q["Rate"]) == 120 && N(q["Price"]) > 0)
                .OrderByDescending(q => N(q["Price"])).ThenBy(q => N(q["Shop"])).FirstOrDefault();
            if (quote == null) continue;
            var stacks = Rows(wallet["stacks"]).Where(s => N(s["id"]) == rule.Item && N(s["type"]) == 5 && N(s["expiryTime"]) == 0).ToArray();
            // Locked stacks count toward the total reserve, but never become sale orders.
            long remaining = Math.Max(0, stacks.Sum(s => N(s["count"])) - rule.Reserve);
            foreach (var stack in stacks.Where(s => !DailyTradeSnapshot.Flag(s["keepFlag"]) && N(s["invenIndex"]) > 0)
                .OrderBy(s => N(s["count"])).ThenBy(s => N(s["invenIndex"])))
            {
                long count = Math.Min(remaining, Math.Min(N(stack["count"]), 99999));
                if (count <= 0) continue;
                result.Add(O(("item", rule.Item), ("instance", stack["invenIndex"]), ("count", count), ("shop", quote["Shop"]),
                    ("price", quote["Price"]), ("value", checked(count * N(quote["Price"]))), ("reserve", rule.Reserve)));
                remaining -= count;
            }
        }
        return result.ToArray();
    }
}

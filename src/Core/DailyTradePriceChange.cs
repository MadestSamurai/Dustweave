using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Announced prices are forecast inputs only. Live quotes authorize sales.</summary>
public static class DailyTradePriceChange
{
    public static readonly DateOnly EffectiveDate = new(2026, 11, 16);
    private sealed record Change(long Recipe, long Item, long Old, long New);
    // Unit peak prices: floor(base price * 120 / 100), never rounded per batch.
    private static readonly Change[] Changes = [
        new(101,2002,33,66), new(102,2003,50,78), new(201,2004,55,126),
        new(103,2028,70,90), new(310,2029,177,324), new(302,2005,124,312),
        new(104,2006,51,72), new(202,2007,98,144), new(303,2008,110,294),
        new(203,2010,164,186), new(402,2011,259,468), new(304,2012,218,306),
        new(401,2014,441,588), new(205,2015,171,186), new(403,2016,376,546),
        new(305,2017,201,330), new(404,2018,216,414), new(306,2019,228,360),
        new(307,2021,302,348), new(301,2022,177,306), new(406,2023,331,456),
        new(308,2024,148,318), new(408,2027,307,570), new(106,2030,151,168),
        new(409,2035,427,570)
    ];
    public static Dictionary<long,long> ExpectedPrices(JsonObject catalog, DateOnly today)
    {
        var result = new Dictionary<long,long>();
        // Once maintenance is due, do not keep inventing a future activation date.
        // The newly captured tables become authoritative, including later changes.
        if (today >= EffectiveDate) return result;
        var items = Rows(catalog["items"]).ToDictionary(i => N(i["id"]));
        foreach (var c in Changes)
            if (items.TryGetValue(c.Item, out var item) && N(item["sale"]) == c.Old &&
                Rows(catalog["recipes"]).Any(r => N(r["id"]) == c.Recipe && N(r["output"]) == c.Item && N(r["count"]) == 1))
                result[c.Item] = c.New;
        return result;
    }
    public static bool UseCalendar(JsonObject catalog, DateOnly today) => today >= new DateOnly(2026,10,31) &&
        Changes.Any(c => Rows(catalog["items"]).Any(i => N(i["id"]) == c.Item && N(i["sale"]) is var p && (p == c.Old || p == c.New)) &&
            Rows(catalog["recipes"]).Any(r => N(r["id"]) == c.Recipe && N(r["output"]) == c.Item));
    public static bool AwaitingPrice(JsonObject catalog, long item, DateOnly today) =>
        today >= EffectiveDate && today <= EffectiveDate.AddDays(1) && Changes.Any(c => c.Item == item &&
            Rows(catalog["items"]).Any(i => N(i["id"]) == item && N(i["sale"]) == c.Old) &&
            Rows(catalog["recipes"]).Any(r => N(r["id"]) == c.Recipe && N(r["output"]) == item));
}

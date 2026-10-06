using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyTradeSnapshot
{
    public static bool Flag(JsonNode? value) => value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? B(value) : N(value) != 0;
    private static JsonArray Counted(JsonObject e, string id, string path = "$items")
    {
        var rows = R(e, id, path) as JsonArray ?? throw new InvalidDataException("Missing trade collection " + id);
        Require(rows.Count == N(R(e, id, "Count")), "Truncated trade collection " + id);
        return rows;
    }
    public static (JsonObject State, JsonObject Proof) Convert(JsonObject catalog, JsonObject e, JsonObject daily, long nowTicks)
    {
        var frame = e["Frame"]!.AsObject();
        var guild = daily["Guild"]!.AsObject();
        Require(S(e["Error"]) == "" && S(daily["Error"]) == "" && S(guild["Error"]) == "", "Observer error");
        Require(N(frame["BridgeVersion"]) >= 24, "Trade observer unsupported");
        foreach (var stamp in new[] { e["AtUtcTicks"], frame["AtUtcTicks"], daily["FrameUtcTicks"] })
            Require(nowTicks - N(stamp) >= 0 && nowTicks - N(stamp) <= 30_000_000, "Trade observation is stale");
        foreach (string k in new[] { "AccountKey", "PlayerKey", "ProcessId", "ProcessStartTicks" })
            Require(JsonNode.DeepEquals(frame[k], daily[k]), "Trade account/process changed");
        Require(JsonNode.DeepEquals(guild["ClientMvid"], catalog["client"]), "Trade client mismatch");
        var native = State(e, "trade.native", "$self");
        Require(Math.Abs(N(native["ServerTicks"]) - N(guild["ServerTicks"])) <= 30_000_000, "Trade clock mismatch");
        Require(N(guild["ResetTicks"]) > N(native["ServerTicks"]) && S(guild["ResetTicks"]) == S(guild["CycleKey"]), "Invalid trade cycle");
        Require(S(native["Date"]) == new DateTime(N(native["ServerTicks"]), DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Trade date mismatch");
        var available = native["AvailableShops"]!.AsArray().Select(N).ToHashSet();
        var unavailable = native["UnavailableShops"]!.AsArray().Select(N).ToHashSet();
        Require(available.Count == native["AvailableShops"]!.AsArray().Count && !available.Overlaps(unavailable), "Invalid shop availability");
        var shops = Rows(R(e, "trade.available_shops", "$items"));
        Require(shops.Length == N(R(e, "trade.available_shops", "ὫὨὧὫὯὭὨὢὦὬὪ.Count")) && shops.Select(r => N(r["Id"])).ToHashSet().SetEquals(available.Concat(unavailable)), "Shop availability truncated");
        Require(Rows(catalog["items"]).All(r => available.Contains(N(r["shop"]))), "Premium stores inaccessible; valuation requires reduced catalog");
        var food = Rows(Counted(e, "trade.inventory")).Single(r => S(r["Key"]) == "Food");
        var stacks = Rows(food["Value.Values"]);
        Require(stacks.Length == N(food["Value.Count"]), "Food inventory truncated");
        var inventory = new Dictionary<long, long>();
        var locked = new Dictionary<long, long>();
        var instances = new HashSet<string>();
        foreach (var r in stacks)
        {
            long id = N(r["id"]), q = N(r["count"]);
            Require(N(r["type"]) == 5 && N(r["expiryTime"]) == 0 && id > 0 && q >= 0 && N(r["invenIndex"]) > 0 && instances.Add(S(r["invenIndex"])), "Invalid food stack");
            inventory[id] = checked(inventory.GetValueOrDefault(id) + q);
            if (Flag(r["keepFlag"]))
                locked[id] = checked(locked.GetValueOrDefault(id) + q);
        }
        var characters = Rows(Counted(e, "trade.characters"));
        var unlocked = Counted(e, "trade.recipes", "$self").Select(N).ToHashSet();
        long cookingLevel = 0;
        bool canBargain = false;
        var talents = new JsonArray();
        Require(catalog["characters"] is JsonObject chars && chars.Count > 0, "Missing character talents");
        foreach (var c in characters)
        {
            if (Flag(c["Temporary"]) || catalog["characters"]![S(c["Id"])] is not JsonObject talent)
                continue;
            var skill = talent["skills"]?[S(c["TalentLevel"])] as JsonObject ?? throw new InvalidDataException("Unknown talent tier");
            long kind = N(talent["kind"]), level = N(c["TalentLevel"]);
            talents.Add(O(("character", c["Id"]), ("kind", kind), ("level", level)));
            if (kind == 7)
                cookingLevel = Math.Max(cookingLevel, level);
            if (kind == 16 && skill["values"]!.AsArray().Take(2).Select(N).SequenceEqual(new long[] { 100, 60 }) && N(skill["potions"]) == 10)
                canBargain = true;
        }
        bool active = B(native["BargainActive"]);
        Require(active == B(R(e, "trade.bargain", "$self")) && (!active || N(native["BargainPercent"]) == 60), "Unsupported active bargain");
        var products = Rows(catalog["offers"]).ToDictionary(r => (N(r["shop"]), N(r["product"])));
        var expected = products.Keys.Where(k => available.Contains(k.Item1)).ToHashSet();
        var offers = new JsonArray();
        var potions = new List<JsonObject>();
        var seen = new HashSet<(long, long)>();
        foreach (var r in Rows(native["Offers"]))
        {
            var key = (N(r["Shop"]), N(r["Product"]));
            Require(seen.Add(key) && available.Contains(key.Item1) && N(r["PriceType"]) == 4, "Unknown native shop offer/currency");
            if (N(r["Type"]) == 12)
            {
                long price = N(r["Price"]), rate = N(r["Rate"]), basePrice = N(r["BasePrice"]), discount = N(r["ReputationDiscount"]);
                Require(N(r["NoBargain"]) == 1 && price > 0 && basePrice > 0 && rate > 0 && discount is >= 0 and < 100 && price == (basePrice * rate / 100) * (100 - discount) / 100, "Unexpected potion quote");
                potions.Add(r);
                continue;
            }
            Require(N(r["Type"]) == 5 && expected.Contains(key), "Unknown food offer");
            var p = products[key];
            Require(N(r["Item"]) == N(p["item"]) && N(r["BasePrice"]) == N(p["base_price"]) && N(r["Limit"]) == N(p["limit"]) && N(r["NoBargain"]) == 0, "Food quote differs from rules");
            offers.Add(O(("shop", key.Item1), ("product", key.Item2), ("remaining", r["Remaining"]), ("price", r["Price"]), ("bargain_price", N(p["base_price"]) * 40 / 100)));
        }
        Require(Rows(offers).Select(r => (N(r["shop"]), N(r["product"]))).ToHashSet().SetEquals(expected), "Incomplete food supply");
        long shop = N(R(e, "trade.shop_ui", "ὬὥὫὥὮὤὭὥὪὥὣ"));
        Require(S(R(e, "trade.shop_ui", "ὯὣὡὫὨὫὡὧὩὬὠ")) == "Buy", "请打开商店购买页读取完整报价");
        var display = Rows(R(e, "trade.shop_ui", "$items"));
        Require(display.Length == N(R(e, "trade.shop_ui", "ὤὦὣὥὭὣὫὨὡὢὮ.Count")), "Displayed shop truncated");
        var nativeOffers = Rows(native["Offers"]).ToDictionary(r => (N(r["Shop"]), N(r["Product"])));
        int matched = 0;
        foreach (var r in display)
        {
            if (N(r["ὢὭὩὠὪὯὮὧὩὣὧ"]) is not (5 or 12))
                continue;
            long product = N(r["ὯὫὪὡὭὤὪὮὫὬὣ"]);
            if (!nativeOffers.TryGetValue((shop, product), out var offer))
                throw new DailyTradeQuotePendingException("商店商品列表尚未对应当前供货", O(("shop", shop), ("product", product), ("reason", "product_missing")));
            long displayedPrice = N(r["ὣὡὢὩὥὥὨὯὩὥὯ"]), displayedLimit = N(r["ὠὥὬὠὨὧὡὩὨὤὯ"]);
            if (N(offer["Price"]) != displayedPrice || N(offer["Limit"]) != displayedLimit)
                throw new DailyTradeQuotePendingException($"商店{shop}商品{product}：报价{N(offer["Price"])}／显示{displayedPrice}，限购{N(offer["Limit"])}／显示{displayedLimit}",
                    O(("shop", shop), ("product", product), ("native_price", offer["Price"]), ("displayed_price", displayedPrice), ("native_limit", offer["Limit"]), ("displayed_limit", displayedLimit)));
            matched++;
        }
        Require(matched > 0 && potions.Count > 0, "Displayed pricing not verified");
        var potion = potions.OrderBy(r => N(r["Price"])).ThenByDescending(r => N(r["Remaining"])).First();
        var recipes = Rows(catalog["recipes"]).Where(r => unlocked.Contains(N(r["id"])) && N(r["talent_level"]) <= cookingLevel).Select(r => r["id"]);
        static JsonObject Quantities(Dictionary<long, long> d) => new(d.Select(p => new KeyValuePair<string, JsonNode?>(p.Key.ToString(CultureInfo.InvariantCulture), JsonValue.Create(p.Value))));
        var state = O(("schema", 1), ("context", O(("account", frame["AccountKey"]), ("player", frame["PlayerKey"]), ("player_name", daily["PlayerName"]), ("server", guild["ServerKey"]), ("cycle", guild["CycleKey"]), ("client", catalog["client"]), ("database_sha256", catalog["database_sha256"]))), ("captured_utc", new DateTimeOffset(N(e["AtUtcTicks"]), TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture)), ("game_date", native["Date"]), ("inventory_complete", true), ("offers_complete", true), ("recipes_complete", true), ("gold", R(e, "trade.currency", "Gold")), ("potions", R(e, "trade.currency", "Catalyst")), ("potion_price", potion["Price"]), ("potion_buy_limit", potion["Remaining"]), ("can_bargain", canBargain), ("bargain_active", active), ("items", Quantities(inventory)), ("protected_items", Quantities(locked)), ("recipes", Array(recipes)), ("offers", offers), ("today_quotes", Array(Rows(native["Quotes"]).Select(q => O(("item", q["Item"]), ("shop", q["Shop"]), ("price", q["Price"]), ("rate", q["Rate"]))))));
        DailyTradeOptimizer.Validate(catalog, state);
        var proof = O(("evidence_hash", DailyTradeCatalog.Fingerprint(e)), ("daily_hash", DailyTradeCatalog.Fingerprint(daily)), ("food_stacks", stacks.Length), ("food_types", inventory.Count), ("displayed_prices_matched", matched), ("available_shops", available.Order().ToArray()), ("food_offers", offers.Count), ("unlocked_recipes", unlocked.Count), ("usable_recipes", state["recipes"]!.AsArray().Count), ("talents", talents), ("protected_items", Quantities(locked)), ("resources_spent", false));
        return (state, proof);
    }
}

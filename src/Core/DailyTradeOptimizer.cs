using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyTradeOptimizer
{
    public const long LowFunds = 10_000_000;
    public const string Warning = "当前金币少于1000万。跑商周转约30天，采购后需等待各物品120%收购日；请保留其他玩法所需金币。计划会按实际可用资金缩减采购。";
    private static long Integer(JsonNode? v, string name, long minimum = 0)
    {
        Require(v is JsonValue && v.GetValueKind() == System.Text.Json.JsonValueKind.Number, "Invalid " + name);
        long n = N(v);
        Require(n >= minimum && n <= 1_000_000_000_000, "Invalid " + name);
        return n;
    }
    public static string NextSale(string date, int day)
    {
        var current = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Require(day is >= 1 and <= 31, "Invalid sale day");
        for (int i = 0; i < 14; i++)
        {
            var month = new DateOnly(current.Year, current.Month, 1).AddMonths(i);
            if (day <= DateTime.DaysInMonth(month.Year, month.Month))
            {
                var next = new DateOnly(month.Year, month.Month, day);
                if (next >= current)
                    return next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
        throw new InvalidDataException("No premium date");
    }
    public static JsonObject Preferences(JsonObject? input = null)
    {
        var settings = O(("enabled", false), ("include_break_even_resales", false), ("reserve_gold", 0), ("max_spend", null), ("reserve_items", new JsonObject()));
        foreach (var pair in input ?? new())
        {
            Require(settings.ContainsKey(pair.Key), "Unknown trade setting");
            settings[pair.Key] = Copy(pair.Value);
        }
        Require(settings["enabled"]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "Invalid trade switch");
        Require(settings["include_break_even_resales"]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "Invalid break-even trade switch");
        Integer(settings["reserve_gold"], "reserve_gold");
        if (settings["max_spend"] != null)
            Integer(settings["max_spend"], "max_spend");
        foreach (var pair in settings["reserve_items"]!.AsObject())
        {
            Require(long.TryParse(pair.Key, out long id) && id > 0 && id.ToString(CultureInfo.InvariantCulture) == pair.Key, "Invalid reserve item");
            Integer(pair.Value, "reserve quantity");
        }
        return settings;
    }
    public static void Validate(JsonObject catalog, JsonObject state)
    {
        Require(N(catalog["schema"]) == 1 && N(state["schema"]) == 1, "Unsupported trade schema");
        var c = state["context"]!.AsObject();
        foreach (string k in new[] { "account", "player", "server", "cycle", "client" })
            Require(c[k]?.GetValueKind() == System.Text.Json.JsonValueKind.String && S(c[k]).Length > 0, "Trade identity incomplete");
        Require(JsonNode.DeepEquals(c["client"], catalog["client"]) && JsonNode.DeepEquals(c["database_sha256"], catalog["database_sha256"]), "Trade tables differ from client/database");
        string stamp = S(state["captured_utc"]);
        Require(stamp.EndsWith('Z') || stamp.Length >= 6 && stamp[^6] is '+' or '-', "Snapshot timezone required");
        DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture);
        DateOnly.ParseExact(S(state["game_date"]), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (string k in new[] { "inventory_complete", "offers_complete", "recipes_complete" })
            Require(B(state[k]), "Missing observation: " + k);
        foreach (string k in new[] { "gold", "potions", "potion_price", "potion_buy_limit" })
            Integer(state[k], k, k == "potion_price" ? 1 : 0);
        foreach (string k in new[] { "can_bargain", "bargain_active" })
            Require(state[k]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "Unknown bargain state");
        var items = Rows(catalog["items"]);
        var ids = items.Select(i => Integer(i["id"], "item", 1)).ToHashSet();
        Require(ids.Count == items.Length, "Duplicate trade item");
        foreach (var i in items)
        {
            Integer(i["sale"], "sale");
            Integer(i["stack"], "stack", 1);
            Require(Integer(i["day"], "sale day", 1) <= 31, "Invalid sale day");
        }
        var offers = Rows(catalog["offers"]);
        foreach (var p in offers)
        {
            Require(ids.Contains(N(p["item"])), "Offer item missing");
            foreach (string k in new[] { "shop", "product", "base_price", "limit" })
                Integer(p[k], k, 1);
        }
        Require(offers.Select(p => (N(p["shop"]), N(p["product"]))).Distinct().Count() == offers.Length, "Duplicate product");
        var products = offers.ToDictionary(p => (N(p["shop"]), N(p["product"])));
        var seen = new HashSet<(long, long)>();
        foreach (var p in Rows(state["offers"]))
        {
            var key = (N(p["shop"]), N(p["product"]));
            Require(seen.Add(key) && products.ContainsKey(key), "Duplicate or unknown offer");
            Integer(p["remaining"], "remaining");
            Integer(p["price"], "price", 1);
            Require(N(p["remaining"]) <= N(products[key]["limit"]) && N(p["bargain_price"]) == N(products[key]["base_price"]) * 40 / 100, "Unexpected native quote");
        }
        foreach (string key in new[] { "items", "capacity_limits", "protected_items" })
            foreach (var pair in state[key] as JsonObject ?? new())
            {
                Require(long.TryParse(pair.Key, out long id) && id > 0 && id.ToString(CultureInfo.InvariantCulture) == pair.Key, "Invalid inventory id");
                Integer(pair.Value, "inventory quantity");
                if (key == "protected_items")
                    Require(N(pair.Value) <= N(state["items"]?[pair.Key]), "Protected quantity exceeds inventory");
            }
        var recipes = Rows(catalog["recipes"]);
        var known = recipes.Select(r => N(r["id"])).ToHashSet();
        var outputs = recipes.Select(r => N(r["output"])).ToHashSet();
        Require(known.Count == recipes.Length && outputs.Count == recipes.Length, "Duplicate recipe/output");
        var unlocked = state["recipes"]!.AsArray().Select(N).ToArray();
        Require(unlocked.Distinct().Count() == unlocked.Length && unlocked.All(known.Contains), "Unknown or duplicate recipe");
        foreach (var r in recipes)
        {
            var materials = r["materials"]!.AsObject();
            Require(ids.Contains(N(r["output"])) && materials.Count > 0, "Recipe item missing");
            Integer(r["count"], "recipe count", 1);
            Integer(r["potions"], "recipe potions", 1);
            foreach (var p in materials)
            {
                long id = long.Parse(p.Key, CultureInfo.InvariantCulture);
                Require(ids.Contains(id) && !outputs.Contains(id), "Missing or chained recipe input");
                Integer(p.Value, "recipe material", 1);
            }
        }
        var quotes = Rows(state["today_quotes"] ?? new JsonArray());
        Require(quotes.Select(q => (N(q["item"]), N(q["shop"]))).Distinct().Count() == quotes.Length, "Duplicate sale quote");
        foreach (var q in quotes)
            foreach (string k in new[] { "item", "shop", "price", "rate" })
                Integer(q[k], "quote " + k, k is "item" or "shop" ? 1 : 0);
    }
    private static Dictionary<long, long> Quantities(JsonNode? node) => (node as JsonObject ?? new()).ToDictionary(p => long.Parse(p.Key, CultureInfo.InvariantCulture), p => N(p.Value));
    private sealed record Candidate(string Mode, long Opening, double[] X, double Profit, double? Bound, bool Optimal, bool Tie, long[] Prices, long Cash, long Potions, long PotionBuy, long CurrentProfit, JsonObject Forecast, long[] BreakEven)
    {
        public long BreakEvenValue => BreakEven.Select((q, j) => q * Prices[j]).Sum();
    }

    private static long[] AllocateBreakEven(double[] x, int[] eligible, int[] saleColumns, long[] prices,
        double[] upper, List<double[]> rows, List<double> lower, List<double> caps, double seconds, Action? check)
    {
        var allocated = new long[prices.Length];
        if (eligible.Length == 0) return allocated;
        // Keep all profitable purchases, cooking and future supplies fixed. Only
        // add equal-price purchase/resale pairs within every original constraint.
        var extraRows = new List<double[]>(); var extraLower = new List<double>(); var extraCaps = new List<double>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = eligible.Select(j => rows[i][j] + rows[i][saleColumns[j]]).ToArray();
            if (row.All(v => v == 0)) continue;
            double used = rows[i].Select((v, j) => v * x[j]).Sum();
            extraRows.Add(row); extraLower.Add(lower[i] - used); extraCaps.Add(caps[i] - used);
        }
        var solved = DailyLinearOptimizer.Solve(eligible.Select(j => -(double)prices[j]).ToArray(),
            eligible.Select(j => Math.Max(0, upper[j] - x[j])).ToArray(), extraRows, extraLower, extraCaps, true,
            Math.Min(seconds, 2), check, initial: new double[eligible.Length]);
        if (solved == null) return allocated;
        for (int i = 0; i < eligible.Length; i++)
        {
            int j = eligible[i]; long q = (long)Math.Round(solved.Values[i]);
            allocated[j] = q; x[j] += q; x[saleColumns[j]] += q;
        }
        return allocated;
    }
    public static JsonObject Plan(JsonObject catalog, JsonObject state, JsonObject? inputSettings = null, double timeLimit = 15, Action? check = null)
    {
        check?.Invoke();
        var started = Stopwatch.StartNew();
        Validate(catalog, state);
        var settings = Preferences(inputSettings);
        bool includeBreakEven = B(settings["include_break_even_resales"]);
        Require(double.IsFinite(timeLimit) && timeLimit > 0 && timeLimit <= 300, "Invalid time limit");
        var stock = Quantities(state["items"]);
        var reserved = Quantities(settings["reserve_items"]);
        var items = Rows(catalog["items"]).ToDictionary(i => N(i["id"]));
        Require(reserved.Keys.All(items.ContainsKey), "Unknown reserved item");
        foreach (var p in Quantities(state["protected_items"]))
            reserved[p.Key] = Math.Max(reserved.GetValueOrDefault(p.Key), p.Value);
        var available = stock.ToDictionary(p => p.Key, p => Math.Max(0, p.Value - reserved.GetValueOrDefault(p.Key)));
        var capacities = Quantities(state["capacity_limits"]);
        var offers = Rows(catalog["offers"]).ToDictionary(p => (N(p["shop"]), N(p["product"])));
        var products = Rows(state["offers"]).Where(p => N(p["remaining"]) > 0).Select(p => { var r = offers[(N(p["shop"]), N(p["product"]))].DeepClone().AsObject(); foreach (string k in new[] { "remaining", "price", "bargain_price" }) r[k] = Copy(p[k]); return r; }).ToArray();
        var unlocked = state["recipes"]!.AsArray().Select(N).ToHashSet();
        var recipes = Rows(catalog["recipes"]).Where(r => unlocked.Contains(N(r["id"]))).ToArray();
        var keys = items.Keys.Order().ToArray();
        var pos = keys.Select((k, j) => (k, j)).ToDictionary(p => p.k, p => p.j);
        long budget = Math.Max(0, N(state["gold"]) - N(settings["reserve_gold"]));
        if (settings["max_spend"] != null)
            budget = Math.Min(budget, N(settings["max_spend"]));
        int saleStart = products.Length + recipes.Length, carryStart = saleStart + keys.Length;
        var future = new DailyTradeForecast(catalog, state, carryStart);
        var carryKeys = future.CarryKeys;
        int n = future.Count, potionCol = future.Start - 1;
        var balance = keys.Select(_ => new double[n]).ToArray();
        var upper = Enumerable.Repeat(DailyLinearOptimizer.Infinity, n).ToArray();
        var objective = new double[n];
        for (int j = 0; j < products.Length; j++)
        {
            balance[pos[N(products[j]["item"])]][j] = 1;
            upper[j] = N(products[j]["remaining"]);
        }
        for (int i = 0; i < recipes.Length; i++)
        {
            int j = products.Length + i;
            var r = recipes[i];
            var parts = Quantities(r["materials"]);
            foreach (var p in parts)
                balance[pos[p.Key]][j] -= p.Value;
            balance[pos[N(r["output"])]][j] += N(r["count"]);
            objective[j] = N(r["potions"]) * N(state["potion_price"]);
            upper[j] = parts.Min(p => (available.GetValueOrDefault(p.Key) + products.Where(o => N(o["item"]) == p.Key).Sum(o => N(o["remaining"]))) / p.Value);
        }
        for (int i = 0; i < keys.Length; i++)
        {
            balance[i][saleStart + i] = -1;
            objective[saleStart + i] = -N(items[keys[i]]["sale"]);
        }
        for (int i = 0; i < carryKeys.Length; i++)
        {
            long k = carryKeys[i];
            balance[pos[k]][carryStart + i] = -1;
            upper[carryStart + i] = available.GetValueOrDefault(k) + products.Where(p => N(p["item"]) == k).Sum(p => N(p["remaining"]));
        }
        upper[potionCol] = N(state["potion_buy_limit"]);
        long baseValue = keys.Sum(k => available.GetValueOrDefault(k) * N(items[k]["sale"]));
        var modes = B(state["bargain_active"]) ? new[] { ("active", 0L) } : B(state["can_bargain"]) ? new[] { ("bargain", 10L), ("normal", 0L) } : new[] { ("normal", 0L) };
        var candidates = new List<Candidate>();
        // The achievement strategy excludes the one-off bargaining fee from
        // ranking, but cash, potions and the economic ledger still charge it.
        long RankingFee(Candidate c) => includeBreakEven && c.BreakEvenValue > 0 ? c.Opening * N(state["potion_price"]) : 0;
        double RankingProfit(Candidate c) => c.Profit + RankingFee(c);
        bool unresolvedMode = false;
        foreach (var (mode, opening) in modes)
        {
            var obj = (double[])objective.Clone();
            var cash = new double[n];
            var potion = new double[n];
            var prices = products.Select(p => N(p[mode == "normal" ? "price" : "bargain_price"])).ToArray();
            for (int j = 0; j < products.Length; j++)
                obj[j] = cash[j] = prices[j];
            cash[potionCol] = N(state["potion_price"]);
            potion[potionCol] = -1;
            for (int i = 0; i < recipes.Length; i++)
                potion[products.Length + i] = N(recipes[i]["potions"]);
            var rows = new List<double[]>(balance) { cash, potion };
            var lower = keys.Select(k => (double)-available.GetValueOrDefault(k)).ToList();
            var cap = new List<double>(lower);
            lower.AddRange([double.NegativeInfinity, double.NegativeInfinity]);
            cap.AddRange([budget, N(state["potions"]) - opening]);
            foreach (long k in keys)
                if (capacities.ContainsKey(k))
                {
                    var row = new double[n];
                    for (int j = 0; j < products.Length; j++)
                        if (N(products[j]["item"]) == k)
                            row[j] = 1;
                    if (row.Any(x => x != 0))
                    {
                        rows.Add(row);
                        lower.Add(double.NegativeInfinity);
                        cap.Add(Math.Max(0, capacities[k] - stock.GetValueOrDefault(k)));
                    }
                }
            for (int i = 0; i < recipes.Length; i++)
            {
                long k = N(recipes[i]["output"]);
                if (!capacities.ContainsKey(k))
                    continue;
                var row = new double[n];
                row[products.Length + i] = N(recipes[i]["count"]);
                rows.Add(row);
                lower.Add(double.NegativeInfinity);
                cap.Add(Math.Max(0, capacities[k] - stock.GetValueOrDefault(k)));
            }
            var currentValue = (double[])obj.Clone();
            for (int i = 0; i < carryKeys.Length; i++) currentValue[carryStart + i] = -N(items[carryKeys[i]]["sale"]);
            future.Add(upper, obj, rows, lower, cap, cash, budget, carryStart);
            if (candidates.Count > 0)
            {
                var relaxation = DailyLinearOptimizer.Solve(obj, upper, rows, lower, cap, false, Math.Min(timeLimit, 2), check);
                if (relaxation?.Optimal == true && -relaxation.Objective - (includeBreakEven ? 0 : opening * N(state["potion_price"])) - baseValue < candidates.Max(RankingProfit) - .01)
                    continue;
            }
            var solved = DailyLinearOptimizer.Solve(obj, upper, rows, lower, cap, true, timeLimit, check, future.Start, future.IntegralRecipes);
            if (solved == null) { unresolvedMode = true; continue; }
            var x = solved.Values.ToArray();
            future.ReleaseFutureResales(x, carryStart, saleStart);
            bool tie = false;
            if (solved.Values.Length > 0)
            {
                double exact = obj.Select((a, j) => a * x[j]).Sum();
                // Maximize realizable current gain at the SAME joint profit before minimizing
                // cash. Otherwise equal-valued "cook tomorrow" can defer a profitable recipe forever.
                var second = DailyLinearOptimizer.Solve(currentValue, upper, [.. rows, obj], [.. lower, exact - 1e-6], [.. cap, exact + 1e-6], true, Math.Min(timeLimit, 2), check, future.Start, future.IntegralRecipes, x);
                if (second != null)
                {
                    x = second.Values.ToArray();
                    Require(Math.Abs(obj.Select((a, j) => a * x[j]).Sum() - exact) < 0.01, "Realization tie-break changed profit");
                    future.ReleaseFutureResales(x, carryStart, saleStart);
                    tie = second.Optimal;
                }
            }
            future.ReleaseFutureResales(x, carryStart, saleStart);
            long used = opening + (long)Math.Round(recipes.Select((r, i) => x[products.Length + i] * N(r["potions"])).Sum());
            long bought = Math.Max(0, used - N(state["potions"]));
            x[potionCol] = bought;
            var saleColumns = products.Select(p => saleStart + pos[N(p["item"])]).ToArray();
            var breakEvenProducts = Enumerable.Range(0, products.Length)
                .Where(j => prices[j] > 0 && prices[j] == N(items[N(products[j]["item"])]["sale"])).ToArray();
            // Remove arbitrary zero-profit solver ties first. Necessary recipe
            // ingredients are not excluded just because their raw resale is flat.
            foreach (int j in breakEvenProducts)
            {
                long q = (long)Math.Round(Math.Min(x[j], x[saleColumns[j]]));
                x[j] -= q; x[saleColumns[j]] -= q;
            }
            future.RemoveBreakEvenResales(x);
            var breakEven = includeBreakEven
                ? AllocateBreakEven(x, breakEvenProducts, saleColumns, prices, upper, rows, lower, cap, timeLimit, check)
                : new long[products.Length];
            Require(!x.Where((v, j) => v < -1e-6 || v > upper[j] + 1e-6).Any(), "Trade integer bounds failed");
            for (int i = 0; i < rows.Count; i++)
            {
                double v = rows[i].Select((a, j) => a * x[j]).Sum();
                Require(v >= lower[i] - 1e-4 && v <= cap[i] + 1e-4, "Trade feasibility failed");
            }
            double profit = -obj.Select((a, j) => a * x[j]).Sum() - opening * N(state["potion_price"]) - baseValue;
            double? bound = solved.Bound == null ? null : -solved.Bound - opening * N(state["potion_price"]) - baseValue;
            candidates.Add(new(mode, opening, x, profit, bound, solved.Optimal, tie, prices, (long)Math.Round(cash.Select((a, j) => a * x[j]).Sum()), used, bought, -(long)Math.Round(currentValue.Select((a, j) => a * x[j]).Sum()) - opening * N(state["potion_price"]) - baseValue, future.Report(x), breakEven));
        }
        Require(candidates.Count > 0, "No feasible trade plan");
        var best = candidates.OrderByDescending(RankingProfit)
            .ThenByDescending(c => c.CurrentProfit + RankingFee(c))
            .ThenByDescending(c => c.BreakEvenValue).ThenBy(c => c.Cash).ThenBy(c => c.Opening).First();
        var purchases = new JsonArray();
        var cooking = new JsonArray();
        var sales = new JsonArray();
        var holds = new JsonArray();
        for (int i = 0; i < carryKeys.Length; i++)
        {
            long k = carryKeys[i], q = (long)best.X[carryStart + i];
            if (q == 0)
                continue;
            var h = O(("item", k), ("name", items[k]["name"]), ("count", q), ("estimated_value", q * N(items[k]["sale"])));
            h["price"] = Copy(items[k]["sale"]);
            h["forecast_days"] = 29;
            h["reason"] = "future_recipe_bottleneck";
            holds.Add(h);
        }
        for (int j = 0; j < products.Length; j++)
        {
            long q = (long)best.X[j];
            if (q == 0)
                continue;
            var p = products[j];
            purchases.Add(O(("shop", p["shop"]), ("product", p["product"]), ("item", p["item"]), ("name", items[N(p["item"])]["name"]), ("count", q), ("price", best.Prices[j]), ("cost", q * best.Prices[j]), ("break_even_count", best.BreakEven[j])));
        }
        for (int i = 0; i < recipes.Length; i++)
        {
            long q = (long)best.X[products.Length + i];
            if (q == 0)
                continue;
            var r = recipes[i];
            cooking.Add(O(("recipe", r["id"]), ("item", r["output"]), ("name", items[N(r["output"])]["name"]), ("count", q), ("potions", q * N(r["potions"])), ("materials", new JsonObject(Quantities(r["materials"]).Select(p => new KeyValuePair<string, JsonNode?>(p.Key.ToString(), JsonValue.Create(p.Value * q))))), ("sale_date", NextSale(S(state["game_date"]), I(items[N(r["output"])]["day"])))));
        }
        var quotes = Rows(state["today_quotes"] ?? new JsonArray()).ToDictionary(q => (N(q["item"]), N(q["shop"])));
        for (int i = 0; i < keys.Length; i++)
        {
            long k = keys[i], q = (long)best.X[saleStart + i];
            if (q == 0)
                continue;
            var item = items[k];
            string when = NextSale(S(state["game_date"]), I(item["day"]));
            quotes.TryGetValue((k, N(item["shop"])), out var quote);
            bool today = when == S(state["game_date"]) && quote != null && N(quote["rate"]) == 120 && N(quote["price"]) == N(item["sale"]);
            sales.Add(O(("item", k), ("name", item["name"]), ("count", q), ("price", item["sale"]), ("value", q * N(item["sale"])), ("shop", item["shop"]), ("date", when), ("today_quote_confirmed", today)));
        }
        // Exact integer inventory and economic audits are independent of the matrix solver.
        var expected = keys.ToDictionary(k => k, k => available.GetValueOrDefault(k));
        foreach (var p in Rows(purchases))
            expected[N(p["item"])] += N(p["count"]);
        foreach (var r in Rows(cooking))
        {
            var recipe = recipes.Single(v => N(v["id"]) == N(r["recipe"]));
            expected[N(r["item"])] += N(r["count"]) * N(recipe["count"]);
            foreach (var p in Quantities(r["materials"]))
                expected[p.Key] -= p.Value;
        }
        var allocated = Rows(sales).Concat(Rows(holds)).GroupBy(r => N(r["item"])).ToDictionary(g => g.Key, g => g.Sum(r => N(r["count"])));
        Require(expected.All(p => p.Value >= 0 && allocated.GetValueOrDefault(p.Key) == p.Value), "Trade material audit failed");
        long buyCost = Rows(purchases).Sum(p => N(p["cost"])), saleValue = Rows(sales).Sum(s => N(s["value"])), holdValue = Rows(holds).Sum(h => N(h["estimated_value"]));
        Require(saleValue + holdValue - buyCost - best.Potions * N(state["potion_price"]) - baseValue == best.CurrentProfit, "Trade economic ledger failed");
        Require(Math.Abs(best.CurrentProfit - holdValue + best.Forecast["net_value"]!.GetValue<double>() - best.Profit) < 1e-4, "Trade joint forecast ledger failed");
        double? globalBound = unresolvedMode || candidates.Any(c => c.Bound == null) ? null : candidates.Max(c => c.Bound);
        double? strategyBound = globalBound == null ? null : candidates.Max(c => c.Bound + RankingFee(c));
        bool proven = strategyBound != null && strategyBound - RankingProfit(best) < .01;
        var warnings = new JsonArray();
        if (N(state["gold"]) < LowFunds)
            warnings.Add(O(("code", "low_working_capital"), ("message", Warning)));
        if (stock.Any(p => p.Value > 0 && !items.ContainsKey(p.Key)))
            warnings.Add(O(("code", "unvalued_stock"), ("message", "部分库存不在120%跑商范围，未用于采购或收益计算。")));
        return O(("schema", 1), ("mode", "plan_only"), ("context", state["context"]), ("captured_utc", state["captured_utc"]), ("game_date", state["game_date"]), ("input_hash", DailyTradeCatalog.Fingerprint(state)), ("catalog_hash", DailyTradeCatalog.Fingerprint(catalog)), ("settings", settings), ("warnings", warnings), ("gold", state["gold"]), ("cycle_days", 30),
            ("solver", O(("elapsed_seconds", Math.Round(started.Elapsed.TotalSeconds, 3)), ("engine", "dotnet-highs-1.15.1"), ("scope", "joint_current_and_29_day_supply"), ("optimal", proven), ("forecast_profit", best.Profit), ("profit_upper_bound", globalBound), ("gap_gold", globalBound == null ? null : Math.Max(0, globalBound.Value - best.Profit)), ("strategy_excludes_bargain_fee", includeBreakEven), ("strategy_profit", RankingProfit(best)), ("strategy_upper_bound", strategyBound), ("realization_tie_optimal", best.Tie), ("cash_tie_optimal", false))),
            ("summary", O(("incremental_profit", best.CurrentProfit), ("eventual_sale_value", saleValue), ("carryover_estimated_value", holdValue), ("initial_stock_value", baseValue), ("purchase_cost", buyCost), ("potions_used", best.Potions), ("potion_cost", N(state["potion_price"]) * best.Potions), ("potion_unit_price", state["potion_price"]), ("potions_to_buy", best.PotionBuy), ("cash_required", best.Cash), ("cash_remaining", N(state["gold"]) - best.Cash), ("break_even_count", best.BreakEven.Sum()), ("break_even_value", best.BreakEvenValue))),
            ("bargain", O(("mode", best.Mode), ("start", best.Mode == "bargain"), ("potions", best.Opening))), ("purchases", purchases), ("cooking", cooking), ("sales", sales), ("holds", holds), ("forecast", best.Forecast), ("drop_forecast", state["future_supply"]), ("resources_spent", false),
            ("notes", new[] { "共同分配当前库存与29轮已解锁供货，优先总收益；收益相同时优先完成本轮可盈利加工，并释放仅用于未来原料售卖的库存。", "只执行本轮采购与制作。已知周收集路线按掉落概率估计未来供给，不把预计掉落作为当前库存。", "本轮增益按原料高价卖价核账，未来收益单列且不用于今日垫资。库存变化后重新规划，料理等到120%报价确认再出售。" }));
    }
}

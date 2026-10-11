using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Shared 29-day supplies plus bounded end-stock value. Future actions are never executable orders.</summary>
internal sealed class DailyTradeForecast
{
    public long[] CarryKeys { get; }
    public int Start { get; }
    public int Count { get; }
    public int[] IntegralRecipes => Enumerable.Range(cookStart, recipes.Length).Concat(Enumerable.Range(tailStart, tail.Length)).ToArray();
    public double[] Cost { get; private set; } = [];
    private readonly JsonObject[] offers, recipes;
    private readonly Dictionary<long, JsonObject> items;
    private readonly long[] keys;
    private readonly JsonObject state;
    private readonly Dictionary<long, long> supply;
    private readonly int buyStart, cookStart, saleStart, tailStart, turnoverStart;
    private readonly (long Item, long Limit, double Value)[] tail;
    private readonly (long Item, long Stock, double Price, string Date)[] turnover;
    private double[] fundingCost = [];
    private long fundingBudget;
    private int currentSaleStart;
    private const int TurnoverDays = 7;
    private const double TurnoverShare = .5;

    public DailyTradeForecast(JsonObject catalog, JsonObject snapshot, int currentStart, IReadOnlyDictionary<long, long> available, Action? check = null)
    {
        state = snapshot; supply = DailyTradeDropForecast.Supply(catalog, snapshot);
        items = Rows(catalog["items"]).ToDictionary(i => N(i["id"]));
        keys = items.Keys.Order().ToArray();
        var visible = Rows(state["offers"]).Select(p => (N(p["shop"]), N(p["product"]))).ToHashSet();
        offers = Rows(catalog["offers"]).Where(p => visible.Contains((N(p["shop"]), N(p["product"])))).ToArray();
        var unlocked = state["recipes"]!.AsArray().Select(N).ToHashSet();
        recipes = Rows(catalog["recipes"]).Where(r => unlocked.Contains(N(r["id"]))).ToArray();
        CarryKeys = recipes.SelectMany(r => r["materials"]!.AsObject().Select(p => long.Parse(p.Key))).Distinct().Order().ToArray();
        Start = currentStart + CarryKeys.Length + 1;
        buyStart = Start; cookStart = buyStart + offers.Length; saleStart = cookStart + recipes.Length;
        tailStart = saleStart + keys.Length;
        tail = EndStockValues(check);
        turnoverStart = tailStart + tail.Length;
        var today = DateOnly.ParseExact(S(state["game_date"]), "yyyy-MM-dd");
        turnover = keys.Where(k => available.GetValueOrDefault(k) > 0 && N(items[k]["sale"]) > 0)
            .Select(k => (Item: k, Stock: available[k], Price: N(items[k]["sale"]) * TurnoverShare,
                Date: DailyTradeOptimizer.NextSale(S(state["game_date"]), I(items[k]["day"]))))
            .Where(p => DateOnly.ParseExact(p.Date, "yyyy-MM-dd").DayNumber - today.DayNumber <= TurnoverDays
                && (p.Date != S(state["game_date"]) || Rows(state["today_quotes"]).Any(q =>
                    N(q["item"]) == p.Item && N(q["shop"]) == N(items[p.Item]["shop"])
                    && N(q["rate"]) == 120 && N(q["price"]) == N(items[p.Item]["sale"]))))
            .ToArray();
        Count = turnoverStart + turnover.Length;
    }

    // A finite integer horizon calls an incomplete final batch "surplus" and
    // repeatedly spends it on lower-yield recipes. Value a bounded residue using
    // the dual prices of ONE joint recurring-supply LP, not per-recipe maxima.
    // Its fractions describe average long-run rates; all 29-day recipes and
    // ending stock quantities in the actual forecast remain integral.
    private (long Item, long Limit, double Value)[] EndStockValues(Action? check)
    {
        var daily = keys.ToDictionary(k => k, k => offers.Where(p => N(p["item"]) == k).Sum(p => (double)N(p["limit"])) + supply.GetValueOrDefault(k) / 29d);
        var viable = recipes.Where(r => r["materials"]!.AsObject().All(p => daily[long.Parse(p.Key)] > 0)
            && N(r["count"]) * N(items[N(r["output"])]["sale"]) > N(r["potions"]) * N(state["potion_price"]) + r["materials"]!.AsObject().Sum(p => N(p.Value) * N(items[long.Parse(p.Key)]["sale"]))).ToArray();
        var limits = CarryKeys.ToDictionary(k => k, k => viable.Select(r => N(r["materials"]?[k.ToString()]) - 1).DefaultIfEmpty(0).Max());
        if (!limits.Any(p => p.Value > 0)) return [];
        int cook = offers.Length, sell = cook + viable.Length, n = sell + keys.Length;
        var cost = new double[n]; var upper = Enumerable.Repeat(DailyLinearOptimizer.Infinity, n).ToArray();
        var balances = keys.ToDictionary(k => k, _ => new double[n]);
        for (int i = 0; i < offers.Length; i++)
        {
            var offer = offers[i]; balances[N(offer["item"])][i] = 1;
            upper[i] = N(offer["limit"]); cost[i] = Price(offer);
        }
        for (int i = 0; i < viable.Length; i++)
        {
            var recipe = viable[i]; int j = cook + i;
            foreach (var p in recipe["materials"]!.AsObject()) balances[long.Parse(p.Key)][j] -= N(p.Value);
            balances[N(recipe["output"])][j] += N(recipe["count"]);
            cost[j] = N(recipe["potions"]) * N(state["potion_price"]);
        }
        for (int i = 0; i < keys.Length; i++) { balances[keys[i]][sell + i] = -1; cost[sell + i] = -N(items[keys[i]]["sale"]); }
        var rhs = keys.Select(k => -supply.GetValueOrDefault(k) / 29d).ToArray();
        var solved = DailyLinearOptimizer.Solve(cost, upper, keys.Select(k => balances[k]).ToArray(), rhs, rhs, false, 2, check);
        if (solved?.Optimal != true) return [];
        return CarryKeys.Where(k => limits[k] > 0).Select(k => (Item: k, Limit: limits[k], Value: Math.Round(solved.RowDual[System.Array.IndexOf(keys, k)], 6)))
            .Where(p => double.IsFinite(p.Value) && p.Value > N(items[p.Item]["sale"]) + 1e-6).ToArray();
    }

    public void Add(double[] upper, double[] objective, List<double[]> rows, List<double> lower, List<double> caps,
        double[] currentCash, long budget, int carryStart, int currentSales)
    {
        int n = Count;
        Cost = new double[n];
        var balances = keys.ToDictionary(k => k, _ => new double[n]);
        var funding = (double[])currentCash.Clone();
        for (int i = 0; i < CarryKeys.Length; i++) balances[CarryKeys[i]][carryStart + i] = 1;
        for (int i = 0; i < offers.Length; i++)
        {
            var p = offers[i]; int j = buyStart + i;
            balances[N(p["item"])][j] = 1;
            Cost[j] = funding[j] = Price(p);
            upper[j] = 29 * N(p["limit"]);
        }
        for (int i = 0; i < recipes.Length; i++)
        {
            var r = recipes[i]; int j = cookStart + i;
            foreach (var p in r["materials"]!.AsObject()) balances[long.Parse(p.Key)][j] -= N(p.Value);
            balances[N(r["output"])][j] = N(r["count"]);
            Cost[j] = funding[j] = N(r["potions"]) * N(state["potion_price"]);
        }
        for (int i = 0; i < keys.Length; i++)
        {
            balances[keys[i]][saleStart + i] = -1;
            Cost[saleStart + i] = -N(items[keys[i]]["sale"]);
        }
        for (int i = 0; i < tail.Length; i++)
        {
            var ending = tail[i]; int j = tailStart + i;
            balances[ending.Item][j] = -1; upper[j] = ending.Limit; Cost[j] = -ending.Value;
        }
        foreach (var pair in balances) { rows.Add(pair.Value); lower.Add(-supply.GetValueOrDefault(pair.Key)); caps.Add(-supply.GetValueOrDefault(pair.Key)); }
        // Today's cash constraint remains unchanged. Only forecast funding can use
        // half of near-term sales of ORIGINAL, unlocked inventory, capped at 25%
        // of usable cash. Neither new purchases nor hypothetical drops earn credit.
        fundingBudget = budget; currentSaleStart = currentSales;
        fundingCost = (double[])funding.Clone();
        var turnoverCap = new double[n];
        for (int i = 0; i < turnover.Length; i++)
        {
            var p = turnover[i]; int j = turnoverStart + i;
            funding[j] = -1; turnoverCap[j] = 1;
            upper[j] = p.Stock * p.Price;
            var sold = new double[n]; sold[j] = 1;
            sold[currentSaleStart + System.Array.IndexOf(keys, p.Item)] = -p.Price;
            rows.Add(sold); lower.Add(double.NegativeInfinity); caps.Add(0);
        }
        rows.Add(turnoverCap); lower.Add(double.NegativeInfinity); caps.Add(budget / 4);
        rows.Add(funding); lower.Add(double.NegativeInfinity); caps.Add(budget);
        for (int j = Start; j < n; j++) objective[j] = Cost[j];
    }

    // Credit has no objective value. Normalize solver ties after resale cleanup,
    // so diagnostics show only the allowance actually needed by this plan.
    public void ReconcileTurnover(double[] x)
    {
        double need = Math.Max(0, fundingCost.Select((v, j) => v * x[j]).Sum() - fundingBudget);
        Require(need <= fundingBudget / 4 + 1e-4, "Forecast turnover allowance exceeded");
        for (int i = 0; i < turnover.Length; i++)
        {
            var p = turnover[i];
            double quantity = Math.Max(0, Math.Min(p.Stock, x[currentSaleStart + System.Array.IndexOf(keys, p.Item)]));
            double used = Math.Min(need, quantity * p.Price);
            x[turnoverStart + i] = used; need -= used;
        }
        Require(need <= 1e-4, "Forecast turnover is not backed by current saleable stock");
    }

    private long Price(JsonObject offer) => N(offer["base_price"]) * (B(state["can_bargain"]) || B(state["bargain_active"]) ? 40 : 100) / 100;

    // Equal-price future purchases/resales must not consume the spare funding
    // available to actual orders. Purchases needed for recipes remain intact.
    public void RemoveBreakEvenResales(double[] x)
    {
        for (int i = 0; i < offers.Length; i++)
        {
            long k = N(offers[i]["item"]);
            if (Price(offers[i]) != N(items[k]["sale"])) continue;
            int sale = saleStart + System.Array.IndexOf(keys, k);
            double q = Math.Min(x[buyStart + i], x[sale]);
            x[buyStart + i] -= q;
            x[sale] -= q;
        }
    }

    // Move any current stock assigned only to future raw resale back to this round.
    // Both resource balances and joint profit stay exactly unchanged.
    public void ReleaseFutureResales(double[] x, int carryStart, int currentSaleStart)
    {
        for (int i = 0; i < CarryKeys.Length; i++)
        {
            int k = System.Array.IndexOf(keys, CarryKeys[i]);
            long q = (long)Math.Min(Math.Round(x[carryStart + i]), Math.Floor(Math.Max(0, x[saleStart + k]) + 1e-7));
            x[carryStart + i] -= q;
            x[saleStart + k] -= q;
            x[currentSaleStart + k] += q;
        }
    }
    public JsonObject Report(double[] x) => O(("days", 29), ("executable", false), ("drop_supply", supply),
        ("net_value", -Cost.Select((v, j) => v * x[j]).Sum()),
        ("terminal_method", "bounded_batch_residue_joint_supply_duals"),
        ("terminal_option_value", tail.Select((p, i) => x[tailStart + i] * (p.Value - N(items[p.Item]["sale"]))).Sum()),
        ("terminal_stock", Array(tail.Select((p, i) => O(("item", p.Item), ("count", x[tailStart + i]), ("limit", p.Limit), ("unit_value", p.Value), ("raw_sale_price", items[p.Item]["sale"]))).Where(r => r["count"]!.GetValue<double>() > 1e-8))),
        ("purchase_cost", offers.Select((_, i) => (long)Cost[buyStart + i] * x[buyStart + i]).Sum()),
        ("potion_cost", recipes.Select((_, i) => (long)Cost[cookStart + i] * x[cookStart + i]).Sum()),
        ("turnover", O(("days", TurnoverDays), ("receipt_share", TurnoverShare), ("cash_limit", fundingBudget / 4),
            ("allowance_used", Enumerable.Range(turnoverStart, turnover.Length).Sum(i => x[i])),
            ("today_spendable", false), ("source", "existing_unlocked_stock_allocated_to_sale"),
            ("receipts", Array(turnover.Select((p, i) => O(("item", p.Item), ("date", p.Date),
                ("allowance", x[turnoverStart + i]))).Where(r => r["allowance"]!.GetValue<double>() > 1e-6))))),
        ("cooking", Array(recipes.Select((r, i) => O(("recipe", r["id"]), ("count", x[cookStart + i]))).Where(r => r["count"]!.GetValue<double>() > 1e-8))));
}

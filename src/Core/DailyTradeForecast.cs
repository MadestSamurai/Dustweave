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
    private readonly int buyStart, cookStart, saleStart, tailStart;
    private readonly (long Item, long Limit, double Value)[] tail;

    public DailyTradeForecast(JsonObject catalog, JsonObject snapshot, int currentStart, Action? check = null)
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
        Count = tailStart + tail.Length;
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
        double[] currentCash, long budget, int carryStart)
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
        // Forecast revenue never funds today's orders or duplicates current working capital.
        rows.Add(funding); lower.Add(double.NegativeInfinity); caps.Add(budget);
        for (int j = Start; j < n; j++) objective[j] = Cost[j];
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
        ("cooking", Array(recipes.Select((r, i) => O(("recipe", r["id"]), ("count", x[cookStart + i]))).Where(r => r["count"]!.GetValue<double>() > 1e-8))));
}

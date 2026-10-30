using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Rolling dated cash/inventory forecast; only today's integral orders leave this class.</summary>
internal static class DailyTradeCalendar
{
    private const int Days = 62;
    private static Dictionary<long,long> Quantities(JsonNode? value) => value is JsonObject obj
        ? obj.ToDictionary(p => long.Parse(p.Key,CultureInfo.InvariantCulture), p => N(p.Value)) : new();
    private sealed record Offer(long Item, long Limit, long Price, JsonObject? Source = null);
    private sealed class Model
    {
        public readonly List<double> Upper = [], Objective = [], Lower = [], Caps = [];
        public readonly List<IReadOnlyDictionary<int,double>> Rows = [];
        public readonly List<int> Integer = [];
        public int Variable(double upper, bool integer = false, double cost = 0)
        { int j = Upper.Count; Upper.Add(upper); Objective.Add(cost); if (integer) Integer.Add(j); return j; }
        public void Row(Dictionary<int,double> row, double low, double high) { Rows.Add(row); Lower.Add(low); Caps.Add(high); }
        public DailyLinearOptimizer.Result? Solve(double seconds, Action? check, double[]? cost = null, double[]? initial = null) =>
            DailyLinearOptimizer.SolveSparse(cost ?? Objective.ToArray(), Upper.ToArray(), Rows, Lower, Caps, true,
                seconds, check, 0, Integer.ToArray(), initial);
    }
    private sealed record Day(int[] Buy, int[] Cook, int[] Sell, int[] Stock, int Potions, int BuyPotions, int Cash, int Bargain);
    private sealed record Candidate(string Mode, long Fee, double[] X, double Value, bool Optimal, bool Tie,
        Day[] Calendar, Offer[] Offers, double? Bound, double Seconds);
    private static void Add(Dictionary<int,double> row, int key, double value)
    { if (value != 0) row[key] = row.GetValueOrDefault(key) + value; }

    public static JsonObject Plan(JsonObject catalog, JsonObject state, JsonObject settings, string catalogHash, double seconds, Action? check)
    {
        var started = Stopwatch.StartNew();
        var today = DateOnly.ParseExact(S(state["game_date"]), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var expectedPrices = DailyTradePriceChange.ExpectedPrices(catalog, today);
        var items = Rows(catalog["items"]).OrderBy(i => N(i["id"])).ToArray();
        var keys = items.Select(i => N(i["id"])).ToArray();
        var index = keys.Select((k,i) => (k,i)).ToDictionary(p => p.k, p => p.i);
        var unlocked = state["recipes"]!.AsArray().Select(N).ToHashSet();
        var recipes = Rows(catalog["recipes"]).Where(r => unlocked.Contains(N(r["id"]))).ToArray();
        var materials = recipes.Select(r => Quantities(r["materials"])).ToArray();
        var stock = Quantities(state["items"]);
        var reserved = Quantities(settings["reserve_items"]);
        Require(reserved.Keys.All(index.ContainsKey), "Unknown reserved item");
        foreach (var p in Quantities(state["protected_items"])) reserved[p.Key] = Math.Max(reserved.GetValueOrDefault(p.Key), p.Value);
        var available = keys.Select(k => Math.Max(0, stock.GetValueOrDefault(k) - reserved.GetValueOrDefault(k))).ToArray();
        var limits = Quantities(state["capacity_limits"]);
        var capacity = items.Select((i,j) => Math.Max(available[j],
            limits.GetValueOrDefault(keys[j], N(i["stack"])) - Math.Min(stock.GetValueOrDefault(keys[j]), reserved.GetValueOrDefault(keys[j])))).ToArray();
        var supply = DailyTradeDropForecast.Supply(catalog, state);
        var visible = Rows(state["offers"]).ToDictionary(p => (N(p["shop"]), N(p["product"])));
        var catalogOffers = Rows(catalog["offers"]).Where(p => visible.ContainsKey((N(p["shop"]),N(p["product"])))).ToArray();
        long gold = Math.Max(0, N(state["gold"]) - N(settings["reserve_gold"])), potionPrice = N(state["potion_price"]);
        long spend = Math.Min(gold, settings["max_spend"] == null ? gold : N(settings["max_spend"]));
        bool processing = state["processing_enabled"] == null || B(state["processing_enabled"]);
        bool bargain = B(state["can_bargain"]) || B(state["bargain_active"]);
        var futureOffers = catalogOffers.GroupBy(p => (Item:N(p["item"]), Price:N(p["base_price"]) * (bargain ? 40 : 100) / 100))
            .Select(g => new Offer(g.Key.Item, g.Sum(p => N(p["limit"])), g.Key.Price)).ToArray();
        long Price(int i, DateOnly date) => date >= DailyTradePriceChange.EffectiveDate && expectedPrices.TryGetValue(keys[i], out long price) ? price : N(items[i]["sale"]);
        bool TodaySale(int i) => processing && !DailyTradePriceChange.AwaitingPrice(catalog,keys[i],today) &&
            N(items[i]["sale"]) > 0 && I(items[i]["day"]) == today.Day && Rows(state["today_quotes"]).Any(q =>
                N(q["item"]) == keys[i] && N(q["shop"]) == N(items[i]["shop"]) && N(q["rate"]) == 120 && N(q["price"]) == N(items[i]["sale"]));
        var modes = B(state["bargain_active"]) ? new[] { ("active",0L) } : B(state["can_bargain"]) ? new[] { ("bargain",10L),("normal",0L) } : new[] { ("normal",0L) };
        var candidates = new List<Candidate>();
        foreach (var (mode, fee) in modes)
        {
            check?.Invoke();
            var offers = catalogOffers.Select(p => (Catalog:p, State:visible[(N(p["shop"]),N(p["product"]))]))
                .Where(p => N(p.State["remaining"]) > 0).Select(p => new Offer(N(p.Catalog["item"]), N(p.State["remaining"]),
                    N(p.State[mode == "normal" ? "price" : "bargain_price"]), p.Catalog)).ToArray();
            var model = new Model(); var days = new Day[Days];
            for (int t = 0; t < Days; t++)
            {
                var date = today.AddDays(t); bool now = t == 0;
                var dayOffers = now ? offers : futureOffers;
                var buy = dayOffers.Select(o => model.Variable(o.Limit, now)).ToArray();
                // Without a sale today or imminent ingredient overflow, cooking a
                // day earlier has no benefit. Deferring is safe even if every
                // available product is bought tomorrow and keeps pills liquid.
                bool CookNow(int j) => processing && (TodaySale(index[N(recipes[j]["output"])]) || materials[j].Any(p =>
                    available[index[p.Key]] + offers.Where(o=>o.Item==p.Key).Sum(o=>o.Limit) +
                    futureOffers.Where(o=>o.Item==p.Key).Sum(o=>o.Limit) + supply.GetValueOrDefault(p.Key)/29d > capacity[index[p.Key]]));
                var cook = recipes.Select((r,j) => model.Variable(now && !CookNow(j) ? 0 :
                    materials[j].Min(p => (now ? available[index[p.Key]] + offers.Where(o=>o.Item==p.Key).Sum(o=>o.Limit) : capacity[index[p.Key]]) / p.Value), now)).ToArray();
                var sell = items.Select((i,j) => model.Variable((now ? TodaySale(j) : date.Day == I(i["day"]) && Price(j,date) > 0) ? capacity[j] : 0, now)).ToArray();
                var end = capacity.Select(cap => model.Variable(cap)).ToArray();
                int pills = model.Variable(1e12), buyPills = model.Variable(N(state["potion_buy_limit"]), now), walletColumn = model.Variable(1e12);
                int activation = !now && bargain && futureOffers.Length > 0 ? model.Variable(1, true) : -1;
                days[t] = new(buy,cook,sell,end,pills,buyPills,walletColumn,activation);
                var costs = new Dictionary<int,double> { [buyPills] = potionPrice };
                for (int j=0;j<buy.Length;j++) Add(costs,buy[j],dayOffers[j].Price);
                // Buying and cooking precede sales in the production executor.
                var funding = new Dictionary<int,double>(costs);
                if (!now) Add(funding,days[t-1].Cash,-1);
                model.Row(funding,double.NegativeInfinity,now ? spend : 0);
                var wallet = new Dictionary<int,double>(funding) { [walletColumn] = 1 };
                for(int i=0;i<keys.Length;i++) Add(wallet,sell[i],-Price(i,date));
                model.Row(wallet,now ? gold : 0,now ? gold : 0);
                var potion = new Dictionary<int,double> { [pills]=1, [buyPills]=-1 };
                if (!now) Add(potion,days[t-1].Potions,-1);
                for(int j=0;j<cook.Length;j++) Add(potion,cook[j],N(recipes[j]["potions"]));
                if (activation >= 0)
                {
                    Add(potion,activation,10);
                    var use = new Dictionary<int,double>();
                    for(int j=0;j<buy.Length;j++) Add(use,buy[j],1);
                    Add(use,activation,-dayOffers.Sum(o=>o.Limit));
                    model.Row(use,double.NegativeInfinity,0);
                }
                double initialPills = now ? N(state["potions"]) - fee : 0;
                model.Row(potion,initialPills,initialPills);
                if(now)
                {
                    // Do not buy optional future pill stock merely because its
                    // terminal asset value cancels its cost in the objective.
                    long owned=N(state["potions"]);
                    if(owned==0) model.Row(new() { [pills]=1 },0,0);
                    else
                    {
                        int refill=model.Variable(1,true);
                        model.Row(new() { [buyPills]=1,[refill]=-N(state["potion_buy_limit"]) },double.NegativeInfinity,0);
                        model.Row(new() { [pills]=1,[refill]=owned },double.NegativeInfinity,owned);
                    }
                }
                for(int i=0;i<keys.Length;i++)
                {
                    var flow = new Dictionary<int,double> { [end[i]]=1, [sell[i]]=1 };
                    var before = new Dictionary<int,double>();
                    double drop = now ? 0 : supply.GetValueOrDefault(keys[i])/29d;
                    if (!now) { Add(flow,days[t-1].Stock[i],-1); Add(before,days[t-1].Stock[i],1); }
                    for(int j=0;j<buy.Length;j++) if(dayOffers[j].Item == keys[i]) { Add(flow,buy[j],-1); Add(before,buy[j],1); }
                    // Only forecast drops can be lost at capacity; no disposal of real inventory.
                    if(drop>0) { int lost = model.Variable(drop); Add(flow,lost,1); Add(before,lost,-1); }
                    double initial = now ? available[i] : 0;
                    model.Row(before,double.NegativeInfinity,capacity[i]-initial-drop);
                    var output = new Dictionary<int,double>(before);
                    for(int j=0;j<cook.Length;j++)
                    {
                        Add(flow,cook[j],materials[j].GetValueOrDefault(keys[i]));
                        if(N(recipes[j]["output"])==keys[i]) { Add(flow,cook[j],-N(recipes[j]["count"])); Add(output,cook[j],N(recipes[j]["count"])); }
                    }
                    model.Row(output,double.NegativeInfinity,capacity[i]-initial-drop);
                    model.Row(flow,initial+drop,initial+drop);
                }
            }
            var last = days[^1];
            model.Objective[last.Cash]=-1; model.Objective[last.Potions]=-potionPrice;
            for(int i=0;i<keys.Length;i++) model.Objective[last.Stock[i]]=-Price(i,today.AddDays(Days-1));
            var result = model.Solve(Math.Max(.1,seconds / modes.Length),check);
            if(result == null) continue;
            var x=result.Values; bool tie=false;
            // Equal economic value: realize receipts early and keep working capital liquid.
            var primary=model.Objective.ToArray();
            model.Row(primary.Select((v,j)=>(v,j)).Where(p=>p.v!=0).ToDictionary(p=>p.j,p=>p.v),result.Objective-.001,result.Objective+.001);
            if(B(settings["include_break_even_resales"]))
            {
                var achievement=new double[primary.Length];
                for(int j=0;j<offers.Length;j++) if(offers[j].Price==N(items[index[offers[j].Item]]["sale"])) achievement[days[0].Buy[j]]=-1;
                if(achievement.Any(v=>v!=0))
                {
                    var extra=model.Solve(Math.Min(2,seconds/modes.Length),check,achievement,x);
                    if(extra!=null)
                    {
                        x=extra.Values; double amount=achievement.Zip(x,(a,b)=>a*b).Sum();
                        model.Row(achievement.Select((v,j)=>(v,j)).Where(p=>p.v!=0).ToDictionary(p=>p.j,p=>p.v),amount-.001,amount+.001);
                    }
                }
            }
            var liquid = new double[primary.Length];
            foreach(var d in days) liquid[d.Cash]=-1;
            foreach(int j in days[0].Cook) liquid[j]=.001;
            var second=model.Solve(Math.Min(2,seconds / modes.Length),check,liquid,x);
            if(second!=null) { x=second.Values; tie=second.Optimal; }
            candidates.Add(new(mode,fee,x,-primary.Zip(x,(a,b)=>a*b).Sum(),result.Optimal,tie,days,offers,
                result.Bound is double bound ? -bound : null,started.Elapsed.TotalSeconds));
        }
        // Never turn a solver timeout into an old-price liquidation. Retain inventory
        // and return a visible no-order plan if no feasible candidate was found.
        var best=candidates.OrderByDescending(c=>c.Value).FirstOrDefault();
        var purchases=new JsonArray(); var cooking=new JsonArray(); var sales=new JsonArray(); var holds=new JsonArray();
        var remaining=keys.Select((k,i)=>(k,v:available[i])).ToDictionary(p=>p.k,p=>p.v);
        long potionUse=best?.Fee??0, potionBuy=0;
        if(best!=null)
        {
            var d=best.Calendar[0]; long Qty(int j)=>checked((long)Math.Round(best.X[j]));
            potionBuy=Qty(d.BuyPotions);
            for(int j=0;j<best.Offers.Length;j++)
            {
                long q=Qty(d.Buy[j]); if(q==0)continue;
                var o=best.Offers[j]; var item=items[index[o.Item]];
                purchases.Add(O(("shop",o.Source!["shop"]),("product",o.Source["product"]),("item",o.Item),("name",item["name"]),("count",q),("price",o.Price),("cost",q*o.Price),
                    ("break_even_count",B(settings["include_break_even_resales"]) && o.Price==N(item["sale"]) ? q : 0)));
                remaining[o.Item]+=q;
            }
            for(int j=0;j<recipes.Length;j++)
            {
                long q=Qty(d.Cook[j]); if(q==0)continue;
                var r=recipes[j]; long item=N(r["output"]); potionUse+=q*N(r["potions"]);
                remaining[item]+=q*N(r["count"]);
                foreach(var p in materials[j]) remaining[p.Key]-=q*p.Value;
                cooking.Add(O(("recipe",r["id"]),("item",item),("name",items[index[item]]["name"]),("count",q),("potions",q*N(r["potions"])),
                    ("materials",O(materials[j].Select(p=>(p.Key.ToString(),(object?)(q*p.Value))).ToArray())),("sale_date",DailyTradeOptimizer.NextSale(S(state["game_date"]),I(items[index[item]]["day"])))));
            }
            for(int i=0;i<keys.Length;i++)
            {
                long q=Qty(d.Sell[i]); if(q==0)continue;
                Require(TodaySale(i),"Calendar sale quote audit failed"); remaining[keys[i]]-=q;
                sales.Add(O(("item",keys[i]),("name",items[i]["name"]),("count",q),("price",items[i]["sale"]),("value",q*N(items[i]["sale"])),("shop",items[i]["shop"]),("date",state["game_date"]),("today_quote_confirmed",true)));
            }
        }
        foreach(var p in remaining.Where(p=>p.Value>0))
            holds.Add(O(("item",p.Key),("name",items[index[p.Key]]["name"]),("count",p.Value),("price",items[index[p.Key]]["sale"]),
                ("estimated_value",p.Value*N(items[index[p.Key]]["sale"])),("reason","dated_cash_inventory_allocation"),("forecast_days",Days-1)));
        long cost=Rows(purchases).Sum(p=>N(p["cost"])), cash=cost+potionBuy*potionPrice;
        long value=Rows(sales).Sum(p=>N(p["value"])), held=Rows(holds).Sum(p=>N(p["estimated_value"]));
        long initialValue=keys.Select((k,i)=>available[i]*N(items[i]["sale"])).Sum();
        Require(remaining.Values.All(q=>q>=0) && cash<=spend && potionUse<=N(state["potions"])+potionBuy && potionBuy<=N(state["potion_buy_limit"]),"Calendar current resource audit failed");
        // Independent current execution audit; no future receipt, supply or fractional recipe is executable.
        foreach(var o in Rows(purchases)) Require(N(o["count"])<=N(visible[(N(o["shop"]),N(o["product"]))]["remaining"]),"Calendar current stock audit failed");
        foreach(long k in keys)
        {
            long bought=Rows(purchases).Where(o=>N(o["item"])==k).Sum(o=>N(o["count"]));
            long made=Rows(cooking).Where(o=>N(o["item"])==k).Sum(o=>N(o["count"])*N(recipes.Single(r=>N(r["id"])==N(o["recipe"]))["count"]));
            Require(available[index[k]]+bought+made<=capacity[index[k]],"Calendar current capacity audit failed");
        }
        var warnings=new JsonArray();
        if(N(state["gold"])<DailyTradeOptimizer.LowFunds)warnings.Add(O(("code","low_working_capital"),("message",DailyTradeOptimizer.Warning)));
        if(best==null)warnings.Add(O(("code","calendar_no_solution"),("message","本次未能完成跑商日期规划，未生成采购、料理或售卖订单；库存已保留，可稍后重新预览。")));
        if(keys.Any(k=>DailyTradePriceChange.AwaitingPrice(catalog,k,today)))warnings.Add(O(("code","price_change_pending"),("message","料理改价尚未在当前游戏数据中确认，受影响料理暂不出售；其他已确认交易照常规划。")));
        var timeline=new JsonArray();
        if(best!=null) for(int t=0;t<Days;t++)
        {
            var d=best.Calendar[t];
            timeline.Add(O(("date",today.AddDays(t).ToString("yyyy-MM-dd")),("cash_above_reserve",Math.Round(best.X[d.Cash],2)),
                ("purchases",Math.Round(d.Buy.Sum(j=>best.X[j]),2)),("cooking",Math.Round(d.Cook.Sum(j=>best.X[j]),2)),
                ("sales",Math.Round(d.Sell.Sum(j=>best.X[j]),2)),("executable",false)));
        }
        double baseline=gold+initialValue+N(state["potions"])*potionPrice;
        double forecast=(best?.Value??baseline)-baseline;
        return O(("schema",1),("mode","plan_only"),("context",state["context"]),("captured_utc",state["captured_utc"]),("game_date",state["game_date"]),
            ("input_hash",DailyTradeCatalog.Fingerprint(state)),("catalog_hash",catalogHash),("settings",settings),("warnings",warnings),("gold",state["gold"]),("cycle_days",Days),
            ("solver",O(("elapsed_seconds",Math.Round(started.Elapsed.TotalSeconds,3)),("engine","dotnet-highs-1.15.1"),("scope","dated_cash_inventory"),
                ("optimal",best!=null && candidates.Count==modes.Length && candidates.All(c=>c.Optimal)),("forecast_profit",forecast),
                ("profit_upper_bound",candidates.Count==modes.Length && candidates.All(c=>c.Bound!=null) ? candidates.Max(c=>c.Bound)-baseline : null),
                ("strategy_profit",forecast),("realization_tie_optimal",best?.Tie??false),("cash_tie_optimal",best?.Tie??false))),
            ("summary",O(("incremental_profit",value+held-initialValue-cost-potionUse*potionPrice),("eventual_sale_value",value),("carryover_estimated_value",held),
                ("initial_stock_value",initialValue),("purchase_cost",cost),("potions_used",potionUse),("potion_cost",potionUse*potionPrice),("potion_unit_price",potionPrice),
                ("potions_to_buy",potionBuy),("cash_required",cash),("cash_remaining",N(state["gold"])-cash),
                ("break_even_count",Rows(purchases).Sum(o=>N(o["break_even_count"]))),("break_even_value",Rows(purchases).Sum(o=>N(o["break_even_count"])*N(o["price"]))))),
            ("bargain",O(("mode",best?.Mode??"normal"),("start",best?.Mode=="bargain"),("potions",best?.Fee??0))),
            ("purchases",purchases),("cooking",cooking),("sales",sales),("holds",holds),
            ("forecast",O(("days",Days-1),("net_value",forecast),("calendar",timeline),("future_orders_executable",false),
                ("future_recipes_relaxed",true),("effective_date",DailyTradePriceChange.EffectiveDate.ToString("yyyy-MM-dd")),
                ("announced_prices",expectedPrices.Count),("processing_enabled_today",processing))),
            ("drop_forecast",state["future_supply"]),("resources_spent",false));
    }
}

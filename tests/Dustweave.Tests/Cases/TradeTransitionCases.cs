using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TradeTransitionCases
{
    public static Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
        JsonObject Item(long id,long sale,int day=31,long stack=99999)=>O(("id",id),("name","item"+id),("sale",sale),("day",day),("shop",1),("stack",stack));
        JsonObject Recipe(long id,long item,long amount)=>O(("id",id),("output",item),("count",1),("potions",1),("materials",O(("1",amount))));
        JsonObject Offer(long item,long price,long limit)=>O(("shop",1),("product",item),("item",item),("base_price",price),("limit",limit));
        JsonObject Catalog()=>O(("schema",1),("client","oracle"),("database_sha256","oracle"),
            ("items",new[]{Item(1,1),Item(2002,33,1),Item(9000,100)}),
            ("recipes",new[]{Recipe(101,2002,1),Recipe(999,9000,2)}),("offers",new JsonArray()));
        JsonObject State(JsonObject c,long gold=1000)=>O(("schema",1),
            ("context",O(("account","a"),("player","b"),("server","s"),("cycle","1"),("client",c["client"]),("database_sha256",c["database_sha256"]))),
            ("captured_utc","2026-11-01T00:00:00Z"),("game_date","2026-11-01"),("inventory_complete",true),("offers_complete",true),("recipes_complete",true),
            ("gold",gold),("potions",0),("potion_price",45),("potion_buy_limit",10000),("can_bargain",false),("bargain_active",false),
            ("items",new JsonObject()),("recipes",Array(Rows(c["recipes"]).Select(r=>r["id"]))),
            ("offers",Array(Rows(c["offers"]).Select(o=>O(("shop",1),("product",o["product"]),("remaining",o["limit"]),("price",o["base_price"]),("bargain_price",N(o["base_price"])*40/100))))),
            ("today_quotes",new JsonArray(O(("item",2002),("shop",1),("rate",120),("price",33)))));
        long Count(JsonObject p,string field,long item)=>Rows(p[field]).Where(r=>N(r["item"])==item).Sum(r=>N(r["count"]));
        // Independent finite oracle: no replenishment or drops. Enumerate every
        // recipe mix and early-sale decision, including zero capital. Current
        // receipts may finance later pills, never today's pills.
        var c=Catalog();
        foreach(int raw in Enumerable.Range(0,7))foreach(int owned in Enumerable.Range(0,3))foreach(int gold in new[]{0,15,30,45,90})
        {
            var s=State(c,gold);s["items"]=O(("1",raw),("2002",owned));
            var p=DailyTradeOptimizer.Plan(c,s,timeLimit:3);
            long optimum=long.MinValue;
            for(int sold=0;sold<=owned;sold++)for(int a=0;a<=raw;a++)for(int b=0;b<=raw/2;b++)
                if(a+2*b<=raw && 45*(a+b)<=gold+33*sold)
                    optimum=Math.Max(optimum,66*(owned-sold)+33*sold+66*a+100*b+raw-a-2*b-45*(a+b)-33*owned-raw);
            Check(Math.Abs(p["solver"]!["forecast_profit"]!.GetValue<double>()-optimum)<.001,$"transition exhaustive allocation raw={raw} stock={owned} gold={gold}");
            Check(N(p["summary"]!["cash_required"])<=gold,$"transition cannot spend unrealized receipts today {raw}/{owned}/{gold}");
        }
        // A receipt on day 28 allows only the last day's supply, not 29 days of
        // purchases retroactively. A no-cash account has no earlier purchases.
        c=Catalog();c["items"]![1]!["day"]=29;c["items"]![0]!["sale"]=10;
        c["offers"]=new JsonArray(Offer(1,1,2));
        var late=State(c,0);late["recipes"]=new JsonArray();late["items"]=O(("2002",1));late["today_quotes"]=new JsonArray();
        var latePlan=DailyTradeOptimizer.Plan(c,late);
        Check(Math.Abs(latePlan["forecast"]!["purchase_cost"]!.GetValue<double>()-2)<.001 && Rows(latePlan["purchases"]).Length==0,"late receipt can buy only supply remaining after receipt date");
        Check(Count(latePlan,"holds",2002)==1 && Count(latePlan,"sales",2002)==0,"post-change proceeds stay in deferred allocation");
        // Unchanged finished goods have no carry/deferred variable at all.
        // Even a deliberately tiny solver budget must preserve their sale.
        c=Catalog();c["items"]!.AsArray().Add(Item(2031,729,1));
        var timed=State(c);timed["items"]=O(("2031",7),("2002",3));timed["today_quotes"]!.AsArray().Add(O(("item",2031),("shop",1),("rate",120),("price",729)));
        var timedPlan=DailyTradeOptimizer.Plan(c,timed,timeLimit:.0001);
        Check(Count(timedPlan,"sales",2031)==7 && Count(timedPlan,"holds",2002)==3,"bounded solving cannot strand unchanged fish sticks or liquidate rising food");
        Check(!B(timedPlan["solver"]!["optimal"]) || timedPlan["solver"]!["profit_upper_bound"]!=null,"optimality requires a bound even under tiny budgets");
        // An unrelated price table row must not activate transition financing
        // when this account neither owns nor can craft the affected food.
        timed["items"]=O(("2031",7));timed["recipes"]=new JsonArray();
        var untouched=DailyTradeOptimizer.Plan(c,timed);
        Check(N(untouched["forecast"]!["announced_prices"])==0 && untouched["forecast"]!["transition_funding"]==null,"unavailable price-change recipes do not alter normal funding");
        timed["recipes"]=Array(Rows(c["recipes"]).Select(r=>r["id"]));
        untouched=DailyTradeOptimizer.Plan(c,timed);
        Check(N(untouched["forecast"]!["announced_prices"])==0 && untouched["forecast"]!["transition_funding"]==null,"learned but materially impossible changed recipes do not alter unrelated trading");
        c=Catalog();c["items"]!.AsArray().Add(Item(2031,729,1));
        var allocated=State(c,100000);allocated["items"]=O(("1",2),("2002",3),("2031",7));
        allocated["protected_items"]=O(("2002",1),("2031",1));
        allocated["today_quotes"]!.AsArray().Add(O(("item",2031),("shop",1),("rate",120),("price",729)));
        var allocation=DailyTradeOptimizer.Plan(c,allocated,O(("reserve_items",O(("2002",1),("2031",2)))));
        Check(Rows(allocation["cooking"]).Where(r=>N(r["item"])==2002).All(r=>S(r["sale_date"])=="2026-12-01"),"deferred cooking reports its actual post-change peak date");
        var wallet=O(("items",O(("2002",5),("2031",7))),
            ("stacks",new[]{O(("id",2002),("type",5),("invenIndex",1),("count",5)),
                O(("id",2031),("type",5),("invenIndex",2),("count",1),("keepFlag",true)),
                O(("id",2031),("type",5),("invenIndex",3),("count",6))}));
        var actualSales=Rows(DailyTradeProof.SaleStacks(allocation,wallet,c));
        Check(actualSales.All(r=>N(r["item"])==2031 && N(r["instance"])==3) && actualSales.Sum(r=>N(r["count"]))==5,"execution keeps deferred dishes and both locked and explicit reserves while selling ordinary fish sticks");
        c=Catalog();c["items"]=new JsonArray(Item(1,6),Item(2006,51,16));
        c["recipes"]=new JsonArray(Recipe(104,2006,1));c["offers"]=new JsonArray(Offer(1,27,5));
        var zero=State(c,100000);zero["today_quotes"]=new JsonArray();
        var zeroPlan=DailyTradeOptimizer.Plan(c,zero);
        Check(Count(zeroPlan,"purchases",1)==0 && Count(zeroPlan,"cooking",2006)==0 && B(zeroPlan["solver"]!["cash_tie_optimal"]),"transition does not tie up cash in a zero-gain purchased cooking cycle");
        zero["items"]=O(("1",5));var ownedPlan=DailyTradeOptimizer.Plan(c,zero);
        Check(Count(ownedPlan,"cooking",2006)==5 && Count(ownedPlan,"purchases",1)==0,"liquidity tie never delays profitable processing of owned ingredients");
        // Roll actual orders through the whole transition. Future forecasts are
        // deliberately ignored by this independent wallet; only confirmed daily
        // peak sales add gold, and the catalog changes on maintenance day.
        foreach(long opening in new long[]{0,45,1000})
        {
            c=Catalog();c["items"]=new JsonArray(Item(1,2,1),Item(2006,51,19),Item(2031,100,3));
            c["recipes"]=new JsonArray(Recipe(104,2006,1),Recipe(999,2031,2));
            c["offers"]=new JsonArray(Offer(1,1,5));
            long cash=opening,pills=0,soldFish=0,soldChanged=0;
            var stock=new Dictionary<long,long>{{1,7},{2006,4},{2031,3}};
            for(var day=new DateOnly(2026,10,31);day<=new DateOnly(2026,11,22);day=day.AddDays(1))
            {
                if(day==new DateOnly(2026,11,19))c["items"]![1]!["sale"]=72;
                var s=State(c,cash);s["game_date"]=day.ToString("yyyy-MM-dd");s["potions"]=pills;
                s["items"]=new JsonObject(stock.Select(kv=>KeyValuePair.Create<string,JsonNode?>(kv.Key.ToString(),JsonValue.Create(kv.Value))));
                s["today_quotes"]=Array(Rows(c["items"]).Where(i=>N(i["day"])==day.Day).Select(i=>O(("item",i["id"]),("shop",1),("rate",120),("price",i["sale"]))));
                var p=DailyTradeOptimizer.Plan(c,s,timeLimit:3);
                cash-=N(p["summary"]!["cash_required"]);pills+=N(p["summary"]!["potions_to_buy"]);
                foreach(var buy in Rows(p["purchases"]))stock[N(buy["item"])]+=N(buy["count"]);
                foreach(var cook in Rows(p["cooking"]))
                {
                    stock[N(cook["item"])]+=N(cook["count"]);pills-=N(cook["potions"]);
                    foreach(var material in cook["materials"]!.AsObject())stock[long.Parse(material.Key)]-=N(material.Value);
                }
                foreach(var sale in Rows(p["sales"]).Where(r=>B(r["today_quote_confirmed"])))
                {
                    long id=N(sale["item"]),q=N(sale["count"]);stock[id]-=q;cash+=q*N(sale["price"]);
                    if(id==2031)soldFish+=q;if(id==2006)soldChanged+=q;
                    Check(S(sale["date"])==day.ToString("yyyy-MM-dd") && N(sale["price"])==N(Rows(c["items"]).Single(i=>N(i["id"])==id)["sale"]),$"rolling sale uses actual date and quote {opening}/{day}/{id}");
                }
                Check(cash>=0 && pills>=0 && stock.All(kv=>kv.Value>=0) && Count(p,"purchases",1)<=5,$"rolling transition resources remain feasible {opening}/{day}");
            }
            Check(soldFish>=3 && soldChanged>=4,$"rolling transition sells unchanged food and releases held food after actual price change {opening}");
        }
        // Independent packing oracle fills actual partial stacks one unit at a
        // time. It never calls DailyTradeStorage or uses its ceiling formula.
        c=Catalog();c["recipes"]=new JsonArray();c["items"]=new JsonArray(Item(1,20,1,3),Item(2,30,1,3));
        c["offers"]=new JsonArray(Offer(1,5,4),Offer(2,5,4));
        for(int a=1;a<=3;a++)for(int b=1;b<=3;b++)for(int free=0;free<=2;free++)
        {
            var s=State(c,100000);s["items"]=O(("1",a+b));s["today_quotes"]=new JsonArray(O(("item",1),("shop",1),("rate",120),("price",20)),O(("item",2),("shop",1),("rate",120),("price",30)));
            s["storage"]=O(("slots",10+free),("used",10),("stacks",new JsonArray(O(("id",1),("count",a)),O(("id",1),("count",b)))));
            bool Fits(int x,int y)
            {
                var stacks=new List<(int Item,int Count)>{(1,a),(1,b)};int remaining=free;
                foreach(int item in Enumerable.Repeat(1,x).Concat(Enumerable.Repeat(2,y)))
                {
                    int slot=stacks.FindIndex(p=>p.Item==item && p.Count<3);
                    if(slot>=0)stacks[slot]=(item,stacks[slot].Count+1);
                    else{if(remaining--<=0)return false;stacks.Add((item,1));}
                }
                return true;
            }
            int best=0;for(int x=0;x<=4;x++)for(int y=0;y<=4;y++)if(Fits(x,y))best=Math.Max(best,15*x+25*y);
            var p=DailyTradeOptimizer.Plan(c,s);
            Check(N(p["summary"]!["incremental_profit"])==best && Fits((int)Count(p,"purchases",1),(int)Count(p,"purchases",2)),$"shared bag optimum agrees with unit packing {a}/{b}/{free}");
        }
        DailyJson.Write(Path.Combine(output,"transition-late-receipt.json"),latePlan);
        return Task.CompletedTask;
    }
}

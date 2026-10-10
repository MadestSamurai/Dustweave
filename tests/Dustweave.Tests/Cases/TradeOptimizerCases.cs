using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TradeOptimizerCases
{
 public static Task Run(string output,List<string> cases)
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
  void Reject(Action a,string name){bool bad=false;try{a();}catch(Exception e)when(e is InvalidDataException or ArgumentException){bad=true;}Check(bad,name);}
  JsonObject Item(long id,long price)=>O(("id",id),("name","item"+id),("sale",price),("day",1),("shop",1),("stack",99999));
  JsonObject Recipe(long id,long output,long potion,params (string Key,object? Value)[] parts)=>O(("id",id),("output",output),("count",1),("potions",potion),("materials",O(parts)));
  JsonObject Catalog(JsonObject[] items,JsonObject[] recipes,JsonObject[]? offers=null)=>O(("schema",1),("client","synthetic"),("database_sha256","synthetic"),("items",Array(items)),("recipes",Array(recipes)),("offers",Array(offers??[])));
  JsonObject State(JsonObject c,JsonObject stock)=>O(("schema",1),("context",O(("account","a"),("player","b"),("server","s"),("cycle","1"),("client",c["client"]),("database_sha256",c["database_sha256"]))),("captured_utc","2026-10-10T00:00:00Z"),("game_date","2026-10-10"),("inventory_complete",true),("offers_complete",true),("recipes_complete",true),("gold",100000000),("potions",10000),("potion_price",45),("potion_buy_limit",10000),("can_bargain",false),("bargain_active",false),("items",stock),("recipes",Array(Rows(c["recipes"]).Select(r=>r["id"]))),("offers",Array(Rows(c["offers"]).Select(r=>O(("shop",r["shop"]),("product",r["product"]),("remaining",0),("price",r["base_price"]),("bargain_price",N(r["base_price"])*40/100))))),("today_quotes",new JsonArray()));
  JsonObject Supply(JsonObject c,JsonObject items)=>O(("days",29),("source","synthetic"),("database_sha256",c["database_sha256"]),("items",items));
  long Cooks(JsonObject p,long id)=>Rows(p["cooking"]).Where(x=>N(x["recipe"])==id).Sum(x=>N(x["count"]));
  long Holds(JsonObject p,long id)=>Rows(p["holds"]).Where(x=>N(x["item"])==id).Sum(x=>N(x["count"]));
  var c=Catalog([Item(1,12),Item(2,6),Item(3,330)],[Recipe(1,3,2,("1",2),("2",3))]);
  var s=State(c,O(("1",100),("2",150)));var p=DailyTradeOptimizer.Plan(c,s);
  Check(Cooks(p,1)==50&&Holds(p,1)==0,"profitable ready processing is completed rather than perpetually deferred");
  Check(N(p["summary"]!["incremental_profit"])==50*(330-90-24-18),"processing ledger charges raw opportunity value and all talent potions");

  var pie=Catalog([Item(1,36),Item(2,216),Item(3,1481-36),Item(4,2292),Item(5,2484)],
   [Recipe(1,4,10,("1",1),("2",5)),Recipe(2,5,10,("1",1),("3",1))]);
  s=State(pie,O(("1",12),("2",10),("3",12)));p=DailyTradeOptimizer.Plan(pie,s);
  Check(Cooks(p,1)==2&&Cooks(p,2)==10,"scarce partner limits preferred recipe, excess shared ingredient earns profit in second recipe");
  var synergy=Catalog([Item(1,1),Item(2,1),Item(3,120),Item(4,500)],[Recipe(1,3,1,("1",1)),Recipe(2,4,1,("1",1),("2",1))]);
  s=State(synergy,O(("1",10)));s["future_supply"]=Supply(synergy,O(("2",10L)));p=DailyTradeOptimizer.Plan(synergy,s);
  Check(Cooks(p,1)==0&&Cooks(p,2)==0&&Holds(p,1)==10,"forecast preserves complementary stock without manufacturing future ingredients today");
  s["items"] = new JsonObject();p=DailyTradeOptimizer.Plan(synergy,s);
  Check(Rows(p["cooking"]).Length==0&&Rows(p["purchases"]).Length==0,"future supply never permits an executable action with absent inputs");

  s=State(synergy,O(("1",10),("2",10)));s["future_supply"]=Supply(synergy,O(("1",10L),("2",10L)));p=DailyTradeOptimizer.Plan(synergy,s);
  Check(Cooks(p,2)==10,"current complete profitable pair is used even when more pairs arrive later");
  var stagnant=Catalog([Item(1,10),Item(2,1),Item(3,1)],[Recipe(1,3,1,("1",1),("2",1))]);
  s=State(stagnant,O(("1",100)));s["future_supply"]=Supply(stagnant,O(("1",10L)));p=DailyTradeOptimizer.Plan(stagnant,s);
  Check(Holds(p,1)==0&&Rows(p["sales"]).Any(x=>N(x["item"])==1&&N(x["count"])==100),"equal-valued future raw resale cannot strand stock forever");
  s["protected_items"]=O(("1",40));p=DailyTradeOptimizer.Plan(stagnant,s);
  Check(Rows(p["sales"]).Single(x=>N(x["item"])==1)["count"]!.GetValue<long>()==60,"forecast and tie-break respect locked stock");
  s=State(c,O(("1",100),("2",150)));s["gold"]=0;s["potions"]=0;s["potion_buy_limit"]=0;p=DailyTradeOptimizer.Plan(c,s);
  Check(Rows(p["cooking"]).Length==0&&N(p["summary"]!["cash_required"])==0,"future revenue cannot pay for today's potions");
  s=State(c,O(("1",100),("2",150)));s["capacity_limits"]=O(("3",3));p=DailyTradeOptimizer.Plan(c,s);
  Check(Cooks(p,1)==3,"executable cooking honors observed output capacity");

  JsonObject Offer(long product,long item,long price,long limit)=>O(("shop",1),("product",product),("item",item),("base_price",price),("limit",limit));
  JsonObject Available(JsonObject catalog)
  {
   var state=State(catalog,new());state["bargain_active"]=true;
   foreach(var offer in Rows(state["offers"])){
    offer["remaining"]=N(Rows(catalog["offers"]).Single(o=>N(o["product"])==N(offer["product"]))["limit"]);
    offer["price"]=Copy(offer["bargain_price"]);
   }
   return state;
  }
  long Buys(JsonObject plan,long item)=>Rows(plan["purchases"]).Where(r=>N(r["item"])==item).Sum(r=>N(r["count"]));
  var achievement=O(("include_break_even_resales",true));
  Check(!B(DailyTradeOptimizer.Preferences()["include_break_even_resales"]),"break-even purchases are opt-in for existing accounts");
  Reject(()=>DailyTradeOptimizer.Preferences(O(("include_break_even_resales",1))),"break-even switch rejects non-boolean values");
  var flat=Catalog([Item(1,40),Item(2,39)],[],[Offer(1,1,100,10),Offer(2,2,100,10)]);
  s=Available(flat);p=DailyTradeOptimizer.Plan(flat,s);
  Check(Rows(p["purchases"]).Length==0,"default excludes arbitrary zero-profit purchase and resale ties");
  p=DailyTradeOptimizer.Plan(flat,s,achievement);
  Check(Buys(p,1)==10&&Buys(p,2)==0&&N(p["summary"]!["break_even_value"])==400,"opt-in adds equal-price goods but never even one gold of resale loss");
  Check(N(p["summary"]!["incremental_profit"])==0&&N(p["summary"]!["cash_required"])==400&&N(p["summary"]!["eventual_sale_value"])==400,"break-even turnover is not counted as profit or free cash");
  Check(Rows(p["sales"]).All(r=>!B(r["today_quote_confirmed"])&&S(r["date"])=="2026-11-01"),"achievement goods still wait for confirmed premium sale day");
  s["bargain_active"]=false;s["can_bargain"]=true;s["potions"]=0;s["gold"]=1000;
  foreach(var offer in Rows(s["offers"]))offer["price"]=100;
  p=DailyTradeOptimizer.Plan(flat,s,achievement);
  Check(Buys(p,1)==10&&B(p["bargain"]!["start"])&&N(p["summary"]!["incremental_profit"])==-450,"bargaining fee does not disqualify equal-price achievement goods");
  Check(N(p["summary"]!["potions_to_buy"])==10&&N(p["summary"]!["cash_required"])==850,"actual bargaining fee still reserves funds and appears in ledger");
  s["gold"]=449;p=DailyTradeOptimizer.Plan(flat,s,achievement);
  Check(Rows(p["purchases"]).Length==0&&!B(p["bargain"]!["start"]),"ignoring fee in strategy never spends unavailable potion money");
  var noFlat=Catalog([Item(1,41)],[],[Offer(1,1,100,1)]);
  var noFlatState=Available(noFlat);noFlatState["bargain_active"]=false;noFlatState["can_bargain"]=true;
  Rows(noFlatState["offers"])[0]["price"]=100;
  p=DailyTradeOptimizer.Plan(noFlat,noFlatState,achievement);
  Check(Rows(p["purchases"]).Length==0&&!B(p["bargain"]!["start"]),"achievement switch cannot waive bargaining fee for unrelated loss-making plans without equal-price extras");
  s=Available(flat);s["items"]=O(("1",8));s["capacity_limits"]=O(("1",10));s["protected_items"]=O(("1",7));
  p=DailyTradeOptimizer.Plan(flat,s,achievement);
  Check(Buys(p,1)==2&&N(p["summary"]!["break_even_count"])==2&&Rows(p["sales"]).Single()["count"]!.GetValue<long>()==3,"extra buying obeys real stack capacity and preserves protected inventory");
  s=Available(flat);p=DailyTradeOptimizer.Plan(flat,s,O(("include_break_even_resales",true),("reserve_gold",99999900L),("max_spend",80)));
  Check(Buys(p,1)==2&&N(p["summary"]!["cash_required"])==80,"extra turnover respects both account reserve and spending cap");

  var profitable=Catalog([Item(1,60),Item(2,40)],[],[Offer(1,1,100,2),Offer(2,2,100,10)]);
  s=Available(profitable);s["gold"]=2500;
  var normal=DailyTradeOptimizer.Plan(profitable,s);p=DailyTradeOptimizer.Plan(profitable,s,achievement);
  Check(Buys(p,1)==Buys(normal,1)&&Buys(p,2)==2&&p["solver"]!["forecast_profit"]!.GetValue<double>()==normal["solver"]!["forecast_profit"]!.GetValue<double>(),"only funds left after profitable current and future purchases are used for achievements");
  s["gold"]=80;p=DailyTradeOptimizer.Plan(profitable,s,achievement);
  Check(Buys(p,1)==2&&Buys(p,2)==0,"tight budget prioritizes profit over achievement turnover");

  var flatIngredient=Catalog([Item(1,40),Item(2,1),Item(3,200)],[Recipe(1,3,1,("1",1),("2",1))],[Offer(1,1,100,10)]);
  s=Available(flatIngredient);s["items"]=O(("2",1));normal=DailyTradeOptimizer.Plan(flatIngredient,s);
  p=DailyTradeOptimizer.Plan(flatIngredient,s,achievement);
  Check(Buys(normal,1)==1&&Cooks(normal,1)==1&&N(normal["summary"]!["break_even_count"])==0,"disabled option does not forbid equal-price ingredients needed by profitable cooking");
  Check(Buys(p,1)==10&&Cooks(p,1)==1&&N(p["summary"]!["break_even_count"])==9&&N(p["summary"]!["incremental_profit"])==N(normal["summary"]!["incremental_profit"]),"achievement counts only extra direct resales, preserving existing cooking allocations");
  string cached=Path.Combine(output,"break-even-plan.json");
  DailyTradePlan.Generate(flatIngredient,s,null,cached,"a");
  p=DailyTradePlan.Generate(flatIngredient,s,achievement,cached,"a");
  Check(!B(p["cache_reused"])&&N(p["summary"]!["break_even_count"])==9&&DailyTradePlan.Report(p).Contains("不分摊砍价费"),"strategy change invalidates plan cache and export explains fee exclusion");
  p=DailyTradePlan.Generate(flatIngredient,s,achievement,cached,"a");
  Check(B(p["cache_reused"]),"unchanged achievement plan can reuse verified optimum");
  var split=DailyTradeProof.Split(p,s);
  Check(Rows(split["full"]).Single()["count"]!.GetValue<long>()==10,"achievement goods enter the existing favorites purchase execution path");

  var knapsack=Catalog([Item(1,6),Item(2,10)],[],[Offer(1,1,15,3),Offer(2,2,25,3)]);
  for(int budget=0;budget<=32;budget++)
  {
   s=Available(knapsack);s["gold"]=budget;p=DailyTradeOptimizer.Plan(knapsack,s,achievement);
   int best=0;for(int a=0;a<=3;a++)for(int b=0;b<=3;b++)if(6*a+10*b<=budget)best=Math.Max(best,6*a+10*b);
   Check(N(p["summary"]!["break_even_value"])==best&&N(p["summary"]!["incremental_profit"])==0,$"integer extra turnover matches exhaustive budget allocation {budget}");
  }

  // Exhaustive independent oracle: no offers, no random supply; both recipes compete for material 1.
  var competing=Catalog([Item(1,8),Item(2,6),Item(3,90),Item(4,150)],[Recipe(1,3,1,("1",2)),Recipe(2,4,1,("1",3),("2",2))]);
  for(int a=0;a<=6;a++)for(int b=0;b<=3;b++){
   s=State(competing,O(("1",a),("2",b)));p=DailyTradeOptimizer.Plan(competing,s);
   long best=0;for(int x=0;x<=a/2;x++)for(int y=0;y<=b/2;y++)if(2*x+3*y<=a)best=Math.Max(best,x*(90-45-16)+y*(150-45-24-12));
   Check(N(p["summary"]!["incremental_profit"])==best,$"shared-resource optimum equals enumeration {a}/{b}");
  }
  s=State(c,new());s["future_supply"]=Supply(c,O(("1",-1L)));Reject(()=>DailyTradeOptimizer.Plan(c,s),"negative drop forecasts rejected");
  s["future_supply"]=Supply(c,O(("999",1L)));Reject(()=>DailyTradeOptimizer.Plan(c,s),"unknown drop ingredients rejected");
  s["future_supply"]=Supply(c,O(("1",1.5)));Reject(()=>DailyTradeOptimizer.Plan(c,s),"fractional executable-supply payload rejected");
  s["future_supply"]=Supply(c,O(("1",1L)));s["future_supply"]!["database_sha256"]="other";Reject(()=>DailyTradeOptimizer.Plan(c,s),"forecast from another game database rejected");
  var mixed=DailyLinearOptimizer.Solve([-1,-1],[1,1],[[1,1]],[0],[1.5],true,integerColumns:1)!;
  Check(mixed.Values[0]==Math.Round(mixed.Values[0])&&Math.Abs(mixed.Values.Sum()-1.5)<1e-8,"mixed forecast keeps actual actions integral while allowing expected future fractions");
  Reject(()=>DailyLinearOptimizer.Solve([1],[1],[],[],[],true,integerColumns:2),"invalid integer prefix rejected");

  var distribution=O(("draws",2),("all",false),("choices",Array([O(("weight",3),("child",O(("items",O(("1",4)))))),O(("weight",1),("child",O(("items",O(("2",8))))))])));
  var mean=DailyTradeDrops.Mean(distribution);Check(mean[1]==6&&mean[2]==4,"drop means normalize actual ratio sum and draw count");
  var rng=new Random(42);long v1=0,v2=0;for(int i=0;i<10000;i++){var sample=DailyTradeDrops.Sample(distribution,rng);v1+=sample.GetValueOrDefault(1);v2+=sample.GetValueOrDefault(2);}
  Check(Math.Abs(v1/10000d-6)<.12&&Math.Abs(v2/10000d-4)<.24,"paired Monte Carlo generator converges to independent weighted-draw model");
  var model=O(("schema",1),("database_sha256",c["database_sha256"]),("entries",Array([O(("pack",1),("map",2),("kind","pickup"),("id",1),("reward",distribution)),O(("pack",1),("map",0),("kind","monster"),("id",7),("reward",O(("items",O(("1",7))))))])));
  var forecast=DailyTradeDropForecast.Project(model,c,[(1,2,new HashSet<long>{7}),(1,2,new HashSet<long>{7})]);
  Check(N(forecast["items"]!["1"])==53&&N(forecast["items"]!["2"])==16,"weekly source projected once per room, not daily and not duplicated");
  Check(forecast["executable"]!.GetValue<bool>()==false,"drop estimate is explicitly non-executable");
  var monsters=DailyTradeDropForecast.ActiveMonsters(O(("Monsters","[{\"monsterId\":7,\"activeFlag\":true,\"respawnTime\":9999999},{\"monsterId\":8,\"activeFlag\":false}]")));
  Check(monsters.SetEquals([7L]),"forecast excludes inactive quest monsters but retains collected weekly spawns");
  var warm=DailyLinearOptimizer.Solve([-2,-1],[4,4],[[1,1]],[0],[3.5],true,integerColumns:1,initial:[1,1])!;
  Check(warm.Values[0]==3&&Math.Abs(warm.Values[1]-.5)<1e-6,"native warm start is refined and independently audited");
  Reject(()=>DailyLinearOptimizer.Solve([1],[1],[],[],[],true,initial:[double.NaN]),"invalid warm start rejected before native call");
  // A solved horizon may allocate eight of ten current ingredients to later raw resale.
  // Release that allocation even if every secondary MIP runs out of time.
  var flow = new DailyTradeForecast(competing, State(competing,O(("1",10))), 4);
  var vector = new double[flow.Count]; var costs = new double[flow.Count];
  var resourceRows = new List<double[]>(); var lows = new List<double>(); var caps = new List<double>();
  flow.Add(Enumerable.Repeat(DailyLinearOptimizer.Infinity,flow.Count).ToArray(),costs,resourceRows,lows,caps,new double[flow.Count],100000000,4);
  vector[4]=10; vector[flow.Start]=1; vector[flow.Count-4]=8; vector[flow.Count-2]=1;
  double Value()=>costs.Zip(vector,(a,b)=>a*b).Sum()-8*vector[0];
  double beforeValue=Value(); var beforeRows=resourceRows.Select(r=>r.Zip(vector,(a,b)=>a*b).Sum()).ToArray();
  flow.ReleaseFutureResales(vector,4,0);
  Check(vector[4]==2&&vector[0]==8&&vector[flow.Start]==1,"only recipe-needed stock remains after eliminating future raw resale");
  Check(Math.Abs(Value()-beforeValue)<1e-8&&resourceRows.Select(r=>r.Zip(vector,(a,b)=>a*b).Sum()).SequenceEqual(beforeRows),"direct realization preserves both horizon profit and every resource constraint");
  var once=vector.ToArray();flow.ReleaseFutureResales(vector,4,0);
  Check(vector.SequenceEqual(once),"direct realization is idempotent across repeated planning");
  return Task.CompletedTask;
 }
}

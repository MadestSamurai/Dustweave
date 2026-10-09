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

using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;

// Crafting and replenishment share one integer resource model. Priority is:
// feasible craft count, minimum rice, minimum potions, then material scarcity.
public static class EquipmentSupplyPlanner
{
 public const string Wood="501";
 public static JsonObject WithWoodReserve(JsonObject settings){
  var copy=Copy(settings);var reserve=O(copy["material_reserves"]);long wood=Integer(copy["wood_reserve"]??JsonValue.Create(0),"wood reserve");
  reserve[Wood]=Math.Max(N(reserve[Wood]),wood);copy["material_reserves"]=reserve.DeepClone();return copy;
 }
 public static long AvailableRice(JsonObject stock,long reserve){
  if(stock["rice"] is not JsonObject rice)throw new EquipmentSupplyError("equipment.supply.riceUnavailable");
  long free=Integer(rice["free"],"free rice"),bonus=Integer(rice["bonus"],"stored rice");
  // The reserve protects the total balance, including any unused free rice.
  return Math.Max(0,checked(free+bonus)-reserve);
 }
 public static JsonObject Plan(JsonObject stock,JsonObject settings){
  settings=WithWoodReserve(settings);long reserve=Integer(settings["rice_reserve"]??JsonValue.Create(0),"rice reserve");long rice=AvailableRice(stock,reserve);
  int level=(int)Integer(settings["level"]??JsonValue.Create(7),"level",1,9);string lev=level.ToString();var reserves=O(settings["material_reserves"]);
  var recipes=A(EquipmentPlanner.Catalog["recipes"]).Where(r=>B(settings["include_five"])||N(r["potions"])==3).ToArray();
  var routes=A(settings["supply_routes"]).Where(r=>B(r["available"])&&B(r["fixed"])&&N(r["ap"])>0&&(N(r["wood"])>0||N(r["gold"])>0)).ToArray();
  if(recipes.Length==0)throw new InvalidOperationException("No supported crafting recipes.");
  foreach(var r in routes){Integer(r["ap"],"rice per sweep",1,100000);Integer(r["wood"],"wood yield");Integer(r["gold"],"gold yield");}
  int n=recipes.Length,m=routes.Length;double[] Row(Func<JsonNode,double> craft,Func<JsonNode,double>? hunt=null)=>recipes.Select(craft).Concat(routes.Select(r=>hunt?.Invoke(r)??0)).Append(0d).ToArray();
  var rows=new List<double[]>();var caps=new List<double>();void Add(double[] row,double cap){rows.Add(row);caps.Add(cap);}void Fix(double[] row,long value){Add(row,value);Add(row.Select(x=>-x).ToArray(),-value);}
  var keys=recipes.SelectMany(r=>O(r["materials"]).Select(p=>p.Key)).Distinct().ToArray();
  foreach(string k in keys)Add(Row(r=>N(r["materials"]?[k]),r=>k==Wood?-N(r["wood"]):0),Math.Max(0,N(stock["materials"]?[k])-N(reserves[k])));
  long potion=Math.Min(N(stock["potions"]),Integer(settings["potion_budget"]??stock["potions"],"potion budget"));
  Add(Row(r=>N(r["potions"])),potion);
  Add(Row(r=>N(r["gold"]![lev]),r=>-N(r["gold"])),N(stock["gold"]));
  if(settings["gold_budget"]!=null)Add(Row(r=>N(r["gold"]![lev])),Integer(settings["gold_budget"],"gold budget"));
  Add(Row(_=>0,r=>N(r["ap"])),rice);
  long limit=Integer(settings["count_limit"]??JsonValue.Create(potion/recipes.Min(r=>N(r["potions"]))),"craft limit",0,2000000000);
  var countRow=Row(_=>1);Add(countRow,limit);
  // A binary indicator requires replenishing a below-floor wood balance only
  // when actually crafting with wood. Ore recipes remain feasible without it.
  long shortage=Math.Max(0,N(reserves[Wood])-N(stock["materials"]?[Wood]));
  var binary=Row(_=>0);binary[^1]=1;Add(binary,1);
  if(shortage>0){int wi=System.Array.IndexOf(keys,Wood);if(wi>=0){rows[wi][^1]=shortage;var used=Row(r=>N(r["materials"]?[Wood]));used[^1]=-Math.Max(1,checked(limit*recipes.Max(r=>N(r["materials"]?[Wood]))));Add(used,0);}}
  long[] x=IntegerOptimizer.Solve(rows,caps,countRow,true);
  long count=x.Take(n).Sum();if(count==0)return ExistingOnly(stock,settings);Fix(countRow,count);
  var riceRow=Row(_=>0,r=>N(r["ap"]));x=IntegerOptimizer.Solve(rows,caps,riceRow.Select(v=>-v).ToArray(),true);long cost=(long)riceRow.Select((v,i)=>v*x[i]).Sum();Fix(riceRow,cost);
  var potionRow=Row(r=>N(r["potions"]));x=IntegerOptimizer.Solve(rows,caps,potionRow.Select(v=>-v).ToArray(),true);Fix(potionRow,(long)potionRow.Select((v,i)=>v*x[i]).Sum());
  var scarcity=Row(r=>-O(r["materials"]).Sum(p=>(double)N(p.Value)/Math.Max(1,N(stock["materials"]?[p.Key])-N(reserves[p.Key]))));x=IntegerOptimizer.Solve(rows,caps,scarcity,false);
  var sweeps=new JsonArray();for(int i=0;i<m;i++)if(x[n+i]>0){var r=Copy(routes[i]);r["count"]=x[n+i];sweeps.Add(r);}
  var projected=Copy(stock);projected["gold"]=checked(N(stock["gold"])+A(sweeps).Sum(r=>checked(N(r["gold"])*N(r["count"]))));projected["materials"]![Wood]=checked(N(stock["materials"]?[Wood])+A(sweeps).Sum(r=>checked(N(r["wood"])*N(r["count"]))));
  var actualSettings=Copy(settings);actualSettings["supply_enabled"]=false;actualSettings["count_limit"]=count;
  var plan=EquipmentPlanner.Powder(projected,actualSettings);plan["stock"]=Copy(stock);plan["projected_stock"]=projected;
  plan["supply"]=new JsonObject{["routes"]=sweeps,["rice"]=cost,["free_rice"]=Math.Min(N(stock["rice"]?["free"]),cost),["bonus_rice"]=Math.Max(0,cost-N(stock["rice"]?["free"])),["reserve"]=reserve,["wood_reserve"]=N(reserves[Wood]),["settings"]=actualSettings};
  return Unavailable(plan,settings);
 }
 static JsonObject Unavailable(JsonObject plan,JsonObject settings){plan["supply_unavailable"]=Array(A(settings["supply_routes"]).Where(r=>!B(r["available"])).Select(r=>r["table"]));return EquipmentPlanner.Sign(plan);}
 static JsonObject ExistingOnly(JsonObject stock,JsonObject settings){var copy=Copy(settings);copy["supply_enabled"]=false;return Unavailable(EquipmentPlanner.Powder(stock,copy),settings);}
 public static JsonObject AfterSupply(JsonObject plan,JsonObject stock){
  var settings=Copy(plan["supply"]!["settings"]!);settings["supply_enabled"]=false;settings["count_limit"]=N(plan["count"]);settings["gold_budget"]=N(plan["gold"]);settings["potion_budget"]=N(plan["potions"]);
  return EquipmentPlanner.Powder(stock,settings);
 }
}

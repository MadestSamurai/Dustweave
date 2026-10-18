using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public static class EquipmentPlanner
{
 public static JsonObject Catalog {get; private set;}=Resource("catalog.json");
 internal static void Activate(JsonObject catalog)=>Catalog=catalog;
 public static long Rank(JsonNode catalog,JsonNode gear){var values=catalog["rank_values"]![S(catalog["equipment"]![S(gear["equipment_id"])]!["rank_group"])]!;var ranks=A(gear["ranks"]).ToArray();if(ranks.Length!=3)throw new InvalidOperationException("装备精炼数据不完整");return ranks.Select((r,i)=>{long v=Integer(r,"精炼档位",0,4);return v==0?0:N(values[i]![(int)v-1]);}).Sum();}
 public static JsonObject Refine(JsonObject stock,JsonObject settings){
  string mode=S(settings["batch_mode"],"target");if(mode is not "target" and not "higher")throw new InvalidOperationException("批量精炼模式无效");
  long batch=Integer(settings["batch_size"]??JsonValue.Create(5000),"单批精炼次数",1,5000),target=Integer(settings["target"],"目标等级",1,24),gold=Integer(settings["gold_budget"],"金币预算"),powder=Integer(settings["powder_budget"],"粉预算");
  var instances=A(settings["instances"]).Select(v=>S(v)).ToArray();if(instances.Length==0||instances.Distinct().Count()!=instances.Length)throw new InvalidOperationException("请选择装备，且不可重复");var rows=new JsonArray();
  foreach(var id in instances){var item=A(stock["equipment"]).SingleOrDefault(g=>S(g["instance"])==id);if(item==null||N(item["level"])!=9)throw new InvalidOperationException("所选装备不存在或未强化至+9");var row=Copy(item);var definition=Catalog["equipment"]![S(item["equipment_id"])]!;row["name"]=Clone(definition["name"]);row["score"]=Rank(Catalog,item);row["cost"]=Clone(Catalog["refine_cost"]![S(definition["grade"])]);rows.Add(row);}
  var plan=Base(stock,"refine");plan["schema"]=2;plan["batch_mode"]=mode;plan["batch_size"]=batch;plan["native_target"]=mode=="higher"?24:target;plan["target"]=target;plan["rows"]=rows;plan["gold"]=Math.Min(N(stock["gold"]),gold);plan["powder"]=Math.Min(N(stock["materials"]?["10"]),powder);return Sign(plan);
 }
 static JsonObject Base(JsonObject stock,string kind)=>new(){["kind"]=kind,["schema"]=1,["account"]=Clone(stock["account"]),["player"]=Clone(stock["player"]),["catalog"]=Hash(Catalog),["stock"]=Copy(stock)};
 static JsonObject Sign(JsonObject plan){plan["id"]=Hash(plan);return plan;}
 public static void Validate(JsonObject plan){var unsigned=Copy(plan);unsigned.Remove("id");if(S(plan["id"])!=Hash(unsigned)||S(plan["catalog"])!=Hash(Catalog))throw new InvalidOperationException("计划已变化，请重新计算并确认");}
 public static JsonObject Powder(JsonObject stock,JsonObject settings){
  long level=Integer(settings["level"]??JsonValue.Create(7),"强化等级",1,9);string lev=level.ToString();bool five=B(settings["include_five"]);
  long gold=Math.Min(Integer(stock["gold"],"金币库存"),Integer(settings["gold_budget"]??stock["gold"],"金币预算"));long potions=Math.Min(Integer(stock["potions"],"药库存"),Integer(settings["potion_budget"]??stock["potions"],"天赋药预算"));
  var reserves=O(settings["material_reserves"]);foreach(var r in reserves)Integer(r.Value,"保留材料");
  var all=A(Catalog["recipes"]).Where(r=>five||N(r["potions"])==3).ToArray();
  // Remove exact duplicates and recipes strictly dominated in every consumed resource.
  bool Dominates(JsonNode a,JsonNode b)=>N(a["potions"])<=N(b["potions"])&&N(a["gold"]![lev])<=N(b["gold"]![lev])&&N(a["powder"]![lev])>=N(b["powder"]![lev])&&O(a["materials"]).All(p=>N(p.Value)<=N(b["materials"]?[p.Key]));
  var recipes=all.Where((b,i)=>!all.Where((a,j)=>j!=i&&Dominates(a,b)&&(!Dominates(b,a)||j<i)).Any()).ToArray();
  var keys=recipes.SelectMany(r=>O(r["materials"]).Select(p=>p.Key)).Distinct().Order().ToArray();
  var rows=keys.Select(k=>recipes.Select(r=>(double)N(r["materials"]?[k])).ToArray()).ToList();var caps=keys.Select(k=>(double)Math.Max(0,Integer(stock["materials"]?[k]??JsonValue.Create(0),"材料库存")-N(reserves[k]))).ToList();
  rows.Add(recipes.Select(r=>(double)N(r["potions"])).ToArray());caps.Add(potions);rows.Add(recipes.Select(r=>(double)N(r["gold"]![lev])).ToArray());caps.Add(gold);
  if(settings["count_limit"]!=null){rows.Add(recipes.Select(_=>1d).ToArray());caps.Add(Integer(settings["count_limit"],"制作上限"));}
  // All current N recipes yield the same powder per item. Enforce this invariant.
  if(recipes.Select(r=>N(r["powder"]![lev])).Distinct().Count()!=1)throw new InvalidOperationException("配方产粉规则已变化，请更新规划器。");
  long[] x=IntegerOptimizer.Solve(rows,caps,recipes.Select(_=>1d).ToArray(),true);long count=x.Sum();
  void Fix(double[] row,double value){rows.Add(row);caps.Add(value);rows.Add(row.Select(v=>-v).ToArray());caps.Add(-value);}
  Fix(recipes.Select(_=>1d).ToArray(),count);
  x=IntegerOptimizer.Solve(rows,caps,recipes.Select(r=>-(double)N(r["potions"])).ToArray(),true);long drug=x.Select((v,i)=>v*N(recipes[i]["potions"])).Sum();Fix(recipes.Select(r=>(double)N(r["potions"])).ToArray(),drug);
  // Same count and potions: consume the lowest proportion of scarce materials.
  var objective=recipes.Select(r=>-O(r["materials"]).Sum(p=>(double)N(p.Value)/Math.Max(1,N(stock["materials"]?[p.Key])-N(reserves[p.Key])))*100000).ToArray();
  double scale=objective.Max(Math.Abs);if(scale>0)objective=objective.Select(v=>v/scale*1000).ToArray();
  x=IntegerOptimizer.Solve(rows,caps,objective,false);
  var batches=new JsonArray();for(int i=0;i<recipes.Length;i++)if(x[i]>0){var r=recipes[i];batches.Add(new JsonObject{["recipe"]=Clone(r["id"]),["name"]=Clone(r["name"]),["count"]=x[i],["potions"]=N(r["potions"])*x[i],["gold"]=N(r["gold"]![lev])*x[i],["powder"]=N(r["powder"]![lev])*x[i]});}
  var materials=new JsonObject();foreach(var k in keys){long use=x.Select((v,i)=>v*N(recipes[i]["materials"]?[k])).Sum();if(use>0)materials[k]=use;}
  var plan=Base(stock,"powder");plan["level"]=level;plan["include_five"]=five;plan["rows"]=batches;plan["materials"]=materials;plan["count"]=x.Sum();foreach(var key in new[]{"gold","potions","powder"})plan[key]=A(batches).Sum(r=>N(r[key]));return Sign(plan);
 }
 public static JsonObject Normalize(JsonObject evidence){
  JsonArray Counted(string name){var items=LiveEvidence.Reading(evidence,name,"$items")!.AsArray();if(items.Count!=N(LiveEvidence.Reading(evidence,name,"Count")))throw new InvalidOperationException("库存读取不完整");return items;}
  var resources=A(Counted("trade.inventory")).Where(r=>S(r["Key"])=="Resource").ToArray();if(resources.Length!=1)throw new InvalidOperationException("材料库存缺失");var material=new JsonObject();var keep=new JsonObject();var group=resources[0];var items=A(group["Value.Values"]).ToArray();if(items.Length!=N(group["Value.Count"]))throw new InvalidOperationException("材料堆叠读取不完整");var seen=new HashSet<string>();
  foreach(var item in items){string key=S(item["id"]);long count=Integer(item["count"]??JsonValue.Create(0),"材料数量");if(!seen.Add(S(item["invenIndex"])))throw new InvalidOperationException("重复库存实例");if(N(item["expiryTime"])!=0)continue;var target=B(item["keepFlag"])?keep:material;target[key]=N(target[key])+count;}
  var gear=new JsonArray();foreach(var r in A(Counted("policy.equipment")))gear.Add(new JsonObject{["instance"]=S(r["InvenIndex"]),["equipment_id"]=Clone(r["BaseInfo.Id"]),["level"]=Clone(r["BaseInfo.Level"]),["ranks"]=Clone(r["BaseInfo.Rank"]),["locked"]=B(r["LockFlag"]),["kept"]=B(r["KeepFlag"]),["equipped"]=N(r["UseChar"])!=0});
  return new(){["account"]=Clone(evidence["Frame"]!["AccountKey"]),["player"]=Clone(evidence["Frame"]!["PlayerKey"]),["gold"]=Integer(LiveEvidence.Reading(evidence,"trade.currency","Gold"),"金币"),["potions"]=Integer(LiveEvidence.Reading(evidence,"trade.currency","Catalyst"),"天赋药"),["materials"]=material,["protected"]=keep,["equipment"]=Array(A(gear).OrderBy(g=>S(g["instance"]),StringComparer.Ordinal))};
 }
}

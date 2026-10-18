using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public static class LiveEvidence
{
 public const string RefineAuto="ὢὮὫὪὩὨὥὥὬὪὪ";
 public static JsonNode? Reading(JsonNode evidence,string name,string path){var rows=A(evidence["Readings"]).Where(r=>S(r["Id"])==name).ToArray();if(rows.Length!=1||S(rows[0]["Error"])!="")throw new InvalidOperationException("无法读取："+name);var values=A(rows[0]["Values"]).Where(v=>S(v["Path"])==path).ToArray();if(values.Length!=1||S(values[0]["Error"])!="")throw new InvalidOperationException("无法读取："+name+"."+path);return JsonNode.Parse(S(values[0]["Json"]));}
 public static string Actor(JsonNode f)=>Canonical(Select(f,"ProcessId","ProcessStartTicks","Instance","AccountKey","PlayerKey"));
 public static JsonObject Values(JsonNode e){var result=new JsonObject();foreach(var v in A(e["Values"])){if(S(v["Error"])!="")throw new InvalidOperationException("回执字段读取失败");result[S(v["Path"])]=JsonNode.Parse(S(v["Json"]));}return result;}
 public static JsonArray Responses(string role,JsonArray events,JsonObject before,JsonObject after,int requested=1){
  var selected=A(events).Where(e=>S(e["Role"])==role).OrderBy(e=>N(e["Sequence"])).ToArray();var req=selected.Where(e=>S(e["Kind"])=="request").ToArray();var res=selected.Where(e=>S(e["Kind"])=="response").ToArray();
  int max=role=="equipment.refine_batch"?(requested+999)/1000:1;
  if(req.Length<1||req.Length>max||req.Length!=res.Length)throw new InvalidOperationException("操作回执尚未收齐");
  if(res.Any(e=>S(e["Error"])!=""||N(e["ErrorCode"])!=0||!B(e["Accepted"])))throw new InvalidOperationException("原生操作返回错误，请核对记录");
  if(req.Where((r,i)=>N(r["Sequence"])>=N(res[i]["Sequence"])).Any()||selected.Any(e=>Actor(e["Frame"]!)!=Actor(before["Frame"]!))||Actor(after["Frame"]!)!=Actor(before["Frame"]!))throw new InvalidOperationException("账号或回执顺序不符");
  if(!Equal(before["Config"],after["Config"]))throw new InvalidOperationException("操作期间观察配置改变");
  if(role=="equipment.refine_batch"&&Reading(after,"equipment.refine_ui",RefineAuto)?.GetValue<bool>()!=false)throw new InvalidOperationException("原生批量精炼仍在进行");
  return Array(res.Select(Values));
 }
 static JsonObject Clean(JsonObject value){var result=Copy(value);var m=O(result["materials"]);foreach(var key in m.Where(p=>N(p.Value)==0).Select(p=>p.Key).ToArray())m.Remove(key);result["equipment"]=Array(A(result["equipment"]).OrderBy(g=>S(g["instance"]),StringComparer.Ordinal));return result;}
 public static JsonObject VerifyPowder(JsonNode recipe,int count,int level,JsonObject old,JsonObject data,JsonObject current){
  var expected=Copy(old);string lev=level.ToString();long gold=N(recipe["gold"]![lev])*count,drug=N(recipe["potions"])*count,powder=N(recipe["powder"]![lev])*count;expected["gold"]=N(expected["gold"])-gold;expected["potions"]=N(expected["potions"])-drug;
  foreach(var p in O(recipe["materials"]))expected["materials"]![p.Key]=N(expected["materials"]![p.Key])-N(p.Value)*count;expected["materials"]!["10"]=N(expected["materials"]!["10"])+powder;
  if(N(data["UpgradeUsedGold"])!=gold||A(data["LackItemInfo"]).Any())throw new InvalidOperationException("原生费用或材料回执不符");if(!Equal(Clean(expected),Clean(current)))throw new InvalidOperationException("制作分解的库存变化尚未与计划匹配");
  return new(){["recipe"]=Clone(recipe["id"]),["count"]=count,["gold"]=gold,["potions"]=drug,["powder"]=powder};
 }
 public static long BatchCount(JsonObject plan,JsonObject used,JsonNode cost)=>Math.Max(0,Math.Min(N(plan["batch_size"]),Math.Min((N(plan["gold"])-N(used["gold"]))/N(cost["gold"]),(N(plan["powder"])-N(used["powder"]))/N(cost["powder"]))));
 public static JsonObject VerifyRefine(string instance,JsonNode cost,int requested,int nativeTarget,JsonObject old,JsonArray packets,JsonObject current){
  if(packets.Count==0)throw new InvalidOperationException("缺少原生批量精炼回执");var expected=Copy(old);long gold=0,powder=0,count=0;var gear=A(expected["equipment"]).Single(g=>S(g["instance"])==instance);
  foreach(var data in A(packets)){
   long n=Integer(data["TryCount"],"实际精炼次数",0,Math.Min(1000,requested-count));count+=n;if(n==0&&!ReferenceEquals(data,packets.Last()))throw new InvalidOperationException("零次精炼后出现额外回执");long g=0,p=0;
   foreach(var item in A(data["ConsumeItemInfo"])){long amount=Integer(item["count"]??JsonValue.Create(0),"实际消耗");if(N(item["type"])==4)g+=amount;else if(N(item["id"])==10)p+=amount;else throw new InvalidOperationException("精炼消耗了计划外物品");}
   if(g!=N(cost["gold"])*n||p!=N(cost["powder"])*n)throw new InvalidOperationException("精炼次数与实际扣款不符");gold+=g;powder+=p;
   var info=data["EquipInfo"];if(info!=null){if(S(info["invenIndex"])!=instance)throw new InvalidOperationException("精炼回执不是选定装备");gear["ranks"]=Clone(info["baseInfo"]!["rank"]);EquipmentPlanner.Rank(EquipmentPlanner.Catalog,gear);}else if(n!=0)throw new InvalidOperationException("精炼缺少装备结果");
  }
  expected["gold"]=N(expected["gold"])-gold;expected["materials"]!["10"]=N(expected["materials"]!["10"])-powder;
  if(!Equal(Clean(expected),Clean(current)))throw new InvalidOperationException("批量精炼扣款或装备变化尚未与回执匹配");
  return new(){["instance"]=instance,["gold"]=gold,["powder"]=powder,["score"]=EquipmentPlanner.Rank(EquipmentPlanner.Catalog,gear),["attempts"]=count,["requested"]=requested,["native_target"]=nativeTarget,["batches"]=1,["packets"]=packets.Count,["native_result"]=Clone(packets.Last()?["ResultType"]),["lack_items"]=Clone(packets.Last()?["LackItemInfo"])};
 }
 public static JsonObject Progress(JsonObject journal){var result=new JsonObject();foreach(var k in new[]{"count","gold","potions","powder","attempts","batches"})result[k]=A(journal["operations"]).Sum(o=>N(o["result"]?[k]));return result;}
}

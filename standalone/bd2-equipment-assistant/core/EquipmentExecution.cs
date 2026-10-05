using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public static class EquipmentExecution
{
 static JsonObject CanonicalStock(JsonObject stock){var result=Copy(stock);result["equipment"]=Array(A(stock["equipment"]).OrderBy(g=>S(g["instance"]),StringComparer.Ordinal));return result;}
 static JsonObject Guard(JsonObject plan,JsonObject expected,IEquipmentGame game){LiveEquipment.Stopped();var stock=game.Stock();if(!Equal(CanonicalStock(stock),CanonicalStock(expected)))throw new InvalidOperationException("账号或库存与上批结果不同，请重新读取并计算");if(S(stock["account"])!=S(plan["account"])||S(stock["player"])!=S(plan["player"]))throw new InvalidOperationException("账号已切换");return stock;}
 public static JsonObject? Resumable(JsonObject stock){
  Directory.CreateDirectory(Data);foreach(var file in Directory.EnumerateFiles(Data,"*.json").OrderByDescending(File.GetLastWriteTimeUtc))try{
   var journal=Read(file);var plan=journal["plan"]?.AsObject();if(plan==null||S(plan["kind"])!="powder"||S(journal["state"])=="completed"||!A(journal["operations"]).Any())continue;
   if(journal["pending"]!=null&&S(journal["pending"]!["state"])!="rejected")continue;if(journal["expected"]==null||!Equal(CanonicalStock(journal["expected"]!.AsObject()),CanonicalStock(stock)))continue;
   EquipmentPlanner.Validate(plan);var remaining=Copy(plan);remaining["stock"]=Copy(stock);var recipes=A(EquipmentPlanner.Catalog["recipes"]).ToDictionary(r=>N(r["id"]));
   foreach(var op in A(journal["operations"])){var result=op["result"]!;var row=A(remaining["rows"]).Single(r=>N(r["recipe"])==N(result["recipe"]));foreach(var key in new[]{"count","gold","potions","powder"}){remaining[key]=N(remaining[key])-N(result[key]);row[key]=N(row[key])-N(result[key]);}foreach(var p in O(recipes[N(result["recipe"])]["materials"]))remaining["materials"]![p.Key]=N(remaining["materials"]![p.Key])-N(p.Value)*N(result["count"]);}
   remaining["rows"]=Array(A(remaining["rows"]).Where(r=>N(r["count"])>0));var materials=O(remaining["materials"]);foreach(var key in materials.Where(p=>N(p.Value)==0).Select(p=>p.Key).ToArray())materials.Remove(key);
   if(N(remaining["count"])>0)return new(){["plan"]=Copy(plan),["display"]=remaining,["completed"]=LiveEvidence.Progress(journal)};
  }catch(IOException){}catch(InvalidOperationException){}catch(KeyNotFoundException){}catch(System.Text.Json.JsonException){}
  return null;
 }
 static void Presentation(JsonObject journal,IEquipmentGame game,string role,bool waitForResult=true){
  string path=Path.Combine(Data,S(journal["id"])+".json");
  journal["presentation"]=new JsonObject{["state"]="pending",["role"]=role};Write(path,journal);
  if(File.Exists(Stop))return;
  try{game.CompletePresentation(role,waitForResult);journal["presentation"]!["state"]="completed";Write(path,journal);}
  catch(Exception e){journal["presentation"]!["error"]=e.Message;Write(path,journal);throw;}
 }
 public static JsonObject Execute(JsonObject plan,string approved,IEquipmentGame game,Action<JsonObject>? progress=null){
  EquipmentPlanner.Validate(plan);if(approved!=S(plan["id"]))throw new InvalidOperationException("必须确认当前计划才可执行");bool powder=S(plan["kind"])=="powder";
  if(!powder){string mode=S(plan["batch_mode"]);if(N(plan["schema"])!=2||mode is not "target" and not "higher")throw new InvalidOperationException("请重新计算批量精炼计划并确认模式");Integer(plan["batch_size"],"单批次数",1,5000);Integer(plan["target"],"目标等级",1,24);if(N(plan["native_target"])!=(mode=="higher"?24:N(plan["target"])))throw new InvalidOperationException("原生目标与计划模式不符");}
  Directory.CreateDirectory(Data);string path=Path.Combine(Data,S(plan["id"])+".json");foreach(var file in Directory.EnumerateFiles(Data,"*.json")){if(file==path)continue;var previous=Read(file);if(S(previous["plan"]?["account"])==S(plan["account"])&&S(previous["pending"]?["state"]) is "dispatching" or "unknown")throw new InvalidOperationException("此账号有尚未核实的消耗记录，请先核对："+Path.GetFileName(file));}
  JsonObject journal;if(File.Exists(path)){journal=Read(path);if(journal["pending"]!=null&&S(journal["pending"]!["state"])!="rejected")throw new InvalidOperationException("上次操作结果未知，请先核对账本；不会重复消耗");if(S(journal["state"])=="completed")return journal;}
  else journal=new(){["id"]=Clone(plan["id"]),["kind"]=Clone(plan["kind"]),["plan"]=Copy(plan),["operations"]=new JsonArray(),["pending"]=null,["expected"]=Clone(plan["stock"]),["state"]="ready"};
  Guard(plan,journal["expected"]!.AsObject(),game);using var actionScope=game.EnableActions();
  if(S(journal["presentation"]?["state"])=="pending")Presentation(journal,game,S(journal["presentation"]?["role"]),false);
  if(powder){var recipes=A(EquipmentPlanner.Catalog["recipes"]).ToDictionary(r=>N(r["id"]));foreach(var row in A(plan["rows"])){long made=A(journal["operations"]).Where(o=>N(o["result"]?["recipe"])==N(row["recipe"])).Sum(o=>N(o["result"]?["count"]));while(made<N(row["count"])){
   Guard(plan,journal["expected"]!.AsObject(),game);int count=(int)Math.Min(A(journal["operations"]).Any()?N(row["count"])-made:10,N(row["count"])-made);var recipe=recipes[N(row["recipe"])];int level=(int)N(plan["level"]);count=game.PowderPreview(recipe,count,level);Guard(plan,journal["expected"]!.AsObject(),game);
   var result=game.Transaction(journal,"powder.make_break",new(){["ui"]="EquipmentUpgradePopupUI",["field"]="_buttonAccept",["reason"]="执行用户已确认的N装制作强化分解预算"},(old,packets,current)=>LiveEvidence.VerifyPowder(recipe,count,level,old,packets[0]!.AsObject(),current));made+=N(result["count"]);progress?.Invoke(LiveEvidence.Progress(journal));Presentation(journal,game,"powder.make_break");
  }}}
  else foreach(var row in A(plan["rows"]))while(true){var stock=Guard(plan,journal["expected"]!.AsObject(),game);string instance=S(row["instance"]);var gear=A(stock["equipment"]).Single(g=>S(g["instance"])==instance);if(EquipmentPlanner.Rank(EquipmentPlanner.Catalog,gear)>=N(plan["target"]))break;
   var cost=row["cost"]!;int count=(int)LiveEvidence.BatchCount(plan,LiveEvidence.Progress(journal),cost);if(count<1){journal["state"]="budget_exhausted";Write(path,journal);return journal;}game.RefinePreview(instance);Guard(plan,journal["expected"]!.AsObject(),game);int native=(int)N(plan["native_target"]);
   var result=game.Transaction(journal,"equipment.refine_batch",new(){["ui"]="EquipmentUpgradeUI",["operation"]="equipment_refine_batch",["value"]=native,["items"]=new JsonArray(long.Parse(instance),count,N(cost["gold"])*count,N(cost["powder"])*count),["reason"]="执行已确认预算内的原生批量精炼"},(old,packets,current)=>LiveEvidence.VerifyRefine(instance,cost,count,native,old,packets,current));progress?.Invoke(LiveEvidence.Progress(journal));Presentation(journal,game,"equipment.refine_batch");
   if(N(result["score"])<N(plan["target"])&&(N(result["attempts"])<count||A(result["lack_items"]).Any())){journal["state"]="native_stopped";journal["stop_reason"]=Clone(result["native_result"]);Write(path,journal);return journal;}
  }
  journal["state"]="completed";Write(path,journal);if(A(journal["operations"]).Any()&&!File.Exists(Stop))game.Cleanup(powder?"EquipmentMakingUI":"EquipmentUpgradeUI");return journal;
 }
}
public static class EquipmentService
{
 public static JsonObject Handle(JsonObject job,Func<string[],string>? connection=null,Action<JsonObject>? progress=null){
  string op=S(job["operation"]);JsonObject result;
  if(connection!=null&&op is not "capture" and not "self_test")LiveEquipment.ActivateCurrent();
  switch(op){
   case "capture":LiveEquipment.Prepare(connection??throw new InvalidOperationException("缺少连接入口"));using(var game=new LiveEquipment(true))return game.Capture();
   case "execute":using(var game=new LiveEquipment()){game.RefreshCatalog();return EquipmentExecution.Execute(job["plan"]!.AsObject(),S(job["approved_id"]),game,progress);}
   case "gear_views":return new(){["gear"]=GearView.Build(job["stock"]!.AsObject(),job["details"]?.AsObject())};
   case "powder_plan":result=EquipmentPlanner.Powder(job["stock"]!.AsObject(),O(job["settings"]));break;
   case "refine_plan":result=EquipmentPlanner.Refine(job["stock"]!.AsObject(),O(job["settings"]));break;
   case "self_test":
    var stock=new JsonObject{["account"]="fixture",["player"]="fixture",["gold"]=10000,["potions"]=30,["materials"]=new JsonObject{["201"]=30,["204"]=30},["protected"]=new JsonObject(),["equipment"]=new JsonArray()};var plan=EquipmentPlanner.Powder(stock,new(){["count_limit"]=10});if(N(plan["count"])!=10||N(plan["gold"])!=2900||N(plan["powder"])!=100)throw new InvalidOperationException("规划自检失败");return new(){["status"]="passed",["count"]=10,["engine"]="C# / in-process",["realGameTouched"]=false};
   default:throw new InvalidOperationException("不支持的操作");
  }
  EquipmentPlanner.Validate(result);Write(Path.Combine(Data,"plan-"+S(result["id"])+".json"),result);return result;
 }
}

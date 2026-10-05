using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;

// Result presentation is a separate phase from a verified resource transaction.
// Decisions use the observed stack, never a fixed sleep after a server response.
public sealed record EquipmentUiAction(string Ui,string? Field=null,bool Back=false,string? Expect=null,string? Absent=null);
public static class EquipmentUiFlow {
 public static readonly string[] Results={"EquipmentBatchUpgradeResultPopupUI","EquipmentUpgradeResultPopupUI","ItemGetPopupUI"};
 static readonly string[] Navigable={"EquipmentUpgradeUI","EquipmentMakingUI","EquipmentMakingSelectUI","InventoryManageUI","MailUI"};
 public static bool Has(JsonObject frame,string type)=>A(frame["Surfaces"]).Any(u=>S(u["Type"])==type);
 public static EquipmentUiAction Dismiss(string type)=>type switch {
  "ItemGetPopupUI"=>new(type,"_objBackButton",Absent:type),
  "EquipmentUpgradeResultPopupUI"=>new(type,"_objOKButton",Absent:type),
  _=>new(type,Back:true,Absent:type)
 };
 public static string? DismissibleBlocker(JsonObject frame,string destination){
  var blockers=LiveEquipment.Blockers(frame,destination).ToHashSet();
  return A(frame["Surfaces"]).Where(u=>blockers.Contains(S(u["Type"]))&&(Results.Contains(S(u["Type"]))||S(u["Type"])=="NewsPopupEventUI")&&LiveEquipment.Blockers(frame,S(u["Type"])).Length==0)
   .OrderByDescending(u=>N(u["Order"])).Select(u=>S(u["Type"])).FirstOrDefault();
 }
 public static void Cleanup(string destination,string? expectedResult,Func<JsonObject> observe,Action<EquipmentUiAction> send,Action guard,Action wait,Func<double> seconds,double timeout=45){
  string Identity(JsonObject f)=>Canonical(Select(f,"ProcessId","ProcessStartTicks","Instance","AccountKey","PlayerKey","Scene"));
  var bound=Identity(observe());double deadline=seconds()+timeout;bool seen=expectedResult==null;
  while(seconds()<deadline){
   guard();var f=observe();if(Identity(f)!=bound)throw new StepRejected("收尾期间账号或场景改变，已保存结算记录");
   var surfaces=A(f["Surfaces"]).ToArray();if(expectedResult!=null&&Has(f,expectedResult))seen=true;
   var result=surfaces.Where(u=>Results.Contains(S(u["Type"]))||S(u["Type"])=="NewsPopupEventUI"||seen&&S(u["Type"])=="EquipmentUpgradePopupUI")
    .Where(u=>S(u["Type"])!=destination&&LiveEquipment.Blockers(f,S(u["Type"])).Length==0).OrderByDescending(u=>N(u["Order"])).FirstOrDefault();
   if(result!=null){if(B(result["InputReady"]))send(Dismiss(S(result["Type"])));else wait();continue;}
   // Do not cancel the in-flight preview or navigate away before its result exists.
   if(!seen){wait();continue;}
   var blockers=LiveEquipment.Blockers(f,destination);
   var unknown=blockers.Where(t=>!Results.Contains(t)&&t!="NewsPopupEventUI"&&t!="EquipmentUpgradePopupUI"&&!Navigable.Contains(t)).ToArray();
   if(unknown.Length>0)throw new StepRejected("结果已结算，需处理弹窗："+string.Join(", ",unknown));
   if(surfaces.Count(u=>S(u["Type"])==destination)==1&&surfaces.Any(u=>S(u["Type"])==destination&&B(u["InputReady"]))&&blockers.Length==0)return;
   if(destination=="MenuUI"&&Has(f,"GameFieldDefaultUI")&&!Has(f,"MenuUI")&&LiveEquipment.Blockers(f,"GameFieldDefaultUI").Length==0){send(new("GameFieldDefaultUI","_buttonMenu",Expect:"MenuUI"));continue;}
   var next=surfaces.Where(u=>S(u["Type"])!=destination&&Navigable.Contains(S(u["Type"]))&&B(u["InputReady"])&&LiveEquipment.Blockers(f,S(u["Type"])).Length==0).OrderByDescending(u=>N(u["Order"])).FirstOrDefault();
   if(next!=null)send(new(S(next["Type"]),Back:true,Absent:S(next["Type"])));else wait();
  }
  throw new StepRejected(seen?"已结算，结果界面尚未完成收尾；不会重做消费":"已结算，仍未观察到原生结果界面；不会重做消费");
 }
}

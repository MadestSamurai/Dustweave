#nullable disable
using System;
using System.Linq;
using System.Runtime.Serialization;
namespace BD2Equipment.Live {
 public static class LiveProtocol {
  public const int BridgeVersion=64;
  public const string LiveEntries="runtime.json|snapshot.json|error.json|attached.json|performance.json|control.json|command.json|pause|legacy-observation|evidence-config.json|observation-request.json|evidence.json|catalog-request.json|catalog.json|catalog-part.json|events.json|receipts~*";
  public static bool Ready(Frame f,int processId,long startTicks,long now,string previousInstance=null){
   return f!=null&&f.Protocol==1&&f.BridgeVersion==BridgeVersion&&f.ProcessId==processId&&f.ProcessStartTicks==startTicks&&f.AtUtcTicks>now-TimeSpan.FromSeconds(3).Ticks&&f.AtUtcTicks<=now+TimeSpan.FromSeconds(2).Ticks&&!string.IsNullOrEmpty(f.Instance)&&string.IsNullOrEmpty(f.Error)&&(previousInstance==null||f.Instance!=previousInstance);
  }
 }
 [DataContract] public sealed class Surface {
  [DataMember] public int Id;
  [DataMember] public string Type="",Path="";
  [DataMember] public bool Popup;
  [DataMember] public bool InputReady=true;
  [DataMember] public int Order;
  [DataMember] public Target[] Targets=new Target[0];
  [DataMember] public string[] Text=new string[0];
 }
 [DataContract] public sealed class Target {
  [DataMember] public int Id;
  [DataMember] public string Field="",Path="",Route="ui";
  [DataMember] public bool Enabled;
 }
 [DataContract] public sealed class Frame {
  [DataMember] public int Protocol=1,BridgeVersion=LiveProtocol.BridgeVersion,ProcessId;
  [DataMember] public long ProcessStartTicks,AtUtcTicks,Sequence;
  [DataMember] public string Instance="",Scene="",AccountKey="",PlayerKey="",UiToken="",Error="";
  [DataMember] public Surface[] Surfaces=new Surface[0];
 }
 [DataContract] public sealed class Command {
  [DataMember] public string Scope="gameplay",Id="",Kind="",Instance="",AccountKey="",PlayerKey="",Scene="",UiToken="",Reason="";
  [DataMember] public int ProcessId,SurfaceId,TargetId,Value;
  [DataMember] public long[] Items=new long[0];
  [DataMember] public long ProcessStartTicks,ExpiresUtcTicks,ObservedUtcTicks;
 }
 [DataContract] public sealed class Receipt {
  [DataMember] public Command Command;
  [DataMember] public string State="prepared",Error="";
  [DataMember] public bool MayHaveDispatched;
  [DataMember] public long AtUtcTicks;
  [DataMember] public Frame Before,After;
 }

 public static class LivePolicy {
  public static bool GameplayReady(Frame f){return f!=null&&string.IsNullOrEmpty(f.Error)&&!string.IsNullOrEmpty(f.AccountKey)&&!string.IsNullOrEmpty(f.PlayerKey)&&f.Scene!="ReGame"&&!f.Surfaces.Any(u=>u.Type=="DownloadPopupUI"||u.Type=="IntroUI");}
  public static bool PowderKind(string k){return k=="powder_recipe"||k=="powder_options";}
  public static string Gate(Command c,Frame f,long now,bool otherOwner){
   Guid id;if(c==null||c.Id==null||c.Id.Length!=32||!Guid.TryParseExact(c.Id,"N",out id))return "invalid_command_id";
   if(!new[]{"click","pointer","back","detach","powder_recipe","powder_options","equipment_craft_menu","equipment_refine_preview","equipment_refine_batch"}.Contains(c.Kind))return "unsupported_command";
   if(c.Scope!="gameplay")return "unsupported_scope";
   if(!GameplayReady(f)||f.Protocol!=1)return "observation_unavailable";
   if(c.ExpiresUtcTicks<=now||c.ExpiresUtcTicks>now+TimeSpan.FromSeconds(15).Ticks||c.ObservedUtcTicks<now-TimeSpan.FromSeconds(15).Ticks||c.ObservedUtcTicks>now+TimeSpan.FromSeconds(2).Ticks)return "expired_observation";
   if(f.AtUtcTicks<now-TimeSpan.FromSeconds(3).Ticks||f.AtUtcTicks>now+TimeSpan.FromSeconds(2).Ticks)return "stale_frame";
   if(c.ProcessId!=f.ProcessId||c.ProcessStartTicks!=f.ProcessStartTicks||string.IsNullOrEmpty(c.Instance)||c.Instance!=f.Instance)return "process_changed";
   if(c.AccountKey!=f.AccountKey||c.PlayerKey!=f.PlayerKey)return "identity_changed";
   if(c.Scene!=f.Scene||c.UiToken!=f.UiToken)return "screen_changed";
   if(otherOwner)return "another_operation_owns_game";
   if(c.Kind=="detach")return "";
   var ui=f.Surfaces.SingleOrDefault(u=>u.Id==c.SurfaceId);if(ui==null)return "surface_missing";
   if(!ui.InputReady)return "ui_not_ready";
   if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!new[]{"NoticeUI","CharNoticeUI","DetailNoticeUI","CurrencyManageUI","OverheadManageUI"}.Contains(x.Type)&&!ui.Path.StartsWith(x.Path+"/")&&!(ui.Type=="ItemGetPopupUI"&&x.Type=="EquipmentInfoPopupUI"&&x.Path.StartsWith(ui.Path+"/"))))return "foreground_popup";
   if(c.Kind=="equipment_refine_batch"){
    if(c.TargetId!=0||ui.Type!="EquipmentUpgradeUI")return "refine_batch_context_rejected";
    return c.Value>=1&&c.Value<=24&&c.Items!=null&&c.Items.Length==4&&c.Items.All(x=>x>0)&&c.Items[1]<=5000&&c.Items[2]<=2000000000&&c.Items[3]<=2000000000?"":"refine_batch_budget_rejected";
   }
   if(PowderKind(c.Kind)){
    if(c.TargetId!=0||c.Items==null||ui.Type!=(c.Kind=="powder_recipe"?"EquipmentMakingSelectUI":"EquipmentUpgradePopupUI"))return "powder_surface_rejected";
    if(c.Kind=="powder_recipe")return c.Value>=1&&c.Value<=int.MaxValue&&c.Items.Length==0?"":"powder_recipe_rejected";
    return c.Value>=1&&c.Value<=9&&c.Items.Length==1&&c.Items[0]>=1&&c.Items[0]<=int.MaxValue?"":"powder_options_rejected";
   }
   if(c.Kind=="equipment_craft_menu")return ui.Type=="MenuUI"&&c.TargetId==0?"":"preview_surface_rejected";
   if(c.Kind=="equipment_refine_preview")return ui.Type=="InventoryManageUI"&&c.TargetId==0&&c.Items!=null&&c.Items.Length==1&&c.Items[0]>0?"":"refine_selection_rejected";
   string[] allowed={"MenuUI","GameFieldDefaultUI","EquipmentMakingUI","EquipmentMakingSelectUI","EquipmentUpgradePopupUI","EquipmentUpgradeUI","InventoryManageUI","EquipmentBatchUpgradeResultPopupUI","EquipmentUpgradeResultPopupUI","NewsPopupEventUI","ItemGetPopupUI","MailUI"};
   if(!allowed.Contains(ui.Type))return "equipment_surface_required";
   if((c.Kind=="click"||c.Kind=="pointer")&&!ui.Targets.Any(t=>t.Id==c.TargetId&&t.Enabled&&(c.Kind=="pointer"?t.Route=="pointer":t.Route=="ui")))return "target_unavailable";
   return "";
  }
 }
}
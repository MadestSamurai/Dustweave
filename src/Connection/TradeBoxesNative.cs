using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Proto.Net;
namespace BD2Daily.Live {
 [DataContract] internal sealed class TradeBoxItem {
  [DataMember] public long Instance;
  [DataMember] public int Id,Type,Keep,Count;
 }
 [DataContract] internal sealed class TradeBoxState {
  [DataMember] public long Revision;
  [DataMember] public bool Pending,Busy,Full;
  [DataMember] public TradeBoxItem[] Boxes;
 }
 internal static class TradeBoxesNative {
  static long revision;static bool pending;
  static EquipmentForgeSelector Selector(InventoryManageUI ui){
   return (EquipmentForgeSelector)typeof(InventoryManageUI).GetField("_equipmentForgeSelector",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ui);
  }
  static bool ActiveBatch(){
   return Bridge.Find(typeof(InventoryManageUI)).Cast<InventoryManageUI>().Any(ui=>{
    var selector=Selector(ui);
    if(selector==null)return false;
    var progress=(EquipmentBatchProcessUI)typeof(EquipmentForgeSelector).GetField("_equipBatchProcessUI",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(selector);
    return progress!=null&&progress.gameObject.activeInHierarchy;
   });
  }
  static ItemDBInfo[] Items(){
   var all=(IDictionary)typeof(ὣὡὧὡὦὣὣὬὨὪὫ).GetField("ὧὥὤὯὩὤὬὯὦὤὮ",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
   var boxes=all[(ὥὯὯὠὣὪὦὢὫὠὮ)9] as IDictionary;
   return boxes==null?new ItemDBInfo[0]:boxes.Values.Cast<ItemDBInfo>().ToArray();
  }
  internal static TradeBoxState Capture(){
   var network=ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ;
   bool busy=network.ὭὬὠὫὯὭὮὭὥὦὭ;
   return new TradeBoxState{Revision=revision,Pending=pending||ActiveBatch(),Busy=busy,
    Full=ὣὡὧὡὦὣὣὬὨὪὫ.ὬὯὧὣὪὢὡὬὬὯὦ.InvenSlot<=ὣὡὧὡὦὣὣὬὨὪὫ.ὨὤὪὨὡὫὡὨὬὧὨ.Count,
    Boxes=Items().Select(x=>new TradeBoxItem{Instance=x.InvenIndex,Id=x.Id,Type=x.Type,Keep=x.KeepFlag,Count=x.Count}).ToArray()};
  }
  internal static void Open(Command c){
   var state=Capture();
   if(!Bridge.DailyActionsActive||state.Busy||state.Pending||state.Full)throw new InvalidOperationException("tradeboxes.not_ready");
   var inventory=Items().ToDictionary(x=>x.InvenIndex);
   var selected=new System.Collections.Generic.List<ItemDBInfo>();
   for(int i=0;i<c.Items.Length;i+=3){
    ItemDBInfo item;
    // Validate the complete batch before changing selection or opening anything.
    if(!inventory.TryGetValue(c.Items[i],out item)||item.Id!=c.Items[i+1]||
     !TradeBoxRules.Allowed(item.Type,item.KeepFlag,item.Count,checked((int)c.Items[i+2]))||selected.Contains(item))
     throw new InvalidOperationException("tradeboxes.selection_changed");
    selected.Add(item);
   }
   var ui=Bridge.Find(typeof(InventoryManageUI)).Cast<InventoryManageUI>().Single();
   var selector=Selector(ui);
   ui.ClearBoxItemDataInBatch();
   for(int i=0;i<selected.Count;i++)ui.AddBoxItemDataInBatch(selected[i].InvenIndex,checked((int)c.Items[i*3+2]));
   long next=++revision;pending=true;
   // One native batch, with the game's progress UI and reward settlement.
   // Never substitute a host-side request/click loop per box.
   try{selector.OpenBoxItem(()=>{if(revision==next)pending=false;ui.ClearBoxItemDataInBatch();ui.RefreshViewBoxDetail(false);});}
   catch{pending=false;throw;}
  }
 }
}

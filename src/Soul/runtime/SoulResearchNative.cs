using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Proto.Design.common;
using Proto.Net;
using UnityEngine;
namespace BD2Daily.Live {
 internal static class SoulResearchNative {
  static readonly Assembly Client=typeof(InventoryBattleManager).Assembly;
  static readonly BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
  static object Read(string name){return Client.GetType(SoulResearchBindings.Owner,true).GetProperty(name,Static).GetValue(null,null);}
  static IbPlayDBInfo Play {get{return (IbPlayDBInfo)Read(SoulResearchBindings.Play);}}
  static List<IbItemDBInfo> Items {get{return (List<IbItemDBInfo>)Read(SoulResearchBindings.Items);}}
  static List<IbShopItemDBInfo> Shop {get{return (List<IbShopItemDBInfo>)Read(SoulResearchBindings.Shop);}}
  static void Call(int token,params object[] args){Client.ManifestModule.ResolveMethod(token).Invoke(null,args);}
  static InventoryBattleManager Manager {get{return UnityEngine.Object.FindObjectsOfType<InventoryBattleManager>().Single();}}
  internal static bool ResultReady(UIBase ui){
   if(!ui.gameObject.activeInHierarchy)return false;
   var fields=ui.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
   var response=fields.SingleOrDefault(f=>f.FieldType==typeof(IbStageEndResponse));
   return response!=null&&response.GetValue(ui)!=null;
  }
  internal static bool Kind(string kind){return kind=="soul_open"||kind=="soul_views"||kind=="soul_confirm_reset"||kind=="soul_confirm_enter"||kind=="soul_buy"||kind=="soul_upgrade"||kind=="soul_save"||kind=="soul_sale_sync"||kind=="soul_trace"||kind=="soul_enter"||kind=="soul_refresh"||kind=="soul_sell";}
  [DataContract] internal sealed class ViewInfo {
   [DataMember]public int ViewId,ItemId,Level,ShopSlot;
   [DataMember]public long InventoryIndex;
   [DataMember]public string State,Owner;
  }
  [DataContract] internal sealed class ProgressInfo {
   [DataMember]public int RegularSeason,ActiveDungeon,ActiveStage,SelectedDungeon,RefreshPrice;
   [DataMember]public int ActiveShopViews,ActiveItemViews,DeckRebuilds,ShopRebuilds;
   [DataMember]public bool ShopReady;
   [DataMember]public ViewInfo[] Views;
   [DataMember]public int[] ClearedDungeons,SeasonDungeons,WaveCounts;
   [DataMember]public string EnterState;
  }
  internal static ProgressInfo Progress(){
   var lobby=UnityEngine.Object.FindObjectsOfType<InventoryBattleLobbyUI>().SingleOrDefault(x=>x.gameObject.activeInHierarchy);
   var result=new ProgressInfo{RegularSeason=Convert.ToInt32(Read(SoulResearchBindings.Season)),ActiveDungeon=Play.DungeonId,ActiveStage=Play.StageId,
    ClearedDungeons=((List<int>)Read(SoulResearchBindings.Cleared)).ToArray(),SeasonDungeons=new int[0],WaveCounts=new int[0],EnterState="",RefreshPrice=Convert.ToInt32(Get(SoulResearchBindings.RefreshPrice)),DeckRebuilds=deckRebuilds,ShopRebuilds=shopRebuilds};
   var views=UnityEngine.Object.FindObjectsOfType<IBItemBaseView>().Where(x=>x.gameObject.activeInHierarchy).ToArray();
   var shops=UnityEngine.Object.FindObjectsOfType<InventoryBattleShopUI>().Where(x=>x.gameObject.activeInHierarchy).ToArray();
   var mapped=shops.SelectMany(ShopViews).ToArray();
   var managers=UnityEngine.Object.FindObjectsOfType<InventoryBattleManager>();
   var owned=managers.SelectMany(m=>(Dictionary<IbItemDBInfo,IBItemBaseView>)m.GetType().GetField("ὡὠὪὢὥὦὨὡὦὭὦ",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(m)).Select(x=>x.Value).ToArray();
   result.ActiveItemViews=views.Length;result.ActiveShopViews=mapped.Count(x=>x.Value!=null&&x.Value.gameObject.activeInHierarchy);
   result.ShopReady=shops.Length==1&&ShopMatches(shops[0]);
   result.Views=views.Select(v=>{
    var offer=mapped.Where(x=>x.Value==v).Select(x=>x.Key).FirstOrDefault();
    var item=ViewItem(v);
    return new ViewInfo{ViewId=v.GetInstanceID(),ItemId=item==null?0:item.Id,Level=item==null?0:item.Level,InventoryIndex=item==null?0:item.InvenIndex,
     ShopSlot=offer==null?-1:offer.Slot,Owner=offer!=null?"shop":owned.Contains(v)?"owned":"other",State=typeof(IBItemBaseView).GetMethod("get_CurrentState").Invoke(v,null).ToString()};
   }).ToArray();
   if(lobby!=null){
    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    var rows=(List<IBDungeonTable>)lobby.GetType().GetProperty(SoulResearchBindings.LobbyDungeons,flags).GetValue(lobby,null);
    result.SeasonDungeons=rows.Select(x=>x.Id).ToArray();
    result.WaveCounts=rows.Select(x=>((List<IBStageTable>)Get(SoulResearchBindings.StageRows,x.StageGroupId)).Count).ToArray();
    result.SelectedDungeon=Convert.ToInt32(lobby.GetType().GetProperty(SoulResearchBindings.LobbySelected,flags).GetValue(lobby,null));
    var fields=lobby.GetType().GetField("_dungeonInfoFields",flags).GetValue(lobby);
    result.EnterState=fields.GetType().GetProperty("EnterButtonState",flags).GetValue(fields,null).ToString();
   }return result;
  }
  [DataContract] internal sealed class Capability {
   [DataMember]public long InventoryIndex;[DataMember]public int ItemId,Level,MaximumLevel,NextPrice,CostumeLevel;[DataMember]public string Reason;
  }
  static object Get(int token,params object[] args){return Client.ManifestModule.ResolveMethod(token).Invoke(null,args);}
  static Capability CapabilityFor(int id,int level,long index){
   var row=new Capability{InventoryIndex=index,ItemId=id,Level=level,NextPrice=-1,Reason="not_upgradeable"};
   var table=(IBItemTable)Get(SoulResearchBindings.ItemTable,id);
   if(table==null)return row;
   if((int)table.ItemType==1){
    var levels=(List<IBUnitItemTable>)Get(SoulResearchBindings.UnitLevels,table.TypeTableId);
    row.MaximumLevel=levels.Max(x=>x.Id)-1;row.CostumeLevel=row.MaximumLevel;
    if(table.BaseTypeItemId!=0){var method=Client.ManifestModule.ResolveMethod(SoulResearchBindings.Costume);var args=method.GetParameters();var costume=method.Invoke(null,new object[]{table.BaseTypeItemId,Enum.ToObject(args[1].ParameterType,0),0L});row.CostumeLevel=costume==null?0:Convert.ToInt32(costume.GetType().GetProperty("Level").GetValue(costume,null));row.MaximumLevel=Math.Min(row.MaximumLevel,row.CostumeLevel);}
    var next=levels.SingleOrDefault(x=>x.Id==level+2);if(next!=null)row.NextPrice=next.UpgradePrice;
   }else if((int)table.ItemType==2){var levels=(List<IBEquipItemTable>)Get(SoulResearchBindings.EquipLevels,table.TypeTableId);row.MaximumLevel=levels.Max(x=>x.Id)-1;var next=levels.SingleOrDefault(x=>x.Id==level+2);if(next!=null)row.NextPrice=next.UpgradePrice;}else return row;
   row.Reason=level>=row.MaximumLevel?"level_limit":row.NextPrice<0?"missing_next_level":"available";return row;
  }
  internal static Capability[] Capabilities(){
   var current=Items.Select(x=>CapabilityFor(x.Id,x.Level,x.InvenIndex)).Concat(Shop.Where(x=>!x.IsSoldOut).Select(x=>CapabilityFor(x.Id,0,0))).ToArray();
   var known=new HashSet<int>(current.Select(x=>x.ItemId));
   // Growth targets need the account's real caps before a unit is offered.
   // This is a read of native costume data, not ownership of a shop item.
   return current.Concat(SoulResearchBindings.UnitItemIds.Where(id=>!known.Contains(id)).Select(id=>CapabilityFor(id,0,0))).ToArray();
  }
  internal static string Validate(Command c,Frame f,Surface ui){
   if(c.Kind=="soul_open")return LivePolicy.GameplayReady(f)&&ui.Type=="PackListUI"&&c.TargetId==0&&c.Value==3012&&!f.Surfaces.Any(x=>x.Id!=ui.Id&&x.Popup&&!LivePolicy.PassiveSurface(x.Type)&&x.Order>=ui.Order)?"":"soul_cartridge_unavailable";
   if(!LivePolicy.GameplayReady(f)||ui.Type!=(c.Kind=="soul_confirm_enter"||c.Kind=="soul_confirm_reset"?"MessagePopupUI":c.Kind=="soul_enter"?"InventoryBattleLobbyUI":"InventoryBattleStageUI")||c.TargetId!=0||c.Items==null||c.Items.Length<4)return "soul_context_rejected";
   if(f.Surfaces.Any(x=>x.Id!=ui.Id&&x.Popup&&!LivePolicy.PassiveSurface(x.Type)&&x.Order>=ui.Order))return "soul_popup_blocked";
   if(c.Items.Take(4).Any(v=>v<0||v>int.MaxValue))return "soul_state_rejected";
   if(c.Kind=="soul_enter"||c.Kind=="soul_confirm_enter")return c.Items.Length==4&&c.Value>0?"":"soul_enter_rejected";
   if(c.Kind=="soul_confirm_reset")return c.Items.Length==5&&c.Value==0?"":"soul_reset_rejected";
   if(c.Kind=="soul_refresh")return c.Items.Length==6&&c.Value==0?"":"soul_refresh_rejected";
   if(c.Kind=="soul_sell")return c.Items.Length==8&&c.Value==0?"":"soul_sell_rejected";
   if(c.Kind=="soul_sale_sync")return c.Items.Length==6&&c.Value==0&&c.Items[4]>0&&c.Items[5]>=0&&c.Items[5]<=int.MaxValue?"":"soul_sale_sync_rejected";
   if(c.Kind=="soul_buy")return c.Items.Length==7&&c.Value>=0&&c.Value<4&&c.Items[4]>0&&c.Items[5]>=0&&c.Items[6]>=0?"":"soul_buy_rejected";
   if(c.Kind=="soul_upgrade")return c.Items.Length==8&&c.Value>=0&&c.Value<=5?"":"soul_upgrade_rejected";
   if(c.Kind=="soul_save")return c.Value==0&&c.Items.Length>4&&(c.Items.Length-4)%3==0?"":"soul_deck_rejected";
   return c.Items.Length==4&&c.Value==0?"":"soul_read_rejected";
  }
  static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
  static int deckRebuilds,shopRebuilds;
  static IbItemDBInfo ViewItem(IBItemBaseView view){return (IbItemDBInfo)typeof(IBItemBaseView).GetMethod("get_ItemData").Invoke(view,null);}
  static bool ViewSold(IBItemBaseView view){return (bool)typeof(IBItemBaseView).GetMethod("get_IsSoldOut").Invoke(view,null);}
  static Dictionary<IbShopItemDBInfo,IBItemBaseView> ShopViews(InventoryBattleShopUI shop){return (Dictionary<IbShopItemDBInfo,IBItemBaseView>)shop.GetType().GetField("ὣὨὫὮὥὧὤὭὤὭὯ",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(shop);}
  static bool ShopMatches(InventoryBattleShopUI shop){
   var mapped=ShopViews(shop);return mapped.Count==Shop.Count&&Shop.All(s=>mapped.Any(v=>v.Key.Slot==s.Slot&&v.Key.Id==s.Id&&v.Key.IsSoldOut==s.IsSoldOut&&v.Value!=null&&ViewItem(v.Value)!=null&&ViewItem(v.Value).Id==s.Id&&ViewSold(v.Value)==s.IsSoldOut));
  }
  static void MarkBought(InventoryBattleShopUI shop,IbShopItemDBInfo offer){
   // ShowShopItemSoldOut assumes drag-to-buy already moved the original view
   // into storage. Direct native purchases keep it here: reuse that exact view.
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var views=ShopViews(shop);var pairs=views.ToArray();
   var data=(Dictionary<IbShopItemDBInfo,IbItemDBInfo>)shop.GetType().GetField("ὢὨὤὨὪὥὫὥὤὬὪ",flags).GetValue(shop);
   var dataPairs=data.ToArray();
   views.Clear();data.Clear();offer.IsSoldOut=true;
   foreach(var pair in pairs){if(pair.Key.Slot==offer.Slot){pair.Key.IsSoldOut=true;pair.Value.SetAffordable(false);pair.Value.SetSoldOut(true);}views.Add(pair.Key,pair.Value);}
   foreach(var pair in dataPairs){if(pair.Key.Slot==offer.Slot)pair.Key.IsSoldOut=true;data.Add(pair.Key,pair.Value);}
  }
  static void ReclaimOrphanShopViews(){
   Require(!Play.IsStageEnter,"Cannot reconcile views during battle");
   var shops=UnityEngine.Object.FindObjectsOfType<InventoryBattleShopUI>().Where(x=>x.gameObject.activeInHierarchy).ToArray();
   Require(shops.Length==1&&ShopMatches(shops[0]),"Shop has not finished native loading");
   var retained=new HashSet<IBItemBaseView>(ShopViews(shops[0]).Values);
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var pool=(ItemViewPool)shops[0].GetType().GetField("ὫὨὪὣὬὠὩὫὯὨὮ",flags).GetValue(shops[0]);
   var active=(HashSet<IBItemBaseView>)pool.GetType().GetField("ὥὫὣὧὦὭὫὨὣὪὭ",flags).GetValue(pool);
   foreach(var view in active.ToArray()){
    var item=ViewItem(view);
    if(view.gameObject.activeInHierarchy&&!retained.Contains(view)&&item!=null&&item.InvenIndex<0&&typeof(IBItemBaseView).GetMethod("get_CurrentState").Invoke(view,null).ToString()=="Shop")pool.Return(view);
   }
  }
  static void SyncGold(){Manager.SyncGold(Play.Coin);}
  static IBItemBaseView OwnedView(long index){return ((Dictionary<IbItemDBInfo,IBItemBaseView>)Manager.GetType().GetField("ὡὠὪὢὥὦὨὡὦὭὦ",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Manager)).Where(x=>x.Key.InvenIndex==index).Select(x=>x.Value).SingleOrDefault();}
  static void ShowPurchase(IbItemDBInfo item,int cost,int coin){
   var manager=Manager;
   var storage=(StorageSystemPhysics)manager.GetType().GetField("_storageSystem",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
   Require(storage!=null&&OwnedView(item.InvenIndex)==null,"Purchased item is already displayed or storage is unavailable");
   var drop=(Vector3)typeof(StorageSystemPhysics).GetMethod("get_AreaDropZone").Invoke(storage,null);
   manager.CreateStorageItem(item,drop,false);
   foreach(var stage in UnityEngine.Object.FindObjectsOfType<InventoryBattleStageUI>())if(stage.gameObject.activeInHierarchy)stage.PlayBuyAnimation(cost,coin);
  }
  static void SyncShop(){
   SyncGold();
   var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
   foreach(var shop in UnityEngine.Object.FindObjectsOfType<InventoryBattleShopUI>())if(shop.gameObject.activeInHierarchy){
    if(ShopMatches(shop))continue;
    // Repaint already-received data once. LoadShopItems issues another native
    // read and clears the pool before the response, so it is not a repaint API.
    shop.GetType().GetMethod("ὫὫὥὢὤὮὤὦὮὯὫ",flags).Invoke(shop,null);
    shop.GetType().GetMethod("ὦὩὨὡὣὨὨὭὦὨὯ",flags).Invoke(shop,null);
    shopRebuilds++;
   }
  }
  static void SyncDeck(){
   SyncGold();Manager.LoadDeckForStage();deckRebuilds++;
  }
  internal static void Execute(Command c,UIBase ui){
   Require(Client.ManifestModule.ModuleVersionId.ToString()==SoulResearchBindings.Mvid,"Soul client changed");
   if(c.Kind=="soul_open"){
    var pack=(PackTable)Get(SoulResearchBindings.PackRow,3012);
    Require(pack!=null&&pack.Id==3012&&pack.PackType==14&&Get(SoulResearchBindings.OwnedPack,3012)!=null,"Soul cartridge is not owned or available");
    PackListLoopScrollItem.StartPackSelection(pack);return;
   }
   var p=Play;
   if(c.Kind=="soul_confirm_reset"){
    Require(p.Season==c.Items[0]&&p.DungeonId==c.Items[1]&&p.StageId==c.Items[2]&&p.Coin==c.Items[3]&&p.Life==c.Items[4]&&p.DungeonId>0&&!p.IsStageEnter,"Active dungeon changed; no reset sent");
    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    var callbacks=ui.GetType().GetFields(flags).Where(f=>typeof(Delegate).IsAssignableFrom(f.FieldType)).Select(f=>f.GetValue(ui) as Delegate).Where(d=>d!=null).SelectMany(d=>d.GetInvocationList());
    Require(callbacks.Any(d=>d.Target is InventoryBattleLobbyUI&&d.Method.Name=="ὬὦὭὩὯὫὬὤὮὮὥ"),"This is not the audited active-dungeon reset confirmation");
    var button=(UnityEngine.UI.Button)ui.GetType().GetField("_buttonOK",flags).GetValue(ui);
    Require(button.gameObject.activeInHierarchy&&button.interactable,"Reset confirmation unavailable");ui.OnClickUI(button.gameObject);return;
   }
   if(c.Kind=="soul_confirm_enter"){
    var progress=Progress();Require(progress.RegularSeason==c.Items[0]&&p.DungeonId==0&&c.Items[1]==0&&p.StageId==c.Items[2]&&p.Coin==c.Items[3]&&progress.SelectedDungeon==c.Value&&progress.SeasonDungeons.Contains(c.Value),"Rechallenge identity changed");
    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    var callbacks=ui.GetType().GetFields(flags).Where(f=>typeof(Delegate).IsAssignableFrom(f.FieldType)).Select(f=>f.GetValue(ui) as Delegate).Where(d=>d!=null).SelectMany(d=>d.GetInvocationList()).ToArray();
    Require(callbacks.Any(d=>d.Target is InventoryBattleLobbyUI&&d.Method.Name=="ὤὬὦὠὭὧὧὯὣὠὤ"),"This is not the audited new rechallenge confirmation");
    var button=(UnityEngine.UI.Button)ui.GetType().GetField("_buttonOK",flags).GetValue(ui);
    Require(button.gameObject.activeInHierarchy&&button.interactable,"Rechallenge confirmation unavailable");ui.OnClickUI(button.gameObject);return;
   }
   if(c.Kind=="soul_enter"){
    var progress=Progress();Require(progress.RegularSeason==c.Items[0]&&p.DungeonId==c.Items[1]&&p.StageId==c.Items[2]&&p.Coin==c.Items[3],"Soul campaign changed; no entry sent");
    var target=p.DungeonId>0?p.DungeonId:c.Value;
    Require(target>0&&c.Value==target&&progress.SeasonDungeons.Contains(target),"Dungeon changed or is outside the current season");
    var lobby=(InventoryBattleLobbyUI)ui;lobby.SelectDungeonInfo(target,true);progress=Progress();
    Require(progress.SelectedDungeon==target&&(progress.EnterState=="Challenge"||progress.EnterState=="OnGoing"||progress.EnterState=="Rechallenge"),"Dungeon is not natively enterable; no request sent");
    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    var fields=lobby.GetType().GetField("_dungeonInfoFields",flags).GetValue(lobby);
    var button=(UnityEngine.UI.Button)fields.GetType().GetField("_btnEnterStage",flags).GetValue(fields);
    Require(button.gameObject.activeInHierarchy&&button.interactable,"Dungeon enter button unavailable");
    lobby.OnClickUI(button.gameObject);return;
   }
   Require(p!=null&&p.Season==c.Items[0]&&p.DungeonId==c.Items[1]&&p.StageId==c.Items[2]&&p.Coin==c.Items[3],"Soul progress changed; no request sent");
   if(c.Kind=="soul_views"){ReclaimOrphanShopViews();return;}
   if(c.Kind=="soul_trace"){SoulResearchTrace.Start();return;}
   if(c.Kind=="soul_sale_sync"){
    // The native sell response removes the item and updates CurrentGold,
    // but does not update IbPlayDBInfo.Coin. Match that accepted receipt to
    // native state instead of reopening main/item/deck/shop network reads.
    Require(!p.IsStageEnter&&!Items.Any(x=>x.InvenIndex==c.Items[4]),"Sold item still exists or battle started");
    int coins=Manager.GetCurrentGold();
    Require(coins==c.Items[5],"Native sale wallet differs from accepted receipt");
    p.Coin=coins;return;
   }
   if(c.Kind=="soul_sell"){
    Require(!p.IsStageEnter,"Cannot sell during battle");
    var item=Items.SingleOrDefault(x=>x.InvenIndex==c.Items[4]);
    Require(item!=null&&item.Id==c.Items[5]&&item.Level==c.Items[6],"Sell item changed");
    Require(!((List<IbDeckDBInfo>)Read(SoulResearchBindings.Deck)).Any(x=>x.InvenIndex==item.InvenIndex),"Sell requires confirmed removal from current deck");
    var table=(IBItemTable)Get(SoulResearchBindings.ItemTable,item.Id);
    Require((item.Type==1||item.Type==2)&&(table.ItemPrice+1)/2==c.Items[7],"Sell refund changed or unsupported item");
    var view=OwnedView(item.InvenIndex);
    Require(view!=null&&ViewItem(view).Id==item.Id&&typeof(IBItemBaseView).GetMethod("get_CurrentState").Invoke(view,null).ToString()=="Storage","Sell requires the exact owned storage view");
    var manager=Manager;
    manager.GetType().GetField("ὩὧὡὭὠὤὧὫὥὧὡ",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(manager,view);
    // Native selling removes the storage/physics view and returns it to the
    // pool. A bare RPC would leave it visible until the next full layout save.
    manager.SellSelectedItem();return;
   }
   if(c.Kind=="soul_refresh"){
    Require(p.ShopReloadCount==c.Items[4],"Shop refresh count changed");
    var price=Convert.ToInt32(Get(SoulResearchBindings.RefreshPrice));Require(price==c.Items[5]&&p.Coin>=price,"Shop refresh price changed");
    Call(SoulResearchBindings.ShopRefresh,(Action)SyncShop);return;
   }
   if(c.Kind=="soul_upgrade"){
    var item=Items.SingleOrDefault(x=>x.InvenIndex==c.Items[4]);Require(item!=null&&item.Level==c.Items[5]&&item.Id==c.Items[7],"Upgrade item changed");
    int count=c.Value==0?1:c.Value,total=0;
    for(int step=0;step<count;step++){var cap=CapabilityFor(item.Id,item.Level+step,item.InvenIndex);Require(cap.Reason=="available"&&cap.NextPrice>=0,"Upgrade exceeds current account capability; no request sent");total=checked(total+cap.NextPrice);}
    Require(total==c.Items[6]&&p.Coin>=total,"Upgrade total price changed; no request sent");
    Call(SoulResearchBindings.Upgrade,item,count,(Action<IbItemDBInfo,int,int,int>)((changed,coin,cost,level)=>{
     // Use the client's level updater: it removes mutable protobuf dictionary
     // keys before changing Level, then restores their entries correctly.
     var view=OwnedView(changed.InvenIndex);
     // A reconciliation read may replace the DTO list while views still hold
     // their original DTOs. The native updater explicitly updates both copies.
     Client.ManifestModule.ResolveMethod(SoulResearchBindings.ApplyLevel).Invoke(Manager,new object[]{view==null?changed:ViewItem(view),level});
     Play.Coin=coin;SyncGold();
     if(view!=null){var unit=view as IBUnitItemView;if(unit!=null)unit.PlayAnimation("Victory");view.PlayLevelupEffect();}
     foreach(var stage in UnityEngine.Object.FindObjectsOfType<InventoryBattleStageUI>())if(stage.gameObject.activeInHierarchy){stage.RefreshTooltipAfterUpgrade();stage.PlayBuyAnimation(cost,coin);stage.RefreshDeckStamina();}
    }));return;
   }
   if(c.Kind=="soul_buy"){
    var offer=Shop.SingleOrDefault(s=>s.Slot==c.Value);
    Require(offer!=null&&!offer.IsSoldOut&&offer.Id==c.Items[4]&&offer.Type==c.Items[5]&&offer.Price==c.Items[6]&&p.Coin>=offer.Price,"Soul shop offer changed; no purchase sent");
    var before=new HashSet<long>(Items.Select(x=>x.InvenIndex));
    Call(SoulResearchBindings.Buy,c.Value,(Action<IbShopBuyResponse>)(r=>{
     Play.Coin=r.CurrentCoin;
     foreach(var shop in UnityEngine.Object.FindObjectsOfType<InventoryBattleShopUI>())if(shop.gameObject.activeInHierarchy)MarkBought(shop,offer);
     var added=Items.Where(x=>!before.Contains(x.InvenIndex)).ToArray();
     Require(added.Length==1&&added[0].Id==offer.Id,"Purchased inventory differs from receipt");
     ShowPurchase(added[0],offer.Price,r.CurrentCoin);SyncGold();}));return;
   }
   var deck=new List<IbDeckDBInfo>();var seen=new HashSet<long>();
   for(int i=4;i<c.Items.Length;i+=3){
    long index=c.Items[i],position=c.Items[i+1],rotation=c.Items[i+2];
    Require(index>0&&seen.Add(index)&&Items.Any(x=>x.InvenIndex==index)&&position>=0&&position<49&&rotation>=0&&rotation<4,"Invalid owned deck; no save sent");
    deck.Add(new IbDeckDBInfo{InvenIndex=index,Position=(int)position,RotationCount=(int)rotation});
   }
   Call(SoulResearchBindings.Save,deck,(Action)SyncDeck);
  }
 }
}

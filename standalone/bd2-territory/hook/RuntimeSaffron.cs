using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Proto.Net;
using Proto.Design.common;
using UnityEngine;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed class SaffronJournal
 {
  public string Account{get;set;}="";public string Pending{get;set;}="";public string State{get;set;}="";
  public string Reason{get;set;}="";public long BuffEnd{get;set;}public int Harvested{get;set;}
  public long At{get;set;}public long Currency{get;set;}public long[] Stocks{get;set;}=new long[0];
 }
 internal sealed partial class RuntimeEngine
 {
  private SaffronStatus saffron=new SaffronStatus();private string saffronOwner="",saffronPath="";
  private SaffronJournal saffronJournal;private bool saffronCallback,saffronRecoverySent;private long saffronActionAt,saffronStarted,saffronPhaseAt;
  private string[] saffronFields=new string[0];private bool saffronFilling;
  private LifeGatheringObject[] saffronOrder=new LifeGatheringObject[0];private int saffronVisit,saffronInitial;
  private double saffronRouteSeconds;private bool saffronHadHarvest,saffronSpent;
  private long SaffronNow()=>Convert.ToInt64(B.InvokeOn("Saffron.Now",B.Invoke("Saffron.Clock")));
  private long FoodCount(int id)=>SaleInventory().Where(x=>x.Id==id&&!x.IsDisableSlot&&x.KeepFlag==0).Sum(x=>(long)x.Count);
  private long FoodEnd(int id)=>((IEnumerable<LifeEatFoodDBInfo>)B.Invoke("Saffron.Foods")).Where(x=>x.ItemId==id).Select(x=>x.EndTime).DefaultIfEmpty(0).Max();
  private int FieldSeed(LifeFarmFieldObject f)=>CurrentDb(f)?.InnerObject.Where(x=>x.Status>0).Select(x=>x.ObjectId).FirstOrDefault()??0;
  private LifeGatheringObject FieldCrop(LifeFarmFieldObject f)=>B.Read("Farm.Crop",f) as LifeGatheringObject;
  private bool SaffronAllows(LifeGatheringObject n)
  {
   if(control==null||!control.SaffronDaily)return true;
   if(Kind(n)!=3)return false;
   return farms.Any(f=>B.Active(f)&&FieldCrop(f)==n&&SaffronCycle.AllowHarvest(saffron.State,FieldSeed(f),saffronFields.Contains(FarmKey(f))));
  }
  private void SaffronPhase(string state,long now)
  {if(saffron.State==state)return;saffron.State=state;saffronPhaseAt=now;if(saffronJournal!=null){saffronJournal.State=state;SaveSaffron();}}
  private void SaveSaffron()
  {if(saffronJournal.Account!=account)return;saffronJournal.Reason=saffron.Reason;saffronJournal.BuffEnd=saffron.BuffEnd;saffronJournal.Harvested=saffron.Harvested;LocalStorage.WriteJsonAtomically(saffronPath,saffronJournal);LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"saffron-latest.json"),saffronJournal);}
  private void SaffronEnd(string state,string reason,long now)
  {StopMotion();saffron.Reason=reason;SaffronPhase(state,now);}
  private void SaffronSubmit(string kind,long now,Action<Action> send)
  {
   if(saffronJournal.Pending.Length>0)throw new InvalidOperationException("上一藏红花操作尚未核对，未重复提交");
   if(control==null||!control.SaffronDaily||!control.Valid(DateTime.UtcNow.Ticks,pid)||control.OwnerId!=owner)throw new InvalidOperationException("藏红花控制权已改变，未提交操作");
   saffronCallback=false;saffronJournal.Pending=kind;saffronJournal.At=saffronActionAt=now;saffronJournal.Currency=SaleCurrency();saffronJournal.Stocks=SaffronCycle.Foods.Select(FoodCount).ToArray();
   SaveSaffron();
   if(kind=="eat"||kind.StartsWith("buy-"))saffronSpent=true;
   var own=saffronJournal;send(()=>{if(ReferenceEquals(own,saffronJournal))saffronCallback=true;});
  }
  private void SaffronValidateCatalog()
  {
   var recipe=(LifeCookTable)B.Invoke("Tables.Cook",SaffronCycle.Recipe);
   if(recipe==null||recipe.ResultItemId!=SaffronCycle.Dish||!recipe.RequireItemUniqueId.SequenceEqual(new[]{202,215})||!recipe.RequireItemCount.SequenceEqual(new[]{40,20}))throw new InvalidOperationException("藏红花料理配方已变化，未使用料理");
   foreach(int id in SaffronCycle.Foods){var item=(LifeItemTable)B.Invoke("Tables.ItemById",id);
    if(item==null||!(bool)B.Invoke("Layout.Unlocked",item.UnlockContentType,item.UnlockContentLevel)||!item.BuffId.Select(b=>(LifeBuffTable)B.Invoke("Saffron.Buff",b)).Any(b=>b!=null&&b.BuffType==32&&b.BuffValue==1&&b.BuffDuration==600))throw new InvalidOperationException("增产料理未解锁或效果已变化，未使用料理");
   }
   ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Seed),1);ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Potato),1);
  }
  private LifeBuyItemTable[] MissingFoodPurchases(long serverNow)
  {
   var rows=((IEnumerable<LifeBuyItemTable>)B.Invoke("Saffron.BuyRows",1)).ToArray();
   return SaffronCycle.Foods.Where(id=>FoodEnd(id)<=serverNow&&FoodCount(id)<1).Select(id=>{
    var matches=rows.Where(x=>x.GroupId==1&&x.ItemId==id&&x.ItemType==66&&x.ItemCount==1&&x.PriceType==64&&x.PriceId==0&&x.PriceCount>0&&x.PurchaseLimitCount==0).ToArray();
    if(matches.Length!=1)throw new InvalidOperationException("缺少的增产料理当前不能购买，未使用料理");return matches[0];
   }).ToArray();
  }
  private bool SaffronCapacity(int count,int seed)
  {
   var crop=ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",seed),1);
   // Each quality can receive the harvest. Reserve against the smallest stack
   // headroom, including the three additional crop-yield buffs.
   var ids=((IEnumerable<int>)B.Invoke("Tables.ItemIds",crop.IngredientId)).ToArray();
   long need=(long)count*(crop.Yield+3);
   return ids.Length>0&&ids.All(id=>{var item=(LifeItemTable)B.Invoke("Tables.ItemById",id);return item!=null&&item.StackCount-Convert.ToInt64(B.Invoke("Inventory.Count",id))>=need;});
  }
  private void SaffronSetSeed(TerritoryControl c,int seed)
  {
   c.FixedCrop=true;c.FixedSeedId=seed;c.RecipeId=SaffronCycle.Recipe;
   if(RecipeBatchPlanner.SwitchPlanting(progress,c.RecipeId,seed,farmStage>0)){SaveProgress(progress);crops=ReadPlantingCrops(c.RecipeId,seed);lastCrops=DateTime.UtcNow.Ticks;}
  }
  private void SaffronPlant(TerritorySnapshot s,TerritoryControl c,long now,int seed)
  {
   SaffronSetSeed(c,seed);var crop=crops.Single();RecipeBatchPlanner.Choose(progress,crops);
   if(SaleCurrency()<(long)crop.Price*farms.Count(Empty))throw new InvalidOperationException("种植预算不足，未继续种植");
   if(!PlantingTransaction.WithinBudget(progress,owner,c.PlantingBudget,crop.Price,farms.Count(Empty)))throw new InvalidOperationException("种植预算不足，未继续种植");
   targetFarm=farms.Where(Empty).OrderBy(f=>(f.GetFieldWorldCenter()-player.transform.position).sqrMagnitude).FirstOrDefault();
   if(targetFarm!=null)Plant(s,crop,now);
  }
  private void SaffronTick(TerritorySnapshot s,TerritoryControl c,long now)
  {
   s.Saffron=saffron;
   try
   {
    if(saffronOwner!=owner){saffronOwner=owner;saffron=new SaffronStatus();s.Saffron=saffron;saffronOrder=new LifeGatheringObject[0];saffronVisit=0;saffronHadHarvest=false;saffronSpent=false;saffronFilling=false;
     saffronPath=Path.Combine(LocalStorage.DataRoot,"saffron-"+account+".json");saffronJournal=ReadLayoutFile<SaffronJournal>(saffronPath)??new SaffronJournal{Account=account};
     if(saffronJournal.Account!=account||saffronJournal.Pending==null)throw new InvalidOperationException("藏红花上次操作身份不匹配，未继续执行");
     saffronStarted=saffronPhaseAt=now;
     if(saffronJournal.Pending.Length>0){saffron.State="recover";saffronRecoverySent=saffronCallback=false;saffronActionAt=now;}
    }
    if(saffron.State=="completed"||saffron.State=="skipped"||saffron.State=="partial"){s.Reason=saffron.Reason;return;}
    if(now-saffronStarted>TimeSpan.FromMinutes(18).Ticks)throw new InvalidOperationException("藏红花环节超时，已停止并保留当前进度");
    if(saffron.State=="recover"){
     StopMotion();s.Reason="正在同步藏红花库存与增产效果，不重复上次操作";
     if(now-saffronActionAt>TimeSpan.FromSeconds(20).Ticks)throw new InvalidOperationException("藏红花库存暂时无法同步；未重复购买或使用料理，可稍后重试此环节");
     if(network.NativeBusy||network.Waiting)return;
     if(!saffronRecoverySent){saffronRecoverySent=true;var own=saffronJournal;B.Invoke("Sales.Refresh",(Action)(()=>{if(ReferenceEquals(own,saffronJournal))saffronCallback=true;}));return;}
     if(!saffronCallback)return;
     // Preserve the unknown operation, then plan solely from a fresh native
     // LifeInfo response (inventory, currency, fields and duration buffs).
     var history=Path.Combine(LocalStorage.DataRoot,"saffron-history");Directory.CreateDirectory(history);
     LocalStorage.WriteJsonAtomically(Path.Combine(history,now+"-unknown.json"),saffronJournal);
     saffronJournal.Pending="";saffronCallback=false;SaffronPhase("idle",now);return;
    }
    if(saffronJournal.Pending.Length>0){
     StopMotion();s.Reason="等待藏红花操作确认："+saffronJournal.Pending;
     if(!saffronCallback){if(now-saffronActionAt>TimeSpan.FromSeconds(15).Ticks)throw new InvalidOperationException("藏红花操作回执未确认，未重复提交；请核对诊断记录");return;}
     saffronJournal.Pending="";SaveSaffron();saffronCallback=false;
    }
    if(network.NativeBusy||network.Waiting||CookingBusy()||SalesBusy()){s.Reason="等待当前领地操作结算";if(SalesBusy())SalesTick(s,c,now);if(CookingBusy())CookingTick(s,c,now);return;}
    if(saffron.State=="idle"){
     SaffronValidateCatalog();
     int totalFields=worldObjects.Values.Count(o=>((LifeBuildingObjectTable)B.Invoke("Tables.Building",o.ObjectId))?.FunctionType==3);
     if(farms.Length==0||totalFields!=farms.Length||farms.Any(f=>CurrentDb(f)==null||ReadFarmReadiness(f)==FarmReadiness.Unknown))throw new InvalidOperationException("农田未完整载入，未使用料理");
     saffronFields=farms.Select(FarmKey).OrderBy(x=>x).ToArray();
     if(farms.All(Empty)){SaffronPhase("replant",now);return;}
     if(farms.Any(f=>!Empty(f)&&FieldSeed(f)!=SaffronCycle.Seed))throw new InvalidOperationException("农田含其他作物，未使用料理；请为藏红花环节准备空田或成熟藏红花");
     saffronOrder=farms.Where(f=>!Empty(f)).Select(FieldCrop).ToArray();saffronInitial=saffronOrder.Length;
     if(saffronOrder.Any(n=>n==null||!Harvestable(n)))throw new InvalidOperationException("藏红花尚未全部成熟，未使用料理；成熟后再运行");
     saffronOrder=saffronOrder.OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).ToArray();
     SaffronSubmit("refresh",now,done=>B.Invoke("Sales.Refresh",done));SaffronPhase("survey",now);return;
    }
    if(!saffronFields.SequenceEqual(farms.Select(FarmKey).OrderBy(x=>x)))throw new InvalidOperationException("农田布局已变化，已停止藏红花环节");
    long serverNow=SaffronNow();
    if(saffron.State=="survey"){
     if(now-saffronPhaseAt>TimeSpan.FromSeconds(180).Ticks)throw new InvalidOperationException("无法及时到达全部藏红花，未使用料理");
     if(saffronOrder.Any(n=>n==null||!Harvestable(n)))throw new InvalidOperationException("藏红花状态变化，未使用料理");
     if(saffronVisit<saffronOrder.Length){var n=saffronOrder[saffronVisit];
      if(!Allowed(n,now))throw new InvalidOperationException("藏红花存在不可达点，未使用料理");
      if(CanGatherAt(n,player.transform.position)&&Detected(3).Contains(n)){saffronVisit++;return;}
      Approach(n,n.transform.position,s,now,.7f);return;
     }
     saffronRouteSeconds=TimeSpan.FromTicks(now-saffronPhaseAt).TotalSeconds;
     Array.Reverse(saffronOrder); // Start harvesting where the completed survey ended.
     SaffronPhase("prepare",now);
    }
    if(saffron.State=="prepare"){
     if(now-saffronPhaseAt>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("收割准备超时，未使用料理");
     var missing=MissingFoodPurchases(serverNow);long reserve=(long)farms.Length*(ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Seed),1).Price+10);
     double available=SaffronCycle.Foods.Select(id=>FoodEnd(id)>serverNow?(FoodEnd(id)-serverNow)/1000.0:580).Min();
     bool slots=SaleInventory().Length+missing.Length+3<=((LifeDefaultTable)B.Read("Tables.Default",null)).DefaultConsumableInvenSlotCount;
     if(!SaffronCycle.CanBuff(saffronInitial,saffronOrder.All(n=>n!=null&&Harvestable(n)),true,slots&&SaffronCapacity(saffronInitial,SaffronCycle.Seed),SaleCurrency()>=reserve+missing.Sum(r=>(long)r.PriceCount),saffronRouteSeconds,available))throw new InvalidOperationException("收割时间、背包容量或积分不足，未使用料理");
     if(missing.Length>0){var row=missing[0];var priceType=((MethodInfo)B.Api("Saffron.Buy")).GetParameters()[3].ParameterType;
      SaffronSubmit("buy-"+row.ItemId,now,done=>B.Invoke("Saffron.Buy",row.GroupId,row.Id,1,Enum.ToObject(priceType,row.PriceType),row.PriceId,row.PriceCount,done));return;
     }
     targetNode=saffronOrder.First();if(!CanGatherAt(targetNode,player.transform.position)||!Detected(3).Contains(targetNode)||!FootReady(s))throw new InvalidOperationException("尚未站到可收割位置，未使用料理");
     var consume=new List<ItemDBInfo>();foreach(int id in SaffronCycle.Foods.Where(id=>FoodEnd(id)<=serverNow)){
      var item=SaleInventory().FirstOrDefault(x=>x.Id==id&&x.Count>0&&x.KeepFlag==0&&!x.IsDisableSlot);if(item==null)throw new InvalidOperationException("增产料理库存变化，未使用料理");var one=item.Clone();one.Count=1;consume.Add(one);
     }
     SaffronPhase("buff",now);if(consume.Count>0)SaffronSubmit("eat",now,done=>B.Invoke("Saffron.Eat",consume,done));return;
    }
    if(saffron.State=="buff"){
     saffron.BuffEnd=SaffronCycle.Foods.Select(FoodEnd).Min();
     if(saffron.BuffEnd-serverNow<SaffronCycle.HarvestSeconds(saffronInitial,saffronRouteSeconds)*1000)throw new InvalidOperationException("三种增产效果未完整确认，未继续收割");
     saffronHadHarvest=true;SaffronPhase("harvest",now);
    }
    if(saffron.State=="harvest"){
     if(serverNow>=saffron.BuffEnd)throw new InvalidOperationException("增产效果已结束，藏红花未收完；已停止，未无加成收割");
     var remaining=farms.Where(f=>FieldSeed(f)==SaffronCycle.Seed&&!Empty(f)).Select(FieldCrop).ToArray();saffron.Harvested=saffronInitial-remaining.Length;
     if(remaining.Length==0){targetNode=null;SaffronPhase("potatoes",now);return;}
     targetNode=saffronOrder.FirstOrDefault(n=>n!=null&&remaining.Contains(n)&&Harvestable(n));
     if(targetNode==null||!Allowed(targetNode,now))throw new InvalidOperationException("藏红花收割路径变化，停止后续操作");
     Gather(s,now);return;
    }
    if(saffron.State=="potatoes"||saffron.State=="drain"){
     var grown=farms.Where(f=>FieldSeed(f)==SaffronCycle.Potato&&!Empty(f)).ToArray();
     targetNode=grown.Select(FieldCrop).Where(n=>n!=null&&Harvestable(n)).OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
     if(!saffronFilling&&grown.Length>0){
      if(targetNode!=null){if(!Allowed(targetNode,now))throw new InvalidOperationException("土豆收割路径暂不可达，保留作物");Gather(s,now);return;}
      s.Reason="等待本批土豆成熟，不提前补种藏红花";return;
     }
     if(saffron.State=="drain"&&serverNow<saffron.BuffEnd){s.Reason="剩余增产时间不足一轮土豆；效果结束后补种藏红花";return;}
     var potato=ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Potato),1);
     string action=SaffronCycle.PotatoAction(serverNow,saffron.BuffEnd,potato.GrowthSeconds,SaffronCycle.HarvestSeconds(farms.Length,saffronRouteSeconds),saffronFilling,farms.Count(Empty),targetNode==null?0:1,grown.Length,SaleCurrency()>=(long)farms.Length*(potato.Price+100),SaffronCapacity(farms.Length,SaffronCycle.Potato));
     if(action=="plant"){
      if(!saffronFilling){saffronFilling=true;saffron.PotatoRounds++;}
      targetNode=null;SaffronPlant(s,c,now,SaffronCycle.Potato);return;
     }
     saffronFilling=false;
     if(action=="harvest"){if(!Allowed(targetNode,now))throw new InvalidOperationException("土豆收割路径暂不可达，保留作物");Gather(s,now);return;}
     if(action=="wait"){s.Reason="等待本批土豆成熟，不提前补种藏红花";return;}
     if(action=="replant"){SaffronPhase("replant",now);return;}
     SaffronPhase("drain",now);s.Reason="剩余增产时间不足一轮土豆；效果结束后补种藏红花";return;
    }
    if(saffron.State=="replant"){
     if(farms.Any(f=>!Empty(f)&&FieldSeed(f)!=SaffronCycle.Seed))throw new InvalidOperationException("尚有其他作物未收完，未覆盖种植");
     if(farms.Any(Empty)){SaffronPlant(s,c,now,SaffronCycle.Seed);return;}
     SaffronPhase("cook",now);
    }
    if(saffron.State=="cook"){
     SaffronSetSeed(c,SaffronCycle.Seed);
     if(CookingCount(SaffronCycle.Recipe,1000)>0){c.Cooking=true;c.CookingBatch=1000;CookingTick(s,c,now);return;}
     SaffronPhase("sell",now);
    }
    if(saffron.State=="sell"){
     var stocks=SaleStocks().Where(x=>x.Item==SaffronCycle.Dish&&x.Eligible&&!x.Locked).OrderBy(x=>x.Index).ToArray();long remaining=SaffronCycle.SellCount(stocks.Sum(x=>(long)x.Count));var lines=new List<SaleLine>();
     foreach(var stock in stocks){int count=(int)Math.Min(remaining,stock.Count);if(count>0){lines.Add(new SaleLine{Index=stock.Index,Item=stock.Item,Before=stock.Count,Count=count,Group=stock.Group,Row=stock.Row,Price=stock.Price});remaining-=count;}}
     if(lines.Count>0){SalesTick(s,c,now,lines.Take(32).ToArray(),"saffron");return;}
     var saffronStock=ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Seed),1);
     if(saffronStock.Inventory>=20)throw new InvalidOperationException("已补种并售卖成品，仍有藏红花原料；土豆或料理容量不足，已保留供下次制作");
     SaffronEnd("completed",saffronHadHarvest?"藏红花已收割，增产土豆循环结束；已补种并制作料理，成品保留一份":"已补种藏红花，成熟后再运行收割",now);return;
    }
   }
   catch(Exception e){SaffronEnd(saffronHadHarvest||saffronSpent?"partial":"skipped",e.GetBaseException().Message,now);s.Reason=saffron.Reason;}
  }
 }
}

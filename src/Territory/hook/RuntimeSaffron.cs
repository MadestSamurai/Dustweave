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
 internal sealed partial class RuntimeEngine
 {
  private SaffronStatus saffron=new SaffronStatus();private string saffronOwner="",saffronPath="";
  private SaffronJournal saffronJournal;private bool saffronCallback,saffronRecoverySent;private long saffronActionAt,saffronStarted,saffronPhaseAt;
  private string[] saffronFields=new string[0];private bool saffronFilling;
  private LifeGatheringObject[] saffronOrder=new LifeGatheringObject[0];private int saffronInitial;
  private bool saffronHadHarvest,saffronSpent;
  private bool saffronResources;private readonly HashSet<string> saffronFinishedResources=new HashSet<string>();private readonly HashSet<string> saffronSkippedResources=new HashSet<string>();
  private string saffronResourceKey="";
  private Component saffronAccessTarget;private Vector3 saffronAccessPoint;private long saffronAccessAt,saffronAccessArrived;
  private string saffronAccessKey="";private readonly HashSet<string> saffronAccessVisited=new HashSet<string>();
  private bool SaffronAccess(Component target)=>control!=null&&control.SaffronDaily&&saffronOwner==control.OwnerId&&saffron.State=="idle"&&target!=null&&target==saffronAccessTarget;
  private Vector3 RouteCenter(Component target)=>SaffronAccess(target)?saffronAccessPoint:target is LifeFarmFieldObject farm?farm.GetFieldWorldCenter():target.transform.position;
  private void SaffronLoadFields(TerritorySnapshot s,long now,int expected)
  {
   // Native world placement exists before the camera activates farm objects.
   // Move normally to a registered field; do not activate objects or consume food.
   if(saffronAccessAt==0){saffronAccessAt=now;saffronAccessVisited.Clear();saffronAccessTarget=null;saffronAccessKey="";}
   if(now-saffronAccessAt>TimeSpan.FromSeconds(90).Ticks)throw new InvalidOperationException("接近农田后数据仍未就绪，已停止此环节；未使用新料理");
   var ground=LayoutGround();var world=B.Read("Inventory.World",null) as IEnumerable<LifeWorldObjectPlaceDBInfo>;
   s.Reason="正在接近已登记的农田，等待场景载入；暂不使用料理";
   if(ground==null||world==null||expected==0)return;
   if(!FootReady(s))return;
   if(saffronAccessTarget!=null){
    if(CanGatherAt(saffronAccessTarget,player.transform.position)){
     if(saffronAccessArrived==0)saffronAccessArrived=now;
     if(now-saffronAccessArrived<TimeSpan.FromSeconds(2).Ticks)return;
     saffronAccessVisited.Add(saffronAccessKey);saffronAccessTarget=null;
    }else if(!Allowed(saffronAccessTarget,now)){saffronAccessVisited.Add(saffronAccessKey);saffronAccessTarget=null;}
    else {Approach(saffronAccessTarget,saffronAccessPoint,s,now,.7f);return;}
   }
   var loaded=new HashSet<string>(farms.Where(f=>CurrentDb(f)!=null&&ReadFarmReadiness(f)!=FarmReadiness.Unknown).Select(FarmKey));
   var candidates=world.SelectMany(chunk=>chunk.Object.Where(o=>((LifeBuildingObjectTable)B.Invoke("Tables.Building",o.ObjectId))?.FunctionType==3)
    .Select(o=>new{Key=TerritoryNetwork.Key(chunk.ChunkId,o),Chunk=chunk.ChunkId,Db=o}))
    .Where(x=>!loaded.Contains(x.Key)&&!saffronAccessVisited.Contains(x.Key)&&ground.FindChunkById(x.Chunk)!=null)
    .Select(x=>new{ x.Key,Point=ground.GetCellWorldPosition(x.Chunk,x.Db.X,x.Db.Y)})
    .OrderBy(x=>(x.Point-player.transform.position).sqrMagnitude).ToArray();
   if(candidates.Length==0)return;
   // A few loading approaches suffice for a crop cluster. Never tour every field
   // before consuming timed buffs, and never repeat an unreachable destination.
   if(saffronAccessVisited.Count>=3)throw new InvalidOperationException("部分农田未载入或无法接近，已停止此环节；未使用新料理");
   var next=candidates[0];saffronAccessKey=next.Key;saffronAccessPoint=next.Point;saffronAccessTarget=ground;saffronAccessArrived=0;
   navigation.Reset();ResetLocalRecovery();triedDestinations.Clear();skip.Remove(ground.GetInstanceID());
   LocalStorage.Log("农田载入接近 key="+next.Key+" loaded="+loaded.Count+"/"+expected+" from="+player.transform.position+" to="+next.Point);
   Approach(saffronAccessTarget,saffronAccessPoint,s,now,.7f);
  }
  private long saffronWaitEnd;private int saffronHarvestedBefore;private bool saffronRoundPlanted;private string saffronDecision="",saffronResumeState="";
  private long SaffronNow()=>Convert.ToInt64(B.InvokeOn("Saffron.Now",B.Invoke("Saffron.Clock")));
  private long FoodCount(int id)=>SaleInventory().Where(x=>x.Id==id&&!x.IsDisableSlot&&x.KeepFlag==0).Sum(x=>(long)x.Count);
  private long FoodEnd(int id)=>((IEnumerable<LifeEatFoodDBInfo>)B.Invoke("Saffron.Foods")).Where(x=>x.ItemId==id).Select(x=>x.EndTime).DefaultIfEmpty(0).Max();
  private int FieldSeed(LifeFarmFieldObject f)=>CurrentDb(f)?.InnerObject.Where(x=>x.Status>0).Select(x=>x.ObjectId).FirstOrDefault()??0;
  private LifeGatheringObject FieldCrop(LifeFarmFieldObject f)=>B.Read("Farm.Crop",f) as LifeGatheringObject;
  private bool SaffronAllows(LifeGatheringObject n)
  {
   if(control==null||!control.SaffronDaily)return true;
   return Kind(n)==3||saffronResources&&(Kind(n)==1||Kind(n)==2);
  }
  // Called only after AwaitGathering has settled the current native action.
  private void SaffronReturnForMaturePotatoes()
  {
   if(jumping||!saffronResources||!(saffron.State=="potatoes"||saffron.State=="drain"))return;
   var planted=farms.Where(f=>FieldSeed(f)==SaffronCycle.Potato&&!Empty(f)).ToArray();
   if(!SaffronCycle.ReturnToPotatoes(planted.Length,planted.Count(f=>FieldCrop(f)!=null&&Harvestable(FieldCrop(f))),farms.All(f=>CurrentDb(f)!=null&&ReadFarmReadiness(f)!=FarmReadiness.Unknown),network.NativeBusy||network.Waiting||SalesBusy()||CookingBusy()))return;
   StopMotion();targetNode=null;saffronResources=false;navigation.Reset();triedDestinations.Clear();
  }
  private bool SaffronGatherResources(TerritorySnapshot s,TerritoryControl c,long now)
  {
   saffronResources=true;
   var surplus=UrgentSurplus(c,now);if(surplus.Length>0&&SalesTick(s,c,now,surplus))return true;
   var available=nodes.Where(n=>Harvestable(n)&&(Kind(n)==1||Kind(n)==2)).ToArray();
   if(saffronResourceKey.Length>0&&!available.Any(n=>ResourceKey(n)==saffronResourceKey))saffronFinishedResources.Add(saffronResourceKey);
   foreach(var node in available){string key=ResourceKey(node);if(failures.TryGetValue(key,out var memory)&&memory.Failures>=2)saffronSkippedResources.Add(key);}
   var remaining=available.Where(n=>!saffronFinishedResources.Contains(ResourceKey(n))&&!saffronSkippedResources.Contains(ResourceKey(n))).ToArray();
   var eligible=remaining.Where(n=>Allowed(n,now)).ToArray();
   if(targetNode==null||!eligible.Contains(targetNode)){
    StopMotion();targetNode=eligible.OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
    ResetLocalRecovery();travel.Reset();
   }
   s.Saffron.GatheredResources=saffronFinishedResources.Count;s.Saffron.SkippedResources=saffronSkippedResources.Count;
   if(targetNode==null){saffronResourceKey="";if(remaining.Length>0)s.Reason="等待树矿路径重试，土豆成熟后优先收割";return remaining.Length>0;}
   saffronResourceKey=ResourceKey(targetNode);
   if(!failures.TryGetValue(saffronResourceKey,out var selected))failures[saffronResourceKey]=selected=new TargetFailureMemory();selected.Seen(now);
   Gather(s,now);return true;
  }
  private LifeGatheringObject SaffronMatureCrop()=>farms.Select(FieldCrop).Where(n=>n!=null&&Harvestable(n)).OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
  private void SaffronPhase(string state,long now)
  {if(saffron.State==state)return;saffron.State=state;saffronPhaseAt=now;LocalStorage.Log("藏红花阶段 "+state+" harvested="+saffron.Harvested+" buffEnd="+saffron.BuffEnd);if(saffronJournal!=null){saffronJournal.State=state;if(state=="harvest"||state=="potatoes"||state=="drain"||state=="replant"||state=="cook"||state=="sell")saffronJournal.WorkState=state;SaveSaffron();}}
  private void SaveSaffron()
  {if(saffronJournal.Account!=account)return;saffronJournal.Reason=saffron.Reason;saffronJournal.BuffEnd=saffron.BuffEnd;saffronJournal.Harvested=saffron.Harvested;saffronJournal.PotatoRounds=saffron.PotatoRounds;saffronJournal.PotatoRoundLimit=saffron.PotatoRoundLimit;saffronJournal.PotatoRoundPlanted=saffronRoundPlanted;saffronJournal.Filling=saffronFilling;LocalStorage.WriteJsonAtomically(saffronPath,saffronJournal);LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"saffron-latest.json"),saffronJournal);}
  private void SaffronEnd(string state,string reason,long now)
  {StopMotion();saffronResources=false;if(state=="completed"&&saffronSkippedResources.Count>0){state="partial";reason="藏红花流程已结束；部分树矿暂不可达，已跳过，其他日常继续";}saffron.Reason=reason;SaffronPhase(state,now);}
  private void SaffronSubmit(string kind,long now,Action<Action> send)
  {
   if(saffronJournal.Pending.Length>0)throw new InvalidOperationException("上一藏红花操作尚未核对，未重复提交");
   if(control==null||!control.SaffronDaily||!control.Valid(DateTime.UtcNow.Ticks,pid)||control.OwnerId!=owner)throw new InvalidOperationException("藏红花控制权已改变，未提交操作");
   saffronCallback=false;saffronRecoverySent=false;saffronJournal.Pending=kind;saffronJournal.At=saffronActionAt=now;saffronJournal.Currency=SaleCurrency();saffronJournal.Stocks=SaffronCycle.Foods.Select(FoodCount).ToArray();
   SaveSaffron();
   if(kind=="eat"||kind.StartsWith("buy-")||kind.StartsWith("cook-"))saffronSpent=true;
   var own=saffronJournal;send(()=>{if(ReferenceEquals(own,saffronJournal)&&saffronActionAt==now&&saffronJournal.Pending==kind)saffronCallback=true;});
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
  private SaffronFoodPreparation[] PrepareSaffronFoods(long serverNow)
  {
   var missing=SaffronCycle.Foods.Where(id=>FoodEnd(id)<=serverNow&&FoodCount(id)<1).ToArray();
   if(missing.Length==0)return new SaffronFoodPreparation[0];
   var recipes=((IEnumerable<int>)B.Invoke("Tables.CookIds")).Select(id=>(LifeCookTable)B.Invoke("Tables.Cook",id)).Where(x=>x!=null&&missing.Contains(x.ResultItemId)).ToArray();
   var inputs=missing.Select(id=>{var matches=recipes.Where(x=>x.ResultItemId==id).ToArray();
    if(matches.Length>1)throw new InvalidOperationException("增产料理配方不唯一，未制作或购买");var recipe=matches.SingleOrDefault();
    return recipe==null?new SaffronFoodRecipe{Food=id}:new SaffronFoodRecipe{Food=id,Recipe=recipe.Id,Ingredients=recipe.RequireItemUniqueId.ToArray(),Counts=recipe.RequireItemCount.ToArray()};
   }).ToArray();
   var inventory=SaleInventory().Where(x=>!x.IsDisableSlot&&x.KeepFlag==0).ToArray();
   var stocks=inputs.SelectMany(x=>x.Ingredients).Distinct().ToDictionary(id=>id,id=>{var ids=CookingItemIds(id);return inventory.Where(x=>ids.Contains(x.Id)).Sum(x=>(long)x.Count);});
   return SaffronCycle.PrepareFoods(inputs,stocks);
  }
  private LifeBuyItemTable[] MissingFoodPurchases(IEnumerable<int> missing)
  {
   if(!missing.Any())return new LifeBuyItemTable[0];
   var rows=((IEnumerable<LifeBuyItemTable>)B.Invoke("Saffron.BuyRows",1)).ToArray();
   return missing.Select(id=>{
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
   targetFarm=farms.Where(f=>Empty(f)&&Allowed(f,now)).OrderBy(f=>(f.GetFieldWorldCenter()-player.transform.position).sqrMagnitude).FirstOrDefault();
   if(targetFarm!=null)Plant(s,crop,now);
   else s.Reason="等待暂不可达农田的重试间隔";
  }
  private void SaffronDecision(TerritorySnapshot s,string action,long serverNow,int empty,int grown)
  {
   saffron.ServerNow=serverNow;saffron.EmptyFields=empty;saffron.PotatoFields=grown;
   string key=action+"|"+empty+"|"+grown+"|"+s.Reason;
   if(key==saffronDecision)return;saffronDecision=key;
   LocalStorage.Log("藏红花循环 action="+action+" completedRounds="+saffron.PotatoRounds+" targetRounds="+saffron.PotatoRoundLimit+" empty="+empty+" potatoes="+grown);
  }
  private void SaffronTick(TerritorySnapshot s,TerritoryControl c,long now)
  {
   s.Saffron=saffron;
   try
   {
    if(c.SaffronPotatoRounds<1||c.SaffronPotatoRounds>3)throw new InvalidOperationException("藏红花收割后的土豆轮数必须为 1 至 3。");
    if(saffronOwner!=owner){saffronOwner=owner;saffron=new SaffronStatus{State="sync"};s.Saffron=saffron;saffronOrder=new LifeGatheringObject[0];saffronHadHarvest=false;saffronSpent=false;saffronFilling=false;saffronRoundPlanted=false;saffronWaitEnd=0;saffronDecision="";
     saffronAccessTarget=null;saffronAccessAt=saffronAccessArrived=0;saffronAccessKey="";saffronAccessVisited.Clear();
     saffronPath=Path.Combine(LocalStorage.DataRoot,"saffron-"+account+".json");saffronJournal=ReadLayoutFile<SaffronJournal>(saffronPath)??new SaffronJournal{Account=account};
     if(saffronJournal.Account!=account||saffronJournal.Pending==null)throw new InvalidOperationException("藏红花上次操作身份不匹配，未继续执行");
     saffronResumeState=string.IsNullOrEmpty(saffronJournal.WorkState)?saffronJournal.State:saffronJournal.WorkState;saffron.BuffEnd=saffronJournal.BuffEnd;saffron.Harvested=saffronJournal.Harvested;
     saffron.PotatoRounds=saffronJournal.PotatoRoundLimit>0?saffronJournal.PotatoRounds:saffronResumeState=="drain"?1:0;
     saffron.PotatoRoundLimit=c.SaffronPotatoRounds;saffronRoundPlanted=saffronJournal.PotatoRoundPlanted;saffronFilling=saffronJournal.Filling;
     saffronResources=false;saffronFinishedResources.Clear();saffronSkippedResources.Clear();saffronResourceKey="";saffronStarted=saffronPhaseAt=now;
     if(saffronJournal.Pending.Length>0){saffron.State="recover";saffronRecoverySent=saffronCallback=false;saffronActionAt=now;}
    }
    if(saffron.State=="completed"||saffron.State=="skipped"||saffron.State=="partial"){s.Reason=saffron.Reason;return;}
    if(now-saffronStarted>TimeSpan.FromMinutes(35).Ticks)throw new InvalidOperationException("藏红花环节超时，已停止并保留当前进度");
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
     if(saffronJournal.Pending=="eat"&&!saffronCallback&&!network.NativeBusy&&!network.Waiting){
      // Native effects are authoritative even when the optional UI callback was
      // interrupted. Never eat again or restart preparation after they appear.
      var ends=SaffronCycle.Foods.Select(FoodEnd).ToArray();
      if(SaffronCycle.FoodsActive(SaffronNow(),ends)){saffron.BuffEnd=ends.Min();saffronCallback=true;LocalStorage.Log("三种增产效果已生效，继续收割；不重复使用料理");}
      else if(!saffronRecoverySent&&now-saffronActionAt>=TimeSpan.FromSeconds(2).Ticks){
       saffronRecoverySent=true;LocalStorage.Log("料理回调尚未完成，同步一次现有效果；不重复使用料理");B.Invoke("Sales.Refresh",(Action)(()=>{}));return;
      }
     }
     if(!saffronCallback){if(now-saffronActionAt>TimeSpan.FromSeconds(15).Ticks)throw new InvalidOperationException("藏红花操作回执未确认，未重复提交；请核对诊断记录");return;}
     if(saffronJournal.Pending.StartsWith("cook-")||saffronJournal.Pending.StartsWith("buy-")){
      int food=int.Parse(saffronJournal.Pending.Substring(saffronJournal.Pending.IndexOf('-')+1));
      if(!SaffronCycle.PreparedFoodConfirmed(food,saffronJournal.Stocks,SaffronCycle.Foods.Select(FoodCount).ToArray()))throw new InvalidOperationException("增产料理份数未增加，已停止准备，未重复制作或购买");
      saffronPhaseAt=now; // Progress, not a longer wait for the same operation.
     }
     saffronJournal.Pending="";SaveSaffron();saffronCallback=false;
    }
    if(network.NativeBusy||network.Waiting||CookingBusy()||SalesBusy()){s.Reason="等待当前领地操作结算";if(SalesBusy())SalesTick(s,c,now);if(CookingBusy())CookingTick(s,c,now);return;}
    if(saffron.State=="sync"){
     // Reconcile even when the previous operation already has a receipt: native
     // crops and duration buffs may have advanced while daily was disconnected.
     SaffronSubmit("refresh",now,done=>B.Invoke("Sales.Refresh",done));saffron.State="idle";return;
    }
    if(saffron.State=="idle"){
     SaffronValidateCatalog();
     int totalFields=worldObjects.Values.Count(o=>((LifeBuildingObjectTable)B.Invoke("Tables.Building",o.ObjectId))?.FunctionType==3);
     if(farms.Length==0||totalFields!=farms.Length||farms.Any(f=>CurrentDb(f)==null||ReadFarmReadiness(f)==FarmReadiness.Unknown)){
      SaffronLoadFields(s,now,totalFields);return;
     }
     saffronAccessTarget=null;saffronAccessAt=0;saffronAccessVisited.Clear();
     saffronFields=farms.Select(FarmKey).OrderBy(x=>x).ToArray();
     long currentTime=SaffronNow();saffron.BuffEnd=SaffronCycle.Foods.Select(FoodEnd).Min();
     bool sameCycle=saffronJournal.BuffEnd>0&&saffronJournal.BuffEnd==saffron.BuffEnd;
     // Replant/cook can resume after buffs have disappeared from the native list.
     bool recentReplant=saffronJournal.BuffEnd>0&&currentTime-saffronJournal.BuffEnd<TimeSpan.FromHours(19).TotalMilliseconds&&
      (saffronResumeState=="replant"||saffronResumeState=="cook"||saffronResumeState=="sell");
     bool recentCycle=saffronJournal.BuffEnd>0&&currentTime-saffronJournal.BuffEnd<TimeSpan.FromHours(19).TotalMilliseconds&&saffronJournal.Harvested>0;
     if(!(sameCycle||recentCycle||recentReplant)){saffron.Harvested=0;saffron.PotatoRounds=0;saffronRoundPlanted=false;saffronFilling=false;}
     saffronHarvestedBefore=saffron.Harvested;
     saffronHadHarvest=saffron.Harvested>0;
     if((recentReplant||recentCycle)&&saffron.BuffEnd==0)saffron.BuffEnd=saffronJournal.BuffEnd;
     var occupied=farms.Where(f=>!Empty(f)).ToArray();
     saffronOrder=occupied.Where(f=>FieldSeed(f)==SaffronCycle.Seed).Select(FieldCrop).ToArray();saffronInitial=saffronOrder.Length;
     int potatoes=occupied.Count(f=>FieldSeed(f)==SaffronCycle.Potato);
     string resume=SaffronCycle.ResumePhase(farms.Length-occupied.Length,saffronInitial,potatoes,occupied.Length-saffronInitial-potatoes,
      saffronOrder.All(n=>n!=null&&Harvestable(n)),recentCycle||saffron.BuffEnd>currentTime,recentReplant);
     if(resume=="unripe"){
      targetNode=occupied.Where(f=>FieldSeed(f)!=SaffronCycle.Seed).Select(FieldCrop).Where(n=>n!=null&&Harvestable(n)).OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
      if(targetNode!=null){Gather(s,now);return;}
      throw new InvalidOperationException("藏红花尚未全部成熟，未使用新料理；成熟后再运行");
     }
     if(potatoes>0)saffronRoundPlanted=true;SaffronPhase(resume,now);return;
    }
    if(!saffronFields.SequenceEqual(farms.Select(FarmKey).OrderBy(x=>x)))throw new InvalidOperationException("农田布局已变化，已停止藏红花环节");
    long serverNow=SaffronNow();
    saffron.ServerNow=serverNow;saffron.WaitUntil=0;
    if(saffron.State=="approach"){
     if(now-saffronPhaseAt>TimeSpan.FromSeconds(180).Ticks)throw new InvalidOperationException("未能到达藏红花收割位置，未使用新料理");
     if(saffronOrder.Any(n=>n==null||!Harvestable(n)))throw new InvalidOperationException("藏红花状态变化，未使用料理");
     // Go directly to one harvest position. Do not walk every field before
     // eating: remaining crops are approached only as part of actual harvesting.
     targetNode=saffronOrder.OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).First();
     if(!Allowed(targetNode,now))throw new InvalidOperationException("藏红花收割位置暂不可达，未使用新料理");
     if(!FootReady(s))return;
     if(!CanGatherAt(targetNode,player.transform.position)||!Detected(3).Contains(targetNode)){
      Approach(targetNode,targetNode.transform.position,s,now,.7f);return;
     }
     SaffronPhase("prepare",now);
    }
    if(saffron.State=="prepare"){
     if(now-saffronPhaseAt>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("收割准备超时，未使用料理");
     var preparation=PrepareSaffronFoods(serverNow);var missing=MissingFoodPurchases(preparation.Where(x=>!x.Cook).Select(x=>x.Food));long reserve=(long)farms.Length*(ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Seed),1).Price+10);
     double available=SaffronCycle.Foods.Select(id=>FoodEnd(id)>serverNow?(FoodEnd(id)-serverNow)/1000.0:580).Min();
     bool slots=SaleInventory().Length+preparation.Length+3<=((LifeDefaultTable)B.Read("Tables.Default",null)).DefaultConsumableInvenSlotCount;
     bool positioned=targetNode!=null&&CanGatherAt(targetNode,player.transform.position)&&Detected(3).Contains(targetNode);
     if(!positioned){SaffronPhase("approach",now);return;}
     if(!SaffronCycle.CanBuff(saffronInitial,saffronOrder.All(n=>n!=null&&Harvestable(n)),positioned,slots&&SaffronCapacity(saffronInitial,SaffronCycle.Seed),SaleCurrency()>=reserve+missing.Sum(r=>(long)r.PriceCount),available))throw new InvalidOperationException("收割时间、背包容量或积分不足，未使用新料理");
     var craft=preparation.FirstOrDefault(x=>x.Cook);
     if(craft!=null){var recipe=(LifeCookTable)B.Invoke("Tables.Cook",craft.Recipe);var ingredients=CookingIngredients(recipe,1);StopMotion();
      SaffronSubmit("cook-"+craft.Food,now,done=>B.Invoke("Cooking.Make",recipe.Id,1,ingredients,done));s.Reason="正在使用已有材料补足增产料理";return;
     }
     if(missing.Length>0){var row=missing[0];var priceType=((MethodInfo)B.Api("Saffron.Buy")).GetParameters()[3].ParameterType;
      SaffronSubmit("buy-"+row.ItemId,now,done=>B.Invoke("Saffron.Buy",row.GroupId,row.Id,1,Enum.ToObject(priceType,row.PriceType),row.PriceId,row.PriceCount,done));return;
     }
     if(!FootReady(s))return;
     var consume=new List<ItemDBInfo>();foreach(int id in SaffronCycle.Foods.Where(id=>FoodEnd(id)<=serverNow)){
      var item=SaleInventory().FirstOrDefault(x=>x.Id==id&&x.Count>0&&x.KeepFlag==0&&!x.IsDisableSlot);if(item==null)throw new InvalidOperationException("增产料理库存变化，未使用料理");var one=item.Clone();one.Count=1;consume.Add(one);
     }
     SaffronPhase("buff",now);if(consume.Count>0)SaffronSubmit("eat",now,done=>B.Invoke("Saffron.Eat",consume,done));return;
    }
    if(saffron.State=="buff"){
     saffron.BuffEnd=SaffronCycle.Foods.Select(FoodEnd).Min();
     if(saffron.BuffEnd-serverNow<SaffronCycle.HarvestSeconds(saffronInitial)*1000)throw new InvalidOperationException("三种增产效果未完整确认，未继续收割");
     saffronHadHarvest=true;SaffronPhase("harvest",now);
    }
    if(saffron.State=="harvest"){
     if(serverNow>=saffron.BuffEnd)throw new InvalidOperationException("增产效果已结束，藏红花未收完；已停止，未无加成收割");
     var remaining=farms.Where(f=>FieldSeed(f)==SaffronCycle.Seed&&!Empty(f)).Select(FieldCrop).ToArray();saffron.Harvested=saffronHarvestedBefore+saffronInitial-remaining.Length;
     if(remaining.Length==0){targetNode=null;SaffronPhase("potatoes",now);return;}
     if(targetNode==null||!remaining.Contains(targetNode)||!Harvestable(targetNode))targetNode=remaining.Where(n=>n!=null&&Harvestable(n)).OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
     if(targetNode==null||!Allowed(targetNode,now))throw new InvalidOperationException("藏红花收割路径变化，停止后续操作");
     Gather(s,now);return;
    }
    if(saffron.State=="potatoes"||saffron.State=="drain"){
     var potato=ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Potato),1);
     var grown=farms.Where(f=>FieldSeed(f)==SaffronCycle.Potato&&!Empty(f)).ToArray();
     var matureCrop=SaffronMatureCrop();
     int empty=farms.Count(Empty);
     bool fieldsKnown=farms.All(f=>CurrentDb(f)!=null&&ReadFarmReadiness(f)!=FarmReadiness.Unknown);
     // Growing crops of other kinds may occupy part of the land. They do not
     // prevent a potato batch from completing; unknown fields still do.
     if(SaffronCycle.ObservePotatoRound(saffronJournal,fieldsKnown?empty+grown.Length:0,empty,grown.Length)){
      saffronRoundPlanted=saffronJournal.PotatoRoundPlanted;saffronFilling=saffronJournal.Filling;saffron.PotatoRounds=saffronJournal.PotatoRounds;SaveSaffron();
     }
     long replantCost=(long)farms.Length*ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",SaffronCycle.Seed),1).Price;
     bool affordable=SaleCurrency()>=replantCost+(long)empty*potato.Price,capacity=SaffronCapacity(farms.Length,SaffronCycle.Potato);
     int maturePotatoes=grown.Count(f=>FieldCrop(f)!=null&&Harvestable(FieldCrop(f)));
     string action=SaffronCycle.PotatoAction(saffron.PotatoRounds,saffron.PotatoRoundLimit,saffronFilling,empty,grown.Length>0?maturePotatoes:matureCrop==null?0:1,grown.Length,affordable,capacity);
     if(action!="wait"){if(saffronResources)StopMotion();saffronResources=false;targetNode=matureCrop;}
     if(action=="wait"){
      if(saffronWaitEnd==0)saffronWaitEnd=serverNow+(potato.GrowthSeconds+30L)*1000;
      if(serverNow>saffronWaitEnd)throw new InvalidOperationException("土豆成熟状态未及时更新，已保留作物；可稍后重跑此环节");
      saffron.WaitUntil=saffronWaitEnd;s.Reason="等待本批土豆成熟，不提前补种藏红花";
     }else saffronWaitEnd=0;
     SaffronDecision(s,action,serverNow,empty,grown.Length);
     if(action=="plant"){
      if(!saffronFilling){saffronFilling=true;SaffronPhase("potatoes",now);SaveSaffron();}
      targetNode=null;SaffronPlant(s,c,now,SaffronCycle.Potato);return;
     }
     if(saffronFilling){saffronFilling=false;SaveSaffron();}
     if(action=="harvest"){if(!Allowed(targetNode,now))throw new InvalidOperationException("土豆收割路径暂不可达，保留作物");Gather(s,now);return;}
     if(action=="wait"){SaffronGatherResources(s,c,now);return;}
     if(action=="replant"){SaffronPhase("resources",now);return;}
     throw new InvalidOperationException(!affordable?"土豆轮次尚未完成，积分不足且需保留藏红花补种费用；已保留进度，其他日常继续":"土豆轮次尚未完成，库存空间或农田状态暂不允许种植；已保留进度，其他日常继续");
    }
    if(saffron.State=="resources"){
     if(SaffronGatherResources(s,c,now))return;
     StopMotion();targetNode=null;saffronResources=false;SaffronPhase("replant",now);
    }
    if(saffron.State=="replant"){
     targetNode=SaffronMatureCrop();
     if(targetNode!=null){Gather(s,now);return;}
     if(farms.Any(Empty)){SaffronPlant(s,c,now,SaffronCycle.Seed);return;}
     SaffronPhase("cook",now);
    }
    if(saffron.State=="cook"){
     if(!c.SaffronProcessing){SaffronEnd("completed","已完成种收并补种藏红花；自动料理与售卖未开启",now);return;}
     // Check on every batch, including a resumed session. Locked premium stock
     // remains untouched and is never used as fallback cooking material.
     var grades=((IEnumerable<int>)B.Invoke("Tables.ItemIds",215)).Select(id=>(LifeItemTable)B.Invoke("Tables.ItemById",id)).ToArray();
     if(grades.Any(x=>x==null||x.UniqueId!=215)||!grades.Any(x=>x.Grade==1))throw new InvalidOperationException("藏红花等级数据未就绪，未料理或售卖");
     var premium=SaffronCycle.PremiumSales(SaleStocks(),grades.ToDictionary(x=>x.Id,x=>x.Grade));
     if(premium.Length>0){SalesTick(s,c,now,premium.Take(32).ToArray(),"saffron-premium");return;}
     SaffronSetSeed(c,SaffronCycle.Seed);
     if(CookingCount(SaffronCycle.Recipe,1000)>0){c.Cooking=true;c.CookingBatch=1000;CookingTick(s,c,now);return;}
     SaffronPhase("sell",now);
    }
    if(saffron.State=="sell"){
     if(!c.SaffronProcessing){SaffronEnd("completed","已完成种收并补种藏红花；自动料理与售卖未开启",now);return;}
     var stocks=SaleStocks().Where(x=>x.Item==SaffronCycle.Dish&&x.Eligible&&!x.Locked).OrderBy(x=>x.Index).ToArray();long remaining=SaffronCycle.SellCount(stocks.Sum(x=>(long)x.Count));var lines=new List<SaleLine>();
     foreach(var stock in stocks){int count=(int)Math.Min(remaining,stock.Count);if(count>0){lines.Add(new SaleLine{Index=stock.Index,Item=stock.Item,Before=stock.Count,Count=count,Group=stock.Group,Row=stock.Row,Price=stock.Price});remaining-=count;}}
     if(lines.Count>0){SalesTick(s,c,now,lines.Take(32).ToArray(),"saffron");return;}
     if(CookingItemIds(215).Sum(id=>Convert.ToInt64(B.Invoke("Inventory.Count",id)))>=20)throw new InvalidOperationException("已补种并售卖成品，仍有藏红花原料；土豆或料理容量不足，已保留供下次制作");
     if(farms.Any(f=>!Empty(f)&&FieldSeed(f)!=SaffronCycle.Seed))throw new InvalidOperationException("已完成空田补种与料理；部分农田作物尚未成熟，保留生长，其他日常继续");
     SaffronEnd("completed",saffronHadHarvest?"藏红花已收割，增产土豆循环结束；已补种并制作料理，成品保留一份":"已补种藏红花，成熟后再运行收割",now);return;
    }
   }
   catch(Exception e){SaffronEnd(saffronHadHarvest||saffronSpent?"partial":"skipped",e.GetBaseException().Message,now);s.Reason=saffron.Reason;}
  }
 }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Proto.Net;
using Proto.Design.common;
using FieldEvent.Life;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private static RuntimeEngine current;private readonly Harmony patch=new Harmony("bd2.territory.inputs");private readonly TerritoryNetwork network=new TerritoryNetwork();
  private readonly int pid=System.Diagnostics.Process.GetCurrentProcess().Id;private Timer timer;private int ioBusy;private bool stopped;private bool handoffRequested;
  private volatile TerritoryControl control=new TerritoryControl();private volatile TerritorySnapshot latest=new TerritorySnapshot();
  private UIBase[] surfaces=new UIBase[0];private LifeFarmFieldObject[] farms=new LifeFarmFieldObject[0];private LifeGatheringObject[] nodes=new LifeGatheringObject[0];
  private PlayerController player;private PlayerMoveController move;private LifePlayerToolEquipmentController tool;private LifeFarmingUIPanel panel;private LifeFarmFieldObject targetFarm;private LifeGatheringObject targetNode;
  private readonly TerritoryWorkCycle workCycle=new TerritoryWorkCycle();
  private string lastWorkPhase="";
  private readonly GatheringProgress gathering=new GatheringProgress();private bool gatheringReposition;
  private readonly NavigationProgress navigation=new NavigationProgress();private readonly List<Vector3> triedDestinations=new List<Vector3>();
  private readonly InteractionProgress interaction=new InteractionProgress();
  private readonly Dictionary<string,TargetFailureMemory> failures=new Dictionary<string,TargetFailureMemory>();
  private long resourceSelections;private string activeTargetKey="";private Vector3 finalDestination;private bool bypassing;private int detours;private Collider blockingCollider;private long lastProbe,nextResourceSelection;
  private Component walkTarget;private Vector3 walkDestination;private long lastPathCheck;private string walkScene="";
  private readonly Dictionary<string,LifeWorldObjectDBInfo> worldObjects=new Dictionary<string,LifeWorldObjectDBInfo>();
  private readonly Dictionary<int,FarmEmptyProgress> emptyProgress=new Dictionary<int,FarmEmptyProgress>();
  private string farmScene="";
  private readonly PlantingPreviewProgress plantingPreview=new PlantingPreviewProgress();
  private long lastWorldRead;private int worldReplies=-1;private string lastFarmDiagnostic="";private bool protectResources;
  private readonly Dictionary<int,long> skip=new Dictionary<int,long>();
  private RecipeBatchProgress progress;private string progressPath="",owner="",fault="",account="",lastReason="";private long lastScan,lastCrops,lastTick,lastInput,entered;
  private string[] batchFields=new string[0];private bool ownsMove,ownsTool;private int farmStage;private long spent;private CropStock[] crops=new CropStock[0];
  internal void Start()
  {
   if(timer!=null)return;B.ValidateCompiledClient();B.Validate();network.Start();current=this;
   try{var pump=typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null);if(pump==null)throw new MissingMethodException("GameCameraManager.LateUpdate");patch.Patch(pump,postfix:new HarmonyMethod(typeof(RuntimeEngine),nameof(Frame)));patch.Patch((MethodInfo)B.Api("Gather.CanGather"),postfix:new HarmonyMethod(typeof(RuntimeEngine),nameof(MatureOnly)));patch.Patch((MethodInfo)B.Api("Gather.Near"),postfix:new HarmonyMethod(typeof(RuntimeEngine),nameof(OwnedAutoAim)));timer=new Timer(_=>IO(),null,0,100);}catch{Stop();throw;}
  }
  private static void Frame(){current?.Tick();}
  private void IO()
  {
   if(Interlocked.Exchange(ref ioBusy,1)!=0)return;
   try{if(stopped)return;var path=Path.Combine(LocalStorage.DataRoot,"control.json");try{using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(path)??new byte[0])){if(f.Length>16000)throw new IOException("控制文件过大");control=(TerritoryControl)new DataContractJsonSerializer(typeof(TerritoryControl)).ReadObject(f);}}catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Runtime.Serialization.SerializationException){}
    LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"latest.json"),latest);Loader.WriteStatus("active","");
   }catch(Exception e){LocalStorage.Log("io "+e.Message);}finally{Interlocked.Exchange(ref ioBusy,0);}
  }
  private void Tick()
  {
   if(stopped)return;long now=DateTime.UtcNow.Ticks;if(now-lastTick<TimeSpan.FromMilliseconds(100).Ticks)return;lastTick=now;
   var s=new TerritorySnapshot{ProcessId=pid,CapturedUtcTicks=now,Scene=SceneManager.GetActiveScene().name};
   try
   {
    if(now-lastScan>=TimeSpan.FromMilliseconds(popupProgress.Pending?100:1000).Ticks)
    {
     terrainGates=UnityEngine.Object.FindObjectsOfType<FieldEvent.Life.Chunk.LifePlaceableObject_ColliderGate>().Where(B.Active).ToArray();
     lastScan=now;surfaces=UnityEngine.Object.FindObjectsOfType<UIBase>().Where(B.Active).ToArray();farms=UnityEngine.Object.FindObjectsOfType<LifeFarmFieldObject>().Where(B.Active).ToArray();nodes=UnityEngine.Object.FindObjectsOfType<LifeGatheringObject>().Where(B.Active).ToArray();
    }
    var field=B.Read("Field.Instance",null);player=field==null?null:B.Read("Field.Player",field) as PlayerController;move=player==null?null:B.Read("Player.Move",player) as PlayerMoveController;
    var ctx=field==null?null:B.Read("Field.Context",field);tool=ctx==null?null:ctx.GetType().GetMethods().Single(m=>m.Name=="GetTickBase"&&m.IsGenericMethod&&m.GetParameters().Length==0).MakeGenericMethod(typeof(LifePlayerToolEquipmentController)).Invoke(ctx,null) as LifePlayerToolEquipmentController;
    terrainController=ctx==null?null:ctx.GetType().GetMethods().Single(m=>m.Name=="GetTickBase"&&m.IsGenericMethod&&m.GetParameters().Length==0).MakeGenericMethod(typeof(FieldEvent.Life.LifeChunkController)).Invoke(ctx,null) as FieldEvent.Life.LifeChunkController;
    s.Ready=player!=null&&move!=null&&tool!=null&&surfaces.Any(v=>v is AvatarLifeGameFieldDefaultUI||v is AvatarLifeHousingEditUI||v is AvatarLifeLevelUpPopupUI||v is AvatarLifeNewItemPopupUI);
    if(farmScene!=s.Scene){workCycle.Reset();farmScene=s.Scene;gatheringStands.Clear();emptyProgress.Clear();network.ClearEvidence();failures.Clear();skip.Clear();ResetLocalRecovery();}
    network.Reconcile(now);RefreshWorld(now);SettleReceipt();SettleCooking();SettleSales();s.SoldItems=sales==null?0:sales.ConfirmedItems;s.SaleCurrency=sales==null?0:sales.ConfirmedCurrency;s.SalesState=sales==null?"":sales.State;s.Cooked=cooking==null?0:cooking.ConfirmedTotal;s.CookingState=cooking==null?"":cooking.State;
    var c=control??new TerritoryControl();s.OwnerId=c.OwnerId;
    s.Trees=nodes.Count(n=>Harvestable(n)&&Kind(n)==1);s.Ores=nodes.Count(n=>Harvestable(n)&&Kind(n)==2);s.Mature=nodes.Count(n=>Harvestable(n)&&Kind(n)==3);s.RetryTargets=nodes.Count(n=>Harvestable(n)&&failures.TryGetValue(ResourceKey(n),out var retry)&&retry.Failures>0);
    s.GatherReplies=network.GatherReplies;s.ReceivedItems=network.ReceivedItems;s.Network=network.Last;s.Spent=spent;
    ReadCatalog(s,c,now);
    if(LayoutTick(s,c,now)){Publish(s);return;}
    if(handoffRequested){StopMove();s.Reason="等待当前操作结算后交接组件";Publish(s);return;}
    if(!c.Valid(now,pid)){Release();s.Reason="自动领地已停止";Publish(s);return;}
    if(owner!=c.OwnerId){Release();owner=c.OwnerId;fault="";network.ClearError();spent=0;account="";}
    if(!s.Ready){Release();s.Reason="请进入可走动的 Fantasia Territory 领地";Publish(s);return;}
    if(terrainController==null||terrainController.GetChunkManager()==null){StopMove();s.Reason="等待领地水域与桥梁数据";Publish(s);return;}
    if(c.Farming&&c.FixedCrop&&c.FixedSeedId<=0)throw new InvalidOperationException("请选择要固定种植的作物。");
    var user=(UserDBInfo)B.Read("Account.User",null);if(user==null||user.OwnerIndex<=0)throw new InvalidOperationException("尚未读取账号身份");string key=user.OwnerIndex.ToString();
    if(account!=key)
    {
     if(account.Length>0)throw new InvalidOperationException("游戏账号已切换，请停止后重新开启自动化");
     Release();network.ClearEvidence();emptyProgress.Clear();failures.Clear();skip.Clear();account=key;progressPath=Path.Combine(LocalStorage.DataRoot,"progress-"+key+".json");progress=LoadProgress(progressPath,key);LoadCooking(key);LoadSales(key);if(RecipeBatchPlanner.SwitchPlanting(progress,c.RecipeId,c.FixedCrop?c.FixedSeedId:0,false))SaveProgress(progress);crops=c.Farming?ReadPlantingCrops(progress.RecipeId,progress.FixedSeedId):new CropStock[0];
     SettleReceipt();
    }
    // Account / owner changes invalidate empty-field evidence. Count only AFTER that reset.
    RefreshFarmSnapshot(s);
    if(c.Farming&&(progress.RecipeId!=c.RecipeId||progress.FixedSeedId!=(c.FixedCrop?c.FixedSeedId:0))&&RecipeBatchPlanner.SwitchPlanting(progress,c.RecipeId,c.FixedCrop?c.FixedSeedId:0,farmStage>0)){SaveProgress(progress);targetFarm=null;lastCrops=0;}
    s.ActiveRecipeId=progress.RecipeId;s.ActiveFixedSeedId=progress.FixedSeedId;spent=progress.BudgetOwner==owner?progress.Spent:0;
    if(c.Farming&&now-lastCrops>TimeSpan.FromSeconds(1).Ticks){crops=ReadPlantingCrops(progress.RecipeId,progress.FixedSeedId);lastCrops=now;}s.Crops=crops;s.BatchSeedId=progress.SeedId;s.BatchPlanted=progress.Planted;s.BatchTotal=farmStage>0?batchFields.Length:progress.PlannedCount;s.CompletedBatches=progress.Batches;
    if(!c.Farming&&farmStage>0&&farmStage<4)Release();
    if(ownsTool&&targetNode!=null&&!Enabled(Kind(targetNode),c))StopMotion();
    s.Enabled=true;if(RecoverInterruptedOperations(s,now)){Publish(s);return;}
    bool pending=network.Waiting;if(network.Error.Length>0)fault=network.Error;if(fault.Length>0){Release();s.Error=fault;s.Reason="已暂停："+fault;Publish(s);return;}
    s.Enabled=true;
    if(c.Farming&&s.FarmState!=lastFarmDiagnostic){lastFarmDiagnostic=s.FarmState;LocalStorage.Log("农田状态 "+s.FarmState);}
    var popups=surfaces.Where(v=>B.Active(v)&&(bool)B.Read("Ui.Visible",v)&&!(bool)B.Invoke("Ui.IsHud",v)).ToArray();
    var blocker=popups.FirstOrDefault(v=>!(v is AvatarLifeLevelUpPopupUI)&&!(v is AvatarLifeNewItemPopupUI)&&!(farmStage>0&&v is AvatarLifeFarmingCroplistPopupUI));
    if(blocker!=null){StopMotion();s.Reason="等待关闭游戏弹窗："+blocker.GetType().Name;Publish(s);return;}
    var level=popups.FirstOrDefault(v=>v is AvatarLifeLevelUpPopupUI||v is AvatarLifeNewItemPopupUI);
    if(level!=null){ConfirmTerritoryPopup(level,s,now);Publish(s);return;}
    if(popupProgress.WaitForSettle(now)){StopMove();s.Reason="等待领地升级／解锁界面切换完成";Publish(s);return;}
    if(vehicleRequest.TimedOut(now))throw new InvalidOperationException("载具加载超过 8 秒，已暂停；等待游戏完成加载后可重新开始。");
    // Sale reconciliation must run before generic request waiting, otherwise a missing
    // sale callback prevents the recovery path itself from ever being reached.
    if(SalesBusy()&&!network.NonSalesWaiting&&!(bool)B.Read("Tool.Busy",tool)&&!ToolLoading()){SalesTick(s,c,now);Publish(s);return;}
    if(AwaitGathering(s,now,pending)){Publish(s);return;}
    RefreshLocalCacheContext();
    if(farmStage>0){AdvancePlant(s,now);Publish(s);return;}
    if(progress.PendingToken.Length>0){s.Reason="等待播种确认，必要时自动同步农田状态";Publish(s);return;}
    if(UnityEngine.Object.FindObjectsOfType<LifeFarmingUIPanel>().Any(B.Active)){StopMotion();s.Reason="请关闭手动播种界面后继续";Publish(s);return;}
    // Capacity checks run before continuing a resource, walking or starting another
    // gather pass. A new gather receipt invalidates the five-second idle probe cache.
    if(SalesBusy()){SalesTick(s,c,now);Publish(s);return;}
    if(CookingBusy()){CookingTick(s,c,now);Publish(s);return;}
    var surplus=UrgentSurplus(c,now);
    if(surplus.Length>0&&SalesTick(s,c,now,surplus)){Publish(s);return;}
    // Check owned walking every tick, independent of the configurable interval and target selection.
    if(ownsMove){ContinueWalk(s,now,c);Publish(s);return;}
    // Native tool motions may anchor the player. Observe the whole action above before any movement decision.
    var moveState=B.Read("Field.MoveState",field)?.ToString();if(moveState=="DontMove"||moveState=="Anchored"){s.Reason="等待游戏恢复角色移动";Publish(s);return;}
    if(now-lastInput<TimeSpan.FromMilliseconds(c.IntervalMs).Ticks){WarmLocalGrid();s.Reason=lastReason;Publish(s);return;}
    // Finish a damaged node before switching tasks; a partially damaged resource heals after inactivity.
    if(targetNode!=null&&Harvestable(targetNode)&&Enabled(Kind(targetNode),c)&&workCycle.AcceptsResource(ResourceKey(targetNode))){Gather(s,now);Publish(s);return;}targetNode=null;
    var harvestTargets=nodes.Where(n=>Kind(n)==3&&Harvestable(n)&&Enabled(Kind(n),c)&&Allowed(n,now)).Select(ResourceKey).ToArray();
    var resources=nodes.Where(n=>Kind(n)!=3&&Harvestable(n)&&Enabled(Kind(n),c)&&Allowed(n,now)).Select(ResourceKey).ToArray();
    var emptyFields=c.Farming&&crops.Length>0?farms.Where(f=>Empty(f)&&Allowed(f,now)).ToArray():new LifeFarmFieldObject[0];
    var previousPhase=workCycle.Phase;var phase=workCycle.Advance(harvestTargets,resources,emptyFields.Select(FarmKey));
    if(phase==TerritoryWorkPhase.Harvesting||phase==TerritoryWorkPhase.Gathering)
    {
     targetFarm=null;targetNode=SelectResource(c,now);
     if(targetNode!=null)Gather(s,now);else s.Reason=phase==TerritoryWorkPhase.Harvesting?"收完本批作物后集中补种":"本轮采集尚未结束，继续处理成熟资源";
     Publish(s);return;
    }
    if(phase==TerritoryWorkPhase.Planting)
    {
     if(previousPhase!=TerritoryWorkPhase.Planting){crops=ReadPlantingCrops(progress.RecipeId,progress.FixedSeedId);lastCrops=now;s.Crops=crops;}
     var crop=crops.Single(x=>x.SeedId==RecipeBatchPlanner.Choose(progress,crops));
     if(PlantingTransaction.WithinBudget(progress,owner,c.PlantingBudget,crop.Price,1))
     {
      if(targetFarm==null||!Empty(targetFarm)||!Allowed(targetFarm,now)||!workCycle.AcceptsField(FarmKey(targetFarm)))
       targetFarm=emptyFields.Where(f=>workCycle.AcceptsField(FarmKey(f))).OrderBy(f=>(f.GetFieldWorldCenter()-player.transform.position).sqrMagnitude).FirstOrDefault();
      if(targetFarm!=null){Plant(s,crop,now);Publish(s);return;}
     }
     // Nothing affordable in this planting pass. Do not wait on a field forever
     // or clear its native ownership; gathering stays available in the next pass.
     workCycle.Planted(emptyFields.Select(FarmKey));
     phase=workCycle.Advance(harvestTargets,resources,new string[0]);
    }
    targetFarm=null;
    if(phase==TerritoryWorkPhase.Processing)
    {
     if(CookingTick(s,c,now)){Publish(s);return;}
     if(SalesTick(s,c,now)){Publish(s);return;}
     workCycle.FinishProcessing();
    }
    if(c.Farming&&crops.Length>0&&!PlantingTransaction.WithinBudget(progress,owner,c.PlantingBudget,crops.Single(x=>x.SeedId==RecipeBatchPlanner.Choose(progress,crops)).Price,1))s.Reason="剩余预算不足继续播种；仍可采集和收获";
    else s.Reason=c.Farming&&s.EmptyFields==0?"等待空田或作物成熟；按实际农田分批播种":"等待作物自然成熟或资源刷新";
    if(targetNode==null)WarmLocalGrid();
    if(targetNode==null&&s.RetryTargets>0)s.Reason="待重试资源 "+s.RetryTargets+" 个，保留目标并定期重新检测";
    Publish(s);
   }catch(Exception e){fault=e.GetBaseException().Message;try{Release();}catch{}s.Enabled=false;s.Error=fault;s.Reason="已暂停："+fault;Publish(s);}
  }
  private void RefreshFarmSnapshot(TerritorySnapshot s){s.Fields=farms.Length;s.EmptyFields=farms.Count(Empty);s.FarmState=FarmDiagnostic();}
  private void Publish(TerritorySnapshot s){s.WorkPhase=(CookingBusy()||SalesBusy()?TerritoryWorkPhase.Processing:workCycle.Phase).ToString();s.WorkRemaining=CookingBusy()||SalesBusy()?0:workCycle.Remaining;if(s.Enabled&&s.WorkPhase!=lastWorkPhase){lastWorkPhase=s.WorkPhase;LocalStorage.Log("工作阶段 "+s.WorkPhase+" remaining="+s.WorkRemaining);}if(s.FarmState.Length==0)try{RefreshFarmSnapshot(s);}catch(Exception e){s.FarmState="农田状态暂不可读取："+e.GetBaseException().Message;}if(s.Reason!=lastReason){lastReason=s.Reason;LocalStorage.Log(s.Reason);}latest=s;}
  private static bool Enabled(int kind,TerritoryControl c)=>kind==1?c.Logging:kind==2?c.Mining:kind==3&&c.Farming;
  private bool Allowed(Component c,long now)=>(!skip.TryGetValue(c.GetInstanceID(),out var until)||now>=until)&&(!(c is LifeGatheringObject n)||!failures.TryGetValue(ResourceKey(n),out var f)||f.Available(now));
  private static string ResourceKey(LifeGatheringObject n)
  {var p=B.Read("Gather.Parent",n);return p==null?n.GetInstanceID().ToString():B.Read("Parent.Chunk",p)+":"+B.Read("Parent.Id",p)+":"+B.Read("Parent.X",p)+":"+B.Read("Parent.Y",p)+":"+B.Read("Gather.Id",n)+":"+B.Read("Gather.Index",n);}
  private IEnumerable<LifeGatheringObject> Detected(int kind)
  {
   var manager=B.Singleton(typeof(gamfs.Life.LifeManager));if(manager==null)return new LifeGatheringObject[0];
   var sets=(System.Collections.IDictionary)B.Read("Gather.Detected",manager);var key=Enum.ToObject(B.Type("FunctionKind"),kind);
   return sets!=null&&sets.Contains(key)?((IEnumerable<LifeGatheringObject>)sets[key]).Where(n=>n!=null).ToArray():new LifeGatheringObject[0];
  }
  private static void OwnedAutoAim(object __0,Vector3 __1,ref LifeGatheringObject __result)
  {
   var e=current;if(e==null||!e.protectResources)return;
   try{int kind=Convert.ToInt32(__0);if(kind<1||kind>3)return;var candidates=e.Detected(kind).Where(Harvestable).ToArray();
    __result=candidates.Contains(e.targetNode)?e.targetNode:candidates.OrderBy(n=>(n.transform.position-__1).sqrMagnitude).FirstOrDefault();
   }catch(Exception ex){__result=null;e.fault="采集目标核对失败："+ex.GetBaseException().Message;}
  }
  private LifeGatheringObject SelectResource(TerritoryControl c,long now)
  {
   if(now<nextResourceSelection)return null;nextResourceSelection=now+TimeSpan.FromSeconds(1).Ticks;
   var from=player.transform.position;
   var available=nodes.Where(n=>Harvestable(n)&&Enabled(Kind(n),c)).ToArray();
   foreach(var n in available){var key=ResourceKey(n);if(!failures.TryGetValue(key,out var memory))failures[key]=memory=new TargetFailureMemory();memory.Seen(now);}
   var eligible=available.Where(n=>Allowed(n,now)&&workCycle.AcceptsResource(ResourceKey(n))).ToArray();
   // Reserve every third selection for an overdue resource. Repeated crop/tree spawns cannot starve ores.
   var review=eligible.Where(n=>failures[ResourceKey(n)].ReviewDue(now)).OrderBy(n=>failures[ResourceKey(n)].WaitingSince).ThenBy(n=>Vector3.Distance(from,n.transform.position)).FirstOrDefault();
   var bestNode=review!=null&&resourceSelections%3==2?review:eligible.OrderBy(n=>Math.Max(0,Vector3.Distance(from,n.transform.position)-(Kind(n)==3?2:0))).FirstOrDefault();
   if(bestNode!=null)
   {
    resourceSelections++;activeTargetKey=ResourceKey(bestNode);failures[activeTargetKey].Selected(now);ResetLocalRecovery();travel.Reset();
    interaction.Select(bestNode.GetInstanceID());LocalStorage.Log("选定资源 target="+bestNode.GetInstanceID()+" kind="+Kind(bestNode)+" retry="+failures[activeTargetKey].Failures+" key="+activeTargetKey);
   }
   return bestNode;
  }
  private static int Kind(LifeGatheringObject n)=>Convert.ToInt32(B.Read("Gather.Function",n));
  private static bool FinalGrowth(LifeGatheringObject n)=>n!=null&&ResourceReadiness.Mature(n.GetStatus(),Convert.ToInt32(B.Read("Gather.FinalStage",n)));
  private static bool Harvestable(LifeGatheringObject n)=>B.Active(n)&&n.CanGatheringState()&&FinalGrowth(n)&&Convert.ToDouble(B.Read("Gather.Hp",n))>0;
  private static void MatureOnly(LifeGatheringObject __instance,ref bool __result)
  {
   // A mature target can share the weapon collider with a young tree. Protect incidental hits too,
   // only during a tool action initiated by this runtime; manual play keeps the original rule.
   var engine=current;if(engine==null||!engine.protectResources||!__result)return;
   try{int kind=Kind(__instance);if(kind==1||kind==2)__result=FinalGrowth(__instance);}
   catch(Exception e){__result=false;engine.fault="生长阶段核对失败："+e.GetBaseException().Message;}
  }
  private static LifeWorldObjectDBInfo Db(LifeFarmFieldObject f){var p=f.GetPlaceableObject();return p==null?null:(LifeWorldObjectDBInfo)B.Read("Farm.Db",p);}
  private void RefreshWorld(long now)
  {
   if(now-lastWorldRead<TimeSpan.FromSeconds(1).Ticks&&worldReplies==network.GatherReplies)return;
   lastWorldRead=now;worldReplies=network.GatherReplies;worldObjects.Clear();
   var world=B.Read("Inventory.World",null) as IEnumerable<LifeWorldObjectPlaceDBInfo>;if(world==null)return;
   foreach(var chunk in world)foreach(var obj in chunk.Object)worldObjects[TerritoryNetwork.Key(chunk.ChunkId,obj)]=obj;
  }
  private LifeWorldObjectDBInfo CurrentDb(LifeFarmFieldObject f)
  {if(!B.Active(f)||Db(f)==null)return null;return worldObjects.TryGetValue(FarmKey(f),out var db)?db:null;}
  private bool Empty(LifeFarmFieldObject f)=>ReadFarmReadiness(f)==FarmReadiness.Empty;
  private FarmReadiness ReadFarmReadiness(LifeFarmFieldObject f)
  {
   if(!B.Active(f))return FarmReadiness.Unknown;
   int id=f.GetInstanceID();if(!emptyProgress.TryGetValue(id,out var state))emptyProgress[id]=state=new FarmEmptyProgress();
   var db=CurrentDb(f);var crop=db==null?null:B.Read("Farm.Crop",f) as LifeGatheringObject;
   state.Observe(DateTime.UtcNow.Ticks,db!=null,db!=null&&db.InnerObject.Any(x=>x.Status>0),db!=null&&(bool)B.Read("Farm.Occupied",f),B.Active(crop)&&Convert.ToDouble(B.Read("Gather.Hp",crop))>0,db!=null&&f.IsPreviewCandidateAvailable(f.GetFieldWorldCenter()),network.Waiting,db!=null&&network.HarvestConfirmed(FarmKey(f)));
   return state.Status;
  }
  private string FarmDiagnostic()
  {
   int planted=0,unknown=0,occupied=0,cached=0,live=0,stale=0;
   foreach(var f in farms)
   {
    var db=CurrentDb(f);if(db==null){unknown++;continue;}
    if(db.InnerObject.Any(x=>x.Status>0)){planted++;if(Empty(f))stale++;}
    if((bool)B.Read("Farm.Occupied",f))occupied++;
    if(Convert.ToInt32(B.Read("Farm.Seed",f))>0)cached++;
    var crop=B.Read("Farm.Crop",f) as LifeGatheringObject;if(B.Active(crop)&&Convert.ToDouble(B.Read("Gather.Hp",crop))>0)live++;
   }
   return "共 "+farms.Length+" 格，世界缓存在种 "+planted+"（已核对空田 "+stale+"）"+"，原生占用 "+occupied+"，存活作物 "+live+"，缓存种子 "+cached+"，数据未载入 "+unknown;
  }
  private static string FarmKey(LifeFarmFieldObject f)=>TerritoryNetwork.Key(Convert.ToInt32(B.Read("Farm.Chunk",f.GetPlaceableObject())),Db(f));
  private CropStock[] ReadPlantingCrops(int recipeId,int fixedSeedId)
  {return fixedSeedId>0?new[]{ReadCrop((LifeCropSeedTable)B.Invoke("Tables.Crop",fixedSeedId),1)}:ReadCrops(recipeId);}
  private CropStock[] ReadCrops(int recipeId)
  {
   var recipe=(LifeCookTable)B.Invoke("Tables.Cook",recipeId);if(recipe==null||recipe.RequireItemUniqueId.Count==0||recipe.RequireItemUniqueId.Count!=recipe.RequireItemCount.Count||recipe.RequireItemCount.Any(v=>v<=0))throw new InvalidOperationException("料理配方不完整");
   var seeds=((IEnumerable<int>)B.Invoke("Tables.CropIds")).Select(id=>(LifeCropSeedTable)B.Invoke("Tables.Crop",id)).Where(v=>v!=null).ToArray();
   return recipe.RequireItemUniqueId.Select((id,i)=>{
    var matches=seeds.Where(v=>v.ItemGroupId==id).ToArray();if(matches.Length!=1)throw new InvalidOperationException("含无法自动种植的原料");return ReadCrop(matches[0],recipe.RequireItemCount[i]);
   }).ToArray();
  }
  private CropStock ReadCrop(LifeCropSeedTable seed,int required)
  {
   if(seed==null)throw new InvalidOperationException("当前客户端已无此作物，请重新选择。");
   int id=seed.ItemGroupId;var item=(LifeItemTable)B.Invoke("Tables.Item",id);var reward=(LifeGatheringObjectTable)B.Invoke("Tables.Harvest",id);var user=(LifeUserDBInfo)B.Read("Inventory.User",null);
   if(item==null||seed.PriceType!=64||seed.PriceId!=0||seed.PriceCount<0||reward==null||reward.RewardItemUniqueId.Count!=1||reward.RewardItemUniqueId[0]!=id||reward.RewardItemCount.Count!=1||reward.RewardItemCount[0]<=0)throw new InvalidOperationException("作物价格或收获表已变化");
   if(user?.LifeCharLevelInfo==null||user.LifeCharLevelInfo.FarmingLevel<item.UnlockContentLevel)throw new InvalidOperationException("当前账号尚未解锁配方作物");
   var itemIds=(IEnumerable<int>)B.Invoke("Tables.ItemIds",id);long stock=itemIds.Sum(v=>Convert.ToInt64(B.Invoke("Inventory.Count",v)));
   var loadedFields=farms.Where(f=>Db(f)!=null).GroupBy(FarmKey).ToDictionary(g=>g.Key,g=>g.First());
   int growing=worldObjects.Count(entry=>((LifeBuildingObjectTable)B.Invoke("Tables.Building",entry.Value.ObjectId))?.FunctionType==3&&entry.Value.InnerObject.Any(n=>n.ObjectId==seed.Id&&n.Status>0)&&(!loadedFields.TryGetValue(entry.Key,out var field)||!Empty(field)));
   return new CropStock{SeedId=seed.Id,IngredientId=id,Name=(string)B.Invoke("Text.Name",item.NameTextId),Required=required,Yield=reward.RewardItemCount[0],Inventory=stock,Growing=growing,Price=seed.PriceCount,GrowthSeconds=seed.TotalGrowthTime};
  }
  private void Plant(TerritorySnapshot s,CropStock crop,long now)
  {
   s.Target=crop.Name+" · 按当前农田批量播种";
   if(!targetFarm.IsPlayerInInteractionRange(player.transform.position)){Approach(targetFarm,targetFarm.GetFieldWorldCenter(),s,now,1.0f);return;}
   StopMotion();if(!FootReady(s))return;navigation.Reset();triedDestinations.Clear();B.InvokeOn("Farm.Open",targetFarm);panel=UnityEngine.Object.FindObjectsOfType<LifeFarmingUIPanel>().FirstOrDefault(B.Active);if(panel==null)throw new InvalidOperationException("播种界面未打开");
   // Take ownership immediately, so a failed preview closes the panel instead of leaving manual UI behind.
   farmStage=1;entered=lastInput=now;batchFields=new string[0];plantingPreview.Reset();
   // The multi button is hidden on the seed-list page; select a seed before clicking it.
   panel.OnClickSelectedCropSeed(crop.SeedId);
   if(!(bool)B.Read("Panel.Multi",panel))
   {
    var toggle=(ButtonOnOffComponent)B.Read("Panel._multiCropSeedButtonObj",panel);
    var button=(GameObject)B.Read(toggle.IsOn()?"Button.On":"Button.Off",toggle);
    if(!B.Active(button)||!panel.IsClickUI(button))throw new InvalidOperationException("批量播种开关尚未可用");
   }
   if(!(bool)B.Read("Panel.Multi",panel))throw new InvalidOperationException("未能开启游戏原生批量播种");
   s.Reason="准备批量播种 "+crop.Name+"；正在读取实际预览数量";
  }
  private LifeFarmFieldObject[] PlantedPreviews()=>farms.Where(f=>Db(f)!=null&&batchFields.Contains(FarmKey(f))&&(bool)B.Read("Farm.Occupied",f)&&Convert.ToInt32(B.Read("Farm.Seed",f))>0).ToArray();
  private void ValidatePlantedPreview()
  {
   var previews=PlantedPreviews();
   if(!PlantingTransaction.SameKeys(batchFields,previews.Select(FarmKey).ToArray())||previews.Any(f=>Convert.ToInt32(B.Read("Farm.Seed",f))!=progress.SeedId))throw new InvalidOperationException("批量预览必须是同一种作物的全部原定空田，已停止付款");
  }
  private void AdvancePlant(TerritorySnapshot s,long now)
  {
   s.Reason="整批播种确认 · "+farmStage;s.Target=batchFields.Length>0?"同种作物 × "+batchFields.Length+" · 一次提交":"正在读取本片农田";
   if(farmStage<4&&now-entered>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("播种阶段未推进，请检查游戏界面；不会重复支付"+(plantingPreview.Reason.Length>0?"；"+plantingPreview.Reason:""));
   if(now-lastInput<TimeSpan.FromMilliseconds(Math.Max(350,control.IntervalMs)).Ticks)return;
   if(farmStage==1)
   {
    if(panel==null||!B.Active(panel))throw new InvalidOperationException("播种界面意外关闭");
    var group=B.Singleton(B.Type("FarmGroup"));if(group==null)throw new InvalidOperationException("农田分组尚未就绪");
    var preview=((IEnumerable<LifeFarmFieldObject>)B.InvokeOn("FarmGroup.Preview",group)).ToArray();
    var keys=preview.Select(f=>B.Active(f)&&Db(f)!=null?FarmKey(f):"").ToArray();
    var readiness=preview.Select(ReadFarmReadiness).ToArray();
    var decision=plantingPreview.Evaluate((bool)B.Read("Panel.Multi",panel),keys,readiness);
    if(decision==PlantingPreviewDecision.Refresh)
    {
     // Ask the game's existing preview events to recompute once; no movement or direct field edits.
     if(targetFarm==null||!B.Active(targetFarm))throw new InvalidOperationException("播种目标已消失");
     targetFarm.ShowNearestFarmCellPreview(true);targetFarm.RefreshNearestFarmCellPreview();
     lastInput=now;s.Reason=plantingPreview.Reason;return;
    }
    if(decision==PlantingPreviewDecision.Wait){s.Reason=plantingPreview.Reason;return;}
    if(decision==PlantingPreviewDecision.Reject){LocalStorage.Log("播种预检失败："+plantingPreview.Reason+"；"+s.FarmState);throw new InvalidOperationException(plantingPreview.Reason);}
    batchFields=keys;
    s.BatchTotal=keys.Length;
    var crop=crops.Single(x=>x.SeedId==progress.SeedId);
    if(!PlantingTransaction.WithinBudget(progress,owner,control.PlantingBudget,crop.Price,keys.Length)||!(bool)B.Read("Panel.CanAfford",panel))
    {
     // Do not reopen this whole group on every tick, or shrink the game's preview after quoting it.
     foreach(var f in preview)skip[f.GetInstanceID()]=now+TimeSpan.FromSeconds(60).Ticks;
     CloseOwnedPanel();lastInput=now;s.Reason="当前 "+keys.Length+" 格播种费用超出余额或剩余预算；继续处理其他农田、采集和收获";return;
    }
    if(!panel.IsClickUI((GameObject)B.Read("Panel._cropButtonObj",panel)))throw new InvalidOperationException("游戏未接受整批预览");
    farmStage=2;lastInput=now;return;
   }
   if(farmStage==2)
   {
    ValidatePlantedPreview();
    panel.IsClickUI((GameObject)B.Read("Panel._completeButtonObj",panel));farmStage=3;lastInput=now;return;
   }
   if(farmStage==3)
   {
    var pop=UnityEngine.Object.FindObjectsOfType<AvatarLifeFarmingCroplistPopupUI>().FirstOrDefault(B.Active);if(pop==null)return;
    var list=((IEnumerable<int>)B.Read("Popup.Seeds",pop)).ToArray();
    if(list.Length!=batchFields.Length||list.Length==0||list.Any(seed=>seed!=progress.SeedId))throw new InvalidOperationException("确认清单必须与本批农田数量和作物一致，已停止付款");
    ValidatePlantedPreview();
    var b=(ButtonOnOffComponent)B.Read("Popup.Ok",pop);var button=(GameObject)B.Read("Button.On",b);
    if(!b.IsOn()||!B.Active(button))throw new InvalidOperationException("游戏未允许支付整批播种费用");
    var crop=crops.Single(x=>x.SeedId==progress.SeedId);
    var intent=PlantingTransaction.Begin(progress,batchFields,crop.Price,owner,control.PlantingBudget,Guid.NewGuid().ToString("N"));
    intent.PendingSubmittedTicks=now;SaveProgress(intent);network.Arm(intent);farmStage=4;lastInput=now;
    LocalStorage.Log("提交整批播种 token="+intent.PendingToken+" fields="+intent.PendingKeys.Length+" seed="+intent.PendingSeed+" cost="+intent.PendingCost);
    pop.OnClickUI(button);return;
   }
   if(farmStage==4){s.Reason="等待 "+progress.PlannedCount+" 格整批播种回执";return;}
  }

  private bool ToolLoading()
  {
   // Busy is set only after asynchronous equipment loading has completed.
   return new[]{"Tool.LoadingBase","Tool.LoadingParts"}.Any(key=>{var list=B.Read(key,tool);return list!=null&&Convert.ToInt32(list.GetType().GetProperty("Count").GetValue(list,null))>0;});
  }
  private bool AwaitGathering(TerritorySnapshot s,long now,bool requestPending)
  {
   bool busy=(bool)B.Read("Tool.Busy",tool),loading=ToolLoading(),wasPending=gathering.Pending;
   double hp=targetNode==null?(wasPending?0:double.NaN):Convert.ToDouble(B.Read("Gather.Hp",targetNode));
   bool ready=gathering.Observe(now,busy,loading,requestPending,hp,network.GatherReplies);
   if(wasPending&&!gathering.Pending)
   {
    // A naturally completed action releases ownership without sending CancelAutoUseTool.
    ownsTool=false;protectResources=false;
    if(hp<=0&&activeTargetKey.Length>0)workCycle.Gathered(activeTargetKey);
    if(gathering.TargetProgressed)
    {gatheringStands.Succeeded(gathering.Target);if(activeTargetKey.Length>0)failures.Remove(activeTargetKey);}
    else if(targetNode!=null&&Harvestable(targetNode))
    {gatheringStands.Record(gathering.Target,Point(gatheringStand),now);LocalStorage.Log("记录空挥站位 target="+gathering.Target+" stand="+gatheringStand.ToString("F3"));}
    LocalStorage.Log("采集动作结束 target="+gathering.Target+" progress="+gathering.Progressed+" targetProgress="+gathering.TargetProgressed+" misses="+gathering.Misses+" hp="+hp+" replies="+network.GatherReplies);
   }
   if(ready)return false;
   if(gathering.Stalled(now))throw new InvalidOperationException("工具动作超过 45 秒无命中或收获回执，已暂停；请检查角色和网络状态");
   if(targetNode!=null)s.Target=(Kind(targetNode)==1?"砍树":Kind(targetNode)==2?"采矿":"收获作物")+" · "+targetNode.GetInstanceID();
   s.Reason=requestPending?"等待服务器确认采集／播种":loading?"等待工具加载":busy?"等待采集动作自然完成":"等待采集动作收尾";
   return true;
  }
  private void FaceGatheringTarget()
  {
   // SetRotationForce also calls SetRotateAndAnimation. Never use it during a tool animation.
   var direction=targetNode.transform.position-player.transform.position;direction.y=0;
   if(direction.sqrMagnitude<=.0001f)return;
   direction.Normalize();string previousMode=B.Read("Player.MoveType",move).ToString();
   // Navigation's direction getter ignores the manual-direction setter, even after StopMove.
   if(previousMode!="CharController")B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","CharController"));
   B.InvokeOn("Player.Face",player,direction);
   var avatar=B.Read("Player.AvatarRoot",move) as Transform;
   var logical=(Vector3)B.Read("Player.Direction",move);logical.y=0;
   double modelError=avatar==null?double.NaN:Vector3.Angle(avatar.forward,direction);
   LocalStorage.Log("采集开始转向 target="+targetNode.GetInstanceID()+" kind="+Kind(targetNode)+" distance="+(targetNode.transform.position-player.transform.position).magnitude.ToString("F2")+" mode="+previousMode+"->"+B.Read("Player.MoveType",move)+" logical="+Vector3.Angle(logical,direction).ToString("F1")+" model="+modelError.ToString("F1"));
   // Facing is best effort; hit damage and server receipts decide whether a move is needed.
  }
  private void Gather(TerritorySnapshot s,long now)
  {
   int kind=Kind(targetNode);s.Target=(kind==1?"砍树":kind==2?"采矿":"收获作物")+" · "+targetNode.GetInstanceID();
   interaction.Select(targetNode.GetInstanceID());
   if(gathering.Target!=targetNode.GetInstanceID()){gathering.Select(targetNode.GetInstanceID());gatheringReposition=false;activeTargetKey=ResourceKey(targetNode);}
   if(!gatheringReposition&&gathering.NeedsReposition)
   {
    if(gathering.Repositions>=2){SkipWalkTarget(targetNode,s,now,"gather_no_hit_after_reposition");return;}
    StopMotion();gathering.Reposition();gatheringReposition=true;
    LocalStorage.Log("采集空挥已结算，改换站位 target="+targetNode.GetInstanceID()+" attempt="+gathering.Repositions+" from="+player.transform.position.ToString("F3"));
   }
   if(gatheringReposition)
   {Approach(targetNode,targetNode.transform.position,s,now,.7f,gathering.Repositions);return;}
   bool detected=Detected(kind).Contains(targetNode)&&CanGatherAt(targetNode,player.transform.position);
   var decision=interaction.Decide(now,detected,Harvestable(targetNode));
   if(decision==InteractionDecision.WaitForDetection){s.Reason="已到位，等待游戏更新采集范围";return;}
   if(decision!=InteractionDecision.UseTool)
   {
    if(decision==InteractionDecision.Reposition)LocalStorage.Log("到位未检测 target="+targetNode.GetInstanceID()+" detected="+string.Join(",",Detected(kind).Select(n=>n.GetInstanceID().ToString()).ToArray())+" distance="+Vector3.Distance(player.transform.position,targetNode.transform.position).ToString("F2"));
    Approach(targetNode,targetNode.transform.position,s,now,.7f);return;
   }
   StopMove();if(!FootReady(s))return;double hp=Convert.ToDouble(B.Read("Gather.Hp",targetNode));
   var direction=targetNode.transform.position-player.transform.position;direction.y=0;
   if(direction.sqrMagnitude<=.0001f){StopMotion();gathering.Reposition();gatheringReposition=true;s.Reason="与采集点重叠，重新走位";return;}
   navigation.Reset();triedDestinations.Clear();FaceGatheringTarget();
   int group=B.EnumValue("ToolKind",kind==1?"LoggingTool":kind==2?"MiningTool":"FarmingTool");var owned=(LifeToolDBInfo)B.Invoke("Inventory.Tool",group);int id=owned?.Id??1;
   var ev=Activator.CreateInstance(TerritoryIl.InstanceCheckType((MethodInfo)B.Api("Tool.Event")),new object[]{group,id});
   gatheringStand=player.transform.position;gathering.Issued(now,hp,network.GatherReplies);protectResources=true;B.InvokeOn("Tool.Event",tool,ev);ownsTool=true;
   gathering.Observe(now,(bool)B.Read("Tool.Busy",tool),ToolLoading(),network.Waiting,hp,network.GatherReplies);
   lastInput=now;s.Reason="已面向目标，使用工具采集";
  }
  private NavMeshAgent NavAgent()=>move==null?null:B.Read("Player.NavAgent",move) as NavMeshAgent;
  private static double PathLength(NavMeshPath path)
  {double length=0;var corners=path.corners;for(int i=1;i<corners.Length;i++)length+=Vector3.Distance(corners[i-1],corners[i]);return length;}
  private bool FindStand(Component target,Vector3 center,float radius,Vector3 from,NavMeshQueryFilter filter,bool retry,out Vector3 chosen,out double best)
  {
   chosen=Vector3.zero;best=double.PositiveInfinity;
   foreach(var point in StandCandidates(target,center,radius,retry))
   {
    NavMeshHit hit;var path=new NavMeshPath();
    if(!NavMesh.SamplePosition(point,out hit,.3f,filter)||FlatDistance(point,hit.position)>.08f)continue;
    var standing=hit.position+Vector3.up*RootLift;if(!CanGatherAt(target,standing)||!StandClear(standing))continue;
    if(!NavMesh.CalculatePath(from,hit.position,filter,path)||path.status!=NavMeshPathStatus.PathComplete||!SurfacePathAllowed(path.corners))continue;
    double length=PathLength(path),direct=Vector3.Distance(from,hit.position);
    if(length>Math.Max(12,direct*4+4)||length>=best)continue;best=length;chosen=hit.position;
   }
   return !double.IsInfinity(best);
  }
  private void CaptureFailure(string reason)
  {if(DateTime.UtcNow.Ticks-lastProbe<TimeSpan.FromSeconds(5).Ticks)return;lastProbe=DateTime.UtcNow.Ticks;SceneProbe.Capture(reason);}
  private static NavMeshQueryFilter NavFilter(NavMeshAgent agent)=>new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
  private void SkipWalkTarget(Component target,TerritorySnapshot s,long now,string reason)
  {
   int id=target==null?navigation.Target:target.GetInstanceID();
   LocalStorage.Log("目标保留待重试 target="+id+" reason="+reason+" attempts="+navigation.Attempts+" remaining="+navigation.BestRemaining.ToString("F2"));
   CaptureFailure(reason);StopMove();ResetLocalRecovery();if(gathering.Target==id&&!gathering.Pending)gathering.Reset();long until=now+TimeSpan.FromSeconds(15).Ticks;
   if(target is LifeGatheringObject resource){var key=ResourceKey(resource);if(!failures.TryGetValue(key,out var memory))failures[key]=memory=new TargetFailureMemory();memory.Failed(now);until=memory.Until;}
   skip[id]=until;
   if(target is LifeFarmFieldObject)targetFarm=null;else targetNode=null;
   gatheringReposition=false;navigation.Reset();interaction.Reset();triedDestinations.Clear();s.Reason="目标保留在重试队列，"+((until-now)/TimeSpan.TicksPerSecond)+" 秒后再检查";
  }
  private void ContinueWalk(TerritorySnapshot s,long now,TerritoryControl c)
  {
   s.Reason=localMoving?"沿实体通路绕行，保持原目标":ownsVehicle?"使用载具赶路":"移动至目标";s.Target=(walkTarget is LifeGatheringObject resource?(Kind(resource)==1?"砍树":"采矿／收获"):"播种")+" · "+navigation.Target;
   if(walkScene!=s.Scene)throw new InvalidOperationException("寻路期间场景发生变化，已停止移动；请确认领地位置后重新开始");
   if(walkTarget==null||(walkTarget is LifeGatheringObject node&&(!Harvestable(node)||!Enabled(Kind(node),c)))||(walkTarget is LifeFarmFieldObject farm&&(!c.Farming||!Empty(farm))))
   {StopMove();navigation.Reset();triedDestinations.Clear();s.Reason="移动目标已变化，重新选择";return;}
   ContinueRoute(s,now,c);
  }
  private void ContinueNavMesh(TerritorySnapshot s,long now,TerritoryControl c)
  {
   if(now-lastPathCheck<TimeSpan.FromMilliseconds(300).Ticks)return;lastPathCheck=now;
   var from=player.transform.position;var agent=NavAgent();
   if(!StandClear(walkDestination+Vector3.up*RootLift)){if(!BeginLocalRoute(walkTarget,s,now,"destination_obstructed"))SkipWalkTarget(walkTarget,s,now,"destination_obstructed");return;}
   bool arrived=FlatDistance(from,walkDestination)<=.12f&&Math.Abs(from.y-walkDestination.y)<=.45f&&StandClear(from);
   if(!bypassing)arrived=GatherStandReached(walkTarget,from,arrived,walkTarget is LifeGatheringObject nearby&&Detected(Kind(nearby)).Contains(nearby));
   if(arrived&&bypassing){walkDestination=finalDestination;bypassing=false;B.InvokeOn("Player.StartMove",player);if(!(bool)B.InvokeOn("Player.SetMoveNav",move,walkDestination,null,true)){SkipWalkTarget(walkTarget,s,now,"bypass_resume_failed");return;}arrived=false;}
   var path=new NavMeshPath();bool valid=agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh&&NavMesh.CalculatePath(from,walkDestination,NavFilter(agent),path)&&path.status==NavMeshPathStatus.PathComplete;
   if(valid&&!SurfacePathAllowed(path.corners))
   {move.ClearMove();move.StopMove();agent.ResetPath();if(!BeginLocalRoute(walkTarget,s,now,"water_requires_bridge"))SkipWalkTarget(walkTarget,s,now,"water_requires_bridge");return;}
   double remaining=valid?PathLength(path):double.PositiveInfinity;
   if(bypassing&&agent!=null){var rest=new NavMeshPath();if(NavMesh.CalculatePath(walkDestination,finalDestination,NavFilter(agent),rest)&&rest.status==NavMeshPathStatus.PathComplete)remaining+=PathLength(rest);else valid=false;}
   var corners=valid?path.corners:new Vector3[0];Collider obstacle=null;
   if(!arrived&&corners.Length>1){var d=corners[1]-from;d.y=0;var ahead=from+Vector3.ClampMagnitude(d,1.0f);obstacle=Obstacle(from,ahead);}
   if(obstacle!=null)
   {
    // Plan around live solid geometry; visuals and placement footprints are not obstacles.
    blockingCollider=obstacle;LocalStorage.Log("实体阻挡，立即规划绕行 target="+navigation.Target+" collider="+obstacle.name+" type="+obstacle.GetType().Name+" at="+obstacle.bounds.center);CaptureFailure("physical_obstacle");
    agent.isStopped=true;CancelVehicle();s.Reason="前方路径被占用，规划绕行";
    if(BeginLocalRoute(walkTarget,s,now,"physical_obstacle"))return;
    SkipWalkTarget(walkTarget,s,now,"physical_obstacle_no_local_route");return;
   }


   // Native stoppingDistance can grow when stuck. Do not accept its 'arrived' flag across a fence.
   var decision=navigation.Observe(now,remaining,valid,arrived);
   if(decision==NavigationDecision.Moving){UpdateVehicle(now,c,remaining);if(player.IsGetOnVehicle())s.Reason="使用载具赶路";return;}
   if(decision!=NavigationDecision.Moving&&decision!=NavigationDecision.Arrived&&BeginLocalRoute(walkTarget,s,now,navigation.Reason))return;
   var target=walkTarget;string reason=navigation.Reason;
   LocalStorage.Log("寻路结果 target="+navigation.Target+" decision="+decision+" reason="+reason+" remaining="+remaining.ToString("F2")+" best="+navigation.BestRemaining.ToString("F2"));
   StopMove();
   if(decision==NavigationDecision.Arrived){interaction.Select(navigation.Target);interaction.Arrived(now);gatheringReposition=false;s.Reason="已靠近目标，准备操作";return;}
   if(decision==NavigationDecision.Skip){SkipWalkTarget(target,s,now,reason);return;}
   s.Reason="路径未推进，尝试另一个可达落脚点";
  }
  private bool Approach(Component target,Vector3 center,TerritorySnapshot s,long now,float radius,int angleStep=0)
  {
   if(navigation.Target!=target.GetInstanceID()){navigation.Select(target.GetInstanceID());triedDestinations.Clear();}travel.Select(target.GetInstanceID());
   return StartRoute(target,center,s,now,radius);
  }
  private bool ApproachNavMesh(Component target,Vector3 center,TerritorySnapshot s,long now,float radius)
  {
   if(navigation.Attempts>=NavigationProgress.MaxAttempts){if(!BeginLocalRoute(target,s,now,"approach_exhausted"))SkipWalkTarget(target,s,now,"approach_exhausted");return false;}
   StopMotion();
   // Keep the normal mode-switch / start / SetMoveNav sequence, without Field.Walk's ResumeNav callback.
   // Pause the previous quest route; stopping territory automation must not resume it and run elsewhere.
   var quest=B.Singleton(typeof(QuestNavigationManager));if(quest!=null)B.InvokeOn("Quest.Pause",quest);
   ownsMove=true;walkTarget=target;walkScene=s.Scene;
   B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","Navigation"));
   var agent=NavAgent();
   if(agent==null||!agent.isActiveAndEnabled||!agent.isOnNavMesh){if(!BeginLocalRoute(target,s,now,"nav_agent_unavailable"))SkipWalkTarget(target,s,now,"nav_agent_unavailable");return false;}
   var from=player.transform.position;double best;Vector3 chosen;
   FindStand(target,center,radius,from,NavFilter(agent),true,out chosen,out best);
   if(double.IsInfinity(best)){if(!BeginLocalRoute(target,s,now,"no_bounded_complete_path"))SkipWalkTarget(target,s,now,"no_bounded_complete_path");return false;}
   navigation.Begin(now,best);walkDestination=finalDestination=chosen;bypassing=false;detours=0;blockingCollider=null;triedDestinations.Add(chosen);lastPathCheck=now;
   B.InvokeOn("Player.StartMove",player);
   if(!(bool)B.InvokeOn("Player.SetMoveNav",move,chosen,null,true)){if(!BeginLocalRoute(target,s,now,"native_path_rejected"))SkipWalkTarget(target,s,now,"native_path_rejected");return false;}
   LocalStorage.Log("寻路开始 target="+navigation.Target+" attempt="+navigation.Attempts+" from="+from+" to="+chosen+" route="+best.ToString("F2"));
   lastInput=now;s.Reason="移动至目标";return false;
  }
  private void StopMove()
  {
   ClearLocalMotion();StopEscape();CancelVehicle();
   if(ownsMove&&move!=null)
   {
    move.ClearMove();move.StopMove();var agent=NavAgent();if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh)agent.ResetPath();
    B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","CharController"));
   }
   if(ownsMove)navigation.CancelAttempt();ownsMove=false;walkTarget=null;
  }
  private void StopMotion(){StopMove();if(ownsTool&&tool!=null)tool.CancelAutoUseTool();ownsTool=false;if(tool==null||!(bool)B.Read("Tool.Busy",tool)&&!ToolLoading())protectResources=false;}
  private void CloseOwnedPanel()
  {
   var user=B.Read("Account.User",null) as UserDBInfo;
   if(panel!=null&&B.Active(panel)&&progress!=null&&user!=null&&user.OwnerIndex.ToString()==progress.Account)
   {
    if(string.IsNullOrEmpty(progress.PendingToken))foreach(var popup in UnityEngine.Object.FindObjectsOfType<AvatarLifeFarmingCroplistPopupUI>().Where(B.Active)){popup.SetActiveForce(false);popup.CloseForceUI();}
    panel.OnCloseFarmingUI();
   }
   panel=null;farmStage=0;targetFarm=null;batchFields=new string[0];
  }
  private void Release()
  {
   workCycle.Reset();lastWorkPhase="";StopMotion();ResetLocalRecovery();if(farmStage>0&&farmStage<4)CloseOwnedPanel();
   // A submitted operation outlives its UI phase. Keep its persisted intent until an authoritative result arrives.
   farmStage=0;targetFarm=null;targetNode=null;gathering.Reset();gatheringReposition=false;navigation.Reset();interaction.Reset();triedDestinations.Clear();network.ForgetUnsentArm();
  }
  private void SettleReceipt()
  {
   var reply=network.LastPlantReply;if(progress==null||reply==null||!reply.RequestMatches||(!reply.Accepted&&!reply.Rejected))return;
   var next=PlantingTransaction.Settle(progress,reply);if(ReferenceEquals(next,progress))return;
   SaveProgress(next);workCycle.Planted(reply.Keys);CloseOwnedPanel();lastInput=DateTime.UtcNow.Ticks;lastCrops=0;lastWorldRead=0;RefreshWorld(lastInput);
  }
  private static RecipeBatchProgress LoadProgress(string path,string key)
  {
   if(!File.Exists(path))return new RecipeBatchProgress{Account=key};using(var f=File.OpenRead(path)){var p=(RecipeBatchProgress)new DataContractJsonSerializer(typeof(RecipeBatchProgress)).ReadObject(f);return RecipeBatchPlanner.Restore(p,key);}
  }
  private void SaveProgress(RecipeBatchProgress next)
  {LocalStorage.WriteJsonAtomically(progressPath,next);progress=next;}
  internal void PrepareHandoff(){handoffRequested=true;control=new TerritoryControl();StopMove();}
  internal string HandoffBusy(){
   if(ioBusy!=0)return "snapshot writer";
   if(network.NativeBusy||network.NonSalesWaiting||Convert.ToInt32(B.Read("Network.QueuedHarvest",null))>0)return "harvest response";
   if(tool!=null&&((bool)B.Read("Tool.Busy",tool)||ToolLoading()))return "native tool action";
   // Unresolved planting is durable and reconciled by the replacement component.
   if(vehiclePending)return "vehicle transition";
   // Unknown sales are durable and recoverable by the replacement component once native requests end.
   if(LayoutBusy())return "layout receipt";
   return "";
  }
  internal void Stop(){Release();stopped=true;timer?.Dispose();timer=null;current=null;network.Dispose();patch.UnpatchAll("bd2.territory.inputs");}
 }
}

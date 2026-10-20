using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Proto.Net;
namespace BD2Territory.Runtime
{
 // Observe the original Send and callbacks; no packet replacement, forged requests, or reward mutation.
 internal sealed class TerritoryNetwork:IDisposable
 {
  private sealed class Pending {public string RecoveryAccount;public LifeInfoRequest Info;public SalesProgress InfoIntent;public LifeShopSellRequest Sale;public SalesProgress SaleIntent;public LifeCookingRequest Cook;public CookingProgress CookIntent;internal MethodInfo Method;internal object Target;internal DateTime At;internal string Kind;internal LifeWorldObjectPositionSaveRequest Position;internal LifeWorldObjectPlaceSaveRequest Place;internal LifeSeedingRequest Seed;internal LifeWorldObjectGatheringRequest Gather;internal int Sequence;internal RecipeBatchProgress Intent;}
  private static TerritoryNetwork current;
  private readonly Harmony patch=new Harmony("bd2.territory.network");private readonly object sync=new object();
  private readonly HashSet<string> harvested=new HashSet<string>();
  internal bool HarvestConfirmed(string key){lock(sync)return harvested.Contains(key);}
  internal void ClearEvidence(){lock(sync)harvested.Clear();}
  private Dictionary<MethodBase,string> handlers;private readonly List<Pending> pending=new List<Pending>();
  internal volatile string LayoutRejectedKey="";
  internal volatile string Error="",Last="尚无领地请求";internal int GatherReplies,ReceivedItems;internal volatile PlantingReply LastPlantReply;private RecipeBatchProgress armed;
  private string recoveryArm="";private long lastRecoveryRequest;
  internal sealed class RecoveryEvidence {public long RequestedTicks;public string Account;public LifeInfoResponse Snapshot;} internal volatile RecoveryEvidence Recovery;
  internal volatile bool RecoveryNeeded;
  private SalesProgress infoArm;internal volatile SalesSnapshot LastSalesSnapshot;
  internal bool NativeBusy{get{var manager=(BDNetwork.NetworkManager)TerritoryBindings.Invoke("Sales.Network");return manager==null||(bool)TerritoryBindings.InvokeOn("Sales.NetworkBusy",manager)||(int)TerritoryBindings.InvokeOn("Sales.NetworkBatchCount",manager)>0;}}
  internal bool NonSalesWaiting{get{lock(sync)return pending.Any(p=>p.SaleIntent==null&&p.InfoIntent==null);}}
  internal bool BeginSalesRefresh(SalesProgress intent)
  {
   if(NativeBusy)return false;
   lock(sync){if(armed!=null||cookArm!=null||pending.Any(p=>(p.SaleIntent==null||p.SaleIntent.Token!=intent.Token)&&(p.InfoIntent==null||p.InfoIntent.Token!=intent.Token)))return false;
    // Only observer entries are retired, never the game's request queue. Native idle is mandatory.
    pending.RemoveAll(p=>p.InfoIntent!=null&&p.InfoIntent.Token==intent.Token);saleArm=null;infoArm=intent;LastSalesSnapshot=null;return true;}
  }
  internal void FinishSalesRecovery(string token)
  {lock(sync){pending.RemoveAll(p=>(p.SaleIntent!=null&&p.SaleIntent.Token==token)||(p.InfoIntent!=null&&p.InfoIntent.Token==token));saleArm=null;infoArm=null;}}
  private SalesProgress saleArm;internal volatile SalesReply LastSaleReply;
  internal void ArmSales(SalesProgress intent){lock(sync){if(saleArm!=null||infoArm!=null||cookArm!=null||pending.Count>0)throw new InvalidOperationException("Another territory request is pending");saleArm=intent;}}
  private CookingProgress cookArm;internal volatile CookingReply LastCookReply;
  internal void ArmCooking(CookingProgress intent){lock(sync){if(cookArm!=null||saleArm!=null||pending.Count>0)throw new InvalidOperationException("Another territory request is pending");cookArm=intent;}}
  internal void Arm(RecipeBatchProgress intent)
  {lock(sync){if(armed!=null||pending.Any(p=>p.Intent!=null))throw new InvalidOperationException("已有播种请求等待发送或确认");armed=intent.Copy();}}
  internal void ForgetUnsentArm(){lock(sync){armed=null;saleArm=null;infoArm=null;}}
  internal void Start()
  {
   current=this;handlers=new Dictionary<MethodBase,string>();var helper=TerritoryBindings.Type("Inventory");
   var methods=helper.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Concat(new[]{helper}).SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)).Where(m=>m.ReturnType==typeof(bool)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(byte[]),typeof(int),typeof(int)}));
   foreach(var t in new[]{typeof(LifeSeedingResponse),typeof(LifeWorldObjectGatheringResponse),typeof(LifeWorldObjectPlaceSaveResponse),typeof(LifeWorldObjectPositionSaveResponse),typeof(LifeCookingResponse),typeof(LifeShopSellResponse),typeof(LifeInfoResponse)})
   {var matched=methods.Where(m=>TerritoryIl.CallsParser(m,t)).ToArray();if(matched.Length==0)throw new InvalidOperationException("缺少领地回执 "+t.Name);foreach(var m in matched)handlers.Add(m,t.Name.Replace("Response",""));}
   var send=typeof(BDNetwork.NetworkManager).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name=="Send"&&m.GetParameters().Length==6&&m.GetParameters()[0].ParameterType==typeof(Google.Protobuf.IMessage));
   try{foreach(var m in handlers.Keys)patch.Patch(m,postfix:new HarmonyMethod(typeof(TerritoryNetwork),m.IsStatic?nameof(StaticResponse):nameof(Response)));patch.Patch(send,prefix:new HarmonyMethod(typeof(TerritoryNetwork),nameof(Sent)));}catch{Dispose();throw;}
  }
  private static void Sent(object __0,object __1)
  {
   var n=current;if(n==null)return;
   try{var seed=__0 as LifeSeedingRequest;var gather=__0 as LifeWorldObjectGatheringRequest;var place=__0 as LifeWorldObjectPlaceSaveRequest;var position=__0 as LifeWorldObjectPositionSaveRequest;var cook=__0 as LifeCookingRequest;var sale=__0 as LifeShopSellRequest;var info=__0 as LifeInfoRequest;if(seed==null&&gather==null&&place==null&&position==null&&cook==null&&sale==null&&(info==null||(n.infoArm==null&&n.recoveryArm.Length==0)))return;var cb=__1 as Delegate;if(cb==null)throw new InvalidOperationException("领地请求缺少原生回调");
    lock(n.sync){if(n.pending.Count>=128){n.pending.RemoveAt(0);n.RecoveryNeeded=true;LocalStorage.Log("network observer capacity reached; preserving transaction journals");}n.pending.Add(new Pending{RecoveryAccount=info==null?"":n.recoveryArm,Info=info==null?null:info.Clone(),InfoIntent=info==null?null:n.infoArm,Sale=sale==null?null:sale.Clone(),SaleIntent=sale==null?null:n.saleArm,Cook=cook==null?null:cook.Clone(),CookIntent=cook==null?null:n.cookArm,Method=cb.Method,Target=cb.Target,At=DateTime.UtcNow,Kind=info!=null?"LifeInfo":sale!=null?"LifeShopSell":cook!=null?"LifeCooking":seed!=null?"LifeSeeding":place!=null?"LifeWorldObjectPlaceSave":position!=null?"LifeWorldObjectPositionSave":"LifeWorldObjectGathering",Position=position==null?null:position.Clone(),Place=place==null?null:place.Clone(),Seed=seed==null?null:seed.Clone(),Gather=gather==null?null:gather.Clone(),Sequence=info!=null?info.Seq:sale!=null?sale.Seq:cook!=null?cook.Seq:seed!=null?seed.Seq:place!=null?place.Seq:position!=null?position.Seq:gather.Seq,Intent=seed!=null?n.armed:null});if(seed!=null)n.armed=null;if(cook!=null)n.cookArm=null;if(sale!=null){n.saleArm=null;LocalStorage.Log("Territory sale dispatched token="+n.pending.Last().SaleIntent?.Token+" seq="+sale.Seq);}if(info!=null){n.infoArm=null;n.recoveryArm="";}n.Last="等待领地服务器响应";}
   }catch(Exception e){if((__0 is LifeShopSellRequest&&n.saleArm!=null)||(__0 is LifeInfoRequest&&n.infoArm!=null))LocalStorage.Log("Territory sale send observation failed: "+e.GetBaseException().Message);else n.Error=e.GetBaseException().Message;}
  }
  private static void StaticResponse(byte[] __0,int __2,bool __result,MethodBase __originalMethod){Response(null,__0,__2,__result,__originalMethod);} private static void Response(object __instance,byte[] __0,int __2,bool __result,MethodBase __originalMethod)
  {
   var n=current;if(n==null)return;
   Pending observed=null;
   try{lock(n.sync){var matches=n.pending.Where(x=>x.Method==__originalMethod&&ReferenceEquals(x.Target,__instance)).ToArray();if(matches.Length!=1)return;var p=matches[0];observed=p;n.pending.Remove(p);n.Last=p.Kind+" error="+__2+" accepted="+__result;LocalStorage.Log(n.Last);
    if(!string.IsNullOrEmpty(p.RecoveryAccount)){
      if(__2==0&&__result){var refreshed=LifeInfoResponse.Parser.ParseFrom(__0);if(refreshed.LifeUserInfo!=null){n.Recovery=new RecoveryEvidence{Snapshot=refreshed,Account=p.RecoveryAccount,RequestedTicks=p.At.Ticks};}}
      return;
    }
    if(p.InfoIntent!=null)
    {
     var snapshot=new SalesSnapshot{Token=p.InfoIntent.Token,Account=p.InfoIntent.Account,RequestedTicks=p.At.Ticks,CapturedTicks=DateTime.UtcNow.Ticks,Error=n.Last};
     if(__2==0&&__result){var response=LifeInfoResponse.Parser.ParseFrom(__0);snapshot.Accepted=response.LifeUserInfo!=null;snapshot.Currency=response.LifeUserInfo==null?-1:response.LifeUserInfo.LifeCoin;snapshot.Items=response.LifeItemInfo.Select(x=>new SaleStock{Index=x.InvenIndex,Item=x.Id,Kind=x.Type,Count=x.Count,Locked=x.KeepFlag!=0||x.IsDisableSlot}).ToArray();}
     n.LastSalesSnapshot=snapshot;return;
    }
    if(p.Sale!=null)
    {
     if(p.SaleIntent!=null){var intent=p.SaleIntent;var actual=p.Sale.SellItemInfo.Select(x=>new SaleLine{Index=x.InvenIndex,Group=x.GroupId,Row=x.Id,Count=x.SellCount}).ToArray();
      bool match=SurplusSales.Matches(intent.Lines,actual);long reward=0;
      if(__2==0&&__result){var response=LifeShopSellResponse.Parser.ParseFrom(__0);if(response.RewardInfo!=null)reward=response.RewardInfo.ItemInfo.Where(x=>x.Type==64&&x.Id==0).Sum(x=>(long)x.Count);}
      n.LastSaleReply=new SalesReply{Token=intent.Token,Matches=match,Accepted=match&&__2==0&&__result,Rejected=match&&__2>0,Reward=reward,Error=n.Last};LocalStorage.Log("Territory sale receipt token="+intent.Token+" seq="+p.Sequence+" matches="+match+" accepted="+n.LastSaleReply.Accepted+" reward="+reward+" expected="+intent.Lines.Sum(x=>(long)x.Count*x.Price));
     }
     if(p.SaleIntent==null&&(__2!=0||!__result))n.Error=n.Last;return;
    }
    if(p.Cook!=null)
    {
     if(p.CookIntent!=null){var intent=p.CookIntent;bool match=p.Cook.Id==intent.Recipe&&p.Cook.Count==intent.Count;bool reward=false;
      if(__2==0&&__result){var response=LifeCookingResponse.Parser.ParseFrom(__0);reward=response.RewardInfoBundle!=null&&response.RewardInfoBundle.ItemInfo.Where(x=>x.Id==intent.ResultItem).Sum(x=>x.Count)>=intent.Count;}
      n.LastCookReply=new CookingReply{Token=intent.Token,Recipe=p.Cook.Id,Count=p.Cook.Count,Accepted=match&&__2==0&&__result&&reward,Rejected=match&&__2>0,Error=__2==0&&__result&&reward?"":n.Last};
     }
     if(__2!=0||!__result)n.Error=n.Last;return;
    }
    if(p.Place!=null||p.Position!=null)
    {
     if(__2!=0||!__result)
     {
      n.Error=n.Last;
      if(__2>0)
      {
       var chunk=p.Place?.ObjectPlaceInfo;int objectId=chunk?.Object.FirstOrDefault()?.ObjectId??0;
       if(p.Position!=null&&p.Position.MoveInfo.Count==1){chunk=p.Position.MoveInfo[0].AfterObjectPlaceInfo;objectId=p.Position.MoveInfo[0].BeforeObjectPlaceInfo?.Object.FirstOrDefault()?.ObjectId??0;}
       if(chunk!=null&&chunk.Object.Count==1){var o=chunk.Object[0];n.LayoutRejectedKey=new LayoutEntry{ObjectId=objectId,ChunkId=chunk.ChunkId,X=o.X,Y=o.Y,Rotate=o.Rotate}.Key;}
      }
     }
     return;
    }
    if(p.Seed!=null)
    {
     // Associate the result with the persisted operation token, never with whichever UI happens to be open now.
     if(__2==0&&__result)foreach(var info in p.Seed.SeedingInfo)if(info.ObjectPlaceInfo!=null)foreach(var obj in info.ObjectPlaceInfo.Object)n.harvested.Remove(Key(info.ObjectPlaceInfo.ChunkId,obj));
     if(p.Intent==null){if(__2!=0||!__result)n.Error=n.Last;return;}
     // Native code emits one LifeSeedingInfo (one field and its unit charge) per crop in ONE request.
     var shape=p.Seed.SeedingInfo.All(x=>x.ObjectPlaceInfo!=null&&x.UseItemInfo!=null&&x.ObjectPlaceInfo.Object.Count==1&&x.ObjectPlaceInfo.Object[0].InnerObject.Count==1);
     var cells=shape?p.Seed.SeedingInfo.Select(x=>new PlantingCell{Key=Key(x.ObjectPlaceInfo.ChunkId,x.ObjectPlaceInfo.Object[0]),Seed=x.ObjectPlaceInfo.Object[0].InnerObject[0].ObjectId,Cost=x.UseItemInfo.Count,CurrencyType=x.UseItemInfo.Type,CurrencyId=x.UseItemInfo.Id}).ToArray():new PlantingCell[0];
     var reply=new PlantingReply{Token=p.Intent.PendingToken,Keys=cells.Select(c=>c.Key).ToArray(),Seed=cells.Length>0?cells[0].Seed:0,Cost=cells.Sum(c=>c.Cost),RequestMatches=PlantingTransaction.MatchesRequest(p.Intent,cells)};
     if(reply.RequestMatches)
     {
      if(__2>0)reply.Rejected=true;
      else if(__2==0&&__result)
      {
       var response=LifeSeedingResponse.Parser.ParseFrom(__0);
       var received=response.ObjectPlaceInfo.SelectMany(x=>x.Object.Select(o=>new{Key=Key(x.ChunkId,o),Obj=o})).ToArray();
       reply.Accepted=PlantingTransaction.SameKeys(reply.Keys,received.Select(x=>x.Key).ToArray())&&received.All(x=>x.Obj.InnerObject.Count==1&&x.Obj.InnerObject[0].ObjectId==reply.Seed&&x.Obj.InnerObject[0].Status>0);
      }
     }
     LocalStorage.Log("播种整批回执 fields="+cells.Length+" seed="+reply.Seed+" cost="+reply.Cost+" matched="+reply.RequestMatches+" accepted="+reply.Accepted);
     if(!reply.RequestMatches)reply.Error="原生播种请求与预览记录不一致";
     else if(!reply.Accepted)reply.Error=reply.Rejected?n.Last:"播种结果未知，请核对世界数据";
     // All reply fields are initialized before this single publication, including on a worker callback thread.
     n.LastPlantReply=reply;if(reply.Error.Length>0)n.Error=reply.Error;
    }
    else
    {
     if(__2!=0||!__result){n.Error=n.Last;return;}
     var r=LifeWorldObjectGatheringResponse.Parser.ParseFrom(__0);int count=r.RewardInfoBundle==null?0:r.RewardInfoBundle.ItemInfo.Sum(i=>i.Count);n.GatherReplies++;n.ReceivedItems+=count;
     // A successful batch can cover multiple chunks while ObjectPlaceInfo in the response is singular.
     // Keep accepted request identities independently of the game's partially refreshed world cache.
     if(p.Gather!=null)foreach(var chunk in p.Gather.ObjectPlaceInfo)foreach(var obj in chunk.Object)n.harvested.Add(Key(chunk.ChunkId,obj));
     if(count==0)LocalStorage.Log("采集已由服务器确认；本次无新增物品，继续处理库存余量和下一个目标");
    }
   }}catch(Exception e){var message="回执核对失败："+e.GetBaseException().Message;
    if(observed!=null&&(observed.SaleIntent!=null||observed.InfoIntent!=null)){n.Last=message;LocalStorage.Log("Territory sale observation failed token="+(observed.SaleIntent??observed.InfoIntent).Token+" "+message);}
    else n.Error=message;
   }
  }
  internal static string Key(int chunk,LifeWorldObjectDBInfo o)=>chunk+":"+o.ObjectId+":"+o.X+":"+o.Y;
  internal void Reconcile(long now){
   if(NativeBusy)return;
   lock(sync){
    var retired=pending.Where(p=>now-p.At.Ticks>=TimeSpan.FromSeconds(BD2.LocalIpc.RequestObservation.TimeoutSeconds).Ticks).ToArray();
    foreach(var p in retired)pending.Remove(p);
    if(retired.Length>0){RecoveryNeeded=true;Last="网络已恢复，正在同步领地状态；不会重复旧操作";LocalStorage.Log("network observer retired="+retired.Length+"; native queues idle; journals retained");}
   }
  }
  internal bool RequestRecovery(long now,string account){
   if(NativeBusy)return false;
   lock(sync){
    if(pending.Count>0||armed!=null||cookArm!=null||saleArm!=null||infoArm!=null||now-lastRecoveryRequest<TimeSpan.FromSeconds(30).Ticks)return false;
    lastRecoveryRequest=now;recoveryArm=account;Last="正在同步领地库存与农田，恢复后继续";
   }
   TerritoryBindings.Invoke("Sales.Refresh",new object[]{null});return true;
  }
  internal bool Waiting {get{lock(sync){return pending.Count>0;}}}
  internal void ClearError(){lock(sync){if(pending.Count==0){Error="";LayoutRejectedKey="";}}}
  public void Dispose(){current=null;patch.UnpatchAll("bd2.territory.network");}
 }
}

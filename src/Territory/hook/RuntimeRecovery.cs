using System;
using System.IO;
using System.Linq;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private long recoverySince;
  private bool RecoverInterruptedOperations(TerritorySnapshot s,long now)
  {
   bool planting=progress!=null&&!string.IsNullOrEmpty(progress.PendingToken);
   bool cookingPending=CookingBusy();
   if(!network.RecoveryNeeded&&!planting&&!cookingPending){recoverySince=0;return false;}
   if(recoverySince==0)recoverySince=now;
   long submitted=planting?progress.PendingSubmittedTicks:cookingPending?cooking.SubmittedTicks:recoverySince;
   if(submitted>0&&now-submitted<TimeSpan.FromSeconds(30).Ticks&&!network.RecoveryNeeded)return false;
   StopMotion();s.Reason="等待同步领地状态，恢复后自动继续；不会重复旧操作";
   if(network.NativeBusy)return true;
   var evidence=network.Recovery;if(evidence==null||evidence.Account!=account||evidence.RequestedTicks<recoverySince)
   {network.RequestRecovery(now,account);return true;}
   var snapshot=evidence.Snapshot;
   var history=Path.Combine(LocalStorage.DataRoot,"recovery-history");Directory.CreateDirectory(history);
   LocalStorage.WriteJsonAtomically(Path.Combine(history,now+"-"+Guid.NewGuid().ToString("N")+".json"),
    new RecoveryRecord{Account=account,RequestedTicks=evidence.RequestedTicks,Planting=planting?progress:null,Cooking=cookingPending?cooking:null,
     Currency=snapshot.LifeUserInfo.LifeCoin,Items=snapshot.LifeItemInfo.Count,WorldChunks=snapshot.ObjectPlaceInfo.Count});
   if(planting){
    var keys=snapshot.ObjectPlaceInfo.SelectMany(w=>w.Object.Where(o=>o.InnerObject.Count==1&&o.InnerObject[0].ObjectId==progress.PendingSeed&&o.InnerObject[0].Status>0)
       .Select(o=>TerritoryNetwork.Key(w.ChunkId,o))).ToArray();
    var next=PlantingTransaction.ReconcileSnapshot(progress,keys);
    SaveProgress(next);CloseOwnedPanel();farmStage=0;
   }
   if(cookingPending){cooking.State="reconciled";LocalStorage.WriteJsonAtomically(cookingPath,cooking);}
   lastCrops=lastCatalog=lastWorldRead=0;network.RecoveryNeeded=false;network.Recovery=null;recoverySince=0;network.ClearError();
   RefreshWorld(now);workCycle.Reset();s.Reason="已同步当前领地状态，继续自动化";LocalStorage.Log(s.Reason);return true;
  }
  [System.Runtime.Serialization.DataContract] public sealed class RecoveryRecord {
   [System.Runtime.Serialization.DataMember] public string Account{get;set;}[System.Runtime.Serialization.DataMember] public long RequestedTicks{get;set;}[System.Runtime.Serialization.DataMember] public RecipeBatchProgress Planting{get;set;}
   [System.Runtime.Serialization.DataMember] public CookingProgress Cooking{get;set;}[System.Runtime.Serialization.DataMember] public long Currency{get;set;}[System.Runtime.Serialization.DataMember] public int Items{get;set;}[System.Runtime.Serialization.DataMember] public int WorldChunks{get;set;}
  }
 }
}

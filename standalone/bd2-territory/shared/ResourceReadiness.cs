namespace BD2Territory
{
 public static class ResourceReadiness
 {
  public static bool Mature(int status,int finalStage)=>finalStage>0&&status>=finalStage;
  // Strict baseline; reconciliation below handles stale client world caches using native field state.
  public static bool EmptyField(bool known,bool serverCrop,bool occupied,bool liveCrop)=>known&&!serverCrop&&!occupied&&!liveCrop;
 }
 public enum FarmReadiness { Unknown, Occupied, LiveCrop, NativeUnavailable, PendingRequest, ReconcilingCache, Empty }
 public sealed class FarmEmptyProgress
 {
  private long emptySince;
  public FarmReadiness Status{get;private set;}=FarmReadiness.Unknown;
  public bool Observe(long now,bool known,bool cachedCrop,bool occupied,bool liveCrop,bool nativePreviewAllowed,bool requestPending,bool harvestConfirmed)
  {
   Status=!known?FarmReadiness.Unknown:occupied?FarmReadiness.Occupied:liveCrop?FarmReadiness.LiveCrop:!nativePreviewAllowed?FarmReadiness.NativeUnavailable:requestPending?FarmReadiness.PendingRequest:FarmReadiness.Empty;
   if(Status!=FarmReadiness.Empty){emptySince=0;return false;}
   if(emptySince==0)emptySince=now;
   // The normal game preview is authoritative for availability. A cache mismatch additionally needs
   // an accepted harvest or stable agreement from occupancy, live crop and native preview checks.
   if(cachedCrop&&!harvestConfirmed&&now-emptySince<System.TimeSpan.FromSeconds(2).Ticks)Status=FarmReadiness.ReconcilingCache;
   return Status==FarmReadiness.Empty;
  }
 }
}

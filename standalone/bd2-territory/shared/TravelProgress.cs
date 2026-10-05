using System;
namespace BD2Territory
{
 // One short native dash per selected target, never an unbounded loop of movement retries.
 public sealed class TravelProgress
 {
  public int Target{get;private set;}public bool DashUsed{get;private set;}public bool Dashing{get;private set;}
  private long dashStarted;
  public void Reset(){Target=0;DashUsed=false;Dashing=false;}
  public void Select(int target){if(Target==target)return;Target=target;DashUsed=false;Dashing=false;}
  public bool BeginDash(long now,bool enabled,bool ready)
  {if(!enabled||!ready||DashUsed||Target==0)return false;DashUsed=Dashing=true;dashStarted=now;return true;}
  public bool FinishDash(long now,double distance,bool nativeActive,bool enabled)
  {return Dashing&&(!enabled||!nativeActive||double.IsNaN(distance)||double.IsInfinity(distance)||distance>=1.2||now-dashStarted>=TimeSpan.FromMilliseconds(450).Ticks);}
  public void EndDash(){Dashing=false;}
  public static bool ShouldMount(bool enabled,bool mounted,bool pending,bool detour,double remaining)
  {return enabled&&!mounted&&!pending&&!detour&&!double.IsNaN(remaining)&&!double.IsInfinity(remaining)&&remaining>=6;}
  public static bool ShouldDismount(bool enabled,bool navigating,bool detour,double remaining)
  {return !enabled||!navigating||detour||double.IsNaN(remaining)||double.IsInfinity(remaining)||remaining<=2;}
 }
 // Cancellation survives a late asset-load callback, including pause and scene/target changes.
 public sealed class VehicleRequest
 {
  public bool Pending{get;private set;}public bool Cancelled{get;private set;}
  private long started;
  public void Begin(long now){if(Pending)throw new InvalidOperationException("载具仍在加载");Pending=true;Cancelled=false;started=now;}
  public void Cancel(){Cancelled=true;}
  public bool Complete(bool stillWanted){bool keep=Pending&&!Cancelled&&stillWanted;Pending=false;return keep;}
  public bool TimedOut(long now)=>Pending&&now-started>=TimeSpan.FromSeconds(8).Ticks;
 }
}

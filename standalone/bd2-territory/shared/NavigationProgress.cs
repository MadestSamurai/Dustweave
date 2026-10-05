using System;
namespace BD2Territory
{
 public enum NavigationDecision { Moving, Arrived, Retry, Skip }
 // Progress means a shorter remaining WALKABLE route, not displacement or distance through a fence.
 public sealed class NavigationProgress
 {
  public const int MaxAttempts=2;
  public int Target{get;private set;}public int Attempts{get;private set;}public bool Active{get;private set;}
  public double BestRemaining{get;private set;}public string Reason{get;private set;}="";
  private bool replanned,recovered;private long started,lastProgress,invalidSince;private double timeLimit;
  public void Reset(){Target=0;Attempts=0;Active=false;Reason="";}
  public void Select(int target){if(Target==target)return;if(Active)throw new InvalidOperationException("寻路中不能更换目标");Reset();Target=target;}
  public void Begin(long now,double pathLength)
  {
   if(Active||Attempts>=MaxAttempts||!Finite(pathLength)||pathLength<0)throw new InvalidOperationException("寻路尝试不可启动");
   Attempts++;Active=true;replanned=recovered=false;started=lastProgress=now;invalidSince=0;BestRemaining=pathLength;timeLimit=Math.Min(45,Math.Max(12,8+pathLength*2));Reason="";
  }
  public NavigationDecision Observe(long now,double remaining,bool validPath,bool arrived)
  {
   if(!Active)throw new InvalidOperationException("没有进行中的寻路");
   if(arrived){Active=false;Reason="arrived";return NavigationDecision.Arrived;}
   if((now-started)/((double)TimeSpan.TicksPerSecond)>=timeLimit)return Fail("time_limit");
   if(!validPath||!Finite(remaining)||remaining<0)
   {if(invalidSince==0)invalidSince=now;if(now-invalidSince>=TimeSpan.FromSeconds(2).Ticks)return Fail("invalid_path");}
   else
   {
    invalidSince=0;
    // A new global best is required; circling between the same two distances never renews the timer.
    if(BestRemaining-remaining>=.35){BestRemaining=remaining;lastProgress=now;}
   }
   if(now-lastProgress>=TimeSpan.FromSeconds(4).Ticks)return Fail("no_route_progress");
   return NavigationDecision.Moving;
  }
  public void Replan(long now,double remaining)
  {if(!Active||replanned||!Finite(remaining)||remaining<0)throw new InvalidOperationException("本次路径不能重复绕行");replanned=true;BestRemaining=remaining;lastProgress=now;invalidSince=0;}
  public void CancelAttempt(){Active=false;Reason="interrupted";}
  public bool Recover(long now,double remaining)
  {
   if(recovered||Attempts==0||(!Active&&Reason!="no_route_progress")||!Finite(remaining)||remaining<0||(now-started)/((double)TimeSpan.TicksPerSecond)>=timeLimit)return false;
   recovered=true;Active=true;BestRemaining=remaining;lastProgress=now;invalidSince=0;Reason="";return true;
  }
  private NavigationDecision Fail(string reason){Active=false;Reason=reason;return Attempts<MaxAttempts?NavigationDecision.Retry:NavigationDecision.Skip;}
  private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
 }
}

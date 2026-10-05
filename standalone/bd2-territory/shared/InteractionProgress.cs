using System;
namespace BD2Territory
{
 public enum InteractionDecision { Approach, WaitForDetection, UseTool, Reposition }
 // Arrival and detector updates have different clocks; do not spend a second route after one frame.
 public sealed class InteractionProgress
 {
  public int Target{get;private set;}private long arrived;
  public void Select(int target){if(Target!=target){Target=target;arrived=0;}}
  public void Arrived(long now){arrived=now;}
  public void Reset(){Target=0;arrived=0;}
  public InteractionDecision Decide(long now,bool detected,bool usable)
  {
   if(detected&&usable)return InteractionDecision.UseTool;
   if(arrived==0)return InteractionDecision.Approach;
   return now-arrived<TimeSpan.FromMilliseconds(700).Ticks?InteractionDecision.WaitForDetection:InteractionDecision.Reposition;
  }
 }
 public sealed class TargetFailureMemory
 {
  public int Failures{get;private set;}public long Until{get;private set;}public long WaitingSince{get;private set;}public long LastAttempt{get;private set;}
  public void Seen(long now){if(WaitingSince==0)WaitingSince=now;}
  public void Selected(long now){Seen(now);LastAttempt=now;WaitingSince=now;}
  public bool ReviewDue(long now)=>Available(now)&&(Failures>0||now-WaitingSince>=TimeSpan.FromMinutes(2).Ticks);
  public void Failed(long now){Seen(now);Failures++;LastAttempt=now;Until=now+TimeSpan.FromSeconds(Failures==1?15:Failures==2?30:60).Ticks;}
  public void Succeeded(){Failures=0;Until=0;WaitingSince=0;LastAttempt=0;}
  public bool Available(long now)=>now>=Until;
 }
 public static class RuntimeHandoffRules
 {
  public static bool CanStop(bool controlActive,bool ownsMovement,bool toolBusy,bool toolLoading,bool networkPending,bool plantingPending)
   =>!controlActive&&!ownsMovement&&!toolBusy&&!toolLoading&&!networkPending&&!plantingPending;
 }
}

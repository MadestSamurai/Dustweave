using System;
namespace BD2Territory
{
 // Own the complete native action, including loading, consecutive swings and server settlement.
 // No angle or movement-state threshold can interrupt this lifetime.
 public sealed class GatheringProgress
 {
  public int Target{get;private set;}public int Misses{get;private set;}public int Repositions{get;private set;}
  public bool Pending{get;private set;}public bool Progressed{get;private set;}public bool TargetProgressed{get;private set;}
  private bool sawBusy;private long issuedAt,idleSince,lastProgress;private double lowestHp;private int replies;
  public bool NeedsReposition=>Misses>=1;
  public void Select(int target)
  {
   if(Target==target)return;
   if(Pending)throw new InvalidOperationException("工具动作中不能更换目标");
   Reset();Target=target;
  }
  public void Reset(){Target=0;Misses=0;Repositions=0;Pending=false;Progressed=false;TargetProgressed=false;sawBusy=false;idleSince=0;}
  public void Issued(long now,double hp,int gatherReplies)
  {
   if(Pending)throw new InvalidOperationException("上一工具动作尚未结束");
   Pending=true;Progressed=false;TargetProgressed=false;sawBusy=false;idleSince=0;issuedAt=lastProgress=now;lowestHp=hp;replies=gatherReplies;
  }
  public bool Observe(long now,bool busy,bool loading,bool requestPending,double hp,int gatherReplies)
  {
   if(!Pending)return !busy&&!loading&&!requestPending;
   // Multi-target tools can harvest adjacent cells while the selected target remains unchanged.
   if(hp<lowestHp||gatherReplies>replies)
   {Progressed=true;lastProgress=now;}
   if(hp<lowestHp){TargetProgressed=true;Misses=0;Repositions=0;lowestHp=hp;}
   replies=Math.Max(replies,gatherReplies);sawBusy|=busy;
   if(busy||loading||requestPending){idleSince=0;return false;}
   if(idleSince==0)idleSince=now;
   // Debounce the gap between native consecutive swings and allow the final response to arrive.
   if(now-idleSince<TimeSpan.FromMilliseconds(600).Ticks)return false;
   if(!sawBusy&&!Progressed&&now-issuedAt<TimeSpan.FromSeconds(5).Ticks)return false;
   if(!TargetProgressed)Misses++;
   Pending=false;return true;
  }
  public bool Stalled(long now)=>Pending&&now-lastProgress>TimeSpan.FromSeconds(45).Ticks;
  public override string ToString()=>"target="+Target+" pending="+Pending+" targetProgress="+TargetProgressed+" anyProgress="+Progressed+" misses="+Misses+" repositions="+Repositions;
  public void Reposition(){if(Pending)throw new InvalidOperationException("工具动作中不能重新走位");Misses=0;Repositions++;}
 }
}

using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private bool jumping,jumpAirborne;private long jumpAt,nextJump;private Vector3 jumpStart,jumpDirection,jumpLanding;
  private int plannedJumpIndex=-1,jumpPlanningChecks;private long plannedJumpWait;
  private readonly RouteMemo<RouteSampleKey,RouteJump[]> jumpPlans=new RouteMemo<RouteSampleKey,RouteJump[]>(512);
  private readonly System.Collections.Generic.List<Vector3> jumpAttempts=new System.Collections.Generic.List<Vector3>();
  private static readonly FieldInfo[] jumpSpeedFields=TerritoryClient.JumpSpeedFields.Select(n=>typeof(MoveController).GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)).ToArray();
  private bool JumpSweep(Vector3 from,Vector3 to)
  {
   Vector3 low,high;float radius;BodyCapsule(from,out low,out high,out radius);var delta=to-from;
   // Leave skin contact with the floor; retain the body's volume against walls/roofs.
   radius=Math.Max(.05f,radius-Math.Min(.04f,Skin*.5f));
   return !Physics.CapsuleCastAll(low,high,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)
    .Any(h=>MovementCollider(h.collider)&&!(delta.y>=0&&TraversalRules.WalkableNormal(h.normal.y,SlopeLimit)));
  }
  private bool JumpLanding(Vector3 from,Vector3 input,double frame,out Vector3 landing)
  {return PredictJumpLanding(from,input,frame,jumpSpeedFields.Length==3&&jumpSpeedFields[0]!=null?Convert.ToDouble(jumpSpeedFields[0].GetValue(move)):0,out landing);}
  private bool PredictJumpLanding(Vector3 from,Vector3 input,double frame,double speed,out Vector3 landing)
  {
   landing=from;if(jumpSpeedFields.Length!=3||jumpSpeedFields.Any(f=>f==null))return false;var previous=from;var direction=input.normalized;
   double maximum=Convert.ToDouble(jumpSpeedFields[1].GetValue(move)),acceleration=Convert.ToDouble(jumpSpeedFields[2].GetValue(move));
   if(!double.IsNaN(maximum)&&maximum>0&&maximum<30&&speed>=0&&acceleration>=0)
   {
    if(Math.Abs(direction.z)==1f)maximum*=1.3;
    foreach(var sample in JumpTrajectory.Samples(speed,maximum,acceleration,input.magnitude,frame))
    {
     var point=from+direction*(float)sample.X+Vector3.up*(float)sample.Y;
     if(!SurfaceSegmentAllowed(previous,point))return false;
     Vector3 ground;
     if(point.y<previous.y&&GroundPoint(new Vector3(point.x,from.y,point.z),out ground)&&point.y<=ground.y+.04f)
     {if(FlatDistance(from,ground)<.6f||!JumpSweep(previous,ground+Vector3.up*.06f))return false;landing=ground;return true;}
     if(!JumpSweep(previous,point))return false;previous=point;
    }
   }
   return false;
  }
  private bool PlannedLanding(Vector3 from,Vector3 input,out Vector3 landing)
  {
   Vector3 b;landing=from;
   // Planned takeoff starts from rest, including after dismounting.
   if(!PredictJumpLanding(from,input,1.0/30,0,out landing)||!PredictJumpLanding(from,input,1.0/60,0,out b))return false;
   if(FlatDistance(landing,b)>.45f||!StaticLocalEdge(Point(landing),Point(b),false))return false;
   float speed=Convert.ToSingle(jumpSpeedFields[1].GetValue(move));if(Math.Abs(input.normalized.z)==1f)speed*=1.3f;
   // Include the forward travel before the next 100 ms touchdown observation.
   var onward=landing+input.normalized*(.15f+speed*input.magnitude*.15f);Vector3 ground;
   return GroundPoint(onward,out ground)&&StaticLocalEdge(Point(landing),Point(ground),false);
  }
  private void ResetJumpPlanning(){jumpPlans.Clear();jumpPlanningChecks=0;plannedJumpWait=0;}
  private System.Collections.Generic.IEnumerable<RouteJump> PlanJumps(RoutePoint p)
  {
   if(jumpSpeedFields.Length!=3||jumpSpeedFields.Any(f=>f==null)||walkTarget==null)return new RouteJump[0];
   return jumpPlans.Get(new RouteSampleKey(p),DateTime.UtcNow.Ticks,()=>{
    var from=Vector(p);var goal=RouteCenter(walkTarget);var delta=goal-from;delta.y=0;
    if(jumpPlanningChecks>=64||delta.sqrMagnitude<1||!StandClear(from)||Obstacle(from,from+delta.normalized*2.5f)==null)return new RouteJump[0];
    jumpPlanningChecks++;var found=new System.Collections.Generic.List<RouteJump>();
    foreach(float angle in new[]{0f,35f,-35f})foreach(float strength in new[]{1f,.8f,.6f})
    {
     var input=Quaternion.Euler(0,angle,0)*delta.normalized*strength;Vector3 land;
     if(!PlannedLanding(from,input,out land)||FlatDistance(land,goal)>=FlatDistance(from,goal)-.25f||StaticLocalEdge(p,Point(land),false))continue;
     found.Add(new RouteJump(Point(land),input.x,input.z));
    }
    return found.ToArray();
   });
  }
  private void ContinuePlannedJump(TerritorySnapshot s,long now,TerritoryControl c,RouteJump plan)
  {
   move.ClearMove();move.StopMove();CancelVehicle();if(player.IsGetOnVehicle())player.GetOffVehicle();
   if(plannedJumpWait==0)plannedJumpWait=now;
   var manager=B.Singleton(typeof(FieldActionManager)) as FieldActionManager;
   if(vehiclePending||!Body.isGrounded||player.IsGetOnVehicle()||manager==null||!manager.CanJump()||manager.IsInDashAction()||manager.IsInActionWithFieldItem()||network.Waiting||ToolLoading()||(tool!=null&&(bool)B.Read("Tool.Busy",tool)))
   {s.Reason="已到起跳点，等待下车与动作收尾";if(now-plannedJumpWait>TimeSpan.FromSeconds(2).Ticks)RetryLocal(s,now,"planned_jump_not_ready",player.transform.position);return;}
   Vector3 landing;var input=new Vector3((float)plan.InputX,0,(float)plan.InputZ);
   if(!PlannedLanding(player.transform.position,input,out landing)||FlatDistance(landing,Vector(plan.Landing))>.5f)
   {RetryLocal(s,now,"planned_jump_changed",Vector(plan.Landing));return;}
   StartJump(s,now,manager,input,landing,localIndex);
  }
  private bool TryJumpRecovery(TerritorySnapshot s,long now,TerritoryControl c,Vector3 goal)
  {
   if(jumping||now<nextJump||jumpAttempts.Count>=3||vehiclePending||player==null||move==null||Body==null||!Body.isGrounded||player.IsGetOnVehicle()||network.Waiting||ToolLoading()||(tool!=null&&(bool)B.Read("Tool.Busy",tool)))return false;
   var manager=B.Singleton(typeof(FieldActionManager)) as FieldActionManager;
   if(manager==null||!manager.CanJump()||manager.IsInDashAction()||manager.IsInActionWithFieldItem())return false;
   var from=player.transform.position;if(jumpAttempts.Any(p=>FlatDistance(p,from)<.6f))return false;move.ClearMove();move.StopMove();
   var toward=goal-from;toward.y=0;if(toward.sqrMagnitude<.01f)return false;
   var best=double.PositiveInfinity;Vector3 chosen=Vector3.zero,land=from;
   foreach(float angle in new[]{0f,35f,-35f,70f,-70f})foreach(float strength in new[]{1f,.8f,.6f})
   {
    var input=Quaternion.Euler(0,angle,0)*toward.normalized*strength;Vector3 a;
    if(!PlannedLanding(from,input,out a))continue;
    // Jump only over something walking cannot cross, toward a usable continuation.
    if(StaticLocalEdge(Point(from),Point(a),false))continue;
    if(FlatDistance(a,goal)>=FlatDistance(from,goal)-.25f)continue;
    double cost=FlatDistance(a,goal);if(cost>=best)continue;best=cost;chosen=input;land=a;
   }
   if(double.IsInfinity(best))return false;
   jumpAttempts.Add(from);nextJump=now+TimeSpan.FromSeconds(3).Ticks;StartJump(s,now,manager,chosen,land,-1);return true;
  }
  private void StartJump(TerritorySnapshot s,long now,FieldActionManager manager,Vector3 chosen,Vector3 land,int routeIndex)
  {
   CancelVehicle();var agent=NavAgent();if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh)agent.ResetPath();
   // Native jumping uses the same character controller as walking.
   if(B.Read("Player.MoveType",move).ToString()!="CharController")B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","CharController"));
   var quest=B.Singleton(typeof(QuestNavigationManager));if(quest!=null)B.InvokeOn("Quest.Pause",quest);
   jumpStart=player.transform.position;jumpDirection=chosen;jumpLanding=land;jumpAt=now;jumpAirborne=false;jumping=true;plannedJumpIndex=routeIndex;plannedJumpWait=0;
   B.InvokeOn("Player.Face",player,chosen);B.InvokeOn("Player.StartMove",player);manager.PlayJump();lastInput=now;
   LocalStorage.Log("跳跃开始 planned="+(routeIndex>=0)+" from="+jumpStart.ToString("F3")+" landing="+land.ToString("F3")+" direction="+chosen.ToString("F3"));s.Reason="跳跃并持续前进，越过低矮障碍";
  }
  private void ContinueJump(TerritorySnapshot s,long now,TerritoryControl c)
  {
   var from=player.transform.position;double elapsed=(now-jumpAt)/(double)TimeSpan.TicksPerSecond;
   if(!Body.isGrounded)jumpAirborne=true;
   if((jumpAirborne&&Body.isGrounded)||elapsed>1.6||(!jumpAirborne&&elapsed>.45))
   {
    jumping=false;move.ClearMove();move.StopMove();InvalidateLocalArea(jumpStart);localPaths.Forget(localTarget);
    LocalStorage.Log("跳跃结束 airborne="+jumpAirborne+" seconds="+elapsed.ToString("F2")+" actual="+from.ToString("F3")+" expected="+jumpLanding.ToString("F3"));
    int completed=plannedJumpIndex;plannedJumpIndex=-1;
    if(completed>=0&&jumpAirborne&&Body.isGrounded&&FlatDistance(from,jumpLanding)<.7f&&StaticLocalEdge(Point(from),Point(jumpLanding),false))
    {localJumps.Remove(completed);localIndex=completed+1;localMotion.Begin(now,Point(from));s.Reason="已越过障碍，继续预先规划的路线";return;}
    if(!BeginLocalRoute(walkTarget,s,now,"jump_landed_replan"))SkipWalkTarget(walkTarget,s,now,"jump_replan_unavailable");return;
   }
   // Ground step/wall tests must not cancel the horizontal input halfway through the arc.
   B.InvokeOn("Player.Face",player,jumpDirection);B.InvokeOn("Player.StartMove",player);lastInput=now;s.Reason="跳跃中保持前进，等待实际落地";
  }
 }
}

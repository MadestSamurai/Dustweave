using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private AdaptiveLocalRoute localSearch;private Vector3[] localRoute=new Vector3[0];private int localIndex,localTarget;
  private bool localMoving,localFine,localTrial;private Vector3 localOrigin;private long localPlanAt;private readonly LocalMotionProgress localMotion=new LocalMotionProgress();
  private readonly LocalRecoveryBudget localBudget=new LocalRecoveryBudget();private readonly LocalObstructionMemory localBlocked=new LocalObstructionMemory();
  private static RoutePoint Point(Vector3 p)=>new RoutePoint(p.x,p.y,p.z);
  private static Vector3 Vector(RoutePoint p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
  private static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
  private RoutePoint? LocalSample(RoutePoint p)
  {
   var ground=CachedGround(p);if(!ground.HasValue)return null;var point=Vector(ground.Value);
   if(!localBlocked.SampleAllowed(Point(localOrigin),Point(point))||!localTrial&&!CachedTraversalClear(point))return null;
   return ground;
  }
  private bool LocalEdge(RoutePoint a,RoutePoint b)
  {
   return localBlocked.EdgeAllowed(a,b)&&StaticLocalEdge(a,b,false,localTrial);
  }
  private bool PlanEdge(RoutePoint a,RoutePoint b)
  {
   // Recovery exclusions belong to this attempt; never memoize them as permanent terrain.
   if(!localBlocked.EdgeAllowed(a,b))return false;
   return localTrial?StaticLocalEdge(a,b,true,true):localEdges.Get(new RouteEdgeKey(a,b),DateTime.UtcNow.Ticks,()=>StaticLocalEdge(a,b,true,false));
  }
  private bool StaticLocalEdge(RoutePoint a,RoutePoint b,bool cached,bool trial)
  {
   var from=Vector(a);var to=Vector(b);if(!SurfaceSegmentAllowed(from,to))return false;
   int steps=Math.Max(1,(int)Math.Ceiling(FlatDistance(from,to)/.1f));var previous=from;
   for(int i=1;i<=steps;i++)
   {var wanted=Vector3.Lerp(from,to,(float)i/steps);wanted.y=previous.y;Vector3 ground;
    if(cached){var found=CachedGround(Point(wanted));if(!found.HasValue)return false;ground=Vector(found.Value);}
    else if(!GroundPoint(wanted,out ground,false))return false;
    if(!TraversalRules.SurfaceChange(ground.y-previous.y,FlatDistance(previous,ground),StepHeight,SlopeLimit,Skin)||!trial&&(!TraversalClear(ground)||Obstacle(previous,ground)!=null))return false;previous=ground;}
   return Math.Abs(previous.y-to.y)<=Math.Max(.12f,Skin+.03f);
  }
  private bool InInteractionRange(Component target,Vector3 point)
  {
   if(target is LifeFarmFieldObject farm)return farm.IsPlayerInInteractionRange(point);
   var sector=target.GetComponent<CircleSectorCollider>();return sector!=null&&sector.IsDetectionInCollisionArea(point);
  }
  private IEnumerable<Vector3> StandCandidates(Component target,Vector3 center,float radius,bool retry)
  {
   var sector=target is LifeGatheringObject?target.GetComponent<CircleSectorCollider>():null;
   float reach=sector==null?Math.Max(.7f,radius):Convert.ToSingle(B.Read("Detector.Distance",sector));
   // Sample inside the interaction sector; actual capsule/shape tests decide clearance.
   var from=player.transform.position;center.y+=RootLift;
   foreach(var candidate in LocalStandPoints.Create(Point(center),Point(from),reach,target is LifeGatheringObject resource&&Kind(resource)==1))
   {
    Vector3 p;if(!GroundPoint(Vector(candidate),out p,true))continue;
    if(!CanGatherAt(target,p)||triedDestinations.Any(t=>FlatDistance(t,p)<.18f))continue;
    if(retry&&(triedDestinations.Any(t=>FlatDistance(t,p)<.12f)||(gatheringReposition&&FlatDistance(from,p)<.25f)))continue;
    yield return p;
   }
  }
  private void ResetLocalRecovery(){localBudget.Reset();localBlocked.Clear();localTarget=0;localFine=false;localTrial=false;}
  private void ClearLocalMotion(){localSearch=null;localMoving=false;localRoute=new Vector3[0];localIndex=0;}
  private bool BeginLocalRoute(Component target,TerritorySnapshot s,long now,string reason)
  {
   if(target==null||player==null||move==null)return false;
   if(localTarget!=target.GetInstanceID()){ResetLocalRecovery();localTarget=target.GetInstanceID();}
   if(!localBudget.Plan(now))return false;
   CancelVehicle();if(player.IsGetOnVehicle())player.GetOffVehicle();
   move.ClearMove();move.StopMove();var agent=NavAgent();if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh)agent.ResetPath();
   B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","CharController"));
   var quest=B.Singleton(typeof(QuestNavigationManager));if(quest!=null)B.InvokeOn("Quest.Pause",quest);
   navigation.CancelAttempt();ownsMove=true;walkTarget=target;walkScene=s.Scene;bypassing=false;
   var center=target is LifeFarmFieldObject farm?farm.GetFieldWorldCenter():target.transform.position;
   var goals=StandCandidates(target,center,.7f,gatheringReposition).Select(Point).ToArray();
   if(goals.Length==0){LocalStorage.Log("局部规划无站位 target="+target.GetInstanceID()+" reason="+reason);return false;}
   localOrigin=player.transform.position;localMoving=true;localPlanAt=now;
   var saved=localPaths.Reuse(target.GetInstanceID(),now,Point(localOrigin),p=>goals.Any(g=>RoutePoint.Distance(g,p)<.06)&&CanGatherAt(target,Vector(p)),LocalEdge);
   if(saved==null)saved=localPaths.ReuseCorridor(Point(localOrigin),goals,LocalEdge);
   if(saved!=null){localSearch=null;SetLocalRoute(saved,now);s.Reason="复用已检测通路，实时核对障碍";LocalStorage.Log("实体路线缓存命中 target="+target.GetInstanceID()+" points="+saved.Length);return true;}
   localSearch=new AdaptiveLocalRoute(Point(localOrigin),goals,LocalSample,PlanEdge,false,localBudget.Plans==1?8:14,BridgeRouteGuides());
   localRoute=new Vector3[0];localIndex=0;
   LocalStorage.Log("实体局部规划 target="+target.GetInstanceID()+" reason="+reason+" plan="+localBudget.Plans+" goals="+goals.Length+" from="+player.transform.position);
   s.Reason="检测周围障碍，规划普通移动绕行";return true;
  }
  private void SetLocalRoute(RoutePoint[] path,long now)
  {localRoute=path.Select(Vector).ToArray();finalDestination=localRoute.Last();localIndex=1;localMotion.Begin(now,Point(player.transform.position));}
  private void RetryLocal(TerritorySnapshot s,long now,string reason,Vector3 blocked,bool stalled=false)
  {
   move.ClearMove();move.StopMove();InvalidateLocalArea(blocked);localPaths.Forget(localTarget);if(stalled){localBlocked.Record(Point(blocked));localTrial=false;}
   LocalStorage.Log("路线重算 reason="+reason+" observedStall="+stalled+" from="+player.transform.position.ToString("F3")+" point="+blocked.ToString("F3")+" motion="+localMotion+" exclusions="+localBlocked);CaptureFailure(reason);
   if(!BeginLocalRoute(walkTarget,s,now,reason))SkipWalkTarget(walkTarget,s,now,reason);
  }
  private void ContinueLocal(TerritorySnapshot s,long now,TerritoryControl c)
  {
   s.Reason="沿实体通路绕行，保持原目标";
   if(localBudget.Expired(now)){SkipWalkTarget(walkTarget,s,now,"local_recovery_budget");return;}
   if(vehiclePending){move.StopMove();localMotion.Begin(now,Point(player.transform.position));s.Reason="等待载具加载结束后绕行";return;}
   var field=B.Read("Field.Instance",null);string state=B.Read("Field.MoveState",field)?.ToString();
   if(state=="DontMove"||state=="Anchored"){move.ClearMove();localMotion.Begin(now,Point(player.transform.position));s.Reason="等待游戏恢复角色移动";return;}
   if(localSearch!=null)
   {
    if(now-localPlanAt>TimeSpan.FromSeconds(15).Ticks){if(!localTrial){localTrial=true;if(BeginLocalRoute(walkTarget,s,now,"native_probe_after_prediction_timeout"))return;}SkipWalkTarget(walkTarget,s,now,"local_planning_budget");return;}
    // A slow wide detour is not evidence of a narrow passage. Keep the coarse
    // frontier until it is exhausted; restarting at 0.1 m multiplies area by 16.
    // Authored bridge guides cover narrow crossings without refining the whole map.
    var status=localSearch.Step(256,6);localFine=localSearch.Cell<.4;s.Reason=(localFine?"细化窄路网格 · ":"扫描地面和碰撞体 · ")+localSearch.Expanded+" 个路点";
    if(status==RouteSearchState.Searching)return;
    if(status==RouteSearchState.Exhausted)
    {if(!localTrial){localTrial=true;if(BeginLocalRoute(walkTarget,s,now,"native_traversal_probe"))return;}int count=localSearch.Expanded;LocalStorage.Log("实体局部规划未找到通路 target="+walkTarget.GetInstanceID()+" expanded="+count);if(localBudget.Plans<2&&BeginLocalRoute(walkTarget,s,now,"expand_local_area"))return;SkipWalkTarget(walkTarget,s,now,"local_route_exhausted");return;}
    var path=localSearch.Path;localPaths.Save(localTarget,now,path);SetLocalRoute(path,now);
    LocalStorage.Log("实体局部路线就绪 target="+walkTarget.GetInstanceID()+" points="+localRoute.Length+" goal="+localRoute.Last()+" planMs="+((now-localPlanAt)/TimeSpan.TicksPerMillisecond)+" expanded="+localSearch.Expanded+" cell="+localSearch.Cell+" floorCache="+localGround.Hits+"/"+localGround.Misses+" edgeCache="+localEdges.Hits+"/"+localEdges.Misses);localSearch=null;
   }
   var from=player.transform.position;
   bool detected=walkTarget is LifeGatheringObject resource&&Detected(Kind(resource)).Contains(resource);
   if(GatherStandReached(walkTarget,from,localIndex>=localRoute.Length,detected))
   {int id=walkTarget.GetInstanceID();LocalStorage.Log("实体站位到达 target="+id+" recovery="+gatheringReposition+" actual="+from.ToString("F3")+" goal="+finalDestination.ToString("F3"));StopMove();ResetLocalRecovery();navigation.Reset();interaction.Select(id);interaction.Arrived(now);gatheringReposition=false;s.Reason="绕行已到位，准备采集";return;}
   while(localIndex<localRoute.Length&&FlatDistance(from,localRoute[localIndex])<.10f&&(localIndex<localRoute.Length-1||CanGatherAt(walkTarget,from)))localIndex++;
   if(localIndex>=localRoute.Length){if(!CanGatherAt(walkTarget,from))RetryLocal(s,now,"local_arrival_outside_range",from);else move.StopMove();return;}
   // Follow a visible corridor, not every grid corner. Recheck against live colliders before steering.
   for(int j=Math.Min(localRoute.Length-1,localIndex+4);j>localIndex;j--)if(FlatDistance(from,localRoute[j])<=1.4f&&LocalEdge(Point(from),Point(localRoute[j]))){localIndex=j;break;}
   var dest=localRoute[localIndex];var delta=dest-from;delta.y=0;
   var ahead=from+Vector3.ClampMagnitude(delta,.7f);
   var obstacle=Obstacle(from,ahead);
   if(obstacle!=null){blockingCollider=obstacle;s.Reason="预测存在接触，按游戏正常移动验证通行";}else blockingCollider=null;
   double remaining=FlatDistance(from,dest);for(int i=localIndex+1;i<localRoute.Length;i++)remaining+=FlatDistance(localRoute[i-1],localRoute[i]);
   if(localMotion.Stalled(now,Point(from),remaining))
   {
    // A near-goal collision is a bad interaction stand, not evidence that the whole road is blocked.
    if(FlatDistance(from,finalDestination)<.3f)
    {triedDestinations.Add(finalDestination);RetryLocal(s,now,"local_stand_unreachable",finalDestination);return;}
    if(TryEscape(s,now,c,blockingCollider))return;
    RetryLocal(s,now,"local_motion_stalled",from+delta.normalized*.35f,true);return;
   }
   if(!localBlocked.EdgeAllowed(Point(from),Point(dest))||!StaticLocalEdge(Point(from),Point(dest),false,true)){RetryLocal(s,now,"local_ground_changed",dest);return;}
   // Mounting changes speed, not the movement controller. A* keeps steering via CharController.
   UpdateVehicle(now,c,remaining);
   if(vehiclePending){move.StopMove();localMotion.Begin(now,Point(player.transform.position));return;}
   if(player.IsGetOnVehicle())s.Reason="使用载具沿实体通路行进";
   B.InvokeOn("Player.Face",player,delta.normalized*Mathf.Clamp(delta.magnitude/.65f,.1f,1f));B.InvokeOn("Player.StartMove",player);lastInput=now;
  }
 }
}

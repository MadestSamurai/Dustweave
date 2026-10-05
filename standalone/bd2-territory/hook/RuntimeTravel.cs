using System;
using UnityEngine;
using UnityEngine.AI;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private readonly TravelProgress travel=new TravelProgress();
  private readonly VehicleRequest vehicleRequest=new VehicleRequest();
  private bool vehiclePending,ownsVehicle;private long nextVehicle;
  private Vector3 escapeStart,escapeDirection;private object dashManager;
  private bool FootReady(TerritorySnapshot s)
  {
   CancelVehicle();if(player!=null&&player.IsGetOnVehicle())player.GetOffVehicle();
   if(!vehiclePending)return true;s.Reason="等待载具加载收尾后操作";return false;
  }
  private void CancelVehicle()
  {
   vehicleRequest.Cancel();
   if(ownsVehicle&&player!=null&&player.IsGetOnVehicle())player.GetOffVehicle();ownsVehicle=false;
  }
  private void UpdateVehicle(long now,TerritoryControl c,double remaining)
  {
   if(TravelProgress.ShouldDismount(c.UseVehicle,ownsMove,bypassing,remaining)){CancelVehicle();return;}
   if(now<nextVehicle||!TravelProgress.ShouldMount(c.UseVehicle,player.IsGetOnVehicle(),vehiclePending,bypassing,remaining))return;
   nextVehicle=now+TimeSpan.FromSeconds(10).Ticks;
   if(!player.IsCanGetOnVehicle())return;
   var rider=player;var requestOwner=owner;var requestScene=walkScene;int target=navigation.Target;
   vehicleRequest.Begin(now);vehiclePending=true;
   LocalStorage.Log("载具上车请求 target="+target+" remaining="+remaining.ToString("F2"));
   rider.GetOnVehicle(delegate(FieldAvatarVehicleObjectController vehicle)
   {
    try
    {
     var live=control;bool keep=vehicleRequest.Complete(!stopped&&rider!=null&&rider==player&&vehicle!=null&&ownsMove&&!travel.Dashing&&!bypassing&&walkTarget!=null&&navigation.Target==target&&walkScene==requestScene&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().name==requestScene&&owner==requestOwner&&live!=null&&live.OwnerId==requestOwner&&live.Valid(DateTime.UtcNow.Ticks,pid)&&live.UseVehicle&&Vector3.Distance(rider.transform.position,finalDestination)>2);
     if(!keep){if(rider!=null&&rider.IsGetOnVehicle())rider.GetOffVehicle();ownsVehicle=false;}
     else ownsVehicle=rider.IsGetOnVehicle();
     LocalStorage.Log("载具加载完成 retained="+ownsVehicle+" target="+target);
    }
    catch(Exception e){fault="载具状态收尾失败："+e.GetBaseException().Message;}
    finally{vehiclePending=false;}
   });
  }
  private bool TryEscape(TerritorySnapshot s,long now,TerritoryControl c,Collider obstacle)
  {
   if(!c.DashRecovery||travel.DashUsed||vehiclePending||tool==null||(bool)B.Read("Tool.Busy",tool)||ToolLoading()||network.Waiting)return false;
   var field=B.Read("Field.Instance",null);string state=B.Read("Field.MoveState",field)?.ToString();if(state=="DontMove"||state=="Anchored")return false;
   var manager=B.Singleton(typeof(FieldActionManager));if(manager==null||(bool)B.InvokeOn("Dash.Active",manager)||!(bool)B.InvokeOn("Dash.Can",manager)||(bool)B.InvokeOn("Dash.FieldAction",manager))return false;
   var data=B.Read("Dash.Data",manager);if(data==null||!(bool)B.InvokeOn("Dash.Ready",data))return false;
   var agent=NavAgent();if(c.UseNavMesh&&!localMoving&&(agent==null||!agent.isActiveAndEnabled||!agent.isOnNavMesh))return false;
   var from=player.transform.position;var filter=agent==null?new NavMeshQueryFilter():NavFilter(agent);var toward=finalDestination-from;toward.y=0;
   if(obstacle!=null){toward=from-obstacle.bounds.center;toward.y=0;}if(toward.sqrMagnitude<.001f)toward=Vector3.forward;
   double best=double.PositiveInfinity;Vector3 selected=Vector3.zero;
   foreach(float degrees in new[]{0f,45f,-45f,90f,-90f,135f,-135f,180f})
   {
    var direction=Quaternion.Euler(0,degrees,0)*toward.normalized;
    // Verify an escape corridor longer than the intended 1.2 m burst, plus a walkable landing.
    var p=from+direction*2.5f;NavMeshHit hit;
    double cost;if(!SurfaceSegmentAllowed(from,p))continue;
    if(localMoving||!c.UseNavMesh)
    {
     Vector3 end,landing;
     if(!GroundPoint(p,out end)||!StaticLocalEdge(Point(from),Point(end),false,false)||!GroundPoint(from+direction*1.2f,out landing))continue;
     cost=FlatDistance(landing,finalDestination);
    }
    else
    {
     if(!NavMesh.SamplePosition(p,out hit,.2f,filter)||Math.Abs(hit.position.y-from.y)>.4f||!StandClear(hit.position+Vector3.up*RootLift)||Obstacle(from,hit.position+Vector3.up*RootLift)!=null||NavMesh.Raycast(from,hit.position,out hit,filter))continue;
     var landing=from+direction*1.2f;if(!NavMesh.SamplePosition(landing,out hit,.2f,filter)||!StandClear(hit.position+Vector3.up*RootLift))continue;
     var path=new NavMeshPath();if(!NavMesh.CalculatePath(hit.position,finalDestination,filter,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
     cost=PathLength(path);if(cost>navigation.BestRemaining+8)continue;
    }
    if(cost>=best)continue;best=cost;selected=direction;
   }
   if(double.IsInfinity(best)||!travel.BeginDash(now,c.DashRecovery,true))return false;
   // Clear the old path BEFORE dash starts: native StopDashAction otherwise resumes that path/quest.
   CancelVehicle();if(player.IsGetOnVehicle())player.GetOffVehicle();
   move.ClearMove();move.StopMove();if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh)agent.ResetPath();B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","CharController"));
   var quest=B.Singleton(typeof(QuestNavigationManager));if(quest!=null)B.InvokeOn("Quest.Pause",quest);
   escapeStart=from;escapeDirection=selected;dashManager=manager;B.InvokeOn("Player.Face",player,selected);
   B.InvokeOn("Dash.Play",manager);lastInput=now;
   LocalStorage.Log("冲刺脱困 target="+navigation.Target+" from="+from+" direction="+selected+" accepted="+B.InvokeOn("Dash.Active",manager));
   s.Reason="短距冲刺脱困，保持原目标";return true;
  }
  private void StopEscape()
  {
   if(!travel.Dashing)return;
   // Resetting the nav path prevents native dash cleanup from restoring an obsolete destination.
   var agent=NavAgent();if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh){agent.isStopped=true;agent.ResetPath();}
   if(dashManager!=null)B.InvokeOn("Dash.Stop",dashManager);
   if(move!=null){move.ClearMove();move.StopMove();}travel.EndDash();dashManager=null;
  }
  private void ContinueEscape(TerritorySnapshot s,long now,TerritoryControl c)
  {
   s.Reason="短距冲刺脱困";var from=player.transform.position;double distance=Vector3.Distance(from,escapeStart);
   bool active=dashManager!=null&&(bool)B.InvokeOn("Dash.Active",dashManager);
   if(!travel.FinishDash(now,distance,active,c.DashRecovery)&&SurfaceSegmentAllowed(from,from+escapeDirection*.8f)&&Obstacle(from,from+escapeDirection*.8f)==null)return;
   StopEscape();LocalStorage.Log("冲刺结束，按实际位置重算 target="+navigation.Target+" moved="+distance.ToString("F2")+" at="+from);
   ResumeRouteAfterDash(s,now,c);
  }
  private void ResumeNavMesh(TerritorySnapshot s,long now,TerritoryControl c)
  {
   var from=player.transform.position;
   B.InvokeOn("Player.ChangeMoveType",move,B.EnumObject("MoveKind","Navigation"));var agent=NavAgent();
   Vector3 chosen;double cost;var center=walkTarget is LifeFarmFieldObject farm?farm.GetFieldWorldCenter():walkTarget.transform.position;
   if(agent==null||!agent.isActiveAndEnabled||!agent.isOnNavMesh||!FindStand(walkTarget,center,.7f,from,NavFilter(agent),false,out chosen,out cost)||!navigation.Recover(now,cost))
   {SkipWalkTarget(walkTarget,s,now,"dash_replan_unavailable");return;}
   walkDestination=finalDestination=chosen;bypassing=false;lastPathCheck=now;
   B.InvokeOn("Player.StartMove",player);
   if(!(bool)B.InvokeOn("Player.SetMoveNav",move,chosen,null,true)){SkipWalkTarget(walkTarget,s,now,"dash_replan_rejected");return;}
   s.Reason="脱困后重新寻路，继续原目标";
  }
 }
}

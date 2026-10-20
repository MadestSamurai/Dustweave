using UnityEngine;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  // These are the only entry points into native routing. Default and missing settings use A*.
  private bool StartRoute(Component target,Vector3 center,TerritorySnapshot s,long now,float radius)
  {
   if(control!=null&&control.UseNavMesh)return ApproachNavMesh(target,center,s,now,radius);
   StopMotion();
   if(!BeginLocalRoute(target,s,now,"astar_default"))SkipWalkTarget(target,s,now,"astar_unavailable");
   return false;
  }
  private void ContinueRoute(TerritorySnapshot s,long now,TerritoryControl c)
  {
   // Disabling NavMesh retires its current route (including an in-flight recovery dash).
   // Enabling it finishes a current A* route and applies to the next approach.
   if(!c.UseNavMesh&&!localMoving)
   {
    StopEscape();
    if(!BeginLocalRoute(walkTarget,s,now,"navmesh_disabled"))SkipWalkTarget(walkTarget,s,now,"astar_unavailable");
    return;
   }
   if(travel.Dashing){ContinueEscape(s,now,c);return;}
   if(localMoving){ContinueLocal(s,now,c);return;}
   ContinueNavMesh(s,now,c);
  }
  private void ResumeRouteAfterDash(TerritorySnapshot s,long now,TerritoryControl c)
  {
   if(localMoving||!c.UseNavMesh)
   {
    if(!BeginLocalRoute(walkTarget,s,now,"dash_replan_physical"))SkipWalkTarget(walkTarget,s,now,"dash_replan_unavailable");
    return;
   }
   ResumeNavMesh(s,now,c);
  }
 }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BD2Territory;
namespace BD2Daily.Live
{
 // A route waypoint is never the interaction anchor for the next replan.
 public sealed class SquareApproach
 {
  public RoutePoint Anchor { get; private set; }
  public RoutePoint Stand { get; private set; }
  public SquareApproach(RoutePoint anchor){Anchor=Stand=anchor;}
  public void SelectStand(RoutePoint stand){Stand=stand;}
  public static RoutePoint[] InteractionPoints(RoutePoint center,RoutePoint from,double reach,Func<RoutePoint,RoutePoint?> project,Func<RoutePoint,bool> inRange,bool preferClose=false)
  {
   if(double.IsNaN(reach)||double.IsInfinity(reach)||reach<=0)return new RoutePoint[0];
   var found=new List<RoutePoint>();double start=Math.Atan2(from.Z-center.Z,from.X-center.X);
   // Keep all approach directions. Taking the nearest 24 points from concentric
   // circles can omit the only side reachable around a wall or shop counter.
   for(int i=0;i<32;i++)
   {
    double angle=start+i*Math.PI/16;
    foreach(double fraction in (preferClose?new[]{.25,.35,.5,.65,.8,.94}:new[]{.94,.75,.5,.25}))
    {
     var p=project(new RoutePoint(center.X+Math.Cos(angle)*reach*fraction,center.Y,center.Z+Math.Sin(angle)*reach*fraction));
     if(!p.HasValue||!inRange(p.Value))continue;
     found.Add(p.Value);break;
    }
   }
   return found.OrderBy(p=>RoutePoint.Distance(from,p)).ToArray();
  }
 }
}

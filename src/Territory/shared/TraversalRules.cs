using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Territory
{
 public static class TraversalRules
 {
  public static double ContactMargin(double skin)=>Math.Max(.015,Math.Min(.05,skin));
  public static bool WalkableNormal(double normalY,double slopeDegrees)=>normalY>0&&normalY+1e-6>=Math.Cos(Math.Max(0,Math.Min(89,slopeDegrees))*Math.PI/180);
  public static bool CanStep(double top,double foot,double step,double skin)=>step>0&&top-foot<=step+Math.Min(.02,ContactMargin(skin));
  public static bool SurfaceChange(double rise,double distance,double step,double slopeDegrees,double skin)
  {
   if(double.IsNaN(rise)||double.IsInfinity(rise))return false;
   if(rise<0)return -rise<=Math.Max(.45,step)+ContactMargin(skin);
   return rise<=Math.Max(step,Math.Max(0,distance)*Math.Tan(Math.Max(0,Math.Min(89,slopeDegrees))*Math.PI/180))+ContactMargin(skin);
  }
 }
 // Actual route progress, including stepping upward; a turn or sub-centimetre jitter is not progress.
 public sealed class LocalMotionProgress
 {
  private long advancedAt;private double best=double.PositiveInfinity;private RoutePoint anchor;
  public override string ToString()=>"lastProgressUtcTicks="+advancedAt+" bestRemaining="+best.ToString("F3");
  public void Begin(long now,RoutePoint from){advancedAt=now;anchor=from;best=double.PositiveInfinity;}
  public bool Stalled(long now,RoutePoint position,double remaining)
  {
   if(advancedAt==0)Begin(now,position);
   if(double.IsPositiveInfinity(best)){best=remaining;anchor=position;}
   if(best-remaining>=.06||(Math.Abs(position.Y-anchor.Y)>=.04&&RoutePoint.Distance(position,anchor)>=.02&&remaining<=best+.04))
   {best=Math.Min(best,remaining);anchor=position;advancedAt=now;}
   return now-advancedAt>=TimeSpan.FromSeconds(3).Ticks;
  }
 }
 // Only observed, sustained physical stalls create exclusions. Never record a failed
 // geometric prediction here. A player already inside the tolerance can always leave.
 public sealed class LocalObstructionMemory
 {
  private readonly List<RoutePoint> points=new List<RoutePoint>();private const double Radius=.26;
  public void Clear()=>points.Clear();
  public void Record(RoutePoint point){points.Add(point);if(points.Count>16)points.RemoveAt(0);}
  public bool SampleAllowed(RoutePoint origin,RoutePoint p)=>points.All(b=>RoutePoint.Distance(b,p)>=Radius||Outward(b,origin,p));
  private static bool Outward(RoutePoint c,RoutePoint a,RoutePoint b)
  {
   double x=a.X-c.X,z=a.Z-c.Z,dx=b.X-a.X,dz=b.Z-a.Z;
   return x*x+z*z<Radius*Radius&&x*dx+z*dz>=-1e-9&&RoutePoint.Distance(c,b)>RoutePoint.Distance(c,a)+1e-5;
  }
  public bool EdgeAllowed(RoutePoint a,RoutePoint b)
  {
   foreach(var p in points)
   {
    if(Outward(p,a,b))continue;
    double dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz;
    double t=len<1e-12?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/len));
    if(RoutePoint.Distance(p,new RoutePoint(a.X+dx*t,a.Y,a.Z+dz*t))<Radius)return false;
   }
   return true;
  }
  public override string ToString()=>string.Join(";",points.Select(p=>p.X.ToString("F3")+","+p.Y.ToString("F3")+","+p.Z.ToString("F3")));
 }

}

using System;
namespace BD2SecretVision.Rules {
// Shared by the live gate and desktop planner. A suppressed attack timer may be overdue.
public static class AttackOpportunity {
 public const double Horizon=30;
 public static double Available(string state,double normal,double special,double control,double carve){
  control=Math.Max(0,control);
  if(state!="Move"&&control<=0)return 0;
  return Math.Max(control+Math.Max(0,Math.Min(normal,special)),Math.Max(0,carve));
 }
 public static double CarveSeconds(string state,string carve,bool interrupted,double wallDistance,double speed,double control,double timeStop){
  if((state!="Move"&&control<=0)||(carve!="Ready"&&carve!="Progress"))return 0;
  // Carve clears on a Wall cell. Use the fastest possible arrival, independent of direction.
  // Power cancels the Ready coroutine without clearing Ready; movement and attacks stay blocked.
  double duration=carve=="Ready"&&interrupted?Horizon:Math.Min(Horizon,Math.Max(0,control)+Math.Max(0,wallDistance)/Math.Max(1,speed));
  // A time-stop state transition can invalidate the carve branch before it reaches a wall.
  if(timeStop>0)duration=Math.Min(duration,timeStop);
  return duration;
 }
 public static double StationarySeconds(string state,string carve,bool interrupted,double control,double timeStop){
  if(carve!="Ready"||!interrupted||(state!="Move"&&control<=0))return Math.Max(0,control);
  return Math.Max(control,timeStop>0?Math.Min(Horizon,timeStop):Horizon);
 }
 public static double DistanceToWall(double x,double y,double wallX,double wallY,double cell){
  double dx=Math.Max(0,Math.Max(wallX-x,x-wallX-cell)),dy=Math.Max(0,Math.Max(wallY-y,y-wallY-cell));
  return Math.Sqrt(dx*dx+dy*dy);
 }
}
}

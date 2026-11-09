using System;
using System.Collections.Generic;
using System.Linq;

namespace BD2Daily.Live {
 // Geometry shared by the native pilot and offline experiments. No game writes,
 // monster classification, talent decisions or global map exclusions live here.
 public struct FieldPoint {
  public double X,Y,Z;
  public FieldPoint(double x,double y,double z){X=x;Y=y;Z=z;}
  public static FieldPoint operator +(FieldPoint a,FieldPoint b){return new FieldPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
  public static FieldPoint operator -(FieldPoint a,FieldPoint b){return new FieldPoint(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
  public static FieldPoint operator *(FieldPoint a,double b){return new FieldPoint(a.X*b,a.Y*b,a.Z*b);}
  public double Squared {get{return X*X+Y*Y+Z*Z;}}
  public static double Dot(FieldPoint a,FieldPoint b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
  public static double Distance(FieldPoint a,FieldPoint b){return Math.Sqrt((a-b).Squared);}
  public bool Finite {get{return !double.IsNaN(X)&&!double.IsNaN(Y)&&!double.IsNaN(Z)&&!double.IsInfinity(X)&&!double.IsInfinity(Y)&&!double.IsInfinity(Z);}}
 }
 public sealed class FieldExclusion {
  public int Id;
  public FieldPoint Center;
  public double Radius;
 }
 public sealed class FieldAvoidancePlan {
  public FieldPoint[] Points=new FieldPoint[0];
  public string Reason="";
  public int Queries;
  public double Length;
  // A checked native route already clears every exclusion. Its polygon
  // corners describe geometry, not separate destinations for SetMoveNav.
  public bool Direct;
  public bool Found {get{return Points.Length>0;}}
 }
 public sealed class FieldMovingObstacle {
  public FieldPoint Position,Velocity;
  public double Radius,HalfHeight;
 }
 public static class FieldAvoidancePlanner {
  static double Clamp(double x,double a,double b){return Math.Max(a,Math.Min(b,x));}
  public static double SegmentDistance(FieldPoint a,FieldPoint b,FieldPoint point){
   var d=b-a;double n=d.Squared;return FieldPoint.Distance(point,a+d*(n<=1e-12?0:Clamp(FieldPoint.Dot(point-a,d)/n,0,1)));
  }
  public static bool Clear(FieldPoint[] path,IEnumerable<FieldExclusion> exclusions,bool escape=true){
   if(path==null||path.Length==0||path.Any(p=>!p.Finite))return false;
   foreach(var hazard in exclusions){
    if(hazard==null||!hazard.Center.Finite||double.IsNaN(hazard.Radius)||double.IsInfinity(hazard.Radius)||hazard.Radius<=0)return false;
    double start=FieldPoint.Distance(path[0],hazard.Center);
    for(int i=1;i<path.Length;i++){
     if(SegmentDistance(path[i-1],path[i],hazard.Center)>=hazard.Radius-1e-6)continue;
     // Replanning may begin inside a moving monster's margin. Permit only an
     // outward escape, never a crossing through it or an inward first step.
     double next=FieldPoint.Distance(path[i],hazard.Center);
     if(escape&&start<hazard.Radius&&next>start+1e-6&&FieldPoint.Dot(path[i-1]-hazard.Center,path[i]-path[i-1])>=-1e-8){start=next;continue;}
     return false;
    }
    if(FieldPoint.Distance(path[path.Length-1],hazard.Center)<hazard.Radius-1e-6)return false;
   }
   return true;
  }
  public static double Length(FieldPoint[] points){double value=0;for(int i=1;i<points.Length;i++)value+=FieldPoint.Distance(points[i-1],points[i]);return value;}
  public static bool MotionClear(FieldPoint[] path,double speed,FieldMovingObstacle[] obstacles,double horizon){
   if(path==null||path.Length==0||speed<=0||double.IsNaN(speed)||double.IsInfinity(speed)||path.Any(p=>!p.Finite))return false;
   double elapsed=0;var point=path[0];
   for(int i=1;i<path.Length&&elapsed<horizon;i++){
    double length=FieldPoint.Distance(point,path[i]);if(length<1e-8){point=path[i];continue;}
    double duration=Math.Min(length/speed,horizon-elapsed);var velocity=(path[i]-point)*(speed/length);
    foreach(var o in obstacles)if(Crossing(point,velocity,o.Position+o.Velocity*elapsed,o.Velocity,o.Radius,o.HalfHeight,duration))return false;
    point=point+velocity*duration;elapsed+=duration;
   }
   foreach(var o in obstacles)if(Crossing(point,new FieldPoint(),o.Position+o.Velocity*elapsed,o.Velocity,o.Radius,o.HalfHeight,Math.Max(0,horizon-elapsed)))return false;
   return true;
  }
  public static FieldAvoidancePlan Plan(FieldPoint start,FieldPoint goal,FieldExclusion[] hazards,
    Func<FieldPoint,FieldPoint,FieldPoint[]> nativePath,Func<FieldPoint,FieldPoint?> project,int maxQueries=256){
   var answer=new FieldAvoidancePlan();
   if(!start.Finite||!goal.Finite||hazards==null||maxQueries<1){answer.Reason="invalid_input";return answer;}
   if(hazards.Any(h=>h==null||!h.Center.Finite||h.Radius<=0||double.IsNaN(h.Radius)||double.IsInfinity(h.Radius))){answer.Reason="invalid_hazard";return answer;}
   foreach(var h in hazards)if(FieldPoint.Distance(goal,h.Center)<h.Radius-1e-6){answer.Reason="goal_in_contact_zone";return answer;}
   var direct=nativePath(start,goal);answer.Queries++;
   if(direct==null||direct.Length==0||FieldPoint.Distance(direct[0],start)>.3||FieldPoint.Distance(direct[direct.Length-1],goal)>.3){answer.Reason="native_path_unavailable";return answer;}
   if(Clear(direct,hazards)){answer.Points=direct;answer.Length=Length(direct);answer.Direct=true;return answer;}
   var nodes=new List<FieldPoint>{start,goal};const int sides=16;
   // Ring chords clear the true contact margin. Candidate projection may bend
   // them around the actual terrain; every resulting native edge is rechecked.
   foreach(var h in hazards.OrderBy(h=>Enumerable.Range(1,Math.Max(0,direct.Length-1)).Select(i=>SegmentDistance(direct[i-1],direct[i],h.Center)).DefaultIfEmpty(FieldPoint.Distance(start,h.Center)).Min()).Take(8)){
    double radius=(h.Radius+.12)/Math.Cos(Math.PI/sides);
    for(int i=0;i<sides;i++){
     double a=2*Math.PI*i/sides;var p=project(new FieldPoint(h.Center.X+Math.Cos(a)*radius,h.Center.Y,h.Center.Z+Math.Sin(a)*radius));
     if(!p.HasValue||!p.Value.Finite||nodes.Any(n=>FieldPoint.Distance(n,p.Value)<.08)||hazards.Any(x=>FieldPoint.Distance(p.Value,x.Center)<x.Radius-1e-6))continue;
     nodes.Add(p.Value);
    }
   }
   var cost=Enumerable.Repeat(double.PositiveInfinity,nodes.Count).ToArray();cost[0]=0;
   // Straight-line distance is a poor heuristic on maps with walls and long
   // corridors: it spends the whole budget exploring harmless ring points.
   // Reuse native paths as the obstacle-free lower bound, and cache each edge.
   var native=new Dictionary<int,FieldPoint[]>();native[1]=direct;
   var estimate=Enumerable.Repeat(double.NaN,nodes.Count).ToArray();estimate[0]=Length(direct);estimate[1]=0;
   Func<int,int,FieldPoint[]> edge=(a,b)=>{
    int key=a*nodes.Count+b;FieldPoint[] value;if(native.TryGetValue(key,out value))return value;
    if(answer.Queries>=maxQueries)return null;
    value=nativePath(nodes[a],nodes[b]);answer.Queries++;native[key]=value;return value;
   };
   Func<int,double> heuristic=n=>{
    if(!double.IsNaN(estimate[n]))return estimate[n];
    var path=edge(n,1);return estimate[n]=path!=null&&path.Length>1?Length(path):FieldPoint.Distance(nodes[n],goal);
   };
   var visited=new bool[nodes.Count];var parent=Enumerable.Repeat(-1,nodes.Count).ToArray();var legs=new FieldPoint[nodes.Count][];
   bool exhausted=false;
   for(int expand=0;expand<nodes.Count;expand++){
    int current=-1;double score=double.PositiveInfinity;
    for(int i=0;i<nodes.Count;i++)if(!visited[i]&&!double.IsPositiveInfinity(cost[i])&&cost[i]+heuristic(i)<score){current=i;score=cost[i]+heuristic(i);}
    if(current<0)break;
    if(current==1||exhausted&&parent[1]>=0){
     var chain=new List<int>();for(int n=1;n!=0;n=parent[n]){if(n<0){answer.Reason="invalid_parent";return answer;}chain.Add(n);}chain.Reverse();
     var result=new List<FieldPoint>{start};foreach(int n in chain)result.AddRange(legs[n].Skip(1));
     answer.Points=result.ToArray();answer.Length=cost[1];return answer;
    }
    visited[current]=true;
    foreach(int next in Enumerable.Range(1,nodes.Count-1).Where(i=>!visited[i]).OrderBy(i=>FieldPoint.Distance(nodes[current],nodes[i])+FieldPoint.Distance(nodes[i],goal))){
     if(cost[current]+FieldPoint.Distance(nodes[current],nodes[next])>=cost[next]-1e-8)continue;
     if(answer.Queries>=maxQueries&&!native.ContainsKey(current*nodes.Count+next)){exhausted=true;continue;}
     var path=edge(current,next);
     if(path==null||path.Length<2||FieldPoint.Distance(path[0],nodes[current])>.3||FieldPoint.Distance(path[path.Length-1],nodes[next])>.3||!Clear(path,hazards))continue;
     double distance=cost[current]+Length(path);if(distance>=cost[next]-1e-8)continue;
     cost[next]=distance;parent[next]=current;legs[next]=path;
    }
    if(exhausted&&parent[1]<0)break;
   }
   answer.Reason=exhausted?"query_budget":"local_detour_unavailable";return answer;
  }
  // Predict one crossing, not the union of the moving object's whole track.
  // Different floors never conflict. Negative time and non-finite data reject.
  public static bool Crossing(FieldPoint player,FieldPoint playerVelocity,FieldPoint obstacle,FieldPoint obstacleVelocity,double radius,double halfHeight,double horizon){
   if(!player.Finite||!playerVelocity.Finite||!obstacle.Finite||!obstacleVelocity.Finite||radius<=0||halfHeight<=0||horizon<0||double.IsNaN(horizon)||double.IsInfinity(horizon))return false;
   var relative=player-obstacle;var velocity=playerVelocity-obstacleVelocity;
   if(double.IsNaN(radius)||double.IsInfinity(radius)||double.IsNaN(halfHeight)||double.IsInfinity(halfHeight))return false;
   double from=0,to=horizon;
   if(Math.Abs(velocity.Y)<1e-12){if(Math.Abs(relative.Y)>=halfHeight)return false;}
   else {double a=(-halfHeight-relative.Y)/velocity.Y,b=(halfHeight-relative.Y)/velocity.Y;from=Math.Max(from,Math.Min(a,b));to=Math.Min(to,Math.Max(a,b));if(to<from)return false;}
   double planar=velocity.X*velocity.X+velocity.Z*velocity.Z;
   double t=planar<1e-12?from:Clamp(-(relative.X*velocity.X+relative.Z*velocity.Z)/planar,from,to);
   var near=relative+velocity*t;
   return Math.Abs(near.Y)<=halfHeight&&near.X*near.X+near.Z*near.Z<radius*radius;
  }
 }
}

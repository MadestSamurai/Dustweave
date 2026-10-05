using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace BD2Territory
{
 public struct RoutePoint
 {
  public double X,Y,Z;public RoutePoint(double x,double y,double z){X=x;Y=y;Z=z;}
  public static double Distance(RoutePoint a,RoutePoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
 }
 public static class LocalStandPoints
 {
  public static IEnumerable<RoutePoint> Create(RoutePoint center,RoutePoint from,double reach,bool nearSurface=false)
  {
   if(reach<=0||reach>10)yield break;double angle=Math.Atan2(from.Z-center.Z,from.X-center.X);
   foreach(double fraction in nearSurface?new[]{.96,.9,.8,.65,.5,.35}:new[]{.96,.9,.8,.65})for(int i=0;i<32;i++)
   {double a=angle+i*Math.PI/16;yield return new RoutePoint(center.X+Math.Cos(a)*reach*fraction,center.Y,center.Z+Math.Sin(a)*reach*fraction);}
  }
 }
 public enum RouteSearchState { Searching, Found, Exhausted }
 // Incremental A*: actual terrain/capsule queries supplied by the main-thread adapter, no NavMesh dependency.
 public sealed class LocalRouteSearch
 {
  private sealed class Node { public int X,Z;public bool Anchor;public RoutePoint P;public double Cost;public Node Parent; }
  private sealed class Entry {public Node Node;public double Cost,Score;public long Order;}
  private readonly SortedSet<Entry> open=new SortedSet<Entry>(Comparer<Entry>.Create((a,b)=>{int c=a.Score.CompareTo(b.Score);return c!=0?c:a.Order.CompareTo(b.Order);}));
  private struct GridKey : IEquatable<GridKey>
  {
   public int X,Z,H;public GridKey(int x,int z,double y){X=x;Z=z;H=(int)Math.Round(y*20);}
   public bool Equals(GridKey b)=>X==b.X&&Z==b.Z&&H==b.H;
   public override bool Equals(object b)=>b is GridKey&&Equals((GridKey)b);
   public override int GetHashCode(){unchecked{return (X*397^Z)*397^H;}}
  }
  private readonly Dictionary<GridKey,Node> nodes=new Dictionary<GridKey,Node>();private readonly Dictionary<RouteSampleKey,RoutePoint?> samples=new Dictionary<RouteSampleKey,RoutePoint?>();
  private readonly Func<RoutePoint,RoutePoint?> sample;private readonly Func<RoutePoint,RoutePoint,bool> clear;
  private readonly RoutePoint[] anchors;private readonly Dictionary<GridKey,Node> anchorNodes=new Dictionary<GridKey,Node>();
  private readonly RoutePoint origin,center;private readonly RoutePoint[] goals;private readonly double step,radius,minX,maxX,minZ,maxZ;private readonly int limit;private long serial;
  public RouteSearchState State{get;private set;}public RoutePoint[] Path{get;private set;}=new RoutePoint[0];public int Expanded{get;private set;}
  public override string ToString()=>"state="+State+" expanded="+Expanded+" open="+open.Count+" samples="+samples.Count+" goals="+goals.Length+" path="+Path.Length;

  public LocalRouteSearch(RoutePoint start,RoutePoint[] destinations,Func<RoutePoint,RoutePoint?> samplePoint,Func<RoutePoint,RoutePoint,bool> edgeClear,double cell=.4,double margin=8,int maxNodes=18000,bool worldAligned=false,RoutePoint[] guides=null)
  {
   if(cell<=0||margin<0||maxNodes<1)throw new ArgumentException("Invalid local route bounds");
   anchors=guides??new RoutePoint[0];
   origin=worldAligned?new RoutePoint(Math.Round(start.X/cell)*cell,start.Y,Math.Round(start.Z/cell)*cell):start;goals=destinations;sample=samplePoint;clear=edgeClear;step=cell;limit=maxNodes;
   if(goals.Length==0){State=RouteSearchState.Exhausted;return;}
   center=new RoutePoint(goals.Average(p=>p.X),start.Y,goals.Average(p=>p.Z));radius=goals.Max(p=>RoutePoint.Distance(p,center));
   minX=Math.Min(start.X,goals.Min(p=>p.X))-margin;maxX=Math.Max(start.X,goals.Max(p=>p.X))+margin;
   minZ=Math.Min(start.Z,goals.Min(p=>p.Z))-margin;maxZ=Math.Max(start.Z,goals.Max(p=>p.Z))+margin;
   var first=new Node{P=start};nodes[new GridKey(0,0,start.Y)]=first;Push(first);
  }
  private double Estimate(RoutePoint p)=>Math.Max(0,RoutePoint.Distance(p,center)-radius);
  private void Push(Node n){open.Add(new Entry{Node=n,Cost=n.Cost,Score=n.Cost+Estimate(n.P),Order=serial++});}
  public RouteSearchState Step(int budget,double milliseconds=6)
  {
   if(State!=RouteSearchState.Searching)return State;var clock=Stopwatch.StartNew();int used=0;
   while(open.Count>0&&used<budget&&(used==0||clock.Elapsed.TotalMilliseconds<milliseconds))
   {
    var entry=open.Min;open.Remove(entry);var n=entry.Node;if(entry.Cost!=n.Cost)continue;used++;Expanded++;
    foreach(var goal in goals)if(RoutePoint.Distance(n.P,goal)<=step*1.8&&clear(n.P,goal))
    {var path=new List<RoutePoint>{goal};for(var back=n;back!=null;back=back.Parent)path.Add(back.P);path.Reverse();Path=path.ToArray();State=RouteSearchState.Found;return State;}
    if(Expanded>=limit){State=RouteSearchState.Exhausted;return State;}
    // Native bridge centre lines are sparse extra vertices, not permission to cross water.
    // Validate every connector with the same ground / water / capsule rules as grid edges.
    for(int i=0;i<anchors.Length;i++)
    {
     var guide=anchors[i];double distance=RoutePoint.Distance(n.P,guide);
     if(distance<.0001||distance>step*1.8||guide.X<minX||guide.X>maxX||guide.Z<minZ||guide.Z>maxZ)continue;
     var sampleKey=new RouteSampleKey(new RoutePoint(guide.X,n.P.Y,guide.Z));
     if(!samples.TryGetValue(sampleKey,out var floor)){floor=sample(sampleKey.Point);samples[sampleKey]=floor;}
     if(!floor.HasValue)continue;var point=floor.Value;double cost=n.Cost+distance;
     var guideKey=new GridKey(i,0,point.Y);
     if(anchorNodes.TryGetValue(guideKey,out var next)&&next.Cost<=cost)continue;
     if(!clear(n.P,point))continue;
     if(next==null){next=new Node{Anchor=true,X=(int)Math.Round((point.X-origin.X)/step),Z=(int)Math.Round((point.Z-origin.Z)/step)};anchorNodes[guideKey]=next;}
     next.P=point;next.Cost=cost;next.Parent=n;Push(next);
    }
    for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
    {
     if(dx==0&&dz==0&&!n.Anchor)continue;int x=n.X+dx,z=n.Z+dz;var p=new RoutePoint(Math.Round(origin.X+x*step,5),n.P.Y,Math.Round(origin.Z+z*step,5));
     if(p.X<minX||p.X>maxX||p.Z<minZ||p.Z>maxZ)continue;
     var sampleKey=new RouteSampleKey(p);if(!samples.TryGetValue(sampleKey,out var ground)){ground=sample(p);samples[sampleKey]=ground;}
     if(!ground.HasValue)continue;p=ground.Value;double cost=n.Cost+RoutePoint.Distance(n.P,p);
     var key=new GridKey(x,z,p.Y);if(nodes.TryGetValue(key,out var next)&&next.Cost<=cost)continue;
     // Sweep the whole edge; diagonal corners cannot cut through a solid corner.
     if(!clear(n.P,p))continue;
     if(next==null){next=new Node{X=x,Z=z};nodes[key]=next;}
     next.P=p;next.Cost=cost;next.Parent=n;Push(next);
    }
   }
   if(open.Count==0)State=RouteSearchState.Exhausted;return State;
  }
 }
 public sealed class LocalRecoveryBudget
 {
  public int Plans{get;private set;}private long started;
  public override string ToString()=>"plans="+Plans+" startedUtcTicks="+started;
  public void Reset(){Plans=0;started=0;}
  public bool Expired(long now)=>Plans>0&&now-started>=TimeSpan.FromSeconds(90).Ticks;
  public bool Plan(long now){if(Plans>=6||Expired(now))return false;if(Plans==0)started=now;Plans++;return true;}
 }
}

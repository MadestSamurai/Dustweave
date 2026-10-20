#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Territory
{
 // Exact sampled coordinates: no nearby point is assumed to share floor or clearance.
 public struct RouteSampleKey : IEquatable<RouteSampleKey>
 {
  private readonly double x,y,z;
  public RouteSampleKey(RoutePoint p){x=p.X;y=p.Y;z=p.Z;}
  public RoutePoint Point=>new RoutePoint(x,y,z);
  public bool Equals(RouteSampleKey v)=>x==v.x&&y==v.y&&z==v.z;
  public override bool Equals(object v)=>v is RouteSampleKey&&Equals((RouteSampleKey)v);
  public override int GetHashCode(){unchecked{return (x.GetHashCode()*397^y.GetHashCode())*397^z.GetHashCode();}}
 }
 public struct RouteEdgeKey : IEquatable<RouteEdgeKey>
 {
  private readonly RouteSampleKey a,b;
  public RouteEdgeKey(RoutePoint from,RoutePoint to){a=new RouteSampleKey(from);b=new RouteSampleKey(to);}
  public bool Touches(RouteRegion r)=>r.Touches(a.Point,b.Point);
  public bool Near(RoutePoint point,double radius)=>RoutePoint.Distance(a.Point,point)<=radius||RoutePoint.Distance(b.Point,point)<=radius;
  public bool Equals(RouteEdgeKey v)=>a.Equals(v.a)&&b.Equals(v.b);
  public override bool Equals(object v)=>v is RouteEdgeKey&&Equals((RouteEdgeKey)v);
  public override int GetHashCode(){unchecked{return a.GetHashCode()*397^b.GetHashCode();}}
 }
 // Bounded FIFO memoization. Zero lifetime retains static geometry until explicitly invalidated.
 // Cache only static physics; executed corridors are checked against live colliders.
 public sealed class RouteMemo<TKey,TValue>
 {
  private sealed class Entry{public long At,Serial;public TValue Value;}
  private readonly Dictionary<TKey,Entry> entries=new Dictionary<TKey,Entry>();
  private readonly Queue<KeyValuePair<TKey,long>> order=new Queue<KeyValuePair<TKey,long>>();
  private readonly int capacity;private readonly long lifetime;private long serial;
  public int Count=>entries.Count;public long Hits{get;private set;}public long Misses{get;private set;}
  public RouteMemo(int capacity,long lifetime=0){if(capacity<1||lifetime<0)throw new ArgumentOutOfRangeException();this.capacity=capacity;this.lifetime=lifetime;}
  public TValue Get(TKey key,long now,Func<TValue> compute)
  {
   if(entries.TryGetValue(key,out var found)&&now>=found.At&&(lifetime==0||now-found.At<lifetime)){Hits++;return found.Value;}
   Misses++;var value=compute();var entry=new Entry{At=now,Serial=++serial,Value=value};entries[key]=entry;order.Enqueue(new KeyValuePair<TKey,long>(key,entry.Serial));
   while(order.Count>capacity){var old=order.Dequeue();if(entries.TryGetValue(old.Key,out var e)&&e.Serial==old.Value)entries.Remove(old.Key);}
   return value;
  }
  public void RemoveWhere(Func<TKey,bool> predicate){foreach(var key in entries.Keys.Where(predicate).ToArray())entries.Remove(key);}
  public void Clear(){entries.Clear();order.Clear();}
 }
 public sealed class LocalRouteMemory
 {
  private sealed class Saved{public int Target;public long At;public RoutePoint[] Points;}
  private readonly List<Saved> paths=new List<Saved>();
  public void Clear(){paths.Clear();}
  public void Forget(int target){paths.RemoveAll(p=>p.Target==target);}
  public void Save(int target,long now,RoutePoint[] points)
  {if(points==null||points.Length<2)return;Forget(target);paths.Add(new Saved{Target=target,At=now,Points=(RoutePoint[])points.Clone()});if(paths.Count>64)paths.RemoveAt(0);}
  public void Invalidate(RouteRegion region)
  {paths.RemoveAll(p=>p.Points.Zip(p.Points.Skip(1),(a,b)=>region.Touches(a,b)).Any(v=>v));}
  public RoutePoint[] ReuseCorridor(RoutePoint from,RoutePoint[] goals,Func<RoutePoint,RoutePoint,bool> connector)
  {
   // A route belongs to geometry, not to a crop instance. Reuse forward or backward
   // for a new nearby target, with real connectors and live execution checks.
   var options=new List<Tuple<double,RoutePoint[],int,int,RoutePoint>>();
   foreach(var saved in paths)
   {
    var points=saved.Points;var joins=Enumerable.Range(0,points.Length).Where(i=>RoutePoint.Distance(from,points[i])<=1.4).ToArray();
    if(joins.Length==0||goals.Length==0)continue;
    var lengths=new double[points.Length];for(int k=1;k<points.Length;k++)lengths[k]=lengths[k-1]+RoutePoint.Distance(points[k-1],points[k]);
    for(int j=0;j<points.Length;j++)
    {
     var near=goals[0];double last=double.PositiveInfinity;
     foreach(var goal in goals){double d=RoutePoint.Distance(goal,points[j]);if(d<last){last=d;near=goal;}}
     if(last>1.4)continue;
     foreach(int i in joins)if(i!=j)options.Add(Tuple.Create(RoutePoint.Distance(from,points[i])+Math.Abs(lengths[j]-lengths[i])+last,points,i,j,near));
    }
   }
   foreach(var item in options.OrderBy(v=>v.Item1).Take(8))
   {
    var points=item.Item2;int i=item.Item3,j=item.Item4;
    if(!connector(from,points[i])||!connector(points[j],item.Item5))continue;
    var middle=i<j?points.Skip(i).Take(j-i+1):points.Skip(j).Take(i-j+1).Reverse();
    return new[]{from}.Concat(middle).Concat(new[]{item.Item5}).ToArray();
   }
   return null;
  }
  public RoutePoint[] Reuse(int target,long now,RoutePoint from,Func<RoutePoint,bool> goalAllowed,Func<RoutePoint,RoutePoint,bool> connector)
  {
   paths.RemoveAll(p=>now<p.At);
   var saved=paths.LastOrDefault(p=>p.Target==target);if(saved==null||!goalAllowed(saved.Points.Last()))return null;
   // Join only the local part of a saved corridor. Every executed edge is rechecked by the adapter.
   for(int i=saved.Points.Length-1;i>=1;i--)if(RoutePoint.Distance(from,saved.Points[i])<=1.4&&connector(from,saved.Points[i]))return new[]{from}.Concat(saved.Points.Skip(i)).ToArray();
   return null;
  }
 }
 public sealed class AdaptiveLocalRoute
 {
  private LocalRouteSearch search;private int previous;private readonly RoutePoint start;private readonly RoutePoint[] goals,guides;
  private readonly Func<RoutePoint,RoutePoint?> sample;private readonly Func<RoutePoint,RoutePoint,bool> edge;private readonly double margin;
  public double Cell{get;private set;}public int Expanded=>previous+search.Expanded;public RoutePoint[] Path=>search.Path;public RouteSearchState State=>search.State;
  public AdaptiveLocalRoute(RoutePoint start,RoutePoint[] goals,Func<RoutePoint,RoutePoint?> sample,Func<RoutePoint,RoutePoint,bool> edge,bool fine=false,double margin=8,RoutePoint[] guides=null)
  {this.start=start;this.goals=goals;this.sample=sample;this.edge=edge;this.margin=margin;this.guides=guides;Cell=fine?.1:.4;Begin();}
  private void Begin(){search=new LocalRouteSearch(start,goals,sample,edge,Cell,margin,Cell>.1?4000:18000,true,guides);}
  public bool Refine(){if(Cell<=.1)return false;previous+=search.Expanded;Cell=.1;Begin();return true;}
  public RouteSearchState Step(int nodes,double ms=6)
  {
   var state=search.Step(nodes,ms);
   if(state==RouteSearchState.Exhausted&&Refine())return RouteSearchState.Searching;
   return state;
  }
 }
}

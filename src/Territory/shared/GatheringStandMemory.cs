using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Territory
{
 // Remember actual failed attack positions, independently of path-planner resets and target switches.
 public sealed class GatheringStandMemory
 {
  private sealed class Miss {public RoutePoint Point;public long At;}
  private readonly Dictionary<int,List<Miss>> targets=new Dictionary<int,List<Miss>>();
  public const double Separation=.25;
  private static readonly long Lifetime=TimeSpan.FromMinutes(5).Ticks;
  private void Prune(long now)
  {foreach(var id in targets.Keys.ToArray()){targets[id].RemoveAll(m=>now-m.At>=Lifetime);if(targets[id].Count==0)targets.Remove(id);}}
  public void Record(int target,RoutePoint point,long now)
  {
   Prune(now);
   if(!targets.TryGetValue(target,out var list))
   {
    if(targets.Count>=128)targets.Remove(targets.OrderBy(x=>x.Value.Last().At).First().Key);
    targets[target]=list=new List<Miss>();
   }
   list.RemoveAll(m=>RoutePoint.Distance(m.Point,point)<.08);list.Add(new Miss{Point=point,At=now});
   if(list.Count>8)list.RemoveAt(0);
  }
  public bool Allows(int target,RoutePoint point,long now)
  {return !targets.TryGetValue(target,out var list)||!list.Any(m=>now-m.At<Lifetime&&RoutePoint.Distance(m.Point,point)<Separation);}
  public void Succeeded(int target){targets.Remove(target);}
  public void Clear(){targets.Clear();}
  public override string ToString()=>"failedTargets="+targets.Count+" stands="+targets.Values.Sum(v=>v.Count);
 }
}

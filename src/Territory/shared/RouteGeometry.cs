using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Territory
{
 public struct RouteRegion
 {
  public double MinX,MinZ,MaxX,MaxZ;
  public RouteRegion(double minX,double minZ,double maxX,double maxZ){MinX=minX;MinZ=minZ;MaxX=maxX;MaxZ=maxZ;}
  public bool Contains(RoutePoint p)=>p.X>=MinX&&p.X<=MaxX&&p.Z>=MinZ&&p.Z<=MaxZ;
  public bool Touches(RoutePoint a,RoutePoint b)=>Math.Max(a.X,b.X)>=MinX&&Math.Min(a.X,b.X)<=MaxX&&Math.Max(a.Z,b.Z)>=MinZ&&Math.Min(a.Z,b.Z)<=MaxZ;
 }
 public sealed class RouteGeometryStamp
 {public int Id;public string Revision="";public RouteRegion Region;}
 // Compare copied geometry, never references to mutable Unity objects. Inventory changes,
 // cooking replies and non-colliding NPC movement are deliberately absent.
 public sealed class RouteGeometryChanges
 {
  private Dictionary<int,RouteGeometryStamp> before=new Dictionary<int,RouteGeometryStamp>();
  public void Clear(){before.Clear();}
  public RouteRegion[] Update(IEnumerable<RouteGeometryStamp> current)
  {
   var next=current.ToDictionary(v=>v.Id);var dirty=new List<RouteRegion>();
   foreach(var old in before.Values)if(!next.TryGetValue(old.Id,out var item)||old.Revision!=item.Revision)dirty.Add(old.Region);
   foreach(var item in next.Values)if(!before.TryGetValue(item.Id,out var old)||old.Revision!=item.Revision)dirty.Add(item.Region);
   before=next;return dirty.ToArray();
  }
}
}

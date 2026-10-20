using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private readonly RouteMemo<RouteSampleKey,RoutePoint?> localGround=new RouteMemo<RouteSampleKey,RoutePoint?>(65536);
  private readonly RouteMemo<RouteEdgeKey,bool> localEdges=new RouteMemo<RouteEdgeKey,bool>(131072);
  private readonly RouteMemo<RouteSampleKey,bool> localClear=new RouteMemo<RouteSampleKey,bool>(65536);
  private bool CachedTraversalClear(Vector3 p)=>localClear.Get(new RouteSampleKey(Point(p)),DateTime.UtcNow.Ticks,()=>TraversalClear(p));
  private readonly LocalRouteMemory localPaths=new LocalRouteMemory();private string localCacheContext="";private int warmIndex;
  private readonly RouteGeometryChanges localGeometry=new RouteGeometryChanges();private long localGeometryScan=-1;
  private RoutePoint? CachedGround(RoutePoint p)
  {return localGround.Get(new RouteSampleKey(p),DateTime.UtcNow.Ticks,()=>{Vector3 ground;return GroundPoint(Vector(p),out ground,false)?(RoutePoint?)Point(ground):null;});}
  private void InvalidateLocalArea(Vector3 point)
  {InvalidateLocalRegion(new RouteRegion(point.x-2,point.z-2,point.x+2,point.z+2));}
  private void InvalidateLocalRegion(RouteRegion region)
  {localGround.RemoveWhere(k=>region.Contains(k.Point));localClear.RemoveWhere(k=>region.Contains(k.Point));localEdges.RemoveWhere(k=>k.Touches(region));localPaths.Invalidate(region);}
  private RouteGeometryStamp GeometryStamp(Collider collider)
  {
   var b=collider.bounds;float margin=BodyRadius+.2f;
   var mesh=collider as MeshCollider;
   string shape=collider.GetType().Name+"/"+b.center.ToString("F4")+"/"+b.size.ToString("F4")+"/"+collider.transform.eulerAngles.ToString("F3")+"/"+(mesh==null||mesh.sharedMesh==null?0:mesh.sharedMesh.GetInstanceID());
   return new RouteGeometryStamp{Id=collider.GetInstanceID(),Revision=shape,Region=new RouteRegion(b.min.x-margin,b.min.z-margin,b.max.x+margin,b.max.z+margin)};
  }
  private void RefreshLocalCacheContext()
  {
   var body=Body;var gm=terrainController==null?null:terrainController.GetChunkManager();
   string shape=body==null?"default":body.radius.ToString("R")+"/"+body.height.ToString("R")+"/"+body.center.ToString("F4")+"/"+body.transform.lossyScale.ToString("F4")+"/"+StepHeight.ToString("R")+"/"+body.slopeLimit.ToString("R")+"/"+body.skinWidth.ToString("R");
   string map=gm==null?"missing":gm.GetInstanceID()+"/"+gm.transform.position.ToString("F4")+"/"+gm.transform.eulerAngles.ToString("F3")+"/"+gm.transform.lossyScale.ToString("F4");
   var life=B.Read("Inventory.User",null) as Proto.Net.LifeUserDBInfo;map+="/"+(life==null?"missing":life.LifeWorldId+"/"+string.Join(",",life.ChunkId.OrderBy(v=>v).Select(v=>v.ToString()).ToArray()));
   string context=farmScene+"/"+account+"/"+player.GetInstanceID()+"/"+RootLift.ToString("R")+"/"+shape+"/"+map;
   if(context!=localCacheContext)
   {localCacheContext=context;localGround.Clear();localClear.Clear();localEdges.Clear();localPaths.Clear();localGeometry.Clear();localGeometryScan=-1;warmIndex=0;LocalStorage.Log("寻路缓存重建：地图、账号或角色碰撞参数变化");}
   if(localGeometryScan==lastScan)return;localGeometryScan=lastScan;
   // Placed facilities and resource colliders can change. NPCs and inventory are not
   // static route geometry. Observe actual bounds, not footprint-based obstacles.
   var colliders=new Dictionary<int,Collider>();
   foreach(var obj in UnityEngine.Object.FindObjectsOfType<FieldEvent.Life.Chunk.LifePlaceableObject>())
    foreach(var collider in obj.GetComponentsInChildren<Collider>())if(MovementCollider(collider))colliders[collider.GetInstanceID()]=collider;
   foreach(var obj in nodes)if(obj!=null)
    foreach(var collider in obj.GetComponentsInChildren<Collider>())if(MovementCollider(collider))colliders[collider.GetInstanceID()]=collider;
   // Bridge triggers are passability evidence even though they are not obstacles.
   foreach(var gate in terrainGates){var box=LiveBridge(gate);if(box!=null)colliders[box.GetInstanceID()]=box;}
   var dirty=localGeometry.Update(colliders.Values.Select(GeometryStamp));
   if(dirty.Length>0)
   {
    // One dictionary pass for a batch of harvested / moved objects.
    localGround.RemoveWhere(k=>dirty.Any(r=>r.Contains(k.Point)));localClear.RemoveWhere(k=>dirty.Any(r=>r.Contains(k.Point)));localEdges.RemoveWhere(k=>dirty.Any(k.Touches));
    foreach(var region in dirty)localPaths.Invalidate(region);
    LocalStorage.Log("寻路缓存局部更新 regions="+dirty.Length+" ground="+localGround.Count+" edges="+localEdges.Count);
   }
  }
  private void WarmLocalGrid()
  {
   var watch=Stopwatch.StartNew();var from=player.transform.position;
   for(int n=0;n<4&&(n==0||watch.Elapsed.TotalMilliseconds<1);n++)
   {int i=warmIndex++%121;CachedGround(new RoutePoint(Math.Round(Math.Round(from.x/.4)*.4+(i%11-5)*.4,5),from.y,Math.Round(Math.Round(from.z/.4)*.4+(i/11-5)*.4,5)));}
  }
 }
}

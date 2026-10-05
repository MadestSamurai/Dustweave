using System;
using System.Linq;
using System.Reflection;
using FieldEvent.Life;
using FieldEvent.Life.Chunk;
using UnityEngine;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private LifeChunkController terrainController;
  private LifePlaceableObject_ColliderGate[] terrainGates=new LifePlaceableObject_ColliderGate[0];
  private static readonly FieldInfo gateBoxField=typeof(LifePlaceableObject_ColliderGate).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(f=>f.FieldType==typeof(BoxCollider));
  private string TerrainKind(Vector3 p)
  {
   var gm=terrainController==null?null:terrainController.GetChunkManager();if(gm==null)return "Unknown";
   var xy=terrainController.WorldPositionToWorldCell(p);var cell=gm.GetCellAtWorldCell(xy.x,xy.y);
   var chunk=cell.isValid?gm.FindChunkById(cell.chunkId):null;
   return chunk==null?"Unknown":chunk.GetCellType(cell.localX,cell.localY).ToString();
  }
  private BoxCollider LiveBridge(LifePlaceableObject_ColliderGate gate)
  {
   if(gate==null||!gate.isActiveAndEnabled)return null;
   var lpo=gate.GetComponent<LifePlaceableObject>();
   // A water decoration alone is not a bridge. Require the game's native crossing
   // gate on an existing placed object, never a temporary placement preview.
   if(lpo==null||!lpo.HasWaterCell()||B.Read("Farm.Db",lpo)==null||(bool)B.Read("Layout.Temporary",lpo))return null;
   var box=gateBoxField.GetValue(gate) as BoxCollider;
   return box!=null&&box.enabled&&box.isTrigger&&box.gameObject.activeInHierarchy?box:null;
  }
  private bool BridgeContains(BoxCollider box,Vector3 p)
  {
   var q=box.transform.InverseTransformPoint(p)-box.center;var scale=box.transform.lossyScale;
   float rx=BodyRadius/Math.Max(.001f,Math.Abs(scale.x)),rz=BodyRadius/Math.Max(.001f,Math.Abs(scale.z));
   return Math.Abs(q.x)+rx<=box.size.x*.5f+.00001f&&Math.Abs(q.z)+rz<=box.size.z*.5f+.00001f&&Math.Abs(q.y)<=box.size.y*.5f+.1f;
  }
  private bool BridgeCovers(Vector3 a,Vector3 b)
  {foreach(var gate in terrainGates){var box=LiveBridge(gate);if(box!=null&&BridgeContains(box,a)&&BridgeContains(box,b))return true;}return false;}
  private bool SurfaceAllowed(Vector3 p,out bool bridge)
  {
   bridge=false;string kind=TerrainKind(p);if(kind=="Ground")return true;
   if(kind!="Water")return false;bridge=BridgeCovers(p,p);return bridge;
  }
  private bool SurfaceAllowed(Vector3 p){bool bridge;return SurfaceAllowed(p,out bridge);}
  private bool SurfaceSegmentAllowed(Vector3 a,Vector3 b)
  {
   var gm=terrainController==null?null:terrainController.GetChunkManager();if(gm==null)return false;
   // Native conversion includes the map rotation and centring offset. Use its cell
   // origin so rotated river maps receive exactly the same cell boundaries.
   var zero=gm.transform.InverseTransformPoint(gm.WorldCellToWorldPosition(0,0));
   var ca=gm.transform.InverseTransformPoint(a)-zero;var cb=gm.transform.InverseTransformPoint(b)-zero;
   var cuts=SurfaceSegments.Cuts(ca.x/GroundChunk.CELL_SIZE,ca.z/GroundChunk.CELL_SIZE,cb.x/GroundChunk.CELL_SIZE,cb.z/GroundChunk.CELL_SIZE,FlatDistance(a,b));
   for(int i=1;i<cuts.Length;i++)
   {
    var start=Vector3.Lerp(a,b,(float)cuts[i-1]);var end=Vector3.Lerp(a,b,(float)cuts[i]);
    string kind=TerrainKind(Vector3.Lerp(a,b,(float)((cuts[i-1]+cuts[i])*.5)));
    if(kind=="Ground")continue;
    if(kind!="Water"||!BridgeCovers(start,end))return false;
   }
   return true;
  }
  private bool SurfacePathAllowed(Vector3[] path)
  {if(path==null||path.Length==0)return false;if(!SurfaceAllowed(path[0]))return false;for(int i=1;i<path.Length;i++)if(!SurfaceSegmentAllowed(path[i-1],path[i]))return false;return SurfaceAllowed(path[path.Length-1]);}
  private bool BridgeGround(Collider collider)
  {
   // The native crossing gate can ignore the large floor collider together with
   // river-bank walls. That does not remove authored bridge support, but never
   // accepts arbitrary ignored decorations as a floor.
   if(collider==null||!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy)return false;
   if(collider.GetComponentInParent<GroundChunkManager>()!=null)return true;
   var lpo=collider.GetComponentInParent<LifePlaceableObject>();
   return lpo!=null&&terrainGates.Any(g=>g!=null&&g.GetComponent<LifePlaceableObject>()==lpo&&LiveBridge(g)!=null);
  }
  private RoutePoint[] BridgeRouteGuides()
  {
   var points=new System.Collections.Generic.List<RoutePoint>();
   foreach(var gate in terrainGates)
   {
    var box=LiveBridge(gate);if(box==null)continue;
    // The native gate's local Z is the bridge length. Include a short approach on
    // either bank; all samples still require actual ground and valid water cover.
    float scale=Math.Max(.001f,Math.Abs(box.transform.lossyScale.z));
    float half=box.size.z*.5f+.65f/scale;int count=Math.Max(1,(int)Math.Ceiling(half*2*scale/.1f));
    for(int i=0;i<=count;i++)
    {var local=box.center;local.y-=box.size.y*.5f;local.z+=-half+half*2*i/count;points.Add(Point(box.transform.TransformPoint(local)));}
   }
   return points.ToArray();
  }
  private string SurfaceContext()
  {
   var gm=terrainController==null?null:terrainController.GetChunkManager();
   return (gm==null?"missing":gm.GetInstanceID().ToString())+"/"+string.Join(";",terrainGates.OrderBy(g=>g==null?0:g.GetInstanceID()).Select(g=>{var b=LiveBridge(g);return b==null?"missing":b.GetInstanceID()+"/"+b.transform.position.ToString("F4")+"/"+b.transform.eulerAngles.ToString("F3")+"/"+b.transform.lossyScale.ToString("F4")+"/"+b.center.ToString("F4")+"/"+b.size.ToString("F4");}).ToArray());
  }
 }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

static class TerritoryNavigationCases
{
    public static void Run(List<string> cases)
    {
        // Run the production planner and movement adapter. Only native world queries
        // and movement are replaced by a deterministic world; no game is connected.
        var paths = new[] { "hook/RuntimeLocalNavigation.cs", "shared/LocalRoute.cs",
            "shared/LocalNavigationCache.cs", "shared/RouteGeometry.cs", "shared/TraversalRules.cs" };
        var sources = paths.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(
            Path.Combine(TestPaths.SourceRoot, "src/Territory", p)), path: p)).Append(CSharpSyntaxTree.ParseText(Fixture));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("TerritoryNavigation" + Guid.NewGuid().ToString("N"), sources, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var buffer = new MemoryStream(); var emitted = compilation.Emit(buffer);
        if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        try { cases.AddRange((string[])Assembly.Load(buffer.ToArray()).GetType("BD2Territory.Runtime.RuntimeEngine")!
            .GetMethod("Exercise")!.Invoke(null, null)!); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }

    const string Fixture = """
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude==0?new Vector3():this*(1/magnitude);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float s)=>new Vector3(a.x*s,a.y*s,a.z*s);
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
  public static Vector3 ClampMagnitude(Vector3 a,float m)=>a.magnitude>m?a.normalized*m:a;
  public string ToString(string format)=>x.ToString(format)+","+y.ToString(format)+","+z.ToString(format);
 }
 public static class Mathf { public static float Clamp(float x,float a,float b)=>Math.Max(a,Math.Min(b,x)); }
 public class Transform { public Vector3 position; }
 public class Component {
  public int Id=42; public Transform transform=new Transform(); public int GetInstanceID()=>Id;
  public T GetComponent<T>() where T:new()=>new T();
 }
 public class Collider:Component {}
}
namespace UnityEngine.AI { public class NavMeshAgent { public bool isActiveAndEnabled,isOnNavMesh; public void ResetPath(){} } }
public class Player:Component { public Vector3 Facing; public int Starts; public bool IsGetOnVehicle()=>false; public void GetOffVehicle(){} }
public class Movement { public int Stops; public void ClearMove(){} public void StopMove(){Stops++;} }
public class LifeFarmFieldObject:Component { public bool IsPlayerInInteractionRange(Vector3 p)=>Vector3.Distance(p,transform.position)<.75f; }
public class LifeGatheringObject:Component {}
public class CircleSectorCollider { public bool IsDetectionInCollisionArea(Vector3 p)=>true; }
public class QuestNavigationManager {}
namespace BD2Territory {
 public class TerritorySnapshot { public string Scene="field",Reason=""; }
 public class TerritoryControl {}
}
namespace BD2Territory.Runtime {
 static class TerritoryBindings {
  public static object Read(string name,object owner)=>name=="Detector.Distance"?(object).7f:null;
  public static object Singleton(Type type)=>null;
  public static object EnumObject(string type,string name)=>name;
  public static void InvokeOn(string name,object owner,params object[] args){
   if(name=="Player.Face")((Player)owner).Facing=(Vector3)args[0];
   if(name=="Player.StartMove")((Player)owner).Starts++;
  }
 }
 static class LocalStorage { public static void Log(string message){} }
 sealed class Navigation { public void CancelAttempt(){} public void Reset(){} public void Select(int id){} public void Arrived(long now){} }
 internal sealed partial class RuntimeEngine {
  private Player player=new Player(); private Movement move=new Movement();
  private Navigation navigation=new Navigation(),interaction=new Navigation();
  private Component walkTarget; private string walkScene; private Vector3 finalDestination,saffronAccessPoint;
  private bool ownsMove,bypassing,gatheringReposition,vehiclePending; private long lastInput;
  private Collider blockingCollider; private List<Vector3> triedDestinations=new List<Vector3>();
  private readonly RouteMemo<RouteSampleKey,RoutePoint?> localGround=new RouteMemo<RouteSampleKey,RoutePoint?>(65536);
  private readonly RouteMemo<RouteSampleKey,bool> localClear=new RouteMemo<RouteSampleKey,bool>(65536);
  private readonly RouteMemo<RouteEdgeKey,bool> localEdges=new RouteMemo<RouteEdgeKey,bool>(131072);
  private readonly LocalRouteMemory localPaths=new LocalRouteMemory();
  private Func<Vector3,bool> clear=p=>true,land=p=>true;
  private Func<Vector3,Vector3> floor=p=>p;
  private Func<Vector3,Vector3,bool> segment=null;
  private readonly Collider wall=new Collider(); private int groundReads,clearReads,edgeReads,arrivals;
  private string skipped="";
  private float RootLift=>0; private float StepHeight=>.3f; private float Skin=>.02f; private float SlopeLimit=>45;
  private bool GroundPoint(Vector3 p,out Vector3 found,bool clearance=true){groundReads++;found=floor(p);return land(p)&&(!clearance||clear(found));}
  private RoutePoint? CachedGround(RoutePoint p)=>localGround.Get(new RouteSampleKey(p),DateTime.UtcNow.Ticks,()=>{Vector3 f;return GroundPoint(Vector(p),out f,false)?(RoutePoint?)Point(f):null;});
  private bool TraversalClear(Vector3 p){clearReads++;return clear(p);}
  private bool CachedTraversalClear(Vector3 p)=>localClear.Get(new RouteSampleKey(Point(p)),DateTime.UtcNow.Ticks,()=>TraversalClear(p));
  private bool Along(Vector3 a,Vector3 b,Func<Vector3,bool> allowed){int n=Math.Max(1,(int)Math.Ceiling(Vector3.Distance(a,b)/.02f));for(int i=1;i<=n;i++)if(!allowed(Vector3.Lerp(a,b,(float)i/n)))return false;return true;}
  private bool SurfaceSegmentAllowed(Vector3 a,Vector3 b)=>Along(a,b,land);
  private Collider Obstacle(Vector3 a,Vector3 b){edgeReads++;return (segment==null?Along(a,b,clear):segment(a,b))?null:wall;}
  private void InvalidateLocalArea(Vector3 p){var r=new RouteRegion(p.x-2,p.z-2,p.x+2,p.z+2);localGround.RemoveWhere(k=>r.Contains(k.Point));localClear.RemoveWhere(k=>r.Contains(k.Point));localEdges.RemoveWhere(k=>k.Touches(r));localPaths.Invalidate(r);}
  private bool SaffronAccess(Component c)=>false;
  private bool CanGatherAt(Component c,Vector3 p)=>FlatDistance(c.transform.position,p)<.75f;
  private Vector3 RouteCenter(Component c)=>c.transform.position;
  private void CancelVehicle(){} private UnityEngine.AI.NavMeshAgent NavAgent()=>null;
  private RoutePoint[] BridgeRouteGuides()=>guides; private RoutePoint[] guides=new RoutePoint[0];
  private void CaptureFailure(string reason){}
  private void SkipWalkTarget(Component c,TerritorySnapshot s,long now,string reason){skipped=reason;ClearLocalMotion();move.StopMove();}
  private bool GatherStandReached(Component c,Vector3 p,bool end,bool detected)=>CanGatherAt(c,p);
  private IEnumerable<LifeGatheringObject> Detected(int kind)=>new LifeGatheringObject[0]; private int Kind(LifeGatheringObject c)=>1;
  private void StopMove(){arrivals++;ClearLocalMotion();move.StopMove();}
  private bool TryEscape(TerritorySnapshot s,long now,TerritoryControl c,Collider hit)=>false;
  private void UpdateVehicle(long now,TerritoryControl c,double remaining){}
  private TerritorySnapshot snapshot=new TerritorySnapshot(); private long time=100000000;
  private void Start(Vector3 target){walkTarget=new LifeFarmFieldObject();walkTarget.transform.position=target;if(!BeginLocalRoute(walkTarget,snapshot,time,"fixture"))throw new Exception("fixture route start failed");}
  private void Tick(){ContinueLocal(snapshot,time,new TerritoryControl());time+=TimeSpan.TicksPerMillisecond*100;}
  private void Follow(){for(int i=0;i<800&&localMoving;i++){int before=player.Starts;Tick();if(player.Starts>before){var dest=localRoute[localIndex];var next=player.transform.position+Vector3.ClampMagnitude(dest-player.transform.position,.16f);if(!Along(player.transform.position,next,clear))throw new Exception("movement crossed a physical wall");player.transform.position=floor(next);}}}
  public static string[] Exercise(){
   var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
   RuntimeEngine House(){var e=new RuntimeEngine();e.clear=p=>p.x<1||p.x>3||Math.Abs(p.z)>1.2f;return e;}
   var e=House();e.Start(new Vector3(4,0,0));e.Follow();
   Check(e.arrivals==1&&e.skipped=="","ordinary planning walks around a house to the farm");
   e=House();e.Start(new Vector3(4,0,0));e.time+=TimeSpan.FromSeconds(16).Ticks;e.Tick();
   Check(!e.LocalSample(new RoutePoint(2,0,0)).HasValue&&!e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(4,0,0)),"planning timeout never disables house clearance or wall sweeps");
   e.Follow();Check(e.arrivals==1&&e.skipped=="","planning timeout still permits a real detour around the house");
   // A stale cached rejection must be retried against the current world, not fixed
   // by accepting collisions. Exhaust a bounded closed-world search first.
   e=new RuntimeEngine();e.land=p=>Math.Abs(p.x)<.2f&&Math.Abs(p.z)<.2f||p.x>3.2f&&p.x<4.8f&&Math.Abs(p.z)<.8f;
   e.Start(new Vector3(4,0,0));while(e.localSearch.State==RouteSearchState.Searching)e.localSearch.Step(256,1000);
   e.land=p=>true;e.clear=House().clear;e.Tick();
   Check(e.localGround.Count==0&&!e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(4,0,0)),"exhausted search discards stale terrain without allowing solid walls");
   e.Follow();Check(e.arrivals==1,"fresh scan recovers from unloaded terrain and takes the physical detour");
   e=new RuntimeEngine();e.Start(new Vector3(4,0,0));e.Tick();int sent=e.player.Starts;
   e.clear=p=>p.x<.2f||p.x>.9f||Math.Abs(p.z)>.6f;e.Tick();
   Check(e.player.Starts==sent&&e.localSearch!=null,"a newly placed wall stops steering immediately and replans before collision");
   e.Follow();Check(e.arrivals==1,"changed geometry invalidates the cached route and reaches the target by detour");
   e=new RuntimeEngine();e.Start(new Vector3(4,0,0));e.time+=TimeSpan.FromSeconds(16).Ticks;e.Tick();
   e.segment=(a,b)=>!(Math.Min(a.x,b.x)<.21f&&Math.Max(a.x,b.x)>.21f);
   Check(!e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(.4,0,0)),"thin fence collision blocks an edge even when both sample points are empty");
   e=new RuntimeEngine();e.Start(new Vector3(4,0,0));e.time+=TimeSpan.FromSeconds(16).Ticks;e.Tick();
   e.clear=p=>p.x<1||p.x>3||Math.Abs(p.z)>1.2f;
   Check(!e.LocalEdge(new RoutePoint(0,0,0),new RoutePoint(4,0,0)),"route reuse and corner smoothing retain collision checks after recovery");
   e=new RuntimeEngine();e.Start(new Vector3(4,0,0));e.floor=p=>new Vector3(p.x,p.x>=1?.25f:0,p.z);
   Check(e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(2,.25,0)),"legal native steps remain traversable");
   e.localGround.Clear();e.localEdges.Clear();e.floor=p=>new Vector3(p.x,p.x>=1?1.5f:0,p.z);
   Check(!e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(2,1.5,0)),"a house-height rise cannot become a walkable step");
   e=new RuntimeEngine();e.Start(new Vector3(4,0,0));e.Follow();
   Check(e.arrivals==1,"nonblocking crops and decorative floor tiles do not prevent travel");
   e=new RuntimeEngine();e.clear=p=>p.x<1||p.x>3||Math.Abs(p.z)<.28f;e.Start(new Vector3(4,0,0));e.Follow();
   Check(e.arrivals==1,"a narrow legal passage between buildings stays usable");
   e=new RuntimeEngine();e.land=p=>p.x<1||p.x>3||Math.Abs(p.z-.19f)<.17f;
   e.guides=Enumerable.Range(0,31).Select(i=>new RoutePoint(.5+i*.1,0,.19)).ToArray();e.Start(new Vector3(4,0,0));e.Follow();
   Check(e.arrivals==1,"native bridge guide still crosses between shore and farm");
   e=new RuntimeEngine();e.land=p=>p.x<1||p.x>3;e.Start(new Vector3(4,0,0));
   e.time+=TimeSpan.FromSeconds(16).Ticks;e.Tick();
   Check(!e.PlanEdge(new RoutePoint(0,0,0),new RoutePoint(4,0,0)),"refresh cannot invent a bridge over water");
   e=House();e.clear=p=>p.x<1||p.x>5||Math.Abs(p.z)>1.2f||p.x>3.1f&&p.x<4.9f&&Math.Abs(p.z)<.9f;e.Start(new Vector3(4,0,0));e.Follow();
   Check(e.arrivals==0&&e.skipped!=""&&e.player.Starts==0,"fully enclosed target ends within the recovery budget without walking through houses");
   return checks.ToArray();
  }
 }
}
""";
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 public sealed class RecipeCatalogProbe {public int ProcessId{get;set;}public long CapturedUtcTicks{get;set;}public RecipeOption[] Recipes{get;set;}}
 public static class SceneProbe
 {
  private static readonly Harmony patch=new Harmony("bd2.territory.probe."+typeof(SceneProbe).Assembly.GetName().Name);
  private static bool pending;
  public static void Inspect(){if(pending)return;pending=true;patch.Patch(typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic),postfix:new HarmonyMethod(typeof(SceneProbe),nameof(Frame)));}
  private static string Pos(Vector3 v)=>v.ToString("F3");
  private static void Frame()
  {
   if(!pending)return;pending=false;
   try{Capture("manual");}finally{patch.UnpatchAll(patch.Id);}
  }
  internal static void Capture(string reason)
  {
   try
   {
    B.ValidateCompiledClient();B.Validate();var text=new StringBuilder();
    var recipes=((System.Collections.Generic.IEnumerable<int>)B.Invoke("Tables.CookIds")).Select(id=>{var row=(Proto.Design.common.LifeCookTable)B.Invoke("Tables.Cook",id);var item=(Proto.Design.common.LifeItemTable)B.Invoke("Tables.ItemById",row.ResultItemId);return new RecipeOption{Id=id,Name=item==null?"missing":(string)B.Invoke("Text.Name",item.NameTextId),Available=item!=null};}).ToArray();
    LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"recipe-catalog-probe.json"),new RecipeCatalogProbe{ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id,CapturedUtcTicks=DateTime.UtcNow.Ticks,Recipes=recipes});
    var field=B.Read("Field.Instance",null);var player=B.Read("Field.Player",field) as PlayerController;
    var move=B.Read("Player.Move",player) as PlayerMoveController;var agent=B.Read("Player.NavAgent",move) as NavMeshAgent;
    text.AppendLine("reason="+reason);
    text.AppendLine("time="+DateTime.UtcNow.ToString("O")+" pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" player="+Pos(player.transform.position)+" mode="+B.Read("Player.MoveType",move));
    text.AppendLine("agent="+(agent==null?"null":("radius="+agent.radius+" baseOffset="+agent.baseOffset+" active="+agent.isActiveAndEnabled+" onMesh="+agent.isOnNavMesh+" pos="+Pos(agent.transform.position))));
    foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a=>(a.GetName().Name??"").StartsWith("BD2Territory.Runtime",StringComparison.Ordinal)))
    {
     var loader=assembly.GetType("BD2Territory.Runtime.Loader");var active=loader?.GetField("engine",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);if(active==null)continue;
     text.AppendLine("activeEngine="+assembly.GetName().Name);
     var terrain=active.GetType().GetMethod("TerrainKind",BindingFlags.Instance|BindingFlags.NonPublic);var surface=active.GetType().GetMethod("SurfaceContext",BindingFlags.Instance|BindingFlags.NonPublic);
     text.AppendLine("surface="+surface?.Invoke(active,null)+" playerTerrain="+terrain?.Invoke(active,new object[]{player.transform.position}));
     foreach(var name in new[]{"StepHeight","RootLift","BodyRadius"})text.AppendLine(" traversal "+name+"="+active.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active,null));
     foreach(var name in new[]{"targetNode","walkTarget","walkDestination","finalDestination","blockingCollider","navigation","gatheringReposition","gathering","gatheringStand","gatheringStands","vehiclePending","ownsVehicle","escapeStart","escapeDirection","localMoving","localTrial","localIndex","localSearch","localBudget","localBlocked","localRoute","localTarget","localPlanAt","localMotion"})
     {var f=active.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);var value=f?.GetValue(active);text.AppendLine(" engine "+name+"="+value);if(value is Component component)text.AppendLine(" selectedId="+component.GetInstanceID()+" pos="+Pos(component.transform.position));if(value is System.Collections.Generic.IEnumerable<Vector3> points)text.AppendLine(" points="+string.Join(";",points.Select(Pos).ToArray()));}
    }
    var body=RuntimeEngine.MovementBody(move);text.AppendLine("stepOffset="+(body==null?"missing":body.stepOffset.ToString("R"))+" slopeLimit="+(body==null?"missing":body.slopeLimit.ToString("R"))+" skinWidth="+(body==null?"missing":body.skinWidth.ToString("R")));
    if(body!=null)text.AppendLine("nativeBody radius="+body.radius+" height="+body.height+" localCenter="+Pos(body.center)+" scale="+Pos(body.transform.lossyScale));
    foreach(var c in player.GetComponentsInChildren<Collider>())text.AppendLine("playerCollider="+c.GetType().Name+" "+c.name+" layer="+c.gameObject.layer+" trigger="+c.isTrigger+" rotation="+Pos(c.transform.eulerAngles)+" center="+Pos(c.bounds.center)+" size="+Pos(c.bounds.size)+(c is BoxCollider box?" localCenter="+Pos(box.center)+" localSize="+Pos(box.size):""));
    var manager=B.Singleton(typeof(gamfs.Life.LifeManager));
    for(int kind=1;kind<=3;kind++){var n=B.InvokeOn("Gather.Near",manager,Enum.ToObject(B.Type("FunctionKind"),kind),player.transform.position) as LifeGatheringObject;text.AppendLine("nativeNearest kind="+kind+" id="+(n==null?0:n.GetInstanceID()));}
    foreach(var f in manager.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))if(f.FieldType.IsGenericType&&f.FieldType.GetGenericArguments().Any(t=>t.IsGenericType&&t.GetGenericArguments().Contains(typeof(LifeGatheringObject))))
    {var d=f.GetValue(manager) as System.Collections.IDictionary;if(d!=null)foreach(System.Collections.DictionaryEntry e in d)text.AppendLine("detected "+e.Key+"="+string.Join(",",((System.Collections.IEnumerable)e.Value).Cast<LifeGatheringObject>().Where(n=>n!=null).Select(n=>n.GetInstanceID().ToString()).ToArray()));}
    foreach(var n in UnityEngine.Object.FindObjectsOfType<LifeGatheringObject>().OrderBy(n=>(n.transform.position-player.transform.position).sqrMagnitude))
    {
     text.AppendLine("node="+n.GetInstanceID()+" kind="+B.Read("Gather.Function",n)+" name="+n.name+" pos="+Pos(n.transform.position)+" stage="+n.GetStatus()+" final="+B.Read("Gather.FinalStage",n)+" hp="+B.Read("Gather.Hp",n)+" can="+n.CanGatheringState());
     foreach(var c in n.GetComponentsInChildren<Collider>())text.AppendLine(" collider="+c.GetType().Name+" enabled="+c.enabled+" trigger="+c.isTrigger+" layer="+c.gameObject.layer+" rotation="+Pos(c.transform.eulerAngles)+" center="+Pos(c.bounds.center)+" size="+Pos(c.bounds.size)+(c is BoxCollider box?" localCenter="+Pos(box.center)+" localSize="+Pos(box.size):""));
     var sector=n.GetComponent<CircleSectorCollider>();if(sector!=null)foreach(var f in typeof(CircleSectorCollider).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>f.Name.StartsWith("_")||f.FieldType==typeof(Transform)))text.AppendLine(" sector "+f.Name+"="+f.GetValue(sector));
    }
    foreach(var f in UnityEngine.Object.FindObjectsOfType<LifeFarmFieldObject>())
    {var placed=f.GetPlaceableObject();var db=placed==null?null:B.Read("Farm.Db",placed) as Proto.Net.LifeWorldObjectDBInfo;var crop=B.Read("Farm.Crop",f) as LifeGatheringObject;
     text.AppendLine("farm="+f.GetInstanceID()+" chunk="+(placed==null?null:B.Read("Farm.Chunk",placed))+" localDb="+db+" occupied="+B.Read("Farm.Occupied",f)+" crop="+(crop==null?"null":crop.GetInstanceID().ToString())+" preview="+f.IsPreviewCandidateAvailable(f.GetFieldWorldCenter()));}
    foreach(var c in Physics.OverlapSphere(player.transform.position,4,~0,QueryTriggerInteraction.Ignore).Where(c=>!c.transform.IsChildOf(player.transform)))text.AppendLine("nearObstacle="+c.name+" type="+c.GetType().Name+" enabled="+c.enabled+" layer="+c.gameObject.layer+" ignoredPlayerLayer="+Physics.GetIgnoreLayerCollision(body==null?player.gameObject.layer:body.gameObject.layer,c.gameObject.layer)+" ignoredPlayerPair="+(body!=null&&Physics.GetIgnoreCollision(body,c))+" rotation="+Pos(c.transform.eulerAngles)+" center="+Pos(c.bounds.center)+" size="+Pos(c.bounds.size)+(c is BoxCollider box?" localCenter="+Pos(box.center)+" localSize="+Pos(box.size):""));
    var bytes=Encoding.UTF8.GetBytes(text.ToString());LocalStorage.WriteAtomically(Path.Combine(LocalStorage.DataRoot,"scene-probe.txt"),bytes);
    var folder=Path.Combine(LocalStorage.DataRoot,"diagnostics");Directory.CreateDirectory(folder);
    LocalStorage.WriteAtomically(Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt"),bytes);
    foreach(var old in new DirectoryInfo(folder).GetFiles("*.txt").OrderByDescending(f=>f.Name,StringComparer.Ordinal).Skip(12))old.Delete();
   }catch(Exception e){LocalStorage.WriteAtomically(Path.Combine(LocalStorage.DataRoot,"scene-probe.txt"),Encoding.UTF8.GetBytes("ERROR "+e));}
  }
 }
}

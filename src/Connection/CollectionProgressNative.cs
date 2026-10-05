using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Google.Protobuf;
using Proto.Net;
using gamfs;
using Proto.Design.pack1;
using UnityEngine;
using UnityEngine.AI;
namespace BD2Daily.Live {
 [DataContract] internal sealed class CollectionTarget {
  [DataMember] public int Map,Id,Group,Type;
 }
 [DataContract] internal sealed class WeeklyStealTarget {
  [DataMember] public int Map,Npc,Group;
  [DataMember] public string Name="";
 }
 [DataContract] internal sealed class WeeklyStealNpc {
  [DataMember] public int Npc,Instance;
  [DataMember] public bool Near,Reachable;
  [DataMember] public float Distance;
  [DataMember] public string Reason="";
 }
 [DataContract] internal sealed class CollectionNetworkState {
  [DataMember] public string Query="",State="idle",Error="",Rewards="[]",Monsters="[]";
  [DataMember] public bool StealReady;
  [DataMember] public string Steals="[]";
  [DataMember] public WeeklyStealTarget[] StealTargets=new WeeklyStealTarget[0];
  [DataMember] public int Pack;
  [DataMember] public long Week,Now,At;
  [DataMember] public CollectionTarget[] Drops=new CollectionTarget[0],Targets=new CollectionTarget[0];
  [DataMember] public int[] Maps=new int[0];
 }
 [DataContract] internal sealed class CollectionTravelMap {
  [DataMember] public int Id;
  [DataMember] public bool CanSummon,HasWaypoint,Inside,Hunting;
  [DataMember] public int[] Exits;
 }
 [DataContract] internal sealed class TravelApproach {
  [DataMember] public int Instance,Destination;
  [DataMember] public string Kind="",Reason="";
  [DataMember] public bool Reachable;
  [DataMember] public float Distance;
 }
 [DataContract] internal sealed class CollectionTravelState {
  [DataMember] public int Pack,Map;
  [DataMember] public bool Ground;
  [DataMember] public CollectionTravelMap[] Maps;
  [DataMember] public TravelApproach[] Approaches;
 }
 [DataContract] internal sealed class RouteProbePoint {
  [DataMember] public int Id;
  [DataMember] public float X,Y,Z,Distance;
 }
 [DataContract] internal sealed class RouteProbeState {
  [DataMember] public int Pack,Map;
  [DataMember] public RouteProbePoint[] Points;
 }
 [DataContract] internal sealed class TalentRowState { [DataMember] public int Instance,Group,Kind; [DataMember] public long Character; [DataMember] public double Cooldown; [DataMember] public string Reason,Gate; [DataMember] public bool PackRestricted; }
 [DataContract] internal sealed class RouteCollider { [DataMember] public int Id,Layer; [DataMember] public string Name,Type,Tag,Owner; [DataMember] public bool Enabled,Trigger; [DataMember] public float[] Center,Size,Rotation,LocalSize; }
 [DataContract] internal sealed class GateContactState { [DataMember] public int Object,Instance; [DataMember] public bool Contact; [DataMember] public float Depth; [DataMember] public RouteCollider Gate,Body; [DataMember] public RouteCollider[] Nearby; }
 internal static class CollectionProgressNative {
  [DataContract] internal sealed class SceneContext { [DataMember] public string[] ActiveTypes,Uis,FieldFlags; }
  internal static object Context(){
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   return new SceneContext{ActiveTypes=Bridge.Find(typeof(MonoBehaviour)).Where(x=>x!=null).Select(x=>x.GetType().Name).Distinct().OrderBy(x=>x).Take(600).ToArray(),
    Uis=Resources.FindObjectsOfTypeAll<UIBase>().Where(x=>x!=null&&x.gameObject.scene.IsValid()).Select(x=>x.GetType().Name+"|self="+x.gameObject.activeSelf+"|hier="+x.gameObject.activeInHierarchy+"|parent="+(x.transform.parent==null?"":x.transform.parent.name)).ToArray(),
    FieldFlags=field==null?new string[0]:field.GetType().GetFields(flags).Where(x=>x.FieldType==typeof(bool)||x.FieldType.IsEnum).Select(x=>x.Name+"="+x.GetValue(field)).ToArray()};
  }
  static RouteCollider Shape(Collider c){
   if(c==null)return null;var b=c.bounds;return new RouteCollider{Id=c.GetInstanceID(),Name=c.gameObject.name,Type=c.GetType().Name,Tag=c.tag,Layer=c.gameObject.layer,Owner=c.transform.parent==null?"":c.transform.parent.name,Enabled=c.enabled,Trigger=c.isTrigger,Center=new[]{b.center.x,b.center.y,b.center.z},Size=new[]{b.size.x,b.size.y,b.size.z},Rotation=new[]{c.transform.rotation.x,c.transform.rotation.y,c.transform.rotation.z,c.transform.rotation.w},LocalSize=c is BoxCollider?new[]{((BoxCollider)c).size.x,((BoxCollider)c).size.y,((BoxCollider)c).size.z}:new float[0]};
  }
  // Explicit diagnostic demand only; no global collider scan in normal travel.
  internal static object GateContacts(){
   var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;var body=player.ὭὦὫὤὮὧὦὡὨὤὢ.ὬὨὡὫὭὮὠὡὧὦὭ;
   return Bridge.Find(typeof(GateSpotData)).Cast<GateSpotData>().Where(g=>g.gameObject.name.StartsWith("Gate_")&&Vector3.Distance(player.transform.position,g.transform.position)<4).Select(g=>{
    var c=g.ὣὥὥὮὣὧὢὠὡὨὬ;Vector3 direction;float depth=0;bool hit=c!=null&&body!=null&&Physics.ComputePenetration(body,body.transform.position,body.transform.rotation,c,c.transform.position,c.transform.rotation,out direction,out depth);
    return new GateContactState{Object=g.ὪὬὣὧὥὨὦὯὨὮὬ,Instance=g.GetInstanceID(),Contact=hit,Depth=depth,Gate=Shape(c),Body=Shape(body),Nearby=Physics.OverlapSphere(g.transform.position,2).Take(64).Select(Shape).ToArray()};
   }).ToArray();
  }
  // Mirror the character/resource gates of the native talent row without clicking.
  internal static object TalentRows(){
   return Bridge.Find(typeof(QuickMenuTalentSkillLoopScrollItem)).Cast<QuickMenuTalentSkillLoopScrollItem>().Select(row=>{
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
    var t=row.GetType();var table=(Proto.Design.common.TalentSkillTable)t.GetField("ὣὡὪὭὤὨὭὣὪὧὩ",flags).GetValue(row);
    long index=Convert.ToInt64(t.GetField("ὣὥὠὭὣὯὤὧὮὡὦ",flags).GetValue(row));
    var ch=ὣὡὧὡὦὣὣὬὨὪὫ.ὣὢὨὠὦὥὯὢὥὠὪ(index);
    bool restricted=ch!=null&&ch.Hp>0&&ὢὭὤὣὠὦὥὠὢὣὩ.ὬὧὣὮὧὣὭὩὪὪὬ(ch);
    string reason=ch==null?"character_missing":ch.Hp<=0?"character_dead":restricted?"character_pack_restricted":ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst)<table.CatalystValue?"catalyst_insufficient":"";
    long end=Convert.ToInt64(t.GetField("ὯὦὥὮὣὩὩὥὥὭὮ",flags).GetValue(row));
    double cooldown=end<=0?0:Math.Max(0,(ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStampToDateTime(end)-ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now()).TotalSeconds);
    return new TalentRowState{Instance=row.GetInstanceID(),Group=table.GroupId,Kind=table.ClassType,Character=index,Reason=reason,Cooldown=cooldown,Gate=TalentGate(row,row.GetComponentInParent<QuickMenuUI>(),reason,cooldown),PackRestricted=restricted};
   }).ToArray();
  }
  // Read the native handler's gates without invoking CheckTalentSkillAttempted
  // (which mutates its two-frame latch) or any notice/network-producing method.
  internal static string TalentGate(QuickMenuTalentSkillLoopScrollItem row,QuickMenuUI menu,string rowReason=null,double? rowCooldown=null){
   if(row==null||menu==null||!row.isActiveAndEnabled||row.GetComponentInParent<QuickMenuUI>()!=menu)return "talent_reject:row_changed";
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
   var table=(Proto.Design.common.TalentSkillTable)row.GetType().GetField("ὣὡὪὭὤὨὭὣὪὧὩ",flags).GetValue(row);
   if(menu.ὠὩὠὩὫὪὥὧὢὫὭ)return "talent_reject:quick_slot_setting";
   if(menu.ὧὭὨὢὥὩὫὯὠὫὭ)return "talent_wait:timeline";
   var manager=TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ;var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(manager==null||player==null)return "talent_reject:field_unavailable";
   if(manager.ὧὪὯὮὬὧὪὤὩὩὦ)return "talent_wait:field_skill";
   var animation=player.GetType().GetMethod("get_AnimationController").Invoke(player,null);
   if(animation==null)return "talent_reject:animation_unavailable";
   if((bool)animation.GetType().GetMethod("get_IsPlayTalentAnimation").Invoke(animation,null))return "talent_wait:animation";
   if((bool)manager.GetType().GetField("_isAlreadyTalentSkillAttempted",flags).GetValue(manager))return "talent_wait:attempt_latch";
   if(table.ClassType==6&&manager.IsTalentSkillReseaching(ὪὯὯὢὨὮὨὠὥὤὬ.FieldRewardResearch))return "talent_wait:research_active";
   if((table.ClassType==2||table.ClassType==17)&&manager.IsTalentSkillDurationing((ὪὯὯὢὨὮὨὠὥὤὬ)table.ClassType))return "talent_wait:duration_active";
   // The research/duration cached row can outlive its manager entry for one frame.
   string cache=table.ClassType==6?"ὪὮὣὤὩὯὦὭὧὬὬ":table.ClassType==2||table.ClassType==17?"ὯὭὭὩὤὥὨὫὣὬὠ":"";
   if(cache.Length>0){var data=row.GetType().GetField(cache,flags).GetValue(row);if(data!=null&&(bool)typeof(ὩὠὠὧὭὠὬὪὩὮὫ).GetField("ὫὢὢὬὫὫὡὩὪὩὮ",flags).GetValue(data))return "talent_wait:cached_effect";}
   long character=(long)row.GetType().GetField("ὣὥὠὭὣὯὤὧὮὡὦ",flags).GetValue(row);var ch=ὣὡὧὡὦὣὣὬὨὪὫ.ὣὢὨὠὦὥὯὢὥὠὪ(character);
   if(ch==null||ch.Hp<=0||ὢὭὤὣὠὦὥὠὢὣὩ.ὬὧὣὮὧὣὭὩὪὪὬ(ch))return "talent_reject:character_unavailable";
   if(ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst)<table.CatalystValue)return "talent_reject:catalyst_insufficient";
   long end=(long)row.GetType().GetField("ὯὦὥὮὣὩὩὥὥὭὮ",flags).GetValue(row);
   if(end>0&&ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStampToDateTime(end)>ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now())return "talent_wait:cooldown";
   foreach(string name in new[]{"_objDisableImage","_goBlock"})if(((GameObject)row.GetType().GetField(name,flags).GetValue(row)).activeSelf)return "talent_reject:row_disabled";
   return "";
  }
  static string travelKey="";static long travelAt;static CollectionTravelMap[] travelMaps;
  internal static CollectionTravelState Travel(Frame frame){
   var map=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ;
   if(map==null)throw new InvalidOperationException("Current field map is unavailable");
   string key=frame.AccountKey+"|"+frame.PlayerKey+"|"+map.PackId;
   if(travelMaps==null||travelKey!=key||DateTime.UtcNow.Ticks-travelAt>TimeSpan.FromSeconds(10).Ticks){
    bool hasTable=ὢὯὩὦὣὮὩὥὦὡὧ.ὡὨὦὯὩὭὣὢὪὫὣ();
    travelMaps=ὩὥὫὮὯὥὮὪὨὡὩ.ὫὣὪὯὭὦὩὬὫὥὭ().Where(x=>x.PackId==map.PackId).Select(x=>new CollectionTravelMap{
     Id=x.Id,Inside=x.IsInsideMap==1,Hunting=ὧὧὧὪὦὠὭὣὫὡὤ.ὯὠὩὨὯὤὬὬὤὡὫ(x.Id),HasWaypoint=hasTable&&ὢὯὩὦὣὮὩὥὦὡὧ.ὯὬὮὮὪὤὧὮὬὧὦ(x.Id),CanSummon=hasTable&&x.IsInsideMap!=1&&ὢὯὩὦὣὮὩὥὦὡὧ.ὯὬὮὮὪὤὧὮὬὧὦ(x.Id),
     Exits=x.GateId.Where(g=>g>0).Select(g=>ὩὥὫὮὯὥὮὪὨὡὩ.ὨὨὡὫὦὡὥὫὡὤὦ(g)).Distinct().ToArray()
    }).ToArray();travelKey=key;travelAt=DateTime.UtcNow.Ticks;
   }
   return new CollectionTravelState{Pack=map.PackId,Map=map.Id,Ground=TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsAvailableWayPointMakeOnGround(),Maps=travelMaps,Approaches=Approaches(frame)};
  }

  // Publishing evidence must never enable, warp or move the native agent.
  // The executor rechecks CanNavReach immediately before movement.
  internal static bool TryApproach(FieldObjectBase target,out Vector3 endpoint,out float distance,out string reason){
   endpoint=Vector3.zero;distance=0;reason="";

   var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(player==null){reason="player_missing";return false;}
   var agent=player.ὭὦὫὤὮὧὦὡὨὤὢ.ὢὮὡὭὢὥὠὧὧὯὬ;
   // CanNavReach creates the agent lazily. Observation must not create/enable it.
    float height=agent==null?1.1f:agent.height;
    int mask=(int)typeof(MoveController).GetProperty("ὤὥὮὬὡὣὮὢὪὧὨ",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(player.ὭὦὫὤὮὧὦὡὨὤὢ,null);
   NavMeshHit start,end;
   if(!NavMesh.SamplePosition(player.transform.position,out start,height*2,mask)){reason="player_off_navigation";return false;}
   // Same logical point as QuestNavigationManager, not a model pivot.
   if(!NavMesh.SamplePosition(target.ὩὬὧὢὧὬὠὡὦὥὧ,out end,height*2,mask)){reason="target_off_navigation";return false;}
   var path=new NavMeshPath();
   if(!NavMesh.CalculatePath(start.position,end.position,mask,path)||path.status!=NavMeshPathStatus.PathComplete){reason="path_incomplete";return false;}
   endpoint=end.position;var points=path.corners;
   for(int i=1;i<points.Length;i++)distance+=Vector3.Distance(points[i-1],points[i]);
   return true;
  }
  static string approachKey="";static long approachAt;static TravelApproach[] approaches;
  static TravelApproach[] Approaches(Frame frame){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var p=field.ὪὨὯὢὫὮὨὩὮὡὬ.transform.position;
   string key=frame.AccountKey+"|"+frame.PlayerKey+"|"+frame.Scene+"|"+field.ὮὬὬὮὠὮὪὠὧὩὪ.Id+"|"+Math.Round(p.x,1)+"|"+Math.Round(p.y,1)+"|"+Math.Round(p.z,1);
   if(approaches!=null&&key==approachKey&&DateTime.UtcNow.Ticks-approachAt<TimeSpan.FromSeconds(2).Ticks)return approaches;
   approaches=Bridge.Find(typeof(FieldObjectBase)).Cast<FieldObjectBase>().Where(x=>x is WayPointController||(x is GateSpotData&&x.gameObject.name.StartsWith("Gate_"))).Select(x=>{
    Vector3 end;float length=0;string error="";var g=x as GateSpotData;
    // The game's gate would show a purchase/locked notice here. A travel plan
    // can reject that entry from read-only account state before starting movement.
    if(g!=null&&ὧὧὧὪὦὠὭὣὫὡὤ.ὯὠὩὨὯὤὬὬὤὡὫ(g.ὤὦὨὨὭὮὯὡὧὤὯ)){
     if(!ὣὡὧὡὦὣὣὬὨὪὫ.ὦὤὡὧὯὠὬὬὦὣὭ(Define_ContentOpenType.ContentUnlockHunting,field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId))error="hunting_locked";
     else if(ὣὡὧὡὦὣὣὬὨὪὫ.ὣὨὦὫὩὯὢὪὧὨὠ<=0)error="hunting_ap_unavailable";
    }
    bool can=error.Length==0&&TryApproach(x,out end,out length,out error);
    return new TravelApproach{Instance=x.GetInstanceID(),Kind=g==null?"waypoint":"gate",Destination=g==null?0:g.ὤὦὨὨὭὮὯὡὧὤὯ,Reachable=can,Distance=length,Reason=error};
   }).ToArray();approachKey=key;approachAt=DateTime.UtcNow.Ticks;return approaches;
  }


  internal static bool IsStoryField(int id){
   var pack=ὤὫὬὭὯὪὢὬὬὩὣ.ὬὣὢὥὫὪὬὮὣὠὦ(id);
   return pack!=null&&(pack.PackType==0||pack.PackType==1)&&ὣὡὧὡὦὣὣὬὨὪὫ.ὬὣὢὥὫὪὬὮὣὠὦ(id)!=null;
  }
  static int AreaMask(MoveController move){return (int)typeof(MoveController).GetProperty("ὤὥὮὬὡὣὮὢὪὧὨ",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(move,null);}
  static string probeKey="";static RouteProbeState probe;
  internal static RouteProbeState Probe(Frame frame){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var map=field.ὮὬὬὮὠὮὪὠὧὩὪ;
   var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;string region=string.Join(",",Approaches(frame).Where(x=>x.Reachable).Select(x=>x.Instance).OrderBy(x=>x));
   string key=frame.AccountKey+"|"+frame.Scene+"|"+map.Id+"|"+player.GetInstanceID()+"|"+region;
   if(probe!=null&&key==probeKey)return probe;
   var origin=player.transform.position;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;int mask=AreaMask(move);
   var mesh=NavMesh.CalculateTriangulation();var candidates=new List<Vector3>();NavMeshHit start;
   var gates=Bridge.Find(typeof(GateSpotData)).Cast<GateSpotData>().Where(g=>g.gameObject.name.StartsWith("Gate_")).ToArray();
   if(NavMesh.SamplePosition(origin,out start,2.2f,mask)){
    int triangles=mesh.indices.Length/3;int stride=Math.Max(1,triangles/160);
    for(int i=0;i<triangles;i+=stride){
     if((mask&(1<<mesh.areas[i]))==0)continue;
     var point=(mesh.vertices[mesh.indices[i*3]]+mesh.vertices[mesh.indices[i*3+1]]+mesh.vertices[mesh.indices[i*3+2]])/3;
     if(Vector3.Distance(point,origin)<1||gates.Any(g=>g.ὣὥὥὮὣὧὢὠὡὨὬ!=null&&Vector3.Distance(g.ὣὥὥὮὣὧὢὠὡὨὬ.ClosestPoint(point),point)<1.5f))continue;
     var path=new NavMeshPath();if(NavMesh.CalculatePath(start.position,point,mask,path)&&path.status==NavMeshPathStatus.PathComplete&&ProbeStaysInRoom(path.corners,gates))candidates.Add(point);
    }
   }
   var selected=new List<Vector3>();
   while(candidates.Count>0&&selected.Count<8){
    var point=candidates.OrderByDescending(v=>selected.Count==0?Vector3.Distance(v,origin):selected.Min(w=>Vector3.Distance(v,w))).First();
    candidates.RemoveAll(v=>Vector3.Distance(v,point)<1.5f);selected.Add(point);
   }
   probe=new RouteProbeState{Pack=map.PackId,Map=map.Id,Points=selected.Select((v,i)=>{
    var path=new NavMeshPath();float distance=0;
    if(!NavMesh.CalculatePath(start.position,v,mask,path)||path.status!=NavMeshPathStatus.PathComplete)return null;
    for(int n=1;n<path.corners.Length;n++)distance+=Vector3.Distance(path.corners[n-1],path.corners[n]);
    return new RouteProbePoint{Id=i+1,X=v.x,Y=v.y,Z=v.z,Distance=distance};
   }).Where(x=>x!=null).ToArray()};probeKey=key;return probe;
  }
  // A reachable endpoint can still lie beyond a native doorway trigger. Only
  // diagnostic free-roam probes avoid gates; intentional door navigation does not.
  static bool ProbeStaysInRoom(Vector3[] corners,GateSpotData[] gates){
   var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   var body=player.ὭὦὫὤὮὧὦὡὨὤὢ.ὬὨὡὫὭὮὠὡὧὦὭ;
   float radius=body==null?.35f:Math.Max(body.bounds.extents.x,body.bounds.extents.z)+.1f;
   foreach(var gate in gates){
    var collider=gate.ὣὥὥὮὣὧὢὠὡὨὬ;if(collider==null||!collider.enabled)continue;
    // Probe points must stay in this room. A character capsule can touch a
    // rotated trigger even when its centreline misses it. Conservatively use
    // expanded world bounds here; intentional doorway navigation is unchanged.
    var bounds=collider.bounds;bounds.Expand(new Vector3(radius*2,1.1f,radius*2));
    for(int i=1;i<corners.Length;i++){
     var from=corners[i-1]+Vector3.up*.55f;var to=corners[i]+Vector3.up*.55f;var delta=to-from;
     if(bounds.Contains(from)){
      // Allow only the first segment escaping away from the arrival trigger,
      // never a path crossing through it to the opposite side.
      if(i==1&&!bounds.Contains(to)&&Vector3.Dot(delta,from-bounds.center)>=0)continue;
      return false;
     }
     float hit;if(delta.sqrMagnitude>.0001f&&bounds.IntersectRay(new Ray(from,delta.normalized),out hit)&&hit<=delta.magnitude)return false;
    }
   }
   return true;
  }
  internal static float StartProbe(Frame frame,Command command){
   var state=Probe(frame);if(command.Value!=state.Map||command.Items.Length!=1)throw new InvalidOperationException("Probe belongs to another map");
   var p=state.Points.Single(x=>x.Id==command.Items[0]);var target=new Vector3(p.X,p.Y,p.Z);
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;
   if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"||!IsStoryField(state.Pack))throw new InvalidOperationException("Probe requires non-event idle field");
   if(field.ὮὬὬὮὠὮὪὠὧὩὪ.MapType==1&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth))throw new InvalidOperationException("Probe in battle field requires active stealth");
   if(!move.CanNavReach(target))throw new InvalidOperationException("travel_unreachable:probe_native_check");
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
   if(!move.SetMoveNav(target,null,true)){field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);throw new InvalidOperationException("travel_unreachable:probe_start");}
   return p.Distance;
  }

  static CollectionNetworkState result=new CollectionNetworkState();
  static Command owner;
  internal static CollectionNetworkState Capture(){
   if(owner!=null&&!Matches(owner))return new CollectionNetworkState{State="invalid",Error="Collection query account, connection or cartridge changed"};
   return result;
  }
  static bool Matches(Command c){
   var f=Bridge.CurrentFrame;
   return f!=null&&f.ProcessId==c.ProcessId&&f.ProcessStartTicks==c.ProcessStartTicks&&f.Instance==c.Instance&&f.AccountKey==c.AccountKey&&f.PlayerKey==c.PlayerKey&&Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ==c.Value;
  }
  static bool Accept(CollectionNetworkState state,Command c,int error){
   if(!object.ReferenceEquals(state,result))return false;
   if(error!=0||!Matches(c)||!Bridge.CollectionReadAllowed||state.Week!=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.GetWeeklyResetTime().Ticks){state.State="failed";state.Error="Collection read rejected or context changed: "+error;return false;}
   return true;
  }
  internal static void Query(Command c){
   if(!Matches(c)||BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE")throw new InvalidOperationException("Collection query requires current idle cartridge");
   if(result.State=="pending"&&DateTime.UtcNow.Ticks-result.At<TimeSpan.FromSeconds(40).Ticks)throw new InvalidOperationException("Collection read is already pending");
   var maps=c.Items.Select(x=>checked((int)x)).ToArray();
   foreach(int id in maps){var m=ὩὥὫὮὯὥὮὪὨὡὩ.ὧὥὮὪὣὬὦὡὫὧὢ(id);if(m==null||m.PackId!=c.Value)throw new InvalidOperationException("Collection map is outside requested cartridge");}
   var raw=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var objects=raw.GetTableList<FieldRewardObjectTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_PACK,"FieldRewardObjectTable",c.Value);
   var drops=new List<CollectionTarget>();var monsters=new List<CollectionTarget>();
   foreach(var r in objects.Where(x=>maps.Contains(x.MapId))){
    var group=ὧὣὭὥὢὢὠὣὥὫὣ.ὯὫὭὤὭὧὬὢὯὯὦ(c.Value,r.FieldObjectGroupId);
    if(group.ὫὣὬὥὪὫὪὤὫὮὧ==1||group.ὫὣὬὥὪὫὪὤὫὮὧ==3)drops.Add(new CollectionTarget{Map=r.MapId,Id=r.Id,Group=r.FieldObjectGroupId,Type=group.ὫὣὬὥὪὫὪὤὫὮὧ});
   }
   foreach(int id in maps){
    var map=ὩὥὫὮὯὥὮὪὨὡὩ.ὧὥὮὪὣὬὦὡὫὧὢ(id);
    foreach(int mid in ὧὩὫὫὩὣὥὨὥὤὠ.ὪὯὤὭὢὭὪὮὬὭὢ(c.Value,map.MapScenePath,ὢὩὭὬὪὪὬὩὫὪὬ.Monster).Distinct()){
     var m=ὨὩὤὥὡὫὮὪὣὫὨ.ὥὩὡὬὧὬὥὣὨὡὥ(c.Value,mid);
     if(m.ὯὢὭὮὤὪὨὬὭὭὮ>0&&m.ὫὣὬὥὪὫὪὤὫὮὧ!=3&&(m.ὬὬὨὠὪὧὠὦὠὢὣ||m.ὫὣὬὥὪὫὪὤὫὮὧ==2))monsters.Add(new CollectionTarget{Map=id,Id=mid,Group=m.ὯὢὭὮὤὪὨὬὭὭὮ,Type=m.ὫὣὬὥὪὫὪὤὫὮὧ});
    }
   }
   owner=c;var next=new CollectionNetworkState{Query=c.Id,Pack=c.Value,Maps=maps,Drops=drops.ToArray(),Targets=monsters.ToArray(),State="pending",At=DateTime.UtcNow.Ticks,Week=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.GetWeeklyResetTime().Ticks};result=next;
   var request=new FieldObjectInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ,PackId=c.Value};
   // Read-only responses are kept separately: never overwrite another cartridge's game cache.
   ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ.Send(request,(byte[] data,int code,int error)=>{
    if(!Accept(next,c,error))return false;
    try{
     next.Rewards="["+string.Join(",",FieldObjectInfoResponse.Parser.ParseFrom(data).FieldRewardObtainInfo.Select(x=>JsonFormatter.Default.Format(x)).ToArray())+"]";
     if(monsters.Count==0){FinishQuery(next,c);return true;}
     var req=new MonsterInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ};req.GroupId.AddRange(monsters.Select(x=>x.Group).Distinct());
     ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ.Send(req,(byte[] bytes,int packet,int err)=>{
      if(!Accept(next,c,err))return false;
      try{next.Monsters="["+string.Join(",",MonsterInfoResponse.Parser.ParseFrom(bytes).MonsterInfo.Select(x=>JsonFormatter.Default.Format(x)).ToArray())+"]";FinishQuery(next,c);return true;}catch(Exception ex){next.State="failed";next.Error=ex.Message;return false;}
     });return true;
    }catch(Exception ex){next.State="failed";next.Error=ex.Message;return false;}
   });
  }
  // Select only weekly NPC talents the account actually owns; never fabricate a skill.
  static WeeklyStealTarget[] StealTargets(int pack,int[] maps){
   var groups=new HashSet<int>();
   foreach(var ch in ὣὡὧὡὦὣὣὬὨὪὫ.ὨὯὯὪὢὯὡὪὡὫὯ){
    var ct=ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id);if(ct==null)continue;
    var talent=ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(ct.TalentId);if(talent==null)continue;
    var skill=ὯὤὮὦὮὬὨὢὩὪὦ.ὩὫὩὤὤὤὢὣὥὤὨ(talent.TalentSkillGroupId,1);
    if(skill!=null&&skill.ClassType==1&&skill.ResetType==2)groups.Add(skill.GroupId);
   }
   return Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetTableList<FieldNpcTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_PACK,"FieldNpcTable",pack)
    .Where(n=>maps.Contains(n.MapId)).SelectMany(n=>n.TalentSkillGroupList.Where(groups.Contains).Distinct().Select(g=>new WeeklyStealTarget{Map=n.MapId,Npc=n.Id,Group=g,Name=ὪὥὦὥὭὠὦὡὠὧὤ.ὥὠὬὢὭὬὣὠὦὣὥ(n.NpcNameTextId)})).ToArray();
  }
  static void FinishQuery(CollectionNetworkState value,Command c){
   if(c.Kind!="collection_query_steal"){Finish(value);return;}
   var req=new PackInGameInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ,PackId=c.Value};
   ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ.Send(req,(byte[] bytes,int packet,int error)=>{
    if(!Accept(value,c,error))return false;
    try{
     value.Steals="["+string.Join(",",PackInGameInfoResponse.Parser.ParseFrom(bytes).TalentNpcInfo.Select(x=>JsonFormatter.Default.Format(x)).ToArray())+"]";
     value.StealTargets=StealTargets(c.Value,value.Maps);value.StealReady=true;Finish(value);return true;
    }catch(Exception ex){value.State="failed";value.Error=ex.Message;return false;}
   });
  }
  internal static object StealNpcs(){
   if(owner==null||!Matches(owner)||!result.StealReady)return new WeeklyStealNpc[0];
   var ids=new HashSet<int>(result.StealTargets.Select(x=>x.Npc));
   return Bridge.Find(typeof(NPCController)).Cast<NPCController>().Where(n=>ids.Contains(n.ὪὬὣὧὥὨὦὯὨὮὬ)).Select(n=>{
    Vector3 point;float distance;string reason;bool can=TryApproach(n,out point,out distance,out reason);
    return new WeeklyStealNpc{Npc=n.ὪὬὣὧὥὨὦὯὨὮὬ,Instance=n.GetInstanceID(),Near=n.ὩὤὨὮὥὦὫὭὫὭὨ,Reachable=can,Distance=distance,Reason=reason};
   }).ToArray();
  }
  internal static bool IsStealNpc(NPCController npc){return npc!=null&&owner!=null&&Matches(owner)&&result.StealReady&&result.Week==ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.GetWeeklyResetTime().Ticks&&result.StealTargets.Any(t=>t.Map==GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ.Id&&t.Npc==npc.ὪὬὣὧὥὨὦὯὨὮὬ);}
  internal static bool StealTalkInputReady(UIBase value){
   var ui=value as BalloonScriptUI;if(ui==null)return true;
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;var type=ui.GetType();
   // CloseBalloonUI leaves UIBase input and its touch object enabled while the
   // talent outro restores the player. That is no longer an actionable dialogue.
   if(type.GetField("ὠὩὭὦὧὣὬὫὩὧὣ",flags).GetValue(ui).ToString()!="NpcTalent"||(int)type.GetField("ὥὦὥὮὧὥὢὫὯὣὤ",flags).GetValue(ui)!=1)return true;
   return !(bool)type.GetField("ὨὡὥὪὥὦὭὧὠὬὩ",flags).GetValue(ui);
  }
  internal static void StealTalk(BalloonScriptUI ui,Command c){
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;var type=ui.GetType();
   var npc=(NPCController)type.GetField("ὠὩὤὮὯὪὡὪὦὯὢ",flags).GetValue(ui);
   var kind=type.GetField("ὠὩὭὦὧὣὬὫὩὧὣ",flags).GetValue(ui).ToString();
   int talent=(int)type.GetField("ὥὦὥὮὧὥὢὫὯὣὤ",flags).GetValue(ui);
   if(kind!="NpcTalent"||talent!=1||!IsStealNpc(npc)||npc.ὪὬὣὧὥὨὦὯὨὮὬ!=c.Value)throw new InvalidOperationException("Not the requested steal result dialogue");
   if(!StealTalkInputReady(ui))return; // Outro began after observation; no extra click.
   var touch=(GameObject)type.GetField("_objTouchButton",flags).GetValue(ui);
   var choice=(VisualNovelSelectChoiceUI)type.GetField("_selectUI",flags).GetValue(ui);
   if(!touch.activeInHierarchy||(choice!=null&&choice.gameObject.activeInHierarchy))throw new InvalidOperationException("Steal dialogue not ready for advance");
   ui.OnClickUI(touch);
  }
  internal static void StealConfirm(StealInfoPopupUI ui,Command c){
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
   var type=ui.GetType();int npc=(int)type.GetField("ὣὮὡὮὣὥὫὬὨὣὬ",flags).GetValue(ui);
   var skill=(Proto.Design.common.TalentSkillTable)type.GetField("ὣὡὪὭὤὨὭὣὪὧὩ",flags).GetValue(ui);
   if(npc!=c.Value||skill.GroupId!=c.Items[0]||skill.ClassType!=1||skill.ResetType!=2||owner==null||!Matches(owner)||!result.StealReady||!IsStealNpc(GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.GetNPCController(npc))||!result.StealTargets.Any(t=>t.Npc==npc&&t.Group==skill.GroupId))throw new InvalidOperationException("Weekly steal context changed");
   if(ὧὥὩὣὭὯὧὫὫὥὠ.ὪὪὣὡὩὩὥὭὯὩὯ(npc,skill.GroupId)>ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp())throw new InvalidOperationException("Weekly steal already completed");
   if(ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst)<skill.CatalystValue)throw new InvalidOperationException("Weekly steal catalyst insufficient");
   ui.OnClickUI((GameObject)type.GetField("_objButtonOk",flags).GetValue(ui));
  }
  static void Finish(CollectionNetworkState value){value.Now=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp();value.At=DateTime.UtcNow.Ticks;value.State="ready";}
 }
 // Guard the automatic proximity pickup before it changes object state or sends
 // FieldObjectReward. Never spoof success or remove a server-side reward.
 internal static class TalentSafetyNative {
  static HarmonyLib.Harmony guard;
  static int blockedObject,blockedCount;static long blockedAt;
  static readonly System.Reflection.BindingFlags Flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
  internal static void Start(){
   if(guard!=null)return;
   guard=new HarmonyLib.Harmony("bd2.daily.talent.pickup.v1");
   guard.Patch(typeof(FieldRewardObjectController).GetMethod("InteractionFieldObject",Type.EmptyTypes),prefix:new HarmonyLib.HarmonyMethod(typeof(TalentSafetyNative).GetMethod("BeforePickup",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)));
  }
  internal static void Stop(){if(guard!=null)guard.Unpatch(typeof(FieldRewardObjectController).GetMethod("InteractionFieldObject",Type.EmptyTypes),HarmonyLib.HarmonyPatchType.All,guard.Id);guard=null;}
  [DataContract] internal sealed class ResearchState {
   [DataMember] public bool Active;
   [DataMember] public double Remaining;
   [DataMember] public int Group,BlockedObject,BlockedCount;
   [DataMember] public long BlockedAt;
  }
  internal static ResearchState Research(){
   var manager=TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ;var result=new ResearchState{BlockedObject=blockedObject,BlockedCount=blockedCount,BlockedAt=blockedAt};
   ὥὪὧὦὦὭὬὠὨὮὪ data;
   if(manager==null||!manager.TryGetTalentSkillResearchData(ὪὯὯὢὨὮὨὠὥὤὬ.FieldRewardResearch,out data)||data==null||data.ὧὫὪὯὧὣὪὡὩὭὩ==null)return result;
   result.Group=data.ὧὫὪὯὧὣὪὡὩὭὩ.GroupId;
   var server=ὧὥὩὣὭὯὧὫὫὥὠ.ὪὤὬὪὠὯὥὠὯὫὢ(result.Group);
   double remaining=server==null?0:(ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStampToDateTime(server.EndTime)-ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now()).TotalSeconds;
   result.Remaining=Math.Max(0,Math.Min(data.ὠὨὠὭὬὤὪὢὫὨὮ,remaining));
   result.Active=data.ὫὢὢὬὫὫὡὩὪὩὮ&&manager.IsTalentSkillReseaching(ὪὯὯὢὨὮὨὠὥὤὬ.FieldRewardResearch);
   return result;
  }
  static bool BeforePickup(FieldRewardObjectController __instance){
   if(!Bridge.DailyActionsActive||__instance==null||!__instance.ὦὫὧὯὥὫὦὪὣὦὪ)return true;
   try{var research=Research();bool ready=LivePolicy.HiddenPickupReady(research.Active,research.Remaining);
    if(!ready){int id=__instance.ὪὬὣὧὥὨὦὯὨὮὬ;blockedObject=id;blockedCount++;blockedAt=DateTime.UtcNow.Ticks;}return ready;}
   catch{return false;}
  }
  internal static string PopupContext(UIBase ui){
   if(!(ui is MessagePopupUI)||ui.gameObject.name!="ErrorMessagePopupUI(Clone)")return "";
   var type=typeof(MessagePopupUI);
   var label=type.GetField("_textMessage",Flags).GetValue(ui) as TMPro.TMP_Text;
   var cancel=type.GetField("_buttonCancel",Flags).GetValue(ui) as UnityEngine.UI.Button;
   // Network errors with callbacks can restart the game. Only acknowledge the
   // plain single-OK notification; never invoke a purchase/restart callback.
   if(label==null||!System.Text.RegularExpressions.Regex.IsMatch(label.text??"",@"error\s*:\s*100005(?![0-9])"))return "";
   if(cancel!=null&&cancel.gameObject.activeInHierarchy)return "";
   if(new[]{"ὪὣὠὬὡὦὬὥὡὪὡ","ὬὠὡὢὤὣὣὦὯὦὠ"}.Any(n=>type.GetField(n,Flags).GetValue(ui)!=null))return "";
   return "talent_inactive_ack_only";
  }
  internal static void Acknowledge(UIBase ui){
   if(PopupContext(ui)!="talent_inactive_ack_only")throw new InvalidOperationException("Talent error popup changed");
   var button=(UnityEngine.UI.Button)typeof(MessagePopupUI).GetField("_buttonOK",Flags).GetValue(ui);
   if(button==null||!button.gameObject.activeInHierarchy||!button.interactable)throw new InvalidOperationException("Talent error confirmation is not ready");
   ui.OnClickUI(button.gameObject);
  }
 }

}

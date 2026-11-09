using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.AI;
using gamfs;
namespace BD2Daily.Live {
 // Owns only navigation explicitly requested by the daily controller. It never
 // changes enemies, collision masks, talents, battle state or collection state.
 internal sealed class FieldRoutePilot {
  [DataContract] internal sealed class State {
   [DataMember]public string Status="idle",Reason="";
   [DataMember]public int Map,Replans,Queries,Yields,Evasions,Ticks,Resumes;
   [DataMember]public double Length,WaitSeconds,TotalTickMs,MaxTickMs;
   [DataMember]public bool Protected;
   [DataMember]public float[] Goal;
  }
  sealed class Motion {internal Vector3 Position,Velocity;internal long At;}
  static FieldRoutePilot current;static State state=new State();
  readonly int map;readonly Vector3 goal;readonly long until;readonly bool stealth;
  readonly FieldExclusion[] gates;
  readonly Dictionary<int,Motion> motions=new Dictionary<int,Motion>();
  Vector3[] path;Vector3 evadeGoal,lastPosition;int corner;long next,waitSince,clearSince,lastPlan,lockedAt,sentAt,progressAt;bool waiting,evading;float speed=4.8f;
  FieldRoutePilot(Vector3 target,Vector3[] points,bool protect,long expires,FieldExclusion[] gateZones){
   gates=gateZones;
   goal=target;path=points;corner=points.Length>1?1:0;stealth=protect;until=expires;
   map=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ.Id;
   lastPosition=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ.transform.position;progressAt=DateTime.UtcNow.Ticks;
  }
  internal static State Capture(){return state;}
  static FieldPoint P(Vector3 p){return new FieldPoint(p.x,p.y,p.z);}
  static Vector3 V(FieldPoint p){return new Vector3((float)p.X,(float)p.Y,(float)p.Z);}
  static Vector3 V(float[] p){return new Vector3(p[0],p[1],p[2]);}
  static CollectionProgressNative.HazardObservation Scan(){return (CollectionProgressNative.HazardObservation)CollectionProgressNative.Hazards();}
  internal static FieldExclusion[] Elites(){return Elites(Scan());}
  static FieldExclusion[] Elites(CollectionProgressNative.HazardObservation scan){
   return scan.Objects.Where(o=>o.Kind==5&&o.Active&&!o.Suppressible&&o.Respawn<=0&&o.Error==""&&o.ContactRadius>0)
    .Select(o=>new FieldExclusion{Id=o.Instance,Center=P(V(o.Position)),Radius=o.ContactRadius+.25}).ToArray();
  }
  static int Mask(){var move=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ.ὭὦὫὤὮὧὦὡὨὤὢ;return (int)typeof(MoveController).GetProperty("ὤὥὮὬὡὣὮὢὪὧὨ",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(move,null);}
  static FieldPoint[] NativePath(FieldPoint a,FieldPoint b,int mask){
   NavMeshHit start,end;if(!NavMesh.SamplePosition(V(a),out start,.7f,mask)||!NavMesh.SamplePosition(V(b),out end,.7f,mask))return null;
   var route=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,mask,route)||route.status!=NavMeshPathStatus.PathComplete)return null;
   return route.corners.Select(P).ToArray();
  }
  internal static FieldAvoidancePlan Plan(Vector3 start,Vector3 target,FieldExclusion[] hazards){
   int mask=Mask();return FieldAvoidancePlanner.Plan(P(start),P(target),hazards,(a,b)=>NativePath(a,b,mask),p=>{
    NavMeshHit hit;if(!NavMesh.SamplePosition(V(p),out hit,.45f,mask)||Math.Abs(hit.position.y-p.Y)>.7)return null;return (FieldPoint?)P(hit.position);
   });
  }
  internal static bool SafeEndpoint(Vector3 point,FieldExclusion[] hazards){return hazards.All(h=>FieldPoint.Distance(P(point),h.Center)>h.Radius+.02);}
  // Local NPC/prop approaches must not walk through an unrelated map trigger.
  // Deliberate gate travel does not opt in. Only enabled trigger volumes at the
  // player's elevation participate; no map, monster or collider is disabled.
  static FieldExclusion[] GateExclusions(){
   var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   var body=SquareRoutePilot.MovementBody(player.ὭὦὫὤὮὧὦὡὨὤὢ);
   float radius=body==null?.25f:body.radius*Math.Max(Math.Abs(body.transform.lossyScale.x),Math.Abs(body.transform.lossyScale.z));
   var extent=body==null?new Bounds(player.transform.position+Vector3.up*.5f,new Vector3(.5f,1,.5f)):body.bounds;
   return Bridge.Find(typeof(GateSpotData)).Cast<GateSpotData>().Select(g=>g.ὣὥὥὮὣὧὢὠὡὨὬ)
    .Where(c=>c!=null&&c.enabled&&c.isTrigger&&c.gameObject.activeInHierarchy&&c.bounds.max.y>=extent.min.y&&c.bounds.min.y<=extent.max.y)
    .Select(c=>new FieldExclusion{Id=c.GetInstanceID(),Center=new FieldPoint(c.bounds.center.x,player.transform.position.y,c.bounds.center.z),
     Radius=Math.Sqrt(c.bounds.extents.x*c.bounds.extents.x+c.bounds.extents.z*c.bounds.extents.z)+radius+.1}).ToArray();
  }
  internal static float Start(Vector3 target,bool protect,bool avoidGates=false){
   Cancel("replaced");
   var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   var scan=Scan();var gates=avoidGates?GateExclusions():new FieldExclusion[0];
   var plan=Plan(player.transform.position,target,Elites(scan).Concat(gates).ToArray());
   if(!plan.Found)throw new InvalidOperationException("travel_unreachable:"+plan.Reason);
   state=new State{Status="walking",Protected=protect,Map=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ.Id,Length=plan.Length,Queries=plan.Queries,Goal=new[]{target.x,target.y,target.z}};
   // Static gates need a checked route, not a stop at every NavMesh corner.
   // SetMoveNav handles those corners continuously, including the controller's
   // ground offset. Keep active avoidance for actual detours or moving hazards.
   if(plan.Direct&&!scan.Objects.Any(o=>o.Active&&(o.Kind==5&&!o.Suppressible||o.Type=="FieldGhostTrainTrapController"))){state.Status="native_walk";SendTo(target);return (float)plan.Length;}
   current=new FieldRoutePilot(target,plan.Points.Select(V).ToArray(),protect,DateTime.UtcNow.AddSeconds(LiveProtocol.NavigationSeconds(plan.Length)).Ticks,gates);
   current.Send();return (float)plan.Length;
  }
  void Send(){SendTo(evading?evadeGoal:path[corner]);sentAt=DateTime.UtcNow.Ticks;}
  static void SendTo(Vector3 target){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;
   move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
   if(!move.SetMoveNav(target,null,true))throw new InvalidOperationException("Native detour navigation did not start");
  }
  static void StopMove(){var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;if(field!=null&&field.ὪὨὯὢὫὮὨὩὮὡὬ!=null)field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);}
  internal static void Cancel(string reason,bool stop=true){if(current==null)return;current=null;state.Status="stopped";state.Reason=reason;if(stop)StopMove();}
  internal static void Tick(){if(current==null||DateTime.UtcNow.Ticks<current.next)return;var timer=System.Diagnostics.Stopwatch.StartNew();try{current.Update();}catch(Exception e){Cancel("pilot_error:"+e.GetBaseException().Message);}finally{state.Ticks++;state.TotalTickMs+=timer.Elapsed.TotalMilliseconds;state.MaxTickMs=Math.Max(state.MaxTickMs,timer.Elapsed.TotalMilliseconds);}}
  bool Evade(Vector3 position,Vector3 center,FieldExclusion[] elites,FieldMovingObstacle[] obstacles){
   int mask=Mask();Vector3 offset=center-position;FieldPoint[] best=null;double score=double.PositiveInfinity;
   foreach(double radius in new[]{1.3,2.1,3.2})for(int i=0;i<16;i++){
    double a=Math.PI*i/8;var desired=position+new Vector3((float)(Math.Cos(a)*radius),0,(float)(Math.Sin(a)*radius));NavMeshHit hit;
    if(!NavMesh.SamplePosition(desired,out hit,.45f,mask)||Math.Abs(hit.position.y-position.y)>.8)continue;
    var route=NativePath(P(position),P(hit.position),mask);if(route==null||!FieldAvoidancePlanner.Clear(route,elites))continue;
    if(!FieldAvoidancePlanner.MotionClear(route.Select(p=>p+P(offset)).ToArray(),speed,obstacles,1.6))continue;
    double value=FieldAvoidancePlanner.Length(route)+FieldPoint.Distance(route[route.Length-1],P(goal));
    if(value<score){score=value;best=route;}
   }
   if(best==null)return false;
   evadeGoal=V(best[best.Length-1]);evading=true;waiting=false;state.Status="avoiding_cart";state.Evasions++;Send();return true;
  }
  void Update(){
   long now=DateTime.UtcNow.Ticks;if(now<next)return;next=now+TimeSpan.TicksPerMillisecond*100;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var player=field==null?null:field.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(!Bridge.CollectionReadAllowed||now>until||field==null||player==null||field.ὮὬὬὮὠὮὪὠὧὩὪ==null||field.ὮὬὬὮὠὮὪὠὧὩὪ.Id!=map){Cancel("navigation_context_changed");return;}
   if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"){Cancel("battle",false);return;}
   if(stealth&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth)){Cancel("stealth_expired");return;}
   var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;var agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;
   // Knockback and short native animations temporarily own movement. Wait for
   // native control to return, then replan from the actual landing position.
   if(move.ὠὭὬὪὫὣὠὡὡὠὫ==MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.DontMove){
    if(lockedAt==0)lockedAt=now;state.Status="waiting_for_native_movement";
    if(now-lockedAt>TimeSpan.TicksPerSecond*6)Cancel("local_movement_locked",false);return;
   }
   var position=player.transform.position;
   if(Vector3.Distance(position,goal)<.4f){current=null;state.Status="arrived";return;}
   if(Vector3.Distance(position,lastPosition)>.15f){lastPosition=position;progressAt=now;}
   var scan=Scan();var elites=Elites(scan).Concat(gates).ToArray();
   if(lockedAt!=0){
    lockedAt=0;progressAt=now;var resumed=Plan(position,goal,elites);state.Queries+=resumed.Queries;state.Replans++;
    if(!resumed.Found){Cancel("local_replan_failed:"+resumed.Reason);return;}
    path=resumed.Points.Select(V).ToArray();corner=path.Length>1?1:0;waiting=evading=false;clearSince=0;state.Status="walking";state.Reason="";Send();
   }
   if(evading&&Vector3.Distance(position,evadeGoal)<.4f){
    evading=false;StopMove();var returnPlan=Plan(position,goal,elites);state.Queries+=returnPlan.Queries;
    if(!returnPlan.Found){Cancel("local_replan_failed:"+returnPlan.Reason);return;}
    path=returnPlan.Points.Select(V).ToArray();corner=path.Length>1?1:0;waiting=true;waitSince=now;clearSince=0;
   }
   var remaining=new[]{P(position)}.Concat((evading?new[]{evadeGoal}:path.Skip(corner)).Select(P)).ToArray();
   if(!FieldAvoidancePlanner.Clear(remaining,elites)){
    // Throttling a new path search must never resume the known blocked path.
    if(now-lastPlan<TimeSpan.TicksPerMillisecond*400)return;
    lastPlan=now;var plan=Plan(position,goal,elites);state.Queries+=plan.Queries;state.Replans++;
    if(!plan.Found){StopMove();if(!waiting){waiting=true;waitSince=now;}clearSince=0;state.Status="waiting_for_clearance";state.Reason=plan.Reason;if(now-waitSince>TimeSpan.TicksPerSecond*4)Cancel("elite_clearance_timeout");return;}
    path=plan.Points.Select(V).ToArray();corner=path.Length>1?1:0;state.Length=plan.Length;waiting=false;evading=false;state.Status="walking";state.Reason="";Send();
   }
   if(agent!=null&&agent.velocity.magnitude>1)speed=Math.Max(1,agent.velocity.magnitude);
   var direction=((evading?evadeGoal:path[corner])-position);direction.y=0;var intended=direction.normalized*speed;
   var obstacles=new List<FieldMovingObstacle>();
   var body=SquareRoutePilot.MovementBody(move);float bodyRadius=body==null?.25f:body.radius*Math.Max(Math.Abs(body.transform.lossyScale.x),Math.Abs(body.transform.lossyScale.z));
   var bodyCenter=body==null?position+Vector3.up*.5f:body.bounds.center;float bodyHalf=body==null?.5f:body.bounds.extents.y;
   foreach(var obj in scan.Objects.Where(o=>o.Active&&o.Enabled&&!o.Paused&&o.Type=="FieldGhostTrainTrapController"&&o.Error=="")){
    var p=V(obj.Position);Motion previous;Vector3 velocity=Vector3.zero;
    if(motions.TryGetValue(obj.Instance,out previous)){
     double seconds=(now-previous.At)/(double)TimeSpan.TicksPerSecond;if(seconds>.02&&seconds<1){velocity=(p-previous.Position)/(float)seconds;if(velocity.magnitude>25)velocity=Vector3.zero;}
    }
    motions[obj.Instance]=new Motion{Position=p,Velocity=velocity,At=now};
    foreach(var shape in obj.Shapes.Where(s=>s.Enabled&&s.Trigger)){
     var center=V(shape.Center);float radius=Math.Max(shape.Size[0],shape.Size[2])*.5f+bodyRadius+.15f;
     obstacles.Add(new FieldMovingObstacle{Position=P(center),Velocity=P(velocity),Radius=radius,HalfHeight=shape.Size[1]*.5+bodyHalf});
    }
   }
   bool crossing=obstacles.Any(o=>FieldAvoidancePlanner.Crossing(P(bodyCenter),P(intended),o.Position,o.Velocity,o.Radius,o.HalfHeight,.8));
   bool stopSafe=FieldAvoidancePlanner.MotionClear(new[]{P(bodyCenter)},speed,obstacles.ToArray(),1.6);
   if(crossing){
    if((!stopSafe||waiting&&now-waitSince>TimeSpan.TicksPerSecond*2)&&Evade(position,bodyCenter,elites,obstacles.ToArray()))return;
    clearSince=0;if(!waiting){waiting=true;waitSince=now;state.Yields++;StopMove();}state.Status="yielding_to_cart";state.WaitSeconds=(now-waitSince)/(double)TimeSpan.TicksPerSecond;
    // A stopped/broken cart must not turn into an infinite wait. The host gets
    // a bounded, concrete obstruction so it can choose another local approach.
    if(now-waitSince>TimeSpan.TicksPerSecond*4)Cancel("cart_clearance_timeout");return;
   }
   if(waiting){if(clearSince==0){clearSince=now;return;}if(now-clearSince<TimeSpan.TicksPerMillisecond*150)return;waiting=false;progressAt=now;state.Status="walking";state.Reason="";Send();}
   if(!evading&&corner<path.Length-1&&Vector3.Distance(position,path[corner])<.35f){corner++;Send();}
   // The native arrival callback can finish the previous corner after the next
   // destination was submitted. Resume only our still-owned, checked segment;
   // a real obstruction returns to the host instead of retrying indefinitely.
   if(now-progressAt>TimeSpan.TicksPerSecond*6){Cancel("local_navigation_stalled");return;}
   if(agent!=null&&!agent.pathPending&&(agent.isStopped||!agent.hasPath)&&now-sentAt>TimeSpan.TicksPerMillisecond*300){state.Resumes++;Send();}
  }
 }
}

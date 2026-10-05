using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using BD2Territory;
using gamfs;
namespace BD2Daily.Live {
 // Shares the territory planner, physical progress and temporary exclusion rules.
 // Only movement is automated here. Native proximity/trigger logic owns rewards.
 internal sealed partial class SquareRoutePilot {
  internal static SquareRoutePilot Current;
  private PlayerController player;private PlayerMoveController move;private string scene,account,owner,command,kind;
  private FieldObjectBase target;private Collider reward;private Vector3 goal,origin,previous;
  private AdaptiveLocalRoute search;private RoutePoint[] goals,path=new RoutePoint[0];private int index,plans,expanded;
  private bool active;private string state="idle",reason="",geometry="";private long started,planAt,expires,lastTick,blockedAt;private int tickFrame=-1;
  private double travelled,remaining,planMilliseconds,maxSliceMilliseconds;private readonly LocalMotionProgress progress=new LocalMotionProgress();
  private readonly LocalObstructionMemory blocked=new LocalObstructionMemory();
  private readonly RouteMemo<RouteSampleKey,RoutePoint?> groundCache=new RouteMemo<RouteSampleKey,RoutePoint?>(24000);
  private readonly RouteMemo<RouteEdgeKey,bool> edgeCache=new RouteMemo<RouteEdgeKey,bool>(20000);
  private readonly LocalRouteMemory corridors=new LocalRouteMemory();
  private static RoutePoint Point(Vector3 p)=>new RoutePoint(p.x,p.y,p.z);
  private static Vector3 Vector(RoutePoint p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
  private static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
  internal static void Cancel(string why){if(Current!=null)Current.Finish("cancelled",why);}
  internal static void Begin(Command c,FieldObjectBase fieldTarget,Collider rewardTarget,Vector3 destination){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var actor=field.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(actor==null||!LiveProtocol.IsSquareScene(c.Scene))throw new InvalidOperationException("广场角色或场景不可用");
   Cancel("replaced_by_new_route");var pilot=new SquareRoutePilot{player=actor,move=actor.ὭὦὫὤὮὧὦὡὨὤὢ,scene=c.Scene,account=c.AccountKey,owner=c.PlayerKey,command=c.Id,kind=c.Kind,target=fieldTarget,reward=rewardTarget,goal=destination,origin=actor.transform.position,previous=actor.transform.position,started=DateTime.UtcNow.Ticks,active=true};
   if(pilot.move==null||pilot.Body==null)throw new InvalidOperationException("广场角色碰撞控制器不可用");
   pilot.expires=pilot.started+TimeSpan.FromSeconds(Math.Min(240,Math.Max(75,FlatDistance(pilot.origin,destination)*2+30))).Ticks;
   Current=pilot;pilot.StopMotion();pilot.move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.CharController);pilot.Plan("initial",false);
  }
  internal static void Tick(Frame frame,bool permitted){
   var p=Current;if(p==null||!p.active)return;
   if(p.tickFrame==Time.frameCount)return;p.tickFrame=Time.frameCount;
   try{p.Update(frame,permitted);}catch(Exception ex){p.Finish("failed",ex.GetType().Name+": "+ex.Message);}
  }
  internal static SquareRouteStatus Observe(){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var actor=field==null?null:field.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(actor==null||!LiveProtocol.IsSquareScene(SceneManager.GetActiveScene().name))return null;
   var pos=actor.transform.position;var p=Current;
   return new SquareRouteStatus{State=p==null?"idle":p.state,Reason=p==null?"":p.reason,Command=p==null?"":p.command,Kind=p==null?"":p.kind,X=pos.x,Y=pos.y,Z=pos.z,GoalX=p==null?0:p.goal.x,GoalY=p==null?0:p.goal.y,GoalZ=p==null?0:p.goal.z,Plans=p==null?0:p.plans,Expanded=p==null?0:p.expanded,PathPoints=p==null?0:p.path.Length,Waypoint=p==null?0:p.index,Remaining=p==null?0:p.remaining,Travelled=p==null?0:p.travelled,Started=p==null?0:p.started,Expires=p==null?0:p.expires,PlanMs=p==null?0:p.planMilliseconds,MaxSliceMs=p==null?0:p.maxSliceMilliseconds,FloorHits=p==null?0:p.groundCache.Hits,FloorMisses=p==null?0:p.groundCache.Misses,Geometry=p==null?"":p.geometry,Landmarks=Bridge.Find(typeof(FieldObjectBase)).Cast<FieldObjectBase>().Where(x=>x.gameObject.activeInHierarchy&&(x is NPCController||x is FieldStatueObjectController||x is FieldGuildRaidStatueObjectController||x is Field.SquareStatueObject)).Take(32).Select(x=>{var q=x.ὩὬὧὢὧὬὠὡὦὥὧ;return new SquareLandmark{Name=x.GetType().Name+":"+x.name,Id=x.GetInstanceID(),X=q.x,Y=q.y,Z=q.z};}).ToArray()};
  }
  private void StopMotion(){
   var nav=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;if(nav!=null)nav.ClearQuestNav(true);
   if(move==null)return;move.ClearMove();move.StopMove();var agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;if(agent!=null&&agent.isActiveAndEnabled&&agent.isOnNavMesh)agent.ResetPath();

  }
  private void Finish(string phase,string why){if(active)StopMotion();active=false;state=phase;reason=why;search=null;}
  private RoutePoint? Sample(RoutePoint p){
   var found=groundCache.Get(new RouteSampleKey(p),DateTime.UtcNow.Ticks,()=>{Vector3 floor;return GroundPoint(Vector(p),out floor,false)?(RoutePoint?)Point(floor):null;});
   return found.HasValue&&blocked.SampleAllowed(Point(player.transform.position),found.Value)&&TraversalClear(Vector(found.Value))?found:null;
  }
  private bool Edge(RoutePoint a,RoutePoint b,bool cached){
   if(!blocked.EdgeAllowed(a,b))return false;
   Func<bool> check=()=>{var previous=Vector(a);int count=Math.Max(1,(int)Math.Ceiling(RoutePoint.Distance(a,b)/.12));
    for(int i=1;i<=count;i++){var wanted=Vector3.Lerp(Vector(a),Vector(b),(float)i/count);wanted.y=previous.y;Vector3 ground;
     if(!GroundPoint(wanted,out ground,false)||!TraversalRules.SurfaceChange(ground.y-previous.y,FlatDistance(previous,ground),StepHeight,SlopeLimit,Skin)||!TraversalClear(ground)||Obstacle(previous,ground)!=null)return false;
     previous=ground;
    }return Math.Abs(previous.y-b.Y)<=Math.Max(.12,Skin+.03);
   };
   return cached?edgeCache.Get(new RouteEdgeKey(a,b),DateTime.UtcNow.Ticks,check):check();
  }
  private IEnumerable<RoutePoint> Destinations(){
   var from=player.transform.position;var center=reward==null?goal:reward.bounds.center;center.y=goal.y;
   var choices=new List<Vector3>{center};
   if(reward!=null){var box=reward.bounds;for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)choices.Add(new Vector3(box.center.x+x*Math.Max(.1f,box.extents.x*.65f),goal.y,box.center.z+z*Math.Max(.1f,box.extents.z*.65f)));}
   else foreach(var point in LocalStandPoints.Create(Point(center),Point(from),1.1))choices.Add(Vector(point));
   foreach(var v in choices.OrderBy(v=>FlatDistance(v,from))){Vector3 p;if(!DestinationGround(v,out p))continue;if(reward!=null&&FlatDistance(p,reward.ClosestPoint(p))>BodyRadius*.55f)continue;yield return Point(p);}
  }
  private string DescribeGround(Vector3 v){
   return "body r="+BodyRadius+" h="+Body.height+" lift="+RootLift+" step="+StepHeight+" skin="+Skin+" layer="+Body.gameObject.layer+" origin="+player.transform.position+" goal="+v+" | "+string.Join(";",Physics.RaycastAll(v+Vector3.up,Vector3.down,4,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Take(8).Select(h=>h.collider.name+" layer="+h.collider.gameObject.layer+" y="+h.point.y+" ny="+h.normal.y+" movement="+MovementCollider(h.collider)+" clear="+StandClear(h.point+Vector3.up*RootLift)).ToArray());
  }
  private void Plan(string why,bool stalled){
   StopMotion();plans++;if(plans>6){Finish("failed","反复绕障仍无法到达，已保留目标和诊断");return;}
   var now=DateTime.UtcNow.Ticks;if(stalled){edgeCache.Clear();groundCache.Clear();corridors.Clear();}
   geometry=DescribeGround(goal);goals=Destinations().Take(24).ToArray();if(goals.Length==0){Finish("failed","没有符合实际碰撞规则的目标站位");return;}
   goal=Vector(goals[0]);state="planning";reason=why;planAt=now;blockedAt=0;index=0;path=new RoutePoint[0];
   var saved=corridors.ReuseCorridor(Point(player.transform.position),goals,(a,b)=>Edge(a,b,false));
   if(saved!=null){SetPath(saved);return;}
   search=new AdaptiveLocalRoute(Point(player.transform.position),goals,Sample,(a,b)=>Edge(a,b,true),false,plans>1?18:10);
  }
  private void SetPath(RoutePoint[] points){path=points;index=1;search=null;state="moving";reason="A* 普通方向移动";goal=Vector(path.Last());progress.Begin(DateTime.UtcNow.Ticks,Point(player.transform.position));}
  private bool Arrived(){
   if(target!=null&&reward==null)return target.ὩὤὨὮὥὦὫὭὫὭὨ;
   if(reward!=null)return FlatDistance(player.transform.position,reward.ClosestPoint(player.transform.position))<BodyRadius*.7f;
   return FlatDistance(player.transform.position,goal)<.13f&&Math.Abs(player.transform.position.y-goal.y)<.25f;
  }
  private void Update(Frame frame,bool permitted){
   long now=DateTime.UtcNow.Ticks;
   if(!permitted||player==null||frame==null||frame.AtUtcTicks<now-TimeSpan.FromSeconds(3).Ticks||frame.AccountKey!=account||frame.PlayerKey!=owner||SceneManager.GetActiveScene().name!=scene){Finish("cancelled","停止请求、身份或场景变化");return;}
   if(now>=expires){Finish("failed","广场 A* 路线超出总时间预算");return;}
   bool blockedUi=frame.Surfaces.Any(s=>!LivePolicy.PassiveSurface(s.Type)&&s.Type!="GameFieldDefaultUI");
   string mode=move.ὠὭὬὪὫὣὠὡὡὠὫ.ToString();
   if(blockedUi||mode=="DontMove"||mode=="Anchored"){
    if(blockedAt==0){blockedAt=now;StopMotion();}progress.Begin(now,Point(player.transform.position));
    if(now-blockedAt>TimeSpan.FromSeconds(15).Ticks)Finish("failed","界面或角色移动持续受限，已停止移动并保留现场");return;
   }
   if(blockedAt!=0){blockedAt=0;progress.Begin(now,Point(player.transform.position));}
   var from=player.transform.position;travelled+=FlatDistance(previous,from);previous=from;
   if(Arrived()){Finish("arrived","已到达原生交互范围");return;}
   if(search!=null){
    if(now-planAt>TimeSpan.FromSeconds(20).Ticks){Finish("failed","A* 地面规划超时");return;}
    var timer=System.Diagnostics.Stopwatch.StartNew();var found=search.Step(128,3);var ms=timer.Elapsed.TotalMilliseconds;planMilliseconds+=ms;maxSliceMilliseconds=Math.Max(maxSliceMilliseconds,ms);expanded=search.Expanded;
    if(found==RouteSearchState.Searching)return;
    if(found==RouteSearchState.Exhausted){if(plans<2){Plan("扩大绕行范围",false);return;}Finish("failed","A* 未找到实际可走通路");return;}
    var points=search.Path;corridors.Save(1,now,points);SetPath(points);
   }
   while(index<path.Length&&RoutePoint.Distance(Point(from),path[index])<.11)index++;
   if(index>=path.Length){Plan("终点尚未进入交互范围",false);return;}
   // Amortize expensive physics while issuing normal steering every rendered frame.
   if(now-lastTick>TimeSpan.FromMilliseconds(120).Ticks){
    lastTick=now;
    for(int j=Math.Min(path.Length-1,index+5);j>index;j--)if(RoutePoint.Distance(Point(from),path[j])<=1.8&&Edge(Point(from),path[j],false)){index=j;break;}
    if(!Edge(Point(from),path[index],false)){Plan("前方实际碰撞发生变化",true);return;}
   }
   var dest=Vector(path[index]);var delta=dest-from;delta.y=0;
   remaining=delta.magnitude;for(int i=index+1;i<path.Length;i++)remaining+=RoutePoint.Distance(path[i-1],path[i]);
   if(progress.Stalled(now,Point(from),remaining)){
    blocked.Record(Point(from+delta.normalized*.35f));Plan("连续 3 秒没有净位移，局部绕障重算",true);return;
   }
   player.SetRotation(delta.normalized*Mathf.Clamp(delta.magnitude/.65f,.1f,1f));player.SetMoveStart();
  }
 }
}

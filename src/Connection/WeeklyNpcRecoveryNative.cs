using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Proto.Net;
using Proto.Design.common;
using gamfs;
namespace BD2Daily.Live {
 internal static partial class WeeklyNpcNative {
  static HarmonyLib.Harmony navigationGuard;
  static FieldObjectBase objectNavigation;
  static int objectNavigationMap,objectDialogueQuest;
  static long objectNavigationStarted;
  static void StopObjectNavigation(){
   objectDialogueQuest=0;
   if(objectNavigation==null)return;
   objectNavigation=null;
   FieldRoutePilot.Cancel("npc_approach_finished");
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(field!=null&&field.ὪὨὯὢὫὮὨὩὮὡὬ!=null)field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);
  }
  static void NavigateObject(FieldObjectBase target,int dialogueQuest=0){
   StopObjectNavigation();
   Bridge.ReleaseFieldNavigation();
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
   if(target.ὩὤὨὮὥὦὫὭὫὭὨ){if(dialogueQuest!=0)Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.StartQuestNav(dialogueQuest);return;}
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   UnityEngine.Vector3 point;float distance;string reason;
   // CanNavReach projects onto the navigation surface, but SetMoveNav does not.
   // Submit the same projected endpoint we checked, not the elevated prop pivot.
   if(!CollectionProgressNative.TryApproach(target,out point,out distance,out reason)){result.NavigationRecovery="object_unreachable:"+reason;return;}
   try{FieldRoutePilot.Start(point,false,true);}
   catch(InvalidOperationException e){
    FieldRoutePilot.Cancel("npc_approach_failed");
    field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);result.NavigationRecovery="object_navigation_not_started:"+e.Message;return;
   }
   result.NavigationRecovery="";
   objectNavigation=target;objectNavigationMap=field.ὮὬὬὮὠὮὪὠὧὩὪ.Id;objectDialogueQuest=dialogueQuest;
   objectNavigationStarted=DateTime.UtcNow.Ticks;
   result.NavigationStopTrace="";
  }
  static void TickObjectNavigation(){
   if(objectNavigation==null)return;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(field==null||field.ὪὨὯὢὫὮὨὩὮὡὬ==null||field.ὮὬὬὮὠὮὪὠὧὩὪ==null||field.ὮὬὬὮὠὮὪὠὧὩὪ.Id!=objectNavigationMap
     ||!objectNavigation.gameObject.activeInHierarchy){StopObjectNavigation();return;}
   if(objectNavigation.ὩὤὨὮὥὦὫὭὫὭὨ){
    int dialogue=objectDialogueQuest;StopObjectNavigation();
    // Once in range, the game's own quest action still opens the dialogue and
    // advances it. Movement never fabricates a conversation or quest result.
    if(dialogue!=0&&PlanActive()&&PlannedNavigationAllowed(dialogue))Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.StartQuestNav(dialogue);
    return;
   }
   var pilot=FieldRoutePilot.Capture();
   var move=field.ὪὨὯὢὫὮὨὩὮὡὬ.ὭὦὫὤὮὧὦὡὨὤὢ;var agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;
   // An owned prop reference is not evidence that movement is still running.
   // Expose real termination; the host reobserves/replans under its bounded
   // movement retry policy. Never restart or repeat an interaction here.
   bool nativeStopped=pilot.Status=="native_walk"&&DateTime.UtcNow.Ticks-objectNavigationStarted>TimeSpan.TicksPerSecond
    &&move.ὠὭὬὪὫὣὠὡὡὠὫ!=MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.DontMove
    &&agent!=null&&agent.isOnNavMesh&&!agent.pathPending&&(agent.isStopped||!agent.hasPath);
   if(pilot.Status=="stopped"||pilot.Status=="arrived"||nativeStopped){
    result.NavigationRecovery="object_navigation_stopped:"+(nativeStopped?"native_path_ended_before_object_range":pilot.Reason.Length>0?pilot.Reason:pilot.Status);
    StopObjectNavigation();
   }
  }
  static Command planOwner;static int plannedQuest,plannedPack;static long planUntil;
  static int blockedNavigationQuest,blockedNavigationCount;
  // The native target resolver accepts a gate before a matching NPC if the
  // gate happens to occur first in the scene list. Retain its native dialogue
  // setup, but prefer the live NPC for this explicitly owned TalkManual step.
  static readonly MethodInfo NavigationTargetSetup=FindNavigationTargetSetup();
  static MethodInfo FindNavigationTargetSetup(){
   var methods=typeof(QuestNavigationManager).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic)
    .Where(m=>m.ReturnType==typeof(void)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(FieldObjectBase),typeof(int)})).ToArray();
   return methods.Length==1?methods[0]:null;
  }
  static bool PreferCurrentNpc(QuestNavigationManager __instance,int __0,ref UnityEngine.Vector3 __result){
   TodayQuestTable q;
   if(NavigationTargetSetup==null||!PlanActive()||__0!=plannedQuest||!PlannedNavigationAllowed(__0)
     ||!Tables.TryGetValue(__0,out q)||q.ConditionType!=19||q.MagicValue.Count<2)return true;
   var npc=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.GetNPCController(q.MagicValue[0]);
   if(npc==null||!npc.gameObject.activeInHierarchy)return true;
   NavigationTargetSetup.Invoke(__instance,new object[]{npc,__0});
   __result=npc.ὩὬὧὢὧὬὠὡὦὥὧ;
   return false;
  }
  static void ObserveNavigationStop(MoveController __instance){
   // Record only the first stop of this owned local approach. No callback,
   // game state or return value is modified, and unrelated movement is ignored.
   try{
    var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
    if(objectNavigation==null||result.NavigationStopTrace.Length!=0||field==null||field.ὪὨὯὢὫὮὨὩὮὡὬ==null
      ||!ReferenceEquals(field.ὪὨὯὢὫὮὨὩὮὡὬ.ὭὦὫὤὮὧὦὡὨὤὢ,__instance))return;
    var trace=new System.Diagnostics.StackTrace(false).ToString();
    result.NavigationStopTrace=trace.Length>6000?trace.Substring(0,6000):trace;
   }catch{ /* Optional evidence must not affect movement. */ }
  }
  static void PlanNavigation(Command c,TodayQuestTable q){
   StopObjectNavigation();
   Bridge.ReleaseFieldNavigation();
   if(navigationGuard==null){
    var guard=new HarmonyLib.Harmony("bd2.daily.weekly.navigation.v1");
    guard.Patch(typeof(QuestNavigationManager).GetMethod("StartQuestNav",new[]{typeof(int),typeof(bool)}),
     prefix:new HarmonyLib.HarmonyMethod(typeof(WeeklyNpcNative).GetMethod("BeforeQuestNavigation",BindingFlags.Static|BindingFlags.NonPublic)));
    guard.Patch(typeof(QuestNavigationManager).GetMethod("GetQuestTarget",new[]{typeof(int)}),
     prefix:new HarmonyLib.HarmonyMethod(typeof(WeeklyNpcNative).GetMethod("PreferCurrentNpc",BindingFlags.Static|BindingFlags.NonPublic)));
    foreach(var name in new[]{"SetNavStop","SetNavPause"}){
     try{var method=typeof(MoveController).GetMethod(name,Type.EmptyTypes);if(method!=null)guard.Patch(method,
      prefix:new HarmonyLib.HarmonyMethod(typeof(WeeklyNpcNative).GetMethod("ObserveNavigationStop",BindingFlags.Static|BindingFlags.NonPublic)));}
     catch{ /* Diagnostic tracing is optional, never a prerequisite for navigation. */ }
    }
    navigationGuard=guard;
   }
   planOwner=c;plannedQuest=q.Id;plannedPack=q.PackId;planUntil=DateTime.UtcNow.AddMinutes(10).Ticks;
   // Cancels pending animation coroutines and the map-load ResumeNav callback,
   // not just visible movement. The host will navigate to the selected wild map.
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
   navQuest=0;navUntil=0;
  }
  static bool PlanActive(){return plannedQuest!=0&&Bridge.DailyActionsActive&&Matches(planOwner)&&DateTime.UtcNow.Ticks<planUntil;}
  internal static bool OwnsQuestError(){return PlanActive()&&navQuest==plannedQuest&&Matches(owner);}
  static bool PlannedNavigationAllowed(int id){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var map=field==null?null:field.ὮὬὬὮὠὮὪὠὧὩὪ;TodayQuestTable q;
   return map!=null&&map.PackId==plannedPack&&Tables.TryGetValue(id,out q)
    &&WeeklyNpcProgressGuard.AllowPlannedNavigation(plannedQuest,id,q.ConditionType,map.Id,q.MapId,
      ὧὧὧὪὦὠὭὣὫὡὤ.ὯὠὩὨὯὤὬὬὤὡὫ(map.Id),Active(id));
  }
  static bool BeforeQuestNavigation(int __0){
   if(!PlanActive())return true;
   if(PlannedNavigationAllowed(__0))return true;
   blockedNavigationQuest=__0;blockedNavigationCount++;
   CancelUnplannedNavigation();
   return false;
  }
  static void CancelUnplannedNavigation(){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   // ClearQuestNav dereferences the player when a movement is latched. During
   // scene unload, reject resumption now and clear it after the player is ready.
   if(field!=null&&field.ὪὨὯὢὫὮὨὩὮὡὬ!=null)Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
  }
  internal static void ReleaseNavigationGuard(){
   plannedQuest=plannedPack=0;planUntil=0;planOwner=null;
   if(navigationGuard!=null){
    navigationGuard.Unpatch(typeof(QuestNavigationManager).GetMethod("StartQuestNav",new[]{typeof(int),typeof(bool)}),HarmonyLib.HarmonyPatchType.All,navigationGuard.Id);
    navigationGuard.Unpatch(typeof(QuestNavigationManager).GetMethod("GetQuestTarget",new[]{typeof(int)}),HarmonyLib.HarmonyPatchType.All,navigationGuard.Id);
    foreach(var name in new[]{"SetNavStop","SetNavPause"}){
     var method=typeof(MoveController).GetMethod(name,Type.EmptyTypes);if(method!=null)navigationGuard.Unpatch(method,HarmonyLib.HarmonyPatchType.All,navigationGuard.Id);
    }
   }
   navigationGuard=null;
  }
  static void GuardNavigation(){
   if(plannedQuest==0)return;
   if(!PlanActive()){ReleaseNavigationGuard();return;}
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var map=field==null?null:field.ὮὬὬὮὠὮὪὠὧὩὪ;
   if(map!=null&&map.PackId!=plannedPack){ReleaseNavigationGuard();return;}
   var nav=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   int id=nav.ὢὬὧὣὣὣὪὯὬὮὥ;
   if(id!=0&&nav.ὤὬὪὨὠὢὢὭὧὫὨ&&!PlannedNavigationAllowed(id)){
    blockedNavigationQuest=id;blockedNavigationCount++;CancelUnplannedNavigation();
   }
  }
  static readonly FieldInfo NavigationObjectsField=typeof(QuestNavigationManager).GetField("ὢὧὡὢὥὯὬὬὯὭὧ",BindingFlags.Instance|BindingFlags.NonPublic);
  static int[] NavigationObjects(){
   if(NavigationObjectsField==null)throw new InvalidOperationException("Cannot inspect quest navigation collection cache");
   var value=NavigationObjectsField.GetValue(Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ) as List<int>;
   if(value==null)throw new InvalidOperationException("Quest navigation collection cache has changed");
   return value.ToArray();
  }
  static FieldQuestObjectController[] QuestObjects(int quest){
   return GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldQuestObjectController>()
    .Where(x=>x!=null&&x.ὦὣὣὧὭὭὣὬὭὤὨ==quest&&ObjectEligible(x)).ToArray();
  }
  static void CaptureNavigationRecovery(QuestDBInfo current){
   result.NavigationCollected=NavigationObjects();result.NavigationStale=new int[0];
   if(current==null||!Tables.ContainsKey(current.Id))return;
   var live=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetCurrentPackQuest(current.Id);
   if(live==null)return;var q=Tables[current.Id];
   result.NavigationStale=WeeklyNpcProgressGuard.StaleNavigationObjects(q.ConditionType,QuestObjects(q.Id).Select(x=>x.ὪὬὣὧὥὨὦὯὨὮὬ),live.ObjectId,result.NavigationCollected);
  }
  static void RepairObjectNavigation(TodayQuestTable q){
   if(q.ConditionType!=2&&q.ConditionType!=9)return;
   var pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   var navigation=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var current=pack.GetCurrentPackQuest(q.Id);
   if(current==null)throw new InvalidOperationException("Quest changed before navigation recovery");
   int[] cached=NavigationObjects();
   var objects=QuestObjects(q.Id);
   int[] stale=WeeklyNpcProgressGuard.StaleNavigationObjects(q.ConditionType,objects.Select(x=>x.ὪὬὣὧὥὨὦὯὨὮὬ),current.ObjectId,cached);
   if(stale.Length==0)return;
   bool busy=objects.Any(x=>x.ὨὪὨὩὥὠὡὮὮὠὬ.ToString()!="None");
   bool here=field.ὮὬὬὮὠὮὪὠὧὩὪ!=null&&field.ὮὬὬὮὠὮὪὠὧὩὪ.Id==q.MapId&&pack.ὢὠὮὠὥὥὥὣὡὮὯ==q.PackId;
   if(!WeeklyNpcProgressGuard.CanRepairNavigation(here,objectNavigation!=null||navigation.ὤὬὪὨὠὢὢὭὧὫὨ,Evidence.Waiting||ὮὢὦὯὧὣὦὡὧὯὢ.ὡὦὩὡὠὥὦὣὯὠὣ,busy,q.ConditionCount-current.ObjectId.Distinct().Count(),stale.Length)) {
    result.NavigationRecovery="deferred_busy_or_other_map";return;
   }
   // Rebuild only this task's stale exclusions via native cache methods. Do not
   // mutate QuestDBInfo, replay QuestUpdate, or manufacture QuestClear.
   int[] retained=cached.Except(stale).ToArray();
   navigation.ClearObjectCollected();
   foreach(int id in retained)navigation.AddObjectCollect(id);
   foreach(var target in objects.Where(x=>stale.Contains(x.ὪὬὣὧὥὨὦὯὨὮὬ)))target.RefreshQuest();
   result.NavigationRemoved=stale;result.NavigationRecovery="repaired_stale_collection_cache";
   CaptureNavigationRecovery(current);
  }
 }
}

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
  static int objectNavigationMap;
  static void StopObjectNavigation(){
   if(objectNavigation==null)return;
   objectNavigation=null;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(field!=null&&field.ὪὨὯὢὫὮὨὩὮὡὬ!=null)field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);
  }
  static void NavigateObject(FieldObjectBase target){
   StopObjectNavigation();
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
   if(target.ὩὤὨὮὥὦὫὭὫὭὨ)return;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;
   var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;var point=target.ὩὬὧὢὧὬὠὡὦὥὧ;
   // StartFieldObjectNav(type) only resolves NPCs, gates and waypoints in
   // the client. Quest/carry props must use their actual scene position.
   if(!move.CanNavReach(point)){result.NavigationRecovery="object_unreachable";return;}
   move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
   if(!move.SetMoveNav(point,null,true)){
    field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);result.NavigationRecovery="object_navigation_not_started";return;
   }
   objectNavigation=target;objectNavigationMap=field.ὮὬὬὮὠὮὪὠὧὩὪ.Id;
  }
  static void TickObjectNavigation(){
   if(objectNavigation==null)return;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(field==null||field.ὮὬὬὮὠὮὪὠὧὩὪ==null||field.ὮὬὬὮὠὮὪὠὧὩὪ.Id!=objectNavigationMap
     ||!objectNavigation.gameObject.activeInHierarchy||objectNavigation.ὩὤὨὮὥὦὫὭὫὭὨ)StopObjectNavigation();
  }
  static Command planOwner;static int plannedQuest,plannedPack;static long planUntil;
  static int blockedNavigationQuest,blockedNavigationCount;
  static void PlanNavigation(Command c,TodayQuestTable q){
   StopObjectNavigation();
   if(navigationGuard==null){
    var guard=new HarmonyLib.Harmony("bd2.daily.weekly.navigation.v1");
    guard.Patch(typeof(QuestNavigationManager).GetMethod("StartQuestNav",new[]{typeof(int),typeof(bool)}),
     prefix:new HarmonyLib.HarmonyMethod(typeof(WeeklyNpcNative).GetMethod("BeforeQuestNavigation",BindingFlags.Static|BindingFlags.NonPublic)));
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
   if(navigationGuard!=null)navigationGuard.Unpatch(typeof(QuestNavigationManager).GetMethod("StartQuestNav",new[]{typeof(int),typeof(bool)}),HarmonyLib.HarmonyPatchType.All,navigationGuard.Id);
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

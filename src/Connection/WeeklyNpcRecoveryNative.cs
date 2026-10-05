using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Proto.Net;
using Proto.Design.common;
using gamfs;
namespace BD2Daily.Live {
 internal static partial class WeeklyNpcNative {
  static readonly FieldInfo NavigationObjectsField=typeof(QuestNavigationManager).GetField("ὢὧὡὢὥὯὬὬὯὭὧ",BindingFlags.Instance|BindingFlags.NonPublic);
  static int[] NavigationObjects(){
   if(NavigationObjectsField==null)throw new InvalidOperationException("Cannot inspect quest navigation collection cache");
   var value=NavigationObjectsField.GetValue(Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ) as List<int>;
   if(value==null)throw new InvalidOperationException("Quest navigation collection cache has changed");
   return value.ToArray();
  }
  static void CaptureNavigationRecovery(QuestDBInfo current){
   result.NavigationCollected=NavigationObjects();result.NavigationStale=new int[0];
   if(current==null||!Tables.ContainsKey(current.Id))return;
   var live=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetCurrentPackQuest(current.Id);
   if(live==null)return;var q=Tables[current.Id];
   result.NavigationStale=WeeklyNpcProgressGuard.StaleNavigationObjects(q.ConditionType,q.MagicValue,live.ObjectId,result.NavigationCollected);
  }
  static void RepairObjectNavigation(TodayQuestTable q){
   if(q.ConditionType!=2&&q.ConditionType!=9)return;
   var pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   var navigation=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var current=pack.GetCurrentPackQuest(q.Id);
   if(current==null)throw new InvalidOperationException("Quest changed before navigation recovery");
   int[] cached=NavigationObjects();
   int[] stale=WeeklyNpcProgressGuard.StaleNavigationObjects(q.ConditionType,q.MagicValue,current.ObjectId,cached);
   if(stale.Length==0)return;
   var objects=field.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldQuestObjectController>().Where(x=>x!=null&&x.ὦὣὣὧὭὭὣὬὭὤὨ==q.Id&&q.MagicValue.Contains(x.ὪὬὣὧὥὨὦὯὨὮὬ)).ToArray();
   bool busy=objects.Any(x=>x.ὨὪὨὩὥὠὡὮὮὠὬ.ToString()!="None");
   bool here=field.ὮὬὬὮὠὮὪὠὧὩὪ!=null&&field.ὮὬὬὮὠὮὪὠὧὩὪ.Id==q.MapId&&pack.ὢὠὮὠὥὥὥὣὡὮὯ==q.PackId;
   if(!WeeklyNpcProgressGuard.CanRepairNavigation(here,navigation.ὤὬὪὨὠὢὢὭὧὫὨ,Evidence.Waiting||ὮὢὦὯὧὣὦὡὧὯὢ.ὡὦὩὡὠὥὦὣὯὠὣ,busy,q.ConditionCount-current.ObjectId.Distinct().Count(),stale.Length)) {
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

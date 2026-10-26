using System.Collections.Generic;
using System.Linq;
namespace BD2Daily.Live {
 // Some TodayQuestInfo replies omit in-step ObjectId/Value already confirmed by
 // QuestUpdate. A read must never roll back that live, same-step native state.
 public static class WeeklyNpcProgressGuard {
  // Quest marks include area guides whose data explicitly forbids interaction.
  // Actual pickups can hide their marks, so icon visibility is not eligibility.
  public static bool ManualObject(bool known, int disabled, int automatic) {
   return known&&disabled==0&&automatic==0;
  }
  // Scene ownership and the object's interaction rules are authoritative;
  // the navigation marker alone never establishes eligibility.
  public static bool CanInteractObject(int condition, int quest, int objectQuest, bool ready) {
   return quest>0&&quest==objectQuest&&ready&&(condition==1||condition==2||condition==9||condition==18);
  }
  public static bool AllowPlannedNavigation(int plannedQuest,int requestedQuest,int condition,int currentMap,int targetMap,bool huntingMap,bool active) {
   return plannedQuest>0&&requestedQuest==plannedQuest&&active&&condition!=1&&currentMap>0&&currentMap==targetMap&&!huntingMap;
  }
  // Navigation remembers successful pickups separately from QuestDBInfo. A
  // cancelled/reaccepted quest can keep obsolete exclusions for the whole session.
  public static int[] StaleNavigationObjects(int condition, IEnumerable<int> targets, IEnumerable<int> confirmed, IEnumerable<int> navigation) {
   if(condition!=2&&condition!=9)return new int[0];
   return navigation.Intersect(targets).Except(confirmed).Distinct().OrderBy(x=>x).ToArray();
  }
  public static bool CanRepairNavigation(bool sameMap, bool moving, bool requestPending, bool objectBusy, int remaining, int staleCount) {
   return sameMap&&!moving&&!requestPending&&!objectBusy&&remaining>0&&staleCount>0;
  }
  public static bool WouldRegress(int currentValue, IEnumerable<int> currentObjects, int queriedValue, IEnumerable<int> queriedObjects) {
   return queriedValue < currentValue || currentObjects.Except(queriedObjects).Any();
  }
 }
}

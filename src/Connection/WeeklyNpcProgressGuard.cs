using System.Collections.Generic;
using System.Linq;
namespace BD2Daily.Live {
 // Some TodayQuestInfo replies omit in-step ObjectId/Value already confirmed by
 // QuestUpdate. A read must never roll back that live, same-step native state.
 public static class WeeklyNpcProgressGuard {
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

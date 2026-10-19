namespace BD2FiendHunter.Shared {
// Predicted impact phase: decisions still pass through the game's normal input/cancellation checks.
public static class AttackTiming {
 public static float NextImpact(int boss,string animation,double phase){
  if(boss!=2)return Impact(animation);
  // Tiger combos have contact damage before the visible explosion. Keep each window separate.
  switch(animation){
   case "Attack1":case "Attack2":return .40f;
   case "Attack3":case "Attack4":return .44f;
   case "Attack5":return .16f;
   case "Attack6":return .57f;
   case "Attack7":return .54f;
   case "Attack8":return .45f;
   case "Attack6-2":return .49f;
   case "Attack9":return .32f;
   case "Attack10":case "Attack11":case "Attack12":return .49f;
   case "Attack13":case "Attack14":return phase<.24?.15f:.44f;
   case "Attack13-2":case "Attack14-2":return .44f;
   case "Attack15":case "Attack16":return .42f;
   case "Attack17":case "Attack17-2":return .48f;
   case "Attack18":case "Attack19":return .58f;
   case "Attack20":case "Attack21":return .65f;
   case "Attack22":return .45f;
   case "Roar":return .49f;
   default:return -1;
  }
 }
 public static bool Imminent(string animation,double phase,double duration,double distance,bool attacking){
  float impact=Impact(animation);double eta=(impact-phase)*duration;
  return attacking&&impact>0&&duration>0&&distance<12&&eta<.14&&eta>-.14;
 }
 public static float Impact(string animation){
  switch(animation){
   case "Roar":return .5f;
   case "Attack1":case "Attack2":case "Attack3":case "Attack4":case "Attack1-2":case "Attack2-2":case "Attack3-2":case "Attack4-2":return .49f;
   case "Attack3-3":case "Attack4-3":return .4f;
   case "Attack5":return .49f;
   case "Attack6":case "Rage_Skill3":return .36f;
   case "Attack7":return .48f;
   case "Attack8":return .56f;
   case "Skill1":return .63f;
   case "Skill2":return .84f;
   case "Skill3":return .7f;
   case "Rage_Skill1":case "Rage_Skill2":return .49f;
   case "Rage_Skill4":return .48f;
   case "Rage_Skill5":return .67f;
   case "Rage_Skill6":return .3f;
   default:return -1;
  }
 }
}
}

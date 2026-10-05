using System;
using System.Linq;
using System.Runtime.Serialization;
using F=ὩὯὯὥὧὤὬὠὩὬὭ;
using T=ὪὩὠὪὨὫὦὯὢὮὠ;
namespace BD2Daily.Live {
 [DataContract] internal sealed class FriendshipState {
  [DataMember] public bool Available;
  [DataMember] public int Free,DailyLimit,Selected;
  [DataMember] public FriendshipCandidate[] Candidates=new FriendshipCandidate[0];
 }
 [DataContract] internal sealed class FriendshipCandidate {
  [DataMember] public int Id,Costume,Level,Remaining;
  [DataMember] public bool Max,Quick;
 }
 [DataContract] internal sealed class MonsterSeason {
  [DataMember] public bool Returned,Available;
  [DataMember] public int Season,Monster;
  [DataMember] public string Period="";
 }
 internal static class FriendshipNative {
  internal static FriendshipState Capture(){
   var state=new FriendshipState{Available=F.ὨὪὥὪὪὭὡὥὭὤὢ(false)};
   if(!state.Available)return state;
   state.Free=checked((int)ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.FriendshipApFree));
   state.DailyLimit=ὩὣὩὠὧὠὢὪὬὫὬ.ὢὢὦὬὩὮὭὦὦὬὥ.MaxCounselingAP;
   var ui=Bridge.Find(typeof(FriendshipManageUI)).Cast<FriendshipManageUI>().SingleOrDefault();
   state.Selected=ui==null?0:ui.ὨὢὪὠὧὨὠὠὫὩὪ;
   state.Candidates=T.ὬὮὬὬὨὪὮὪὨὠὥ().Select(id=>T.Ὣὣὧὣὦὣὣὣὠὢὤ(id))
    .Where(t=>t!=null && ὪὪὡὭὩὣὫὭὨὯὨ.ὪὡὥὫὩὤὦὥὫὢὥ(t.CostumeId,ὠὫὣὦὡὨὩὥὨὯὯ.TT_NONE,0L)!=null)
    .Select(t=>new FriendshipCandidate{Id=t.Id,Costume=t.CostumeId,Level=F.ὮὭὣὢὭὭὥὫὯὤὬ(t.Id)?.Level??1,
      Max=F.ὯὨὣὡὩὢὩὨὯὩὬ(t.Id),Remaining=F.ὤὧὧὤὨὨὪὭὨὢὧ(t.Id),Quick=F.ὧὨὠὮὧὨὥὧὡὮὦ(t.Id)}).ToArray();
   return state;
  }
  internal static void Select(Command c){
   var s=Capture();var target=s.Candidates.SingleOrDefault(x=>x.Id==c.Value);
   if(!s.Available||s.Free<1||target==null||target.Max||target.Remaining<1)
    throw new InvalidOperationException("Counseling target is no longer eligible");
   var existing=Bridge.Find(typeof(FriendshipManageUI)).Cast<FriendshipManageUI>().SingleOrDefault();
   if(existing!=null)existing.SelectCostumeItem(target.Id,true);
   else FriendshipManageUI.OpenUIByCostumeId(target.Costume);
  }
  internal static void Complete(Command c,UIBase owner){
   var ui=(FriendshipManageUI)owner;var s=Capture();
   var target=s.Candidates.SingleOrDefault(x=>x.Id==c.Value);
   if(s.Selected!=c.Value||s.Free<1||target==null||target.Max||target.Remaining<1)
    throw new InvalidOperationException("Ordinary counseling eligibility changed");
   var sessions=F.ὫὬὪὭὦὭὨὨὯὫὬ(c.Value);
   if(sessions==null||sessions.Count==0)sessions=T.ὨὬὭὨὬὬὩὠὨὩὬ(c.Value);
   if(sessions==null||sessions.Count==0)throw new InvalidOperationException("Counseling sessions unavailable");
   int session=sessions.OrderBy(x=>x).First();
   if(T.ὦὫὢὨὢὯὠὢὣὣὢ(c.Value,session)==null)throw new InvalidOperationException("Counseling session table unavailable");
   // Same normal counseling request and correct choice index as the game's
   // TimelineVisualNovelManager; no gift, currency purchase or quick unlock.
   F.ὡὤὥὨὩὧὠὭὫὩὫ(c.Value,session,ὩὣὩὠὧὠὢὪὬὫὬ.ὢὢὦὬὩὮὭὦὦὬὥ.CorrectSelectDialogIndex,
    (rewards,correct,exp)=>{if(ui!=null){ui.RefreshUI();ui.AddPendingNoticeInfoes(rewards,exp);}});
  }
  internal static MonsterSeason[] MonsterSeasons(){
   return new[]{false,true}.Select(returned=>{
    var t=ὠὥὯὧὤὢὣὦὯὮὨ.ὦὩὤὠὥὣὬὯὤὮὡ(returned);
    return t==null?new MonsterSeason{Returned=returned}:new MonsterSeason{
     Returned=returned,Available=true,Season=t.SeasonInfo.Season,Monster=t.MonsterHuntId,
     Period=ὠὥὯὧὤὢὣὦὯὮὨ.ὠὧὬὫὨὠὦὢὬὪὬ(returned?"MonsterHunt_Return":"MonsterHunt_Regular").ToString()};
   }).ToArray();
  }
 }
}

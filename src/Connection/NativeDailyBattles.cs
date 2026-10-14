using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using gamfs;
using TheraBytes.BetterUi.gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class NativeEventState {
  [DataMember] public int Current,Total;
  [DataMember] public bool Battle,Result,Lost,Continuing;
 }
 [DataContract] internal sealed class SalvationState {
  [DataMember] public bool Ready,Idle,Unlocked,Season,Popup,FreeOnly;
  [DataMember] public int Level,Selected,Cost,Count,PopupLevel,PopupCost;
  [DataMember] public long Free,Paid,Stones,RemainingCost,Reward;
 }
 // Uses ordinary game menus. No simulated fights, currency edits, or paid attempts.
 internal static class NativeDailyBattles {
  static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  static object Get(object obj,string name){for(var t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(obj);}throw new MissingFieldException(obj.GetType().Name,name);}
  static bool Active(object obj,string field){var go=Get(obj,field) as GameObject;return go!=null&&go.activeInHierarchy;}
  static bool FreeOnly(object popup){var q=Get(popup,"_buttonFreeOnly");return (bool)q.GetType().GetProperty("IsEnable").GetValue(q,null);}
  internal static NativeEventState Event(){
   var bpm=BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   var tuple=ὥὬὢὮὫὯὥὨὬὣὤ.ὤὪὬὯὤὩὩὩὮὭὫ;
   var result=Bridge.Find(typeof(BattleResultUI)).Cast<BattleResultUI>().FirstOrDefault();
   bool eventBattle=bpm!=null&&bpm.IsEventBattleMode();
   bool lost=eventBattle&&result!=null&&Active(result,"_loseObject");
   bool finished=result!=null&&(lost?Active(result,"_objectLoseExitButton"):Active(result,"_winObject")&&Active(result,"_objectWinExitButton"));
   return new NativeEventState{Current=tuple.Item1,Total=tuple.Item2,Battle=eventBattle,Result=eventBattle&&finished,Lost=lost,
    Continuing=eventBattle&&!lost&&ὥὬὢὮὫὯὥὨὬὣὤ.ὧὯὦὡὨὥὡὮὤὣὤ()&&!ὥὬὢὮὫὯὥὨὬὣὤ.ὫὯὣὮὠὭὥὠὯὯὠ()};
  }
  internal static SalvationState Salvation(){
   var lobby=Bridge.Find(typeof(RogueLikeLobbyUI)).Cast<RogueLikeLobbyUI>().FirstOrDefault();
   var popup=Bridge.Find(typeof(RogueLikeSkipPopupUI)).Cast<RogueLikeSkipPopupUI>().FirstOrDefault();
   var currency=ὣὡὧὡὦὣὣὬὨὪὫ.ὬὯὧὣὪὢὡὬὬὯὦ;
   var s=new SalvationState{Ready=lobby!=null,Idle=ὠὦὯὪὦὠὩὦὫὫὡ.ὩὬὡὬὡὣὢὢὪὥὨ(),
    Unlocked=ὣὡὧὡὦὣὣὬὨὪὫ.ὦὤὡὧὯὠὬὬὦὣὭ(Proto.Net.Define_ContentOpenType.ContentUnlockEvilCastleRogueLike,1),
    Season=ὫὨὪὠὢὨὮὤὩὤὮ.ὤὪὦὠὡὠὠὣὮὡὮ.SeasonState(5)==Proto.Net.Define_ScheduleStateType.ScheduleSeason,
    Free=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ(ὥὯὯὠὣὪὦὢὫὠὮ.RoguelikeTicketFree,0),Paid=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ(ὥὯὯὠὣὪὦὢὫὠὮ.RoguelikeTicketPaid,0),Stones=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ(ὥὯὯὠὣὪὦὢὫὠὮ.RoguelikeGrowthPoint,0),
    Level=ὠὦὯὪὦὠὩὦὫὫὡ.ὬὭὩὮὢὯὮὥὫὪὭ,Cost=ὢὭὠὯὨὭὪὡὣὪὧ.ὧὩὡὨὠὥὪὭὢὪὡ.RoguelikeApCount};
   if(lobby==null)return s; // Growth cache is populated by the native lobby info response.
   s.Selected=lobby.ὧὣὨὪὩὡὭὪὧὭὩ.ὤὧὧὦὮὧὪὣὬὦὨ;
   var growth=ὠὦὯὪὦὠὩὦὫὫὡ.ὨὦὯὩὣὦὣὠὦὫὤ;
   Require(growth!=null,"Salvation growth data unavailable");
   foreach(var type in Enum.GetValues(typeof(ὢὭὠὯὨὭὪὡὣὪὧ.ὦὤὫὥὦὨὡὨὪὤὡ)).Cast<ὢὭὠὯὨὭὪὡὣὪὧ.ὦὤὫὥὦὨὡὨὪὤὡ>()){
    if(type.ToString()=="NONE"||type.ToString()=="MAX")continue;
    var levels=ὢὭὠὯὨὭὪὡὣὪὧ.ὨὤὫὨὠὪὧὣὤὦὣ((int)type);
    Require(levels!=null&&levels.Count>0,"Salvation growth table unavailable");
    int current=growth.Where(x=>x.Type==(int)type).Select(x=>x.Level).DefaultIfEmpty(0).Single();
    foreach(var row in levels.Where(x=>x.Id>current)){
     Require(row.PriceType==(int)ὥὯὯὠὣὪὦὢὫὠὮ.RoguelikeGrowthPoint&&row.PriceCount>=0,"Salvation growth price changed");
     s.RemainingCost=checked(s.RemainingCost+row.PriceCount);
    }
   }
   if(s.Level>0){var row=ὢὭὠὯὨὭὪὡὣὪὧ.ὭὠὬὫὯὪὪὮὠὧὤ(s.Level);Require(row!=null,"Salvation cleared level unavailable");s.Reward=checked((long)Math.Truncate(ὢὭὠὯὨὭὪὡὣὪὧ.ὧὩὡὨὠὥὪὭὢὪὡ.ObsidianChangeRate*Math.Truncate(row.QuickBattleScore*row.ScoreBonusRate)));}
   s.Popup=popup!=null;
   if(popup!=null){s.FreeOnly=FreeOnly(popup);s.Count=(int)Get(popup,"ὭὮὭὢὮὫὬὦὣὣὧ");s.PopupLevel=(int)Get(popup,"ὭὦὫὫὣὨὪὨὡὥὤ");s.PopupCost=(int)Get(popup,"ὩὤὦὯὯὨὫὧὥὡὯ");}
   return s;
  }
  internal static void Execute(Command c,UIBase ui){
   if(c.Kind=="salvation_open"){
    var pack=ὤὫὬὭὯὪὢὬὬὩὣ.ὬὣὢὥὫὪὬὮὣὠὦ(3003);
    Require(pack!=null&&pack.PackType==4&&ὣὡὧὡὦὣὣὬὨὪὫ.ὬὣὢὥὫὪὬὮὣὠὦ(3003)!=null,"Salvation cartridge not owned");
    PackListLoopScrollItem.StartPackSelection(pack);return;
   }
   if(c.Kind=="salvation_walk"){
    var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
    Require(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId==3003&&BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()=="BMT_NONE","Salvation field changed");
    var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;
    if(c.Value==0){field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);return;}
    var gate=Bridge.Find(typeof(EvilCastleLobbyGateController)).Cast<EvilCastleLobbyGateController>().Single();
    var collider=gate.GetComponentsInChildren<Collider>().FirstOrDefault(x=>x.isTrigger&&x.enabled);
    Require(collider!=null,"Salvation entrance trigger unavailable");
    var point=collider.bounds.center;point.y=player.transform.position.y;
    Require(move.CanNavReach(point),"Salvation entrance unreachable");
    Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
    move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
    Require(move.SetMoveNav(point,null,true),"Salvation entrance navigation did not start");return;
   }
   if(c.Kind=="salvation_select"||c.Kind=="salvation_popup"){
    var s=Salvation();Require(s.Ready&&s.Idle&&s.Unlocked&&s.Season&&s.Level==c.Items[0]&&s.Level>0,"Salvation sweep eligibility changed");
    var page=((RogueLikeLobbyUI)ui).ὧὣὨὪὩὡὭὪὧὭὩ;
    if(c.Kind=="salvation_popup"){Require(s.Selected==s.Level&&Active(page,"_objQuickClearButtonEnable"),"Salvation sweep button unavailable");page.OnClickUI((GameObject)Get(page,"_objQuickClearButton"));return;}
    var ids=page.ὢὧὠὩὦὬὦὩὧὦὠ;int index=ids.IndexOf(s.Level);Require(index>=0,"Salvation level missing");
    if(c.Value==0){((BetterLoopScrollRect)Get(page,"_levelScroll")).FocusToIndex(index,2,true,ids.Count);return;}
    var row=page.GetComponentsInChildren<RogueLikeLevelScrollItem>().Single(x=>(int)Get(x,"ὪὥὧὮὫὤὮὧὪὣὪ")==index);
    row.OnClickItem(row.gameObject);return;
   }
   if(c.Kind=="salvation_confirm"){
    var s=Salvation();Require(s.Ready&&s.Idle&&s.Unlocked&&s.Season&&s.Popup&&s.FreeOnly,"Salvation sweep is not ready");
    Require(c.Items[0]==s.Level&&s.Selected==s.Level&&s.PopupLevel==s.Level&&c.Items[2]==s.Free&&c.Items[3]==s.Stones&&c.Items[4]==s.RemainingCost,"Salvation plan changed");
    Require(s.Cost>0&&s.PopupCost==s.Cost&&s.Reward>0&&s.RemainingCost>s.Stones,"Salvation growth is already funded");
    long needed=(s.RemainingCost-s.Stones+s.Reward-1)/s.Reward;
    Require(c.Value==s.Count&&c.Value>0&&c.Value<=needed&&(long)c.Value*s.Cost<=s.Free-c.Items[1],"Salvation sweep exceeds free key budget");
    var popup=(RogueLikeSkipPopupUI)ui;Require(!(bool)Get(popup,"ὮὩὪὪὦὤὯὯὧὨὪ"),"Salvation popup is closing");popup.OnClickUI((GameObject)Get(popup,"_buttonBattle"));return;
   }
   if(c.Kind=="event_native_select"){
    var lobby=(EventBattleUI)ui;int id=checked((int)c.Items[2]);
    Require(lobby.ὤὮὯὫὪὡὤὡὫὬὪ==c.Items[0]&&lobby.ὢὣὤὬὮὩὡὬὥὫὩ==c.Items[1]&&id==lobby.ὣὨὭὢὦὪὮὫὣὦὥ,"Event frontier changed");
    if(c.Value==0){((BetterLoopScrollRect)Get(lobby,"_battleStageScroll")).FocusToIndex(lobby.ὥὣὤὧὥὬὤὧὣὤὠ.IndexOf(id),2,true,lobby.ὥὣὤὧὥὬὤὧὣὤὠ.Count);return;}
    lobby.SetSelectItem(lobby.GetComponentsInChildren<EventBattleStageScrollItem>().Single(x=>x.ὯὫὪὡὭὤὪὮὫὬὣ==id),true);return;
   }
   if(c.Kind=="event_native_confirm"){
    var popup=(BattleSkipPopupUI)ui;var lobby=Bridge.Find(typeof(EventBattleUI)).Cast<EventBattleUI>().Single();
    Require(lobby.ὤὮὯὫὪὡὤὡὫὬὪ==c.Items[0]&&lobby.ὢὣὤὬὮὩὡὬὥὫὩ==c.Items[1]&&lobby.ὪὣὨὣὭὤὥὮὡὬὦ==c.Items[2]&&lobby.ὣὨὭὢὦὪὮὫὣὦὥ==c.Items[2],"Event auto target changed");
    Require(Get(popup,"ὩὥὭὬὬὥὡὠὥὧὭ").ToString()=="PackEventBattle"&&Get(popup,"ὥὩὢὦὫὩὭὬὬὦὥ").ToString()=="Auto","Not event native auto popup");
    var t=ὥὬὢὮὫὯὥὨὬὣὤ.ὡὥὡὭὯὫὥὥὮὮὥ((int)c.Items[1],(int)c.Items[2]);
    Require((int)Get(popup,"ὠὬὦὫὩὨὣὫὤὢὨ")==c.Items[1]&&(int)Get(popup,"ὦὠὠὧὩὩὮὥὫὧὡ")==c.Items[2]&&(int)Get(popup,"ὪὪὥὧὡὪὥὡὡὣὡ")==t.BattleDeckId,"Event auto popup changed");
    Require(FreeOnly(popup)&&t.EventApCount>0&&(int)Get(popup,"ὭὮὭὢὮὫὬὦὣὣὧ")==c.Value&&c.Value<=ὣὡὧὡὦὣὣὬὨὪὫ.ὬὯὧὣὪὢὡὬὬὯὦ.EventApFree/t.EventApCount,"Event auto exceeds free AP");
    popup.OnClickUI((GameObject)Get(popup,"_buttonBattle"));return;
   }
   if(c.Kind=="event_native_exit"){
    var state=Event();Require(state.Result&&!state.Continuing,"Event batch is still continuing");
    var result=(BattleResultUI)ui;string name=state.Lost?"_objectLoseExitButton":"_objectWinExitButton";
    Require(Active(result,name),"Event result exit not ready");result.OnClickUI((GameObject)Get(result,name));return;
   }
   throw new InvalidOperationException("Unknown native daily battle operation");
  }
 }
}

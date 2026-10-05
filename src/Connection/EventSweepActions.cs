using System;
using Proto.Net;
using System.Linq;
using System.Reflection;
using UnityEngine;
using TheraBytes.BetterUi.gamfs;
namespace BD2Daily.Live {
 // Native free-AP sweep only. No battle planner or turn operations.
 internal static class EventSweepActions {
  static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  static FieldInfo Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);}
  static bool SweepEligible(EventBattleUI ui,int id){
   int e=ui.ὤὮὯὫὪὡὤὡὫὬὪ,g=ui.ὢὣὤὬὮὩὡὬὥὫὩ;
   var t=ὥὬὢὮὫὯὥὨὬὣὤ.ὡὥὡὭὯὫὥὥὮὮὥ(g,id);
   var p=ὣὩὦὨὭὣὪὢὡὥὮ.ὥὨὯὭὬὡὢὢὩὢὤ(e,g,id);
   var d=ὬὤὬὬὦὨὧὮὭὬὫ.ὩὥὯὭὠὡὨὥὪὬὨ(t.BattleDeckId,true);
   return t.QuickBattlePossible==1&&t.EventApCount>0&&p!=null&&p.EventUid==e&&p.GroupId==g&&p.Id==id&&p.BattleChallengeIndex.OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,d.BonusRewardId.Count))&&ὣὩὦὨὭὣὪὢὡὥὮ.ὯὫὧὢὡὦὫὠὬὫὣ(ui.ὫὥὣὮὧὦὠὯὭὥὬ,g);
  }
  internal static void Execute(Command c,UIBase surface){
   if(c.Kind=="event_sweep_select"){
    var lobby=(EventBattleUI)surface;
    Require(lobby.ὤὮὯὫὪὡὤὡὫὬὪ==c.Items[0]&&lobby.ὢὣὤὬὮὩὡὬὥὫὩ==c.Items[1],"Event sweep scope changed");
    int id=checked((int)c.Items[2]);
    var eligible=lobby.ὥὣὤὧὥὬὤὧὣὤὠ.Where(x=>SweepEligible(lobby,x)).ToArray();
    Require(eligible.Length>0&&(PluginHost.Available?id==eligible.Max():id==15&&lobby.ὫὥὣὮὧὦὠὯὭὥὬ.ToString()=="Challenge"),"Highest cleared sweep stage changed");
    if(lobby.ὪὣὨὣὭὤὥὮὡὬὦ==id)return;
    if(c.Value==0){
     var scroll=(BetterLoopScrollRect)Field(lobby,"_battleStageScroll").GetValue(lobby);
     scroll.FocusToIndex(lobby.ὥὣὤὧὥὬὤὧὣὤὠ.IndexOf(id),2,true,lobby.ὥὣὤὧὥὬὤὧὣὤὠ.Count);return;
    }
    var row=lobby.GetComponentsInChildren<EventBattleStageScrollItem>(false).Single(x=>x.ὯὫὪὡὭὤὪὮὫὬὣ==id);
    lobby.SetSelectItem(row,true);return;
   }
   if(c.Kind=="event_sweep_confirm"){
    var popup=(BattleSkipPopupUI)surface;
    var lobby=Bridge.Find(typeof(EventBattleUI)).Cast<EventBattleUI>().Single();
    Require(lobby.ὤὮὯὫὪὡὤὡὫὬὪ==c.Items[0]&&lobby.ὢὣὤὬὮὩὡὬὥὫὩ==c.Items[1]&&lobby.ὪὣὨὣὭὤὥὮὡὬὦ==c.Items[2],"Event sweep scope changed");
    int id=checked((int)c.Items[2]);
    Require(SweepEligible(lobby,id)&&(PluginHost.Available?id==lobby.ὥὣὤὧὥὬὤὧὣὤὠ.Where(x=>SweepEligible(lobby,x)).Max():id==15&&lobby.ὫὥὣὮὧὦὠὯὭὥὬ.ToString()=="Challenge"&&c.Value<=5),"Sweep eligibility changed");
    var t=ὥὬὢὮὫὯὥὨὬὣὤ.ὡὥὡὭὯὫὥὥὮὮὥ(lobby.ὢὣὤὬὮὩὡὬὥὫὩ,id);
    Func<string,object> get=n=>Field(popup,n).GetValue(popup);
    Require((Define_BattleModeType)get("ὩὥὭὬὬὥὡὠὥὧὭ")==Define_BattleModeType.PackEventBattle&&get("ὥὩὢὦὫὩὭὬὬὦὥ").ToString()=="Skip","Not event sweep popup");
    Require((int)get("ὠὬὦὫὩὨὣὫὤὢὨ")==c.Items[1]&&(int)get("ὦὠὠὧὩὩὮὥὫὧὡ")==id&&(int)get("ὪὪὥὧὡὪὥὡὡὣὡ")==c.Items[3]&&t.BattleDeckId==c.Items[3],"Sweep popup stage changed");
    var free=typeof(QuickBattleUI).GetField("_buttonFreeOnly",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(popup);
    Require((bool)free.GetType().GetProperty("IsEnable").GetValue(free,null),"Free AP only required");
    Require((int)get("ὭὮὭὢὮὫὬὦὣὣὧ")==c.Value&&t.EventApCount>0&&(int)get("ὩὤὦὯὯὨὫὧὥὡὯ")==t.EventApCount&&c.Value<=ὣὡὧὡὦὣὣὬὨὪὫ.ὬὯὧὣὪὢὡὬὬὯὦ.EventApFree/t.EventApCount,"Sweep count exceeds free AP");
    Require(!(bool)get("ὧὧὦὨὠὥὬὦὢὪὦ"),"Sweep popup is busy");
    var button=(GameObject)get("_buttonBattle");Require(button.activeInHierarchy,"Sweep confirmation hidden");popup.OnClickUI(button);return;
   }
   throw new InvalidOperationException("Unknown sweep operation");
  }
 }
}

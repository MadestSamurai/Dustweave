using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Google.Protobuf;
using Proto.Net;
using UnityEngine;
namespace BD2Daily.Live {
 [DataContract] internal sealed class RewardPage {
  [DataMember] public string[] Catalog=new string[0],Missions=new string[0];
  [DataMember] public string Kind="",Table="",Group="",Cache="",Schedule="",Currency="";
  [DataMember] public int TableId,EventId,Tab,Unlocked,Page,Received,Total,Batch,Cost;
  [DataMember] public long Balance;
  [DataMember] public bool Ready,Claim,Free,Renew,AllowedCurrency,Auto,Single;
 }
 [DataContract] internal sealed class PassDefinition {
  [DataMember] public int PassId;
  [DataMember] public string Table="",Group="",Schedule="",Title="";
  [DataMember] public string[] Missions=new string[0];
 }
 internal static class RewardNative {
  // Use the same dated ChangeMissionTable predicate as PassRootUI. The packaged
  // MissionTable includes historical replacements and is not the active task set.
  internal static PassDefinition CapturePass(){
   var ui=Bridge.Find(typeof(PassRootUI)).Cast<PassRootUI>().SingleOrDefault();
   var result=new PassDefinition();if(ui==null)return result;
   result.PassId=ui.ὢὡὧὮὫὫὬὥὪὪὢ;
   var owner=Bridge.Find(typeof(PassUI)).Cast<PassUI>().SingleOrDefault();
   // The prefab becomes active before RefreshPassUI assigns its ID. Native
   // table getters show a game error popup for ID 0, even if we catch exceptions.
   // Observation must not call them until the page belongs to an active pass.
   if(owner==null||!LivePolicy.PassDefinitionReady(result.PassId,owner.ὠὤὭὯὯὪὯὧὮὪὬ.Select(x=>x.ὥὬὭὪὪὦὬὩὦὧὠ)))return result;
   var table=ὡὨὩὥὦὣὧὫὮὤὮ.ὥὦὩὦὯὮὥὮὥὮὪ(result.PassId);
   var group=ὡὨὩὥὦὣὧὫὮὤὮ.ὡὨὩὩὡὤὩὤὯὧὭ(result.PassId);
   result.Table=Json(table);result.Group=Json(group);
   result.Schedule=Json(ὧὫὫὠὪὫὯὢὩὮὤ.ὬὯὭὠὧὠὣὭὤὯὥ(Define_EventType.EventPass,result.PassId));
   result.Title=ὪὥὦὥὭὠὦὡὠὧὤ.ὬὡὢὧὤὠὦὯὥὥὪ(table.BannerFontLocalTextId);
   result.Missions=group.MissionGroupId.SelectMany(id=>ὮὦὮὣὥὢὠὫὭὫὭ.ὯὧὠὠὥὫὡὫὤὧὥ(id)).Select(id=>Json(ὮὦὮὣὥὢὠὫὭὫὭ.ὢὦὩὩὢὪὫὯὫὯὩ(id))).ToArray();
   return result;
  }

  static string Json(IMessage m){return m==null?"null":JsonFormatter.Default.Format(m);}
  static T Get<T>(object o,string name){for(var t=o.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);if(f!=null)return (T)f.GetValue(o);}throw new MissingFieldException(name);}
  static bool Enabled(object o,string name){var b=Get<ButtonOnOffComponent>(o,name);return b!=null&&b.IsOn();}
  static bool Token(int kind,int id){return (kind==8||kind==13||kind==14) && id>0;}
  internal static RewardPage Capture(){
   // Same eligibility predicate as EventUI.HasEvent, readable without opening it.
   var active=ὠὩὩὦὢὭὤὩὦὧὯ.ὠὮὮὢὭὩὥὤὯὧὭ().Where(t=>ὧὫὫὠὪὫὯὢὩὮὤ.ὤὢὧὨὭὩὡὤὧὪὣ((Define_EventType)t.EventType,t.EventId));
   var r=new RewardPage{Catalog=active.Select(t=>Json(t)).ToArray()};
   var ui=Bridge.Find(typeof(EventUI)).Cast<EventUI>().SingleOrDefault();
   if(ui==null)return r;
   // Selection indices belong to the rendered UI, not the independent catalogue.
   var rows=ui.ὠὣὦὯὭὪὨὣὣὧὯ.Select(id=>ὠὩὩὦὢὭὤὩὦὧὯ.ὬὡὢὭὤὬὫὤὡὦὠ(id)).ToArray();
   int idx=ui.ὫὬὮὫὪὢὠὯὥὮὡ;if(idx<0||idx>=rows.Length)return r;
   var t=rows[idx];r.TableId=t.Id;r.EventId=t.EventId;r.Table=Json(t);
   var p=ui.ὥὮὬὣὢὦὢὯὪὪὭ;if(p==null||!p.gameObject.activeInHierarchy)return r;
   r.Kind=p.GetType().Name;r.Ready=!p.IsBlockOtherTouch();
   var schedule=ὧὫὫὠὪὫὯὢὩὮὤ.ὬὯὭὠὧὠὣὭὤὯὥ((Define_EventType)t.EventType,t.EventId);r.Schedule=Json(schedule);
   if(p is EventMissionUI){
    if(Get<int>(p,"ὡὨὨὠὬὡὪὨὤὪὦ")!=t.EventId){r.Ready=false;return r;}
    var g=ὮὦὮὣὥὢὠὫὭὫὭ.ὡὨὩὩὡὤὩὤὯὧὭ(t.EventId);r.Group=Json(g);
    r.Tab=Get<int>(p,"ὠὢὤὨὨὣὡὨὫὪὮ");r.Unlocked=g.MissionGroupId.Count-1;
    if(g.EventMissionType==3){if(schedule==null)throw new InvalidOperationException("Mission schedule missing");r.Unlocked=Math.Min(r.Unlocked,(ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now()-ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStampToDateTime(schedule.StartDate)).Days);}
    r.Claim=Enabled(p,"_recievAllButton");
    r.Missions=((EventMissionUI)p).ὦὪὡὮὪὦὫὭὯὡὣ.Select(id=>Json(ὮὦὮὣὥὢὠὫὭὫὭ.ὢὦὩὩὢὪὫὯὫὯὩ(id))).ToArray();
   }else if(p is MiniGameRouletteUI){
    var roulette=(MiniGameRouletteUI)p;var g=roulette.GetMiniGameRouletteTable();r.Group=Json(g);r.Cache=Json(roulette.GetRouletteDBInfo());
    if(g.Id!=t.EventId){r.Ready=false;return r;}
    r.Currency=((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType).ToString();r.AllowedCurrency=Token(g.ItemType,g.ItemId);r.Cost=g.ItemCount;
    r.Balance=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType,g.ItemId);
    r.Single=Get<ButtonOnOffComponent>(p,"_btnPlay_Once").gameObject.activeInHierarchy&&Enabled(p,"_btnPlay_Once");r.Free=roulette.GetRouletteDBInfo().FreeApCount>0&&Enabled(p,"_btnPlay_Free");r.Batch=roulette.GetCanMaxDrawCountForOnce();r.Claim=Enabled(p,r.Batch>1?"_btnPlay_Several":"_btnPlay_Once");
   }else if(p is MiniGameDiceUI){
    var dice=(MiniGameDiceUI)p;var g=ὡὥὯὥὢὡὥὣὭὫὨ.ὧὠὢὠὮὤὦὧὡὭὨ(t.EventId);
    r.Group=Json(g);if(g==null||Get<int>(p,"ὢὯὡὣὧὦὡὯὭὩὬ")!=t.EventId){r.Ready=false;return r;}
    var actual=Get<EventScheduleDBInfo>(p,"ὭὦὬὬὮὭὡὧὣὨὭ");
    if(schedule==null||actual.Id!=schedule.Id){r.Ready=false;return r;}
    r.Cost=g.ItemCount;r.AllowedCurrency=Token(g.ItemType,g.ItemId);r.Currency=((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType).ToString();
    r.Balance=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType,g.ItemId);
    r.Auto=MiniGamesNative.Auto(dice);r.Claim=Enabled(p,"_buttonThrowDice");
    r.Claim=r.Claim&&ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now()<ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStampToDateTime(schedule.EndDate);
   }else if(p is MiniGamePuzzleUI){
    if(Get<int>(p,"ὦὠὧὠὯὯὢὮὡὥὬ")!=t.EventId){r.Ready=false;return r;}
    var actual=Get<EventScheduleDBInfo>(p,"ὭὦὬὬὮὭὡὧὣὨὭ");
    if(schedule==null||actual==null||actual.Id!=schedule.Id){r.Ready=false;return r;}
    var g=ὢὮὪὮὧὥὢὬὨὤὡ.ὡὣὧὧὮὮὧὦὬὦὮ(t.EventId);
    // Use the server cache: the UI snapshot stays on the old board until reward animations finish.
    var info=ὡὥὨὥὩὡὠὤὠὧὬ.ὥὧὦὤὩὩὯὣὢὡὩ(schedule.Id);
    if(g==null||info==null||info.EventScheduleId!=schedule.Id){r.Ready=false;return r;}
    r.Group=Json(g);r.Cache=Json(info);r.Cost=g.ItemCount;
    r.AllowedCurrency=Token(g.ItemType,g.ItemId);r.Currency=((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType).ToString();
    r.Balance=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType,g.ItemId);
    r.Page=info.ClearCount;r.Total=checked(g.ColumnCount*g.ColumnCount);r.Received=info.PuzzleOpen.Count;
    if(r.Cost<=0||r.Total<=0||r.Received>r.Total)throw new InvalidOperationException("Puzzle board or token cost is invalid");
    r.Batch=checked((int)Math.Min(r.Balance/r.Cost,r.Total-r.Received));
    r.Claim=Enabled(p,"_buttonPlayAll");r.Renew=Enabled(p,"_buttonRenewal");
    r.Ready=r.Ready&&Json(Get<MiniPuzzleDBInfo>(p,"ὯὭὩὩὭὡὪὤὦὡὣ"))==r.Cache;
   }else if(p is EventExchangeUI){
    int id=Get<int>(p,"ὥὠὦὢὮὬὨὤὠὤὭ");if(id!=t.EventId){r.Ready=false;return r;}
    var g=ὣὮὢὠὩὬὨὯὤὬὧ.ὭὭὢὡὤὤὢὫὯὭὬ(id);r.Group=Json(g);
    r.Cache=Json(schedule==null?null:ὧὫὫὠὪὫὯὢὩὮὤ.ὧὢὩὮὨὢὩὨὢὨὦ(schedule.Id,id));
    r.Currency=((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType).ToString();r.AllowedCurrency=Token(g.ItemType,g.ItemId)&&!t.IsExchangeTypeLuckyDraw();r.Cost=g.ItemCount;
    r.Balance=ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ((ὥὯὯὠὣὪὦὢὫὠὮ)g.ItemType,g.ItemId);
    r.Page=Get<int>(p,"ὪὮὬὭὬὢὦὠὣὭὦ");r.Received=Get<int>(p,"ὣὭὪὤὥὫὫὨὫὥὤ");r.Total=Get<int>(p,"ὯὥὣὨὨὯὤὤὫὦὣ");r.Batch=Get<int>(p,"ὫὧὡὭὪὯὩὩὤὧὥ");
    r.Claim=Enabled(p,"_maximumButton");r.Renew=Enabled(p,"_renewalButton");
    r.Ready=r.Ready&&!Get<bool>(p,"ὪὢὬὬὥὧὥὯὣὮὬ")&&!Get<bool>(p,"ὮὩὨὪὫὩὫὭὩὫὣ")&&!Get<bool>(p,"ὡὫὫὥὦὨὥὨὧὮὡ");
   }
   return r;
  }
  internal static void Dispatch(Command c,UIBase owner){
   var ui=(EventUI)owner;var state=Capture();
   if(c.Kind=="reward_select"){
    if(!ui.HasEvent(c.Value))throw new InvalidOperationException("Event no longer active");
    var t=ὠὩὩὦὢὭὤὩὦὧὯ.ὬὡὢὭὤὬὫὤὡὦὠ(c.Value);
    if(!new[]{4,7,12,17,19}.Contains(t.EventType))throw new InvalidOperationException("Unsupported reward category");
    if(ui.ὥὮὬὣὢὦὢὯὪὪὭ!=null&&ui.ὥὮὬὣὢὦὢὯὪὪὭ.IsBlockOtherTouch())throw new InvalidOperationException("Previous event busy");
    ui.SetByEvent(c.Value);return;
   }
   if(!state.Ready||c.Items.Length!=1||c.Items[0]!=state.TableId)throw new InvalidOperationException("Reward selection changed or busy");
   var p=ui.ὥὮὬὣὢὦὢὯὪὪὭ;
   if(c.Kind=="reward_tab"){
    if(!(p is EventMissionUI)||c.Value<0||c.Value>state.Unlocked)throw new InvalidOperationException("Mission tab locked");
    var tab=Get<IndexedToggleTabGroupComponent>(p,"_toggleTabGroup").ὯὠὯὨὨὤὠὫὪὩὧ.Single(x=>x.ὪὥὩὪὧὦὡὢὧὬὮ==c.Value);
    p.OnClickUI(tab.gameObject);return;
   }
   if(p is MiniGameDiceUI){
    if(c.Value==7){MiniGamesNative.StopDice();return;}
    if(c.Value!=6||state.Auto||!state.AllowedCurrency||!state.Claim||state.Cost<=0||state.Balance<state.Cost)throw new InvalidOperationException("Dice no longer eligible");
    MiniGamesNative.StartDice((MiniGameDiceUI)p,c);return;
   }
   string button=null;
   if(p is MiniGamePuzzleUI&&state.AllowedCurrency){
    if(c.Value==9&&state.Claim&&state.Batch>0&&state.Cost>0&&state.Balance>=(long)state.Batch*state.Cost)button="_buttonPlayAll";
   }
   if(c.Value==1&&p is EventMissionUI&&state.Claim)button="_recievAllButton";
   if(p is MiniGameRouletteUI){
    if(c.Value==2&&state.Free)button="_btnPlay_Free";
    if(c.Value==8&&state.Single&&state.AllowedCurrency&&state.Cost>0&&state.Balance>=state.Cost)button="_btnPlay_Once";
    if(c.Value==3&&state.AllowedCurrency&&state.Claim&&state.Batch>0&&state.Cost>0&&state.Balance>=(long)state.Batch*state.Cost)button=state.Batch>1?"_btnPlay_Several":"_btnPlay_Once";
   }
   if(p is EventExchangeUI&&state.AllowedCurrency){
    if(c.Value==4&&state.Claim&&state.Batch>0&&state.Cost>0&&state.Balance>=(long)state.Batch*state.Cost)button="_maximumButton";
    // Finish each page before advancing; a key item alone never discards its remainder.
    if(c.Value==5&&state.Renew&&state.Total>0&&state.Received>=state.Total)button="_renewalButton";
   }
   if(button==null)throw new InvalidOperationException("Reward action no longer eligible");
   p.OnClickUI(Get<ButtonOnOffComponent>(p,button).gameObject);
  }
 }
}

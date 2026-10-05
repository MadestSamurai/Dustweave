using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Google.Protobuf;
using Proto.Net;
using Proto.Design.pack11001;
using UnityEngine;
using UnityEngine.UI;
namespace BD2Daily.Live {
 [DataContract] internal sealed class QuizRow {
  [DataMember] public int Group,Id,OpenDay;
  [DataMember] public bool Open,Complete;
 }
 [DataContract] internal sealed class QuizPage {
  [DataMember] public int HubUid,EventUid,HubId,Group,QuizId,Cursor;
  [DataMember] public bool Available,Playing,Touch,Choosing;
  [DataMember] public QuizRow[] Quizzes=new QuizRow[0];
  [DataMember] public string[] Talks=new string[0],Selects=new string[0];
  [DataMember] public int[] Displayed=new int[0];
 }
 [DataContract] internal sealed class DiceLease {
  [DataMember] public string Instance="",AccountKey="",PlayerKey="";
  [DataMember] public long ExpiresUtcTicks;
 }
 [DataContract] internal sealed class SichuanDailyHub {
  [DataMember] public int Uid,Event;
 }
 [DataContract] internal sealed class SichuanDailyPage {
  [DataMember] public SichuanDailyHub[] Hubs=new SichuanDailyHub[0];
  [DataMember] public bool Ready,Playing,StartEnabled;
  [DataMember] public int Group,Level,Max;
  [DataMember] public int[] Open=new int[0],Cleared=new int[0];
 }
 internal static class MiniGamesNative {
  internal static T Get<T>(object o,string name){
   for(var t=o.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);if(f!=null)return(T)f.GetValue(o);}
   throw new MissingFieldException(name);
  }
  static T UI<T>() where T:UnityEngine.Object {return Bridge.Find(typeof(T)).Cast<T>().SingleOrDefault();}
  static string Json(IMessage value){return JsonFormatter.Default.Format(value);}
  internal static QuizPage Capture(){
   var result=new QuizPage();
   var hub=ὤὯὯὬὧὬὯὣὢὯὨ.ὠὮὫὫὯὭὡὡὨὢὬ;
   if(hub==null)return result;
   result.HubUid=hub.Uid;result.HubId=hub.HubId;
   var slot=hub.GetMiniEventHubSlotDBInfo(ὠὤὯὮὧὪὠὦὯὯὨ.Quiz);
   if(slot==null)return result;
   result.EventUid=slot.EventUid;result.Available=hub.IsOpenHub()&&slot.IsOpen();
   var table=ὥὬὢὮὫὯὥὨὬὣὤ.ὤὣὭὡὯὥὭὬὥὫὡ(hub.HubId,11);
   if(table==null)return result;
   int days=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.GetPassedDayasBasedOnDailyResetTime(slot.GetStartTime(),ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now());
   result.Quizzes=ὭὫὠὫὤὢὨὡὮὯὢ.ὧὪὠὧὨὭὡὦὫὠὪ(table.HubContentId).Select(q=>new QuizRow{
    Group=q.GroupId,Id=q.Id,OpenDay=q.OpenDelayDays,Open=result.Available&&days>=q.OpenDelayDays,
    Complete=ὨὢὠὥὧὡὦὤὬὨὦ.ὤὦὦὦὮὬὢὬὡὧὬ(slot.EventUid,q.GroupId,q.Id)}).ToArray();
   var active=ὤὯὯὬὧὬὯὣὢὯὨ.ὣὣὨὯὢὡὪὤὥὨὭ;
   var ui=UI<BalloonScriptUI>();
   if(active==null||ui==null)return result;
   result.Playing=true;result.Group=active.GroupId;result.QuizId=active.Id;
   var rows=Get<List<NPCTalkTable>>(ui,"ὥὬὭὢὯὦὭὪὤὡὠ");
   result.Talks=rows.Select(x=>Json(x)).ToArray();
   result.Cursor=Get<int>(ui,"ὧὢὫὫὢὩὫὧὦὩὢ");
   result.Touch=Get<GameObject>(ui,"_objTouchButton").activeInHierarchy;
   var select=Get<VisualNovelSelectChoiceUI>(ui,"_selectUI");result.Choosing=select!=null&&select.gameObject.activeInHierarchy;
   result.Selects=rows.Where(x=>x.SelectDialogId>0).Select(x=>x.SelectDialogId).Distinct().Select(id=>Json(ὨὧὠὡὨὬὣὦὩὣὢ.ὫὭὣὪὮὥὭὯὭὠὪ(id))).ToArray();
   if(result.Choosing){
    if(result.Cursor<1||result.Cursor>rows.Count)throw new InvalidOperationException("Quiz cursor invalid");
    var choice=rows[result.Cursor-1];if(choice.BubbleType!=4||choice.SelectDialogId<=0)throw new InvalidOperationException("Quiz choice changed");
    result.Displayed=Get<Dictionary<int,List<int>>>(ui,"ὨὠὪὬὦὫὦὡὡὮὢ")[choice.SelectDialogId].ToArray();
   }
   return result;
  }
  internal static void Dispatch(Command c,UIBase owner){
   var p=Capture();
   if(c.Kind=="quiz_hub"){
    if(!p.Available||p.HubUid!=c.Value)throw new InvalidOperationException("Quiz event closed or changed");
    ὤὯὯὬὧὬὯὣὢὯὨ.ὤὣὬὦὨὭὫὠὤὬὥ();return;
   }
   if(c.Kind=="quiz_list"){
    if(!p.Available||p.HubUid!=c.Value)throw new InvalidOperationException("Quiz hub changed");
    ((MiniEventMainUI)owner).OnClickContentBtn(ὠὤὯὮὧὪὠὦὯὯὨ.Quiz);return;
   }
   if(c.Kind=="quiz_leave"){((MiniEventMainUI)owner).CloseEvent();return;}
   if(c.Items.Length!=3||c.Items[0]!=p.EventUid||!p.Available)throw new InvalidOperationException("Quiz identity changed");
   int group=(int)c.Items[1],id=(int)c.Items[2];
   var q=p.Quizzes.Single(x=>x.Group==group&&x.Id==id);
   if(!q.Open||q.Complete)throw new InvalidOperationException("Quiz already complete or locked");
   if(c.Kind=="quiz_play"){if(p.Playing)throw new InvalidOperationException("Another quiz running");((MiniEventQuizUI)owner).PlayQuiz(group,id);return;}
   if(!p.Playing||p.Group!=group||p.QuizId!=id)throw new InvalidOperationException("Quiz dialogue changed");
   var balloon=(BalloonScriptUI)owner;
   if(c.Value==0){
    if(p.Choosing||!p.Touch)throw new InvalidOperationException("Quiz dialogue not ready");
    balloon.OnClickUI(Get<GameObject>(balloon,"_objTouchButton"));return;
   }
   if(!p.Choosing||!p.Displayed.Contains(c.Value))throw new InvalidOperationException("Quiz option unavailable");
   var choiceUI=Get<VisualNovelSelectChoiceUI>(balloon,"_selectUI");
   int index=Array.IndexOf(p.Displayed,c.Value);
   var item=choiceUI.GetComponentsInChildren<VisualNovelSelectChoiceItem>().Single(x=>x.gameObject.activeInHierarchy&&x.ὪὥὩὪὧὦὡὢὧὬὮ==index);
   var button=Get<Button>(item,"_button");if(!button.interactable)throw new InvalidOperationException("Quiz option not ready");
   choiceUI.OnClickUI(button.gameObject);
  }
  internal static SichuanDailyPage Sichuan(){
   var p=new SichuanDailyPage();var entries=new List<SichuanDailyHub>();
   foreach(var h in ὢὫὠὮὮὧὪὪὣὨὯ.ὠὯὪὧὭὭὭὪὣὡὦ()){
    var e=ὧὫὫὠὪὫὯὢὩὮὤ.ὡὬὧὠὫὭὨὫὩὪὧ(h.EventUid);if(e==null||!ὧὫὫὠὪὫὯὢὩὮὤ.ὤὢὧὨὭὩὡὤὧὪὣ(e))continue;
    var t=ὥὬὢὮὫὯὥὨὬὣὤ.ὡὫὨὬὯὩὦὭὫὠὩ(e.EventId);if(t!=null&&t.EventClearType==(int)ὫὯὯὨὤὪὥὫὨὥὫ.SichuanMahjong)entries.Add(new SichuanDailyHub{Uid=e.Id,Event=e.EventId});
   }
   p.Hubs=entries.OrderByDescending(x=>x.Uid).ToArray();var ui=UI<SichuanStagePopupUI>();var main=UI<SichuanMainUI>();SichuanBoardUI board;p.Playing=SichuanBoardUI.TryGetActiveBoardUI(out board);
   p.Ready=(ui!=null||main!=null||p.Playing)&&ὥὯὯὤὫὠὢὯὠὤὤ.ὭὧὠὡὠὡὠὦὨὬὨ!=null;
   if(p.Playing){p.Group=board.ὫὠὧὤὥὡὢὡὬὡὮ;p.Level=board.ὣὧὯὧὤὩὦὭὬὢὢ;}
   if(!p.Ready)return p;p.Max=ὢὣὠὢὯὥὯὨὩὤὭ.ὪὪὬὣὨὣὭὭὡὮὮ(1);
   if(p.Max<1||p.Max>1000)throw new InvalidOperationException("Invalid regular Sichuan levels");
   p.Open=Enumerable.Range(1,p.Max).Where(id=>ὥὯὯὤὫὠὢὯὠὤὤ.ὥὤὪὤὫὯὯὭὡὠὠ(1,id)).ToArray();
   p.Cleared=Enumerable.Range(1,p.Max).Where(id=>{var r=ὥὯὯὤὫὠὢὯὠὤὤ.ὦὧὤὪὯὢὫὥὠὧὬ(1,id);return r!=null&&ὥὯὯὤὫὠὢὯὠὤὤ.ὪὬὡὬὢὨὬὥὢὧὨ(r);}).ToArray();
   if(ui!=null){p.Group=Get<int>(ui,"ὬὥὪὬὮὫὤὪὤὧὪ");p.Level=Get<int>(ui,"ὤὫὤὦὣὫὪὧὨὣὣ");p.StartEnabled=Get<GameObject>(ui,"_buttonStart").activeInHierarchy;}return p;
  }
  internal static void DispatchSichuan(Command c,UIBase owner){
   var p=Sichuan();
   if(c.Kind=="sichuan_open"){
    var h=p.Hubs.Single(x=>x.Uid==c.Value&&x.Event==c.Items[0]);((MiniGameHubUI)owner).ClickPlayMiniGame(h.Uid,h.Event);return;
   }
   if(c.Kind=="sichuan_select"){
    if(!p.Ready||p.Playing||c.Items.Length!=0||!p.Open.Contains(c.Value))throw new InvalidOperationException("Regular Sichuan stage unavailable");
    ((SichuanStagePopupUI)owner).SetUI(1,c.Value);return;
   }
   throw new InvalidOperationException("Unknown Sichuan operation");
  }
  static MiniGameDiceUI ownedDice;
  static string ownedAccount="",ownedPlayer="",ownedInstance="";
  internal static bool Auto(MiniGameDiceUI ui){return Get<bool>(ui,"ὦὩὬὩὭὡὩὪὥὬὠ");}
  internal static void SetAuto(MiniGameDiceUI ui,bool enabled){if(Auto(ui)!=enabled)ui.OnClickUI(Get<Button>(ui,"_buttonAutoPlay").gameObject);}
  internal static void StartDice(MiniGameDiceUI ui,Command c){
   if(Auto(ui))throw new InvalidOperationException("Dice already running");
   ownedDice=ui;ownedAccount=c.AccountKey;ownedPlayer=c.PlayerKey;ownedInstance=c.Instance;SetAuto(ui,true);
  }
  internal static void StopDice(){if(ownedDice!=null&&Auto(ownedDice))SetAuto(ownedDice,false);ownedDice=null;}
  internal static void GuardDice(Frame f,string root){
   if(ownedDice==null)return;
   if(!Auto(ownedDice)){ownedDice=null;return;}
   bool allowed=false;
   try{using(var stream=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"dice-lease.json"))??new byte[0])){
    var lease=(DiceLease)new DataContractJsonSerializer(typeof(DiceLease)).ReadObject(stream);
    allowed=!(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"pause"))!=null)&&f.Instance==ownedInstance&&f.AccountKey==ownedAccount&&f.PlayerKey==ownedPlayer&&lease.Instance==f.Instance&&lease.AccountKey==f.AccountKey&&lease.PlayerKey==f.PlayerKey&&lease.ExpiresUtcTicks>DateTime.UtcNow.Ticks;
   }}catch(IOException){}catch(SerializationException){}
   if(!allowed)StopDice();
  }
 }
}

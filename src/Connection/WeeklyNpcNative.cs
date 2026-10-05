using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Google.Protobuf;
using Proto.Net;
using Proto.Design.common;
using UnityEngine;
using gamfs;
using Q=ὮὢὦὯὧὣὦὡὧὯὢ;
using Clock=ὫὨὪὠὢὨὮὤὩὤὮ;
namespace BD2Daily.Live {
 [DataContract] internal sealed class WeeklyNpcWild {
  [DataMember] public int Step,Map,Monster,Group,Count;
  [DataMember] public bool Available;
 }
 [DataContract] internal sealed class WeeklyNpcObject {
  [DataMember] public int Instance,Id,Quest;
  [DataMember] public bool Near,Ready,Available,Collected,Eligible;
  [DataMember] public string State="",Interaction="";
  [DataMember] public float Distance;
 }
 [DataContract] internal sealed class WeeklyNpcState {
  [DataMember] public string Query="",State="idle",Error="",Rows="[]",Active="[]";
  [DataMember] public int Pack,Map,Limit,Remaining,Completed,NavQuest,CarryId;
  [DataMember] public bool CanAccept,ActionsSupported,CacheRefreshDeferred;
  [DataMember] public string ServerActive="[]";
  [DataMember] public string CurrentName="";
  [DataMember] public int[] NavigationCollected=new int[0],NavigationStale=new int[0],NavigationRemoved=new int[0];
  [DataMember] public string NavigationRecovery="";
  [DataMember] public bool Navigating,BattleReady,BattlePlaying,BattleAuto;
  [DataMember] public string BattleResult="";
  [DataMember] public long Week,Now,At;
  [DataMember] public WeeklyNpcBoardTarget[] Boards=new WeeklyNpcBoardTarget[0];
  [DataMember] public WeeklyNpcBoardState Board=new WeeklyNpcBoardState();
  [DataMember] public int[] Posted=new int[0],Cleared=new int[0];
  [DataMember] public WeeklyNpcWild[] Wild=new WeeklyNpcWild[0];
  [DataMember] public WeeklyNpcObject[] Objects=new WeeklyNpcObject[0],CarryObjects=new WeeklyNpcObject[0];
 }
 internal static partial class WeeklyNpcNative {
  static WeeklyNpcState result=new WeeklyNpcState();static Command owner;static int navQuest;static long navUntil;
  static Dictionary<int,TodayQuestTable> tables;
  static Dictionary<int,TodayQuestTable> Tables {
   get { return tables??(tables=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetTableList<TodayQuestTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"TodayQuestTable").Where(x=>x.Type==2).ToDictionary(x=>x.Id)); }
  }
  static long Week {get{return Clock.ὠὥὬὪὮὬὢὨὬὮὫ.GetWeeklyResetTime().Ticks;}}
  static bool Matches(Command c){var f=Bridge.CurrentFrame;return f!=null&&c!=null&&f.ProcessId==c.ProcessId&&f.ProcessStartTicks==c.ProcessStartTicks&&f.Instance==c.Instance&&f.AccountKey==c.AccountKey&&f.PlayerKey==c.PlayerKey&&result.Week==Week;}
  static bool Active(int id){return Q.ὨὢὧὪὩὯὣὦὠὠὧ.Any(x=>x.Id==id);}
  internal static string Preflight(Command c){
   int pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ;
   if(c.Kind=="weekly_npc_query"){
    var map=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ;
    if(pack!=c.Value||map==null||map.PackId!=pack||ὣὡὧὡὦὣὣὬὨὪὫ.ὬὣὢὥὫὪὬὮὣὠὦ(pack)==null)return "npc_query_cartridge_changed";
    if(result.State=="pending"&&DateTime.UtcNow.Ticks-result.At<TimeSpan.FromSeconds(40).Ticks)return "npc_query_pending";
    return "";
   }
   return !CollectionProgressNative.IsStoryField(pack)||!result.ActionsSupported?"npc_actions_unsupported_cartridge":"";
  }
  internal static WeeklyNpcState Capture(){
   if(owner==null)return result;
   if(!Matches(owner))return new WeeklyNpcState{State="invalid",Error="NPC quest identity or weekly reset changed"};
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ;
   result.Map=field==null?0:field.Id;result.Board=CaptureBoard();
   result.CanAccept=result.ActionsSupported&&!Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὧὣὯὭὨὨὧὫὭὯ.Any(x=>ὨὧὠὡὨὬὣὦὩὣὢ.ὧὩὡὬὦὫὮὢὨὮὢ(x.Id).ὫὣὬὥὪὫὪὤὫὮὧ==0);
   var current=Q.ὨὢὧὪὩὯὣὦὠὠὧ.FirstOrDefault(x=>Tables.ContainsKey(x.Id)&&Tables[x.Id].PackId==result.Pack);
   result.CurrentName=current==null?"":ὨὧὠὡὨὬὣὦὩὣὢ.ὩὯὡὬὮὭὧὥὫὠὯ(ὨὧὠὡὨὬὣὦὩὣὢ.ὧὩὡὬὦὫὮὢὨὮὢ(current.Id));
   CaptureNavigationRecovery(current);
   result.Active="["+string.Join(",",Q.ὨὢὧὪὩὯὣὦὠὠὧ.Select(x=>JsonFormatter.Default.Format(Tables.ContainsKey(x.Id)&&Tables[x.Id].PackId==result.Pack?(Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetCurrentPackQuest(x.Id)??x):x)).ToArray())+"]";
   var activeIds=new HashSet<int>(Q.ὨὢὧὪὩὯὣὦὠὠὧ.Where(x=>Tables.ContainsKey(x.Id)&&Tables[x.Id].PackId==result.Pack).Select(x=>x.Id));
   result.Objects=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldQuestObjectController>()
    .Where(x=>x!=null&&activeIds.Contains(x.ὦὣὣὧὭὭὣὬὭὤὨ))
    .Select(x=>new WeeklyNpcObject{Instance=x.GetInstanceID(),Id=x.ὪὬὣὧὥὨὦὯὨὮὬ,Quest=x.ὦὣὣὧὭὭὣὬὭὤὨ,Near=x.ὩὤὨὮὥὦὫὭὫὭὨ,Ready=ObjectReady(x),Available=x.gameObject.activeInHierarchy,Collected=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.IsClearQuestObject(x.ὦὣὣὧὭὭὣὬὭὤὨ,x.ὪὬὣὧὥὨὦὯὨὮὬ),Eligible=x.ὨὧὨὦὦὢὦὢὫὥὪ,State=x.ὣὩὪὯὣὡὩὨὦὤὦ.ToString(),Interaction=x.ὨὪὨὩὥὠὡὮὮὠὬ.ToString(),Distance=(x.ὩὬὧὢὧὬὠὡὦὥὧ-GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ.transform.position).sqrMagnitude}).ToArray();
   var action=Singleton<FieldActionManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   result.CarryId=action.IsInActionWithFieldItem()?action.ὪὮὫὯὯὯὠὬὯὬὤ.ὯὫὪὡὭὤὪὮὫὬὣ:0;
   result.CarryObjects=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldActionObjectController>()
    .Where(x=>x!=null&&activeIds.Any(id=>OwnsCarry(x,Tables[id])))
    .Select(x=>new WeeklyNpcObject{Instance=x.GetInstanceID(),Id=x.ὪὬὣὧὥὨὦὯὨὮὬ,Quest=activeIds.Single(id=>OwnsCarry(x,Tables[id])),Near=x.ὩὤὨὮὥὦὫὭὫὭὨ,Ready=CarryReady(x),Available=x.gameObject.activeInHierarchy&&!action.CheckFieldItemBeforeInteract(x.ὪὬὣὧὥὨὦὯὨὮὬ),Distance=(x.ὩὬὧὢὧὬὠὡὦὥὧ-GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ.transform.position).sqrMagnitude}).ToArray();
   result.Navigating=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὤὬὪὨὠὢὢὭὧὫὨ;
   result.NavQuest=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὬὧὣὣὣὪὯὬὮὥ;
   var ui=Bridge.Find(typeof(BattleUI_FieldBattle)).Cast<BattleUI_FieldBattle>().SingleOrDefault();
   var battle=BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   result.BattleReady=ui!=null&&ui.ὣὤὮὪὠὧὤὮὫὡὠ&&!ui.ὯὦὪὣὡὪὭὤὪὤὨ;
   result.BattlePlaying=ui!=null&&ui.ὯὦὪὣὡὪὭὤὪὤὨ;result.BattleAuto=ui!=null&&battle.GetAutoBattleFlag();result.BattleResult=ui==null?"":battle.GetBattleResultType().ToString();
   return result;
  }
  static bool OwnsCarry(FieldActionObjectController source,TodayQuestTable q){
   // ObjectMove validates IsInActionWithFieldItem, not an item ID. Scene lift
   // props can belong to a main quest (e.g. 37) instead of the active weekly ID.
   return q.ConditionType==9&&source.ὡὪὬὬὤὠὩὥὦὯὧ.ὫὣὬὥὪὫὪὤὫὮὧ==1&&source.ὯὬὧὮὯὮὭὪὦὭὮ.ὤὪὭὧὠὪὫὨὫὧὩ==q.MapId;
  }
  static bool CarryReady(FieldActionObjectController target){
   var action=Singleton<FieldActionManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   return target.gameObject.activeInHierarchy&&target.ὩὤὨὮὥὦὫὭὫὭὨ&&target.ὡὪὬὬὤὠὩὥὦὯὧ.ὫὣὬὥὪὫὪὤὫὮὧ==1&&!action.IsInActionWithFieldItem()&&!action.CheckFieldItemBeforeInteract(target.ὪὬὣὧὥὨὦὯὨὮὬ);
  }
  static bool ObjectReady(FieldQuestObjectController target){
   return !Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.IsClearQuestObject(target.ὦὣὣὧὭὭὣὬὭὤὨ,target.ὪὬὣὧὥὨὦὯὨὮὬ)&&target.gameObject.activeInHierarchy&&target.ὨὧὨὦὦὢὦὢὫὥὪ&&target.ὩὤὨὮὥὦὫὭὫὭὨ&&target.ὣὩὪὯὣὡὩὨὦὤὦ==FieldQuestObjectController.ὢὠὣὠὡὭὥὣὨὦὦ.Idle&&target.ὨὪὨὩὥὠὡὮὮὠὬ.ToString()=="None";
  }
  static bool Accept(WeeklyNpcState next,Command c,int error){
   if(!ReferenceEquals(result,next))return false;
   if(error!=0||!Matches(c)||!Bridge.CollectionReadAllowed||Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ!=next.Pack){next.State="failed";next.Error="NPC quest query rejected or cartridge changed: "+error;return false;}return true;
  }
  internal static void Query(Command c){
   if(result.State=="pending"&&DateTime.UtcNow.Ticks-result.At<TimeSpan.FromSeconds(40).Ticks)throw new InvalidOperationException("NPC quest query already pending");
   int pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ;
   if(pack!=c.Value||GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ==null||ὣὡὧὡὦὣὣὬὨὪὫ.ὬὣὢὥὫὪὬὮὣὠὦ(pack)==null)throw new InvalidOperationException("NPC progress query requires the owned current field cartridge");
   // TodayQuestInfo is global, including while collecting character/event packs.
   // Only story cartridges expose NPC actions or require pack-specific hunt tables.
   var next=new WeeklyNpcState{Query=c.Id,State="pending",Pack=pack,ActionsSupported=CollectionProgressNative.IsStoryField(pack),Week=Week,At=DateTime.UtcNow.Ticks};result=next;owner=c;
   Clock.ὩὮὠὠὩὭὭὠὥὬὪ.Send(new TodayQuestInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ},(byte[] data,int packet,int error)=>{
    if(!Accept(next,c,error))return false;
    try{
     var response=TodayQuestInfoResponse.Parser.ParseFrom(data);
     // This response can omit partially collected objects. Applying it in that
     // case would erase QuestUpdate-confirmed progress from PackManager/Q.
     // Keep both observations for diagnostics; never merge or fabricate game state.
     next.ServerActive="["+string.Join(",",response.QuestInfo.Select(x=>JsonFormatter.Default.Format(x)).ToArray())+"]";
     var packManager=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
     next.CacheRefreshDeferred=response.QuestInfo.Any(reply=>{
      var live=packManager.GetCurrentPackQuest(reply.Id);
      return live!=null&&Tables.ContainsKey(live.Id)&&Tables[live.Id].PackId==pack&&WeeklyNpcProgressGuard.WouldRegress(live.Value,live.ObjectId,reply.Value,reply.ObjectId);
     });
     if(!next.CacheRefreshDeferred)Q.ὯὬὪὡὩὯὤὯὡὢὪ(response,packet,error);
     next.Limit=ὨὭὭὯὧὢὣὢὤὫὥ.ὨὣὭὩὫὪὢὯὣὠὥ;
     next.Posted=response.TodayQuestId.ToArray();next.Cleared=response.ClearQuestIds.ToArray();
     next.Completed=next.Cleared.Count(id=>Tables.ContainsKey(id)&&Tables[id].NextQuestId==0);
     next.Remaining=Math.Max(0,next.Limit-next.Completed-response.QuestInfo.Count);
     var included=new HashSet<int>(next.Cleared.Where(Tables.ContainsKey));
     foreach(int id in next.Posted.Concat(response.QuestInfo.Select(x=>x.Id)).Concat(next.Cleared)){
      int walk=id;var seen=new HashSet<int>();
      while(walk!=0&&Tables.ContainsKey(walk)&&seen.Add(walk)){included.Add(walk);walk=Tables[walk].NextQuestId;}
      walk=id;seen.Clear();while(walk!=0&&Tables.ContainsKey(walk)&&seen.Add(walk)){included.Add(walk);walk=Tables[walk].PriorQuestId;}
     }
     // Progress readers need offered/accepted chains, not every quest in the game.
     next.Rows="["+string.Join(",",included.Select(id=>JsonFormatter.Default.Format(Tables[id])).ToArray())+"]";
     if(!next.ActionsSupported){Finish(next);return true;}next.Boards=BoardTargets(pack);
     var raw=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
     var monsters=raw.GetTableList<Proto.Design.pack1.FieldMonsterTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_PACK,"FieldMonsterTable",pack).Where(x=>x.RegenId>0&&x.UseBattleSkip==1).ToArray();
     var decks=raw.GetTableList<Proto.Design.pack1.BattleDeckTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_PACK,"BattleDeckTable",pack).ToDictionary(x=>x.Id);
     var candidates=new List<WeeklyNpcWild>();
     foreach(var map in ὩὥὫὮὯὥὮὪὨὡὩ.ὫὣὪὯὭὦὩὬὫὥὭ().Where(x=>x.PackId==pack&&!ὧὧὧὪὦὠὭὣὫὡὤ.ὯὠὩὨὯὤὬὬὤὡὫ(x.Id))){
      var placed=new HashSet<int>(ὧὩὫὫὩὣὥὨὥὤὠ.ὪὯὤὭὢὭὪὮὬὭὢ(pack,map.MapScenePath,ὢὩὭὬὪὪὬὩὫὪὬ.Monster));
      foreach(var q in Tables.Values.Where(x=>x.PackId==pack&&x.ConditionType==1))foreach(var monster in monsters.Where(x=>placed.Contains(x.Id))){
       var counts=monster.BattleDeckId.Where(decks.ContainsKey).Select(id=>decks[id].CharId.Count(q.MagicValue.Contains)).ToArray();
       int count=counts.Length==monster.BattleDeckId.Count&&counts.Length>0?counts.Min():0;
       if(count>0)candidates.Add(new WeeklyNpcWild{Step=q.Id,Map=map.Id,Monster=monster.Id,Group=monster.RegenId,Count=count});
      }
     }
     next.Wild=candidates.ToArray();
     if(candidates.Count==0){Finish(next);return true;}
     var req=new MonsterInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ};req.GroupId.AddRange(candidates.Select(x=>x.Group).Distinct());
     Clock.ὩὮὠὠὩὭὭὠὥὬὪ.Send(req,(byte[] bytes,int code,int err)=>{
      if(!Accept(next,c,err))return false;
      try{
       var info=MonsterInfoResponse.Parser.ParseFrom(bytes).MonsterInfo.ToDictionary(x=>x.MonsterId);long now=Clock.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp();
       foreach(var target in next.Wild){MonsterDBInfo monster;if(!info.TryGetValue(target.Monster,out monster)||monster.GroupId!=target.Group)throw new InvalidOperationException("NPC target monster response incomplete");target.Available=monster.ActiveFlag&&monster.RespawnTime<=now&&(monster.LifeEndTime<=0||monster.LifeEndTime>now);}
       Finish(next);return true;
      }catch(Exception ex){next.State="failed";next.Error=ex.Message;return false;}
     });return true;
    }catch(Exception ex){next.State="failed";next.Error=ex.Message;return false;}
   });
  }
  static void Finish(WeeklyNpcState next){next.Now=Clock.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp();next.At=DateTime.UtcNow.Ticks;next.State="ready";}
  internal static void Dispatch(Command c,UIBase ui){
   if(c.Kind=="weekly_npc_query"){Query(c);return;}
   if(!result.ActionsSupported||!CollectionProgressNative.IsStoryField(Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ))throw new InvalidOperationException("NPC actions require an owned story cartridge");
   if(owner==null||!Matches(owner)||result.State!="ready")throw new InvalidOperationException("Fresh NPC quest observation required");
   if(BoardDispatch(c,ui))return;
   if(!Tables.ContainsKey(c.Value))throw new InvalidOperationException("Fresh NPC quest observation required");
   var q=Tables[c.Value];int pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὢὠὮὠὥὥὥὣὡὮὯ;
   if(pack!=q.PackId||pack!=result.Pack)throw new InvalidOperationException("NPC quest belongs to another cartridge");
   if(c.Kind=="weekly_npc_accept"){
    if(DateTime.UtcNow.Ticks-result.At>TimeSpan.FromSeconds(30).Ticks||!result.Posted.Contains(q.Id)||q.PriorQuestId!=0||Q.ὩὣὬὮὪὥὭὨὧὣὡ<1||Active(q.Id)||Q.ὥὫὣὧὩὧὦὮὤὧὠ(q.Id))throw new InvalidOperationException("NPC quest no longer offered or weekly quota exhausted");
    if(Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὧὣὯὭὨὨὧὫὭὯ.Any(x=>ὨὧὠὡὨὬὣὦὩὣὢ.ὧὩὡὬὦὫὮὢὨὮὢ(x.Id).ὫὣὬὥὪὫὪὤὫὮὧ==0)||Q.ὨὢὧὪὩὯὣὦὠὠὧ.Any(x=>Tables.ContainsKey(x.Id)&&Tables[x.Id].PackId==pack))throw new InvalidOperationException("Finish the current cartridge quest first");
    var seen=new HashSet<int>();var step=q;
    while(step!=null){
     if(!seen.Add(step.Id)||!new[]{1,2,9,18,19}.Contains(step.ConditionType))throw new InvalidOperationException("NPC quest chain not supported");
     if(step.ConditionType==1&&c.Items[0]==0&&result.Wild.Where(x=>x.Step==step.Id&&x.Available).GroupBy(x=>x.Monster).Sum(g=>g.First().Count)<step.ConditionCount)throw new InvalidOperationException("Hunting-only NPC quest is disabled");
     step=step.NextQuestId==0?null:Tables[step.NextQuestId];
    }
    AcceptOnBoard(c,ui);return;
   }
   if(c.Kind=="weekly_npc_talk"){
    if(navQuest!=q.Id||DateTime.UtcNow.Ticks>navUntil)throw new InvalidOperationException("No active NPC quest dialogue lease");
    var balloon=(BalloonScriptUI)ui;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
    var choice=(VisualNovelSelectChoiceUI)balloon.GetType().GetField("_selectUI",flags).GetValue(balloon);
    var npc=(NPCController)balloon.GetType().GetField("ὠὩὤὮὯὪὡὪὦὯὢ",flags).GetValue(balloon);
    int id=npc==null?0:npc.ὪὬὣὧὥὨὦὯὨὮὬ;
    if(choice!=null&&choice.gameObject.activeInHierarchy||id!=q.AcceptNpcId&&id!=q.CompleteNpcId&&!(q.ConditionType==19&&q.MagicValue.Count>0&&id==q.MagicValue[0]))throw new InvalidOperationException("NPC dialogue target or choice changed");
    var touch=(GameObject)balloon.GetType().GetField("_objTouchButton",flags).GetValue(balloon);if(touch.activeInHierarchy)balloon.OnClickUI(touch);return;
   }
   if(!Active(q.Id))throw new InvalidOperationException("NPC quest step no longer active");
   if(c.Kind=="weekly_npc_carry_nav"||c.Kind=="weekly_npc_lift"){
    if(q.ConditionType!=9||Singleton<FieldActionManager>.ὪὫὢὨὯὭὦὪὦὨὣ.IsInActionWithFieldItem())throw new InvalidOperationException("Not an empty-handed carry quest");
    var source=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldActionObjectController>().Single(x=>x!=null&&x.GetInstanceID()==c.Items[0]);
    if(!OwnsCarry(source,q)||!source.gameObject.activeInHierarchy)throw new InvalidOperationException("Carry source does not belong to active quest");
    if(c.Kind=="weekly_npc_lift"){
     if(navQuest!=q.Id||DateTime.UtcNow.Ticks>navUntil||!CarryReady(source))throw new InvalidOperationException("Carry source changed or is not reachable");
     source.InteractionFieldObject();return;
    }
    navQuest=q.Id;navUntil=DateTime.UtcNow.AddSeconds(180).Ticks;
    Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.StartFieldObjectNav(source.ὪὬὣὧὥὨὦὯὨὮὬ,ὢὩὭὬὪὪὬὩὫὪὬ.ActionObject,false);return;
   }
   if(c.Kind=="weekly_npc_interact"){
    if(navQuest!=q.Id||DateTime.UtcNow.Ticks>navUntil||!new[]{2,9,18}.Contains(q.ConditionType))throw new InvalidOperationException("No active NPC quest object lease");
    var target=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldQuestObjectController>().Single(x=>x!=null&&x.GetInstanceID()==c.Items[0]);
    if(target.ὦὣὣὧὭὭὣὬὭὤὨ!=q.Id||!ObjectReady(target))throw new InvalidOperationException("NPC quest object changed, busy or out of range");
    if(q.ConditionType==9){
     var action=Singleton<FieldActionManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
     if(!action.IsInActionWithFieldItem()||!GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὫὢὬὢὧὫὭὧὫὡὡ.OfType<FieldActionObjectController>().Any(x=>x!=null&&OwnsCarry(x,q)&&x.ὪὬὣὧὥὨὦὯὨὮὬ==action.ὪὮὫὯὯὯὠὬὯὬὤ.ὯὫὪὡὭὤὪὮὫὬὣ))throw new InvalidOperationException("Carry quest requires its own held object");
    }
    target.InteractionFieldObject();return;
   }
   if(c.Kind=="weekly_npc_auto"){
    var battle=BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ;var fieldUi=(BattleUI_FieldBattle)ui;
    if(q.ConditionType!=1||c.Items[0]!=1||navQuest!=q.Id||!fieldUi.ὣὤὮὪὠὧὤὮὫὡὠ||fieldUi.ὯὦὪὣὡὪὭὤὪὤὨ||!fieldUi.CanTouchUICondition()||battle.ὦὪὦὤὤὥὭὨὣὫὡ==null||!battle.ὦὪὦὤὤὥὭὨὣὫὡ.CharId.Any(q.MagicValue.Contains))throw new InvalidOperationException("Not the current NPC hunt target battle");
    navUntil=DateTime.UtcNow.AddMinutes(10).Ticks;fieldUi.DoAutoBattle(true);return;
   }
   if(c.Kind=="weekly_npc_nav"){
    if(q.ConditionType==1&&c.Items[0]==0)throw new InvalidOperationException("Hunting travel not allowed");
    RepairObjectNavigation(q);
    navQuest=q.Id;navUntil=DateTime.UtcNow.AddSeconds(180).Ticks;
    Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.StartQuestNav(q.Id);return;
   }
   throw new InvalidOperationException("Unsupported NPC quest action");
  }
  internal static void Stop(){
   if(navQuest==0)return;navQuest=0;navUntil=0;
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
   if(GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ!=null)GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);
  }
  internal static void Tick(){if(navQuest!=0&&(!Bridge.CollectionReadAllowed||!Matches(owner)||DateTime.UtcNow.Ticks>navUntil))Stop();}
 }
}

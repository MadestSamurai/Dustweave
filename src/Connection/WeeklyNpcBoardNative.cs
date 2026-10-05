using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.UI;
using gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class WeeklyNpcBoardTarget {
  [DataMember] public int Map,Npc,Instance;
  [DataMember] public bool Near;
 }
 [DataContract] internal sealed class WeeklyNpcBoardState {
  [DataMember] public int Ui,PopupQuest;
  [DataMember] public bool PopupCanAccept;
  [DataMember] public int[] VisibleQuests=new int[0];
 }
 internal static partial class WeeklyNpcNative {
  const BindingFlags BoardFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static T BoardField<T>(object value,string name){return (T)value.GetType().GetField(name,BoardFlags).GetValue(value);}
  internal static bool IsBoard(NPCController npc){return npc!=null&&npc.gameObject.activeInHierarchy&&npc.ὠὫὥὧὭὭὥὠὭὤὣ!=null&&npc.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὬὢὣὯὢὯὮὦὦὣ.Contains((int)ὪὪὫὣὤὯὡὧὥὥὮ.QuestBoard);}
  static QuestBoardUI CurrentBoard(){return Bridge.Find(typeof(QuestBoardUI)).Cast<QuestBoardUI>().SingleOrDefault(x=>x.gameObject.activeInHierarchy);}
  static QuestPopupUI CurrentQuestPopup(){return Bridge.Find(typeof(QuestPopupUI)).Cast<QuestPopupUI>().SingleOrDefault(x=>x.gameObject.activeInHierarchy);}
  static int PopupQuest(QuestPopupUI popup){return popup==null?0:BoardField<int>(popup,"ὤὦὥὫὯὧὥὤὦὩὠ");}
  static bool PopupAccept(QuestPopupUI popup){return popup!=null&&BoardField<QuestPopupUI.ὢὨὫὭὡὬὨὫὥὨὨ>(popup,"ὭὪὫὮὡὫὨὫὫὩὠ")==QuestPopupUI.ὢὨὫὭὡὬὨὫὥὨὨ.DefaultQuestAccept&&BoardField<GameObject>(popup,"_objReAcceptButton").activeInHierarchy;}
  internal static string BoardContext(UIBase ui){
   if(ui is QuestBoardUI)return "weekly_npc_board";
   var popup=ui as QuestPopupUI;
   return PopupAccept(popup)?"weekly_npc_accept:"+PopupQuest(popup):"";
  }
  static WeeklyNpcBoardTarget[] BoardTargets(int pack){
   return Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetTableList<Proto.Design.pack1.FieldNpcTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_PACK,"FieldNpcTable",pack)
    .Where(x=>x.InteractionList.Contains((int)ὪὪὫὣὤὯὡὧὥὥὮ.QuestBoard)).Select(x=>new WeeklyNpcBoardTarget{Map=x.MapId,Npc=x.Id}).ToArray();
  }
  static WeeklyNpcBoardState CaptureBoard(){
   foreach(var target in result.Boards){
    var npc=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.GetNPCController(target.Npc);
    bool here=target.Map==result.Map&&IsBoard(npc);
    target.Instance=here?npc.GetInstanceID():0;target.Near=here&&npc.ὩὤὨὮὥὦὫὭὫὭὨ;
   }
   var board=CurrentBoard();var popup=CurrentQuestPopup();
   return new WeeklyNpcBoardState{Ui=board==null?0:board.GetInstanceID(),PopupQuest=PopupQuest(popup),PopupCanAccept=PopupAccept(popup),VisibleQuests=board==null?new int[0]:board.GetComponentsInChildren<QuestBoardLoopScrollItem>(false).Where(x=>x.gameObject.activeInHierarchy).Select(x=>BoardField<int>(x,"ὠὥὠὠὣὡὨὪὭὤὩ")).ToArray()};
  }
  static bool BoardDispatch(Command c,UIBase ui){
   if(c.Kind=="weekly_npc_board_stop"){if(c.Value!=result.Pack)throw new InvalidOperationException("任务板卡带改变");Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);return true;}
   if(c.Kind=="weekly_npc_board_nav"||c.Kind=="weekly_npc_board_open"){
    if(c.Value!=result.Pack)throw new InvalidOperationException("任务板卡带改变");
    var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
    var ids=result.Boards.Where(x=>x.Map==field.ὮὬὬὮὠὮὪὠὧὩὪ.Id).Select(x=>x.Npc).ToArray();
    var npc=Bridge.Find(typeof(NPCController)).Cast<NPCController>().Where(x=>IsBoard(x)&&ids.Contains(x.ὪὬὣὧὥὨὦὯὨὮὬ)).OrderBy(x=>Vector3.Distance(x.transform.position,field.ὪὨὯὢὫὮὨὩὮὡὬ.transform.position)).FirstOrDefault();
    if(npc==null)throw new InvalidOperationException("当前地图没有可用公告板");
    if(c.Kind=="weekly_npc_board_nav"){var nav=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;nav.ClearQuestNav(true);if(!npc.ὩὤὨὮὥὦὫὭὫὭὨ)nav.StartFieldObjectNav(npc.ὪὬὣὧὥὨὦὯὨὮὬ,ὢὩὭὬὪὪὬὩὫὪὬ.NPC,false);return true;}
    if(!npc.ὩὤὨὮὥὦὫὭὫὭὨ)throw new InvalidOperationException("尚未走到公告板交互范围");
    Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);npc.InteractionFieldObject();return true;
   }
   if(c.Kind!="weekly_npc_board_scroll"&&c.Kind!="weekly_npc_board_select")return false;
   var board=ui as QuestBoardUI;
   if(board==null||!board.gameObject.activeInHierarchy||!Bridge.InputReady(board)||!IsBoard(BoardField<NPCController>(board,"ὪὧὭὦὬὢὮὮὯὦὬ")))throw new InvalidOperationException("请先打开当前地图的公告板");
   int index=board.ὧὮὠὠὧὭὥὥὥὭὨ.IndexOf(c.Value);
   if(index<0||!result.Posted.Contains(c.Value))throw new InvalidOperationException("任务已不在公告板列表中");
   if(c.Kind=="weekly_npc_board_scroll"){BoardField<LoopScrollRect>(board,"_loopScroll").ScrollToCell(index,0);return true;}
   var item=board.GetComponentsInChildren<QuestBoardLoopScrollItem>(false).SingleOrDefault(x=>x.gameObject.activeInHierarchy&&BoardField<int>(x,"ὠὥὠὠὣὡὨὪὭὤὩ")==c.Value);
   if(item==null)throw new InvalidOperationException("目标任务尚未滚动到可见范围");
   item.OnClickItem(item.gameObject);return true;
  }
  static void AcceptOnBoard(Command c,UIBase ui){
   var board=CurrentBoard();var popup=ui as QuestPopupUI;
   if(board==null||!IsBoard(BoardField<NPCController>(board,"ὪὧὭὦὬὢὮὮὯὦὬ"))||PopupQuest(popup)!=c.Value||!PopupAccept(popup)||!Bridge.InputReady(popup))throw new InvalidOperationException("任务确认页已改变，未接取任务");
   // Use the visible native confirmation handler, including its close/callback lifecycle.
   popup.OnClickUI(BoardField<GameObject>(popup,"_objReAcceptButton"));
  }
 }
}

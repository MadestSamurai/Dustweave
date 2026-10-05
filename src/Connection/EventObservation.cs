using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Google.Protobuf;
using Proto.Net;
namespace BD2Daily.Live {
 [DataContract] internal sealed class EventStageObservation {
  [DataMember] public int Id;
  [DataMember] public string Table,Deck,Progress;
 }
 [DataContract] internal sealed class EventLobbyObservation {
  [DataMember] public int Event,Group,Selected,Latest,FreeAp;
  [DataMember] public string Category;
  [DataMember] public bool Eligible;
  [DataMember] public EventStageObservation[] Stages;
  [DataMember] public string[] OwnDeck;
 }
 internal static class EventObservation {
  static string Json(IMessage m){return m==null?"null":JsonFormatter.Default.Format(m);}
  internal static EventLobbyObservation Lobby(){
   var ui=Bridge.Find(typeof(EventBattleUI)).Cast<EventBattleUI>().Single();
   int e=ui.ὤὮὯὫὪὡὤὡὫὬὪ,g=ui.ὢὣὤὬὮὩὡὬὥὫὩ;
   var rows=ui.ὥὣὤὧὥὬὤὧὣὤὠ.Select(id=>{
    var t=ὥὬὢὮὫὯὥὨὬὣὤ.ὡὥὡὭὯὫὥὥὮὮὥ(g,id);
    return new EventStageObservation{Id=id,Table=Json(t),Deck=Json(ὬὤὬὬὦὨὧὮὭὬὫ.ὩὥὯὭὠὡὨὥὪὬὨ(t.BattleDeckId,true)),Progress=Json(ὣὩὦὨὭὣὪὢὡὥὮ.ὥὨὯὭὬὡὢὢὩὢὤ(e,g,id))};
   }).ToArray();
   if(rows.Length==0||rows.Length>100)throw new InvalidOperationException("Event stage list not ready");
   return new EventLobbyObservation{Event=e,Group=g,Selected=ui.ὪὣὨὣὭὤὥὮὡὬὦ,Latest=ui.ὣὨὭὢὦὪὮὫὣὦὥ,Category=ui.ὫὥὣὮὧὦὠὯὭὥὬ.ToString(),FreeAp=ὣὡὧὡὦὣὣὬὨὪὫ.ὯὮὣὧὦὦὧὩὪὭὭ,Eligible=ὣὩὦὨὭὣὪὢὡὥὮ.ὯὫὧὢὡὦὫὠὬὫὣ(ui.ὫὥὣὮὧὦὠὯὭὥὬ,g),Stages=rows,OwnDeck=ὣὡὧὡὦὣὣὬὨὪὫ.ὫὬὯὪὥὪὨὢὭὤὢ.Select(x=>Json(x)).ToArray()};
  }
 }
}

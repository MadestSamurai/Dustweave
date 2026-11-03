using System;
using System.Linq;
using System.Runtime.Serialization;
using Proto.Net;

namespace BD2Daily.Live {
 [DataContract] internal sealed class EventPresetState {
  [DataMember] public long Revision;
  [DataMember] public int Slot,Units,ErrorCode;
  [DataMember] public string State="idle";
 }
 internal static class EventPresetNative {
  private static EventPresetState state=new EventPresetState();
  private static Command owner;
  internal static EventPresetState Capture(){return state;}
  private static bool SameActor(Command c){
   var f=Bridge.CurrentFrame;
   return f!=null&&f.ProcessId==c.ProcessId&&f.ProcessStartTicks==c.ProcessStartTicks&&
    f.AccountKey==c.AccountKey&&f.PlayerKey==c.PlayerKey;
  }
  private static PresetDBInfo Slot(int number){
   // Native preset slots are zero based; the settings show the game's one-based labels.
   return ὣὡὧὡὦὣὣὬὨὪὫ.ὮὬὦὯὤὫὧὡὨὫὣ.SingleOrDefault(p=>p.Slot==number-1);
  }
  private static int Units(PresetDBInfo p){return p==null?0:p.DeckInfo.Count(d=>d.DeckBaseInfo!=null&&d.DeckBaseInfo.CharInvenIndex!=0);}
  internal static void Execute(Command c){
   if(!SameActor(c)||!Bridge.DailyActionsActive)throw new InvalidOperationException("event_preset_session_changed");
   if(c.Kind=="event_preset_read"){
    var next=new EventPresetState{Revision=state.Revision+1,Slot=c.Value,State="reading"};state=next;owner=c;
    // The stock info entry supplies an IMessage callback and requires a native batch.
    // Use its byte-response equivalent here, then the same stock cache update handler.
    var request=new PresetInfoRequest{Seq=ὠὦὨὨὨὥὢὬὧὦὦ.ὬὪὭὤὥὭὢὮὧὯὤ};
    ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ.Send(request,(byte[] data,int code,int error)=>{
     if(!SameActor(c))return false;
     try{
      bool accepted=ὣὡὧὡὦὣὣὬὨὪὫ.ὢὠὬὮὭὢὬὣὧὪὧ(PresetInfoResponse.Parser.ParseFrom(data),code,error);
      if(state!=next||!SameActor(c))return accepted;
      next.ErrorCode=error;next.Units=accepted?Units(Slot(c.Value)):0;
      next.State=!accepted?"failed":next.Units==0?"empty":"ready";return accepted;
     }catch(Exception){if(state==next){next.ErrorCode=error;next.State="failed";}return false;}
    });return;
   }
   if(owner==null||!SameActor(owner)||state.Revision!=c.Items[0]||state.Slot!=c.Value||state.State!="ready")
    throw new InvalidOperationException("event_preset_read_again");
   var applying=state;applying.Units=Units(Slot(c.Value));
   // A slot cleared after the read must never clear the current team.
   if(applying.Units==0){applying.State="empty";return;}
   applying.State="applying";
   // Always apply, including when the native selected-slot marker already matches.
   ὣὡὧὡὦὣὣὬὨὪὫ.ὧὬὭὩὣὮὪὮὫὯὫ(c.Value-1,()=>{
    if(state==applying&&SameActor(c))applying.State="applied";
   });
  }
 }
}

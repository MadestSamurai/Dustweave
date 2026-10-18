namespace BD2ApostleDefense
{
 // Native manager flags are cleared while the result UI can still be present.
 // Preserve observed terminal evidence, never infer defeat from opening the result menu.
 public sealed class SettlementTracker
 {
  private string account="",room="";private int pid;private long start;private bool pending,roomEnded;
  public void Observe(Snapshot s)
  {
   bool boundary=s.Account!=account||s.ProcessId!=pid||s.ProcessStart!=start||
    s.Stage=="login"||s.Stage=="lobby"||s.Stage=="matching"||s.Stage=="match-failed"||
    (s.Room.Length>0&&room.Length>0&&s.Room!=room)||
    (s.Stage=="playing"&&!s.Win&&!s.Dead&&!s.RoomEnded);
   if(boundary){pending=false;roomEnded=false;room="";}
   account=s.Account;pid=s.ProcessId;start=s.ProcessStart;
   bool inRound=s.Stage=="playing"||s.Stage=="ended"||s.Stage=="waiting-round"||s.Stage=="result"||s.Stage=="confirm-exit"||s.Stage=="settling";
   if(inRound&&(s.Win||s.Dead||s.RoomEnded)){pending=true;roomEnded|=s.RoomEnded;}
   if(s.Room.Length>0)room=s.Room;
   s.SettlementPending=pending;s.SettlementRoomEnded=roomEnded;
  }
 }
}

namespace BD2ApostleDefense
{
 public static class GameFlow
 {
  public static bool RoundFinished(Snapshot s){return s.Win||s.Dead||s.RoomEnded||s.SettlementPending;}
  public static bool BoardAction(string kind){return kind=="summon"||kind=="sell"||kind=="move"||kind=="upgrade";}
  // GameEnd deliberately closes the battle TCP. Only unfinished rooms need its recovery.
  public static bool NeedsBattleConnection(Snapshot s){return !s.RoomEnded&&!s.SettlementRoomEnded&&s.Room.Length>0&&
   (s.Stage=="playing"||s.Stage=="waiting-round"||s.Stage=="ended"||s.Stage=="result"||s.Stage=="confirm-exit"||s.Stage=="settling");}
  public static bool NetworkBlocked(Snapshot s){return s.NetworkHold&&NeedsBattleConnection(s);}
  public static bool SettlementAction(string kind){return kind=="open-result"||kind=="settle"||kind=="confirm-exit";}
  public static bool CanSettle(Snapshot s){return RoundFinished(s)&&!s.Exiting;}
  // Native death clears my units and changes the watched player. Their old receipts can no longer arrive.
  public static bool Superseded(string kind,Snapshot before,Snapshot after)
  {
   if(before.Account!=after.Account)return false;
   if(BoardAction(kind))return RoundFinished(after)||after.Exiting||after.Stage=="lobby"||after.Stage=="match-failed"||
    (after.Room.Length>0&&after.Room!=before.Room);
   if(SettlementAction(kind)&&after.Room.Length>0&&after.Room!=before.Room)return true;
   return kind=="match-start"&&(after.Stage=="lobby"||after.Stage=="match-failed");
  }
  public static bool Receipt(string kind,Snapshot before,Snapshot after)
  {
   // A popup can prevent the NEXT click; it cannot undo a completed scene transition.
   if(before.Account!=after.Account)return false;
   bool lobby=after.Stage=="lobby"&&!after.Exiting;
   bool sameRoom=before.Room==after.Room||(after.Room.Length==0&&after.SettlementPending);
   switch(kind)
   {
    case "start":return after.Stage=="matching"||after.Stage=="match-failed"||after.Stage=="waiting-round"||after.Stage=="playing";
    case "retry":return lobby||after.Stage=="matching";
    case "match-start":return after.Stage=="playing"||after.Stage=="waiting-round"||(sameRoom&&RoundFinished(after));
    case "open-result":return lobby||(sameRoom&&(after.Exiting||(CanSettle(after)&&(after.Stage=="result"||after.Stage=="confirm-exit"))));
    case "settle":return lobby||(sameRoom&&CanSettle(after)&&after.Stage=="confirm-exit");
    case "confirm-exit":return lobby;
    case "close-event":return !after.EventPopupOpen;
    default:return false;
   }
  }
  public static int TimeoutSeconds(string kind){return kind=="move"?2:kind=="settle"||kind=="confirm-exit"?30:15;}
 }
}

using System;
using System.Linq;
namespace BD2ApostleDefense
{
 // Only an explicit, still-owned opening decision may abandon a live match.
 // Native result/confirmation screens alone never imply permission to give up.
 public sealed class OpeningRestart
 {
  private Control intent;private long expires;
  public static string Reject(Decision a,Snapshot s)
  {
   if(a.Policy!="clear50-opening"||s.Exiting)return "重开条件已变化，按当前界面继续";
   if(s.RestartPending&&(s.Stage=="playing"||s.Stage=="result"||s.Stage=="confirm-exit"))return "";
   if(s.Stage!="playing"||!s.Ready||!s.MyView||GameFlow.RoundFinished(s)||s.Wave<=10||a.Wave!=s.Wave||s.Room.Length==0)
    return "只在已观察的开局结束后判断重新召唤";
   if(!s.Catalog.Units.Any(d=>d.Grade>=7&&d.Weight>0)||s.Units.Any(u=>!u.Ready||!s.Catalog.Units.Any(d=>d.Id==u.Id)||s.Catalog.Units.Any(d=>d.Id==u.Id&&d.Grade>=7)))
    return "核心或召唤状态已变化，保留当前对局";
   return "";
  }
  public void Begin(Control c,long now){intent=new Control{Owner=c.Owner,Account=c.Account,ProcessId=c.ProcessId,ProcessStart=c.ProcessStart,Room=c.Room};expires=now+TimeSpan.FromMinutes(2).Ticks;}
  public void Clear(){intent=null;expires=0;}
  public void Observe(Snapshot s,Control c,long now)
  {
   if(intent!=null&&(c==null||!c.Enabled||c.Expires<now||c.Owner!=intent.Owner||c.Account!=intent.Account||
    s.Account!=intent.Account||s.ProcessId!=intent.ProcessId||s.ProcessStart!=intent.ProcessStart||now>expires||
    s.Stage=="login"||s.Stage=="lobby"||s.Stage=="matching"||s.Stage=="match-failed"||
    (s.Room.Length>0&&s.Room!=intent.Room)))Clear();
   s.RestartPending=intent!=null;
  }
 }
}

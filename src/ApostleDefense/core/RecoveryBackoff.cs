namespace BD2ApostleDefense;

// Retain automation intent without repeatedly clicking an unresponsive action. A failed move
// cools that unit (including reverse swaps), leaving summons and other units available.
public sealed class RecoveryBackoff
{
 private readonly Dictionary<string,(int Count,long Until)> failures=new();
 private string session="";
 private static string Key(Decision d)=>d.Kind=="move"?"move:"+d.UnitIndex:d.Kind;
 public void Reset(){failures.Clear();session="";}
 public void Sync(Snapshot s)
 {
  string next=$"{s.Account}|{s.ProcessId}|{s.ProcessStart}|{s.Room}";
  if(next==session)return;failures.Clear();session=next;
 }
 public void Record(Decision d,bool success,long now)
 {
  string key=Key(d);if(success){failures.Remove(key);return;}
  int count=failures.TryGetValue(key,out var old)?Math.Min(5,old.Count+1):1;
  failures[key]=(count,now+TimeSpan.FromSeconds(Math.Min(30,1<<count)).Ticks);
 }
 public bool CanMove(int index,long now)=>Ready("move:"+index,now);
 public bool Ready(Decision d,long now)=>Ready(Key(d),now);
 private bool Ready(string key,long now)=>!failures.TryGetValue(key,out var value)||now>=value.Until;
}

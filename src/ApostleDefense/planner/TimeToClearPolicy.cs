namespace BD2ApostleDefense;

// Score each early-wave checkpoint once; all continuing actions stay with AnnealedPolicy.
public sealed class TimeToClearPolicy
{
 private readonly TimeToClearModel model=new();
 private readonly HashSet<int> assessed=new();
 private string session="";
 private int lastWave;
 public void Reset(){session="";lastWave=0;assessed.Clear();}
 public Decision? Decide(Snapshot s,Settings settings,long now,bool awaitingRefill=false)
 {
  string key=$"{s.ProcessId}|{s.ProcessStart}|{s.Account}|{s.Room}";
  if(key!=session||s.Wave<lastWave){Reset();session=key;}lastWave=s.Wave;
  if(!settings.Clear50||!settings.AutoNext||settings.StopAfterRound||awaitingRefill||
   s.AchievementsKnown&&s.ClearProgress>=s.ClearTarget||s.Stage!="playing"||!s.Ready||!s.MyView||
   s.Room.Length==0||s.Exiting||s.Blocker.Length>0||GameFlow.NetworkBlocked(s)||GameFlow.RoundFinished(s)||
   s.At<now-TimeSpan.FromSeconds(1).Ticks||s.At>now+TimeSpan.FromSeconds(1).Ticks||
   !TimeToClearModel.AtCheckpoint(s)||assessed.Contains(s.Wave))return null;
  if(!model.TryScore(s,out double score,out double threshold))return null;
  assessed.Add(s.Wave);
  return score<threshold?new(){Kind="restart",Policy=TimeToClearModel.Policy,Wave=s.Wave,Score=score,
   Reason="退火策略：本局继续收益偏低，重新匹配以缩短累计通关时间"}:null;
 }
}

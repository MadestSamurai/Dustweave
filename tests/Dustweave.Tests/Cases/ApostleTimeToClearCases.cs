extern alias apostle;
using N = apostle::BD2ApostleDefense;

internal static class ApostleTimeToClearCases
{
 public static void Run(List<string> cases)
 {
  void Check(bool value,string text){if(!value)throw new Exception(text);cases.Add("apostle stage restart: "+text);}
  long now=DateTime.UtcNow.Ticks;
  N.Snapshot Scene()=>new(){At=now,ProcessId=10,ProcessStart=20,Account=new('a',64),Room="synthetic",Stage="playing",Ready=true,MyView=true,Wave=5,SecondsLeft=29,FrameSeconds=1.0/60,
   Boards=[new(){Id=19}],Path=[new(){X=-1,Z=-1},new(){X=1,Z=-1},new(){X=1,Z=1},new(){X=-1,Z=1}],
   Units=[new(){Index=7,Id=1,Grid=19,Ready=true}],Catalog=new(){Key="synthetic",SummonCost=25,MaxGrade=8,MaxUpgrade=99,GameOverCount=100,Advantage=1,Penalty=.5,
    Units=[new(){Id=1,Grade=1,Element=4,Attack=1,Interval=1,Range=3,Weight=1}],
    Waves=Enumerable.Range(1,50).Select(w=>new N.WaveDef{Id=w,Hp=100000,Count=w%10==0?1:60,Duration=30,Speed=1,SpawnInterval=.5,Boss=w%10==0,Element=3}).ToArray()}};
  var settings=new N.Settings{Clear50=true,Rare=false};var s=Scene();var policy=new N.TimeToClearPolicy();
  var d=policy.Decide(s,settings,now);
  Check(d is {Kind:"restart",Policy:N.TimeToClearModel.Policy},"weak checkpoint requests a native restart");
  Check(policy.Decide(s,settings,now)==null,"same checkpoint is assessed once");
  s.Room="next";Check(policy.Decide(s,settings,now)?.Kind=="restart","new room resets assessment");
  Check(new N.Planner().Decide(s,settings,now).Kind=="restart","production planner routes through the stage rule");
  Check(N.OpeningRestart.Reject(d!,s)=="","native guard independently accepts current weak board");
  foreach(int fps in new[]{24,30,60,120,144}){s.FrameSeconds=1.0/fps;Check(new N.TimeToClearPolicy().Decide(s,settings,now)?.Kind=="restart",$"no FPS eligibility switch at {fps}");}
  s=Scene();s.FrameSeconds=0;Check(new N.TimeToClearPolicy().Decide(s,settings,now)!=null,"missing frame sample uses reference cadence");
  foreach(var cfg in new[]{new N.Settings{Clear50=false,Rare=true},new N.Settings{AutoNext=false},new N.Settings{StopAfterRound=true}})
   Check(new N.TimeToClearPolicy().Decide(s,cfg,now)==null,"goal and continuation settings prohibit abandonment");
  Check(new N.TimeToClearPolicy().Decide(s,settings,now,true)==null,"sell replacement obligation takes priority");
  s.AchievementsKnown=true;s.ClearProgress=1;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"completed clear goal is not restarted");s=Scene();
  s.SecondsLeft=30;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"wait for checkpoint maturity");
  s.SecondsLeft=20;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"late attachment does not score an out-of-distribution board");s=Scene();
  s.Wave=50;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"final wave is never deliberately abandoned");s=Scene();
  s.At=now-TimeSpan.FromSeconds(2).Ticks;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"stale observation cannot trigger exit");s=Scene();
  s.NetworkHold=true;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"connection recovery cannot trigger exit");s=Scene();
  s.Units[0].Ready=false;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"incomplete spawn is not scored");s=Scene();
  s.EnemyCount=1;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"partial enemy snapshot is not scored");s=Scene();
  s.Units[0].Grid=999;Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"missing grid is rejected without throwing");s=Scene();
  s.Gold=100000;s.Catalog.Units[0].Attack=100000;s.Catalog.Units[0].Interval=.01;
  Check(new N.TimeToClearPolicy().Decide(s,settings,now)==null,"strong board continues despite low rarity");
  Check(N.OpeningRestart.Reject(d!,s)!="","fresh improved board rejects an earlier restart decision");s=Scene();
  var tracker=new N.OpeningRestart();var c=new N.Control{Enabled=true,Expires=now+TimeSpan.FromSeconds(10).Ticks,Owner=new('b',32),Account=s.Account,ProcessId=s.ProcessId,ProcessStart=s.ProcessStart,Room=s.Room,Action=d!};
  tracker.Begin(c,now);s.Stage="confirm-exit";tracker.Observe(s,c,now);
  Check(s.RestartPending&&s.RestartPolicy==N.TimeToClearModel.Policy,"native confirmation retains the owned stage intent");
  Check(new N.Planner().Decide(s,settings,now) is {Kind:"restart",Policy:N.TimeToClearModel.Policy},"confirmation uses original strategy intent");
  Check(N.OpeningRestart.Reject(d!,s)=="","confirmation does not re-score a vanished board");
  Check(new N.Planner().Decide(s,new(){AutoNext=false},now).Kind=="wait","disabling continuation stops confirmation clicks");
  c.Owner=new('c',32);tracker.Observe(s,c,now);Check(!s.RestartPending,"ownership change cancels pending exit");
  Check(N.OpeningRestart.Reject(d!,s)!="","unowned confirmation is never accepted");
  s=Scene();tracker.Begin(c,now);s.Stage="lobby";tracker.Observe(s,c,now);Check(!s.RestartPending,"return to lobby clears intent");
  var model=new N.TimeToClearModel();s=Scene();Check(model.TryScore(s,out var before,out _),"synthetic score is available");
  s.Boards[0].X=100;Check(model.TryScore(s,out var after,out _)&&before!=after,"geometry cache invalidates when board geometry changes");
 }
}

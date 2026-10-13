extern alias apostle;
using N = apostle::BD2ApostleDefense;

internal static class ApostleAnnealingCases
{
 public static void Run(List<string> cases)
 {
  void Check(bool value,string text){if(!value)throw new Exception(text);cases.Add("apostle annealing: "+text);}
  long now=DateTime.UtcNow.Ticks;
  N.Snapshot Scene()=>new(){At=now,ProcessId=10,ProcessStart=20,Account=new('a',64),Room="synthetic",Stage="playing",Ready=true,MyView=true,Wave=1,SecondsLeft=30,Gold=0,CanSummon=false,
   CanUpgrade=Enumerable.Repeat(true,5).ToArray(),UpgradeCosts=[10,10,10,10,10],
   Boards=[new(){Id=12,X=100},new(){Id=37},new(){Id=91,X=100}],
   Path=[new(){Id=10,X=-1,Z=-1},new(){Id=20,X=1,Z=-1},new(){Id=30,X=1,Z=1},new(){Id=40,X=-1,Z=1}],
   Units=[new(){Index=7,Id=1,Grid=12,Ready=true}],
   Catalog=new(){Key="synthetic",SummonCost=25,GameOverCount=100,MaxGrade=8,MaxUpgrade=99,Advantage=1,Penalty=.5,
    Units=[new(){Id=1,Grade=5,Element=4,Attack=100,UpAttack=100,Interval=.2,Range=3,Sell=8,Weight=100},new(){Id=2,Grade=1,Element=0,Attack=1,Interval=1,Range=200,Sell=8,Weight=100}],
    Waves=[new(){Id=1,Hp=10000,Count=60,Duration=30,Speed=1,SpawnInterval=.5},new(){Id=11,Hp=10000,Count=60,Duration=30,Speed=1,SpawnInterval=.5},new(){Id=50,Hp=100000,Count=1,Duration=60,Speed=1,Boss=true,EnemyTable=90}]}};
  var settings=new N.Settings{Rare=false};var s=Scene();var p=new N.Planner();
  var move=p.Decide(s,settings,now);
  Check(move is {Kind:"move",UnitIndex:7,TargetGrid:37,Policy:N.AnnealedPolicy.Policy},"sparse grid identifiers map correctly and free placement survives a gold wait");
  Check(p.Focus.Contains("离线退火"),"active clear goal names offline annealing");
  Check(new N.Planner().Decide(s,settings,now,_=>false).Kind=="wait","failed-move cooldown blocks placement without spending");
  s.Blocker="UnknownPopupUI";Check(p.Decide(s,settings,now).Kind=="wait","unknown popup guard remains ahead of strategy");s.Blocker="";
  s.MyView=false;Check(p.Decide(s,settings,now).Kind=="wait","opponent board never moves");s.MyView=true;
  s.Units[0].Grid=37;s.Gold=20;s.Levels[4]=12;
  Check(p.Decide(s,settings,now) is {Kind:"upgrade",Element:4},"ordinary tier-five army can develop beyond the old early upgrade ceiling");
  s.Wave=11;Check(p.Decide(s,settings,now).Kind!="restart","no rarity-only restart after wave ten");
  s.Units[0].Ready=false;Check(p.Decide(s,settings,now).Kind=="wait","pending unit models block new decisions");
  s=Scene();s.Boards=s.Boards.Take(2).ToArray();s.Units=[s.Units[0],new(){Index=18,Id=2,Grid=37,Ready=true}];
  move=p.Decide(s,settings,now);Check(move is {Kind:"move",TargetIndex:18},"full-board exchange includes displaced unit output");
  Check(new N.Planner().Decide(s,settings,now,i=>i!=18).Kind=="wait","exchange honors both units' recovery cooldowns");
  p.Confirm(move,s,now);Check(p.Decide(s,settings,now+1).Kind=="wait","confirmed swaps cool down both participants");
  s.Room="next-room";Check(p.Decide(s,settings,now+1).Kind=="move","new room clears prior movement history");
  s=Scene();s.Wave=50;s.EnemyCount=1;s.Enemies=[new(){Id=99,Table=90,Hp=10000,Speed=1,X=-1,Z=-1,NextPoint=20}];
  move=new N.Planner().Decide(s,settings,now);
  Check(move is {Kind:"move",Policy:N.AnnealedPolicy.ChasePolicy,TargetEnemy:99},"boss chase works without high-tier core");
  Check(new N.Planner().Decide(s,settings,now+TimeSpan.FromSeconds(1).Ticks).Kind=="wait","stale boss observation cannot produce a chase");
  var control=new N.Control{Enabled=true,Owner=new('b',32),Account=s.Account,ProcessId=s.ProcessId,ProcessStart=s.ProcessStart,Room=s.Room,BoardKey=s.BoardKey,Expires=now+TimeSpan.FromSeconds(10).Ticks,SnapshotAt=now,Action=move};
  Check(N.Guards.Reject(control,s,now)=="","native guard accepts a fresh identified boss chase");
  Check(N.Guards.Reject(control,s,now+TimeSpan.FromSeconds(1).Ticks)!="","native guard rejects delayed boss chase");
  s.Enemies=[];Check(N.Guards.Reject(control,s,now)!="","native guard rejects disappeared chase target");
  s=Scene();s.Gold=25;s.CanSummon=false;s.UpgradeCosts=[1,1,1,1,1];
  var policy=new N.AnnealedPolicy();policy.Confirm(new(){Kind="sell",Policy=N.AnnealedPolicy.Policy},s,now);
  move=policy.Decide(s,now);Check(move.Kind=="move","pending replacement still permits useful free placement");s.Units[0].Grid=move.TargetGrid;
  Check(policy.Decide(s,now).Kind=="wait","replacement reserve cannot be consumed by cheap upgrade");
  s.CanSummon=true;Check(policy.Decide(s,now).Kind=="summon","replacement resumes as soon as native summon is ready");
  s.Room="replacement-reset";Check(policy.Decide(s,now).Kind!="wait","replacement obligation never leaks into next room");
  s=Scene();s.AchievementsKnown=true;s.ClearProgress=1;s.ClearTarget=1;s.RareProgress=0;
  var rare=new N.Planner();rare.Decide(s,new(){Clear50=true,Rare=true},now);
  Check(!rare.Focus.Contains("退火"),"completed clear goal returns to existing rare-summon objective");
  s=Scene();s.Stage="result";s.Win=true;s.RoomEnded=true;
  Check(new N.Planner().Decide(s,settings,now).Kind=="settle","result submission retains existing lifecycle");
  s=Scene();s.Levels=[];Check(new N.Planner().Decide(s,settings,now).Kind=="wait","partial attribute snapshot waits instead of throwing");
  s=Scene();s.Units=[s.Units[0],new(){Index=8,Id=1,Grid=12,Ready=true}];Check(new N.Planner().Decide(s,settings,now).Kind=="wait","duplicate occupied cell waits for consistent snapshot");
  Check(Enumerable.Range(0,4).Select(N.AnnealedPolicy.PopulationLimit).SequenceEqual(new[]{49,37,60,48}),"frozen annealing populations match selected policy");
  Check(Enumerable.Range(0,4).Select(N.AnnealedPolicy.UpgradeLimit).SequenceEqual(new[]{99,47,99,77}),"frozen ordinary upgrade limits match selected policy");
 }
}

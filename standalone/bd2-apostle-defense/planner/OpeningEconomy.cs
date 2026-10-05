namespace BD2ApostleDefense;

public sealed partial class Planner
{
 // A wave's useful output changes at kill breakpoints: doubling an already
 // lethal single hit does not create another attack or another income source.
 public double ClearRate(UnitDef u,Board b,int level,WaveDef w,Snapshot s)
 {
  double hit=(u.Attack+u.UpAttack*(double)level)*ElementMultiplier(u.Element,w.Element,s.Catalog);
  if(hit<=0||w.Hp<=0)return 0;
  double attacks=Math.Max(1,Math.Ceiling(w.Hp/hit));
  double targets=u.SplashRange>0?Math.Min(Math.Max(1,w.Count),1+2*u.SplashRange/Math.Max(.25,w.Speed*w.SpawnInterval)):1;
  return Coverage(u,b,s.Path)*Math.Min(targets,8)/(attacks*Math.Max(.1,u.Interval+1.0/60));
 }

 private double OpeningUtility(UnitDef u,Board b,Snapshot s,int extra)
 {
  var current=Current(s);
  var next=s.Catalog.Waves.Where(w=>w.Id>current.Id).OrderBy(w=>w.Id).FirstOrDefault()??current;
  int level=s.Levels[u.Element]+extra;
  // Opening money must keep the current wave productive. The adjacent wave is
  // a modest tie-breaker, not the distant 10/50-wave boss's uncapped damage.
  return .8*ClearRate(u,b,level,current,s)+.2*ClearRate(u,b,level,next,s);
 }

 private bool OpeningUpgradeAllowed(int element,Snapshot s,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards)
 {
  var wave=Current(s);double before=0,after=0;
  foreach(var u in s.Units)
  {
   var d=defs[u.Id];int level=s.Levels[d.Element];
   before+=ClearRate(d,boards[u.Grid],level,wave,s);
   after+=ClearRate(d,boards[u.Grid],level+(d.Element==element?1:0),wave,s);
  }
  if(after<=before+1e-9)return false;
  if(s.Gold>=s.Catalog.SummonCost)return true;
  // When saving toward a summon, upgrading is allowed only if the extra kills
  // repay it quickly enough to reach that same summon sooner. Counts are rough
  // route-based estimates, not simulated win rates or guaranteed income.
  double spawnRate=wave.Count/Math.Max(1,wave.Duration);
  if(s.EnemyCount>0)spawnRate+=s.EnemyCount/Math.Max(1,s.SecondsLeft);
  double incomeBefore=Math.Min(before,spawnRate)*wave.Gold;
  double incomeAfter=Math.Min(after,spawnRate)*wave.Gold;
  if(incomeAfter<=0)return false;
  double saveTime=incomeBefore<=0?double.PositiveInfinity:(s.Catalog.SummonCost-s.Gold)/incomeBefore;
  double investTime=(s.Catalog.SummonCost-s.Gold+s.UpgradeCosts[element])/incomeAfter;
  double horizon=s.SecondsLeft>0?s.SecondsLeft:wave.Duration;
  return investTime<saveTime*.9&&investTime<=horizon;
 }
}

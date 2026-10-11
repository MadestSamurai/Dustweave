namespace BD2ApostleDefense;

public sealed partial class Planner
{
 private string strategyRoom="";
 private bool observedOpening;
 private int selectedCore=-1;
 private int selectedSupport=-1,recruitWave=-1,recruitsInWave;
 private bool ClearStrategy(Snapshot s,Settings settings)=>settings.Clear50&&(!s.AchievementsKnown||s.ClearProgress<s.ClearTarget)
  &&s.Catalog.Units.Any(d=>d.Grade>=7&&d.Weight>0);
 private void ObserveOpening(Snapshot s)
 {
  if(strategyRoom!=s.Room){strategyRoom=s.Room;observedOpening=false;selectedCore=-1;selectedSupport=-1;recruitWave=-1;recruitsInWave=0;}
  if(s.Stage=="playing"&&s.Wave<=10)observedOpening=true;
 }
 private Unit? Core(Snapshot s,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards)
 {
  var high=s.Units.Where(u=>u.Ready&&defs[u.Id].Grade>=7).ToArray();
  if(high.Length==0){selectedCore=-1;return null;}
  // Keep a stable core until a higher tier arrives. Do not change investment every wave.
  var current=high.FirstOrDefault(u=>u.Index==selectedCore);
  int bestGrade=high.Max(u=>defs[u.Id].Grade);
  if(current!=null&&defs[current.Id].Grade==bestGrade)return current;
  var core=high.OrderByDescending(u=>defs[u.Id].Grade).ThenByDescending(u=>defs[u.Id].Element is 0 or 4)
   .ThenByDescending(u=>s.Boards.Max(b=>Utility(defs[u.Id],b,s))).First();selectedCore=core.Index;return core;
 }
 private HashSet<int> CoreElements(Snapshot s,Dictionary<int,UnitDef> defs,Unit? core)
 {
  var result=new HashSet<int>{0,4};
  if(core!=null&&defs[core.Id].Element is not (0 or 4))result.Add(defs[core.Id].Element);
  else
  {
   // The third element need not be another rare core. A tier-5/6 counter is a
   // useful complement, particularly against a boss that resists the main core.
   var candidates=s.Units.Where(u=>u.Ready&&defs[u.Id].Grade>=5&&defs[u.Id].Element is not (0 or 4)).ToArray();
   var flexible=candidates.FirstOrDefault(u=>u.Index==selectedSupport);
   int grade=candidates.Select(u=>defs[u.Id].Grade).DefaultIfEmpty(0).Max();
   if(flexible==null||defs[flexible.Id].Grade<grade)
    flexible=candidates.OrderByDescending(u=>defs[u.Id].Grade).ThenByDescending(u=>s.Boards.Max(b=>Utility(defs[u.Id],b,s))).FirstOrDefault();
   selectedSupport=flexible?.Index??-1;
   if(flexible!=null)result.Add(defs[flexible.Id].Element);
  }
  return result;
 }
 private void ConfirmRecruit(Decision d)
 {
  if(d.Policy!="clear50-recruit"||d.Kind is not ("summon" or "sell"))return;
  if(recruitWave!=d.Wave){recruitWave=d.Wave;recruitsInWave=0;}
  recruitsInWave++;
 }
 private Decision? RecruitComplement(Snapshot s,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards,Unit core)
 {
  // A lone core plus low tiers cannot always solve the late elemental checks.
  // Reserve at most two rolls per safe non-boss wave, rather than saving for
  // upgrades forever. Affordable planned upgrades still have higher priority.
  if(Current(s).Boss||s.Wave>35||s.Levels[defs[core.Id].Element]<3||s.EnemyCount>=s.Catalog.GameOverCount*.35)return null;
  if(new[]{0,4}.All(e=>s.Units.Any(u=>u.Ready&&defs[u.Id].Element==e&&defs[u.Id].Grade>=5)))return null;
  if(recruitWave!=s.Wave){recruitWave=s.Wave;recruitsInWave=0;}
  if(recruitsInWave>=2||s.Catalog.SummonCost<=0)return null;
  const string reason="补齐暗水高阶输出，每个安全波次最多补抽两次";
  if(s.Units.Length<s.Boards.Length)
   return s.CanSummon&&s.Gold>=s.Catalog.SummonCost?new(){Kind="summon",Policy="clear50-recruit",Wave=s.Wave,Reason=reason}:null;
  double total=s.Units.Sum(u=>Utility(defs[u.Id],boards[u.Grid],s));
  var weakest=s.Units.Where(u=>u.Ready&&u.Index!=core.Index&&defs[u.Id].Grade<5&&s.Gold+defs[u.Id].Sell>=s.Catalog.SummonCost)
   .OrderBy(u=>Utility(defs[u.Id],boards[u.Grid],s)).FirstOrDefault();
  return weakest!=null&&Utility(defs[weakest.Id],boards[weakest.Grid],s)<=total*.05
   ?new(){Kind="sell",UnitIndex=weakest.Index,UnitId=weakest.Id,Grid=weakest.Grid,Policy="clear50-recruit",Wave=s.Wave,Reason=reason}:null;
 }
 private Decision? OpeningRoll(Snapshot s,Settings settings,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards,Unit? core)
 {
  if(core!=null)return null;
  if(s.Wave>10&&observedOpening&&settings.AutoNext&&!settings.StopAfterRound)
   return new(){Kind="restart",Policy="clear50-opening",Wave=s.Wave,Reason="前10波未形成7/8阶核心，退出本局后重新召唤"};
  if(s.Wave>10)return null; // A tool attached to a late round never discards it retrospectively.
  Focus="开局筛选：前10波优先召唤7/8阶核心";
  if(s.Units.Length<s.Boards.Length)
  {
   if(s.CanSummon&&s.Gold>=s.Catalog.SummonCost)return Act("summon","前10波优先刷核心，金币留给召唤");
   var wave=Current(s);
   if(s.Units.Length>=3&&!wave.Boss&&s.EnemyCount<s.Catalog.GameOverCount*.5&&s.Gold<s.Catalog.SummonCost)
   {
    // Spare board slots do not make holding every low-tier card useful. Recycle a
    // small contributor when the retained earners comfortably cover this wave.
    double total=s.Units.Sum(u=>Power(defs[u.Id],boards[u.Grid],s.Levels[defs[u.Id].Element],wave,s));
    double required=wave.Hp*wave.Count/Math.Max(1,wave.Duration);
    var spare=s.Units.Where(u=>u.Ready&&s.Gold+defs[u.Id].Sell>=s.Catalog.SummonCost)
     .Select(u=>new{Unit=u,Power=Power(defs[u.Id],boards[u.Grid],s.Levels[defs[u.Id].Element],wave,s)})
     .Where(x=>x.Power<=total*.1&&total-x.Power>=required*1.3)
     .OrderByDescending(x=>defs[x.Unit.Id].Sell).ThenBy(x=>x.Power).FirstOrDefault();
    if(spare!=null)return new(){Kind="sell",UnitIndex=spare.Unit.Index,UnitId=spare.Unit.Id,Grid=spare.Unit.Grid,Policy="clear50-opening",Reason="保留产金主力，回收低贡献使徒继续刷核心"};
   }
   return Act("wait","等待击杀金币，开局不分散升级属性");
  }
  if(s.Units.Length<=1)return null;
  // Keep the strongest earners. Each sale must immediately finance the replacement;
  // the controller confirms it and fills the vacant slot before making another choice.
  var replace=s.Units.Where(u=>u.Ready&&s.Gold+defs[u.Id].Sell>=s.Catalog.SummonCost)
   .OrderBy(u=>Utility(defs[u.Id],boards[u.Grid],s)).ThenBy(u=>defs[u.Id].Grade).FirstOrDefault();
  return replace==null?Act("wait","保留基础阵容产金，等待下一次补抽"):
   new(){Kind="sell",UnitIndex=replace.Index,UnitId=replace.Id,Grid=replace.Grid,Policy="clear50-opening",Reason="开局卖出最弱使徒，立即补抽7/8阶核心"};
 }
 private Decision? Consolidate(Snapshot s,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards,Unit? core,HashSet<int> elements)
 {
  if(core==null||s.Units.Length<=1||Current(s).Boss||s.EnemyCount>=s.Catalog.GameOverCount*.7)return null;
  var junk=s.Units.Where(u=>u.Ready&&u.Index!=core.Index&&defs[u.Id].Grade<7&&!elements.Contains(defs[u.Id].Element))
   .OrderBy(u=>Utility(defs[u.Id],boards[u.Grid],s)).FirstOrDefault();
  if(junk==null||s.Gold+defs[junk.Id].Sell<s.Catalog.SummonCost)return null;
  // Off-colour cards are temporary income, not permanent upgrade targets. Avoid
  // removing a substantial contributor before the high-tier core is positioned.
  double total=s.Units.Sum(u=>Utility(defs[u.Id],boards[u.Grid],s));
  if(Utility(defs[junk.Id],boards[junk.Grid],s)>total*.15)return null;
  return new(){Kind="sell",UnitIndex=junk.Index,UnitId=junk.Id,Grid=junk.Grid,Policy="clear50-core",Reason="保留高阶核心与暗水体系，回收低贡献杂色卡的金币"};
 }
 private Decision? CoreInvestment(Snapshot s,Dictionary<int,UnitDef> defs,Dictionary<int,Board> boards,HashSet<int> elements)
 {
  // The future-wave model cannot justify waiting while the live lane is filling.
  // Use an affordable improvement to current-wave output before reserving for a
  // more efficient future upgrade. Restrict it to the same core elements; crowd
  // pressure is not permission to buy unrelated cheap upgrades.
  var current=Current(s);
  if(!current.Boss&&s.Catalog.GameOverCount>0&&s.EnemyCount>=s.Catalog.GameOverCount*.6)
  {
   int urgent=-1;double immediateValue=0;
   foreach(int element in elements.OrderBy(e=>e))
   {
    int price=s.UpgradeCosts[element];
    if(price<=0||price>s.Gold||!s.CanUpgrade[element]||s.Levels[element]>=s.Catalog.MaxUpgrade)continue;
    double extra=s.Units.Where(u=>u.Ready&&defs[u.Id].Element==element).Sum(u=>
     Power(defs[u.Id],boards[u.Grid],s.Levels[element]+1,current,s)-Power(defs[u.Id],boards[u.Grid],s.Levels[element],current,s));
    if(extra/price>immediateValue+1e-10){immediateValue=extra/price;urgent=element;}
   }
   if(urgent>=0)return new(){Kind="upgrade",Element=urgent,Policy="clear50-core",Score=immediateValue,Reason="升级核心体系，补足后续波次与首领输出"};
  }
  // Budget for the next five waves and the next boss (at most ten waves away).
  // Considering unaffordable upgrades is essential: buying a roll whenever gold
  // reaches 25 otherwise prevents a 45+ gold core upgrade indefinitely.
  int wave=Math.Max(1,s.Wave);
  var nextBoss=s.Catalog.Waves.FirstOrDefault(w=>w.Boss&&w.Id>=wave&&w.Id<=wave+10);
  var horizon=s.Catalog.Waves.Where(w=>w.Id>=wave&&(w.Id<=wave+5||ReferenceEquals(w,nextBoss))).ToArray();
  int best=-1;double value=0;
  foreach(int element in elements.OrderBy(e=>e))
  {
   int price=s.UpgradeCosts[element];
   // A low-tier dark/water support group can beat an expensive core upgrade in
   // total marginal output. Elements are already restricted to the core plan.
   if(price<=0||s.Levels[element]>=s.Catalog.MaxUpgrade||!s.Units.Any(u=>defs[u.Id].Element==element))continue;
   double gain=0;
   foreach(var w in horizon)
   {
    double required=w.Hp*w.Count/Math.Max(1,w.Duration);
    if(w.Id==wave&&w.Boss&&s.Enemies.Length>0)required=s.Enemies.Sum(e=>e.Hp)/Math.Max(1,s.SecondsLeft);
    required*=w.Boss?1.3:1.2;
    if(required<=0)continue;
    double power=s.Units.Sum(u=>Power(defs[u.Id],boards[u.Grid],s.Levels[defs[u.Id].Element],w,s));
    double deficit=Math.Max(0,required-power);
    if(deficit<=0)continue;
    double extra=s.Units.Where(u=>defs[u.Id].Element==element).Sum(u=>Power(defs[u.Id],boards[u.Grid],s.Levels[element]+1,w,s)-Power(defs[u.Id],boards[u.Grid],s.Levels[element],w,s));
    gain+=Math.Min(deficit,Math.Max(0,extra))/required;
   }
   double efficiency=gain/price;
   if(efficiency>value+1e-10){value=efficiency;best=element;}
  }
  if(best<0)return null;
  Focus="核心培养：为后续波次与首领预留升级金币";
  return s.Gold>=s.UpgradeCosts[best]&&s.CanUpgrade[best]
   ?new(){Kind="upgrade",Element=best,Policy="clear50-core",Score=value,Reason="升级核心体系，补足后续波次与首领输出"}
   :Act("wait","保留金币等待核心升级，暂不消耗于随机召唤");
 }
}

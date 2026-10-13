using System.Numerics;

namespace BD2ApostleDefense;

// Frozen offline simulated-annealing result, selected on independent draw/timing trials.
// Only the decision policy runs here; no offline simulator, seed access or random-rate changes.
public sealed class AnnealedPolicy
{
 public const string Policy = "clear50-annealed";
 public const string ChasePolicy = "clear50-annealed-chase";
 private static readonly double[] X = [1,1,.9002206256684432,.21015787297070324,0,0,.5143419804602553,0,0,.7878408697143358,.008080410804370296,.6948426817444849,.8477460459862471,.7866979237759949,.05129428789086469,.21993362264999639,.6909438258241467,.9936903785894299,.13561697845122808,0,1,0,.906274689317154,0];
 private static readonly double[] Extra = [.5653155534273868,.28578353195102085,.8553000474473259,.9431684130200544,1,.42581169605539076,1,.7616779988770842,.009997876172129503,.034556408776884005,0,0];
 private readonly Dictionary<int,long> moved = new();
 private readonly Dictionary<int,double[]> coverage = new();
 private string session = "", geometry = "";
 private int wave = -1, rolls;
 private bool refill, counting;
 public void Reset() { session=""; geometry=""; wave=-1; rolls=0; refill=false; counting=false; moved.Clear(); coverage.Clear(); }
 private void Sync(Snapshot s)
 {
  string key=$"{s.ProcessId}|{s.ProcessStart}|{s.Account}|{s.Room}";
  if(session!=key){Reset();session=key;}
  if(wave!=s.Wave){wave=s.Wave;rolls=0;counting=false;}
 }
 public void Confirm(Decision d,Snapshot s,long now)
 {
  Sync(s);
  if(d.Kind=="summon"){if(counting&&d.Wave==s.Wave)rolls++;refill=false;}
  if(d.Kind=="sell"&&d.Policy==Policy)refill=true;
  if(d.Kind=="move"){moved[d.UnitIndex]=now;if(d.TargetIndex>=0)moved[d.TargetIndex]=now;}
 }
 public static int PopulationLimit(int phase)=>Math.Min(68,(int)Math.Round(3+X[phase]*21)+(int)Math.Round(Extra[phase]*44));
 public static int UpgradeLimit(int phase)=>(int)Math.Round(8+Extra[4+phase]*91);
 private static Vector2 V(Point p)=>new((float)p.X,(float)p.Z);
 private static bool Finite(Point p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
 private static double Frame(Snapshot s)=>double.IsFinite(s.FrameSeconds)&&s.FrameSeconds>0?Math.Clamp(s.FrameSeconds,1.0/240,1.0/10):1.0/60;
 private double[] Coverage(UnitDef d,Snapshot s)
 {
  if(coverage.TryGetValue(d.Id,out var cached))return cached;
  var result=new double[s.Boards.Length];float radius=(float)d.Range*(float)d.Range;
  for(int b=0;b<result.Length;b++)
  {
   double hit=0,total=0;
   for(int i=0;i<s.Path.Length;i++)
   {
    var a=V(s.Path[i]);var z=V(s.Path[(i+1)%s.Path.Length]);float length=Vector2.Distance(a,z);
    for(int n=0;n<128;n++){total+=length;if(Vector2.DistanceSquared(a+(z-a)*((n+.5f)/128),V(s.Boards[b]))<=radius)hit+=length;}
   }
   result[b]=total>0?hit/total:0;
  }
  return coverage[d.Id]=result;
 }
 public Decision Decide(Snapshot s,long now,Func<int,bool>? canMove=null,bool awaitingRefill=false)
 {
  Sync(s);
  Decision Wait(string message)=>new(){Reason=message,Policy=Policy,Wave=s.Wave};
  Decision Action(string kind,string message,int element=-1)=>new(){Kind=kind,Reason=message,Policy=Policy,Wave=s.Wave,Element=element};
  if(s.Levels.Length!=5||s.UpgradeCosts.Length!=5||s.CanUpgrade.Length!=5||s.Path.Length<2||s.Path.Any(p=>!Finite(p))||s.Boards.Any(p=>!Finite(p))||s.Units.Select(u=>u.Index).Distinct().Count()!=s.Units.Length||s.Units.Select(u=>u.Grid).Distinct().Count()!=s.Units.Length||s.Catalog.Units.Select(d=>d.Id).Distinct().Count()!=s.Catalog.Units.Length)
   return Wait("等待使徒与格位数据同步");
  var defs=s.Catalog.Units.ToDictionary(d=>d.Id);
  if(s.Units.Any(u=>!u.Ready||!defs.ContainsKey(u.Id)))return Wait("等待己方盘面与使徒加载完成");
  if(defs.Values.Any(d=>d.Element<0||d.Element>=5||!double.IsFinite(d.Range)||d.Range<=0||!double.IsFinite(d.Interval)||d.Interval<=0))return Wait("等待游戏规则表");
  var current=s.Catalog.Waves.FirstOrDefault(w=>w.Id==s.Wave);
  if(current==null||current.Duration<=0||current.Hp<=0||s.Catalog.SummonCost<=0)return Wait("等待游戏规则表");
  string key=Identity.Hash(s.Catalog.Key+string.Join(";",s.Boards.Cast<Point>().Concat(s.Path).Select(p=>$"{p.Id}:{p.X:R}:{p.Z:R}"))+string.Join(";",defs.Values.Select(d=>$"{d.Id}:{d.Range:R}")));
  if(geometry!=key){geometry=key;coverage.Clear();}
  var columns=s.Boards.Select((b,i)=>(b.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
  var occupied=s.Units.ToDictionary(u=>u.Grid);
  bool CanSummon()=>s.CanSummon&&s.Gold>=s.Catalog.SummonCost&&s.Units.Length<s.Boards.Length;
  bool Cooling(Unit u)=>canMove!=null&&!canMove(u.Index)||moved.TryGetValue(u.Index,out var at)&&now-at<TimeSpan.FromSeconds(.8).Ticks;
  Decision Move(Unit u,int column,double gain,bool chase=false,int target=-1)=>new(){Kind="move",Policy=chase?ChasePolicy:Policy,Wave=s.Wave,UnitIndex=u.Index,UnitId=u.Id,Grid=u.Grid,TargetGrid=s.Boards[column].Id,TargetIndex=occupied.GetValueOrDefault(s.Boards[column].Id)?.Index??-1,TargetEnemy=target,Score=gain,Reason=chase?"退火策略：追击首领，计入换位损失":"退火策略：调整站位，提高路线覆盖"};
  bool replacing=refill||awaitingRefill;
  if(replacing&&CanSummon())return Action("summon","出售已确认，优先补抽填回空位");
  if(s.Units.Length==0)return CanSummon()?Action("summon","退火策略：召唤基础阵容"):Wait("退火策略：等待召唤条件就绪");
  counting=true;
  int phase=s.Wave<=10?0:s.Wave<=20?1:s.Wave<=35?2:3;
  int cap=Math.Min(s.Boards.Length,PopulationLimit(phase));
  bool core=s.Units.Any(u=>defs[u.Id].Grade>=7),boss=current.Boss;
  var nextBoss=s.Catalog.Waves.Where(w=>w.Id>=s.Wave&&w.Boss).OrderBy(w=>w.Id).FirstOrDefault()??s.Catalog.Waves.OrderBy(w=>w.Id).Last();
  var final=s.Catalog.Waves.OrderBy(w=>w.Id).Last();
  double Power(Unit u,WaveDef w,int extra=0)
  {
   var d=defs[u.Id];double range=Coverage(d,s)[columns[u.Grid]];
   // 40 is the calibrated stream-density scale, not a navigation distance.
   double uptime=w.Boss?range:Math.Min(1,range*40/Math.Max(.1,w.Speed*w.SpawnInterval));
   double hit=(d.Attack+(double)d.UpAttack*(s.Levels[d.Element]+extra))*Planner.ElementMultiplier(d.Element,w.Element,s.Catalog);
   double splash=d.SplashRange>0?Math.Min(w.Count,1+2*d.SplashRange/Math.Max(.25,w.Speed*w.SpawnInterval)):1;
   return Math.Min(hit,w.Hp)*uptime*Math.Min(splash,8)/(d.Interval+Frame(s));
  }
  Decision? move=null;double moveGain=0;
  foreach(var u in s.Units)
  {
   if(canMove!=null&&!canMove(u.Index))continue;
   var d=defs[u.Id];var cv=Coverage(d,s);
   int dest=Enumerable.Range(0,s.Boards.Length).Where(i=>!occupied.ContainsKey(s.Boards[i].Id)).OrderByDescending(i=>cv[i]).ThenBy(i=>i).DefaultIfEmpty(-1).First();
   if(dest<0)continue;
   double gain=(cv[dest]-cv[columns[u.Grid]])*d.Attack*(1+s.Levels[d.Element]);
   if(gain>moveGain+1e-4){moveGain=gain;move=Move(u,dest,gain);}
  }
  if(!boss)foreach(var a in s.Units)foreach(var b in s.Units)
  {
   if(a.Index>=b.Index||Cooling(a)||Cooling(b))continue;
   var da=defs[a.Id];var db=defs[b.Id];var ca=Coverage(da,s);var cb=Coverage(db,s);int ia=columns[a.Grid],ib=columns[b.Grid];
   double gain=(ca[ib]-ca[ia])*da.Attack*(1+s.Levels[da.Element])+(cb[ia]-cb[ib])*db.Attack*(1+s.Levels[db.Element]);
   if(gain>moveGain+1e-4){moveGain=gain;move=Move(a,ib,gain);}
  }
  if(!boss&&move!=null)return move;
  if(replacing){if(boss){var chase=Chase();if(chase!=null)return chase;}return Wait("出售已确认，等待金币和召唤按钮就绪后补位");}
  double total=0,weak=double.MaxValue;Unit? weakest=null;
  foreach(var u in s.Units){double value=Power(u,current);total+=value;if(value<weak&&defs[u.Id].Grade<3){weakest=u;weak=value;}}
  double required=(boss?s.Enemies.Sum(e=>Math.Max(0,e.Hp))/Math.Max(1,s.SecondsLeft):current.Hp*current.Count/current.Duration)*(.6+X[4+phase]*2.4);
  bool pressured=s.EnemyCount>55||total<required;
  var elements=Enumerable.Range(0,5).OrderByDescending(e=>s.Units.Where(u=>defs[u.Id].Element==e).Sum(u=>Power(u,nextBoss)+Power(u,final)*.2));
  int upgrade=-1;double best=0;
  foreach(int e in elements)
  {
   int cost=s.UpgradeCosts[e];if(cost<=0||cost==int.MaxValue||s.Levels[e]>=s.Catalog.MaxUpgrade||!core&&s.Levels[e]>=UpgradeLimit(phase))continue;
   double gain=s.Units.Where(u=>defs[u.Id].Element==e).Sum(u=>Power(u,current,1)-Power(u,current)+(.1+X[18]*2)*(Power(u,nextBoss,1)-Power(u,nextBoss)));
   double value=gain/Math.Pow(cost,.4+X[16]*1.1);if(value>best){best=value;upgrade=e;}
  }
  bool Affordable()=>upgrade>=0&&s.CanUpgrade[upgrade]&&s.Gold>=s.UpgradeCosts[upgrade];
  if(s.Units.Length<Math.Min(3,cap)&&CanSummon())return Action("summon","退火策略：召唤基础阵容");
  if(pressured&&Affordable())return Action("upgrade","退火策略：升级属性，缓解当前波次压力",upgrade);
  if(boss){var chase=Chase();if(chase!=null)return chase;}
  bool canRoll=!boss&&rolls<(int)Math.Round(1+X[12+phase]*39);
  if(s.Units.Length<cap&&canRoll&&CanSummon()&&s.Gold>=s.Catalog.SummonCost+Extra[8+phase]*(upgrade<0?0:s.UpgradeCosts[upgrade])&&(!core||!pressured||upgrade<0))return Action("summon","退火策略：扩充阵容并保留升级预算");
  if(weakest!=null&&s.Units.Length>=3&&canRoll&&s.Gold+defs[weakest.Id].Sell>=s.Catalog.SummonCost&&weak<total*(.005+X[8+phase]*.25)&&s.EnemyCount<55&&total-weak>required)
   return new(){Kind="sell",Policy=Policy,Wave=s.Wave,UnitIndex=weakest.Index,UnitId=weakest.Id,Grid=weakest.Grid,Score=total-weak-required,Reason="退火策略：保留主力，回收低贡献使徒后补抽"};
  if(Affordable())return Action("upgrade","退火策略：培养阵容，准备后续首领",upgrade);
  return Wait("退火策略：保持当前输出，等待金币或波次变化");

  Decision? Chase()
  {
   double age=(now-s.At)/(double)TimeSpan.TicksPerSecond;
   if(age<-.1||age>.6||s.SecondsLeft<=0||s.Path.Select(p=>p.Id).Distinct().Count()!=s.Path.Length)return null;
   var active=s.Enemies.Where(e=>e.Hp>0).OrderByDescending(e=>e.Hp).Take(6).ToArray();
   if(active.Length==0||active.Any(e=>!Finite(e)||!double.IsFinite(e.Hp)||!double.IsFinite(e.Speed)||e.Speed<0||!s.Path.Any(p=>p.Id==e.NextPoint)))return null;
   // Native fixed-step travel, from the observed position/next waypoint only.
   var positions=new Vector2[active.Length,4];
   for(int j=0;j<active.Length;j++)
   {
    var enemy=active[j];var at=V(enemy);int next=Array.FindIndex(s.Path,p=>p.Id==enemy.NextPoint);int previous=0;
    for(int n=1;n<=4;n++)
    {
     int ticks=(int)((Math.Max(0,age)+.25+n*.8/4)/.02);
     for(int tick=previous;tick<ticks;tick++){var target=V(s.Path[next]);var delta=target-at;float distance=delta.Length();float step=(float)enemy.Speed*.02f;at=distance<=step?target:at+delta/distance*step;if(Vector2.Distance(at,target)<.1f)next=(next+1)%s.Path.Length;}
     positions[j,n-1]=at;previous=ticks;
    }
   }
   var cached=new Dictionary<int,double[]>();
   double[] Scores(Unit u)
   {
    if(cached.TryGetValue(u.Id,out var values))return values;
    var d=defs[u.Id];float radius=(float)d.Range*(float)d.Range;double output=(d.Attack+(double)d.UpAttack*s.Levels[d.Element])/(d.Interval+Frame(s));values=new double[s.Boards.Length];
    for(int b=0;b<values.Length;b++)for(int j=0;j<active.Length;j++)for(int n=0;n<4;n++)if(Vector2.DistanceSquared(V(s.Boards[b]),positions[j,n])<=radius)values[b]+=Math.Min(active[j].Hp,output*.8/4)*Planner.ElementMultiplier(d.Element,active[j].Element,s.Catalog);
    return cached[u.Id]=values;
   }
   Decision? selected=null;double gainBest=0;
   foreach(var u in s.Units)
   {
    if(Cooling(u))continue;var d=defs[u.Id];var scores=Scores(u);int from=columns[u.Grid];double output=(d.Attack+(double)d.UpAttack*s.Levels[d.Element])/(d.Interval+Frame(s));
    for(int b=0;b<scores.Length;b++)
    {
     if(b==from)continue;double gain=scores[b]-scores[from];var other=occupied.GetValueOrDefault(s.Boards[b].Id);
     if(other!=null){if(Cooling(other))continue;var displaced=Scores(other);gain+=displaced[from]-displaced[b];}
     if(gain>Math.Max(gainBest,output*.8*.01)){gainBest=gain;selected=Move(u,b,gain,true,active[0].Id);}
    }
   }
   return selected;
  }
 }
}

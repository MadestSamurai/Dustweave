namespace BD2ApostleDefense;

// Short-horizon expected-value planner. Scores are estimates, never claimed as a win probability.
public sealed partial class Planner
{
 private readonly Dictionary<string,double> coverageCache=new();private string geometry="";
 private readonly BossChase bossChase=new();
 public void ResetTactics()=>bossChase.Reset();
 public void Confirm(Decision d,Snapshot s,long now)=>bossChase.Confirm(d,s,now);
 public string Focus {get;private set;}="50波通关";
 public double Coverage(UnitDef u,Board b,Point[] path)
 {
  if(path.Length<2)return 0;
  var key=$"{u.Id}:{u.Range}:{b.Id}";if(coverageCache.TryGetValue(key,out var value))return value;
  double hit=0,total=0;
  for(int i=0;i<path.Length;i++)
  {
   var a=path[i];var z=path[(i+1)%path.Length];double length=Distance(a,z);if(length<1e-6)continue;
   int samples=Math.Max(8,(int)Math.Ceiling(length*8));
   for(int n=0;n<samples;n++){double t=(n+.5)/samples;var p=new Point{X=a.X+(z.X-a.X)*t,Y=a.Y+(z.Y-a.Y)*t,Z=a.Z+(z.Z-a.Z)*t};total+=length/samples;if(Distance(p,b)<=u.Range)hit+=length/samples;}
  }
  return coverageCache[key]=total>0?hit/total:0;
 }
 public static double Distance(Point a,Point b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
 public static double ElementMultiplier(int attack,int defense,Catalog c)
 {
  if((attack==0&&defense==1)||(attack==1&&defense==2)||(attack==2&&defense==0)||(attack==3&&defense==4)||(attack==4&&defense==3))return 1+c.Advantage;
  if((attack==0&&defense==2)||(attack==1&&defense==0)||(attack==2&&defense==1))return 1-c.Penalty;
  return 1;
 }
 private double Power(UnitDef u,Board b,int level,WaveDef w,Snapshot s)
 {
  double coverage=Coverage(u,b,s.Path);
  double attack=(u.Attack+u.UpAttack*(double)level)*ElementMultiplier(u.Element,w.Element,s.Catalog);
  // Number of targets in the splash disk estimated from spawn spacing; bosses keep their actual count.
  double splash=u.SplashRange>0?Math.Min(w.Count,1+2*u.SplashRange/Math.Max(.25,w.Speed*w.SpawnInterval)):1;
  splash=Math.Min(splash,8); // conservative crowding cap; no claim to reproduce native target timing.
  return Math.Min(attack,w.Hp)*coverage*Math.Max(1,splash)/Math.Max(.1,u.Interval+1.0/60);
 }
 private WaveDef Current(Snapshot s)=>s.Catalog.Waves.FirstOrDefault(w=>w.Id==Math.Max(1,s.Wave))??s.Catalog.Waves[0];
 private double Utility(UnitDef u,Board b,Snapshot s,int extra=0)
 {
  var cur=Current(s);var next=s.Catalog.Waves.FirstOrDefault(w=>w.Boss&&w.Id>cur.Id)??cur;var final=s.Catalog.Waves.Last();
  return .60*Power(u,b,s.Levels[u.Element]+extra,cur,s)+.30*Power(u,b,s.Levels[u.Element]+extra,next,s)+.10*Power(u,b,s.Levels[u.Element]+extra,final,s);
 }
 public static int[] Assignment(double[,] scores)
 {
  int n=scores.GetLength(0),m=scores.GetLength(1);
  if(n>m)throw new ArgumentException("使徒数量超过可用格数");
  if(n==0)return Array.Empty<int>();
  foreach(double value in scores)if(!double.IsFinite(value))throw new ArgumentException("布阵评分包含无效数值");
  // Rectangular Hungarian assignment: exact maximum additive utility in O(n*n*m), O(n+m)
  // auxiliary storage. No subset masks or exponential allocation tied to the board size.
  var rowPotential=new double[n+1];var colPotential=new double[m+1];
  var matchedRow=new int[m+1];var previous=new int[m+1];
  for(int row=1;row<=n;row++)
  {
   matchedRow[0]=row;int column=0;
   var distance=Enumerable.Repeat(double.PositiveInfinity,m+1).ToArray();var used=new bool[m+1];
   do
   {
    used[column]=true;int currentRow=matchedRow[column],next=0;double delta=double.PositiveInfinity;
    for(int j=1;j<=m;j++)if(!used[j])
    {
     double cost=-scores[currentRow-1,j-1]-rowPotential[currentRow]-colPotential[j];
     if(cost<distance[j]){distance[j]=cost;previous[j]=column;}
     if(distance[j]<delta){delta=distance[j];next=j;}
    }
    for(int j=0;j<=m;j++)if(used[j]){rowPotential[matchedRow[j]]+=delta;colPotential[j]-=delta;}else distance[j]-=delta;
    column=next;
   }while(matchedRow[column]!=0);
   do{int from=previous[column];matchedRow[column]=matchedRow[from];column=from;}while(column!=0);
  }
  var result=new int[n];for(int j=1;j<=m;j++)if(matchedRow[j]>0)result[matchedRow[j]-1]=j-1;
  return result;
 }
 public static bool WorthMove(double gain,double affectedUtility)
 {
  // Prevent numerical churn, but do not let unrelated strong units suppress a melee rescue.
  return gain>Math.Max(1e-6,affectedUtility*.025);
 }
 public static bool CanReroll(double unitDps,double armyDps,double requiredDps,int enemies,int limit,bool boss,double secondsLeft)
 {
  // An unreachable unit contributes nothing to the current route. Removing it cannot weaken
  // the army, even when the coarse absolute DPS estimate says the whole army is insufficient.
  if(unitDps<=1e-6)return true;
  if(enemies>=limit*.5||(boss&&secondsLeft<15))return false;
  if(armyDps-unitDps>=requiredDps*1.2)return true;
  // During ordinary low-pressure waves, permit a small, explicit temporary output loss.
  return !boss&&unitDps<=armyDps*.01;
 }
 private static Decision Act(string kind,string reason)=>new(){Kind=kind,Reason=reason};
 public Decision Decide(Snapshot s,Settings settings,long? now=null,Func<int,bool>? canMove=null)
 {
  if(s.Stage=="lobby"&&!s.Exiting&&s.CanDismissEvent)return Act("close-event","关闭返回大厅后的活动弹窗，继续核对成就");
  if(s.Blocker!="")return Act("wait","等待关闭弹窗："+s.Blocker);
  if(s.Exiting)return Act("wait","正在等待游戏提交结算并返回大厅");
  if(s.Stage=="lobby")
  {
   if(!s.AchievementsKnown)return Act("refresh","向服务器读取两个成就的实际进度");
   if(Guards.GoalsMet(s,settings))return Act("complete","所选成就均已由服务器确认完成");
   if(settings.StopAfterRound)return Act("complete","本局已结算，按设置停止");
   return settings.AutoNext?Act("start","快速匹配下一局"):Act("wait","请在游戏中开始对局，或开启自动续局");
  }
  if(s.Stage=="match-failed")return Act("retry","关闭匹配失败提示后重新匹配");
  if(s.Stage=="matching"&&s.CanStartMatch)return Act("match-start","匹配房间已就绪，点击原生开始按钮");
  if(s.Stage=="ended"&&GameFlow.CanSettle(s))return Act("open-result","当前对局已结束，打开结算");
  if(s.Stage=="result"&&GameFlow.CanSettle(s))return Act("settle","点击结算退出，等待确认框或返回大厅");
  if(s.Stage=="confirm-exit"&&GameFlow.CanSettle(s))return Act("confirm-exit","确认离开已结束的对局并提交成绩");
  if(s.Stage!="playing"||!s.Ready||!s.MyView)return Act("wait",s.Stage=="matching"?"等待原生匹配倒计时":"等待己方盘面与使徒加载完成");
  if(s.Catalog.Waves.Length==0||s.Catalog.Units.Length==0)return Act("wait","等待游戏规则表");
  if(s.Boards.Length==0||s.Units.Length>s.Boards.Length||s.Boards.Select(b=>b.Id).Distinct().Count()!=s.Boards.Length||s.Units.Any(u=>!s.Boards.Any(b=>b.Id==u.Grid)))return Act("wait","等待使徒与格位数据同步");
  string key=Identity.Hash(string.Join(";",s.Boards.Cast<Point>().Concat(s.Path).Select(p=>$"{p.Id}:{p.X:R}:{p.Y:R}:{p.Z:R}"))+s.Catalog.Key);if(key!=geometry){geometry=key;coverageCache.Clear();}
  bool rareMode=settings.Rare&&(!settings.Clear50||(s.AchievementsKnown&&s.ClearProgress>=s.ClearTarget));
  // Pending spawns must settle before achievement completion; stop spending on extra rolls meanwhile.
  if(rareMode&&s.AchievementsKnown&&s.RareProgress+s.RoundRare>=s.RareTarget)rareMode=false;
  Focus=rareMode?"最高级使徒：保留产金核心，增加召唤次数":"50波通关：输出、覆盖与首领准备";
  var defs=s.Catalog.Units.ToDictionary(u=>u.Id);var boards=s.Boards.ToDictionary(b=>b.Id);var cur=Current(s);
  if(cur.Boss)
  {
   var chase=bossChase.Decide(s,settings,now??s.At,canMove);Focus="BOSS追击：动态换位与持续输出";
   if(chase!=null)return chase;
  }
  else
  {
  double[,] matrix=new double[s.Units.Length,s.Boards.Length];
  for(int i=0;i<s.Units.Length;i++)for(int j=0;j<s.Boards.Length;j++)matrix[i,j]=Utility(defs[s.Units[i].Id],s.Boards[j],s);
  var target=Assignment(matrix);
  // A global assignment can contain cycles whose direct target swaps are poor first steps.
  // Examine every executable empty-slot move / pair swap; use the global target only for ties.
  var columns=s.Boards.Select((b,i)=>(b.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
  var occupants=s.Units.Select((u,i)=>(u.Grid,i)).ToDictionary(x=>x.Grid,x=>x.i);
  Decision? bestMove=null;double moveGain=0;
  bool bestFollowsTarget=false;
  for(int i=0;i<s.Units.Length;i++)
  {
   var u=s.Units[i];if(canMove!=null&&!canMove(u.Index))continue;int from=columns[u.Grid];
   for(int j=0;j<s.Boards.Length;j++)
   {
    if(j==from)continue;var destination=s.Boards[j];
    double affected=matrix[i,from],ownGain=matrix[i,j]-affected,gain=ownGain;int other=-1;
    if(occupants.TryGetValue(destination.Id,out int occupied))
    {other=occupied;if(canMove!=null&&!canMove(s.Units[other].Index))continue;gain+=matrix[other,from]-matrix[other,j];}
    // The relocated beneficiary must improve meaningfully and the pair must gain overall.
    // A powerful ranged occupant keeping the same coverage must not suppress an idle melee.
    if(!WorthMove(ownGain,affected)||!WorthMove(gain,affected))continue;
    bool follows=target[i]==j;
    if(gain>moveGain+1e-8||(Math.Abs(gain-moveGain)<=1e-8&&follows&&!bestFollowsTarget))
    {
     moveGain=gain;bestFollowsTarget=follows;var d=defs[u.Id];string name=string.IsNullOrWhiteSpace(d.Name)?$"{Names.Element(d.Element)}属性{d.Grade}阶使徒 #{u.Index}":$"{d.Name} #{u.Index}";
     bestMove=new(){Kind="move",UnitIndex=u.Index,UnitId=u.Id,Grid=u.Grid,TargetGrid=destination.Id,TargetIndex=other<0?-1:s.Units[other].Index,Reason=$"调整{name}：格{u.Grid}→格{destination.Id}，改善受影响使徒的射程覆盖",Score=gain};
    }
   }
  }
  if(bestMove!=null)return bestMove;
  }
  double total=s.Units.Sum(u=>Utility(defs[u.Id],boards[u.Grid],s));
  double currentDps=s.Units.Sum(u=>Power(defs[u.Id],boards[u.Grid],s.Levels[defs[u.Id].Element],cur,s));
  double required=cur.Boss?s.Enemies.Sum(e=>e.Hp)/Math.Max(1,s.SecondsLeft):cur.Hp*cur.Count/Math.Max(1,cur.Duration);
  if(cur.Boss&&required==0)required=cur.Hp*cur.Count/Math.Max(1,cur.Duration);
  bool urgent=s.EnemyCount>=s.Catalog.GameOverCount*.6||currentDps<required*1.2;
  var odds=s.Catalog.Units.ToDictionary(d=>d.Id,d=>d.Weight);
  double weight=s.Catalog.Units.Sum(d=>(double)d.Weight);if(weight<=0||s.Catalog.SummonCost<=0)return Act("wait","召唤概率表不可用");
  var emptyBoards=s.Boards.Where(b=>!s.Units.Any(u=>u.Grid==b.Id)).ToArray();
  var empty=emptyBoards.FirstOrDefault();
  bool opening=empty!=null&&!cur.Boss&&!s.Catalog.Waves.Any(w=>w.Boss&&w.Id<=cur.Id);
  if(opening&&s.Units.Length<Math.Min(3,s.Boards.Length))
   return s.CanSummon&&s.Gold>=s.Catalog.SummonCost
    ?Act("summon","开局先补足基础阵容，再考虑属性升级")
    :Act("wait","开局保留金币，等待召唤下一名使徒");
  double SpendingValue(UnitDef u,Board b,int extra=0)=>opening?OpeningUtility(u,b,s,extra):Utility(u,b,s,extra);
  // A summoned unit can be moved on the next decision. Score its best empty
  // destination, not only the native first-empty spawn slot (which can be inland).
  double rollValue=empty==null?0:s.Catalog.Units.Sum(d=>odds[d.Id]*emptyBoards.Max(b=>SpendingValue(d,b)))/weight/s.Catalog.SummonCost;
  Decision? bestUpgrade=null;double upgradeValue=0;
  for(int e=0;e<5;e++)if(s.CanUpgrade[e]&&s.Levels[e]<s.Catalog.MaxUpgrade&&s.UpgradeCosts[e]>0&&s.Gold>=s.UpgradeCosts[e])
  {
   if(opening&&!OpeningUpgradeAllowed(e,s,defs,boards))continue;
   double gain=s.Units.Where(u=>defs[u.Id].Element==e).Sum(u=>SpendingValue(defs[u.Id],boards[u.Grid],1)-SpendingValue(defs[u.Id],boards[u.Grid]));double v=gain/s.UpgradeCosts[e];
   if(v>upgradeValue){upgradeValue=v;bestUpgrade=new(){Kind="upgrade",Element=e,Score=v,Reason=$"升级{Names.Element(e)}属性，强化现有{ s.Units.Count(u=>defs[u.Id].Element==e)}名使徒"};}
  }
  if(empty!=null)
  {
   if(bestUpgrade!=null&&upgradeValue>rollValue*(rareMode?2.5:1.15)&&(urgent||!rareMode))return bestUpgrade;
   if(s.CanSummon&&s.Gold>=s.Catalog.SummonCost)return Act("summon",rareMode?"保留核心，利用空位继续召唤":"补充空位，扩大输出与元素覆盖");
   // Before the first boss, an unaffordable summon does not make every affordable
   // upgrade worthwhile. Compare its value and income payback before spending.
   if(bestUpgrade!=null&&urgent&&(!opening||upgradeValue>rollValue*(rareMode?2.5:1.15)))return bestUpgrade;
   return Act("wait","等待击杀收益，保留现有使徒");
  }
  // Sell + reroll is evaluated as a compound action with sufficient purchase money and survival reserve.
  Decision? bestSell=null;double replaceValue=0;
  foreach(var u in cur.Boss?Array.Empty<Unit>():s.Units)
  {
   if(s.Units.Length<=1)break;var d=defs[u.Id];double value=Utility(d,boards[u.Grid],s);
   double contribution=Power(d,boards[u.Grid],s.Levels[d.Element],cur,s);
   if(s.Gold+d.Sell<s.Catalog.SummonCost||!CanReroll(contribution,currentDps,required,s.EnemyCount,s.Catalog.GameOverCount,cur.Boss,s.SecondsLeft))continue;
   double netCost=Math.Max(1,s.Catalog.SummonCost-d.Sell);
   double expectation=s.Catalog.Units.Sum(x=>odds[x.Id]*Utility(x,boards[u.Grid],s))/weight;
   // Rare mode gives an explicit throughput bonus only to a low-contribution roll slot.
   double bonus=rareMode&&value<=total*.14?Math.Max(1,total*.10):0;
   double roi=(expectation-value+bonus)/netCost;
   if(roi>replaceValue)
   {
    replaceValue=roi;string name=string.IsNullOrWhiteSpace(d.Name)?$"{Names.Element(d.Element)}属性{d.Grade}阶使徒 #{u.Index}":$"{d.Name} #{u.Index}";
    string why=contribution<=1e-6?"当前位置覆盖不到路线":$"预计占当前输出{contribution/Math.Max(1e-6,currentDps):P1}";
    bestSell=new(){Kind="sell",UnitIndex=u.Index,UnitId=u.Id,Grid=u.Grid,Score=roi,Reason=$"卖出{name}（格{u.Grid}，{why}）并补抽；重抽预期收益为正"};
   }
  }
  if(bestSell!=null&&replaceValue>upgradeValue*(rareMode?0.8:1.25))return bestSell;
  if(bestUpgrade!=null&&(!rareMode||urgent||bestSell==null))return bestUpgrade;
  return Act("wait",cur.Boss?bossChase.Status:urgent?"保留当前输出，等待金币进行升级":"当前阵容优于可负担的替换，等待收益");
 }
}
public static class Names
{
 public static string Element(int e)=>e>=0&&e<5?new[]{"水","火","风","光","暗"}[e]:"?";
 public static string Stage(string s)=>s switch{"lobby"=>"小游戏大厅","matching"=>"匹配中","playing"=>"战斗中","ended"=>"等待结算","result"=>"结算页面","confirm-exit"=>"确认结算","settling"=>"正在提交结算并返回大厅","match-failed"=>"匹配失败","login"=>"等待登录",_=>"等待盘面"};
}

namespace BD2ApostleDefense;

// Receding-horizon native move/swap selection. This is a tactical estimate, not a full match simulator.
public sealed class BossChase
{
 public const string Policy = "boss-chase";
 private readonly Dictionary<int, long> movedAt = new();
 private string session = "";
 public string Status { get; private set; } = "";
 public double LastHorizon { get; private set; }
 public void Reset() { movedAt.Clear(); session = ""; }
 private void Sync(Snapshot s)
 {
  string key = $"{s.ProcessId}|{s.ProcessStart}|{s.Account}|{s.Room}|{s.Wave}";
  if (session == key) return;
  movedAt.Clear(); session = key;
 }
 public void Confirm(Decision d, Snapshot s, long now)
 {
  Sync(s);
  if (d.Policy != Policy || d.Wave != s.Wave) return;
  movedAt[d.UnitIndex] = now;
  if (d.TargetIndex >= 0) movedAt[d.TargetIndex] = now;
 }
 public readonly record struct Position(double X, double Y, double Z)
 {
  public static Position Of(Point p) => new(p.X, p.Y, p.Z);
  public double DistanceSquared(Position b) => (X-b.X)*(X-b.X)+(Y-b.Y)*(Y-b.Y)+(Z-b.Z)*(Z-b.Z);
 }
 // Native enemies walk toward the captured next waypoint and advance when within 0.1 world units.
 // Preserve the actual path-list order, including wraparound and noncontiguous point IDs.
 public static Position[] Predict(Enemy enemy, Point[] path, double step, int count)
 {
  int next = Array.FindIndex(path, p => p.Id == enemy.NextPoint);
  if (next < 0 || path.Length < 2) throw new ArgumentException("Missing enemy waypoint");
  var result = new Position[count]; var at = Position.Of(enemy);
  for (int i = 0; i < count; i++)
  {
   result[i] = at;
   double left = step;
   while (left > 1e-8)
   {
    double dt = Math.Min(.02, left); left -= dt;
    var target = Position.Of(path[next]); double distance = Math.Sqrt(at.DistanceSquared(target));
    double fraction = distance > 1e-9 ? Math.Min(1, enemy.Speed * dt / distance) : 1;
    at = new(at.X+(target.X-at.X)*fraction, at.Y+(target.Y-at.Y)*fraction, at.Z+(target.Z-at.Z)*fraction);
    if (at.DistanceSquared(target) < .01) next = (next + 1) % path.Length;
   }
  }
  return result;
 }
 public Decision? Decide(Snapshot s, Settings settings, long now, Func<int,bool>? canMove=null)
 {
  Sync(s); Status = "";
  var wave = s.Catalog.Waves.FirstOrDefault(w => w.Id == s.Wave);
  if (wave?.Boss != true) return null;
  Status = "BOSS追击：等待首领与路径同步";
  double age = (now-s.At) / (double)TimeSpan.TicksPerSecond;
  if (s.Stage != "playing" || !s.MyView || !s.Ready || s.Exiting || GameFlow.RoundFinished(s) || s.Blocker.Length > 0 || age < -.1 || age > .6 || s.SecondsLeft <= 0 || wave.EnemyTable <= 0 || s.Path.Length < 2) return null;
  var enemies = s.Enemies.Where(e => e.Hp > 0).ToArray();
  if (enemies.Length == 0 || enemies.Select(e => e.Id).Distinct().Count() != enemies.Length ||
      enemies.Any(e => !double.IsFinite(e.Hp) || !double.IsFinite(e.Speed) || e.Speed < 0 || !Finite(e) || !s.Path.Any(p => p.Id == e.NextPoint)) || s.Path.Any(p => !Finite(p)) || s.Path.Select(p => p.Id).Distinct().Count() != s.Path.Length) return null;
  var boss = enemies.Select(e => e.Table == wave.EnemyTable).ToArray();
  if (!boss.Any(b => b)) { Status = "BOSS追击：等待首领出现或结算"; return null; }
  var defs = s.Catalog.Units.ToDictionary(d => d.Id);
  if (s.Units.Any(u => !defs.ContainsKey(u.Id) || u.AttackState < 0 || u.AttackState > 2 || !double.IsFinite(u.AttackElapsed))) return null;
  if (s.Boards.Any(b => !Finite(b)) || s.Units.Length == 0) return null;
  const double step = 1.0 / 30;
  double lead = Math.Max(0, age) + .25; // Snapshot age + file/native dispatch budget.
  double reaction = Math.Clamp(settings.IntervalMs / 1000.0 + .6, .8, 3.6);
  double horizon = Math.Min(s.SecondsLeft, Math.Max(2.8, lead + reaction + .7)); LastHorizon = horizon;
  if (horizon <= lead + .15) { Status = "BOSS追击：保持输出，等待波次结算"; return null; }
  int frames = (int)Math.Ceiling(horizon / step) + 1, executeFrame = Math.Min(frames-1, (int)Math.Ceiling(lead / step));
  var paths = enemies.Select(e => Predict(e, s.Path, step, frames)).ToArray();
  var boards = s.Boards; var columns = boards.Select((b,i) => (b.Id,i)).ToDictionary(x => x.Id,x => x.i);
  if (s.Units.Any(u => !columns.ContainsKey(u.Grid))) return null;
  var occupant = s.Units.Select((u,i) => (u.Grid,i)).ToDictionary(x => x.Grid,x => x.i);
  var lookup = enemies.Select((e,i) => (e.Id,i)).ToDictionary(x => x.Id,x => x.i);
  var values = new Dictionary<(int unit,int grid),double[]>();
  double[] Damage(int index, int column)
  {
   if (values.TryGetValue((index,column), out var known)) return known;
   var u = s.Units[index]; var d = defs[u.Id]; var original = Position.Of(boards[columns[u.Grid]]); var destination = Position.Of(boards[column]);
   var damage = new double[enemies.Length]; int phase = u.AttackState;
   int target = lookup.GetValueOrDefault(u.TargetEnemy, -1); double elapsed = Math.Max(0,u.AttackElapsed), range2 = d.Range*d.Range;
   // Carry the observed attack timer and target across relocation. Moving is not a new summon.
   for (int frame = 0; frame < frames; frame++)
   {
    var pos = frame < executeFrame ? original : destination;
    bool Alive(int e) => e >= 0 && damage[e] < enemies[e].Hp;
    if (phase == 0)
    {
     elapsed += step;
     if (elapsed + 1e-9 < d.Interval) continue;
     if (Alive(target) && pos.DistanceSquared(paths[target][frame]) <= range2) phase = 1;
     else { elapsed = 0; target = -1; phase = 2; }
    }
    else if (phase == 2)
    {
     double nearest = double.MaxValue; target = -1;
     for (int e = 0; e < enemies.Length; e++) if (Alive(e))
     {
      double distance = pos.DistanceSquared(paths[e][frame]);
      if (distance <= range2 && distance < nearest) { nearest = distance; target = e; }
     }
     if (target >= 0) phase = 1;
    }
    else
    {
     if (!Alive(target)) { target = -1; elapsed = 0; phase = 2; continue; }
     double hit = Math.Max(0, d.Attack+(double)d.UpAttack*s.Levels[d.Element]) * Planner.ElementMultiplier(d.Element,enemies[target].Element,s.Catalog);
     damage[target] += Math.Min(hit, Math.Max(0,enemies[target].Hp-damage[target]));
     if (d.SplashRange > 0) for (int e = 0; e < enemies.Length; e++)
      if (e != target && Alive(e) && paths[e][frame].DistanceSquared(paths[target][frame]) <= d.SplashRange*d.SplashRange)
       damage[e] += Math.Min(hit, Math.Max(0,enemies[e].Hp-damage[e]));
     elapsed = 0; phase = 0;
    }
   }
   return values[(index,column)] = damage;
  }
  var baseline = new double[s.Units.Length][]; var total = new double[enemies.Length];
  for (int i=0;i<s.Units.Length;i++) { baseline[i]=Damage(i,columns[s.Units[i].Grid]);for(int e=0;e<enemies.Length;e++)total[e]+=baseline[i][e]; }
  // Count useful damage only, so already-covered low-HP bosses don't provoke unnecessary moves.
  double Weight(int e) => boss[e] ? 1 : s.EnemyCount >= s.Catalog.GameOverCount*.5 ? .8 : .2;
  double oldValue = Enumerable.Range(0,enemies.Length).Sum(e=>Weight(e)*Math.Min(enemies[e].Hp,total[e]));
  double bestGain = 0; Decision? best = null;
  bool Cooling(int index) => (canMove!=null&&!canMove(index)) || (movedAt.TryGetValue(index,out long time) && now-time < TimeSpan.FromMilliseconds(700).Ticks);
  for (int i=0;i<s.Units.Length;i++)
  {
   var unit=s.Units[i]; var d=defs[unit.Id]; if(!unit.Ready || d.Attack<=0 || d.Range<=0 || Cooling(unit.Index))continue;
   int from=columns[unit.Grid]; var origin=Position.Of(boards[from]); double range2=d.Range*d.Range;
   // Stay put while the core still has a continuous boss target through the next decision window.
   bool needsMove=false;
   int checkEnd=Math.Min(frames-1,(int)Math.Ceiling((lead+reaction)/step));
   for(int frame=executeFrame;frame<=checkEnd;frame++)
    if(!Enumerable.Range(0,enemies.Length).Any(e=>boss[e]&&origin.DistanceSquared(paths[e][frame])<=range2)) { needsMove=true;break; }
   if(!needsMove)continue;
   for(int j=0;j<boards.Length;j++)
   {
    if(j==from)continue;
    var position=Position.Of(boards[j]);
    int focus=Enumerable.Range(0,enemies.Length).Where(e=>boss[e]&&position.DistanceSquared(paths[e][executeFrame])<=range2).OrderBy(e=>position.DistanceSquared(paths[e][executeFrame])).DefaultIfEmpty(-1).First();
    if(focus<0)continue; // The destination must be able to hit upon arrival, not after a long wait.
    int other=occupant.GetValueOrDefault(boards[j].Id,-1);
    if(other>=0&&(!s.Units[other].Ready||Cooling(s.Units[other].Index)))continue;
    var proposed=Damage(i,j); var exchange=other>=0?Damage(other,from):null;
    double ownGain=0,gain=-oldValue;
    for(int e=0;e<enemies.Length;e++)
    {
     ownGain+=Weight(e)*(proposed[e]-baseline[i][e]);
     double after=total[e]+proposed[e]-baseline[i][e]+(other>=0?exchange![e]-baseline[other][e]:0);
     gain+=Weight(e)*Math.Min(enemies[e].Hp,Math.Max(0,after));
    }
    double hitValue=(d.Attack+(double)d.UpAttack*s.Levels[d.Element])*Planner.ElementMultiplier(d.Element,enemies[focus].Element,s.Catalog);
    double existing=Enumerable.Range(0,enemies.Length).Sum(e=>Weight(e)*baseline[i][e]);
    double threshold=Math.Max(1,Math.Max(hitValue*.5,existing*.06));
    if(ownGain<=threshold||gain<=threshold||gain<=bestGain+1e-6)continue;
    bestGain=gain;
    string label=string.IsNullOrWhiteSpace(d.Name)?$"{Names.Element(d.Element)}属性{d.Grade}阶 #{unit.Index}":$"{d.Name} #{unit.Index}";
    best=new(){Kind="move",Policy=Policy,Wave=s.Wave,TargetEnemy=enemies[focus].Id,UnitIndex=unit.Index,UnitId=unit.Id,Grid=unit.Grid,TargetGrid=boards[j].Id,TargetIndex=other<0?-1:s.Units[other].Index,Score=gain,
     Reason=$"BOSS追击：{label} 格{unit.Grid}→格{boards[j].Id}，未来{horizon:0.0}秒全队有效伤害预计增加{gain:N0}（已扣交换损失）"};
   }
  }
  Status=best==null?"BOSS追击：保持当前输出，监测离开射程的时机":best.Reason;
  return best;
 }
 private static bool Finite(Point p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
}

namespace BD2SecretVision;
public static class Planning {
 public static double Travel(Player p,double distance,double delay=0){double speed=Math.Max(1,p.Speed),boost=p.SuperBonus*(1-p.SlowStacks*.1),baseSpeed=Math.Max(1,speed-boost),remain=Math.Max(0,p.SuperRemaining-delay),d=Math.Min(distance,speed*remain);return d/speed+(distance-d)/baseSpeed;}
 public static double Distance(IEnumerable<Point> points){var p=points.ToArray();return Enumerable.Range(1,Math.Max(0,p.Length-1)).Sum(i=>Math.Abs(p[i].X-p[i-1].X)+Math.Abs(p[i].Y-p[i-1].Y));}
 public static double ItemValue(Item item,Player p,double remaining){
 if(item.Type=="HopscotchItemSpeedUp"){if(p.SpeedStacks>=5)return 0;var speed=Math.Max(1,p.Speed-p.SuperBonus*(1-p.SlowStacks*.1));return remaining/speed-remaining/(speed+10);}
 if(item.Type=="HopscotchItemSuperUp"){if(p.SuperRemaining>1)return 0;var speed=Math.Max(1,p.Speed);return Math.Min(10,remaining/(speed+100))*100/speed;}
 return item.Type=="HopscotchItemTimer"?3.5:0;
 }
 public static double Window(Board b,double walk=0){var e=b.Enemy;return Math.Max(0,Rules.AttackOpportunity.Available(e.State,e.NormalInterval-e.NormalElapsed,e.SpecialInterval-e.SpecialElapsed,e.ControlRemaining,e.CarveBlockedRemaining)-walk-.55);}
 public static Point[] Trim(Board b,Point start,IEnumerable<Point> corners){
 bool active=false;var output=new List<Point>();var a=start;var seen=new HashSet<(Point,Point)>();
 foreach(var target in corners){if(a.X!=target.X&&a.Y!=target.Y)throw new RouteFailure("diagonal");
 while(a!=target){int dx=Math.Sign(target.X-a.X),dy=Math.Sign(target.Y-a.Y);var z=new Point(a.X+dx,a.Y+dy);if(z.X<1||z.Y<1||z.X>=b.Width||z.Y>=b.Height)throw new RouteFailure("outside-grid");
 bool s1,s2;if(dx!=0){int x=Math.Min(a.X,z.X);s1=b.Claimed(x,a.Y);s2=b.Claimed(x,a.Y-1);}else{int y=Math.Min(a.Y,z.Y);s1=b.Claimed(a.X,y);s2=b.Claimed(a.X-1,y);}
 if(s1&&s2)throw new RouteFailure("interior-edge");if(b.HasTopology?Topology.Edge(b,a,z)=='.':!s1&&!s2)active=true;
 if(b.HasTopology&&Topology.Edge(b,a,z)=='.'){var (x1,y1,x2,y2)=Topology.Adjacent(a,z);if(b.Rows[y1][x1]!='.'&&b.Rows[y2][x2]!='.')throw new RouteFailure("undrawable-edge");}
 if(b.HasTopology&&Topology.Edge(b,a,z)=='T')throw new RouteFailure("existing-trail");
 if(active){var key=a.X<z.X||a.X==z.X&&a.Y<z.Y?(a,z):(z,a);if(!seen.Add(key))throw new RouteFailure("self-trail");}
 a=z;if(active&&(b.HasTopology?Topology.Border(b,z):Enumerable.Range(-1,2).Any(i=>Enumerable.Range(-1,2).Any(j=>b.Claimed(z.X+i,z.Y+j))))){output.Add(z);return output.ToArray();}
 }output.Add(target);}
 throw new RouteFailure("route-not-closed");
 }
 public static bool BodySafe(Board b,Point start,Point[] corners,double delay=0){
 var points=new[]{start}.Concat(corners).Select(p=>((p.X-b.Width/2d)*b.CellSize,(p.Y-b.Height/2d)*b.CellSize)).ToArray();
 double distance=Distance(new[]{start}.Concat(corners))*b.CellSize,duration=Travel(b.Player,distance,delay),speed=b.Player.Speed,bonus=b.Player.SuperBonus*(1-b.Player.SlowStacks*.1),remain=Math.Max(0,b.Player.SuperRemaining-delay);
 for(double t=0;t<=duration;t+=.1){double d=speed*Math.Min(t,remain)+Math.Max(0,t-remain)*(speed-bonus);var pos=points[^1];
 for(int i=1;i<points.Length;i++){var a=points[i-1];var z=points[i];double len=Math.Abs(a.Item1-z.Item1)+Math.Abs(a.Item2-z.Item2);if(d<=len){double f=d/Math.Max(.001,len);pos=(a.Item1+(z.Item1-a.Item1)*f,a.Item2+(z.Item2-a.Item2)*f);break;}d-=len;}
 foreach(var body in b.Bodies){double moving=Math.Max(0,delay+t-Math.Max(b.Enemy.ControlRemaining,body.FrozenRemaining)),known=Math.Min(moving,body.MotionHorizon),x=body.Position[0]+body.MotionVelocity[0]*known,y=body.Position[1]+body.MotionVelocity[1]*known,r=body.Radius+b.Player.Radius+8+Math.Max(body.SpeedBound,Math.Sqrt(body.Velocity.Sum(v=>v*v)))*Math.Max(0,moving-known);if(Math.Sqrt(Math.Pow(pos.Item1-x,2)+Math.Pow(pos.Item2-y,2))<r)return false;}
 }return true;
 }
 public static IEnumerable<int> Lengths(int maximum)=>new[]{2,4,8,12,20,30,40,50,65,80,100,130,170,254,maximum}.Select(x=>Math.Min(x,maximum)).Where(x=>x>0).Distinct().Order();
 public static bool Drawable(Board b,Point a,Point z){var (x1,y1,x2,y2)=Topology.Adjacent(a,z);return x1>=0&&x2>=0&&y1>=0&&y2>=0&&x1<b.Width&&x2<b.Width&&y1<b.Height&&y2<b.Height&&(b.Rows[y1][x1]=='.'||b.Rows[y2][x2]=='.');}
 public static bool SafeEdge(Board b,Point a,Point z){if(b.HasTopology)return Topology.Safe(b,a,z);if(a.X!=z.X){int x=Math.Min(a.X,z.X);return b.Claimed(x,a.Y)!=b.Claimed(x,a.Y-1);}int y=Math.Min(a.Y,z.Y);return b.Claimed(a.X,y)!=b.Claimed(a.X-1,y);}
 public static Dictionary<Point,Point?> Boundary(Board b){
 if(b.HasTopology){
  var previous=new Dictionary<Point,Point?>{[b.Player.Point]=null};var costs=new Dictionary<Point,int>{[b.Player.Point]=0};var frontier=new PriorityQueue<Point,int>();frontier.Enqueue(b.Player.Point,0);var exits=new Dictionary<Point,bool>();
  bool HasExit(Point p){if(exits.TryGetValue(p,out bool yes))return yes;yes=Topology.Directions.Any(d=>{var z=new Point(p.X+d.X,p.Y+d.Y);return z.X>=1&&z.Y>=1&&z.X<b.Width&&z.Y<b.Height&&Topology.Edge(b,p,z)=='.'&&Drawable(b,p,z);});exits[p]=yes;return yes;}
  while(frontier.TryDequeue(out var a,out var cost)){if(cost!=costs[a])continue;foreach(var d in Topology.Directions){var z=new Point(a.X+d.X,a.Y+d.Y);if(z.X<1||z.Y<1||z.X>=b.Width||z.Y>=b.Height||!SafeEdge(b,a,z))continue;
   // Prefer the inner side of a claimed rim where drawing can escape a pursuer.
   // Both remain native safe edges; this only resolves path choice, not collision rules.
   int next=cost+4+(HasExit(z)?0:2);if(costs.TryGetValue(z,out int old)&&old<=next)continue;costs[z]=next;previous[z]=a;frontier.Enqueue(z,next);
  }}return previous;
 }
 var queue=new Queue<Point>();var seen=new Dictionary<Point,Point?>{[b.Player.Point]=null};queue.Enqueue(b.Player.Point);
 while(queue.TryDequeue(out var a))foreach(var d in new[]{new Point(1,0),new Point(-1,0),new Point(0,1),new Point(0,-1)}){var z=new Point(a.X+d.X,a.Y+d.Y);if(seen.ContainsKey(z)||z.X<1||z.Y<1||z.X>=b.Width||z.Y>=b.Height||!SafeEdge(b,a,z))continue;seen[z]=a;queue.Enqueue(z);}return seen;}
 public static Point[] SafePath(Board b,Point goal,Dictionary<Point,Point?>? seen=null){seen??=Boundary(b);if(!seen.ContainsKey(goal))throw new RouteFailure("no-safe-boundary-path");var path=new List<Point>();var a=goal;while(a!=b.Player.Point){path.Add(a);a=seen[a]!.Value;}path.Reverse();var nodes=new List<Point>();var previous=b.Player.Point;Point? direction=null;foreach(var p in path){var d=new Point(p.X-previous.X,p.Y-previous.Y);if(direction!=null&&d!=direction)nodes.Add(previous);previous=p;direction=d;}if(path.Count>0)nodes.Add(path[^1]);return nodes.ToArray();}
 public static (int X0,int Y0,int X1,int Y1) Bounds(IEnumerable<Point> ps){var a=ps.ToArray();return(a.Min(x=>x.X),a.Min(x=>x.Y),a.Max(x=>x.X),a.Max(x=>x.Y));}
 public static Point Axis(Board b,int side,int u,int v)=>side switch{0=>new(u,v),1=>new(b.Width-v,u),2=>new(b.Width-u,b.Height-v),3=>new(v,b.Height-u),_=>throw new ArgumentException()};
 public static Point Inverse(Board b,int side,int x,int y,bool cell=false)=>side switch{0=>new(x,y),1=>new(y,b.Width-x-(cell?1:0)),2=>new(b.Width-x-(cell?1:0),b.Height-y-(cell?1:0)),3=>new(b.Height-y-(cell?1:0),x),_=>throw new ArgumentException()};
 public static int Limit(Board b,int side)=>side%2==0?b.Width-1:b.Height-1;
 public sealed record Access(int Side,int U,int V,double Seconds,int WalkCells,int DrawCells);
 public static Access SelectAccess(Board b){var walks=Boundary(b);var choices=new List<Access>();
 for(int side=0;side<4;side++){var lows=new Dictionary<int,int>();for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.Claimed(x,y)){var p=Inverse(b,side,x,y,true);lows[p.X]=Math.Min(lows.GetValueOrDefault(p.X,int.MaxValue),p.Y);}
 foreach(var (u,v) in lows){if(u<1||u+4>Limit(b,side))continue;var start=Axis(b,side,u,v);if(!walks.ContainsKey(start))continue;int draw=0;try{if(v>1){var corners=Trim(b,start,[Axis(b,side,u,1),Axis(b,side,u+4,1),Axis(b,side,u+4,v)]);if(corners[0]!=Axis(b,side,u,1))continue;draw=(int)Distance(new[]{start}.Concat(corners));}}catch(RouteFailure){continue;}
 int walk=(int)Distance(new[]{b.Player.Point}.Concat(SafePath(b,start,walks)));double wt=Travel(b.Player,walk*b.CellSize);choices.Add(new(side,u,v,wt+Travel(b.Player,draw*b.CellSize,wt),walk,draw));}}
 return choices.OrderBy(c=>c.Seconds).ThenBy(c=>c.V).ThenBy(c=>c.U).FirstOrDefault()??throw new RouteFailure("no-reachable-edge");
 }
 public sealed record Shape(int Progress,Point[] Corners,double BaseDistance);
 public sealed record Choice(int Progress,Point[] Corners,double DrawSeconds,double WalkSeconds,double WindowSeconds,string[] Items,double SavedSeconds,double Score,bool Feasible);
 public static Choice Choose(Board b,Point start,IEnumerable<Shape> shapes,double remaining,bool area=false){
 var walk=SafePath(b,start);double wt=Travel(b.Player,Distance(new[]{b.Player.Point}.Concat(walk))*b.CellSize),window=Window(b,wt);var candidates=new List<Choice>();
 foreach(var shape in shapes){Point[] corners;try{corners=Trim(b,start,shape.Corners);}catch(RouteFailure){continue;}
 var polygon=new[]{start}.Concat(corners).Append(start).ToArray();if(area&&Math.Abs(Enumerable.Range(1,polygon.Length-1).Sum(i=>(long)polygon[i-1].X*polygon[i].Y-(long)polygon[i].X*polygon[i-1].Y))<1)continue;
 double duration=Travel(b.Player,Distance(new[]{start}.Concat(corners))*b.CellSize,wt);var bounds=Bounds(new[]{start}.Concat(shape.Corners));var items=new List<string>();double value=0;
 foreach(var item in b.Items){double x=item.Position[0]/b.CellSize+b.Width/2d,y=item.Position[1]/b.CellSize+b.Height/2d;if(!item.Collected&&x>=bounds.X0&&x<bounds.X1&&y>=bounds.Y0&&y<bounds.Y1&&item.Remaining>wt+duration+.2){value+=ItemValue(item,b.Player,remaining*b.CellSize);items.Add(item.Type);}}
 double detour=Math.Max(0,duration-Travel(b.Player,shape.BaseDistance*b.CellSize,wt));if(detour>value+.2)continue;
 candidates.Add(new(shape.Progress,corners,duration,wt,window,items.ToArray(),value,shape.Progress/(wt+duration+.15)+Math.Max(0,value-detour)*2,duration<=window&&BodySafe(b,start,corners,wt)));
 }
 return candidates.Where(c=>c.Feasible).OrderByDescending(c=>c.Score).FirstOrDefault()??candidates.OrderBy(c=>c.DrawSeconds).FirstOrDefault()??throw new RouteFailure("no-legal-candidate");
 }
 public sealed record Gap(int Side,int Lo,int Hi);
 public static Gap[] Gaps(Board b){var output=new List<Gap>();for(int side=0;side<4;side++){int? start=null;int limit=Limit(b,side);for(int u=1;u<=limit;u++){Point cell=side switch{0=>new(u,1),1=>new(b.Width-2,u),2=>new(b.Width-1-u,b.Height-2),_=>new(1,b.Height-1-u)};bool missing=u<limit&&!b.Claimed(cell.X,cell.Y);if(missing&&start==null)start=u;else if(!missing&&start!=null){output.Add(new(side,start.Value,u));start=null;}}}return output.ToArray();}
}

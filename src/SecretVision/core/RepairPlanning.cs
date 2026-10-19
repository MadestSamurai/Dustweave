namespace BD2SecretVision;
public static class RepairPlanning {
 public sealed record Plan(Point Start,Point[] Corners,double WalkSeconds,double DrawSeconds,int Cells,int RimCells,int NewEdges,bool Feasible,string Key,double Score,string Purpose="repair",int ObjectiveProgress=0,double ItemSavedSeconds=0);
 sealed record Candidate(Point Start,Point[] Corners,double Walk,double Draw,int Progress,bool Feasible,string Key,string Purpose);
 public static int RimDistance(Board b,Point p)=>Math.Min(Math.Min(p.X-1,b.Width-1-p.X),Math.Min(p.Y-1,b.Height-1-p.Y));
 public static Plan? Choose(Board b,Func<string,bool>? excluded=null,bool allowPickup=true){
  var walks=Planning.Boundary(b);var topology=new Topology(b);var gaps=Planning.Gaps(b);var candidates=new Dictionary<string,Candidate>();
  int distance=walks.Keys.Min(p=>RimDistance(b,p));bool access=distance>0;
  double remainingDistance=b.CellSize*(2d*distance+2*gaps.Sum(g=>g.Hi-g.Lo));
  void Add(Point start,Point[] raw,string purpose){
   if(!walks.ContainsKey(start))return;
   try{
    var corners=Planning.Trim(b,start,raw);var key=Topology.RouteKey(start,corners);if(candidates.ContainsKey(key)||excluded?.Invoke(key)==true)return;
    int progress=access?distance-corners.Min(p=>RimDistance(b,p)):0;
    if(purpose=="connect-rim"&&progress<=0)return;
    if(purpose=="advance-rim"){
     var touched=new HashSet<Point>();var a=start;
     foreach(var z in Topology.Expand(start,corners).Skip(1)){var (x1,y1,x2,y2)=Topology.Adjacent(a,z);foreach(var p in new[]{new Point(x1,y1),new Point(x2,y2)})if((p.X==1||p.X==b.Width-2||p.Y==1||p.Y==b.Height-2)&&b.Rows[p.Y][p.X]=='.')touched.Add(p);a=z;}
     progress=touched.Count;if(progress==0)return;
    }
    double wt=Planning.Travel(b.Player,Planning.Distance(Planning.SafePath(b,start,walks).Prepend(b.Player.Point))*b.CellSize),dt=Planning.Travel(b.Player,Planning.Distance(corners.Prepend(start))*b.CellSize,wt);
    candidates[key]=new(start,corners,wt,dt,progress,dt<=Planning.Window(b,wt)&&Planning.BodySafe(b,start,corners,wt),key,purpose);
   }catch(RouteFailure){}
  }
  // Strategic routes are always generated before optional pickups. A tiny central capture
  // must never suppress the connection to the outer rim.
  if(access){
   for(int side=0;side<4;side++){
    var sources=walks.Keys.Select(p=>(Point:p,Axis:Planning.Inverse(b,side,p.X,p.Y))).Where(p=>p.Axis.Y>1).OrderBy(p=>p.Axis.Y+Planning.Distance(Planning.SafePath(b,p.Point,walks).Prepend(b.Player.Point))*.5).Take(8);
    foreach(var s in sources)foreach(int width in new[]{2,4,8})foreach(int sign in new[]{1,-1})foreach(int length in Planning.Lengths(s.Axis.Y-1).OrderDescending()){
     int u=s.Axis.X,v=s.Axis.Y,other=u+sign*width,end=v-length;
     Add(s.Point,[Planning.Axis(b,side,u,end),Planning.Axis(b,side,other,end),Planning.Axis(b,side,other,v)],"connect-rim");
     Add(s.Point,[Planning.Axis(b,side,other,v),Planning.Axis(b,side,other,end),Planning.Axis(b,side,u,end),s.Point],"connect-rim");
    }
   }
  }else{
   foreach(var gap in gaps){
    var endpoints=new List<int>{gap.Lo,gap.Hi};
    endpoints.AddRange(walks.Keys.Select(p=>Planning.Inverse(b,gap.Side,p.X,p.Y)).Where(p=>p.Y==1&&p.X>=gap.Lo&&p.X<=gap.Hi).OrderBy(p=>Math.Abs(p.X-Planning.Inverse(b,gap.Side,b.Player.X,b.Player.Y).X)).Take(2).Select(p=>p.X));
    foreach(int origin in endpoints.Distinct())foreach(int sign in new[]{1,-1}){
     int maximum=sign>0?gap.Hi-origin:origin-gap.Lo;if(maximum<=0)continue;var start=Planning.Axis(b,gap.Side,origin,1);if(!walks.ContainsKey(start))continue;
     foreach(int length in Planning.Lengths(maximum).Append(1).Distinct()){
      int end=origin+sign*length;Add(start,[Planning.Axis(b,gap.Side,end,1)],"advance-rim");
      foreach(int depth in new[]{1,2,4,8,12,20,32,48}){
       Add(start,[Planning.Axis(b,gap.Side,origin,depth+1),Planning.Axis(b,gap.Side,end,depth+1),Planning.Axis(b,gap.Side,end,1)],"advance-rim");
       Add(start,[Planning.Axis(b,gap.Side,end,1),Planning.Axis(b,gap.Side,end,depth+1),Planning.Axis(b,gap.Side,origin,depth+1),start],"advance-rim");
      }
     }
    }
   }
  }
  var items=b.Items.Where(i=>!i.Collected&&i.Remaining>.3&&Planning.ItemValue(i,b.Player,remainingDistance)>0).ToArray();
  if(allowPickup)foreach(var item in items){
   int ix=(int)Math.Floor(item.Position[0]/b.CellSize+b.Width/2d),iy=(int)Math.Floor(item.Position[1]/b.CellSize+b.Height/2d);
   foreach(var start in walks.Keys.OrderBy(p=>Math.Abs(p.X-ix)+Math.Abs(p.Y-iy)).Take(8)){
    int x=ix>=start.X?ix+1:ix,y=iy>=start.Y?iy+1:iy;
    Add(start,[new(x,start.Y),new(x,y),new(start.X,y),start],"pickup");
    Add(start,[new(start.X,y),new(x,y),new(x,start.Y),start],"pickup");
   }
  }
  // Shortlist by strategic progress, never by small local area divided by short time.
  var strategic=candidates.Values.Where(c=>c.Purpose!="pickup").OrderByDescending(c=>c.Feasible).ThenByDescending(c=>c.Progress/(c.Walk+c.Draw+.3)).ToArray();
  var shortlist=strategic.Take(40).Concat(strategic.GroupBy(c=>c.Start).SelectMany(g=>g.OrderBy(c=>c.Draw).ThenBy(c=>c.Walk).Take(2)).Take(32)).Concat(candidates.Values.Where(c=>c.Purpose=="pickup").OrderBy(c=>c.Walk+c.Draw).Take(16)).DistinctBy(c=>c.Key);
  int empty=b.Rows.Sum(r=>r.Count(c=>c=='.'));
  Plan? best=null,pickup=null;
  foreach(var c in shortlist){var gain=topology.Predict(c.Start,c.Corners,items.Length>0);if(gain.NewEdges==0)continue;
   int progress=access?c.Progress:gain.RimCells;string purpose=c.Purpose;
   // Retained rim edges can require a zero-cell connector before the last closing stroke.
   if(!access&&progress==0)progress=c.Progress;
   double saved=0;foreach(var item in items){int ix=(int)Math.Floor(item.Position[0]/b.CellSize+b.Width/2d),iy=(int)Math.Floor(item.Position[1]/b.CellSize+b.Height/2d);if(item.Remaining>c.Walk+c.Draw+.3&&gain.Filled.Contains(iy*b.Width+ix))saved+=Planning.ItemValue(item,b.Player,remainingDistance);}
   double cost=c.Walk+c.Draw+.3,score=progress/cost;
   // A final closure completing most of the remaining map dominates minor rim repairs.
   if(!access&&gain.Cells>=empty*.8)score+=100;
   var plan=new Plan(c.Start,c.Corners,c.Walk,c.Draw,gain.Cells,gain.RimCells,gain.NewEdges,c.Feasible,c.Key,score,purpose,progress,saved);
   if(purpose=="pickup"){
    // Pay for all travel and drawing before crediting the future speed benefit.
    // The caller requires a strategic advance before another optional pickup.
    if(c.Feasible&&saved>cost+.5&&(pickup==null||saved-cost>pickup.ItemSavedSeconds-pickup.WalkSeconds-pickup.DrawSeconds-.3))pickup=plan;
   }else if(progress>0&&(best==null||(plan.Feasible&&!best.Feasible)||(plan.Feasible==best.Feasible&&(plan.Feasible?plan.Score>best.Score:plan.DrawSeconds<best.DrawSeconds-1e-6||Math.Abs(plan.DrawSeconds-best.DrawSeconds)<1e-6&&plan.WalkSeconds<best.WalkSeconds))))best=plan;
  }
  // Do not leave a useful controlled-boss window for a discretionary detour.
  if(pickup!=null&&b.Enemy.CarveBlockedRemaining<=0&&b.Enemy.ControlRemaining<=0&&
    (best==null||pickup.ItemSavedSeconds>pickup.WalkSeconds+pickup.DrawSeconds+.8+best.ItemSavedSeconds))return pickup;
  return best;
 }
}

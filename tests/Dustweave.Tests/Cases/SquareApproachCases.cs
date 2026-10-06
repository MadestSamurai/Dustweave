using BD2Daily.Live;
using BD2Territory;

static class SquareApproachCases
{
 public static Task Run(string root,List<string> cases)
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
  static double Radius(RoutePoint p)=>Math.Sqrt(p.X*p.X+p.Z*p.Z);
  var center=new RoutePoint(0,0,0);var from=new RoutePoint(5,0,0);
  RoutePoint? Floor(RoutePoint p)=>Radius(p)<.7?null:p;
  bool Circle(RoutePoint p)=>Radius(p)<3;
  var approach=new SquareApproach(center);
  for(int i=0;i<7;i++)
  {
   var stands=SquareApproach.InteractionPoints(approach.Anchor,from,3,Floor,Circle);
   approach.SelectStand(stands[0]);from=approach.Stand;
   Check(approach.Anchor.X==0&&approach.Anchor.Z==0&&Radius(approach.Stand)<3,"replan retains merchant anchor "+i);
  }
  var goals=SquareApproach.InteractionPoints(center,new RoutePoint(5,0,0),3,Floor,Circle);
  Check(goals.Length==32&&goals.All(p=>Radius(p)>.7&&Circle(p)),"interaction domain is distinct from solid body");
  Check(goals.Any(p=>p.X < -2)&&goals.Any(p=>p.X>2)&&goals.Any(p=>p.Z < -2)&&goals.Any(p=>p.Z>2),"valid stands retain every approach side");
  Check(goals.All(p=>Radius(p)>1.1),"native reach is not forced into old one metre ring");
  bool Edge(RoutePoint a,RoutePoint b)
  {
   double dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz;
   double t=len<1e-12?0:Math.Clamp(-(a.X*dx+a.Z*dz)/len,0,1);
   return Radius(new RoutePoint(a.X+dx*t,0,a.Z+dz*t))>=.7;
  }
  RoutePoint[] Solve(RoutePoint start,RoutePoint[] destinations)
  {
   var search=new LocalRouteSearch(start,destinations,Floor,Edge,.3,6,12000);
   for(int i=0;i<200&&search.State==RouteSearchState.Searching;i++)search.Step(256,100);
   Check(search.State==RouteSearchState.Found,"A* finds native interaction stand");
   Check(search.Path.Zip(search.Path.Skip(1),(a,b)=>Edge(a,b)).All(x=>x),"A* retains physical NPC collision");
   return search.Path;
  }
  var path=Solve(new RoutePoint(5,0,0),goals);
  Check(Circle(path[^1])&&path[^1].X>2,"A* enters interaction region without entering solid body");
  bool Sector(RoutePoint p)=>Circle(p)&&p.X<0&&Math.Abs(p.Z)<=-p.X*Math.Tan(Math.PI/8);
  var front=SquareApproach.InteractionPoints(center,new RoutePoint(5,0,0),3,Floor,Sector);
  Check(front.Length>0&&front.All(Sector),"merchant back side is excluded by native sector predicate");
  Check(Sector(Solve(new RoutePoint(5,0,0),front)[^1]),"opposite side start routes around real body to merchant front");
  var rear=SquareApproach.InteractionPoints(center,new RoutePoint(5,0,0),3,p=>p.X>0?null:Floor(p),Circle);
  Check(rear.Length>0&&rear.All(p=>p.X<=0),"far side candidates survive blocked nearest approaches");
  foreach(double invalid in new[]{0,-1,double.NaN,double.PositiveInfinity})
   Check(SquareApproach.InteractionPoints(center,from,invalid,Floor,Circle).Length==0,"invalid native reach is rejected: "+invalid);
  Check(SquareApproach.InteractionPoints(center,from,3,p=>new RoutePoint(p.X,5,p.Z),p=>p.X*p.X+p.Y*p.Y+p.Z*p.Z<10).Length==0,"native range is checked after ground projection");
  var close=SquareApproach.InteractionPoints(center,new RoutePoint(5,0,0),3,Floor,Circle,true);
  Check(close.Length==32&&close.All(p=>Radius(p)>=.7&&Radius(p)<.8),"merchant chooses inner clear stands rather than interaction boundary");
  Check(Radius(Solve(new RoutePoint(5,0,0),close)[^1])<.8,"A* reaches a close merchant stand without entering the body");
  var wideBody=SquareApproach.InteractionPoints(center,from,3,p=>Radius(p)<1.4?null:p,Circle,true);
  Check(wideBody.Length>0&&wideBody.All(p=>Radius(p)>=1.4),"large physical bodies move close stands outward safely");
  var status=new System.Text.Json.Nodes.JsonObject{["SquareNavigation"]=new System.Text.Json.Nodes.JsonObject{["Kind"]="square_shop_nav",["State"]="moving"}};
  Check(!BD2Daily.DailySquareNavigation.MerchantArrived(status,true),"native near does not end close approach prematurely");
  status["SquareNavigation"]!["State"]="arrived";
  Check(BD2Daily.DailySquareNavigation.MerchantArrived(status,true),"close arrival and native proximity permit shop interaction");
  Check(!BD2Daily.DailySquareNavigation.MerchantArrived(status,false),"close arrival still requires native proximity");
  status["SquareNavigation"]!["Kind"]="square_route_probe";
  Check(!BD2Daily.DailySquareNavigation.MerchantArrived(status,true),"unrelated route arrival cannot satisfy merchant approach");
  return Task.CompletedTask;
 }
}

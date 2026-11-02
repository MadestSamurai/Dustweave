using BD2Daily.Live;

static class FieldAvoidanceCases
{
    public static void Run(List<string> cases)
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add(name);}
        static FieldPoint P(double x,double z,double y=0)=>new(x,y,z);
        static FieldPoint[] Line(FieldPoint a,FieldPoint b)=>[a,b];
        static FieldPoint? Ground(FieldPoint p)=>p;
        var random=new Random(8741);
        for(int i=0;i<140;i++)
        {
            double angle=random.NextDouble()*2*Math.PI,radius=.55+random.NextDouble()*1.5,span=radius+2+random.NextDouble()*5;
            var center=P(random.NextDouble()*10-5,random.NextDouble()*10-5);
            var vector=P(Math.Cos(angle)*span,Math.Sin(angle)*span);
            var start=center-vector;var goal=center+vector;
            FieldExclusion[] hazards=[new(){Id=i,Center=center,Radius=radius}];
            var plan=FieldAvoidancePlanner.Plan(start,goal,hazards,Line,Ground);
            Check(plan.Found&&FieldAvoidancePlanner.Clear(plan.Points,hazards)&&plan.Queries<=256&&
                FieldPoint.Distance(plan.Points[^1],goal)<1e-6&&plan.Length<2*span+4*radius,
                "elite detour preserves reachable goal and short safe path "+i);
            var straight=FieldAvoidancePlanner.Plan(start,goal,[new(){Center=center+P(0,0,5),Radius=radius}],Line,Ground);
            Check(straight.Found&&straight.Queries==1&&Math.Abs(straight.Length-2*span)<1e-6,"other floor does not cause elite detour "+i);
        }
        for(int i=0;i<80;i++)
        {
            double speed=1+i*.11,offset=1+i*.04,t=offset/speed;
            Check(FieldAvoidancePlanner.Crossing(P(-offset,0),P(speed,0),P(0,-offset),P(0,speed),.8,1,t+.1),"predict crossing cart "+i);
            Check(!FieldAvoidancePlanner.Crossing(P(-offset,0),P(speed,0),P(0,-offset,4),P(0,speed),.8,1,t+.1),"cart on different floor is ignored "+i);
            Check(!FieldAvoidancePlanner.Crossing(P(-offset,0),P(-speed,0),P(0,-offset),P(0,-speed),.4,1,t),"diverging cart does not cause yielding "+i);
        }
        FieldExclusion[] h=[new(){Center=P(0,0),Radius=1}];
        Check(FieldAvoidancePlanner.Clear([P(.5,0),P(2,0)],h),"outward escape allowed from moving contact margin");
        Check(!FieldAvoidancePlanner.Clear([P(.5,0),P(-2,0)],h),"escape cannot pass through monster");
        Check(!FieldAvoidancePlanner.Plan(P(-3,0),P(0,0),h,Line,Ground).Found,"occupied endpoint distinguished from inaccessible map");
        Check(!FieldAvoidancePlanner.Plan(P(-3,0),P(3,0),h,(a,b)=>[a,P(0,0)],Ground).Found,"incomplete native path cannot masquerade as arrival");
        var blocked=FieldAvoidancePlanner.Plan(P(-3,0),P(3,0),h,Line,_=>null);
        Check(!blocked.Found&&blocked.Reason=="local_detour_unavailable","unprojectable local ring is explicit without banning map");
        var budget=FieldAvoidancePlanner.Plan(P(-3,0),P(3,0),h,Line,Ground,1);
        Check(!budget.Found&&budget.Queries==1&&budget.Reason=="query_budget","detour search has bounded work");
        Check(FieldAvoidancePlanner.Crossing(P(0,0,3),P(0,0,-3),P(0,0),P(0,0),1,1,1),"vertical overlap interval considered for ramps");
        Check(!FieldAvoidancePlanner.Crossing(P(0,0),P(0,0),P(0,0),P(0,0),double.NaN,1,1),"invalid cart radius rejected");
        Check(FieldAvoidancePlanner.Plan(P(0,0),P(10,0),[],Line,Ground).Queries==1,"safe map retains native path with one query");
        // Two local hazards on opposite sides of a long wall. Euclidean A*
        // repeatedly expands the wrong side; native distances include the gap.
        FieldPoint[] WallRoute(FieldPoint a,FieldPoint b)=>a.X<5!=b.X<5
            ? [a,P(a.X,30),P(b.X,30),b] : [a,b];
        FieldExclusion[] wallHazards=[new(){Center=P(0,10),Radius=.9},new(){Center=P(10,15),Radius=.9}];
        foreach(var ends in new[]{(P(0,0),P(10,0)),(P(10,0),P(0,0))})
        {
            var detour=FieldAvoidancePlanner.Plan(ends.Item1,ends.Item2,wallHazards,WallRoute,Ground);
            Check(detour.Found&&detour.Queries<=256&&FieldAvoidancePlanner.Clear(detour.Points,wallHazards),"native-distance search resolves multiple hazards across a long wall");
        }
    }
}

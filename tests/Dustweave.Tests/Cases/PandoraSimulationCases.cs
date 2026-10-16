using Dustweave.Pandora;
using System.Diagnostics;
using System.Text.Json;

internal static class PandoraSimulationCases
{
    public static void Run(string output, List<string> cases)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); cases.Add("pandora-simulation: " + message); }
        Check(new Control().ForecastMovement,"challenge prediction is enabled in the native control defaults");
        CombatBody Enemy(double x, double z, double speed=0, double hp=100) => new() { Id=1, Position=new(x,z), Speed=speed, Hp=hp, Damage=100, Radius=.4, Known=true };
        CombatFrame Frame(params CombatBody[] enemies) { var f=new CombatFrame { Hp=700, PlayerRadius=.4, ErrorRadius=0, Enemies=enemies }; CombatSimulation.Prepare(f); return f; }
        CombatForecast Sim(CombatFrame f, Point dir=default, double duration=1.8) => CombatSimulation.Run(f,dir,dir,_=>5,_=>true,duration);
        var contact=Frame(Enemy(0,0)); contact.Defence=20;
        Check(Sim(contact,duration:.9).Damage==80 && Sim(contact,duration:1.8).Damage==160,"contact uses per-enemy damage deadlines and integer defence");
        contact.InvulnerableFor=2; Check(Sim(contact).Damage==0,"native invulnerability prevents contact damage");
        contact.InvulnerableFor=.04;Check(Sim(contact,duration:.075).Damage==80,"an immunity expiring inside a simulation step cannot hide the next contact");
        contact.InvulnerableFor=0; contact.Enemies[0].StunFor=2; Check(Sim(contact).Damage==0,"stun blocks movement and contact attacks");
        contact.Enemies[0].StunFor=0; contact.Enemies[0].AttackAfter=2; Check(Sim(contact).Damage==0,"an overlapping enemy on cooldown cannot damage early");
        var fast=Frame(Enemy(-4,0,8));
        Check(Sim(fast,new(1,0)).Damage>0,"faster pursuit closes on a fleeing player instead of preserving cached velocity forever");
        Check(Sim(fast,new(0,1)).FirstHit > Sim(fast,new(-1,0)).FirstHit,"crossing into an approaching enemy is worse than moving across its path");
        var field=Frame(Enemy(2,0,2));
        field.Attacks=[new(){Id=1, Known=true, Position=new(),Radius=4,Damage=150,After=.1,Interval=.3,Lifetime=3,Motion=AttackMotion.Attached}];
        Check(Sim(field).Damage==0 && Sim(field).Kills==1,"a ready persistent field kills before contact");
        field.Attacks[0].After=1.5; Check(Sim(field).Damage>0,"a late field tick cannot retroactively erase contact damage");
        field.Attacks[0].Known=false; Check(Sim(field).Kills==0,"unknown attacks never become guaranteed future kills");
        var pierce=Frame(Enemy(3,0),Enemy(1,0)); pierce.Player=new(50,50);
        pierce.Attacks=[new(){Known=true,Position=new(),Velocity=new(20,0),Radius=.4,Damage=150,Lifetime=2,Pierce=1,Motion=AttackMotion.Linear}];
        var shot=Sim(pierce); Check(shot.Kills==1,"one-pierce shot cannot clear two enemies");
        var shotPositions=CombatSimulation.Run(pierce,new(),new(),_=>0,_=>true,.2,useDamage:true,retainPositions:true);
        Check(shotPositions.Kills==1,"fast projectiles sweep between samples");
        pierce.Attacks[0].Lifetime=.01; Check(Sim(pierce).Kills==0,"a projectile expiring before a distant collision earns no kill");
        var boxSweep=Frame(Enemy(1,0));boxSweep.Player=new(50,50);
        boxSweep.Attacks=[new(){Known=true,Position=new(),Velocity=new(40,0),Box=true,Axis=new(1,0),HalfLength=.15,HalfWidth=.2,Damage=150,Lifetime=2,Pierce=1,Motion=AttackMotion.Linear}];
        Check(Sim(boxSweep,duration:.1).Kills==1,"a thin rectangular projectile crossing between samples still hits");
        var orbitFrame=Frame(Enemy(0,2));orbitFrame.Player=new(20,20);
        orbitFrame.Attacks=[new(){Known=true,Position=new(2,0),OrbitOffset=new(2,0),AngularSpeed=Math.PI,Radius=.35,Damage=150,Lifetime=1,Motion=AttackMotion.Orbit}];
        Check(Sim(orbitFrame,duration:.6).Kills==1,"an existing orbit follows its angular phase rather than a straight velocity");
        orbitFrame.Attacks[0].Lifetime=.15;Check(Sim(orbitFrame,duration:.6).Kills==0,"an expired orbit cannot supply a future kill");
        var initial=Frame(Enemy(0,0)); initial.Player=new(50,50); initial.Attacks=[new(){Known=true,Position=new(),Radius=3,Damage=150,Lifetime=2,Motion=AttackMotion.Fixed}];
        Check(Sim(initial).Kills==0,"existing on-enter overlap is not credited a second time");
        var blocked=CombatSimulation.Run(Frame(),new(1,0),new(1,0),_=>5,p=>p.X<1);
        Check(blocked.Blocked,"branches crossing arena boundaries are rejected");
        var rim=Frame();rim.Player=new(9.95,0);
        var reenter=CombatSimulation.Run(rim,new(-1,0),new(-1,0),_=>5,p=>Math.Abs(p.X)<=10&&Math.Abs(p.Z)<=10);
        Check(!reenter.Blocked,"a player already overlapping the conservative boundary can move back inside");
        double Depth(Func<Point,bool> clear,double radius=.4)=>CombatSimulation.OpenDepth(new(),new(1,0),5,radius,clear);
        Check(Depth(_=>true)==5 && Depth(p=>p.X<1.05 || p.X>1.1)<1,
            "an empty endpoint beyond a thin wall cannot be counted as an escape corridor");
        Check(Depth(p=>p.X<1 || p.Z<.2,.1)==5 && Depth(p=>p.X<1 || p.Z<.2,.45)<1,
            "escape corridors must fit the whole player, not just its centre");
        bool SpaceArena(Point p)=>Math.Abs(p.X)<10&&Math.Abs(p.Z)<10;
        double SpaceAt(Point p){var f=Frame();f.Player=p;return CombatSimulation.Run(f,new(),new(),_=>5,SpaceArena,.1).EscapeSpace;}
        Check(SpaceAt(new())>SpaceAt(new(9,0)) && SpaceAt(new(9,0))>SpaceAt(new(9,9)),
            "empty walls and corners reduce onward capacity even without a single monster");
        var pocket=CombatSimulation.Run(Frame(),new(),new(),_=>5,p=>Math.Abs(p.X)<1.2&&Math.Abs(p.Z)<1.2,.1);
        Check(pocket.EscapeSpace<1,"a shallow empty pocket is not twelve full escape directions");
        var frozenSpace=Frame(Enemy(2,0));frozenSpace.Enemies[0].StunFor=5;
        var bodySpace=Sim(frozenSpace,duration:.1).EscapeSpace;
        frozenSpace.Enemies[0].ColliderEnabled=false;
        Check(bodySpace<Sim(frozenSpace,duration:.1).EscapeSpace,"stun never creates fictitious open space through a crowd, while disabled colliders do not block exits");
        int spaceQueries=0;CombatSimulation.OpenDepth(new(),new(1,0),100,.4,p=>{spaceQueries++;return true;});
        Check(spaceQueries<=165,"geometric lookahead has a fixed probe budget even with extreme speed");
        var planner=new CombatRoutePlanner(); var routes=Enumerable.Range(0,32).Select(i=>new Route{Direction=new(Math.Cos(i*Math.PI/16),Math.Sin(i*Math.PI/16))}).ToArray();
        var corner=Frame(Enemy(4,4,2)); corner.Player=new(9,9);
        var escape=planner.Choose(corner,routes,new(1,0),_=>5,p=>Math.Abs(p.X)<10&&Math.Abs(p.Z)<10);
        Check(escape!=null && !planner.Selected!.Blocked && planner.Selected.EscapeSpace>0,"corner forecast preserves an onward escape rather than running into the wall");
        var bendFrame=Frame();var bendRoutes=new[]{new Route{Direction=new(1,0),LootGain=100},new Route{Direction=new()}};
        bool Corridor(Point p)=>p.X<4 && p.X>-.5 && (p.X>1 || Math.Abs(p.Z)<.5);
        var bendRoute=planner.Choose(bendFrame,bendRoutes,new(1,0),_=>5,Corridor);
        Check(bendRoute.Direction.X>.9 && !planner.Selected.Blocked && planner.Selected.Horizon>1,"a valid first leg survives pruning when it must turn before a wall");
        var quiet=Frame(); quiet.Time=1; planner.Reset();
        var keep=planner.Choose(quiet,routes,new(1,0),_=>5,_=>true)!.Direction; quiet.Time+=.2;
        var keepAgain=planner.Choose(quiet,routes,keep,_=>5,_=>true)!.Direction;
        Check(CombatSimulation.Dot(keep,keepAgain)>.98,"equivalent safe routes retain heading");
        var urgent=Frame(Enemy(.9,0,3)); urgent.Time=1.3;
        var urgentRoute=planner.Choose(urgent,routes,keep,_=>5,_=>true)!;
        Check(CombatSimulation.Dot(urgentRoute.Direction,keep)<.5,"a new collision overrides heading retention immediately");
        var guard=new CombatSafety();var heavy=Frame(Enemy(1.5,0,3));heavy.Enemies[0].Damage=600;
        var replacement=guard.Choose(heavy,new(){Direction=new(1,0)},routes,new(1,0),_=>5,_=>true);
        Check(guard.Selected.Damage==0 && replacement.Direction.X<.5,"a stale route toward a heavy contact is replaced with a current safe exit");
        var unchanged=guard.Choose(quiet,new(){Direction=new(1,0)},routes,new(1,0),_=>5,_=>true);
        Check(guard.Evaluations==1 && unchanged.Direction.X==1,"safe advice avoids a redundant all-direction sweep");
        var cooldownGuard=Frame(Enemy(1,0));cooldownGuard.Enemies[0].AttackAfter=1;
        guard.Choose(cooldownGuard,new(){Direction=new(1,0)},routes,new(1,0),_=>5,_=>true);
        Check(guard.Evaluations==1,"contact on a native cooldown does not create a false emergency");
        var exchange=Frame(Enemy(1,0),Enemy(-3,0),Enemy(-5,0),Enemy(-7,0));
        foreach(var e in exchange.Enemies){e.Damage=195;e.AttackInterval=10;}
        var exits=new[]{new Route{Direction=new(1,0)},new Route{Direction=new(-1,0)}};
        bool Narrow(Point p)=>Math.Abs(p.Z)<.55;
        planner.Reset();var trade=planner.Choose(exchange,exits,new(),_=>5,Narrow);
        Check(trade.Direction.X>0 && planner.Selected.Damage==195 && planner.Selected.RemainingHp==505,"one affordable contact beats a corridor with three later contacts");
        exchange.Hp=150;planner.Reset();trade=planner.Choose(exchange,exits,new(),_=>5,Narrow);
        Check(trade.Direction.X<0,"the same contact route becomes unacceptable when its first hit is lethal");
        var heal=Frame(Enemy(1,0));heal.Enemies[0].Damage=195;heal.Enemies[0].AttackInterval=10;heal.Hp=300;heal.MaxHp=700;
        heal.Healing=[new(){Id=1,Position=new(2,0),Radius=.7,Amount=350,Speed=20}];
        var recovered=Sim(heal,new(1,0));
        Check(recovered.Damage==195 && recovered.Healing==350 && recovered.RemainingHp==455 && recovered.MinimumHp==105,"reachable recovery is applied after contact and preserves the minimum HP along the route");
        planner.Reset();var rescue=planner.Choose(heal,exits,new(),_=>5,Narrow);
        Check(rescue.Direction.X>0 && planner.Selected.RemainingHp>heal.Hp,"an affordable route to healing can outrank moving away without recovery");
        heal.Hp=150;var deadBeforeHeal=Sim(heal,new(1,0));
        Check(deadBeforeHeal.RemainingHp==0 && deadBeforeHeal.Healing==0 && double.IsFinite(deadBeforeHeal.LethalAt),"healing beyond a lethal contact never resurrects a forecast");
        var overflow=Frame();overflow.Hp=650;overflow.MaxHp=700;overflow.Healing=[new(){Position=new(),Radius=1,Amount=350,Speed=20}];
        var capped=Sim(overflow);Check(capped.Healing==50 && capped.RemainingHp==700,"healing caps at maximum HP and a pickup is consumed only once");
        overflow.Healing[0].Position=new(20,0);Check(Sim(overflow).Healing==0,"visible but unreachable healing supplies no HP credit");
        overflow.Healing[0].Position=new(2,0);overflow.Healing[0].Lifetime=.05;
        Check(Sim(overflow,new(1,0)).Healing==0,"a pickup expiring before arrival supplies no HP credit");
        var chasingHeal=Frame();chasingHeal.Hp=200;chasingHeal.MaxHp=700;
        chasingHeal.Healing=[new(){Position=new(-1,0),Radius=.8,Amount=400,Speed=8,Following=true}];
        var tooFast=CombatSimulation.Run(chasingHeal,new(1,0),new(1,0),_=>9,_=>true);
        Check(tooFast.Healing==0 && Sim(chasingHeal).Healing==400,"a following heal is not applied while the player outruns it");
        var hostileShot=Frame(new CombatBody{Id=1,Position=new(5,0),Velocity=new(-10,0),Known=true,Projectile=true,Damage=300,Radius=.2,Lifetime=2});hostileShot.Defence=50;
        Check(Sim(hostileShot).Damage==250,"enemy projectiles use their own attack value and player defence");
        hostileShot.Enemies[0].Lifetime=.3;
        Check(Sim(hostileShot).Damage==0,"an enemy projectile expiring before arrival cannot cause future damage");
        var frozenCrowd=Frame(Enemy(2,0,3,10000),Enemy(3,1,3,10000),Enemy(3,-1,3,10000));
        foreach(var e in frozenCrowd.Enemies){e.StunFor=5;e.Damage=300;}
        Check(Sim(frozenCrowd,new(1,0)).ControlDamage==0,"early challenge movement keeps its established control evaluation");
        frozenCrowd.PreserveControlSpace=true;
        var intoFrozen=Sim(frozenCrowd,new(1,0));
        Check(intoFrozen.Damage==0 && intoFrozen.RemainingHp==700 && intoFrozen.ControlDamage>0,"a stun keeps the true HP forecast intact without making an unsafe posture safe");
        var frozenRoutes=new[]{new Route{Direction=new(1,0),LootGain=200},new Route{Direction=new(-1,0)}};
        planner.Reset();var frozenExit=planner.Choose(frozenCrowd,frozenRoutes,new(1,0),_=>5,_=>true);
        Check(frozenExit.Direction.X<0 && planner.Selected.ControlDamage==0,"loot inside a stunned crowd cannot outrank an open retreat");
        var closeFrozen=Frame(Enemy(1.5,0,3,10000));closeFrozen.PreserveControlSpace=true;closeFrozen.Enemies[0].StunFor=5;closeFrozen.Enemies[0].Damage=600;
        var frozenGuard=guard.Choose(closeFrozen,new(){Direction=new(1,0)},routes,new(1,0),_=>5,_=>true);
        Check(guard.Selected.ControlDamage==0 && Planner.SegmentDistance(closeFrozen.Enemies[0].Position,new(),new(frozenGuard.Direction.X*4.5,frozenGuard.Direction.Z*4.5))>.8,"fresh validation rejects moving into a stunned heavy attacker even before it can hit");
        var safeWait=Frame(Enemy(3,0,10,10000));safeWait.PreserveControlSpace=true;safeWait.Enemies[0].StunFor=5;
        Check(Sim(safeWait).Damage==0 && Sim(safeWait).ControlDamage==0,"a real stun preserves a safe waiting position without inventing pursuit");
        var insideFrozen=Frame(Enemy(.3,0,3,10000));insideFrozen.PreserveControlSpace=true;insideFrozen.Enemies[0].StunFor=5;
        Check(Sim(insideFrozen,new(-1,0)).ControlDamage==0 && Sim(insideFrozen,new(1,0)).ControlDamage>0,"leaving an existing stunned overlap stays possible while entering deeper remains unsafe");
        var crossing=Frame(Enemy(1.5,0,0,10000));crossing.PreserveControlSpace=true;crossing.Enemies[0].StunFor=4;crossing.Enemies[0].Damage=650;
        crossing.ControlledExit=true;crossing.ControlTarget=new(7,0);
        Check(Sim(crossing,new(1,0)).Damage==0 && Sim(crossing,new(1,0)).ControlDamage==0,
            "a verified escape route may cross a frozen body while its real control window covers the crossing");
        crossing.ControlTarget=new(-7,0);
        Check(Sim(crossing,new(1,0)).ControlDamage>0,"moving deeper away from the escape target retains the stun posture penalty");
        crossing.ControlTarget=new(3.5,0);planner.Reset();
        var shortExit=planner.Choose(crossing,[new(){Direction=new(1,0),LootGain=200},new(){Direction=new()}],new(),_=>5,
            p=>p.X>=-1&&p.X<=4.5&&Math.Abs(p.Z)<1);
        Check(shortExit.Direction.X>.9 && !planner.Selected.Blocked && planner.Selected.Damage==0 && planner.Selected.End.X>3,
            "a short controlled opening is usable even when a full straight continuation would run into a wall");
        crossing.ControlTarget=new(7,0);crossing.Enemies[0].StunFor=.1;
        Check(Sim(crossing,new(1,0)).Damage>0,"escape mode cannot invent control after an enemy recovers");
        crossing.Enemies[0].StunFor=0;crossing.Enemies[0].Projectile=true;
        Check(Sim(crossing,new(1,0)).Damage>0,"escape control does not erase projectile damage");
        var random=new Random(12571); int scenes=300, finite=0; var milliseconds=new List<double>(); double allocated=0;
        var flow=new CombatFlow { CellSize=1,MinX=0,MinZ=0,Width=5,Height=5,Obstacles=new int[25],Initial=new Point[25] };
        Check(flow.Valid && CombatSimulation.Dot(flow.Direction(new(.5,.5),new(4.5,4.5),false),CombatSimulation.Unit(new(1,1)))>.999,"open native direction field selects the lowest four-neighbour distance with eight-way travel");
        flow.Initial[0]=new(-1,0); Check(flow.Direction(new(.5,.5),new(4.5,4.5),true).X==-1,"the existing native flow remains authoritative until its refresh deadline");
        var maze=new CombatFlow { CellSize=1,Width=5,Height=5,Obstacles=new int[25],Initial=new Point[25] };
        foreach(int z in Enumerable.Range(0,4))maze.Obstacles[z*5+2]=int.MaxValue;
        Check(maze.Direction(new(1.5,.5),new(3.5,.5),false).Z>0,"an obstacle wall redirects pursuit toward the opening rather than through the wall");
        var analytic = new CombatFlow { CellSize=1,Width=5,Height=5,OpenArena=true,InitialGoal=24 };
        Check(analytic.Valid,"an empty native arena can be represented without copying thousands of zero cells");
        bool equal=true;
        for(int goal=0;goal<25;goal++)for(int cell=0;cell<25;cell++)
            equal &= Point.Distance(analytic.Direction(new(cell%5+.5,cell/5+.5),new(goal%5+.5,goal/5+.5),false),flow.Direction(new(cell%5+.5,cell/5+.5),new(goal%5+.5,goal/5+.5),false))<1e-9;
        Check(equal,"analytical open-arena pursuit equals the native grid algorithm for every cell and target");
        var worker=new CombatWorker(); using var enter=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        Check(worker.Submit(Frame(),routes,new(1,0),_=>5,p=>{enter.Set();release.Wait();return true;}),"one detached forecast can start");
        Check(enter.Wait(3000) && !worker.Submit(Frame(),routes,new(),_=>5,_=>true),"busy forecasting never builds a queue of stale jobs");
        worker.Reset();release.Set();Check(SpinWait.SpinUntil(()=>!worker.Busy,3000)&&worker.Take(.1)==null,"round reset invalidates an in-flight forecast");
        var wf=Frame();wf.Time=1;worker.Submit(wf,routes,new(1,0),_=>5,_=>true);Check(SpinWait.SpinUntil(()=>!worker.Busy,3000)&&worker.Take(1.2)?.Route!=null,"a fresh background result is consumable");
        worker.Submit(wf,routes,new(1,0),_=>5,_=>true);Check(SpinWait.SpinUntil(()=>!worker.Busy,3000)&&worker.Take(1.5)==null,"expired background advice is discarded");
        for(int s=0;s<scenes;s++)
        {
            var f=Frame(Enumerable.Range(0,16+s%49).Select(i=>{double a=random.NextDouble()*Math.PI*2,r=2+random.NextDouble()*18;var e=Enemy(r*Math.Cos(a),r*Math.Sin(a),1+random.NextDouble()*8);e.Id=i;e.AttackAfter=random.NextDouble();return e;}).ToArray());
            f.Time=s*.2; f.Attacks=Enumerable.Range(0,s%12).Select(i=>new CombatAttack{Id=i,Known=true,Position=new(),Velocity=new(8*Math.Cos(i),8*Math.Sin(i)),Radius=.35,Damage=130,Lifetime=1,Pierce=2,Motion=AttackMotion.Linear}).ToArray();
            long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            var answer=planner.Choose(f,routes,new(1,0),_=>5,p=>Math.Abs(p.X)<30&&Math.Abs(p.Z)<30);
            milliseconds.Add(watch.Elapsed.TotalMilliseconds);allocated+=GC.GetAllocatedBytesForCurrentThread()-before;
            if(answer!=null&&double.IsFinite(answer.Score)&&planner.Evaluations<=56) finite++;
        }
        Check(finite==scenes,"300 randomized dense scenes produce finite decisions within a fixed branch budget");
        milliseconds.Sort();
        File.WriteAllText(Path.Combine(output,"pandora-simulation-benchmark.json"),JsonSerializer.Serialize(new{scenes,meanMs=milliseconds.Average(),p95Ms=milliseconds[(int)(scenes*.95)],maxMs=milliseconds.Last(),allocatedBytesPerPlan=allocated/scenes,nativeMonoMeasured=false}));
    }
}

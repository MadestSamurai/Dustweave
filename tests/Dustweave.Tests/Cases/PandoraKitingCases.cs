using Dustweave.Pandora;

internal static class PandoraKitingCases
{
    public static void Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("pandora-kiting: " + name); }
        bool Arena(Point p) => Math.Abs(p.X) <= 12 && Math.Abs(p.Z) <= 12;
        Threat Boss(double x, double z, double speed = 2.5) => new() { Id = 1, Boss = true, Position = new(x, z), Radius = 1, MoveSpeed = speed, Weight = 3 };
        var arrival=BossArrival.Prepare(new(-60,-60),new(60,60),10,8,false);
        Check(arrival.Active && !BossArrival.Prepare(new(-60,-60),new(60,60),21,8,false).Active &&
            !BossArrival.Prepare(new(-60,-60),new(60,60),10,8,true).Active,"boss preparation only runs shortly before a known arrival, not during its fight");
        Check(arrival.Cost(new(0,0))==0 && arrival.Cost(new(20,20))==0 && arrival.Cost(new(59,59))>arrival.Cost(new(55,55)),"boss preparation leaves a broad interior and a continuous escape gradient even at the corner");
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            var origin=new Point(57*x,57*z);var route=Planner.Choose(origin,8,[],[new(){Position=new(59*x,59*z),Amount=1000}],new(x,z),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,arrival:arrival);
            Check(route.Direction.X*x<0 && route.Direction.Z*z<0,"scheduled boss preparation exits corner "+x+","+z);
        }
        var edgeOrigin=new Point(57,57);Threat[] blockedExit=[new(){Position=new(53,53),Radius=2.5,Velocity=new(2,2)}];
        var edgeRoute=Planner.Choose(edgeOrigin,8,blockedExit,[],new(),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,arrival:arrival);
        Check(!edgeRoute.Collision,"preparing for a boss never blindly walks through the mob blocking the diagonal exit");
        var stagedFrame=new CombatFrame{Player=edgeOrigin,Hp=700,MapMin=new(-60,-60),MapMax=new(60,60),Bounded=true};CombatSimulation.Prepare(stagedFrame);
        var stagedPlanner=new CombatRoutePlanner();var staged=Planner.Choose(edgeOrigin,8,[],[],new(),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,frame:stagedFrame,combat:stagedPlanner,arrival:arrival);
        Check(staged.Direction.X<0 && staged.Direction.Z<0 && staged.PositionCost>0,"the background combat planner retains boss preparation rather than discarding its spatial cost");
        var turningPlanner=new CombatRoutePlanner();var turningFrame=new CombatFrame{Player=new(0,0),Hp=700,MapMin=new(-60,-60),MapMax=new(60,60),Bounded=true};CombatSimulation.Prepare(turningFrame);
        var outward=Planner.Choose(turningFrame.Player,8,[],[],new(1,0),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,frame:turningFrame,combat:turningPlanner);
        turningFrame.Player=new(44,0);turningFrame.Time=.1;CombatSimulation.Prepare(turningFrame);
        var inward=Planner.Choose(turningFrame.Player,8,[],[],outward.Direction,p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,frame:turningFrame,combat:turningPlanner,arrival:arrival);
        Check(outward.Direction.X>.9 && inward.Direction.X<0,"heading hysteresis cannot retain an outward route when a safe route preserves more boss preparation space");
        var legacy=Planner.Choose(edgeOrigin,8,[],[new(){Position=new(58,57)}],new(),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,farmCurrency:true,arrival:arrival);
        Check(legacy.PositionCost==0,"currency farming does not inherit challenge boss preparation");
        var room=ArenaRoom.Create(new(-60,-60),new(60,60),8,9);
        Check(room.Cost(new())==0 && room.Cost(new(20,20))==0 && room.Cost(new(59,59))>room.Cost(new(59,0)) && room.Cost(new(59,0))>room.Cost(new(50,0)),
            "normal challenge routing values broad radial space and penalizes two blocked sides more than one without forcing a centre point");
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            var p=new Point(57*x,57*z);var f=new CombatFrame{Player=p,Hp=700,Room=room};CombatSimulation.Prepare(f);
            var r=Planner.Choose(p,8,[],[new(){Position=new(59*x,59*z),Amount=10000}],new(x,z),q=>Math.Abs(q.X)<60&&Math.Abs(q.Z)<60,
                frame:f,combat:new CombatRoutePlanner(),room:room);
            Check(r.Direction.X*x<0 && r.Direction.Z*z<0,"normal challenge avoids staying at corner loot "+x+","+z);
        }
        var roomGuard=Planner.Choose(edgeOrigin,8,blockedExit,[],new(),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,room:room);
        Check(!roomGuard.Collision,"ordinary wall preference never overrides a live crowd blocking the inward route");
        var coinRoutes=new List<Route>();Planner.Choose(edgeOrigin,8,[],[],new(),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,farmCurrency:true,room:room,candidates:coinRoutes);
        Check(coinRoutes.All(r=>r.RoomCost==0),"normal currency routes retain their previous boundary policy");
        turningPlanner.Reset();turningFrame.Player=new();turningFrame.Time=1;turningFrame.Room=room;
        var first=Planner.Choose(turningFrame.Player,8,[],[],new(1,0),p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,frame:turningFrame,combat:turningPlanner,room:room);
        turningFrame.Player=new(48,0);turningFrame.Time=1.1;
        var roomTurn=Planner.Choose(turningFrame.Player,8,[],[],first.Direction,p=>Math.Abs(p.X)<60&&Math.Abs(p.Z)<60,frame:turningFrame,combat:turningPlanner,room:room);
        Check(first.Direction.X>.9 && roomTurn.Direction.X<0,"ordinary spatial cost survives background heading hysteresis before the player reaches the wall");
        var movement = new MovementProfile();
        for (int i = 0; i < 144; i++) movement.Observe(1d / 144);
        double Quantize(double x) => Math.Truncate(x * 100) / 100;
        var actualSpeed = movement.Resolve(5.75, (double)1.3f, Quantize);
        Check(Math.Abs(actualSpeed(new(1, 0)) - 4.32) < .001 && Math.Abs(actualSpeed(new(0, 1)) - 7.2) < .001,
            "native per-frame truncation and axial rule explain the captured 5.75 stat / 4.32 travel mismatch");
        Check(actualSpeed(new(.707, .707)) == actualSpeed(new(1, 0)), "diagonal movement cannot receive the native axial multiplier");
        movement.Reset();
        for (int i = 0; i < 60; i++) movement.Observe(1d / 60);
        var lowerRateSpeed = movement.Resolve(5.75, (double)1.3f, Quantize);
        Check(Math.Abs(lowerRateSpeed(new(1, 0)) - 5.4) < .001, "forecast follows actual native frame displacement without changing game frame rate");
        double worstPursuitGap = double.PositiveInfinity, worstFollowupJump = double.PositiveInfinity;
        for (int rotation = 0; rotation < 12; rotation++)
        {
            double theta = rotation * Math.PI / 6;
            var p = new Point(5.13 * Math.Cos(theta), 5.13 * Math.Sin(theta)); var b = new Point(); var previous = new Point();
            const double dt = .075;
            for (int step = 0; step < 192; step++)
            {
                double jumpTime = step * dt - 12;
                var enemy = Boss(b.X, b.Z, 4.9); enemy.Radius = .52; enemy.PreferredGap = 4.4;
                if (jumpTime >= 0)
                {
                    enemy.Motion = BossMotion.Jump; enemy.JumpEvading = true; enemy.MotionSpeed = 9;
                    enemy.JumpTracking = jumpTime < 1.75; enemy.JumpTrackStart = Math.Max(0, .65 - jumpTime);
                    enemy.JumpTrackEnd = Math.Max(0, 1.75 - jumpTime); enemy.JumpImpactAfter = Math.Max(0, 2.1 - jumpTime);
                    enemy.JumpAttackUntil = 2.5 - jumpTime; enemy.JumpRadius = 4.7;
                }
                var route = Planner.Choose(p, 5.75, [enemy], [], previous, q => Math.Abs(q.X) < 90 && Math.Abs(q.Z) < 90,
                    movementSpeed: actualSpeed);
                previous = route.Direction; double speed = actualSpeed(previous);
                p = new(p.X + previous.X * speed * dt, p.Z + previous.Z * speed * dt);
                double chasing = jumpTime < 0 ? dt * 4.9 : Math.Max(0, Math.Min(jumpTime + dt, 1.75) - Math.Max(jumpTime, .65)) * 9;
                double length = Point.Distance(p, b), f = Math.Min(1, chasing / Math.Max(.001, length));
                b = new(b.X + (p.X - b.X) * f, b.Z + (p.Z - b.Z) * f);
                if (jumpTime < 0) worstPursuitGap = Math.Min(worstPursuitGap, Point.Distance(p, b) - .52);
                if (jumpTime + dt >= 2.1) worstFollowupJump = Math.Min(worstFollowupJump, Point.Distance(p, b) - 5.15);
            }
        }
        Check(worstPursuitGap > 2.5, "twelve rotated live-speed pursuits retain separation instead of being caught before the jump");
        Check(worstFollowupJump > 0, "follow-up jumps remain clear using the real directional movement profile");
        var data = new BossDamageModel(); var measured = Boss(0, 0);
        data.Observe(measured, 0, 4, 1000, true, 6, 1);
        data.Observe(measured, 0, 4, 100, true, 6, 1);
        Check(!measured.DamageConfidence && measured.ObservedDps == 0, "zero elapsed time cannot produce infinite DPS");
        data.Reset();
        for (int i = 0; i <= 40; i++) data.Observe(measured, i * .1, 4, 1000 - i * 10, true, 6, 1);
        Check(Math.Abs(measured.ObservedDps - 100) < .001 && Math.Abs(measured.KillSeconds - 6) < .001, "HP loss measures effective DPS and time to kill");
        double productiveRange = measured.PreferredGap;
        for (int i = 41; i <= 80; i++) data.Observe(measured, i * .1, 9, 600, true, 6, 1);
        Check(measured.PreferredGap < 6, "an unproductive far band cannot displace measured close-range damage");
        for (int i = 81; i <= 160; i++) data.Observe(measured, i * .1, 9, 600, true, 6, 1);
        Check(measured.ObservedDps == 0 && !measured.DamageConfidence, "DPS expires after sustained zero damage");
        data.Forget(measured.Id); data.Observe(measured, 17, 5, 9000, true, 6, 1);
        Check(!measured.DamageConfidence && double.IsPositiveInfinity(measured.KillSeconds), "pooled boss spawn cannot inherit earlier damage");
        var second = Boss(3, 2); second.Id = 2;
        for (int i = 0; i <= 20; i++) data.Observe(second, 20 + i * .1, 6, 1000 - i * 20, true, 6, 1);
        Check(second.ObservedDps > 190 && !measured.DamageConfidence, "different bosses retain separate damage histories");
        var immune = Boss(0, 0); immune.Id = 3;
        for (int i = 0; i <= 30; i++) data.Observe(immune, i * .1, 2, 1000, false, 7, 1);
        Check(immune.PreferredGap > 7 && !immune.DamageConfidence, "jump immunity does not teach a false melee damage range");
        var outOfRange = Boss(0, 0); var noHits = new BossDamageModel();
        for (int i = 0; i <= 90; i++) noHits.Observe(outOfRange, i * .1, 9, 1000, true, 7, 1);
        Check(outOfRange.PreferredGap < 6 && outOfRange.PreferredGap >= 2.75, "sustained vulnerable zero DPS cautiously narrows range without inventing damage");
        data.Observe(second, 30, 5, 8000, true, 6, 1);
        Check(!second.DamageConfidence, "health reset or long observation gap invalidates the old fight");
        var burst = Boss(0, 0); var slow = Boss(0, 0); var lowHealth = Boss(0, 0);
        foreach (var pair in new[] { (burst, 150.0, 1.0), (slow, 10.0, 1.0), (lowHealth, 150.0, .2) })
        {
            var model = new BossDamageModel();
            for (int i = 0; i <= 20; i++) model.Observe(pair.Item1, i * .1, 5, 500 - i * .1 * pair.Item2, true, 6, pair.Item3);
        }
        Check(burst.PreferredGap < slow.PreferredGap && lowHealth.PreferredGap > burst.PreferredGap, "measured kill window and health change standoff without overriding safety");

        var dash = Boss(0, 0); dash.Motion = BossMotion.Dash; dash.MotionEnd = new(6, 0); dash.MotionSpeed = 20; dash.PhaseSeconds = .3;
        var atEnd = BossKiting.Advance(dash, new(), new(20, 0), 0, .3);
        Check(Math.Abs(atEnd.X - 6) < .001, "dash prediction stops at its actual destination");
        var after = BossKiting.Advance(dash, atEnd, new(20, 0), .3, .2);
        Check(after.X > 6 && after.X < 7, "after a dash only ordinary pursuit speed is extrapolated");
        var windup = Boss(0, 0); windup.Motion = BossMotion.Dash; windup.MotionEnd = new(10, 0);
        windup.MotionDelay = .4; windup.PhaseSeconds = .9; windup.MotionSpeed = 20;
        Check(BossKiting.Advance(windup, new(), new(20, 0), 0, .3).X == 0, "stationary dash windup remains stationary before release");
        Check(Math.Abs(BossKiting.Advance(windup, new(), new(20, 0), .3, .3).X - 4) < .001, "forecast crosses windup into fast dash instead of ordinary pursuit");
        windup.WindupSpeed = 2;
        Check(Math.Abs(BossKiting.Advance(windup, new(), new(20, 0), 0, .2).X - .4) < .001, "native mobile windup follows at current walking speed");
        var rageDash = Boss(0,0); rageDash.Motion = BossMotion.Dash; rageDash.MotionEnd = new(20,0); rageDash.PhaseSeconds=1; rageDash.MotionSpeed=40;
        Check(BossKiting.Advance(rageDash,new(),new(30,0),0,.2).X > BossKiting.Advance(dash,new(),new(30,0),0,.2).X, "observed rage dash speed reaches the player earlier");
        var turn = BossKiting.Advance(Boss(0, 0), new(), new(0, 10), 0, 1);
        Check(Math.Abs(turn.X) < .001 && turn.Z > 2, "pursuit can turn toward the future player position");
        var area = new AttackArea { A = new(), B = new(10, 0), Radius = 2, ActiveFor = .2 };
        Check(area.Clearance(new(2, 0), .1) < 0 && double.IsPositiveInfinity(area.Clearance(new(2, 0), .3)), "completed attack footprints expire");

        var routes = new List<object>();
        foreach (var fixture in new[] {
            ("near-spawn", new Point(0,0), Boss(3,0)),
            ("right-wall", new Point(10,0), Boss(5,0)),
            ("corner", new Point(10,10), Boss(6,6)),
            ("safe-distance", new Point(0,0), Boss(-9,0)),
            ("open-orbit", new Point(0,0), Boss(-7,0)) })
        {
            int queries = 0;
            var route = Planner.Choose(fixture.Item2, 4, [fixture.Item3], [], new(), p => { queries++; return Arena(p); });
            Check(route.PredictionSeconds > 2.3 && queries < 6000, fixture.Item1 + " uses bounded multi-turn forecast");
            Check(Math.Abs(route.Direction.X) + Math.Abs(route.Direction.Z) > .1, fixture.Item1 + " remains mobile");
            var end = new Point(fixture.Item2.X + route.Direction.X * 3.2, fixture.Item2.Z + route.Direction.Z * 3.2);
            Check(Arena(end), fixture.Item1 + " respects arena bounds");
            if (fixture.Item1 == "near-spawn") Check(route.Direction.X < -.5, "close spawn creates clearance before orbiting");
            if (fixture.Item1 == "right-wall") Check(Math.Abs(route.Direction.Z) > .5, "wall approach turns instead of retreating straight into the boundary");
            if (fixture.Item1 == "corner") Check(route.Direction.X < 0 || route.Direction.Z < 0, "corner chooses a way back into the arena");
            if (fixture.Item1 == "safe-distance") Check(route.Direction.X < .8, "safe separation does not reward endless radial retreat");
            routes.Add(new { name = fixture.Item1, route, queries });
        }
        var imminent = new AttackArea { A = new(-10,0), B = new(10,0), Radius = 1.5 };
        var dodge = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), Arena, warnings: [imminent]);
        Check(Math.Abs(dodge.Direction.Z) > .5, "attack escape outranks preferred DPS distance");
        var aiming = Boss(0, 0, 4); aiming.Motion = BossMotion.Dash; aiming.DashAiming = true;
        aiming.MotionDelay = 2; aiming.PhaseSeconds = 3; aiming.MotionSpeed = 20; aiming.DashRange = 20; aiming.DashRadius = 2.22;
        aiming.MotionEnd = new(-20,0);
        var retreat = Planner.Choose(new(-5.5,0), 4.5, [aiming], [], new(0,1), p => Math.Abs(p.X)<40 && Math.Abs(p.Z)<40);
        Check(retreat.Direction.X < -.8, "dash warning abandons DPS orbit and creates distance before native direction lock");
        var wideDash = Boss(-5,0); wideDash.Motion=BossMotion.Dash; wideDash.MotionEnd=new(15,0); wideDash.MotionSpeed=20; wideDash.PhaseSeconds=1; wideDash.DashRadius=2.2;
        Check(BossKiting.ImmediateGap(wideDash,new(0,1.8),new(),4.5,.4)<0, "wide dash contact cannot be declared safe using the much smaller body collider");
        // Native sequence: 2 s tracking wind-up, then a fixed direction at 20 units/s.
        // Start at the observed ~5.5-unit gap where the former orbit was hit.
        double worstDashClearance=double.PositiveInfinity;
        for(int angle=0;angle<12;angle++)
        {
            double theta=angle*Math.PI/6; var p=new Point(-5.5*Math.Cos(theta),-5.5*Math.Sin(theta));
            var b=new Point(); var end=new Point(); var previous=new Point(); const double dt=.05;
            for(int step=0;step<62;step++)
            {
                double time=step*dt; bool ready=step<40;
                if(step==40){double length=Point.Distance(p,b);end=new Point(b.X+(p.X-b.X)*20/length,b.Z+(p.Z-b.Z)*20/length);}
                var enemy=Boss(b.X,b.Z,4); enemy.Motion=BossMotion.Dash;enemy.DashAiming=ready;enemy.MotionDelay=Math.Max(0,2-time);
                enemy.MotionEnd=ready ? new Point(p.X*20/Point.Distance(p,b),p.Z*20/Point.Distance(p,b)) : end;
                enemy.PhaseSeconds=enemy.MotionDelay+(ready?1:Point.Distance(b,end)/20);enemy.MotionSpeed=20;enemy.DashRange=20;enemy.DashRadius=2.22;
                var warning=ready ? Array.Empty<AttackArea>() : [new AttackArea{A=b,B=end,Radius=2.67,ActiveFor=enemy.PhaseSeconds+.2}];
                var route=Planner.Choose(p,4.5,[enemy],[],previous,q=>Math.Abs(q.X)<40&&Math.Abs(q.Z)<40,warnings:warning);previous=route.Direction;
                var nextP=new Point(p.X+previous.X*4.5*dt,p.Z+previous.Z*4.5*dt);
                var nextB=ready ? b : BossKiting.Advance(enemy,b,nextP,0,dt);
                if(!ready && Point.Distance(b,end)>.3)
                {
                    var r0=new Point(p.X-b.X,p.Z-b.Z);var r1=new Point(nextP.X-nextB.X,nextP.Z-nextB.Z);
                    worstDashClearance=Math.Min(worstDashClearance,Planner.SegmentDistance(new(),r0,r1)-2.22-.45);
                }
                p=nextP;b=nextB;
            }
        }
        Check(worstDashClearance > 0, "twelve rotated live-hit setups retreat through wind-up and clear the locked dash footprint");
        // The captured lethal jump began at a 5.31-unit gap: the boss was initially
        // still, then tracked at 9 units/s; its landing radius was 4.5, not body .52.
        var jump = Boss(0, 0, 4.9); jump.Radius = .52; jump.Motion = BossMotion.Jump;
        jump.JumpEvading = jump.JumpTracking = true; jump.MotionSpeed = 9;
        jump.JumpTrackStart = .65; jump.JumpTrackEnd = 1.75; jump.JumpImpactAfter = 2.1; jump.JumpAttackUntil = 2.5; jump.JumpRadius = 4.7;
        Check(BossKiting.Advance(jump, new(), new(20,0),0,.5).X == 0, "jump forecast preserves only the actual pre-takeoff wait");
        Check(Math.Abs(BossKiting.Advance(jump,new(),new(20,0),.5,.5).X-3.15)<.001, "jump forecast crosses EnableMoving into fast pursuit");
        Check(Math.Abs(BossKiting.Advance(jump,new(),new(20,0),1.5,.5).X-2.25)<.001, "jump forecast stops pursuit at DisableMoving");
        var jumpRetreat = Planner.Choose(new(-5.31,0),5.232,[jump],[],new(0,1),p=>Math.Abs(p.X)<40&&Math.Abs(p.Z)<40);
        Check(jumpRetreat.Direction.X < -.9, "jump windup abandons output orbit before the boss starts moving");
        double worstJumpClearance = double.PositiveInfinity;
        for(int angle=0;angle<12;angle++)
        {
            double theta=angle*Math.PI/6;var p=new Point(-5.31*Math.Cos(theta),-5.31*Math.Sin(theta));var b=new Point();var previous=new Point();
            const double dt=.05;
            for(int step=0;step<50;step++)
            {
                double time=step*dt;
                var enemy=Boss(b.X,b.Z,4.9);enemy.Radius=.52;enemy.Motion=BossMotion.Jump;enemy.MotionSpeed=9;enemy.JumpEvading=true;
                enemy.JumpTracking=time<1.75;enemy.JumpTrackStart=Math.Max(0,.65-time);enemy.JumpTrackEnd=Math.Max(0,1.75-time);
                enemy.JumpImpactAfter=Math.Max(0,2.1-time);enemy.JumpAttackUntil=2.5-time;enemy.JumpRadius=4.7;
                var warnings=enemy.JumpTracking ? Array.Empty<AttackArea>() : [new AttackArea{A=b,B=b,Radius=5.15,ActiveFor=enemy.JumpAttackUntil}];
                var route=Planner.Choose(p,5.232,[enemy],[],previous,q=>Math.Abs(q.X)<40&&Math.Abs(q.Z)<40,warnings:warnings);
                previous=route.Direction;var next=new Point(p.X+previous.X*5.232*dt,p.Z+previous.Z*5.232*dt);
                // Independent native pursuit with a fixed landing after tracking ends.
                if(time>=.65 && time<1.75){double length=Point.Distance(b,next),f=Math.Min(1,9*dt/Math.Max(.001,length));b=new(b.X+(next.X-b.X)*f,b.Z+(next.Z-b.Z)*f);}
                p=next;if(time+dt>=2.1)worstJumpClearance=Math.Min(worstJumpClearance,Point.Distance(p,b)-5.15);
            }
        }
        Check(worstJumpClearance > 0, "twelve rotated captured-gap jump scenarios clear the landing and stay outside through impact");
        jump.JumpTracking=false;jump.JumpTrackEnd=0;jump.JumpImpactAfter=0;
        Check(BossKiting.Advance(jump,new(),new(20,0),0,.8).X==0, "locked landing never follows stale chase velocity");
        Check(BossKiting.ImmediateGap(jump,new(3,0),new(),5,.2)<0, "landing uses native attack radius rather than the small body");
        imminent.ActiveFor = .05;
        var expiring = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), Arena, warnings: [imminent]);
        Check(Math.Abs(expiring.Direction.Z) > .5, "imminent warning expiry does not justify waiting in its footprint");
        var fork = Planner.Choose(new(9,0), 4, [Boss(4,0),Boss(9,6)], [], new(), Arena);
        Check(fork.Direction.Z < -.5, "wall escape accounts for a second boss blocking the upper route");
        var trapped = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), _ => false);
        Check(trapped.Direction.X == 0 && trapped.Direction.Z == 0, "unreachable geometry does not create a fictitious exit");
        var watch = System.Diagnostics.Stopwatch.StartNew(); int count = 100;
        var crowd = Enumerable.Range(0, 104).Select(i => new Threat { Position = new(15 + i % 10, -15 + i / 10), Radius = .4, Velocity = new(-.3,.3) }).ToList(); crowd.Add(Boss(-7,0));
        for (int i = 0; i < count; i++) Planner.Choose(new(), 4, crowd, [], new(0,1), Arena);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pandora-kiting.json"), System.Text.Json.JsonSerializer.Serialize(new {
            status = "passed", realGameTouched = false, averageMs = watch.Elapsed.TotalMilliseconds / count, worstJumpClearance, worstDashClearance, routes
        }, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
}

using Dustweave.Pandora;

internal static class PandoraTacticsCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("pandora tactics: " + name); }
        var tactics = new CombatTactics();
        var ultimate = new SkillOption { TargetType = 3, Subtype = 10, AttackCount = 25, ProjectileSpeed = 20, Duration = 1 };
        Threat[] Ring(double radius) => Enumerable.Range(0, 8).Select(i => new Threat { Id = i + 1, Position = new(radius * Math.Cos(i * Math.PI / 4), radius * Math.Sin(i * Math.PI / 4)), Radius = .3 }).ToArray();
        TacticalDecision Decide(Threat[]? enemies = null, Drop[]? items = null, double hp = 100, double now = 1, bool ready = true, Func<Point,bool>? passable = null, AttackArea[]? warnings = null)
            => tactics.Decide(new(), 4, hp, 100, now, enemies ?? [], items ?? [], passable ?? (_ => true), warnings ?? [], ultimate, ready, false);
        var heal = new Drop { Id = 1, Type = 0, Amount = 40, Position = new(2, 0) };
        Check(Decide(items:[heal]).Target == null, "full health preserves healing pickup");
        Check(Decide(items:[heal], hp:95).Reserved.Contains(heal), "minor injury does not waste most of a large heal");
        Check(Decide(items:[heal], hp:50).Target == heal, "meaningful missing health prioritizes healing");
        var stun = new Drop { Id = 2, Type = 6, Amount = 4, Position = new(.4, .4) };
        Check(Decide(items:[stun]).Reserved.Contains(stun), "EMP is reserved without a crowd");
        var surround = Decide(Ring(3.5), [stun]);
        Check(surround.Surrounded && surround.Target == stun && !surround.Cast && surround.SkillReason == "collect-stun-first", "accessible EMP precedes the skill when a surrounded player has time");
        var nearPickup=Planner.Choose(new(),4,Ring(3.5),[stun],new(),_=>true,tactic:surround);
        Check(Math.Abs(nearPickup.Direction.X)+Math.Abs(nearPickup.Direction.Z)>.1, "a close pickup cannot make every walking endpoint look worse than waiting");
        Check(Decide(Ring(3.5),[stun],now:1.9).Cast, "an EMP that never confirms cannot suppress the skill indefinitely");
        Check(Decide(Ring(1.4), [stun]).Cast, "imminent contact does not delay an available rescue skill for a pickup");
        Check(!Decide(Ring(30)).Cast, "distant enemies do not cause an empty cast");
        Check(!Decide(Ring(3.5).Select(t => { t.Projectile = true; return t; }).ToArray()).Cast, "enemy projectiles do not masquerade as ultimate targets or a monster surround");
        var paired = Decide(Ring(3.5), [stun], now:2);
        Check(!paired.Stunned, "seeing a pickup is not confirmation of its effect");
        tactics.ItemApplied(6, 4, 2);
        paired = Decide(Ring(3.5), [stun], now:2.1);
        Check(paired.Stunned && paired.Cast && paired.SkillReason == "stun-then-skill" && paired.Target == null, "confirmed EMP starts the combo without collecting a second stun");
        Check(!Decide(Ring(3.5), now:6.1).Stunned, "stun duration expires using active game time");
        tactics.CastAttempt(6.1, false, paired.Aim);
        Check(!Decide(Ring(3.5), now:6.3).Cast && tactics.Casts == 0, "rejected native clicks are paced and never recorded as successful casts");
        Check(Decide(Ring(3.5), now:7.2).Cast, "a rejected cast can retry while a useful target remains");
        tactics.CastAttempt(7.2, true, new(1,0));
        Check(Decide(now:7.3).Escaping && tactics.Casts == 1, "only confirmed casts establish a short escape preference");
        Check(!Decide(now:10).Escaping, "escape preference is bounded");
        var orbit = Decide([new() { Position=new(-3,0) },new() { Position=new(-3,1) }], now:8.1);
        Check(orbit.Escaping && orbit.Escape.X > 0 && Math.Abs(orbit.Escape.Z) > .7, "after using the opening movement follows the surviving crowd's outside edge");
        Check(!Decide(Ring(3.5), ready:false, now:11).Cast, "native cooldown always blocks casts");

        tactics.Reset();
        var bomb = new Drop { Id = 3, Type = 2, Position = new(2, 0) };
        var magnet = new Drop { Id = 4, Type = 1, Position = new(0, 2) };
        Drop[] xp = Enumerable.Range(0,40).Select(i => new Drop { Id = i+10, Position = new(-4, i*.05) }).ToArray();
        Threat[] pressure = [new() { Position = new(-4, 0), Radius = .5 }];
        var bombFirst = Decide(pressure, [bomb,magnet,..xp]);
        Check(bombFirst.Target == bomb && bombFirst.Reserved.Contains(magnet), "safe nearby bomb is collected before a magnet");
        var noBomb = Decide(pressure, [magnet,..xp]);
        Check(noBomb.Target == magnet, "a missing or expired bomb cannot hold the magnet forever");
        var farBomb = new Drop { Type = 2, Position = new(30, 0) };
        Check(Decide(pressure,[farBomb,magnet,..xp]).Target == magnet, "far bomb cannot suppress a useful magnet");
        Check(Decide(pressure,[bomb,magnet,..xp],passable:p => p.X < 1).Target == magnet, "blocked bomb cannot suppress a useful magnet");
        tactics.ItemApplied(2,100,2);
        Check(Decide(items:[magnet,..xp],now:2.1).Target != magnet, "confirmed bomb gives its drop-spawn update a short active-time window");
        Check(Decide(items:[magnet,..xp],now:2.4).Target == magnet, "magnet follows bomb without waiting on stale threat counts");
        Check(Decide(pressure,[heal,magnet,..xp],hp:10,now:2.4).Target == heal, "critical healing outranks the bomb-magnet sequence");

        var forward = new Threat { Position = new(4,0), Radius = .4 };
        Check(CombatTactics.CanHit(new(),new(1,0),forward,ultimate) && !CombatTactics.CanHit(new(),new(-1,0),forward,ultimate), "directional fan uses the chosen facing rather than movement's opposite side");
        var radial = new SkillOption { Subtype = 11, TargetType = 1, AttackCount = 8, ProjectileSpeed = 10, Duration = 3 };
        Check(CombatTactics.CanHit(new(),new(-1,0),forward,radial) == CombatTactics.CanHit(new(),new(1,0),forward,radial), "radial skill rays cannot be rotated by the player's facing");
        Check(!CombatTactics.CanHit(new(),new(1,0),new() { Position = new(4,0), Boss=true, Motion=BossMotion.Jump },ultimate), "invulnerable boss jumps do not trigger a cast");
        Check(Decide([new(){Boss=true,Position=new(-3,0),Radius=.5}],passable:p=>p.X>=-.5).Cast, "a useful target remains castable even if its direction faces a wall; movement stays independent");
        var bosses = new[] { new Threat { Id=99, Boss=true, Position=new(-7,0), Radius=1, MoveSpeed=1, PreferredGap=6 } };
        var bossHeal = Decide(bosses,[heal],hp:20,now:3);
        Check(bossHeal.Target == heal && bossHeal.Cast, "boss combat keeps a safe rescue pickup and a useful ultimate");
        var route = Planner.Choose(new(),4,bosses,[heal],new(),_=>true,tactic:bossHeal);
        Check(!route.Collision && route.Direction.X > 0, "boss planner integrates safe healing with its collision forecast");
        var hazard = new AttackArea { A=new(-10,0),B=new(10,0),Radius=1.5 };
        var avoid = Planner.Choose(new(),4,[],[heal],new(),_=>true,warnings:[hazard],tactic:Decide(items:[heal],hp:10,warnings:[hazard]));
        Check(Math.Abs(avoid.Direction.Z)>.5, "leaving an active attack warning outranks a low-health pickup");
        var saved = Decide(items:[heal]);
        var xpBeyondHeal = new Drop { Position = new(4,0), Amount=100 };
        var preserve = Planner.Choose(new(),4,[],[heal,xpBeyondHeal],new(),_=>true,tactic:saved);
        Check(Planner.SegmentDistance(heal.Position,new(),new(preserve.Direction.X*3.2,preserve.Direction.Z*3.2)) > heal.PickupRadius, "incidental loot route avoids wasting a reserved heal when there is room");
        tactics.CastAttempt(20,true,new(1,0));
        var escape = Decide([new() {Position=new(1.5,0),Radius=.5}],now:20.1);
        var escapeRoute = Planner.Choose(new(),4,[new(){Position=new(1.5,0),Radius=.5}],[],new(),_=>true,tactic:escape);
        Check(!escapeRoute.Collision, "confirmed cast never treats a still-living enemy as an open exit");
        var wall = Decide(Ring(3.5), [heal], hp:10, now:20.1, passable:_=>false);
        Check(wall.Target == null && Planner.Choose(new(),4,[],[heal],new(),_=>false,tactic:wall).Direction.X==0,
            "a tactical objective never creates a route outside native bounds");
        tactics.Reset();
        Check(tactics.Bombs==0 && tactics.Stuns==0 && tactics.Casts==0 && !Decide(now:1).Escaping && !Decide(now:1).Stunned, "round restart clears every tactical timer and counter");
        TacticalDecision Late(double seconds,bool currency=false) => tactics.Decide(new(),4,100,100,30,Ring(6),[stun],_=>true,[],ultimate,false,currency,seconds);
        Check(Late(301).Target==null && Late(180).Target==stun,"the last five minutes permit a safe EMP before the crowd becomes an emergency");
        Check(!Late(301).Stalling && Late(301).LootScale==1 && Late(300).Stalling,"the strategy changes at 15 minutes while early growth priorities stay unchanged");
        Check(Late(121).StunUrgency==0 && Late(30).StunUrgency>Late(90).StunUrgency,"the last two minutes progressively add a separate EMP priority");
        Check(Late(30).SurvivalUrgency>Late(90).SurvivalUrgency && Late(30).LootScale<Late(90).LootScale,"time pressure progressively prioritizes survival over ordinary loot");
        Check(Late(30,true).Target==null && Late(30,true).LootScale==1,"currency farming does not inherit the challenge survival deadline");
        tactics.ItemApplied(6,5,30);tactics.CastAttempt(30,true,new(1,0));
        var protectedWindow=Late(30);
        Check(protectedWindow.StunSafety && protectedWindow.Target==null && protectedWindow.LootScale==.1,"an active stun is preserved and ordinary loot loses priority");
        protectedWindow.Target=null;protectedWindow.Reserved.Clear();
        Check(CombatTactics.Utility(new(),new(3,0),4,protectedWindow)==0,"casting while monsters are stunned does not reward charging along the attack direction");
        var earlyDrops=new[]{new Drop{Position=new(4,0),Amount=10000}};
        var earlyPlan=new List<Route>();var latePlan=new List<Route>();
        Planner.Choose(new(),4,[],earlyDrops,new(),_=>true,region:new(4,0),tactic:new(),candidates:earlyPlan);
        Planner.Choose(new(),4,[],earlyDrops,new(),_=>true,region:new(4,0),tactic:protectedWindow,candidates:latePlan);
        Check(latePlan.Max(r=>r.LootGain)<=18 && latePlan.Max(r=>r.LootGain)<earlyPlan.Max(r=>r.LootGain),"late control cannot be pulled back into a former experience region by a saturated loot score");
        var ordinaryChest=new Drop{Type=5,Position=new(2,0)};
        var growth=new TacticalDecision{Target=ordinaryChest};
        var stall=new TacticalDecision{Target=ordinaryChest,Stalling=true,LootScale=.25};
        Check(CombatTactics.Utility(new(),new(3,0),4,stall)==CombatTactics.Utility(new(),new(3,0),4,growth)*.25,"late chest routing follows the same reduced growth priority as its selection");
        tactics.Reset();

        // A player already inside a surround must use real control windows to
        // cross out, rather than treating every stunned body as a permanent wall.
        var frozenRing=Ring(3.5);foreach(var e in frozenRing){e.StunFor=4.9;e.Radius=1;}
        tactics.ItemApplied(6,5,100);
        Check(!tactics.Decide(new(),4,100,100,100.1,frozenRing,[],_=>true,[],ultimate,false,false,800).BreakingOut,
            "ordinary early growth does not activate the new controlled escape policy");
        var prep=BossArrival.Prepare(new(-60,-60),new(60,60),12,4,false);
        Check(tactics.Decide(new(),4,100,100,100.1,frozenRing,[],_=>true,[],ultimate,false,false,800,prep).ControlledExit,
            "the same escape policy assists an early boss preparation window");
        Check(!tactics.Decide(new(),4,100,100,100.1,frozenRing,[],_=>true,[],ultimate,false,true,800,prep).BreakingOut,
            "currency farming never inherits challenge boss preparation escape");
        var breakout=tactics.Decide(new(),4,100,100,100.1,frozenRing,[stun],_=>true,[],ultimate,false,false,150);
        Check(breakout.BreakingOut && breakout.ControlledExit && Point.Distance(breakout.ControlTarget,new())>4 && breakout.Target==null,
            "an existing surround uses the confirmed stun to plan a route beyond its edge without immediately wasting another EMP");
        Check(CombatTactics.Utility(new(),new(breakout.ControlTarget.X*.25,breakout.ControlTarget.Z*.25),4,breakout)>0,
            "the controlled escape has movement value independent of attack aim and ordinary loot");
        var wallStop=tactics.Decide(new(),4,100,100,100.1,frozenRing,[],p=>Math.Abs(p.X)<5.01&&Math.Abs(p.Z)<5.01,[],ultimate,false,false,150);
        Check(!wallStop.ControlledExit || Math.Abs(wallStop.ControlTarget.X)+.6<5.01 && Math.Abs(wallStop.ControlTarget.Z)+.6<5.01,
            "a controlled exit leaves the character footprint inside the arena rather than only its centre");
        var escapeFrame=new CombatFrame{Hp=700,MaxHp=700,PreserveControlSpace=true,ControlledExit=breakout.ControlledExit,ControlTarget=breakout.ControlTarget,
            Enemies=frozenRing.Select(e=>new CombatBody{Id=e.Id,Position=e.Position,Radius=e.Radius,Hp=10000,Damage=650,StunFor=e.StunFor,Known=true}).ToArray()};
        CombatSimulation.Prepare(escapeFrame);var exitPlanner=new CombatRoutePlanner();
        var crossingRoute=Planner.Choose(new(),4,frozenRing,[],new(),_=>true,tactic:breakout,frame:escapeFrame,combat:exitPlanner);
        Check(Point.Distance(crossingRoute.Direction,new())>.5 && exitPlanner.Selected.Damage==0 && exitPlanner.Selected.ControlDamage==0,
            "the actual combat planner walks through the controlled surround toward the verified exit instead of waiting inside");
        stun.Following=true;
        var pending=tactics.Decide(new(),4,100,100,100.2,frozenRing,[stun],_=>true,[],ultimate,false,false,150);
        Check(pending.Target==stun && pending.ControlledExit && pending.StunRemaining<4.9,
            "a following EMP remains a pickup objective without inventing its application or new duration");
        stun.Following=false;
        var freshThreat=new Threat{Position=new(.9,0),Radius=.3};
        var blockedRefresh=tactics.Decide(new(),4,100,100,101,[..frozenRing,freshThreat],[stun],_=>true,[],ultimate,false,false,149);
        Check(blockedRefresh.Target!=stun,"EMP renewal cannot walk through a fresh unstunned attacker blocking its pickup route");
        freshThreat.Position=new(-.9,0);
        var urgentRefresh=tactics.Decide(new(),4,100,100,101.1,[..frozenRing,freshThreat],[stun],_=>true,[],ultimate,false,false,149);
        Check(urgentRefresh.Target==stun,"an unstunned reinforcement closing in can justify an early EMP renewal during escape");
        foreach(var e in frozenRing)e.StunFor=.6;
        var refresh=tactics.Decide(new(),4,100,100,104.4,frozenRing,[stun],_=>true,[],ultimate,false,false,145);
        Check(refresh.BreakingOut && refresh.Stunned && refresh.Target==stun && !refresh.Reserved.Contains(stun),
            "a surrounded player approaches the next EMP before the old stun expires");
        var unconfirmed=tactics.Decide(new(),4,100,100,105.1,frozenRing,[stun],_=>true,[],ultimate,false,false,144);
        Check(!unconfirmed.Stunned,"a planned EMP renewal never extends control without a native pickup confirmation");
        tactics.ItemApplied(6,5,105.2);
        Check(tactics.Decide(new(),4,100,100,105.3,frozenRing,[],_=>true,[],ultimate,false,false,144).StunRemaining>4.8,
            "a confirmed second EMP resets the escape's control clock");
        tactics.Decide(new(),4,100,100,106,[],[],_=>true,[],ultimate,false,false,143);
        Check(!tactics.Decide(new(),4,100,100,107.1,[],[],_=>true,[],ultimate,false,false,142).BreakingOut,
            "escape mode ends after actually staying outside the crowd");
        tactics.Reset();tactics.ItemApplied(6,5,100);foreach(var e in frozenRing)e.StunFor=0;
        var freshRing=tactics.Decide(new(),4,100,100,100.1,frozenRing,[],_=>true,[],ultimate,false,false,150);
        Check(freshRing.BreakingOut && !freshRing.ControlledExit,"fresh unstunned bodies cannot inherit a previous EMP's safe crossing");
        tactics.Reset();

        // Late corner holds survive fresh observations, not just one scoring pass.
        var cornerOrigin=new Point(16,16);
        var cornerRoom=ArenaRoom.Create(new(-20,-20),new(20,20),4,12);
        Func<Point,bool> cornerBounds=p=>Math.Abs(p.X)<20 && Math.Abs(p.Z)<20;
        Threat[] CornerRing(double frozen=0) => Ring(3.5).Select(e=>new Threat{Id=e.Id,Position=new(e.Position.X+16,e.Position.Z+16),Radius=.3,StunFor=frozen,MoveSpeed=3}).ToArray();
        TacticalDecision Corner(Threat[]? enemies=null,Drop[]? items=null,double remaining=200,double now=100.1,bool currency=false,Point? origin=null,AttackArea[]? areas=null,ArenaRoom? room=null)
            => tactics.Decide(origin??cornerOrigin,4,20,100,now,enemies??CornerRing(),items??[],cornerBounds,areas??[],ultimate,true,currency,remaining,room:room??cornerRoom);
        tactics.Reset();
        Check(!Corner(remaining:301).CornerLocked && !Corner(currency:true).CornerLocked,"corner hold is restricted to the last five minutes of challenge mode");
        var corner=Corner();
        Check(corner.CornerLocked && corner.CornerHold && !corner.ControlledExit && corner.Cast,"a trapped late corner stops movement while retaining useful skill casts");
        var bait=new[]{new Drop{Type=0,Amount=100,Position=new(15,15)},new Drop{Type=5,Position=new(15,15)},new Drop{Type=6,Amount=100,Position=new(15,15)},new Drop{Type=3,Amount=1e12,Position=new(15,15)}};
        var quietCorner=Corner(enemies:[],items:bait,now:102);
        Check(quietCorner.CornerHold && quietCorner.Target==null && !quietCorner.Escaping,"a brief gap, missing health and tempting items cannot release a corner latch");
        var cornerCandidates=new List<Route>();
        var cornerRoute=Planner.Choose(cornerOrigin,4,[],bait,new(-1,-1),cornerBounds,region:new(0,0),tactic:quietCorner,candidates:cornerCandidates,room:cornerRoom);
        Check(Point.Distance(cornerRoute.Direction,new())==0 && cornerRoute.Reason=="corner-hold" && cornerCandidates.Count==0,"loot, old heading and generic route scoring cannot override the late corner hold");
        Check(tactics.Decide(cornerOrigin,4,100,100,103,[],[],cornerBounds,[],ultimate,false,false,199).CornerHold,"a missing room observation cannot erase an existing corner latch");
        tactics.CastAttempt(103,true,new(-1,-1));
        Check(Corner(now:103.1).CornerHold && !Corner(now:103.1).Escaping,"a confirmed skill cast does not by itself justify charging out of a corner");
        Check(!Corner(enemies:[],origin:new(0,0),now:104).CornerLocked,"actually reaching the clear interior releases corner hold");
        tactics.Reset();
        var frontCrowd=new[]{new Threat{Position=new(13,16),Radius=.3},new Threat{Position=new(16,13),Radius=.3},new Threat{Position=new(13,13),Radius=.3}};
        Check(Corner(enemies:frontCrowd).CornerHold,"corner walls count as lost escape space before the crowd makes contact");
        tactics.Reset();
        Check(!Corner(enemies:Ring(3.5),origin:new()).CornerLocked,"an ordinary interior surround keeps its existing controlled-breakout policy");
        Corner();tactics.Reset();
        Check(!Corner(enemies:[]).CornerLocked,"round reset clears the corner latch");
        Corner();
        var cornerBoss=new[]{new Threat{Boss=true,Position=new(10,16),Radius=1,MoveSpeed=1,PreferredGap=6}};
        var bossCorner=Corner(enemies:cornerBoss);
        Check(!bossCorner.CornerLocked && !bossCorner.CornerHold && Point.Distance(Planner.Choose(cornerOrigin,4,cornerBoss,[],new(),cornerBounds,tactic:bossCorner).Direction,new())>.5,"boss combat retains its dedicated movement policy");

        tactics.Reset();Corner();tactics.ItemApplied(6,5,100);
        var safeCorner=Corner(enemies:CornerRing(4.9),items:bait);
        Check(safeCorner.CornerLocked && safeCorner.ControlledExit && !safeCorner.CornerHold && safeCorner.Target==null && safeCorner.ControlTarget.X<=12 && safeCorner.ControlTarget.Z<=12,"confirmed control permits a complete escape into the interior without chasing another pickup");
        var safeCornerRoute=Planner.Choose(cornerOrigin,4,CornerRing(4.9),bait,new(1,1),cornerBounds,tactic:safeCorner,room:cornerRoom);
        var intended=CombatSimulation.Unit(CombatSimulation.Sub(safeCorner.ControlTarget,cornerOrigin));
        Check(Point.Distance(safeCornerRoute.Direction,intended)<1e-8,"a certified corner exit follows the checked direction rather than a loot-scored alternative");
        var freshBlock=new Threat{Position=new(15.9,15.9),Radius=.3,MoveSpeed=8};
        Check(Corner(enemies:[..CornerRing(4.9),freshBlock]).CornerHold,"a newly spawned unstunned attacker invalidates the exit even while the EMP timer is running");
        Check(Corner(enemies:[..CornerRing(4.9),new(){Projectile=true,Position=new(16,16),Velocity=new(20,0),Radius=.3}]).CornerHold,"projectiles do not inherit monster stun protection");
        Check(Corner(enemies:CornerRing(4.9),areas:[new(){A=new(-20,16),B=new(20,16),Radius=4}]).CornerHold,"an attack warning invalidates an otherwise controlled corner exit");
        Check(Corner(enemies:CornerRing(),items:bait).CornerHold,"global EMP confirmation cannot replace individual monster control evidence");
        Check(Corner(enemies:CornerRing(.5),items:bait,now:104.5).CornerHold,"future EMP pickups never extend a short remaining control window");
        Check(Corner(enemies:[],now:105.1).CornerHold,"the corner hold survives expired control even when the crowd temporarily clears");
        var noCertificate=Planner.Choose(cornerOrigin,4,[],bait,new(-1,-1),cornerBounds,tactic:new(){CornerLocked=true});
        Check(Point.Distance(noCertificate.Direction,new())==0,"missing exit evidence defaults to standing still");

        // Rotate corners and vary nearby attackers: no unconfirmed movement may
        // escape the latch in any orientation, even with extremely valuable loot.
        for(int seed=0;seed<128;seed++)
        {
            tactics.Reset();var random=new Random(seed);double sx=(seed&1)==0?1:-1,sz=(seed&2)==0?1:-1;
            var position=new Point(sx*(16+random.NextDouble()*2),sz*(16+random.NextDouble()*2));
            var nearby=Enumerable.Range(0,6).Select(i=>new Threat{Position=new(position.X-sx*(1+random.NextDouble()*3),position.Z-sz*(1+random.NextDouble()*3)),Radius=.3,MoveSpeed=2+random.NextDouble()*8}).ToArray();
            var decision=tactics.Decide(position,4,30,100,100,nearby,bait,cornerBounds,[],ultimate,true,false,random.Next(1,300),room:cornerRoom);
            var stationary=Planner.Choose(position,4,nearby,bait,new(-sx,-sz),cornerBounds,tactic:decision);
            Check(decision.CornerHold && Point.Distance(stationary.Direction,new())==0,"unconfirmed corner escape is suppressed "+seed);
        }
        tactics.Reset();

        // Rotate a dense surround through different walls and approach angles. Every
        // decision must remain finite and the existing safety ordering must hold.
        for(int seed=0;seed<32;seed++)
        {
            var angle=seed*Math.PI/16;
            var enemies=Ring(3.5);
            var item=new Drop{Type=0,Amount=40,Position=new(Math.Cos(angle)*2,Math.Sin(angle)*2)};
            Func<Point,bool> bounds=p=>Math.Abs(p.X)<5&&Math.Abs(p.Z)<5;
            var d=Decide(enemies,[item],hp:20,passable:bounds);
            var r=Planner.Choose(new(),4,enemies,[item],new(),bounds,tactic:d);
            Check(double.IsFinite(r.Score)&&double.IsFinite(r.Direction.X)&&double.IsFinite(r.Direction.Z),"bounded crowd decision "+seed);
        }
        var dense = Enumerable.Range(0,104).Select(i=>new Threat{Position=new(12+i%10,-8+i/10),Radius=.4}).Concat(
            Enumerable.Range(0,80).Select(i=>new Threat{Position=new(9+i%10,-12+i/10),Projectile=true,Velocity=new(-.5,.2)})).ToList();
        dense.Add(new Threat{Boss=true,Position=new(-7,0),Radius=1,MoveSpeed=2,PreferredGap=6});
        var drops=Enumerable.Range(0,32).Select(i=>new Drop{Id=i,Type=i%7,Position=new(2+i%4,2+i/4),Amount=30}).Concat(
            Enumerable.Range(0,256).Select(i=>new Drop{Id=i+32,Position=new(1+i%12,-2+i/12),Amount=1})).ToArray();
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<100;i++)
        {
            var d=tactics.Decide(new(),4,30,100,30,dense,drops,_=>true,[],ultimate,true,false);
            Planner.Choose(new(),4,dense,drops,new(),_=>true,tactic:d);
        }
        watch.Stop();
        System.IO.File.WriteAllText(System.IO.Path.Combine(root,"pandora-tactics.json"),System.Text.Json.JsonSerializer.Serialize(new{threats=dense.Count,drops=drops.Length,iterations=100,averageMilliseconds=watch.Elapsed.TotalMilliseconds/100}));
        var controlledCrowd=Enumerable.Range(0,104).Select(i=>new Threat{Id=i,Position=new((3+i%5)*Math.Cos(i*.7),(3+i%5)*Math.Sin(i*.7)),Radius=.4,StunFor=4.9}).ToArray();
        tactics.Reset();tactics.ItemApplied(6,5,30);int maximumQueries=0;
        watch.Restart();
        for(int i=0;i<32;i++)
        {
            int queries=0;
            tactics.Decide(new(),4,100,100,30.1,controlledCrowd,[],p=>{queries++;return Math.Abs(p.X)<12&&Math.Abs(p.Z)<12;},[],ultimate,false,false,100);
            maximumQueries=Math.Max(maximumQueries,queries);
        }
        watch.Stop();
        Check(maximumQueries<=320,"controlled exit bounds native path queries even with a full captured crowd");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root,"pandora-breakout.json"),System.Text.Json.JsonSerializer.Serialize(new{threats=104,iterations=32,maximumQueries,averageMilliseconds=watch.Elapsed.TotalMilliseconds/32}));
    }
}

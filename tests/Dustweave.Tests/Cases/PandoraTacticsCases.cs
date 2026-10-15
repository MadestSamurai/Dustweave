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
    }
}

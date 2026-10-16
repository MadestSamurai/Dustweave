#nullable disable
using System;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    public sealed class TacticalDecision
    {
        public Drop Target;
        public readonly List<Drop> Reserved = new List<Drop>();
        public Point Aim, Escape, ControlTarget;
        public bool Cast, Surrounded, Emergency, Stunned, Escaping;
        public bool StunSafety, Stalling;
        public bool BreakingOut, ControlledExit;
        public bool CornerLocked, CornerHold;
        public double StunRemaining;
        public double SurvivalUrgency, StunUrgency, LootScale=1;
        public int Nearby, Hits;
        public string Reason = "hold", SkillReason = "hold";
    }

    // Decisions use observed game state. A disappearing pickup or a successful UI
    // click is not evidence that its effect has actually been applied.
    public sealed class CombatTactics
    {
        double bombAt = double.NegativeInfinity, stunUntil, attemptAfter, escapeUntil;
        double stunWaitAt; int waitingStun;
        bool breakingOut; double clearSince=-1;
        bool cornerLocked;
        Point controlTarget; bool hasControlTarget;
        Point escape;
        public int Bombs, Stuns, Casts;
        public void Reset() { bombAt = double.NegativeInfinity; stunUntil = attemptAfter = escapeUntil = stunWaitAt = 0; waitingStun = 0; escape = new Point(); Bombs = Stuns = Casts = 0; breakingOut=cornerLocked=false;clearSince=-1;hasControlTarget=false; }
        public void ItemApplied(int type, double amount, double now)
        {
            if (type == 2) { bombAt = now; Bombs++; }
            if (type == 6) { stunUntil = now + Math.Max(0, amount); waitingStun = 0; Stuns++; }
        }
        public void CastAttempt(double now, bool confirmed, Point aim)
        {
            attemptAfter = now + (confirmed ? .5 : 1);
            if (confirmed) { Casts++; escape = aim; escapeUntil = now + 2.5; }
        }

        public TacticalDecision Decide(Point origin, double speed, double hp, double maxHp, double now,
            IList<Threat> threats, IList<Drop> drops, Func<Point, bool> passable, IList<AttackArea> warnings,
            SkillOption ultimate, bool ready, bool farmCurrency, double remainingSeconds=double.PositiveInfinity,BossArrival preparation=null,ArenaRoom room=null)
        {
            var result = new TacticalDecision { Stunned = now < stunUntil, Escaping = now < escapeUntil, Escape = escape };
            result.Stalling=!farmCurrency && remainingSeconds<=300;
            bool preparing=!farmCurrency && preparation!=null && preparation.Active;
            result.StunRemaining=Math.Max(0,stunUntil-now);
            result.StunSafety=(result.Stalling || preparing) && result.Stunned;
            result.SurvivalUrgency=farmCurrency?0:Math.Max(0,Math.Min(1,1-remainingSeconds/300));
            result.StunUrgency=farmCurrency?0:Math.Max(0,Math.Min(1,1-remainingSeconds/120));
            result.LootScale=farmCurrency?1:result.StunSafety?.1:1-.75*result.SurvivalUrgency;
            speed = Math.Max(.1, speed); double fraction = hp / Math.Max(1, maxHp);
            var sectors = new bool[12]; int close = 0,normalClose=0; bool imminent = false,bossPresent=false,uncontrolledImminent=false;double normalGap=100;
            foreach (var t in threats)
            {
                if (t.Projectile) continue;
                double distance = Point.Distance(origin, t.Position), gap = distance - t.Radius - .45;
                bossPresent|=t.Boss;
                if(!t.Boss){normalGap=Math.Min(normalGap,gap);if(gap<7)normalClose++;}
                if (gap < 7) result.Nearby++;
                if (gap > Math.Max(4, speed * 1.2)) continue;
                close++;
                var v = new Point(t.Position.X - origin.X, t.Position.Z - origin.Z);
                double approaching = -(v.X * t.Velocity.X + v.Z * t.Velocity.Z) / Math.Max(.01, distance);
                imminent |= gap < 1.1 || approaching > .1 && gap / approaching < .65;
                uncontrolledImminent|=!t.Boss && t.StunFor<=.3 && (gap<1.1 || approaching>.1 && gap/approaching<.65);
                double angle = Math.Atan2(v.Z, v.X), half = Math.Asin(Math.Min(1, (t.Radius + 1) / Math.Max(.01, distance)));
                for (int s = 0; s < 12; s++) if (Math.Cos(s * Math.PI / 6 - angle) >= Math.Cos(half + Math.PI / 12)) sectors[s] = true;
            }
            int occupied = 0; foreach (bool s in sectors) if (s) occupied++;
            result.Surrounded = close >= 3 && occupied >= 6;
            result.Emergency = imminent && close >= 2 || fraction < .3 && close > 0;
            if(!result.Stalling || bossPresent)cornerLocked=false;
            else if(room!=null)
            {
                double x=Math.Min(origin.X-room.Min.X,room.Max.X-origin.X);
                double z=Math.Min(origin.Z-room.Min.Z,room.Max.Z-origin.Z);
                double inset=Math.Min(8,room.Radius);
                // In a corner, the two walls already remove escape directions.
                // Do not wait for monsters to surround all sides or make contact.
                if(x<inset && z<inset && (result.Surrounded || normalClose>=3 && (result.Emergency || normalGap<4)))cornerLocked=true;
                // A temporary gap or a tempting drop does not release the latch.
                // Release only after actually reaching the open interior.
                if(x>=inset && z>=inset && !result.Surrounded && normalGap>3 && normalClose<=5)cornerLocked=false;
            }
            result.CornerLocked=cornerLocked;
            if(!(result.Stalling || preparing) || bossPresent){breakingOut=false;clearSince=-1;hasControlTarget=false;}
            else
            {
                if(result.Surrounded || result.Emergency && normalClose>=3){breakingOut=true;clearSince=-1;}
                // One open sector is not yet an escape. Keep the objective until
                // the player has actually reached the crowd's outside for a while.
                if(breakingOut && !result.Surrounded && occupied<=3 && normalClose<=5 && normalGap>3)
                { if(clearSince<0)clearSince=now; if(now-clearSince>=1){breakingOut=false;hasControlTarget=false;} }
                else clearSince=-1;
            }
            result.BreakingOut=breakingOut;
            if(breakingOut || cornerLocked)
            {
                result.LootScale=0;
                if(result.Stunned && TryControlExit(origin,speed,result.StunRemaining,threats,passable,warnings,
                    hasControlTarget?(Point?)controlTarget:null,preparation,room,cornerLocked,out controlTarget))
                {hasControlTarget=true;result.ControlledExit=true;result.ControlTarget=controlTarget;result.Reason="leave-surround";}
            }
            if (result.Escaping && now > escapeUntil - 1.8)
            {
                // Once the initial opening has been used, follow the outside of
                // the surviving crowd instead of driving back through its centre.
                double x = 0, z = 0, weight = 0;
                foreach (var t in threats) if (!t.Boss && !t.Projectile)
                {
                    double distance = Point.Distance(origin, t.Position);
                    if (distance > 10) continue;
                    double w = 1 / Math.Max(1, distance);
                    x += (origin.X - t.Position.X) * w; z += (origin.Z - t.Position.Z) * w; weight += w;
                }
                double length = Math.Sqrt(x * x + z * z);
                if (weight > 0 && length > .1)
                {
                    x /= length; z /= length; double side = -z * escape.X + x * escape.Z >= 0 ? 1 : -1;
                    var tangent = new Point(-z * side + x * .35, x * side + z * .35);
                    double norm = Point.Distance(new Point(), tangent);
                    result.Escape = new Point(tangent.X / norm, tangent.Z / norm);
                }
            }

            // Keep an accessible bomb ahead of a nearby magnet, without waiting for a
            // distant/unsafe bomb or chasing an expired object indefinitely.
            Drop bomb = null, stun = null; double bombDistance = double.PositiveInfinity, best = 0;
            int experience = 0, coins = 0;
            foreach (var d in drops) { if (d.Type == 3) experience++; if (d.Type == 4) coins++; }
            foreach (var d in drops) if (d.Type == 2 && Point.Distance(origin, d.Position) < bombDistance &&
                Point.Distance(origin, d.Position) / speed <= 3 && SafeApproach(origin, d.Position, speed, threats, passable, warnings))
            { bomb = d; bombDistance = Point.Distance(origin, d.Position); }
            foreach (var d in drops)
            {
                double distance = Point.Distance(origin, d.Position), value = 0;
                bool reserve = false; string label = "";
                switch (d.Type)
                {
                    case 0:
                        double missing = Math.Max(0, maxHp - hp), useful = Math.Min(missing, d.Amount);
                        reserve = useful <= 0 || fraction > .65 && missing < d.Amount * .8;
                        if (!reserve) { value = fraction < .35 ? 160 : 55 + 35 * useful / Math.Max(1, maxHp); label = "heal"; }
                        break;
                    case 1:
                        reserve = result.BreakingOut || now - bombAt < .35 || bomb != null && result.Nearby > 0 && bombDistance <= distance + speed * 1.5;
                        if (!reserve) { value = Math.Min(70, 6 + experience * .6 + (farmCurrency ? coins * .3 : 0)); label = "magnet"; }
                        break;
                    case 2: value = result.Nearby > 0 ? 70 : 12; label = "bomb"; break;
                    case 5: reserve=result.BreakingOut;value = 48; label = "upgrade"; break;
                    case 6:
                        // Begin the next pickup approach before the current EMP
                        // expires, accounting for travel. Confirmation still owns
                        // the timer; seeing another EMP never extends it.
                        double arrival=Math.Max(0,distance-d.PickupRadius)/speed;
                        bool chain=result.BreakingOut && result.Stunned && (uncontrolledImminent || result.StunRemaining<=Math.Min(2.5,Math.Max(.65,arrival+.5)));
                        bool collecting=result.BreakingOut && d.Following;
                        reserve = !collecting && (result.Stunned && !chain || normalClose < 2 || !(result.Surrounded || result.Emergency || result.Stalling));
                        if (!reserve) { value = (result.BreakingOut?200:125)+(fraction<.35?0:50*result.SurvivalUrgency+200*result.StunUrgency); label = "stun"; }
                        break;
                }
                if (reserve) { result.Reserved.Add(d); continue; }
                if(result.Stalling && (d.Type==1 || d.Type==5))value*=result.LootScale;
                if (value <= 0 || distance > Math.Max(18, speed * 4) || !SafeApproach(origin, d.Position, speed, threats, passable, warnings,result.BreakingOut)) continue;
                if (d.Type == 6 && (stun == null || distance < Point.Distance(origin, stun.Position))) stun = d;
                double score = value / (1 + Math.Max(0, distance - d.PickupRadius) / speed);
                if (score > best) { best = score; result.Target = d; result.Reason = label; }
            }
            if(!cornerLocked && result.BreakingOut && result.Stunned && result.Target!=null && result.Target.Type==6 &&
                Point.Distance(origin,result.Target.Position)>.05)
            {
                // A confirmed reachable EMP can be the next waypoint of the
                // escape. Its future control is never credited before collection.
                result.ControlledExit=true;result.ControlTarget=result.Target.Position;
            }
            if(cornerLocked)
            {
                result.Target=null;stun=null;result.Escaping=false;result.LootScale=0;
                result.CornerHold=!result.ControlledExit;
                result.Reason=result.CornerHold?"corner-hold":"confirmed-corner-exit";
            }
            if (!ready || now < attemptAfter || ultimate == null) { result.SkillReason = "cooldown"; return result; }
            int bossHits; SelectAim(origin, speed, threats, passable, ultimate, out result.Aim, out result.Hits, out bossHits);
            bool crowd = result.Surrounded || result.Emergency || result.Nearby >= 8 || result.Stunned && result.Nearby >= 3;
            // A ready cooldown by itself never warrants a cast. Boss casts need a
            // vulnerable target; ordinary casts need useful coverage of real units.
            result.Cast = result.Hits >= (result.Emergency ? 1 : 2) && crowd || bossHits > 0;
            if (result.Cast && stun != null && !result.Stunned && !result.Emergency && result.Target == stun &&
                Math.Max(0, Point.Distance(origin, stun.Position) - stun.PickupRadius) / speed < .7)
            {
                if (waitingStun != stun.Id) { waitingStun = stun.Id; stunWaitAt = now; }
                if (now - stunWaitAt < .85) { result.Cast = false; result.SkillReason = "collect-stun-first"; return result; }
            }
            result.SkillReason = result.Cast ? result.Stunned ? "stun-then-skill" : bossHits > 0 ? "boss-in-range" : "open-exit" : "no-useful-target";
            return result;
        }

        // Nearby path checks keep tactical objectives from pulling the movement
        // planner repeatedly toward a blocked pickup. The movement planner still
        // performs its full collision and warning checks on every executed step.
        static bool SafeApproach(Point origin, Point target, double speed, IList<Threat> threats, Func<Point, bool> passable, IList<AttackArea> warnings,bool controlled=false)
        {
            double length = Point.Distance(origin, target), seconds = length / speed;
            int samples = Math.Max(1, Math.Min(24, (int)Math.Ceiling(length / .5)));
            foreach (var area in warnings) if (area.Clearance(origin, 0) < 0) return false;
            for (int i = 1; i <= samples; i++)
            {
                double f = (double)i / samples, t = controlled?seconds*f:Math.Min(1.5, seconds * f);
                var p = new Point(origin.X + (target.X - origin.X) * f, origin.Z + (target.Z - origin.Z) * f);
                if (!passable(p)) return false;
                foreach (var area in warnings) if (area.Clearance(p, t) < .1) return false;
                foreach (var threat in threats)
                {
                    if(controlled && !threat.Boss && !threat.Projectile && threat.StunFor>t+.3)continue;
                    var q = threat.Boss ? BossKiting.Advance(threat, threat.Position, p, 0, t) : new Point(threat.Position.X + threat.Velocity.X * t, threat.Position.Z + threat.Velocity.Z * t);
                    if (Point.Distance(p, q) < threat.Radius + .6) return false;
                }
            }
            return true;
        }

        static double CrowdPressure(Point p,IList<Threat> threats,double time,out double gap)
        {
            gap=100;double pressure=0;
            foreach(var t in threats)if(!t.Boss&&!t.Projectile)
            {
                double travel=Math.Max(0,time-t.StunFor);
                var q=new Point(t.Position.X+t.Velocity.X*travel,t.Position.Z+t.Velocity.Z*travel);
                double g=Point.Distance(p,q)-t.Radius-.45;
                gap=Math.Min(gap,g);pressure+=1/Math.Pow(1+Math.Max(0,g),2);
            }
            return pressure;
        }
        static bool TryControlExit(Point origin,double speed,double stunRemaining,IList<Threat> threats,
            Func<Point,bool> passable,IList<AttackArea> warnings,Point? previous,BossArrival preparation,ArenaRoom room,bool strictCorner,out Point target)
        {
            target=new Point();double startGap;double start=CrowdPressure(origin,threats,0,out startGap);
            double duration=Math.Min(2.5,stunRemaining-.4);if(duration<.25)return false;
            var destinations=new List<Tuple<Point,double>>();
            for(int i=0;i<32;i++)
            {
                var dir=new Point(Math.Cos(i*Math.PI/16),Math.Sin(i*Math.PI/16));
                // Several travel lengths find an opening before a wall, without
                // requiring a whole remaining-stun-length straight line.
                for(int j=4;j>=1;j--)
                {
                    double time=duration*j/4;var end=new Point(origin.X+dir.X*speed*time,origin.Z+dir.Z*speed*time);
                    if(!passable(end))continue;
                    double gap;double pressure=CrowdPressure(end,threats,time,out gap);
                    if(gap<1.25 || pressure>=start*.7)continue;
                    if(strictCorner && (room==null || gap<3 ||
                        Math.Min(Math.Min(end.X-room.Min.X,room.Max.X-end.X),Math.Min(end.Z-room.Min.Z,room.Max.Z-end.Z))<Math.Min(8,room.Radius)))continue;
                    double score=pressure+1/Math.Max(1,gap);
                    if(preparation!=null && preparation.Active)score+=preparation.Cost(end)/100;
                    if(room!=null)score+=room.Cost(end)/100;
                    if(previous.HasValue){var old=new Point(previous.Value.X-origin.X,previous.Value.Z-origin.Z);double norm=Point.Distance(old,new Point());if(norm>1)score+=.15*(1-(dir.X*old.X+dir.Z*old.Z)/norm);}
                    destinations.Add(Tuple.Create(end,score));
                }
            }
            destinations.Sort((a,b)=>a.Item2.CompareTo(b.Item2));
            // Bound detailed route checks independently of the number of bodies.
            var directions=new List<Point>();
            foreach(var destination in destinations)
            {
                var delta=new Point(destination.Item1.X-origin.X,destination.Item1.Z-origin.Z);double length=Point.Distance(delta,new Point());
                var dir=new Point(delta.X/length,delta.Z/length);bool duplicate=false;
                foreach(var prior in directions)if(dir.X*prior.X+dir.Z*prior.Z>.94){duplicate=true;break;}
                if(duplicate)continue;
                directions.Add(dir);
                // Leave space for the character, not only the target's centre.
                // Otherwise the route validator rejects a seemingly open exit
                // whose stopping point still overlaps the arena boundary.
                var end=destination.Item1;const double margin=.6;
                bool fits=passable(new Point(end.X-margin,end.Z)) && passable(new Point(end.X+margin,end.Z)) &&
                    passable(new Point(end.X,end.Z-margin)) && passable(new Point(end.X,end.Z+margin));
                if(fits && (strictCorner?ConfirmedCornerPath(origin,end,speed,stunRemaining,threats,passable,warnings):SafeApproach(origin,end,speed,threats,passable,warnings,true)))
                {target=destination.Item1;return true;}
                if(directions.Count>=8)break;
            }
            return false;
        }

        // A future pickup is never borrowed as present protection. The whole
        // route and a stopping margin must fit the control already confirmed.
        // Every refresh rechecks new spawns, projectiles and warnings; if that
        // certificate disappears, the corner policy returns to standing still.
        static bool ConfirmedCornerPath(Point origin,Point target,double speed,double remaining,IList<Threat> threats,Func<Point,bool> passable,IList<AttackArea> warnings)
        {
            double length=Point.Distance(origin,target),duration=length/speed;
            if(duration+.65>remaining)return false;
            int samples=Math.Max(1,Math.Min(64,(int)Math.Ceiling(length/.35)));
            var previous=origin;
            for(int i=0;i<=samples;i++)
            {
                double t=duration*i/samples;var p=new Point(origin.X+(target.X-origin.X)*i/samples,origin.Z+(target.Z-origin.Z)*i/samples);
                if(!passable(p) || !passable(new Point(p.X-.6,p.Z)) || !passable(new Point(p.X+.6,p.Z)) ||
                    !passable(new Point(p.X,p.Z-.6)) || !passable(new Point(p.X,p.Z+.6)))return false;
                foreach(var area in warnings)if(area.Clearance(p,t)<.2)return false;
                foreach(var threat in threats)
                {
                    if(!threat.Projectile && !threat.Boss && threat.StunFor>duration+.65)continue;
                    double radius=threat.Radius+.75;
                    if(threat.Projectile)
                    {
                        var a=new Point(threat.Position.X+threat.Velocity.X*Math.Max(0,t-duration/samples),threat.Position.Z+threat.Velocity.Z*Math.Max(0,t-duration/samples));
                        var b=new Point(threat.Position.X+threat.Velocity.X*t,threat.Position.Z+threat.Velocity.Z*t);
                        if(CombatSimulation.SweptGap(previous,p,a,b,radius)<0)return false;
                    }
                    else
                    {
                        // Reachable envelope, not the velocity of a currently
                        // frozen enemy. Unknown movement speed is conservative.
                        double mobility=Math.Max(threat.MoveSpeed>0?threat.MoveSpeed:15,Point.Distance(threat.Velocity,new Point()));
                        double reach=radius+mobility*Math.Max(0,t+.65-threat.StunFor);
                        if(Point.Distance(p,threat.Position)<reach)return false;
                    }
                }
                previous=p;
            }
            return true;
        }

        public static double Utility(Point origin, Point end, double speed, TacticalDecision tactic)
        {
            if (tactic == null) return 0;
            double gain = 0;
            if(tactic.ControlledExit)
                gain+=Math.Max(-260,Math.Min(260,(Point.Distance(origin,tactic.ControlTarget)-Point.Distance(end,tactic.ControlTarget))*100));
            if (tactic.Target != null)
            {
                var d = tactic.Target;
                // Score crossing the contact circle, not just the 0.8-second end
                // point. Otherwise every full-speed move overshoots a close pickup
                // and standing still wins forever. Aim inside the circle as well.
                double radius = Math.Min(.25, d.PickupRadius);
                double before = Math.Max(0, Point.Distance(origin, d.Position) - radius);
                double after = Math.Max(0, Planner.SegmentDistance(d.Position, origin, end) - radius);
                gain += Math.Max(-260, Math.Min(260, (before - after) * (tactic.Reason == "heal" || tactic.Reason == "stun" ? 140 : 90)));
                if (before > 0 && Planner.SegmentDistance(d.Position, origin, end) <= d.PickupRadius) gain += 100;
                if(tactic.Stalling && (d.Type==1 || d.Type==5))gain*=tactic.LootScale;
            }
            foreach (var d in tactic.Reserved)
                if (Point.Distance(origin, d.Position) > d.PickupRadius && Planner.SegmentDistance(d.Position, origin, end) <= d.PickupRadius) gain -= 180;
            // This is only a directional preference. Never subtract live enemies
            // from collision checks on the assumption that the cast killed them.
            if (tactic.Escaping && !tactic.StunSafety) gain += 95 * ((end.X - origin.X) * tactic.Escape.X + (end.Z - origin.Z) * tactic.Escape.Z) / Math.Max(.1, speed);
            return gain;
        }

        static void SelectAim(Point origin, double speed, IList<Threat> threats, Func<Point, bool> passable, SkillOption skill, out Point aim, out int hits, out int bossHits)
        {
            aim = new Point(1, 0); hits = bossHits = 0; double best = double.NegativeInfinity;
            for (int i = 0; i < 32; i++)
            {
                double angle = i * Math.PI / 16; var dir = new Point(Math.Cos(angle), Math.Sin(angle));
                int count = 0, bosses = 0;
                foreach (var threat in threats) if (CanHit(origin, dir, threat, skill)) { count++; if (threat.Boss) bosses++; }
                double room = 0; for (int j = 1; j <= 3; j++) { if (!passable(new Point(origin.X + dir.X * speed * j * .4, origin.Z + dir.Z * speed * j * .4))) break; room++; }
                if (count == 0) continue;
                double score = count * 2 + bosses * 4 + room * 2;
                if (score > best) { best = score; aim = dir; hits = count; bossHits = bosses; }
            }
        }
        public static bool CanHit(Point origin, Point aim, Threat target, SkillOption skill)
        {
            if (target.Projectile || target.Boss && target.Motion == BossMotion.Jump) return false;
            double range = Math.Max(skill.Range, skill.ProjectileSpeed * skill.Duration);
            if (range <= 0) return false;
            double distance = Point.Distance(origin, target.Position);
            double time = skill.ProjectileSpeed > 0 ? Math.Min(.6, distance / skill.ProjectileSpeed) : 0;
            var pos = new Point(target.Position.X + target.Velocity.X * time, target.Position.Z + target.Velocity.Z * time);
            if (Point.Distance(origin, pos) - target.Radius > range) return false;
            if (skill.Subtype == 10 || skill.Subtype == 11)
            {
                int count = Math.Max(1, Math.Min(skill.Subtype == 10 ? 179 : 64, skill.AttackCount));
                // The fan's rays are fixed at 180/(count+1), while radial blades
                // start at world-forward. Rotating the player cannot rotate those.
                for (int k = 0; k < count; k++)
                {
                    double a = skill.Subtype == 10 ? Math.Atan2(aim.Z, aim.X) - Math.PI / 2 + (k + 1) * Math.PI / (count + 1) : Math.PI / 2 - k * 2 * Math.PI / count;
                    var end = new Point(origin.X + Math.Cos(a) * range, origin.Z + Math.Sin(a) * range);
                    if (Planner.SegmentDistance(pos, origin, end) <= target.Radius + .2) return true;
                }
                return false;
            }
            if (skill.TargetType != 3) return true;
            return Planner.SegmentDistance(pos, origin, new Point(origin.X + aim.X * range, origin.Z + aim.Z * range)) <= target.Radius + Math.Max(.2, skill.Size);
        }
    }
}

#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace Dustweave.Pandora
{
    public sealed class CombatBody
    {
        public int Id;
        public Point Position, Velocity, OriginOffset;
        public double Radius, Speed, Hp, Damage, AttackAfter, AttackInterval = 1, StunFor, MotionHoldFor, RefreshAfter, Lifetime=double.PositiveInfinity;
        public bool Projectile, Known, ColliderEnabled = true;
    }
    public enum AttackMotion { Fixed, Linear, Attached, Orbit }
    public sealed class CombatAttack
    {
        public int Id, SkillId, Pierce = int.MaxValue;
        public Point Position, Velocity, Axis = new Point(1, 0), OrbitCenter, OrbitOffset;
        public double Radius, HalfLength, HalfWidth, Damage, After, Interval, Lifetime, ExplosionRadius, AngularSpeed;
        public bool Box, Known;
        public AttackMotion Motion;
    }
    public sealed class WeaponClock
    {
        public int Id, Level, Subtype; public double Next, Period;
    }
    public sealed class CombatPickup
    {
        public int Id;
        public Point Position;
        public double Radius, Amount, Speed, Lifetime = double.PositiveInfinity;
        public bool Following;
    }
    public sealed class CombatFrame
    {
        public Point Player;
        public double Time, Hp, MaxHp, PlayerRadius = .45, Defence, InvulnerableFor, ErrorRadius = .12, RefreshSeconds = .08;
        public CombatBody[] Enemies = new CombatBody[0];
        public CombatAttack[] Attacks = new CombatAttack[0];
        public WeaponClock[] Weapons = new WeaponClock[0];
        public CombatPickup[] Healing = new CombatPickup[0];
        public int[][] Neighbors;
        public CombatFlow Flow;
        public Point MapMin, MapMax;
        public bool Bounded;
        public bool PreserveControlSpace;
        public bool ControlledExit;
        public Point ControlTarget;
        public ArenaRoom Room;
    }
    public sealed class CombatForecast
    {
        public Point End, NextDirection;
        public double Damage, ImmediateDamage, UrgentDamage, DiscountedDamage, FirstHit = double.PositiveInfinity, Gap = 100, Exposure, Kills, EscapeSpace, Horizon;
        public double Healing, RemainingHp, MinimumHp, NearHp, NearMinimumHp, LethalAt = double.PositiveInfinity;
        public double ControlDamage, ControlRisk;
        public bool Blocked;
        public int Steps, Hits;
        public Point[] EnemyPositions;
        public double Cost(double hp)
        {
            // A survivable early hit can be better than several later hits. Recovery
            // is credited at arrival, never before the damage that could prevent it.
            double loss = hp - RemainingHp, nearLoss = hp - NearHp;
            double fatal = LethalAt <= .6 ? Math.Max(1,hp)*40 : double.IsInfinity(LethalAt) ? 0 : Math.Max(1,hp)*2*Math.Exp(-1.25*(LethalAt-.6));
            return ImmediateDamage*.3 + DiscountedDamage*.7 + loss*2 + nearLoss*.5 +
                Math.Max(0,hp-MinimumHp)*.3 + Exposure*.25 + fatal + ControlRisk;
        }
    }
    public sealed class CombatWorkspace
    {
        internal Point[] Positions = new Point[0], Old = new Point[0], Velocities = new Point[0];
        internal double[] Health = new double[0], Ready = new double[0], Retarget = new double[0], HitDistance = new double[0], NextTick = new double[0];
        internal int[] HitOrder = new int[0], Used = new int[0];
        internal bool[,] Fired = new bool[0,0], Overlapping = new bool[0,0];
        internal Point[] PickupPositions = new Point[0];
        internal bool[] PickupFollowing = new bool[0], PickupTaken = new bool[0];
        internal void Prepare(int bodies, int attacks, int pickups)
        {
            if (Positions.Length < bodies)
            {
                int capacity = Math.Max(bodies, Math.Max(32, Positions.Length * 2));
                Positions = new Point[capacity]; Old = new Point[capacity]; Velocities = new Point[capacity];
                Health = new double[capacity]; Ready = new double[capacity]; Retarget = new double[capacity];
                HitDistance = new double[capacity]; HitOrder = new int[capacity];
            }
            if (Used.Length < attacks) { Used = new int[Math.Max(16, attacks)]; NextTick = new double[Used.Length]; }
            if (Fired.GetLength(0) < attacks || Fired.GetLength(1) < bodies)
            { Fired = new bool[Used.Length, Positions.Length]; Overlapping = new bool[Used.Length, Positions.Length]; }
            else { Array.Clear(Fired, 0, Fired.Length); Array.Clear(Overlapping, 0, Overlapping.Length); }
            Array.Clear(Used, 0, Used.Length);
            if(PickupPositions.Length<pickups) { PickupPositions=new Point[pickups];PickupFollowing=new bool[pickups];PickupTaken=new bool[pickups]; }
            else Array.Clear(PickupTaken,0,PickupTaken.Length);
        }
    }
    // Detached snapshots only. No Unity calls, mutation of native state, invented
    // future random targets, or averaging weapon damage into guaranteed kills.
    public static class CombatSimulation
    {
        static bool Clear(Point p, double radius, Func<Point,bool> passable)
        {
            return passable(p) && passable(new Point(p.X-radius,p.Z)) && passable(new Point(p.X+radius,p.Z)) &&
                passable(new Point(p.X,p.Z-radius)) && passable(new Point(p.X,p.Z+radius));
        }
        // Spatial capacity, not a promise that enemies will stay still. Stop at
        // the first blocked footprint: empty space beyond a wall is not an exit.
        // At most 32 samples per ray, independent of population and frame rate.
        public static double OpenDepth(Point origin, Point direction, double distance, double radius, Func<Point,bool> passable)
        {
            distance=Math.Max(0,Math.Min(10,distance));
            if(distance<1e-6 || !Clear(origin,radius,passable))return 0;
            int samples=Math.Max(1,(int)Math.Ceiling(distance/.35));
            double step=distance/samples,depth=0;
            for(int s=1;s<=samples;s++)
            {
                var p=Add(origin,Mul(direction,step*s));
                if(!Clear(p,radius,passable))break;
                depth=step*s;
            }
            return depth;
        }
        public static Point Add(Point a, Point b) => new Point(a.X + b.X, a.Z + b.Z);
        public static Point Sub(Point a, Point b) => new Point(a.X - b.X, a.Z - b.Z);
        public static Point Mul(Point p, double s) => new Point(p.X * s, p.Z * s);
        public static Point Unit(Point p) { double n = Math.Sqrt(p.X * p.X + p.Z * p.Z); return n < 1e-9 ? new Point() : Mul(p, 1 / n); }
        public static double Dot(Point a, Point b) => a.X * b.X + a.Z * b.Z;
        public static Point Rotate(Point p, double a) => new Point(p.X * Math.Cos(a) - p.Z * Math.Sin(a), p.X * Math.Sin(a) + p.Z * Math.Cos(a));
        public static double SweptGap(Point p0, Point p1, Point q0, Point q1, double radius)
        {
            var r = Sub(p0, q0); var v = Sub(Sub(p1, p0), Sub(q1, q0)); double vv = Dot(v, v);
            double t = vv < 1e-12 ? 0 : Math.Max(0, Math.Min(1, -Dot(r, v) / vv));
            return Point.Distance(Add(r, Mul(v, t)), new Point()) - radius;
        }
        public static Point Pursuit(CombatBody body, Point from, Point player, double t, double dt)
        {
            if (body.Projectile || !body.Known) return Add(from, Mul(body.Velocity, dt));
            if (t < body.StunFor) return from;
            if (t < body.MotionHoldFor || t < body.RefreshAfter) return Add(from, Mul(body.Velocity, dt));
            var delta = Sub(player, from); double distance = Point.Distance(player, from);
            // Native close-target stop: squared planar distance < .05.
            if (distance * distance < .05) return from;
            return Add(from, Mul(Unit(delta), Math.Min(distance, Math.Max(0, body.Speed) * dt)));
        }
        public static void Prepare(CombatFrame frame)
        {
            int n = frame.Enemies.Length; frame.Neighbors = new int[n][];
            var nearest = new int[7]; var distances = new double[7];
            for (int i = 0; i < n; i++)
            {
                int count = 0;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || frame.Enemies[j].Projectile) continue;
                    var delta = Sub(frame.Enemies[i].Position, frame.Enemies[j].Position); double square = Dot(delta, delta);
                    int at = count;
                    if (count == 7) { if (square >= distances[6]) continue; at = 6; }
                    while (at > 0 && square < distances[at - 1]) { if (at < 7) { nearest[at] = nearest[at - 1]; distances[at] = distances[at - 1]; } at--; }
                    nearest[at] = j; distances[at] = square; count = Math.Min(7, count + 1);
                }
                frame.Neighbors[i] = new int[count]; Array.Copy(nearest, frame.Neighbors[i], count);
            }
        }
        static Point AttackPosition(CombatAttack a, CombatFrame f, Point player, double t)
        { return a.Motion == AttackMotion.Orbit ? Add(Add(a.OrbitCenter,Sub(player,f.Player)),Rotate(a.OrbitOffset,a.AngularSpeed*t)) : a.Motion == AttackMotion.Attached ? Add(a.Position, Sub(player, f.Player)) : a.Motion == AttackMotion.Linear ? Add(a.Position, Mul(a.Velocity, t)) : a.Position; }
        static bool Slab(double origin,double delta,double extent,ref double enter,ref double exit)
        {
            if(Math.Abs(delta)<1e-9)return Math.Abs(origin)<=extent;
            double a=(-extent-origin)/delta,b=(extent-origin)/delta;
            enter=Math.Max(enter,Math.Min(a,b));exit=Math.Min(exit,Math.Max(a,b));return enter<=exit;
        }
        static bool SweptBox(CombatAttack a,Point from,Point to,Point enemyFrom,Point enemyTo,double error)
        {
            Point start=Sub(enemyFrom,from),delta=Sub(Sub(enemyTo,to),start);double enter=0,exit=1;
            var side=new Point(-a.Axis.Z,a.Axis.X);
            return Slab(Dot(start,a.Axis),Dot(delta,a.Axis),Math.Max(0,a.HalfLength-error),ref enter,ref exit) &&
                Slab(Dot(start,side),Dot(delta,side),Math.Max(0,a.HalfWidth-error),ref enter,ref exit);
        }
        static bool Contains(CombatAttack a, Point center, Point enemy, double error)
        {
            var d = Sub(enemy, center);
            // Inner geometry: do not promote broad-phase overlap into a certain hit.
            if (!a.Box) { double radius = Math.Max(0,a.Radius-error); return Dot(d,d) <= radius*radius; }
            return Math.Abs(Dot(d, a.Axis)) <= Math.Max(0, a.HalfLength - error) &&
                Math.Abs(d.X * -a.Axis.Z + d.Z * a.Axis.X) <= Math.Max(0, a.HalfWidth - error);
        }
        public static CombatForecast Run(CombatFrame f, Point first, Point second, Func<Point, double> speed,
            Func<Point, bool> passable, double horizon = 1.8, double turnAt = .6, bool useDamage = true, bool retainPositions = false, CombatWorkspace workspace = null, Func<double,Point> recordedPath = null, bool ignoreControl = false)
        {
            int n = f.Enemies.Length, count = useDamage?f.Attacks.Length:0;
            workspace = workspace ?? new CombatWorkspace(); workspace.Prepare(n, count, f.Healing.Length);
            var positions = workspace.Positions; var old = workspace.Old; var health = workspace.Health; var ready = workspace.Ready;
            var velocities = workspace.Velocities; var retarget = workspace.Retarget;
            var hitOrder = workspace.HitOrder; var hitDistance = workspace.HitDistance;
            var fired = workspace.Fired; var overlapping = workspace.Overlapping; var used = workspace.Used; var nextTick = workspace.NextTick;
            for (int i = 0; i < n; i++) { positions[i] = f.Enemies[i].Position; velocities[i] = f.Enemies[i].Velocity; retarget[i] = f.Enemies[i].RefreshAfter; health[i] = f.Enemies[i].Hp > 0 ? f.Enemies[i].Hp : double.PositiveInfinity; ready[i] = Math.Max(0, f.Enemies[i].AttackAfter); }
            for (int a = 0; a < count; a++)
            {
                nextTick[a] = Math.Max(0, f.Attacks[a].After);
                for (int i = 0; i < n; i++) overlapping[a, i] = Contains(f.Attacks[a], f.Attacks[a].Position, positions[i], 0);
            }
            for(int i=0;i<f.Healing.Length;i++) { workspace.PickupPositions[i]=f.Healing[i].Position;workspace.PickupFollowing[i]=f.Healing[i].Following; }
            var result = new CombatForecast { End = f.Player, NextDirection = second, RemainingHp=f.Hp, MinimumHp=f.Hp, NearHp=f.Hp, NearMinimumHp=f.Hp }; var player = f.Player;
            double firstSpeed = Math.Max(0,speed(first)), secondSpeed = Math.Max(0,speed(second));
            bool initialFlow = true; var flowTarget = player; double flowRefresh = f.Flow == null ? double.PositiveInfinity : f.Flow.RefreshAfter;
            double step = .075; horizon = Math.Max(.001, Math.Min(3, horizon));
            result.Horizon = horizon;
            for (double t = 0; t < horizon - .00001; t += step)
            {
                double dt = Math.Min(step, horizon - t); var d = t + .0001 < turnAt ? first : second;
                if (f.Flow != null && f.Flow.Valid && t >= flowRefresh) { initialFlow = false; flowTarget = player; flowRefresh = t + Math.Max(.025, f.Flow.RefreshSeconds); }
                var next = recordedPath == null ? Add(player, Mul(d, (t + .0001 < turnAt ? firstSpeed : secondSpeed) * dt)) : recordedPath(t+dt);
                bool startClear=Clear(player,f.PlayerRadius,passable);
                if (!Clear(next,f.PlayerRadius,passable) || startClear && !Clear(Add(player, Mul(Sub(next, player), .5)),f.PlayerRadius,passable)) { result.Blocked = true; result.End = player; return result; }
                Array.Copy(positions, old, n);
                for (int i = 0; i < n; i++)
                {
                    var body = f.Enemies[i]; if (health[i] <= 0) continue;
                    if(body.Projectile && t>=body.Lifetime) {health[i]=0;continue;}
                    if (body.Projectile || !body.Known || t < body.MotionHoldFor) { positions[i] = Add(old[i], Mul(body.Velocity, body.Projectile?Math.Min(dt,Math.Max(0,body.Lifetime-t)):dt)); continue; }
                    if (t < body.StunFor) continue;
                    if (t + .0001 >= retarget[i])
                    {
                        retarget[i] = t + Math.Max(.025, Math.Min(.25, f.RefreshSeconds));
                        var basePosition = Sub(old[i], body.OriginOffset);
                        var target = Sub(player, basePosition); var chase = f.Flow != null && f.Flow.Valid ? f.Flow.Direction(basePosition, flowTarget, initialFlow) : new Point();
                        if (Dot(chase,chase) < 1e-8) chase = Unit(target);
                        var separation = new Point();
                        if (f.Neighbors != null) foreach (int j in f.Neighbors[i])
                        {
                            if (health[j] <= 0) continue; var away = Sub(basePosition, Sub(old[j], f.Enemies[j].OriginOffset)); double square = Dot(away, away);
                            double reach = 1+f.Enemies[j].Radius;
                            if (square < 1e-8 || square > reach*reach) continue;
                            separation = Add(separation, Mul(away, (Dot(chase, away) >= 0 ? .5 : 1.5) / square));
                        }
                        var wish = Add(chase, separation); double length = Point.Distance(wish, new Point());
                        velocities[i] = Dot(target, target) < .05 || length * length < .2 ? new Point() : Mul(wish, Math.Max(0, body.Speed) / Math.Max(1, length));
                    }
                    positions[i] = Add(old[i], Mul(velocities[i], dt));
                }
                // Damage deadlines are processed conservatively: a same-step kill may
                // happen after contact and cannot erase a hit already predicted here.
                for (int i = 0; i < n; i++)
                {
                    var e = f.Enemies[i]; if (health[i] <= 0 || !e.ColliderEnabled) continue;
                    double margin = f.ErrorRadius + (e.Known && !e.Projectile ? Math.Min(.65, .12 * t * e.Speed) : .1 * t);
                    double gap = SweptGap(player, next, old[i], positions[i], e.Radius + f.PlayerRadius + margin);
                    result.Gap = Math.Min(result.Gap, gap);
                    double safeGap = gap+.3;
                    result.Exposure += dt * (gap < 0 ? 35 + -gap * 30 : 1.2 / (safeGap*safeGap));
                    if (gap > 0 || t + dt < ready[i] || !ignoreControl && t + dt <= e.StunFor || t + dt <= f.InvulnerableFor) continue;
                    // Crossing already-controlled bodies is permitted only along
                    // an observed escape/renewal route and before their real stun
                    // ends. New spawns and projectiles never inherit that control.
                    if(ignoreControl && f.ControlledExit && e.Known && !e.Projectile && t+dt+.3<e.StunFor &&
                        Point.Distance(next,f.ControlTarget)<Point.Distance(player,f.ControlTarget)-.001)continue;
                    if(ignoreControl && t<e.StunFor && Point.Distance(f.Player,e.Position)<=e.Radius+f.PlayerRadius+f.ErrorRadius &&
                        Point.Distance(player,old[i])<=e.Radius+f.PlayerRadius+margin &&
                        Point.Distance(next,positions[i])>Point.Distance(player,old[i])+.001)continue;
                    double damage = Math.Max(0, (int)e.Damage - (int)f.Defence);
                    if (!e.Known && damage <= 0) damage = Math.Max(1, f.Hp * .25);
                    result.Damage += damage; if (t < .6) result.ImmediateDamage += damage;
                    if(t < .35) result.UrgentDamage += damage;
                    // Future casts and unobserved knockback are not guaranteed.
                    // Discount remote risk instead of treating a pessimistic death
                    // after several unmodelled attacks as a certain present death.
                    result.DiscountedDamage += damage * Math.Exp(-1.25*t);
                    result.RemainingHp=Math.Max(0,result.RemainingHp-damage);
                    result.MinimumHp=Math.Min(result.MinimumHp,result.RemainingHp);
                    if(result.RemainingHp<=0) result.LethalAt=Math.Min(result.LethalAt,t);
                    if(t<.6) result.NearMinimumHp=Math.Min(result.NearMinimumHp,result.RemainingHp);
                    result.Hits++; result.FirstHit = Math.Min(result.FirstHit, t);
                    ready[i] = t + Math.Max(.075, e.AttackInterval);
                    if (e.Projectile) health[i] = 0;
                }
                if (useDamage) for (int a = 0; a < count; a++)
                {
                    var shot = f.Attacks[a]; if (!shot.Known || shot.Damage <= 0 || shot.Lifetime < t + dt || t + dt < shot.After || used[a] >= shot.Pierce) continue;
                    var from = AttackPosition(shot, f, player, t); var to = AttackPosition(shot, f, next, t + dt);
                    bool tick = shot.Interval > 0 && t + dt >= nextTick[a];
                    if (tick) nextTick[a] += Math.Max(.075, shot.Interval);
                    // Resolve linear pierce in travel order, independent of capture order.
                    int hits = 0; var axis = Unit(shot.Velocity);
                    for (int i = 0; i < n; i++)
                    {
                        var e = f.Enemies[i]; if (health[i] <= 0 || e.Projectile || !e.Known || !e.ColliderEnabled || used[a] >= shot.Pierce) continue;
                        bool inside = Contains(shot, to, positions[i], f.ErrorRadius);
                        bool crossed = shot.Motion == AttackMotion.Linear && (shot.Box ? SweptBox(shot,from,to,old[i],positions[i],f.ErrorRadius) :
                            SweptGap(from, to, old[i], positions[i], Math.Max(0, shot.Radius - f.ErrorRadius)) < 0);
                        bool hit = shot.Interval > 0 ? tick && inside : !fired[a, i] && !overlapping[a, i] && (inside || crossed);
                        overlapping[a, i] = inside;
                        if (!hit) continue;
                        hitOrder[hits] = i; hitDistance[hits++] = shot.Motion == AttackMotion.Linear ? Dot(Sub(old[i], from), axis) : i;
                    }
                    if (hits > 1 && shot.Motion == AttackMotion.Linear) Array.Sort(hitDistance, hitOrder, 0, hits);
                    for (int h = 0; h < hits && used[a] < shot.Pierce; h++)
                    {
                        int i = hitOrder[h]; if (health[i] <= 0) continue;
                        fired[a, i] = true; used[a]++; double before = health[i]; health[i] -= shot.Damage;
                        if (before > 0 && health[i] <= 0) result.Kills++;
                        // Explosion geometry is a lower bound; collider-order variation
                        // never increases the number of guaranteed contacts.
                        if (shot.ExplosionRadius > 0) for (int j = 0; j < n; j++) if (j != i && health[j] > 0 && f.Enemies[j].Known && !f.Enemies[j].Projectile && Point.Distance(positions[j], positions[i]) + f.ErrorRadius < shot.ExplosionRadius)
                        { before = health[j]; health[j] -= shot.Damage; if (before > 0 && health[j] <= 0) result.Kills++; }
                    }
                }
                // Native items first enter the pickup trigger, then travel toward
                // the player. They apply only after reaching the contact threshold.
                // Resolve damage first for a conservative same-step ordering.
                for(int i=0;i<f.Healing.Length;i++)
                {
                    var item=f.Healing[i];if(workspace.PickupTaken[i] || t+dt>item.Lifetime)continue;
                    var position=workspace.PickupPositions[i];
                    if(!workspace.PickupFollowing[i] && Planner.SegmentDistance(position,player,next)<=Math.Max(0,item.Radius-f.ErrorRadius)) workspace.PickupFollowing[i]=true;
                    if(!workspace.PickupFollowing[i])continue;
                    if(Point.Distance(position,next)<Math.Max(0,Math.Sqrt(.45)-f.ErrorRadius))
                    {
                        workspace.PickupTaken[i]=true;
                        if(result.RemainingHp>0) { double heal=Math.Min(Math.Max(0,item.Amount),Math.Max(0,Math.Max(f.Hp,f.MaxHp)-result.RemainingHp));result.Healing+=heal;result.RemainingHp+=heal; }
                    }
                    else workspace.PickupPositions[i]=Add(position,Mul(Unit(Sub(next,position)),Math.Min(Point.Distance(next,position),Math.Max(0,item.Speed)*dt)));
                }
                if(t<.6)result.NearHp=result.RemainingHp;
                player = next; result.Steps++;
            }
            result.End = player;
            if (retainPositions) { result.EnemyPositions = new Point[n]; Array.Copy(positions, result.EnemyPositions, n); }
            for (int k = 0; k < 12; k++)
            {
                var d = new Point(Math.Cos(k * Math.PI / 6), Math.Sin(k * Math.PI / 6));
                double reach=Math.Max(2,Math.Min(10,speed(d)));
                double depth=OpenDepth(player,d,reach,f.PlayerRadius+f.ErrorRadius,passable);
                for (int i = 0; depth>0 && i < n; i++)
                {
                    if(health[i]<=0 || !f.Enemies[i].ColliderEnabled)continue;
                    var offset=Sub(positions[i],player);double along=Dot(offset,d);
                    double radius=f.Enemies[i].Radius+f.PlayerRadius+f.ErrorRadius;
                    double square=Dot(offset,offset),side=Math.Max(0,square-along*along);
                    if(square<=radius*radius) { depth=0;break; }
                    if(along<=0 || side>=radius*radius)continue;
                    depth=Math.Min(depth,Math.Max(0,along-Math.Sqrt(radius*radius-side)));
                }
                // Area-equivalent depth: a shallow pocket must not count as a
                // complete direction. Controlled bodies still occupy this space.
                result.EscapeSpace+=(depth/reach)*(depth/reach);
            }
            if(!ignoreControl && f.PreserveControlSpace && f.Enemies.Any(e=>e.Known&&!e.Projectile&&e.StunFor>0))
            {
                // Keep the true HP forecast above, but do not relax route safety
                // just because an EMP currently prevents contact. Monsters stay
                // frozen at their real positions; entering their contact area is
                // still unsafe. Do not invent pursuit that would force fleeing
                // from an otherwise safe place during the control window.
                // Position safety cannot depend on a hoped-for clear during the
                // stun. Omitting friendly attacks also bounds this extra check.
                var posture=Run(f,first,second,speed,passable,Math.Min(.9,horizon),turnAt,false,false,workspace,recordedPath,true);
                result.ControlDamage=posture.Damage;
                // This is a space penalty, not fictitious HP loss or death.
                result.ControlRisk=posture.Exposure*.25+posture.Damage*2;
            }
            return result;
        }
    }
    public sealed class CombatRoutePlanner
    {
        public int Evaluations { get; private set; }
        public CombatForecast Selected { get; private set; }
        public CombatForecast Baseline { get; private set; }
        public Point Direction { get; private set; }
        readonly CombatWorkspace workspace = new CombatWorkspace();
        double selectedAt; bool hasDirection;
        public void Reset() { Evaluations = 0; Selected = Baseline = null; hasDirection = false; selectedAt = 0; }
        public Route Choose(CombatFrame frame, IList<Route> candidates, Point previous, Func<Point, double> speed, Func<Point, bool> passable)
        {
            Evaluations = 0;
            var shortlist = new List<Tuple<Route, CombatForecast, double>>();
            var immediate = new List<Tuple<Route, CombatForecast, double>>();
            foreach (var r in candidates)
            {
                var f = CombatSimulation.Run(frame, r.Direction, r.Direction, speed, passable, .45, workspace: workspace); Evaluations++;
                if (f.Blocked) continue;
                double cost = Score(frame, r, f, previous);
                immediate.Add(Tuple.Create(r, f, cost));
            }
            immediate.Sort((a,b)=>Compare(a,b));
            var roots = new List<Route>();
            foreach (var r in immediate)
            {
                if (roots.Count >= 9) break;
                // Retain diverse exits instead of spending the entire budget on a
                // tight cluster around a single loot pile.
                if (roots.Count > 0 && roots.Any(x=>CombatSimulation.Dot(x.Direction,r.Item1.Direction)>.94)) continue;
                roots.Add(r.Item1);
            }
            var continuation = immediate.FirstOrDefault(x=>CombatSimulation.Dot(x.Item1.Direction,previous)>.98);
            if (continuation != null && !roots.Contains(continuation.Item1)) roots.Add(continuation.Item1);
            foreach(var r in roots)
            {
                var f = CombatSimulation.Run(frame,r.Direction,r.Direction,speed,passable,workspace:workspace); Evaluations++;
                if (!f.Blocked) shortlist.Add(Tuple.Create(r,f,Score(frame,r,f,previous)));
            }
            if (immediate.Count == 0) { Selected = null; return null; }
            shortlist.Sort((a, b) => Compare(a,b));
            var best = shortlist.Count == 0 ? null : shortlist[0]; Baseline = best == null ? null : best.Item2;
            // A fixed branch count keeps work independent of frame rate and latency.
            // Only the first action is executed; every later action is revalidated.
            var branchRoots=(shortlist.Count==0?immediate:shortlist).Take(3).ToList();
            // A straight continuation hitting a wall is not proof that its first
            // command is unusable. Retain one such exit and try turning before it.
            var bend=immediate.FirstOrDefault(x=>roots.Contains(x.Item1)&&!shortlist.Any(y=>ReferenceEquals(y.Item1,x.Item1)));
            if(bend!=null && !branchRoots.Any(x=>ReferenceEquals(x.Item1,bend.Item1)))
            { if(branchRoots.Count==3)branchRoots.RemoveAt(2);branchRoots.Add(bend); }
            foreach (var root in branchRoots) foreach (double angle in new[] { -.8, .8, -1.55, 1.55 })
            {
                if (CombatSimulation.Dot(root.Item1.Direction, root.Item1.Direction) < .01) continue;
                var second = CombatSimulation.Rotate(root.Item1.Direction, angle);
                var forecast = CombatSimulation.Run(frame, root.Item1.Direction, second, speed, passable, workspace: workspace); Evaluations++;
                if (forecast.Blocked) continue;
                double score = Score(frame, root.Item1, forecast, previous) + 2;
                var candidate=Tuple.Create(root.Item1,forecast,score);
                if (best == null || Compare(candidate,best)<0) best = candidate;
            }
            // A verified opening can be closer than a full movement horizon. Test
            // reaching it and stopping, rather than rejecting the whole direction
            // because continuing beyond the opening would hit the arena boundary.
            // One extra branch only; waking enemies still use their real deadlines.
            if(frame.ControlledExit)
            {
                var offset=CombatSimulation.Sub(frame.ControlTarget,frame.Player);
                var toward=CombatSimulation.Unit(offset);
                var exit=immediate.OrderByDescending(x=>CombatSimulation.Dot(x.Item1.Direction,toward)).FirstOrDefault();
                if(exit!=null && CombatSimulation.Dot(exit.Item1.Direction,toward)>.94)
                {
                    double reach=CombatSimulation.Dot(offset,exit.Item1.Direction)/Math.Max(.1,speed(exit.Item1.Direction));
                    if(reach>=.15 && reach<=1.2)
                    {
                        var forecast=CombatSimulation.Run(frame,exit.Item1.Direction,new Point(),speed,passable,turnAt:reach,workspace:workspace);Evaluations++;
                        var candidate=Tuple.Create(exit.Item1,forecast,Score(frame,exit.Item1,forecast,previous)+2);
                        if(!forecast.Blocked && (best==null || Compare(candidate,best)<0))best=candidate;
                    }
                }
            }
            if (best == null || best.Item2.LethalAt<=.35 && immediate[0].Item2.LethalAt>.35) best = immediate[0];
            // Retain a comparable heading briefly, but recheck near-term survival
            // and its HP cost before accepting the earlier route again.
            if (hasDirection && frame.Time - selectedAt < .8)
            {
                var old = shortlist.Where(x => CombatSimulation.Dot(x.Item1.Direction, Direction) > .98).OrderBy(x => x.Item3).FirstOrDefault();
                // Do not spend the boss's approach window preserving a heading
                // that gives away the safe inward route's turning space.
                if (old != null && old.Item1.PositionCost<=best.Item1.PositionCost+2 &&
                    (frame.Room==null || frame.Room.Cost(old.Item2.End)<=frame.Room.Cost(best.Item2.End)+2) &&
                    !(old.Item2.ControlDamage>0 && best.Item2.ControlDamage==0) &&
                    !(frame.PreserveControlSpace && !double.IsInfinity(old.Item2.LethalAt) && double.IsInfinity(best.Item2.LethalAt)) &&
                    old.Item2.LethalAt>.6 && old.Item2.NearMinimumHp>=Math.Min(best.Item2.NearMinimumHp,frame.Hp*.5) && old.Item3 <= best.Item3 + Math.Max(12,frame.Hp*.08)) best = old;
            }
            if (!hasDirection || CombatSimulation.Dot(best.Item1.Direction, Direction) < .98) selectedAt = frame.Time;
            hasDirection = true; Direction = best.Item1.Direction; Selected = best.Item2;
            return new Route { Direction = best.Item1.Direction, Score = -best.Item3, LootGain = best.Item1.LootGain,
                WarningCost=best.Item1.WarningCost,PositionCost=best.Item1.PositionCost,RoomCost=best.Item1.RoomCost,
                Danger = Selected.Exposure, Collision = Selected.ImmediateDamage > 0, PredictionSeconds = Selected.Horizon,
                EscapeSpace = Selected.EscapeSpace, Reason = "short-horizon-combat" };
        }
        static double Score(CombatFrame frame, Route r, CombatForecast f, Point previous)
        {
            double dot = CombatSimulation.Dot(r.Direction, previous);
            return f.Cost(frame.Hp) + r.WarningCost * 5000 + r.PositionCost + (frame.Room==null?r.RoomCost:frame.Room.Cost(f.End)) - Math.Max(-200, Math.Min(200, r.LootGain)) * .35 - f.EscapeSpace * 4 + (1 - dot) * 9;
        }
        static int Compare(Tuple<Route,CombatForecast,double> a,Tuple<Route,CombatForecast,double> b)
        {
            int fatal=(a.Item2.LethalAt<=.35).CompareTo(b.Item2.LethalAt<=.35);
            if(fatal!=0)return fatal;
            if(a.Item2.ControlRisk>0 || b.Item2.ControlRisk>0)
            { int actual=(!double.IsInfinity(a.Item2.LethalAt)).CompareTo(!double.IsInfinity(b.Item2.LethalAt));if(actual!=0)return actual; }
            int control=(a.Item2.ControlDamage>0).CompareTo(b.Item2.ControlDamage>0);
            return control!=0?control:a.Item3.CompareTo(b.Item3);
        }
    }
    // Revalidate the first command with the newest frame. The background planner
    // can be one decision old; comparing only it and the legacy route is not enough.
    public sealed class CombatSafety
    {
        readonly CombatWorkspace workspace=new CombatWorkspace();
        public CombatForecast Selected { get; private set; }
        public int Evaluations { get; private set; }
        public Route Choose(CombatFrame frame, Route proposed, IList<Route> candidates, Point previous, Func<Point,double> speed, Func<Point,bool> clear)
        {
            Evaluations=1;
            var best=CombatSimulation.Run(frame,proposed.Direction,proposed.Direction,speed,clear,.35,workspace:workspace);
            var route=proposed;
            if(best.Blocked || best.Damage>0 || best.ControlDamage>0)
            {
                // Distant bodies cannot contact within this command. Keep an extra
                // separation halo, then validate the winner against the full frame.
                double maximumSpeed=Math.Max(speed(new Point(1,0)),speed(new Point(0,1)));
                var near=new CombatFrame { Player=frame.Player,Time=frame.Time,Hp=frame.Hp,MaxHp=frame.MaxHp,PlayerRadius=frame.PlayerRadius,
                    Defence=frame.Defence,InvulnerableFor=frame.InvulnerableFor,ErrorRadius=frame.ErrorRadius,RefreshSeconds=frame.RefreshSeconds,Flow=frame.Flow,Attacks=frame.Attacks,Healing=frame.Healing,PreserveControlSpace=frame.PreserveControlSpace,
                    ControlledExit=frame.ControlledExit,ControlTarget=frame.ControlTarget,Room=frame.Room,
                    Enemies=frame.Enemies.Where(e=>Point.Distance(e.Position,frame.Player)<=e.Radius+frame.PlayerRadius+frame.ErrorRadius+2.1+(maximumSpeed+Math.Max(e.Speed,Point.Distance(e.Velocity,new Point())))*.9).ToArray() };
                CombatSimulation.Prepare(near);
                best=CombatSimulation.Run(near,proposed.Direction,proposed.Direction,speed,clear,.35,workspace:workspace);Evaluations++;
                var choices=new List<Tuple<Route,CombatForecast,double>>();
                if(!best.Blocked) choices.Add(Tuple.Create(proposed,best,Risk(proposed,best,frame,previous)));
                foreach(var candidate in candidates)
                {
                    if(CombatSimulation.Dot(candidate.Direction,proposed.Direction)>.999)continue;
                    var forecast=CombatSimulation.Run(near,candidate.Direction,candidate.Direction,speed,clear,.35,workspace:workspace);Evaluations++;
                    if(forecast.Blocked)continue;
                    choices.Add(Tuple.Create(candidate,forecast,Risk(candidate,forecast,frame,previous)));
                }
                choices.Sort((a,b)=>a.Item3.CompareTo(b.Item3));
                // Compare a few onward exits, rather than forbidding every early
                // hit. A single affordable contact can open a route to recovery.
                var exits=new List<Route>();
                foreach(var c in choices)
                {
                    if(exits.Count==3)break;
                    if(!exits.Any(e=>CombatSimulation.Dot(e.Direction,c.Item1.Direction)>.94))exits.Add(c.Item1);
                }
                if(!exits.Contains(proposed))exits.Add(proposed);
                double score=double.PositiveInfinity;
                foreach(var candidate in exits)
                {
                    var forecast=CombatSimulation.Run(near,candidate.Direction,candidate.Direction,speed,clear,.9,workspace:workspace);Evaluations++;
                    if(forecast.Blocked)continue;
                    double value=Risk(candidate,forecast,frame,previous);
                    if(value<score){score=value;best=forecast;route=candidate;}
                }
                // The reduced scene is an optimization only. Recheck the selected
                // exit against every body before making any HP trade.
                var complete=CombatSimulation.Run(frame,route.Direction,route.Direction,speed,clear,.9,workspace:workspace);Evaluations++;
                if(complete.Blocked || complete.LethalAt<=.35)
                {
                    var safe=choices.FirstOrDefault(c=>c.Item2.LethalAt>.35);
                    if(safe!=null){route=safe.Item1;complete=CombatSimulation.Run(frame,route.Direction,route.Direction,speed,clear,.35,workspace:workspace);Evaluations++;}
                }
                best=complete;
            }
            Selected=best;
            if(ReferenceEquals(route,proposed))return proposed;
            return new Route {Direction=route.Direction,Score=route.Score,LootGain=route.LootGain,Danger=best.Exposure,
                WarningCost=route.WarningCost,PositionCost=route.PositionCost,RoomCost=route.RoomCost,Collision=best.Damage>0,PredictionSeconds=best.Horizon,Reason="fresh-health-route"};
        }
        static double Risk(Route route,CombatForecast forecast,CombatFrame frame,Point previous) => forecast.Cost(frame.Hp)+
            (forecast.ControlRisk>0 && forecast.LethalAt<=.9?Math.Max(1,frame.Hp)*100:0)+
            (forecast.ControlDamage>0?Math.Max(1,frame.Hp)*10:0)+
            (1-CombatSimulation.Dot(route.Direction,previous))*Math.Max(12,frame.Hp*.06)+route.WarningCost*5000+route.PositionCost+
            (frame.Room==null?route.RoomCost:frame.Room.Cost(forecast.End))-forecast.EscapeSpace*4;
    }
}

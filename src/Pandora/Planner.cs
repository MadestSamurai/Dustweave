#nullable disable
using System;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    public struct Point { public double X, Z; public Point(double x, double z) { X = x; Z = z; } public static double Distance(Point a, Point b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)); } }
    public sealed class Threat
    {
        public Point Position, Velocity, MotionEnd; public double Radius; public double Weight = 1; public bool Boss, Projectile;
        public int Id; public BossMotion Motion; public double MoveSpeed, MotionSpeed, PhaseSeconds, MotionDelay, WindupSpeed, StunFor;
        public bool DashAiming; public double DashRadius, DashRange;
        public bool JumpEvading, JumpTracking;
        public double JumpTrackStart, JumpTrackEnd, JumpImpactAfter, JumpAttackUntil, JumpRadius;
        public double PreferredGap = 6, ObservedDps, KillSeconds = double.PositiveInfinity; public bool DamageConfidence;
    }
    public sealed class Drop { public Point Position; public int Id; public int Type = 3; public double Amount = 1, PickupRadius = .6; public bool Following; }
    public sealed class Route { public Point Direction; public double Score, LootGain, Danger, WarningCost, PositionCost, RoomCost, BossClearance, EscapeSpace, PredictionSeconds, ObservedDps, PreferredGap, KillSeconds; public bool Collision; public string Reason; }
    public static class Planner
    {
        public static double PickupRadius(int type, double magnet, double contact) { return type == 3 || type == 4 ? Math.Max(.1, magnet) : Math.Max(.1, contact); }
        public static double LootValue(int type, double amount, double expUnit, int experienceDrops)
        {
            switch (type)
            {
                case 3: return Math.Min(30, Math.Max(.25, amount / Math.Max(1, expUnit))); // Experience, normalized to the common orb.
                case 4: return .08; // Coins do not improve survival.
                case 1: return Math.Min(35, 2 + experienceDrops * .35); // Magnet becomes valuable when experience has accumulated.
                case 2: return 4; // Screen damage pickup.
                case 5: return 15; // Skill chest.
                case 6: return 0; // EMP: reserve for crowd control; CombatTactics decides when.
                default: return 0;
            }
        }
        public static Route Choose(Point origin, double speed, IList<Threat> threats, IList<Drop> loot, Point previous, Func<Point, bool> passable, Point? region = null, IList<AttackArea> warnings = null, bool farmCurrency = false, TacticalDecision tactic = null, Func<Point, double> movementSpeed = null, CombatFrame frame = null, CombatRoutePlanner combat = null, IList<Route> candidates = null, BossArrival arrival = null,ArenaRoom room=null)
        {
            warnings = warnings ?? new AttackArea[0]; bool bossPresent = false; foreach (var n in threats) bossPresent |= n.Boss;
            if(!farmCurrency && !bossPresent && tactic!=null && tactic.CornerLocked)
            {
                bool exit=tactic.ControlledExit && !tactic.CornerHold;
                return new Route {Direction=exit?CombatSimulation.Unit(CombatSimulation.Sub(tactic.ControlTarget,origin)):new Point(),
                    Reason=exit?"confirmed-corner-exit":"corner-hold"};
            }
            if(farmCurrency || bossPresent)room=null;
            if (bossPresent) { loot = new Drop[0]; region = null; } // No discarded loot calculations during a boss forecast.
            if (tactic != null && (tactic.Target != null || tactic.Escaping)) region = null;
            if (tactic != null && tactic.Stalling) region = null;
            bool preparing = !farmCurrency && !bossPresent && arrival != null && arrival.Active;
            if(preparing) { region=null; loot=new Drop[0]; }
            Drop chest = null; foreach (var x in loot) if (x.Type == 5 && (chest == null || Point.Distance(origin, x.Position) < Point.Distance(origin, chest.Position))) chest = x;
            Route best = null; var bossCandidates = new List<Route>(); speed = Math.Max(.1, speed); const double horizon = .8;
            double expUnit = double.PositiveInfinity; int expCount = 0;
            foreach (var x in loot) if (x.Type == 3 && x.Amount > 0) { expUnit = Math.Min(expUnit, x.Amount); expCount++; }
            if (double.IsInfinity(expUnit)) expUnit = 1;
            var values = new double[loot.Count]; var before = new double[loot.Count]; double scale = Math.Max(2, speed * 2);
            for (int j = 0; j < loot.Count; j++) { values[j] = tactic != null && loot[j].Type != 3 && loot[j].Type != 4 ? 0 : farmCurrency && loot[j].Type == 4 ? Math.Min(18, 3 + Math.Sqrt(Math.Max(0, loot[j].Amount))) : LootValue(loot[j].Type, loot[j].Amount, expUnit, expCount); before[j] = Potential(Point.Distance(origin, loot[j].Position), loot[j].PickupRadius, scale); }
            if (tactic != null) chest = null; // Tactical selection already ranks chests against rescue pickups.
            for (int i = 0; i < 33; i++)
            {
                var d = i == 32 ? new Point(0, 0) : new Point(Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16));
                if (movementSpeed != null) speed = Math.Max(.1, movementSpeed(d));
                bool blocked = false, collision = false; double danger = 0;
                for (int k = 1; k <= 5; k++)
                {
                    double t = k * .16; var p = new Point(origin.X + d.X * speed * t, origin.Z + d.Z * speed * t);
                    if (!passable(p)) { blocked = true; break; }
                    foreach (var n in threats)
                    {
                        if (n.Boss) continue; // Pursuit and bounded dash motion are checked below.
                        var q = new Point(n.Position.X + n.Velocity.X * t, n.Position.Z + n.Velocity.Z * t); double gap = Point.Distance(p, q) - n.Radius - .45;
                        if (gap < 0) collision = true; danger += n.Weight * (gap < 0 ? 800 + (-gap) * 200 : 2 / ((gap + .35) * (gap + .35)));
                    }
                }
                if (blocked) continue;
                // Swept relative motion catches projectiles crossing between the five samples.
                foreach (var n in threats)
                {
                    if (n.Boss)
                    {
                        double bossGap = BossKiting.ImmediateGap(n, origin, d, speed, horizon);
                        if (bossGap < 0) collision = true;
                        danger += n.Weight * (bossGap < 0 ? 1200 + -bossGap * 300 : 2 / Math.Pow(bossGap + .35, 2));
                        continue;
                    }
                    double rx = origin.X - n.Position.X, rz = origin.Z - n.Position.Z, vx = d.X * speed - n.Velocity.X, vz = d.Z * speed - n.Velocity.Z;
                    double vv = vx * vx + vz * vz, t = vv < 1e-8 ? 0 : Math.Max(0, Math.Min(horizon, -(rx * vx + rz * vz) / vv));
                    double gap = Math.Sqrt((rx + vx * t) * (rx + vx * t) + (rz + vz * t) * (rz + vz * t)) - n.Radius - .45;
                    if (gap < 0) { collision = true; danger += n.Weight * (1200 + (-gap) * 300); }
                }
                var end = new Point(origin.X + d.X * speed * horizon, origin.Z + d.Z * speed * horizon); double gain = 0;
                for (int j = 0; j < loot.Count; j++)
                {
                    var x = loot[j]; double distance = Point.Distance(end, x.Position);
                    gain += values[j] * (Potential(distance, x.PickupRadius, scale) - before[j]) * 12;
                    // Score all drops swept into the pickup circle, not just the nearest endpoint.
                    if (Point.Distance(origin, x.Position) > x.PickupRadius && SegmentDistance(x.Position, origin, end) <= x.PickupRadius) gain += values[j] * 4;
                }
                // A large pile must never outweigh a predicted collision.
                gain = 180 * Math.Tanh(gain / 180);
                if(tactic!=null && !farmCurrency)gain*=tactic.LootScale;
                if (chest != null)
                {
                    // Explicit route to the nearest upgrade chest; hundreds of ordinary orbs cannot bury it.
                    double progress = Math.Max(0, Point.Distance(origin, chest.Position) - chest.PickupRadius) - Math.Max(0, SegmentDistance(chest.Position, origin, end) - chest.PickupRadius);
                    gain = Math.Max(-60, Math.Min(60, gain)) + Math.Max(-200, Math.Min(200, progress * 90));
                    if (SegmentDistance(chest.Position, origin, end) <= chest.PickupRadius) gain += 100;
                }
                if (chest == null && region.HasValue)
                {
                    double progress = Point.Distance(origin, region.Value) - SegmentDistance(region.Value, origin, end);
                    gain = Math.Max(-60, Math.Min(60, gain)) + Math.Max(-160, Math.Min(160, progress * 65));
                }
                double warningCost = AttackArea.RouteCost(origin, d, speed, horizon, warnings, passable);
                double bossClearance = double.PositiveInfinity, bossCost = 0;
                foreach (var n in threats) if (n.Boss)
                    {
                        var future = BossKiting.Advance(n, n.Position, end, 0, horizon);
                        bossClearance = Math.Min(bossClearance, Point.Distance(end, future) - n.Radius);
                        bossCost += n.DashAiming || n.JumpEvading ? -Point.Distance(end, future) * 120 : Math.Pow(Point.Distance(end, future) - n.Radius - n.PreferredGap, 2) * 20;
                    }
                // A short-route fallback also holds a useful distance; it must not
                // resurrect the old unbounded radial-retreat objective.
                double tacticalGain = CombatTactics.Utility(origin, end, speed, tactic);
                foreach (var n in threats) if (n.DashAiming || n.JumpEvading) { tacticalGain = 0; break; }
                if (bossPresent) gain = -bossCost - (i == 32 ? 300 : 0) + tacticalGain;
                else if (tactic != null) gain = (tactic.Target != null || tactic.Escaping ? Math.Max(-60, Math.Min(60, gain)) : gain) + tacticalGain;
                double positionCost=preparing?arrival.Cost(end):0;
                double roomCost=room==null?0:room.Cost(end);
                double score = gain - danger - positionCost - roomCost + .8 * (d.X * previous.X + d.Z * previous.Z) - (i == 32 ? .4 : 0);
                var candidate = new Route
                {
                    Direction = d,
                    Score = score,
                    LootGain = bossPresent ? tacticalGain : gain,
                    Danger = danger,
                    Collision = collision,
                    WarningCost = warningCost,
                    PositionCost = positionCost,
                    RoomCost = roomCost,
                    BossClearance = bossPresent ? bossClearance : 0,
                    Reason = warnings.Count > 0 ? "快速脱离Boss攻击范围" : bossPresent ? "远离Boss，沿安全方向移动" : danger > 80 ? "避开近身威胁" : preparing ? "Boss即将出现，离开边角留出退路" : tactic != null && tactic.Target != null ? "按当前需要拾取道具" : tactic != null && tactic.Escaping ? "利用缺口绕行脱围" : chest != null ? "优先拾取升级宝箱" : region.HasValue ? "收完当前经验区域" : expCount > 0 ? "优先收集成片经验" : loot.Count > 0 ? "拾取有用道具" : "移动避敌"
                };
                if (Better(candidate, best)) best = candidate;
                if(candidates != null) candidates.Add(candidate);
                if (bossPresent || frame != null) bossCandidates.Add(candidate);
            }
            if (bossPresent) best = BossKiting.Choose(origin, speed, threats, warnings, bossCandidates, previous, passable, movementSpeed) ?? best;
            else if (frame != null && combat != null) best = combat.Choose(frame, bossCandidates, previous, movementSpeed ?? (_ => speed), passable) ?? best;
            return best ?? new Route { Direction = new Point(0, 0), Score = -1e9, Reason = "等待可通行方向" };
        }
        // Collision-free exits first; warning exposure then dominates rewards and boss distance.
        static bool Better(Route a, Route b)
        {
            if (b == null) return true;
            if (a.Collision != b.Collision) return !a.Collision;
            if (a.Collision) return a.Score - a.WarningCost * 5000 > b.Score - b.WarningCost * 5000;
            if (Math.Abs(a.WarningCost - b.WarningCost) > .002) return a.WarningCost < b.WarningCost;
            return a.Score > b.Score;
        }
        static double Potential(double distance, double radius, double scale) { return 1 / (1 + Math.Max(0, distance - radius) / scale); }
        public static double SegmentDistance(Point p, Point a, Point b) { double x = b.X - a.X, z = b.Z - a.Z, l = x * x + z * z, t = l < 1e-8 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * x + (p.Z - a.Z) * z) / l)); return Point.Distance(p, new Point(a.X + x * t, a.Z + z * t)); }
    }
}

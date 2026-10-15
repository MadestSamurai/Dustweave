#nullable disable
using System;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    public enum BossMotion { Pursuit, Hold, Dash, Jump }

    // HP deltas, not nominal weapon damage. Each boss/round owns its samples.
    // The caller supplies an active clock: upgrades and pause menus cannot dilute DPS.
    public sealed class BossDamageModel
    {
        sealed class Sample { public double Time, Seconds, Damage, Gap; public bool Vulnerable; }
        sealed class Track
        {
            public double Time, Hp, Gap; public bool Vulnerable;
            public readonly Queue<Sample> Samples = new Queue<Sample>();
        }
        readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        public void Reset() { tracks.Clear(); }
        public void Forget(int id) { tracks.Remove(id); }
        public void Observe(Threat boss, double now, double gap, double hp, bool vulnerable, double fallbackRange, double playerHealth)
        {
            if (double.IsNaN(hp) || double.IsInfinity(hp) || hp < 0) return;
            Track t;
            if (!tracks.TryGetValue(boss.Id, out t) || now < t.Time || now - t.Time > 1 || hp > t.Hp + .01)
            {
                if (tracks.Count >= 32) tracks.Clear();
                tracks[boss.Id] = t = new Track { Time = now, Hp = hp, Gap = gap, Vulnerable = vulnerable };
            }
            double elapsed = now - t.Time;
            if (elapsed >= .04)
            {
                t.Samples.Enqueue(new Sample { Time = now, Seconds = elapsed, Damage = Math.Max(0, t.Hp - hp), Gap = (gap + t.Gap) * .5, Vulnerable = vulnerable && t.Vulnerable });
                t.Time = now; t.Hp = hp; t.Gap = gap; t.Vulnerable = vulnerable;
            }
            while (t.Samples.Count > 0 && (t.Samples.Peek().Time < now - 12 || t.Samples.Count > 256)) t.Samples.Dequeue();
            double duration = 0, damage = 0, observed = 0, vulnerableSeconds = 0; var times = new double[10]; var hits = new double[10];
            foreach (var s in t.Samples)
            {
                observed += s.Seconds;
                if (s.Time >= now - 6) { duration += s.Seconds; damage += s.Damage; }
                if (!s.Vulnerable) continue;
                vulnerableSeconds += s.Seconds;
                int bin = Math.Max(0, Math.Min(9, (int)(s.Gap / 1.5)));
                times[bin] += s.Seconds; hits[bin] += s.Damage;
            }
            boss.ObservedDps = duration >= 1.5 ? damage / duration : 0;
            boss.DamageConfidence = duration >= 1.5 && damage > 0;
            boss.KillSeconds = boss.DamageConfidence ? hp / boss.ObservedDps : double.PositiveInfinity;
            double range = Math.Max(3, Math.Min(9, fallbackRange)), best = 0;
            for (int i = 0; i < times.Length; i++) if (times[i] >= 1.5) best = Math.Max(best, hits[i] / times[i]);
            // Use the farthest measured band retaining >=90% of the best observed DPS.
            // Unobserved distances are not treated as zero damage or as an invitation to approach.
            if (best > 0) for (int i = 0; i < times.Length; i++)
                if (times[i] >= 1.5 && hits[i] / times[i] >= best * .9) range = Math.Max(2.75, Math.Min(10, (i + .5) * 1.5));
            if (best == 0 && observed > 4 && vulnerableSeconds > 3)
                range = Math.Max(2.75, range - Math.Min(2.5, (observed - 4) * .4));
            double margin = !boss.DamageConfidence ? .75 : boss.KillSeconds > 10 ? .65 : boss.KillSeconds < 3 ? -.35 : .2;
            if (playerHealth < .4) margin += 1;
            boss.PreferredGap = Math.Max(2.75, Math.Min(11, range + margin));
        }
    }

    public static class BossKiting
    {
        const int Width = 12;
        const double Step = .8;
        sealed class Node
        {
            public Route Root; public Point Player, Direction; public Point[] Bosses;
            public double Cost, Time, Space, Gap;
        }
        public static Point Advance(Threat boss, Point from, Point player, double time, double dt)
        {
            double phaseLeft = Math.Max(0, boss.PhaseSeconds - time);
            double active = Math.Min(dt, phaseLeft);
            if (boss.Motion == BossMotion.Dash && active > 0)
                from = Toward(from, boss.MotionEnd, Math.Max(0, boss.MotionSpeed) * active);
            else if (boss.Motion == BossMotion.Jump && active > 0)
                from = Toward(from, player, Math.Max(0, boss.MotionSpeed) * active);
            // A known wind-up/recovery is stationary only for its remaining duration.
            // Beyond that, use pursuit as an uncertainty envelope; never invent a next skill.
            double chase = boss.Motion == BossMotion.Pursuit ? dt : dt - active;
            return Toward(from, player, Math.Max(0, boss.MoveSpeed) * chase);
        }
        static Point Toward(Point a, Point b, double distance)
        {
            double length = Point.Distance(a, b), f = length < 1e-9 ? 0 : Math.Min(1, distance / length);
            return new Point(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f);
        }
        public static double ImmediateGap(Threat boss, Point player, Point direction, double speed, double horizon)
        {
            var p = player; var b = boss.Position; double clear = double.PositiveInfinity;
            for (double t = 0; t < horizon - .00001; t += .1)
            {
                double dt = Math.Min(.1, horizon - t);
                var q = new Point(p.X + direction.X * speed * dt, p.Z + direction.Z * speed * dt);
                var next = Advance(boss, b, q, t, dt);
                clear = Math.Min(clear, SegmentGap(p, q, b, next) - boss.Radius - .45);
                p = q; b = next;
            }
            return clear;
        }
        // Fixed 3-step, 12-wide beam: 33 first moves + at most 216 continuations.
        // Only the first direction is executed; every observation replans from real positions.
        public static Route Choose(Point origin, double speed, IList<Threat> threats, IList<AttackArea> warnings,
            IList<Route> candidates, Point previous, Func<Point, bool> passable)
        {
            var bosses = new List<Threat>(); foreach (var t in threats) if (t.Boss) bosses.Add(t);
            if (bosses.Count == 0 || candidates.Count == 0) return null;
            var beam = new List<Node>();
            var initial = new Point[bosses.Count]; for (int i = 0; i < initial.Length; i++) initial[i] = bosses[i].Position;
            var seed = new Node { Player = origin, Bosses = initial, Direction = previous };
            foreach (var route in candidates)
            {
                var node = Extend(seed, route.Direction, route, speed, bosses, threats, warnings, passable);
                if (node == null) continue;
                node.Cost += Math.Min(500, route.Danger * .015);
                node.Cost -= route.LootGain; // Only contextual rescue/item objectives enter boss forecasts.
                if (Math.Abs(route.Direction.X) + Math.Abs(route.Direction.Z) < .01) node.Cost += 180;
                beam.Add(node);
            }
            Trim(beam);
            if (beam.Count == 0) return null;
            for (int depth = 1; depth < 3; depth++)
            {
                var next = new List<Node>();
                foreach (var node in beam)
                    for (int angle = 0; angle < 9; angle++)
                    {
                        double a = Math.Atan2(node.Direction.Z, node.Direction.X) + angle * Math.PI / 4;
                        var dir = angle == 8 ? new Point() : new Point(Math.Cos(a), Math.Sin(a));
                        var child = Extend(node, dir, node.Root, speed, bosses, threats, warnings, passable);
                        if (child != null) next.Add(child);
                    }
                if (next.Count == 0) break;
                Trim(next); beam = next;
            }
            var best = beam[0]; var result = best.Root;
            result.Score = -best.Cost; result.BossClearance = best.Gap; result.EscapeSpace = best.Space; result.PredictionSeconds = best.Time;
            Threat nearest = bosses[0]; foreach (var b in bosses) if (Point.Distance(origin, b.Position) < Point.Distance(origin, nearest.Position)) nearest = b;
            result.ObservedDps = nearest.ObservedDps; result.KillSeconds = nearest.KillSeconds; result.PreferredGap = nearest.PreferredGap;
            result.Reason = result.WarningCost > .002 ? "避开当前BOSS攻击" : best.Space < .5 ? "绕出墙角，保留退路" : "保持输出距离，绕行牵制BOSS";
            return result;
        }
        static Node Extend(Node parent, Point direction, Route root, double speed, IList<Threat> bosses,
            IList<Threat> threats, IList<AttackArea> warnings, Func<Point, bool> passable)
        {
            var locations = (Point[])parent.Bosses.Clone(); var p = parent.Player;
            double cost = parent.Cost, gap = double.PositiveInfinity;
            for (int sample = 1; sample <= 4; sample++)
            {
                double time = parent.Time + (sample - 1) * .2;
                var q = new Point(p.X + direction.X * speed * .2, p.Z + direction.Z * speed * .2);
                if (!passable(q)) return null;
                for (int b = 0; b < bosses.Count; b++)
                {
                    var boss = bosses[b]; var next = Advance(boss, locations[b], q, time, .2);
                    // Continuous relative segment clearance also catches a fast dash between samples.
                    double distance = SegmentGap(p, q, locations[b], next) - boss.Radius - .45;
                    double uncertainty = .12 * (time + .2);
                    double clear = distance - uncertainty;
                    cost += clear < 0 ? 1800 + -clear * 900 : 5 / Math.Pow(clear + .3, 2);
                    double endGap = Point.Distance(q, next) - boss.Radius;
                    double wanted = boss.PreferredGap > 0 ? boss.PreferredGap : 6;
                    cost += .2 * (Math.Pow(Math.Max(0, wanted - endGap), 2) * 11 + Math.Pow(Math.Max(0, endGap - wanted - 1), 2) * 4);
                    locations[b] = next; gap = sample == 4 && b == 0 ? endGap : Math.Min(gap, endGap);
                }
                // Long-range missile/ordinary-unit predictions are soft costs; current
                // collisions and attack warnings still dominate through the root comparator.
                if (parent.Time > 0) foreach (var threat in threats) if (!threat.Boss)
                {
                    var a = new Point(threat.Position.X + threat.Velocity.X * time, threat.Position.Z + threat.Velocity.Z * time);
                    var b = new Point(a.X + threat.Velocity.X * .2, a.Z + threat.Velocity.Z * .2);
                    double reach = speed * .2 + (Math.Abs(threat.Velocity.X) + Math.Abs(threat.Velocity.Z)) * .2 + threat.Radius + .65;
                    if (Math.Abs(p.X - a.X) > reach || Math.Abs(p.Z - a.Z) > reach) continue;
                    double clear = SegmentGap(p, q, a, b) - threat.Radius - .45;
                    if (clear < .2) cost += (Math.Max(0, .2 - clear) * 250 + 50) * threat.Weight;
                }
                foreach (var area in warnings)
                {
                    double penetration = -Math.Min(area.Clearance(p, time), area.Clearance(q, time + .2));
                    if (penetration > 0) cost += (1 + penetration) * 500 * .2;
                }
                p = q;
            }
            // Count usable escape sectors, including a second point so we turn before
            // reaching the wall, not after straight retreat becomes impossible.
            double open = 0;
            for (int i = 0; i < 8; i++)
            {
                double x = Math.Cos(i * Math.PI / 4), z = Math.Sin(i * Math.PI / 4);
                if (!passable(new Point(p.X + x * speed * .5, p.Z + z * speed * .5))) continue;
                open += .5;
                if (passable(new Point(p.X + x * speed, p.Z + z * speed))) open += .5;
            }
            cost += (8 - open) * 22 + Math.Max(0, 4 - open) * 90;
            cost += 5 * (1 - direction.X * parent.Direction.X - direction.Z * parent.Direction.Z);
            return new Node { Root = root, Player = p, Direction = direction, Bosses = locations, Cost = cost, Time = parent.Time + Step, Space = open / 8, Gap = gap };
        }
        static double SegmentGap(Point p, Point q, Point a, Point b)
        {
            double x = p.X - a.X, z = p.Z - a.Z, dx = q.X - p.X - b.X + a.X, dz = q.Z - p.Z - b.Z + a.Z;
            double length = dx * dx + dz * dz, t = length < 1e-9 ? 0 : Math.Max(0, Math.Min(1, -(x * dx + z * dz) / length));
            return Math.Sqrt((x + dx * t) * (x + dx * t) + (z + dz * t) * (z + dz * t));
        }
        static int Compare(Node a, Node b)
        {
            if (a.Root.Collision != b.Root.Collision) return a.Root.Collision ? 1 : -1;
            // Quantized ranks, rather than pairwise epsilon comparisons, keep the
            // beam sort transitive when three warning costs are very close together.
            double aWarning = Math.Round(a.Root.WarningCost / .002), bWarning = Math.Round(b.Root.WarningCost / .002);
            if (!a.Root.Collision && aWarning != bWarning) return aWarning.CompareTo(bWarning);
            return (a.Cost + a.Root.WarningCost * 5000).CompareTo(b.Cost + b.Root.WarningCost * 5000);
        }
        static void Trim(List<Node> nodes) { nodes.Sort(Compare); if (nodes.Count > Width) nodes.RemoveRange(Width, nodes.Count - Width); }
    }
}

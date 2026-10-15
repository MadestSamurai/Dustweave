#nullable disable
using System;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    // A conservative horizontal footprint swept along A--B. A==B represents a circle.
    // Radius already includes the player's body and a small clearance margin.
    public sealed class AttackArea
    {
        public Point A, B, Velocity;
        public double Radius;
        public double ActiveFor = double.PositiveInfinity;
        public int BossId, PatternId;
        public string Kind;
        public double Clearance(Point p, double time = 0)
        {
            if (time > ActiveFor) return double.PositiveInfinity;
            double ax = A.X + Velocity.X * time, az = A.Z + Velocity.Z * time, dx = B.X - A.X, dz = B.Z - A.Z;
            double l = dx * dx + dz * dz, t = l < 1e-10 ? 0 : Math.Max(0, Math.Min(1, ((p.X - ax) * dx + (p.Z - az) * dz) / l));
            double x = p.X - ax - dx * t, z = p.Z - az - dz * t; return Math.Sqrt(x * x + z * z) - Radius;
        }
        public static double RouteCost(Point start, Point direction, double speed, double horizon, IList<AttackArea> areas, Func<Point, bool> passable)
        {
            var end = new Point(start.X + direction.X * speed * horizon, start.Z + direction.Z * speed * horizon);
            double exposure = Exposure(start, end, horizon, areas); bool inside = false;
            foreach (var a in areas) inside |= a.Clearance(start) < 0;
            if (!inside) return exposure;
            // A short horizon alone can prefer standing against a wall to first moving through
            // the middle of a large warning. Look through to an actual reachable exit instead.
            for (int i = 1; i <= 80; i++)
            {
                double t = i * .05; var p = new Point(start.X + direction.X * speed * t, start.Z + direction.Z * speed * t);
                if (!passable(p)) break;
                // An expiring warning is not a physical escape: do not wait inside an
                // attack merely because its remaining animation is shorter than the exit.
                bool clear = true; foreach (var a in areas) if (a.Clearance(p, Math.Min(t, a.ActiveFor)) < .1) { clear = false; break; }
                if (clear) return t * 10 + exposure * .05;
            }
            return 1000 + exposure;
        }
        public static double Exposure(Point start, Point end, double horizon, IList<AttackArea> areas)
        {
            double cost = 0;
            foreach (var a in areas)
            {
                // Integrate penetration rather than only checking the endpoint: no crossing a warning
                // to reach loot, no re-entry after leaving, and shortest-side exits from a dash lane.
                double travel = Point.Distance(start, end) + Math.Sqrt(a.Velocity.X * a.Velocity.X + a.Velocity.Z * a.Velocity.Z) * horizon;
                int steps = Math.Max(16, Math.Min(128, (int)Math.Ceiling(travel / Math.Max(.05, a.Radius * .4))));
                for (int i = 1; i <= steps; i++)
                {
                    double f = (i - .5) / steps; var p = new Point(start.X + (end.X - start.X) * f, start.Z + (end.Z - start.Z) * f);
                    double depth = -a.Clearance(p, horizon * f);
                    if (depth > 0) cost += (1 + depth) * horizon / steps;
                }
            }
            return cost;
        }
    }
}

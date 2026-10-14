#nullable disable
using System;
using System.Linq;
using System.Collections.Generic;
namespace Dustweave.Pandora
{
    // A committed set of drops. Re-evaluating local density cannot silently change its destination.
    public sealed class RegionPlanner
    {
        readonly HashSet<int> members = new HashSet<int>();
        public Point Anchor, Target;
        public bool Active { get; private set; }
        public int Switches { get; private set; }
        public double RemainingFraction { get; private set; }
        public string Reason { get; private set; } = "未选择区域";
        double originalValue, initialRate, radius, holdUntil, lastProgress, bestDistance, lastRemaining;
        public void Reset() { Active = false; members.Clear(); Switches = 0; }
        static double Value(Drop x) { return Math.Max(1, x.Amount); }
        sealed class Cluster { public Point Center; public List<Drop> Members; public double Value, Rate; }
        public Point? Select(Point origin, double speed, IList<Drop> drops, IList<Threat> threats, double now)
        {
            speed = Math.Max(.1, speed); var exp = drops.Where(x => x.Type == 3).ToArray();
            if (exp.Length == 0) { Active = false; members.Clear(); RemainingFraction = 0; Reason = "当前区域已收空"; return null; }
            if (Active)
            {
                var left = exp.Where(x => members.Contains(x.Id) && Point.Distance(x.Position, Anchor) <= radius * 1.5).ToArray();
                double remaining = left.Sum(Value); RemainingFraction = remaining / Math.Max(1, originalValue);
                if (left.Length == 0 || RemainingFraction <= .10) { Active = false; Reason = "当前区域已基本收完"; }
                else
                {
                    // Clear an actual remaining drop, not the empty center between surrounding drops.
                    var next = left.OrderBy(x => Point.Distance(origin, x.Position)).First(); Target = next.Position;
                    if (remaining < lastRemaining) { lastProgress = now; bestDistance = Point.Distance(origin, Target); }
                    lastRemaining = remaining;
                    double distance = Point.Distance(origin, Target);
                    if (distance < bestDistance - .3) { bestDistance = distance; lastProgress = now; }
                    bool danger = threats.Any(x => Point.Distance(x.Position, Target) < x.Radius + 1.3);
                    if (danger && now - lastProgress > 2) { Active = false; Reason = "区域被敌人占据，改找安全区域"; }
                    else if (now - lastProgress > 5) { Active = false; Reason = "持续无法接近，重新选择区域"; }
                }
            }
            double clusterRadius = Math.Max(3, exp.Max(x => x.PickupRadius) + 1.2); Cluster best = null;
            // Deterministic spatial seeds keep candidate count bounded while preserving distant piles.
            var seeds = exp.GroupBy(x => Tuple.Create((int)Math.Floor(x.Position.X / clusterRadius), (int)Math.Floor(x.Position.Z / clusterRadius))).Select(g => g.First());
            foreach (var seed in seeds)
            {
                var group = exp.Where(x => Point.Distance(x.Position, seed.Position) <= clusterRadius).ToList(); double value = group.Sum(Value);
                var center = new Point(group.Sum(x => x.Position.X * Value(x)) / value, group.Sum(x => x.Position.Z * Value(x)) / value);
                double risk = threats.Where(x => Point.Distance(x.Position, center) < x.Radius + 3).Sum(x => x.Weight);
                double rate = value / (1 + Point.Distance(origin, center) / speed) / (1 + risk * 2);
                if (best == null || rate > best.Rate) best = new Cluster { Center = center, Members = group, Value = value, Rate = rate };
            }
            bool change = !Active;
            if (Active && now >= holdUntil && Point.Distance(best.Center, Anchor) > radius * 1.5)
            {
                double extraDistance = Math.Max(0, Point.Distance(origin, best.Center) + Point.Distance(best.Center, Target) - Point.Distance(origin, Target));
                double net = best.Value - extraDistance / speed * initialRate;
                // Compare with the ORIGINAL commitment, not the dwindling leftovers; include returning to it.
                change = net > originalValue * 2 && best.Rate > initialRate * 1.8;
                if (change) Reason = "新区域收益覆盖往返成本";
            }
            if (change)
            {
                if (members.Count > 0) Switches++; members.Clear(); foreach (var x in best.Members) members.Add(x.Id);
                Anchor = best.Center; Target = best.Members.OrderBy(x => Point.Distance(origin, x.Position)).First().Position; radius = clusterRadius; originalValue = lastRemaining = best.Value; initialRate = best.Rate;
                Active = true; RemainingFraction = 1; holdUntil = now + 3; lastProgress = now; bestDistance = Point.Distance(origin, Target);
                if (Reason == "未选择区域" || Reason == "当前区域已收空") Reason = "选定经验区域，持续收集";
            }
            return Active ? (Point?)Target : null;
        }
    }
}

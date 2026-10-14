#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace Dustweave.Pandora
{
    public sealed class ComboPlan
    {
        public int WeaponId, PassiveId, CurrentLevel, MaximumLevel, RemainingSteps;
        public string WeaponName, MaximumName, PassiveName;
        public bool NeedWeapon, NeedPassive, Complete, Feasible, Planned;
        public double FutureValue;
    }
    public sealed class ComboScore { public double Bonus, OpportunityCost; public string Reason = ""; }
    public static class ComboPlanner
    {
        // The server still determines offered upgrades and chest rewards. This is a build plan,
        // not permission to grant an evolution or to assume a chest has predetermined contents.
        public static ComboPlan[] Plans(SkillContext c)
        {
            if (c.Catalog.Count == 0) return new ComboPlan[0];
            int weaponFree = Math.Max(0, c.Capacity - c.Owned.Count(x => x.Type == 1));
            int passiveFree = Math.Max(0, c.Capacity - c.Owned.Count(x => x.Type == 2));
            double total = Math.Max(1, c.Owned.Sum(x => SkillPlanner.WeaponPower(x, c)));
            var plans = new List<ComboPlan>();
            foreach (var group in c.Catalog.Where(x => x.Type == 1 && x.SynergyId > 0).GroupBy(x => x.Id))
            {
                var first = group.OrderBy(x => x.Level).First(); var final = group.FirstOrDefault(x => x.Level == x.MaxLevel);
                var passive = c.Catalog.FirstOrDefault(x => x.Id == first.SynergyId && x.Type == 2 && x.Level == 1);
                if (final == null || passive == null) continue;
                var own = c.Owned.FirstOrDefault(x => x.Id == first.Id); bool complete = own != null && own.Level >= final.MaxLevel;
                bool missing = c.Owned.All(x => x.Id != passive.Id) && !complete;
                int steps = complete ? 0 : Math.Max(0, final.MaxLevel - (own == null ? 0 : own.Level)) + (missing ? 1 : 0);
                double gain = Math.Max(0, SkillPlanner.WeaponPower(final, c) - (own == null ? 0 : SkillPlanner.WeaponPower(own, c)));
                double time = Math.Min(1, Math.Max(0, c.TargetSeconds - c.Seconds) / Math.Max(1, steps * 12));
                double value = complete ? 0 : (20 + 70 * Math.Log(1 + gain / total)) * Math.Pow(.75, Math.Max(0, steps - 1)) * time;
                plans.Add(new ComboPlan
                {
                    WeaponId = first.Id,
                    PassiveId = passive.Id,
                    WeaponName = first.Name,
                    MaximumName = final.Name,
                    PassiveName = passive.Name,
                    CurrentLevel = own == null ? 0 : own.Level,
                    MaximumLevel = final.MaxLevel,
                    RemainingSteps = steps,
                    NeedWeapon = own == null,
                    NeedPassive = missing,
                    Complete = complete,
                    Feasible = complete || ((own != null || weaponFree > 0) && (!missing || passiveFree > 0)),
                    FutureValue = value
                });
            }
            // Small exact dynamic program over remaining weapon slots and DISTINCT passive slots.
            // Retains the best value for every capacity state; shared partners are charged only once.
            var partners = plans.Where(x => x.NeedPassive).Select(x => x.PassiveId).Distinct().ToArray();
            // Unknown much larger future catalogs fail back to immediate evaluation rather than exploding.
            if (partners.Length > 20 || plans.Count > 60) return plans.ToArray();
            var frontier = new Dictionary<Tuple<int, int>, Selection> { { Tuple.Create(0, 0), new Selection() } };
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i]; if (!plan.Feasible || plan.Complete || plan.FutureValue <= 0) continue;
                int bit = plan.NeedPassive ? 1 << Array.IndexOf(partners, plan.PassiveId) : 0;
                foreach (var entry in frontier.ToArray())
                {
                    int weapons = entry.Key.Item1 + (plan.NeedWeapon ? 1 : 0), mask = entry.Key.Item2 | bit;
                    if (weapons > weaponFree || Bits(mask) > passiveFree) continue;
                    var key = Tuple.Create(weapons, mask); double value = entry.Value.Value + plan.FutureValue; Selection old;
                    if (!frontier.TryGetValue(key, out old) || value > old.Value) frontier[key] = new Selection { Value = value, Choices = entry.Value.Choices | (1L << i) };
                }
            }
            var best = frontier.Values.OrderByDescending(x => x.Value).First();
            for (int i = 0; i < plans.Count; i++) plans[i].Planned = (best.Choices & (1L << i)) != 0;
            return plans.ToArray();
        }
        sealed class Selection { public double Value; public long Choices; }
        static int Bits(int value) { int n = 0; while (value != 0) { value &= value - 1; n++; } return n; }
        public static ComboScore Score(SkillOption candidate, SkillContext c)
        {
            var result = new ComboScore(); if (candidate.Type != 1 && candidate.Type != 2) return result;
            var all = Plans(c); var plan = all.FirstOrDefault(x => x.WeaponId == candidate.Id && x.Planned);
            bool isNew = c.Owned.All(x => x.Id != candidate.Id);
            if (candidate.Type == 1)
            {
                if (plan != null)
                {
                    result.Bonus = candidate.Level >= plan.MaximumLevel ? 20 : candidate.Level == plan.MaximumLevel - 1 ? 35 : 12;
                    if (!plan.NeedPassive) result.Bonus += 8;
                    result.Reason = "推进组合：" + plan.MaximumName + "＋" + plan.PassiveName;
                }
                else if (isNew && all.Any(x => x.Planned && x.NeedWeapon) && c.Owned.Count(x => x.Type == 1) >= c.Capacity - 2)
                {
                    result.OpportunityCost = 40; result.Reason = "保留武器栏给可完成的最高级组合";
                }
            }
            else if (isNew)
            {
                var matched = all.Where(x => x.Planned && x.NeedPassive && x.PassiveId == candidate.Id).ToArray();
                if (matched.Length > 0)
                {
                    result.Bonus = matched.Sum(x => x.CurrentLevel >= x.MaximumLevel - 1 ? 65 : x.CurrentLevel >= x.MaximumLevel - 2 ? 40 : x.CurrentLevel > 0 ? 24 : 8);
                    result.Reason = "补齐组合能力：" + string.Join("、", matched.Select(x => x.MaximumName));
                }
                else
                {
                    int free = c.Capacity - c.Owned.Count(x => x.Type == 2);
                    int reserved = all.Where(x => x.Planned && x.NeedPassive).Select(x => x.PassiveId).Distinct().Count();
                    if (free <= reserved && reserved > 0)
                    {
                        result.OpportunityCost = all.Any(x => x.Planned && x.NeedPassive && x.CurrentLevel >= x.MaximumLevel - 1) ? 100 : 65;
                        result.Reason = "占用缺失配套能力的预留栏位";
                    }
                }
            }
            return result;
        }
    }
}

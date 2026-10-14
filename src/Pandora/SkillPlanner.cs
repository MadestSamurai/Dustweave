#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace Dustweave.Pandora
{
    public sealed class SkillOption
    {
        public int Id, Type, Subtype, Stat, Level, MaxLevel, SynergyId, TargetType, AttackCount, Pierce, Knockback;
        public string Name;
        public double Value, Cooldown, Duration, Range, Size, ProjectileTerm, ProjectileSpeed;
    }
    public sealed class SkillContext
    {
        public double Hp = 1, MaxHp = 1, Seconds, HitDamage = 100, Defence, Recovery, PlayerAttackInterval = .5;
        public double Cooldown = 1, Duration = 1, ProjectileSpeed = 1, TargetSeconds = 1200;
        public int ExtraProjectiles, Capacity = 6;
        public bool Boss, FarmCurrency;
        public List<SkillOption> Owned = new List<SkillOption>();
        public List<SkillOption> Catalog = new List<SkillOption>();
    }
    public sealed class SkillEvaluation
    {
        public int Id, Level; public string Name, Reason;
        public double Score, OutputGain, Survival, Synergy, ComboOpportunityCost; public string ComboReason = "";
    }
    public static class SkillPlanner
    {
        // Source-backed cast periods; coverage is deliberately an estimate, not a battle simulator.
        public static double WeaponPower(SkillOption w, SkillContext c, double cooldownRatio = 1, double durationRatio = 1, double projectileRatio = 1)
        {
            if (w.Type != 1) return 0;
            double count = Math.Max(1, w.AttackCount + (w.Subtype == 6 || w.Subtype == 11 ? 0 : c.ExtraProjectiles));
            double cd = Math.Max(0, w.Cooldown * c.Cooldown * cooldownRatio);
            double duration = Math.Max(0, w.Duration * c.Duration * durationRatio);
            double ps = Math.Max(.01, w.ProjectileSpeed * c.ProjectileSpeed * projectileRatio);
            double period = cd, hits = count, coverage = 1;
            switch (w.Subtype)
            {
                case 2: case 7: case 10: period += Math.Max(0, w.ProjectileTerm * c.Cooldown * cooldownRatio) * (count - 1); break;
                case 3:
                    period += duration;
                    // Rotating weapons are contact-based, not continuously hitting every enemy.
                    hits = Math.Max(1, count * duration * ps / (360) * .65); coverage = c.Boss ? .3 : .8; break;
                case 6: period = Math.Max(.05, c.PlayerAttackInterval); hits = 1; coverage = c.Boss ? .2 : 1.7; break;
                case 9:
                    period += w.Range / ps + duration;
                    hits = count * Math.Max(1, duration / Math.Max(.05, c.PlayerAttackInterval) * .4); coverage = c.Boss ? .6 : 1.2; break;
            }
            if (w.Subtype == 4) coverage = c.Boss ? 1 : 1 + Math.Min(2, Math.Max(0, w.Size) * .25);
            else if (w.Subtype == 5 || w.Subtype == 8 || w.Subtype == 11) coverage = c.Boss ? .7 : 1.4;
            else if (w.Subtype == 2 || w.Subtype == 7 || w.Subtype == 10) coverage = 1 + Math.Min(c.Boss ? .1 : 1.5, Math.Max(0, w.Pierce) * .15);
            if (c.Boss && w.Range > 0 && w.Range < 5) coverage *= .6;
            return Math.Max(0, w.Value) * hits * coverage / Math.Max(.15, period);
        }
        static double NeutralValue(int stat) { return stat == 10 ? 0 : 1; }
        public static SkillEvaluation Evaluate(SkillOption candidate, SkillContext c)
        {
            var owned = c.Owned; var old = owned.FirstOrDefault(x => x.Id == candidate.Id);
            double hp = Math.Max(0, Math.Min(1, c.Hp / Math.Max(1, c.MaxHp)));
            double total = owned.Sum(x => WeaponPower(x, c));
            double gain = 0, survival = 0, synergy = 0, other = 0;
            string reason = "比较升级的边际收益";
            if (candidate.Type == 0)
            {
                other = candidate.Subtype == 2001 ? (hp < .3 ? 180 : hp < .6 ? 110 : hp < .9 ? 45 : -20) : (c.FarmCurrency ? 60 : -10);
                reason = candidate.Subtype == 2001 ? "按缺血程度判断即时恢复" : "生存目标降低金币奖励优先级";
            }
            else if (candidate.Type == 1)
            {
                gain = (WeaponPower(candidate, c) - (old == null ? 0 : WeaponPower(old, c))) / Math.Max(.1, total);
                int active = owned.Count(x => x.Type == 1);
                if (old == null && active >= c.Capacity) return new SkillEvaluation { Id = candidate.Id, Name = candidate.Name, Level = candidate.Level, Score = -1000, Reason = "武器栏已满" };
                other = old == null && active < 3 ? 12 : 0;
                // Knockback has value even when its benefit is absent from damage totals.
                if (candidate.Knockback > 0 && (old == null || old.Knockback == 0) && !c.Boss) survival = hp < .5 ? 18 : 10;
                if (c.Catalog.Count == 0 && candidate.SynergyId > 0 && owned.Any(x => x.Id == candidate.SynergyId))
                {
                    if (candidate.Level == candidate.MaxLevel) synergy = 12;
                    else if (candidate.Level == candidate.MaxLevel - 1) synergy = 18;
                    else synergy = 4;
                }
                reason = "升级前后输出估计＋覆盖／击退＋进化搭配";
            }
            else if (candidate.Type == 2)
            {
                double previous = old == null ? NeutralValue(candidate.Stat) : old.Value;
                double ratio = previous > 0 ? candidate.Value / previous : 1;
                switch (candidate.Stat)
                {
                    case 3: gain = ratio - 1; break;
                    case 6: gain = (owned.Sum(x => WeaponPower(x, c, ratio)) - total) / Math.Max(.1, total); break;
                    case 5: gain = (owned.Sum(x => WeaponPower(x, c, 1, ratio)) - total) / Math.Max(.1, total); break;
                    case 9: gain = (owned.Sum(x => WeaponPower(x, c, 1, 1, ratio)) - total) / Math.Max(.1, total); break;
                    case 1:
                        // Increasing max HP does not heal current HP in the client.
                        survival = Math.Max(0, ratio - 1) * (hp < .3 ? 20 : 65); reason = "提高血量上限，不计作即时回血"; break;
                    case 10:
                        survival = Math.Min(1 - hp, Math.Max(0, candidate.Value - previous) * 12 / Math.Max(1, c.MaxHp)) * 160;
                        if (old == null && hp < .7) survival += 8; reason = "未来12秒恢复量，不等同立即救命"; break;
                    case 11:
                        double saved = Math.Max(0, c.Defence * (ratio - 1));
                        survival = 140 * Math.Min(.8, saved / Math.Max(1, c.HitDamage - c.Defence)) * (hp < .5 ? 1.4 : 1);
                        reason = "按近期入伤估算固定防御收益"; break;
                    case 2: survival = Math.Max(0, ratio - 1) * (c.Boss || hp < .5 ? 160 : 85); reason = "移动速度改善脱离攻击范围"; break;
                    case 4: other = Math.Max(0, ratio - 1) * (c.Seconds < 600 ? 70 : 35); reason = "拾取范围促进升级，避险优先级保持不变"; break;
                    case 7: other = Math.Max(0, ratio - 1) * Math.Max(0, 100 * (1 - c.Seconds / 1200)); reason = "经验加成按剩余成长时间估值"; break;
                    case 8: other = c.FarmCurrency ? Math.Max(0, ratio - 1) * Math.Max(20, 100 * (1 - c.Seconds / Math.Max(1, c.TargetSeconds))) : -8; reason = "按当前模式与剩余时间评估金币收益"; break;
                }
                // A utility passive can be the missing component of an almost-evolved weapon.
                if (c.Catalog.Count == 0 && old == null) foreach (var w in owned.Where(x => x.Type == 1 && x.SynergyId == candidate.Id && x.Level < x.MaxLevel)) synergy += w.Level >= w.MaxLevel - 1 ? 55 : w.Level >= w.MaxLevel - 2 ? 24 : 8;
                int passive = owned.Count(x => x.Type == 2);
                if (old == null && passive >= c.Capacity) return new SkillEvaluation { Id = candidate.Id, Name = candidate.Name, Level = candidate.Level, Score = -1000, Reason = "能力栏已满" };
                if (c.Catalog.Count == 0 && old == null && passive >= c.Capacity - 1 && synergy == 0) other -= 8;
            }
            var combo = ComboPlanner.Score(candidate, c);
            synergy += combo.Bonus; other -= combo.OpportunityCost;
            // Low HP does not imply automatic rejection of offence: clearing threats can save HP.
            double output = 140 * Math.Log(Math.Max(.05, 1 + gain)) * (hp < .5 ? 1.15 : 1);
            return new SkillEvaluation { Id = candidate.Id, Level = candidate.Level, Name = candidate.Name, Score = output + survival + synergy + other, OutputGain = gain, Survival = survival, Synergy = synergy, ComboOpportunityCost = combo.OpportunityCost, ComboReason = combo.Reason, Reason = reason };
        }
    }
}

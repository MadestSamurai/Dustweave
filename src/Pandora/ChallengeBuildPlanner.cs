#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dustweave.Pandora
{
    public sealed class ChallengeBuild
    {
        public bool Known, CorePossible;
        public int PossiblePairs, RecommendedPairs, Paired, Weapons, Passives;
        public bool Viable { get { return Known && CorePossible && PossiblePairs >= 5; } }
        public int Quality { get { return (Viable ? 10000 : 0) + (CorePossible ? 1000 : 0) + RecommendedPairs * 100 + PossiblePairs; } }
    }
    public sealed class ChallengeChoice
    {
        public string Action = "choose", Reason = "ordinary", Name = "";
        public int Id, Level, Rerolls;
        public ChallengeBuild Build;
    }
    // Only called by automatic Challenge mode. The normal currency policy is unchanged.
    public static class ChallengeBuildPlanner
    {
        // Identify roles by native mechanics and the native evolution partner, never localized text/IDs.
        static int PartnerStat(SkillOption w, SkillContext c)
        { var p = c.Catalog.FirstOrDefault(x => x.Type == 2 && x.Id == w.SynergyId); return p == null ? 0 : p.Stat; }
        public static int WeaponTier(SkillOption w, SkillContext c)
        {
            int stat = PartnerStat(w, c);
            if (w.Type != 1) return 0;
            if (w.Subtype == 8 && stat == 2 || w.Subtype == 4 && stat == 4) return 3; // whip / lightning
            if (w.Subtype == 3 && stat == 5 || w.Subtype == 2 && stat == 9 || w.Subtype == 5 && stat == 3) return 2;
            return 0;
        }
        static int PassiveTier(SkillOption p)
        { return p.Stat == 2 ? 4 : p.Stat == 6 || p.Stat == 3 || p.Stat == 9 ? 3 : p.Stat == 4 ? 2 : p.Stat == 5 ? 1 : 0; }
        static bool Paired(SkillOption w, HashSet<int> passives)
        { return w.MaxLevel > 1 && w.Level >= w.MaxLevel || w.SynergyId > 0 && passives.Contains(w.SynergyId); }

        // Enumerate remaining weapon slots, then choose distinct passive partners. Owned slots
        // cannot be replaced by rerolling. Shared partners cost only one slot. At most 16 weapons.
        public static ChallengeBuild Inspect(SkillContext c, SkillOption candidate = null)
        {
            var owned = c.Owned.Where(x => x.Type == 1 || x.Type == 2).ToList();
            if (candidate != null && (candidate.Type == 1 || candidate.Type == 2))
            { owned.RemoveAll(x => x.Id == candidate.Id); owned.Add(candidate); }
            var weapons = owned.Where(x => x.Type == 1).ToArray();
            var passives = new HashSet<int>(owned.Where(x => x.Type == 2).Select(x => x.Id));
            var catalog = c.Catalog.Where(x => x.Type == 1 && x.Level == 1 && x.SynergyId > 0).GroupBy(x => x.Id).Select(x => x.First()).ToArray();
            var shoes = c.Catalog.FirstOrDefault(x => x.Type == 2 && x.Stat == 2);
            var core = catalog.Where(x => WeaponTier(x, c) == 3).ToArray();
            var result = new ChallengeBuild { Weapons = weapons.Length, Passives = passives.Count, Paired = weapons.Count(w => Paired(w, passives)) };
            // Future table changes must not silently turn into endless restarts.
            if (catalog.Length > 16 || core.Length != 2 || shoes == null || c.Capacity < 5 ||
                catalog.Count(x => WeaponTier(x, c) > 0) != 5 ||
                catalog.Any(w => !c.Catalog.Any(p => p.Type == 2 && p.Id == w.SynergyId)) ||
                owned.Any(o => !c.Catalog.Any(x => x.Type == o.Type && x.Id == o.Id))) return result;
            result.Known = true;
            if (weapons.Length > c.Capacity || passives.Count > c.Capacity) return result;
            var missing = catalog.Where(w => weapons.All(o => o.Id != w.Id)).ToArray();
            int freeWeapons = c.Capacity - weapons.Length;
            // Fill to capacity: adding a hypothetical weapon cannot reduce achievable pair count.
            int take = Math.Min(freeWeapons, missing.Length);
            var selected = new List<SkillOption>(weapons);
            Action score = () =>
            {
                if (core.Any(w => selected.All(s => s.Id != w.Id))) return;
                var available = new HashSet<int>(passives) { shoes.Id };
                if (available.Count > c.Capacity) return;
                // Enumerate the small set of remaining partners as well: shared partners and
                // the five-pair minimum make a per-item greedy score insufficient.
                var groups = selected.Where(w => !Paired(w, available)).GroupBy(w => w.SynergyId)
                    .Where(g => g.Key > 0).Select(g => new { Count = g.Count(), Recommended = g.Count(w => WeaponTier(w, c) > 0) }).ToArray();
                int basePairs = selected.Count(w => Paired(w, available));
                int baseRecommended = selected.Count(w => WeaponTier(w, c) > 0 && Paired(w, available));
                int slots = Math.Min(groups.Length, c.Capacity - available.Count);
                for (int mask = 0; mask < 1 << groups.Length; mask++)
                {
                    int bits = 0, pairs = basePairs, recommended = baseRecommended;
                    for (int i = 0; i < groups.Length; i++) if ((mask & (1 << i)) != 0)
                    { bits++; pairs += groups[i].Count; recommended += groups[i].Recommended; }
                    if (bits != slots) continue;
                    var possible = new ChallengeBuild { Known = true, CorePossible = true, PossiblePairs = pairs, RecommendedPairs = recommended };
                    if (possible.Quality > result.Quality)
                    { result.CorePossible = true; result.PossiblePairs = pairs; result.RecommendedPairs = recommended; }
                }
            };
            Action<int, int> search = null;
            search = (index, left) =>
            {
                if (left == 0) { score(); return; }
                for (int i = index; i <= missing.Length - left; i++)
                { selected.Add(missing[i]); search(i + 1, left - 1); selected.RemoveAt(selected.Count - 1); }
            };
            search(0, take);
            return result;
        }
        static double Priority(SkillOption option, SkillContext c)
        {
            bool isNew = c.Owned.All(x => x.Id != option.Id);
            if (option.Type == 0) return option.Subtype == 2001 && c.Hp < c.MaxHp * .3 ? 900 : -100;
            if (option.Type == 1)
            {
                int tier = WeaponTier(option, c);
                if (option.Level >= option.MaxLevel && option.MaxLevel > 1) return 850 + tier * 10;
                return (isNew ? 500 : 250) + tier * (isNew ? 100 : 40) + Math.Min(5, option.Level) * 3;
            }
            if (option.Type != 2) return -1000;
            int passiveTier = PassiveTier(option);
            bool completes = isNew && c.Owned.Any(w => w.Type == 1 && w.SynergyId == option.Id && w.Level >= w.MaxLevel - 1);
            if (completes) return 830 + passiveTier;
            if (isNew) return 450 + passiveTier * 100;
            // Magnet level one already unlocks lightning evolution. It must not crowd out
            // weapon damage, speed or cooldown upgrades merely because range scales quickly.
            if (option.Stat == 4) return 80;
            return 220 + passiveTier * 35;
        }
        public static ChallengeChoice Choose(SkillOption[] options, SkillContext c, int rerolls, bool enabled)
        {
            if (options.Length == 0) throw new InvalidOperationException("Pandora upgrade choices unavailable");
            var before = enabled && !c.FarmCurrency ? Inspect(c) : new ChallengeBuild();
            var valid = options.Where(o => (o.Type != 1 && o.Type != 2) || c.Owned.Any(x => x.Id == o.Id) || c.Owned.Count(x => x.Type == o.Type) < c.Capacity).ToArray();
            if (valid.Length == 0) throw new InvalidOperationException("Pandora choices exceed native capacity");
            var ranked = valid.Select(o => new { Option = o, Build = before.Known ? Inspect(c, o) : before,
                Priority = before.Known ? Priority(o, c) : 0, Score = SkillPlanner.Evaluate(o, c).Score })
                .OrderByDescending(x => x.Build.Quality).ThenByDescending(x => x.Priority).ThenByDescending(x => x.Score).ThenBy(x => x.Option.Id).ToArray();
            if (before.Known && ranked.Any(x => !x.Build.Known))
            {
                var ordinary = valid.OrderByDescending(o => SkillPlanner.Evaluate(o, c).Score).First();
                return new ChallengeChoice { Id = ordinary.Id, Level = ordinary.Level, Name = ordinary.Name, Reason = "unknown-build-contract", Build = new ChallengeBuild() };
            }
            var best = ranked[0];
            var result = new ChallengeChoice { Id = best.Option.Id, Level = best.Option.Level, Name = best.Option.Name, Rerolls = Math.Max(0, rerolls), Build = best.Build };
            if (!before.Known) return result;
            if (!before.Viable) { result.Action = "restart"; result.Reason = "fewer-than-five-pairs-or-core-blocked"; return result; }
            bool losesBuild = best.Build.Quality < before.Quality;
            bool newOffPlan = c.Owned.All(o => o.Id != best.Option.Id) &&
                (best.Option.Type == 1 && WeaponTier(best.Option, c) == 0 || best.Option.Type == 2 && PassiveTier(best.Option) == 0);
            bool usefulPartner = newOffPlan && best.Option.Type == 2 && c.Owned.Any(w => w.Type == 1 && w.SynergyId == best.Option.Id);
            if (rerolls > 0 && (losesBuild || newOffPlan && !usefulPartner))
            { result.Action = "reroll"; result.Reason = "reserve-build-slots"; return result; }
            if (!best.Build.Viable) { result.Action = "restart"; result.Reason = "no-viable-choice-after-rerolls"; return result; }
            result.Reason = best.Build.RecommendedPairs < 5 ? "paired-fallback" : "recommended-build";
            return result;
        }
    }
}

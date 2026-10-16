using Dustweave;
using Dustweave.Pandora;
using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class PandoraBuildCases
{
    // Synthetic data: retain mechanics and relationships, not copied client tables.
    static List<SkillOption> Catalog()
    {
        int[] sub = [2, 3, 4, 5, 6, 2, 7, 8, 9, 2, 10];
        int[] stat = [6, 5, 4, 3, 1, 7, 8, 2, 10, 9, 11];
        var result = new List<SkillOption>();
        for (int i = 0; i < sub.Length; i++)
        {
            for (int level = 1; level <= 6; level++) result.Add(new() { Id = 100 + i, Type = 1, Subtype = sub[i], Level = level,
                MaxLevel = 6, SynergyId = 200 + i, Name = "weapon " + i, Value = 10 + level * 5, Cooldown = 2, AttackCount = 1, Duration = 2, ProjectileSpeed = 10 });
            for (int level = 1; level <= 5; level++) result.Add(new() { Id = 200 + i, Type = 2, Subtype = 1, Level = level,
                MaxLevel = 5, Stat = stat[i], SynergyId = 100 + i, Name = "passive " + i, Value = stat[i] == 6 ? 1 - level * .1 : 1 + level * .2 });
        }
        result.Add(new() { Id = 300, Type = 0, Subtype = 2001, Level = 1, MaxLevel = 1, Name = "heal" });
        result.Add(new() { Id = 301, Type = 0, Subtype = 2002, Level = 1, MaxLevel = 1, Name = "coin" });
        return result;
    }
    public static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("pandora build: " + name); }
        var catalog = Catalog();
        SkillOption S(int id, int level = 1) => catalog.Single(x => x.Id == id && x.Level == level);
        SkillContext Context(params int[] ids) => new() { Catalog = catalog, Owned = ids.Select(id => S(id)).ToList(), Hp = 100, MaxHp = 100 };
        ChallengeChoice Pick(SkillContext c, int rerolls, params SkillOption[] choices) => ChallengeBuildPlanner.Choose(choices, c, rerolls, true);
        var start = Context(100);
        var shape = ChallengeBuildPlanner.Inspect(start);
        Check(shape.Known && shape.Viable && shape.PossiblePairs == 6 && shape.RecommendedPairs == 5 && shape.Paired == 0, "starting weapon leaves room for six pairs; future potential is not current ownership");
        Check(Pick(start, 7, S(101), S(107), S(103)).Id == 107, "whip takes priority over secondary weapons");
        Check(Pick(start, 7, S(101), S(102), S(103)).Id == 102, "lightning takes priority over secondary weapons");
        Check(Pick(start, 7, S(207), S(200), S(203)).Id == 207, "shoes are the first auxiliary choice");
        Check(Pick(start, 7, S(200), S(202), S(201)).Id == 200, "cooldown drink precedes magnet and battery");
        Check(Pick(start, 7, S(203), S(202), S(201)).Id == 203, "attack arm precedes magnet and battery");
        Check(Pick(start, 7, S(209), S(202), S(201)).Id == 209, "projectile speed precedes magnet and battery");
        var magnet = Context(100, 202, 201, 203);
        Check(Pick(magnet, 7, S(202, 2), S(201, 2), S(203, 2)).Id == 203, "magnet upgrades defer to offensive upgrades");
        Check(Pick(magnet, 7, S(202, 2), S(201, 2)).Id == 201, "battery upgrade beats further pickup range");
        var nearEvolution = Context(100, 107, 207, 203); nearEvolution.Owned.RemoveAll(x => x.Id == 107); nearEvolution.Owned.Add(S(107, 5));
        Check(Pick(nearEvolution, 7, S(107, 6), S(203, 2)).Id == 107, "offered weapon evolution is not delayed for a passive upgrade");
        nearEvolution.Owned.RemoveAll(x => x.Id == 207);
        Check(Pick(nearEvolution, 7, S(207), S(203, 2)).Id == 207, "missing partner is acquired before irrelevant growth");
        Check(Pick(start, 7, S(104), S(105), S(106)).Action == "reroll", "off-plan weapons consume a real reroll before locking a slot");
        Check(Pick(start, 0, S(104), S(105), S(106)).Action == "choose", "no rerolls still permits a paired substitute instead of demanding perfection");
        Check(Pick(start, 0, S(100, 2)).Action == "choose", "running out of rerolls alone does not end a viable run");
        var five = Context(100, 107, 102, 101, 109, 105, 200, 207, 202, 201, 209, 205);
        shape = ChallengeBuildPlanner.Inspect(five);
        Check(shape.Viable && shape.RecommendedPairs == 4 && shape.Paired == 6, "one secondary recommendation can be replaced by a paired weapon");
        Check(Pick(five, 0, S(100, 2), S(105, 2)).Action == "choose", "acceptable substitute build keeps playing");
        var exactlyFive = Context(100, 107, 102, 101, 109, 103, 200, 207, 201, 209, 203, 205);
        Check(ChallengeBuildPlanner.Inspect(exactlyFive).Paired == 5 && Pick(exactlyFive, 0, S(102, 2)).Action == "choose", "five actual pairs with both core weapons and shoes are sufficient; six evolutions are not required");
        var locked = Context(100, 107, 102, 101, 109, 103, 200, 207, 202, 204, 205, 206);
        Check(!ChallengeBuildPlanner.Inspect(locked).Viable && Pick(locked, 7, S(100, 2)).Action == "restart", "irreversible slots below five pairs restart even when rerolls remain");
        var lastSlot = Context(100, 107, 102, 101, 109, 103, 200, 207, 202, 201, 204);
        Check(Pick(lastSlot, 2, S(205), S(206)).Action == "reroll", "last auxiliary slot is reserved for the fifth pair");
        Check(Pick(lastSlot, 0, S(205), S(206)).Action == "restart", "unavoidable loss of the fifth pair requests restart");
        Check(Pick(lastSlot, 0, S(203), S(205)).Id == 203, "native partner association wins over an incompatible auxiliary");
        var translated = Context(100); translated.Catalog = catalog.Select(s => new SkillOption { Id = s.Id + 1000, Level = s.Level, Type = s.Type, Subtype = s.Subtype,
            Stat = s.Stat, MaxLevel = s.MaxLevel, SynergyId = s.SynergyId > 0 ? s.SynergyId + 1000 : 0, Name = "different language", Value = s.Value, Cooldown = s.Cooldown }).ToList();
        translated.Owned = [translated.Catalog.Single(x => x.Id == 1100 && x.Level == 1)];
        Check(ChallengeBuildPlanner.Inspect(translated).Viable && ChallengeBuildPlanner.Choose(translated.Catalog.Where(x => x.Level == 1 && (x.Id == 1107 || x.Id == 1101)).ToArray(), translated, 7, true).Id == 1107,
            "translated names and shifted table IDs preserve role selection");
        var unknown = Context(100); unknown.Catalog = [];
        Check(Pick(unknown, 7, S(104)).Action == "choose" && !Pick(unknown, 7, S(104)).Build.Known, "missing catalog never invents an irreversible failure");
        Check(Pick(start, 7, new SkillOption { Id = 999, Type = 1, Subtype = 3, Level = 1, MaxLevel = 6 }).Action == "choose", "unknown newly offered gear cannot trigger a speculative restart");
        var currency = Context(100); currency.FarmCurrency = true;
        Check(Pick(currency, 7, S(104)).Action == "choose" && !Pick(currency, 7, S(104)).Build.Known, "currency mode never rerolls or restarts for the Challenge build");
        Check(!ChallengeBuildPlanner.Choose([S(104)], start, 7, false).Build.Known, "disabled Challenge equipment planning does not enforce the profile");

        var restart = new BuildRestart(); restart.Request(17, 10);
        Check(restart.Owns(17, true, true, true) && !restart.Owns(18, true, true, true) && !restart.Owns(17, false, true, true) && !restart.Owns(17, true, false, true) && !restart.Owns(17, true, true, false), "restart is limited to its game, challenge, auto-selection and stage");
        restart.Phase = BuildRestartPhase.ExitSent; restart.PauseId = 5;
        Check(restart.CanConfirm(17, 5, true) && !restart.CanConfirm(17, 6, true) && !restart.CanConfirm(17, 5, false), "exit confirmation requires the exact native pause callback");
        restart.Phase = BuildRestartPhase.Confirmed;
        Check(!restart.CanConfirm(17, 5, true) && restart.Expired(55), "confirmed exits are never submitted twice and recovery is bounded");
        restart.Reset(); Check(!restart.Pending && !restart.CanConfirm(17, 5, true), "a new run clears all old exit authority");
        var frame = PandoraClient.DiagnosticState(new JsonObject { ["Build"] = new JsonObject { ["Restart"] = "Requested" } });
        Check(frame["Build"]?["Restart"]?.ToString() == "Requested", "shared diagnostics include build restart reasons");

        // Many randomized offer sequences check structural decisions, not combat win rate.
        int paired = 0, restarts = 0, totalRerolls = 0, decisions = 0; var timings = new List<double>();
        for (int seed = 0; seed < 128; seed++)
        {
            var random = new Random(seed); var c = Context(100); int rerolls = seed % 8; bool restarted = false;
            for (int step = 0; step < 100; step++)
            {
                var offered = catalog.Where(x => x.Type <= 2 && (x.Type == 0 ||
                    c.Owned.Any(o => o.Id == x.Id && x.Level == o.Level + 1) ||
                    x.Level == 1 && c.Owned.All(o => o.Id != x.Id) && c.Owned.Count(o => o.Type == x.Type) < c.Capacity))
                    .Where(x => x.Type != 1 || x.Level < x.MaxLevel || c.Owned.Any(p => p.Id == x.SynergyId))
                    .Where(x => x.Type != 0).OrderBy(_ => random.Next()).Take(3).ToArray();
                if (offered.Length == 0) break;
                var timer = Stopwatch.StartNew(); var decision = Pick(c, rerolls, offered); timings.Add(timer.Elapsed.TotalMilliseconds); decisions++;
                if (decision.Action == "reroll") { if (rerolls <= 0) throw new Exception("imaginary reroll"); rerolls--; totalRerolls++; continue; }
                if (decision.Action == "restart") { restarts++; restarted = true; break; }
                var selected = offered.Single(x => x.Id == decision.Id && x.Level == decision.Level);
                c.Owned.RemoveAll(x => x.Id == selected.Id); c.Owned.Add(selected);
                if (c.Owned.Count(x => x.Type == 1) > 6 || c.Owned.Count(x => x.Type == 2) > 6) throw new Exception("capacity exceeded");
                if (!ChallengeBuildPlanner.Inspect(c).Viable) throw new Exception("accepted a provably dead build");
            }
            if (!restarted && ChallengeBuildPlanner.Inspect(c).Paired >= 5) paired++;
        }
        Check(decisions > 1000 && totalRerolls > 0 && paired > 0 && restarts > 0, "random offers exercise viable builds, native rerolls and early restarts");
        timings.Sort();
        DailyJson.Write(Path.Combine(root, "pandora-build.json"), new { seeds = 128, decisions, paired, restarts, totalRerolls,
            meanMs = timings.Average(), p95Ms = timings[(int)(timings.Count * .95)], maxMs = timings[^1], combatWinRateMeasured = false, realGameTouched = false });
    }
}

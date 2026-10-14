using System.Text.Json.Nodes;
using Dustweave;
using Dustweave.Pandora;

internal static class PandoraCases
{
    public static void Run(List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("pandora: " + name); }
        Stage[] stages = [
            new() { Group = 1, Id = 1, Unlocked = true },
            new() { Group = 2, Id = 1, Unlocked = true },
            new() { Group = 2, Id = 2, Unlocked = false },
            new() { Group = 3, Id = 1, Challenge = true, Unlocked = false },
            new() { Group = 4, Id = 1, Challenge = true, Unlocked = true }
        ];
        Check(ReferenceEquals(LoopRules.Select(stages, "Currency", 2, 1), stages[1]), "same difficulty id on another map never changes the selected farm stage");
        Check(LoopRules.Select(stages, "Currency", 2, 2) == null, "locked difficulty is not selected");
        Check(LoopRules.Select(stages, "Currency", 4, 1) == null, "currency mode cannot select challenge");
        Check(ReferenceEquals(LoopRules.Select(stages, "Challenge", 2, 1), stages[4]), "challenge uses an unlocked challenge entry");
        Check(LoopRules.Select([], "Challenge", 0, 0) == null, "missing native stages cannot fabricate a selection");

        long now = DateTime.UtcNow.Ticks;
        var control = new Control { Enabled = true, RunId = "test", Expires = now + TimeSpan.FromSeconds(4).Ticks };
        Check(control.Valid(now) && !control.Valid(now + TimeSpan.FromSeconds(4).Ticks), "movement authority expires when the host heartbeat stops");
        control.Expires = now + TimeSpan.FromHours(1).Ticks;
        Check(!control.Valid(now), "unbounded movement lease is rejected");
        control.Expires = now + TimeSpan.FromSeconds(4).Ticks; control.Mode = "unknown";
        Check(!control.Valid(now), "unrecognized mode cannot enable movement");
        control.Mode = "Challenge"; control.RunId = "";
        Check(!control.Valid(now), "unidentified run is rejected");

        var gate = new TransitionGate();
        Check(!gate.Waiting("result:1", 0, out _), "initial result is actionable");
        gate.Sent("result:1", 1);
        Check(gate.Waiting("result:1", 2, out var timeout) && !timeout, "slow settlement cannot duplicate a retry request");
        Check(gate.Waiting("result:1", 26, out timeout) && timeout, "unchanged settlement has a bounded wait");
        Check(!gate.Waiting("confirm:2", 27, out _), "retry confirmation is a separate native transition");
        gate.Sent("skill:3:5", 28);
        Check(!gate.Waiting("skill:3:6", 29, out _), "reused skill window at the next level remains actionable");
        gate.Sent(LoopRules.SkillPage(3, 10, 8), 29);
        Check(gate.Waiting(LoopRules.SkillPage(3, 10, 8), 30, out _), "same pending native selection is not repeated");
        Check(!gate.Waiting(LoopRules.SkillPage(3, 10, 9), 31, out _), "new native choices at the same level are not mistaken for the previous request");
        gate.Sent("result:1", 30); gate.Reset();
        Check(!gate.Waiting("result:1", 31, out _), "a new round can reuse the pooled result window");

        var coin = new Drop { Id = 1, Type = 4, Amount = 20, Position = new(3, 0) };
        var xp = new Drop { Id = 2, Type = 3, Amount = 1, Position = new(-3, 0) };
        var currency = Planner.Choose(new(0, 0), 4, [], [coin, xp], new(), _ => true, farmCurrency: true);
        var survival = Planner.Choose(new(0, 0), 4, [], [coin, xp], new(), _ => true);
        Check(currency.Direction.X > .5 && survival.Direction.X < -.5, "currency objective changes safe loot selection");
        var pile = Enumerable.Range(1, 256).Select(i => new Drop { Id = i, Type = 4, Amount = 999, Position = new(3, 0) }).ToArray();
        var safe = Planner.Choose(new(0, 0), 4, [new() { Position = new(2, 0), Radius = .6 }], pile, new(), _ => true, farmCurrency: true);
        Check(!safe.Collision, "even a dense currency pile cannot justify predicted contact");
        var boss = Planner.Choose(new(0, 0), 4, [new() { Boss = true, Position = new(4, 0), Radius = 1, Weight = 3 }], pile, new(), _ => true, farmCurrency: true);
        Check(boss.Direction.X < -.5, "currency mode still retreats from a boss");
        var warning = new AttackArea { A = new(-5, 0), B = new(5, 0), Radius = 1.5 };
        var escape = Planner.Choose(new(0, 0), 4, [], pile, new(), _ => true, warnings: [warning], farmCurrency: true);
        Check(Math.Abs(escape.Direction.Z) > .5, "attack warning escape outranks currency collection");
        var blocked = Planner.Choose(new(0, 0), 4, [], pile, new(), _ => false, farmCurrency: true);
        Check(blocked.Direction.X == 0 && blocked.Direction.Z == 0, "no passable direction does not move through bounds");

        var region = new RegionPlanner();
        Drop[] firstPile = [new() { Id = 1, Position = new(4, 0) }, new() { Id = 2, Position = new(4, 1) }];
        region.Select(new(), 4, firstPile, [], 0);
        var retained = region.Select(new(1, 0), 4, [.. firstPile, new() { Id = 3, Position = new(-5, 0), Amount = 3 }], [], 1);
        Check(retained?.X > 0 && region.Switches == 0, "nearby experience commitment avoids oscillating between piles");
        Check(region.Select(new(), 4, [], [], 2) == null, "cleared drops release the committed region");

        var heal = new SkillOption { Type = 0, Subtype = 2001 };
        var reward = new SkillOption { Type = 0, Subtype = 2002 };
        var context = new SkillContext { FarmCurrency = true, Hp = 100, MaxHp = 100 };
        Check(SkillPlanner.Evaluate(reward, context).Score > SkillPlanner.Evaluate(heal, context).Score, "currency reward beats unnecessary healing");
        context.Hp = 20;
        Check(SkillPlanner.Evaluate(heal, context).Score > SkillPlanner.Evaluate(reward, context).Score, "emergency healing protects the farming run");
        var passive = new SkillOption { Id = 8, Type = 2, Stat = 8, Value = 1.2 };
        context.Hp = 100;
        double farmScore = SkillPlanner.Evaluate(passive, context).Score;
        context.FarmCurrency = false;
        Check(farmScore > SkillPlanner.Evaluate(passive, context).Score, "currency bonus is valued according to the selected objective");
        context.Capacity = 1; context.Owned.Add(new() { Id = 10, Type = 1, Value = 10, AttackCount = 1, Cooldown = 1 });
        Check(SkillPlanner.Evaluate(new() { Id = 11, Type = 1, Value = 1000 }, context).Score < 0, "full skill capacity cannot select a fictitious extra weapon");

        var diagnostic = PandoraClient.DiagnosticState(JsonNode.Parse("""{"At":"now","Running":false,"Faulted":true,"Reason":"transition-timeout","Stage":{"Id":2,"Group":1},"Units":[1,2],"Drops":[3,4]}""")!.AsObject());
        Check(diagnostic["Reason"]?.ToString() == "transition-timeout" && diagnostic["Stage"]?["Group"]?.GetValue<int>() == 1, "diagnostic retains the blocked transition and exact stage");
        Check(!diagnostic.ContainsKey("Units") && !diagnostic.ContainsKey("Drops"), "diagnostics exclude per-frame object lists");
        Check(DailyDiagnosticExport.Allowed("tools/pandora-state.json") && DailyDiagnosticExport.Allowed("tools/pandora-runtime-error.json"), "standard log export includes Pandora recovery information");
        bool rejected = false;
        try { _ = new PandoraClient("unused", 0, 0, ""); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid host identity fails before opening a connection");
        Check(BD2Daily.SuiteRules.ModuleEntry("pandora") == "Dustweave.Pandora.Runtime.Loader", "Pandora belongs to the unified component catalog");
    }
}

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

        var entry = new RoundEntry();
        EntryAction Entry(double time, bool playing = false, int resultId = 1, int revision = 7,
            bool popup = false, bool main = false, bool sameStage = true, bool selected = true, bool ready = true)
            => entry.Observe(time, playing, resultId, revision, popup, main, sameStage, selected, ready);
        entry.Begin(0, 1, 7);
        Check(Entry(.2, popup:true) == EntryAction.Wait, "retry confirmation must finish its native close before result recovery");
        Check(Entry(.5, ready:false) == EntryAction.Wait && entry.CloseAttempts == 0, "restoring result animation does not consume close attempts");
        Check(Entry(1, selected:false) == EntryAction.Wait, "disappeared confirmation alone cannot authorize a retry");
        Check(Entry(2) == EntryAction.CloseConfirmedResult && entry.Pending, "confirmed retry resumes closing a stranded result instead of waiting 45 seconds");
        Check(Entry(2.3) == EntryAction.Wait, "result close recovery is paced without resubmitting retry");
        Check(Entry(3, ready:false) == EntryAction.Wait, "accepted result close animation is allowed to finish");
        Check(Entry(4, resultId:0) == EntryAction.Wait, "removed result alone is not a running round");
        Check(Entry(5, playing:true, resultId:0) == EntryAction.Started && !entry.Pending, "actual round start releases movement and the pending transition");
        foreach (var changed in new[] { "window", "revision", "stage", "main", "callback" })
        {
            entry.Begin(0, 1, 7);
            Check(Entry(1, resultId:changed=="window"?2:1, revision:changed=="revision"?8:7,
                main:changed=="main", sameStage:changed!="stage", selected:changed!="callback") == EntryAction.Wait && entry.CloseAttempts == 0,
                "retry recovery does not affect changed " + changed);
        }
        entry.Begin(0, 1, 7);
        Check(Entry(1)==EntryAction.CloseConfirmedResult && Entry(2)==EntryAction.CloseConfirmedResult && Entry(3)==EntryAction.CloseConfirmedResult && Entry(4)==EntryAction.Wait,
            "repeated native refusal permits at most three result closes");
        Check(Entry(45)==EntryAction.Blocked, "unresolved entry still stops with a bounded diagnosis");
        entry.Reset();
        Check(Entry(46)==EntryAction.Wait && !entry.Pending && entry.CloseAttempts==0, "new control run discards old retry authority");
        entry.Begin(0);
        Check(Entry(1)==EntryAction.Wait, "character entry does not authorize closing an unrelated result");
        Check(Entry(2,playing:true,resultId:0,sameStage:false)==EntryAction.Wait, "another stage does not satisfy the requested entry");
        Check(Entry(3,playing:true,resultId:0)==EntryAction.Started, "initial character entry still resumes on the selected round");
        for(int round=1;round<=50;round++)
        {
            entry.Begin(round*10, 10, round);
            var action=entry.Observe(round*10+1,false,10,round,false,false,true,true,false);
            var close=entry.Observe(round*10+2,false,10,round,false,false,true,true,true);
            var started=entry.Observe(round*10+3,true,0,round,false,false,true,false,false);
            Check(action==EntryAction.Wait&&close==EntryAction.CloseConfirmedResult&&started==EntryAction.Started,
                "pooled Game Over result recovers exactly once in repeated round "+round);
        }

        var manual = new Control { Mode = "Challenge" };
        Check(manual.AutoSelectEquipment && !gate.ManualChoice(manual, true), "challenge keeps automatic choices enabled by default");
        manual.AutoSelectEquipment = false;
        gate.Sent("skill:3:5", 32);
        Check(gate.ManualChoice(manual, true) && !gate.Waiting("skill:3:5", 152, out _), "manual selection clears automation timeout while the human chooses");
        Check(gate.ManualChoice(manual, true) && !gate.ManualChoice(manual, false), "manual choice waits as long as needed and releases when the page closes");
        gate.Sent("result:2", 153);
        Check(!gate.ManualChoice(manual, false) && gate.Waiting("result:2", 179, out timeout) && timeout, "manual choices do not disable settlement timeout recovery");
        manual.Mode = "Currency";
        Check(!LoopRules.WaitForEquipment(manual, true), "currency farming continues automatic choices regardless of the challenge setting");
        var oldControl = System.Text.Json.JsonSerializer.Deserialize<Control>("{\"Mode\":\"Challenge\"}", new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
        Check(oldControl.AutoSelectEquipment, "older control files preserve automatic choice default");

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

        var diagnostic = PandoraClient.DiagnosticState(JsonNode.Parse("""{"At":"now","Running":false,"Faulted":true,"Reason":"transition-timeout","AutoSelectEquipment":false,"Stage":{"Id":2,"Group":1},"Units":[1,2],"Drops":[3,4]}""")!.AsObject());
        Check(diagnostic["Reason"]?.ToString() == "transition-timeout" && diagnostic["Stage"]?["Group"]?.GetValue<int>() == 1, "diagnostic retains the blocked transition and exact stage");
        Check(!diagnostic.ContainsKey("Units") && !diagnostic.ContainsKey("Drops"), "diagnostics exclude per-frame object lists");
        Check(diagnostic["AutoSelectEquipment"]?.GetValue<bool>() == false, "diagnostics distinguish manual equipment selection from an automation stall");
        Check(DailyDiagnosticExport.Allowed("tools/pandora-state.json") && DailyDiagnosticExport.Allowed("tools/pandora-runtime-error.json") && DailyDiagnosticExport.Allowed("tools/pandora-transitions.json"), "standard log export includes Pandora recovery information");
        var transition=PandoraClient.DiagnosticState(JsonNode.Parse("""{"Ui":"result:1","Entry":{"Pending":true,"CloseAttempts":1},"TransitionUi":[{"RetrySelected":true,"CanClose":false}]}""")!.AsObject());
        Check(transition["Entry"]?["CloseAttempts"]?.GetValue<int>()==1&&transition["TransitionUi"]?[0]?["CanClose"]?.GetValue<bool>()==false,
            "diagnostics retain native callback and animation/close evidence");
        var history=new JsonArray();
        for(int n=0;n<100;n++)PandoraClient.AppendTransition(history,new JsonObject{["Completed"]=n});
        Check(history.Count==64&&history[0]?["Completed"]?.GetValue<int>()==36&&history[63]?["Completed"]?.GetValue<int>()==99,
            "transition history is bounded and keeps the latest fault context");
        bool rejected = false;
        try { _ = new PandoraClient("unused", 0, 0, ""); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid host identity fails before opening a connection");
        Check(BD2Daily.SuiteRules.ModuleEntry("pandora") == "Dustweave.Pandora.Runtime.Loader", "Pandora belongs to the unified component catalog");
    }
}

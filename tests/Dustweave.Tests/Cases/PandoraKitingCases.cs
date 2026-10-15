using Dustweave.Pandora;

internal static class PandoraKitingCases
{
    public static void Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("pandora-kiting: " + name); }
        bool Arena(Point p) => Math.Abs(p.X) <= 12 && Math.Abs(p.Z) <= 12;
        Threat Boss(double x, double z, double speed = 2.5) => new() { Id = 1, Boss = true, Position = new(x, z), Radius = 1, MoveSpeed = speed, Weight = 3 };
        var data = new BossDamageModel(); var measured = Boss(0, 0);
        data.Observe(measured, 0, 4, 1000, true, 6, 1);
        data.Observe(measured, 0, 4, 100, true, 6, 1);
        Check(!measured.DamageConfidence && measured.ObservedDps == 0, "zero elapsed time cannot produce infinite DPS");
        data.Reset();
        for (int i = 0; i <= 40; i++) data.Observe(measured, i * .1, 4, 1000 - i * 10, true, 6, 1);
        Check(Math.Abs(measured.ObservedDps - 100) < .001 && Math.Abs(measured.KillSeconds - 6) < .001, "HP loss measures effective DPS and time to kill");
        double productiveRange = measured.PreferredGap;
        for (int i = 41; i <= 80; i++) data.Observe(measured, i * .1, 9, 600, true, 6, 1);
        Check(measured.PreferredGap < 6, "an unproductive far band cannot displace measured close-range damage");
        for (int i = 81; i <= 160; i++) data.Observe(measured, i * .1, 9, 600, true, 6, 1);
        Check(measured.ObservedDps == 0 && !measured.DamageConfidence, "DPS expires after sustained zero damage");
        data.Forget(measured.Id); data.Observe(measured, 17, 5, 9000, true, 6, 1);
        Check(!measured.DamageConfidence && double.IsPositiveInfinity(measured.KillSeconds), "pooled boss spawn cannot inherit earlier damage");
        var second = Boss(3, 2); second.Id = 2;
        for (int i = 0; i <= 20; i++) data.Observe(second, 20 + i * .1, 6, 1000 - i * 20, true, 6, 1);
        Check(second.ObservedDps > 190 && !measured.DamageConfidence, "different bosses retain separate damage histories");
        var immune = Boss(0, 0); immune.Id = 3;
        for (int i = 0; i <= 30; i++) data.Observe(immune, i * .1, 2, 1000, false, 7, 1);
        Check(immune.PreferredGap > 7 && !immune.DamageConfidence, "jump immunity does not teach a false melee damage range");
        var outOfRange = Boss(0, 0); var noHits = new BossDamageModel();
        for (int i = 0; i <= 90; i++) noHits.Observe(outOfRange, i * .1, 9, 1000, true, 7, 1);
        Check(outOfRange.PreferredGap < 6 && outOfRange.PreferredGap >= 2.75, "sustained vulnerable zero DPS cautiously narrows range without inventing damage");
        data.Observe(second, 30, 5, 8000, true, 6, 1);
        Check(!second.DamageConfidence, "health reset or long observation gap invalidates the old fight");
        var burst = Boss(0, 0); var slow = Boss(0, 0); var lowHealth = Boss(0, 0);
        foreach (var pair in new[] { (burst, 150.0, 1.0), (slow, 10.0, 1.0), (lowHealth, 150.0, .2) })
        {
            var model = new BossDamageModel();
            for (int i = 0; i <= 20; i++) model.Observe(pair.Item1, i * .1, 5, 500 - i * .1 * pair.Item2, true, 6, pair.Item3);
        }
        Check(burst.PreferredGap < slow.PreferredGap && lowHealth.PreferredGap > burst.PreferredGap, "measured kill window and health change standoff without overriding safety");

        var dash = Boss(0, 0); dash.Motion = BossMotion.Dash; dash.MotionEnd = new(6, 0); dash.MotionSpeed = 20; dash.PhaseSeconds = .3;
        var atEnd = BossKiting.Advance(dash, new(), new(20, 0), 0, .3);
        Check(Math.Abs(atEnd.X - 6) < .001, "dash prediction stops at its actual destination");
        var after = BossKiting.Advance(dash, atEnd, new(20, 0), .3, .2);
        Check(after.X > 6 && after.X < 7, "after a dash only ordinary pursuit speed is extrapolated");
        var turn = BossKiting.Advance(Boss(0, 0), new(), new(0, 10), 0, 1);
        Check(Math.Abs(turn.X) < .001 && turn.Z > 2, "pursuit can turn toward the future player position");
        var area = new AttackArea { A = new(), B = new(10, 0), Radius = 2, ActiveFor = .2 };
        Check(area.Clearance(new(2, 0), .1) < 0 && double.IsPositiveInfinity(area.Clearance(new(2, 0), .3)), "completed attack footprints expire");

        var routes = new List<object>();
        foreach (var fixture in new[] {
            ("near-spawn", new Point(0,0), Boss(3,0)),
            ("right-wall", new Point(10,0), Boss(5,0)),
            ("corner", new Point(10,10), Boss(6,6)),
            ("safe-distance", new Point(0,0), Boss(-9,0)),
            ("open-orbit", new Point(0,0), Boss(-7,0)) })
        {
            int queries = 0;
            var route = Planner.Choose(fixture.Item2, 4, [fixture.Item3], [], new(), p => { queries++; return Arena(p); });
            Check(route.PredictionSeconds > 2.3 && queries < 6000, fixture.Item1 + " uses bounded multi-turn forecast");
            Check(Math.Abs(route.Direction.X) + Math.Abs(route.Direction.Z) > .1, fixture.Item1 + " remains mobile");
            var end = new Point(fixture.Item2.X + route.Direction.X * 3.2, fixture.Item2.Z + route.Direction.Z * 3.2);
            Check(Arena(end), fixture.Item1 + " respects arena bounds");
            if (fixture.Item1 == "near-spawn") Check(route.Direction.X < -.5, "close spawn creates clearance before orbiting");
            if (fixture.Item1 == "right-wall") Check(Math.Abs(route.Direction.Z) > .5, "wall approach turns instead of retreating straight into the boundary");
            if (fixture.Item1 == "corner") Check(route.Direction.X < 0 || route.Direction.Z < 0, "corner chooses a way back into the arena");
            if (fixture.Item1 == "safe-distance") Check(route.Direction.X < .8, "safe separation does not reward endless radial retreat");
            routes.Add(new { name = fixture.Item1, route, queries });
        }
        var imminent = new AttackArea { A = new(-10,0), B = new(10,0), Radius = 1.5 };
        var dodge = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), Arena, warnings: [imminent]);
        Check(Math.Abs(dodge.Direction.Z) > .5, "attack escape outranks preferred DPS distance");
        imminent.ActiveFor = .05;
        var expiring = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), Arena, warnings: [imminent]);
        Check(Math.Abs(expiring.Direction.Z) > .5, "imminent warning expiry does not justify waiting in its footprint");
        var fork = Planner.Choose(new(9,0), 4, [Boss(4,0),Boss(9,6)], [], new(), Arena);
        Check(fork.Direction.Z < -.5, "wall escape accounts for a second boss blocking the upper route");
        var trapped = Planner.Choose(new(), 4, [Boss(-6,0)], [], new(), _ => false);
        Check(trapped.Direction.X == 0 && trapped.Direction.Z == 0, "unreachable geometry does not create a fictitious exit");
        var watch = System.Diagnostics.Stopwatch.StartNew(); int count = 100;
        var crowd = Enumerable.Range(0, 104).Select(i => new Threat { Position = new(15 + i % 10, -15 + i / 10), Radius = .4, Velocity = new(-.3,.3) }).ToList(); crowd.Add(Boss(-7,0));
        for (int i = 0; i < count; i++) Planner.Choose(new(), 4, crowd, [], new(0,1), Arena);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pandora-kiting.json"), System.Text.Json.JsonSerializer.Serialize(new {
            status = "passed", realGameTouched = false, averageMs = watch.Elapsed.TotalMilliseconds / count, routes
        }, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
}

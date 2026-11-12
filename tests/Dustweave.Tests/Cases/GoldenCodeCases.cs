using Dustweave;
using Dustweave.Golden;
using System.Text.Json;

static class GoldenCodeCases
{
    public static Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string key) { try { action(); } catch (InvalidDataException e) { Check(e.Message == key, key + " rejects with actionable error"); return; } throw new Exception("Accepted " + key); }
        Formation Team() => new() { Season = 50, Width = 6, Height = 5, Units = [new() { Costume = 10101, Position = 0 }, new() { Costume = 503402, Position = 29 }], Attack = [27, 1], Defense = [4] };
        var sample = Team(); string code = FormationCode.Encode(sample); var decoded = FormationCode.Decode(code);
        Check(decoded.Units[1].Costume == 503402 && decoded.Units[1].Position == 29 && decoded.Attack.SequenceEqual([27, 1]) && FormationCode.Encode(decoded) == code, "formation code roundtrip preserves identities, positions, order and both blessing sets");
        Check(FormationCode.Encode(new() { Season = sample.Season, Width = 6, Height = 5, Units = sample.Units, Attack = [1, 27], Defense = [4] }) != code, "blessing order is preserved instead of silently treated as an unordered set");
        Check(code.Length < 60, "two-unit code stays below sixty ASCII characters");
        Reject(() => FormationCode.Decode(code[..^1] + (code[^1] == 'A' ? 'B' : 'A')), "goldencode.invalid");
        Reject(() => FormationCode.Decode("BD2G2.abc"), "goldencode.version");
        Reject(() => FormationCode.Decode("BD2G1." + new string('A', 4096)), "goldencode.invalid");
        Reject(() => FormationCode.Encode(new() { Season = 1, Width = 6, Height = 5, Units = [new() { Costume = 1, Position = 0 }, new() { Costume = 2, Position = 0 }] }), "goldencode.invalid");
        Reject(() => FormationCode.Encode(new() { Season = 1, Width = 6, Height = 5, Units = [new() { Costume = 1, Position = 0 }, new() { Costume = 1, Position = 1 }] }), "goldencode.invalid");
        var vectors = new List<object>();
        void Vector(Formation f) => vectors.Add(new { code = FormationCode.Encode(f), formation = new { season = f.Season, width = f.Width, height = f.Height, units = f.Units.Select(u => new { costume = u.Costume, position = u.Position }), attack = f.Attack, defense = f.Defense } });
        Vector(sample); Vector(new() { Season = int.MaxValue, Width = 256, Height = 256, Units = [new() { Costume = int.MaxValue, Position = 65535 }], Attack = [int.MaxValue], Defense = [] });
        Vector(new() { Season = 0, Width = 1, Height = 1, Units = [new() { Costume = 1, Position = 0 }] });
        var random = new Random(20261112); var seen = new Dictionary<string, string>();
        for (int t = 0; t < 2500; t++)
        {
            int count = random.Next(1, 31); var f = new Formation { Season = random.Next(), Width = 6, Height = 5,
                Units = Enumerable.Range(0, count).Select(i => new FormationUnit { Costume = 1000000 + t * 64 + i, Position = i }).OrderBy(_ => random.Next()).ToArray(),
                Attack = [1, random.Next(2, 100000)], Defense = [random.Next(1, 100000)] };
            string encoded = FormationCode.Encode(f), identity = JsonSerializer.Serialize(f, new JsonSerializerOptions { IncludeFields = true });
            if (seen.TryGetValue(encoded, out var other) && other != identity) throw new Exception("Distinct formations collided"); seen[encoded] = identity;
            if (FormationCode.Encode(FormationCode.Decode(encoded)) != encoded) throw new Exception("Canonical roundtrip failed");
            if (t < 100) Vector(f);
        }
        Check(seen.Count == 2500, "2500 varying future IDs, seasons and ordered formations remain distinct and roundtrip canonically");
        var r = new FormationRules { Season = 51, Width = 6, Height = 5, Starts = 100, Ends = 200, MaxUnits = 10, CostLimit = 200,
            CompleteCosts = true, DefaultCost = 30, Costs = new() { [10101] = 50 }, AttackPoints = 4, DefensePoints = 4, BlessingCosts = new() { [1] = 1, [27] = 2, [4] = 3 } };
        int[] owned = [10101, 503402];
        Check(FormationImport.Validate(sample, r, owned, 101) == 80, "old-season code imports using current authoritative explicit and complete default costs");
        r.CostLimit = 79; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.cost_exceeded"); r.CostLimit = 200;
        r.CompleteCosts = false; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.cost_missing"); r.CompleteCosts = true;
        r.BannedBlessings = [27]; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.bless_unavailable"); r.BannedBlessings = [];
        r.BlessingCosts.Remove(27); Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.bless_unavailable"); r.BlessingCosts[27] = 2;
        r.DefensePoints = 2; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.bless_points"); r.DefensePoints = 4;
        Reject(() => FormationImport.Validate(sample, r, [10101], 101), "goldencode.missing_costumes");
        Reject(() => FormationImport.Validate(sample, r, owned, 200), "goldencode.season_unavailable");
        r.BannedPositions = [29]; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.banned_unit"); r.BannedPositions = [];
        r.Width = 5; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.board_changed"); r.Width = 6;
        r.MaxUnits = 1; Reject(() => FormationImport.Validate(sample, r, owned, 101), "goldencode.too_many"); r.MaxUnits = 10;
        Check(FormationImport.Validate(sample, r, owned, 101) == 80, "restored current rules allow import again without clearing account state");
        DailyJson.Write(Path.Combine(root, "golden-code-vectors.json"), vectors);
        return Task.CompletedTask;
    }
}

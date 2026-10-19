using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyAbsorptionVisit(string Operation, long Pack, long Map, long AtUtcTicks,
    bool AlongNpc, DailyDisplayName PackName, DailyDisplayName MapName);
public sealed record DailyAbsorptionDay(string Account, string Player, string Server, string Cycle, IReadOnlyList<DailyAbsorptionVisit> Visits)
{
    public int Maps => Visits.Select(v => (v.Pack, v.Map)).Distinct().Count();
}

// One compact current-day projection per account. It is a report, never a source
// for deciding whether a map is complete or authorizing another skill use.
public static class DailyAbsorptionLog
{
    private static readonly object gate = new();
    private static string PathFor(string root, string account)
    {
        if (!DailyProfiles.ValidKey(account)) throw new InvalidDataException("Invalid absorption report account");
        return Path.Combine(root, "live", "absorption-history", account + ".json");
    }
    private static DailyAbsorptionDay? Load(string root, string account)
    {
        string path = PathFor(root, account);
        // Most accounts have no report before their first cast. Do not spend the
        // shared reader's transient-file retry budget on every timeline refresh.
        if (!File.Exists(path)) return null;
        var day = DailyJson.TryRead<DailyAbsorptionDay>(path);
        return day != null && day.Account == account && !string.IsNullOrEmpty(day.Player) && !string.IsNullOrEmpty(day.Server)
            && long.TryParse(day.Cycle, out var cycle) && cycle > 0 && day.Visits != null
            && day.Visits.All(v => v != null && !string.IsNullOrEmpty(v.Operation) && v.Pack > 0 && v.Map > 0
                && v.AtUtcTicks > 0 && v.AtUtcTicks <= DateTime.MaxValue.Ticks && v.PackName != null && v.MapName != null) ? day : null;
    }
    public static DailyAbsorptionDay? Read(string root, JsonObject context)
    {
        try
        {
            var day = Load(root, S(context["actor"]?[3]));
            return day != null && day.Player == S(context["actor"]?[4]) && day.Server == S(context["server"])
                && day.Cycle == S(context["cycle"]) ? day : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { return null; }
    }
    internal static QueueView Attach(string root, QueueView view)
    {
        if (view.Period is not { } period) return view;
        var day = Read(root, O(("actor", new JsonArray(0, 0, "", view.Account, period.Player)), ("server", period.Server), ("cycle", period.Cycle)));
        if (day == null) return view;
        QueueStage Enrich(QueueStage stage) => stage.Task == "weekly_mainline" ? stage with { Absorptions = day } : stage;
        return view with { Stages = view.Stages.Select(Enrich).ToArray(), PlanStages = view.PlanStages?.Select(Enrich).ToArray() };
    }
    internal static DailyAbsorptionDay Merge(DailyAbsorptionDay? old, DailyAbsorptionDay incoming)
    {
        if (old == null) return incoming;
        if (old.Account != incoming.Account || old.Player != incoming.Player || old.Server != incoming.Server) return old;
        if (old.Cycle != incoming.Cycle) return long.Parse(old.Cycle) > long.Parse(incoming.Cycle) ? old : incoming;
        return incoming with { Visits = old.Visits.Concat(incoming.Visits).DistinctBy(v => v.Operation).OrderBy(v => v.AtUtcTicks).ThenBy(v => v.Operation).ToArray() };
    }
    public static void Record(string root, JsonObject op)
    {
        if (S(op["state"]) != "completed" || !B(op["result"]?["IsSuccess"])
            || DailyFieldTalentProof.AbsorptionLocation(op) is not { } location) return;
        string account = S(op["account"]), cycle = S(op["cycle"]);
        if (!DailyProfiles.ValidKey(account) || !long.TryParse(cycle, out long n) || n <= 0) return;
        DailyDisplayName Name(string key) => op["scope"]?[key]?.Deserialize<DailyDisplayName>() ?? new("", "", "");
        var incoming = new DailyAbsorptionDay(account, S(op["player"]), S(op["server"]), cycle,
            [new(S(op["id"]), location.Pack, location.Map, N(op["at"]), B(op["scope"]?["along_npc"]), Name("pack_name"), Name("map_name"))]);
        if (incoming.Player.Length == 0 || incoming.Server.Length == 0 || incoming.Visits[0].Operation.Length == 0
            || incoming.Visits[0].AtUtcTicks <= 0 || incoming.Visits[0].AtUtcTicks > DateTime.MaxValue.Ticks) return;
        lock (gate)
        {
            var old = Load(root, account);
            var merged = Merge(old, incoming);
            if (old == null || !old.Visits.SequenceEqual(merged.Visits) || old.Cycle != merged.Cycle)
                DailyJson.Write(PathFor(root, account), merged);
        }
    }
    internal static void Copy(string source, string target, string account)
    {
        lock (gate)
        {
            var incoming = Load(source, account);
            if (incoming == null) return;
            var old = Load(target, account);
            var merged = Merge(old, incoming);
            if (old == null || !old.Visits.SequenceEqual(merged.Visits) || old.Cycle != merged.Cycle)
                DailyJson.Write(PathFor(target, account), merged);
        }
    }
}

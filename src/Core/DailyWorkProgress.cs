using System.Text.Json;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Presentation only: never used as completion proof or replay permission.</summary>
public sealed record DailyWorkProgress(string Step, int Completed = 0, int Total = 0, string Unit = "")
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : 0;
    public static DailyWorkProgress? Read(JsonElement item, string queueState)
    {
        if (queueState != "running" || !item.TryGetProperty("state", out var state)
            || state.GetString() is not ("running" or "pending")) return null;
        return ReadRecorded(item);
    }
    public static DailyWorkProgress? ReadRecorded(JsonElement item)
    {
        if (!item.TryGetProperty("progress", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        string Text(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int Count(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? Math.Max(0, n) : 0;
        int total = Count("total");
        return new(Text("step"), Math.Min(Count("completed"), total), total, Text("unit"));
    }

    // The budget starts when this run begins. Previously spent uses and work
    // that needs a future reset are not part of today's denominator.
    public static DailyWorkProgress Allowance(int start, int current, int limit, string unit, int remaining = int.MaxValue)
    {
        int budget = Math.Max(0, limit - Math.Max(0, start));
        int completed = (int)Math.Clamp((long)current - start, 0, budget);
        int total = (int)Math.Min(budget, (long)completed + Math.Max(0, remaining));
        return new("plan", completed, total, unit);
    }
    public JsonObject ToJson() => new() { ["step"] = Step, ["completed"] = Completed, ["total"] = Total, ["unit"] = Unit };
    internal static IDisposable Observe(DailyManagedBusiness business, Action<JsonObject, JsonArray, JsonObject> observer)
        => new Observation(business, observer);
    private sealed class Observation : IDisposable
    {
        private readonly DailyManagedBusiness business;
        private readonly Action<JsonObject, JsonArray, JsonObject> observer;
        public Observation(DailyManagedBusiness business, Action<JsonObject, JsonArray, JsonObject> observer)
        { this.business = business; this.observer = observer; business.WorkObserved += observer; }
        public void Dispose() => business.WorkObserved -= observer;
    }
    internal static int Responses(JsonObject operation, JsonArray events, JsonObject after, string role)
    {
        if (operation["before"]?["Frame"] is not JsonObject before || after["Frame"] is not JsonObject frame
            || !DailyEvidence.SameActor(before, frame)) return 0;
        return events.OfType<JsonObject>().Where(e => DailyData.S(e["Role"]) == role && DailyData.S(e["Kind"]) == "response"
            && DailyData.B(e["Accepted"]) && DailyData.N(e["Sequence"]) > 0
            && e["Frame"] is JsonObject actor && DailyEvidence.SameActor(before, actor))
            .Select(e => DailyData.N(e["Sequence"])).Distinct().Count();
    }

    // A handled stage is not necessarily a successful game objective. Keep
    // business states, warnings and retry eligibility entirely independent.
    public static bool Handled(QueueStage stage) => stage.State is "completed" or "skipped" or "partial" or "blocked" or "failed" or "error"
        || stage.State == "recovery_required" && stage.FinishedAt.HasValue;
    public static int Count(IReadOnlyList<QueueStage> stages) => stages.Count(s => s.State != "skipped");
    public static int HandledCount(IReadOnlyList<QueueStage> stages) => stages.Count(s => s.State != "skipped" && Handled(s));

    // Coarse effort weights keep a multi-map journey from counting like one click.
    // These are not elapsed-time predictions. Only confirmed units advance a running stage.
    public static int Weight(string task) => task switch
    {
        "weekly_mainline" => 16, "weekly_npc" or "weekly_steal" or "trade" => 6,
        "territory_saffron" or "mirror" or "event_battle" => 4,
        "management" or "hunting" => 2, _ => 1
    };
    public static double Percent(IReadOnlyList<QueueStage> stages)
    {
        if (stages.Count == 0) return 0;
        var work = stages.Where(s => s.State != "skipped").ToArray();
        if (work.All(Handled)) return 100;
        double done = work.Sum(s => Weight(s.Task) * (Handled(s) ? 1d : (s.Work ?? s.RecordedWork)?.Fraction ?? 0));
        return Math.Min(99, 100 * done / work.Sum(s => Weight(s.Task)));
    }

    public static string Operation(string role, JsonObject scope) => role switch
    {
        "dispatch.start" => DailyData.S(scope["_proof"]?["kind"]) == "bargain" ? "bargain" : Talent((int)DailyData.N(scope["kind"])),
        "mainline.waypoint_use" => "travel",
        "mirror.end" or "weekly.book.start" or "event.native_batch" => "battle",
        "hunt.dispatch" or "event.sweep" or "salvation.sweep" => "sweep",
        "equipment.craft" or "trade.cook" => "craft",
        "equipment.refine" or "equipment.enchant" => "enhance",
        "equipment.recycle" => "recycle",
        "trade.buy" => "buy", "trade.sell" => "sell",
        "dispatch.claim_and_start" or "dispatch.collect_all" => "dispatch",
        "gacha.free_all" => "draw", "friendship.counsel" => "counsel",
        "management.collect" or "cafeteria.collect" or "life.helpers" or "fishing.trap" => "collect",
        _ => "execute"
    };
    public static string Talent(int kind) => kind switch
    {
        3 => "suppress", 4 => "absorb", 6 => "search", 17 => "stealth", 15 => "travel", 1 => "steal", _ => "talent"
    };
}

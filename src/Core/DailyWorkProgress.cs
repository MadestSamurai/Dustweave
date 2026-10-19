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
            || state.GetString() is not ("running" or "pending")
            || !item.TryGetProperty("progress", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        string Text(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int Count(string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? Math.Max(0, n) : 0;
        int total = Count("total");
        return new(Text("step"), Math.Min(Count("completed"), total), total, Text("unit"));
    }

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
        if (stages.All(s => s.State is "completed" or "skipped")) return 100;
        double done = stages.Sum(s => Weight(s.Task) * (s.State is "completed" or "skipped" ? 1d : s.Work?.Fraction ?? 0));
        return Math.Min(99, 100 * done / stages.Sum(s => Weight(s.Task)));
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

using System.Text.Json.Nodes;
namespace Dustweave;

// Readiness checks share the enclosing operation's deadline. A nested read may
// wait for fresh data, but cannot restart or extend that operation's budget.
internal sealed class DailyObservationWindow : IDisposable
{
    private static readonly AsyncLocal<DailyObservationWindow?> current = new();
    private readonly DailyObservationWindow? parent;
    private readonly Func<double> clock;
    private readonly double end;
    internal static DailyObservationWindow? Current => current.Value;
    internal double Remaining => Math.Max(0, Math.Min(end - clock(), parent?.Remaining ?? double.PositiveInfinity));
    internal static DailyObservationWindow Begin(double seconds, Func<double> clock) => new(seconds, clock);
    internal static DailyObservationWindow Inherit(double fallbackSeconds, Func<double> clock) => new(Current?.Remaining ?? fallbackSeconds, clock);
    private DailyObservationWindow(double seconds, Func<double> clock)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        this.clock = clock;
        end = clock() + seconds;
        parent = current.Value;
        current.Value = this;
    }
    public void Dispose() => current.Value = parent;
    internal static StageHostException Timeout(string message) => new("adapter", message) { HoldQueue = true, ObservationTimedOut = true };
}

public sealed partial class DailyCommandDriver
{
    // Evidence is published before the UI snapshot. A known identity-sync frame is not a new actor.
    private static bool EvidenceFrameReady(JsonObject frame, JsonObject bound)
    {
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance" })
            if (frame[key] == null || !JsonNode.DeepEquals(frame[key], bound[key]))
                throw new StageHostException("identity", "证据所属游戏进程或连接已变化，已保留进度。");
        foreach (string key in new[] { "AccountKey", "PlayerKey" })
            if (Text(frame, key).Length > 0 && !JsonNode.DeepEquals(frame[key], bound[key]))
                throw new StageHostException("identity", "证据所属账号或角色已变化，已保留进度。");
        string error = Text(frame, "Error");
        if (error == "Waiting for fresh account identity") return false;
        if (error.Length > 0)
            throw new StageHostException("adapter", "证据观察失败：" + error);
        if (!DailyEvidence.SameActor(frame, bound))
            throw new StageHostException("adapter", "证据缺少账号身份，且未报告可恢复的切场状态。");
        return true;
    }
    private static JsonObject EvidenceFrameSummary(JsonObject frame)
    {
        var result = new JsonObject();
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey", "Scene", "AtUtcTicks", "Error" })
            result[key] = frame[key]?.DeepClone();
        return result;
    }
    private void SaveEvidenceObservation(string outcome, JsonObject expected, JsonObject? first, JsonObject? last, JsonObject request, double elapsed)
    {
        try
        {
            DailyJson.Write(Path.Combine(root, "live", "evidence-observations", now() + "-" + Guid.NewGuid().ToString("N") + ".json"),
                new { outcome, expected = EvidenceFrameSummary(expected), first, last, request, elapsedSeconds = elapsed, gameplayReplayed = false });
        }
        catch { /* Diagnostics must not mask identity/stop failures or interrupt recovered gameplay. */ }
    }
}

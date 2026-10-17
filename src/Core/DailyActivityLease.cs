using System.Text.Json;
namespace Dustweave;

/// <summary>No command was submitted: a fresh identity observation is temporarily unavailable.</summary>
public sealed class DailyObservationPendingException(string reason, string detail = "") : IOException("等待游戏身份数据恢复；未发送操作。")
{
    public string Reason { get; } = reason;
    public string Detail { get; } = detail;
}
public sealed class DailyToolSessionChangedException(string message) : InvalidOperationException(message);

public static class DailyToolIdentity
{
    public static DailySnapshot Require(GameInstance game, string account, string player,
        Func<GameInstance?> find, Func<DailyIdentityRead> read, Action<int>? delay = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        DailyIdentityRead observed = new(null, "snapshot-missing");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (find() != game) throw new DailyToolSessionChangedException("游戏会话已结束，请返回日常助手重新连接。");
            observed = read();
            var snapshot = observed.Snapshot;
            if (observed.Problem == "process-mismatch" || snapshot != null && (snapshot.ProcessId != game.ProcessId || snapshot.ProcessStartTicks != game.StartTicks))
                throw new DailyToolSessionChangedException("游戏进程已变化，请返回日常助手重新连接。");
            // A positive identity mismatch is terminal, even during the retry window.
            // Missing/old data grants no permission; only a new matching frame can pass.
            if (snapshot != null && ((!string.IsNullOrEmpty(snapshot.AccountKey) && snapshot.AccountKey != account) || (!string.IsNullOrEmpty(snapshot.PlayerKey) && snapshot.PlayerKey != player)))
                throw new DailyToolSessionChangedException("游戏账号已变化，请返回日常助手重新连接。");
            if (snapshot != null && snapshot.AccountKey == account && snapshot.PlayerKey == player && !string.IsNullOrEmpty(account) && !string.IsNullOrEmpty(player) && snapshot.FrameUtcTicks >= DateTime.UtcNow.AddSeconds(-5).Ticks)
                return snapshot;
            if (snapshot != null) observed = new(snapshot, snapshot.FrameUtcTicks < DateTime.UtcNow.AddSeconds(-5).Ticks ? "snapshot-stale" : "identity-loading", snapshot.State);
            // Retry only the read, never its caller's command; a slow transport call
            // already used its deadline and is left to the tool's next async poll.
            if (attempt == 2 || watch.ElapsedMilliseconds > 200) break;
            (delay ?? Thread.Sleep)(25 * (attempt + 1));
        }
        throw new DailyObservationPendingException(observed.Problem, observed.Detail);
    }
}

/// <summary>Exclusive execution ownership, independent of a settings window's lifetime.</summary>
public sealed class DailyActivityLease(string root, Func<DateTime>? clock = null) : IDisposable
{
    readonly object sync = new();
    IDisposable? lease;
    DateTime lastActive;
    DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;
    public bool Held { get { lock(sync) return lease != null; } }
    public void Enter() { lock(sync) { lease ??= DailyToolControl.Acquire(root); lastActive = Now; } }
    public void Observe(bool enabled, bool gameAlive, bool fresh, bool pending)
    {
        lock(sync)
        {
            if(lease == null) return;
            if(!gameAlive) { Release(); return; }
            if(enabled || !fresh || pending) { lastActive = Now; return; }
            if(Now - lastActive >= TimeSpan.FromMilliseconds(750)) Release();
        }
    }
    void Release() { lease?.Dispose(); lease = null; }
    public void Dispose() { lock(sync) Release(); }
}

public static class DailyActivityRules
{
    public static bool ExecutionPending(string? reason) => !string.IsNullOrEmpty(reason) && !reason.EndsWith("snapshot writer",StringComparison.Ordinal);
    public static bool RequiresOwnership(string name, byte[] bytes)
    {
        string leaf = name[(name.LastIndexOf('~') + 1)..];
        // Capture-only hints and cancellation never take control from another activity.
        if(leaf is "stop" or "pause" or "enabled-until.txt" or "catalog-request.json" or "observation-request.json") return false;
        if(leaf is "control.json" or "execution-lease.json" or "lease.json")
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var value = document.RootElement;
                if(value.TryGetProperty("Enabled", out var enabled)) { if(enabled.ValueKind == JsonValueKind.False)return false;if(enabled.ValueKind==JsonValueKind.True)return true; }
                foreach(var key in new[]{"UntilUtcTicks","Expires","ExpiresUtcTicks"})
                    if(value.TryGetProperty(key,out var until) && until.TryGetInt64(out var ticks) && ticks == 0) return false;
            }
            catch(JsonException) { } // Invalid commands cannot bypass ownership.
        }
        return true;
    }
}

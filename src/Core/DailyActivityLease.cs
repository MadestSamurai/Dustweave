using System.Text.Json;
namespace Dustweave;

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

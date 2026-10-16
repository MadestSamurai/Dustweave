namespace Dustweave;

public static class DailySandboxStorage
{
    // Settings remain copy-on-write. Operational state must not fall through to
    // the host: appending a sandbox log otherwise copies an entire host log.
    public static readonly string[] PrivatePaths = ["live", "suite", "suite-cache", "rules", "trade", "tools", "updates",
        "queue-history", "parallel-progress", "parallel-workers", "*.log*", "*.jsonl*", "queue-ui.json", "queue-stop", "parallel-queue.json", "connection.json",
        "run.json", "run-error.json", "ui-operation-error.json", "connection-watchdog.json", "startup-permit.json",
        "log-cleanup-last.json", "cache-location.json", "cache-storage.lock"];
    public static string[] Paths(string root) => PrivatePaths.Select(p=>Path.Combine(root,p)).ToArray();
}

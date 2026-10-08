using System.Diagnostics;
using System.Text.Json;

namespace Dustweave;

public static class DailyPluginProbe
{
    public static string InstalledGameManagedDirectory()
    {
        string executable = Accounts.GameLauncher.ResolveExecutable();
        string managed = Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable) + "_Data", "Managed");
        if (!Directory.Exists(managed)) throw new IOException("plugins.client_missing");
        return managed;
    }
    public static async Task RunAsync(string executable, string dataRoot, DailyPluginInfo info, CancellationToken token = default, TimeSpan? limit = null)
    {
        string folder = Path.Combine(dataRoot, "extensions", "checks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string report = Path.Combine(folder, "result.json");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.GetCurrentDirectory() };
        start.ArgumentList.Add("--check-plugin"); start.ArgumentList.Add(report);
        start.Environment["DUSTWEAVE_PLUGIN"] = info.Root;
        // Reuse only content-keyed interface caches, in a root separate from live accounts/queues.
        start.Environment["BD2_DAILY_DATA_ROOT"] = Path.Combine(dataRoot, "extensions", "check-cache");
        using var child = Process.Start(start) ?? throw new IOException("plugins.probe_failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(90));
        try { await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().ConfigureAwait(false);
            DailyJson.Write(Path.Combine(folder, "timeout.json"), new { child = child.Id, stopped = child.HasExited, atUtc = DateTimeOffset.UtcNow, realGameTouched = false });
            token.ThrowIfCancellationRequested(); throw new IOException("plugins.probe_timeout");
        }
        if (child.ExitCode != 0 || !File.Exists(report)) throw new IOException("plugins.probe_failed");
        using var result = JsonDocument.Parse(await File.ReadAllBytesAsync(report, token).ConfigureAwait(false));
        if (result.RootElement.GetProperty("status").GetString() != "passed" || result.RootElement.GetProperty("info").GetProperty("Fingerprint").GetString() != info.Fingerprint)
            throw new IOException("plugins.probe_failed");
    }
}

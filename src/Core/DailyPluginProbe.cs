using System.Diagnostics;
using System.Text.Json;

namespace Dustweave;

public static class DailyPluginProbe
{
    internal const string GameExecutableVariable = "DUSTWEAVE_PLUGIN_GAME_EXECUTABLE";
    public static string InstalledGameManagedDirectory()
        => GameManagedDirectory(Environment.GetEnvironmentVariable(GameExecutableVariable), Accounts.GameLauncher.ResolveExecutable);

    internal static string GameManagedDirectory(string? selectedExecutable, Func<string> installedExecutable)
    {
        // A read-only probe must compile against the same client the host found,
        // even when that installation has no launcher registry entry. Validate
        // again in the child in case files moved after the host selected them.
        string executable = Accounts.GameInstallation.Validate(selectedExecutable ?? installedExecutable());
        return Path.Combine(Path.GetDirectoryName(executable)!, "BrownDust II_Data", "Managed");
    }
    public static string FailureCode(Exception error) => error is Accounts.SessionManagerException || error.Message == "plugins.client_missing"
        ? "plugins.client_missing" : "plugins.probe_failed";

    // Preserve the original public entry for existing extension/package callers.
    public static Task RunAsync(string executable, string dataRoot, DailyPluginInfo info, CancellationToken token = default, TimeSpan? limit = null)
        => RunAsync(executable, dataRoot, info, null, token, limit);

    public static async Task RunAsync(string executable, string dataRoot, DailyPluginInfo info, string? gameExecutable, CancellationToken token = default, TimeSpan? limit = null)
    {
        string folder = Path.Combine(Path.GetFullPath(dataRoot), "extensions", "checks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string report = Path.Combine(folder, "result.json");
        var start = new ProcessStartInfo(info.IsTool && info.ToolApiVersion == 1 ? Path.Combine(info.Root, info.ToolExecutable) : executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = info.IsTool ? info.Root : Directory.GetCurrentDirectory() };
        if(info.ToolApiVersion == 2) { start.ArgumentList.Add("--tool-plugin"); start.ArgumentList.Add(info.Root); start.ArgumentList.Add(info.Fingerprint); }
        start.ArgumentList.Add(info.IsTool ? "--dustweave-probe" : "--check-plugin"); start.ArgumentList.Add(report);
        DailyPlugin.PinEnvironment(start, info.IsTool ? new(false,"missing","","","",[]) : info);
        start.Environment.Remove(GameExecutableVariable);
        if (gameExecutable != null) start.Environment[GameExecutableVariable] = gameExecutable;
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
        DailyJson.Write(Path.Combine(folder, "process.json"), new { atUtc = DateTimeOffset.UtcNow, child = child.Id, exitCode = child.ExitCode, reportExists = File.Exists(report), plugin = info.Id, version = info.Version, fingerprint = info.Fingerprint, realGameTouched = false });
        if (!File.Exists(report)) throw new IOException("plugins.probe_failed", new IOException($"Plugin check exited with code {child.ExitCode} without a result report. Record: {folder}"));
        try
        {
            using var result = JsonDocument.Parse(await File.ReadAllBytesAsync(report, token).ConfigureAwait(false));
            if (child.ExitCode != 0 || !result.RootElement.TryGetProperty("status", out var status) || status.GetString() != "passed")
            {
                string detail = result.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString()! : "Plugin check did not pass.";
                string code = result.RootElement.TryGetProperty("code", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "plugins.client_missing"
                    ? "plugins.client_missing" : "plugins.probe_failed";
                throw new IOException(code, new IOException($"{detail}\nExit code: {child.ExitCode}. Record: {report}"));
            }
            if (info.IsTool)
            {
                if (!result.RootElement.TryGetProperty("toolApiVersion", out var protocol) || protocol.GetInt32() != info.ToolApiVersion || DailyPlugin.Inspect(info.Root).Fingerprint != info.Fingerprint)
                    throw new IOException("plugins.probe_failed");
                if(info.HookSources.Length>0)await CheckToolHooksAsync(executable,folder,info,gameExecutable,token);
            }
            else if (!result.RootElement.TryGetProperty("info", out var checkedInfo) || !checkedInfo.TryGetProperty("Fingerprint", out var fingerprint) || fingerprint.GetString() != info.Fingerprint)
                throw new IOException("plugins.probe_failed", new IOException($"Plugin check returned a different or missing package fingerprint. Record: {report}"));
        }
        catch (JsonException error) { throw new IOException("plugins.probe_failed", error); }
    }
    private static async Task CheckToolHooksAsync(string executable,string folder,DailyPluginInfo info,string? gameExecutable,CancellationToken token)
    {
        string report=Path.Combine(folder,"tool-hooks.json");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("--check-plugin");start.ArgumentList.Add(report);
        DailyPlugin.PinEnvironment(start,info);
        start.Environment["BD2_DAILY_DATA_ROOT"]=Path.Combine(folder,"hook-check-cache");
        if(gameExecutable!=null)start.Environment[GameExecutableVariable]=gameExecutable;
        using var process=Process.Start(start)??throw new IOException("plugins.probe_failed");
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromSeconds(120));
        try{await process.WaitForExitAsync(limit.Token);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(entireProcessTree:true);throw;}
        using var result=JsonDocument.Parse(await File.ReadAllBytesAsync(report,token));
        if(process.ExitCode!=0||result.RootElement.GetProperty("status").GetString()!="passed"||result.RootElement.GetProperty("info").GetProperty("Fingerprint").GetString()!=info.Fingerprint)
            throw new IOException("plugins.probe_failed",new IOException(result.RootElement.ToString()));
    }
}

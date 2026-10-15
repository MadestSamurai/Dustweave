using System.Diagnostics;
using System.IO;

namespace Dustweave.Desktop;

// A separate UI process keeps this gate ahead of WPF tool hosts, sandbox workers
// and connection helpers, without initializing an Application twice.
internal static class LegacyMigrationStartup
{
    private const string ScopeVariable = "DUSTWEAVE_LEGACY_STARTUP_SCOPE";
    internal const int AcceptedExitCode = 42;
    private static string? ownedScope;
    internal static bool UpdateAcknowledged { get; private set; }
    internal sealed record Scope(int Pid, long Started, string Executable, string Version, bool Accepted);

    private static bool OwnerAlive(Scope scope)
    {
        try
        {
            using var owner = Process.GetProcessById(scope.Pid);
            return !owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == scope.Started &&
                string.Equals(owner.MainModule?.FileName, scope.Executable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(scope.Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                scope.Version == DailyProductVersion.Current;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool InheritsAcknowledgement()
    {
        var path = Environment.GetEnvironmentVariable(ScopeVariable);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var scope = DailyJson.TryRead<Scope>(path);
        return scope is { Accepted: true } && OwnerAlive(scope);
    }

    internal static bool Ensure(string[] args)
    {
        if (!DailyLegacyMigration.RequiresAcknowledgement(args) || InheritsAcknowledgement()) return true;
        Environment.SetEnvironmentVariable(ScopeVariable, null);
        using var self = Process.GetCurrentProcess();
        var scope = new Scope(self.Id, self.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!, DailyProductVersion.Current, false);
        var directory = Path.Combine(DailyIdentity.DataRoot, "legacy-startup", Guid.NewGuid().ToString("N"));
        var request = Path.Combine(directory, "request.json");
        var ready = Path.Combine(directory, "visible.json");
        DailyJson.Write(request, scope);
        var start = new ProcessStartInfo(scope.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--legacy-migration-gate"); start.ArgumentList.Add(request);
        try
        {
            using var dialog = Process.Start(start) ?? throw new IOException("Migration notice did not start.");
            while (!dialog.WaitForExit(100))
            {
                if (!UpdateAcknowledged && args.Length == 2 && args[0] == "--updated" && File.Exists(ready))
                {
                    // A rendered notice proves healthy startup, not user consent. No game code has run.
                    DailyUpdateInstaller.Acknowledge(args[1]); UpdateAcknowledged = true;
                }
            }
            if (dialog.ExitCode != AcceptedExitCode) return false;
            DailyJson.Write(request, scope with { Accepted = true });
            ownedScope = request;
            Environment.SetEnvironmentVariable(ScopeVariable, request);
            return true;
        }
        finally
        {
            TryDelete(ready);
            if (ownedScope != request) { TryDelete(request); TryRemoveEmpty(directory); }
        }
    }

    internal static Scope RequireOwner(string request)
    {
        var scope = DailyJson.TryRead<Scope>(request) ?? throw new InvalidDataException("Missing migration startup owner.");
        if (scope.Accepted || !OwnerAlive(scope)) throw new InvalidDataException("Migration startup owner has exited.");
        return scope;
    }
    internal static void Dispose()
    {
        if (ownedScope is {} path) { TryDelete(path); TryRemoveEmpty(Path.GetDirectoryName(path)!); ownedScope = null; }
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void TryRemoveEmpty(string path) { try { Directory.Delete(path, false); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

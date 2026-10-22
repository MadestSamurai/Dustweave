namespace Dustweave;

// Only immutable game inputs bypass copy-on-write. In particular, launcher.settings,
// LocalLow, the registry and all account/cache/log directories remain private.
internal static class DailySandboxGameFiles
{
    internal static string[] Rules(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) ||
            !Path.GetFileName(executable).Equals("BrownDust II.exe", StringComparison.OrdinalIgnoreCase) ||
            executable.Any(c => char.IsControl(c) || "*?%,".Contains(c)))
            throw new InvalidDataException("client_update.sync_path");
        string root = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        return new[] { "BrownDust II.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", @"BrownDust II_Data\*", @"MonoBleedingEdge\*" }
            .Select(name => Path.Combine(root, name)).ToArray();
    }

    internal static async Task EnsureAsync(string executable, Func<Task<string>> read,
        Func<Task<bool>> idle, Func<string, Task> append, Action<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] rules = Rules(executable);
        string[] Values(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] missing = rules.Except(Values(await read()), StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length == 0) return;
        // A running process keeps its old rule view. Configure before bootstrap,
        // never change the rules beneath an already running isolated game.
        if (!await idle()) throw new InvalidOperationException("client_update.sync_busy");
        progress("client_update.syncing");
        foreach (string rule in missing)
        {
            token.ThrowIfCancellationRequested();
            await append(rule);
        }
        if (rules.Except(Values(await read()), StringComparer.OrdinalIgnoreCase).Any())
            throw new IOException("client_update.sync_failed");
    }
}

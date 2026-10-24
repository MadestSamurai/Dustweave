namespace Dustweave;

// Addressable lives beside the shipped Unity files but is a WRITABLE cache.
// Never grant a directory-wide ReadFilePath to BrownDust II_Data again.
internal static class DailySandboxGameFiles
{
    internal static async Task<bool> ExperimentAsync(string executable,string boxRoot,string box,string installation,Func<Task<string>> read,
        Func<Task<bool>> idle,Func<string,Task> remove,CancellationToken token)
    {
        var experiment=DailyLoadingExperiment.Active();if(experiment?.Mode!="legacy-files")return false;
        await DailyLoadingFileTransaction.PrepareAsync(experiment,executable,boxRoot,box,read,idle,remove,token);
        return true;
    }
    internal static string[] Rules(string executable)
    {
        string root=GameRoot(executable),data=Path.Combine(root,"BrownDust II_Data");
        var paths=ProgramPaths.Select(name=>Path.Combine(root,name));
        if(Directory.Exists(data))paths=paths.Concat(Directory.EnumerateFiles(data).Where(p=>DailySandboxProgramSync.IsProgram(Path.GetRelativePath(root,p))));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Kept for recognizing/migrating 1.4.17 configuration and pending research
    // transactions. These must never be installed by a production launch.
    internal static string[] LegacyRules(string executable)
        => new[] { "BrownDust II.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", @"BrownDust II_Data\*", @"MonoBleedingEdge\*" }
            .Select(name => Path.Combine(GameRoot(executable), name)).ToArray();

    internal static readonly string[] ProgramPaths = ["BrownDust II.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", @"MonoBleedingEdge\*",
        @"BrownDust II_Data\Managed\*", @"BrownDust II_Data\Plugins\*", @"BrownDust II_Data\Resources\*",
        @"BrownDust II_Data\StreamingAssets\aa\*", @"BrownDust II_Data\StreamingAssets\Video\*",
        @"BrownDust II_Data\StreamingAssets\DefaultDB.db", @"BrownDust II_Data\StreamingAssets\manifest.xml",
        @"BrownDust II_Data\StreamingAssets\google-services-desktop.json", @"BrownDust II_Data\StreamingAssets\UnityServicesProjectConfiguration.json",
        @"BrownDust II_Data\globalgamemanagers", @"BrownDust II_Data\globalgamemanagers.assets",
        @"BrownDust II_Data\boot.config", @"BrownDust II_Data\data.unity3d", @"BrownDust II_Data\ScriptingAssemblies.json",
        @"BrownDust II_Data\RuntimeInitializeOnLoads.json", @"BrownDust II_Data\app.info"];

    internal static string GameRoot(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) ||
            !Path.GetFileName(executable).Equals("BrownDust II.exe", StringComparison.OrdinalIgnoreCase) ||
            executable.Any(c => char.IsControl(c) || "*?%,".Contains(c)))
            throw new InvalidDataException("client_update.sync_path");
        return Path.GetDirectoryName(Path.GetFullPath(executable))!;
    }

    internal static async Task EnsureAsync(string executable, Func<Task<string>> read,
        Func<Task<bool>> idle, Func<string, Task> append, Action<string> progress, CancellationToken token,
        Func<string,Task>? remove = null, DailySandboxFileMode mode = DailySandboxFileMode.Synchronized, string? previousExecutable = null)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("sandbox.manage.mode_invalid");
        string[] rules = mode == DailySandboxFileMode.Synchronized ? Rules(executable) : [];
        string[] Values(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var existing = Values(await read());
        var owned = LegacyRules(executable).Concat(Rules(executable));
        if (!string.IsNullOrEmpty(previousExecutable)) owned = owned.Concat(LegacyRules(previousExecutable)).Concat(Rules(previousExecutable));
        bool Owned(string rule,string exe) {
            string root=GameRoot(exe)+Path.DirectorySeparatorChar;
            return rule.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&DailySandboxProgramSync.IsProgram(rule[root.Length..]);
        }
        var obsolete = existing.Where(r=>owned.Contains(r,StringComparer.OrdinalIgnoreCase)||Owned(r,executable)
            || (!string.IsNullOrEmpty(previousExecutable)&&Owned(r,previousExecutable)))
            .Except(rules,StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = rules.Except(existing, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length == 0 && obsolete.Length == 0) return;
        if (obsolete.Length > 0 && remove == null) throw new IOException("client_update.sync_failed");
        // A running process keeps its old rule view. Configure before bootstrap,
        // never change the rules beneath an already running isolated game.
        if (!await idle()) throw new InvalidOperationException("client_update.sync_busy");
        progress("client_update.syncing");
        foreach (string rule in obsolete)
        {
            token.ThrowIfCancellationRequested();
            await remove!(rule);
        }
        foreach (string rule in missing)
        {
            token.ThrowIfCancellationRequested();
            await append(rule);
        }
        var verified = Values(await read());
        if (rules.Except(verified, StringComparer.OrdinalIgnoreCase).Any() || obsolete.Intersect(verified,StringComparer.OrdinalIgnoreCase).Any())
            throw new IOException("client_update.sync_failed");
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace Dustweave;

/// <summary>Client downloads/backups, not the publisher's ten-version delta baseline window.</summary>
public static class UpdateCacheRetention
{
    public const int MaximumVersions = 2, MaximumDays = 7;
    private sealed record VersionCache(string Path, DailyVersion Version, DailyStorageItem[] Files);
    private sealed record Attempt(string Path, DailyStorageItem[] Files, bool Protected, bool Terminal);
    private static bool Same(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static T? Read<T>(string path) where T : class
    {
        if (!Exists(path)) return null;
        DailyCacheStorage.NoLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 4 * 1024 * 1024) throw new IOException("Update record too large");
        return JsonSerializer.Deserialize<T>(file, DailyUpdateJson.Options);
    }
    private static bool ParentRunning(DailyUpdateJob job)
    {
        if (job.ParentPid <= 0 || job.ParentStartTicks <= 0) return false;
        try
        {
            using var parent = Process.GetProcessById(job.ParentPid);
            return !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == job.ParentStartTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    private static (bool Protected, bool Terminal) State(string attempt)
    {
        try
        {
            string jobPath = Path.Combine(attempt, "job.json"), backup = Path.Combine(attempt, "backup");
            var job = Read<DailyUpdateJob>(jobPath);
            // Copying the helper can fail before a job is created. No install can run without it.
            if (job == null) return (Exists(jobPath) || Exists(backup), false);
            string versionPath = Path.GetDirectoryName(Path.GetDirectoryName(attempt))!;
            if (job.Release == null || job.Asset == null || !Path.IsPathFullyQualified(job.Target) ||
                !Path.GetFileName(job.Target).Equals("Dustweave.exe", StringComparison.OrdinalIgnoreCase) ||
                !Same(job.Directory, versionPath) || Path.GetFileName(versionPath) != job.Release.Version + "-" + job.Asset.Flavor ||
                job.Nonce != Path.GetFileName(attempt) || job.Nonce.Length != 32 || !job.Nonce.All(Uri.IsHexDigit)) return (true, false);
            DailyCacheStorage.NoLinks(job.Target);
            if (ParentRunning(job)) return (true, false);
            string target = Path.GetDirectoryName(job.Target)!, markerPath = Path.Combine(target, DailyUpdateTransaction.MarkerName);
            if (Exists(markerPath))
            {
                var marker = Read<DailyUpdateMarker>(markerPath);
                // Even a terminal journal is still read by the bootstrap until its marker is cleared.
                if (marker == null || Same(marker.JobPath, jobPath)) return (true, false);
            }
            var journal = Read<DailyUpdateJournal>(Path.Combine(backup, "transaction.json"));
            if (journal == null) return (Exists(backup), false);
            bool inventoryStage = (job.Repair || job.InventoryUpgrade) && Same(journal.Staging, Path.Combine(versionPath, "repair-staging"));
            if (!Same(journal.Target, target) || !Same(journal.Backup, backup) || !(Within(journal.Staging, attempt) || inventoryStage)) return (true, false);
            bool terminal = journal.State is "completed" or "rolled_back";
            return (!terminal, terminal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or
            System.ComponentModel.Win32Exception or System.Security.SecurityException or NullReferenceException)
        { return (true, false); }
    }
    public static bool CanClean(DailyCacheUnit unit) => (unit.UpdateAttempts ?? []).All(a => !State(a).Protected);

    public static DailyCacheUnit[] Plan(string updates, DailyStorageItem[] files, DateTime utcNow, CancellationToken cancel = default)
    {
        var versions = new List<VersionCache>();
        foreach (var group in files.GroupBy(f => Path.GetRelativePath(updates, f.Path).Split(Path.DirectorySeparatorChar)[0]))
        {
            int suffix = group.Key.LastIndexOf('-');
            if (suffix < 0 || group.Key[(suffix + 1)..] is not ("Lite" or "Portable")) continue;
            try { versions.Add(new(Path.Combine(updates, group.Key), DailyVersion.Parse(group.Key[..suffix]), group.ToArray())); }
            catch (InvalidDataException) { }
        }
        var recent = versions.Select(v => v.Version).Distinct().OrderByDescending(v => v).Take(MaximumVersions).ToHashSet();
        DateTime cutoff = utcNow.AddDays(-MaximumDays);
        var units = new List<DailyCacheUnit>();
        foreach (var version in versions)
        {
            cancel.ThrowIfCancellationRequested();
            string attemptsRoot = Path.Combine(version.Path, "attempts");
            var attempts = version.Files.Where(f => Within(f.Path, attemptsRoot))
                .GroupBy(f => Path.GetRelativePath(attemptsRoot, f.Path).Split(Path.DirectorySeparatorChar)[0])
                .Select(g =>
                {
                    string path = Path.Combine(attemptsRoot, g.Key); var state = State(path);
                    bool unknown = g.Any(f => Path.GetFileName(f.Path) == "job.json" && !Same(f.Path, Path.Combine(path, "job.json")) ||
                        Path.GetFileName(f.Path) == "transaction.json" && !Same(f.Path, Path.Combine(path, "backup", "transaction.json")));
                    return new Attempt(path, g.ToArray(), state.Protected || unknown, state.Terminal);
                }).ToArray();
            bool unknownRecovery = version.Files.Any(f => Path.GetFileName(f.Path) is "job.json" or "transaction.json" && !Within(f.Path, attemptsRoot));
            bool expired = !recent.Contains(version.Version) || version.Files.Max(f => f.Written) <= cutoff;
            if (expired && !unknownRecovery && attempts.All(a => !a.Protected))
            {
                units.Add(new(version.Path, version.Files) { UpdateAttempts = attempts.Select(a => a.Path).ToArray() });
                continue;
            }
            string? lastTerminal = attempts.Where(a => a.Terminal).OrderByDescending(a => a.Files.Max(f => f.Written)).FirstOrDefault()?.Path;
            foreach (var attempt in attempts.Where(a => !a.Protected))
            {
                if (expired || !attempt.Terminal || attempt.Files.Max(f => f.Written) <= cutoff || attempt.Path != lastTerminal)
                { units.Add(new(attempt.Path, attempt.Files) { UpdateAttempts = [attempt.Path] }); continue; }
                // A confirmed start/rollback no longer needs extraction and helper copies; keep the latest backup/receipt.
                foreach (string name in new[] { "staging", "staging-full", "Dustweave.Updater.exe" })
                {
                    string path = Path.Combine(attempt.Path, name);
                    var discard = attempt.Files.Where(f => Same(f.Path, path) || Within(f.Path, path)).ToArray();
                    if (discard.Length > 0) units.Add(new(path, discard) { UpdateAttempts = [attempt.Path] });
                }
            }
        }
        return units.ToArray();
    }
}

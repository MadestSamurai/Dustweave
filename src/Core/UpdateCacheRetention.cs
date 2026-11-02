using System.Diagnostics;
using System.Text.Json;

namespace Dustweave;

/// <summary>Client downloads/backups, not the publisher's ten-version delta baseline window.</summary>
public static class UpdateCacheRetention
{
    public const int MaximumVersions = 2, MaximumDays = 7;
    private sealed record VersionCache(string Path, DailyVersion Version, DailyStorageItem[] Files);
    private sealed record AttemptState(bool Protected, bool Terminal = false, string? Target = null, bool Completed = false, string[]? Payloads = null);
    private sealed record Attempt(string Path, DailyStorageItem[] Files, AttemptState State, DateTime Finished);
    private sealed record VersionState(VersionCache Cache, Attempt[] Attempts, bool UnknownRecovery);
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
    private static AttemptState State(string attempt)
    {
        try
        {
            string jobPath = Path.Combine(attempt, "job.json"), backup = Path.Combine(attempt, "backup");
            var job = Read<DailyUpdateJob>(jobPath);
            // Copying the helper can fail before a job is created. No install can run without it.
            if (job == null) return new(Exists(jobPath) || Exists(backup));
            string versionPath = Path.GetDirectoryName(Path.GetDirectoryName(attempt))!;
            if (job.Release == null || job.Asset == null || !Path.IsPathFullyQualified(job.Target) ||
                !Path.GetFileName(job.Target).Equals("Dustweave.exe", StringComparison.OrdinalIgnoreCase) ||
                !Same(job.Directory, versionPath) || Path.GetFileName(versionPath) != job.Release.Version + "-" + job.Asset.Flavor ||
                job.Nonce != Path.GetFileName(attempt) || job.Nonce.Length != 32 || !job.Nonce.All(Uri.IsHexDigit)) return new(true);
            DailyCacheStorage.NoLinks(job.Target);
            string target = Path.GetDirectoryName(job.Target)!, markerPath = Path.Combine(target, DailyUpdateTransaction.MarkerName);
            if (Exists(markerPath))
            {
                var marker = Read<DailyUpdateMarker>(markerPath);
                // Even a terminal journal is still read by the bootstrap until its marker is cleared.
                if (marker == null || Same(marker.JobPath, jobPath)) return new(true);
            }
            var journal = Read<DailyUpdateJournal>(Path.Combine(backup, "transaction.json"));
            // Only the pre-transaction handoff still depends on the original host.
            // A completed transaction with no marker must not be pinned forever
            // when Windows reuses its historical PID for an inaccessible process.
            if (journal == null) return new(Exists(backup) || ParentRunning(job));
            bool inventoryStage = (job.Repair || job.InventoryUpgrade) && Same(journal.Staging, Path.Combine(versionPath, "repair-staging"));
            if (!Same(journal.Target, target) || !Same(journal.Backup, backup) || !(Within(journal.Staging, attempt) || inventoryStage)) return new(true);
            bool terminal = journal.State is "completed" or "rolled_back";
            // Only exact payload names from this transaction can be discarded early.
            var payloads = new[] { job.Asset.FileName, job.DeltaFileName }
                .Where(n => !string.IsNullOrEmpty(n) && n == Path.GetFileName(n) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .SelectMany(n => new[] { n!, n + ".partial" }).ToArray();
            return new(!terminal, terminal, Path.GetFullPath(target), journal.State == "completed", payloads);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or
            System.ComponentModel.Win32Exception or System.Security.SecurityException or NullReferenceException)
        { return new(true); }
    }
    public static bool CanClean(DailyCacheUnit unit) => (unit.UpdateAttempts ?? []).All(a => !State(a).Protected);
    internal static IDisposable? AcquireDownload(DailyCacheUnit unit)
    {
        if (unit.UpdateDownloadLock is not { } path || !Exists(path)) return null;
        // Whole-version removal already locks this file with the rest of the unit.
        if (unit.Files.Any(f => Same(f.Path, path))) return null;
        DailyCacheStorage.NoLinks(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }

    public static DailyCacheUnit[] Plan(string updates, DailyStorageItem[] files, DateTime utcNow, CancellationToken cancel = default)
        => Plan(updates,files,utcNow,cancel,MaximumDays);
    internal static DailyCacheUnit[] Plan(string updates, DailyStorageItem[] files, DateTime utcNow, CancellationToken cancel, int days)
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
        DateTime cutoff = utcNow.AddDays(-Math.Clamp(days,2,MaximumDays));
        var states = new List<VersionState>();
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
                    var attemptFiles = g.ToArray();
                    DateTime finished = attemptFiles.FirstOrDefault(f => Same(f.Path, Path.Combine(path, "backup", "transaction.json")))?.Written ?? DateTime.MinValue;
                    return new Attempt(path, attemptFiles, state with { Protected = state.Protected || unknown }, finished);
                }).ToArray();
            bool unknownRecovery = version.Files.Any(f => Path.GetFileName(f.Path) is "job.json" or "transaction.json" && !Within(f.Path, attemptsRoot));
            states.Add(new(version, attempts, unknownRecovery));
        }
        // Backups belong to an installation, not a release cache. A prepared newer
        // download must not evict its last recovery copy. Prefer a confirmed upgrade;
        // a failed retry usually only backs up the version already restored on disk.
        var retained = states.SelectMany(v => v.Attempts)
            .Where(a => !a.State.Protected && a.State.Terminal && a.Finished > cutoff &&
                a.Files.Any(f => Within(f.Path, Path.Combine(a.Path, "backup")) && !Same(f.Path, Path.Combine(a.Path, "backup", "transaction.json"))))
            .GroupBy(a => a.State.Target!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.State.Completed).ThenByDescending(a => a.Finished)
                .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase).First().Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var units = new List<DailyCacheUnit>();
        foreach (var state in states)
        {
            cancel.ThrowIfCancellationRequested();
            var version = state.Cache; var attempts = state.Attempts; bool unknownRecovery = state.UnknownRecovery;
            void Add(string path, DailyStorageItem[] discard, IEnumerable<Attempt> dependencies)
            {
                if (discard.Length > 0) units.Add(new(path, discard)
                { UpdateAttempts = dependencies.Select(a => a.Path).ToArray(), UpdateDownloadLock = Path.Combine(version.Path, "download.lock") });
            }
            bool expired = !recent.Contains(version.Version) || version.Files.Max(f => f.Written) <= cutoff;
            if (expired && !unknownRecovery && attempts.All(a => !a.State.Protected && !retained.Contains(a.Path)))
            {
                Add(version.Path, version.Files, attempts);
                continue;
            }
            // Completed installs no longer read ZIPs. Failed-only downloads remain
            // retryable; any pending or unknown recovery protects shared payloads.
            if (!unknownRecovery && attempts.All(a => !a.State.Protected))
            {
                var names = attempts.Where(a => a.State.Completed).SelectMany(a => a.State.Payloads ?? [])
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in version.Files.Where(f => Same(Path.GetDirectoryName(f.Path)!, version.Path) && names.Contains(Path.GetFileName(f.Path))))
                    Add(file.Path, [file], attempts);
            }
            foreach (var attempt in attempts.Where(a => !a.State.Protected))
            {
                if (!retained.Contains(attempt.Path) && (expired || !attempt.State.Terminal || attempt.Finished <= cutoff))
                { Add(attempt.Path, attempt.Files, [attempt]); continue; }
                if (!retained.Contains(attempt.Path))
                {
                    // Keep small recent receipts/errors for diagnosis. Only discard
                    // the duplicate program; a receipt-only attempt cannot claim the
                    // retained-backup slot on a later pass.
                    string backup = Path.Combine(attempt.Path, "backup");
                    foreach (var file in attempt.Files.Where(f => Within(f.Path, backup) && !Same(f.Path, Path.Combine(backup, "transaction.json"))))
                        Add(file.Path, [file], [attempt]);
                }
                // A confirmed start/rollback no longer needs extraction and helper copies; keep the latest backup/receipt.
                foreach (string name in new[] { "staging", "staging-full", "Dustweave.Updater.exe" })
                {
                    string path = Path.Combine(attempt.Path, name);
                    var discard = attempt.Files.Where(f => Same(f.Path, path) || Within(f.Path, path)).ToArray();
                    Add(path, discard, [attempt]);
                }
            }
        }
        return units.ToArray();
    }
}

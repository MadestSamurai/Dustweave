using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
namespace Dustweave;

public static class DailyUpdateEngine
{
    private static string Attempt(DailyUpdateJob job) => Path.Combine(job.Directory, "attempts", job.Nonce);
    private static string MutexName(string target) => @"Local\Dustweave-Updater-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(target).ToUpperInvariant())));
    private static bool Acquire(Mutex mutex) { try { return mutex.WaitOne(0); } catch (AbandonedMutexException) { return true; } }
    public static void ValidateJob(DailyUpdateJob job, string path)
    {
        DailyUpdateHost.RequireHost();
        if (job.Nonce.Length != 32 || !job.Nonce.All(Uri.IsHexDigit) || !Path.IsPathFullyQualified(job.Target) ||
            !Path.GetFileName(job.Target).Equals("Dustweave.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        string expected = Path.Combine(DailyUpdateHost.DataRoot, "updates", job.Release.Version + "-" + job.Asset.Flavor);
        if (!Path.GetFullPath(job.Directory).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(Attempt(job), "job.json")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        DailyUpdates.EnsureNoLinks(job.Directory); DailyUpdates.EnsureNoLinks(job.Target);
        var signed = DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json")) ?? throw new InvalidDataException("updates.signature_failed");
        var feed = DailyUpdateSignatures.Verify(signed, DailyUpdateTrust.Production());
        var found = feed.Releases.SingleOrDefault(r => r.Version == job.Release.Version);
        var release = found == null ? null : DailyUpdates.WithBaselines(feed,found);
        var asset = release?.Assets.SingleOrDefault(a => a.Flavor == job.Asset.Flavor);
        if (asset != job.Asset || release?.Layout != job.Release.Layout || release?.RuntimeVersion != job.Release.RuntimeVersion ||
            release?.InstallerProtocol != job.Release.InstallerProtocol) throw new InvalidDataException("updates.signature_failed");
        if (job.DeltaFileName != null && !((release?.Deltas ?? []).Any(d => d.FileName == job.DeltaFileName && d.Flavor == job.Asset.Flavor)))
            throw new InvalidDataException("updates.signature_failed");
        if (job.InventoryUpgrade)
        {
            // Acknowledgement/recovery see the target descriptor. Source identity
            // is checked separately, immediately before the first target write.
            DailyUpdateInventory.ValidateJob(job, feed);
            _ = DailyUpdateInventory.ReadSource(job, feed);
            _ = DailyUpdateRepair.ReadVerified(job.Directory, release!, asset!);
        }
        if (job.Release.Layout == DailyUpdates.BundledLayout && (job.InventoryUpgrade || job.DeltaFileName != null))
            throw new InvalidDataException("updates.invalid_package");
        if (job.Repair && job.Release.Layout != DailyUpdates.BundledLayout)
        {
            if (job.DeltaFileName != null) throw new InvalidDataException("updates.invalid_package");
            _ = DailyUpdateRepair.ReadVerified(job.Directory, release!, asset!);
        }
    }
    public static void Acknowledge(string nonce, string productVersion)
    {
        string target = Environment.ProcessPath!;
        string markerPath = Path.Combine(Path.GetDirectoryName(target)!, DailyUpdateTransaction.MarkerName);
        var marker = DailyUpdateJson.TryRead<DailyUpdateMarker>(markerPath) ?? throw new IOException("updates.recovery_required");
        var job = DailyUpdateJson.TryRead<DailyUpdateJob>(marker.JobPath) ?? throw new IOException("updates.recovery_required");
        ValidateJob(job, marker.JobPath);
        if (nonce != job.Nonce || job.Release.Version != productVersion ||
            !Path.GetFullPath(job.Target).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        // Commit is durable before the acknowledgement. A lost helper/ack must not undo a healthy start.
        var journal = DailyUpdateTransaction.Read(Path.Combine(Attempt(job), "backup"), Path.GetDirectoryName(target)!);
        if (journal.State != "awaiting_start") throw new IOException("updates.recovery_required");
        DailyUpdateTransaction.Save(journal with { State = "completed" });
        DailyUpdateJson.Write(Path.Combine(Attempt(job), "ready.json"), new Ready(Environment.ProcessId, productVersion, nonce));
    }
    public static int Run(string jobPath, bool recovery = false)
    {
        DailyUpdateJob? job = null; DailyUpdateJournal? journal = null;
        bool parentExited = false, ownInstance = false, ownUpdate = false, mayRestart = false, validated = false;
        Mutex? updateMutex = null, instance = null;
        Process? next = null;
        IDisposable? storage = null;
        string? markerPath = null;
        string phase = "validate", previousVersion = "";
        void Phase(string value)
        {
            phase = value;
            try { if (validated && job != null) DailyUpdateJson.Write(Path.Combine(Attempt(job), "updater-state.json"),
                new { phase, atUtc = DateTimeOffset.UtcNow, target = job.Target, from = previousVersion, to = job.Release.Version,
                    helperPid = Environment.ProcessId, targetPid = next?.Id }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        void Finish(string state, Exception? error = null)
        {
            var installed = !validated || job == null ? null : DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(Path.GetDirectoryName(job.Target)!, "update-package.json"));
            var result = new Result(state, job?.Release.Version ?? "", DateTimeOffset.UtcNow, error?.ToString(), phase,
                job?.Target, previousVersion, installed?.Version, job?.Nonce);
            DailyUpdateJson.Write(Path.Combine(DailyUpdateHost.DataRoot, "update-result.json"), result);
            try { if (validated && job != null) DailyUpdateJson.Write(Path.Combine(Attempt(job), "update-result.json"), result); }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
        }
        try
        {
            storage = DailyUpdateHost.AcquireStorage(DailyUpdateHost.DataRoot);
            job = DailyUpdateJson.TryRead<DailyUpdateJob>(jobPath) ?? throw new InvalidDataException("updates.invalid_package");
            ValidateJob(job, jobPath);
            validated = true;
            previousVersion = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(Path.GetDirectoryName(job.Target)!, "update-package.json"))?.Version ?? "";
            Phase("wait-parent");
            string target = Path.GetDirectoryName(job.Target)!, attempt = Attempt(job);
            markerPath = Path.Combine(target, DailyUpdateTransaction.MarkerName);
            updateMutex = new Mutex(false, MutexName(job.Target));
            // The launcher may still be releasing its mutex after starting this helper.
            for (int i = 0; i < 20 && !(ownUpdate = Acquire(updateMutex)); i++) Thread.Sleep(100);
            if (!ownUpdate) return 2;
            instance = new Mutex(false, DailyApplication.InstanceMutex);
            if (!recovery)
            {
                string staging = Path.Combine(attempt, "staging");

                try
                {
                    using var parent = Process.GetProcessById(job.ParentPid);
                    if (parent.StartTime.ToUniversalTime().Ticks == job.ParentStartTicks && !parent.WaitForExit(60000))
                        throw new IOException("updates.app_busy");
                }
                catch (ArgumentException) { }
                parentExited = true;
                if (!(ownInstance = Acquire(instance))) throw new IOException("updates.app_busy");
                mayRestart = true;
                using (var activity = DailyUpdateHost.AcquireControl(DailyUpdateHost.DataRoot))
                {
                    if (DailyUpdateHost.HasIsolatedWindows()) throw new IOException("updates.isolated_open");
                    if (File.Exists(job.Target) ? DailyUpdateTransaction.Hash(job.Target) != job.OriginalSha256 : !job.Repair || job.OriginalSha256 != "")
                        throw new InvalidDataException("updates.target_changed");
                    if (!job.Repair)
                    {
                        var installed = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(target, "update-package.json")) ?? throw new InvalidDataException("updates.flavor_missing");
                        if (installed.Flavor != job.Asset.Flavor || DailyUpdates.VersionOf(installed.Version) >= DailyUpdates.VersionOf(job.Release.Version))
                            throw new InvalidDataException("updates.target_changed");
                        if (job.InventoryUpgrade)
                        {
                            var signed = DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json")) ?? throw new InvalidDataException("updates.signature_failed");
                            DailyUpdateInventory.ValidateSource(job, DailyUpdateSignatures.Verify(signed, DailyUpdateTrust.Production()), installed);
                        }
                        else if (job.Release.Layout == DailyUpdates.BundledLayout ? installed.Layout != DailyUpdates.BundledLayout :
                            job.Release.Layout >= 2 && (installed.Layout != job.Release.Layout || installed.RuntimeVersion != job.Release.RuntimeVersion || job.DeltaFileName == null)) throw new InvalidDataException("updates.manual_layout");
                    }
                    Phase("prepare-files");
                    string[] names = ExtractPayload(job, target, ref staging, out var retained);
                    journal = DailyUpdateTransaction.Prepare(staging, target, Path.Combine(attempt, "backup"), names, retained, removeObsolete: !job.Repair,
                        installRecoveryEntryFirst: job.InventoryUpgrade || job.Repair && job.Release.Layout != DailyUpdates.BundledLayout,
                        previousFiles: job.InventoryUpgrade ? DailyUpdateInventory.ReadSource(job,
                            DailyUpdateSignatures.Verify(DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json"))!, DailyUpdateTrust.Production())).Files.Select(f => f.Name).ToArray() : null);
                    var marker = new DailyUpdateMarker(Path.GetFullPath(jobPath), job.Nonce, DailyUpdateTransaction.Hash(Environment.ProcessPath!));
                    DailyUpdateJson.Write(markerPath, marker);
                    Phase("replace-files");
                    journal = DailyUpdateTransaction.Apply(journal);
                }
                instance.ReleaseMutex(); ownInstance = false; instance.Dispose(); instance = null;
                Phase("start-updated");
                next = Start(job.Target, "--updated", job.Nonce);
                Phase("await-startup-acknowledgement");
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromMinutes(2))
                {
                    var ready = DailyUpdateJson.TryRead<Ready>(Path.Combine(attempt, "ready.json"));
                    var persisted = DailyUpdateTransaction.Read(journal.Backup, target);
                    if (persisted.State == "completed" && (ready == null || ready.Nonce == job.Nonce))
                    {
                        File.Delete(markerPath);
                        Finish("completed");
                        return 0;
                    }
                    if (next.HasExited) break;
                    Thread.Sleep(250);
                }
                throw new IOException("updates.new_start_failed");
            }
            // Let the bootstrap process exit before replacing its executable.
            for (int i = 0; i < 100 && !(ownInstance = Acquire(instance)); i++) Thread.Sleep(100);
            if (!ownInstance) throw new IOException("updates.app_busy");
            using (var activity = DailyUpdateHost.AcquireControl(DailyUpdateHost.DataRoot))
            {
                if (DailyUpdateHost.HasIsolatedWindows()) throw new IOException("updates.isolated_open");
                journal = DailyUpdateTransaction.Read(Path.Combine(attempt, "backup"), target);
                if (journal.State != "completed") journal = DailyUpdateTransaction.Restore(journal);
                File.Delete(markerPath);
            }
            Phase("recover-interrupted");
            Finish(journal.State == "completed" ? "completed" : "rolled_back");
            instance.ReleaseMutex(); ownInstance = false; instance.Dispose(); instance = null;
            StartAfterRecovery(job.Target, journal.State == "rolled_back" ? job.Release.Version : null);
            return 0;
        }
        catch (Exception error)
        {
            // Preserve the failed new process's startup evidence before the old
            // version overwrites startup-state/error during the rollback restart.
            if (validated && job != null) PreserveStartupEvidence(DailyUpdateHost.DataRoot, Attempt(job));
            string state = "failed";
            if (job != null && journal != null)
            {
                try
                {
                    var persisted = DailyUpdateTransaction.Read(journal.Backup, journal.Target);
                    if (persisted.State == "completed") { if (markerPath != null) File.Delete(markerPath); Finish("completed"); return 0; }
                    if (next != null && !next.HasExited) { next.Kill(); if (!next.WaitForExit(10000)) throw new IOException("updates.app_busy"); }
                    if (!ownInstance) { instance ??= new Mutex(false, DailyApplication.InstanceMutex); if (!(ownInstance = Acquire(instance))) throw new IOException("updates.app_busy"); }
                    using var activity = DailyUpdateHost.AcquireControl(DailyUpdateHost.DataRoot);
                    DailyUpdateTransaction.Restore(persisted);
                    if (markerPath != null) File.Delete(markerPath);
                    state = "rolled_back";
                }
                catch (Exception rollback) { error = new AggregateException(error, rollback); state = "recovery_required"; }
            }
            Finish(state, error);
            if (ownInstance) { instance!.ReleaseMutex(); ownInstance = false; }
            instance?.Dispose(); instance = null;
            if (job != null && parentExited && mayRestart && state != "recovery_required")
            {
                try { StartAfterRecovery(job.Target, job.Release.Version); } catch { }
            }
            return 1;
        }
        finally
        {
            next?.Dispose();
            if (ownInstance) instance!.ReleaseMutex(); instance?.Dispose();
            if (ownUpdate) updateMutex!.ReleaseMutex(); updateMutex?.Dispose();
            storage?.Dispose();
        }
    }
    internal static void PreserveStartupEvidence(string data, string attempt)
    {
        foreach (string name in new[] { "startup-state.json", "startup-error.json", "launcher-error.txt", "bootstrap-error.txt" })
        {
            try
            {
                string source = Path.Combine(data, name);
                if (name is "launcher-error.txt" or "bootstrap-error.txt")
                    source = Path.Combine(data, "startup", name);
                if (!File.Exists(source) || new FileInfo(source).Length > 1024 * 1024) continue;
                DailyUpdates.EnsureNoLinks(source);
                DailyUpdateTransaction.CopyDurable(source, Path.Combine(attempt, "startup", name));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Keep the original updater failure. */ }
        }
    }
    private static string[] ExtractPayload(DailyUpdateJob job, string target, ref string staging, out Dictionary<string, string>? retained)
    {
        retained = null;
        var signed = DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json"))!;
        var verified = new DailyVerifiedUpdate(DailyUpdateSignatures.Verify(signed, DailyUpdateTrust.Production()), signed, "");
        var release = DailyUpdates.WithBaselines(verified.Feed, verified.Feed.Releases.Single(r => r.Version == job.Release.Version));
        var asset = release.Assets.Single(a => a.Flavor == job.Asset.Flavor);
        if (job.Release.Layout != DailyUpdates.BundledLayout && (job.Repair || job.InventoryUpgrade))
        {
            var repair = DailyUpdateRepair.ReadVerified(job.Directory, release, asset);
            staging = DailyUpdateRepair.Staging(job.Directory);
            var names = DailyUpdateRepair.VerifyStaging(repair, target, staging, out var keep);
            retained = keep;
            return names;
        }
        var delta = release.Deltas?.SingleOrDefault(d => d.FileName == job.DeltaFileName);
        if (delta != null)
        {
            try
            {
                string path = Path.Combine(job.Directory, delta.FileName);
                var manifest = DailyUpdateDeltaPackage.Read(path, release, asset, delta);
                var names = DailyUpdateDeltaPackage.Extract(path, target, staging, release, asset, delta, copyUnchanged: false);
                retained = manifest.Files.Where(f => f.Mode == "copy").ToDictionary(f => f.Name, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
                return names;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                if (release.Layout >= 2) throw new InvalidDataException("updates.manual_delta", error);
                // The base can change after the download preflight. Fetch the full package
                // before any target writes; the old app is restarted if this also fails.
                DailyUpdateJson.Write(Path.Combine(Attempt(job), "delta-fallback.json"), new { error = error.ToString() });
                staging += "-full";
                using var client = DailyUpdates.Client();
                DailyUpdateTransport.Production().DownloadAsync(client, verified, release.Version, asset.Flavor,
                    DailyUpdateHost.DataRoot, null, default, allowDelta: false).GetAwaiter().GetResult();
            }
        }
        return DailyUpdates.Extract(Path.Combine(job.Directory, "package.zip"), staging, release, asset);
    }
    private static Process Start(string path, params string[] args)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("updates.start_failed");
    }
    private static Process StartAfterRecovery(string path, string? failedVersion)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! };
        info.ArgumentList.Add("--desktop-session");
        if (failedVersion != null) info.Environment["DUSTWEAVE_UPDATE_ROLLBACK"] = failedVersion;
        return Process.Start(info) ?? throw new IOException("updates.start_failed");
    }
    public sealed record Ready(int ProcessId, string Version, string Nonce);
    public sealed record Result(string State, string Version, DateTimeOffset AtUtc, string? Error = null,
        string? Phase = null, string? Target = null, string? PreviousVersion = null, string? InstalledVersion = null, string? Attempt = null);
}

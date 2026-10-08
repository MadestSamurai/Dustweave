using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
namespace Dustweave.Desktop;

internal static class DailyUpdateInstaller
{
    private static string Attempt(DailyUpdateJob job) => Path.Combine(job.Directory, "attempts", job.Nonce);
    private static string MutexName(string target) => @"Local\Dustweave-Updater-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(target).ToUpperInvariant())));
    private static bool Acquire(Mutex mutex) { try { return mutex.WaitOne(0); } catch (AbandonedMutexException) { return true; } }
    internal static void ValidateJob(DailyUpdateJob job, string path)
    {
        DailySandbox.RequireHost();
        if (job.Nonce.Length != 32 || !job.Nonce.All(Uri.IsHexDigit) || !Path.IsPathFullyQualified(job.Target) ||
            !Path.GetFileName(job.Target).Equals("Dustweave.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        string expected = Path.Combine(DailyIdentity.DataRoot, "updates", job.Release.Version + "-" + job.Asset.Flavor);
        if (!Path.GetFullPath(job.Directory).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(Attempt(job), "job.json")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        DailyUpdates.EnsureNoLinks(job.Directory); DailyUpdates.EnsureNoLinks(job.Target);
        var signed = DailyJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json")) ?? throw new InvalidDataException("updates.signature_failed");
        var feed = DailyUpdateSignatures.Verify(signed, DailyUpdateTrust.Production());
        var release = feed.Releases.SingleOrDefault(r => r.Version == job.Release.Version);
        var asset = release?.Assets.SingleOrDefault(a => a.Flavor == job.Asset.Flavor);
        if (asset != job.Asset || release?.Layout != job.Release.Layout) throw new InvalidDataException("updates.signature_failed");
        if (job.DeltaFileName != null && !((release?.Deltas ?? []).Any(d => d.FileName == job.DeltaFileName && d.Flavor == job.Asset.Flavor)))
            throw new InvalidDataException("updates.signature_failed");
    }
    // Called before normal UI/utility loading. No network and no game commands.
    internal static bool RecoverBeforeStartup(string[] args)
    {
        string target = Environment.ProcessPath!;
        if (!Path.GetFileName(target).Equals("Dustweave.exe", StringComparison.OrdinalIgnoreCase)) return false;
        string markerPath = Path.Combine(Path.GetDirectoryName(target)!, DailyUpdateTransaction.MarkerName);
        if (!File.Exists(markerPath)) return false;
        try
        {
            var marker = DailyJson.TryRead<DailyUpdateMarker>(markerPath) ?? throw new IOException("updates.recovery_required");
            var job = DailyJson.TryRead<DailyUpdateJob>(marker.JobPath) ?? throw new IOException("updates.recovery_required");
            ValidateJob(job, marker.JobPath);
            if (!Path.GetFullPath(job.Target).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) || marker.Nonce != job.Nonce)
                throw new InvalidDataException("updates.invalid_package");
            var journal = DailyUpdateTransaction.Read(Path.Combine(Attempt(job), "backup"), Path.GetDirectoryName(target)!);
            if (journal.State is "completed" or "rolled_back") { File.Delete(markerPath); return false; }
            if (args.Length == 2 && args[0] == "--updated" && args[1] == job.Nonce && journal.State == "awaiting_start")
                return false;
            using var mutex = new Mutex(false, MutexName(target));
            if (!Acquire(mutex)) return true; // The existing updater owns recovery/restart.
            try
            {
                string helper = Path.Combine(Attempt(job), "Dustweave.Updater.exe");
                if (DailyUpdateTransaction.Hash(helper) != marker.HelperSha256) throw new InvalidDataException("updates.hash_failed");
                Start(helper, "--recover-update", marker.JobPath);
            }
            finally { mutex.ReleaseMutex(); }
            return true;
        }
        catch (Exception e)
        {
            DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "update-result.json"), new Result("recovery_required", "", DateTimeOffset.UtcNow, e.ToString()));
            DailyLanguage.Current.Initialize(DailyIdentity.DataRoot);
            System.Windows.MessageBox.Show(DailyLanguage.Current.Get("updates.recovery_required"), "Dustweave");
            return true;
        }
    }
    internal static void Acknowledge(string nonce)
    {
        string target = Environment.ProcessPath!;
        string markerPath = Path.Combine(Path.GetDirectoryName(target)!, DailyUpdateTransaction.MarkerName);
        var marker = DailyJson.TryRead<DailyUpdateMarker>(markerPath) ?? throw new IOException("updates.recovery_required");
        var job = DailyJson.TryRead<DailyUpdateJob>(marker.JobPath) ?? throw new IOException("updates.recovery_required");
        ValidateJob(job, marker.JobPath);
        if (nonce != job.Nonce || job.Release.Version != DailyProductVersion.Current) throw new InvalidDataException("updates.invalid_package");
        // Commit is durable before the acknowledgement. A lost helper/ack must not undo a healthy start.
        var journal = DailyUpdateTransaction.Read(Path.Combine(Attempt(job), "backup"), Path.GetDirectoryName(target)!);
        if (journal.State != "awaiting_start") throw new IOException("updates.recovery_required");
        DailyUpdateTransaction.Save(journal with { State = "completed" });
        DailyJson.Write(Path.Combine(Attempt(job), "ready.json"), new Ready(Environment.ProcessId, DailyProductVersion.Current, nonce));
    }
    internal static int Run(string jobPath, bool recovery = false)
    {
        DailyUpdateJob? job = null; DailyUpdateJournal? journal = null;
        bool parentExited = false, ownInstance = false, ownUpdate = false, mayRestart = false;
        Mutex? updateMutex = null, instance = null;
        Process? next = null;
        string? markerPath = null;
        try
        {
            job = DailyJson.TryRead<DailyUpdateJob>(jobPath) ?? throw new InvalidDataException("updates.invalid_package");
            ValidateJob(job, jobPath);
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
                using (var activity = DailyToolControl.Acquire(DailyIdentity.DataRoot))
                {
                    if (DailySandbox.HasIsolatedWindows()) throw new IOException("updates.isolated_open");
                    if (DailyUpdateTransaction.Hash(job.Target) != job.OriginalSha256) throw new InvalidDataException("updates.target_changed");
                    var installed = DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(target, "update-package.json")) ?? throw new InvalidDataException("updates.flavor_missing");
                    if (installed.Flavor != job.Asset.Flavor || DailyUpdates.VersionOf(installed.Version) >= DailyUpdates.VersionOf(job.Release.Version))
                        throw new InvalidDataException("updates.target_changed");
                    string[] names = ExtractPayload(job, target, ref staging);
                    journal = DailyUpdateTransaction.Prepare(staging, target, Path.Combine(attempt, "backup"), names);
                    var marker = new DailyUpdateMarker(Path.GetFullPath(jobPath), job.Nonce, DailyUpdateTransaction.Hash(Environment.ProcessPath!));
                    DailyJson.Write(markerPath, marker);
                    journal = DailyUpdateTransaction.Apply(journal);
                }
                instance.ReleaseMutex(); ownInstance = false; instance.Dispose(); instance = null;
                next = Start(job.Target, "--updated", job.Nonce);
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromMinutes(2))
                {
                    var ready = DailyJson.TryRead<Ready>(Path.Combine(attempt, "ready.json"));
                    var persisted = DailyUpdateTransaction.Read(journal.Backup, target);
                    if (persisted.State == "completed" && (ready == null || ready.Nonce == job.Nonce))
                    {
                        File.Delete(markerPath);
                        DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "update-result.json"), new Result("completed", job.Release.Version, DateTimeOffset.UtcNow));
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
            using (var activity = DailyToolControl.Acquire(DailyIdentity.DataRoot))
            {
                if (DailySandbox.HasIsolatedWindows()) throw new IOException("updates.isolated_open");
                journal = DailyUpdateTransaction.Read(Path.Combine(attempt, "backup"), target);
                if (journal.State != "completed") journal = DailyUpdateTransaction.Restore(journal);
                File.Delete(markerPath);
            }
            DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "update-result.json"), new Result(journal.State == "completed" ? "completed" : "rolled_back", job.Release.Version, DateTimeOffset.UtcNow));
            instance.ReleaseMutex(); ownInstance = false; instance.Dispose(); instance = null;
            Start(job.Target, DailyDesktopLaunch.ChildSwitch);
            return 0;
        }
        catch (Exception error)
        {
            string state = "failed";
            if (job != null && journal != null)
            {
                try
                {
                    var persisted = DailyUpdateTransaction.Read(journal.Backup, journal.Target);
                    if (persisted.State == "completed") { if (markerPath != null) File.Delete(markerPath); return 0; }
                    if (next != null && !next.HasExited) { next.Kill(); if (!next.WaitForExit(10000)) throw new IOException("updates.app_busy"); }
                    if (!ownInstance) { instance ??= new Mutex(false, DailyApplication.InstanceMutex); if (!(ownInstance = Acquire(instance))) throw new IOException("updates.app_busy"); }
                    using var activity = DailyToolControl.Acquire(DailyIdentity.DataRoot);
                    DailyUpdateTransaction.Restore(persisted);
                    if (markerPath != null) File.Delete(markerPath);
                    state = "rolled_back";
                }
                catch (Exception rollback) { error = new AggregateException(error, rollback); state = "recovery_required"; }
            }
            DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "update-result.json"), new Result(state, job?.Release.Version ?? "", DateTimeOffset.UtcNow, error.ToString()));
            if (ownInstance) { instance!.ReleaseMutex(); ownInstance = false; }
            instance?.Dispose(); instance = null;
            if (job != null && parentExited && mayRestart && state != "recovery_required")
            {
                try { Start(job.Target, DailyDesktopLaunch.ChildSwitch); } catch { }
            }
            return 1;
        }
        finally
        {
            next?.Dispose();
            if (ownInstance) instance!.ReleaseMutex(); instance?.Dispose();
            if (ownUpdate) updateMutex!.ReleaseMutex(); updateMutex?.Dispose();
        }
    }
    private static string[] ExtractPayload(DailyUpdateJob job, string target, ref string staging)
    {
        var signed = DailyJson.TryRead<DailySignedUpdate>(Path.Combine(job.Directory, "signed-feed.json"))!;
        var verified = new DailyVerifiedUpdate(DailyUpdateSignatures.Verify(signed, DailyUpdateTrust.Production()), signed, "");
        var release = verified.Feed.Releases.Single(r => r.Version == job.Release.Version);
        var asset = release.Assets.Single(a => a.Flavor == job.Asset.Flavor);
        var delta = release.Deltas?.SingleOrDefault(d => d.FileName == job.DeltaFileName);
        if (delta != null)
        {
            try { return DailyUpdateDeltaPackage.Extract(Path.Combine(job.Directory, delta.FileName), target, staging, release, asset, delta); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // The base can change after the download preflight. Fetch the full package
                // before any target writes; the old app is restarted if this also fails.
                DailyJson.Write(Path.Combine(Attempt(job), "delta-fallback.json"), new { error = error.ToString() });
                staging += "-full";
                using var client = DailyUpdates.Client();
                DailyUpdateTransport.Production().DownloadAsync(client, verified, release.Version, asset.Flavor,
                    DailyIdentity.DataRoot, null, default, allowDelta: false).GetAwaiter().GetResult();
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
    internal sealed record Ready(int ProcessId, string Version, string Nonce);
    internal sealed record Result(string State, string Version, DateTimeOffset AtUtc, string? Error = null);
}

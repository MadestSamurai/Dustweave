using System.Collections.Concurrent;
using System.Diagnostics;

namespace Dustweave;

public sealed record DailyResourceCopy(string Sandbox, string Relative, long Bytes, long StoredBytes, DateTime Written);
public sealed record DailyResourcePlan(DailyResourceCopy[] Copies, int Examined, int Deferred)
{
    public long Bytes => Copies.Sum(c => c.StoredBytes);
}
public sealed record DailyResourceResult(int Files, long Bytes, int Deferred, bool MoreWork = false);
public sealed record DailyResourceProgress(int Examined, int Matched, long Bytes);

public sealed partial class DailyStorageMaintenance
{
    internal string ResourceSource { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Gamfs", "BrownDust II", "SoundData");
    internal Func<bool> ResourceHostIdle { get; init; } = GameIdle;
    internal Func<string, bool>? ResourceView { get; init; }
    internal long ResourceBatchBytes { get; init; } = 512L * 1024 * 1024;
    static readonly ConcurrentDictionary<string, int> ResourceOffsets = new(StringComparer.OrdinalIgnoreCase);
    static bool GameIdle()
    {
        var processes = Process.GetProcessesByName("BrownDust II");
        try { return processes.Length == 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    bool ResourceReady(Box box)
    {
        if (!CanClean(box) || !ResourceHostIdle()) return false;
        string boxRoot = Path.Combine(box.Directory, "root");
        if (File.Exists(Path.Combine(boxRoot, "FilePaths.dat")) || File.Exists(Path.Combine(boxRoot, "Snapshots.ini"))
            || Directory.EnumerateDirectories(boxRoot, "snapshot-*", SearchOption.TopDirectoryOnly).Any()) return false;
        if (ResourceView != null) return ResourceView(box.Name);
        if (boxIdle != BoxIdle) return false; // Synthetic callers must opt into their fixture view explicitly.
        var installation = DailySandbox.Installation();
        return installation != null && DailySandboxAudio.NormalView(ResourceSource,
            setting => Accounts.SandboxConfiguration.Read(installation, box.Name, setting, true));
    }
    public Task<DailyResourcePlan> CheckResourceCopiesAsync(CancellationToken token = default, IProgress<DailyResourceProgress>? progress = null)
        => Task.Run(async () =>
        {
            var copies = new List<DailyResourceCopy>(); int examined = 0, deferred = 0; long bytes = 0;
            await DailySandbox.ConfigurationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                foreach (string directory in InstanceDirectories(out _))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (ReadBox(directory) is not { } box || !ResourceReady(box)) { deferred++; continue; }
                        string overlay = Path.Combine(box.Directory, "root", DailySandboxAudio.RelativeRoot);
                        foreach (var file in DailySandboxAudio.Candidates(overlay, token))
                        {
                            token.ThrowIfCancellationRequested(); examined++;
                            try
                            {
                                if (DailySandboxAudio.Compare(overlay, ResourceSource, file, false, () => ResourceReady(box), token) is long stored)
                                { copies.Add(new(directory, file.Relative, file.Bytes, stored, file.Written)); bytes += stored; }
                                else deferred++;
                            }
                            catch (Exception e) when (Expected(e)) { deferred++; }
                            progress?.Report(new(examined, copies.Count, bytes));
                        }
                    }
                    catch (Exception e) when (Expected(e)) { deferred++; }
                }
            }
            finally { DailySandbox.ConfigurationGate.Release(); }
            return new DailyResourcePlan(copies.ToArray(), examined, deferred);
        }, token);

    /// <summary>The preview is only a selection. Re-open and hash everything under leases before deleting.</summary>
    public Task<DailyResourceResult> OptimizeResourceCopiesAsync(DailyResourcePlan plan, CancellationToken token = default)
        => Task.Run(() => OptimizeResourceCopiesCore(plan, false, token), token);

    // This pass is separate from expired-file cleanup: retained settings never become disposable resources.
    public Task<DailyResourceResult> MaintainResourceCopiesAsync(CancellationToken token = default)
        => Task.Run(() => OptimizeResourceCopiesCore(null, true, token), token);

    async Task<DailyResourceResult> OptimizeResourceCopiesCore(DailyResourcePlan? plan, bool bounded, CancellationToken token)
    {
        int files = 0, deferred = 0, visited = 0; long bytes = 0, examinedBytes = 0; bool more = false;
        var watch = Stopwatch.StartNew();
        using var control = DailyToolControl.AcquireMaintenance(root);
        await DailySandbox.ConfigurationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Also serialize with main-window tools and update/migration operations.
            var candidates = new List<(Box Box, DailySandboxAudio.Candidate File)>();
            var allowed = plan?.Copies.ToLookup(c => Path.GetFullPath(c.Sandbox), StringComparer.OrdinalIgnoreCase);
            foreach (string directory in InstanceDirectories(out _))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (ReadBox(directory) is not { } box || !ResourceReady(box)) { deferred++; continue; }
                    if (allowed != null)
                    {
                        foreach (var file in allowed[Path.GetFullPath(directory)].DistinctBy(c => c.Relative, StringComparer.OrdinalIgnoreCase))
                            candidates.Add((box, new(file.Relative, file.Bytes, file.StoredBytes, file.Written)));
                    }
                    else candidates.AddRange(DailySandboxAudio.Candidates(Path.Combine(box.Directory, "root", DailySandboxAudio.RelativeRoot), token).Select(f => (box, f)));
                }
                catch (Exception e) when (Expected(e)) { deferred++; }
            }
            int offset = bounded && ResourceOffsets.TryGetValue(root, out int value) && candidates.Count > 0 ? value % candidates.Count : 0;
            for (; visited < candidates.Count; visited++)
            {
                token.ThrowIfCancellationRequested();
                if (bounded && visited > 0 && (examinedBytes >= ResourceBatchBytes || watch.Elapsed > TimeSpan.FromSeconds(5))) { more = true; break; }
                var (box, file) = candidates[(offset + visited) % candidates.Count]; examinedBytes += file.Bytes;
                try
                {
                    string overlay = Path.Combine(box.Directory, "root", DailySandboxAudio.RelativeRoot);
                    if (DailySandboxAudio.Compare(overlay, ResourceSource, file, true, () => ResourceReady(box), token) is long stored)
                    { files++; bytes += stored; }
                    else deferred++;
                }
                catch (Exception e) when (Expected(e)) { deferred++; }
            }
            if (bounded)
            {
                // Removed entries disappear next time. Leave the cursor on the next retained entry.
                if (more && candidates.Count > files) ResourceOffsets[root] = (offset + visited - files) % (candidates.Count - files);
                else ResourceOffsets.TryRemove(root, out _);
            }
        }
        finally
        {
            DailySandbox.ConfigurationGate.Release();
            // A canceled pass may already have reclaimed complete files. Preserve that evidence.
            try { DailyJson.Write(Path.Combine(root, "resource-optimization-last.json"), new { atUtc = DateTimeOffset.UtcNow, files, bytes, deferred,
                canceled = token.IsCancellationRequested, more, credentialsTouched = false }); }
            catch (Exception e) when (Expected(e)) { }
        }
        return new(files, bytes, deferred, more);
    }
}

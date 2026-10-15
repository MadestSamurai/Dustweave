using System.Security.Cryptography;
namespace Dustweave;

public sealed record DailyCacheLocation(string Path, string[]? Retired = null);
public sealed record DailyStorageItem(string Path, long Bytes, DateTime Written);
public sealed record DailyCacheUnit(string Path, DailyStorageItem[] Files);
public sealed record DailyStorageReport(long TotalBytes, long CacheBytes, long ReclaimableBytes, int Skipped, DailyCacheUnit[] Units);
public sealed record DailyCacheMove(string Path, long Bytes, int RetainedFiles);

/// <summary>Only reproducible inputs move. IPC, identities, journals and OTA recovery keep stable paths.</summary>
public static class DailyCacheStorage
{
    public static IReadOnlyList<string> Areas { get; } = Array.AsReadOnly(new[] { "suite-cache", "trade/catalogs", "rules", "live/prepared", "live/binding-cache" });
    private static string Configuration(string root) => Path.Combine(root, "cache-location.json");
    public static string Location(string root) => DailyJson.TryRead<DailyCacheLocation>(Configuration(root)) is { Path.Length: > 0 } value ? Path.GetFullPath(value.Path) : Path.GetFullPath(root);
    private static string[] Roots(string root) => new[] { Path.GetFullPath(root), Location(root) }.Concat(DailyJson.TryRead<DailyCacheLocation>(Configuration(root))?.Retired ?? []).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Area(string root, string name)
    {
        if (!Areas.Contains(name)) throw new ArgumentException("Unknown cache area", nameof(name));
        return Path.Combine(Location(root), name.Replace('/', Path.DirectorySeparatorChar));
    }
    public static IDisposable Lease(string root, bool exclusive = false)
    {
        Directory.CreateDirectory(root);
        return new FileStream(Path.Combine(root, "cache-storage.lock"), FileMode.OpenOrCreate, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
    }
    public static void Touch(string file)
    {
        try { if (File.Exists(file)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static bool Same(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string parent) => Same(path, parent) || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("storage.link");
    }
    private static List<DailyStorageItem> Files(string root, CancellationToken cancel, ref int skipped)
    {
        var result = new List<DailyStorageItem>();
        if (!Directory.Exists(root)) return result;
        try { NoLinks(root); } catch(IOException) { skipped++; return result; }
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in new DirectoryInfo(directory).GetFileSystemInfos())
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = entry.Attributes;
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry.FullName);
                        else { var file = (FileInfo)entry; result.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc)); }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return result;
    }
    public static Task<DailyStorageReport> ScanAsync(string root, CancellationToken cancel = default) => Task.Run(() => Scan(root, cancel), cancel);
    public static DailyStorageReport Scan(string root, CancellationToken cancel = default) => Scan(root, cancel, true);
    private static DailyStorageReport Scan(string root, CancellationToken cancel, bool includeOtherData)
    {
        root = Path.GetFullPath(root); string cache = Location(root); int skipped = 0;
        string[] roots=Roots(root);
        var all = new List<DailyStorageItem>();
        if(includeOtherData) all.AddRange(Files(root, cancel, ref skipped));
        else { foreach(string area in Areas.Append("updates")) all.AddRange(Files(Path.Combine(root,area),cancel,ref skipped)); }
        foreach(string basis in roots.Where(r=>!Same(r,root))) foreach(string area in Areas) all.AddRange(Files(Path.Combine(basis,area),cancel,ref skipped));
        var units = new List<DailyCacheUnit>(); var cut = DateTime.UtcNow.AddDays(-1);
        // Keep two recently used generations per component, not two across all tools.
        void Generations(string parent)
        {
            var groups = all.Where(f => Within(f.Path, parent)).GroupBy(f => Path.GetRelativePath(parent, f.Path).Split(Path.DirectorySeparatorChar)[0])
                .Where(g => Directory.Exists(Path.Combine(parent, g.Key)))
                .OrderByDescending(g => g.Max(f => f.Written)).ToArray();
            foreach (var group in groups.Skip(2)) if (group.Max(f => f.Written) < cut) units.Add(new(Path.Combine(parent, group.Key), group.ToArray()));
        }
        foreach (string basis in roots)
        {
            if(!Same(basis,cache))
            {
                // A successful migration can retain busy source files; keep accounting for them until safely removed.
                foreach(string area in Areas){string folder=Path.Combine(basis,area);var old=all.Where(f=>Within(f.Path,folder)).ToArray();if(old.Length>0)units.Add(new(folder,old));}
                continue;
            }
            var suite = Path.Combine(basis, "suite-cache");
            // Modules have an additional tool and generation level.
            var observer = all.Where(f => Within(f.Path, suite) && !Within(f.Path, Path.Combine(suite, "modules"))).ToArray();
            foreach (var group in observer.GroupBy(f => Path.GetRelativePath(suite, f.Path).Split(Path.DirectorySeparatorChar)[0]).OrderByDescending(g => g.Max(f => f.Written)).Skip(2))
                if (group.Max(f => f.Written) < cut && group.Key.Length == 64 && group.Key.All(Uri.IsHexDigit)) units.Add(new(Path.Combine(suite, group.Key), group.ToArray()));
            var moduleRoot = Path.Combine(suite, "modules");
            foreach (string tool in all.Where(f => Within(f.Path, moduleRoot)).Select(f => Path.GetRelativePath(moduleRoot, f.Path).Split(Path.DirectorySeparatorChar)[0]).Distinct()) Generations(Path.Combine(moduleRoot, tool));
            Generations(Path.Combine(basis, "trade", "catalogs"));
            Generations(Path.Combine(basis, "live", "prepared"));
            var ruleRoot = Path.Combine(basis, "rules");
            foreach (string area in all.Where(f => Within(f.Path, ruleRoot)).Select(f => Path.GetRelativePath(ruleRoot, f.Path).Split(Path.DirectorySeparatorChar)[0]).Distinct()) Generations(Path.Combine(ruleRoot, area));
            foreach (var file in all.Where(f => Within(f.Path, Path.Combine(basis, "live", "binding-cache")) && f.Written < DateTime.UtcNow.AddDays(-7))) units.Add(new(file.Path, [file]));
        }
        string updates = Path.Combine(root, "updates");
        var versions = all.Where(f => Within(f.Path, updates)).GroupBy(f => Path.GetRelativePath(updates, f.Path).Split(Path.DirectorySeparatorChar)[0])
            .Where(g => Directory.Exists(Path.Combine(updates, g.Key))).OrderByDescending(g => g.Max(f => f.Written)).ToArray();
        foreach (var version in versions.Skip(1))
        {
            if (version.Max(f => f.Written) >= cut) continue;
            var jobs = version.Where(f => Path.GetFileName(f.Path) == "job.json").ToArray();
            bool terminal = jobs.Length > 0 && jobs.All(f => DailyJson.TryRead<DailyUpdateJournal>(Path.Combine(Path.GetDirectoryName(f.Path)!, "backup", "transaction.json"))?.State is "completed" or "rolled_back");
            if (terminal) units.Add(new(Path.Combine(updates, version.Key), version.ToArray()));
        }
        long cacheBytes = all.Where(f => Within(f.Path, updates) || roots.Any(r=>Areas.Any(a => Within(f.Path, Path.Combine(r, a))))).Sum(f => f.Bytes);
        return new(all.Sum(f => f.Bytes), cacheBytes, units.Sum(u => u.Files.Sum(f => f.Bytes)), skipped, units.ToArray());
    }
    private static bool Unchanged(DailyStorageItem f)
    {
        NoLinks(f.Path); var now = new FileInfo(f.Path);
        return now.Exists && now.Length == f.Bytes && now.LastWriteTimeUtc == f.Written;
    }
    private static void EmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        NoLinks(root);
        foreach (string child in Directory.GetDirectories(root)) EmptyDirectories(child);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }
    public static Task<DailyLogCleanupResult> CleanAsync(string root, CancellationToken cancel = default) => Task.Run(() =>
    {
        using var lease = Lease(root, true);
        var plan = Scan(root, cancel, false); int removed = 0, skipped = plan.Skipped; long bytes = 0;
        foreach (var unit in plan.Units)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                // A newer download/compilation or a new recovery file invalidates the entire unit.
                int ignored = 0;
                if (Directory.Exists(unit.Path) && (Files(unit.Path, cancel, ref ignored).Count != unit.Files.Length || ignored != 0) || unit.Files.Any(f => !Unchanged(f))) { skipped++; continue; }
                // Lock every file first; don't partially delete a unit still in use by an older app.
                var handles = new List<FileStream>();
                try
                {
                    foreach (var file in unit.Files) handles.Add(new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Delete));
                    if (unit.Files.Any(f => !Unchanged(f))) { skipped++; continue; }
                    foreach (var file in unit.Files) { File.Delete(file.Path); removed++; bytes += file.Bytes; }
                }
                finally { foreach (var handle in handles) handle.Dispose(); }
                EmptyDirectories(unit.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new DailyLogCleanupResult(removed, bytes, skipped);
    }, cancel);
    public static Task<DailyCacheMove> MoveAsync(string root, string destination, CancellationToken cancel = default, IProgress<long>? progress = null) => Task.Run(() =>
    {
        root = Path.GetFullPath(root); destination = Path.GetFullPath(destination);
        using var lease = Lease(root, true);
        string previous = Location(root);
        var roots=Roots(root);
        if (roots.Any(r=>Within(destination,r)||Within(r,destination))) throw new IOException("storage.overlap");
        NoLinks(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("storage.exists");
        string staging = destination + ".moving-" + Guid.NewGuid().ToString("N");
        var files = new List<DailyStorageItem>(); int skipped = 0;
        foreach (var area in Areas) files.AddRange(Files(Path.Combine(previous, area), cancel, ref skipped));
        if (skipped != 0) throw new IOException("storage.unreadable");
        var drive=new DriveInfo(Path.GetPathRoot(destination)!);
        if(drive.IsReady && drive.AvailableFreeSpace < files.Sum(f=>f.Bytes)+16*1024*1024)throw new IOException("storage.space");
        Directory.CreateDirectory(staging); bool committed = false, renamed = false; long copied = 0, lastProgress = 0;
        try
        {
            foreach (var file in files)
            {
                cancel.ThrowIfCancellationRequested();
                if (!Unchanged(file)) throw new IOException("storage.changed");
                string to = Path.Combine(staging, Path.GetRelativePath(previous, file.Path)); Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                using (var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    var buffer = new byte[128 * 1024]; int count;
                    while ((count = input.Read(buffer)) > 0) { cancel.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); copied += count; if(Environment.TickCount64-lastProgress>=100){lastProgress=Environment.TickCount64;progress?.Report(copied);} }
                    output.Flush(true); input.Position = 0; output.Position = 0;
                    if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(output))) throw new IOException("storage.changed");
                }
                File.SetLastWriteTimeUtc(to, file.Written);
            }
            cancel.ThrowIfCancellationRequested();
            if (files.Any(f => !Unchanged(f))) throw new IOException("storage.changed");
            Directory.Move(staging, destination); renamed = true;
            var location=new DailyCacheLocation(destination,roots.Where(r=>!Same(r,root)).ToArray());
            try { DailyJson.Write(Configuration(root),location); committed=true; }
            catch(IOException) { committed=Same(Location(root),destination); if(!committed)throw; }
            int retained = 0;
            foreach (var file in files)
                try { if (Unchanged(file)) File.Delete(file.Path); else retained++; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { retained++; }
            foreach (var area in Areas) try { EmptyDirectories(Path.Combine(previous, area)); } catch (Exception error) when(error is IOException or UnauthorizedAccessException) { }
            return new DailyCacheMove(destination, copied, retained);
        }
        finally
        {
            // Only our new staging tree is removable on failure; the original is never rolled back by deletion.
            if (!committed)
            {
                string abandoned = renamed ? destination : staging; int ignored = 0;
                foreach (var file in Files(abandoned, CancellationToken.None, ref ignored)) try { File.Delete(file.Path); } catch (Exception error) when(error is IOException or UnauthorizedAccessException) { }
                try { EmptyDirectories(abandoned); } catch (Exception error) when(error is IOException or UnauthorizedAccessException) { }
            }
        }
    }, cancel);
}

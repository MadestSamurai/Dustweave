using System.Security.Cryptography;
namespace Dustweave;

public sealed record DailyCacheLocation(string Path, string[]? Retired = null);
public sealed record DailyStorageItem(string Path, long Bytes, DateTime Written)
{
    public long DiskBytes { get; init; } = Bytes;
}
public sealed record DailyCacheUnit(string Path, DailyStorageItem[] Files)
{
    public string[]? UpdateAttempts { get; init; }
    internal string? CaptureControlPath { get; init; }
}
public sealed record DailyStorageReport(long TotalBytes, long CacheBytes, long ReclaimableBytes, int Skipped, DailyCacheUnit[] Units)
{
    internal IReadOnlyList<DailyStorageItem> AllFiles { get; init; } = [];
    internal IReadOnlyList<DailyStorageItem> CacheFiles { get; init; } = [];
}
public sealed record DailyCacheMove(string Path, long Bytes, int RetainedFiles);

/// <summary>Only reproducible inputs move. IPC, identities, journals and OTA recovery keep stable paths.</summary>
public static class DailyCacheStorage
{
    public static IReadOnlyList<string> Areas { get; } = Array.AsReadOnly(new[] { "suite-cache", "trade/catalogs", "rules", "live/prepared", "live/binding-cache" });
    private static string Configuration(string root) => Path.Combine(root, "cache-location.json");
    public static string Location(string root) => Accounts.SandboxProcessScope.CurrentBox.Length == 0 && DailyJson.TryRead<DailyCacheLocation>(Configuration(root)) is { Path.Length: > 0 } value ? Path.GetFullPath(value.Path) : Path.GetFullPath(root);
    private static string[] Roots(string root) => Accounts.SandboxProcessScope.CurrentBox.Length != 0 ? [Path.GetFullPath(root)] : new[] { Path.GetFullPath(root), Location(root) }.Concat(DailyJson.TryRead<DailyCacheLocation>(Configuration(root))?.Retired ?? []).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Area(string root, string name)
    {
        if (!Areas.Contains(name)) throw new ArgumentException("Unknown cache area", nameof(name));
        return Path.Combine(Location(root), name.Replace('/', Path.DirectorySeparatorChar));
    }
    public static IDisposable Lease(string root, bool exclusive = false)
    {
        return DailyUpdateHost.AcquireStorage(root, exclusive);
    }
    public static void Touch(string file)
    {
        try { if (File.Exists(file)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static bool Same(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    // Callers normalize roots once; enumerated files are already absolute. Avoid
    // repeatedly normalizing every file for every generation during inventory.
    private static bool Within(string path, string parent)
    {
        path=path.Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar);
        parent=Path.TrimEndingDirectorySeparator(parent.Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar));
        return path.Equals(parent,StringComparison.OrdinalIgnoreCase)||path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    public static void NoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("storage.link");
    }
    internal static List<DailyStorageItem> Files(string root, CancellationToken cancel, ref int skipped)
    {
        var result = new List<DailyStorageItem>();
        if (!Directory.Exists(root)) return result;
        try { NoLinks(root); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { skipped++; return result; }
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
                        else { var file = (FileInfo)entry; result.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc) { DiskBytes = DailyStorageCompression.StoredBytes(file.FullName, file.Length, attributes) }); }
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
    internal static DailyStorageReport ScanIsolated(string root, CancellationToken cancel, IReadOnlyList<DailyStorageItem>? inventory = null) => Scan(root, cancel, true, true, inventory);
    internal static DailyStorageReport ScanRetention(string root, DailyLogPolicy policy, CancellationToken cancel, bool fixedRoot=false, IReadOnlyList<DailyStorageItem>? inventory=null, string? captureControl=null) => Scan(root,cancel,true,fixedRoot,inventory,policy,captureControl);
    private static DailyStorageReport Scan(string root, CancellationToken cancel, bool includeOtherData, bool fixedRoot = false, IReadOnlyList<DailyStorageItem>? scannedFiles = null, DailyLogPolicy? policy=null, string? captureControl=null)
    {
        root = Path.GetFullPath(root); string cache = fixedRoot ? root : Location(root); int skipped = 0;
        int days=(policy??DailyLogCleanup.Load(root)).Normalize().Days;
        string[] roots=fixedRoot ? [root] : Roots(root);
        var all = new List<DailyStorageItem>();
        if(scannedFiles!=null) all.AddRange(scannedFiles.Where(f=>Within(f.Path,root)));
        else if(includeOtherData) all.AddRange(Files(root, cancel, ref skipped));
        else { foreach(string area in Areas.Append("updates").Concat(DailyHistoricalCache.Areas)) all.AddRange(Files(Path.Combine(root,area),cancel,ref skipped)); }
        foreach(string basis in roots.Where(r=>!Same(r,root))) foreach(string area in Areas) all.AddRange(Files(Path.Combine(basis,area),cancel,ref skipped));
        var inventory=all.DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        long totalBytes=inventory.Sum(f=>f.DiskBytes);
        string updates = Path.Combine(root, "updates");
        var managed=roots.SelectMany(r=>Areas.Select(a=>Path.Combine(r,a))).Append(updates).Concat(DailyHistoricalCache.Areas.Select(a=>Path.Combine(root,a))).ToArray();
        // Historical queues/receipts contribute to the total, not to cache-generation planning.
        all=inventory.Where(f=>managed.Any(p=>Within(f.Path,p))).ToList();
        var units = new List<DailyCacheUnit>(); var cut = DateTime.UtcNow.AddDays(-1); var expires = DateTime.UtcNow.AddDays(-days);
        // Keep two recently used generations per component, not two across all tools.
        void Generations(string parent)
        {
            var groups = all.Where(f => Within(f.Path, parent)).GroupBy(f => Path.GetRelativePath(parent, f.Path).Split(Path.DirectorySeparatorChar)[0])
                .Where(g => Directory.Exists(Path.Combine(parent, g.Key)))
                .OrderByDescending(g => g.Max(f => f.Written)).ToArray();
            foreach (var (group,index) in groups.Select((g,i)=>(g,i)))
                if (group.Max(f => f.Written) < expires || index >= 2 && group.Max(f => f.Written) < cut)
                    units.Add(new(Path.Combine(parent, group.Key), group.ToArray()));
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
            foreach (var (group,index) in observer.GroupBy(f => Path.GetRelativePath(suite, f.Path).Split(Path.DirectorySeparatorChar)[0]).OrderByDescending(g => g.Max(f => f.Written)).Select((g,i)=>(g,i)))
                if ((group.Max(f => f.Written) < expires || index >= 2 && group.Max(f => f.Written) < cut) && group.Key.Length == 64 && group.Key.All(Uri.IsHexDigit)) units.Add(new(Path.Combine(suite, group.Key), group.ToArray()));
            var moduleRoot = Path.Combine(suite, "modules");
            foreach (string tool in all.Where(f => Within(f.Path, moduleRoot)).Select(f => Path.GetRelativePath(moduleRoot, f.Path).Split(Path.DirectorySeparatorChar)[0]).Distinct()) Generations(Path.Combine(moduleRoot, tool));
            foreach(string generation in all.Where(f=>Within(f.Path,suite)).Select(f=>
            {
                var parts=Path.GetRelativePath(suite,f.Path).Split(Path.DirectorySeparatorChar);
                return parts.Length>=5&&parts[0]=="modules" ? Path.Combine(suite,parts[0],parts[1],parts[2])
                    :parts.Length>=3&&parts[0].Length==64&&parts[0].All(Uri.IsHexDigit)?Path.Combine(suite,parts[0]):"";
            }).Where(p=>p.Length>0).Distinct()) Generations(generation);
            Generations(Path.Combine(basis, "trade", "catalogs"));
            Generations(Path.Combine(basis, "live", "prepared"));
            var ruleRoot = Path.Combine(basis, "rules");
            foreach (string area in all.Where(f => Within(f.Path, ruleRoot)).Select(f => Path.GetRelativePath(ruleRoot, f.Path).Split(Path.DirectorySeparatorChar)[0]).Distinct()) Generations(Path.Combine(ruleRoot, area));
            foreach (var file in all.Where(f => Within(f.Path, Path.Combine(basis, "live", "binding-cache")) && f.Written < expires)) units.Add(new(file.Path, [file]));
            // Client variants and compiler scratch data must not grow inside a retained generation.
            foreach (var group in all.Where(f=>Within(f.Path,suite)).GroupBy(f=>Path.GetDirectoryName(f.Path)!))
            {
                string name=Path.GetFileName(group.Key);
                if(name.Length==38 && name.StartsWith("build-",StringComparison.Ordinal) && name[6..].All(Uri.IsHexDigit)
                    && group.Max(f=>f.Written)<cut) units.Add(new(group.Key,Files(group.Key,cancel,ref skipped).ToArray()));
            }
            // Pre-.NET catalog bundles are not used by the current planner.
            string catalogs=Path.Combine(basis,"trade","catalogs");
            foreach(string generation in all.Where(f=>Within(f.Path,catalogs)).Select(f=>Path.GetRelativePath(catalogs,f.Path).Split(Path.DirectorySeparatorChar)[0]).Distinct())
                foreach(string legacy in new[]{"worker","trade-worker"})
                {
                    string folder=Path.Combine(catalogs,generation,"bundle",legacy);
                    var files=all.Where(f=>Within(f.Path,folder)).ToArray();
                    if(files.Length>0 && files.Max(f=>f.Written)<cut) units.Add(new(folder,files));
                }
        }
        units.AddRange(UpdateCacheRetention.Plan(updates, all.Where(f => Within(f.Path, updates)).ToArray(), DateTime.UtcNow, cancel, days));
        units.AddRange(DailyHistoricalCache.Plan(root,all,DateTime.UtcNow,days,captureControl));
        long cacheBytes = all.Sum(f => f.DiskBytes);
        // Parent expiry supersedes nested scratch/legacy units; count and delete each file once.
        var distinct=new List<DailyCacheUnit>();
        foreach(var unit in units.OrderBy(u=>u.Path.Length)) if(!distinct.Any(p=>Within(unit.Path,p.Path))) distinct.Add(unit);
        return new(totalBytes, cacheBytes, distinct.Sum(u => u.Files.Sum(f => f.DiskBytes)), skipped, distinct.ToArray()) { AllFiles=inventory,CacheFiles=all };
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
    public static Task<DailyLogCleanupResult> CleanAsync(string root, CancellationToken cancel = default) => CleanLocalAsync(root, false, cancel);
    internal static Task<DailyLogCleanupResult> CleanLocalAsync(string root, bool fixedRoot, CancellationToken cancel, DailyLogPolicy? policy=null, string? captureControl=null) => Task.Run(() =>
    {
        using var lease = Lease(root, true);
        var plan = Scan(root, cancel, false, fixedRoot, policy:policy,captureControl:captureControl);
        var result = DeleteUnits(plan.Units, cancel);
        return result with { Skipped = result.Skipped + plan.Skipped };
    }, cancel);
    internal static DailyLogCleanupResult DeleteUnits(IEnumerable<DailyCacheUnit> units, CancellationToken cancel, Func<bool>? available = null)
    {
        int removed=0,skipped=0;long bytes=0;
        foreach (var unit in units)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var capture=DailyHistoricalCache.AcquireCapture(unit);
                if (available?.Invoke()==false || !UpdateCacheRetention.CanClean(unit)) { skipped++; continue; }
                // A newer download/compilation or a new recovery file invalidates the entire unit.
                int ignored = 0;
                if (Directory.Exists(unit.Path) && (Files(unit.Path, cancel, ref ignored).Count != unit.Files.Length || ignored != 0) || unit.Files.Any(f => !Unchanged(f))) { skipped++; continue; }
                // Lock every file first; don't partially delete a unit still in use by an older app.
                var handles = new List<FileStream>();
                try
                {
                    foreach (var file in unit.Files) handles.Add(new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                    if (available?.Invoke()==false || unit.Files.Any(f => !Unchanged(f)) || !UpdateCacheRetention.CanClean(unit)) { skipped++; continue; }
                    foreach (var file in unit.Files) { File.Delete(file.Path); removed++; bytes += file.DiskBytes; }
                }
                finally { foreach (var handle in handles) handle.Dispose(); }
                EmptyDirectories(unit.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new DailyLogCleanupResult(removed, bytes, skipped);
    }
    public static void DiscardBuild(string parent,string directory)
    {
        // Only the compiler's newly created sibling scratch directory; never a caller-supplied tree.
        string name=Path.GetFileName(directory);
        if(!Same(Path.GetDirectoryName(directory)!,parent) || name.Length!=38 || !name.StartsWith("build-",StringComparison.Ordinal) || !name[6..].All(Uri.IsHexDigit))return;
        try
        {
            int skipped=0;var files=Files(directory,CancellationToken.None,ref skipped);
            if(skipped==0)DeleteUnits([new(directory,files.ToArray())],CancellationToken.None);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
    public static Task<DailyCacheMove> MoveAsync(string root, string destination, CancellationToken cancel = default, IProgress<long>? progress = null) => Task.Run(() =>
    {
        root = Path.GetFullPath(root); destination = Path.GetFullPath(destination);
        using var lease = Lease(root, true);
        string previous = Location(root);
        var roots=Roots(root);
        if (roots.Any(r=>Within(destination,r)||Within(r,destination))) throw new IOException("storage.overlap");
        if(Within(destination,DailySandbox.Store)||Within(destination,Path.Combine(Path.GetTempPath(),".net")))throw new IOException("storage.overlap");
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

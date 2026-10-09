using System.Text.Json;
namespace Dustweave;

public sealed record DailyLogPolicy(bool Automatic = true, int Days = 7, int LimitMiB = 512)
{
    public DailyLogPolicy Normalize() => this with { Days = Math.Clamp(Days, 2, 90), LimitMiB = Math.Clamp(LimitMiB, 128, 4096) };
}
public sealed record DailyLogFile(string Path, long Bytes, DateTime Written, DateTime Created);
public sealed record DailyLogUnit(string Path, bool Step, DailyLogFile[] Files)
{
    public long Bytes => Files.Sum(f => f.Bytes);
    public DateTime Written => Files.Max(f => f.Written);
}
public sealed record DailyLogPlan(string Root, DailyLogPolicy Policy, DateTime At, long ManagedBytes, DailyLogUnit[] Units, int Skipped)
{
    public long ReclaimableBytes => Units.Sum(u => u.Bytes);
    public int Files => Units.Sum(u => u.Files.Length);
}
public sealed record DailyLogScanProgress(int Entries, long Bytes, int Examined);
public sealed record DailyLogCleanupResult(int Files, long Bytes, int Skipped);

// An explicit deletion allowlist, deliberately much narrower than diagnostic export.
// Queue/account/history/business journals are durable state, not disposable logs.
public static class DailyLogCleanup
{
    static readonly HashSet<string> StepNames = new(StringComparer.Ordinal) { "before.json", "intent.json", "transport.json", "result.json" };
    public static DailyLogPolicy Load(string root) => (DailyJson.TryRead<DailyLogPolicy>(Path.Combine(root, "log-retention.json")) ?? new()).Normalize();
    public static void Save(string root, DailyLogPolicy policy) => DailyJson.Write(Path.Combine(root, "log-retention.json"), policy.Normalize());
    static bool Id(string value) => value.Length == 32 && value.All(char.IsAsciiHexDigit);
    static bool Safe(string root, string path)
    {
        root = Path.GetFullPath(root); path = Path.GetFullPath(path);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (string? p = path; p != null; p = Path.GetDirectoryName(p))
        {
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
    static DailyLogFile Stamp(FileInfo f, bool refresh = true) { if(refresh) f.Refresh(); return new(f.FullName, f.Length, f.LastWriteTimeUtc, f.CreationTimeUtc); }
    static JsonDocument Read(string path, long maximum = 16 * 1024 * 1024)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Record too large for automatic cleanup");
        return JsonDocument.Parse(stream);
    }
    static string Text(JsonElement d, string key) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static bool CompletedStep(string root, string folder)
    {
        string id = Path.GetFileName(folder);
        if (!Id(id)) return false;
        string intent = Path.Combine(folder,"intent.json"), result = Path.Combine(folder,"result.json");
        if (!Safe(root,folder) || (File.GetAttributes(intent)&FileAttributes.ReparsePoint)!=0 || (File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0) return false;
        using var command = Read(intent, 128 * 1024);
        if (Text(command.RootElement,"Id") != id) return false;
        using var final = Read(result);
        if (Text(final.RootElement,"state") != "observed_expected_ui" || Text(final.RootElement,"id") != id || Text(final.RootElement,"engine") != "dotnet-driver-v1") return false;
        // UI visibility is not proof of a transaction. Keep its command until the
        // owning business record itself confirms completion. Unknown formats stay.
        string reason = Text(command.RootElement,"Reason");
        if (reason.Contains("business:",StringComparison.Ordinal))
        {
            int start = reason.IndexOf("business:",StringComparison.Ordinal) + 9;
            string business = reason[start..].Split('|')[0];
            if (!Id(business)) return false;
            bool found = false;
            foreach (var tree in new[]{"business","managed-business"})
            {
                string path = Path.Combine(root,"live",tree,business+".json");
                if (!File.Exists(path)) continue;
                if (!Safe(root,path)) return false;
                using var record = Read(path);
                if (Text(record.RootElement,"id") != business || Text(record.RootElement,"state") is not ("completed" or "rejected" or "server_rejected" or "superseded")) return false;
                found = true;
            }
            if (!found) return false;
        }
        return true;
    }
    static bool LogName(string name) => name.EndsWith(".log",StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase)
        || Enumerable.Range(1,3).Any(n => name.EndsWith(".jsonl."+n,StringComparison.OrdinalIgnoreCase) || name.EndsWith(".log."+n,StringComparison.OrdinalIgnoreCase))
        || name.EndsWith(".log.previous",StringComparison.OrdinalIgnoreCase);
    static IEnumerable<FileInfo> Logs(string root, string folder, int depth, CancellationToken token)
    {
        if (!Directory.Exists(folder)) yield break;
        var pending = new Stack<(string Path,int Depth)>(); pending.Push((folder,depth));
        while(pending.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            FileSystemInfo[] items;
            try { items = new DirectoryInfo(entry.Path).GetFileSystemInfos(); } catch(IOException) { continue; } catch(UnauthorizedAccessException) { continue; }
            foreach(var item in items)
            {
                token.ThrowIfCancellationRequested();
                if((item.Attributes & FileAttributes.ReparsePoint)!=0) continue;
                if(item is FileInfo file && LogName(file.Name) && Safe(root,file.FullName)) yield return file;
                else if(item is DirectoryInfo dir && entry.Depth>0) pending.Push((dir.FullName,entry.Depth-1));
            }
        }
    }
    public static Task<DailyLogPlan> ScanAsync(string root, DailyLogPolicy policy, CancellationToken token = default, IProgress<DailyLogScanProgress>? progress = null) => Task.Run(()=>Scan(root,policy,DateTime.UtcNow,token,progress),token);
    public static DailyLogPlan Scan(string root, DailyLogPolicy policy, DateTime now, CancellationToken token = default, IProgress<DailyLogScanProgress>? progress = null)
    {
        root=Path.GetFullPath(root);policy=policy.Normalize();long total=0;int skipped=0;
        var candidates=new List<DailyLogUnit>();int entriesCount=0,examined=0;
        void Report(){progress?.Report(new(entriesCount,total,examined));}
        // Never include caches, account stores, queues, exported ZIPs or transaction evidence.
        foreach(var (tree,depth) in new[]{("",0),("live/diagnostics",1),("tools",3),("parallel-workers",2)})
        {
            string folder=Path.Combine(root,tree);
            if(!Directory.Exists(folder) || tree.Length>0 && !Safe(root,folder)) continue;
            foreach(var file in Logs(root,folder,depth,token))
            {
                try { var stamp=Stamp(file);total+=stamp.Bytes;if(stamp.Written<now.AddDays(-2)) candidates.Add(new(file.FullName,false,[stamp])); }
                catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
            }
        }
        string steps=Path.Combine(root,"live","steps");
        if(Directory.Exists(steps) && Safe(root,steps)) foreach(var folder in new DirectoryInfo(steps).EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();
            try {
                if(!Id(folder.Name)||(folder.Attributes & FileAttributes.ReparsePoint)!=0)continue;
                var entries=folder.GetFileSystemInfos();
                var files=entries.OfType<FileInfo>().ToArray();
                if(entries.Length!=files.Length || files.Length==0 || files.Any(f=>!StepNames.Contains(f.Name)||(f.Attributes & FileAttributes.ReparsePoint)!=0)) {skipped++;continue;}
                var unit=new DailyLogUnit(folder.FullName,true,files.Select(f=>Stamp(f,false)).ToArray());total+=unit.Bytes;entriesCount+=files.Length;if(entriesCount%500<files.Length)Report();
                if(unit.Written<now.AddDays(-2))candidates.Add(unit);
            }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
        }
        Report();var selected=new List<DailyLogUnit>();long remaining=total;
        foreach(var unit in candidates.OrderBy(u=>u.Written))
        {
            token.ThrowIfCancellationRequested();
            if(unit.Written>=now.AddDays(-policy.Days) && remaining <= policy.LimitMiB*1024L*1024)continue;
            if(++examined%100==0)Report();
            try { if(unit.Step&&!CompletedStep(root,unit.Path)){skipped++;continue;} }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){skipped++;continue;}
            selected.Add(unit);remaining-=unit.Bytes;
        }
        Report();return new(root,policy,DateTime.UtcNow,total,selected.ToArray(),skipped);
    }
    static bool AllowedUnit(string root, DailyLogUnit unit)
    {
        if(unit.Files.Length==0)return false;
        if(unit.Step)return Path.GetDirectoryName(unit.Path)==Path.Combine(root,"live","steps") && Id(Path.GetFileName(unit.Path))
            && unit.Files.All(f=>Path.GetDirectoryName(f.Path)==unit.Path && StepNames.Contains(Path.GetFileName(f.Path)));
        if(unit.Files.Length!=1 || unit.Path!=unit.Files[0].Path || !LogName(Path.GetFileName(unit.Path)))return false;
        string relative=Path.GetRelativePath(root,unit.Path).Replace(Path.DirectorySeparatorChar,'/');
        return !relative.Contains('/') || new[]{"live/diagnostics/","tools/","parallel-workers/"}.Any(prefix=>relative.StartsWith(prefix,StringComparison.Ordinal));
    }
    public static Task<DailyLogCleanupResult> ApplyAsync(DailyLogPlan plan, CancellationToken token = default) => Task.Run(()=>Apply(plan,token),token);
    public static DailyLogCleanupResult Apply(DailyLogPlan plan, CancellationToken token = default)
    {
        // Only a freshly scanned plan may be applied. Recheck each whole step before
        // touching it, including the owning transaction and all file timestamps.
        if(DateTime.UtcNow-plan.At>TimeSpan.FromMinutes(30))throw new InvalidOperationException("logs.rescan");
        int deleted=0,skipped=plan.Skipped;long bytes=0;
        foreach(var unit in plan.Units)
        {
            token.ThrowIfCancellationRequested();
            try {
                if(!AllowedUnit(plan.Root,unit) || unit.Written>=DateTime.UtcNow.AddDays(-2) || unit.Files.Any(f=>!Safe(plan.Root,f.Path)||Stamp(new FileInfo(f.Path))!=f)) {skipped++;continue;}
                if(unit.Step && (!CompletedStep(plan.Root,unit.Path)||Directory.GetFileSystemEntries(unit.Path).Length!=unit.Files.Length)){skipped++;continue;}
                foreach(var file in unit.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if(!Safe(plan.Root,file.Path)||Stamp(new FileInfo(file.Path))!=file){skipped++;continue;}
                    // A locked file is skipped. Cleanup never waits for a writer or retries an operation.
                    if(DailyLogFileRemoval.Delete(file)){deleted++;bytes+=file.Bytes;}else skipped++;
                }
                if(unit.Step && Directory.Exists(unit.Path) && !Directory.EnumerateFileSystemEntries(unit.Path).Any())Directory.Delete(unit.Path,false);
            }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}catch(JsonException){skipped++;}
        }
        return new(deleted,bytes,skipped);
    }
}

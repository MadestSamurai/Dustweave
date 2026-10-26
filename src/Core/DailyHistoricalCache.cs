using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustweave;

// Temporary product-owned evidence only. User-selected collector outputs, account
// stores, installed plugins and transaction journals are never searched here.
internal static class DailyHistoricalCache
{
    internal static readonly string[] Areas=["loading-experiment","extensions/checks"];
    static readonly Regex TraceName=new(@"^runtime-[1-9][0-9]*-[1-9][0-9]*\.jsonl$",RegexOptions.CultureInvariant);
    static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or JsonException;

    internal static IDisposable? AcquireCapture(DailyCacheUnit unit)
    {
        if(unit.CaptureControlPath is not {} control)return null;
        DailyCacheStorage.NoLinks(control);
        string parent=Path.GetDirectoryName(control)!;
        Directory.CreateDirectory(parent);
        string lockPath=Path.Combine(parent,"capture.lock");
        DailyCacheStorage.NoLinks(lockPath);
        var lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            // The collector owns this same lock through its final drain. A runtime
            // writer may outlive a crashed collector briefly; allow its lease to expire.
            if(File.Exists(control))
            {
                using var file=new FileStream(control,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(file.Length>64*1024)throw new IOException("Capture state unavailable");
                var state=JsonSerializer.Deserialize<DailyLoadingExperiment.Control>(file,DailyJson.Options);
                if(state==null||!Guid.TryParseExact(state.Id,"N",out _)||state.UntilUtcTicks<=0||
                    state.UntilUtcTicks>=DateTime.UtcNow.AddMinutes(-1).Ticks)
                    throw new IOException("Capture is active or its state is unavailable");
            }
            return lease;
        }
        catch(JsonException error){lease.Dispose();throw new IOException("Capture state unavailable",error);}
        catch{lease.Dispose();throw;}
    }

    internal static DailyCacheUnit[] Plan(string root,IEnumerable<DailyStorageItem> files,DateTime now,int days,string? control=null)
    {
        var units=new List<DailyCacheUnit>();
        DateTime cutoff=now.AddDays(-Math.Clamp(days,2,7));
        foreach(string area in Areas)
        {
            string folder=Path.GetFullPath(Path.Combine(root,area)),prefix=folder+Path.DirectorySeparatorChar;
            bool capture=area=="loading-experiment";
            foreach(var group in files.Where(f=>f.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                .GroupBy(f=>Path.GetRelativePath(folder,f.Path).Split(Path.DirectorySeparatorChar)[0]))
            {
                if(!Guid.TryParseExact(group.Key,"N",out _)||group.Max(f=>f.Written)>=cutoff)continue;
                string path=Path.Combine(folder,group.Key);
                if(group.Any(f=>Path.GetDirectoryName(f.Path)!=path||
                    !(capture?TraceName.IsMatch(Path.GetFileName(f.Path))||Path.GetFileName(f.Path)=="capture-error.txt":
                        Path.GetFileName(f.Path) is "result.json" or "timeout.json")))continue;
                var unit=new DailyCacheUnit(path,group.ToArray()) {CaptureControlPath=capture?control??DailyLoadingExperiment.ControlPath:null};
                try{using var lease=AcquireCapture(unit);units.Add(unit);}
                catch(Exception e)when(Expected(e)){/* A running/unknown capture remains counted, never reclaimable. */}
            }
        }
        return units.ToArray();
    }
}

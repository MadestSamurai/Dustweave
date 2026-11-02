namespace Dustweave;

public sealed record DailyStorageDisk(string Path,long Bytes,long ReclaimableBytes);
internal sealed class DailyStorageDisks
{
    readonly Dictionary<string,DailyStorageItem> files=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> reclaimable=new(StringComparer.OrdinalIgnoreCase);
    internal void Add(IEnumerable<DailyStorageItem> measured,IEnumerable<string> removable)
    {
        foreach(var file in measured)files.TryAdd(file.Path,file);
        reclaimable.UnionWith(removable);
    }
    internal DailyStorageDisk[] Read(Func<string,string>? disk=null) => files.Values
        .GroupBy(f=>(disk??(p=>System.IO.Path.GetPathRoot(p)!))(f.Path),StringComparer.OrdinalIgnoreCase)
        .OrderBy(g=>g.Key,StringComparer.OrdinalIgnoreCase)
        .Select(g=>new DailyStorageDisk(g.Key,g.Sum(f=>f.DiskBytes),g.Where(f=>reclaimable.Contains(f.Path)).Sum(f=>f.DiskBytes))).ToArray();
}

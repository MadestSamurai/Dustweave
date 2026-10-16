using System.Diagnostics;
using System.Text.Json;
namespace Dustweave;

public sealed record DailyStorageScope(string Kind, string Path, long Bytes, long CacheBytes, long LogBytes, long ReclaimableBytes, int Skipped);
public sealed record DailyStorageOverview(DailyStorageScope[] Scopes)
{
    public long TotalBytes => Scopes.Sum(s=>s.Bytes);
    public long CacheBytes => Scopes.Sum(s=>s.CacheBytes);
    public long LogBytes => Scopes.Sum(s=>s.LogBytes);
    public long ReclaimableBytes => Scopes.Sum(s=>s.ReclaimableBytes);
    public int Skipped => Scopes.Sum(s=>s.Skipped);
    public long SandboxBytes => Scopes.Where(s=>s.Kind=="sandbox").Sum(s=>s.Bytes);
    public long ExtractionBytes => Scopes.Where(s=>s.Kind=="extraction").Sum(s=>s.Bytes);
}

/// <summary>Accounts and game data are counted, but deletion is restricted to each application's allowlists.</summary>
public sealed class DailyStorageMaintenance
{
    readonly string root;
    readonly string? instances, extraction;
    readonly Func<string,bool> boxIdle;
    readonly Func<bool> extractionIdle;
    static readonly string[] ExtractionNames=["Dustweave","Dustweave.Updater","BD2DailyAssistant","BD2DailyAssistant.Updater"];
    sealed record Box(string Directory,string Data,string Name,string Account);
    public DailyStorageMaintenance(string root) : this(root, Shared(root)?DailySandbox.Store:null,
        Shared(root)?Path.Combine(Path.GetTempPath(),".net"):null, BoxIdle, ExtractionIdle) { }
    // Explicit locations/probes keep synthetic checks entirely outside real user storage.
    public DailyStorageMaintenance(string root,string? instances,string? extraction,Func<string,bool> boxIdle,Func<bool> extractionIdle)
    { this.root=Path.GetFullPath(root);this.instances=instances;this.extraction=extraction;this.boxIdle=boxIdle;this.extractionIdle=extractionIdle; }
    static bool Shared(string root) => Accounts.SandboxProcessScope.CurrentBox.Length==0 &&
        Accounts.SandboxConfiguration.SameRoot(root,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant"));
    static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException or System.ComponentModel.Win32Exception;
    static bool BoxIdle(string box)
    {
        // Query the driver, not a saved process ID. Errors defer cleanup, never authorize it.
        var processes=Process.GetProcesses();
        try
        {
            foreach(var p in processes)
            {
                if(p.Id<=0)continue;
                try { if(Accounts.SandboxProcessScope.BoxOf(p.Id).Equals(box,StringComparison.OrdinalIgnoreCase))return false; }
                catch(IOException) when(p.HasExited) { }
            }
            return true;
        }
        catch(Exception e) when(Expected(e)){return false;}
        finally{foreach(var p in processes)p.Dispose();}
    }
    static bool ExtractionIdle()
    {
        // Old single-file hosts may lazily load more files from their extraction directory.
        if(typeof(DailyStorageMaintenance).Assembly.Location.Length==0)return false;
        foreach(string name in ExtractionNames)
        {
            var processes=Process.GetProcessesByName(name);
            try { if(processes.Any(p=>p.Id!=Environment.ProcessId&&!p.HasExited))return false; }
            catch(Exception e) when(Expected(e)){return false;}
            finally{foreach(var p in processes)p.Dispose();}
        }
        return true;
    }
    Box? ReadBox(string directory)
    {
        DailyCacheStorage.NoLinks(directory);
        DailyCacheStorage.NoLinks(Path.Combine(directory,"instance.json"));
        var registration=DailyJson.TryRead<DailySandboxRequest>(Path.Combine(directory,"instance.json"));
        if(registration==null || !DailyProfiles.ValidKey(registration.Account) || !DailySandbox.ValidBoxName(registration.Box)
            || Path.GetFileName(directory)!=DailySandbox.CanonicalBoxName(registration.Account))return null;
        string data=Path.Combine(directory,"root","user","current","AppData","Local","BD2DailyAssistant");
        DailyCacheStorage.NoLinks(data);
        return new(directory,data,registration.Box,registration.Account);
    }
    bool CanClean(Box box)
    {
        try
        {
            var current=ReadBox(box.Directory);
            if(current!=box || !boxIdle(box.Name))return false;
            // Custom test probes do not load Sandboxie. Production must verify renamed bindings too.
            if(boxIdle==BoxIdle && DailySandbox.Installation() is {} installation)
                return Accounts.SandboxConfiguration.SameRoot(Accounts.SandboxConfiguration.Read(installation,box.Name,"FileRootPath"),Path.Combine(box.Directory,"root"));
            return true;
        }
        catch(Exception e) when(Expected(e)){return false;}
    }
    string[] InstanceDirectories(out int skipped)
    {
        skipped=0;
        try
        {
            if(instances==null || !Directory.Exists(instances))return [];
            DailyCacheStorage.NoLinks(instances);
            return Directory.GetDirectories(instances);
        }
        catch(Exception e) when(Expected(e)){skipped++;return [];}
    }
    IEnumerable<string> ExtractionDirectories()
    {
        if(extraction==null)yield break;
        foreach(string name in ExtractionNames)
        {
            string folder=Path.Combine(extraction,name);
            if(Directory.Exists(folder))yield return folder;
        }
    }
    internal static DailyCacheUnit[] ExtractionPlan(string folder,DailyStorageItem[] files,DateTime now)
    {
        return files.GroupBy(f=>Path.GetRelativePath(folder,f.Path).Split(Path.DirectorySeparatorChar)[0])
            .Where(g=>g.Key.Length==12 && g.Key.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-') && Directory.Exists(Path.Combine(folder,g.Key)))
            .OrderByDescending(g=>g.Max(f=>f.Written)).Select((g,i)=>(Group:g,Index:i))
            .Where(v=>v.Group.Max(f=>f.Written)<now.AddDays(-7) || v.Index>=2&&v.Group.Max(f=>f.Written)<now.AddDays(-1))
            .Select(v=>new DailyCacheUnit(Path.Combine(folder,v.Group.Key),v.Group.ToArray())).ToArray();
    }
    public Task<DailyStorageOverview> ScanAsync(DailyLogPolicy policy,CancellationToken token=default)=>Task.Run(()=>
    {
        var scopes=new List<DailyStorageScope>();
        var storage=DailyCacheStorage.Scan(root,token);
        var logs=DailyLogCleanup.Scan(root,policy,DateTime.UtcNow,token);
        var counted=storage.AllFiles.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        scopes.Add(new("main",root,storage.TotalBytes,storage.CacheBytes,logs.ManagedBytes,storage.ReclaimableBytes+logs.ReclaimableBytes,storage.Skipped+logs.Skipped));
        var directories=InstanceDirectories(out int omitted);
        if(omitted>0)scopes.Add(new("sandbox",instances!,0,0,0,0,omitted));
        foreach(string directory in directories)
        {
            token.ThrowIfCancellationRequested(); int skipped=0;
            var files=DailyCacheStorage.Files(directory,token,ref skipped);
            var measured=files.Where(f=>counted.Add(f.Path)).ToArray();
            var included=measured.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long cache=0,log=0,reclaim=0;
            try
            {
                if(ReadBox(directory) is {} box && Directory.Exists(box.Data))
                {
                    var s=DailyCacheStorage.ScanIsolated(box.Data,token,files);var l=DailyLogCleanup.Scan(box.Data,policy,DateTime.UtcNow,token);
                    cache=s.CacheFiles.Where(f=>included.Contains(f.Path)).Sum(f=>f.Bytes);log=l.ManagedBytes;skipped+=s.Skipped+l.Skipped;
                    if(CanClean(box))reclaim=s.Units.SelectMany(u=>u.Files).Where(f=>included.Contains(f.Path)).Sum(f=>f.Bytes)
                        +l.Units.SelectMany(u=>u.Files).Where(f=>included.Contains(f.Path)).Sum(f=>f.Bytes);
                }
            }
            catch(Exception e) when(Expected(e)){skipped++;}
            scopes.Add(new("sandbox",directory,measured.Sum(f=>f.Bytes),cache,log,reclaim,skipped));
        }
        bool canExtract=extractionIdle();
        foreach(string folder in ExtractionDirectories())
        {
            int skipped=0;var files=DailyCacheStorage.Files(folder,token,ref skipped).ToArray();
            var measured=files.Where(f=>counted.Add(f.Path)).ToArray();var included=measured.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            scopes.Add(new("extraction",folder,measured.Sum(f=>f.Bytes),measured.Sum(f=>f.Bytes),0,
                canExtract?ExtractionPlan(folder,files,DateTime.UtcNow).Sum(u=>u.Files.Where(f=>included.Contains(f.Path)).Sum(f=>f.Bytes)):0,skipped));
        }
        return new DailyStorageOverview(scopes.ToArray());
    },token);
    public static async Task<DailyLogCleanupResult> MaintainLocalAsync(string root,DailyLogPolicy policy,bool fixedRoot,CancellationToken token)
    {
        DailyCacheStorage.NoLinks(root);
        using var control=DailyUpdateHost.AcquireControl(root);
        await DailyLogEvidence.FlushAsync(root,token);
        await DailyLogEvidence.MaintainAsync(root,token);
        var plan=await DailyLogCleanup.ScanAsync(root,policy,token);
        var logs=await DailyLogCleanup.ApplyAsync(plan,token);
        var cache=await DailyCacheStorage.CleanLocalAsync(root,fixedRoot,token);
        return new(logs.Files+cache.Files,logs.Bytes+cache.Bytes,logs.Skipped+cache.Skipped);
    }
    public Task<DailyLogCleanupResult> CleanAsync(DailyLogPolicy policy,CancellationToken token=default)=>Task.Run(()=>CleanCoreAsync(policy,token),token);
    async Task<DailyLogCleanupResult> CleanCoreAsync(DailyLogPolicy policy,CancellationToken token)
    {
        var result=await MaintainLocalAsync(root,policy,false,token);
        void Add(DailyLogCleanupResult r)=>result=new(result.Files+r.Files,result.Bytes+r.Bytes,result.Skipped+r.Skipped);
        // Serialize with host-initiated sandbox launches/rebinding, then lock each data root separately.
        await DailySandbox.ConfigurationGate.WaitAsync(token);
        try
        {
            var directories=InstanceDirectories(out int omitted);Add(new(0,0,omitted));
            foreach(string directory in directories)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if(ReadBox(directory) is not {} box || !Directory.Exists(box.Data) || !CanClean(box)){Add(new(0,0,1));continue;}
                    Add(await MaintainLocalAsync(box.Data,policy,true,token));
                }
                catch(Exception e) when(Expected(e)){Add(new(0,0,1));}
            }
        }
        finally{DailySandbox.ConfigurationGate.Release();}
        foreach(string folder in ExtractionDirectories())
        {
            token.ThrowIfCancellationRequested();
            int skipped=0;var files=DailyCacheStorage.Files(folder,token,ref skipped).ToArray();
            var cleaned=DailyCacheStorage.DeleteUnits(ExtractionPlan(folder,files,DateTime.UtcNow),token,extractionIdle);
            Add(cleaned with {Skipped=cleaned.Skipped+skipped});
        }
        return result;
    }
    // Workers have no main-window timer. Maintenance is bounded and cannot change the queue outcome.
    public static async Task FinishWorkerAsync(string root,TimeSpan? budget=null)
    {
        using var lifetime=new CancellationTokenSource(budget??TimeSpan.FromSeconds(20));
        try
        {
            var policy=DailyLogCleanup.Load(root);
            if(policy.Automatic)await MaintainLocalAsync(root,policy,true,lifetime.Token);
            else {using var control=DailyUpdateHost.AcquireControl(root);await DailyLogEvidence.FlushAsync(root,lifetime.Token);}
        }
        catch(OperationCanceledException) { }
        catch(Exception e) when(Expected(e)) { }
    }
}

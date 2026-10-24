using System.Diagnostics;
using System.Text.Json;
namespace Dustweave;

public sealed record DailyStorageScope(string Kind, string Path, long Bytes, long CacheBytes, long LogBytes, long ReclaimableBytes, int Skipped)
{
    public int OptimizableFiles { get; init; }
}
public sealed record DailyStorageOverview(DailyStorageScope[] Scopes)
{
    public long TotalBytes => Scopes.Sum(s=>s.Bytes);
    public long CacheBytes => Scopes.Sum(s=>s.CacheBytes);
    public long LogBytes => Scopes.Sum(s=>s.LogBytes);
    public long ReclaimableBytes => Scopes.Sum(s=>s.ReclaimableBytes);
    public int Skipped => Scopes.Sum(s=>s.Skipped);
    public long SandboxBytes => Scopes.Where(s=>s.Kind=="sandbox").Sum(s=>s.Bytes);
    public long ExtractionBytes => Scopes.Where(s=>s.Kind=="extraction").Sum(s=>s.Bytes);
    public int OptimizableFiles => Scopes.Sum(s=>s.OptimizableFiles);
}

/// <summary>Accounts and game data are counted, but deletion is restricted to each application's allowlists.</summary>
public sealed class DailyStorageMaintenance
{
    readonly string root;
    readonly string? instances, extraction, crashes;
    readonly Func<string,bool> boxIdle;
    readonly Func<bool> extractionIdle;
    readonly Func<string,bool> extractionSafe;
    static readonly string[] ExtractionNames=["Dustweave","Dustweave.Updater","BD2DailyAssistant","BD2DailyAssistant.Updater"];
    sealed record Box(string Directory,string Data,string Name,string Account);
    public DailyStorageMaintenance(string root) : this(root, Shared(root)?DailySandbox.Store:null,
        Shared(root)?Path.Combine(Path.GetTempPath(),".net"):null, BoxIdle, ExtractionIdle,
        Shared(root)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrashDumps"):null,
        directory=>MayCleanExtraction(directory,AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string,typeof(object).Assembly.Location.Length==0)) { }
    // Explicit locations/probes keep synthetic checks entirely outside real user storage.
    public DailyStorageMaintenance(string root,string? instances,string? extraction,Func<string,bool> boxIdle,Func<bool> extractionIdle,string? crashes=null,Func<string,bool>? extractionSafe=null)
    { this.root=Path.GetFullPath(root);this.instances=instances;this.extraction=extraction;this.boxIdle=boxIdle;this.extractionIdle=extractionIdle;this.crashes=crashes;this.extractionSafe=extractionSafe??(_=>true); }
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
        // Other hosts may still load native files lazily. The current host's
        // precise extraction generation is excluded separately.
        foreach(string name in ExtractionNames)
        {
            var processes=Process.GetProcessesByName(name);
            try { if(processes.Any(p=>p.Id!=Environment.ProcessId&&!p.HasExited))return false; }
            catch(Exception e) when(Expected(e)){return false;}
            finally{foreach(var p in processes)p.Dispose();}
        }
        return true;
    }
    internal static bool MayCleanExtraction(string directory,string? nativePaths,bool bundledRuntime)
    {
        if(string.IsNullOrWhiteSpace(nativePaths))return !bundledRuntime;
        try
        {
            string basis=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            foreach(string value in nativePaths.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
            {
                string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
                if(path.Equals(basis,StringComparison.OrdinalIgnoreCase)||path.StartsWith(basis+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return false;
            }
            return true;
        }
        catch(Exception error) when(Expected(error)){return false;}
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
        scopes.Add(new("main",root,storage.TotalBytes,storage.CacheBytes,logs.ManagedBytes,storage.ReclaimableBytes+logs.ReclaimableBytes,storage.Skipped+logs.Skipped)
            {OptimizableFiles=CompressionPlan(root,storage.AllFiles).Length});
        var directories=InstanceDirectories(out int omitted);
        if(omitted>0)scopes.Add(new("sandbox",instances!,0,0,0,0,omitted));
        foreach(string directory in directories)
        {
            token.ThrowIfCancellationRequested(); int skipped=0;
            var files=DailyCacheStorage.Files(directory,token,ref skipped);
            var measured=files.Where(f=>counted.Add(f.Path)).ToArray();
            var included=measured.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long cache=0,log=0,reclaim=0;int optimizable=0;
            try
            {
                if(ReadBox(directory) is {} box && Directory.Exists(box.Data))
                {
                    var s=DailyCacheStorage.ScanIsolated(box.Data,token,files);var l=DailyLogCleanup.Scan(box.Data,policy,DateTime.UtcNow,token);
                    cache=s.CacheFiles.Where(f=>included.Contains(f.Path)).Sum(f=>f.DiskBytes);log=l.ManagedBytes;skipped+=s.Skipped+l.Skipped;
                    string dumpFolder=Path.Combine(Path.GetDirectoryName(box.Data)!,"CrashDumps");
                    log+=files.Where(f=>Path.GetDirectoryName(f.Path)==dumpFolder&&DailyCrashStorage.Own(f.Path)&&included.Contains(f.Path)).Sum(f=>f.DiskBytes);
                    if(CanClean(box))
                    {
                        reclaim=s.Units.SelectMany(u=>u.Files).Where(f=>included.Contains(f.Path)).Sum(f=>f.DiskBytes)
                            +l.Units.SelectMany(u=>u.Files).Where(f=>included.Contains(f.Path)).Sum(f=>f.DiskBytes)
                            +DailyCrashStorage.Plan(dumpFolder,files,DateTime.UtcNow,policy.Days).SelectMany(u=>u.Files).Where(f=>included.Contains(f.Path)).Sum(f=>f.DiskBytes);
                        optimizable=DailyStorageCompression.Plan(box.Data,files.Where(f=>included.Contains(f.Path)),DateTime.UtcNow).Length;
                    }
                }
            }
            catch(Exception e) when(Expected(e)){skipped++;}
            scopes.Add(new("sandbox",directory,measured.Sum(f=>f.DiskBytes),cache,log,reclaim,skipped){OptimizableFiles=optimizable});
        }
        bool canExtract=extractionIdle();
        foreach(string folder in ExtractionDirectories())
        {
            int skipped=0;var files=DailyCacheStorage.Files(folder,token,ref skipped).ToArray();
            var measured=files.Where(f=>counted.Add(f.Path)).ToArray();var included=measured.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            scopes.Add(new("extraction",folder,measured.Sum(f=>f.DiskBytes),measured.Sum(f=>f.DiskBytes),0,
                canExtract?ExtractionPlan(folder,files,DateTime.UtcNow).Where(u=>extractionSafe(u.Path)).Sum(u=>u.Files.Where(f=>included.Contains(f.Path)).Sum(f=>f.DiskBytes)):0,skipped));
        }
        if(crashes!=null)
        {
            int skipped=0;var files=DailyCacheStorage.Files(crashes,token,ref skipped).Where(f=>Path.GetDirectoryName(f.Path)==crashes&&DailyCrashStorage.Own(f.Path)&&counted.Add(f.Path)).ToArray();
            long bytes=files.Sum(f=>f.DiskBytes);
            scopes.Add(new("crashes",crashes,bytes,0,bytes,DailyCrashStorage.Plan(crashes,files,DateTime.UtcNow,policy.Days).SelectMany(u=>u.Files).Sum(f=>f.DiskBytes),skipped));
        }
        return new DailyStorageOverview(scopes.ToArray());
    },token);
    static DailyStorageItem[] CompressionPlan(string root,IEnumerable<DailyStorageItem> files,bool fixedRoot=false) =>
        (fixedRoot?new[]{root}:new[]{root,DailyCacheStorage.Location(root)}).Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(basis=>DailyStorageCompression.Plan(basis,files,DateTime.UtcNow)).DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    public static async Task<DailyLogCleanupResult> MaintainLocalAsync(string root,DailyLogPolicy policy,bool fixedRoot,CancellationToken token)
    {
        DailyCacheStorage.NoLinks(root);
        using var control=DailyUpdateHost.AcquireControl(root);
        await DailyLogEvidence.FlushAsync(root,token);
        await DailyLogEvidence.MaintainAsync(root,token);
        var plan=await DailyLogCleanup.ScanAsync(root,policy,token);
        var logs=await DailyLogCleanup.ApplyAsync(plan,token);
        var cache=await DailyCacheStorage.CleanLocalAsync(root,fixedRoot,token);
        return logs.Add(cache).Add(CompressLocal(root,fixedRoot,token));
    }
    static DailyLogCleanupResult CompressLocal(string root,bool fixedRoot,CancellationToken token)
    {
        using var storage=DailyCacheStorage.Lease(root,true);
        var inventory=fixedRoot?DailyCacheStorage.ScanIsolated(root,token):DailyCacheStorage.Scan(root,token);
        return DailyStorageCompression.Apply(CompressionPlan(root,inventory.AllFiles,fixedRoot),token,root);
    }
    static DailyLogCleanupResult ContinueLocal(string root,bool fixedRoot,CancellationToken token)
    {
        DailyCacheStorage.NoLinks(root);
        using var control=DailyUpdateHost.AcquireControl(root);
        return CompressLocal(root,fixedRoot,token);
    }
    public Task<DailyLogCleanupResult> CleanAsync(DailyLogPolicy policy,CancellationToken token=default)=>Task.Run(()=>CleanCoreAsync(policy,token),token);
    // Continuations never repeat journal reconciliation, log retention or update cleanup.
    public Task<DailyLogCleanupResult> ContinueCompressionAsync(CancellationToken token=default)=>Task.Run(()=>CleanCoreAsync(new(),token,true),token);
    async Task<DailyLogCleanupResult> CleanCoreAsync(DailyLogPolicy policy,CancellationToken token,bool compressionOnly=false)
    {
        var result=compressionOnly?ContinueLocal(root,false,token):await MaintainLocalAsync(root,policy,false,token);
        void Add(DailyLogCleanupResult r)=>result=result.Add(r);
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
                    Add(compressionOnly?ContinueLocal(box.Data,true,token):await MaintainLocalAsync(box.Data,policy,true,token));
                    if(!compressionOnly)Add(CleanDumps(Path.Combine(Path.GetDirectoryName(box.Data)!,"CrashDumps"),policy,token,()=>CanClean(box)));
                }
                catch(Exception e) when(Expected(e)){Add(new(0,0,1));}
            }
        }
        finally{DailySandbox.ConfigurationGate.Release();}
        if(compressionOnly)return result;
        if(crashes!=null)Add(CleanDumps(crashes,policy,token));
        foreach(string folder in ExtractionDirectories())
        {
            token.ThrowIfCancellationRequested();
            int skipped=0;var files=DailyCacheStorage.Files(folder,token,ref skipped).ToArray();
            var cleaned=DailyCacheStorage.DeleteUnits(ExtractionPlan(folder,files,DateTime.UtcNow).Where(u=>extractionSafe(u.Path)).ToArray(),token,extractionIdle);
            Add(cleaned with {Skipped=cleaned.Skipped+skipped});
        }
        return result;
    }
    static DailyLogCleanupResult CleanDumps(string folder,DailyLogPolicy policy,CancellationToken token,Func<bool>? available=null)
    {
        int skipped=0;var files=DailyCacheStorage.Files(folder,token,ref skipped);
        var result=DailyCacheStorage.DeleteUnits(DailyCrashStorage.Plan(folder,files,DateTime.UtcNow,policy.Days),token,available);
        return result with{Skipped=result.Skipped+skipped};
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

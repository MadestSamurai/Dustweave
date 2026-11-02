using System.Security.Cryptography;
using Dustweave.Accounts;
namespace Dustweave;

// The transport is injectable: regression tests never touch Sandboxie or real accounts.
internal interface IStorageSandboxTransport
{
    string[] Boxes();
    string Read(string box,string setting);
    bool Idle(string box);
    Task Set(string box,string setting,string value);
}
internal sealed class StorageSandboxTransport : IStorageSandboxTransport
{
    string Installation => DailySandbox.Installation() ?? throw new IOException("parallel.install_required");
    public string[] Boxes()=>DailySandbox.Installation() is {} installation?SandboxConfiguration.Boxes(installation):[];
    public string Read(string box,string setting)=>SandboxConfiguration.Read(Installation,box,setting);
    public bool Idle(string box)=>SandboxConfiguration.Processes(Installation,box).Length==0;
    public async Task Set(string box,string setting,string value)
    {
        await DailySandbox.Run(Installation,"SbieIni.exe",["set",box,setting,value],CancellationToken.None);
        if(!Read(box,setting).Trim().Equals(value,StringComparison.OrdinalIgnoreCase))throw new IOException("storage.binding_failed");
    }
}

public sealed class DailyStorageBindingException(string directory,string label,string reason) : IOException("storage.sandbox_unowned")
{
    public string DirectoryPath { get; } = directory;
    public string Label { get; } = label;
    public string Reason { get; } = reason;
}

public sealed class DailyStorageMigration
{
    internal sealed record Box(string Name,string Source,string Target,string Enabled);
    internal sealed record Tree(string Source,string Target);
    internal sealed record Copied(string Source,string Target,long Length,DateTime Written,string Hash);
    internal sealed record Journal(string Id,string Phase,string Destination,Tree[] Trees,Box[] Boxes,Copied[] Files);
    readonly string root,legacyStore;
    readonly IStorageSandboxTransport sandbox;
    internal Action<string>? Checkpoint { get; init; }
    string Pending => Path.Combine(root,"storage-migration.json");
    public DailyStorageMigration(string root) : this(root,DailyStorageLocations.LegacySandboxes,new StorageSandboxTransport()) { }
    internal DailyStorageMigration(string root,string legacyStore,IStorageSandboxTransport sandbox)
    {this.root=DailyStoragePath.ResolveRoot(root);this.legacyStore=DailyStoragePath.ResolveRoot(legacyStore);this.sandbox=sandbox;}
    static bool Same(string a,string b)=>SandboxConfiguration.SameRoot(a,b);
    static bool Within(string a,string b)=>Same(a,b)||Path.GetFullPath(a).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    static void NoLinks(string path)=>DailyCacheStorage.NoLinks(path);
    static DailyStorageLocation Location(Journal j)=>new(j.Destination,Path.Combine(j.Destination,"instances"),j.Id);
    void Save(Journal j){DailyJson.Write(Pending,j);Checkpoint?.Invoke(j.Phase);}
    // Old per-account mode preferences (and a removed sandbox's remaining notes)
    // can create a state directory without ever creating an isolated game root.
    // Preserve those bytes during the move; they are not proof of a registered box.
    internal static bool MetadataOnly(string state)
    {
        string name=Path.GetFileName(state),gameRoot=Path.Combine(state,"root");
        if(name.Length!=30 || !name.StartsWith("Dustweave_",StringComparison.Ordinal) || !name[10..].All(Uri.IsHexDigit)
            || File.Exists(Path.Combine(state,"instance.json")) || File.Exists(Path.Combine(state,"removal.json")) || File.Exists(gameRoot))return false;
        NoLinks(state);NoLinks(gameRoot);
        string recovery=Path.GetDirectoryName(DailyLoadingFileTransaction.Pending(gameRoot))!;
        foreach(string directory in Directory.GetDirectories(state))
        {
            NoLinks(directory);
            if(Same(directory,gameRoot)&&!Directory.EnumerateFileSystemEntries(directory).Any())continue;
            // Even a no-op recovery check leaves this empty lock behind. A
            // pending journal, backup or other content must still be resolved.
            if(!Same(directory,recovery))return false;
            foreach(string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                NoLinks(entry);
                if(!Same(entry,Path.Combine(recovery,"transaction.lock"))||!File.Exists(entry)||new FileInfo(entry).Length!=0)return false;
            }
        }
        return true;
    }
    DailyStorageBindingException BindingError(string state,string reason,DailySandboxRequest? owner=null,string? actual=null,string? alias=null)
    {
        string label=string.IsNullOrWhiteSpace(owner?.Name)?Path.GetFileName(state):owner.Name;
        try { DailyJson.Write(Path.Combine(root,"live","diagnostics","storage-migration-check.json"),new {
            atUtc=DateTimeOffset.UtcNow,reason,label,stateDirectory=state,box=owner?.Box,
            expectedRoot=Path.Combine(state,"root"),actualRoot=actual,conflictingBox=alias,changed=false }); }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
        return new(state,label,reason);
    }
    void RequireIdle(IEnumerable<Box> boxes)
    {foreach(var box in boxes)if(!sandbox.Idle(box.Name))throw new IOException("storage.sandbox_busy");}

    // Call under the host single-instance/control locks. Interrupted copying or
    // rebinding rolls back; a published routing record always rolls forward.
    public async Task RecoverAsync(CancellationToken token=default)
    {
        if(!File.Exists(Pending))return;
        using var control=DailyUpdateHost.AcquireControl(root);
        await DailySandbox.ConfigurationGate.WaitAsync(token);
        try {using var lease=DailyCacheStorage.Lease(root,true);await Task.Run(RecoverCoreAsync,token);}
        finally{DailySandbox.ConfigurationGate.Release();}
    }
    async Task RecoverCoreAsync()
    {
        if(!File.Exists(Pending))return;
        var j=DailyJson.TryRead<Journal>(Pending)??throw new IOException("storage.recovery_required");
        Validate(j);
        var active=DailyStorageLocations.Read(root);
        if(active?.Id==j.Id)await FinishAsync(j);
        else await RollbackAsync(j);
    }
    void Validate(Journal j)
    {
        if(!Guid.TryParseExact(j.Id,"N",out _) || !Path.IsPathFullyQualified(j.Destination)
            || j.Phase is not ("copying" or "switching" or "committed") || j.Trees==null || j.Boxes==null || j.Files==null)
            throw new IOException("storage.recovery_required");
        NoLinks(j.Destination);
        if(!DailyStorageLocations.Matches(DailyJson.TryRead<DailyStorageLocation>(Path.Combine(j.Destination,"storage-owner.json")),Location(j)))throw new IOException("storage.recovery_required");
        foreach(var tree in j.Trees)
        {
            NoLinks(tree.Source);NoLinks(tree.Target);
            if(!Within(tree.Target,j.Destination)||Same(tree.Target,j.Destination)||Within(tree.Source,j.Destination)||Within(j.Destination,tree.Source))throw new IOException("storage.recovery_required");
        }
        foreach(var file in j.Files)
            if(!j.Trees.Any(t=>Within(file.Source,t.Source)&&Same(file.Target,Path.Combine(t.Target,Path.GetRelativePath(t.Source,file.Source)))))throw new IOException("storage.recovery_required");
        foreach(var box in j.Boxes)
            if(!DailySandbox.ValidBoxName(box.Name)||!j.Trees.Any(t=>Within(box.Source,t.Source)&&Same(box.Target,Path.Combine(t.Target,Path.GetRelativePath(t.Source,box.Source)))))throw new IOException("storage.recovery_required");
    }
    async Task RollbackAsync(Journal j)
    {
        // Check every binding before changing any; never overwrite an unrelated manual edit.
        RequireIdle(j.Boxes);
        foreach(var b in j.Boxes)if(!Same(sandbox.Read(b.Name,"FileRootPath"),b.Source)&&!Same(sandbox.Read(b.Name,"FileRootPath"),b.Target))throw new IOException("storage.recovery_required");
        foreach(var b in j.Boxes)
        {
            if(!Same(sandbox.Read(b.Name,"FileRootPath"),b.Source))await sandbox.Set(b.Name,"FileRootPath",b.Source);
            await sandbox.Set(b.Name,"Enabled",b.Enabled);
        }
        // The target is exclusively ours, marked before journaling and never launched.
        // Login is already restored. A failure deleting the disposable partial
        // copy must not leave a recovery journal that blocks the next startup.
        File.Delete(Pending);
        try{DeleteOwnedTarget(j);}catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {DailyJson.Write(Path.Combine(root,"storage-migration-last.json"),new {j.Id,atUtc=DateTimeOffset.UtcNow,j.Destination,rolledBack=true,partialCopyRetained=true});}
    }
    static void DeleteOwnedTarget(Journal j)
    {
        // No recursive deletion across links, even for an unfinished copy.
        void Delete(string directory)
        {
            NoLinks(directory);
            foreach(var file in Directory.GetFiles(directory)) {NoLinks(file);File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly);File.Delete(file);}
            foreach(var child in Directory.GetDirectories(directory))Delete(child);
            Directory.Delete(directory);
        }
        if(Directory.Exists(j.Destination))Delete(j.Destination);
    }
    async Task<int> FinishAsync(Journal j)
    {
        foreach(var b in j.Boxes)if(!Same(sandbox.Read(b.Name,"FileRootPath"),b.Target))throw new IOException("storage.recovery_required");
        foreach(var b in j.Boxes)if(!sandbox.Read(b.Name,"Enabled").Trim().Equals(b.Enabled,StringComparison.OrdinalIgnoreCase))await sandbox.Set(b.Name,"Enabled",b.Enabled);
        int retained=0;
        foreach(var f in j.Files)
        {
            try
            {
                if(!File.Exists(f.Source))continue;
                NoLinks(f.Source);NoLinks(f.Target);
                var info=new FileInfo(f.Source);
                if(info.Length!=f.Length || info.LastWriteTimeUtc!=f.Written || Hash(f.Source)!=f.Hash || !File.Exists(f.Target)||Hash(f.Target)!=f.Hash){retained++;continue;}
                File.SetAttributes(f.Source,info.Attributes&~FileAttributes.ReadOnly);File.Delete(f.Source);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){retained++;}
        }
        foreach(var tree in j.Trees)try{RemoveEmpty(tree.Source);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
        DailyJson.Write(Path.Combine(root,"storage-migration-last.json"),new {j.Id,atUtc=DateTimeOffset.UtcNow,j.Destination,retained,sourceTrees=j.Trees.Select(t=>t.Source).ToArray()});
        File.Delete(Pending);
        return retained;
    }
    static void RemoveEmpty(string directory)
    {
        if(!Directory.Exists(directory))return;
        NoLinks(directory);foreach(string child in Directory.GetDirectories(directory))RemoveEmpty(child);
        if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
    }

    public async Task<DailyCacheMove> MoveAsync(string destination,CancellationToken token=default,IProgress<long>? progress=null)
    {
        DailySandbox.RequireHost();
        using var control=DailyUpdateHost.AcquireControl(root);
        await DailySandbox.ConfigurationGate.WaitAsync(token);
        try
        {
            using var lease=DailyCacheStorage.Lease(root,true);
            return await Task.Run(()=>MoveCoreAsync(destination,token,progress),token);
        }
        finally{DailySandbox.ConfigurationGate.Release();}
    }
    async Task<DailyCacheMove> MoveCoreAsync(string destination,CancellationToken token,IProgress<long>? progress)
    {
        await RecoverCoreAsync();
        destination=DailyStoragePath.ResolveRoot(destination);NoLinks(destination);
        string cache=DailyCacheStorage.Location(root),store=DailyStorageLocations.Read(root)?.Sandboxes??legacyStore;
        foreach(string previous in new[]{root,cache,store,Path.Combine(Path.GetTempPath(),".net")}.Concat(DailyCacheStorage.KnownRoots(root)).Concat(DailyStorageLocations.Read(root)?.PreviousSandboxes??[]))
            if(Within(destination,previous)||Within(previous,destination))throw new IOException("storage.overlap");
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("storage.exists");
        var trees=DailyCacheStorage.Areas.Select(a=>new Tree(Path.Combine(cache,a),Path.Combine(destination,a)))
            .Append(new Tree(store,Path.Combine(destination,"instances"))).Where(t=>Directory.Exists(t.Source)).ToArray();
        var boxes=new List<Box>();
        if(Directory.Exists(store))
        {
            var registered=sandbox.Boxes();
            var configured=registered.ToDictionary(b=>b,b=>sandbox.Read(b,"FileRootPath").Trim(),StringComparer.OrdinalIgnoreCase);
            foreach(string state in Directory.GetDirectories(store))
            {
                NoLinks(state);
                if(!Directory.EnumerateFileSystemEntries(state).Any())continue;
                string from=Path.Combine(state,"root"),marker=Path.Combine(state,"instance.json");
                if(File.Exists(Path.Combine(state,"removal.json"))||File.Exists(DailyLoadingFileTransaction.Pending(from)))throw new IOException("storage.recovery_required");
                var owner=DailyJson.TryRead<DailySandboxRequest>(marker);
                if(owner==null&&MetadataOnly(state)&&!configured.ContainsKey(Path.GetFileName(state))&&!configured.Values.Any(p=>Same(p,from)))continue;
                if(owner==null)throw BindingError(state,File.Exists(marker)?"registration_invalid":"registration_missing");
                if(!DailyProfiles.ValidKey(owner.Account)||!DailySandbox.ValidBoxName(owner.Box)
                    ||!Path.GetFileName(state).Equals(DailySandbox.CanonicalBoxName(owner.Account),StringComparison.OrdinalIgnoreCase))throw BindingError(state,"registration_invalid",owner);
                if(!configured.TryGetValue(owner.Box,out string? actual))throw BindingError(state,"not_registered",owner);
                if(!Same(actual,from))throw BindingError(state,"path_mismatch",owner,actual);
                string? alias=registered.FirstOrDefault(b=>!b.Equals(owner.Box,StringComparison.OrdinalIgnoreCase)&&Same(configured[b],from));
                if(alias!=null)throw BindingError(state,"duplicate_root",owner,actual,alias);
                boxes.Add(new(owner.Box,from,Path.Combine(destination,"instances",Path.GetFileName(state),"root"),sandbox.Read(owner.Box,"Enabled").Trim()));
            }
        }
        RequireIdle(boxes);
        int skipped=0;
        var inventory=trees.SelectMany(t=>DailyCacheStorage.Files(t.Source,token,ref skipped).Select(f=>(Tree:t,File:f))).ToArray();
        if(skipped!=0)throw new IOException("storage.unreadable");
        var drive=new DriveInfo(Path.GetPathRoot(destination)!);
        if(drive.IsReady&&drive.AvailableFreeSpace<inventory.Sum(x=>x.File.Bytes)+32*1024*1024)throw new IOException("storage.space");
        token.ThrowIfCancellationRequested();
        var journal=new Journal(Guid.NewGuid().ToString("N"),"copying",destination,trees,boxes.ToArray(),[]);
        Directory.CreateDirectory(destination);DailyJson.Write(Path.Combine(destination,"storage-owner.json"),Location(journal));
        Save(journal);
        long copied=0,lastProgress=0;
        try
        {
            // Prevent new launches while the registry hive and writable game files are copied.
            foreach(var b in boxes){await sandbox.Set(b.Name,"Enabled","n");RequireIdle(boxes);}
            foreach(var tree in trees)CopyDirectories(tree.Source,tree.Target,token);
            Directory.CreateDirectory(Path.Combine(destination,"instances"));
            var files=new List<Copied>();
            var buffer=new byte[256*1024];
            foreach(var (tree,file) in inventory)
            {
                token.ThrowIfCancellationRequested();NoLinks(file.Path);
                string target=Path.Combine(tree.Target,Path.GetRelativePath(tree.Source,file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using(var input=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                {
                    using var hasher=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);int count;
                    while((count=input.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,count);hasher.AppendData(buffer,0,count);copied+=count;if(Environment.TickCount64-lastProgress>100){progress?.Report(copied);lastProgress=Environment.TickCount64;}}
                    output.Flush(true);output.Position=0;
                    string hash=Convert.ToHexString(hasher.GetHashAndReset());
                    if(hash!=Convert.ToHexString(SHA256.HashData(output)))throw new IOException("storage.changed");
                    files.Add(new(file.Path,target,file.Bytes,file.Written,hash));
                }
                File.SetLastWriteTimeUtc(target,file.Written);
            }
            RequireIdle(boxes);
            int ignored=0;
            if(trees.Sum(t=>DailyCacheStorage.Files(t.Source,token,ref ignored).Count)!=files.Count||ignored!=0)throw new IOException("storage.changed");
            foreach(var f in files)if(new FileInfo(f.Source).Length!=f.Length||File.GetLastWriteTimeUtc(f.Source)!=f.Written||Hash(f.Source)!=f.Hash)throw new IOException("storage.changed");
            token.ThrowIfCancellationRequested();
            journal=journal with {Phase="switching",Files=files.ToArray()};Save(journal);
            foreach(var b in boxes){RequireIdle(boxes);await sandbox.Set(b.Name,"FileRootPath",b.Target);Checkpoint?.Invoke("binding");}
            // One atomic routing publication is the commit point. Recovery reads it,
            // not an in-memory flag, including an interrupted File.Replace.
            var previous=DailyStorageLocations.Read(root);
            DailyJson.Write(DailyStorageLocations.FileName(root),Location(journal) with {
                PreviousCaches=new[]{cache}.Concat(previous?.PreviousCaches??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                PreviousSandboxes=new[]{store}.Concat(previous?.PreviousSandboxes??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });Checkpoint?.Invoke("published");
            journal=journal with {Phase="committed"};Save(journal);
            int retained=await FinishAsync(journal);progress?.Report(copied);
            return new(destination,copied,retained);
        }
        catch
        {
            // Cancellation is honored during copying, never halfway through rollback.
            if(DailyStorageLocations.Read(root)?.Id!=journal.Id)await RollbackAsync(journal);
            throw;
        }
    }
    static void CopyDirectories(string source,string target,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();NoLinks(source);Directory.CreateDirectory(target);
        foreach(string child in Directory.GetDirectories(source))CopyDirectories(child,Path.Combine(target,Path.GetFileName(child)),token);
    }
}

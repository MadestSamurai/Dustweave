using Dustweave.Accounts;
namespace Dustweave;

// User-approved recovery into a new directory. Salvage is additive: missing
// inputs do not block intact accounts, and original remnants are never deleted.
public sealed partial class DailyStorageMigration
{
    public sealed record RepairPreview(string Directory,string SandboxDirectory,int Preserved,int Rebuild,int Unresolved,long Bytes)
    { public string Signature { get; init; } = ""; }
    internal sealed record SandboxRebuild(string Account,int Slot,string Name,string Box,string Root);
    sealed record RepairPlan(DailyStorageLocation Before,RepairPreview Preview,Tree[] Trees,Box[] Boxes,SandboxRebuild[] Rebuilds,int Skipped);
    internal const string RebuildFile="storage-rebuild.json";

    // Inspect metadata near the old directory, not all files on every disk.
    IEnumerable<string> Nearby(DailyStorageLocation location,CancellationToken token)
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue=new Queue<(string Path,int Depth)>();
        foreach(string? path in new[]{Path.GetDirectoryName(location.Cache),Path.GetDirectoryName(Path.GetDirectoryName(location.Cache))})
            if(path!=null&&!Same(Path.GetPathRoot(path)!,path))queue.Enqueue((path,0));
        var watch=System.Diagnostics.Stopwatch.StartNew();int count=0;
        while(queue.TryDequeue(out var item)&&count<512&&watch.Elapsed.TotalSeconds<3)
        {
            token.ThrowIfCancellationRequested();if(!seen.Add(item.Path))continue;count++;
            string[] children=[];bool candidate=false;
            try
            {
                NoLinks(item.Path);if(!Directory.Exists(item.Path))continue;
                string marker=Path.Combine(item.Path,"storage-owner.json");NoLinks(marker);
                candidate=File.Exists(marker)&&new FileInfo(marker).Length<=65536
                    &&DailyStorageLocations.Matches(DailyJson.TryRead<DailyStorageLocation>(marker),location);
                if(!candidate&&item.Depth<2)children=Directory.EnumerateDirectories(item.Path).Take(128).ToArray();
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){continue;}
            if(candidate)yield return item.Path;
            foreach(string child in children)queue.Enqueue((child,item.Depth+1));
        }
    }
    static bool HasContents(string path)
    {
        // Access denied is not evidence that login data was lost.
        try {return (File.GetAttributes(path)&FileAttributes.Directory)!=0?Directory.EnumerateFileSystemEntries(path).Any():true;}
        catch(FileNotFoundException){return false;}
        catch(DirectoryNotFoundException){return false;}
    }
    public Task<RepairPreview> InspectRepairAsync(string destination,IReadOnlyList<DailyAccount> accounts,CancellationToken token=default)
        =>Task.Run(()=>PlanRepair(destination,accounts,token).Preview,token);

    RepairPlan PlanRepair(string destination,IReadOnlyList<DailyAccount> accounts,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(File.Exists(Pending)||File.Exists(RebindPending))throw new IOException("storage.rebind.pending");
        var before=DailyStorageLocations.ReadConfigured(root)??throw new IOException("storage.rebind.not_configured");
        destination=DailyStoragePath.ResolveRoot(destination);NoLinks(destination);
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("storage.exists");
        if(!new DriveInfo(Path.GetPathRoot(destination)!).IsReady)throw new IOException("storage.repair.drive_unavailable");
        var cacheRoots=new[]{before.Cache}.Concat(Nearby(before,token)).Concat(before.PreviousCaches).Append(root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stores=new[]{before.Sandboxes}.Concat(cacheRoots.Where(p=>!Same(p,root)).Select(p=>Path.Combine(p,"instances")))
            .Concat(before.PreviousSandboxes).Append(legacyStore).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(string source in cacheRoots.Concat(stores).Append(root))
            if(Within(destination,source)||Within(source,destination))throw new IOException("storage.overlap");
        var trees=new List<Tree>();var boxes=new List<Box>();var rebuilds=new List<SandboxRebuild>();
        int kept=0,missing=0,unresolved=0;
        var registered=sandbox.Boxes();
        var bindings=registered.ToDictionary(b=>b,b=>sandbox.Read(b,"FileRootPath").Trim(),StringComparer.OrdinalIgnoreCase);
        RequireIdle(bindings.Where(p=>Path.IsPathFullyQualified(p.Value)&&stores.Any(s=>Within(p.Value,s)))
            .Select(p=>new Box(p.Key,p.Value,p.Value,sandbox.Read(p.Key,"Enabled"))).ToArray());
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string area in DailyCacheStorage.Areas)
        {
            foreach(string basis in cacheRoots)
            {
                string source=Path.Combine(basis,area);try{NoLinks(source);if(!Directory.Exists(source))continue;}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException){unresolved++;continue;}
                trees.Add(new(source,Path.Combine(destination,area)));break;
            }
        }
        foreach(var account in accounts.Where(a=>a.Valid&&DailyProfiles.ValidKey(a.AccountKey)&&a.SlotNumber is >=1 and <=100).DistinctBy(a=>a.AccountKey))
        {
            token.ThrowIfCancellationRequested();string canonical=DailySandbox.CanonicalBoxName(account.AccountKey);
            string targetState=Path.Combine(destination,"instances",canonical),target=Path.Combine(targetState,"root");
            var candidates=new List<(string State,DailySandboxRequest Owner)>();
            foreach(string store in stores)
            {
                string state=Path.Combine(store,canonical),marker=Path.Combine(state,"instance.json");
                try
                {
                    NoLinks(state);NoLinks(marker);NoLinks(Path.Combine(state,"root"));
                    var owner=DailyJson.TryRead<DailySandboxRequest>(marker);
                    if(owner?.Account==account.AccountKey&&DailySandbox.ValidBoxName(owner.Box)&&!File.Exists(Path.Combine(state,"removal.json")))candidates.Add((state,owner));
                }
                catch(Exception e)when(e is IOException or UnauthorizedAccessException){unresolved++;}
            }
            // The actually configured readable instance wins over historical copies.
            var live=candidates.Where(c=>bindings.TryGetValue(c.Owner.Box,out string? actual)&&Same(actual,Path.Combine(c.State,"root"))).ToArray();
            var chosen=live.Length==1?live[0]:candidates.Count==1?candidates[0]:default;
            string box=chosen.Owner?.Box??canonical;
            string? old=bindings.GetValueOrDefault(box);
            if(chosen.Owner==null)
            {
                var aliases=bindings.Where(p=>stores.Any(s=>Same(p.Value,Path.Combine(s,canonical,"root")))).ToArray();
                if(aliases.Length==1){box=aliases[0].Key;old=aliases[0].Value;}
            }
            // Never seize a same-named third-party box. Configure a fresh alias
            // later through the ordinary account launch path instead.
            if(old!=null&&!stores.Any(s=>Same(old,Path.Combine(s,canonical,"root"))))
            {
                chosen=default;unresolved++;int suffix=0;
                do{box=canonical+"R"+(++suffix);}while(bindings.ContainsKey(box)&&suffix<9);
                if(bindings.ContainsKey(box)||!DailySandbox.ValidBoxName(box))throw new IOException("storage.repair.alias");
                old=null;
            }
            if(chosen.Owner!=null)
            {
                trees.Add(new(chosen.State,targetState));used.Add(chosen.State);
                if(HasContents(Path.Combine(chosen.State,"root")))kept++;else missing++;
            }
            else
            {
                missing++;rebuilds.Add(new(account.AccountKey,account.SlotNumber,account.Name,box,target));
            }
            if(old!=null)
            {
                var aliases=bindings.Where(p=>Same(p.Value,old)).ToArray();
                if(aliases.Length!=1)throw new IOException("storage.repair.alias");
                boxes.Add(new(box,old,target,sandbox.Read(box,"Enabled").Trim()));
                // Include the old registration mapping for rollback even when
                // the corresponding directory has already disappeared.
                if(!trees.Any(t=>Same(t.Source,Path.GetDirectoryName(old)!)&&Same(t.Target,targetState)))
                    trees.Add(new(Path.GetDirectoryName(old)!,targetState){RecoveryMappingOnly=true});
            }
        }
        // Preserve unclaimed/ambiguous remnants separately, not as active logins.
        int archive=0;
        foreach(string store in stores)
        {
            try
            {
                NoLinks(store);if(!Directory.Exists(store))continue;
                foreach(string state in Directory.EnumerateDirectories(store))
                {
                    if(used.Contains(state)||!HasContents(state))continue;
                    NoLinks(state);trees.Add(new(state,Path.Combine(destination,"recovered",(++archive).ToString(),Path.GetFileName(state))));unresolved++;
                }
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){unresolved++;}
        }
        RequireIdle(boxes);
        int skipped=0;long bytes=trees.Where(t=>!t.RecoveryMappingOnly).Sum(t=>DailyCacheStorage.Files(t.Source,token,ref skipped).Sum(f=>f.Bytes));
        unresolved+=skipped;
        string signature=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{before,trees,boxes,rebuilds})));
        return new(before,new(destination,Path.Combine(destination,"instances"),kept,missing,unresolved,bytes){Signature=signature},trees.ToArray(),boxes.ToArray(),rebuilds.ToArray(),skipped);
    }

    public async Task<RepairPreview> RepairAsync(string destination,IReadOnlyList<DailyAccount> accounts,RepairPreview approved,CancellationToken token=default,IProgress<MoveProgress>? phases=null)
    {
        DailySandbox.RequireHost();Context("repair-lock",root,destination);
        try
        {
            using var control=DailyUpdateHost.AcquireControl(root);
            await DailySandbox.ConfigurationGate.WaitAsync(token);
            try
            {
                using var lease=DailyCacheStorage.Lease(root,true);
                return await Task.Run(async ()=>
                {
                    await RecoverCoreAsync();
                    var plan=PlanRepair(destination,accounts,token);
                    if(plan.Preview!=approved)throw new IOException("storage.repair.changed");
                    var drive=new DriveInfo(Path.GetPathRoot(plan.Preview.Directory)!);
                    if(drive.AvailableFreeSpace<plan.Preview.Bytes+32*1024*1024)throw new IOException("storage.space");
                    var j=new Journal(Guid.NewGuid().ToString("N"),"copying",plan.Preview.Directory,plan.Trees,plan.Boxes,[]){PreserveSources=true};
                    token.ThrowIfCancellationRequested();Directory.CreateDirectory(j.Destination);DailyJson.Write(Path.Combine(j.Destination,"storage-owner.json"),Location(j));Save(j);
                    try
                    {
                        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"Enabled","n");RequireIdle(j.Boxes);
                        var report=new Reporter(phases);report.Start(MoveStage.Copying,plan.Preview.Bytes*2);
                        var files=new List<Copied>();var written=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        int skipped=0;
                        foreach(var tree in j.Trees.Where(t=>!t.RecoveryMappingOnly))
                        {
                            token.ThrowIfCancellationRequested();
                            foreach(var file in DailyCacheStorage.Files(tree.Source,token,ref skipped))
                            {
                                string target=Path.Combine(tree.Target,Path.GetRelativePath(tree.Source,file.Path));
                                if(!written.Add(target))continue;
                                NoLinks(file.Path);NoLinks(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                using var input=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
                                using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                                {
                                    var buffer=new byte[256*1024];int n;
                                    while((n=input.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);report.Add(n);}
                                    output.Flush(true);input.Position=0;output.Position=0;
                                    string hash=HashWithProgress(output,report,token);
                                    if(hash!=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)))throw new IOException("storage.changed");
                                    files.Add(new(file.Path,target,file.Bytes,file.Written,hash));
                                }
                                File.SetLastWriteTimeUtc(target,file.Written);
                                Checkpoint?.Invoke("repair-file");
                            }
                        }
                        Directory.CreateDirectory(plan.Preview.SandboxDirectory);
                        foreach(var rebuild in plan.Rebuilds)
                        {
                            string state=Path.GetDirectoryName(rebuild.Root)!;
                            DailyJson.Write(Path.Combine(state,RebuildFile),rebuild);Directory.CreateDirectory(rebuild.Root);
                        }
                        foreach(var b in j.Boxes)Directory.CreateDirectory(b.Target);
                        token.ThrowIfCancellationRequested();RequireIdle(j.Boxes);
                        j=j with{Phase="switching",Files=files.ToArray()};Save(j);report.Start(MoveStage.Switching);
                        foreach(var b in j.Boxes){await sandbox.Set(b.Name,"FileRootPath",b.Target);Checkpoint?.Invoke("binding");}
                        DailyJson.Write(DailyStorageLocations.FileName(root),Location(j) with{
                            PreviousCaches=new[]{plan.Before.Cache}.Concat(plan.Before.PreviousCaches).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            PreviousSandboxes=new[]{plan.Before.Sandboxes}.Concat(plan.Before.PreviousSandboxes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()});Checkpoint?.Invoke("published");
                        await FinishAsync(j);report.Start(MoveStage.Finishing);
                        var result=plan.Preview with{Unresolved=plan.Preview.Unresolved+Math.Max(0,skipped-plan.Skipped)};
                        DailyJson.Write(Path.Combine(root,"storage-repair-last.json"),new{atUtc=DateTimeOffset.UtcNow,result,copiedFiles=files.Count,originalsPreserved=true,credentialsRestored=false});
                        return result;
                    }
                    catch {await RecoverCoreAsync();throw;}
                },token);
            }
            finally {DailySandbox.ConfigurationGate.Release();}
        }
        catch(Exception error){RecordFailure("repair",destination,error);throw;}
    }
    internal static SandboxRebuild? ReadRebuild(string state)
    {
        var item=DailyJson.TryRead<SandboxRebuild>(Path.Combine(state,RebuildFile));
        return item!=null&&DailyProfiles.ValidKey(item.Account)&&item.Slot is >=1 and <=100&&!string.IsNullOrWhiteSpace(item.Name)
            &&DailySandbox.ValidBoxName(item.Box)&&Same(item.Root,Path.Combine(state,"root"))
            &&Path.GetFileName(state)==DailySandbox.CanonicalBoxName(item.Account)?item:null;
    }
}

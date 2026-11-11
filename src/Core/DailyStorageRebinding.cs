namespace Dustweave;

public sealed partial class DailyStorageMigration
{
    internal sealed record RebindJournal(DailyStorageLocation Before,DailyStorageLocation After,Box[] Boxes);
    public sealed record RebindPreview(string Previous,string Selected,int Sandboxes);
    string RebindPending=>Path.Combine(root,"storage-rebind.json");
    static bool ValidLocation(DailyStorageLocation? value)=>value!=null&&Guid.TryParseExact(value.Id,"N",out _)
        &&!string.IsNullOrWhiteSpace(value.Cache)&&!string.IsNullOrWhiteSpace(value.Sandboxes)&&Path.IsPathFullyQualified(value.Cache)&&Same(value.Sandboxes,Path.Combine(value.Cache,"instances"));

    // Inspection does not write anything, enumerate game resources or load login data.
    // Recheck the whole plan while holding the same leases before applying it.
    public Task<RebindPreview> InspectRebindAsync(string directory,CancellationToken token=default)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        var j=PlanRebind(directory,token);
        return new RebindPreview(j.Before.Cache,j.After.Cache,j.Boxes.Length);
    },token);

    RebindJournal PlanRebind(string directory,CancellationToken token)
    {
        if(File.Exists(Pending)||File.Exists(RebindPending))throw new IOException("storage.rebind.pending");
        var before=DailyStorageLocations.ReadConfigured(root)??throw new IOException("storage.rebind.not_configured");
        directory=DailyStoragePath.ResolveRoot(directory);NoLinks(directory);
        if(!Directory.Exists(directory))throw new IOException("storage.rebind.missing");
        string ownerPath=Path.Combine(directory,"storage-owner.json");NoLinks(ownerPath);
        if(!File.Exists(ownerPath))throw new IOException("storage.rebind.owner_missing");
        if(new FileInfo(ownerPath).Length>65536)throw new IOException("storage.rebind.owner_mismatch");
        var owner=DailyJson.TryRead<DailyStorageLocation>(ownerPath);
        if(!ValidLocation(owner)||!DailyStorageLocations.Matches(owner,before))throw new IOException("storage.rebind.owner_mismatch");
        // Two populated locations may have diverged; this is recovery of a moved
        // directory, not a way to replace current accounts with a stale copy.
        NoLinks(before.Cache);
        if(!Same(directory,before.Cache)&&(File.Exists(before.Cache)||Directory.Exists(before.Cache)&&Directory.EnumerateFileSystemEntries(before.Cache).Any()))throw new IOException("storage.rebind.original_exists");
        if(!Same(directory,before.Cache)&&(Within(directory,before.Cache)||Within(before.Cache,directory)))throw new IOException("storage.overlap");
        if(Within(directory,root)||Within(root,directory))throw new IOException("storage.overlap");
        var after=before with {Cache=directory,Sandboxes=Path.Combine(directory,"instances")};
        NoLinks(after.Sandboxes);
        if(!Directory.Exists(after.Sandboxes))throw new IOException("storage.rebind.instances_missing");
        var registered=sandbox.Boxes();
        var configured=registered.ToDictionary(b=>b,b=>sandbox.Read(b,"FileRootPath").Trim(),StringComparer.OrdinalIgnoreCase);
        var boxes=new List<Box>();
        foreach(string state in Directory.GetDirectories(after.Sandboxes))
        {
            token.ThrowIfCancellationRequested();
            NoLinks(state);
            if(!Directory.EnumerateFileSystemEntries(state).Any())continue;
            string target=Path.Combine(state,"root"),source=Path.Combine(before.Sandboxes,Path.GetFileName(state),"root");
            NoLinks(target);NoLinks(Path.Combine(state,"instance.json"));
            if(File.Exists(Path.Combine(state,"removal.json")))throw BindingError(state,"pending-removal");
            var account=DailyJson.TryRead<DailySandboxRequest>(Path.Combine(state,"instance.json"));
            if(account==null&&MetadataOnly(state)&&!configured.ContainsKey(Path.GetFileName(state))&&!configured.Values.Any(p=>Same(p,source)||Same(p,target)))continue;
            if(account==null||!DailyProfiles.ValidKey(account.Account)||!DailySandbox.ValidBoxName(account.Box)
                ||!Path.GetFileName(state).Equals(DailySandbox.CanonicalBoxName(account.Account),StringComparison.OrdinalIgnoreCase))throw BindingError(state,"registration_invalid",account);
            if(!Directory.Exists(target))throw BindingError(state,"root_missing",account);
            if(!configured.TryGetValue(account.Box,out string? actual))throw BindingError(state,"not_registered",account);
            if(!Same(actual,source)&&!Same(actual,target))throw BindingError(state,"path_mismatch",account,actual);
            string? alias=registered.FirstOrDefault(b=>!b.Equals(account.Box,StringComparison.OrdinalIgnoreCase)&&(Same(configured[b],source)||Same(configured[b],target)));
            if(alias!=null)throw BindingError(state,"duplicate_root",account,actual,alias);
            boxes.Add(new(account.Box,actual,target,sandbox.Read(account.Box,"Enabled").Trim()));
        }
        // A partial copy must not silently drop an existing account registration.
        if(configured.Any(p=>Path.IsPathFullyQualified(p.Value)&&(Within(p.Value,before.Sandboxes)||Within(p.Value,after.Sandboxes))&&!boxes.Any(b=>b.Name.Equals(p.Key,StringComparison.OrdinalIgnoreCase))))
            throw new IOException("storage.rebind.incomplete");
        RequireIdle(boxes);
        return new(before,after,boxes.ToArray());
    }

    public async Task<RebindPreview> RebindAsync(string directory,CancellationToken token=default)
    {
        Context("rebind-lock",root,directory);
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
                    var j=PlanRebind(directory,token);
                    token.ThrowIfCancellationRequested();
                    Context("rebind",j.Before.Cache,j.After.Cache);
                    DailyJson.Write(RebindPending,j);
                    try
                    {
                        Checkpoint?.Invoke("rebind-prepared");
                        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"Enabled","n");
                        RequireIdle(j.Boxes);
                        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"FileRootPath",b.Target);
                        Checkpoint?.Invoke("rebind-bindings");
                        DailyJson.Write(Path.Combine(j.After.Cache,"storage-owner.json"),j.After);
                        Checkpoint?.Invoke("rebind-owner");
                        DailyJson.Write(DailyStorageLocations.FileName(root),j.After);
                        Checkpoint?.Invoke("rebind-published");
                        await RecoverRebindCoreAsync();
                    }
                    catch(Exception original)
                    {
                        try {await RecoverRebindCoreAsync();}
                        catch(Exception recovery){throw new IOException("storage.rebind.pending",new AggregateException(original,recovery));}
                        throw;
                    }
                    return new RebindPreview(j.Before.Cache,j.After.Cache,j.Boxes.Length);
                },token);
            }
            finally {DailySandbox.ConfigurationGate.Release();}
        }
        catch(Exception error){RecordFailure("rebind",directory,error);throw;}
    }

    async Task RecoverRebindCoreAsync()
    {
        if(!File.Exists(RebindPending))return;
        if(File.Exists(Pending))throw new IOException("storage.rebind.pending");
        var j=DailyJson.TryRead<RebindJournal>(RebindPending);
        if(j==null||!ValidLocation(j.Before)||!ValidLocation(j.After)||j.Before.Id!=j.After.Id||j.Boxes==null
            ||j.Boxes.Select(b=>b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=j.Boxes.Length)
            throw new IOException("storage.rebind.pending");
        if(Within(j.After.Cache,root)||Within(root,j.After.Cache)
            ||!Same(j.After.Cache,j.Before.Cache)&&(Within(j.After.Cache,j.Before.Cache)||Within(j.Before.Cache,j.After.Cache)))throw new IOException("storage.rebind.pending");
        NoLinks(j.After.Cache);NoLinks(Path.Combine(j.After.Cache,"storage-owner.json"));
        if(!Directory.Exists(j.After.Cache)||!Directory.Exists(j.After.Sandboxes))throw new IOException("storage.rebind.missing");
        var active=DailyStorageLocations.ReadConfigured(root);
        var owner=DailyJson.TryRead<DailyStorageLocation>(Path.Combine(j.After.Cache,"storage-owner.json"));
        if((!DailyStorageLocations.Matches(active,j.Before)&&!DailyStorageLocations.Matches(active,j.After))
            ||(!DailyStorageLocations.Matches(owner,j.Before)&&!DailyStorageLocations.Matches(owner,j.After)))throw new IOException("storage.rebind.pending");
        bool committed=DailyStorageLocations.Matches(active,j.After);
        foreach(var b in j.Boxes)
        {
            string state=Path.GetFileName(Path.GetDirectoryName(b.Target)!);
            if(!DailySandbox.ValidBoxName(b.Name)||state.Length!=30||!state.StartsWith("Dustweave_",StringComparison.Ordinal)||!state[10..].All(Uri.IsHexDigit)
                ||!Same(b.Target,Path.Combine(j.After.Sandboxes,state,"root"))
                ||(!Same(b.Source,b.Target)&&!Same(b.Source,Path.Combine(j.Before.Sandboxes,state,"root"))))throw new IOException("storage.rebind.pending");
            string actual=sandbox.Read(b.Name,"FileRootPath");
            if(!Same(actual,b.Source)&&!Same(actual,b.Target))throw new IOException("storage.rebind.pending");
        }
        // A crash after the last Enabled write may leave only the journal to
        // retire. Do not stop a game that subsequently started on the correct root.
        bool settled=DailyStorageLocations.Matches(owner,committed?j.After:j.Before)
            &&j.Boxes.All(b=>Same(sandbox.Read(b.Name,"FileRootPath"),committed?b.Target:b.Source)
                &&sandbox.Read(b.Name,"Enabled").Trim().Equals(b.Enabled,StringComparison.OrdinalIgnoreCase));
        if(!settled)
        {
        RequireIdle(j.Boxes);
        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"Enabled","n");
        RequireIdle(j.Boxes);
        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"FileRootPath",committed?b.Target:b.Source);
        DailyJson.Write(Path.Combine(j.After.Cache,"storage-owner.json"),committed?j.After:j.Before);
        foreach(var b in j.Boxes)await sandbox.Set(b.Name,"Enabled",b.Enabled);
        }
        DailyJson.Write(Path.Combine(root,"storage-rebind-last.json"),new {atUtc=DateTimeOffset.UtcNow,previous=j.Before.Cache,selected=j.After.Cache,sandboxes=j.Boxes.Length,completed=committed,rolledBack=!committed});
        File.Delete(RebindPending);
    }
}

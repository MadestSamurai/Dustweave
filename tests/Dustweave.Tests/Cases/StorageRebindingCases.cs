using Dustweave;
internal static partial class StorageMigrationCases
{
    static async Task Rebinding(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-rebind: "+label);}
        async Task<(Fixture f,string selected)> Moved(string name,int count=2)
        {
            var f=Make(output,"rebind-"+name,count);
            await Service(f).MoveAsync(f.Target);
            string selected=Path.Combine(Path.GetDirectoryName(f.Target)!,"新位置 & data");
            // Only synthetic, task-owned destinations may be moved by this fixture.
            if(!Path.GetFullPath(f.Target).StartsWith(Path.GetFullPath(output)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture escaped test output");
            Directory.Move(f.Target,selected);
            return(f,selected);
        }
        async Task Reject(Fixture f,string selected,string message)
        {
            string raw=File.ReadAllText(DailyStorageLocations.FileName(f.Root));
            var bindings=f.Driver.Values.ToDictionary(x=>x.Key,x=>x.Value["FileRootPath"]);
            try {await Service(f).RebindAsync(selected);throw new Exception("Expected "+message);}
            catch(IOException e) {Check(e.Message==message,"rejects "+message);}
            Check(File.ReadAllText(DailyStorageLocations.FileName(f.Root))==raw&&bindings.All(x=>f.Driver.Read(x.Key,"FileRootPath")==x.Value),"rejection preserves routing and registrations: "+message);
        }
        var (empty,emptyPath)=await Moved("cache-only",0);
        var preview=await Service(empty).InspectRebindAsync(emptyPath);
        Check(preview.Sandboxes==0&&preview.Previous==empty.Target&&preview.Selected==emptyPath,"cache-only preview works without Sandboxie accounts");
        Check(DailyStorageLocations.ReadConfigured(empty.Root)!.Cache==empty.Target,"preview does not change routing");
        await Service(empty).RebindAsync(emptyPath);
        Check(DailyCacheStorage.Location(empty.Root)==emptyPath&&Directory.Exists(DailyStorageLocations.Sandboxes(empty.Root)),"renamed directory restores real cache and sandbox resolvers");
        await Service(empty).RebindAsync(emptyPath);
        await Service(empty).RecoverAsync();
        Check(DailyCacheStorage.Location(empty.Root)==emptyPath,"repeated binding and startup recovery are idempotent");

        var (normal,target)=await Moved("accounts");
        normal.Driver.Values[normal.Driver.Boxes()[0]]["Enabled"]="n";
        var expectedEnabled=normal.Driver.Values.ToDictionary(p=>p.Key,p=>p.Value["Enabled"]);
        var payload=Directory.GetFiles(target,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!="storage-owner.json").ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        var accountBytes=File.ReadAllBytes(Path.Combine(normal.Root,"accounts","saved.bin"));
        var updateBytes=File.ReadAllBytes(Path.Combine(normal.Root,"updates","1.5.10-Portable","attempts","keep","backup","transaction.json"));
        var result=await Service(normal).RebindAsync(target);
        Check(result.Sandboxes==2&&normal.Driver.Boxes().All(b=>normal.Driver.Read(b,"FileRootPath").StartsWith(Path.Combine(target,"instances"),StringComparison.OrdinalIgnoreCase)),"all registered sandbox roots rebound, including custom names");
        Check(expectedEnabled.All(p=>normal.Driver.Read(p.Key,"Enabled")==p.Value),"original enabled/disabled states preserved");
        Check(payload.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value))&&Directory.GetFiles(target,"*",SearchOption.AllDirectories).Length==payload.Count+1,"game, login, settings and cached bytes stay untouched");
        Check(File.ReadAllBytes(Path.Combine(normal.Root,"accounts","saved.bin")).SequenceEqual(accountBytes)&&File.ReadAllBytes(Path.Combine(normal.Root,"updates","1.5.10-Portable","attempts","keep","backup","transaction.json")).SequenceEqual(updateBytes),"host accounts and update recovery remain untouched");
        Check(!Directory.Exists(normal.Target)&&!File.Exists(Path.Combine(normal.Root,"storage-rebind.json")),"no empty replacement or pending recovery after success");
        var settled=DailyStorageLocations.Read(normal.Root)!;
        var prior=settled with {Cache=normal.Target,Sandboxes=Path.Combine(normal.Target,"instances")};
        var finishedBoxes=normal.Driver.Boxes().Select(b=>{
            string path=normal.Driver.Read(b,"FileRootPath");
            return new DailyStorageMigration.Box(b,Path.Combine(prior.Sandboxes,Path.GetFileName(Path.GetDirectoryName(path)!),"root"),path,normal.Driver.Read(b,"Enabled"));
        }).ToArray();
        DailyJson.Write(Path.Combine(normal.Root,"storage-rebind.json"),new DailyStorageMigration.RebindJournal(prior,settled,finishedBoxes));
        normal.Driver.Running.Add(normal.Driver.Boxes()[1]);
        normal.Driver.Fail=(_,_,_)=>true;
        await Service(normal).RecoverAsync();
        Check(!File.Exists(Path.Combine(normal.Root,"storage-rebind.json")),"already completed bindings retire leftover journal without touching a running game");
        normal.Driver.Fail=null;normal.Driver.Running.Clear();

        var (bad,badPath)=await Moved("invalid");
        await Reject(bad,bad.Target,"storage.rebind.missing");
        await Reject(bad,Path.GetDirectoryName(badPath)!,"storage.rebind.owner_missing");
        string owner=Path.Combine(badPath,"storage-owner.json"),original=File.ReadAllText(owner);
        File.WriteAllText(owner,"{}");await Reject(bad,badPath,"storage.rebind.owner_mismatch");
        var configured=DailyStorageLocations.ReadConfigured(bad.Root)!;
        DailyJson.Write(owner,configured with {Id=Guid.NewGuid().ToString("N")});await Reject(bad,badPath,"storage.rebind.owner_mismatch");
        File.WriteAllText(owner,original);
        Directory.CreateDirectory(bad.Target);File.WriteAllText(Path.Combine(bad.Target,"keep.txt"),"existing data");
        await Reject(bad,badPath,"storage.rebind.original_exists");File.Delete(Path.Combine(bad.Target,"keep.txt"));
        bad.Driver.Running.Add(bad.Driver.Boxes()[0]);await Reject(bad,badPath,"storage.sandbox_busy");bad.Driver.Running.Clear();
        string box=bad.Driver.Boxes()[0],oldBinding=bad.Driver.Read(box,"FileRootPath");
        bad.Driver.Values[box]["FileRootPath"]=Path.Combine(output,"unrelated");await Reject(bad,badPath,"storage.sandbox_unowned");bad.Driver.Values[box]["FileRootPath"]=oldBinding;
        bad.Driver.Values["Alias"]=new(){{"FileRootPath",oldBinding},{"Enabled","y"}};await Reject(bad,badPath,"storage.sandbox_unowned");bad.Driver.Values.Remove("Alias");
        var stopped=new CancellationTokenSource();stopped.Cancel();
        try{await Service(bad).RebindAsync(badPath,stopped.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        Check(!File.Exists(Path.Combine(bad.Root,"storage-rebind.json")),"pre-commit cancellation leaves no transaction");
        await Service(bad).RebindAsync(badPath);
        Check(DailyStorageLocations.Read(bad.Root)!.Cache==badPath&&Directory.Exists(bad.Target),"restored conditions permit continuation; an empty old folder is preserved and does not block recovery");

        var (partial,partialPath)=await Moved("partial");
        string instances=Path.Combine(partialPath,"instances"),missing=Path.Combine(Path.GetDirectoryName(partialPath)!,"held-instances");
        Directory.Move(instances,missing);await Reject(partial,partialPath,"storage.rebind.instances_missing");Directory.Move(missing,instances);
        string state=Directory.GetDirectories(instances)[0],held=Path.Combine(Path.GetDirectoryName(partialPath)!,"held-account");
        Directory.Move(state,held);await Reject(partial,partialPath,"storage.rebind.incomplete");Directory.Move(held,state);
        await Service(partial).RebindAsync(partialPath);
        Check(DailyStorageLocations.Read(partial.Root)!.Cache==partialPath,"restoring missing account permits rebind without resetting accounts");

        foreach(string point in new[]{"rebind-prepared","rebind-bindings","rebind-owner","rebind-published"})
        {
            var (crash,crashPath)=await Moved(point);
            string pending=Path.Combine(crash.Root,"storage-rebind.json"),route=DailyStorageLocations.FileName(crash.Root),ownerPath=Path.Combine(crashPath,"storage-owner.json");
            byte[]? pendingBytes=null,routeBytes=null,ownerBytes=null;
            Dictionary<string,Dictionary<string,string>>? stateAtCrash=null;
            var service=new DailyStorageMigration(crash.Root,crash.Store,crash.Driver){Checkpoint=phase=>{
                if(phase!=point)return;
                pendingBytes=File.ReadAllBytes(pending);routeBytes=File.ReadAllBytes(route);ownerBytes=File.ReadAllBytes(ownerPath);
                stateAtCrash=crash.Driver.Values.ToDictionary(p=>p.Key,p=>new Dictionary<string,string>(p.Value));
                throw new IOException("synthetic interruption");
            }};
            try{await service.RebindAsync(crashPath);throw new Exception("Expected checkpoint failure");}catch(IOException e){Check(e.Message=="synthetic interruption","ordinary exception recovers at "+point);}
            Check(!File.Exists(pending),"in-process recovery retires journal at "+point);
            // Restore exactly the durable state recorded at the simulated process
            // exit, then use a fresh service's real startup recovery entry point.
            File.WriteAllBytes(pending,pendingBytes!);File.WriteAllBytes(route,routeBytes!);File.WriteAllBytes(ownerPath,ownerBytes!);
            crash.Driver.Values.Clear();foreach(var pair in stateAtCrash!)crash.Driver.Values[pair.Key]=pair.Value;
            await Service(crash).RecoverAsync();await Service(crash).RecoverAsync();
            Check(!File.Exists(pending)&&DailyStorageLocations.ReadConfigured(crash.Root)!.Cache==(point=="rebind-published"?crashPath:crash.Target),"restart reconciles actual commit point at "+point);
            await Service(crash).RebindAsync(crashPath);
            Check(DailyStorageLocations.Read(crash.Root)!.Cache==crashPath&&crash.Driver.Boxes().All(b=>crash.Driver.Read(b,"Enabled")=="y"),"retry after restart restores usable route at "+point);
        }
        var (failure,failurePath)=await Moved("driver-failure");
        int failed=0;
        failure.Driver.Fail=(_,setting,value)=>setting=="FileRootPath"&&value.StartsWith(failurePath)&&failed++==0;
        try{await Service(failure).RebindAsync(failurePath);throw new Exception("Expected driver failure");}catch(IOException){}
        failure.Driver.Fail=null;
        Check(DailyStorageLocations.ReadConfigured(failure.Root)!.Cache==failure.Target&&failure.Driver.Boxes().All(b=>failure.Driver.Read(b,"Enabled")=="y"),"partial driver failure rolls back all bindings and enabled states");
        await Service(failure).RebindAsync(failurePath);
        Check(DailyStorageLocations.Read(failure.Root)!.Cache==failurePath,"driver recovery is retryable");
    }
}

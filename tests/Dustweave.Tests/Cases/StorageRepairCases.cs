using Dustweave;
internal static partial class StorageMigrationCases
{
    static async Task Repairing(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-repair: "+label);}
        DailyAccount[] Accounts()=>Enumerable.Range(1,2).Select(i=>new DailyAccount(i,"Fixture "+i,new string((char)('a'+i),64),"",true,false,"")).ToArray();
        async Task<Fixture> Prepared(string name)
        {var f=Make(output,"repair-"+name);await Service(f).MoveAsync(f.Target);return f;}
        string NewTarget(Fixture f)=>Path.Combine(Path.GetDirectoryName(f.Root)!,"恢复 & storage");
        void Lose(string path)
        {
            // Preserve synthetic payload outside the bounded discovery neighborhood.
            string full=Path.GetFullPath(path),test=Path.GetFullPath(output)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(test,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture outside test root");
            string held=Path.Combine(output,"offline-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.GetDirectoryName(held)!);Directory.Move(path,held);
        }
        async Task<DailyStorageMigration.RepairPreview> Recover(Fixture f)
        {var s=Service(f);string target=NewTarget(f);var p=await s.InspectRepairAsync(target,Accounts());return await s.RepairAsync(target,Accounts(),p);}
        var gone=await Prepared("all-missing");Lose(gone.Target);
        byte[] accounts=File.ReadAllBytes(Path.Combine(gone.Root,"accounts","saved.bin"));
        byte[] update=File.ReadAllBytes(Path.Combine(gone.Root,"updates","1.5.10-Portable","attempts","keep","backup","transaction.json"));
        string routing=File.ReadAllText(DailyStorageLocations.FileName(gone.Root));
        var before=await Service(gone).InspectRepairAsync(NewTarget(gone),Accounts());
        Check(before.Preserved==0&&before.Rebuild==2&&!Directory.Exists(NewTarget(gone))&&File.ReadAllText(DailyStorageLocations.FileName(gone.Root))==routing,"inspection recognizes lost instances without writing new storage");
        var repaired=await Service(gone).RepairAsync(NewTarget(gone),Accounts(),before);
        Check(DailyCacheStorage.Location(gone.Root)==NewTarget(gone)&&DailyStorageLocations.Read(gone.Root)!.Sandboxes==repaired.SandboxDirectory,"missing old routing no longer blocks real cache and sandbox resolution");
        Check(accounts.SequenceEqual(File.ReadAllBytes(Path.Combine(gone.Root,"accounts","saved.bin")))&&update.SequenceEqual(File.ReadAllBytes(Path.Combine(gone.Root,"updates","1.5.10-Portable","attempts","keep","backup","transaction.json"))),"saved accounts and update recovery remain byte-identical");
        Check(gone.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(repaired.SandboxDirectory)&&p.Value["Enabled"]=="y"),"lost sandbox bindings point to new storage and retain enabled state");
        string? original=Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT",gone.Root);
            foreach(var a in Accounts())
            {
                string state=Path.Combine(repaired.SandboxDirectory,DailySandbox.CanonicalBoxName(a.AccountKey));
                var permit=DailyStorageMigration.ReadRebuild(state);
                Check(permit?.Account==a.AccountKey&&DailySandbox.BoxName(a.AccountKey)==permit.Box,"rebuild authorization retains exact saved account and custom sandbox name");
                File.WriteAllText(Path.Combine(state,"instance.json.rebuild-interrupted"),"{");
                var owner=DailySandboxRegistration.Resolve(a.AccountKey,state,permit!.Root,()=>@"C:\Game\BrownDust II.exe");
                Check(owner?.Account==a.AccountKey&&owner.Box==permit.Box&&!File.Exists(Path.Combine(state,DailyStorageMigration.RebuildFile)),"normal registration actually accepts rebuilding instead of demanding repair again");
                Check(DailyJson.TryRead<DailySandboxRequest>(Path.Combine(state,"instance.json"))==owner,"interrupted unpublished index does not prevent a complete rebuild index");
                Check(DailySandboxRegistration.Resolve(a.AccountKey,state,permit.Root,()=>@"C:\Game\BrownDust II.exe")==owner,"repeated registration adopts rebuilt progress");
            }
        }
        finally{Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT",original);}
        await Service(gone).RecoverAsync();Check(!File.Exists(Path.Combine(gone.Root,"storage-migration.json")),"successful repair leaves no permanent pending journal");

        var partial=await Prepared("partial");string lostState=Path.Combine(partial.Target,"instances",DailySandbox.CanonicalBoxName(Accounts()[1].AccountKey));Lose(lostState);
        string survivor=Path.Combine(partial.Target,"instances",DailySandbox.CanonicalBoxName(Accounts()[0].AccountKey),"root","RegHive");byte[] hive=File.ReadAllBytes(survivor);
        var part=await Recover(partial);
        Check(part.Preserved==1&&part.Rebuild==1,"one missing account does not prevent recovery of the other");
        Check(File.ReadAllBytes(Path.Combine(part.SandboxDirectory,DailySandbox.CanonicalBoxName(Accounts()[0].AccountKey),"root","RegHive")).SequenceEqual(hive)&&File.ReadAllBytes(survivor).SequenceEqual(hive),"surviving login hive is copied intact and original remains");
        Check(File.Exists(Path.Combine(part.Directory,"suite-cache","generation","component.dll")),"existing regenerable cache is reused where available");

        var moved=await Prepared("moved");string renamed=Path.Combine(Path.GetDirectoryName(moved.Target)!,"renamed folder");Directory.Move(moved.Target,renamed);
        var found=await Recover(moved);Check(found.Preserved==2&&found.Rebuild==0&&Directory.Exists(renamed),"owned moved directory is discovered and preserved while recovering into selected location");

        var unknown=await Prepared("unknown-owner");string firstState=Path.Combine(unknown.Target,"instances",DailySandbox.CanonicalBoxName(Accounts()[0].AccountKey));File.WriteAllText(Path.Combine(firstState,"instance.json"),"{}");
        var other=await Recover(unknown);
        Check(other.Preserved==1&&other.Rebuild==1&&other.Unresolved>0&&Directory.GetFiles(Path.Combine(other.Directory,"recovered"),"RegHive",SearchOption.AllDirectories).Length==1,"unclaimed remnants are retained separately and do not block intact accounts");
        Check(!File.Exists(Path.Combine(other.SandboxDirectory,DailySandbox.CanonicalBoxName(Accounts()[0].AccountKey),"root","RegHive")),"ambiguous old login is never mixed into the rebuilt account");

        foreach(string checkpoint in new[]{"copying","binding","published"})
        {
            var f=await Prepared("crash-"+checkpoint);Lose(Path.Combine(f.Target,"instances",DailySandbox.CanonicalBoxName(Accounts()[1].AccountKey)));
            string target=NewTarget(f);var preview=await Service(f).InspectRepairAsync(target,Accounts());bool hit=false;
            var failing=new DailyStorageMigration(f.Root,f.Store,f.Driver){Checkpoint=point=>{if(!hit&&point==checkpoint){hit=true;throw new IOException("synthetic interruption");}}};
            try{await failing.RepairAsync(target,Accounts(),preview);throw new Exception("Expected interruption");}catch(IOException e){Check(e.Message=="synthetic interruption","injected repair interruption: "+checkpoint);}
            await Service(f).RecoverAsync();
            if(checkpoint!="published")await Recover(f);
            Check(DailyCacheStorage.Location(f.Root)==target&&f.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(Path.Combine(target,"instances"))&&p.Value["Enabled"]=="y"),"interrupted repair can actually continue: "+checkpoint);
            Check(File.Exists(Path.Combine(f.Target,"suite-cache","generation","component.dll")),"rollback and completion preserve original remnants: "+checkpoint);
        }
        var busy=await Prepared("busy");busy.Driver.Running.Add(busy.Driver.Boxes()[0]);
        try{await Service(busy).InspectRepairAsync(NewTarget(busy),Accounts());throw new Exception("Expected busy");}catch(IOException e){Check(e.Message=="storage.sandbox_busy"&&!Directory.Exists(NewTarget(busy)),"active sandbox is not copied or rebound");}
        busy.Driver.Running.Clear();await Recover(busy);Check(DailyCacheStorage.Location(busy.Root)==NewTarget(busy),"stopping the sandbox permits recovery");
        var cancel=await Prepared("cancel");var cp=await Service(cancel).InspectRepairAsync(NewTarget(cancel),Accounts());using var stop=new CancellationTokenSource();stop.Cancel();
        try{await Service(cancel).RepairAsync(NewTarget(cancel),Accounts(),cp,stop.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        Check(!Directory.Exists(NewTarget(cancel)),"cancelled inspection cannot create a destination");await Recover(cancel);
        var during=await Prepared("cancel-copy");File.WriteAllBytes(Path.Combine(during.Target,"suite-cache","large.bin"),new byte[3*1024*1024]);
        var dp=await Service(during).InspectRepairAsync(NewTarget(during),Accounts());using var cancelCopy=new CancellationTokenSource();
        var copying=new DailyStorageMigration(during.Root,during.Store,during.Driver){Checkpoint=point=>{if(point=="repair-file")cancelCopy.Cancel();}};
        try{await copying.RepairAsync(NewTarget(during),Accounts(),dp,cancelCopy.Token);throw new Exception("Expected copy cancellation");}catch(OperationCanceledException){}
        Check(!Directory.Exists(NewTarget(during))&&during.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(during.Target)&&p.Value["Enabled"]=="y"),"cancellation during copying rolls back pointers and preserves the source");
        await Recover(during);Check(DailyCacheStorage.Location(during.Root)==NewTarget(during),"copy cancellation has a working retry");
        var locked=await Prepared("locked-copy");var lp=await Service(locked).InspectRepairAsync(NewTarget(locked),Accounts());
        using(var held=new FileStream(Path.Combine(locked.Target,"suite-cache","generation","component.dll"),FileMode.Open,FileAccess.Read,FileShare.None))
        {
            try{await Service(locked).RepairAsync(NewTarget(locked),Accounts(),lp);throw new Exception("Expected locked-file failure");}catch(IOException){}
        }
        Check(!Directory.Exists(NewTarget(locked))&&locked.Driver.Values.All(p=>p.Value["Enabled"]=="y"),"copy failure restores enabled states without publishing incomplete storage");
        await Recover(locked);Check(DailyCacheStorage.Location(locked.Root)==NewTarget(locked),"released file can be recovered without resetting accounts");
        var changed=await Prepared("changed-preview");var prior=await Service(changed).InspectRepairAsync(NewTarget(changed),Accounts());
        changed.Driver.Values[changed.Driver.Boxes()[0]]["Enabled"]="n";
        try{await Service(changed).RepairAsync(NewTarget(changed),Accounts(),prior);throw new Exception("Expected preview change");}catch(IOException e){Check(e.Message=="storage.repair.changed"&&!Directory.Exists(NewTarget(changed)),"stale binding preview does not authorize a different transaction");}
        await Recover(changed);Check(changed.Driver.Values[changed.Driver.Boxes()[0]]["Enabled"]=="n","new preview preserves a disabled sandbox");
        var exists=await Prepared("existing-target");Directory.CreateDirectory(NewTarget(exists));File.WriteAllText(Path.Combine(NewTarget(exists),"keep.txt"),"keep");
        try{await Service(exists).InspectRepairAsync(NewTarget(exists),Accounts());throw new Exception("Expected occupied target");}catch(IOException e){Check(e.Message=="storage.exists"&&File.ReadAllText(Path.Combine(NewTarget(exists),"keep.txt"))=="keep","recovery never merges into an occupied destination");}
        Check(DailyDiagnosticExport.Allowed("storage-repair-last.json")&&!DailyDiagnosticExport.Allowed("storage-rebuild.json"),"diagnostics include a bounded recovery summary, not account rebuild metadata");
    }
}

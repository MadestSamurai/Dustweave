using Dustweave;
using Dustweave.Accounts;
internal static class StorageMigrationCases
{
    sealed class Driver : IStorageSandboxTransport
    {
        internal readonly Dictionary<string,Dictionary<string,string>> Values=new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Running=[];
        internal Func<string,string,string,bool>? Fail;
        public string[] Boxes()=>Values.Keys.ToArray();
        public string Read(string box,string setting)=>Values[box].GetValueOrDefault(setting,"");
        public bool Idle(string box)=>!Running.Contains(box);
        public Task Set(string box,string setting,string value)
        {
            if(Fail?.Invoke(box,setting,value)==true)throw new IOException("synthetic binding failure");
            Values[box][setting]=value;return Task.CompletedTask;
        }
    }
    sealed record Fixture(string Root,string Store,string Target,Driver Driver,Dictionary<string,byte[]> Files);
    static Fixture Make(string output,string name,int count=2)
    {
        string basis=Path.Combine(output,"storage-migration",name),root=Path.Combine(basis,"host"),store=Path.Combine(basis,"instances"),target=Path.Combine(basis,"destination");
        Directory.CreateDirectory(root);var files=new Dictionary<string,byte[]>();var driver=new Driver();
        void Put(string path,byte[] value){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,value);files[path]=value;}
        Put(Path.Combine(root,"suite-cache","generation","component.dll"),[1,2,3,4]);
        Put(Path.Combine(root,"accounts","saved.bin"),[5,4,3]);
        Put(Path.Combine(root,"updates","1.5.10-Portable","attempts","keep","backup","transaction.json"),[8,7,6]);
        for(int i=1;i<=count;i++)
        {
            string account=new string((char)('a'+i),64),state=Path.Combine(store,DailySandbox.CanonicalBoxName(account));
            string box="RenamedAccount"+i,boxRoot=Path.Combine(state,"root");
            DailyJson.Write(Path.Combine(state,"instance.json"),new DailySandboxRequest(box,i,account,"Fixture",Path.Combine(basis,"BrownDust II.exe"),DateTimeOffset.UtcNow));
            Put(Path.Combine(boxRoot,"RegHive"),[9,4,(byte)i]);
            Put(Path.Combine(boxRoot,"user","current","AppData","Local","SessionVault","encrypted.bd2slot"),[91,42,(byte)i]);
            Put(Path.Combine(boxRoot,"drive","d","game","Addressable","resource.bin"),[3,9,(byte)i]);
            Put(Path.Combine(boxRoot,"user","current","AppData","Local","BD2DailyAssistant","suite-cache","gen","observer.dll"),[7,1,(byte)i]);
            DailyJson.Write(Path.Combine(boxRoot,"user","current","AppData","Local","BD2DailyAssistant","sandbox-binding.json"),new DailySandboxBinding(box,account,"Fixture",i));
            Directory.CreateDirectory(Path.Combine(boxRoot,"empty-folder"));
            driver.Values[box]=new(){{"FileRootPath",boxRoot},{"Enabled","y"}};
        }
        return new(root,store,target,driver,files);
    }
    static DailyStorageMigration Service(Fixture f)=>new(f.Root,f.Store,f.Driver);
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-migration: "+label);}
        async Task Fails(Func<Task> action,string label){try{await action();throw new Exception("Expected failure: "+label);}catch(IOException){cases.Add("storage-migration: "+label);}}
        string Preferences(Fixture fixture,bool emptyRoot=false)
        {
            string state=Path.Combine(fixture.Store,DailySandbox.CanonicalBoxName(new string('e',64)));
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state,"launch-mode.json"),"{\"mode\":\"compatible\"}");
            File.WriteAllText(Path.Combine(state,"note.txt"),"Preserve this historical note.");
            Directory.CreateDirectory(Path.Combine(state,"loading-experiment-recovery"));
            File.WriteAllBytes(Path.Combine(state,"loading-experiment-recovery","transaction.lock"),[]);
            if(emptyRoot)Directory.CreateDirectory(Path.Combine(state,"root"));
            return state;
        }
        async Task<DailyStorageBindingException> BindingFails(Fixture fixture,string state,string reason)
        {
            try{await Service(fixture).MoveAsync(fixture.Target);throw new Exception("Expected binding rejection: "+reason);}
            catch(DailyStorageBindingException error)
            {
                Check(error.Reason==reason&&error.DirectoryPath==state,"binding rejection identifies directory and reason: "+reason);
                Check(!Directory.Exists(fixture.Target)&&!File.Exists(DailyStorageLocations.FileName(fixture.Root))
                    &&fixture.Driver.Values.All(p=>p.Value["Enabled"]=="y"),"binding rejection changes no routing or enabled states: "+reason);
                using var evidence=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root,"live","diagnostics","storage-migration-check.json")));
                Check(evidence.RootElement.GetProperty("reason").GetString()==reason
                    &&evidence.RootElement.GetProperty("stateDirectory").GetString()==state
                    &&!evidence.RootElement.GetProperty("changed").GetBoolean(),"binding diagnostics preserve the preflight failure: "+reason);
                return error;
            }
        }
        var mixed=Make(output,"metadata-and-registered");string metadata=Preferences(mixed);
        Check(DailyStorageMigration.MetadataOnly(metadata),"historical launch preferences without registration or game data are metadata");
        await Service(mixed).MoveAsync(mixed.Target);
        string movedMetadata=Path.Combine(mixed.Target,"instances",Path.GetFileName(metadata));
        Check(File.ReadAllText(Path.Combine(movedMetadata,"launch-mode.json"))=="{\"mode\":\"compatible\"}"
            &&File.ReadAllText(Path.Combine(movedMetadata,"note.txt"))=="Preserve this historical note."
            &&new FileInfo(Path.Combine(movedMetadata,"loading-experiment-recovery","transaction.lock")).Length==0
            &&!File.Exists(Path.Combine(movedMetadata,"instance.json")),"metadata moves intact without inventing an account registration");
        Check(mixed.Driver.Boxes().Length==2&&mixed.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(mixed.Target))
            &&mixed.Files.Where(p=>p.Key.StartsWith(mixed.Store)).All(p=>File.ReadAllBytes(Path.Combine(mixed.Target,"instances",Path.GetRelativePath(mixed.Store,p.Key))).SequenceEqual(p.Value)),
            "metadata does not block existing renamed sandboxes or alter their login data");
        var only=Make(output,"metadata-only",0);string emptyMetadata=Preferences(only,true);
        await Service(only).MoveAsync(only.Target);
        Check(Directory.Exists(Path.Combine(only.Target,"instances",Path.GetFileName(emptyMetadata),"root"))&&only.Driver.Boxes().Length==0,
            "metadata with an empty root moves without any sandbox registration");

        var missing=Make(output,"missing-owner",0);string missingState=Preferences(missing,true);
        File.WriteAllBytes(Path.Combine(missingState,"root","RegHive"),[1,2,3]);
        await BindingFails(missing,missingState,"registration_missing");
        Check(File.ReadAllBytes(Path.Combine(missingState,"root","RegHive")).SequenceEqual(new byte[]{1,2,3}),"unowned game data is never classified as preferences or removed");
        var registeredMetadata=Make(output,"registered-metadata",0);string registeredState=Preferences(registeredMetadata);
        registeredMetadata.Driver.Values[Path.GetFileName(registeredState)]=new(){{"FileRootPath",Path.Combine(registeredState,"root")},{"Enabled","y"}};
        await BindingFails(registeredMetadata,registeredState,"registration_missing");
        var aliasMetadata=Make(output,"renamed-metadata",0);string aliasState=Preferences(aliasMetadata);
        aliasMetadata.Driver.Values["RenamedExistingBox"]=new(){{"FileRootPath",Path.Combine(aliasState,"root")},{"Enabled","y"}};
        await BindingFails(aliasMetadata,aliasState,"registration_missing");
        var malformed=Make(output,"malformed-owner",0);string malformedState=Preferences(malformed);
        File.WriteAllText(Path.Combine(malformedState,"instance.json"),"{");
        await BindingFails(malformed,malformedState,"registration_invalid");
        var unregistered=Make(output,"unregistered-owner",1);string unregisteredState=Directory.GetDirectories(unregistered.Store).Single();
        unregistered.Driver.Values.Clear();
        await BindingFails(unregistered,unregisteredState,"not_registered");
        var mismatch=Make(output,"mismatched-owner",1);string mismatchState=Directory.GetDirectories(mismatch.Store).Single();
        mismatch.Driver.Values["RenamedAccount1"]["FileRootPath"]=Path.Combine(mismatch.Root,"different-game-root");
        var mismatchError=await BindingFails(mismatch,mismatchState,"path_mismatch");
        Check(mismatchError.Label=="Fixture"&&mismatch.Driver.Values["RenamedAccount1"]["FileRootPath"]==Path.Combine(mismatch.Root,"different-game-root"),
            "real mismatch names the account and preserves the manually configured path");
        var duplicate=Make(output,"duplicate-owner",1);string duplicateState=Directory.GetDirectories(duplicate.Store).Single();
        duplicate.Driver.Values["AnotherBox"]=new(){{"FileRootPath",Path.Combine(duplicateState,"root")},{"Enabled","y"}};
        await BindingFails(duplicate,duplicateState,"duplicate_root");
        var pending=Make(output,"pending-removal",0);string pendingState=Preferences(pending);
        File.WriteAllText(Path.Combine(pendingState,"removal.json"),"{}");
        try{await Service(pending).MoveAsync(pending.Target);throw new Exception("Expected recovery rejection");}
        catch(IOException error){Check(error.Message=="storage.recovery_required"&&!Directory.Exists(pending.Target),"pending removal is not bypassed by metadata classification");}
        var recoveryMetadata=Make(output,"metadata-pending-recovery",0);string recoveryState=Preferences(recoveryMetadata);
        File.WriteAllText(DailyLoadingFileTransaction.Pending(Path.Combine(recoveryState,"root")),"{}");
        try{await Service(recoveryMetadata).MoveAsync(recoveryMetadata.Target);throw new Exception("Expected recovery rejection");}
        catch(IOException error){Check(error.Message=="storage.recovery_required"&&!Directory.Exists(recoveryMetadata.Target),"pending game-file recovery is not treated as an inert lock");}
        var backupMetadata=Make(output,"metadata-recovery-backup",0);string backupState=Preferences(backupMetadata);
        Directory.CreateDirectory(Path.Combine(backupState,"loading-experiment-recovery","backup"));
        await BindingFails(backupMetadata,backupState,"registration_missing");
        var unknownMetadata=Make(output,"metadata-unknown-directory",0);string unknownState=Preferences(unknownMetadata);
        Directory.CreateDirectory(Path.Combine(unknownState,"other-game-data"));
        await BindingFails(unknownMetadata,unknownState,"registration_missing");
        var f=Make(output,"complete");
        var result=await Service(f).MoveAsync(f.Target);
        Check(result.Bytes>0&&result.RetainedFiles==0,"copies and verifies complete cache and sandbox trees");
        Check(DailyCacheStorage.Location(f.Root)==f.Target&&DailyStorageLocations.Read(f.Root)!.Sandboxes==Path.Combine(f.Target,"instances"),"publishes cache and sandbox routing together");
        foreach(var entry in f.Files)
        {
            bool moved=entry.Key.StartsWith(f.Store+Path.DirectorySeparatorChar)||entry.Key.Contains("suite-cache");
            string path=entry.Key.StartsWith(f.Store+Path.DirectorySeparatorChar)?Path.Combine(f.Target,"instances",Path.GetRelativePath(f.Store,entry.Key)):
                moved?Path.Combine(f.Target,Path.GetRelativePath(f.Root,entry.Key)):entry.Key;
            Check(File.ReadAllBytes(path).SequenceEqual(entry.Value),"preserves bytes: "+Path.GetFileName(entry.Key));
            if(moved)Check(!File.Exists(entry.Key),"removes verified source: "+Path.GetFileName(entry.Key));
        }
        Check(f.Driver.Values.All(p=>p.Value["Enabled"]=="y"&&p.Value["FileRootPath"].StartsWith(f.Target)),"keeps renamed sandbox identities and restores enabled state");
        Check(Directory.GetDirectories(Path.Combine(f.Target,"instances"),"empty-folder",SearchOption.AllDirectories).Length==2,"preserves empty directories");
        var before=await new DailyStorageMaintenance(f.Root,Path.Combine(f.Target,"instances"),null,_=>false,()=>true).ScanAsync(new());
        Check(before.Disks.Sum(d=>d.Bytes)==before.TotalBytes,"disk totals count relocated main cache and whole sandboxes exactly once");
        Check(before.Disks.Sum(d=>d.ReclaimableBytes)==before.ReclaimableBytes,"disk reclaimable totals agree with cleanup scope");
        string second=f.Target+"-second";await Service(f).MoveAsync(second);
        Check(DailyCacheStorage.Location(f.Root)==second&&f.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(second)),"second relocation follows the current physical store");
        Check(DailyStorageLocations.Read(f.Root)!.PreviousSandboxes.Contains(f.Store),"retains previous stores for accounting without adopting their accounts");
        File.Move(Path.Combine(second,"storage-owner.json"),Path.Combine(second,"storage-owner.off"));
        await Fails(()=>Task.Run(()=>DailyCacheStorage.Location(f.Root)),"missing destination cannot fall back to an empty C-drive store");
        File.Move(Path.Combine(second,"storage-owner.off"),Path.Combine(second,"storage-owner.json"));

        var busy=Make(output,"busy");busy.Driver.Running.Add(busy.Driver.Boxes()[0]);
        await Fails(()=>Service(busy).MoveAsync(busy.Target),"running sandbox blocks relocation before copying or rebinding");
        Check(!Directory.Exists(busy.Target)&&busy.Driver.Values.All(p=>p.Value["Enabled"]=="y"),"busy rejection leaves settings untouched");
        var failure=Make(output,"binding-failure");bool failed=false;
        failure.Driver.Fail=(box,setting,value)=>setting=="FileRootPath"&&box=="RenamedAccount2"&&value.StartsWith(failure.Target)&&!failed&&(failed=true);
        await Fails(()=>Service(failure).MoveAsync(failure.Target),"one failed binding rolls back every already-switched sandbox");
        Check(failure.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(failure.Store)&&p.Value["Enabled"]=="y")&&!Directory.Exists(failure.Target),"rollback restores old roots and removes only the uncommitted target");
        Check(failure.Files.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"rollback preserves host credentials, sandbox hives and OTA journals");

        var recovery=Make(output,"interrupted-rebinding");
        recovery.Driver.Fail=(box,setting,value)=>setting=="FileRootPath"&&
            (box=="RenamedAccount2"&&value.StartsWith(recovery.Target)||box=="RenamedAccount1"&&value.StartsWith(recovery.Store));
        await Fails(()=>Service(recovery).MoveAsync(recovery.Target),"failed rollback retains the journal and both complete copies");
        Check(File.Exists(Path.Combine(recovery.Root,"storage-migration.json"))&&Directory.Exists(recovery.Store)&&Directory.Exists(recovery.Target),"partial binding switch preserves recovery evidence");
        recovery.Driver.Fail=null;await Service(recovery).RecoverAsync();
        Check(recovery.Driver.Values.All(p=>p.Value["FileRootPath"].StartsWith(recovery.Store)&&p.Value["Enabled"]=="y")&&!Directory.Exists(recovery.Target),"next startup restores every binding after interruption halfway through rebinding");

        var interrupted=Make(output,"interrupted-copy");
        var service=new DailyStorageMigration(interrupted.Root,interrupted.Store,interrupted.Driver){Checkpoint=phase=>{if(phase=="copying")throw new IOException("synthetic process exit");}};
        await Fails(()=>service.MoveAsync(interrupted.Target),"durable journal survives a process exit before copy");
        await Service(interrupted).RecoverAsync();
        Check(!Directory.Exists(interrupted.Target)&&!File.Exists(Path.Combine(interrupted.Root,"storage-migration.json")),"next startup recovers an interrupted copy without creating new accounts");
        var committed=Make(output,"interrupted-after-commit");
        service=new DailyStorageMigration(committed.Root,committed.Store,committed.Driver){Checkpoint=phase=>{if(phase=="published")throw new IOException("synthetic process exit");}};
        await Fails(()=>service.MoveAsync(committed.Target),"routing publication remains authoritative when journal finalization is interrupted");
        Check(DailyStorageLocations.Read(committed.Root)!.Cache==committed.Target&&Directory.Exists(committed.Store),"both source and verified target survive interruption after commit");
        await Service(committed).RecoverAsync();
        Check(committed.Driver.Values.All(p=>p.Value["Enabled"]=="y"&&p.Value["FileRootPath"].StartsWith(committed.Target)),"recovery rolls forward and restores launch capability after commit");
        Check(!File.Exists(Path.Combine(committed.Root,"storage-migration.json")),"recovery completes once");

        var cancellation=Make(output,"cancel");using var cancel=new CancellationTokenSource();
        service=new DailyStorageMigration(cancellation.Root,cancellation.Store,cancellation.Driver){Checkpoint=phase=>{if(phase=="copying")cancel.Cancel();}};
        try{await service.MoveAsync(cancellation.Target,cancel.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){ }
        Check(!Directory.Exists(cancellation.Target)&&cancellation.Driver.Values.All(p=>p.Value["Enabled"]=="y"),"cancel restores all temporarily disabled boxes");
        var locked=Make(output,"active-control",0);
        using(var control=DailyUpdateHost.AcquireControl(locked.Root))
        {
            try{await Service(locked).MoveAsync(locked.Target);throw new Exception("Expected lock");}catch(InvalidOperationException){ }
            Check(!Directory.Exists(locked.Target),"active automation retains exclusive control over migration");
        }
        var legacy=Make(output,"legacy-cache",0);string old=legacy.Target+"-old";
        await DailyCacheStorage.MoveAsync(legacy.Root,old);await Service(legacy).MoveAsync(legacy.Target);
        Check(File.Exists(Path.Combine(legacy.Target,"suite-cache","generation","component.dll")),"upgrades a previously moved cache without losing it");
        Check(Directory.Exists(Path.Combine(legacy.Target,"instances")),"first future sandbox will use new location even with zero saved sandboxes");
        var disks=new DailyStorageDisks();
        var a=new DailyStorageItem("C:\\host\\one",100,DateTime.UtcNow){DiskBytes=60};var b=new DailyStorageItem("D:\\cache\\two",250,DateTime.UtcNow){DiskBytes=150};
        disks.Add([a,b],[b.Path]);disks.Add([a,b],[b.Path]);
        var report=disks.Read(p=>p[..3]);
        Check(report.Length==2&&report[0].Bytes==60&&report[0].ReclaimableBytes==0&&report[1].Bytes==150&&report[1].ReclaimableBytes==150,"per-disk accounting uses stored bytes and deduplicates overlaps");
    }
}

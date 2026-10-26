using System.Text.Json.Nodes;
using Dustweave;
internal static class StorageMaintenanceCases
{
    static async Task HistoricalRetention(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("history-retention: "+label);}
        string area=Path.GetFullPath(Path.Combine(output,"history-retention")),root=Path.Combine(area,"host"),instances=Path.Combine(area,"instances"),net=Path.Combine(area,".net");
        string control=Path.Combine(area,"collector","active.json");Directory.CreateDirectory(Path.GetDirectoryName(control)!);
        string Put(string basis,string relative,int days=4)
        {string p=Path.GetFullPath(Path.Combine(basis,relative));Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,"fixture");File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-days));return p;}
        string id=Guid.NewGuid().ToString("N"),trace=Put(root,"loading-experiment/"+id+"/runtime-123-456.jsonl");
        string traceError=Put(root,"loading-experiment/"+id+"/capture-error.txt");
        string recent=Put(root,"loading-experiment/"+Guid.NewGuid().ToString("N")+"/runtime-123-456.jsonl",1);
        string mixedId=Guid.NewGuid().ToString("N"),mixed=Put(root,"loading-experiment/"+mixedId+"/runtime-123-456.jsonl",10);
        string unknown=Put(root,"loading-experiment/"+mixedId+"/user-export.zip",10);
        string outside=Put(area,"chosen-output/runtime/trace.jsonl",10),archive=Put(root,"exports/diagnostics.zip",10);
        string account=Put(root,"accounts/vault.json",10),pending=Put(root,"live/managed-business/pending.json",10);
        string installed=Put(root,"extensions/versions/installed/plugin.dll",10);
        string probe=Put(root,"extensions/checks/"+Guid.NewGuid().ToString("N")+"/result.json");
        string component=Put(root,"rules/daily/generation/table.json"),binding=Put(root,"live/binding-cache/entry.json");
        string download=Put(root,"updates/9.0.0-Portable/package.zip");
        string extraction=Put(net,"Dustweave/aaaaaaaaaaaa/runtime.dll");
        string key=new('e',64),box=DailySandbox.CanonicalBoxName(key),boxDirectory=Path.Combine(instances,box),boxData=Path.Combine(boxDirectory,"root/user/current/AppData/Local/BD2DailyAssistant");
        DailyJson.Write(Path.Combine(boxDirectory,"instance.json"),new DailySandboxRequest(box,1,key,"fixture",@"C:\fixture\BrownDust II.exe",DateTimeOffset.UtcNow));
        string boxTrace=Put(boxData,"loading-experiment/"+Guid.NewGuid().ToString("N")+"/runtime-234-567.jsonl"),boxCache=Put(boxData,"rules/daily/generation/table.json");
        DailyLogCleanup.Save(boxData,new(true,7));
        var service=new DailyStorageMaintenance(root,instances,net,_=>true,()=>true){CaptureControlPath=control};
        var longPolicy=new DailyLogPolicy(true,7);var shortPolicy=new DailyLogPolicy(true,3);
        Check((await service.ScanAsync(longPolicy)).ReclaimableBytes==0,"seven-day setting retains four-day caches and unknown historical layouts");
        var plan=DailyCacheStorage.ScanRetention(root,shortPolicy,CancellationToken.None,captureControl:control);
        Check(new[]{trace,probe,component,binding,download}.All(p=>plan.Units.Any(u=>u.Files.Any(f=>f.Path==p))),"selected days cover trace spool, plugin checks, components, bindings and updates");
        var traceUnit=plan.Units.Single(u=>u.Files.Any(f=>f.Path==trace));
        using(var collector=new FileStream(Path.Combine(Path.GetDirectoryName(control)!,"capture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            var busy=DailyCacheStorage.ScanRetention(root,shortPolicy,CancellationToken.None,captureControl:control);
            Check(!busy.Units.Any(u=>u.Files.Any(f=>f.Path==trace)),"active collector excludes source spools from reclaimable size");
            var result=DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None);
            Check(result.Files==0&&result.Skipped==1&&File.Exists(traceError),"collector started after preview prevents partial spool deletion");
        }
        DailyJson.Write(control,new DailyLoadingExperiment.Control(id,"ordinary",Path.Combine(area,"chosen-output"),DateTime.UtcNow.AddSeconds(15).Ticks,0,false));
        Check(DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None).Files==0,"runtime lease protects spools even if collector lock is not held");
        File.WriteAllText(control,"broken");
        Check(DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None).Files==0,"unreadable capture state cannot authorize deletion");
        DailyJson.Write(control,new DailyLoadingExperiment.Control(id,"ordinary",Path.Combine(area,"chosen-output"),DateTime.UtcNow.AddHours(-1).Ticks,0,false));
        using(var writer=new FileStream(trace,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))
        {
            Check(DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None).Files==0&&File.Exists(traceError),"draining runtime writer protects the entire capture group");
        }
        string added=Put(root,"loading-experiment/"+id+"/user-note.txt");
        Check(DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None).Files==0,"new files invalidate a previously reviewed capture unit");
        File.Delete(added);
        string saved=File.ReadAllText(trace);File.AppendAllText(trace,"changed");
        Check(DailyCacheStorage.DeleteUnits([traceUnit],CancellationToken.None).Files==0,"changed capture content invalidates a cleanup plan");
        File.WriteAllText(trace,saved);File.SetLastWriteTimeUtc(trace,traceUnit.Files.Single(f=>f.Path==trace).Written);
        using(var cancel=new CancellationTokenSource())
        {cancel.Cancel();bool stopped=false;try{DailyCacheStorage.DeleteUnits([traceUnit],cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped&&File.Exists(trace),"cancellation preserves pending cleanup files");}
        var overview=await service.ScanAsync(shortPolicy);
        Check(overview.Scopes.Single(s=>s.Kind=="sandbox").ReclaimableBytes>0&&overview.Scopes.Single(s=>s.Kind=="extraction").ReclaimableBytes>0,"host retention setting also governs idle sandbox and runtime caches");
        await service.CleanAsync(shortPolicy);
        Check(new[]{trace,traceError,probe,component,binding,download,boxTrace,boxCache,extraction}.All(p=>!File.Exists(p)),"expired history is reclaimed across main, sandbox and runtime storage");
        Check(new[]{recent,mixed,unknown,outside,archive,account,pending,installed,control}.All(File.Exists),"recent data, unknown files, user exports, identities, recovery and installed plugins survive");
        Check(!Directory.Exists(Path.GetDirectoryName(trace)!),"empty expired capture directory is removed");
        string automatic=Put(root,"rules/daily/automatic/table.json");DailyLogCleanup.Save(root,shortPolicy);
        Check(DailyCacheStorage.Scan(root).Units.Any(u=>u.Files.Any(f=>f.Path==automatic)),"ordinary cache callers load the user's persisted retention setting");
        await DailyCacheStorage.CleanAsync(root);
        Check(!File.Exists(automatic),"direct cache cleanup uses the same saved days as its preview");
    }
    public static async Task Run(string output,List<string> cases)
    {
        await HistoricalRetention(output,cases);
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-maintenance: "+label);}
        string area=Path.Combine(output,"storage-maintenance"),root=Path.Combine(area,"host"),instances=Path.Combine(area,"instances"),net=Path.Combine(area,".net");
        using(var maintenance=DailyToolControl.AcquireMaintenance(root))
        {
            Check(DailyToolControl.IsMaintenanceOwner(root+Path.DirectorySeparatorChar)&&DailyToolControl.IsOccupied(root)&&!DailyToolControl.IsAutomationOccupied(root),"own maintenance is identified without relaxing the control lease");
            Check(!DailyToolControl.IsMaintenanceOwner(instances),"maintenance ownership is scoped to the exact data root");
            bool refused=false;
            try {using var other=DailyToolControl.Acquire(root);}catch(InvalidOperationException){refused=true;}
            Check(refused,"automation cannot overlap maintenance before cancellation drains");
            maintenance.Dispose();maintenance.Dispose();
        }
        Check(!DailyToolControl.IsMaintenanceOwner(root)&&!DailyToolControl.IsOccupied(root),"maintenance ownership and lease are released even after repeated disposal");
        using(var foreground=DailyToolControl.Acquire(root))
        {
            bool refused=false;
            try {using var maintenance=DailyToolControl.AcquireMaintenance(root);}catch(InvalidOperationException){refused=true;}
            Check(refused&&!DailyToolControl.IsMaintenanceOwner(root)&&DailyToolControl.IsAutomationOccupied(root),"a real tool lease is never reclassified as maintenance");
        }
        string Put(string basis,string relative,int size=100,int age=10)
        {string path=Path.Combine(basis,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,new byte[size]);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-age));return path;}
        string hostAccount=Put(root,"accounts/private.json"),hostProgress=Put(root,"queue-history/old.json");
        string expired=Put(root,"trade/catalogs/only-generation/tables/table.json");
        string legacy=Put(root,"trade/catalogs/current/bundle/trade-worker/_internal/python313.dll",100,3);
        string current=Put(root,"trade/catalogs/current/tables/table.json",100,0);
        string scratch=Put(root,"suite-cache/modules/daily/current/client/build-"+new string('a',32)+"/bridge.dll",100,3);
        string module=Put(root,"suite-cache/modules/daily/current/client/module.dll",100,0);
        string staleClient=Put(root,"suite-cache/modules/daily/current/old-client/module.dll",100,9);
        string fresh=Put(root,"suite-cache/modules/daily/fresh/client/module.dll",100,0);
        string released=Put(root,"suite-cache/modules/daily/current/client/build-"+new string('b',32)+"/bridge.dll",100,0);
        DailyCacheStorage.DiscardBuild(Path.GetDirectoryName(Path.GetDirectoryName(released)!)!,Path.GetDirectoryName(released)!);
        Check(!File.Exists(released)&&File.Exists(module),"successful compiler scratch is discarded without changing the reusable module");
        DailyCacheStorage.DiscardBuild(Path.GetDirectoryName(module)!,Path.GetDirectoryName(module)!);
        Check(File.Exists(module),"scratch disposal refuses a non-build directory");
        (string Box,string Data,string Marker) Box(char key)
        {
            string account=new(key,64),name=DailySandbox.CanonicalBoxName(account),dir=Path.Combine(instances,name);
            string marker=Path.Combine(dir,"instance.json");
            DailyJson.Write(marker,new DailySandboxRequest(name,1,account,"fixture",@"C:\fixture\BrownDust II.exe",DateTimeOffset.UtcNow));
            return(name,Path.Combine(dir,"root/user/current/AppData/Local/BD2DailyAssistant"),marker);
        }
        var idle=Box('a');var active=Box('b');
        string idleLog=Put(idle.Data,"runtime.log"),activeLog=Put(active.Data,"runtime.log");
        string idleAccount=Put(idle.Data,"accounts/vault.json");
        string game=Put(Path.GetDirectoryName(idle.Marker)!,"root/user/current/AppData/LocalLow/Gamfs/save.json");
        string registry=Put(Path.GetDirectoryName(idle.Marker)!,"root/RegHive");
        string cache=Put(idle.Data,"suite-cache/modules/daily/old/client/module.dll");
        string foreign=Put(area,"outside/rules/daily/old/table.json");
        DailyJson.Write(Path.Combine(idle.Data,"cache-location.json"),new DailyCacheLocation(Path.Combine(area,"outside")));
        var invalid=Box('c');File.WriteAllText(invalid.Marker,"{broken");
        string invalidLog=Put(invalid.Data,"runtime.log");
        bool extractAvailable=false,activeAvailable=false;
        var service=new DailyStorageMaintenance(root,instances,net,box=>box!=active.Box||activeAvailable,()=>extractAvailable);
        string oldExtract=Put(net,"Dustweave/aaaaaaaaaaaa/runtime.dll");
        string thirdExtract=Put(net,"Dustweave/bbbbbbbbbbbb/runtime.dll",100,2);
        string newExtract=Put(net,"Dustweave/cccccccccccc/runtime.dll",100,0);
        string newExtract2=Put(net,"Dustweave/dddddddddddd/runtime.dll",100,0);
        string unknownExtract=Put(net,"Dustweave/user-notes/keep.txt");
        string otherApp=Put(net,"OtherApp/aaaaaaaaaaaa/runtime.dll");
        string nativePath=Path.GetDirectoryName(oldExtract)!+Path.DirectorySeparatorChar;
        Check(!DailyStorageMaintenance.MayCleanExtraction(Path.GetDirectoryName(oldExtract)!,nativePath,true),"bundled host protects its active generation even after seven days");
        Check(DailyStorageMaintenance.MayCleanExtraction(Path.GetDirectoryName(thirdExtract)!,nativePath,true),"bundled host permits cleanup of other inactive generations");
        Check(!DailyStorageMaintenance.MayCleanExtraction(Path.GetDirectoryName(oldExtract)!,null,true),"unknown bundled runtime location defers cleanup");
        var bundled=new DailyStorageMaintenance(root,null,net,_=>true,()=>true,extractionSafe:d=>DailyStorageMaintenance.MayCleanExtraction(d,nativePath,true));
        var bundledScan=await bundled.ScanAsync(new());
        Check(bundledScan.Scopes.Single(s=>s.Kind=="extraction").ReclaimableBytes==new FileInfo(thirdExtract).Length,"reclaimable count excludes the active generation rather than the entire cache");
        long Sum(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
        var before=await service.ScanAsync(new());
        Check(before.TotalBytes==Sum(root)+Sum(instances)+Sum(Path.Combine(net,"Dustweave")),"overview counts main, complete sandboxes and own extraction trees exactly once");
        Check(before.SandboxBytes==Sum(instances)&&before.ExtractionBytes==Sum(Path.Combine(net,"Dustweave")),"sandbox totals include protected game files instead of hiding them as unmeasured data");
        Check(before.Scopes.Single(s=>s.Path==Path.GetDirectoryName(active.Marker)).ReclaimableBytes==0,"running sandbox is counted but never offered as reclaimable");
        Check(before.Scopes.Single(s=>s.Kind=="extraction").ReclaimableBytes==0,"a running legacy host protects all extraction generations");
        using(var held=new FileStream(legacy,FileMode.Open,FileAccess.Read,FileShare.Read))
        {await service.CleanAsync(new());Check(File.Exists(legacy),"in-use legacy bundle remains intact");}
        Check(!File.Exists(expired)&&!File.Exists(scratch)&&!File.Exists(staleClient),"age limit and scratch cleanup apply inside the last two retained generations");
        Check(!File.Exists(idleLog)&&!File.Exists(cache)&&File.Exists(activeLog)&&File.Exists(invalidLog),"only verified idle sandbox application data is cleaned");
        Check(new[]{hostAccount,hostProgress,idleAccount,game,registry,foreign,current,module,fresh,oldExtract,thirdExtract,unknownExtract,otherApp}.All(File.Exists),"accounts, game state, external cache hints and active extraction data are protected");
        extractAvailable=true;activeAvailable=true;
        await service.CleanAsync(new());
        Check(!File.Exists(legacy)&&!File.Exists(activeLog)&&!File.Exists(oldExtract)&&!File.Exists(thirdExtract),"deferred cleanup resumes after use ends and keeps at most two recent extraction generations");
        Check(new[]{newExtract,newExtract2,unknownExtract,otherApp,game,registry}.All(File.Exists),"unknown extraction layouts and other applications are never deleted");
        var after=await service.ScanAsync(new());
        Check(after.TotalBytes==Sum(root)+Sum(instances)+Sum(Path.Combine(net,"Dustweave")),"post-cleanup statistics match actual remaining files");
        DailyJson.Write(Path.Combine(root,"cache-location.json"),new DailyCacheLocation(idle.Data));
        var overlap=await service.ScanAsync(new());
        Check(overlap.TotalBytes==Sum(root)+Sum(instances)+Sum(Path.Combine(net,"Dustweave")),"legacy overlapping cache locations are counted once across storage scopes");
        File.Delete(Path.Combine(root,"cache-location.json"));
        using(var held=DailyUpdateHost.AcquireControl(root))
        {bool blocked=false;try{await service.CleanAsync(new());}catch(InvalidOperationException){blocked=true;}Check(blocked,"automation ownership prevents maintenance from racing a live operation");}
        string worker=Path.Combine(area,"worker");string id=Guid.NewGuid().ToString("N"),step=Path.Combine(worker,"live/steps",id);
        using(var evidence=new DailyLogEvidence(worker))
        {
            DailyJson.Write(Path.Combine(step,"intent.json"),new{Id=id,Reason="navigate"});
            DailyJson.Write(Path.Combine(step,"before.json"),new{payload=new string('x',4096)});
            DailyJson.Write(Path.Combine(step,"result.json"),new{id,state="dispatched_only",engine="dotnet-driver-v1",after=new{payload=new string('x',4096)}});
            evidence.Complete();
        }
        string workerOld=Put(worker,"runtime.log");
        using(var held=DailyUpdateHost.AcquireControl(worker))await DailyStorageMaintenance.FinishWorkerAsync(worker);
        Check(DailyLogEvidence.HasPending(worker)&&File.Exists(workerOld),"worker cleanup failure is nonfatal and retains queued work for the next opportunity");
        DailyLogCleanup.Save(worker,new(false));await DailyStorageMaintenance.FinishWorkerAsync(worker);
        Check(!DailyLogEvidence.HasPending(worker)&&!File.Exists(Path.Combine(step,"before.json"))&&File.Exists(workerOld),"headless workers compact successful steps even with automatic expiry disabled");
        DailyLogCleanup.Save(worker,new());await DailyStorageMaintenance.FinishWorkerAsync(worker);
        Check(!File.Exists(workerOld)&&File.Exists(Path.Combine(step,"result.json")),"headless workers expire old logs and retain recent results");
        string Receipt(string state,string reason)
        {
            string receiptId=Guid.NewGuid().ToString("N"),path=Path.Combine(worker,"live/receipts",receiptId+".json");
            DailyJson.Write(path,new{Command=new{Id=receiptId,Reason=reason},State=state,After=new{ProcessId=9,Instance="fixture",AccountKey="identity",Surfaces=new string('x',8192)},MayHaveDispatched=true});
            File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-10));return path;
        }
        string historical=Receipt("observed_after_dispatch","navigate"),uncertain=Receipt("unknown","navigate");
        string unresolved=Receipt("observed_after_dispatch","business:"+Guid.NewGuid().ToString("N")+"|consume");
        await DailyStorageMaintenance.FinishWorkerAsync(worker);
        var receipt=DailyJson.TryRead<JsonObject>(historical)!;
        Check(receipt["After"]?["Surfaces"]==null && receipt["After"]?["AccountKey"]?.GetValue<string>()=="identity" && receipt["Command"]!=null,"old settled disk receipts shed UI trees but retain identity, command and outcome");
        Check(DailyJson.TryRead<JsonObject>(uncertain)?["After"]?["Surfaces"]!=null&&DailyJson.TryRead<JsonObject>(unresolved)?["After"]?["Surfaces"]!=null,"unknown receipts and pending consuming operations retain their recovery evidence");
        Check(File.GetLastWriteTimeUtc(historical)<DateTime.UtcNow.AddDays(-9),"compacting historical receipts does not refresh their age");
        using(var stop=new CancellationTokenSource()){stop.Cancel();bool cancelled=false;try{await service.ScanAsync(new(),stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"combined inventory is cancellable");}
        string linked=Path.Combine(instances,"linked");
        try{Directory.CreateSymbolicLink(linked,Path.Combine(area,"outside"));var linkedReport=await service.ScanAsync(new());await service.CleanAsync(new());Check(linkedReport.Skipped>0&&File.Exists(foreign),"linked sandbox trees are neither followed nor cleaned");}catch(UnauthorizedAccessException) { }
    }
}

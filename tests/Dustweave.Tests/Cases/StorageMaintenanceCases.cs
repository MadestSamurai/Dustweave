using System.Text.Json.Nodes;
using Dustweave;
internal static class StorageMaintenanceCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-maintenance: "+label);}
        string area=Path.Combine(output,"storage-maintenance"),root=Path.Combine(area,"host"),instances=Path.Combine(area,"instances"),net=Path.Combine(area,".net");
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

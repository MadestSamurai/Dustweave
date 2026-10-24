using Dustweave;
using System.Diagnostics;
using System.Text.Json.Nodes;
internal static class LoadingExperimentCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
        void Reject(Action action,string name){bool failed=false;try{action();}catch(Exception e)when(e is InvalidOperationException or InvalidDataException or IOException or OperationCanceledException){failed=true;}Check(failed,name);}
        {
            string parent=Path.Combine(output,"comparison-fixture"),group=Path.Combine(parent,"failed-C");Directory.CreateDirectory(group);
            DailyJson.Write(Path.Combine(group,"session.json"),new{mode="legacy-files",deep=true});
            string trace=Path.Combine(group,"runtime-fixture.jsonl");
            string content="{\"Kind\":\"clock\",\"Name\":\"frequency=1000\"}\n{\"Kind\":\"enter\",\"Name\":\"load\",\"Started\":100,\"Stamp\":100,\"Thread\":1}\n{\"Kind\":\"leave\",\"Name\":\"load\",\"Started\":100,\"Stamp\":350,\"Thread\":1}\n{\"Kind\":\"game-build\",\"Name\":\"version=2.37.13\"}\n{\"Kind\":\"closed\",\"Dropped\":2}\ntruncated-row";
            File.WriteAllText(trace,content);
            var comparison=JsonNode.Parse(File.ReadAllText(DailyLoadingComparison.Analyze(parent)))!["groups"]!.AsArray();
            Check(comparison.Count==1&&comparison[0]!["invalidRows"]!.GetValue<int>()==1&&comparison[0]!["dropped"]!.GetValue<int>()==2&&comparison[0]!["longestBoundaries"]![0]!["MaxMs"]!.GetValue<double>()==250&&comparison[0]!["nativeValidation"]==null&&File.ReadAllText(trace)==content,"streaming comparison includes failed groups, measures boundaries, reports gaps and never claims unverified native coverage");
            File.WriteAllText(Path.Combine(group,"STOP"),"done");
            File.AppendAllText(trace,"\n"+System.Text.Json.JsonSerializer.Serialize(new{Kind="closed",Utc=DateTime.UtcNow.AddMinutes(-2).Ticks,Reason="lease-expired-after-read-gap"})+"\n");
            var coverage=JsonNode.Parse(File.ReadAllText(DailyLoadingComparison.Analyze(parent)))!["groups"]![0]!["traceCoverage"]![0]!;
            Check(coverage["endsEarly"]!.GetValue<bool>()&&coverage["closedReason"]!.GetValue<string>()=="lease-expired-after-read-gap","comparison detects an early trace closure even when game route and file export succeeded");
        }
        var account=new DailyAccount(2,"fixture",DailyIdentity.MemberKey("2222"),"",true,false,"");
        var launch=new LoadingExperimentLaunchRequest(Guid.NewGuid().ToString("N"),account.AccountKey,account.SlotNumber,"ordinary");
        var routes=new List<string>();
        foreach(string mode in new[]{"ordinary","isolated","legacy-files"})
            await DailyLoadingExperimentLaunch.DispatchAsync(launch with{Mode=mode},account,a=>{routes.Add("ordinary:"+a.SlotNumber);return Task.CompletedTask;},a=>{routes.Add("isolated:"+a.SlotNumber);return Task.CompletedTask;});
        Check(routes.SequenceEqual(new[]{"ordinary:2","isolated:2","isolated:2"}),"A launches selected ordinary account; B and C launch the same selected sandbox account");
        Reject(()=>DailyLoadingExperimentLaunch.DispatchAsync(launch with{Slot=3},account,_=>throw new Exception("wrong dispatch"),_=>throw new Exception("wrong dispatch")),"stale slot cannot launch a different account");
        Reject(()=>DailyLoadingExperimentLaunch.DispatchAsync(launch with{AccountKey=DailyIdentity.MemberKey("3333")},account,_=>throw new Exception("wrong dispatch"),_=>throw new Exception("wrong dispatch")),"changed account key is rejected before either launch path");
        Reject(()=>DailyLoadingExperimentLaunch.Isolated("unknown"),"unknown experiment group cannot fall back to ordinary startup");
        if(DailyLoadingExperiment.Active()!=null)throw new Exception("An actual loading experiment is active; do not replace its control.");
        byte[]? previous=File.Exists(DailyLoadingExperiment.ControlPath)?File.ReadAllBytes(DailyLoadingExperiment.ControlPath):null;
        string folder=Path.Combine(output,"loading-capture"),source=Path.Combine(folder,"source"),destination=Path.Combine(folder,"chosen-output");Directory.CreateDirectory(source);
        string logs=Path.Combine(source,"live","observation-failures");Directory.CreateDirectory(logs);
        for(int i=0;i<2005;i++)File.WriteAllText(Path.Combine(logs,i+".json"),"{\"status\":\"fixture\",\"accessToken\":\"never-export-this\"}");
        string historical=Path.Combine(logs,"old.json");File.WriteAllText(historical,"{\"old\":true}");File.SetLastWriteTimeUtc(historical,DateTime.UtcNow.AddDays(-4));
        string current=Path.Combine(source,"connection.json");File.WriteAllText(current,"{\"current\":true}");File.SetLastWriteTimeUtc(current,DateTime.UtcNow.AddDays(-4));
        var job=DailyLoadingExperiment.RunCoreAsync(destination,"ordinary",false,()=>new(){{"fixture",source}},()=>Array.Empty<Process>(),false);
        try{
            var session=JsonNode.Parse(File.ReadAllText(Path.Combine(destination,"session.json")))!;string id=session["id"]!.GetValue<string>();
            string spool=Path.Combine(source,"loading-experiment",id,"runtime.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(spool)!);
            using(var f=File.Create(spool)){var block=new byte[1024*1024];Array.Fill<byte>(block,32);block[^1]=10;for(int i=0;i<130;i++)f.Write(block);}
            string copy=Path.Combine(destination,"runtime","fixture","loading-experiment",id,"runtime.jsonl");
            var timer=Stopwatch.StartNew();while((!File.Exists(copy)||new FileInfo(copy).Length<130L*1024*1024)&&timer.Elapsed<TimeSpan.FromSeconds(25))await Task.Delay(100);
            Check(File.Exists(copy)&&new FileInfo(copy).Length==130L*1024*1024,"selected directory preserves runtime trace beyond normal 128 MiB export budget");
            using(var viewer=new FileStream(copy,FileMode.Open,FileAccess.Read,FileShare.Read)){
                File.AppendAllText(spool,"tail-during-temporary-output-lock\n");
                await Task.Delay(4500);
                Check(!job.IsCompleted&&DailyLoadingExperiment.Active()?.Id==id,"temporary destination read lock does not stop recording or expire the game observation lease");
            }
            timer.Restart();while(new FileInfo(copy).Length<new FileInfo(spool).Length&&timer.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
            Check(new FileInfo(copy).Length==new FileInfo(spool).Length,"retry after destination sharing lock preserves the missing tail exactly once");
            timer.Restart();while(Directory.EnumerateFiles(Path.Combine(destination,"records"),"*",SearchOption.AllDirectories).Count()<2006&&timer.Elapsed<TimeSpan.FromSeconds(10))await Task.Delay(100);
            var saved=Directory.EnumerateFiles(Path.Combine(destination,"records"),"*",SearchOption.AllDirectories).ToArray();
            Check(saved.Length==2006,"experiment preserves more than 2000 files without normal export count limit");
            Check(saved.Any(p=>Path.GetFileName(p).StartsWith("connection.json."))&&!saved.Any(p=>Path.GetFileName(p).StartsWith("old.json.")),"experiment retains old current state without replaying unrelated historical archives");
            File.WriteAllText(historical,"{\"nowChanged\":true}");
            timer.Restart();while(!Directory.EnumerateFiles(Path.Combine(destination,"records"),"old.json.*",SearchOption.AllDirectories).Any()&&timer.Elapsed<TimeSpan.FromSeconds(5))await Task.Delay(100);
            Check(Directory.EnumerateFiles(Path.Combine(destination,"records"),"old.json.*",SearchOption.AllDirectories).Any(),"changes to formerly historical evidence are captured during the experiment");
            string arrived=Path.Combine(source,"tools","new-tool","diagnostics");Directory.CreateDirectory(arrived);
            string renamed=Path.Combine(arrived,"status.json");File.WriteAllText(renamed+".tmp","{\"newTool\":true}");File.Move(renamed+".tmp",renamed);
            timer.Restart();while(!Directory.EnumerateFiles(Path.Combine(destination,"records"),"status.json.*",SearchOption.AllDirectories).Any()&&timer.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
            Check(Directory.EnumerateFiles(Path.Combine(destination,"records"),"status.json.*",SearchOption.AllDirectories).Any(),"watcher captures an atomically renamed status in a newly created tool directory");
            string premature=Path.Combine(source,"loading-experiment",id,"runtime-premature.jsonl");File.WriteAllText(premature,"{\"Kind\":\"closed\",\"Reason\":\"fixture\"}\n");
            bool PrematureReported(){try{return File.ReadAllText(Path.Combine(destination,"coverage-errors.jsonl")).Contains("runtime-ended-during-capture");}catch(IOException){return false;}}
            timer.Restart();while(!PrematureReported()&&timer.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
            Check(File.ReadAllText(Path.Combine(destination,"coverage-errors.jsonl")).Contains("runtime-ended-during-capture"),"collector records early in-game trace shutdown as a coverage gap instead of silently reporting complete capture");
            Check(!File.ReadAllText(Directory.EnumerateFiles(Path.Combine(destination,"records"),"*",SearchOption.AllDirectories).First()).Contains("never-export-this"),"unbounded capture still redacts credentials");
            launch=launch with{SessionId=id};
            DailyLoadingExperimentLaunch.RequireActive(destination,launch);
            Reject(()=>DailyLoadingExperimentLaunch.RequireActive(destination,launch with{Mode="isolated"}),"collector group cannot change after recording starts");
            Reject(()=>DailyLoadingExperimentLaunch.RequireActive(destination,launch with{SessionId=Guid.NewGuid().ToString("N")}),"another capture cannot reuse this launch request");
            Reject(()=>DailyLoadingExperimentLaunch.RequireActive(source,launch),"capture output must match its selected directory");
            DailyLoadingExperimentLaunch.Claim(destination);
            Reject(()=>DailyLoadingExperimentLaunch.Claim(destination),"repeated start dispatch is refused for the same capture");
            Reject(()=>DailyLoadingExperimentLaunch.RequireIsolatedSession(id),"ordinary capture cannot bootstrap a sandbox game");
            File.WriteAllText(Path.Combine(destination,"PAUSE-READS"),"test");
            timer.Restart();while(DailyLoadingExperiment.Active()?.PauseUntilUtcTicks<=DateTime.UtcNow.Ticks&&timer.Elapsed<TimeSpan.FromSeconds(5))await Task.Delay(100);
            Check(DailyLoadingExperiment.Active()?.PauseUntilUtcTicks>DateTime.UtcNow.Ticks,"observer pause is explicitly requested and time limited");
            File.WriteAllText(Path.Combine(destination,"STOP"),"done");
            Reject(()=>DailyLoadingExperimentLaunch.RequireActive(destination,launch),"stop marker blocks launch before the observer lease expires");
            await Task.Delay(2500);File.AppendAllText(spool,"final-drained-tail\n");
            File.WriteAllText(Path.Combine(logs,"final-grace.json"),"{\"lateEvidence\":true}");
            Check(await job==0&&DailyLoadingExperiment.Active()==null,"stopping drains trace and removes the experiment marker without stopping a game");
            Check(new FileInfo(copy).Length==130L*1024*1024+"tail-during-temporary-output-lock\n".Length+"final-drained-tail\n".Length,"final runtime tail is preserved after observers stop independently of polling cadence");
            Check(Directory.EnumerateFiles(Path.Combine(destination,"records"),"final-grace.json.*",SearchOption.AllDirectories).Any(),"final scan preserves non-runtime evidence written during the stop grace period");
            Check(session["storageLimitBytes"]==null&&session["automaticDeletion"]!.GetValue<bool>()==false,"capture manifest declares unbounded storage and no automatic deletion");

            string lockedSource=Path.Combine(folder,"locked-source"),lockedOutput=Path.Combine(folder,"locked-output");Directory.CreateDirectory(lockedSource);
            var lockedJob=DailyLoadingExperiment.RunCoreAsync(lockedOutput,"ordinary",false,()=>new(){{"fixture",lockedSource}},()=>Array.Empty<Process>(),false);
            string lockedId=JsonNode.Parse(File.ReadAllText(Path.Combine(lockedOutput,"session.json")))!["id"]!.GetValue<string>();
            string lockedTrace=Path.Combine(lockedSource,"loading-experiment",lockedId,"runtime-pending.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(lockedTrace)!);File.WriteAllText(lockedTrace,"before\n");
            string lockedCopy=Path.Combine(lockedOutput,"runtime","fixture","loading-experiment",lockedId,"runtime-pending.jsonl");
            timer.Restart();while(!File.Exists(lockedCopy)&&timer.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
            using(var viewer=new FileStream(lockedCopy,FileMode.Open,FileAccess.Read,FileShare.Read)){
                File.AppendAllText(lockedTrace,"retained-source-tail\n");File.WriteAllText(Path.Combine(lockedOutput,"STOP"),"done");
                Check(await lockedJob.WaitAsync(TimeSpan.FromSeconds(20))==1&&File.Exists(Path.Combine(lockedOutput,"runtime-copy-pending.json"))&&File.ReadAllText(lockedTrace).Contains("retained-source-tail"),"persistent destination lock at finish reports incomplete capture and retains original trace instead of declaring success");
            }

            string host=Path.Combine(folder,"host"),box=Path.Combine(folder,"box");Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"current");
            string overlay=Path.Combine(box,"drive",host[..1].ToLowerInvariant(),host[3..]);Directory.CreateDirectory(overlay);string old=Path.Combine(overlay,"BrownDust II.exe");File.WriteAllText(old,"previous");
            DailyJson.Write(DailyLoadingExperiment.ControlPath,new DailyLoadingExperiment.Control(Guid.NewGuid().ToString("N"),"legacy-files",destination,DateTime.UtcNow.AddSeconds(30).Ticks,0,false));
            Reject(()=>DailyLoadingExperimentLaunch.RequireIsolatedSession(DailyLoadingExperiment.Active()!.Id),"stopped legacy capture cannot finish a delayed sandbox bootstrap");
            File.Delete(Path.Combine(destination,"STOP"));
            DailyLoadingExperimentLaunch.RequireIsolatedSession(DailyLoadingExperiment.Active()!.Id);
            Check(true,"active C capture permits the sandbox bootstrap");
            var rules=DailySandboxGameFiles.LegacyRules(exe).ToList();int removed=0;
            Task<bool> Apply()=>DailySandboxGameFiles.ExperimentAsync(exe,box,"Fixture","fixture-install",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),rule=>{rules.Remove(rule);removed++;return Task.CompletedTask;},CancellationToken.None);
            string privateFile=Path.Combine(overlay,"launcher.settings");File.WriteAllText(privateFile,"private-channel");
            Check(await Apply()&&removed==5&&!File.Exists(old),"C preserves stale program overlays then starts with host game files instead of rejecting differences");
            Check(File.ReadAllText(privateFile)=="private-channel","C does not move or modify launcher login settings");
            Check(File.ReadAllText(Path.Combine(destination,"program-overlay-before.jsonl")).Contains("different"),"C records actual stale file names and both hashes before changing rules");
            Check(File.ReadAllText(Path.Combine(destination,"program-differences","sandbox","BrownDust II.exe"))=="previous"&&File.ReadAllText(Path.Combine(destination,"program-differences","host","BrownDust II.exe"))=="current","C preserves both complete mismatched versions for analysis, not just their hashes");
            File.WriteAllText(old,"created-during-C");
            var transaction=DailyLoadingFileTransaction.Read(box)!;
            await DailyLoadingFileTransaction.RecoverAsync(box,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),rule=>{rules.Add(rule);return Task.CompletedTask;},CancellationToken.None);
            Check(File.ReadAllText(old)=="previous"&&rules.Count==5,"C restores exact original overlays and all original rules");
            string savedDuring=Path.Combine(Path.GetDirectoryName(DailyLoadingFileTransaction.Pending(box))!,transaction.Id,"during","BrownDust II.exe");
            Check(File.ReadAllText(savedDuring)=="created-during-C","C also preserves overlays created during the experiment");
            Check(!await DailyLoadingFileTransaction.RecoverAsync(box,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),_=>throw new Exception("duplicate restore"),CancellationToken.None),"completed C recovery is idempotent");
            File.Delete(DailyLoadingExperiment.ControlPath);
            Reject(()=>DailyLoadingExperimentLaunch.RequireIsolatedSession(id),"expired experiment cannot start a delayed game");
            DailyLoadingExperimentLaunch.RequireIsolatedSession("");
            Check(true,"ordinary product sandbox launches do not depend on a capture");
            await DailySandboxGameFiles.EnsureAsync(exe,()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),rule=>{rules.Add(rule);return Task.CompletedTask;},_=>{},CancellationToken.None,rule=>{rules.Remove(rule);return Task.CompletedTask;});
            Check(rules.Count==DailySandboxGameFiles.Rules(exe).Length&&!rules.Contains(Path.Combine(host,@"BrownDust II_Data\*")),"next normal launch migrates restored legacy rules without making caches read-only");
            await FileRecoveryCases(folder,Check);
        }finally{
            if(!job.IsCompleted){File.WriteAllText(Path.Combine(destination,"STOP"),"cleanup");await job;}
            if(previous==null){if(File.Exists(DailyLoadingExperiment.ControlPath))File.Delete(DailyLoadingExperiment.ControlPath);}else File.WriteAllBytes(DailyLoadingExperiment.ControlPath,previous);
        }
    }

    static async Task FileRecoveryCases(string folder,Action<bool,string> check)
    {
        {
            string fixture=Path.Combine(folder,"cancel-preservation"),root=Path.Combine(fixture,"root"),host=Path.Combine(fixture,"game");
            Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"current");
            string overlay=DailyLoadingFileTransaction.Overlay(root,exe);Directory.CreateDirectory(overlay);
            string original=Path.Combine(overlay,"BrownDust II.exe");File.WriteAllBytes(original,new byte[6*1024*1024]);
            string destination=Path.Combine(fixture,"capture");Directory.CreateDirectory(destination);
            var control=new DailyLoadingExperiment.Control(Guid.NewGuid().ToString("N"),"legacy-files",destination,DateTime.UtcNow.AddMinutes(1).Ticks,0,true);
            var rules=DailySandboxGameFiles.LegacyRules(exe).ToList();using var stop=new CancellationTokenSource();bool cancelled=false;
            try{await DailyLoadingFileTransaction.PrepareAsync(control,exe,root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),_=>throw new Exception("must not change rules during preservation"),stop.Token,
                phase=>{if(phase.StartsWith("preserved-bytes:",StringComparison.Ordinal))stop.Cancel();});}catch(OperationCanceledException){cancelled=true;}
            check(cancelled&&new FileInfo(original).Length==6*1024*1024&&rules.Count==5&&DailyLoadingFileTransaction.Read(root)==null,"stopping during a large C preservation copy leaves the original and all rules unchanged");
            check(new FileInfo(Path.Combine(destination,"program-differences","sandbox","BrownDust II.exe")).Length==1024*1024&&File.Exists(Path.Combine(destination,"legacy-preparation.json")),"cancelled preservation keeps its partial evidence and visible progress instead of hanging through a large copy");
        }
        // Interrupt at durable intents AND between the filesystem mutation and
        // its outcome record. These are crash recovery tests, not mocked copies.
        string[] phases=["journal-created","move-pending:BrownDust II.exe","moved:BrownDust II.exe","moved:BrownDust II_Data","rule-removed:","prepared"];
        foreach(string phase in phases)
        {
            string id=Guid.NewGuid().ToString("N"),fixture=Path.Combine(folder,"crash-"+id),root=Path.Combine(fixture,"root"),host=Path.Combine(fixture,"game");
            Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"new-program");
            string overlay=DailyLoadingFileTransaction.Overlay(root,exe);Directory.CreateDirectory(Path.Combine(overlay,"BrownDust II_Data"));
            File.WriteAllText(Path.Combine(overlay,"BrownDust II.exe"),"original-program");
            File.WriteAllText(Path.Combine(overlay,"BrownDust II_Data","old.bundle"),"original-assets");
            File.WriteAllText(Path.Combine(overlay,"launcher.settings"),"private-login");
            string destination=Path.Combine(fixture,"capture");Directory.CreateDirectory(destination);
            var control=new DailyLoadingExperiment.Control(id,"legacy-files",destination,DateTime.UtcNow.AddMinutes(1).Ticks,0,true);
            var rules=DailySandboxGameFiles.LegacyRules(exe).Append("unrelated-rule").ToList();
            bool crashed=false;
            try{await DailyLoadingFileTransaction.PrepareAsync(control,exe,root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Remove(r);return Task.CompletedTask;},CancellationToken.None,
                point=>{if(point.StartsWith(phase,StringComparison.Ordinal)){crashed=true;throw new IOException("simulated power loss");}});}catch(IOException)when(crashed){}
            check(crashed,"C injected preparation interruption: "+phase);
            await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Add(r);return Task.CompletedTask;},CancellationToken.None);
            check(File.ReadAllText(Path.Combine(overlay,"BrownDust II.exe"))=="original-program"&&File.ReadAllText(Path.Combine(overlay,"BrownDust II_Data","old.bundle"))=="original-assets"&&File.ReadAllText(Path.Combine(overlay,"launcher.settings"))=="private-login"&&rules.Count==6,"C restores all original files and leaves login/unrelated rules unchanged after "+phase);
        }
        foreach(string phase in new[]{"during-saved:BrownDust II.exe","original-restored:BrownDust II.exe","during-saved:MonoBleedingEdge","original-restored:BrownDust II_Data"})
        {
            string id=Guid.NewGuid().ToString("N"),fixture=Path.Combine(folder,"restore-crash-"+id),root=Path.Combine(fixture,"root"),host=Path.Combine(fixture,"game");
            Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"new");
            string overlay=DailyLoadingFileTransaction.Overlay(root,exe);Directory.CreateDirectory(Path.Combine(overlay,"BrownDust II_Data"));File.WriteAllText(Path.Combine(overlay,"BrownDust II.exe"),"original");File.WriteAllText(Path.Combine(overlay,"BrownDust II_Data","old"),"original");
            string destination=Path.Combine(fixture,"capture");Directory.CreateDirectory(destination);
            var control=new DailyLoadingExperiment.Control(id,"legacy-files",destination,DateTime.UtcNow.AddMinutes(1).Ticks,0,true);var rules=DailySandboxGameFiles.LegacyRules(exe).ToList();
            await DailyLoadingFileTransaction.PrepareAsync(control,exe,root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Remove(r);return Task.CompletedTask;},CancellationToken.None);
            File.WriteAllText(Path.Combine(overlay,"BrownDust II.exe"),"during");Directory.CreateDirectory(Path.Combine(overlay,"MonoBleedingEdge"));File.WriteAllText(Path.Combine(overlay,"MonoBleedingEdge","new"),"during");
            bool blocked=false;try{await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(false),_=>throw new Exception("must not mutate"),CancellationToken.None);}catch(LoadingExperimentBusyException){blocked=true;}
            check(blocked&&rules.Count==0&&File.ReadAllText(Path.Combine(overlay,"BrownDust II.exe"))=="during","running sandbox defers recovery without mutating files: "+phase);
            bool crashed=false;try{await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Add(r);return Task.CompletedTask;},CancellationToken.None,p=>{if(p==phase){crashed=true;throw new IOException("simulated restore crash");}});}catch(IOException)when(crashed){}
            check(crashed,"C injected restoration interruption: "+phase);
            await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Add(r);return Task.CompletedTask;},CancellationToken.None);
            check(File.ReadAllText(Path.Combine(overlay,"BrownDust II.exe"))=="original"&&File.ReadAllText(Path.Combine(overlay,"BrownDust II_Data","old"))=="original"&&!Directory.Exists(Path.Combine(overlay,"MonoBleedingEdge"))&&rules.Count==5,"C recovery finishes after "+phase);
        }
        {
            string id=Guid.NewGuid().ToString("N"),fixture=Path.Combine(folder,"output-loss-"+id),root=Path.Combine(fixture,"root"),host=Path.Combine(fixture,"game");
            Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"current");
            string overlay=DailyLoadingFileTransaction.Overlay(root,exe);Directory.CreateDirectory(overlay);File.WriteAllText(Path.Combine(overlay,"BrownDust II.exe"),"original");
            string destination=Path.Combine(fixture,"capture");Directory.CreateDirectory(destination);
            var control=new DailyLoadingExperiment.Control(id,"legacy-files",destination,DateTime.UtcNow.AddMinutes(1).Ticks,0,true);var rules=DailySandboxGameFiles.LegacyRules(exe).ToList();
            await DailyLoadingFileTransaction.PrepareAsync(control,exe,root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Remove(r);return Task.CompletedTask;},CancellationToken.None);
            // A file at the former directory path faithfully makes output writes
            // fail without relying on platform-specific permission inheritance.
            Directory.Move(destination,destination+"-preserved");File.WriteAllText(destination,"output unavailable");
            DailyLoadingFileTransaction.ObserveGame(root,destination,id,"Fixture",123,456);
            check(DailyLoadingFileTransaction.Read(root) is {GameProcessId:123,GameStartTicks:456},"C process ownership proof survives unavailable output storage");
            await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Add(r);return Task.CompletedTask;},CancellationToken.None);
            check(DailyLoadingFileTransaction.Read(root)?.State=="restored"&&rules.Count==5&&File.ReadAllText(Path.Combine(overlay,"BrownDust II.exe"))=="original","C restores all files and rules even when the chosen output directory is unwritable");
        }
        {
            string fixture=Path.Combine(folder,"host-update-"+Guid.NewGuid().ToString("N")),root=Path.Combine(fixture,"root"),host=Path.Combine(fixture,"game");
            Directory.CreateDirectory(host);string exe=Path.Combine(host,"BrownDust II.exe");File.WriteAllText(exe,"version-one");
            string overlay=DailyLoadingFileTransaction.Overlay(root,exe);Directory.CreateDirectory(overlay);File.WriteAllText(Path.Combine(overlay,"BrownDust II.exe"),"older-sandbox-version");
            var rules=DailySandboxGameFiles.LegacyRules(exe).ToList();
            for(int attempt=0;attempt<3;attempt++){
                string id=Guid.NewGuid().ToString("N"),destination=Path.Combine(fixture,"capture-"+attempt);Directory.CreateDirectory(destination);
                var control=new DailyLoadingExperiment.Control(id,"legacy-files",destination,DateTime.UtcNow.AddMinutes(1).Ticks,0,true);
                bool rejected=false;
                try{await DailyLoadingFileTransaction.PrepareAsync(control,exe,root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Remove(r);return Task.CompletedTask;},CancellationToken.None,
                    phase=>{if(attempt==1&&phase=="moved:BrownDust II.exe")File.WriteAllText(exe,"version-two");});}catch(IOException e)when(e.Message.Contains("主机游戏")){rejected=true;}
                check(rejected==(attempt==1),"C refuses host update during preparation, but accepts a completed host update on the next launch: "+attempt);
                if(!rejected)check(!File.Exists(Path.Combine(overlay,"BrownDust II.exe"))&&DailyLoadingFileTransaction.Read(root)!.HostFiles["BrownDust II.exe"]==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(exe))),"C launches from the exact current host version after repeated old overlay restoration: "+attempt);
                await DailyLoadingFileTransaction.RecoverAsync(root,"Fixture",()=>Task.FromResult(string.Join('\n',rules)),()=>Task.FromResult(true),r=>{rules.Add(r);return Task.CompletedTask;},CancellationToken.None);
                check(File.ReadAllText(Path.Combine(overlay,"BrownDust II.exe"))=="older-sandbox-version"&&rules.Count==5,"C cross-version retry preserves all original files and rules: "+attempt);
            }
        }
    }
}

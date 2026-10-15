using System.Text.Json;
using Dustweave;
internal static class LogCleanupCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool condition,string label){if(!condition)throw new Exception(label);cases.Add("log-cleanup: "+label);}
        string root=Path.Combine(output,"log-cleanup");Directory.CreateDirectory(root);var now=DateTime.UtcNow;
        string FileAt(string relative,string content,int age=15){string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,content);File.SetLastWriteTimeUtc(path,now.AddDays(-age));return path;}
        string Step(string state="observed_expected_ui",int age=15,string reason="navigate"){
            string id=Guid.NewGuid().ToString("N");
            FileAt($"live/steps/{id}/intent.json",JsonSerializer.Serialize(new{Id=id,Reason=reason}),age);
            FileAt($"live/steps/{id}/result.json",JsonSerializer.Serialize(new{state,id,engine="dotnet-driver-v1",after=new{diagnostic="fixture"}}),age);
            FileAt($"live/steps/{id}/before.json","{}",age);return id;
        }
        var policy=new DailyLogPolicy();Check(policy.Automatic&&policy.Days==7&&policy.LimitMiB==512,"sensible automatic retention defaults");
        DailyLogCleanup.Save(root,new(false,30,1024));Check(DailyLogCleanup.Load(root)==new DailyLogPolicy(false,7,1024),"legacy retention migrates to seven-day maximum");
        Check(new DailyLogPolicy(true,0,int.MaxValue).Normalize()==new DailyLogPolicy(true,2,4096),"retention bounds cannot erase recent evidence");
        string completed=Step(),pending=Step("unknown_timeout"),recent=Step(age:1),unproven=Step("dispatched_only");
        string businessId=Guid.NewGuid().ToString("N");string business=FileAt($"live/managed-business/{businessId}.json",JsonSerializer.Serialize(new{id=businessId,state="unknown"}));
        string owned=Step(reason:"business:"+businessId+"|confirm"),missing=Step(reason:"business:"+Guid.NewGuid().ToString("N")+"|confirm");
        string oldLog=FileAt("runtime.log","old log");string newLog=FileAt("live/diagnostics/timeline.jsonl","{}",1);
        string vault=FileAt("accounts/private.log","account-secret"),queue=FileAt("live/queues/fixture/result.json","recovery"),exported=FileAt("diagnostics-export.zip","saved ZIP");
        string tool=FileAt("tools/fishing/runtime.log.previous","old tool log");
        var plan=await DailyLogCleanup.ScanAsync(root,policy);
        Check(plan.Files==17,"all expired diagnostic steps, including errors and legacy dispatches, are selected");
        Check(plan.Units.All(u=>!u.Path.Contains(recent)),"recent evidence remains protected");
        var result=await DailyLogCleanup.ApplyAsync(plan);
        Check(result.Files==17&&result.Bytes==plan.ReclaimableBytes,"manual preview matches actual reclaimed bytes");
        Check(!Directory.Exists(Path.Combine(root,"live","steps",completed))&&!File.Exists(oldLog)&&!File.Exists(tool),"empty completed-step folder and selected logs removed");
        Check(new[]{vault,queue,business,newLog,exported}.All(File.Exists),"accounts, queues, transactions, recent logs and exported ZIPs survive");
        string changed=FileAt("connection.log","old");plan=await DailyLogCleanup.ScanAsync(root,policy);File.AppendAllText(changed," changed");
        await DailyLogCleanup.ApplyAsync(plan);Check(File.Exists(changed),"a file updated after preview is retained");
        File.SetLastWriteTimeUtc(changed,now.AddDays(-15));plan=await DailyLogCleanup.ScanAsync(root,policy);
        using(var held=new FileStream(changed,FileMode.Open,FileAccess.Read,FileShare.Read)){result=await DailyLogCleanup.ApplyAsync(plan);Check(File.Exists(changed)&&result.Skipped>0,"open logs are skipped without waiting");}
        var guarded=Step();plan=await DailyLogCleanup.ScanAsync(root,policy);FileAt($"live/steps/{guarded}/extra.json","new state");await DailyLogCleanup.ApplyAsync(plan);
        Check(File.Exists(Path.Combine(root,"live","steps",guarded,"result.json")),"new files invalidate a completed-step preview");
        string outside=Path.Combine(output,"outside.log");File.WriteAllText(outside,"outside");File.SetLastWriteTimeUtc(outside,now.AddDays(-15));
        var stamp=new FileInfo(outside);var forged=new DailyLogPlan(root,policy,now,7,[new(outside,false,[new(outside,stamp.Length,stamp.LastWriteTimeUtc,stamp.CreationTimeUtc)])],0);
        await DailyLogCleanup.ApplyAsync(forged);Check(File.Exists(outside),"a plan cannot delete outside its data root");
        string link=Path.Combine(root,"tools","linked");Directory.CreateSymbolicLink(link,Path.GetDirectoryName(outside)!);
        plan=await DailyLogCleanup.ScanAsync(root,policy);Check(plan.Units.All(u=>!u.Path.Contains("linked")),"symlink trees are excluded");
        // A completed UI step may become unresolved between preview and cleanup.
        string terminalId=Guid.NewGuid().ToString("N");string journal=FileAt($"live/business/{terminalId}.json",JsonSerializer.Serialize(new{id=terminalId,state="completed"}));
        string terminal=Step(reason:"preview-business:"+terminalId+"|preview");plan=await DailyLogCleanup.ScanAsync(root,policy);
        File.WriteAllText(journal,JsonSerializer.Serialize(new{id=terminalId,state="unknown"}));await DailyLogCleanup.ApplyAsync(plan);
        Check(!File.Exists(Path.Combine(root,"live","steps",terminal,"result.json")) && File.Exists(journal),"expired diagnostics do not remove the independent pending transaction guard");
        string budget=FileAt("oversized.log","",3);using(var stream=File.OpenWrite(budget))stream.SetLength(129L*1024*1024);File.SetLastWriteTimeUtc(budget,now.AddDays(-3));
        plan=await DailyLogCleanup.ScanAsync(root,new(true,30,128));Check(plan.Units.Any(u=>u.Path==budget),"capacity target removes older records before retention expiry");
        File.SetLastWriteTimeUtc(budget,now.AddDays(-1));plan=await DailyLogCleanup.ScanAsync(root,new(true,30,128));Check(plan.Units.All(u=>u.Path!=budget),"capacity never overrides the 48-hour evidence floor");
        using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{await DailyLogCleanup.ScanAsync(root,policy,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"scan supports immediate cancellation");
        bool stale=false;try{DailyLogCleanup.Apply(plan with{At=now.AddHours(-1)});}catch(InvalidOperationException){stale=true;}Check(stale,"stale previews require another scan");
        string odd=Step();FileAt($"live/steps/{odd}/result.json","broken");plan=await DailyLogCleanup.ScanAsync(root,policy);Check(plan.Units.Any(u=>u.Path.Contains(odd)),"malformed diagnostics also expire instead of accumulating forever");
        string query=FileAt("live/reward-queries/123.json","{}"),evt=FileAt("live/event-journal/123-event.json","{}"),diagnostic=FileAt("live/diagnostics/error.json","{}"),tradeRead=FileAt("live/trade-quote-reads/failed.json","{}"),tradeUi=FileAt("trade/diagnostics/capture.json","{}"),travel=FileAt("live/travel-diagnostics/failed.json","{}");
        plan=await DailyLogCleanup.ScanAsync(root,policy);await DailyLogCleanup.ApplyAsync(plan);
        Check(new[]{query,evt,diagnostic,tradeRead,tradeUi,travel}.All(p=>!File.Exists(p)),"queries, event snapshots and trade/route diagnostics expire too");
        string guard=Step("unknown_timeout",reason:"business:"+businessId+"|confirm");
        FileAt($"live/steps/{guard}/result.json",JsonSerializer.Serialize(new {state="unknown_timeout",receipt=new {Id=guard,Status="observed_after_dispatch"}}));
        plan=await DailyLogCleanup.ScanAsync(root,policy);await DailyLogCleanup.ApplyAsync(plan);
        Check(DailyJson.TryRead<System.Text.Json.Nodes.JsonObject>(Path.Combine(root,"live","step-recovery",guard+".json"))?["receipt"]?["Id"]?.GetValue<string>()==guard,"pending receipt survives diagnostic expiry in independent recovery storage");
        string equipmentOld=FileAt("live/diagnostics/equipment/step.before.json","{}"),equipmentRecent=FileAt("live/diagnostics/equipment/capture-evidence.json","{}",1);
        string equipmentJournal=FileAt("tools/equipment/pending-plan.json","{\"state\":\"pending\"}");
        plan=await DailyLogCleanup.ScanAsync(root,policy);await DailyLogCleanup.ApplyAsync(plan);
        Check(!File.Exists(equipmentOld)&&File.Exists(equipmentRecent)&&File.Exists(equipmentJournal),"integrated equipment logs share retention without deleting pending execution state");
        string legacy=Path.Combine(output,"legacy-equipment"),host=Path.Combine(output,"equipment-log-host");
        string LegacyFile(string relative,int age=15){string p=Path.Combine(legacy,relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,"{}");File.SetLastWriteTimeUtc(p,now.AddDays(-age));return p;}
        var legacyOld=new[]{LegacyFile("jobs/old.json"),LegacyFile("live/steps/old/before.json"),LegacyFile("receipts/plan/action.json"),LegacyFile("capture-evidence.json")};
        string legacyFresh=LegacyFile("diagnostics/recent.log",1),legacyHeld=LegacyFile("jobs/held.json");
        var preserved=new[]{LegacyFile("pending-plan.json"),LegacyFile("inventory.json"),LegacyFile("live/command.json"),LegacyFile("jobs/unknown.bin")};
        Check(DailyLogCleanup.HasLegacyEquipmentLogs(legacy),"legacy logs trigger the first idle cleanup without opening the tool");
        using(var held=new FileStream(legacyHeld,FileMode.Open,FileAccess.Read,FileShare.Read))await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(host,legacy);
        Check(legacyOld.All(p=>!File.Exists(p))&&!File.Exists(legacyFresh)&&File.Exists(legacyHeld),"migration imports closed logs and skips files still in use");
        Check(preserved.All(File.Exists)&&!Directory.Exists(Path.Combine(legacy,"receipts"))&&!Directory.Exists(Path.Combine(legacy,"live/steps")),"empty log directories disappear while recovery and unknown files survive");
        string imported=Path.Combine(host,"live/diagnostics/equipment");
        Check(Directory.GetFiles(imported).Count(p=>File.GetLastWriteTimeUtc(p)<now.AddDays(-7))==4,"migration preserves expired log ages");
        // A second run must preserve both versions if the old tool recreates a log.
        LegacyFile("capture-evidence.json",1);await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(host,legacy);
        plan=await DailyLogCleanup.ScanAsync(host,policy);await DailyLogCleanup.ApplyAsync(plan);
        Check(Directory.GetFiles(imported).Length==2&&Directory.GetFiles(imported).All(p=>File.GetLastWriteTimeUtc(p)>now.AddDays(-2)),"central cleanup deletes expired imports and retains recent versions without overwriting");
        string externalDir=Path.Combine(output,"legacy-external");Directory.CreateDirectory(externalDir);string external=Path.Combine(externalDir,"private.json");File.WriteAllText(external,"{}");
        Directory.CreateSymbolicLink(Path.Combine(legacy,"diagnostics"),externalDir);
        await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(host,legacy);
        Check(File.Exists(external)&&Directory.GetFiles(imported).Length==2,"legacy migration never traverses a linked log folder");
        string linkedRoot=Path.Combine(output,"legacy-linked-root");Directory.CreateSymbolicLink(linkedRoot,legacy);
        Check(!DailyLogCleanup.HasLegacyEquipmentLogs(linkedRoot),"linked legacy roots are excluded before scheduling cleanup");
        bool importCancelled=false;try{await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(host,legacy,cancel.Token);}catch(OperationCanceledException){importCancelled=true;}
        Check(importCancelled,"legacy migration cancels promptly when automation resumes");
    }
}

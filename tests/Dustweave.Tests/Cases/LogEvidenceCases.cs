using System.Text.Json.Nodes;
using Dustweave;
internal static class LogEvidenceCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok,string label) { if(!ok)throw new Exception(label);cases.Add("log-evidence: "+label); }
        string root=Path.Combine(output,"log-evidence");Directory.CreateDirectory(root);
        JsonObject Frame() => new() { ["ProcessId"]=123,["Instance"]="fixture",["AccountKey"]="a",["Surfaces"]=new JsonArray(new JsonObject { ["Targets"]=new string('x',16000) }) };
        string Step(string state="dispatched_only", string reason="navigate")
        {
            string id=Guid.NewGuid().ToString("N"), folder=Path.Combine(root,"live","steps",id);
            DailyJson.Write(Path.Combine(folder,"intent.json"),new { Id=id,Reason=reason });
            DailyJson.Write(Path.Combine(folder,"before.json"),Frame());
            DailyJson.Write(Path.Combine(folder,"transport.json"),new { before=Frame() });
            DailyJson.Write(Path.Combine(folder,"result.json"),new { id,state,after=Frame(),receipt=new { Id=id,Status="observed_after_dispatch",After=Frame() } });
            return folder;
        }
        string success;
        using(var scope=new DailyLogEvidence(root))
        {
            success=Step();
            Check(File.Exists(Path.Combine(success,"before.json")),"in-flight snapshots survive a process crash");
            await Task.Yield();scope.Complete();
        }
        Check(DailyLogEvidence.HasPending(root)&&File.Exists(Path.Combine(success,"before.json")),"stage completion queues durable work without blocking on compaction");
        await DailyLogEvidence.FlushAsync(root);
        Check(!DailyLogEvidence.HasPending(root),"idle worker drains persisted compaction work after restart");
        var result=DailyJson.TryRead<JsonObject>(Path.Combine(success,"result.json"))!;
        Check(result["diagnostic_storage"]?.GetValue<string>()=="summary-v1"&&!File.Exists(Path.Combine(success,"before.json"))&&!File.Exists(Path.Combine(success,"transport.json")),"outer success compacts dispatch-only steps");
        Check(result["receipt"]?["Id"]!=null&&result["after"]==null&&result["receipt"]?["After"]?["Surfaces"]==null,"summary retains receipt identity without UI trees");
        string interrupted;
        using(var scope=new DailyLogEvidence(root)){ interrupted=Step(); }
        await DailyLogEvidence.FlushAsync(root);
        Check(File.Exists(Path.Combine(interrupted,"before.json")),"interruption never promotes a dispatched command to success");
        var sequence=new List<string>();
        using(var scope=new DailyLogEvidence(root))
        {
            var sameTime=DateTime.UtcNow.AddMinutes(-1);
            for(int i=0;i<12;i++)
            {
                sequence.Add(Step(i==7?"unknown_timeout":"dispatched_only"));
                File.SetLastWriteTimeUtc(Path.Combine(sequence[^1],"intent.json"),sameTime);
            }
            scope.Complete();
        }
        await DailyLogEvidence.FlushAsync(root);
        Check(Enumerable.Range(4,6).All(i=>File.Exists(Path.Combine(sequence[i],"before.json"))),"recovered failure keeps full evidence plus three preceding and two following steps");
        Check(!File.Exists(Path.Combine(sequence[0],"before.json"))&&!File.Exists(Path.Combine(sequence[11],"before.json")),"unrelated successful steps remain compact");
        string[] oldSteps=Enumerable.Range(0,12).Select(i=>Step(i==7?"unknown_timeout":"dispatched_only")).ToArray();
        foreach(string folder in oldSteps)File.SetLastWriteTimeUtc(Path.Combine(folder,"intent.json"),DateTime.UtcNow.Date);
        DailyJson.Write(Path.Combine(root,"live","log-compaction","legacy-ties.json"),new { complete=true,
            paths=oldSteps.Reverse().SelectMany(folder=>Directory.GetFiles(folder)).Select(p=>Path.GetRelativePath(root,p)).ToArray() });
        await DailyLogEvidence.FlushAsync(root);
        Check(oldSteps.All(folder=>File.Exists(Path.Combine(folder,"before.json"))),"legacy tied timestamps preserve all ambiguous failure neighbors");
        string business=Guid.NewGuid().ToString("N"),businessPath=Path.Combine(root,"live","managed-business",business+".json"),pending;
        DailyJson.Write(businessPath,new { id=business,state="unknown" });
        using(var scope=new DailyLogEvidence(root)){pending=Step("observed_expected_ui","business:"+business+"|confirm");scope.Complete();}
        await DailyLogEvidence.FlushAsync(root);
        Check(File.Exists(Path.Combine(pending,"before.json")),"unresolved business prevents compaction despite outer success");
        string query=Path.Combine(root,"live","weekly-npc-queries",Guid.NewGuid().ToString("N")+".json");
        using(var scope=new DailyLogEvidence(root))
        {
            DailyJson.Write(query,new { state="completed",before=Frame(),after=Frame(),result=new{value="verified"} });
            scope.Complete();
        }
        await DailyLogEvidence.FlushAsync(root);
        Check(new FileInfo(query).Length<1024,"successful queries no longer duplicate entire observations");
        string visiblePopup=Path.Combine(root,"live","managed-business",Guid.NewGuid().ToString("N")+".json");
        using(var scope=new DailyLogEvidence(root))
        {
            DailyJson.Write(visiblePopup,new { state="completed",role=DailyFreeDrawProof.Role,before=new{Frame=Frame()},after=new{Frame=Frame()} });
            scope.Complete();
        }
        await DailyLogEvidence.FlushAsync(root);
        Check(DailyJson.TryRead<JsonObject>(visiblePopup)?["before"]?["Frame"]?["Surfaces"]!=null,"settled draw with an unclosed result popup retains its recovery UI");
        string closed=Path.Combine(root,"live","business",Guid.NewGuid().ToString("N")+".json");
        using(var scope=new DailyLogEvidence(root))
        {
            DailyJson.Write(closed,new { state="completed",presentation_closed=true,before=new{Frame=Frame(),Readings=new[]{"proof"}},after=new{Frame=Frame()},events=new[]{new{Values=new[]{"receipt"},Frame=Frame()}},result=new{cost=10} });
            scope.Complete();
        }
        await DailyLogEvidence.FlushAsync(root);
        var facts=DailyJson.TryRead<JsonObject>(closed)!;
        Check(facts["before"]?["Readings"]!=null&&facts["events"]?[0]?["Values"]!=null&&facts["before"]?["Frame"]?["Surfaces"]==null,"settled business retains accounting and identity proof, discards UI trees");
        File.SetLastWriteTimeUtc(closed,DateTime.UtcNow.AddDays(-8));File.SetLastWriteTimeUtc(businessPath,DateTime.UtcNow.AddDays(-8));
        await DailyLogEvidence.MaintainAsync(root);
        facts=DailyJson.TryRead<JsonObject>(closed)!;
        Check(facts["result"]?["cost"]?.GetValue<int>()==10&&facts["before"]?["Frame"]?["AccountKey"]?.GetValue<string>()=="a"&&facts["before"]?["Readings"]==null&&facts["events"]==null,"seven-day settled history retains business outcome and historical ownership, not detailed snapshots");
        Check(DailyJson.TryRead<JsonObject>(businessPath)?["state"]?.GetValue<string>()=="unknown","pending recovery guard is not expired as a diagnostic");
        Check(File.GetLastWriteTimeUtc(closed)<DateTime.UtcNow.AddDays(-7),"compaction does not renew retention age");
        string rejected=Path.Combine(root,"live","business",Guid.NewGuid().ToString("N")+".json");
        DailyJson.Write(rejected,new{state="server_rejected",before=new{Frame=Frame()},error="fixture rejection"});
        File.SetLastWriteTimeUtc(rejected,DateTime.UtcNow.AddDays(-8));
        File.SetLastWriteTimeUtc(visiblePopup,DateTime.UtcNow.AddDays(-8));
        await DailyLogEvidence.MaintainAsync(root);
        Check(DailyJson.TryRead<JsonObject>(rejected)?["error"]?.GetValue<string>()=="fixture rejection"&&DailyJson.TryRead<JsonObject>(rejected)?["before"]?["Frame"]?["Surfaces"]==null,"expired rejected operation retains its no-replay outcome without full diagnostics");
        Check(DailyJson.TryRead<JsonObject>(visiblePopup)?["before"]?["Frame"]?["Surfaces"]==null,"old completed popup diagnostics expire too");
        string cancelled;
        using(var scope=new DailyLogEvidence(root)) { cancelled=Step();File.Delete(Path.Combine(cancelled,"result.json"));scope.Complete(); }
        await DailyLogEvidence.FlushAsync(root);
        Check(File.Exists(Path.Combine(cancelled,"before.json")),"missing result remains a full diagnostic, never guessed successful");
        using var stop=new CancellationTokenSource();stop.Cancel();bool stopped=false;
        try { await DailyLogEvidence.MaintainAsync(root,stop.Token); } catch(OperationCanceledException) { stopped=true; }
        Check(stopped,"idle maintenance can yield to active work");
    }
}

using System.IO.Compression;
using System.Text.Json;
using Dustweave;
internal static class DiagnosticExportCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("diagnostic-export: "+label);}
        string root=Path.Combine(output,"diagnostic-source");Directory.CreateDirectory(Path.Combine(root,"live","diagnostics"));
        File.WriteAllText(Path.Combine(root,"ui-operation-error.json"),"{\"error\":\"suite.owner-exited\",\"accessToken\":\"secret-value\",\"nested\":{\"password\":\"pw-value\"}}");
        File.WriteAllText(Path.Combine(root,"live","diagnostics","timeline.jsonl"),"{\"kind\":\"start\",\"session_token\":\"token-value\"}\n{\"bad\":");
        File.WriteAllText(Path.Combine(root,"credentials.json"),"private-vault");
        Directory.CreateDirectory(Path.Combine(root,"plugins"));File.WriteAllText(Path.Combine(root,"plugins","secret.json"),"private-plugin");
        File.WriteAllText(Path.Combine(root,"queue-worker.log"),"Authorization: Bearer secret-bearer\npassword=secret-line\nnormal operation");
        string destination=Path.Combine(output,"diagnostics.zip");
        var result=await DailyDiagnosticExport.CreateAsync(root,destination,"synthetic diagnostic summary","1.1.0-beta");
        Check(result.Files==3,"exports the allowlisted operational files");
        using(var archive=ZipFile.OpenRead(destination)){
            Check(archive.GetEntry("manifest.json")!=null&&archive.GetEntry("summary.txt")!=null,"archive contains an inventory and context");
            string all=string.Join('\n',archive.Entries.Select(e=>{using var r=new StreamReader(e.Open());return r.ReadToEnd();}));
            Check(!all.Contains("secret-value")&&!all.Contains("pw-value")&&!all.Contains("token-value")&&!all.Contains("secret-bearer")&&!all.Contains("secret-line"),"structured and text credentials are redacted");
            Check(!all.Contains("private-vault")&&!all.Contains("private-plugin"),"account and plugin stores are outside the export boundary");
            Check(all.Contains("suite.owner-exited")&&all.Contains("normal operation"),"diagnostic reasons remain intact");
            Check(all.Contains("incomplete JSON line omitted"),"in-flight JSON line is not copied unredacted");
        }
        byte[] before=File.ReadAllBytes(destination);bool rejected=false;
        string failureRoot=Path.Combine(output,"diagnostic-failure-source");
        var trail = new DailyDiagnosticTrail(failureRoot, () => 1, () => DateTime.UtcNow.Ticks, 1);
        trail.Observe(CommandDriverCases.Frame());
        for (int i=0;i<20;i++) trail.Failure("failure", new System.Text.Json.Nodes.JsonObject { ["error"]="synthetic popup failure" });
        string faultDirectory=Path.Combine(failureRoot,"live","diagnostics","failures");
        string equipmentDiagnostics=Path.Combine(failureRoot,"live","diagnostics","equipment");Directory.CreateDirectory(equipmentDiagnostics);
        File.WriteAllText(Path.Combine(equipmentDiagnostics,"step.result.json"),"{\"state\":\"failed\",\"token\":\"equipment-secret\"}");
        DailyJson.Write(Path.Combine(failureRoot,"live","absorption-history","fixture.json"),new { Maps=2,Visits=new[]{new {Pack=8,Map=81,AlongNpc=true}} });
        DailyJson.Write(Path.Combine(failureRoot, "connection-access.json"), new { phase = "send-request", state = "disconnected", helperExitCode = 1 });
        Check(Directory.GetFiles(faultDirectory).Length==1,"repeated identical failures retain one bounded snapshot");
        foreach (string name in new[] {"pass-claims-latest.json", "mission-inspection-latest.json"})
            File.WriteAllText(Path.Combine(failureRoot,"live",name),"{\"state\":\"partial\",\"pass_issues\":[{\"pass_id\":501,\"detail\":\"synthetic incompatible pass\"}]}");
        var withFault=await DailyDiagnosticExport.CreateAsync(failureRoot,Path.Combine(output,"with-failure.zip"),"","test");
        using(var archive=ZipFile.OpenRead(withFault.Path)) {
            using(var equipmentLog=new StreamReader(archive.GetEntry("logs/main/live/diagnostics/equipment/step.result.json")!.Open()))Check(!equipmentLog.ReadToEnd().Contains("equipment-secret"),"integrated equipment evidence is exported and redacted with host diagnostics");
            Check(archive.GetEntry("logs/main/connection-access.json") != null, "elevated handshake phase and helper exit status are exported");
            Check(archive.GetEntry("logs/main/live/absorption-history/fixture.json") != null, "confirmed absorption map report is included in diagnostic export");
            Check(archive.Entries.Any(e=>e.FullName.StartsWith("logs/main/live/diagnostics/failures/")),"failure snapshot survives observation rotation and is exported");
            Check(new[]{"pass-claims-latest.json", "mission-inspection-latest.json"}.All(n=>archive.GetEntry("logs/main/live/"+n)!=null),"partial pass and mission inspection results survive export");
        }
        try{await DailyDiagnosticExport.CreateAsync(root,destination,"","1.1.0-beta");}catch(IOException){rejected=true;}
        Check(rejected&&before.SequenceEqual(File.ReadAllBytes(destination)),"existing export is preserved");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"cancelled.zip"),"","1.1.0-beta",cancelled.Token);}catch(OperationCanceledException){}Check(!File.Exists(Path.Combine(output,"cancelled.zip")),"cancelled export leaves no archive");}
        Check(!DailyDiagnosticExport.Allowed("../accounts.json")&&!DailyDiagnosticExport.Allowed("tools/password.json")&&!DailyDiagnosticExport.Allowed("tools/runtime.dll"),"traversal credentials and binaries are excluded");
        File.WriteAllText(Path.Combine(root,"startup-error.json"),"{incomplete");
        using(var locked=new FileStream(Path.Combine(root,"ui-operation-error.json"),FileMode.Open,FileAccess.Read,FileShare.None)){
            var partial=await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"partial.zip"),"","1.1.0-beta");
            Check(partial.Skipped>=2&&partial.Files==2,"locked and incomplete files do not abort other diagnostics");
        }
        Check(DailyIssues.Classify("suite.owner-exited").Code=="owner-ended","owner exit is not account change");
        Check(DailyIssues.Classify("原队列与当前账号、角色或重置周期不一致，请切回原账号；已跨日则开始新队列。").Code=="resume-context"
            && DailyIssues.Classify("原队列的账号、进程或重置周期已变化，请开始新队列。").Code=="resume-context",
            "current and legacy resume failures explain recovery instead of appearing as unknown issues");
        Check(DailyIssues.Classify(new NullReferenceException()).Code=="internal-error",
            "programming failure is described as an internal error, not failed login or user pause");
        Check(DailyIssues.Classify("Need one observed GameFieldDefaultUI; found 0").Code=="field-transition"
            && DailyIssues.Classify("field.not-ready: waiting").Code=="field-transition","historical and current field transition errors explain map recovery");
        Check(DailyIssues.Classify("uncertain: Need one observed GameFieldDefaultUI; found 0").Code=="uncertain","uncertain dispatch still takes priority over map recovery");
        Check(DailyIssues.Classify("suite.identity-unavailable").Code=="identity-unavailable","missing identity is distinguishable");
        Check(DailyIssues.Classify("尚未找到完整登录会话，请先在游戏内登录。").Code == "local-login-incomplete", "legacy session precheck is not unknown or expired login");
        Check(DailyIssues.Classify("connection.login_identity_unavailable").Code == "live-login-unavailable", "live identity timeout differs from local credential readiness");
        Check(DailyDiagnosticExport.Allowed("live/diagnostics/login-failed.json") && DailyDiagnosticExport.Allowed("login-identity-error.json"), "identity evidence is exportable without the credential store");
        Check(DailyIssues.Classify("UI observation interrupted after dispatch; no replay: timeout").Code=="uncertain","uncertain dispatch has priority over timeout retry guidance");
        Check(DailyIssues.Classify(new OperationCanceledException()).Code=="cancelled","user cancellation is not a connection failure");
        Check(DailyIssues.Classify("会话已停止，请在日常助手重新选择功能。").Code=="legacy-session","old generic message does not invent a root cause");
        Check(!DailyDiagnosticExport.Redact("debug: {\"token\":\"hidden-token\"} api_key=hidden-key",false).Contains("hidden-"),"prefixed JSON and generic token text are scrubbed");
        Check(!DailyDiagnosticExport.Redact("\"token=hidden-scalar\"",true).Contains("hidden-scalar"),"scalar JSON is scrubbed");
        string tool=Path.Combine(output,"diagnostic-tool");Directory.CreateDirectory(tool);File.WriteAllText(Path.Combine(tool,"runtime.log"),"tool evidence");
        var combined=await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"combined.zip"),"","1.1.0-beta",additionalSources:new Dictionary<string,string>{{"tool-fixture",tool}});
        using(var archive=ZipFile.OpenRead(combined.Path))Check(archive.GetEntry("logs/tool-fixture/runtime.log")!=null,"related tool logs keep distinct source labels");
        string isolated=Path.Combine(output,"isolated-empty");
        Check(DailyDiagnosticExport.RelatedSources(output,isolated).Count==0,"absent sandbox and tool stores are harmless");
        string box=Path.Combine(isolated,"Dustweave_synthetic");Directory.CreateDirectory(box);
        DailyJson.Write(Path.Combine(box,"registration-mismatch.json"),new {reader="SbieApi_QueryConf_UTF16",configuredRoot="D:\\隔离空间",expectedRoot="D:\\游戏空间"});
        File.WriteAllText(Path.Combine(box,"instance.json"),"private-registration");
        var roots=DailyDiagnosticExport.RelatedSources(output,isolated);
        var registrationExport=await DailyDiagnosticExport.CreateAsync(Path.Combine(output,"empty-main"),Path.Combine(output,"registration-export.zip"),"","test",additionalSources:roots);
        using(var archive=ZipFile.OpenRead(registrationExport.Path)){
            Check(archive.Entries.Any(e=>e.FullName.EndsWith("/registration-mismatch.json")),"sandbox path diagnostics survive missing inner account binding");
            Check(!archive.Entries.Any(e=>e.FullName.EndsWith("/instance.json")),"exporting path evidence never includes sandbox account registration");
        }
        Check(!DailyDiagnosticExport.Allowed("parallel-workers/a/accounts.json")&&!DailyDiagnosticExport.Allowed("tools/fishing/preferences.json")&&!DailyDiagnosticExport.Allowed("live/queue-bootstrap/a/command.json"),"nested vaults preferences and raw commands remain excluded");
        Check(DailyDiagnosticExport.Allowed("runtime.log.1")&&DailyDiagnosticExport.Allowed("suite/diagnostics/ownership.jsonl.3")&&!DailyDiagnosticExport.Allowed("tools/evidence.jsonl.dll"),"only recognized text rotations are included");
        Directory.CreateDirectory(Path.Combine(root,"live","queue-bootstrap","sample"));File.WriteAllText(Path.Combine(root,"live","queue-bootstrap","sample","result.json"),"{\"state\":\"failed\",\"error\":\"owner\"}");
        File.WriteAllText(Path.Combine(root,"connection.json"),"{\"ProcessId\":42,\"StartTicks\":123,\"Fingerprint\":\"fixture\"}");
        File.WriteAllText(Path.Combine(root,"run-error.json"),"{\"error\":\"fixture failure\"}");
        File.WriteAllText(Path.Combine(root,"runtime.log.1"),"previous connection attempt");
        DailySuiteDiagnostics.Record(root,"activation-request",new{OwnerProcessId=42,OwnerStartTicks=123,ownerCheck=new BD2Daily.SuiteOwnerObservation{State="unreadable",NativeError=5}});
        string journal=Path.Combine(root,"suite","diagnostics","ownership.jsonl");
        using(var record=JsonDocument.Parse(File.ReadAllText(journal)))Check(record.RootElement.GetProperty("detail").GetProperty("ownerCheck").GetProperty("NativeError").GetInt32()==5,"ownership journal serializes runtime evidence fields");
        File.AppendAllText(journal,new string(' ',512*1024));DailySuiteDiagnostics.Record(root,"runtime-transition",new{State="paused"});
        Check(File.Exists(journal+".1")&&new FileInfo(journal).Length<4096,"owner history rotates without dropping the latest event");
        string blocked=Path.Combine(output,"diagnostic-unwritable");Directory.CreateDirectory(blocked);File.WriteAllText(Path.Combine(blocked,"suite"),"not a folder");
        DailySuiteDiagnostics.Record(blocked,"synthetic",new{});Check(true,"diagnostic write failure does not stop connection");
        string oversized=Path.Combine(root,"live","diagnostics","large.log");using(var f=File.Create(oversized))f.SetLength(8*1024*1024+1);
        File.AppendAllText(oversized,"\nrecent-tail-evidence token=tail-secret\n");
        var bounded=await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"bounded.zip"),"","1.1.0-beta");
        using(var archive=ZipFile.OpenRead(bounded.Path)){
            using var tail=new StreamReader(archive.GetEntry("logs/main/live/diagnostics/large.log")!.Open());string text=tail.ReadToEnd();
            Check(text.Contains("recent-tail-evidence")&&!text.Contains("tail-secret")&&text.Length<2*1024*1024,"large text log retains a bounded redacted tail");
            using var manifest=JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("inventory").EnumerateArray().Any(x=>x.GetProperty("path").GetString()!.EndsWith("large.log")&&x.GetProperty("truncated").GetBoolean()),"partial logs are explicitly marked in manifest");
            var coverage=manifest.RootElement.GetProperty("coverage")[0];
            Check(coverage.GetProperty("connection").GetBoolean()&&coverage.GetProperty("ownerHistory").GetBoolean()&&coverage.GetProperty("queueBootstrap").GetBoolean(),"export reports coverage instead of treating no skipped files as complete evidence");
            Check(archive.GetEntry("logs/main/run-error.json")!=null&&archive.GetEntry("logs/main/runtime.log.1")!=null&&archive.GetEntry("logs/main/suite/diagnostics/ownership.jsonl.1")!=null,"operational errors and connection rotations are retained");
            using var summary=new StreamReader(archive.GetEntry("summary.txt")!.Open());Check(summary.ReadToEnd().Contains("suite.owner-exited"),"summary includes historical operation failure even if current connection is normal");
        }
        string pressure=Path.Combine(output,"diagnostic-pressure");Directory.CreateDirectory(Path.Combine(pressure,"live","steps","sample"));
        for(int i=0;i<2005;i++)File.WriteAllText(Path.Combine(pressure,"live","steps","sample",i+".json"),"{}");
        File.WriteAllText(Path.Combine(pressure,"ui-operation-error.json"),"{\"error\":\"critical evidence\"}");File.SetLastWriteTimeUtc(Path.Combine(pressure,"ui-operation-error.json"),DateTime.UtcNow.AddDays(-1));
        string[] important = ["live/managed-business/pending.json", "live/business/legacy.json", "live/trade-quote-reads/failed.json", "trade/diagnostics/capture.json", "trade/capture/sample.evidence.json", "trade/executions/account/day/execution.json",
            "live/travel-diagnostics/failed.json", "live/route-atlas/account/2002.json", "live/server-collection/account/week/2002.json",
            "live/weekly-npc-recovery/failed.json", "live/weekly-npc-queries/failed.json"];
        foreach (var relative in important) { string path=Path.Combine(pressure,relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,"{\"error\":\"native stock failure\",\"token\":\"hidden-trade-secret\"}"); File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-1)); }
        var pressured=await DailyDiagnosticExport.CreateAsync(pressure,Path.Combine(output,"pressure.zip"),"","fixture",additionalSources:new Dictionary<string,string>{{"tool-fixture",tool}});
        using(var archive=ZipFile.OpenRead(pressured.Path))Check(pressured.Files==2000&&pressured.Skipped>0&&archive.GetEntry("logs/main/ui-operation-error.json")!=null&&archive.GetEntry("logs/tool-fixture/runtime.log")!=null,"old critical evidence and other tool logs survive a large step archive");
        using(var archive=ZipFile.OpenRead(pressured.Path)) {
            Check(important.All(p=>archive.GetEntry("logs/main/"+p)!=null),"business journals, trade, NPC and route/collection evidence survive newer step-file pressure");
            using var travelManifest=JsonDocument.Parse(new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd());
            var travelCoverage=travelManifest.RootElement.GetProperty("coverage")[0];
            Check(travelCoverage.GetProperty("travelDiagnostics").GetBoolean()&&travelCoverage.GetProperty("routeAtlas").GetBoolean()&&travelCoverage.GetProperty("serverCollection").GetBoolean(),"route evidence coverage is visible in diagnostic export");
            using var log=new StreamReader(archive.GetEntry("logs/main/trade/diagnostics/capture.json")!.Open());
            Check(!log.ReadToEnd().Contains("hidden-trade-secret"),"trade diagnostic secrets remain redacted");
            using var manifest=JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
            var coverage=manifest.RootElement.GetProperty("coverage")[0];
            Check(new[]{"businessJournal","tradeCapture","tradeDiagnostics","tradeExecution","tradeQuote"}.All(k=>coverage.GetProperty(k).GetBoolean()),"export manifest reports business and trade evidence coverage");
        }
        Check(!DailyDiagnosticExport.Allowed("trade/plans/account/settings.json")&&!DailyDiagnosticExport.Allowed("trade/catalogs/rules.json")&&!DailyDiagnosticExport.Allowed("trade/executions/account/planning-catalog.json"),"trade diagnostics exclude settings and bulky rule catalogs");
        string bytePressure=Path.Combine(output,"diagnostic-byte-pressure");
        string business=Path.Combine(bytePressure,"live","managed-business");Directory.CreateDirectory(business);
        string body="{\"state\":\"completed\",\"evidence\":\""+new string('a',7*1024*1024)+"\"}";
        for(int i=0;i<10;i++)File.WriteAllText(Path.Combine(business,i+".json"),body);
        string[] essentials=["live/queues/latest/result.json","live/server-collection/account/week/1.json","live/weekly-npc-queries/latest.json"];
        foreach(string rel in essentials){string path=Path.Combine(bytePressure,rel);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"{\"state\":\"pending\"}");File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-1));}
        var capped=await DailyDiagnosticExport.CreateAsync(bytePressure,Path.Combine(output,"byte-pressure.zip"),"","fixture");
        using(var archive=ZipFile.OpenRead(capped.Path)) {
            Check(capped.Skipped>0&&capped.Bytes<=64*1024*1024&&essentials.All(p=>archive.GetEntry("logs/main/"+p)!=null),"latest queue, server collection and NPC state survive 70 MiB of newer historical business evidence");
            using var manifest=JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());var coverage=manifest.RootElement.GetProperty("coverage")[0];
            Check(coverage.GetProperty("queueResults").GetBoolean()&&coverage.GetProperty("npcQueries").GetBoolean(),"manifest distinguishes queue/NPC coverage from journal coverage");
        }
        string npcPressure=Path.Combine(output,"diagnostic-npc-pressure");
        Directory.CreateDirectory(Path.Combine(npcPressure,"live","weekly-npc-queries"));
        for(int i=0;i<10;i++)File.WriteAllText(Path.Combine(npcPressure,"live","weekly-npc-queries",i+".json"),body);
        string latestBusiness=Path.Combine(npcPressure,"live","managed-business","event-sweep.json");
        Directory.CreateDirectory(Path.GetDirectoryName(latestBusiness)!);
        File.WriteAllText(latestBusiness,"{\"role\":\"event.sweep\",\"state\":\"rejected\"}");
        File.SetLastWriteTimeUtc(latestBusiness,DateTime.UtcNow.AddHours(-1));
        File.WriteAllText(Path.Combine(npcPressure,"live","event-preview-recovery.json"),"{\"confirmation_not_submitted\":true}");
        var npcCapped=await DailyDiagnosticExport.CreateAsync(npcPressure,Path.Combine(output,"npc-byte-pressure.zip"),"","fixture");
        using(var archive=ZipFile.OpenRead(npcCapped.Path))Check(npcCapped.Skipped>0&&npcCapped.Bytes<=64*1024*1024
            &&archive.GetEntry("logs/main/live/managed-business/event-sweep.json")!=null
            &&archive.GetEntry("logs/main/live/event-preview-recovery.json")!=null,
            "Historical NPC queries cannot consume the entire export budget before event transaction and recovery evidence");
    }
}

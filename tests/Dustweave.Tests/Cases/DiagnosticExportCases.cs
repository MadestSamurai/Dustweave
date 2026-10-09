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
        Check(DailyIssues.Classify("suite.identity-unavailable").Code=="identity-unavailable","missing identity is distinguishable");
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
        string oversized=Path.Combine(root,"live","diagnostics","large.log");using(var f=File.Create(oversized))f.SetLength(8*1024*1024+1);
        var bounded=await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"bounded.zip"),"","1.1.0-beta");
        using(var archive=ZipFile.OpenRead(bounded.Path))Check(archive.GetEntry("logs/main/live/diagnostics/large.log")==null,"oversized logs cannot exhaust the archive budget");
    }
}

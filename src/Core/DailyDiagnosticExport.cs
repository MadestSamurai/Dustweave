using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Dustweave;

public sealed record DailyDiagnosticExportResult(string Path, int Files, int Skipped, long Bytes);
public static class DailyDiagnosticExport
{
    public const string WeChat = "SuJakads0133", QQ = "1104563414";
    static readonly HashSet<string> Top = new(StringComparer.OrdinalIgnoreCase) { "ui-operation-error.json", "startup-state.json", "startup-error.json", "queue-worker.log", "queue-ui.json", "connection-watchdog.json", "connection.json", "compatibility.json", "guild-compatibility.json", "startup-compatibility.json", "run.json", "run-error.json", "sandbox-error.json", "parallel-worker-error.json", "plugin-error.json", "runtime.log", "connection.log", "connection-cleanup.log" };
    static readonly string[] Trees = ["suite/diagnostics", "live/diagnostics", "live/queue-bootstrap", "live/queues", "queue-history", "tools", "parallel-workers", "live/steps", "live/business", "live/managed-business", "live/step-recovery",
        "live/trade-quote-reads", "live/trade-closes", "trade/diagnostics", "trade/data-preflights", "trade/capture", "trade/executions", "trade/plans",
        "live/travel-diagnostics", "live/route-atlas", "live/server-collection"];
    static readonly string[] Singles = ["suite/last-transition.json", "updates/check-error.json", "updates/install-error.json", "login-identity-error.json",
        "live/diagnostics/login-request.json", "live/diagnostics/login-verified.json", "live/diagnostics/login-failed.json", "live/trade-failure.json", "live/trade-navigation-cleanup.json"];
    static readonly string[] Rotations = [".1", ".2", ".3", ".previous"];
    static readonly HashSet<string> PrivateNames = new(StringComparer.OrdinalIgnoreCase) { "accounts.json", "preferences.json", "settings.json", "endpoint.json", "session.json", "sessions.json", "command.json" };
    static string Unrotate(string name) => Rotations.FirstOrDefault(s => name.EndsWith(s,StringComparison.OrdinalIgnoreCase)) is { } suffix ? name[..^suffix.Length] : name;
    static bool LogName(string name) => Unrotate(name).EndsWith(".log",StringComparison.OrdinalIgnoreCase) || Unrotate(name).EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase);
    static int Priority(string relative) => Top.Contains(Unrotate(relative)) || Singles.Contains(relative,StringComparer.OrdinalIgnoreCase) || relative.StartsWith("suite/diagnostics/",StringComparison.OrdinalIgnoreCase) ? 0
        : relative.StartsWith("live/steps/",StringComparison.OrdinalIgnoreCase) ? 3
        : new[]{"live/business/","live/managed-business/","live/trade-quote-reads/","trade/diagnostics/","live/travel-diagnostics/"}.Any(p=>relative.StartsWith(p,StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    static readonly Regex SecretKey = new("password|passwd|credential|authorization|cookie|refresh.?token|access.?token|id.?token|session.?token|auth.?token|private.?key|api.?key|secret|dpapi|modulepayload|sessionblob|^token$|^auth$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex SecretText = new("(?i)(\\b(?:password|passwd|authorization|cookie|token|credential|api[_-]?key|access[_-]?token|refresh[_-]?token|id[_-]?token|session[_-]?token|auth[_-]?token|secret|private[_-]?key)[\"']?\\s*[=:]\\s*)(?:Bearer\\s+)?(?:\"[^\"]*\"|'[^']*'|[^\\s,;]+)", RegexOptions.CultureInvariant);
    static readonly Regex Bearer = new(@"(?i)\bBearer\s+[A-Za-z0-9_.+/=-]+|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant);
    public static bool Allowed(string relative)
    {
        var p = relative.Replace('\\','/');
        if (p.Split('/').Any(x => x is ".." or "." || SecretKey.IsMatch(x) || x.Equals("plugins",StringComparison.OrdinalIgnoreCase) || x.Equals("accounts",StringComparison.OrdinalIgnoreCase))) return false;
        if (PrivateNames.Contains(Unrotate(Path.GetFileName(p)))) return false;
        if (Top.Contains(p) || LogName(p) && Top.Contains(Unrotate(p)) || Singles.Contains(p, StringComparer.OrdinalIgnoreCase)) return true;
        if (!Trees.Any(t => p.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase))) return false;
        var name = Path.GetFileName(p);
        if(p.StartsWith("live/queue-bootstrap/",StringComparison.OrdinalIgnoreCase))return name.Equals("result.json",StringComparison.OrdinalIgnoreCase);
        if(p.StartsWith("trade/executions/",StringComparison.OrdinalIgnoreCase)||p.StartsWith("trade/plans/",StringComparison.OrdinalIgnoreCase))
            return new[]{"execution.json","latest.json","snapshot.json","planning-state.json","planning-state.evidence.json","planning-state.proof.json","planning-state.identity.json"}.Contains(name,StringComparer.OrdinalIgnoreCase);
        // Runtime payloads, endpoint credentials, preference/account vaults and binaries are never exported.
        return name.EndsWith(".json",StringComparison.OrdinalIgnoreCase) || LogName(name);
    }
    public static string Redact(string text, bool json)
    {
        string Clean(string s) {
            s = SecretText.Replace(s, "$1[redacted]"); s = Bearer.Replace(s, "[redacted]");
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(user) ? s : s.Replace(user, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        void Visit(JsonNode? node) {
            if (node is JsonObject obj) foreach (var (key,value) in obj.ToArray()) {
                if (SecretKey.IsMatch(key)) obj[key] = "[redacted]";
                else if (value is JsonValue val && val.TryGetValue<string>(out var str)) obj[key] = Clean(str);
                else Visit(value);
            }
            else if (node is JsonArray arr) for (int i=0;i<arr.Count;i++) {
                if (arr[i] is JsonValue val && val.TryGetValue<string>(out var str)) arr[i]=Clean(str); else Visit(arr[i]);
            }
        }
        if (json) { var node = JsonNode.Parse(text, documentOptions:new JsonDocumentOptions{MaxDepth=96}); if(node is JsonValue value && value.TryGetValue<string>(out var scalar))node=JsonValue.Create(Clean(scalar));else Visit(node); return node?.ToJsonString(DailyJson.Options) ?? "null"; }
        var lines = text.Split('\n');
        for(int i=0;i<lines.Length;i++) {
            if(lines[i].TrimStart().StartsWith('{')) { try { lines[i]=Redact(lines[i],true); continue; } catch(JsonException) { lines[i]="[incomplete JSON line omitted]"; continue; } }
            lines[i]=Clean(lines[i]);
        }
        return string.Join('\n',lines);
    }
    public static IReadOnlyDictionary<string,string> RelatedSources(string localData,string sandboxStore)
    {
        var sources=new Dictionary<string,string>();
        foreach(var name in new[]{"BD2Fishing","BD2Territory","BD2Sichuan","BD2Rhythm","BD2ApostleDefense","BD2InfiniteGacha","BD2SecretVision","BD2FiendHunter","BD2EquipmentAssistant"})
            if(Directory.Exists(Path.Combine(localData,name)))sources["tool-"+name]=Path.Combine(localData,name);
        try { if(Directory.Exists(sandboxStore)&&(File.GetAttributes(sandboxStore)&FileAttributes.ReparsePoint)==0) {
            int index=0;
            foreach(var box in Directory.EnumerateDirectories(sandboxStore,"Dustweave_*").Take(32)) {
                if((File.GetAttributes(box)&FileAttributes.ReparsePoint)!=0)continue;
                string data=Path.Combine(box,"root","user","current","AppData","Local","BD2DailyAssistant");
                bool linked=false;for(string? p=data;p!=null&&p.Length>=box.Length;p=Path.GetDirectoryName(p))if(Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0){linked=true;break;}
                if(linked)continue;
                // Read only this product's binding, never the isolated account vault.
                var binding=DailyJson.TryRead<DailySandboxBinding>(Path.Combine(data,"sandbox-binding.json"));
                if(binding!=null&&DailyProfiles.ValidKey(binding.Account)&&DailySandbox.BoxName(binding.Account)==Path.GetFileName(box))sources["isolated-"+(++index).ToString("D2")]=data;
            }
        }} catch(Exception e) when(e is IOException or UnauthorizedAccessException) { /* Other log sources remain usable. */ }
        return sources;
    }
    public static Task<DailyDiagnosticExportResult> CreateAsync(string root, string destination, string summary, string version, CancellationToken token=default,IReadOnlyDictionary<string,string>? additionalSources=null)
        => Task.Run(()=>Create(root,destination,summary,version,token,additionalSources),token);
    public static DailyDiagnosticExportResult Create(string root,string destination,string summary,string version,CancellationToken token=default,IReadOnlyDictionary<string,string>? additionalSources=null)
    {
        root=Path.GetFullPath(root);destination=Path.GetFullPath(destination);
        if(Path.GetExtension(destination)!=".zip")throw new InvalidDataException("diagnostics.export.extension");
        if(File.Exists(destination))throw new IOException("diagnostics.export.exists");
        // Explicit root only: never traverse account stores, installed plugins or the machine.
        var skipped=new List<object>();var files=new List<(FileInfo File,string Root,string Prefix)>();int visited=0;string sourceName="main";
        bool Link(string p)=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0;
        bool SafePath(string path) {
            var current=Path.GetFullPath(path);
            while(current.Length>=root.Length) {
                if(!current.Equals(root,StringComparison.OrdinalIgnoreCase)&&!current.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return false;
                if((File.Exists(current)||Directory.Exists(current))&&Link(current))return false;
                if(current.Equals(root,StringComparison.OrdinalIgnoreCase))return true;
                current=Path.GetDirectoryName(current)!;
            }
            return false;
        }
        void Walk(string folder,int depth) {
            if(depth>8){skipped.Add(new{path=sourceName+"/"+Path.GetRelativePath(root,folder),reason="depth-limit"});return;}
            if(visited>=6000)return;
            try {
                if(!Directory.Exists(folder)||!SafePath(folder))return;
                foreach(var path in Directory.EnumerateFileSystemEntries(folder).OrderByDescending(File.GetLastWriteTimeUtc)) {
                    token.ThrowIfCancellationRequested();if(++visited>6000){skipped.Add(new{path=sourceName+"/"+Path.GetRelativePath(root,folder),reason="scan-limit"});break;}
                    string rel=Path.GetRelativePath(root,path).Replace('\\','/');
                    if(Link(path)){skipped.Add(new{path=sourceName+"/"+rel,reason="link"});continue;}
                    if(Directory.Exists(path))Walk(path,depth+1);else if(Allowed(rel))files.Add((new FileInfo(path),root,sourceName));
                }
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped.Add(new{path=sourceName+"/"+Path.GetRelativePath(root,folder),reason=e.GetType().Name});}
        }
        var sources=new Dictionary<string,string>{{"main",root}};
        if(additionalSources!=null)foreach(var item in additionalSources){if(!Regex.IsMatch(item.Key,@"^[a-zA-Z0-9-]{1,64}$")||item.Key=="main")throw new InvalidDataException("Invalid diagnostic source label");sources.Add(item.Key,Path.GetFullPath(item.Value));}
        foreach(var source in sources){
            root=source.Value;sourceName=source.Key;visited=0;
            if(Directory.Exists(root)&&Link(root)){skipped.Add(new{path=sourceName,reason="link"});continue;}
            foreach(var name in Top.Concat(Singles).Concat(Top.Where(LogName).SelectMany(n=>Rotations.Select(r=>n+r)))) {string p=Path.Combine(root,name);try{if(File.Exists(p)&&SafePath(p))files.Add((new FileInfo(p),root,sourceName));}catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped.Add(new{path=sourceName+"/"+name,reason=e.GetType().Name});}}
            // A large historical step tree must not consume another category's
            // entire enumeration budget. Export byte/file limits still apply below.
            foreach(var tree in Trees){visited=0;Walk(Path.Combine(root,tree),0);}
        }
        string parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);
        string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        int count=0;long total=0;var inventory=new List<object>();var included=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string lastOperation="";
        try {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)) {
                    void Add(string name,string data) {var entry=zip.CreateEntry(name,CompressionLevel.Optimal);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(data);}
                    foreach(var source in files.DistinctBy(f=>f.File.FullName,StringComparer.OrdinalIgnoreCase).OrderBy(f=>Priority(Path.GetRelativePath(f.Root,f.File.FullName).Replace('\\','/'))).ThenByDescending(f=>f.File.LastWriteTimeUtc)) {
                        root=source.Root;var file=source.File;
                        token.ThrowIfCancellationRequested();string rel=source.Prefix+"/"+Path.GetRelativePath(root,file.FullName).Replace('\\','/');
                        if(file.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7)){skipped.Add(new{path=rel,reason="older-than-7-days"});continue;}
                        bool log=LogName(file.Name);
                        if(count>=2000||total>=64*1024*1024||file.Length>8*1024*1024&&!log){skipped.Add(new{path=rel,reason="size-limit"});continue;}
                        try {
                            if(!SafePath(file.FullName))throw new IOException("link");
                            using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                            long originalBytes=stream.Length;bool truncated=originalBytes>8*1024*1024;
                            if(truncated) {
                                if(!log)throw new IOException("size-limit");
                                stream.Seek(-2*1024*1024,SeekOrigin.End);
                                // Start at a complete UTF-8 line; never expose a partial JSON record.
                                int next;do{next=stream.ReadByte();}while(next!=-1&&next!='\n');
                            }
                            using var reader=new StreamReader(stream,new UTF8Encoding(false,true));
                            var buffer=new char[8192];var text=new StringBuilder();int read;
                            while((read=reader.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(text.Length+read>8*1024*1024)throw new IOException("size-limit");text.Append(buffer,0,read);}
                            string data=Redact(text.ToString(),file.Extension.Equals(".json",StringComparison.OrdinalIgnoreCase));
                            int bytes=Encoding.UTF8.GetByteCount(data);if(total+bytes>64*1024*1024){skipped.Add(new{path=rel,reason="size-limit"});continue;}
                            Add("logs/"+rel,data);total+=bytes;count++;included.Add(rel);inventory.Add(new{path=rel,bytes,originalBytes,truncated,atUtc=file.LastWriteTimeUtc});
                            if(rel.Equals("main/ui-operation-error.json",StringComparison.OrdinalIgnoreCase)) {
                                using var record=JsonDocument.Parse(data);var operation=record.RootElement;
                                if(operation.ValueKind==JsonValueKind.Object) {
                                    string at=operation.TryGetProperty("atUtc",out var atValue)?atValue.ToString():"unknown";
                                    string reason=operation.TryGetProperty("error",out var errorValue)?errorValue.ToString().Split('\n')[0].Trim():"unknown";
                                    lastOperation="\n\nLast recorded operation failure (historical, separate from current connection):\nUTC: "+at+"\n"+reason+"\nSee logs/main/ui-operation-error.json and logs/main/suite/.\n";
                                }
                            }
                        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException){skipped.Add(new{path=rel,reason=e.GetType().Name});}
                    }
                    Add("summary.txt",Redact(summary+lastOperation,false));
                    Add("README.txt","Dustweave diagnostics\nWeChat: "+WeChat+"\nQQ: "+QQ+"\nCreated locally. Send privately; may include game character names, task history and paths. Login/session stores, plugins, executables and keys are excluded. This is a best-effort snapshot; see manifest.json for omitted files.\n");
                    bool HasTree(string label,string tree)=>included.Any(p=>p.StartsWith(label+"/"+tree+"/",StringComparison.OrdinalIgnoreCase));
                    var coverage=sources.Keys.Select(label=>new{source=label,operationError=included.Contains(label+"/ui-operation-error.json"),startup=included.Contains(label+"/startup-state.json"),connection=included.Contains(label+"/connection.json"),ownerTransition=included.Contains(label+"/suite/last-transition.json"),ownerHistory=HasTree(label,"suite/diagnostics"),queueBootstrap=HasTree(label,"live/queue-bootstrap"),
                        businessJournal=HasTree(label,"live/business")||HasTree(label,"live/managed-business"),tradeCapture=HasTree(label,"trade/capture"),tradeDiagnostics=HasTree(label,"trade/diagnostics"),tradeExecution=HasTree(label,"trade/executions"),tradeQuote=HasTree(label,"live/trade-quote-reads"),
                        travelDiagnostics=HasTree(label,"live/travel-diagnostics"),routeAtlas=HasTree(label,"live/route-atlas"),serverCollection=HasTree(label,"live/server-collection")});
                    Add("manifest.json",JsonSerializer.Serialize(new{schema=2,version,atUtc=DateTimeOffset.UtcNow,files=count,bytes=total,days=7,sources=sources.Keys,coverage,inventory,skipped,automaticallyUploaded=false},DailyJson.Options));
                }
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();File.Move(temporary,destination);return new(destination,count,skipped.Count,total);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

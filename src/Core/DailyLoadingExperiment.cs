using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dustweave.Accounts;

namespace Dustweave;

// User-selected, unbounded experiment output. Separate from product cache and
// bounded diagnostic ZIPs. This entry does not launch, inject or drive a game.
public static class DailyLoadingExperiment
{
    public static string ControlPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dustweave-loading-experiment", "active.json");
    public sealed record Control(string Id, string Mode, string Output, long UntilUtcTicks, long PauseUntilUtcTicks, bool DeepHooks);
    public static Control? Active()
    {
        var c = DailyJson.TryRead<Control>(ControlPath); long now = DateTime.UtcNow.Ticks;
        return c != null && Guid.TryParseExact(c.Id, "N", out _) && c.UntilUtcTicks > now && c.UntilUtcTicks < now + TimeSpan.FromSeconds(45).Ticks ? c : null;
    }
    public static bool LegacyFiles => Active()?.Mode == "legacy-files";
    public static void WriteRecoveries(string file)
    {
        DailySandbox.RequireHost();var pending=new List<object>();var errors=new List<object>();
        if(Directory.Exists(DailySandbox.Store))foreach(string state in Directory.EnumerateDirectories(DailySandbox.Store)){
            try{string root=Path.Combine(state,"root");DailyCacheStorage.NoLinks(root);var journal=DailyLoadingFileTransaction.Read(root);
                if(journal!=null&&journal.State!="restored")pending.Add(new{output=journal.Output,box=journal.Box,state=journal.State,id=journal.Id});}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){errors.Add(new{instance=Path.GetFileName(state),error=e.Message});}
        }
        DailyJson.Write(file,new{pending,errors});
    }

    public static Task<int> RunAsync(string chosenDirectory, string mode, bool deep) => RunCoreAsync(chosenDirectory,mode,deep,Sources,Process.GetProcesses,true);
    internal static async Task<int> RunCoreAsync(string chosenDirectory,string mode,bool deep,Func<Dictionary<string,string>> sourceProvider,Func<Process[]> processProvider,bool inspectEnvironment)
    {
        if (mode is not ("ordinary" or "isolated" or "legacy-files")) throw new ArgumentException("未知实验模式。");
        string output = Path.GetFullPath(chosenDirectory);
        if (Active() != null) throw new IOException("已有采集正在运行，请先停止该采集。");
        Directory.CreateDirectory(output);DailyCacheStorage.NoLinks(output);
        if(sourceProvider().Values.Any(root=>output.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new IOException("请在织尘缓存和沙盒之外选择保存目录，避免自动清理和重复采集。");
        if (Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("请选择新的空实验子目录，保留以前的记录。");
        string id = Guid.NewGuid().ToString("N");
        DateTime startedUtc=DateTime.UtcNow, historyFromUtc=startedUtc.AddMinutes(-2);
        // A failed output write occurs before the global opt-in marker is created.
        DailyJson.Write(Path.Combine(output,"session.json"),new { id, mode, deep, startedUtc, historyFromUtc, baseline="current status and Player logs plus preceding two minutes; all subsequent changed evidence", collectorPid=Environment.ProcessId, version=DailyPlugin.HostVersion, storageLimitBytes=(long?)null, fileLimit=(int?)null, automaticDeletion=false, credentialsCollected=false });
        using var controlLock = new FileStream(Path.Combine(Path.GetDirectoryName(ControlPath) is {} parent ? Directory.CreateDirectory(parent).FullName : throw new IOException(),"capture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var cancel = new CancellationTokenSource();
        Exception? failure = null;long pauseUntil=0;long copied=0,gaps=0;
        var versions=new Dictionary<string,(long Length,long At,string Hash)>(StringComparer.OrdinalIgnoreCase);
        var known=new HashSet<string>();var offsets=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        var moduleRefresh=new Dictionary<string,DateTime>();var moduleSignatures=new Dictionary<string,string>();
        var runtimeErrors=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runtimeCopyPending=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        var gameProofs=new HashSet<string>();
        var watches=new Dictionary<string,EvidenceWatch>(StringComparer.OrdinalIgnoreCase);
        var errorCounts=new System.Collections.Concurrent.ConcurrentDictionary<string,long>();
        void Log(string file,object row) => File.AppendAllText(Path.Combine(output,file),System.Text.Json.JsonSerializer.Serialize(row)+"\n",new UTF8Encoding(false));
        void Coverage(string area,Exception error) {
            Interlocked.Increment(ref gaps);string key=area+":"+error.GetType().Name+":"+error.HResult;
            long count=errorCounts.AddOrUpdate(key,1,(_,n)=>n+1);
            // Keep first evidence plus cumulative counts; repeated protected
            // service access failures must not drown out a new game failure.
            if(count==1||count%60==0)Log("coverage-errors.jsonl",new {atUtc=DateTimeOffset.UtcNow,area,count,error=error.GetType().Name,detail=DailyDiagnosticExport.Redact(error.Message,false)});
        }
        void CopyRuntime(string source,string relative,string path,FileInfo info,ref bool writing)
        {
            long offset=offsets.GetValueOrDefault(path);
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            long length=input.Length;
            if(length<offset)throw new IOException("Runtime trace unexpectedly shrank.");
            writing=true;string destination=Path.Combine(output,"runtime",source,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string tail="";
            FileStream stream;
            try{stream=new FileStream(destination,FileMode.Append,FileAccess.Write,FileShare.Read);}
            catch(IOException error)when((error.HResult&0xffff) is 32 or 33){
                // A viewer/antivirus may briefly deny write sharing. Nothing was
                // appended yet: retain the offset and retry on the next scan.
                // Disk-full and other write failures remain fatal; never hide
                // partial writes or advance past data we did not preserve.
                runtimeCopyPending[path]=new{source=path,destination,copiedBytes=offset,observedBytes=length};
                writing=false;Coverage("runtime-destination-busy",error);return;
            }
            using(stream){
                input.Position=offset;byte[] buffer=new byte[81920];long left=length-offset;
                // Bound each read to this observed tail. A busy writer cannot
                // keep CopyTo chasing EOF indefinitely and prevent stopping.
                while(left>0){int read=input.Read(buffer,0,(int)Math.Min(left,buffer.Length));if(read==0)throw new IOException("Runtime trace was truncated during capture.");stream.Write(buffer,0,read);left-=read;if(left==0)tail=Encoding.UTF8.GetString(buffer,Math.Max(0,read-4096),Math.Min(4096,read));}
                stream.Flush();offsets[path]=input.Position;
            }
            if(runtimeCopyPending.Remove(path))Log("coverage-errors.jsonl",new{atUtc=DateTimeOffset.UtcNow,area="runtime-destination-recovered",path=relative,copiedBytes=input.Position});
            versions[path]=(info.Length,info.LastWriteTimeUtc.Ticks,"");Interlocked.Increment(ref copied);
            if(tail.Contains("\"Kind\":\"closed\"",StringComparison.Ordinal)&&!File.Exists(Path.Combine(output,"STOP"))&&runtimeErrors.Add(path+":closed"))Coverage("runtime-ended-during-capture",new IOException("游戏内深度记录提前结束；保留系统记录，检查进程是否退出和 runtime 的停止原因。"));
            if(Path.GetFileName(path)=="capture-error.txt"&&runtimeErrors.Add(path))Coverage("runtime-writer",new IOException("Runtime trace writer failed; see preserved capture-error.txt."));
        }
        void CaptureFile(string sourceKey,string sourceRoot,string path,FileInfo? observed=null)
        {
            var captureClock=Stopwatch.StartNew();
            bool writing=false;
            try{
                var info=observed??new FileInfo(path);string identity=path;
                if(versions.TryGetValue(identity,out var previous)&&previous.Length==info.Length&&previous.At==info.LastWriteTimeUtc.Ticks)return;
                string relative=Path.GetRelativePath(sourceRoot,path);string text;
                // Old historical step archives are not this experiment.
                // Preserve current status regardless of age; all changes
                // since the prelude are retained without count/byte limits.
                if(info.LastWriteTimeUtc<historyFromUtc&&!Baseline(relative))return;
                // Metadata checks do not open file contents. Validate the whole
                // ancestor chain only when reading changed evidence; repeating
                // it for every historical file dominated collector CPU time.
                DailyCacheStorage.NoLinks(path);
                if(relative.StartsWith("loading-experiment",StringComparison.OrdinalIgnoreCase)){
                    CopyRuntime(sourceKey,relative,path,info,ref writing);
                    return;
                }
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(input))text=reader.ReadToEnd();
                // Structured runtime trace contains metadata only. JSON/log
                // payloads use the same credential redaction as normal export.
                bool trace=relative.StartsWith("loading-experiment",StringComparison.OrdinalIgnoreCase);
                if(!trace)text=DailyDiagnosticExport.Redact(text,Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase));
                string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                versions[identity]=(info.Length,info.LastWriteTimeUtc.Ticks,hash);
                if(previous.Hash==hash)return;
                string target=Path.Combine(output,"records",sourceKey,relative+"."+DateTime.UtcNow.Ticks);
                writing=true;Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllText(target,text,new UTF8Encoding(false));
                copied++;
                Log("files.jsonl",new {atUtc=DateTimeOffset.UtcNow,source=sourceKey,relative,stored=Path.GetRelativePath(output,target),originalBytes=info.Length,bytes=Encoding.UTF8.GetByteCount(text),sha256=hash,milliseconds=captureClock.Elapsed.TotalMilliseconds});
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){
                // Destination failure ends recording; source failures are
                // retained as explicit gaps and retried, never silently lost.
                if(writing||e is IOException && (e.HResult&0xffff) is 39 or 112)throw;
                Coverage("file:"+Path.GetFileName(path),e);
            }
        }
        try{if(inspectEnvironment){
            string? installation=DailySandbox.Installation();var boxes=new List<object>();
            if(installation!=null)foreach(string box in SandboxConfiguration.Boxes(installation)){
                var values=new Dictionary<string,string>();
                foreach(string setting in new[]{"FileRootPath","ReadFilePath","WriteFilePath","OpenFilePath","OpenPipePath","ClosedFilePath","HideOtherBoxes","HideNonSystemProcesses","Template","DropAdminRights","BlockNetworkFiles","ClosedIpcPath","ClosedFilePath","UseSecurityMode","UsePrivacyMode","NoSecurityIsolation","UseFileDeleteV2","UseRegDeleteV2"})try{values[setting]=SandboxConfiguration.Read(installation,box,setting);}catch(Exception e){Coverage("sandbox-setting:"+setting,e);}
                boxes.Add(new{box,values});
            }
            DailyJson.Write(Path.Combine(output,"environment.json"),new {os=Environment.OSVersion.ToString(),processors=Environment.ProcessorCount,dotnet=Environment.Version.ToString(),installation,boxes,systemUtc=DateTimeOffset.UtcNow});
            try {
                var game = new GameInstallation().Read();
                var manifests = new List<object>(); string? clientVersionHint = null;
                if (game.Error == null) {
                    string directory = Path.GetDirectoryName(game.Executable)!;
                    foreach (string relative in new[]{"BrownDust II.exe","UnityPlayer.dll","UnityCrashHandler64.exe",@"BrownDust II_Data\globalgamemanagers",@"BrownDust II_Data\Managed\Assembly-CSharp.dll",@"BrownDust II_Data\Managed\Unity.ResourceManager.dll",@"BrownDust II_Data\Managed\Unity.Addressables.dll",@"BrownDust II_Data\Managed\UnityEngine.CoreModule.dll",@"BrownDust II_Data\Managed\UnityEngine.UnityWebRequestModule.dll",@"MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll"}) {
                        string path = Path.Combine(directory,relative); DailyCacheStorage.NoLinks(path);
                        if (!File.Exists(path)) { manifests.Add(new{relative,missing=true}); continue; }
                        var info = new FileInfo(path); using var input = File.OpenRead(path);
                        string hash = Convert.ToHexString(SHA256.HashData(input));
                        // Exact private analysis inputs make method tokens and
                        // native module offsets usable after another game update.
                        // This fixed program-only list excludes login/config data.
                        string preserved=Path.Combine(output,"game-binaries","host",relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(preserved)!); input.Position=0;
                        using(var copy=new FileStream(preserved,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){input.CopyTo(copy);copy.Flush(true);}
                        using(var copy=File.OpenRead(preserved))if(Convert.ToHexString(SHA256.HashData(copy))!=hash)throw new IOException("主机程序证据保存校验失败："+relative);
                        manifests.Add(new{relative,bytes=info.Length,lastWriteUtc=info.LastWriteTimeUtc,sha256=hash,preserved=Path.GetRelativePath(output,preserved)});
                        if(relative.EndsWith("globalgamemanagers",StringComparison.Ordinal)) {
                            // A hint only: the game's actual Application.version is
                            // recorded independently by the in-process trace.
                            input.Position=0;using var reader=new StreamReader(input,Encoding.ASCII,leaveOpen:true);
                            clientVersionHint=string.Join(",",System.Text.RegularExpressions.Regex.Matches(reader.ReadToEnd(),@"(?<![0-9.])2\.[0-9]{1,3}\.[0-9]{1,4}(?![0-9.])").Select(m=>m.Value).Distinct());
                        }
                    }
                }
                DailyJson.Write(Path.Combine(output,"host-game-files.json"),new{game.Executable,game.IsManual,game.Error,clientVersionHint,files=manifests,scope="host installation before launch; not proof of sandbox loaded bytes",atUtc=DateTimeOffset.UtcNow});
            } catch(Exception e) { Coverage("host-game-files",e); }
        }}catch(Exception e){Coverage("environment",e);}
        async Task Work(Func<Task> action) { try { await action(); } catch(OperationCanceledException) when(cancel.IsCancellationRequested) {} catch(Exception e){failure=e;cancel.Cancel();} }
        var renewal=Work(async () => {
            while(!cancel.IsCancellationRequested){
                if(File.Exists(Path.Combine(output,"STOP"))){
                    // A readable expired lease distinguishes deliberate STOP
                    // from a transient missing/sharing-locked renewal file.
                    if(DailyJson.TryRead<Control>(ControlPath)?.Id==id)DailyJson.Write(ControlPath,new Control(id,mode,output,1,0,deep));
                    await Task.Delay(3500,cancel.Token);cancel.Cancel();break;
                }
                string pause=Path.Combine(output,"PAUSE-READS");
                if(File.Exists(pause)){File.Move(pause,Path.Combine(output,"pause-request-"+DateTime.UtcNow.Ticks));pauseUntil=DateTime.UtcNow.AddSeconds(20).Ticks;Log("markers.jsonl",new {atUtc=DateTimeOffset.UtcNow,eventName="pause-observers-20s",pauseUntil});}
                DailyJson.Write(ControlPath,new Control(id,mode,output,DateTime.UtcNow.AddSeconds(15).Ticks,pauseUntil,deep));
                await Task.Delay(1000,cancel.Token);
            }
        });
        var processes=Task.Run(()=>Work(async () => {
            while(!cancel.IsCancellationRequested){
                var clock=Stopwatch.StartNew();
                foreach(var p in processProvider())using(p)try{
                    if(!new[]{"BrownDust II","Dustweave","SbieSvc","SbieCtrl","SandMan","SbieRpcSs","SbieDcomLaunch","Start"}.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase))continue;
                    long start=p.StartTime.ToUniversalTime().Ticks;string key=p.Id+"-"+start;
                    if(mode=="legacy-files"&&p.ProcessName=="BrownDust II"&&start>=startedUtc.Ticks&&!gameProofs.Contains(key)&&Directory.Exists(DailySandbox.Store)){
                        string box=SandboxProcessScope.BoxOf(p.Id);
                        foreach(string instance in Directory.EnumerateDirectories(DailySandbox.Store))try{
                            string root=Path.Combine(instance,"root");
                            DailyLoadingFileTransaction.ObserveGame(root,output,id,box,p.Id,start);
                            if(DailyLoadingFileTransaction.Read(root) is { } proof&&proof.Id==id&&proof.GameProcessId==p.Id)gameProofs.Add(key);
                        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){Coverage("game-proof:"+Path.GetFileName(instance),e);}
                    }
                    var threads=new List<object>();foreach(ProcessThread t in p.Threads)using(t)try{threads.Add(new {id=t.Id,cpuTicks=t.TotalProcessorTime.Ticks,state=t.ThreadState.ToString(),wait=t.ThreadState==System.Diagnostics.ThreadState.Wait?t.WaitReason.ToString():""});}catch(Exception e){threads.Add(new{id=t.Id,error=e.GetType().Name});}
                    bool ioReady=LoadingProcessCounters.Read(p.Handle,out var io);
                    Log("processes.jsonl",new {atUtc=DateTimeOffset.UtcNow,pid=p.Id,start,name=p.ProcessName,cpuTicks=p.TotalProcessorTime.Ticks,workingSet=p.WorkingSet64,privateBytes=p.PrivateMemorySize64,handles=p.HandleCount,ioReady,io=new {io.ReadOperations,io.WriteOperations,io.OtherOperations,io.ReadBytes,io.WriteBytes,io.OtherBytes},threads});
                    if(known.Add(key)||!moduleRefresh.TryGetValue(key,out var at)||DateTime.UtcNow-at>TimeSpan.FromSeconds(10)){
                        moduleRefresh[key]=DateTime.UtcNow;
                        string box;try{box=SandboxProcessScope.BoxOf(p.Id);}catch(Exception e){box="unknown:"+e.GetType().Name;}
                        var modules=new List<object>();try{foreach(ProcessModule m in p.Modules)modules.Add(new {name=m.ModuleName,path=m.FileName,bytes=m.ModuleMemorySize,baseAddress=m.BaseAddress.ToInt64(),version=m.FileVersionInfo.FileVersion});}catch(Exception e){Coverage("modules:"+key,e);}
                        string signature=System.Text.Json.JsonSerializer.Serialize(modules);
                        if(!moduleSignatures.TryGetValue(key,out var previousModules)||previousModules!=signature){
                            moduleSignatures[key]=signature;
                            var row=new {atUtc=DateTimeOffset.UtcNow,pid=p.Id,start,box,name=p.ProcessName,modules};
                            DailyJson.Write(Path.Combine(output,"process-"+key+".json"),row);
                            Log("module-history.jsonl",row);
                        }
                    }
                }catch(Exception e){Coverage("process:"+p.Id,e);}
                Log("collector-cost.jsonl",new {atUtc=DateTimeOffset.UtcNow,area="process-sampling",milliseconds=clock.Elapsed.TotalMilliseconds});
                await Task.Delay(1000,cancel.Token);
            }
        }));
        var files=Task.Run(()=>Work(async () => {
            while(!cancel.IsCancellationRequested){
                var scanClock=Stopwatch.StartNew();long inspected=0;
                var sources=sourceProvider();
                foreach(var source in sources) {
                    if(!Directory.Exists(source.Value))continue;
                    if(!watches.TryGetValue(source.Value,out var watch))watches[source.Value]=watch=new EvidenceWatch(source.Value,id);
                    foreach(var error in watch.Errors())Coverage("evidence-watcher:"+source.Key,error);
                    FileInfo[] candidates;
                    try{candidates=watch.Take().ToArray();}
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException){Coverage("evidence-enumeration:"+source.Key,error);continue;}
                    foreach(var candidate in candidates) {
                        cancel.Token.ThrowIfCancellationRequested();
                        inspected++;
                        CaptureFile(source.Key,source.Value,candidate.FullName,candidate);
                    }
                }
                Log("collector-cost.jsonl",new{atUtc=DateTimeOffset.UtcNow,area="evidence-files",milliseconds=scanClock.Elapsed.TotalMilliseconds,inspected});
                DailyJson.Write(Path.Combine(output,"status.json"),new {state="recording",atUtc=DateTimeOffset.UtcNow,copied,gaps,storageLimitBytes=(long?)null,pauseUntil});
                await Task.Delay(2000,cancel.Token);
            }
        }));
        try {
            await Task.WhenAll(renewal,processes,files);
            // Runtime writers flush after lease removal. Drain every changed
            // evidence source once at stop as well, including missed watcher
            // notifications and files created during the grace period.
            if(failure==null)foreach(var source in sourceProvider()){
                if(!Directory.Exists(source.Value))continue;
                FileInfo[] candidates;
                try{candidates=EvidenceFiles(source.Value,id).ToArray();}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException){Coverage("final-evidence-enumeration:"+source.Key,error);continue;}
                foreach(var candidate in candidates)CaptureFile(source.Key,source.Value,candidate.FullName,candidate);
            }
            if(runtimeCopyPending.Count>0){
                DailyJson.Write(Path.Combine(output,"runtime-copy-pending.json"),runtimeCopyPending.Values);
                failure??=new IOException("部分游戏记录仍被其他程序占用，原始记录已保留；本组记录尚未复制完整，请保留采集目录与游戏缓存。");
            }
        }catch(Exception e){failure=e;}
        finally {
            foreach(var watch in watches.Values)watch.Dispose();
            if(DailyJson.TryRead<Control>(ControlPath)?.Id==id)try{File.Delete(ControlPath);}catch{}
            try{
                int traces=Directory.Exists(Path.Combine(output,"runtime"))?Directory.EnumerateFiles(Path.Combine(output,"runtime"),"runtime-*.jsonl",SearchOption.AllDirectories).Count():0;
                DailyJson.Write(Path.Combine(output,"coverage-error-counts.json"),errorCounts);
                DailyJson.Write(Path.Combine(output,"coverage.json"),new{runtimeTraceFiles=traces,runtimeWriterErrors=runtimeErrors.Count,pendingRuntimeCopies=runtimeCopyPending.Count,sourceReadErrors=gaps,collectorFailed=failure!=null,hookCoverage=traces>0?"recorded; check hook-installed and hook-unavailable rows":"missing",systemTrace="check windows-performance.etl and wpr-start.log",storageTruncated=false,historyFromUtc,queueDrops="check Dropped and heartbeat dropped counters in every runtime trace"});
                DailyJson.Write(Path.Combine(output,"status.json"),new {state=failure==null?"stopped":"capture-failed",atUtc=DateTimeOffset.UtcNow,copied,gaps,error=failure?.Message,gameStopped=false});
            }catch{}
        }
        return failure==null?0:1;
    }

    static bool Baseline(string relative)
    {
        string path=relative.Replace('\\','/');
        return !path.Contains('/') || path.StartsWith("loading-experiment/",StringComparison.OrdinalIgnoreCase)
            || path is "live/diagnostics/preparation-latest.json" or "suite/last-transition.json"
            || path.EndsWith("/latest.json",StringComparison.OrdinalIgnoreCase);
    }

    public static async Task RestoreAsync(string output)
    {
        if(Active()!=null)throw new IOException("请先停止当前采集，再恢复文件规则。");
        DailySandbox.RequireHost();
        bool anyJournal=Directory.Exists(output)&&Directory.EnumerateFiles(output,"legacy-files-*.json").Any();
        if(!anyJournal&&Directory.Exists(DailySandbox.Store))anyJournal=Directory.EnumerateDirectories(DailySandbox.Store).Any(s=>File.Exists(DailyLoadingFileTransaction.Pending(Path.Combine(s,"root"))));
        if(!anyJournal)return;
        string installation=DailySandbox.Installation()??throw new IOException("未找到 Sandboxie；恢复副本已保留，请在安装恢复后重新打开采集器。");
        async Task<string> Sbie(string program, params string[] args)
        {
            if(program.Equals("SbieIni.exe",StringComparison.OrdinalIgnoreCase)&&args is ["query",var section,var setting])
                return SandboxConfiguration.Read(installation,section,setting);
            var start=new ProcessStartInfo(Path.Combine(installation,program)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in args)start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("恢复命令未启动。");
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));await process.WaitForExitAsync(timeout.Token);
            string result=await stdout,error=await stderr;
            if(process.ExitCode!=0)throw new IOException("沙盒恢复命令失败："+DailyDiagnosticExport.Redact(error,false));
            return result;
        }
        // The authoritative journal survives a full/disconnected output disk.
        // Search owned instance metadata, never arbitrary user sandbox contents.
        if(Directory.Exists(DailySandbox.Store))foreach(string state in Directory.EnumerateDirectories(DailySandbox.Store))
        {
            string root=Path.Combine(state,"root");DailyCacheStorage.NoLinks(root);
            var pending=DailyLoadingFileTransaction.Read(root);
            if(pending==null||pending.State=="restored"||!Path.GetFullPath(pending.Output).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))continue;
            if(!SandboxConfiguration.SameRoot(await Sbie("SbieIni.exe","query",pending.Box,"FileRootPath"),root))throw new IOException("隔离目录已变更，已保留恢复副本。");
            await DailyLoadingFileTransaction.RecoverAsync(root,pending.Box,
                ()=>Sbie("SbieIni.exe","query",pending.Box,"ReadFilePath"),
                async ()=>(await Sbie("Start.exe","/box:"+pending.Box,"/listpids")).Trim()=="0",
                async rule=>{await Sbie("SbieIni.exe","append",pending.Box,"ReadFilePath",rule);},CancellationToken.None);
        }
        foreach(string file in Directory.Exists(output)?Directory.EnumerateFiles(output,"legacy-files-*.json"):Array.Empty<string>()){
            var journal=DailyJson.TryRead<JsonObject>(file)??throw new IOException("恢复记录无法读取。");
            if(journal["Schema"]?.GetValue<int>()==1||file.EndsWith(".restored.json",StringComparison.OrdinalIgnoreCase))continue;
            string box=journal["box"]!.GetValue<string>(),exe=journal["executable"]!.GetValue<string>();
            if(!DailySandbox.ValidBoxName(box))throw new IOException("恢复记录中的隔离空间名称无效。");
            var current=SandboxConfiguration.Read(installation,box,"FileRootPath");
            if(!SandboxConfiguration.SameRoot(current,journal["boxRoot"]!.GetValue<string>()))throw new IOException("隔离空间已变更位置，保留记录并停止恢复。");
            foreach(var p in Process.GetProcesses())using(p)try{if(SandboxProcessScope.BoxOf(p.Id).Equals(box,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先关闭该隔离空间内的游戏和织尘窗口，再恢复文件规则。");}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){}
            var allowed=DailySandboxGameFiles.Rules(exe).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var item in journal["changed"]!.AsArray()){
                string rule=item!.GetValue<string>();if(!allowed.Contains(rule))throw new IOException("恢复记录包含非游戏文件规则。");
                var existing=SandboxConfiguration.Read(installation,box,"ReadFilePath").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                if(existing.Contains(rule,StringComparer.OrdinalIgnoreCase))continue;
                var start=new ProcessStartInfo(Path.Combine(installation,"SbieIni.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                foreach(string arg in new[]{"append",box,"ReadFilePath",rule})start.ArgumentList.Add(arg);
                using var process=Process.Start(start)??throw new IOException("恢复命令未启动。");using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await process.WaitForExitAsync(timeout.Token);
                if(!SandboxConfiguration.Read(installation,box,"ReadFilePath").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Contains(rule,StringComparer.OrdinalIgnoreCase))throw new IOException("文件规则尚未恢复，原记录已保留。");
            }
            DailyJson.Write(file+".restored.json",new{restoredUtc=DateTimeOffset.UtcNow,box,credentialsChanged=false});
        }
    }

    public static Dictionary<string,string> Sources()
    {
        var roots=new Dictionary<string,string>{{"main",DailyIdentity.DataRoot},{"game-normal",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Gamfs","BrownDust II")}};
        if(Directory.Exists(DailySandbox.Store))foreach(string box in Directory.EnumerateDirectories(DailySandbox.Store)){
            if((File.GetAttributes(box)&FileAttributes.ReparsePoint)!=0)continue;
            string key=Path.GetFileName(box),root=Path.Combine(box,"root","user","current","AppData","Local","BD2DailyAssistant");
            roots[key]=root;
            roots["game-"+key]=Path.Combine(box,"root","user","current","AppData","LocalLow","Gamfs","BrownDust II");
        }
        return roots;
    }
    static IEnumerable<FileInfo> EvidenceFiles(string root,string id)
    {
        if(!Directory.Exists(root))yield break;
        // Enumeration supplies size/timestamps in one directory read. Constructing
        // a new FileInfo and re-querying it for every historical file adds tens of
        // thousands of synchronous metadata calls on large real installations.
        string spool=Path.Combine(root,"loading-experiment",id);
        if(Directory.Exists(spool))foreach(var file in new DirectoryInfo(spool).EnumerateFiles())yield return file;
        foreach(var file in new DirectoryInfo(root).EnumerateFiles())if(DailyDiagnosticExport.Allowed(file.Name)||file.Name is "Player.log" or "Player-prev.log")yield return file;
        foreach(string area in new[]{"live","suite/diagnostics","queue-history","parallel-progress","parallel-workers","tools"}){
            var folder=new DirectoryInfo(Path.Combine(root,area));if(!folder.Exists)continue;
            var pending=new Stack<DirectoryInfo>();pending.Push(folder);
            while(pending.TryPop(out var current)){
                if((current.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var entry in current.EnumerateFileSystemInfos()){
                    if((entry.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                    if(entry is DirectoryInfo child)pending.Push(child);
                    else if(entry is FileInfo file&&DailyDiagnosticExport.Allowed(Path.GetRelativePath(root,file.FullName)))yield return file;
                }
            }
        }
    }
    // Watcher notifications are hints, not authoritative evidence. Current files
    // and the append-only runtime spool are still polled; an overflow or periodic
    // safety scan rediscovers missed changes without walking all history every two seconds.
    sealed class EvidenceWatch : IDisposable
    {
        readonly string root,id;
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> dirty=new(StringComparer.OrdinalIgnoreCase);
        readonly System.Collections.Concurrent.ConcurrentQueue<Exception> errors=new();
        FileSystemWatcher? watcher;int rescan=1;long nextScan;
        public EvidenceWatch(string root,string id){
            this.root=Path.GetFullPath(root);this.id=id;
            try{
                DailyCacheStorage.NoLinks(root);
                watcher=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=65536};
                watcher.Created+=Changed;watcher.Changed+=Changed;watcher.Renamed+=(s,e)=>Changed(s,e);
                watcher.Error+=(_,e)=>{errors.Enqueue(e.GetException());Interlocked.Exchange(ref rescan,1);};
                watcher.EnableRaisingEvents=true;
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException){errors.Enqueue(e);watcher?.Dispose();watcher=null;}
        }
        bool Interested(string path){
            string relative=Path.GetRelativePath(root,path).Replace('\\','/');
            if(relative.StartsWith("../",StringComparison.Ordinal)||Path.IsPathRooted(relative))return false;
            if(relative=="loading-experiment/"+id||relative.StartsWith("loading-experiment/"+id+"/",StringComparison.OrdinalIgnoreCase))return true;
            if(!relative.Contains('/'))return DailyDiagnosticExport.Allowed(relative)||relative is "Player.log" or "Player-prev.log" or "live" or "suite" or "queue-history" or "parallel-progress" or "parallel-workers" or "tools";
            return new[]{"live/","suite/diagnostics/","queue-history/","parallel-progress/","parallel-workers/","tools/"}.Any(area=>relative.StartsWith(area,StringComparison.OrdinalIgnoreCase));
        }
        void Changed(object sender,FileSystemEventArgs e){
            try{if(!Interested(e.FullPath))return;if(Directory.Exists(e.FullPath)){Interlocked.Exchange(ref rescan,1);return;}dirty[e.FullPath]=0;}
            catch(Exception error){errors.Enqueue(error);Interlocked.Exchange(ref rescan,1);}
        }
        public IEnumerable<Exception> Errors(){while(errors.TryDequeue(out var error))yield return error;}
        public IEnumerable<FileInfo> Take(){
            var paths=new Dictionary<string,FileInfo>(StringComparer.OrdinalIgnoreCase);
            if(Interlocked.Exchange(ref rescan,0)!=0||Environment.TickCount64>=nextScan){
                foreach(var file in EvidenceFiles(root,id))paths[file.FullName]=file;
                nextScan=Environment.TickCount64+(watcher==null?10000:60000);
            }
            foreach(string path in dirty.Keys)if(dirty.TryRemove(path,out _)){
                string relative=Path.GetRelativePath(root,path);
                if(relative.StartsWith("loading-experiment"+Path.DirectorySeparatorChar+id+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||DailyDiagnosticExport.Allowed(relative)||Path.GetFileName(path) is "Player.log" or "Player-prev.log")paths[path]=new FileInfo(path);
            }
            string spool=Path.Combine(root,"loading-experiment",id);if(Directory.Exists(spool))foreach(string path in Directory.EnumerateFiles(spool))paths[path]=new FileInfo(path);
            foreach(string directory in new[]{root,Path.Combine(root,"live")})if(Directory.Exists(directory))foreach(string path in Directory.EnumerateFiles(directory))if(DailyDiagnosticExport.Allowed(Path.GetRelativePath(root,path))||Path.GetFileName(path) is "Player.log" or "Player-prev.log")paths[path]=new FileInfo(path);
            return paths.Values.OrderByDescending(file=>file.FullName.StartsWith(spool+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        }
        public void Dispose()=>watcher?.Dispose();
    }
}

internal static class LoadingProcessCounters
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct Counters { public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",EntryPoint="GetProcessIoCounters",SetLastError=true)]
    internal static extern bool Read(nint process,out Counters counters);
}

using System.Diagnostics;
using System.Text.Json.Nodes;
namespace Dustweave;

public sealed record LoadingExperimentLaunchRequest(string SessionId,string AccountKey,int Slot,string Mode);
public static class DailyLoadingExperimentLaunch
{
    public const string WindowSwitch="--loading-experiment-account";
    public const string InspectSwitch="--loading-experiment-inspect";
    public static bool Isolated(string mode)=>mode switch{
        "ordinary"=>false,"isolated" or "legacy-files"=>true,
        _=>throw new InvalidDataException("实验组无效，未启动游戏。")};
    public static void RequireIsolatedSession(string id)
    {
        if(id.Length==0)return; // Existing non-experiment launches are unchanged.
        var active=DailyLoadingExperiment.Active();
        if(active==null||active.Id!=id||!Isolated(active.Mode)||File.Exists(Path.Combine(active.Output,"STOP")))
            throw new OperationCanceledException("本组采集已经停止，未继续隔离启动。");
    }

    // Only display metadata leaves the vault adapter. No credentials or raw IDs.
    public static void WriteAccounts(string output)
    {
        DailySandbox.RequireHost();
        using var sessions=new AccountSessions();
        DailyJson.Write(output,new{accounts=sessions.Read().Accounts.Where(a=>a.Valid).Select(a=>new{key=a.AccountKey,slot=a.SlotNumber,label=a.Name+" · 槽位 "+a.SlotNumber})});
    }
    public static DailyAccount Resolve(LoadingExperimentLaunchRequest request,IEnumerable<DailyAccount> accounts)
    {
        _=Isolated(request.Mode);
        if(!Guid.TryParseExact(request.SessionId,"N",out _)||!DailyProfiles.ValidKey(request.AccountKey))throw new InvalidDataException("实验启动请求无效。");
        return accounts.SingleOrDefault(a=>a.Valid&&a.SlotNumber==request.Slot&&a.AccountKey==request.AccountKey)
            ??throw new InvalidOperationException("所选账号已变更或不可用，请结束本组，刷新账号后重试。");
    }
    public static void RequireActive(string output,LoadingExperimentLaunchRequest request)
    {
        var control=DailyLoadingExperiment.Active();
        var session=DailyJson.TryRead<JsonObject>(Path.Combine(output,"session.json"));
        var status=DailyJson.TryRead<JsonObject>(Path.Combine(output,"status.json"));
        if(control==null||control.Id!=request.SessionId||control.Mode!=request.Mode
            ||!Path.GetFullPath(control.Output).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)
            ||session?["id"]?.GetValue<string>()!=request.SessionId||status?["state"]?.GetValue<string>()!="recording"
            ||File.Exists(Path.Combine(output,"STOP")))throw new InvalidOperationException("采集尚未就绪或已经停止，未启动游戏。请重新开始一组采集。");
    }
    public static void RequireNoGames()
    {
        var games=Process.GetProcessesByName("BrownDust II");
        try{if(games.Length>0)throw new InvalidOperationException("请先关闭已运行的游戏，再开始本组实验。不会自动结束你的游戏。");}
        finally{foreach(var game in games)game.Dispose();}
    }
    public static LoadingExperimentLaunchRequest Read(string path)
    {
        if(Path.GetFileName(path)!="launch-request.json")throw new InvalidDataException("实验请求路径无效。");
        var request=DailyJson.TryRead<LoadingExperimentLaunchRequest>(path)??throw new InvalidDataException("实验启动请求无法读取。");
        RequireActive(Path.GetDirectoryName(Path.GetFullPath(path))!,request);return request;
    }
    public static string Prepare(string output,string key)
    {
        DailySandbox.RequireHost();RequireNoGames();
        var control=DailyLoadingExperiment.Active()??throw new InvalidOperationException("请先开始采集。");
        using var sessions=new AccountSessions();
        var account=sessions.Read().Accounts.FirstOrDefault(a=>a.Valid&&a.AccountKey==key)??throw new InvalidOperationException("未找到所选账号，请刷新账号列表。");
        var request=new LoadingExperimentLaunchRequest(control.Id,key,account.SlotNumber,control.Mode);
        RequireActive(output,request);_ = Resolve(request,[account]);
        string path=Path.Combine(output,"launch-request.json");
        // One explicit button press can dispatch at most one launch per group.
        Claim(output);
        DailyJson.Write(path,request);Report(output,"opening","正在按本组方式启动所选账号…");return path;
    }
    internal static void Claim(string output){using var claim=new FileStream(Path.Combine(output,"launch.claim"),FileMode.CreateNew,FileAccess.Write,FileShare.None);}
    public static void Report(string output,string state,string message)=>DailyJson.Write(Path.Combine(output,"launch-status.json"),new{state,message=DailyDiagnosticExport.Redact(message,false),atUtc=DateTimeOffset.UtcNow});
    public static void TryFail(string path,Exception error)
    {
        try{string output=Path.GetDirectoryName(Path.GetFullPath(path))!;var request=DailyJson.TryRead<LoadingExperimentLaunchRequest>(path);
            if(request!=null&&DailyJson.TryRead<JsonObject>(Path.Combine(output,"session.json"))?["id"]?.GetValue<string>()==request.SessionId)Report(output,"failed",error.Message);
        }catch{}
    }
    public static Task DispatchAsync(LoadingExperimentLaunchRequest request,DailyAccount account,Func<DailyAccount,Task> ordinary,Func<DailyAccount,Task> isolated)
    {
        _=Resolve(request,[account]);return Isolated(request.Mode)?isolated(account):ordinary(account);
    }
    // Explicit collector finish only; this is not a normal host shutdown hook.
    // A matching PID is insufficient: verify creation time, image and box again.
    public static async Task FinishAsync(string output)
    {
        DailySandbox.RequireHost();
        if(DailyLoadingExperiment.Active()!=null)throw new IOException("正在等待采集记录写完，随后关闭本组游戏并恢复。");
        var session=DailyJson.TryRead<JsonObject>(Path.Combine(output,"session.json"));
        DailyLoadingFileTransaction.Journal? journal=null;
        if(Directory.Exists(DailySandbox.Store))foreach(string instance in Directory.EnumerateDirectories(DailySandbox.Store)){
            var candidate=DailyLoadingFileTransaction.Read(Path.Combine(instance,"root"));
            if(candidate!=null&&Path.GetFullPath(candidate.Output).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))journal=candidate;
        }
        if(session==null&&journal==null)throw new IOException("本组记录缺失，未结束任何进程。");
        var request=DailyJson.TryRead<LoadingExperimentLaunchRequest>(Path.Combine(output,"launch-request.json"));
        if(request==null&&journal==null){if(session?["mode"]?.GetValue<string>()=="legacy-files")await DailyLoadingExperiment.RestoreAsync(output);return;}
        if(request!=null&&(session?["id"]?.GetValue<string>()!=request.SessionId||session?["mode"]?.GetValue<string>()!=request.Mode))throw new IOException("本组启动记录不一致，未结束任何进程。");
        if(journal!=null&&request!=null&&journal.Id!=request.SessionId)throw new IOException("C 组本机恢复记录与本组不一致，原文件已保留。");
        string mode=request?.Mode??"legacy-files";
        DateTime started=session?["startedUtc"]?.GetValue<DateTime>()??DateTime.MinValue;
        string expected=journal?.Box??(Isolated(mode)?DailySandbox.BoxName(request!.AccountKey):"");
        var proofs=new Dictionary<int,long>();
        if(journal is {GameProcessId:>0,GameStartTicks:>0})proofs[journal.GameProcessId]=journal.GameStartTicks;
        if(journal!=null){
            var local=DailyJson.TryRead<JsonObject>(Path.Combine(journal.BoxRoot,"user","current","AppData","Local","BD2DailyAssistant","loading-experiment-game.json"));
            if(local?["id"]?.GetValue<string>()==journal.Id&&local["box"]?.GetValue<string>()==journal.Box&&local["executable"]?.GetValue<string>()==journal.Executable)
                proofs[local["pid"]!.GetValue<int>()]=local["start"]!.GetValue<long>();
        }
        void Record(string name,object value){try{DailyJson.Write(Path.Combine(output,name),value);}catch(IOException){}catch(UnauthorizedAccessException){}}
        foreach(string file in Directory.Exists(output)?Directory.EnumerateFiles(output,"process-*.json"):Array.Empty<string>()){
            var record=DailyJson.TryRead<JsonObject>(file);
            if(record?["name"]?.GetValue<string>()!="BrownDust II"||record["box"]?.GetValue<string>()!=expected)continue;
            int pid=record["pid"]!.GetValue<int>();long created=record["start"]!.GetValue<long>();
            if(created>=started.ToUniversalTime().Ticks)proofs[pid]=created;
        }
        async Task Close(int pid,long created)
        {
            try{
                using var process=Process.GetProcessById(pid);
                if(process.StartTime.ToUniversalTime().Ticks!=created||process.ProcessName!="BrownDust II"||!Dustweave.Accounts.SandboxProcessScope.BoxOf(pid).Equals(expected,StringComparison.OrdinalIgnoreCase))return;
                Record("game-close-"+pid+".json",new{pid,created,box=expected,requestedUtc=DateTimeOffset.UtcNow,reason="finish this explicitly launched capture only"});
                process.CloseMainWindow();
                try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));}
                catch(TimeoutException){
                    // Test-owned game only. Record its final state before force
                    // termination; never terminate other games, hosts or services.
                    if(!process.HasExited&&process.StartTime.ToUniversalTime().Ticks==created&&Dustweave.Accounts.SandboxProcessScope.BoxOf(pid).Equals(expected,StringComparison.OrdinalIgnoreCase)){
                        Record("game-close-"+pid+"-timeout.json",new{pid,created,atUtc=DateTimeOffset.UtcNow,process.Responding,process.WorkingSet64,process.HandleCount});
                        process.Kill(false);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
            }catch(ArgumentException){}catch(InvalidOperationException){}
        }
        foreach(var proof in proofs)await Close(proof.Key,proof.Value);
        if(mode=="legacy-files")await DailyLoadingExperiment.RestoreAsync(output);
    }
}

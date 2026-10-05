using BD2Rhythm.Compatibility;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using SharpMonoInjector;
namespace BD2Rhythm;
public sealed class RhythmConnectionState
{
    public int ProcessId {get;set;}
    public long ProcessStartTicks {get;set;}
    public string HookSha256 {get;set;}="";
    public string ToolFingerprint {get;set;}="";
    public long Address {get;set;}
    public string Phase {get;set;}="";
    public string LastError {get;set;}="";
    public string AttemptUtc {get;set;}="";
}
public sealed class RhythmConnection
{
    private readonly string root;
    public RhythmConnection(string? root=null){this.root=root??RhythmIdentity.DataRoot;BD2.LocalIpc.DesktopFiles.Configure(this.root,RhythmIdentity.LiveEntries);}
    public static bool SameProcess(RhythmConnectionState s,int pid,long start)=>s.ProcessId==pid&&s.ProcessStartTicks==start;
    public static bool Fresh(RhythmRuntimeStatus? status,int pid,DateTime since)=>status!=null&&status.ProcessId==pid&&status.Runtime==RhythmIdentity.RuntimeName&&DateTime.TryParse(status.AtUtc,null,DateTimeStyles.RoundtripKind,out var when)&&when>=since&&when<=DateTime.UtcNow.AddSeconds(2);
    public static string ExistingConnectionFailure(RhythmConnectionState state)=>string.IsNullOrWhiteSpace(state.LastError)?"此游戏进程尚未返回组件状态，请等待加载完成后重新连接。":"上次连接失败（"+state.Phase+"）："+state.LastError+" 请重新连接以重新检查组件。";
    public string Connect(Action<string>? progress=null)
    {
        using var game=FindGame();int pid=game.Id;long start=game.StartTime.ToUniversalTime().Ticks;
        string fingerprint=HookCompiler.ToolFingerprint;var path=Path.Combine(root,"connection.json");
        var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,pid,start); if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,pid,start))return "已使用日常助手的统一连接";
        try
        {
            if(pipe.Fingerprint()==fingerprint)
            {
                var report=RhythmJson.Read<RhythmRuntimeStatus>(Path.Combine(root,"runtime.json"));
                if(Fresh(report,pid,DateTime.UtcNow.AddSeconds(-5))&&report!.State=="active")
                {pipe.Open(fingerprint);return $"已连接游戏 {pid} · 本机管道";}
            }
        }
        catch(BD2.LocalIpc.LeaseRevokedException){}
        catch(TimeoutException){}
        catch(IOException){}
        // Resolve the required interfaces and compile against installed metadata before any injection.
        var exe=game.MainModule?.FileName??throw new InvalidOperationException("无法读取游戏路径，请使用与游戏相同的权限运行。");
        var client=Path.Combine(Path.GetDirectoryName(exe)!,Path.GetFileNameWithoutExtension(exe)+"_Data","Managed","Assembly-CSharp.dll");
        progress?.Invoke("正在识别音游接口并生成适配组件，首次连接可能需要数秒…");
        PreparedHook prepared;
        try { prepared=HookCompiler.Prepare(Path.GetDirectoryName(client)!);RhythmJson.Write(Path.Combine(root,"compatibility.json"),prepared.Report); }
        catch(CompatibilityException ex) { RhythmJson.Write(Path.Combine(root,"compatibility.json"),ex.Report);throw; }
        catch(Exception ex) { RhythmJson.Write(Path.Combine(root,"compatibility.json"),new{Status="unsupported",Error=ex.Message,Injection=false});throw; }
        var payload=prepared.Payload;string sha=Convert.ToHexString(SHA256.HashData(payload));
        if(game.HasExited || game.StartTime.ToUniversalTime().Ticks!=start)throw new InvalidOperationException("游戏进程已变化，请重新连接。");
        progress?.Invoke("接口检查通过，正在连接独立音游组件…");
        var state=new RhythmConnectionState{ProcessId=pid,ProcessStartTicks=start,HookSha256=sha,ToolFingerprint=fingerprint,Phase="injecting",AttemptUtc=DateTime.UtcNow.ToString("O")};
        using var injector=new Injector(pid);
        RhythmJson.Write(path,state); // In-flight marker prevents a blind duplicate load after an ambiguous injector failure.
        var attempt=DateTime.UtcNow;
        try {
        state.Address=injector.Inject(payload,"BD2Rhythm.Runtime","Loader","Load").ToInt64();
        state.Phase="awaiting_runtime";
        RhythmJson.Write(path,state);
        var deadline=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<deadline)
        {
            var report=RhythmJson.Read<RhythmRuntimeStatus>(Path.Combine(root,"runtime.json"));
            if(Fresh(report,pid,attempt))
            {
                if(report!.State=="error")throw new InvalidOperationException(report.Error);
                if(report.State=="active"){pipe.Open(fingerprint);state.Phase="ready";RhythmJson.Write(path,state);return $"已连接游戏 {pid} · 音游独立组件";}
            }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("组件交接尚未完成，请等待游戏界面恢复或当前操作结算后重新连接；游戏可以保持运行。");
        } catch(Exception ex) {
            state.LastError=ex.GetBaseException().Message;
            RhythmJson.Write(path,state);
            RhythmJson.Write(Path.Combine(root,"connection-error.json"),new{state.ProcessId,state.Phase,state.AttemptUtc,Error=ex.ToString(),AtUtc=DateTime.UtcNow.ToString("O")});
            throw;
        }
    }
    public static Process FindGame()
    {
        var games=new List<Process>();
        foreach(var p in Process.GetProcesses())try{if(RhythmIdentity.IsGameProcessName(p.ProcessName))games.Add(p);else p.Dispose();}catch{p.Dispose();}
        if(games.Count==1)return games[0];foreach(var p in games)p.Dispose();
        throw new InvalidOperationException(games.Count==0?"请先启动 BrownDust II，再点击连接游戏。":"检测到多个游戏实例，请只保留需要操作的一个。");
    }
}

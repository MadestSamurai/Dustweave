using BD2Territory.Compatibility;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using SharpMonoInjector;
namespace BD2Territory;
public sealed class TerritoryConnectionState
{
    public int ProcessId {get;set;}
    public long ProcessStartTicks {get;set;}
    public string HookSha256 {get;set;}="";
    public string ToolFingerprint {get;set;}="";
    public long Address {get;set;}
}
public sealed class TerritoryConnection
{
    private readonly string root;
    public TerritoryConnection(string? root=null){this.root=root??TerritoryIdentity.DataRoot;BD2.LocalIpc.DesktopFiles.Configure(this.root,TerritoryIdentity.LiveEntries);}
    public static bool SameProcess(TerritoryConnectionState s,int pid,long start)=>s.ProcessId==pid&&s.ProcessStartTicks==start;
    public static bool Fresh(TerritoryRuntimeStatus? status,int pid,DateTime since)=>status!=null&&status.ProcessId==pid&&status.Runtime==TerritoryIdentity.RuntimeName&&DateTime.TryParse(status.AtUtc,null,DateTimeStyles.RoundtripKind,out var when)&&when>=since&&when<=DateTime.UtcNow.AddSeconds(2);
    public string Connect(Action<string>? progress=null,CancellationToken cancellationToken=default)
    {
        using var trace=new TerritoryConnectionTrace(root,progress);
        try {
        cancellationToken.ThrowIfCancellationRequested();trace.Stage("process.find");
        using var game=FindGame();int pid=game.Id;long start=game.StartTime.ToUniversalTime().Ticks;
        string fingerprint=HookCompiler.ToolFingerprint;var path=Path.Combine(root,"connection.json");
        trace.Stage("process.found");TerritoryDiagnostics.Write(root,"process.identity",$"pid={pid}; startedUtcTicks={start}");
        trace.Stage("pipe.probe");
        var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,pid,start); if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,pid,start)){trace.Stage("connected.hosted");return "已使用日常助手的统一连接";}
        try
        {
            if(pipe.Fingerprint()==fingerprint)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report=TerritoryJson.Read<TerritoryRuntimeStatus>(Path.Combine(root,"runtime.json"));
                if(Fresh(report,pid,DateTime.UtcNow.AddSeconds(-5))&&report!.State=="active")
                {cancellationToken.ThrowIfCancellationRequested();pipe.Open(fingerprint);var saved=TerritoryJson.Read<TerritoryConnectionState>(path);if(saved==null||!SameProcess(saved,pid,start))TerritoryJson.Write(path,new TerritoryConnectionState{ProcessId=pid,ProcessStartTicks=start,ToolFingerprint=fingerprint});trace.Stage("connected.reused");return $"已连接游戏 {pid} · 本机管道";}
            }
        }
        catch(BD2.LocalIpc.LeaseRevokedException){}
        catch(TimeoutException){}
        catch(IOException){}
        cancellationToken.ThrowIfCancellationRequested();
        // Resolve the required interfaces and compile against installed metadata before any injection.
        string exe;
        try{exe=game.MainModule?.FileName??throw new InvalidOperationException("无法读取游戏路径。");}
        catch(System.ComponentModel.Win32Exception e) when(e.NativeErrorCode==5){throw new InvalidOperationException("Windows 拒绝访问游戏进程。请用与游戏相同的权限打开领地工具；若游戏以管理员权限运行，也请以管理员身份运行本工具。无需重启游戏。",e);}
        var client=Path.Combine(Path.GetDirectoryName(exe)!,Path.GetFileNameWithoutExtension(exe)+"_Data","Managed","Assembly-CSharp.dll");
        trace.Stage("compatibility.prepare","正在识别领地接口并生成适配组件，首次连接可能需要数秒…");
        PreparedHook prepared;
        try { prepared=HookCompiler.Prepare(Path.GetDirectoryName(client)!);TerritoryJson.Write(Path.Combine(root,"compatibility.json"),prepared.Report); }
        catch(CompatibilityException ex) { TerritoryJson.Write(Path.Combine(root,"compatibility.json"),ex.Report);throw; }
        catch(Exception ex) { TerritoryJson.Write(Path.Combine(root,"compatibility.json"),new{Status="unsupported",Error=ex.Message,Injection=false});throw; }
        cancellationToken.ThrowIfCancellationRequested();trace.Stage("compatibility.ready");
        var payload=prepared.Payload;string sha=Convert.ToHexString(SHA256.HashData(payload));
        if(game.HasExited || game.StartTime.ToUniversalTime().Ticks!=start)throw new InvalidOperationException("游戏进程已变化，请重新连接。");
        trace.Stage("injector.open","接口检查通过，正在连接／更新领地组件，等待原动作结算…");
        var state=new TerritoryConnectionState{ProcessId=pid,ProcessStartTicks=start,HookSha256=sha,ToolFingerprint=fingerprint};
        using var injector=new Injector(pid){DiagnosticStage=stage=>trace.Stage("injector."+stage)};
        TerritoryJson.Write(path,state); // In-flight marker prevents a blind duplicate load after an ambiguous injector failure.
        var attempt=DateTime.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();trace.Stage("injector.invoke");
        state.Address=injector.Inject(payload,"BD2Territory.Runtime","Loader","Load").ToInt64();
        trace.Stage("injector.returned");cancellationToken.ThrowIfCancellationRequested();
        TerritoryJson.Write(path,state);
        trace.Stage("handoff.wait");var deadline=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report=TerritoryJson.Read<TerritoryRuntimeStatus>(Path.Combine(root,"runtime.json"));
            if(Fresh(report,pid,attempt))
            {
                if(report!.State=="error")throw new InvalidOperationException(report.Error);
                if(report.State=="active"){cancellationToken.ThrowIfCancellationRequested();pipe.Open(fingerprint);trace.Stage("connected.ready");return $"已连接游戏 {pid} · 领地独立组件";}
            }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("组件尚未返回连接状态，请确认游戏已完成加载且旧工具已暂停，再次点击连接查看结果。");
        }catch(Exception ex){trace.Fail(ex);throw;}
    }
    // Hosted tools are authorized by the live host session, not a standalone
    // injection receipt. Revalidate ownership before starting, even after Connect.
    public void ValidateStart(int observedPid,int pid,long start)
    {
        if(observedPid!=pid)throw new InvalidOperationException("游戏进程已变化，请重新连接。");
        if(BD2.LocalIpc.HostedConnection.Enabled)
        {
            var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,pid,start);
            if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,pid,start))return;
            throw new InvalidOperationException("统一会话未就绪，请返回日常助手重新连接。");
        }
        var saved=TerritoryJson.Read<TerritoryConnectionState>(Path.Combine(root,"connection.json"));
        if(saved==null)throw new InvalidOperationException("尚无有效连接记录，请先点击「连接 / 更新组件」。");
        if(!SameProcess(saved,pid,start))throw new InvalidOperationException("游戏进程已变化，请重新连接。");
    }
    public static Process FindGame()
    {
        var games=new List<Process>();
        foreach(var p in Process.GetProcesses())try{if(TerritoryIdentity.IsGameProcessName(p.ProcessName))games.Add(p);else p.Dispose();}catch{p.Dispose();}
        if(games.Count==1)return games[0];foreach(var p in games)p.Dispose();
        throw new InvalidOperationException(games.Count==0?"请先启动 BrownDust II，再点击连接游戏。":"检测到多个游戏实例，请只保留需要操作的一个。");
    }
}

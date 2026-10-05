using BD2Sichuan.Compatibility;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using SharpMonoInjector;
namespace BD2Sichuan;
public sealed class SichuanConnectionState
{
    public int ProcessId {get;set;}
    public long ProcessStartTicks {get;set;}
    public string HookSha256 {get;set;}="";
    public string ToolFingerprint {get;set;}="";
    public long Address {get;set;}
}
public sealed class SichuanConnection
{
    private readonly string root;
    public SichuanConnection(string? root=null){this.root=root??SichuanIdentity.DataRoot;BD2.LocalIpc.DesktopFiles.Configure(this.root,SichuanIdentity.LiveEntries);}
    public static bool SameProcess(SichuanConnectionState s,int pid,long start)=>s.ProcessId==pid&&s.ProcessStartTicks==start;
    public static bool Fresh(SichuanRuntimeStatus? status,int pid,DateTime since)=>status!=null&&status.ProcessId==pid&&status.Runtime==SichuanIdentity.RuntimeName&&DateTime.TryParse(status.AtUtc,null,DateTimeStyles.RoundtripKind,out var when)&&when>=since&&when<=DateTime.UtcNow.AddSeconds(2);
    public string Connect(Action<string>? progress=null,bool daily=false)
    {
        using var game=FindGame();int pid=game.Id;long start=game.StartTime.ToUniversalTime().Ticks;
        string fingerprint=HookCompiler.ToolFingerprint+(daily?".daily":"");var path=Path.Combine(root,"connection.json");
        var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,pid,start); if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,pid,start))return "已使用日常助手的统一连接";
        try
        {
            if(pipe.Fingerprint()==fingerprint)
            {
                var report=SichuanJson.Read<SichuanRuntimeStatus>(Path.Combine(root,"runtime.json"));
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
        progress?.Invoke("正在识别连连看接口并生成适配组件，首次连接可能需要数秒…");
        PreparedHook prepared;
        try { prepared=HookCompiler.Prepare(Path.GetDirectoryName(client)!,daily:daily);SichuanJson.Write(Path.Combine(root,"compatibility.json"),prepared.Report); }
        catch(CompatibilityException ex) { SichuanJson.Write(Path.Combine(root,"compatibility.json"),ex.Report);throw; }
        catch(Exception ex) { SichuanJson.Write(Path.Combine(root,"compatibility.json"),new{Status="unsupported",Error=ex.Message,Injection=false});throw; }
        var payload=prepared.Payload;string sha=Convert.ToHexString(SHA256.HashData(payload));
        if(game.HasExited || game.StartTime.ToUniversalTime().Ticks!=start)throw new InvalidOperationException("游戏进程已变化，请重新连接。");
        progress?.Invoke("接口检查通过，正在连接独立连连看组件…");
        var state=new SichuanConnectionState{ProcessId=pid,ProcessStartTicks=start,HookSha256=sha,ToolFingerprint=fingerprint};
        using var injector=new Injector(pid);
        SichuanJson.Write(path,state); // In-flight marker prevents a blind duplicate load after an ambiguous injector failure.
        var attempt=DateTime.UtcNow;
        state.Address=injector.Inject(payload,"BD2Sichuan.Runtime","Loader","Load").ToInt64();
        SichuanJson.Write(path,state);
        var deadline=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<deadline)
        {
            var report=SichuanJson.Read<SichuanRuntimeStatus>(Path.Combine(root,"runtime.json"));
            if(Fresh(report,pid,attempt))
            {
                if(report!.State=="error")throw new InvalidOperationException(report.Error);
                if(report.State=="active"){pipe.Open(fingerprint);return $"已连接游戏 {pid} · 本机管道";}
            }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("组件交接尚未完成，请等待游戏界面恢复或当前操作结算后重新连接；游戏可以保持运行。");
    }
    public static Process FindGame()
    {
        var games=new List<Process>();
        foreach(var p in Process.GetProcesses())try{if(SichuanIdentity.IsGameProcessName(p.ProcessName))games.Add(p);else p.Dispose();}catch{p.Dispose();}
        if(games.Count==1)return games[0];foreach(var p in games)p.Dispose();
        throw new InvalidOperationException(games.Count==0?"请先启动 BrownDust II，再点击连接游戏。":"检测到多个游戏实例，请只保留需要操作的一个。");
    }
}

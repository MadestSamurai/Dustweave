using System.Diagnostics;
using System.Text.Json;
using BD2InfiniteGacha.Compatibility;
using SharpMonoInjector;
namespace BD2InfiniteGacha;
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options=new(){IncludeFields=true,WriteIndented=true};
    public static T? Read<T>(string path)where T:class{try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?null:JsonSerializer.Deserialize<T>(live,Options);using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(f,Options);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}}
    public static void Write<T>(string path,T value){if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value,Options)))return;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(f,value,Options);f.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    public static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,Options),Options)!;
}
public sealed class Preferences
{
    private readonly string path;
    public Preferences(string root){path=Path.Combine(root,"settings.json");}
    private Dictionary<string,RuleDraft> Read()=>!File.Exists(path)?new():JsonFiles.Read<Dictionary<string,RuleDraft>>(path)??throw new InvalidDataException("配置暂时无法读取，已保留原文件。请关闭占用它的程序后重试。");
    public RuleDraft Load(string account,int pool)=>Read().GetValueOrDefault(account+":"+pool)??new();
    public void Save(string account,int pool,RuleDraft rules){if(account.Length!=64||pool<=0)throw new InvalidOperationException("尚未识别账号和卡池");var all=Read();all[account+":"+pool]=rules;JsonFiles.Write(path,all);}
}
public sealed record GameProcess(int Id,long Start,string File);
public interface IClientPort
{
    GameProcess? Find();Snapshot? Read();void Write(Control command);
    Task ConnectAsync(Action<string> progress,CancellationToken cancellation);
}
public sealed class GamePort:IClientPort
{
    private readonly string root;
    public GamePort(string? root=null){this.root=root??Identity.Root;BD2.LocalIpc.DesktopFiles.Configure(this.root,Identity.LiveEntries);}
    public GameProcess? Find(){var all=Process.GetProcessesByName("BrownDust II");try{if(all.Length>1)throw new InvalidOperationException("检测到多个游戏进程，请只保留一个。");if(all.Length==0)return null;var p=all[0];return new(p.Id,p.StartTime.ToUniversalTime().Ticks,p.MainModule?.FileName??throw new InvalidOperationException("无法读取游戏路径，请使用与游戏相同的权限。"));}finally{foreach(var p in all)p.Dispose();}}
    public Snapshot? Read()=>JsonFiles.Read<Snapshot>(Path.Combine(root,"snapshot.json"));
    public void Write(Control command)=>JsonFiles.Write(Path.Combine(root,"control.json"),command);
    private sealed class Connection
    {public int ProcessId{get;set;}public long Start{get;set;}public string Fingerprint{get;set;}="";public long Address{get;set;}public string Error{get;set;}="";}
    public async Task ConnectAsync(Action<string> progress,CancellationToken cancellation)
    {
        var game=Find()??throw new InvalidOperationException("请先启动并登录游戏。");var path=Path.Combine(root,"connection.json");var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,game.Id,game.Start);if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,game.Id,game.Start))return;
        try{if(pipe.Fingerprint()==HookCompiler.Fingerprint){var status=JsonFiles.Read<RuntimeStatus>(Path.Combine(root,"runtime.json"));if(status?.State=="active"&&status.At>DateTime.UtcNow.AddSeconds(-5).Ticks){pipe.Open(HookCompiler.Fingerprint);progress("已连接组件，等待游戏状态");return;}}}
        catch(BD2.LocalIpc.LeaseRevokedException){}catch(TimeoutException){}catch(IOException){}
        progress("解析本机客户端接口并准备组件…");
        string managed=Path.Combine(Path.GetDirectoryName(game.File)!,Path.GetFileNameWithoutExtension(game.File)+"_Data","Managed");
        var prepared=await Task.Run(()=>HookCompiler.Prepare(managed),cancellation);JsonFiles.Write(Path.Combine(root,"compatibility.json"),prepared.Report);
        cancellation.ThrowIfCancellationRequested();if(Find()!=game)throw new InvalidOperationException("游戏进程已经变化，请重新连接。");
        using(var p=Process.GetProcessById(game.Id))if(!p.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("mono-2.0-bdwgc.dll",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("游戏引擎仍在加载，请稍后连接。");
        var current=new Connection{ProcessId=game.Id,Start=game.Start,Fingerprint=HookCompiler.Fingerprint};JsonFiles.Write(path,current);progress("连接组件…");
        try{await Task.Run(()=>{using var injector=new Injector(game.Id);current.Address=injector.Inject(prepared.Payload,"BD2InfiniteGacha.Runtime","Loader","Load").ToInt64();},CancellationToken.None);JsonFiles.Write(path,current);}
        catch(Exception e){current.Error=e.Message;JsonFiles.Write(path,current);throw;}
        var deadline=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<deadline){
            cancellation.ThrowIfCancellationRequested();
            var status=JsonFiles.Read<RuntimeStatus>(Path.Combine(root,"runtime.json"));
            if(status?.ProcessId==game.Id&&status.ProcessStart==game.Start){
                if(status.State=="error")throw new InvalidOperationException(status.Error);
                if(status.State=="active"){pipe.Open(HookCompiler.Fingerprint);return;}
            }
            await Task.Delay(100,cancellation);
        }
        throw new IOException("组件交接尚未完成，请等待当前操作结算后重新连接，游戏可以保持运行。");
    }
}
public sealed class ClientController
{
    private readonly IClientPort port;private readonly Func<long> clock;private readonly Action<string,string>? record;private Control? active;
    private long? unavailableSince,writeFailedSince;private long renewedAt;
    private MatchNotice? pendingMatch;
    public MatchNotice? TakeMatch(){var value=pendingMatch;pendingMatch=null;return value;}
    public bool Running=>active!=null;
    public string Notice{get;private set;}="";
    public ClientController(IClientPort port,Func<long>? clock=null,Action<string,string>? record=null){this.port=port;this.clock=clock??(()=>DateTime.UtcNow.Ticks);this.record=record;}
    private void Note(string reason){if(Notice!=reason){Notice=reason;record?.Invoke("notice",reason);}}
    public static bool Fresh(Snapshot? s,GameProcess? game,long now)=>s!=null&&game!=null&&s.Runtime==Identity.Runtime&&s.ProcessId==game.Id&&s.ProcessStart==game.Start&&s.At>now-TimeSpan.FromSeconds(5).Ticks&&s.At<=now+TimeSpan.FromSeconds(2).Ticks;
    public void Start(Snapshot s,Pool pool,RuleDraft draft)
    {
        if(Running)throw new InvalidOperationException("已在刷新，请先停止。");
        if(!Fresh(s,port.Find(),clock())||s.Account.Length!=64)throw new InvalidOperationException("游戏状态已过期，请连接后重新开始。");
        if(!s.ResultReady||s.PoolId!=pool.Id||s.Locked)throw new InvalidOperationException("请进入所选无限抽抽乐的十连结果页，完成动画并处理锁定状态。");
        string error=Rules.Validate(draft,pool.Costumes);if(error!="")throw new InvalidOperationException(error);
        pendingMatch=null;
        active=new Control{Enabled=true,Owner=Guid.NewGuid().ToString("N"),Account=s.Account,PoolId=pool.Id,PoolKey=pool.Key,ProcessId=s.ProcessId,ProcessStart=s.ProcessStart,Rules=JsonFiles.Clone(draft),Expires=clock()+TimeSpan.FromSeconds(20).Ticks};
        try{port.Write(active);renewedAt=clock();unavailableSince=writeFailedSince=null;Note("");record?.Invoke("start",active.Owner);}catch{active=null;throw;}
    }
    public void Poll(Snapshot? s)
    {
        if(active==null)return;long now=clock();GameProcess? game;
        try{game=port.Find();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Unavailable(now);return;}
        if(game==null||game.Id!=active.ProcessId||game.Start!=active.ProcessStart){Stop("游戏进程已退出或变化");return;}
        if(!Fresh(s,game,now)){Unavailable(now);return;}
        if(s!.Account.Length>0&&s.Account!=active.Account){Stop("账号已变化，请重新开始");return;}
        if(s.Owner==active.Owner&&s.State is "matched" or "error" or "stopped")
        {
            var completion=MatchNotice.From(active,s);
            try{Stop(s.Message);}finally{pendingMatch=completion;}
            return;
        }
        if(s.Account.Length==0){Unavailable(now);return;}
        unavailableSince=null;
        if(now-renewedAt<TimeSpan.FromSeconds(2).Ticks){if(writeFailedSince==null)Note("");return;}
        active.Expires=now+TimeSpan.FromSeconds(20).Ticks;
        try{port.Write(active);renewedAt=now;writeFailedSince=null;Note("");}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){writeFailedSince??=now;Note("运行指令暂时无法保存，正在重试");if(now-writeFailedSince>=TimeSpan.FromSeconds(15).Ticks)Stop("运行指令连续15秒无法保存，已停止");}
    }
    private void Unavailable(long now){unavailableSince??=now;Note("游戏状态短暂中断，正在等待恢复");if(now-unavailableSince>=TimeSpan.FromSeconds(15).Ticks)Stop("游戏状态连续15秒未恢复，请检查连接");}
    public void Stop(string reason="已手动停止")
    {
        bool wasRunning=active!=null;active=null;unavailableSince=writeFailedSince=null;
        if(wasRunning){Note(reason);record?.Invoke("stop",reason);}
        if(wasRunning)port.Write(new Control());
    }
}

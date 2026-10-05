using System.Diagnostics;
using System.Text.Json;
using BD2ApostleDefense.Compatibility;
using SharpMonoInjector;
namespace BD2ApostleDefense;
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options=new(){IncludeFields=true,WriteIndented=true};
    public static T? Read<T>(string path)where T:class{try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?null:JsonSerializer.Deserialize<T>(live,Options);using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(f,Options);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or TimeoutException or ObjectDisposedException){IoDiagnostics.Record(Path.GetDirectoryName(Path.GetFullPath(path))!,"read",path,e);ConnectionDiagnostics.Throttled(Path.GetDirectoryName(Path.GetFullPath(path))!,"read."+Path.GetFileName(path),e);return null;}}
    public static void Write<T>(string path,T value){if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value,Options)))return;AtomicFiles.Write(path,f=>JsonSerializer.Serialize(f,value,Options));}
    public static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,Options),Options)!;
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
    public Task ConnectAsync(Action<string> progress,CancellationToken cancellation)=>Task.Run(()=>ConnectCoreAsync(progress,cancellation));
    private async Task ConnectCoreAsync(Action<string> progress,CancellationToken cancellation)
    {
        using var trace=new ConnectionTrace(root,progress);
        try
        {
        cancellation.ThrowIfCancellationRequested();trace.Stage("process.find");
        var game=Find()??throw new InvalidOperationException("请先启动并登录游戏。");trace.Stage("process.found");var path=Path.Combine(root,"connection.json");trace.Stage("pipe.probe");var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,game.Id,game.Start);if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,game.Id,game.Start))return;
        try{if(pipe.Fingerprint()==HookCompiler.ToolFingerprint){var status=JsonFiles.Read<RuntimeStatus>(Path.Combine(root,"runtime.json"));if(status?.State=="active"&&status.At>DateTime.UtcNow.AddSeconds(-5).Ticks){cancellation.ThrowIfCancellationRequested();pipe.Open(HookCompiler.ToolFingerprint);trace.Stage("connected.reused","已连接组件，等待游戏状态");return;}}}
        catch(BD2.LocalIpc.LeaseRevokedException){}catch(TimeoutException){}catch(IOException){}
        cancellation.ThrowIfCancellationRequested();trace.Stage("compatibility.prepare","解析本机客户端接口并准备组件…");
        string managed=Path.Combine(Path.GetDirectoryName(game.File)!,Path.GetFileNameWithoutExtension(game.File)+"_Data","Managed");
        var prepared=await Task.Run(()=>HookCompiler.Prepare(managed),cancellation);JsonFiles.Write(Path.Combine(root,"compatibility.json"),prepared.Report);
        cancellation.ThrowIfCancellationRequested();if(Find()!=game)throw new InvalidOperationException("游戏进程已经变化，请重新连接。");
        using(var p=Process.GetProcessById(game.Id))if(!p.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("mono-2.0-bdwgc.dll",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("游戏引擎仍在加载，请稍后连接。");
        var current=new Connection{ProcessId=game.Id,Start=game.Start,Fingerprint=HookCompiler.ToolFingerprint};JsonFiles.Write(path,current);trace.Stage("injector.open","连接组件…");
        try{await Task.Run(()=>{using var injector=new Injector(game.Id){DiagnosticStage=stage=>trace.Stage("injector."+stage)};cancellation.ThrowIfCancellationRequested();trace.Stage("injector.invoke");current.Address=injector.Inject(prepared.Payload,"BD2ApostleDefense.Runtime","Loader","Load").ToInt64();},CancellationToken.None);trace.Stage("injector.returned");cancellation.ThrowIfCancellationRequested();JsonFiles.Write(path,current);}
        catch(Exception e){current.Error=e.Message;JsonFiles.Write(path,current);throw;}
        trace.Stage("handoff.wait");var deadline=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<deadline){
            cancellation.ThrowIfCancellationRequested();
            var status=JsonFiles.Read<RuntimeStatus>(Path.Combine(root,"runtime.json"));
            if(status?.ProcessId==game.Id&&status.ProcessStart==game.Start){
                if(status.State=="error")throw new InvalidOperationException(status.Error);
                if(status.State=="active"){pipe.Open(HookCompiler.ToolFingerprint);trace.Stage("connected.ready");return;}
            }
            await Task.Delay(100,cancellation).ConfigureAwait(false);
        }
        throw new IOException("组件交接尚未完成，请等待当前操作结算后重新连接，游戏可以保持运行。");
        }catch(Exception e){trace.Fail(e);throw;}
    }
}

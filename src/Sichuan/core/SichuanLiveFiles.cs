using System.Diagnostics;
using System.Text.Json;
namespace BD2Sichuan;
public sealed class SichuanLiveFiles
{
    public string Root {get;}
    public int IntervalMilliseconds {get;set;}=1000;
    public SichuanLiveFiles(string root){Root=root;}
    public void Lease(bool enabled)
    {
        if(!BD2.LocalIpc.DesktopFiles.Write(Path.Combine(Root,"enabled-until.txt"),System.Text.Encoding.UTF8.GetBytes((enabled?DateTime.UtcNow.AddSeconds(5).Ticks:0).ToString())))throw new IOException("请先连接游戏。");
    }
    private void WriteJson<T>(string name,T value)
    {
        if(!BD2.LocalIpc.DesktopFiles.Write(Path.Combine(Root,name),JsonSerializer.SerializeToUtf8Bytes(value),name=="run-command.json"))throw new IOException("请先连接游戏。");
    }
    public void ExecutionLease(string owner,bool enabled,string reason="")=>WriteJson("execution-lease.json",new SichuanActionLease {OwnerId=owner,UntilUtcTicks=enabled?DateTime.UtcNow.AddSeconds(3).Ticks:0,StopReason=reason,IntervalMilliseconds=IntervalMilliseconds});
    public void Submit(SichuanRunCommand request)=>WriteJson("run-command.json",request);
    public SichuanSnapshot? Read()
    {
        var path=Path.Combine(Root,"latest.json");
        if(!BD2.LocalIpc.DesktopFiles.Read(path,out var bytes)||bytes==null)return null;
        using var stream=new MemoryStream(bytes);
        if(stream.Length>2_000_000)throw new IOException("盘面文件异常。");
        return JsonSerializer.Deserialize<SichuanSnapshot>(stream);
    }
    public static string Key(SichuanSnapshot s)=>s.SessionId+":"+s.Generation+":"+s.BoardHash;
    public static string? Invalid(SichuanSnapshot s,DateTime now,bool verifyProcess=true)
    {
        if(s.Cells==null||s.ImageFiles==null||s.Runtime==null||s.SessionId==null||s.Error==null) return "盘面字段不完整，等待下一次采集。";
        if(s.SchemaVersion!=1 || s.Runtime!=SichuanIdentity.RuntimeName||s.ExecutionProtocol!=3)return "请重新连接本工具内置的连连看组件，游戏可以保持运行。";
        if(!DateTime.TryParse(s.CapturedAtUtc,out var captured)||now.ToUniversalTime()-captured.ToUniversalTime()>TimeSpan.FromSeconds(3)||captured.ToUniversalTime()>now.ToUniversalTime().AddSeconds(2))return "盘面心跳已过期，等待游戏恢复或重新连接。";
        if(verifyProcess){try{using var p=Process.GetProcessById(s.ProcessId);if(p.HasExited||!SichuanIdentity.IsGameProcessName(p.ProcessName))return "游戏进程已退出。";}catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return "游戏进程已退出或暂不可读取。";}}
        if(s.Cells.Length>0){SichuanSolver.Validate(s.Width,s.Height,s.Cells);if(s.BoardHash!=SichuanSolver.Hash(s.Width,s.Height,s.Cells))return "盘面校验未通过，等待下一次采集。";}
        return null;
    }
}

using System.Text.Json;
namespace BD2Rhythm;
public static class RhythmJson {
 public static T? Read<T>(string path)where T:class{try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?null:JsonSerializer.Deserialize<T>(live);using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(s);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}}
 public static void Write<T>(string path,T value){if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value)))return;Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(value));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
public sealed class RhythmSettings{public int JitterMs{get;set;}=50;public int OffsetMs{get;set;}}
public sealed class RhythmControlLink:IDisposable {
 readonly object gate=new();readonly Timer timer;readonly string root;readonly RhythmControl control=new();bool disposed;public string Error{get;private set;}="";
 public bool Enabled{get{lock(gate)return control.Enabled;}}public string OwnerId{get{lock(gate)return control.OwnerId;}}
 public RhythmControlLink(string root){this.root=root;BD2.LocalIpc.DesktopFiles.Configure(root,RhythmIdentity.LiveEntries);timer=new(_=>{lock(gate){if(!disposed&&control.Enabled)try{Write();}catch(Exception e){Error=e.Message;}}},null,500,500);}
 public void Configure(RhythmSettings s){if(!RhythmControl.ValidSettings(s.JitterMs,s.OffsetMs))throw new ArgumentException("随机偏移为0–200ms，整体校准为-500–500ms。");lock(gate){control.JitterMs=s.JitterMs;control.OffsetMs=s.OffsetMs;RhythmJson.Write(Path.Combine(root,"settings.json"),s);if(control.Enabled)Write();}}
 public void Start(int pid){lock(gate){if(disposed)throw new ObjectDisposedException(nameof(RhythmControlLink));control.OwnerId=Guid.NewGuid().ToString("N");control.ProcessId=pid;control.Enabled=true;try{Write();}catch{control.Enabled=false;throw;}}}
 public void Stop(){lock(gate){control.Enabled=false;Write();}}
 void Write(){control.UntilUtcTicks=control.Enabled?DateTime.UtcNow.AddSeconds(5).Ticks:0;RhythmJson.Write(Path.Combine(root,"control.json"),control);Error="";}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;timer.Dispose();try{Stop();}catch(Exception e){Error=e.Message;}}}
 public static bool Fresh(RhythmSnapshot? s,DateTime now)=>s!=null&&s.Schema==1&&s.Runtime==RhythmIdentity.RuntimeName&&s.ProcessId>0&&s.CapturedUtcTicks>=now.AddSeconds(-3).Ticks&&s.CapturedUtcTicks<=now.AddSeconds(2).Ticks;
}

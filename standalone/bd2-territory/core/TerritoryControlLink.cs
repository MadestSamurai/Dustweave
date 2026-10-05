using System.Text.Json;
namespace BD2Territory;
public static class TerritoryJson
{
 public static T? Read<T>(string path) where T:class
 {try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?null:JsonSerializer.Deserialize<T>(live);using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(s);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or TimeoutException or ObjectDisposedException){TerritoryDiagnostics.Throttled(Path.GetDirectoryName(path)!,"read."+Path.GetFileName(path),e);return null;}}
 public static void Write<T>(string path,T value)
 {var bytes=JsonSerializer.SerializeToUtf8Bytes(value);if(BD2.LocalIpc.DesktopFiles.Write(path,bytes))return;AtomicSettingsFile.Write(path,bytes);}
}
public sealed class TerritoryControlLink:IDisposable
{
 private readonly object sync=new(),sendSync=new();private readonly Timer timer;private readonly string root;
 private TerritoryControl command=new();private bool disposed;private long revision,stopVersion;private string error="";
 public string Error {get{lock(sync)return error;}}
 public string OwnerId {get{lock(sync)return command.OwnerId;}}
 public bool Enabled {get{lock(sync)return command.Enabled;}}
 public long StopVersion {get{lock(sync)return stopVersion;}}
 public TerritoryControlLink(string root){this.root=root;BD2.LocalIpc.DesktopFiles.Configure(root,TerritoryIdentity.LiveEntries);timer=new(_=>Pulse(),null,500,500);}
 public void Configure(TerritorySettings s)
 {
  if(!s.ValidSettings())throw new ArgumentException("操作间隔应为 100–60000 毫秒，播种预算不能为负。");
  lock(sendSync)
  {
   lock(sync)if(disposed)throw new ObjectDisposedException(nameof(TerritoryControlLink));
   TerritoryJson.Write(Path.Combine(root,"settings.json"),s);
   bool publish;lock(sync){command.AutoSell=s.AutoSell;command.SellThreshold=s.SellThreshold;command.FixedCrop=s.FixedCrop;command.FixedSeedId=s.FixedSeedId;command.Cooking=s.Cooking;command.CookingBatch=s.CookingBatch;command.RecipeId=s.RecipeId;command.Logging=s.Logging;command.Mining=s.Mining;command.Farming=s.Farming;command.DashRecovery=s.DashRecovery;command.UseVehicle=s.UseVehicle;command.UseNavMesh=s.UseNavMesh;command.IntervalMs=s.IntervalMs;command.PlantingBudget=s.PlantingBudget;revision++;publish=command.Enabled;}
   publish=publish||BD2.LocalIpc.DesktopFiles.HasLease(root);
   if(publish)PublishLocked();
  }
 }
 public void Start(int pid,string layoutToken="",long? expectedStopVersion=null)
 {
  lock(sync){if(disposed)throw new ObjectDisposedException(nameof(TerritoryControlLink));if(expectedStopVersion.HasValue&&expectedStopVersion!=stopVersion)throw new OperationCanceledException();command.LayoutToken=layoutToken;command.OwnerId=Guid.NewGuid().ToString("N");command.ProcessId=pid;command.Enabled=true;revision++;}
  try{Publish();}catch{RequestStop();throw;}
 }
 // Local state can always be revoked without waiting for a pipe or a heartbeat.
 public void RequestStop(){lock(sync){command.Enabled=false;stopVersion++;revision++;}}
 public void Stop(){RequestStop();Publish();}
 // Flush an already requested transition without creating a second stop generation.
 public void Flush()=>Publish();
 private void Publish(){lock(sendSync)PublishLocked();}
 private void PublishLocked()
 {
  while(true)
  {
   TerritoryControl value;long sentRevision;
   lock(sync){sentRevision=revision;value=JsonSerializer.Deserialize<TerritoryControl>(JsonSerializer.Serialize(command))!;value.UntilUtcTicks=value.Enabled?DateTime.UtcNow.AddSeconds(10).Ticks:0;}
   if(value.Enabled||BD2.LocalIpc.DesktopFiles.HasLease(root))TerritoryJson.Write(Path.Combine(root,"control.json"),value);
   lock(sync){error="";if(sentRevision==revision)return;}
   // A stop/settings change arrived during the write: immediately publish current state,
   // never let an in-flight renewal be the final command after stopping.
  }
 }
 private void Pulse()
 {
  if(!Monitor.TryEnter(sendSync))return;
  try{lock(sync)if(disposed||!command.Enabled)return;PublishLocked();}
  catch(Exception e){lock(sync){error=e.Message;if(e is BD2.LocalIpc.LeaseRevokedException){command.Enabled=false;revision++;}}TerritoryDiagnostics.Throttled(root,"heartbeat.failed",e);}
  finally{Monitor.Exit(sendSync);}
 }
 public void Dispose(){lock(sync){if(disposed)return;disposed=true;command.Enabled=false;stopVersion++;revision++;}timer.Dispose();try{Publish();}catch(Exception e){lock(sync)error=e.Message;TerritoryDiagnostics.Throttled(root,"close.stop",e);}}
 public static bool Fresh(TerritorySnapshot? s,DateTime now)=>s!=null && s.Schema==1 && s.Runtime==TerritoryIdentity.RuntimeName && s.ProcessId>0 && s.CapturedUtcTicks<=now.AddSeconds(2).Ticks && s.CapturedUtcTicks>=now.AddSeconds(-3).Ticks;
}

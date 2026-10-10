using Dustweave.Compatibility;
using BD2.LocalIpc;
using System.Text.Json;
namespace Dustweave;
public static class DailySuite {
 public static Func<string,Action<string>,CancellationToken,Task<PreparedDailyHook>>? Prepare {get;set;}
 public static Func<string,string,Action<string>,CancellationToken,Task<byte[]>>? PrepareModule {get;set;}
 public static bool Enabled=>Prepare!=null;
 // Extensions belong to the on-demand daily module; the common observer contains none.
 public static string ConnectionFingerprint(Guid hostModule, string pluginFingerprint) => DailyIdentity.Hash("on-demand-v3|"+hostModule);
 static readonly JsonSerializerOptions Json=new(){IncludeFields=true};
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> LastStatus=new();
 public static string Owner=>Environment.GetEnvironmentVariable("BD2_DAILY_SUITE_OWNER")??throw new InvalidOperationException("统一会话缺少控制器身份。");
 public static async Task ActivateAsync(IGameHost host,string root,string id,Action<string> progress,CancellationToken cancel){
  var game=host.Find()??throw new InvalidOperationException("请先启动游戏，并在日常助手连接一次。");
  var snapshot=host.ReadSnapshot();
  if(snapshot==null||snapshot.ProcessId!=game.ProcessId||snapshot.ProcessStartTicks!=game.StartTicks||snapshot.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-5).Ticks)
   throw new InvalidOperationException("游戏连接已失效，请在日常助手重新连接。");
  if(snapshot.State!="identified"||string.IsNullOrEmpty(snapshot.AccountKey)||string.IsNullOrEmpty(snapshot.PlayerKey))throw new InvalidOperationException("suite.identity-unavailable" );
  var pipe=new PipeClient(Path.Combine(root,"suite"),game.ProcessId,game.StartTicks);
  var previous=Read(root,game);
  if(previous?.Tool==id&&previous.State=="ready"&&previous.Owner==Owner&&previous.Account==snapshot.AccountKey&&previous.Player==snapshot.PlayerKey&&previous.At>=DateTime.UtcNow.AddSeconds(-5).Ticks)return;
  byte[]? payload=null;
  if(previous?.Available.Contains(id)!=true){
   var prepare=PrepareModule??throw new InvalidOperationException("功能准备器未就绪。");
   string managed=Path.Combine(Path.GetDirectoryName(game.Executable)!,Path.GetFileNameWithoutExtension(game.Executable)+"_Data","Managed");
   payload=await prepare(managed,id,progress,cancel);
   cancel.ThrowIfCancellationRequested();
   if(host.Find()!=game)throw new InvalidOperationException("准备功能期间游戏已退出或重启，未启用组件。");
   var current=host.ReadSnapshot();
   if(current==null||current.ProcessId!=game.ProcessId||current.ProcessStartTicks!=game.StartTicks||current.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-5).Ticks||current.AccountKey!=snapshot.AccountKey||current.PlayerKey!=snapshot.PlayerKey)
    throw new InvalidOperationException("准备功能期间账号或连接变化，未启用组件。");
  }
  pipe.Open(DailyHookCompiler.Fingerprint);
  var command=new SuiteCommand {Id=Guid.NewGuid().ToString("N"),Tool=id,Owner=Owner,ProcessId=game.ProcessId,StartTicks=game.StartTicks,Account=snapshot.AccountKey,Player=snapshot.PlayerKey,Expires=DateTime.UtcNow.AddSeconds(40).Ticks,
   OwnerProcessId=int.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_OWNER_PID")!),OwnerStartTicks=long.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_OWNER_START")!)};
  var ownerCheck=SuiteOwnerProbe.Read(command.OwnerProcessId,command.OwnerStartTicks);
  DailySuiteDiagnostics.Record(root,"activation-request",new{command.Id,command.Tool,command.Owner,command.OwnerProcessId,command.OwnerStartTicks,command.ProcessId,command.StartTicks,command.Account,command.Player,observerFingerprint=DailyHookCompiler.Fingerprint,ownerCheck});
  if(ownerCheck.Problem.Length!=0)throw new InvalidOperationException(ownerCheck.Problem);
  if(payload!=null){command.ModulePayload=Convert.ToBase64String(payload);command.ModuleHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));}
  cancel.ThrowIfCancellationRequested();
  pipe.Write("command.json",JsonSerializer.SerializeToUtf8Bytes(command,Json));
  var until=DateTime.UtcNow.AddSeconds(35);
  while(DateTime.UtcNow<until){
   cancel.ThrowIfCancellationRequested();
   if(host.Find()!=game)throw new InvalidOperationException("游戏已退出或重启，功能切换已停止。");
   var state=Read(root,game);
   if(state?.RejectedRequest==command.Id)throw new InvalidOperationException(state.RejectedReason);
   if(state?.Request==command.Id){
    if(state.State=="error"||state.State=="paused")throw new InvalidOperationException(state.Error);
    if(state.State=="ready"&&state.Tool==id){await Task.Delay(600,cancel);return;}
    progress(string.IsNullOrEmpty(state.Error)?"正在切换功能…":state.Error);
   }
   await Task.Delay(200,cancel);
  }
  throw new TimeoutException("统一会话没有确认功能已就绪；没有自动重试。");
 }
 public static SuiteStatus? Read(string root,GameInstance game){
  var bytes=new PipeClient(Path.Combine(root,"suite"),game.ProcessId,game.StartTicks).Read("status.json");
  var state=bytes==null?null:JsonSerializer.Deserialize<SuiteStatus>(bytes,Json);
  if(state!=null){
   string key=root+"|"+game.ProcessId+"|"+game.StartTicks, value=state.Request+"|"+state.Tool+"|"+state.State+"|"+state.Error+"|"+state.RejectedRequest+"|"+state.OwnerProblem+"|"+state.OwnerCheck?.NativeError;
   if(!LastStatus.TryGetValue(key,out var previous)||previous!=value){
    LastStatus[key]=value;
    DailySuiteDiagnostics.Record(root,"runtime-transition",new{game.ProcessId,game.StartTicks,state});
    try{DailyJson.Write(Path.Combine(root,"suite","last-transition.json"),new{atUtc=DateTimeOffset.UtcNow,game.ProcessId,game.StartTicks,state});}catch(IOException){}catch(UnauthorizedAccessException){}
   }
  }
  return state;
 }
 public static void SetToolEnvironment(System.Diagnostics.ProcessStartInfo start,SuiteStatus state,GameInstance game,string id){
  if(state.State!="ready"||state.Tool!=id||state.Owner!=Owner)throw new InvalidOperationException("功能还未准备好。");
  start.Environment["BD2_DAILY_DATA_ROOT"]=DailyIdentity.DataRoot;
  start.Environment["BD2_DAILY_HOSTED_TOOL"]=id;start.Environment["BD2_DAILY_GAME_PID"]=game.ProcessId.ToString();
  start.Environment["BD2_DAILY_GAME_START"]=game.StartTicks.ToString();
  start.Environment["BD2_DAILY_TOOL_FINGERPRINT"]=state.Fingerprints.Single(x=>x.StartsWith(id+"|",StringComparison.Ordinal)).Split('|')[1];
 }
}

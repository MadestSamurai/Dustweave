using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Dustweave;
using BD2Daily.Runtime;
using BD2.LocalIpc;

public static class SuiteFixtureBus {
 public static readonly Dictionary<string,Handoff> Flows=new();
 public static readonly Dictionary<string,int> Starts=new(),Stops=new(),Pauses=new();
 public static readonly HashSet<string> Waiting=new();
 public static void Load(string id,string module){
  if(!Flows.TryGetValue(id,out var flow)){
   flow=new Handoff(module,id,id=="daily"||id.EndsWith("-daily")?"daily":id,false,
    ()=>Starts[id]=Starts.GetValueOrDefault(id)+1,()=>Pauses[id]=Pauses.GetValueOrDefault(id)+1,
    ()=>Waiting.Contains(id)?"network receipt pending":"",()=>Stops[id]=Stops.GetValueOrDefault(id)+1,(_,_)=>{});
   Flows[id]=flow;
  }
  flow.Request(DateTime.UtcNow);
 }
 public static void Unload(string id,string module){if(Flows.TryGetValue(id,out var f))f.Unload();}
}
internal static class UnifiedSuiteCases {
 static readonly JsonSerializerOptions Json=new(){IncludeFields=true};
 static readonly Type Runtime=typeof(SuiteRuntime);
 static void Set(string name,object value)=>Runtime.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,value);
 static T Get<T>(string name)=>(T)Runtime.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
 static byte[] Payload(string id){
  using var assembly=Mono.Cecil.AssemblyDefinition.CreateAssembly(new Mono.Cecil.AssemblyNameDefinition("DemandFixture."+id,new Version(1,0)),"main",Mono.Cecil.ModuleKind.Dll);
  var module=assembly.MainModule;
  string entry=SuiteRules.ModuleEntry(id);int split=entry.LastIndexOf('.');
  var loader=new Mono.Cecil.TypeDefinition(entry[..split],entry[(split+1)..],Mono.Cecil.TypeAttributes.Public|Mono.Cecil.TypeAttributes.Abstract|Mono.Cecil.TypeAttributes.Sealed,module.TypeSystem.Object);
  module.Types.Add(loader);
  foreach(string method in new[]{"Load","Unload"}){
   var m=new Mono.Cecil.MethodDefinition(method,Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,module.TypeSystem.Void);loader.Methods.Add(m);
   var il=m.Body.GetILProcessor();il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr,id);il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr,assembly.Name.FullName);il.Emit(Mono.Cecil.Cil.OpCodes.Call,module.ImportReference(typeof(SuiteFixtureBus).GetMethod(method)!));il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
  }
  var build=new Mono.Cecil.TypeDefinition("BD2.LocalIpc","Build",Mono.Cecil.TypeAttributes.Public|Mono.Cecil.TypeAttributes.Abstract|Mono.Cecil.TypeAttributes.Sealed,module.TypeSystem.Object);
  build.Fields.Add(new Mono.Cecil.FieldDefinition("Fingerprint",Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static|Mono.Cecil.FieldAttributes.Literal|Mono.Cecil.FieldAttributes.HasDefault,module.TypeSystem.String){Constant="fixture-"+id});module.Types.Add(build);
  using var memory=new MemoryStream();assembly.Write(memory);return memory.ToArray();
 }
 public static void Run(List<string> cases){
  void Check(bool value,string message){if(!value)throw new Exception(message);cases.Add("unified-session: "+message);}
  Check(DailyTransport.EndpointExists(false,121)&&DailyTransport.EndpointExists(false,231),"busy IPC endpoint retains identity read and uses bounded authenticated transport");
  Check(!DailyTransport.EndpointExists(false,2)&&!DailyTransport.EndpointExists(false,5),"missing or inaccessible IPC endpoint does not become ready");
  using(var self=Process.GetCurrentProcess()){
   var instance=new GameInstance(self.Id,self.StartTime.ToUniversalTime().Ticks,"");
   string endpoint=Wire.Endpoint(instance.ProcessId,instance.StartTicks);
   using var server=new System.IO.Pipes.NamedPipeServerStream(endpoint,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous);
   using var client=new System.IO.Pipes.NamedPipeClientStream(".",endpoint,System.IO.Pipes.PipeDirection.InOut);
   var accepted=server.WaitForConnectionAsync();client.Connect(1000);accepted.GetAwaiter().GetResult();
   Check(DailyTransport.EndpointReady(instance),"connected single-instance named pipe is busy, not disconnected");
  }
  var hostModule=Guid.Parse("85860bca-8f04-442e-807c-2338d3117b20");
  var connection=DailySuite.ConnectionFingerprint(hostModule,"plugin-a");
  Check(connection==DailySuite.ConnectionFingerprint(hostModule,"plugin-a"),"same host and plugin reuse connection");
  Check(connection==DailySuite.ConnectionFingerprint(hostModule,"plugin-b"),"plugin-only update preserves common observer identity");
  Check(connection==DailySuite.ConnectionFingerprint(hostModule,""),"removing plugin preserves common observer; daily module owns its fingerprint");
  Check(connection!=DailySuite.ConnectionFingerprint(Guid.NewGuid(),"plugin-a"),"host update changes component identity");
  string? priorOwner=Environment.GetEnvironmentVariable("BD2_DAILY_SUITE_OWNER");
  try {
   Environment.SetEnvironmentVariable("BD2_DAILY_SUITE_OWNER","helper-owner");
   foreach(string id in new[]{"fishing-daily","sichuan-daily"}){
    var launch=new ProcessStartInfo();launch.Environment.Remove("BD2_DAILY_DATA_ROOT");
    var state=new SuiteStatus{State="ready",Tool=id,Owner="helper-owner",Fingerprints=new[]{id+"|helper-fingerprint"}};
    DailySuite.SetToolEnvironment(launch,state,new GameInstance(42,123,"game.exe"),id);
    Check(launch.Environment["BD2_DAILY_DATA_ROOT"]==DailyIdentity.DataRoot,"helper explicitly receives the host data root: "+id);
    Check(launch.Environment["BD2_DAILY_GAME_PID"]=="42"&&launch.Environment["BD2_DAILY_TOOL_FINGERPRINT"]=="helper-fingerprint","helper session descriptor retains process and fingerprint: "+id);
   }
  } finally {Environment.SetEnvironmentVariable("BD2_DAILY_SUITE_OWNER",priorOwner);}
  var owned=new HashSet<string>{Runtime.Assembly.FullName!};AppDomain.CurrentDomain.SetData("BD2.LocalIpc.SuiteModules.v1",owned);
  AppDomain.CurrentDomain.SetData("BD2.LocalIpc.Handoff.v1",new List<Dictionary<string,object>>());
  AppDomain.CurrentDomain.SetData("BD2.LocalIpc.Handoff.v1.pending",null);
  var modules=Get<Dictionary<string,Type>>("modules");modules.Clear();
  foreach(string id in new[]{"daily","fishing","territory","fishing-daily"}){
   var asm=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SuiteFixture."+id),AssemblyBuilderAccess.Run);
   var type=asm.DefineDynamicModule("main").DefineType("Loader",TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed);
   foreach(string method in new[]{"Load","Unload"}){
    var il=type.DefineMethod(method,MethodAttributes.Public|MethodAttributes.Static,typeof(void),Type.EmptyTypes).GetILGenerator();
    il.Emit(OpCodes.Ldstr,id);il.Emit(OpCodes.Ldstr,asm.FullName!);il.Emit(OpCodes.Call,typeof(SuiteFixtureBus).GetMethod(method)!);il.Emit(OpCodes.Ret);
   }
   modules[id]=type.CreateType()!;
  }
  var channel=new RuntimeChannel();Set("channel",channel);Set("initialized",true);Set("hasBundle",true);
  Set("status",new SuiteStatus());Set("active","");Set("lastRequest","");Set("loadingId","");Set("request",null!);
  using var owner=Process.GetCurrentProcess();
  var identity=new DailySnapshot{ProcessId=42,ProcessStartTicks=900,AccountKey="account",PlayerKey="player"};
  SuiteCommand Command(string id)=>new(){Id=Guid.NewGuid().ToString("N"),Tool=id,Owner="owner",Account="account",Player="player",ProcessId=42,StartTicks=900,Expires=DateTime.UtcNow.AddSeconds(40).Ticks,OwnerProcessId=owner.Id,OwnerStartTicks=owner.StartTime.ToUniversalTime().Ticks};
  void Send(SuiteCommand command)=>channel.Write("command.json",JsonSerializer.SerializeToUtf8Bytes(command,Json));
  void Step(){
   SuiteRuntime.Tick(identity);
   foreach(var flow in SuiteFixtureBus.Flows.Values.ToArray())for(int frame=0;frame<4;frame++)flow.Tick(DateTime.UtcNow.AddSeconds(frame));
   Set("idle",DateTime.UtcNow.AddSeconds(-1));
  }
  void Finish(){for(int n=0;n<12;n++)Step();}
  SuiteStatus State()=>JsonSerializer.Deserialize<SuiteStatus>(channel.Read("status.json"),Json)!;
  Send(Command("daily"));Finish();
  Check(State().State=="ready"&&State().Tool=="daily","daily module activated by host");
  byte[] rhythm=Payload("rhythm");
  SuiteCommand Demand(){var c=Command("rhythm");c.ModulePayload=Convert.ToBase64String(rhythm);c.ModuleHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rhythm));return c;}
  var mismatch=Demand();mismatch.Account="other";Send(mismatch);Step();
  Check(State().Error=="account-changed"&&!modules.ContainsKey("rhythm")&&SuiteFixtureBus.Flows["daily"].IsActive,"lazy module is not loaded for another account");
  var damaged=Demand();damaged.ModuleHash=new string('0',64);Send(damaged);Step();
  Check(State().Error=="module-hash-mismatch"&&!modules.ContainsKey("rhythm")&&SuiteFixtureBus.Flows["daily"].IsActive,"corrupt requested module does not stop current daily engine");
  Send(Command("rhythm"));Step();Check(State().Error=="module-not-prepared"&&!modules.ContainsKey("rhythm"),"unprepared tool cannot silently activate");
  Send(Demand());Finish();
  Check(State().State=="ready"&&State().Tool=="rhythm"&&SuiteFixtureBus.Starts["rhythm"]==1,"first requested tool is appended and activated without reconnecting observer");
  Check(State().Available.Contains("rhythm")&&State().Fingerprints.Contains("rhythm|fixture-rhythm")&&!modules.ContainsKey("equipment"),"availability contains prepared modules only and leaves unrelated tools untouched");
  var loaded=modules["rhythm"];Send(Command("daily"));Finish();Send(Command("rhythm"));Finish();
  Check(ReferenceEquals(loaded,modules["rhythm"])&&SuiteFixtureBus.Starts["rhythm"]==2,"later tool use reuses loaded assembly without supplying or compiling another payload");
  Send(Command("daily"));Finish();
  int dailyStopsBefore=SuiteFixtureBus.Stops.GetValueOrDefault("daily");
  Send(Command("fishing"));Finish();
  Check(State().Tool=="fishing"&&SuiteFixtureBus.Stops.GetValueOrDefault("daily")==dailyStopsBefore+1,"tool switch retires daily actions");
  Check(SuiteFixtureBus.Starts["fishing"]==1,"one activation per request");
  string old=State().Request;Send(new SuiteCommand{Id=old,Tool="fishing"});Finish();
  Check(SuiteFixtureBus.Starts["fishing"]==1,"repeated request id not executed twice");
  SuiteFixtureBus.Waiting.Add("fishing");Send(Command("territory"));Finish();
  Check(State().State=="switching"&&!SuiteFixtureBus.Starts.ContainsKey("territory"),"pending receipt blocks next engine");
  Check(SuiteFixtureBus.Pauses["fishing"]>0&&SuiteFixtureBus.Stops.GetValueOrDefault("fishing")==0,"pause before acknowledgement without killing engine");
  Send(Command("daily"));Step();Check(State().RejectedReason=="switch-already-pending","concurrent switch rejected without losing original request");SuiteFixtureBus.Waiting.Clear();Finish();
  Check(State().Tool=="territory"&&SuiteFixtureBus.Stops["fishing"]==1,"acknowledged operation switches normally");
  Send(Command("daily"));Finish();int starts=SuiteFixtureBus.Starts["daily"],stops=SuiteFixtureBus.Stops["daily"];
  Send(Command("fishing-daily"));Finish();
  Check(State().Tool=="fishing-daily"&&SuiteFixtureBus.Starts["daily"]==starts&&SuiteFixtureBus.Stops["daily"]==stops,"daily helper shares resident daily execution");
  identity.PlayerKey="different";Step();
  Check(State().State=="paused"&&State().Tool=="","identity change stops both engines");
  Check(!SuiteFixtureBus.Flows.Values.Any(f=>f.IsActive),"identity change leaves no actor");
  identity.PlayerKey="player";
  var expired=Command("territory");expired.Expires=DateTime.UtcNow.AddSeconds(-1).Ticks;Send(expired);Step();
  Check(State().State=="error"&&!SuiteFixtureBus.Flows.Values.Any(f=>f.IsActive),"expired command cannot restart");
  Send(Command("absent"));Step();Check(State().Error=="module-unavailable","unknown module isolated");
  Send(Command("territory"));Finish();
  Check(State().Tool=="territory","error does not poison next valid request");
  Set("ownerPid",int.MaxValue);Step();
  Check(State().State=="paused"&&State().Tool=="","host process exit stops active tool");
  Send(Command("fishing"));Finish();SuiteFixtureBus.Waiting.Add("fishing");Send(Command("territory"));Step();
  Set("began",DateTime.UtcNow.AddMinutes(-1));Step();
  Check(State().State=="error"&&SuiteFixtureBus.Pauses["fishing"]>0,"busy deadline retains paused operation");
  SuiteFixtureBus.Waiting.Clear();SuiteRuntime.Dispose();
  Check(!SuiteFixtureBus.Flows.Values.Any(f=>f.IsActive),"host unload drains every child");
  Check(!owned.Contains(Runtime.Assembly.FullName!),"retired suite relinquishes cooperative ownership");
  SuiteRuntime.Start();channel=Get<RuntimeChannel>("channel");Send(Command("daily"));Finish();Check(State().State=="ready","reconnect reuses loaded modules with a fresh host channel");SuiteRuntime.Dispose();
  var standalone=new Handoff("standalone-a","a","a",false,()=>{},()=>{},()=>"",()=>{},(_,_)=>{});
  standalone.Request(DateTime.UtcNow);for(int i=0;i<5;i++)standalone.Tick(DateTime.UtcNow.AddSeconds(i));
  var second=new Handoff("standalone-b","b","b",false,()=>{},()=>{},()=>"",()=>{},(_,_)=>{});
  second.Request(DateTime.UtcNow);for(int i=0;i<5;i++)second.Tick(DateTime.UtcNow.AddSeconds(i));
  Check(!standalone.IsActive&&second.IsActive,"independent tools remain exclusive after unified host stops");
  Check(SuiteRules.Validate(Command("daily"),identity,DateTime.UtcNow.Ticks)=="","fresh account-bound command accepted");
  var wrong=Command("fishing");wrong.StartTicks++;Check(SuiteRules.Validate(wrong,identity,DateTime.UtcNow.Ticks)=="game-changed","reused game PID rejected");
 }
}
namespace BD2Daily.Runtime {internal static class LocalStorage {internal static string Root=>"suite-test";}}
namespace BD2.LocalIpc {
 public sealed class RuntimeChannel {
  readonly Dictionary<string,byte[]> data=new();
  public byte[] Read(string name)=>data.GetValueOrDefault(name)!;
  public byte[] Take(string name){var value=Read(name);data.Remove(name);return value;}
  public void Write(string name,byte[] value)=>data[name]=value;
  public void Activate(){}
  public void Revoke(){}
 }
 public static class PipeBroker {public static RuntimeChannel Register(string root,string fingerprint,string names,string prefix,bool readOnly)=>new();}
 public static class Build {public const string Fingerprint="offline";}
}

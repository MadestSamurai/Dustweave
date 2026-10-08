using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Dustweave;
using BD2Daily.Live;
using SharpMonoInjector;
using Dustweave.Compatibility;


namespace Dustweave.Connection;
public static class LiveEntry
{
 public static string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(LiveEntry).Module.ModuleVersionId.ToString()+"|plugin-v2|"+DailyPlugin.Current.Fingerprint)));
 public static async Task RunAsync(string[] args,bool configureConsole=true,Func<string[],Task>? invokeUtility=null,string evidenceSpec="daily-evidence-spec.json")
 {
if(configureConsole)Console.OutputEncoding=new UTF8Encoding(false);
var root=Path.Combine(DailyIdentity.DataRoot,"live");Directory.CreateDirectory(root);BD2.LocalIpc.DesktopFiles.Configure(root,LiveProtocol.LiveEntries);
try {
 if(args.Length==1&&args[0]=="locate"){
  var host=new DailyGameHost();var game=host.Find()??throw new Exception("请先启动游戏并进入主城");
  Console.WriteLine(JsonSerializer.Serialize(new{managed=Path.Combine(Path.GetDirectoryName(game.Executable)!,"BrownDust II_Data","Managed"),processId=game.ProcessId,startTicks=game.StartTicks}));return;
 }
 if(args.Length==1&&args[0]=="ready"){
  using var controller=new FileStream(Path.Combine(root,"controller.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
  controller.Lock(0,1);

  // Explicit desktop Start only. Compilation/config generation do not send gameplay.
  var host=new DailyGameHost();var game=host.Find()??throw new Exception("Game not running");
  var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,game.ProcessId,game.StartTicks);string? fingerprint=null;try{fingerprint=pipe.Fingerprint();}catch(IOException){}catch(TimeoutException){}
  var frame=DailyJson.TryRead<Frame>(Path.Combine(root,"snapshot.json"));
  var managed=Path.Combine(Path.GetDirectoryName(game.Executable)!,"BrownDust II_Data","Managed");
  string output=Path.Combine(root,"prepared",DailyIdentity.Version);Directory.CreateDirectory(output);
  async Task Invoke(params string[] argv){
   if(invokeUtility!=null){await invokeUtility(argv);return;}
   var start=DailyTools.StartInfo(AppContext.BaseDirectory,"live");start.RedirectStandardOutput=false;start.RedirectStandardError=false;
   foreach(var arg in argv)start.ArgumentList.Add(arg);
   using var p=Process.Start(start)??throw new Exception("Cannot start preparation helper");
   try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));}
   catch(TimeoutException){if(!p.HasExited)p.Kill(entireProcessTree:true);throw new TimeoutException("Connection preparation timed out: "+argv[0]);}
   if(p.ExitCode!=0)throw new Exception("Connection preparation failed: "+argv[0]);
  }
  bool fresh=frame!=null&&frame.ProcessId==game.ProcessId&&frame.ProcessStartTicks==game.StartTicks&&frame.AtUtcTicks>DateTime.UtcNow.AddSeconds(-3).Ticks;
  if(!fresh||frame!.BridgeVersion!=LiveProtocol.BridgeVersion||fingerprint!=Fingerprint){
   if(DailySuite.Enabled)throw new InvalidOperationException("统一日常模块尚未就绪，请返回日常助手重新连接；不会单独替换组件。");
   await Invoke("prepare",managed,output);
   await Invoke(fresh?"upgrade":"attach",Path.Combine(output,"bridge.dll"));
  }
  // In-process prepare/attach can replace DesktopFiles' routed client. Open the
  // client that subsequent writes actually use, rather than the pre-attach handle.
  if(host.Find()!=game)throw new InvalidOperationException("连接准备期间游戏进程已变化，请重新连接。");
  pipe=BD2.LocalIpc.DesktopFiles.Connect(root,game.ProcessId,game.StartTicks);
  pipe.Open(Fingerprint);BD2.LocalIpc.DesktopFiles.Write(Path.Combine(root,"pause"),new byte[0]);
  await Invoke("taps",managed,DailyTools.EvidenceSpec(AppContext.BaseDirectory,evidenceSpec),Path.Combine(output,"evidence-config.json"));
  // The GUI never starts ready concurrently with a daily worker. Refuse other owners too.
  if(BD2.LocalIpc.DesktopFiles.Exists(Path.Combine(root,"command.json")))throw new Exception("Another operation is in flight");
  using(var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"evidence-config.json"))))DailyJson.Write(Path.Combine(root,"evidence-config.json"),config.RootElement);
  DailyJson.Write(Path.Combine(root,"current-client.json"),new{managed});
  BD2.LocalIpc.DesktopFiles.Read(Path.Combine(root,"evidence-config.json"),out var configBytes);
  string expectedHash=Convert.ToBase64String(SHA256.HashData(configBytes!));
  using var expectedConfig=JsonDocument.Parse(configBytes!);
  var expectedRoles=expectedConfig.RootElement.GetProperty("Taps").EnumerateArray().Select(x=>x.GetProperty("Role").GetString()!).ToHashSet();
  bool evidenceReady=false;
  for(int i=0;i<50;i++){
   using var observed=DailyJson.TryRead<JsonDocument>(Path.Combine(root,"evidence.json"));
   if(observed!=null){var e=observed.RootElement;
    if(e.TryGetProperty("Config",out var hash)&&hash.GetString()==expectedHash&&e.GetProperty("Error").GetString()==""&&
       e.GetProperty("AtUtcTicks").GetInt64()>DateTime.UtcNow.AddSeconds(-3).Ticks&&
       expectedRoles.SetEquals(e.GetProperty("Taps").EnumerateArray().Select(x=>x.GetString()!))){evidenceReady=true;break;}
   }
   await Task.Delay(200);
  }
  if(!evidenceReady)throw new Exception("日常网络回执监听未恢复；保持暂停，尚未开始日常操作，请重新连接。");
  Console.WriteLine("Daily execution connection ready");return;
 }
 if(args.Length==1&&args[0]=="connect-observer"){
  using var sessions=new AccountSessions();
  await CurrentGameObservation.ConnectAsync(sessions,new DailyGameHost(),Console.WriteLine,CancellationToken.None);
  Console.WriteLine("Current game identity observed; account switch controls unchanged.");return;
 }
 if(args.Length==1&&(args[0]=="connect"||args[0]=="check-current")){
  using var sessions=new AccountSessions();
  var coordinator=new DailyCoordinator(sessions,new DailyGameHost(),DailyIdentity.DataRoot);
  coordinator.Progress+=p=>Console.WriteLine(JsonSerializer.Serialize(p));
  Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;coordinator.Stop();};
  if(args[0]=="check-current"){
   var catalog=sessions.Read();
   var target=catalog.Accounts.SingleOrDefault(a=>a.Valid&&a.AccountKey==catalog.CurrentKey)
    ??throw new InvalidOperationException("Current account must already be saved before launch verification");
   await coordinator.InspectAsync(new[]{target});
  }else await coordinator.ConnectCurrentAsync();return;
 }
 if(args.Length==4&&args[0]=="taps"){TapManifest.Generate(args[1],args[2],args[3]);return;}
 if(args.Length==4&&args[0]=="taps-all"){
  var specs=Directory.GetFiles(args[2],"*-evidence-spec.json").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
  if(specs.Length==0)throw new InvalidDataException("No evidence specifications found");
  Directory.CreateDirectory(args[3]);
  foreach(var spec in specs)TapManifest.Generate(args[1],spec,Path.Combine(args[3],Path.GetFileName(spec)));
  File.WriteAllText(Path.Combine(args[3],"result.json"),JsonSerializer.Serialize(new{status="passed",count=specs.Length,realGameTouched=false}));return;
 }
 if(args.Length==3&&args[0]=="legacy-test"){LegacyGuardTest.Prepare(args[1],args[2]);return;}
 if(args.Length==1&&args[0]=="self-test"){SelfTest.Run();return;}
 if(args.Length==3&&(args[0]=="prepare"||args[0]=="generate-live-contract"||args[0]=="generate-plugin-contract")){
  var managed=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
  var assembly=Assembly.GetExecutingAssembly();
  var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Live.")&&n.EndsWith(".cs")).Select(n=>{using var stream=assembly.GetManifestResourceStream(n)!;using var reader=new StreamReader(stream);return CSharpSyntaxTree.ParseText(reader.ReadToEnd(),path:n);}).Concat(DailyPlugin.Current.HookSources.Select((text,index)=>CSharpSyntaxTree.ParseText(text,path:"Plugin."+index+".cs"))).Append(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint="+JsonSerializer.Serialize(Fingerprint)+"; public const string PluginFingerprint="+JsonSerializer.Serialize(DailyPlugin.Current.Fingerprint)+"; } }"));
  if(args[0]=="generate-live-contract"||args[0]=="generate-plugin-contract"){
   var specs=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Live.Spec.")).Select(n=>{using var stream=assembly.GetManifestResourceStream(n)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();});
   var contract=LiveClientBindings.Generate(managed,sources,args[0]=="generate-plugin-contract"?[]:specs,args[0]=="generate-plugin-contract"?"Plugin.":null);
   File.WriteAllText(Path.Combine(output,"live-binding-contract.json"),JsonSerializer.Serialize(contract,new JsonSerializerOptions{WriteIndented=true}));
   Console.WriteLine("Baseline contract generated; no game operation.");return;
  }
  var plugin=DailyPlugin.Current;
  if(plugin.BindingContract.Length!=0){
   var original=sources.ToArray();
   using(var resource=assembly.GetManifestResourceStream("Live.BindingContract.json")!)
    sources=LiveClientBindings.Adapt(managed,original.Where(t=>!t.FilePath.StartsWith("Plugin.",StringComparison.Ordinal)),JsonSerializer.Deserialize<LiveBindingContract>(resource)!,Path.Combine(output,"bindings.json"));
   var contract=JsonSerializer.Deserialize<LiveBindingContract>(File.ReadAllBytes(Path.Combine(plugin.Root,plugin.BindingContract)))!;
   sources=sources.Concat(LiveClientBindings.Adapt(managed,original.Where(t=>t.FilePath.StartsWith("Plugin.",StringComparison.Ordinal)),contract,Path.Combine(output,"plugin-bindings.json")));
  }else using(var resource=assembly.GetManifestResourceStream("Live.BindingContract.json")!)
   sources=LiveClientBindings.Adapt(managed,sources,JsonSerializer.Deserialize<LiveBindingContract>(resource)!,Path.Combine(output,"bindings.json"));
  var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll")){try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
  refs.Add(MetadataReference.CreateFromImage(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")));
  var compilation=CSharpCompilation.Create("BD2Daily.LiveBridge"+LiveProtocol.BridgeVersion+".Hot."+Fingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
  using var bytes=new MemoryStream();var result=compilation.Emit(bytes,manifestResources:new[]{new ResourceDescription("BD2Daily.Harmony.dll",()=>new MemoryStream(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")),true)});
  if(!result.Success)throw new Exception(string.Join("\n",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
  File.WriteAllBytes(Path.Combine(output,"bridge.dll"),bytes.ToArray());
  using var module=Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"));
  DailyJson.Write(Path.Combine(output,"bridge.json"),new{sha256=Convert.ToHexString(SHA256.HashData(bytes.ToArray())),clientMvid=module.Mvid.ToString(),protocol=1});Console.WriteLine("Prepared passive live bridge, game untouched.");return;
 }
 if(args.Length==2&&(args[0]=="attach"||args[0]=="upgrade")){
  bool upgrading=args[0]=="upgrade";
  var game=new DailyGameHost().Find()??throw new Exception("Game not running");
  var identity=new DailyGameHost().ReadSnapshot()??throw new Exception("Connect the daily observer first");
  if(identity.ProcessId!=game.ProcessId||identity.ProcessStartTicks!=game.StartTicks||identity.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-3).Ticks)throw new Exception("Daily observer is stale");
  var pipe=BD2.LocalIpc.DesktopFiles.Connect(root,game.ProcessId,game.StartTicks);
  try{if(pipe.Fingerprint()==Fingerprint&&LiveProtocol.Ready(DailyJson.TryRead<Frame>(Path.Combine(root,"snapshot.json")),game.ProcessId,game.StartTicks,DateTime.UtcNow.Ticks)){Console.WriteLine("Live bridge already attached");return;}}catch(IOException){}catch(TimeoutException){}
  var attempt=Path.Combine(root,$"attach-{game.ProcessId}-{game.StartTicks}.json");
  var payload=Path.GetFullPath(args[1]);var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(payload)!,"bridge.json"))).RootElement;
  var bytes=File.ReadAllBytes(payload);if(Convert.ToHexString(SHA256.HashData(bytes))!=manifest.GetProperty("sha256").GetString())throw new Exception("Payload hash mismatch");
  var managed=Path.Combine(Path.GetDirectoryName(game.Executable)!,"BrownDust II_Data","Managed");
  using var module=Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"));
  if(module.Mvid.ToString()!=manifest.GetProperty("clientMvid").GetString())throw new Exception("Client changed; prepare again");
  using var injector=new Injector(game.ProcessId);
  DailyJson.Write(attempt,new{state="attaching",game,at=DateTimeOffset.UtcNow});
  long address=injector.Inject(bytes,"BD2Daily.Live","Bridge","Load").ToInt64();
  DailyJson.Write(attempt,new{state="attached_waiting_frame",game,address,at=DateTimeOffset.UtcNow});
  for(int i=0;i<70;i++){var f=DailyJson.TryRead<Frame>(Path.Combine(root,"snapshot.json"));if(LiveProtocol.Ready(f,game.ProcessId,game.StartTicks,DateTime.UtcNow.Ticks)){DailyJson.Write(Path.Combine(root,"attach-result.json"),new{state="ready",game,address,instance=f!.Instance,bridgeVersion=LiveProtocol.BridgeVersion,at=DateTimeOffset.UtcNow});Console.WriteLine("Live observation ready; no gameplay action performed.");return;}await Task.Delay(500);}
  throw new TimeoutException("Waiting for component handoff or login; inspect pending native operations, then reconnect.");
 }
 throw new ArgumentException("connect | connect-observer | check-current | self-test | prepare <Managed> <output> | taps <Managed> <spec.json> <output.json> | attach/upgrade <bridge.dll>");
}catch(Exception e){Console.Error.WriteLine(e.GetBaseException());if(args.FirstOrDefault()=="attach"||args.FirstOrDefault()=="upgrade")DailyJson.Write(Path.Combine(root,"attach-error.json"),new{state="error",operation=args[0],error=e.GetBaseException().Message,at=DateTimeOffset.UtcNow});if(!configureConsole)throw;Environment.ExitCode=1;}

 }
}


using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpMonoInjector;
namespace BD2Equipment.Live;
public static class LiveJson {
 public static T? Read<T>(string path) where T:class {try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var data))return data==null?null:JsonSerializer.Deserialize<T>(data,new JsonSerializerOptions{IncludeFields=true});using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(f,new JsonSerializerOptions{IncludeFields=true});}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}}
 public static void Write(string path,object value){if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{IncludeFields=true})))return;Directory.CreateDirectory(Path.GetDirectoryName(path)!);string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(tmp,JsonSerializer.Serialize(value,new JsonSerializerOptions{IncludeFields=true}));File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
}
public static class LiveEntry {
 static LiveEntry(){EquipmentTransport.Configure();}
 public static string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(LiveEntry).Module.ModuleVersionId.ToString())));
 public static string Root=>Path.Combine(Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant"),"live");
 static Process Game(){var games=Process.GetProcessesByName("BrownDust II");if(games.Length!=1){foreach(var p in games)p.Dispose();throw new InvalidOperationException("Open exactly one game instance and enter the home screen.");}return games[0];}
 static byte[] Resource(string name){using var s=typeof(LiveEntry).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing embedded component: "+name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
 public static void Prepare(string managed,string output){
  Directory.CreateDirectory(output);var assembly=typeof(LiveEntry).Assembly; using var adapter=new BD2Equipment.Compatibility.ClientAdapter(managed); LiveJson.Write(Path.Combine(output,"compatibility.json"),adapter.Bindings.Report);
  var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Live.")&&n.EndsWith(".cs")).OrderBy(n=>n,StringComparer.Ordinal).Select(n=>CSharpSyntaxTree.ParseText(adapter.Rewrite(Encoding.UTF8.GetString(Resource(n))),path:n)).Append(CSharpSyntaxTree.ParseText(adapter.Source(),path:"ClientNames.cs")).Append(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint="+JsonSerializer.Serialize(Fingerprint)+"; } }"));
  var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll"))try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}
  byte[] harmony=Resource("Equipment.Harmony.dll");refs.Add(MetadataReference.CreateFromImage(harmony));
  var comp=CSharpCompilation.Create("BD2Equipment.LiveBridge64.Hot."+Fingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
  using var bytes=new MemoryStream();var result=comp.Emit(bytes,manifestResources:new[]{new ResourceDescription("Equipment.Harmony.dll",()=>new MemoryStream(harmony),true)});
  if(!result.Success)throw new InvalidOperationException(string.Join("\n",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
  File.WriteAllBytes(Path.Combine(output,"bridge.dll"),bytes.ToArray());using var module=Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"));
  // Identity must be unambiguous before any game connection is attempted.
  var identity=module.Types.SelectMany(t=>t.Properties).Where(p=>p.PropertyType.FullName=="Proto.Net.UserDBInfo"&&p.GetMethod?.IsStatic==true&&p.Parameters.Count==0).ToArray();
  if(identity.Length!=1)throw new InvalidOperationException("Account identity interface is ambiguous.");
  LiveJson.Write(Path.Combine(output,"bridge.json"),new{sha256=Convert.ToHexString(SHA256.HashData(bytes.ToArray())),clientMvid=module.Mvid.ToString(),protocol=1});
 }
 public static async Task RunAsync(string[] args,bool configureConsole=true){
  if(configureConsole)Console.OutputEncoding=new UTF8Encoding(false);
  try{
   if(args.Length==1&&args[0]=="self-test"){Console.WriteLine(PolicyChecks.Run());return;}
   if(args.Length==1&&args[0]=="locate"){using var g=Game();Console.WriteLine(JsonSerializer.Serialize(new{managed=Path.Combine(Path.GetDirectoryName(g.MainModule!.FileName)!,"BrownDust II_Data","Managed"),processId=g.Id,startTicks=g.StartTime.ToUniversalTime().Ticks}));return;}
   if(args.Length==3&&args[0]=="prepare"){Prepare(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));Console.WriteLine("Equipment component compiled; game untouched.");return;}
   if(args.Length==4&&args[0]=="taps"){TapManifest.Generate(args[1],args[2],args[3]);return;}
   if(args.Length==2&&args[0] is "attach" or "upgrade"){
    Directory.CreateDirectory(Root);using var gate=new FileStream(Path.Combine(Root,"connection.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    using var game=Game();long start=game.StartTime.ToUniversalTime().Ticks;
    var pipe=EquipmentTransport.Connect();
    try{if(pipe.Fingerprint()==Fingerprint&&LiveProtocol.Ready(LiveJson.Read<Frame>(Path.Combine(Root,"snapshot.json"))!,game.Id,start,DateTime.UtcNow.Ticks)){Console.WriteLine("Equipment connection ready.");return;}}catch(IOException){}catch(TimeoutException){}
    var attempt=Path.Combine(Root,$"attach-{game.Id}-{start}.json");
    string payload=Path.GetFullPath(args[1]);var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(payload)!,"bridge.json"))).RootElement;
    byte[] bytes=File.ReadAllBytes(payload);if(Convert.ToHexString(SHA256.HashData(bytes))!=manifest.GetProperty("sha256").GetString())throw new InvalidDataException("Component hash mismatch.");
    string managed=Path.Combine(Path.GetDirectoryName(game.MainModule!.FileName)!,"BrownDust II_Data","Managed");using var module=Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"));
    if(module.Mvid.ToString()!=manifest.GetProperty("clientMvid").GetString())throw new InvalidOperationException("Client changed; reconnect after restarting the tool.");
    LiveJson.Write(attempt,new{state="attaching",processId=game.Id,startTicks=start});
    using var injector=new Injector(game.Id);long address=injector.Inject(bytes,"BD2Equipment.Live","Bridge","Load").ToInt64();
    LiveJson.Write(attempt,new{state="attached_waiting_frame",processId=game.Id,startTicks=start,address});
    for(int i=0;i<70;i++){Frame? f=null;try{f=LiveJson.Read<Frame>(Path.Combine(Root,"snapshot.json"));}catch(TimeoutException){}catch(IOException){}if(LiveProtocol.Ready(f!,game.Id,start,DateTime.UtcNow.Ticks)){LiveJson.Write(attempt,new{state="ready",processId=game.Id,startTicks=start,address,instance=f!.Instance});Console.WriteLine("Equipment connection ready; no resources consumed.");return;}await Task.Delay(500);}
    throw new TimeoutException("No fresh equipment frame yet. Check login or the pending component handoff, then reconnect.");
   }
   throw new ArgumentException("locate | self-test | prepare <Managed> <output> | taps <Managed> <spec> <output> | attach <bridge.dll>");
  }catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException());Environment.ExitCode=1;}
 }
}

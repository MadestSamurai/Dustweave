using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Dustweave.Compatibility;
static class LegacyGuardTest {
 internal static void Prepare(string managed,string output){
  Directory.CreateDirectory(output);
  var refs=new[]{"mscorlib.dll","System.dll","System.Core.dll"}.Select(n=>MetadataReference.CreateFromFile(Path.Combine(managed,n))).Cast<MetadataReference>().ToList();
  var harmony=DailyHookCompiler.Resource("BD2Daily.Harmony.dll");refs.Add(MetadataReference.CreateFromImage(harmony));File.WriteAllBytes(Path.Combine(output,"0Harmony.dll"),harmony);
  void Compile(string name,string code,bool exe=false){using var bytes=new MemoryStream();var r=CSharpCompilation.Create(name,[CSharpSyntaxTree.ParseText(code)],refs,new CSharpCompilationOptions(exe?OutputKind.ConsoleApplication:OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release)).Emit(bytes);if(!r.Success)throw new Exception(string.Join("\n",r.Diagnostics));File.WriteAllBytes(Path.Combine(output,name+(exe?".exe":".dll")),bytes.ToArray());}
  Compile("BD2Daily.Runtime3", """
using System.Runtime.CompilerServices;
namespace BD2Daily.Runtime {
 public class Startup {public bool Visible;}
 public class Snapshot {public string State="starting_game",Scene="ReGame";public Startup Startup=new Startup();}
 public class Result {public bool Supported;public string BlockReason="";}
 public class GuildRuntime {
  public Snapshot snapshot=new Snapshot();public int Calls;
  [MethodImpl(MethodImplOptions.NoInlining)] internal Result Observe(){Calls++;return new Result{Supported=true};}
 }
}
""");
  using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Live.LegacyStartupGuard.cs")!;using var reader=new StreamReader(stream);
  var runner="""
class Runner {
 static int Main(){string root=System.Environment.GetEnvironmentVariable("DAILY_PROBE_OUTPUT");try{
  System.AppDomain.CurrentDomain.AssemblyResolve+=(_,a)=>new System.Reflection.AssemblyName(a.Name).Name=="0Harmony"?System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(System.IO.Path.Combine(root,"0Harmony.dll"))):null;
  var fake=System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(System.IO.Path.Combine(root,"BD2Daily.Runtime3.dll")));
  BD2Daily.Live.LegacyStartupGuard.Start();
  var type=fake.GetType("BD2Daily.Runtime.GuildRuntime");var instance=System.Activator.CreateInstance(type);var observe=type.GetMethod("Observe",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
  var state=type.GetField("snapshot").GetValue(instance);
  int checks=0;System.Action<bool,int> check=(supported,calls)=>{var r=observe.Invoke(instance,null);if((bool)r.GetType().GetField("Supported").GetValue(r)!=supported||(int)type.GetField("Calls").GetValue(instance)!=calls)throw new System.Exception("Legacy startup guard failed");checks++;};
  check(false,0);state.GetType().GetField("State").SetValue(state,"identified");check(false,0);
  state.GetType().GetField("Scene").SetValue(state,"Map1000");check(true,1);
  var startup=state.GetType().GetField("Startup").GetValue(state);startup.GetType().GetField("Visible").SetValue(startup,true);check(false,1);
  type.GetField("snapshot").SetValue(instance,null);check(false,1);
  BD2Daily.Live.LegacyStartupGuard.Stop();check(true,2);
  System.Console.WriteLine("PASS: real game Mono + Harmony startup guard: "+checks);System.IO.File.WriteAllText(System.IO.Path.Combine(root,"runtime.json"),"active|");return 0;
 }catch(System.Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(root,"runtime.json"),e.ToString());return 1;}}
}
""";
  Compile("Probe",reader.ReadToEnd()+runner,true);
 }
}

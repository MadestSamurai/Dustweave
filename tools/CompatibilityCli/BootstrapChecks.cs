using System.Text;
using BD2Daily.Compatibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
public static class BootstrapChecks
{
    public static void Prepare(string managed,string output)
    {
        Directory.CreateDirectory(output);
        var refs=new[]{"mscorlib.dll","System.dll","System.Core.dll"}.Select(f=>MetadataReference.CreateFromFile(Path.Combine(managed,f))).ToArray();
        byte[] Compile(string name,string source,IEnumerable<MetadataReference> references,OutputKind kind=OutputKind.DynamicallyLinkedLibrary,ResourceDescription[]? resources=null)
        {using var bytes=new MemoryStream();var result=CSharpCompilation.Create(name,[CSharpSyntaxTree.ParseText(source)],references,new CSharpCompilationOptions(kind,optimizationLevel:OptimizationLevel.Release)).Emit(bytes,manifestResources:resources);if(!result.Success)throw new Exception(string.Join("\n",result.Diagnostics));return bytes.ToArray();}
        var stub="""
namespace BD2Daily {
 public static class DailyIdentity {public const string LiveEntries="runtime.json";public static string MemberKey(string id){return id;}}
 public static class LiveStore {public static System.Func<string,bool> Handles;public static System.Func<string,byte[]> Read;public static System.Action<string,byte[],bool> Write;}
 public class DailyRuntimeStatus {public string State;public string Error;public int ProcessId;public long ProcessStartTicks;public long AtUtcTicks;}
}
namespace Neo.Unity.Neon {
 public static class NeonSdk {
  public static bool IsInitialized;public static int Reads;
  private static System.Lazy<AuthResult> value=new System.Lazy<AuthResult>(()=>{if(!IsInitialized)throw new System.Exception("NeonSdk is not initialized.");return new AuthResult();});
  public static AuthResult Auth {get{Reads++;return value.Value;}}
 }
 public class AuthResult {public Member LoggedMember=new Member();} public class Member {public long MemberId=42;}
}
namespace BD2Daily.Runtime {
 public static class SdkProbe {public static void Run(){string key;for(int i=0;i<20;i++)if(SdkIdentity.TryRead(out key)||key!="")throw new System.Exception("SDK read before readiness");if(Neo.Unity.Neon.NeonSdk.Reads!=0)throw new System.Exception("Auth was touched");Neo.Unity.Neon.NeonSdk.IsInitialized=true;if(!SdkIdentity.TryRead(out key)||key!="42")throw new System.Exception("SDK readiness did not recover");}}
}
namespace BD2.LocalIpc {
 public static class Build {public const string Fingerprint="probe";}
 public static class RuntimeFiles {
  public static void Start(string root,string fingerprint,string entries){} public static void Activate(){} public static void Revoke(){}
  public static bool Handles(string path){return false;} public static byte[] Read(string path){return null;}public static bool Write(string path,byte[] bytes){return false;}
 }
}
namespace BD2Daily.Runtime {
 internal static class LocalStorage {
  internal static string Root {get{return System.Environment.GetEnvironmentVariable("DAILY_PROBE_OUTPUT");}}
  internal static void Write(string file,BD2Daily.DailyRuntimeStatus status){System.IO.File.WriteAllText(System.IO.Path.Combine(Root,file),status.State+"|"+status.Error);}
 }
 internal sealed class RuntimeEngine {
  private HarmonyLib.Harmony harmony;
  internal void Start(){harmony=new HarmonyLib.Harmony("daily-bootstrap-probe");Loader.WriteStatus("active","");}
  internal void Stop(){harmony=null;} internal void PrepareHandoff(){} internal string HandoffBusy(){return "";}
 }
}
""";
        string Source(string name)=>Encoding.UTF8.GetString(DailyHookCompiler.Resource(name));
        string loader=Source("Hook.Loader.cs");
        string ipc=string.Join("\n",new[]{"Handoff","MainThread","LegacyPilots"}.Select(n=>Source("Hook.Ipc."+n+".cs").Replace("using System;", "").Replace("using System.Collections.Generic;", "").Replace("using System.Reflection;", "")));
        byte[] harmony=DailyHookCompiler.Resource("BD2Daily.Harmony.dll");
        var payload=Compile("BD2Daily.Runtime4.Probe", "using System.Collections.Generic;\nusing System.Globalization;\nusing Neo.Unity.Neon;\n"+loader+ipc+stub+Source("Hook.SdkIdentity.cs").Replace("using System;", "").Replace("using System.Globalization;", "").Replace("using Neo.Unity.Neon;", ""), refs.Append(MetadataReference.CreateFromImage(harmony)),resources:[new ResourceDescription("BD2Daily.Harmony.dll",()=>new MemoryStream(harmony),true)]);
        using(var asm=AssemblyDefinition.ReadAssembly(new MemoryStream(payload)))
        {
            var type=asm.MainModule.GetType("BD2Daily.Runtime.Loader");
            if(type.Fields.Any(f=>f.FieldType.FullName.Contains("RuntimeEngine")||f.FieldType.FullName.Contains("Harmony")))throw new Exception("Loader eagerly binds a runtime dependency");
            foreach(var instruction in type.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions))
                if(instruction.Operand is MemberReference m&&(m.DeclaringType?.FullName.Contains("RuntimeEngine")==true||m.DeclaringType?.FullName.Contains("HarmonyLib")==true))throw new Exception("Loader eagerly invokes runtime dependency");
            asm.Name.Name+=".Replacement";asm.MainModule.Mvid=Guid.NewGuid();asm.Write(Path.Combine(output,"replacement.dll"));
        }
        File.WriteAllBytes(Path.Combine(output,"bootstrap.dll"),payload);
        var runner="""
using System;
using System.IO;
using System.Reflection;
namespace UnityEngine {public static class Canvas {public static event Action willRenderCanvases;public static void Fire(){if(willRenderCanvases!=null)willRenderCanvases();}}}
public class Runner {
 static string Root {get{return Environment.GetEnvironmentVariable("DAILY_PROBE_OUTPUT");}}
 static void Call(Type loader,string method){loader.GetMethod(method).Invoke(null,null);if(method=="Load"){bool ready=false;foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="0Harmony")ready=true;if(!ready)throw new Exception("Dependency missing before main-thread callback");}for(int i=0;i<6;i++){UnityEngine.Canvas.Fire();System.Threading.Thread.Sleep(250);}}
 static void Check(string expected){var state=File.ReadAllText(Path.Combine(Root,"runtime.json"));if(state!=expected+"|")throw new Exception(state);}
 public static void Run(){try{
  foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="0Harmony")throw new Exception("Probe must start without Harmony");
  Assembly loaded=null;foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="BD2Daily.Runtime4.Probe")loaded=a;
  var loader=(loaded??Assembly.Load(File.ReadAllBytes(Path.Combine(Root,"bootstrap.dll")))).GetType("BD2Daily.Runtime.Loader",true);
  loader.Assembly.GetType("BD2Daily.Runtime.SdkProbe",true).GetMethod("Run").Invoke(null,null);
  Call(loader,"Load");Check("active");Call(loader,"Unload");Check("inactive");Call(loader,"Load");Check("active");
  var replacement=Assembly.Load(File.ReadAllBytes(Path.Combine(Root,"replacement.dll"))).GetType("BD2Daily.Runtime.Loader",true);
  Call(replacement,"Load");Check("active");Call(replacement,"Unload");Check("inactive");
  File.WriteAllText(Path.Combine(Root,"passed.txt"),"passed");
 }catch(Exception e){File.WriteAllText(Path.Combine(Root,"outer-error.txt"),e.ToString());}}
 public static int Main(){Run();return File.Exists(Path.Combine(Root,"passed.txt"))?0:1;}
}
""";
        File.WriteAllBytes(Path.Combine(output,"Probe.exe"),Compile("Probe",runner,refs,OutputKind.ConsoleApplication));
    }
}




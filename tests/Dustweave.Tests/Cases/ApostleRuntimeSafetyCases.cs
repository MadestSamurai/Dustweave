extern alias apostle;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Text;
using Compiler = BD2ApostleDefense.Compatibility.HookCompiler;

static class ApostleRuntimeSafetyCases
{
    public static void Run(List<string> cases)
    {
        // Execute the shipped callback code with synthetic native objects. No game
        // assemblies, injection, sockets, or user data are involved.
        var source=Encoding.UTF8.GetString(Compiler.Resource("Hook.NetworkGuard.cs"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(apostle::BD2ApostleDefense.Snapshot).Assembly.Location).Distinct()
            .Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("ApostleCallbacks"+Guid.NewGuid().ToString("N"),
            new[]{CSharpSyntaxTree.ParseText(source),CSharpSyntaxTree.ParseText(Fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var buffer=new MemoryStream();var emitted=compilation.Emit(buffer);
        if(!emitted.Success)throw new Exception(string.Join("\n",emitted.Diagnostics));
        var runner=Assembly.Load(buffer.ToArray()).GetType("Runner")!;
        cases.AddRange((string[])runner.GetMethod("Run")!.Invoke(null,null)!);
    }
    const string Fixture="""
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using BD2ApostleDefense;
using BD2ApostleDefense.Runtime;
namespace HarmonyLib {
 public enum HarmonyPatchType { Prefix }
 public class HarmonyMethod { public HarmonyMethod(Type t,string name){} }
 public class Harmony { public static bool FailRemove;public Harmony(string id){} public void Patch(MethodInfo m,HarmonyMethod prefix){} public void Unpatch(MethodInfo m,HarmonyPatchType p,string id){if(FailRemove)throw new Exception("synthetic unpatch failure");} }
}
namespace Network.TCP { public enum ENetMsg { SCConnectCloseRequest=1,CSDefenseReconnectResponse=2,SCDefenseGameStartRequest=3 } }
namespace BD2ApostleDefense {
 public static class IoDiagnostics { public static readonly List<string> Errors=new List<string>();public static void Record(string root,string op,string path,Exception e){Errors.Add(op);} }
}
namespace BD2ApostleDefense.Runtime {
 public sealed class Tcp { public string Mode="RandomDefense";public object Client;public bool Connected=true;public void Lost(){} }
 public sealed class Socket { public Tcp Owner; }
 public sealed class Monitor { public int Timeout=1000;public Action Lost; }
 public static class Bindings {
  public static Tcp Defense;public static Monitor Monitor;
  public static string Fail="";public static int Reads;
  public static void Native(){}
  public static MemberInfo Api(string role) {
   if(role=="Net.Defense")return typeof(Bindings).GetField("Defense");
   if(role=="Net.Mode")return typeof(Tcp).GetField("Mode");
   if(role=="Net.Monitor")return typeof(Bindings).GetField("Monitor");
   if(role=="Net.Timeout")return typeof(Monitor).GetField("Timeout");
   return typeof(Bindings).GetMethod("Native");
  }
  public static object Read(string role,object owner) {
   Reads++;if(role==Fail)throw new ObjectDisposedException("synthetic retired socket");
   if(role=="Net.Owner")return ((Socket)owner).Owner;
   if(role=="Net.Client")return ((Tcp)owner).Client;
   if(role=="Net.LostCallback")return ((Monitor)owner).Lost;
   if(role=="Net.Message")return owner;
   throw new Exception("unexpected read "+role);
  }
  public static object InvokeOn(string role,object owner,params object[] args){return ((Tcp)owner).Connected;}
  public static object EnumObject(string role,string name){return name;}
 }
 public static class Storage { public static void AppendNetwork(NetworkEvidence e){} }
}
public static class Runner {
 static readonly Type Guard=typeof(NetworkGuard);
 static object Call(string name,params object[] args){return Guard.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
 static void Check(bool ok,string message,List<string> checks){if(!ok)throw new Exception(message);checks.Add(message);}
 public static string[] Run(){var checks=new List<string>();
  var tcp=new Tcp();var socket=new Socket{Owner=tcp};tcp.Client=socket;Bindings.Defense=tcp;
  var lobby=new Snapshot{Stage="lobby"};var battle=new Snapshot{Stage="playing",Room="fixture"};
  NetworkGuard.Install();NetworkGuard.Pump(lobby);
  Check((bool)Call("Lost",tcp)&&(bool)Call("Restored",tcp),"apostle: lobby never suppresses native network callbacks",checks);
  NetworkGuard.Pump(battle);Call("Received",socket,0);
  Check(!(bool)Call("Lost",tcp),"apostle: active battle still retains bounded probe grace",checks);
  Check(!(bool)Call("Restored",tcp),"apostle: fresh battle packet avoids unnecessary reconnect",checks);
  NetworkGuard.Pump(lobby);
  Check((bool)Call("Lost",tcp),"apostle: returning from battle releases probe interception",checks);
  NetworkGuard.Pump(battle);Bindings.Fail="Net.Owner";Call("Received",socket,0);Bindings.Fail="";
  Check(true,"apostle: retired socket getter cannot throw into game receive task",checks);
  Bindings.Fail="Net.Client";
  Check((bool)Call("Lost",tcp)&&(bool)Call("Restored",tcp),"apostle: observer faults fall through to game's native recovery",checks);
  Bindings.Fail="Net.Message";Call("Received",socket,0);Bindings.Fail="";
  NetworkGuard.Flush();Check(IoDiagnostics.Errors.Contains("network-receive")&&IoDiagnostics.Errors.Contains("network-lost")&&IoDiagnostics.Errors.Contains("network-restored"),"apostle: callback errors are retained by background diagnostics",checks);
  var stale=new Socket{Owner=tcp};Call("Received",stale,1);
  var view=new Snapshot{Stage="playing",Room="fixture"};NetworkGuard.Apply(view);
  Check(view.NetworkState!="server-closed","apostle: retired socket cannot close current battle",checks);
  Call("Received",socket,1);NetworkGuard.Apply(view);
  Check(view.NetworkState=="server-closed"&&view.NetworkHold,"apostle: server close remains authoritative",checks);
  NetworkGuard.Remove();Bindings.Fail="Net.Owner";int reads=Bindings.Reads;
  Call("Received",socket,0);
  Check((bool)Call("Lost",tcp)&&(bool)Call("Restored",tcp)&&Bindings.Reads==reads,"apostle: callbacks after unload are inert before reading native objects",checks);
  Bindings.Fail="";NetworkGuard.Install();NetworkGuard.Pump(battle);
  Parallel.Invoke(()=>{for(int i=0;i<1000;i++)Call("Received",socket,0);},()=>{for(int i=0;i<100;i++){NetworkGuard.Remove();NetworkGuard.Install();NetworkGuard.Pump(battle);}});
  NetworkGuard.Remove();Check((bool)Call("Lost",tcp),"apostle: concurrent receive and component retirement preserve native recovery",checks);
  Bindings.Monitor=new Monitor{Lost=tcp.Lost};NetworkGuard.Install();NetworkGuard.Pump(battle);
  Check(Bindings.Monitor.Timeout==3000,"apostle: live battle applies bounded external probe timeout",checks);
  HarmonyLib.Harmony.FailRemove=true;bool removalFailed=false;try{NetworkGuard.Remove();}catch{removalFailed=true;}
  Check(removalFailed&&Bindings.Monitor.Timeout==1000&&(bool)Call("Lost",tcp),"apostle: failed unpatch still restores native timeout and retires callbacks",checks);
  HarmonyLib.Harmony.FailRemove=false;NetworkGuard.Remove();
  return checks.ToArray();
 }
}
""";
}

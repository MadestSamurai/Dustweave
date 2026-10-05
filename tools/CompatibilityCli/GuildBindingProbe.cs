using BD2Daily.Compatibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;
internal static class GuildBindingProbe {
 public static void Prepare(string managed,string output){
  var contract=JsonSerializer.Deserialize<BindingContract>(DailyHookCompiler.Resource("BD2Daily.GuildContract.json"))!;
  var startup=JsonSerializer.Deserialize<BindingContract>(DailyHookCompiler.Resource("BD2Daily.StartupContract.json"))!;
  string startupRoles=string.Join(",",startup.Apis.Select(a=>JsonSerializer.Serialize(a.Role)));
  string roles=string.Join(",",contract.Apis.Select(a=>JsonSerializer.Serialize(a.Role)));
  string source="""
using System;using System.IO;using System.Reflection;
class Probe {
 static int renders;static void OnCanvas(){renders++;}
 static int Main(string[] args){try{
  var a=Assembly.Load(File.ReadAllBytes(args[0]));var t=a.GetType("BD2Daily.Runtime.GuildBindings",true);
  if(!(bool)t.GetField("Supported",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue())throw new Exception("Unsupported guild bindings");
  var get=t.GetMethod("Get",BindingFlags.Static|BindingFlags.NonPublic);int count=0;
  foreach(var role in new string[]{ROLES}){var m=(MemberInfo)get.Invoke(null,new object[]{role});if(m==null||m.DeclaringType.Assembly.GetName().Name!="Assembly-CSharp")throw new Exception("Invalid binding "+role);count++;}
  var startup=a.GetType("BD2Daily.Runtime.StartupBindings",true);var startupGet=startup.GetMethod("Get",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(var role in new string[]{STARTUP_ROLES}){var m=(MemberInfo)startupGet.Invoke(null,new object[]{role});if(m==null)throw new Exception("Invalid startup binding "+role);count++;}
  var canvas=Assembly.Load("UnityEngine.UIModule").GetType("UnityEngine.Canvas",true);var evt=canvas.GetEvent("willRenderCanvases");
  var callback=Delegate.CreateDelegate(evt.EventHandlerType,typeof(Probe).GetMethod("OnCanvas",BindingFlags.Static|BindingFlags.NonPublic));
  evt.AddEventHandler(null,callback);canvas.GetMethod("SendWillRenderCanvases",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);evt.RemoveEventHandler(null,callback);
  if(renders!=1)throw new Exception("Canvas pump did not dispatch");
  File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("DAILY_PROBE_OUTPUT"),"bindings.txt"),"passed|"+count+"|canvas=1");return 0;
 }catch(Exception e){File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("DAILY_PROBE_OUTPUT"),"bindings.txt"),e.ToString());return 1;}}
}
""".Replace("STARTUP_ROLES",startupRoles).Replace("ROLES",roles);
  var references=new[]{"mscorlib.dll","System.dll","System.Core.dll"}.Select(f=>MetadataReference.CreateFromFile(Path.Combine(managed,f)));
  using var bytes=new MemoryStream();var result=CSharpCompilation.Create("BindingProbe",[CSharpSyntaxTree.ParseText(source)],references,new CSharpCompilationOptions(OutputKind.ConsoleApplication)).Emit(bytes);
  if(!result.Success)throw new Exception(string.Join("\n",result.Diagnostics));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"BindingProbe.exe"),bytes.ToArray());
 }
}

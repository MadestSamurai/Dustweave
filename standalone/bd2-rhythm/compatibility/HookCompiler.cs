using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
namespace BD2Rhythm.Compatibility;
public sealed class BindingReport {public string Status{get;set;}="compatible";public string ClientMvid{get;set;}="";public Dictionary<string,int> Members{get;set;}=new();public bool Injection{get;set;}=false;}
public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public sealed class CompatibilityException:Exception {public BindingReport Report{get;} public CompatibilityException(string message,BindingReport report):base(message){Report=report;}}
public static class HookCompiler {
 public static string ToolFingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(HookCompiler).Module.ModuleVersionId.ToString())));
 public static byte[] Resource(string name){using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException(name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
 public static PreparedHook Prepare(string managed){
  using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(managed);
  using var asm=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"Assembly-CSharp.dll"),new ReaderParameters{AssemblyResolver=resolver});var m=asm.MainModule;
  var hud=m.GetType("Rhythm.RhythmHUD")??throw new InvalidOperationException("客户端缺少音游界面。");var input=m.GetType("Rhythm.RhythmGameInput")??throw new InvalidOperationException("客户端缺少音游输入。");
  var report=new BindingReport{ClientMvid=m.Mvid.ToString()};void Add(string role,IMetadataTokenProvider p)=>report.Members.Add(role,p.MetadataToken.ToInt32());
  var lf=hud.Fields.Single(f=>f.FieldType.Resolve() is { } t && t.Module==m && t.Fields.Any(x=>x.FieldType.FullName=="Rhythm.RhythmLevelDataScriptable") && t.Methods.Any(x=>x.Parameters.Count==6&&x.Parameters[0].ParameterType.FullName=="Rhythm.RhythmLevelDataScriptable"));
  var looper=lf.FieldType.Resolve();Add("Looper",lf);Add("Chart",hud.Fields.Single(f=>f.FieldType.FullName=="Rhythm.RhythmLevelDataScriptable"));Add("Input",hud.Fields.Single(f=>f.FieldType.FullName==input.FullName));Add("Blocker",hud.Fields.Single(f=>f.Name=="_inputBlocker"));
  var first=hud.Methods.Single(x=>x.Name=="FirstUpdate"&&x.Parameters.Count==0);Add("FirstUpdate",first);
  var clock=first.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldfld).Select(i=>i.Operand).OfType<FieldReference>().Where(f=>f.DeclaringType.FullName==looper.FullName&&f.FieldType.FullName=="System.Int32").Select(f=>f.Resolve()).Distinct().Single();Add("Clock",clock);
  var state=looper.Properties.Single(p=>p.PropertyType.Resolve() is {IsEnum:true} t && new[]{"Play","Edit","PlayEnd","PlayGameOver"}.All(n=>t.Fields.Any(f=>f.Name==n)));Add("State",state.GetMethod);
  var fever=looper.Properties.Single(p=>p.PropertyType.Resolve() is {IsEnum:true} t&&t.Fields.Any(f=>f.Name=="FeverTime"));Add("Fever",fever.GetMethod);
  var pause=hud.Methods.Single(x=>x.Name=="PauseSound");Add("Paused",pause.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld).Select(i=>i.Operand).OfType<FieldReference>().Where(f=>f.FieldType.FullName=="System.Boolean").Select(f=>f.Resolve()).Distinct().Single());
  Add("Update",hud.Methods.Single(x=>x.Name=="Update"&&x.Parameters.Count==0));Add("Disable",hud.Methods.Single(x=>x.Name=="OnDestroy"&&x.Parameters.Count==0));
  Add("Pump",m.GetType("GameCameraManager").Methods.Single(x=>x.Name=="LateUpdate"&&x.Parameters.Count==0));
  foreach(var name in new[]{"onPressedLeftButton","onReleasedLeftButton","onPressedRightButton","onReleasedRightButton","onPressedLeftSlide","onPressedRightSlide"}){var f=input.Fields.Single(f=>f.Name==name);if(f.FieldType.FullName!="System.Action`1<System.Int32>")throw new InvalidOperationException("输入接口变化："+name);}
  var st=state.PropertyType.Resolve();if(Convert.ToInt32(st.Fields.Single(f=>f.Name=="Play").Constant)!=1)throw new InvalidOperationException("音游状态定义发生变化。");
  var chart=m.GetType("Rhythm.RhythmLevelDataScriptable");foreach(var name in new[]{"noteDataList","bpm","difficulty"})if(!chart.Fields.Any(f=>f.Name==name))throw new InvalidOperationException("谱面字段变化："+name);
  var maps="namespace BD2Rhythm.Runtime { internal static class ClientMap { internal const string Mvid="+JsonSerializer.Serialize(report.ClientMvid)+"; "+string.Join(" ",report.Members.Select(k=>"internal const int "+k.Key+"="+k.Value+";"))+" } }";
  var assembly=typeof(HookCompiler).Assembly;var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.")).OrderBy(n=>n).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();sources.Add(CSharpSyntaxTree.ParseText(maps));
  sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(ToolFingerprint) + "; } }"));
  var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll")){try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
  refs.Add(MetadataReference.CreateFromImage(Resource("BD2Rhythm.Harmony.dll")));
  var compilation=CSharpCompilation.Create("BD2Rhythm.Runtime3.Hot."+ToolFingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));using var output=new MemoryStream();
  var emitted=compilation.Emit(output,manifestResources:new[]{"BD2Rhythm.Harmony.dll"}.Select(n=>new ResourceDescription(n,()=>new MemoryStream(Resource(n)),true)));
  if(!emitted.Success)throw new InvalidOperationException("音游组件未能适配，尚未连接。\n"+string.Join("\n",emitted.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(20)));
  return new(output.ToArray(),report);
 }
}

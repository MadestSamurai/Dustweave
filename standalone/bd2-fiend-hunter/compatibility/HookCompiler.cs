using System.Text;using System.Text.Json;using System.Text.RegularExpressions;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
namespace BD2FiendHunter.Compatibility;
public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public static class HookCompiler {
 public static byte[] Resource(string name){using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException(name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
 public static string Fingerprint=>MetadataIndex.Hash(string.Join("|",typeof(HookCompiler).Assembly.GetManifestResourceNames().Order().Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
 public static PreparedHook Prepare(string managed){
 using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));var contract=JsonSerializer.Deserialize<BindingContract>(Resource("FiendHunter.Contract.json"))!;
 var resolved=BindingResolver.Resolve(index,contract);if(resolved.Report.Status!="compatible")throw new InvalidOperationException("Unsupported client interfaces: "+string.Join("; ",resolved.Report.Errors));
 var player=index.Types.Single(t=>t.FullName=="gamfs.ActionGame.ActionCharPlayer");
 var keyType=player.Methods.Single(m=>m.Name=="OnKeyDown"&&m.Parameters.Count==1).Parameters[0].ParameterType.Resolve();
 for(int key=0;key<5;key++)if(keyType.Fields.SingleOrDefault(f=>f.Name=="Skill"+(key+1))?.Constant is not int actual||actual!=key)throw new InvalidDataException("Native skill button layout changed");
 var names=new Dictionary<string,string>();
 void Add(string a,string b){if(names.TryGetValue(a,out var old)&&old!=b)throw new InvalidDataException("Ambiguous client symbol: "+a);names[a]=b;}
 foreach(var t in resolved.Types){var original=t.Key.Split('/').Last().Split('.').Last();if(MetadataIndex.Obfuscated(original))Add(original,t.Value.Name);}
 foreach(var m in resolved.Members){var old=m.Key.Split('|')[1];if(MetadataIndex.Obfuscated(old))Add(old,m.Value.Name);}
 var sources=typeof(HookCompiler).Assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.")).Select(n=>CSharpSyntaxTree.ParseText(Regex.Replace(Encoding.UTF8.GetString(Resource(n)),@"[\u0370-\u1fff]+",m=>names.GetValueOrDefault(m.Value,m.Value)),new CSharpParseOptions(preprocessorSymbols:new[]{"RUNTIME"}),path:n)).ToList();
 sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(Fingerprint) + "; } }"));
 var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll"))try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}
 refs.Add(MetadataReference.CreateFromImage(Resource("FiendHunter.Harmony.dll")));
 var compilation=CSharpCompilation.Create("BD2FiendHunter.PublicRuntime1.Hot."+Fingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
 using var output=new MemoryStream();var emit=compilation.Emit(output,manifestResources:[new ResourceDescription("FiendHunter.Harmony.dll",()=>new MemoryStream(Resource("FiendHunter.Harmony.dll")),true)]);
 if(!emit.Success)throw new InvalidOperationException(string.Join("\n",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(30)));
 return new(output.ToArray(),resolved.Report);
 }
}

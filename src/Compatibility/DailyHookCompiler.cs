using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace BD2Daily.Compatibility;
public sealed record PreparedDailyHook(byte[] Payload,BindingReport Report,BindingReport GuildReport,BindingReport StartupReport);
public static class DailyHookCompiler
{
    public static byte[] Resource(string name){using var s=typeof(DailyHookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing resource "+name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
    public static string SuiteFingerprint {get;set;}=""; public static string Fingerprint=>SuiteFingerprint.Length>0?MetadataIndex.Hash(BaseFingerprint+"|"+SuiteFingerprint):BaseFingerprint; private static string BaseFingerprint=>MetadataIndex.Hash(typeof(DailyHookCompiler).Module.ModuleVersionId+"|"+string.Join("|",typeof(DailyHookCompiler).Assembly.GetManifestResourceNames().Order().Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
    public static BindingContract Generate(string managed)
    {
        using var i=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var player=i.Find("ὨὬὣὫὩὯὩὩὣὠὧ");
        var data=player.Properties.Single(p=>p.Name=="ὫὩὢὤὬὭὢὣὭὮὣ"&&p.GetMethod?.IsStatic==true&&p.PropertyType.FullName=="Proto.Net.UserDBInfo");
        var camera=i.Find("GameCameraManager");var pump=camera.Methods.Single(m=>m.Name=="LateUpdate"&&m.Parameters.Count==0&&m.ReturnType.FullName=="System.Void");
        TypeContract Capture(TypeDefinition t,IMemberDefinition m)=>new(t.FullName,i.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(i.Body).ToArray(),new[]{new MemberContract(m.Name,MetadataIndex.Signature(m),i.MemberBody(m),i.Uses(m))});
        return new(1,new[]{Capture(player,data),Capture(camera,pump)},new[]{new ApiContract("Player.Data",player.FullName,data.Name,-1,MetadataIndex.Signature(data)),new ApiContract("Frame.Pump",camera.FullName,pump.Name,0,MetadataIndex.Signature(pump))},new());
    }
    public static PreparedDailyHook Prepare(string managed,BindingContract? contract=null,IReadOnlyDictionary<string,byte[]>? suite=null,string manifest="")
    {
        using var i=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        contract??=JsonSerializer.Deserialize<BindingContract>(Resource("BD2Daily.Contract.json"))!;
        var r=BindingResolver.Resolve(i,contract);if(r.Report.Status!="compatible")throw new InvalidOperationException(string.Join("; ",r.Report.Errors));
        using(var sdk=new MetadataIndex(Path.Combine(managed,"Neo.Unity.Neon.dll")))
        {
            foreach(var rule in new[]{("Neo.Unity.Neon.NeonSdk","IsInitialized","System.Boolean",true),("Neo.Unity.Neon.NeonSdk","Auth","Neo.Unity.Neon.NeonAuth",true),("Neo.Unity.Neon.NeonAuth","LoggedMember","Neo.Unity.Neon.Result.NeonAuthMemberResult",false),("Neo.Unity.Neon.Result.NeonAuthMemberResult","MemberId","System.Int64",false)})
            {var p=sdk.Find(rule.Item1).Properties.SingleOrDefault(p=>p.Name==rule.Item2);if(p?.GetMethod==null||!p.GetMethod.IsPublic||p.GetMethod.IsStatic!=rule.Item4||p.PropertyType.FullName!=rule.Item3)throw new InvalidOperationException("Current login identity API is unsupported: "+rule.Item2);}
        }
        var data=(PropertyDefinition)BindingResolver.Api(r,contract.Apis.Single(a=>a.Role=="Player.Data"));
        if(data.GetMethod?.IsStatic!=true||data.PropertyType.FullName!="Proto.Net.UserDBInfo")throw new InvalidOperationException("Player identity getter changed");
        var pump=(MethodDefinition)BindingResolver.Api(r,contract.Apis.Single(a=>a.Role=="Frame.Pump"));
        var sources=typeof(DailyHookCompiler).Assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.")).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();
        string Cs(string s)=>JsonSerializer.Serialize(s);
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2Daily.Runtime { internal static class ClientNames { internal const string DataType="+Cs(data.DeclaringType.FullName)+"; internal const string DataProperty="+Cs(data.Name)+"; internal const string PumpType="+Cs(pump.DeclaringType.FullName)+"; internal const string PumpMethod="+Cs(pump.Name)+"; } }"));
        sources.Add(CSharpSyntaxTree.ParseText(GuildHookContract.Source(i,out var guildReport),path:"GuildBindings.cs"));
        sources.Add(CSharpSyntaxTree.ParseText(StartupHookContract.Source(i,out var startupReport),path:"StartupBindings.cs"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint="+Cs(Fingerprint)+"; } }"));
        var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll")){try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
        refs.Add(MetadataReference.CreateFromImage(Resource("BD2Daily.Harmony.dll")));
        var compilation=CSharpCompilation.Create("BD2Daily.Runtime4.Hot."+Fingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        var resources=new List<ResourceDescription>{new("BD2Daily.Harmony.dll",()=>new MemoryStream(Resource("BD2Daily.Harmony.dll")),true)}; if(suite!=null){foreach(var item in suite){var bytes=item.Value;resources.Add(new("Suite."+item.Key+".dll",()=>new MemoryStream(bytes),true));}resources.Add(new("Suite.manifest",()=>new MemoryStream(Encoding.UTF8.GetBytes(manifest)),true));} using var b=new MemoryStream();var result=compilation.Emit(b,manifestResources:resources);
        if(!result.Success)throw new InvalidOperationException("Component compilation failed before injection: "+string.Join("\n",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(20)));
        return new(b.ToArray(),r.Report,guildReport,startupReport);
    }
}


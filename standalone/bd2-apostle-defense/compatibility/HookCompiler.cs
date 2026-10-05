using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace BD2ApostleDefense.Compatibility;

public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public static class HookCompiler
{
    public static string ToolFingerprint => MetadataIndex.Hash(typeof(HookCompiler).Module.ModuleVersionId+"|"+string.Join("|",typeof(HookCompiler).Assembly.GetManifestResourceNames().OrderBy(n=>n,StringComparer.Ordinal).Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
    public static byte[] Resource(string name)
    {using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing embedded resource: "+name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
    public static BindingContract Contract()=>JsonSerializer.Deserialize<BindingContract>(Resource("BD2ApostleDefense.Contract.json"))!;
    public static PreparedHook Prepare(string managed,BindingContract? contract=null)
    {
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var resolved=BindingResolver.Resolve(index,contract??Contract());
        if(resolved.Report.Status!="compatible")throw new CompatibilityException(resolved.Report);

        ValidateInputs(resolved);
        var assembly=typeof(HookCompiler).Assembly;
        var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.",StringComparison.Ordinal)).OrderBy(n=>n,StringComparer.Ordinal).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText(GenerateSource(resolved),path:"ApostleClient.g.cs"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(ToolFingerprint) + "; } }"));
        var refs=new List<MetadataReference>();
        // Read metadata only. Do not execute or copy game assemblies into the application directory.
        foreach(var file in Directory.EnumerateFiles(managed,"*.dll").OrderBy(x=>x,StringComparer.Ordinal))
        {try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
        refs.Add(MetadataReference.CreateFromImage(Resource("BD2ApostleDefense.Harmony.dll")));
        var compilation=CSharpCompilation.Create("BD2ApostleDefense.Runtime9."+ToolFingerprint.Substring(0,16),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        using var stream=new MemoryStream();
        var emit=compilation.Emit(stream,manifestResources:new[]{new ResourceDescription("BD2ApostleDefense.Harmony.dll",()=>new MemoryStream(Resource("BD2ApostleDefense.Harmony.dll")),true)});
        if(!emit.Success)throw new InvalidOperationException("当前客户端接口无法编译，尚未注入。\n"+string.Join("\n",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(30)));
        var payload=stream.ToArray();

        return new(payload,resolved.Report);
    }
    private static void ValidateInputs(ResolvedBindings r)
    {
        var hud=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Hud.Click"));
        foreach(var name in new[]{"SpawnMyUnit","ReleaseMyUnit","UpgradeElement"})
            if(!hud.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name==name))throw new InvalidOperationException("原生操作入口已变化："+name);
        var my=r.Types[r.Contract.Roles["PlayerKind"]].Fields.Single(f=>f.Name=="MyPlayer");
        if(Convert.ToInt32(my.Constant)!=0)throw new InvalidOperationException("己方盘面枚举发生变化");
        var attackState=(FieldDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Unit.AttackState"));
        var phases=attackState.FieldType.Resolve();
        foreach(var phase in new[]{("Idle",1),("Attack",2),("FindTarget",3)})
            if(phases.Fields.SingleOrDefault(f=>f.Name==phase.Item1)?.Constant is not int actual || actual!=phase.Item2)
                throw new InvalidOperationException("使徒攻击状态枚举发生变化，无法可靠预测换位输出");
    }
    public static string GenerateSource(ResolvedBindings r)
    {
        static string Q(string s)=>JsonSerializer.Serialize(s);
        var types=r.Types.ToDictionary(x=>x.Key,x=>x.Value.FullName.Replace('/','+'));
        foreach(var role in r.Contract.Roles)types[role.Key]=r.Types[role.Value].FullName.Replace('/','+');
        var names=new Dictionary<string,string>();
        foreach(var type in r.Contract.Types)foreach(var member in type.Members)
        {
            var actual=r.Members[BindingResolver.Key(type.Name,member.Name,member.Signature)];
            var key=actual.DeclaringType.FullName.Replace('/','+')+"|"+member.Name;
            if(names.TryGetValue(key,out var previous) && previous!=actual.Name)throw new InvalidOperationException("Reflection overload mapping is ambiguous: "+key);
            names[key]=actual.Name;
        }
        var apiEntries=r.Contract.Apis.Select(api=>
        {
            var m=BindingResolver.Api(r,api);var method=m is MethodDefinition;
            return "{"+Q(api.Role)+",new[]{"+Q(m.DeclaringType.FullName.Replace('/','+'))+","+Q(method?m.MetadataToken.ToInt32().ToString():m.Name)+","+Q(method?"method":"member")+"}}";
        });
        string Dictionary(Dictionary<string,string> d)=>"new System.Collections.Generic.Dictionary<string,string>{"+string.Join(",",d.Select(x=>"{"+Q(x.Key)+","+Q(x.Value)+"}"))+"}";
        return "namespace BD2ApostleDefense.Runtime { internal static class ApostleClient { internal const string CompiledMvid="+Q(r.Report.ClientMvid)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> TypeNames="+Dictionary(types)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> MemberNames="+Dictionary(names)+"; internal static readonly System.Collections.Generic.Dictionary<string,string[]> Apis=new System.Collections.Generic.Dictionary<string,string[]>{"+string.Join(",",apiEntries)+"}; }}";
    }
}
public sealed class CompatibilityException : Exception
{
    public BindingReport Report {get;}
    public CompatibilityException(BindingReport report):base("当前客户端有无法确认的使徒防守接口，尚未注入。\n"+string.Join("\n",report.Errors.Take(12))){Report=report;}
}

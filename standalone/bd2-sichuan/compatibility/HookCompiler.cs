using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace BD2Sichuan.Compatibility;

public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public static class HookCompiler
{
    public static string ToolFingerprint => MetadataIndex.Hash(typeof(HookCompiler).Module.ModuleVersionId+"|"+string.Join("|",typeof(HookCompiler).Assembly.GetManifestResourceNames().OrderBy(n=>n,StringComparer.Ordinal).Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
    public static byte[] Resource(string name)
    {using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing embedded resource: "+name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
    public static BindingContract Contract()=>JsonSerializer.Deserialize<BindingContract>(Resource("BD2Sichuan.Contract.json"))!;
    public static PreparedHook Prepare(string managed,BindingContract? contract=null,bool daily=false)
    {
        string fingerprint=ToolFingerprint+(daily?".daily":"");
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var resolved=BindingResolver.Resolve(index,contract??Contract());
        if(resolved.Report.Status!="compatible")throw new CompatibilityException(resolved.Report);
        ValidatePairEvent(resolved);
        var expectedKinds=resolved.Contract.TileKinds??throw new InvalidDataException("Missing tile-kind contract");
        var actualKinds=resolved.Types[resolved.Contract.Roles["TileKind"]].Fields.Where(f=>f.HasConstant).ToDictionary(f=>f.Name,f=>Convert.ToInt64(f.Constant));
        if(expectedKinds.Count!=actualKinds.Count || expectedKinds.Any(p=>!actualKinds.TryGetValue(p.Key,out var value)||value!=p.Value))
            throw new InvalidOperationException("连连看方块规则发生变化，需要更新求解规则；尚未注入。");
        var assembly=typeof(HookCompiler).Assembly;
        var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.",StringComparison.Ordinal)).OrderBy(n=>n,StringComparer.Ordinal).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText(GenerateSource(resolved),path:"SichuanClient.g.cs"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(fingerprint) + "; public const string Group = " + JsonSerializer.Serialize(daily?"daily":"sichuan") + "; } }"));
        var refs=new List<MetadataReference>();
        // Read metadata only. Do not execute or copy game assemblies into the application directory.
        foreach(var file in Directory.EnumerateFiles(managed,"*.dll").OrderBy(x=>x,StringComparer.Ordinal))
        {try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
        refs.Add(MetadataReference.CreateFromImage(Resource("BD2Sichuan.Harmony.dll")));
        var compilation=CSharpCompilation.Create("BD2Sichuan.Runtime2"+".Hot."+ToolFingerprint.Substring(0,12)+(daily?".Daily":""),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        using var stream=new MemoryStream();
        var emit=compilation.Emit(stream,manifestResources:new[]{new ResourceDescription("BD2Sichuan.Harmony.dll",()=>new MemoryStream(Resource("BD2Sichuan.Harmony.dll")),true)});
        if(!emit.Success)throw new InvalidOperationException("当前客户端接口无法编译，尚未注入。\n"+string.Join("\n",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(30)));
        var payload=stream.ToArray();
        ValidateObservers(index,resolved);
        return new(payload,resolved.Report);
    }
    private static void ValidatePairEvent(ResolvedBindings r)
    {
        var add=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Model.PairAdd"));
        var remove=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Model.PairRemove"));
        var handler=add.Parameters.Single().ParameterType as GenericInstanceType;
        if(handler==null || handler.ElementType.FullName!="System.Action`7" || handler.GenericArguments.Count!=7 ||
           handler.GenericArguments[0].FullName!="System.Int32" || handler.GenericArguments[2].FullName!="System.Int32" ||
           remove.Parameters.Single().ParameterType.FullName!=handler.FullName)
            throw new InvalidOperationException("连连看配对事件签名变化，尚未注入。");
    }
    public static void ValidateObservers(MetadataIndex index,ResolvedBindings r)
    {
        var helper=r.Types[r.Contract.Roles["NetworkHelper"]];
        var methods=MetadataIndex.Walk(new[]{helper}).SelectMany(t=>t.Methods).Where(m=>m.ReturnType.FullName=="System.Boolean" && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Byte[]","System.Int32","System.Int32"}) && m.HasBody).ToArray();
        foreach(var kind in new[]{"Start","End"})
        {
            string response="Proto.Net.MiniGameSichuan"+kind+"Response";
            int count=methods.Count(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference call && call.DeclaringType.FullName==response && call.Name=="get_Parser"));
            if(count!=1)throw new InvalidOperationException(response+" 原生回执入口不唯一，尚未注入："+count);
        }
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
        string TypeName(TypeReference t)=>t is GenericInstanceType g
            ? System.Text.RegularExpressions.Regex.Replace(g.ElementType.FullName,@"`\d+","").Replace('/','+')+"<"+string.Join(",",g.GenericArguments.Select(TypeName))+">"
            :t.FullName.Replace('/','.');
        var add=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Model.PairAdd"));
        var handlerType=TypeName(add.Parameters.Single().ParameterType);
        var pairSource="internal static System.Delegate CreatePairHandler(System.Action<int,int> callback) { return new "+handlerType+"((v0,v1,v2,v3,v4,v5,v6)=>callback(v0,v2)); }";
        string Dictionary(Dictionary<string,string> d)=>"new System.Collections.Generic.Dictionary<string,string>{"+string.Join(",",d.Select(x=>"{"+Q(x.Key)+","+Q(x.Value)+"}"))+"}";
        return "namespace BD2Sichuan.Runtime { internal static class SichuanClient { internal const string CompiledMvid="+Q(r.Report.ClientMvid)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> TypeNames="+Dictionary(types)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> MemberNames="+Dictionary(names)+"; internal static readonly System.Collections.Generic.Dictionary<string,string[]> Apis=new System.Collections.Generic.Dictionary<string,string[]>{"+string.Join(",",apiEntries)+"}; "+pairSource+" }}";
    }
}
public sealed class CompatibilityException : Exception
{
    public BindingReport Report {get;}
    public CompatibilityException(BindingReport report):base("当前客户端有无法确认的连连看接口，尚未注入。\n"+string.Join("\n",report.Errors.Take(12))){Report=report;}
}

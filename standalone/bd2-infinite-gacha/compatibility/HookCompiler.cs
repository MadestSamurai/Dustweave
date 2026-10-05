using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
namespace BD2InfiniteGacha.Compatibility;
public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public static class HookCompiler
{
    public static byte[] Resource(string name){using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException(name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
    public static string Fingerprint=>MetadataIndex.Hash(string.Join("|",typeof(HookCompiler).Assembly.GetManifestResourceNames().Order().Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
    public static BindingContract Generate(string managed)
    {
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var roles=new List<(string role,IMemberDefinition member)>();
        void Method(string role,string type,string name,int count)=>roles.Add((role,index.Find(type).Methods.Single(m=>m.Name==name&&m.Parameters.Count==count)));
        void Property(string role,string type,string name)=>roles.Add((role,index.Find(type).Properties.Single(p=>p.Name==name)));
        void Field(string role,string type,string name)=>roles.Add((role,index.Find(type).Fields.Single(f=>f.Name==name)));
        Property("Player","ὨὬὣὫὩὯὩὩὣὠὧ","ὫὩὢὤὬὭὢὣὭὮὣ");
        Property("Inventory","ὬὮὨὣὣὮὨὢὠὡὮ","ὡὨὪὫὯὮὡὢὠὥὡ");
        Method("Groups","ὯὠὨὩὯὯὯὫὫὤὨ","ὮὡὨὮὯὣὩὦὥὢὡ",1);
        Method("Group","ὯὠὨὩὯὯὯὫὫὤὨ","ὨὩὫὤὩὮὢὠὦὭὬ",1);
        Method("Draw","ὯὠὨὩὯὯὯὫὫὤὨ","ὢὮὪὡὦὤὠὭὭὪὭ",1);
        Method("Expand","ὧὡὥὭὥὡὠὩὧὡὤ","ὧὭὮὢὯὧὯὤὯὭὯ",2);
        Method("Costume","ὣὡὭὨὠὥὢὣὡὬὠ","ὧὡὠὬὢὦὮὦὥὢὡ",1);
        Method("Character","ὣὤὯὠὯὦὢὡὨὢὡ","ὭὭὯὤὦὩὫὮὧὣὥ",1);
        Method("Text","ὨὣὡὪὨὬὡὭὭὬὬ","ὥὢὮὣὮὠὦὧὫὡὥ",1);
        Property("ItemId","ὥὣὮὨὫὣὩὨὪὣὭ","ὢὯὧὤὮὭὮὣὭὪὣ");Property("ItemType","ὥὣὮὨὫὣὩὨὪὣὭ","ὬὯὭὩὡὮὩὨὢὭὣ");
        Field("ResultGroup","GachaResultUI","ὪὩὩὦὤὠὢὠὨὣὨ");Field("ResultItems","GachaResultUI","ὤὠὨὣὢὩὧὭὡὫὧ");
        Field("RedrawButton","GachaResultUI","_objRedrawGachaButton");
        Field("SkipButton","GachaResultUI","_objSkipButton");
        Field("AnimationStage","GachaResultUI","ὯὮὣὬὨὬὤὨὩὤὧ");
        Field("BusyPreview","GachaResultUI","ὨὢὢὡὢὥὬὡὢὨὤ");
        Method("SetResult","GachaResultUI","SetResult",3);
        Method("Preview","ὬὬὯὠὫὡὪὮὢὦὤ","ὮὬὨὤὪὤὫὫὭὭὠ",6);
        Method("Response","ὬὬὯὠὫὡὪὮὢὦὤ","ὦὥὣὢὬὩὧὠὦὤὢ",3);
        Method("Schedule","ὬὬὯὠὫὡὪὮὢὦὤ","ὢὯὡὧὣὪὥὠὧὧὯ",1);
        Method("SavedPreview","ὬὬὯὠὫὡὪὮὢὦὤ","ὮὧὤὮὣὫὨὣὢὨὬ",0);
        Method("Product","ὡὭὣὡὨὪὨὢὦὧὡ","ὯὪὣὧὥὯὬὫὣὡὣ",0);
        Method("Frame","GameCameraManager","LateUpdate",0);
        var types=roles.GroupBy(r=>r.member.DeclaringType).Select(g=>new TypeContract(g.Key.FullName,index.Shape(g.Key),g.Key.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),g.Select(r=>r.member).Distinct().Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray();
        return new(1,types,roles.Select(r=>new ApiContract(r.role,r.member.DeclaringType.FullName,r.member.Name,r.member is MethodDefinition m?m.Parameters.Count:-1,MetadataIndex.Signature(r.member))).ToArray(),new());
    }
    public static PreparedHook Prepare(string managed, string assemblyName="BD2InfiniteGacha.Runtime5")
    {
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var contract=JsonSerializer.Deserialize<BindingContract>(Resource("BD2InfiniteGacha.Contract.json"))!;
        var resolved=BindingResolver.Resolve(index,contract);if(resolved.Report.Status!="compatible")throw new InvalidOperationException(string.Join("; ",resolved.Report.Errors));
        var names=(MethodDefinition)BindingResolver.Api(resolved,contract.Apis.Single(a=>a.Role=="Text"));
        if(!names.Body.Instructions.Any(i=>i.Operand is string text&&text.Contains("NameTextTable")))throw new InvalidOperationException("名称读取接口未确认使用 NameTextTable，已停止连接");
        var preview=(MethodDefinition)BindingResolver.Api(resolved,contract.Apis.Single(a=>a.Role=="Preview"));
        var creates=preview.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Where(m=>m.Name==".ctor").Select(m=>m.DeclaringType.FullName).ToArray();
        if(!creates.Contains("Proto.Net.GachaBuyPreviewRequest")||creates.Any(n=>n.Contains("Request")&&n!="Proto.Net.GachaBuyPreviewRequest"))throw new InvalidOperationException("重复预览接口已改变，拒绝将其用于自动刷新");
        var helper=preview.DeclaringType;
        var refresh=helper.Methods.Single(m=>m.IsStatic&&m.ReturnType.FullName=="System.Void"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Action"})&&m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference call&&call.Name==".ctor"&&call.DeclaringType.FullName=="Proto.Net.GachaInfoRequest"));
        var refreshed=helper.Methods.Single(m=>m.IsStatic&&m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Byte[]","System.Int32","System.Int32"})&&m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference call&&call.Name=="get_Parser"&&call.DeclaringType.FullName=="Proto.Net.GachaInfoResponse"));
        var recoveryDeclarations=new[]{("Refresh",refresh),("RefreshResponse",refreshed)}.Select(x=>"internal const string "+x.Item1+"Type="+JsonSerializer.Serialize(x.Item2.DeclaringType.FullName)+", "+x.Item1+"Name="+JsonSerializer.Serialize(x.Item2.Name)+";");
        var declarations=contract.Apis.Select(a=>{var m=BindingResolver.Api(resolved,a);return "internal const string "+a.Role+"Type="+JsonSerializer.Serialize(m.DeclaringType.FullName)+", "+a.Role+"Name="+JsonSerializer.Serialize(m.Name)+";";});
        var sources=typeof(HookCompiler).Assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.")).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2InfiniteGacha.Runtime { internal static class Names {"+string.Join("\n",declarations.Concat(recoveryDeclarations))+"} }"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(Fingerprint) + "; } }"));
        var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll"))try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}
        refs.Add(MetadataReference.CreateFromImage(Resource("BD2InfiniteGacha.Harmony.dll")));
        var c=CSharpCompilation.Create(assemblyName+".Hot."+Fingerprint.Substring(0,12),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        using var b=new MemoryStream();var result=c.Emit(b,manifestResources:[new ResourceDescription("BD2InfiniteGacha.Harmony.dll",()=>new MemoryStream(Resource("BD2InfiniteGacha.Harmony.dll")),true)]);
        if(!result.Success)throw new InvalidOperationException(string.Join("\n",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(20)));
        return new(b.ToArray(),resolved.Report);
    }
}

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace BD2Territory.Compatibility;

public sealed record PreparedHook(byte[] Payload,BindingReport Report);
public static class HookCompiler
{
    public static string ToolFingerprint => MetadataIndex.Hash(typeof(HookCompiler).Module.ModuleVersionId+"|"+string.Join("|",typeof(HookCompiler).Assembly.GetManifestResourceNames().OrderBy(n=>n,StringComparer.Ordinal).Select(n=>MetadataIndex.Hash(Convert.ToBase64String(Resource(n))))));
    public static byte[] Resource(string name)
    {using var s=typeof(HookCompiler).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing embedded resource: "+name);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
    public static BindingContract Contract()=>JsonSerializer.Deserialize<BindingContract>(Resource("BD2Territory.Contract.json"))!;
    public static PreparedHook Prepare(string managed,BindingContract? contract=null)
    {
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var resolved=BindingResolver.Resolve(index,contract??Contract());
        if(resolved.Report.Status!="compatible")throw new CompatibilityException(resolved.Report);
        ValidateEnums(resolved);
        ValidateRuntimeEntryPoints(resolved);
        ValidateSurfaceInterfaces(index);
        var assembly=typeof(HookCompiler).Assembly;
        var sources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Hook.",StringComparison.Ordinal)).OrderBy(n=>n,StringComparer.Ordinal).Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(Resource(n)),path:n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText(GenerateSource(resolved),path:"TerritoryClient.g.cs"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint = " + JsonSerializer.Serialize(ToolFingerprint) + "; } }"));
        var refs=new List<MetadataReference>();
        // Read metadata only. Do not execute or copy game assemblies into the application directory.
        foreach(var file in Directory.EnumerateFiles(managed,"*.dll").OrderBy(x=>x,StringComparer.Ordinal))
        {try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}}
        refs.Add(MetadataReference.CreateFromImage(Resource("BD2Territory.Harmony.dll")));
        var compilation=CSharpCompilation.Create("BD2Territory.Runtime26."+ToolFingerprint.Substring(0,16),sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        using var stream=new MemoryStream();
        var emit=compilation.Emit(stream,manifestResources:new[]{new ResourceDescription("BD2Territory.Harmony.dll",()=>new MemoryStream(Resource("BD2Territory.Harmony.dll")),true)});
        if(!emit.Success)throw new InvalidOperationException("当前客户端接口无法编译，尚未注入。\n"+string.Join("\n",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(30)));
        var payload=stream.ToArray();
        ValidateObservers(index,resolved);
        return new(payload,resolved.Report);
    }
    private static void ValidateEnums(ResolvedBindings r)
    {
        foreach(var pair in new[]{("MoveKind","CharController",0),("MoveKind","Navigation",1),("ToolKind","LoggingTool",2),("ToolKind","MiningTool",3),("ToolKind","FarmingTool",4),("FunctionKind","LoggingAble",1),("FunctionKind","MiningAble",2),("FunctionKind","FarmingAble",3)})
        {
            var f=r.Types[r.Contract.Roles[pair.Item1]].Fields.SingleOrDefault(f=>f.Name==pair.Item2&&f.HasConstant);
            if(f==null||Convert.ToInt32(f.Constant)!=pair.Item3)throw new InvalidOperationException("领地枚举身份发生变化："+pair);
        }
    }

    private static void ValidateSurfaceInterfaces(MetadataIndex index)
    {
        var chunk=index.Find("FieldEvent.Life.Chunk.GroundChunk");
        var cell=chunk.Methods.Single(m=>m.Name=="GetCellType"&&m.Parameters.Count==2).ReturnType.Resolve();
        if(!cell.IsEnum||!new[]{"None","Ground","Water"}.All(n=>cell.Fields.Any(f=>f.Name==n&&f.HasConstant)))
            throw new InvalidOperationException("Cannot identify territory land/water cells; nothing was injected.");
        var gate=index.Find("FieldEvent.Life.Chunk.LifePlaceableObject_ColliderGate");
        if(gate.Fields.Count(f=>!f.IsStatic&&f.FieldType.FullName=="UnityEngine.BoxCollider")!=1)
            throw new InvalidOperationException("Cannot identify the native bridge crossing gate; nothing was injected.");
    }
    private static FieldDefinition NormalStepField(ResolvedBindings r)
    {
        var type=r.Types["PlayerMoveController"];
        var fields=new List<FieldDefinition>();
        foreach(var method in type.Methods.Where(m=>m.HasBody))
        {
            var il=method.Body.Instructions.Where(i=>i.OpCode.Code!=Mono.Cecil.Cil.Code.Nop).ToArray();
            for(int i=1;i<il.Length;i++)
            if(il[i].OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&il[i].Operand is FieldReference field&&
               il[i-1].Operand is MethodReference read&&read.DeclaringType.FullName=="UnityEngine.CharacterController"&&read.Name=="get_stepOffset"&&
               field.DeclaringType.FullName==type.FullName&&field.FieldType.FullName=="System.Single")fields.Add(field.Resolve());
        }
        var matches=fields.GroupBy(f=>f.FullName).Select(g=>g.First()).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Cannot identify the game's saved step height; nothing was injected.");
        return matches[0];
    }
    private static void ValidateRuntimeEntryPoints(ResolvedBindings r)
    {
        NormalStepField(r);
        var eventApi=r.Contract.Apis.Single(a=>a.Role=="Tool.Event");var handler=(MethodDefinition)BindingResolver.Api(r,eventApi);
        var casts=handler.Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Isinst).Select(i=>((TypeReference)i.Operand).Resolve()).ToArray();
        if(casts.Length!=1||!casts[0].Methods.Any(m=>m.IsConstructor&&!m.IsStatic&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Int32","System.Int32"})))
            throw new InvalidOperationException("工具事件类型无法在注入前唯一确认");
        var face=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Player.Face"));
        if(face.IsStatic||face.ReturnType.FullName!="System.Void"||!face.HasBody||!face.Body.Instructions.Select((i,n)=>new{Instruction=i,Index=n}).Any(x=>x.Instruction.Operand is MethodReference m&&m.Name=="UpdateRotatePlayer"&&x.Index>0&&face.Body.Instructions[x.Index-1].OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_1))
            throw new InvalidOperationException("角色模型转向入口缺少强制刷新，尚未注入");
        // Movement prediction must use the controller actually held by MoveController,
        // not an arbitrary component found on the visible avatar.
        if(r.Types["MoveController"].Fields.Count(f=>!f.IsStatic&&f.FieldType.FullName=="UnityEngine.CharacterController")!=1)
            throw new InvalidOperationException("角色移动控制器无法唯一确认，尚未注入");
        var control=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Player.ChangeMoveType"));
        var moveType=(PropertyDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Player.MoveType"));
        var controlCalls=control.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        if(!controlCalls.Any(m=>m.Name=="StopMove")||!controlCalls.Any(m=>m.FullName==moveType.SetMethod.FullName))
            throw new InvalidOperationException("采集前退出寻路的入口缺少停步或控制模式切换，尚未注入");
        var start=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Player.StartMove"));
        if(!start.HasBody||!start.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetPlayerMoveState"))throw new InvalidOperationException("角色起步入口缺少移动状态设置");
        var back=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Ui.Back"));
        if(!back.HasBody||!back.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="OnClickUI"))throw new InvalidOperationException("弹窗确认入口缺少原生确认事件派发");
        var nav=(MethodDefinition)BindingResolver.Api(r,r.Contract.Apis.Single(a=>a.Role=="Player.SetMoveNav"));
        if(nav.IsStatic||nav.ReturnType.FullName!="System.Boolean"||!nav.HasBody||!nav.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="MoveController"&&m.Name=="SetMoveNav"))throw new InvalidOperationException("角色寻路入口缺少原生完整路径检查");

    }
    public static void ValidateObservers(MetadataIndex index,ResolvedBindings r)
    {
        foreach(var response in new[]{"LifeWorldObjectGatheringResponse","LifeSeedingResponse","LifeWorldObjectPlaceSaveResponse","LifeWorldObjectPositionSaveResponse","LifeCookingResponse","LifeShopSellResponse","LifeInfoResponse"})
        {
            var count=index.Types.SelectMany(t=>t.Methods).Count(m=>m.HasBody && m.ReturnType.FullName=="System.Boolean" && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Byte[]","System.Int32","System.Int32"}) && m.Body.Instructions.Any(i=>i.Operand is MethodReference call && call.DeclaringType.FullName=="Proto.Net."+response && call.Name=="get_Parser"));
            if(count<1)throw new InvalidOperationException("缺少原生回执入口："+response);
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
        apiEntries=apiEntries.Concat(SalesBindings.Resolve(r).Select(pair=>"{"+Q(pair.Key)+",new[]{"+Q(pair.Value.DeclaringType.FullName.Replace('/','+'))+","+Q(pair.Value.MetadataToken.ToInt32().ToString())+","+Q("method")+"}}"));
        string Dictionary(Dictionary<string,string> d)=>"new System.Collections.Generic.Dictionary<string,string>{"+string.Join(",",d.Select(x=>"{"+Q(x.Key)+","+Q(x.Value)+"}"))+"}";
        return "namespace BD2Territory.Runtime { internal static class TerritoryClient { internal const string NormalStepFieldName="+Q(NormalStepField(r).Name)+"; internal const string CompiledMvid="+Q(r.Report.ClientMvid)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> TypeNames="+Dictionary(types)+"; internal static readonly System.Collections.Generic.Dictionary<string,string> MemberNames="+Dictionary(names)+"; internal static readonly System.Collections.Generic.Dictionary<string,string[]> Apis=new System.Collections.Generic.Dictionary<string,string[]>{"+string.Join(",",apiEntries)+"}; }}";
    }
}
public sealed class CompatibilityException : Exception
{
    public BindingReport Report {get;}
    public CompatibilityException(BindingReport report):base("当前客户端有无法确认的领地接口，尚未注入。\n"+string.Join("\n",report.Errors.Take(12))){Report=report;}
}

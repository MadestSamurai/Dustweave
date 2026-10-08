using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Dustweave.Compatibility;

namespace Dustweave.Connection;

public sealed record LiveEnumContract(string Type, Dictionary<string,long> Values);
public sealed record LiveUsageContract(string Key, string[] Anchors, string Accessor);
public sealed record LiveTypeLink(string Type, string Member, int Slot);
public sealed record LiveCallLink(string Member, string Target);
public sealed record LiveBindingContract(BindingContract Bindings, LiveEnumContract[] Enums, LiveUsageContract[] Uses, LiveTypeLink[] Links, LiveCallLink[] Calls);

// Store interface descriptions and one-way hashes, never client binaries or IL.
// Generation is an explicit developer operation against the saved source baseline.
public static class LiveClientBindings
{
    private static readonly Regex Names = new(@"[\u0370-\u1fff]+", RegexOptions.Compiled);
    public static LiveBindingContract Generate(string managed, IEnumerable<SyntaxTree> source, IEnumerable<string>? evidenceSpecs = null, string? sourcePrefix = null)
    {
        using var index = new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var trees = source.ToArray();
        var references = Directory.EnumerateFiles(managed,"*.dll").Select(p=>MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromImage(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")));
        var compilation = CSharpCompilation.Create("LiveContractBaseline", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        if(errors.Length>0)throw new InvalidDataException("Baseline must compile before capturing bindings: "+string.Join("\n",errors.Select(x=>x.ToString()).Take(20)));
        var members = new HashSet<IMemberDefinition>();
        var types = new HashSet<TypeDefinition>();
        var all = index.Types.ToDictionary(t=>t.FullName);
        void Add(ISymbol symbol)
        {
            symbol=symbol.OriginalDefinition;
            var owner = symbol is INamedTypeSymbol type ? type : symbol.ContainingType;
            if(owner==null || symbol.ContainingAssembly?.Name!="Assembly-CSharp")return;
            if(!all.TryGetValue(TypeName(owner.OriginalDefinition),out var actual))throw new InvalidDataException("Unknown baseline owner: "+owner);
            types.Add(actual);
            if(symbol is INamedTypeSymbol)return;
            var matches = MetadataIndex.Members(actual).Where(m=>m.Name==symbol.MetadataName && (m,symbol) switch {
                (FieldDefinition, IFieldSymbol)=>true,
                (PropertyDefinition p, IPropertySymbol s)=>p.Parameters.Select(x=>x.ParameterType.FullName).SequenceEqual(s.Parameters.Select(ParameterName)),
                (MethodDefinition method, IMethodSymbol s)=>method.GenericParameters.Count==s.TypeParameters.Length && method.Parameters.Select(x=>x.ParameterType.FullName).SequenceEqual(s.Parameters.Select(ParameterName)),
                _=>false
            }).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Ambiguous baseline member: "+symbol+" ("+matches.Length+")");
            members.Add(matches[0]);
        }
        foreach(var tree in trees.Where(t => sourcePrefix == null || t.FilePath.StartsWith(sourcePrefix, StringComparison.Ordinal)))
        {
            var model=compilation.GetSemanticModel(tree);
            foreach(var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>().Where(n=>MetadataIndex.Obfuscated(n.Identifier.ValueText)))
            {
                var symbol=model.GetSymbolInfo(name).Symbol;
                if(symbol==null)throw new InvalidDataException("Unresolved source identifier: "+name);
                Add(symbol);
            }
            // Reflection names cannot be inferred from a C# symbol; require a globally unique rewrite later.
            foreach(var literal in tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Where(n=>n.IsKind(SyntaxKind.StringLiteralExpression)&&Names.IsMatch(n.Token.ValueText)))
            {
                var invocation=literal.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                var owner=invocation?.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                var ownerType=owner==null?null:model.GetTypeInfo(owner).Type as INamedTypeSymbol;
                TypeDefinition? reflected=null;
                if(ownerType?.ContainingAssembly?.Name=="Assembly-CSharp")all.TryGetValue(TypeName(ownerType.OriginalDefinition),out reflected);
                foreach(var value in Names.Matches(literal.Token.ValueText).Select(m=>m.Value).Distinct())
                {
                    var matching=(reflected==null?index.Types.SelectMany(MetadataIndex.Members):MetadataIndex.Members(reflected)).Where(m=>m.Name==value).ToArray();
                    if(matching.Length==0)matching=index.Types.SelectMany(MetadataIndex.Members).Where(m=>m.Name==value).ToArray();
                    foreach(var member in matching){types.Add(member.DeclaringType);members.Add(member);}
                    if(matching.Length==0)foreach(var type in index.Types.Where(t=>t.Name==value))types.Add(type);
                }
            }
        }
        EvidenceBindings.Capture(index.Types,evidenceSpecs??[],t=>types.Add(t),m=>members.Add(m));
        // Include a thin forwarding overload so equal-shaped wrappers can be bound
        // by the exact implementation they call, not by parameter count alone.
        for(int pass=0;pass<3;pass++)
        foreach(var method in members.SelectMany(Methods).Where(m=>m.HasBody&&m.Body.Instructions.Count<=12).ToArray())
        {
            var forwarded=method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
            if(forwarded.Length!=1||!MetadataIndex.Obfuscated(forwarded[0].Name)||forwarded[0].DeclaringType.Scope.Name!=index.Module.Name)continue;
            var target=forwarded[0].Resolve();if(target!=null&&target.Module==index.Module){types.Add(target.DeclaringType);members.Add(target);}
        }
        var contractTypes=types.OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new TypeContract(t.FullName,index.Shape(t),
            t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),
            members.Where(m=>m.DeclaringType==t).OrderBy(m=>m.Name,StringComparer.Ordinal).ThenBy(MetadataIndex.Signature).Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray();
        var localUses=LocalUses(index);
        var links=new List<LiveTypeLink>();
        foreach(var member in members)
        {
            var slots=member switch { FieldDefinition f=>new[]{f.FieldType}, PropertyDefinition p=>new[]{p.PropertyType}, MethodDefinition m=>new[]{m.ReturnType}.Concat(m.Parameters.Select(p=>p.ParameterType)).ToArray(), _=>Array.Empty<TypeReference>() };
            for(int n=0;n<slots.Length;n++)if(types.Any(t=>t.FullName==slots[n].FullName))links.Add(new(slots[n].FullName,BindingResolver.Key(member.DeclaringType.FullName,member.Name,MetadataIndex.Signature(member)),n));
        }
        var methodKeys=new Dictionary<string,string>();
        foreach(var member in members)foreach(var method in Methods(member))methodKeys[method.FullName]=BindingResolver.Key(member.DeclaringType.FullName,member.Name,MetadataIndex.Signature(member));
        var calls=members.SelectMany(member=>Methods(member).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>()
            .Where(m=>methodKeys.ContainsKey(m.FullName)).Select(m=>new LiveCallLink(BindingResolver.Key(member.DeclaringType.FullName,member.Name,MetadataIndex.Signature(member)),methodKeys[m.FullName]))).Distinct().ToArray();
        return new(new BindingContract(1,contractTypes,[],new()),types.Where(t=>t.IsEnum).Select(t=>new LiveEnumContract(t.FullName,t.Fields.Where(f=>f.HasConstant).ToDictionary(f=>f.Name,f=>Convert.ToInt64(f.Constant)))).ToArray(),
            members.Select(m=>new LiveUsageContract(BindingResolver.Key(m.DeclaringType.FullName,m.Name,MetadataIndex.Signature(m)),Uses(m,localUses),m is PropertyDefinition property ? new[]{property.GetMethod?.Name,property.SetMethod?.Name}.FirstOrDefault(n=>n!=null&&!MetadataIndex.Obfuscated(n))??"" : "")).ToArray(),links.ToArray(),calls);
    }
    private static string ParameterName(IParameterSymbol p)=>TypeName(p.Type)+(p.RefKind==RefKind.None?"":"&");
    private static string TypeName(ITypeSymbol type)=>type switch {
        IArrayTypeSymbol a=>TypeName(a.ElementType)+"["+new string(',',a.Rank-1)+"]",
        IPointerTypeSymbol p=>TypeName(p.PointedAtType)+"*",
        ITypeParameterSymbol p=>p.Name,
        INamedTypeSymbol n=>(n.ContainingType!=null?TypeName(n.ContainingType.OriginalDefinition)+"/":n.ContainingNamespace.IsGlobalNamespace?"":n.ContainingNamespace.ToDisplayString()+".")+n.MetadataName+
            (!SymbolEqualityComparer.Default.Equals(n,n.OriginalDefinition)&&n.TypeArguments.Length>0?"<"+string.Join(",",n.TypeArguments.Select(TypeName))+">":""),
        _=>throw new InvalidDataException("Unsupported baseline type: "+type)
    };
    public static SyntaxTree[] Adapt(string managed,IEnumerable<SyntaxTree> source,LiveBindingContract contract,string reportPath)
    {
        var rewriter=new Rewriter(ResolveCached(managed,contract,reportPath));
        return source.Select(t=>CSharpSyntaxTree.Create((CSharpSyntaxNode)rewriter.Visit(t.GetRoot())!,path:t.FilePath)).ToArray();
    }
    internal static JsonNode AdaptSpec(string managed,JsonNode spec,string reportPath,out Dictionary<string,string> resolvedNames)
    {
        using var resource=typeof(LiveClientBindings).Assembly.GetManifestResourceStream("Live.BindingContract.json")!;
        var contract=JsonSerializer.Deserialize<LiveBindingContract>(resource)!;
        var names=ResolveCached(managed,contract,reportPath);
        resolvedNames=names;
        JsonNode Rewrite(JsonNode node)
        {
            if(node is JsonObject obj){var result=new JsonObject();foreach(var pair in obj)result[pair.Key]=pair.Value==null?null:Rewrite(pair.Value);return result;}
            if(node is JsonArray array)return new JsonArray(array.Select(x=>x==null?null:Rewrite(x)).ToArray());
            if(node is JsonValue value&&value.TryGetValue<string>(out var text))return JsonValue.Create(Names.Replace(text,m=>names.GetValueOrDefault(m.Value,m.Value)))!;
            return node.DeepClone();
        }
        return Rewrite(spec);
    }
    private sealed record NameCache(string Key,string Payload,string Sha256);
    private sealed record NamePayload(Dictionary<string,string> Names,BindingReport Report);
    internal static Dictionary<string,string> ResolveCached(string managed,LiveBindingContract contract,string reportPath)
    {
        string client;
        using(var input=File.OpenRead(Path.Combine(managed,"Assembly-CSharp.dll")))client=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
        string key=MetadataIndex.Hash("live-names-v1|"+typeof(LiveClientBindings).Module.ModuleVersionId+"|"+client+"|"+JsonSerializer.Serialize(contract));
        string directory=Path.Combine(BD2Daily.DailyIdentity.DataRoot,"live","binding-cache"),path=Path.Combine(directory,key+".json");
        try{
            if(File.Exists(path)){
                var cache=JsonSerializer.Deserialize<NameCache>(File.ReadAllText(path));
                if(cache?.Key==key&&MetadataIndex.Hash(cache.Payload)==cache.Sha256){
                    var saved=JsonSerializer.Deserialize<NamePayload>(cache.Payload)!;
                    if(saved.Report.Status=="compatible"){
                        _=new BD2Daily.Live.EvidenceAliases(saved.Names.Select(p=>new BD2Daily.Live.ClientAlias{Original=p.Key,Current=p.Value}));
                        File.WriteAllText(reportPath,JsonSerializer.Serialize(saved.Report,new JsonSerializerOptions{WriteIndented=true}));return saved.Names;
                    }
                }
            }
        }catch(Exception error) when(error is IOException or JsonException or InvalidOperationException){ /* Rebuild an incomplete cache. */ }
        var names=ResolveNames(managed,contract,reportPath);
        var report=JsonSerializer.Deserialize<BindingReport>(File.ReadAllText(reportPath))!;
        string payload=JsonSerializer.Serialize(new NamePayload(names,report));
        Directory.CreateDirectory(directory);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(new NameCache(key,payload,MetadataIndex.Hash(payload))));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        return names;
    }
    internal static Dictionary<string,string> ResolveNames(string managed,LiveBindingContract contract,string reportPath)
    {
        using var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
        var resolved=Resolve(index,contract);
        void Report(BindingReport report)=>File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        if(resolved.Report.Status!="compatible"){Report(resolved.Report);throw new InvalidDataException("Daily client binding failed: "+string.Join("; ",resolved.Report.Errors));}
        try{
        var map=new Dictionary<string,string>(StringComparer.Ordinal);
        void Map(string old,string current)
        {
            if(!MetadataIndex.Obfuscated(old))return;
            if(map.TryGetValue(old,out var known)&&known!=current)throw new InvalidDataException("Ambiguous daily source name: "+old);
            map[old]=current;
        }
        foreach(var pair in resolved.Types)Map(pair.Key.Split('/').Last().Split('.').Last().Split((char)96)[0],pair.Value.Name.Split((char)96)[0]);
        foreach(var expected in contract.Bindings.Types)
            foreach(var member in expected.Members)Map(member.Name,resolved.Members[BindingResolver.Key(expected.Name,member.Name,member.Signature)].Name);
        foreach(var enumeration in contract.Enums)
            foreach(var expected in enumeration.Values)
            {
                string name=map.GetValueOrDefault(expected.Key,expected.Key);
                var field=resolved.Types[enumeration.Type].Fields.SingleOrDefault(f=>f.Name==name&&f.HasConstant);
                if(field==null||Convert.ToInt64(field.Constant)!=expected.Value)throw new InvalidDataException("Daily enum changed: "+enumeration.Type+"."+expected.Key);
            }
        _=new BD2Daily.Live.EvidenceAliases(map.Select(p=>new BD2Daily.Live.ClientAlias{Original=p.Key,Current=p.Value}));
        Report(resolved.Report);return map;
        }catch(Exception error){Report(resolved.Report with {Status="unsupported",Errors=[error.Message]});throw;}
    }
    private static Dictionary<string,HashSet<string>> LocalUses(MetadataIndex index)
    {
        var result=new Dictionary<string,HashSet<string>>();
        foreach(var method in index.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody))
        {
            var instructions=method.Body.Instructions;
            string Operand(object? value)=>value switch {
                MethodReference m=>MetadataIndex.TypeShape(m.DeclaringType)+"::"+MetadataIndex.Name(m.Name)+"("+string.Join(",",m.Parameters.Select(p=>MetadataIndex.TypeShape(p.ParameterType)))+")",
                FieldReference f=>MetadataIndex.TypeShape(f.DeclaringType)+"::"+MetadataIndex.Name(f.Name)+":"+MetadataIndex.TypeShape(f.FieldType),
                TypeReference t=>MetadataIndex.TypeShape(t),
                Mono.Cecil.Cil.Instruction or Mono.Cecil.Cil.Instruction[]=>"branch",
                Mono.Cecil.Cil.VariableDefinition v=>MetadataIndex.TypeShape(v.VariableType),
                ParameterDefinition p=>"arg:"+p.Index,
                _=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??""
            };
            for(int n=0;n<instructions.Count;n++)
            {
                if(instructions[n].Operand is not MemberReference reference || reference is not (FieldReference or MethodReference))continue;
                string context=MetadataIndex.TypeShape(method.DeclaringType)+"::"+MetadataIndex.Name(method.Name)+"|"+MetadataIndex.Signature(method)+"|"+
                    string.Join(";",instructions.Skip(Math.Max(0,n-4)).Take(Math.Min(instructions.Count,n+5)-Math.Max(0,n-4)).Select((i,j)=>i.OpCode.Code+":"+Operand(i.Operand)));
                if(!result.TryGetValue(reference.FullName,out var anchors))result[reference.FullName]=anchors=new();
                anchors.Add(MetadataIndex.Hash(context));
            }
        }
        return result;
    }
    private static IEnumerable<MethodDefinition> Methods(IMemberDefinition m)=>m switch {MethodDefinition method=>new[]{method},PropertyDefinition p=>new[]{p.GetMethod,p.SetMethod}.Where(x=>x!=null)!,_=>Array.Empty<MethodDefinition>()};
    private static string[] Uses(IMemberDefinition member,Dictionary<string,HashSet<string>> uses)
        =>(member is PropertyDefinition p?new[]{p.GetMethod?.FullName,p.SetMethod?.FullName}:new[]{member.FullName})
        .Where(n=>n!=null).SelectMany(n=>uses.TryGetValue(n!,out var values)?values:Enumerable.Empty<string>()).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
    private static ResolvedBindings Resolve(MetadataIndex index,LiveBindingContract contract)
    {
        var initial=BindingResolver.Resolve(index,contract.Bindings);
        var types=initial.Types;var members=initial.Members;
        var localUses=LocalUses(index);var usage=contract.Uses.ToDictionary(x=>x.Key);
        // Type references from already resolved members preserve semantic relationships.
        for(int pass=0;pass<3;pass++)
        {
            foreach(var expected in contract.Bindings.Types.Where(t=>!types.ContainsKey(t.Name)))
            {
                var linked=new HashSet<string>();
                foreach(var link in contract.Links.Where(l=>l.Type==expected.Name))
                {
                    if(!members.TryGetValue(link.Member,out var member))continue;
                    var slots=member switch { FieldDefinition f=>new[]{f.FieldType}, PropertyDefinition p=>new[]{p.PropertyType}, MethodDefinition m=>new[]{m.ReturnType}.Concat(m.Parameters.Select(p=>p.ParameterType)).ToArray(), _=>Array.Empty<TypeReference>() };
                    if(link.Slot<slots.Length)linked.Add(slots[link.Slot].FullName);
                }
                if(linked.Count>1)throw new InvalidDataException("Conflicting daily type relationships: "+expected.Name);
                var enumeration=contract.Enums.SingleOrDefault(e=>e.Type==expected.Name);
                var candidates=index.Types.Where(t=>linked.Count==1?linked.Contains(t.FullName):
                    enumeration!=null?t.IsEnum&&enumeration.Values.All(e=>t.Fields.Any(f=>f.Name==e.Key&&f.HasConstant&&Convert.ToInt64(f.Constant)==e.Value)):
                    index.Shape(t)==expected.Shape).ToArray();
                string? parent=expected.Name.Contains('/')?expected.Name[..expected.Name.LastIndexOf('/')]:null;
                if(parent!=null&&(types.TryGetValue(parent,out var actualParent)||!MetadataIndex.Obfuscated(parent)))
                    candidates=candidates.Where(t=>t.DeclaringType?.FullName==(actualParent?.FullName??parent)).ToArray();
                if(candidates.Length>1)
                {
                    var ranked=candidates.Select(t=>new{Type=t,Score=t.Methods.Select(index.Body).Where(b=>b.Length>0).Distinct().Intersect(expected.Anchors).Count()}).OrderByDescending(x=>x.Score).ToArray();
                    if(ranked[0].Score>0)candidates=ranked.Where(x=>x.Score==ranked[0].Score).Select(x=>x.Type).ToArray();
                }
                if(candidates.Length==1)types[expected.Name]=candidates[0];
            }
            foreach(var expectedType in contract.Bindings.Types.Where(t=>types.ContainsKey(t.Name)))
            foreach(var expected in expectedType.Members)
            {
                string key=BindingResolver.Key(expectedType.Name,expected.Name,expected.Signature);
                if(members.ContainsKey(key))continue;
                var candidates=MetadataIndex.Members(types[expectedType.Name]).Where(m=>MetadataIndex.Signature(m)==expected.Signature&&
                    (MetadataIndex.Obfuscated(expected.Name)||m.Name==expected.Name)).ToArray();
                if(usage.TryGetValue(key,out var hint)&&hint.Accessor.Length>0)
                    candidates=candidates.Where(m=>m is PropertyDefinition p&&(p.GetMethod?.Name==hint.Accessor||p.SetMethod?.Name==hint.Accessor)).ToArray();
                var targets=contract.Calls.Where(c=>c.Member==key&&c.Target!=key&&members.ContainsKey(c.Target)).SelectMany(c=>Methods(members[c.Target])).Select(m=>m.FullName).ToHashSet();
                if(targets.Count>0)candidates=candidates.Where(m=>{var invoked=Methods(m).Where(x=>x.HasBody).SelectMany(x=>x.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().Select(r=>r.FullName).ToHashSet();return targets.All(invoked.Contains);}).ToArray();
                if(candidates.Length>1){var scored=candidates.Select(m=>new{Member=m,Score=Uses(m,localUses).Intersect(usage.TryGetValue(key,out var anchors)?anchors.Anchors:[]).Count()}).OrderByDescending(x=>x.Score).ToArray();
                    candidates=scored.Length>0&&scored[0].Score>0?scored.Where(x=>x.Score==scored[0].Score).Select(x=>x.Member).ToArray():[];}
                if(candidates.Length==1)members[key]=candidates[0];
            }
        }
        var errors=contract.Bindings.Types.Where(t=>!types.ContainsKey(t.Name)).Select(t=>"Unresolved daily type: "+t.Name)
            .Concat(contract.Bindings.Types.SelectMany(t=>t.Members.Select(m=>BindingResolver.Key(t.Name,m.Name,m.Signature))).Where(k=>!members.ContainsKey(k)).Select(k=>"Unresolved daily member: "+k)).ToArray();
        return new(contract.Bindings,types,members,new BindingReport(errors.Length==0?"compatible":"unsupported",index.Module.Mvid.ToString(),types.Count,members.Count,types.Count(x=>x.Key!=x.Value.FullName),members.Count(x=>x.Key.Split('|')[1]!=x.Value.Name),errors));
    }
    private sealed class Rewriter(Dictionary<string,string> map):CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if(token.IsKind(SyntaxKind.IdentifierToken)&&map.TryGetValue(token.ValueText,out var name))return SyntaxFactory.Identifier(token.LeadingTrivia,name,token.TrailingTrivia);
            if(token.IsKind(SyntaxKind.StringLiteralToken)&&Names.IsMatch(token.ValueText))
            {
                string text=Names.Replace(token.ValueText,m=>map.GetValueOrDefault(m.Value,m.Value));
                return SyntaxFactory.Literal(token.LeadingTrivia,JsonSerializer.Serialize(text),text,token.TrailingTrivia);
            }
            return base.VisitToken(token);
        }
    }

}








using Dustweave.Compatibility;
using Dustweave.Connection;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BD2Daily.Live;

static class LiveBindingCases
{
    public static void Run(string root,List<string> cases)
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add(name);}
        void Reject(Action action,string name){try{action();}catch(InvalidDataException){cases.Add(name);return;}throw new Exception(name);}
        string directory=Path.Combine(root,"live-bindings");Directory.CreateDirectory(directory);
        string Fixture(string name,bool renamed,bool altered=false)
        {
            string dir=Path.Combine(directory,name);Directory.CreateDirectory(dir);
            using var module=ModuleDefinition.CreateModule("Assembly-CSharp.dll",ModuleKind.Dll);
            var store=new TypeDefinition("","Store",TypeAttributes.Public,module.TypeSystem.Object);module.Types.Add(store);
            foreach(var pair in new[]{(renamed?"δ":"α","ReadA",7),(renamed?"ε":"β","ReadB",19)})
            {
                var field=new FieldDefinition(pair.Item1,FieldAttributes.Public|FieldAttributes.Static,module.TypeSystem.Int32);store.Fields.Add(field);
                var method=new MethodDefinition(pair.Item2,MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Int32);store.Methods.Add(method);
                var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldsfld,field);il.Emit(OpCodes.Ldc_I4,pair.Item3);il.Emit(OpCodes.Add);il.Emit(OpCodes.Ret);
            }
            foreach(var pair in new[]{(renamed?"λ":"κ","get_Level"),(renamed?"ν":"μ","get_Other")})
            {
                var getter=new MethodDefinition(pair.Item2,MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.SpecialName,module.TypeSystem.Int32);store.Methods.Add(getter);
                getter.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_1);getter.Body.GetILProcessor().Emit(OpCodes.Ret);
                store.Properties.Add(new PropertyDefinition(pair.Item1,PropertyAttributes.None,module.TypeSystem.Int32){GetMethod=getter});
            }
            var enumeration=new TypeDefinition("",renamed?"ι":"θ",TypeAttributes.Public|TypeAttributes.Sealed,module.ImportReference(typeof(Enum)));module.Types.Add(enumeration);
            enumeration.Fields.Add(new FieldDefinition("value__",FieldAttributes.Public|FieldAttributes.SpecialName|FieldAttributes.RTSpecialName,module.TypeSystem.Int32));
            enumeration.Fields.Add(new FieldDefinition("First",FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.Literal,enumeration){Constant=altered?9:1});
            if(renamed)enumeration.Fields.Add(new FieldDefinition("Added",FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.Literal,enumeration){Constant=2});
            module.Write(Path.Combine(dir,"Assembly-CSharp.dll"));return dir;
        }
        string old=Fixture("old",false),current=Fixture("new",true),changed=Fixture("changed",true,true);
        using var baseline=new MetadataIndex(Path.Combine(old,"Assembly-CSharp.dll"));
        TypeContract Capture(TypeDefinition type,IEnumerable<IMemberDefinition> members)=>new(type.FullName,baseline.Shape(type),[],members.Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),baseline.MemberBody(m),baseline.Uses(m))).ToArray());
        var store=baseline.Find("Store");var enumeration=baseline.Find("θ");var property=store.Properties.Single(p=>p.Name=="κ");
        var contract=new LiveBindingContract(new(1,[Capture(store,store.Fields.Cast<IMemberDefinition>().Append(property)),Capture(enumeration,[])],[],new()),
            [new("θ",new(){{"First",1}})],[new(BindingResolver.Key("Store","κ",MetadataIndex.Signature(property)),[],"get_Level")],[],[]);
        var map=LiveClientBindings.ResolveNames(current,contract,Path.Combine(directory,"names.json"));
        Check(map["α"]=="δ"&&map["β"]=="ε","client field rename follows use sites rather than declaration order");
        Check(map["κ"]=="λ","same-shaped getters use stable accessor identity");
        Check(map["θ"]=="ι","added enum values preserve known action values");
        var cached=LiveClientBindings.ResolveCached(current,contract,Path.Combine(directory,"cached.json"));
        Check(cached["α"]=="δ","verified name cache preserves resolved member identity");
        string cacheDirectory=Path.Combine(BD2Daily.DailyIdentity.DataRoot,"live","binding-cache");
        foreach(string file in Directory.EnumerateFiles(cacheDirectory,"*.json"))File.WriteAllText(file,"{broken cache");
        Check(LiveClientBindings.ResolveCached(current,contract,Path.Combine(directory,"cache-rebuilt.json"))["κ"]=="λ","damaged name cache is rebuilt rather than blocking connection");
        Reject(()=>LiveClientBindings.ResolveCached(changed,contract,Path.Combine(directory,"cache-client-change.json")),"new client content cannot reuse old successful name cache");
        var tree=CSharpSyntaxTree.ParseText("class Consumer { int Read()=>Store.α; string Path=>\"α.child.κ\"; }");
        string rewritten=LiveClientBindings.Adapt(current,[tree],contract,Path.Combine(directory,"source.json"))[0].ToString();
        Check(rewritten.Contains("Store.δ")&&rewritten.Contains("\\u03B4.child.\\u03BB"),"source and reflected string paths adapt together");
        Reject(()=>LiveClientBindings.ResolveNames(changed,contract,Path.Combine(directory,"changed.json")),"changed known enum value refuses action binding");
        Check(System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"changed.json"))).RootElement.GetProperty("Status").GetString()=="unsupported","enum drift diagnostics cannot claim compatibility");
        var ambiguous=new LiveBindingContract(new(1,[Capture(store,[property]) with {Members=[new("κ",MetadataIndex.Signature(property),"",[])]}],[],new()),[],[],[],[]);
        Reject(()=>LiveClientBindings.ResolveNames(current,ambiguous,Path.Combine(directory,"ambiguous.json")),"equally plausible members refuse to guess");
        var aliases=new EvidenceAliases([new(){Original="α",Current="δ"},new(){Original="κ",Current="λ"}]);
        Check(aliases.Current("α.child.κ")=="δ.child.λ"&&aliases.Original("δ.child.λ")=="α.child.κ","read path and UI target aliases round trip without changing workflow keys");
        Check(aliases.Current("中文.Empty.$self")=="中文.Empty.$self","stable read keys and localized text remain unchanged");
        string json=ObservationProjection.Capture(new[]{1,2},"$self",["α"],10,(obj,key)=>aliases.Current(key)=="δ"?(object)((int)obj*2):throw new Exception("Unresolved path"),obj=>System.Text.Json.JsonSerializer.Serialize(obj));
        Check(json.Contains("\\u03B1")&&!json.Contains("\\u03B4"),"projected collection columns retain baseline keys while reading new members");
        var captured=new List<IMemberDefinition>();
        EvidenceBindings.Capture(baseline.Types,["{\"Taps\":[],\"Reads\":[{\"Id\":\"test\",\"Type\":\"Store\",\"Paths\":[\"α\"]}]}"],_=>{},captured.Add);
        Check(captured.Count==1&&captured[0].Name=="α","evidence contract includes only the declared member path");
        using var schemaModule = ModuleDefinition.CreateModule("SchemaFixture", ModuleKind.Dll);
        var unstable = new TypeDefinition("", "ClientRow", TypeAttributes.Public, schemaModule.TypeSystem.Object);
        schemaModule.Types.Add(unstable);
        unstable.Fields.Add(new FieldDefinition("α", FieldAttributes.Public, schemaModule.TypeSystem.Int32));
        Reject(() => ObservationSchema.RequireStable(new ArrayType(unstable), "list._items"), "raw obfuscated collection output rejected during update preparation");
        var nestedList = new GenericInstanceType(schemaModule.ImportReference(typeof(List<>)));
        nestedList.GenericArguments.Add(unstable);
        var dictionary = new GenericInstanceType(schemaModule.ImportReference(typeof(Dictionary<,>)));
        dictionary.GenericArguments.Add(schemaModule.TypeSystem.Int32); dictionary.GenericArguments.Add(nestedList);
        Reject(() => ObservationSchema.RequireStable(dictionary, "map.Values"), "nested obfuscated collections cannot bypass output schema validation");
        var wrapper = new TypeDefinition("", "Wrapper", TypeAttributes.Public, schemaModule.TypeSystem.Object);
        schemaModule.Types.Add(wrapper);wrapper.Fields.Add(new FieldDefinition("Rows", FieldAttributes.Public, new ArrayType(unstable)));
        Reject(() => ObservationSchema.RequireStable(wrapper, "wrapper"), "stable outer DTO cannot hide unstable client row names");
        ObservationSchema.RequireStable(new ArrayType(schemaModule.TypeSystem.Int32), "ids");
        cases.Add("scalar collection output remains compatible");
        ObservationSchema.RequireStable(new TypeReference("Proto.Net", "MissionInfo", schemaModule, schemaModule), "protocol");
        cases.Add("protobuf JSON keeps its own stable schema");
        var nativeRows = new[]{new Dictionary<string,object>{{"δ",501L}}, new Dictionary<string,object>{{"δ",502L}}};
        string projected = ObservationProjection.Capture(nativeRows, "$self", ["α"], 10,
            (obj,key) => ((Dictionary<string,object>)obj)[aliases.Current(key)], obj => System.Text.Json.JsonSerializer.Serialize(obj));
        var projectedRows = System.Text.Json.Nodes.JsonNode.Parse(projected)!.AsArray();
        Check(projectedRows.Select(row=>row!["α"]!.GetValue<long>()).SequenceEqual(new long[]{501,502}), "renamed game rows cross the real projection with stable distinct identities");
        var state=System.Text.Json.Nodes.JsonNode.Parse("{\"highest_level\":28,\"selected_level\":1,\"daily_damage\":0,\"level_record\":100,\"practice\":false,\"quick_available\":false,\"quick_enabled\":false}")!.AsObject();
        var decision=Dustweave.DailyMonsterHunt.Plan(state);
        Check(decision["reason"]!.GetValue<string>()=="native_quick_entry_removed","removed quick-battle entry stops before changing levels or starting a battle");
        state["daily_damage"]=100;Check(Dustweave.DailyMonsterHunt.Plan(state)["state"]!.GetValue<string>()=="skipped","already credited boss rewards remain complete after quick-entry removal");
    }
}

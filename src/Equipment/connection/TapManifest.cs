using Mono.Cecil;
using System.Text.Json.Nodes;
using System.Text.Json;
using BD2Equipment.Live;
static class TapManifest {
 static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> roots){foreach(var t in roots){yield return t;foreach(var child in All(t.NestedTypes))yield return child;}}
 static JsonNode LoadSpec(string path,HashSet<string>? seen=null){
  path=Path.GetFullPath(path);seen??=new(StringComparer.OrdinalIgnoreCase);if(!seen.Add(path))throw new Exception("Evidence spec inheritance cycle");
  var spec=JsonNode.Parse(File.ReadAllText(path))!;
  if(spec["Extends"] is {} parent){
   string name=parent.GetValue<string>();if(Path.GetFileName(name)!=name||!name.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new Exception("Evidence parent must be a sibling JSON file");
   var inherited=LoadSpec(Path.Combine(Path.GetDirectoryName(path)!,name),seen);
   foreach(string field in new[]{"Taps","Reads"}){var merged=new JsonArray();foreach(var item in inherited[field]!.AsArray().Concat(spec[field]!.AsArray()))merged.Add(item!.DeepClone());spec[field]=merged;}
  }
  return spec;
 }
 internal static void Generate(string managed,string specPath,string output){
  using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(managed);
  using var module=ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"),new ReaderParameters{AssemblyResolver=resolver});
  var types=All(module.Types).ToArray();var methods=types.Where(t=>!t.FullName.StartsWith("Proto.")).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
  using var adapter=new BD2Equipment.Compatibility.ClientAdapter(managed);
  var spec=LoadSpec(specPath);var taps=new JsonArray();
  foreach(var rule in spec["Taps"]!.AsArray()){
   string dto=rule!["ResponseType"]!.GetValue<string>(),request=dto.Replace("Response","Request");
   bool Refers(MethodDefinition m,string type)=>m.Body.Instructions.Any(ins=>ins.Operand is MethodReference mr&&mr.DeclaringType.FullName==type);
   var req=methods.Where(m=>m.Name!="MoveNext"&&m.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is MethodReference mr&&mr.DeclaringType.FullName==request)).ToArray();
   // A recursive batch driver also enters its zero-count terminal branch.
   // Observe actual request allocation for these explicitly selected DTOs.
   if(rule["RequestSite"]?.GetValue<string>()=="constructor")req=types.Single(t=>t.FullName==request).Methods.Where(m=>m.IsConstructor&&!m.IsStatic&&m.Parameters.Count==0).ToArray();
   var resp=methods.Where(m=>m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Byte[]","System.Int32","System.Int32"})&&Refers(m,dto)).ToArray();
   if(rule["ResponseViaParserHelper"]?.GetValue<bool>()==true){
    var helpers=methods.Where(m=>Refers(m,dto)).Select(m=>m.FullName).ToHashSet();
    resp=resp.Concat(methods.Where(m=>m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Byte[]","System.Int32","System.Int32"})&&m.Body.Instructions.Any(i=>i.Operand is MethodReference mr&&helpers.Contains(mr.FullName)))).Distinct().ToArray();
   }
   if(rule["ResponseOwner"] is {} owner)resp=resp.Where(m=>m.DeclaringType.FullName==adapter.Name(owner.GetValue<string>()) || (rule["ResponseOwnerIncludesNested"]?.GetValue<bool>()==true && m.DeclaringType.FullName.StartsWith(adapter.Name(owner.GetValue<string>())+"/",StringComparison.Ordinal))).ToArray();
   if(rule["RequestMethod"] is {} requestMethod)req=req.Where(m=>m.Name==requestMethod.GetValue<string>()).ToArray();
   if(rule["ResponseMethod"] is {} responseMethod)resp=resp.Where(m=>m.Name==responseMethod.GetValue<string>()).ToArray();
   var responseNames=rule["ResponseMethods"]?.AsArray().Select(x=>x!.GetValue<string>()).ToArray();
   if(responseNames!=null){resp=resp.Where(m=>responseNames.Contains(m.Name)).ToArray();if(resp.Length!=responseNames.Length)throw new Exception("Missing declared response variant: "+dto);}
   if(req.Length!=1||resp.Length!=(responseNames?.Length??1))throw new Exception($"Ambiguous {dto}: requests={string.Join(",",req.Select(m=>m.FullName))}, responses={string.Join(",",resp.Select(m=>m.FullName))}");
   var record=rule.DeepClone()!.AsObject();record["RequestToken"]=req[0].MetadataToken.ToInt32();record["ResponseToken"]=resp[0].MetadataToken.ToInt32();record["ResponseTokens"]=new JsonArray(resp.Select(m=>(JsonNode?)JsonValue.Create(m.MetadataToken.ToInt32())).ToArray());taps.Add(record);
   Console.WriteLine($"{record["Role"]}: {req[0].MetadataToken} -> {resp[0].MetadataToken}, fields: {string.Join(",",types.Single(t=>t.FullName==dto).Properties.Where(p=>p.GetMethod?.IsStatic==false&&p.GetMethod.IsPublic).Select(p=>p.Name))}");
  }
  TypeReference Bind(TypeReference value,TypeReference owner){
   if(value is GenericParameter gp && owner is GenericInstanceType gi && gp.Type==GenericParameterType.Type)return gi.GenericArguments[gp.Position];
   if(value is GenericInstanceType g){var n=new GenericInstanceType(g.ElementType);foreach(var x in g.GenericArguments)n.GenericArguments.Add(Bind(x,owner));return n;}
   if(value is ArrayType arr)return new ArrayType(Bind(arr.ElementType,owner));return value;
  }
  TypeReference Member(TypeReference source,string name){
   name=adapter.Name(name);
   for(TypeReference? cur=source;cur!=null;){
    var type=cur.Resolve();TypeReference? found=null;
    if(name.EndsWith("()",StringComparison.Ordinal)){var method=type.Methods.SingleOrDefault(m=>m.Name==name[..^2]&&m.Parameters.Count==0);found=method?.ReturnType;}
    else {found=type.Fields.SingleOrDefault(f=>f.Name==name)?.FieldType??type.Properties.SingleOrDefault(p=>p.Name==name&&p.GetMethod!=null)?.PropertyType;}
    if(found!=null)return Bind(found,cur);cur=type.BaseType==null?null:Bind(type.BaseType,cur);
   }
   throw new Exception("Missing read member: "+source.FullName+"."+name);
  }
  TypeReference PathType(TypeReference source,string path){if(path=="$self")return source;foreach(var part in path.Split('.'))source=Member(source,part);return source;}
  TypeReference? ItemType(TypeReference source){
   if(source is ArrayType ar)return ar.ElementType;
   if(source is GenericInstanceType gi&&gi.ElementType.FullName=="System.Collections.Generic.IEnumerable`1")return gi.GenericArguments[0];
   var type=source.Resolve();foreach(var face in type.Interfaces){var result=ItemType(Bind(face.InterfaceType,source));if(result!=null)return result;}
   return type.BaseType==null?null:ItemType(Bind(type.BaseType,source));
  }
  var readErrors=new List<string>();int readCount=0;
  foreach(var rule in spec["Reads"]!.AsArray()){
   try{
    TypeReference start=types.Single(t=>t.FullName==adapter.Name(rule["Type"]!.GetValue<string>()));
    if(rule["StaticMember"] is {} member)start=Member(start,member.GetValue<string>());
    foreach(var path in rule["Paths"]!.AsArray())PathType(start,path!.GetValue<string>());
    if(rule["CollectionPath"] is {} collection){var item=ItemType(PathType(start,collection.GetValue<string>()))??throw new Exception("Collection is not enumerable");foreach(var path in rule["ItemPaths"]!.AsArray())PathType(item,path!.GetValue<string>());}
    readCount++;
   }catch(Exception e){readErrors.Add(rule["Id"]+": "+e.Message);}
  }
  if(readErrors.Count>0)throw new Exception(string.Join("\n",readErrors));
  Console.WriteLine($"Validated all {readCount} native read paths and collection item paths");
  var result=new JsonObject{["Mvid"]=module.Mvid.ToString(),["Taps"]=taps,["Reads"]=spec["Reads"]?.DeepClone()??new JsonArray()};
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,result.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
 }
}

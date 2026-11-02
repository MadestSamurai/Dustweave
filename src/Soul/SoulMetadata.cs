using Mono.Cecil;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Dustweave.Soul;

internal static class SoulMetadata
{
    internal static string Client => SoulAssets.ClientPath;
    internal static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> roots) => roots.SelectMany(t=>new[]{t}.Concat(All(t.NestedTypes)));
    internal static bool Uses(MethodDefinition m,string type,string name) => m.HasBody && m.Body.Instructions.Any(i=>i.Operand is MethodReference r && r.DeclaringType.FullName==type && r.Name==name);
    internal static JsonArray Taps()
    {
        using var module=ModuleDefinition.ReadModule(Client);
        var types=All(module.Types).ToArray();
        var methods=types.SelectMany(t=>t.Methods).ToArray();
        var taps=new JsonArray();
        foreach(var requestType in types.Where(t=>t.FullName.StartsWith("Proto.Net.Ib")&&t.Name.EndsWith("Request")))
        {
            var responseType=types.SingleOrDefault(t=>t.FullName==requestType.FullName.Replace("Request","Response"));
            if(responseType is null)continue;
            var request=methods.Where(m=>m.IsStatic&&!m.DeclaringType.FullName.StartsWith("Proto.")&&Uses(m,requestType.FullName,".ctor")).ToArray();
            var response=methods.Where(m=>m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Count==3&&m.Parameters[0].ParameterType.FullName=="System.Byte[]"&&Uses(m,responseType.FullName,"get_Parser")).ToArray();
            // The audited give-up callback has an empty response and does not
            // parse a protobuf. Resolve its directly referenced native callback
            // and require its explicit PlayInfo.Clear side effect instead.
            if(requestType.Name=="IbDungeonGiveUpRequest"&&request.Length==1&&response.Length==0){
                response=request[0].Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldftn&&i.Operand is MethodReference)
                    .Select(i=>((MethodReference)i.Operand).Resolve()).Where(m=>m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Count==3&&m.Parameters[0].ParameterType.FullName=="System.Byte[]"&&Uses(m,"Proto.Net.IbPlayDBInfo","Clear")).Distinct().ToArray();
            }
            if(request.Length!=1||response.Length==0){
                if(requestType.Name is "IbStageStartRequest" or "IbStageEndRequest")throw new InvalidOperationException("Nonunique battle request/response: "+requestType.Name);
                Console.WriteLine("Unresolved optional observer: "+requestType.Name+" request="+request.Length+" response="+response.Length);continue;
            }
            taps.Add(new JsonObject{["Role"]="soul."+requestType.Name.Replace("Request",""),["RequestToken"]=request[0].MetadataToken.ToInt32(),["ResponseToken"]=response[0].MetadataToken.ToInt32(),["ResponseTokens"]=new JsonArray(response.Select(m=>(JsonNode)JsonValue.Create(m.MetadataToken.ToInt32())!).ToArray()),["ResponseType"]=responseType.FullName,["Paths"]=new JsonArray("$self")});
        }
        return taps;
    }
    internal static void Run()
    {
        using var module=ModuleDefinition.ReadModule(Client);
        var types=All(module.Types).ToArray();
        var requests=types.SelectMany(t=>t.Methods).Where(m=>Uses(m,"Proto.Net.IbMainInfoRequest",".ctor")).ToArray();
        var owners=requests.Select(m=>m.DeclaringType).Distinct().ToArray();
        var dto=types.Where(t=>t.FullName.StartsWith("Proto.Net.Ib")&&(t.Name.EndsWith("Response")||t.Name.EndsWith("DBInfo")));
        var report=new {
            mvid=module.Mvid,
            owners=owners.Select(t=>new {type=t.FullName,
                fields=t.Fields.Where(f=>f.IsStatic).Select(f=>new{name=f.Name,type=f.FieldType.FullName}),
                properties=t.Properties.Where(p=>p.GetMethod?.IsStatic==true).Select(p=>new{name=p.Name,type=p.PropertyType.FullName}),
                requests=t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName.StartsWith("Proto.Net.Ib")&&r.Name==".ctor")).Select(m=>new{m.Name,token=m.MetadataToken.ToInt32(),parameters=m.Parameters.Select(p=>p.ParameterType.FullName),request=m.Body.Instructions.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName.StartsWith("Proto.Net.Ib")&&r.Name==".ctor").Select(i=>((MethodReference)i.Operand).DeclaringType.FullName)})}),
            dto=dto.Select(t=>new {type=t.FullName,properties=t.Properties.Where(p=>p.GetMethod?.IsStatic==false).Select(p=>new{p.Name,type=p.PropertyType.FullName})})
        };
        File.WriteAllText(SoulAssets.MetadataPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var t in owners){Console.WriteLine(t.FullName);foreach(var p in t.Properties.Where(p=>p.GetMethod?.IsStatic==true))Console.WriteLine(p.Name+" "+p.PropertyType.FullName);}
        foreach(var t in types.Where(t=>t.Methods.Any(m=>Uses(m,"Proto.Design.common.IBTriggerTable","get_TriggerType"))))Console.WriteLine("Trigger consumer: "+t.FullName);
    }
}

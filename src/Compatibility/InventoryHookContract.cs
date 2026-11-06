using System.Text.Json;
using Mono.Cecil;
namespace Dustweave.Compatibility;

/// <summary>Read-only caches are identified by current protocol writers, not names.</summary>
public static class InventoryHookContract
{
    public static string Source(MetadataIndex index)
    {
        var fields = new List<FieldDefinition>();
        string error = "";
        foreach (var (row, response, property) in new[] {
            ("CharDBInfo", "CharInfoResponse", "CharInfo"),
            ("CharAwakeDBInfo", "CharAwakeInfoResponse", "CharAwakeInfo"),
            ("CostumeDBInfo", "CostumeInfoResponse", "CostumeInfo"),
            ("EquipDBInfo", "EquipInfoResponse", "EquipInfo") })
        {
            var candidates = index.Types.SelectMany(t => t.Fields).Where(f => f.IsStatic &&
                f.FieldType is GenericInstanceType g && g.ElementType.FullName == "System.Collections.Generic.Dictionary`2" &&
                g.GenericArguments.Count == 2 && g.GenericArguments[1].FullName == "Proto.Net." + row).ToArray();
            var readers = index.Types.SelectMany(t => t.Methods).Where(m => m.IsStatic && m.HasBody && m.Body.Instructions.Any(i =>
                i.Operand is MethodReference call && call.DeclaringType.FullName == "Proto.Net." + response && call.Name == "get_" + property)).ToArray();
            var writers=readers.Where(m=>m.ReturnType.FullName=="System.Boolean" && m.Parameters.Count==3 && m.Parameters[0].ParameterType.FullName=="Google.Protobuf.IMessage" && m.Parameters[1].ParameterType.FullName=="System.Int32" && m.Parameters[2].ParameterType.FullName=="System.Int32").ToArray();
            var reached=new HashSet<string>(StringComparer.Ordinal);
            var visited=new HashSet<string>(StringComparer.Ordinal);
            var queue=new Queue<(MethodDefinition Method,int Depth)>();foreach(var writer in writers)queue.Enqueue((writer,0));
            var methods=index.Types.SelectMany(t=>t.Methods).ToLookup(m=>m.FullName,StringComparer.Ordinal);
            while(queue.Count>0)
            {
                var (method,depth)=queue.Dequeue();if(!visited.Add(method.FullName)||!method.HasBody)continue;
                foreach(var instruction in method.Body.Instructions)
                {
                    if(instruction.Operand is FieldReference field)reached.Add(field.FullName);
                    if(depth<4&&instruction.Operand is MethodReference call)foreach(var next in methods[call.FullName])queue.Enqueue((next,depth+1));
                }
            }
            var direct = candidates.Where(f=>readers.Any(m=>m.Body.Instructions.Any(i=>i.Operand is FieldReference field&&field.FullName==f.FullName))).ToArray();
            var matches = direct.Length==1 ? direct : candidates.Where(f => reached.Contains(f.FullName)).ToArray();
            if (matches.Length != 1) { error = "Inventory cache unresolved: " + row; break; }
            fields.Add(matches[0]);
        }
        // An optional inventory feature must never block normal game connection.
        string Json(string value) => JsonSerializer.Serialize(value);
        return "namespace BD2Daily.Runtime { internal static class InventoryBindings { internal const string Error=" + Json(error) +
            "; internal static readonly string[] Types=new string[]{" + string.Join(",", fields.Select(f => Json(f.DeclaringType.FullName))) +
            "}; internal static readonly string[] Fields=new string[]{" + string.Join(",", fields.Select(f => Json(f.Name))) + "}; } }";
    }
}

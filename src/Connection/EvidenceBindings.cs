using Mono.Cecil;
using System.Text.Json.Nodes;

namespace Dustweave.Connection;

// Follow the declared read path, including generic collections. A short obfuscated
// name can be reused by unrelated client classes, so global name lookup is unsafe.
internal static class EvidenceBindings
{
    internal static void Capture(IEnumerable<TypeDefinition> source, IEnumerable<string> specs,
        Action<TypeDefinition> addType, Action<IMemberDefinition> addMember)
    {
        var types=source.ToArray();
        void CaptureType(TypeDefinition type)
        {
            addType(type);
            if(type.DeclaringType!=null)CaptureType(type.DeclaringType);
        }
        TypeReference Bind(TypeReference value,TypeReference owner)
        {
            if(value is GenericParameter p&&owner is GenericInstanceType instance&&p.Type==GenericParameterType.Type)return instance.GenericArguments[p.Position];
            if(value is GenericInstanceType g){var bound=new GenericInstanceType(g.ElementType);foreach(var item in g.GenericArguments)bound.GenericArguments.Add(Bind(item,owner));return bound;}
            return value is ArrayType array?new ArrayType(Bind(array.ElementType,owner)):value;
        }
        TypeReference Member(TypeReference source,string name)
        {
            for(TypeReference? current=source;current!=null;)
            {
                var type=current.Resolve();
                IMemberDefinition? member=name.EndsWith("()",StringComparison.Ordinal)
                    ?type.Methods.SingleOrDefault(m=>m.Name==name[..^2]&&m.Parameters.Count==0)
                    :(IMemberDefinition?)type.Fields.SingleOrDefault(f=>f.Name==name)??type.Properties.SingleOrDefault(p=>p.Name==name&&p.GetMethod!=null);
                if(member!=null)
                {
                    if(type.Module==types[0].Module){CaptureType(type);addMember(member);}
                    var result=member switch {FieldDefinition f=>f.FieldType,PropertyDefinition p=>p.PropertyType,MethodDefinition m=>m.ReturnType,_=>throw new InvalidDataException(name)};
                    return Bind(result,current);
                }
                current=type.BaseType==null?null:Bind(type.BaseType,current);
            }
            throw new InvalidDataException("Missing baseline evidence member: "+source.FullName+"."+name);
        }
        TypeReference Path(TypeReference source,string path)
        { if(path!="$self")foreach(var part in path.Split('.'))source=Member(source,part);return source; }
        TypeReference? Item(TypeReference source)
        {
            if(source is ArrayType a)return a.ElementType;
            if(source is GenericInstanceType g&&g.ElementType.FullName=="System.Collections.Generic.IEnumerable`1")return g.GenericArguments[0];
            var type=source.Resolve();foreach(var face in type.Interfaces){var value=Item(Bind(face.InterfaceType,source));if(value!=null)return value;}
            return type.BaseType==null?null:Item(Bind(type.BaseType,source));
        }
        foreach(string json in specs)
        {
            var spec=JsonNode.Parse(json)!;
            foreach(var rule in spec["Reads"]!.AsArray())
            {
                if(rule!["Id"]!.GetValue<string>() is "friendship.native" or "monster.schedules" or "mainline.travel" or "mainline.network" or "trade.native" or "rewards.native" or "minigames.quiz")continue;
                var type=types.Single(t=>t.FullName==rule["Type"]!.GetValue<string>());CaptureType(type);
                TypeReference start=type;
                if(rule["StaticMember"] is {} member)start=Member(start,member.GetValue<string>());
                foreach(var path in rule["Paths"]!.AsArray())Path(start,path!.GetValue<string>());
                if(rule["CollectionPath"] is {} collection)
                { var item=Item(Path(start,collection.GetValue<string>()))??throw new InvalidDataException("Baseline collection is not enumerable");foreach(var path in rule["ItemPaths"]!.AsArray())Path(item,path!.GetValue<string>()); }
            }
            foreach(var tap in spec["Taps"]!.AsArray())
            {
                string? owner=tap!["ResponseOwner"]?.GetValue<string>();
                if(owner!=null)CaptureType(types.Single(t=>t.FullName==owner));
                foreach(string field in new[]{"RequestMethod","ResponseMethod","ResponseMethods"})
                {
                    var names=tap[field] is JsonArray a?a.Select(x=>x!.GetValue<string>()).ToArray():tap[field]==null?[]:new[]{tap[field]!.GetValue<string>()};
                    foreach(string name in names)
                    {
                        var candidates=types.Where(t=>field=="RequestMethod"||owner==null||t.FullName==owner||tap["ResponseOwnerIncludesNested"]?.GetValue<bool>()==true&&t.FullName.StartsWith(owner+"/",StringComparison.Ordinal))
                            .SelectMany(t=>t.Methods).Where(m=>m.Name==name).ToArray();
                        if(candidates.Length!=1)throw new InvalidDataException("Ambiguous baseline evidence method: "+name);
                        CaptureType(candidates[0].DeclaringType);addMember(candidates[0]);
                    }
                }
            }
        }
    }
}

// A resolved container is not proof that its serialized rows have stable keys.
// Reject raw client objects with renamed fields before producing an observation config.
internal static class ObservationSchema
{
    internal static void RequireStable(TypeReference source, string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        TypeReference Bind(TypeReference value, TypeReference owner)
        {
            if (value is GenericParameter p && owner is GenericInstanceType g && p.Type == GenericParameterType.Type)
                return g.GenericArguments[p.Position];
            if (value is ArrayType a) return new ArrayType(Bind(a.ElementType, owner));
            if (value is GenericInstanceType nested)
            {
                var result = new GenericInstanceType(nested.ElementType);
                foreach (var argument in nested.GenericArguments) result.GenericArguments.Add(Bind(argument, owner));
                return result;
            }
            return value;
        }
        void Visit(TypeReference value)
        {
            if (!seen.Add(value.FullName)) return;
            if (value is ArrayType a) { Visit(a.ElementType); return; }
            if (value is GenericParameter || value.IsPrimitive || value.FullName is "System.String" or "System.DateTime" or "System.Decimal" or "System.Object") return;
            // Protobuf JSON owns its stable protocol field names, independently of client obfuscation.
            if (value.FullName.StartsWith("Proto.", StringComparison.Ordinal)) return;
            if (value is GenericInstanceType g && (g.ElementType.Namespace.StartsWith("System.Collections", StringComparison.Ordinal) || g.ElementType.FullName == "System.Nullable`1"))
            { foreach (var argument in g.GenericArguments) Visit(argument); return; }
            var type = value.Resolve();
            if (type.IsEnum) return;
            bool dataContract = type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.Serialization.DataContractAttribute");
            bool Included(IMemberDefinition member) => dataContract
                ? member.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.Serialization.DataMemberAttribute")
                : !member.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.Serialization.IgnoreDataMemberAttribute") &&
                  (member is FieldDefinition f && f.IsPublic && !f.IsStatic || member is PropertyDefinition p && p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true && p.Parameters.Count == 0);
            foreach (var member in type.Fields.Cast<IMemberDefinition>().Concat(type.Properties).Where(Included))
            {
                var name = member.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "System.Runtime.Serialization.DataMemberAttribute")?
                    .Properties.Where(p => p.Name == "Name").Select(p => p.Argument.Value as string).FirstOrDefault() ?? member.Name;
                if (name.Any(c => c >= '\u0370' && c <= '\u1fff'))
                    throw new InvalidDataException("Unstable observation output: " + path + " -> " + type.FullName + "." + name + "; use explicit ItemPaths or a stable native DTO.");
                Visit(Bind(member is FieldDefinition field ? field.FieldType : ((PropertyDefinition)member).PropertyType, value));
            }
            if (type.BaseType != null) Visit(Bind(type.BaseType, value));
        }
        Visit(source);
    }
}
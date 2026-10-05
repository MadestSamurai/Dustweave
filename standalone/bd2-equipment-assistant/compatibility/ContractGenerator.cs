using Mono.Cecil;
using System.Text.RegularExpressions;
namespace BD2Equipment.Compatibility;

// Maintainer bootstrap only. Releases resolve the saved structural contract.
public static class ContractGenerator
{
 public static BindingContract Generate(MetadataIndex index,string root)
 {
  var texts=Directory.GetFiles(Path.Combine(root,"hook"),"*.cs").Select(File.ReadAllText)
   .Append(File.ReadAllText(Path.Combine(root,"evidence-spec.json")));
  var names=Regex.Matches(string.Join("\n",texts),@"[\u0370-\u1fff]+").Select(m=>m.Value).ToHashSet();
  var selected=new HashSet<IMemberDefinition>();var types=new HashSet<TypeDefinition>();
  var queue=new Queue<TypeDefinition>(index.Types.Where(t=>names.Contains(t.Name)||new[]{"UIBase","EquipmentMakingSelectUI","EquipmentMakingUI","EquipmentUpgradeUI","EquipmentUpgradePopupUI"}.Contains(t.FullName)));
  var visited=new HashSet<TypeDefinition>();
  void Follow(TypeReference? r){if(r==null)return;if(r is GenericInstanceType g){foreach(var a in g.GenericArguments)Follow(a);r=g.ElementType;}if(r.Scope==index.Module||r.Module==index.Module){var t=index.Types.SingleOrDefault(t=>t.FullName==r.FullName);if(t!=null)queue.Enqueue(t);}}
  while(queue.Count>0){var t=queue.Dequeue();if(!visited.Add(t))continue;Follow(t.BaseType);if(names.Contains(t.Name))types.Add(t);
   foreach(var m in MetadataIndex.Members(t).Where(m=>names.Contains(m.Name))){selected.Add(m);types.Add(m.DeclaringType);if(m is FieldDefinition f)Follow(f.FieldType);if(m is PropertyDefinition p)Follow(p.PropertyType);if(m is MethodDefinition method)Follow(method.ReturnType);}
  }
  foreach(var name in names)if(!types.Any(t=>t.Name==name)&&!selected.Any(m=>m.Name==name))throw new InvalidDataException("Unbound bootstrap symbol: "+name);
  return new(1,types.OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new TypeContract(t.FullName,index.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),selected.Where(m=>m.DeclaringType==t).OrderBy(m=>m.FullName,StringComparer.Ordinal).Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray(),Array.Empty<ApiContract>(),new());
 }
}

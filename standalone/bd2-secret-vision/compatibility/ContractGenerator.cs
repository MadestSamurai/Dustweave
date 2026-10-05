using Mono.Cecil;using System.Text.RegularExpressions;
namespace BD2SecretVision.Compatibility;
public static class ContractGenerator {
 public static BindingContract Generate(MetadataIndex index,string root){
 var source=string.Join("\n",Directory.GetFiles(Path.Combine(root,"hook"),"*.cs").Select(File.ReadAllText));
 var names=Regex.Matches(source,@"[\u0370-\u1fff]+").Select(m=>m.Value).ToHashSet();
 var named=Regex.Matches(source,@"\bHopscotch\w+\b").Select(m=>m.Value).ToHashSet();
 var types=new HashSet<TypeDefinition>();var selected=new HashSet<IMemberDefinition>();
 var queue=new Queue<TypeDefinition>(index.Types.Where(t=>names.Contains(t.Name)||named.Contains(t.Name)));var seen=new HashSet<TypeDefinition>();
 void Follow(TypeReference? r){if(r==null)return;if(r is GenericInstanceType g){foreach(var a in g.GenericArguments)Follow(a);r=g.ElementType;}var t=index.Types.SingleOrDefault(t=>t.FullName==r.FullName);if(t!=null)queue.Enqueue(t);}
 while(queue.Count>0){var t=queue.Dequeue();if(!seen.Add(t))continue;Follow(t.BaseType);if(names.Contains(t.Name)||named.Contains(t.Name))types.Add(t);
 foreach(var m in MetadataIndex.Members(t).Where(m=>names.Contains(m.Name))){types.Add(t);selected.Add(m);if(m is FieldDefinition f)Follow(f.FieldType);if(m is PropertyDefinition p)Follow(p.PropertyType);if(m is MethodDefinition md)Follow(md.ReturnType);}
 }
 foreach(var n in names)if(!types.Any(t=>t.Name==n)&&!selected.Any(m=>m.Name==n))throw new InvalidDataException("Unbound source symbol: "+n);
 return new(1,types.OrderBy(t=>t.FullName).Select(t=>new TypeContract(t.FullName,index.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),selected.Where(m=>m.DeclaringType==t).OrderBy(m=>m.FullName).Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray(),[],new());
 }
}

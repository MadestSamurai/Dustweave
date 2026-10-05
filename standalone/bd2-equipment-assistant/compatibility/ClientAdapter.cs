using System.Text.Json;
using System.Text.RegularExpressions;
namespace BD2Equipment.Compatibility;

public sealed class ClientAdapter : IDisposable
{
 public MetadataIndex Index { get; }
 public ResolvedBindings Bindings { get; }
 public Dictionary<string,string> Names { get; }=new(StringComparer.Ordinal);
 public ClientAdapter(string managed)
 {
  Index=new(Path.Combine(managed,"Assembly-CSharp.dll"));
  using var stream=typeof(ClientAdapter).Assembly.GetManifestResourceStream("Equipment.contract.json")!;
  Bindings=BindingResolver.Resolve(Index,JsonSerializer.Deserialize<BindingContract>(stream)!);
  if(Bindings.Report.Status!="compatible")throw new InvalidOperationException("Client interface adaptation failed: "+string.Join("; ",Bindings.Report.Errors));
  void Add(string old,string current){if(Names.TryGetValue(old,out var previous)&&previous!=current)throw new InvalidOperationException("Ambiguous symbol adaptation: "+old);Names[old]=current;}
  foreach(var t in Bindings.Types)if(MetadataIndex.Obfuscated(t.Key))Add(t.Key,t.Value.FullName);
  foreach(var m in Bindings.Members){string old=m.Key.Split('|')[1];if(MetadataIndex.Obfuscated(old))Add(old,m.Value.Name);}
 }
 public string Name(string name)=>Names.GetValueOrDefault(name,name);
 public string Rewrite(string text)=>Regex.Replace(text,@"[\u0370-\u1fff]+",m=>Names.TryGetValue(m.Value,out var n)?n:m.Value);
 public string Source(){
  string Q(string s)=>JsonSerializer.Serialize(s);
  return "namespace BD2Equipment.Live { internal static class ClientNames { internal const string Mvid="+Q(Index.Module.Mvid.ToString())+"; internal static string Map(string value) { switch(value){"+string.Join("",Names.Select(p=>"case "+Q(p.Key)+": return "+Q(p.Value)+";"))+"default: return value;} } } }";
 }
 public void Dispose()=>Index.Dispose();
}

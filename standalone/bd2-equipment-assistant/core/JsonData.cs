using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace BD2Equipment.Core;
public static class J
{
 public static long N(JsonNode? n,long fallback=0)=>n==null?fallback:long.Parse(n.ToString(),CultureInfo.InvariantCulture);
 public static string S(JsonNode? n,string fallback="")=>n?.ToString()??fallback;
 public static bool B(JsonNode? n){if(n==null)return false;var kind=n.GetValueKind();if(kind==System.Text.Json.JsonValueKind.True)return true;if(kind==System.Text.Json.JsonValueKind.False)return false;if(kind==System.Text.Json.JsonValueKind.Number)return decimal.Parse(n.ToString(),CultureInfo.InvariantCulture)!=0;throw new InvalidOperationException("状态标记不是布尔值或数值");}
 public static JsonObject O(JsonNode? n)=>n?.AsObject()??new();
 public static IEnumerable<JsonNode> A(JsonNode? n)=>n?.AsArray().Where(x=>x!=null).Select(x=>x!)??Enumerable.Empty<JsonNode>();
 public static JsonNode? Clone(JsonNode? n)=>n?.DeepClone();
 public static JsonObject Copy(JsonNode n)=>n.DeepClone().AsObject();
 public static JsonArray Array(IEnumerable<JsonNode?> nodes)=>new(nodes.Select(Clone).ToArray());
 public static JsonObject Select(JsonNode n,params string[] keys){var r=new JsonObject();foreach(var k in keys)r[k]=Clone(n[k]);return r;}
 public static long Integer(JsonNode? n,string name,long min=0,long max=2_000_000_000){if(n==null||n.ToJsonString().Any(c=>!char.IsAsciiDigit(c)&&c!='-')||!long.TryParse(n.ToString(),out long value)||value<min||value>max)throw new InvalidOperationException($"{name}必须是{min}至{max}的整数");return value;}
 public static string Canonical(JsonNode? n){
  if(n is JsonObject o)return "{"+string.Join(",",o.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>Quote(p.Key)+":"+Canonical(p.Value)))+"}";
  if(n is JsonArray a)return "["+string.Join(",",a.Select(Canonical))+"]";
  if(n is JsonValue v&&v.TryGetValue<string>(out var s))return Quote(s);
  return n?.ToJsonString()??"null";
 }
 static string Quote(string value){var b=new StringBuilder("\"");foreach(char c in value)b.Append(c switch {'"'=>"\\\"",'\\'=>"\\\\",'\b'=>"\\b",'\f'=>"\\f",'\n'=>"\\n",'\r'=>"\\r",'\t'=>"\\t",_ when c<32=>"\\u"+((int)c).ToString("x4"),_=>c.ToString()});return b.Append('"').ToString();}
 public static string Hash(JsonNode n)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(n)))).ToLowerInvariant();
 public static bool Equal(JsonNode? a,JsonNode? b)=>Canonical(a)==Canonical(b);
 public static JsonObject Read(string path){var remote=EquipmentReadRetry.Read(()=>{bool handled=BD2.LocalIpc.DesktopFiles.Read(path,out var bytes);return (handled,bytes);});if(remote.handled)return remote.bytes==null?new JsonObject():JsonNode.Parse(remote.bytes)!.AsObject();
  for(int i=0;;i++)try{using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonNode.Parse(file)!.AsObject();}catch(IOException)when(i<4){Thread.Sleep(50);}
 }
 public static void Write(string path,JsonNode value){if(BD2.LocalIpc.DesktopFiles.Write(path,Encoding.UTF8.GetBytes(Canonical(value)),Path.GetFileName(path)=="command.json"))return;Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var data=Encoding.UTF8.GetBytes(Canonical(value));file.Write(data);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public static string ResourceText(string name){using var stream=typeof(J).Assembly.GetManifestResourceStream("Equipment."+name)??throw new InvalidOperationException("缺少内置数据："+name);using var reader=new StreamReader(stream,Encoding.UTF8);return reader.ReadToEnd();}
 public static JsonObject Resource(string name)=>JsonNode.Parse(ResourceText(name))!.AsObject();
 public static string Data=>Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant");
 public static string Stop=>Path.Combine(Data,"stop");
}

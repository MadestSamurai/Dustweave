using System.Text;
using System.Text.Json;
using System.Reflection;
namespace Dustweave;
// Small append-only transition journal, bounded independently of task snapshots.
public static class DailySuiteDiagnostics {
 static readonly object Sync=new();
 static readonly string Version=Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"unknown";
 public static void Record(string root,string kind,object detail) {
  try {lock(Sync){
   string path=Path.Combine(root,"suite","diagnostics","ownership.jsonl");
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   if(File.Exists(path)&&new FileInfo(path).Length>=512*1024){for(int n=3;n>=1;n--){string from=n==1?path:path+"."+(n-1);if(File.Exists(from))File.Move(from,path+"."+n,true);}}
   File.AppendAllText(path,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,version=Version,processId=Environment.ProcessId,kind,detail},new JsonSerializerOptions{IncludeFields=true})+"\n",new UTF8Encoding(false));
  }}catch(Exception e)when(e is IOException or UnauthorizedAccessException){ /* Diagnostics never stop connection/automation. */ }
 }
}

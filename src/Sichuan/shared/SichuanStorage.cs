using System;
using System.IO;
namespace BD2Sichuan {
// Keep live pipe names stable; preferences and evidence are owned by Dustweave.
public static class SichuanStorage {
 public static string HostRoot {get{return AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1") as string ?? Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant");}}
 public static string Data {get{return Path.Combine(HostRoot,"tools","sichuan");}}
 public static string Diagnostics {get{return Path.Combine(Data,"diagnostics");}}
 public static string Images {get{return Path.Combine(Diagnostics,"images");}}
#if DESKTOP
 public static void ImportSettings(string legacy) {
  string target=Path.Combine(Data,"settings.json"),source=Path.Combine(legacy,"settings.json");
  if(File.Exists(target))return;
  try { if(!Ordinary(source)||!Ordinary(Data)||!File.Exists(source)||new FileInfo(source).Length>65536)return;
   var value=System.Text.Json.JsonSerializer.Deserialize<SichuanSettings>(File.ReadAllText(source));if(value!=null&&SichuanRunEngine.ValidInterval(value.IntervalMilliseconds)){Directory.CreateDirectory(Data);value.Save(target);}
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Text.Json.JsonException){}catch(ArgumentException){}
 }
 static bool Ordinary(string p){for(string? d=Path.GetFullPath(p);d!=null;d=Path.GetDirectoryName(d))if((File.Exists(d)||Directory.Exists(d))&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)return false;return true;}
 public static void ImportLegacyLogs(string legacy){
  foreach(string name in new[]{"runtime.log","runtime.log.previous","diagnostics.jsonl","diagnostics.jsonl.previous","events.jsonl","events.jsonl.previous","compatibility.json","compatibility-error.json"}){
   try{string source=Path.Combine(legacy,name);if(!File.Exists(source)||!Ordinary(source)||!Ordinary(Diagnostics))continue;Directory.CreateDirectory(Diagnostics);File.Move(source,Path.Combine(Diagnostics,"legacy-"+Guid.NewGuid().ToString("N")+"-"+name),false);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
#endif
}
}
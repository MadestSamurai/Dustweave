using System;
using System.IO;
namespace BD2Rhythm {
// Live IPC keeps its established identity. Durable files belong to Dustweave.
public static class RhythmStorage {
 public static string HostRoot {get{return AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1") as string ?? Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant");}}
 public static string Data {get{return Path.Combine(HostRoot,"tools","rhythm");}}
 public static string Diagnostics {get{return Path.Combine(Data,"diagnostics");}}
#if DESKTOP
 public static RhythmSettings LoadSettings(string data,string legacy) {
  string path=Path.Combine(data,"settings.json");
  var saved=RhythmJson.Read<RhythmSettings>(path);
  if(saved!=null)return RhythmControl.ValidSettings(saved.JitterMs,saved.OffsetMs)?saved:new RhythmSettings();
  // Import only bounded, valid preferences; never transfer old controls or identity.
  try {
   string old=Path.Combine(legacy,"settings.json");
   if(!File.Exists(path)&&Ordinary(old)&&Ordinary(data)&&File.Exists(old)&&new FileInfo(old).Length<=65536){
    var value=System.Text.Json.JsonSerializer.Deserialize<RhythmSettings>(File.ReadAllText(old));
    if(value!=null&&RhythmControl.ValidSettings(value.JitterMs,value.OffsetMs)){RhythmJson.Write(path,value);return value;}
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Text.Json.JsonException){}
  return new RhythmSettings();
 }
 static bool Ordinary(string p){for(string? d=Path.GetFullPath(p);d!=null;d=Path.GetDirectoryName(d))if((File.Exists(d)||Directory.Exists(d))&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)return false;return true;}
 public static void ImportLegacyLogs(string legacy,string diagnostics){
  foreach(string name in new[]{"runtime.log","runtime.log.previous","connection-error.json","compatibility.json"}){
   try{
    string source=Path.Combine(legacy,name);if(!File.Exists(source)||!Ordinary(source)||!Ordinary(diagnostics))continue;
    Directory.CreateDirectory(diagnostics);File.Move(source,Path.Combine(diagnostics,"legacy-"+Guid.NewGuid().ToString("N")+"-"+name),false);
   }catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
#endif
}
}

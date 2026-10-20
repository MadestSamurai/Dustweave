using System;
using System.IO;
namespace BD2Fishing {
// Keep the live channel and transaction journals at their established identities.
// Preferences and disposable evidence belong to the host's movable cache root.
public static class FishingStorage {
 public static string HostRoot {get{return AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1") as string ?? Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant");}}
 public static string Data {get{return Path.Combine(HostRoot,"tools","fishing");}}
 public static string Diagnostics {get{return Path.Combine(Data,"diagnostics");}}
 public static string Preferences(string liveRoot){return Path.GetFullPath(liveRoot).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(FishingIdentity.DataRoot).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)?Data:liveRoot;}
 public static string DiagnosticRoot(string liveRoot){return Preferences(liveRoot)==liveRoot?liveRoot:Diagnostics;}
#if DESKTOP
 public static FishingConnection ImportLegacyLogsForConnection(string root){if(Preferences(root)!=root)ImportLegacyLogs(root);return new FishingConnection(root);}
 public static void ImportSettings(string legacy) {
  string target=Path.Combine(Data,"settings.json"),source=Path.Combine(legacy,"settings.json");
  if(File.Exists(target))return;
  try { if(!Ordinary(source)||!Ordinary(Data)||!File.Exists(source)||new FileInfo(source).Length>65536)return;
   var value=System.Text.Json.JsonSerializer.Deserialize<FishingSettings>(File.ReadAllText(source));if(value!=null&&FishingControl.ValidSettings(value.NextCastMilliseconds,value.CastGauge)){Directory.CreateDirectory(Data);File.WriteAllText(target,System.Text.Json.JsonSerializer.Serialize(value));}
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Text.Json.JsonException){}catch(ArgumentException){}
 }
 static bool Ordinary(string p){for(string? d=Path.GetFullPath(p);d!=null;d=Path.GetDirectoryName(d))if((File.Exists(d)||Directory.Exists(d))&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)return false;return true;}
 public static void ImportLegacyLogs(string legacy){
  foreach(string name in new[]{"runtime.log","runtime.log.previous","connection.log","connection.log.previous","compatibility.json","compatibility-error.json"}){
   try{string source=Path.Combine(legacy,name);if(!File.Exists(source)||!Ordinary(source)||!Ordinary(Diagnostics))continue;Directory.CreateDirectory(Diagnostics);File.Move(source,Path.Combine(Diagnostics,"legacy-"+Guid.NewGuid().ToString("N")+"-"+name),false);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
#endif
}
}
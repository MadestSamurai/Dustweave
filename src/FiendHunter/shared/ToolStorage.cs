using System;
using System.IO;
namespace BD2FiendHunter.Shared {
// Keep the established pipe identity; durable files belong to the host now.
public static class ToolStorage {
 public static string LegacyRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2FiendHunter");}}
 public static string HostRoot {get{return AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1") as string ?? Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant");}}
 public static string Data {get{return Path.Combine(HostRoot,"tools","fiend-hunter");}}
 public static string Diagnostics {get{return Path.Combine(Data,"diagnostics");}}
#if DESKTOP
 public static void ImportLegacyLogs() {
  // Keep the original IPC root and settings. Only known diagnostics are moved.
  bool Ordinary(string p){for(string? d=Path.GetFullPath(p);d!=null;d=Path.GetDirectoryName(d))if((File.Exists(d)||Directory.Exists(d))&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)return false;return true;}
  foreach(var name in new[]{"events.jsonl","events-previous.jsonl","desktop-error.txt","compatibility.json"}){
   try{var source=Path.Combine(LegacyRoot,name);if(!File.Exists(source)||!Ordinary(source)||!Ordinary(Diagnostics))continue;
    Directory.CreateDirectory(Diagnostics);var target=Path.Combine(Diagnostics,"legacy-"+Guid.NewGuid().ToString("N")+"-"+(name.EndsWith(".txt")?"desktop-error.log":name));
    File.Move(source,target,false);
   }catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
#endif
}
}

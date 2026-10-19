#nullable disable
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace BD2FiendHunter.Shared {
public static class AtomicFiles {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool MoveFileEx(string from,string to,int flags);
 public static bool Exists(string path){
#if DESKTOP
 return BD2.LocalIpc.DesktopFiles.Exists(path);
#elif RUNTIME
 return BD2.LocalIpc.RuntimeFiles.Handles(path)?BD2.LocalIpc.RuntimeFiles.Read(path)!=null:File.Exists(path);
#else
 return File.Exists(path);
#endif
 }
 public static string Read(string path){
#if DESKTOP
 byte[] bytes;if(BD2.LocalIpc.DesktopFiles.Read(path,out bytes)){if(bytes==null)throw new FileNotFoundException(path);return System.Text.Encoding.UTF8.GetString(bytes);}
#elif RUNTIME
 if(BD2.LocalIpc.RuntimeFiles.Handles(path)){var bytes=BD2.LocalIpc.RuntimeFiles.Read(path);if(bytes==null)throw new FileNotFoundException(path);return System.Text.Encoding.UTF8.GetString(bytes);}
#endif
using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(f))return r.ReadToEnd();}
 public static bool TryWrite(string path,string text){
#if DESKTOP
 if(BD2.LocalIpc.DesktopFiles.Write(path,System.Text.Encoding.UTF8.GetBytes(text),Path.GetFileName(path)=="command.json"))return true;
#elif RUNTIME
 if(BD2.LocalIpc.RuntimeFiles.Write(path,System.Text.Encoding.UTF8.GetBytes(text)))return true;
#endif
  var temp=path+".tmp";
  try {File.WriteAllText(temp,text);return MoveFileEx(temp,path,1|8);}
  catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
 }
}
public sealed class Publisher {
 readonly Dictionary<string,string> pending=new Dictionary<string,string>();
 public int Pending=>pending.Count;
 public long Deferrals{get;private set;}
 public void Write(string path,string text){pending[path]=text;if(AtomicFiles.TryWrite(path,text))pending.Remove(path);else Deferrals++;}
 public void Flush(){foreach(var path in new List<string>(pending.Keys)){if(AtomicFiles.TryWrite(path,pending[path]))pending.Remove(path);else Deferrals++;}}
}
}

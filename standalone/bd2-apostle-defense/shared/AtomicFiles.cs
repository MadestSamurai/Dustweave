using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
namespace BD2ApostleDefense
{
 // Used on background I/O workers, never on Unity's frame thread.
 public static class AtomicFiles
 {
  private static readonly object[] Writers=CreateLocks();
  private static object[] CreateLocks(){var value=new object[32];for(int i=0;i<value.Length;i++)value[i]=new object();return value;}
  public static void Write(string path,Action<Stream> serialize)
  {
   path=Path.GetFullPath(path);
   lock(Writers[(StringComparer.OrdinalIgnoreCase.GetHashCode(path)&int.MaxValue)%Writers.Length])Publish(path,serialize);
  }
  private static void Publish(string path,Action<Stream> serialize)
  {
   path=Path.GetFullPath(path);string directory=Path.GetDirectoryName(path)??throw new ArgumentException("Missing parent directory",nameof(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try
   {
    Directory.CreateDirectory(directory);
    using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){serialize(f);f.Flush(true);}
    for(int attempt=0;;attempt++)
    {
     try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);break;}
     catch(Exception e)when((e is IOException||e is UnauthorizedAccessException)&&attempt<2){Thread.Sleep(20*(attempt+1));}
    }
   }
   catch(Exception e){IoDiagnostics.Record(directory,"write",path,e);throw new IOException("File publication failed: "+path+" ("+e.GetType().Name+", 0x"+e.HResult.ToString("X8")+")",e);}
   finally{try{if(File.Exists(temp))File.Delete(temp);}catch(Exception e){IoDiagnostics.Record(directory,"cleanup",path,e);}}
  }
 }
 public static class IoDiagnostics
 {
  private static readonly object Sync=new object();
  private static readonly Dictionary<string,long> Last=new Dictionary<string,long>();
  public static bool Attempt(string root,string operation,string file,Action action)
  {try{action();return true;}catch(Exception e){Record(root,operation,Path.Combine(root,file),e);return false;}}
  public static void Record(string root,string operation,string path,Exception error)
  {
   try{lock(Sync){long now=DateTime.UtcNow.Ticks;string key=operation+"|"+path+"|"+error.GetType().Name+"|"+error.HResult;long last;
    if(Last.TryGetValue(key,out last)&&now-last<TimeSpan.FromSeconds(15).Ticks)return;
    if(Last.Count>128)Last.Clear();Last[key]=now;
    Directory.CreateDirectory(root);string log=Path.Combine(root,"io-errors-"+System.Diagnostics.Process.GetCurrentProcess().Id+".log");
    if(File.Exists(log)&&new FileInfo(log).Length>2*1024*1024){string previous=log+".previous";if(File.Exists(previous))File.Delete(previous);File.Move(log,previous);}
    File.AppendAllText(log,DateTime.UtcNow.ToString("O")+" runtime="+Identity.Runtime+" op="+operation+" path="+path+" HResult=0x"+error.HResult.ToString("X8")+Environment.NewLine+error+Environment.NewLine);
   }}catch{/* An inaccessible diagnostics folder must not mask the original failure. */}
  }
 }
}

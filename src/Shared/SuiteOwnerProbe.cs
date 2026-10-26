#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
namespace BD2Daily {
 [DataContract] public sealed class SuiteOwnerObservation {
  [DataMember] public int ProcessId, NativeError;
  [DataMember] public long ExpectedStartTicks, ObservedStartTicks, CheckedUtcTicks;
  [DataMember] public uint ExitCode;
  [DataMember] public string State="", Problem="", Operation="";
 }
 // Unity Mono's GetProcessById can report ArgumentException for a live process
 // when its internal higher-access OpenProcess fails. Query only the lifetime
 // information we need; access failure is never evidence of process exit.
 public static class SuiteOwnerProbe {
  const uint QueryLimitedInformation=0x1000, Synchronize=0x100000;
  public static SuiteOwnerObservation Read(int pid,long expectedStart) {
   SuiteOwnerObservation result;var watch=Watch(pid,expectedStart,out result);if(watch!=null)watch.Dispose();return result;
  }
  internal static SuiteOwnerWatch Watch(int pid,long expectedStart,out SuiteOwnerObservation result) {
   result=new SuiteOwnerObservation{ProcessId=pid,ExpectedStartTicks=expectedStart,CheckedUtcTicks=DateTime.UtcNow.Ticks,State="unreadable",Problem="suite.owner-unreadable"};
   if(pid<=0||expectedStart<=0){result.State="missing";result.Problem="suite.owner-missing";return null;}
   IntPtr handle=IntPtr.Zero;
   try {
    result.Operation="OpenProcess(query-limited+sync)";handle=OpenProcess(QueryLimitedInformation|Synchronize,false,pid);
    if(handle==IntPtr.Zero){result.NativeError=Marshal.GetLastWin32Error();if(result.NativeError==87){result.State="exited";result.Problem="suite.owner-exited";}return null;}
    long created,exited,kernel,user;
    result.Operation="GetProcessTimes";
    if(!GetProcessTimes(handle,out created,out exited,out kernel,out user)){result.NativeError=Marshal.GetLastWin32Error();return null;}
    result.ObservedStartTicks=DateTime.FromFileTimeUtc(created).Ticks;
    if(result.ObservedStartTicks!=expectedStart){result.State="replaced";result.Problem="suite.owner-replaced";return null;}
    result.Operation="GetExitCodeProcess";uint exitCode;
    if(!GetExitCodeProcess(handle,out exitCode)){result.NativeError=Marshal.GetLastWin32Error();return null;}
    result.ExitCode=exitCode;
    // Exit code 259 is also a legal exit value. The exit timestamp distinguishes it.
    if(exited!=0||exitCode!=259){result.State="exited";result.Problem="suite.owner-exited";return null;}
    result.State="alive";result.Problem="";result.Operation="confirmed";
    // Liveness was established by the final exit query, not when a possibly
    // slow sandbox OpenProcess started.
    result.CheckedUtcTicks=DateTime.UtcNow.Ticks;
    var watch=new SuiteOwnerWatch(handle,pid,expectedStart);handle=IntPtr.Zero;return watch;
   }catch(Exception e){result.Operation=e.GetType().Name;return null;}
   finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
  }
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
 // A validated process handle identifies one lifetime even if Windows reuses its
 // PID. A zero-time wait reads that lifetime directly, without an expiring cache,
 // worker scheduling, repeated OpenProcess, or waiting inside Unity's callback.
 internal sealed class SuiteOwnerWatch : IDisposable {
  IntPtr handle;readonly int pid;readonly long started;
  internal SuiteOwnerWatch(IntPtr handle,int pid,long started){this.handle=handle;this.pid=pid;this.started=started;}
  internal SuiteOwnerObservation Read(){
   var value=new SuiteOwnerObservation{ProcessId=pid,ExpectedStartTicks=started,ObservedStartTicks=started,CheckedUtcTicks=DateTime.UtcNow.Ticks,State="unreadable",Problem="suite.owner-unreadable",Operation="WaitForSingleObject(process,0)"};
   if(handle==IntPtr.Zero)return value;
   uint state=WaitForSingleObject(handle,0);
   if(state==258){value.State="alive";value.Problem="";value.ExitCode=259;}
   else if(state==0){value.State="exited";value.Problem="suite.owner-exited";uint code;if(GetExitCodeProcess(handle,out code))value.ExitCode=code;}
   else value.NativeError=Marshal.GetLastWin32Error();
   return value;
  }
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
 // Opening/querying a process belongs on one bounded worker. Once validated,
 // the nonblocking lifetime signal replaces periodically expiring observations.
 public sealed class SuiteOwnerMonitor : IDisposable {
  readonly object gate=new object();
  readonly Func<int,long,SuiteOwnerObservation> probe;
  readonly Func<long> clock;
  readonly Action<Action> queue;
  readonly Timer refresher;
  readonly bool retainLifetime;
  SuiteOwnerWatch lifetime;
  int pid;long started,revision,next;
  bool pending,disposed;
  SuiteOwnerObservation result;
  public SuiteOwnerMonitor():this(SuiteOwnerProbe.Read,()=>DateTime.UtcNow.Ticks,a=>ThreadPool.QueueUserWorkItem(_=>a())){
   retainLifetime=true;
   // Retry initial acquisition independently of game frames. A retained
   // lifetime never depends on this timer or on ThreadPool scheduling again.
   refresher=new Timer(_=>{try{Schedule();}catch{}},null,500,500);
  }
  public SuiteOwnerMonitor(Func<int,long,SuiteOwnerObservation> probe,Func<long> clock,Action<Action> queue){this.probe=probe;this.clock=clock;this.queue=queue;}
  public SuiteOwnerObservation Read(int processId,long processStart) {
   lock(gate){
    if(pid!=processId||started!=processStart){if(lifetime!=null)lifetime.Dispose();lifetime=null;pid=processId;started=processStart;revision++;result=null;next=0;}
   }
   Schedule();
   lock(gate){
    long now=clock();var value=result;
    if(!disposed&&lifetime!=null)return lifetime.Read();
    if(disposed||value==null||value.CheckedUtcTicks>now||now-value.CheckedUtcTicks>TimeSpan.FromSeconds(3).Ticks)
     value=new SuiteOwnerObservation{ProcessId=processId,ExpectedStartTicks=processStart,CheckedUtcTicks=now,
      State=disposed?"unreadable":result==null?"checking":"stale",Problem="suite.owner-unreadable",Operation="background-query"};
    return value;
   }
  }
  void Schedule(){
   int processId;long processStart,version;
   lock(gate){
    if(disposed||lifetime!=null||pid<=0||started<=0||pending||clock()<next)return;
    pending=true;processId=pid;processStart=started;version=revision;
   }
   try{queue(()=>Refresh(processId,processStart,version));}
   catch{lock(gate){pending=false;next=clock()+TimeSpan.FromMilliseconds(500).Ticks;}throw;}
  }
  void Refresh(int processId,long processStart,long version){
   SuiteOwnerObservation value;SuiteOwnerWatch opened=null;
   try{if(retainLifetime)opened=SuiteOwnerProbe.Watch(processId,processStart,out value);else value=probe(processId,processStart);}
   catch(Exception e){value=new SuiteOwnerObservation{ProcessId=processId,ExpectedStartTicks=processStart,CheckedUtcTicks=clock(),State="unreadable",Problem="suite.owner-unreadable",Operation=e.GetType().Name};}
   lock(gate){pending=false;if(disposed||version!=revision){if(opened!=null)opened.Dispose();return;}lifetime=opened;result=value;next=clock()+TimeSpan.FromMilliseconds(500).Ticks;}
  }
  public void Dispose(){lock(gate){disposed=true;revision++;result=null;if(lifetime!=null)lifetime.Dispose();lifetime=null;}if(refresher!=null)refresher.Dispose();}
 }
}

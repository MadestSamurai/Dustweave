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
  const uint QueryLimitedInformation=0x1000;
  public static SuiteOwnerObservation Read(int pid,long expectedStart) {
   var result=new SuiteOwnerObservation{ProcessId=pid,ExpectedStartTicks=expectedStart,CheckedUtcTicks=DateTime.UtcNow.Ticks,State="unreadable",Problem="suite.owner-unreadable"};
   if(pid<=0||expectedStart<=0){result.State="missing";result.Problem="suite.owner-missing";return result;}
   IntPtr handle=IntPtr.Zero;
   try {
    result.Operation="OpenProcess(query-limited)";handle=OpenProcess(QueryLimitedInformation,false,pid);
    if(handle==IntPtr.Zero){result.NativeError=Marshal.GetLastWin32Error();if(result.NativeError==87){result.State="exited";result.Problem="suite.owner-exited";}return result;}
    long created,exited,kernel,user;
    result.Operation="GetProcessTimes";
    if(!GetProcessTimes(handle,out created,out exited,out kernel,out user)){result.NativeError=Marshal.GetLastWin32Error();return result;}
    result.ObservedStartTicks=DateTime.FromFileTimeUtc(created).Ticks;
    if(result.ObservedStartTicks!=expectedStart){result.State="replaced";result.Problem="suite.owner-replaced";return result;}
    result.Operation="GetExitCodeProcess";uint exitCode;
    if(!GetExitCodeProcess(handle,out exitCode)){result.NativeError=Marshal.GetLastWin32Error();return result;}
    result.ExitCode=exitCode;
    // Exit code 259 is also a legal exit value. The exit timestamp distinguishes it.
    if(exited!=0||exitCode!=259){result.State="exited";result.Problem="suite.owner-exited";return result;}
    result.State="alive";result.Problem="";result.Operation="confirmed";
    // Liveness was established by the final exit query, not when a possibly
    // slow sandbox OpenProcess started.
    result.CheckedUtcTicks=DateTime.UtcNow.Ticks;return result;
   }catch(Exception e){result.Operation=e.GetType().Name;return result;}
   finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
  }
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
 // Kernel/sandbox queries must never run in Unity's frame callback. A single
 // bounded worker refreshes proof; unreadable or expired proof is not authority.
 public sealed class SuiteOwnerMonitor : IDisposable {
  readonly object gate=new object();
  readonly Func<int,long,SuiteOwnerObservation> probe;
  readonly Func<long> clock;
  readonly Action<Action> queue;
  readonly Timer refresher;
  int pid;long started,revision,next;
  bool pending,disposed;
  SuiteOwnerObservation result;
  public SuiteOwnerMonitor():this(SuiteOwnerProbe.Read,()=>DateTime.UtcNow.Ticks,a=>ThreadPool.QueueUserWorkItem(_=>a())){
   // Keep refreshing even when Unity stops producing frames during a load.
   // Otherwise the first resumed frame would incorrectly revoke a live owner.
   refresher=new Timer(_=>{try{Schedule();}catch{}},null,500,500);
  }
  public SuiteOwnerMonitor(Func<int,long,SuiteOwnerObservation> probe,Func<long> clock,Action<Action> queue){this.probe=probe;this.clock=clock;this.queue=queue;}
  public SuiteOwnerObservation Read(int processId,long processStart) {
   lock(gate){
    if(pid!=processId||started!=processStart){pid=processId;started=processStart;revision++;result=null;next=0;}
   }
   Schedule();
   lock(gate){
    long now=clock();var value=result;
    if(disposed||value==null||value.CheckedUtcTicks>now||now-value.CheckedUtcTicks>TimeSpan.FromSeconds(3).Ticks)
     value=new SuiteOwnerObservation{ProcessId=processId,ExpectedStartTicks=processStart,CheckedUtcTicks=now,
      State=disposed?"unreadable":result==null?"checking":"stale",Problem="suite.owner-unreadable",Operation="background-query"};
    return value;
   }
  }
  void Schedule(){
   int processId;long processStart,version;
   lock(gate){
    if(disposed||pid<=0||started<=0||pending||clock()<next)return;
    pending=true;processId=pid;processStart=started;version=revision;
   }
   try{queue(()=>Refresh(processId,processStart,version));}
   catch{lock(gate){pending=false;next=clock()+TimeSpan.FromMilliseconds(500).Ticks;}throw;}
  }
  void Refresh(int processId,long processStart,long version){
   SuiteOwnerObservation value;
   try{value=probe(processId,processStart);}
   catch(Exception e){value=new SuiteOwnerObservation{ProcessId=processId,ExpectedStartTicks=processStart,CheckedUtcTicks=clock(),State="unreadable",Problem="suite.owner-unreadable",Operation=e.GetType().Name};}
   lock(gate){pending=false;if(disposed||version!=revision)return;result=value;next=clock()+TimeSpan.FromMilliseconds(500).Ticks;}
  }
  public void Dispose(){lock(gate){disposed=true;revision++;result=null;}if(refresher!=null)refresher.Dispose();}
 }
}

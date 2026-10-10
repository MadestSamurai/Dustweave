#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
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
    result.State="alive";result.Problem="";result.Operation="confirmed";return result;
   }catch(Exception e){result.Operation=e.GetType().Name;return result;}
   finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
  }
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
}

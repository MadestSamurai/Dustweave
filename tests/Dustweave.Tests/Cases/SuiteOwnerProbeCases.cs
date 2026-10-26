using System.Diagnostics;
using System.Runtime.InteropServices;
using BD2Daily;

internal static class SuiteOwnerProbeCases
{
    // Only this short-lived test child changes its own DACL. No game/host process is modified.
    public static async Task Child(string mode)
    {
        long start=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        string access=mode=="limited"?"0x00101000":"0x00100000";
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;"+access+";;;WD)",1,out var descriptor,out _))throw new Exception("Test descriptor failed");
        try {if(!SetKernelObjectSecurity(GetCurrentProcess(),4,descriptor))throw new Exception("Test DACL failed");}
        finally {LocalFree(descriptor);}
        Console.WriteLine(start);Console.Out.Flush();
        await Task.WhenAny(Console.In.ReadLineAsync(),Task.Delay(15000));
    }
    public static async Task Run(List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("suite-owner-probe: "+label);}
        Check(SuiteOwnerProbe.Read(0,0).Problem=="suite.owner-missing","missing identity is distinct from an exited process");
        Check(SuiteOwnerProbe.Read(int.MaxValue,1).Problem=="suite.owner-exited","invalid PID is confirmed absent");
        await Monitor(cases);
        foreach(string mode in new[]{"limited","denied"})
        {
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--suite-owner-child");start.ArgumentList.Add(mode);
            using var child=Process.Start(start)!;
            try
            {
                string? ready=await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
                if(!long.TryParse(ready,out long ticks))throw new Exception("Synthetic owner did not become ready: "+await child.StandardError.ReadToEndAsync());
                // Elevated CI runners may have SeDebugPrivilege enabled and bypass the
                // child's DACL. Restrict this synchronous query, not the user's session.
                var check=ReadWithoutPrivileges(child.Id,ticks);
                if(mode=="limited")
                {
                    Check(check.State=="alive"&&check.Problem==""&&check.ObservedStartTicks==ticks,"limited query confirms living owner without full process access");
                    Check(ReadWithoutPrivileges(child.Id,ticks+1).Problem=="suite.owner-replaced","same PID with a different start time cannot own automation");
                    Check(check.Operation=="confirmed"&&check.CheckedUtcTicks>0&&check.NativeError==0,"successful proof includes query result and timestamp");
                }
                else Check(check.State=="unreadable"&&check.Problem=="suite.owner-unreadable"&&check.NativeError==5,"access denied is never reported as owner exit (state="+check.State+", operation="+check.Operation+", nativeError="+check.NativeError+")");
                using var retained=new SuiteOwnerMonitor();
                var until=DateTime.UtcNow.AddSeconds(3);
                while(retained.Read(child.Id,ticks).State=="checking"&&DateTime.UtcNow<until)await Task.Delay(10);
                if(mode=="limited")Check(retained.Read(child.Id,ticks).State=="alive","native monitor acquires the live child's exact lifetime");
                await child.StandardInput.WriteLineAsync("stop");await child.StandardInput.FlushAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                Check(child.ExitCode==0,"synthetic child stops normally: "+mode);
                if(mode=="limited")Check(SuiteOwnerProbe.Read(child.Id,ticks).Problem=="suite.owner-exited","actual owner exit remains protected");
                if(mode=="limited")Check(retained.Read(child.Id,ticks).Problem=="suite.owner-exited","retained lifetime detects actual exit immediately without a background refresh");
            }
            finally {if(!child.HasExited){await child.StandardInput.WriteLineAsync("stop");await child.StandardInput.FlushAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}}
        }
    }
    private static async Task Monitor(List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("suite-owner-monitor: "+label);}
        long now=DateTime.UtcNow.Ticks;var jobs=new Queue<Action>();int calls=0;
        using(var monitor=new SuiteOwnerMonitor((pid,start)=>{calls++;return new(){ProcessId=pid,ExpectedStartTicks=start,ObservedStartTicks=start,CheckedUtcTicks=now,State="alive"};},()=>now,jobs.Enqueue))
        {
            Check(monitor.Read(10,20).State=="checking"&&calls==0,"first frame queues proof without querying the OS or declaring readiness");
            for(int i=0;i<1000;i++)monitor.Read(10,20);
            Check(jobs.Count==1,"slow process inspection cannot create an unbounded worker backlog");
            jobs.Dequeue()();
            Check(monitor.Read(10,20).State=="alive","completed proof permits the same process generation");
            now+=TimeSpan.FromMilliseconds(600).Ticks;monitor.Read(10,20);
            now+=TimeSpan.FromSeconds(4).Ticks;
            Check(monitor.Read(10,20).State=="stale"&&monitor.Read(10,20).Problem!="","blocked refresh expires authority instead of keeping automation alive indefinitely");
            Check(monitor.Read(11,21).State=="checking","changing owners never reuses a previous owner's proof");
            jobs.Dequeue()();
            Check(monitor.Read(11,21).State=="checking"&&jobs.Count==1,"late result from the old owner is discarded before scheduling the new owner");
            jobs.Dequeue()();Check(monitor.Read(11,21).ProcessId==11&&monitor.Read(11,21).State=="alive","new owner requires its own successful proof");
            now+=TimeSpan.FromSeconds(1).Ticks;monitor.Read(11,21);monitor.Dispose();jobs.Dequeue()();
            Check(monitor.Read(11,21).State=="unreadable"&&jobs.Count==0,"unloading a module prevents a late worker from reviving it");
        }
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var finished=new ManualResetEventSlim();int blockedCalls=0;
        using var blocked=new SuiteOwnerMonitor((pid,start)=>{
            Interlocked.Increment(ref blockedCalls);entered.Set();
            try{if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("fixture not released");return new(){ProcessId=pid,ExpectedStartTicks=start,CheckedUtcTicks=DateTime.UtcNow.Ticks,State="alive"};}
            finally{finished.Set();}
        },()=>DateTime.UtcNow.Ticks,a=>ThreadPool.QueueUserWorkItem(_=>a()));
        try
        {
            blocked.Read(10,20);Check(entered.Wait(TimeSpan.FromSeconds(2)),"blocking inspection fixture runs on its worker");
            var watch=Stopwatch.StartNew();for(int i=0;i<1000;i++)CheckPending(blocked.Read(10,20));watch.Stop();
            Check(watch.ElapsedMilliseconds<500&&blockedCalls==1,"1,000 frame reads remain nonblocking during a stalled native query ("+watch.ElapsedMilliseconds+" ms)");
        }
        finally {release.Set();await Task.Run(()=>finished.Wait(TimeSpan.FromSeconds(2)));}
        using(var monitor=new SuiteOwnerMonitor())
        using(var self=Process.GetCurrentProcess())
        {
            long start=self.StartTime.ToUniversalTime().Ticks;
            var until=DateTime.UtcNow.AddSeconds(3);
            while(monitor.Read(self.Id,start).State=="checking"&&DateTime.UtcNow<until)await Task.Delay(10);
            Check(monitor.Read(self.Id,start).State=="alive","initial owner proof succeeds before scheduling-stall replay");
            // Replay the reported 3-second stale-owner failure: neither game
            // frames nor the background refresher run until after that deadline.
            ((Timer)typeof(SuiteOwnerMonitor).GetField("refresher",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(monitor)!).Change(Timeout.Infinite,Timeout.Infinite);
            await Task.Delay(3300);
            var resumed=monitor.Read(self.Id,start);
            Check(resumed.State=="alive"&&DateTime.UtcNow.Ticks-resumed.CheckedUtcTicks<TimeSpan.FromSeconds(2).Ticks,
                "live owner survives both frame and background-worker stalls without relying on cached proof");
            var watch=Stopwatch.StartNew();for(int i=0;i<1000;i++)if(monitor.Read(self.Id,start).State!="alive")throw new Exception("live lifetime lost");watch.Stop();
            Check(watch.ElapsedMilliseconds<500,"1,000 exact lifetime checks remain nonblocking ("+watch.ElapsedMilliseconds+" ms)");
            var replaced=monitor.Read(self.Id,start+1);
            until=DateTime.UtcNow.AddSeconds(3);
            while(replaced.State=="checking"&&DateTime.UtcNow<until){await Task.Delay(10);replaced=monitor.Read(self.Id,start+1);}
            Check(replaced.Problem=="suite.owner-replaced","a changed start time invalidates the retained handle and requires fresh identity proof");
            monitor.Dispose();Check(monitor.Read(self.Id,start).State=="unreadable","disposed retained lifetime cannot revive automation");
        }
        static void CheckPending(SuiteOwnerObservation value){if(value.State!="checking"||value.Problem=="")throw new Exception("Blocked native query granted authority");}
    }
    static SuiteOwnerObservation ReadWithoutPrivileges(int pid,long ticks)
    {
        IntPtr token=IntPtr.Zero,restricted=IntPtr.Zero;bool impersonated=false;
        try
        {
            if(!OpenProcessToken(GetCurrentProcess(),0x000A,out token))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if(!CreateRestrictedToken(token,1,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out restricted))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if(!ImpersonateLoggedOnUser(restricted))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            impersonated=true;
            return SuiteOwnerProbe.Read(pid,ticks);
        }
        finally
        {
            bool reverted=!impersonated||RevertToSelf();
            if(restricted!=IntPtr.Zero)CloseHandle(restricted);
            if(token!=IntPtr.Zero)CloseHandle(token);
            if(!reverted)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
    [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool CreateRestrictedToken(IntPtr token,uint flags,uint disableCount,IntPtr disabled,uint privilegeCount,IntPtr deleted,uint restrictCount,IntPtr restricted,out IntPtr newToken);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool ImpersonateLoggedOnUser(IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool RevertToSelf();
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool SetKernelObjectSecurity(IntPtr handle,uint info,IntPtr descriptor);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);
}

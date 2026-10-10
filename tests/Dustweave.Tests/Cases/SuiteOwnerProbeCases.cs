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
                await child.StandardInput.WriteLineAsync("stop");await child.StandardInput.FlushAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                Check(child.ExitCode==0,"synthetic child stops normally: "+mode);
                if(mode=="limited")Check(SuiteOwnerProbe.Read(child.Id,ticks).Problem=="suite.owner-exited","actual owner exit remains protected");
            }
            finally {if(!child.HasExited){await child.StandardInput.WriteLineAsync("stop");await child.StandardInput.FlushAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}}
        }
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

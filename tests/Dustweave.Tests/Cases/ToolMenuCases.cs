using System.Diagnostics;
using Dustweave;

internal static class ToolMenuCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); cases.Add("tool-menu: " + name); }
        Check(DailyToolCatalog.All.Count == 12, "owned tools include the five-dungeon Soul challenge");
        Check(DailyToolCatalog.All.Select(t => t.Id).Distinct().Count() == DailyToolCatalog.All.Count, "unique tool identities");
        Check(DailyToolCatalog.Find("infinite-gacha") is {Repository:"Dustweave", AssemblyName:"BD2InfiniteGacha", MutexName:@"Local\BD2InfiniteGacha-v1"}, "owned gacha points to Dustweave without changing loader or window identity");
        Check(DailyToolCatalog.Search("连连看").Single().Id == "sichuan", "Chinese search");
        Check(DailyToolCatalog.Search(" SECRET ").Single().Id == "secret-vision", "case insensitive trimmed search");
        Check(!DailyToolCatalog.Search("not a tool").Any(), "empty search result");
        Check(DailyToolCatalog.Search("潘多拉").Single().Id == "pandora", "Pandora is discoverable in the host tools menu");
        Check(!DailyToolCatalog.All.Any(t => t.Id == "summoner"), "private tools excluded");
        foreach (var tool in DailyToolCatalog.All)
        {
            Check(DailyToolArguments.TryClassify(tool.Id, [], out var helper) && !helper, tool.Id + " normal launch requires interactive operation guard");
            Check(DailyToolArguments.TryClassify(tool.Id, ["--smoke", "output"], out helper) && helper, tool.Id + " offline UI entry recognized");
            Check(!DailyToolArguments.TryClassify(tool.Id, ["--smoke"], out _), tool.Id + " malformed check cannot open normal UI");
            Check(!DailyToolArguments.TryClassify(tool.Id, ["--unknown", "output"], out _), tool.Id + " unknown check rejected");
        }
        Check(!DailyToolArguments.TryClassify("equipment", ["--identity", "output"], out _), "equipment has no identity command");
        Check(!DailyToolArguments.TryClassify("fiend-hunter", ["--runtime-check", "output"], out _), "foreign diagnostic cannot bypass control");
        Check(!DailyToolArguments.TryClassify("sichuan", ["--connection", "self-test"], out _), "equipment helper not routed to other tools");
        Check(DailyToolArguments.TryClassify("equipment", ["--refine"], out var needsHelper) && !needsHelper, "refine opens interactive tool with operation guard");
        Check(DailyToolArguments.TryClassify("equipment", ["--connection", "self-test"], out needsHelper) && needsHelper, "equipment original connection sub-entry works");
        bool rejected = false; try { DailyToolCatalog.Find("../unknown"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "unknown tool does not launch");
        string directory = Path.Combine(root, "tools-isolated");
        using (DailyToolControl.Acquire(directory))
        {
            rejected = false; try { using var second = DailyToolControl.Acquire(directory); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "daily and tool cannot own control together");
        }
        using (DailyToolControl.Acquire(directory)) Check(true, "normal release allows next operation");
        using var process = Process.GetCurrentProcess();
        var executable = process.MainModule!.FileName!;
        var manager = new DailyToolSession(directory, executable);
        string statePath = Path.Combine(directory, "tools", "window.json");
        Check(manager.Current == null, "missing journal stays idle");
        var state = new DailyToolProcessState("sichuan", process.Id, process.StartTime.ToUniversalTime().Ticks, executable);
        DailyJson.Write(statePath, state);
        Check(manager.Current == state, "reopening menu recognizes original process");
        var foreignScope = new DailyToolSession(directory, executable, containsProcess: _ => false);
        Check(foreignScope.Current == null, "a copied window journal cannot select another sandbox's tool");
        var unknownScope = new DailyToolSession(directory, executable, containsProcess: _ => throw new IOException("scope unavailable"));
        Check(unknownScope.Current == null, "unknown window scope cannot select a process");
        Check(manager.Current == state, "foreign scope lookup leaves original window journal intact");
        DailyJson.Write(statePath, state with { StartTicks = state.StartTicks + 1 });
        Check(manager.Current == null, "reused process id rejected");
        DailyJson.Write(statePath, state with { Executable = executable + ".other" });
        Check(manager.Current == null, "foreign executable rejected");
        DailyJson.Write(statePath, state with { ToolId = "unknown" });
        Check(manager.Current == null, "foreign tool rejected");
        File.WriteAllText(statePath, "{broken");
        Check(manager.Current == null, "corrupt journal cannot target a process");
        File.Delete(statePath);
        CheckHostedProcessSelection(Check);
        CheckRestrictedProcessPath(Check);
    }

    private static void CheckRestrictedProcessPath(Action<bool,string> check)
    {
        // Deny module enumeration on a test-owned child while allowing limited
        // metadata queries. Reproduces the failing access boundary without UAC
        // prompts, game access, or changing this process's permissions.
        string[] keys=["BD2_DAILY_HOSTED_TOOL","BD2_DAILY_GAME_PID","BD2_DAILY_GAME_START"];
        var saved=keys.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("--watchdog-child");
        using var child=Process.Start(start)!;
        byte[] original=new byte[65536];bool changed=false;
        try{
            var handle=child.SafeHandle;
            if(!GetKernelObjectSecurity(handle,4,original,original.Length,out _))throw new System.ComponentModel.Win32Exception();
            var descriptor=new System.Security.AccessControl.RawSecurityDescriptor(original,0);
            var sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            descriptor.DiscretionaryAcl!.InsertAce(0,new System.Security.AccessControl.CommonAce(System.Security.AccessControl.AceFlags.None,System.Security.AccessControl.AceQualifier.AccessDenied,0x410,sid,false,null));
            byte[] restricted=new byte[descriptor.BinaryLength];descriptor.GetBinaryForm(restricted,0);
            if(!SetKernelObjectSecurity(handle,4,restricted))throw new System.ComponentModel.Win32Exception();changed=true;
            Environment.SetEnvironmentVariable(keys[0],"equipment");
            Environment.SetEnvironmentVariable(keys[1],child.Id.ToString());
            Environment.SetEnvironmentVariable(keys[2],child.StartTime.ToUniversalTime().Ticks.ToString());
            // CI may hold SeDebugPrivilege, which bypasses a process DACL. Remove
            // privileges from this thread's temporary token, never from the runner.
            using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();
            if(!CreateRestrictedToken(identity.AccessToken,1,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out var restrictedToken))throw new System.ComponentModel.Win32Exception();
            using(restrictedToken)System.Security.Principal.WindowsIdentity.RunImpersonated(restrictedToken,()=>{
            using var selected=BD2.LocalIpc.HostedConnection.FindGame(_=>true,"ambiguous")!;
            bool denied=false;try{_=selected.MainModule!.FileName;}catch(System.ComponentModel.Win32Exception e)when(e.NativeErrorCode==5){denied=true;}
            check(denied,"fixture reproduces module-enumeration access denied on the selected process");
            check(string.Equals(BD2.LocalIpc.HostedConnection.ExecutablePath(selected),Environment.ProcessPath,StringComparison.OrdinalIgnoreCase),"hosted tool resolves exact executable without module or memory access");
            Environment.SetEnvironmentVariable(keys[2],(child.StartTime.ToUniversalTime().Ticks+1).ToString());
            bool rejected=false;try{_=BD2.LocalIpc.HostedConnection.ExecutablePath(selected);}catch(InvalidOperationException){rejected=true;}
            check(rejected,"limited-rights path query still rejects changed hosted process generation");
            Environment.SetEnvironmentVariable(keys[0],null);
            check(BD2.LocalIpc.HostedConnection.ExecutablePath(selected).Length>0,"standalone path query works with limited rights independently of stale host environment");
            });
        }
        finally{
            if(changed&&!child.HasExited)SetKernelObjectSecurity(child.SafeHandle,4,original);
            foreach(var key in keys)Environment.SetEnvironmentVariable(key,saved[key]);
            if(!child.HasExited)child.Kill();child.WaitForExit(5000);
        }
        var copies=Directory.GetFiles(Path.Combine(TestPaths.SourceRoot,"src"),"HostedConnection.cs",SearchOption.AllDirectories);
        check(copies.Length==9&&copies.Select(File.ReadAllText).Distinct().Count()==1,"all nine integrated tool connection helpers carry the same limited-rights fix");
    }
    [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)]
    static extern bool GetKernelObjectSecurity(Microsoft.Win32.SafeHandles.SafeProcessHandle handle,uint info,byte[] descriptor,int length,out int needed);
    [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)]
    static extern bool SetKernelObjectSecurity(Microsoft.Win32.SafeHandles.SafeProcessHandle handle,uint info,byte[] descriptor);

    [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)]
    static extern bool CreateRestrictedToken(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,uint flags,uint disableSids,IntPtr sids,uint deletePrivileges,IntPtr privileges,uint restrictSids,IntPtr restrictedSids,out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle restricted);

    private static void CheckHostedProcessSelection(Action<bool,string> check)
    {
        string[] keys = ["BD2_DAILY_HOSTED_TOOL", "BD2_DAILY_GAME_PID", "BD2_DAILY_GAME_START"];
        var saved = keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        using var self = Process.GetCurrentProcess();
        void Reject(Action action, string name)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, name);
        }
        Process? Find(Func<string,bool>? match = null) => BD2.LocalIpc.HostedConnection.FindGame(match ?? (_ => true), "multiple test processes");
        void Descriptor(string? pid, string? start)
        {
            Environment.SetEnvironmentVariable(keys[1], pid);
            Environment.SetEnvironmentVariable(keys[2], start);
        }
        string lifetime = self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Environment.SetEnvironmentVariable(keys[0], "territory");
            Descriptor(self.Id.ToString(), lifetime);
            // Every visible process matches. Only the exact parent-bound lifetime
            // may win; a global enumeration would report multiple instances here.
            using (var selected = Find()) check(selected?.Id == self.Id, "hosted tool uses exact parent-bound process despite other visible instances");
            using (var resumed = Find()) check(resumed?.Id == self.Id, "pause and reconnect retain the same hosted target");
            Reject(() => { using var ignored = Find(_ => false); }, "host descriptor cannot select an unrelated executable");
            Descriptor(self.Id.ToString(), (self.StartTime.ToUniversalTime().Ticks + 1).ToString());
            Reject(() => { using var ignored = Find(); }, "stale process lifetime never falls back to a different game");
            foreach (var pid in new[] { null, "bad", "0", "-1", int.MaxValue.ToString() })
            {
                Descriptor(pid, lifetime);
                Reject(() => { using var ignored = Find(); }, "missing or invalid hosted process rejected: " + (pid ?? "missing"));
            }
            foreach (var start in new[] { null, "bad", "0", "-1" })
            {
                Descriptor(self.Id.ToString(), start);
                Reject(() => { using var ignored = Find(); }, "invalid hosted lifetime rejected: " + (start ?? "missing"));
            }
            Environment.SetEnvironmentVariable(keys[0], null);
            using (var missing = Find(_ => false)) check(missing == null, "standalone missing game remains nullable even with a stale host descriptor");
            Reject(() => { using var ignored = Find(); }, "standalone ambiguous process selection remains blocked");
        }
        finally { foreach (var key in keys) Environment.SetEnvironmentVariable(key, saved[key]); }
    }
}

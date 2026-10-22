using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dustweave.Accounts;

static class SandboxScopeCases
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int NativeQuery(nint pid, StringBuilder box, nint image, nint sid, nint session);
    public static Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); cases.Add(name); }
        void Unavailable(Action action, string name)
        {
            try { action(); }
            catch (SandboxScopeException error) when (error.Message == "sandbox.unavailable") { cases.Add(name); return; }
            throw new InvalidOperationException(name);
        }
        const int host = 1, ordinaryGame = 2, boxedGame = 3;
        int queries = 0, inspections = 0;
        bool libraryLoaded = false;
        var failures = new Exception[] {
            new IOException("Sandboxie process query failed (0xC0000080)."),
            new DllNotFoundException("missing installation"),
            new EntryPointNotFoundException("incompatible SbieDll"),
            new BadImageFormatException("damaged/wrong architecture SbieDll"),
            new UnauthorizedAccessException("unreadable library"),
            new Win32Exception(5)
        };
        foreach (var failure in failures)
        {
            queries = 0; inspections = 0; libraryLoaded = false;
            var scope = new SandboxScopeResolver(host, pid => {
                inspections++; return pid == host ? libraryLoaded : pid == boxedGame;
            }, pid => { queries++; libraryLoaded = true; throw failure; });
            Check(scope.CurrentBox == "" && scope.BoxOf(host) == "" && queries == 0 && inspections == 1,
                "ordinary startup never loads installed Sandboxie: " + failure.GetType().Name);
            Check(scope.BoxOf(ordinaryGame) == "", "ordinary game survives optional component failure: " + failure.GetType().Name);
            Check(scope.CurrentBox == "", "loading query library does not reclassify host: " + failure.GetType().Name);
            Unavailable(() => scope.BoxOf(boxedGame), "unknown injected game cannot become host game: " + failure.GetType().Name);
        }
        bool ready = false;
        var repaired = new SandboxScopeResolver(host, pid => pid == boxedGame, pid => {
            if (!ready) throw new IOException("0xC0000080");
            return pid == boxedGame ? "Dustweave_test" : "";
        });
        Check(repaired.BoxOf(ordinaryGame) == "", "disabled driver still permits ordinary game");
        Unavailable(() => repaired.BoxOf(boxedGame), "disabled driver blocks unverified isolation only");
        ready = true;
        Check(repaired.BoxOf(boxedGame) == "Dustweave_test" && repaired.BoxOf(ordinaryGame) == "",
            "repair takes effect on next query without restarting ordinary host");
        var isolated = new SandboxScopeResolver(host, _ => true, pid => pid == ordinaryGame ? "" : pid == host ? "box_A" : "box_B");
        Check(isolated.CurrentBox == "box_A" && isolated.BoxOf(boxedGame) == "box_B" && isolated.BoxOf(ordinaryGame) == "",
            "actual injected host preserves same-box, foreign-box and ordinary separation");
        var brokenIsolated = new SandboxScopeResolver(host, _ => true, _ => throw new SandboxScopeException(new IOException("0xC0000080")));
        Unavailable(() => _ = brokenIsolated.CurrentBox, "unverified injected host never falls back to host registry");
        var unreadable = new SandboxScopeResolver(host, pid => pid == host ? false : throw new Win32Exception(5),
            _ => throw new IOException("driver unavailable"));
        try { unreadable.BoxOf(ordinaryGame); throw new Exception("unreadable target accepted"); }
        catch (SandboxScopeAccessException e) { Check(e.ProcessId == ordinaryGame && e.Message == "connection.scope_permission", "access denial reaches permission recovery instead of blaming Sandboxie"); }
        long generation = 10;
        var verified = new VerifiedProcessScopes(_ => generation);
        verified.Remember(ordinaryGame, generation, "");
        Check(verified.Read(ordinaryGame) == "", "authenticated ordinary scope can be reused for the same process");
        generation++;
        Check(verified.Read(ordinaryGame) == null, "PID reuse invalidates elevated scope proof");
        verified.Remember(boxedGame, generation, "另一账号");
        Check(verified.Read(boxedGame) == "另一账号", "verified foreign sandbox is never converted to ordinary host");
        try { verified.Remember(ordinaryGame, generation - 1, ""); throw new Exception("stale proof accepted"); }
        catch (IOException) { cases.Add("stale elevation reply is rejected before caching"); }
        var exited = new SandboxScopeResolver(host, pid => pid == host ? false : throw new ArgumentException("exited"),
            _ => throw new IOException("driver unavailable"));
        try { exited.BoxOf(ordinaryGame); throw new Exception("exited process accepted"); }
        catch (ArgumentException) { cases.Add("exited process remains distinguishable from an unknown live scope"); }
        try { repaired.BoxOf(0); throw new Exception("invalid PID accepted"); }
        catch (ArgumentOutOfRangeException) { cases.Add("invalid process identity rejected"); }

        // This is the real native startup entry, not just the resolver model. No
        // game is started, connected, modified or enumerated by this check.
        using var self = Process.GetCurrentProcess();
        bool WasLoaded() { self.Refresh(); return self.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("SbieDll.dll", StringComparison.OrdinalIgnoreCase)); }
        bool before = WasLoaded();
        string actual = SandboxProcessScope.CurrentBox;
        Check(before || actual.Length == 0 && !WasLoaded(), "real ordinary process scope does not load optional native library");
        Check(SandboxDriverScope.NormalizeOpenStatus(unchecked((int)0xC0000034)) == SandboxDriverScope.Unavailable
            && SandboxDriverScope.NormalizeOpenStatus(unchecked((int)0xC000000E)) == SandboxDriverScope.Unavailable
            && SandboxDriverScope.NormalizeOpenStatus(unchecked((int)0xC0000022)) == unchecked((int)0xC0000022),
            "driver absence and access denial remain distinct");
        string unicodeBox = "测试_Sandbox";
        var bufferResult = SandboxDriverScope.QueryBuffer(1234, arguments => {
            Check(arguments.Length == 8 && arguments[0] == 0x12340007 && arguments[1] == 1234 && arguments.Skip(3).All(n => n == 0),
                "native query contains only the process-scope read command and expected PID");
            nint header = (nint)arguments[2], buffer = Marshal.ReadIntPtr(header, 8);
            Check(Marshal.ReadInt16(header, 2) == 68, "native Unicode response has the official 34-character capacity");
            byte[] encoded = Encoding.Unicode.GetBytes(unicodeBox + "\0");
            Marshal.Copy(encoded, 0, buffer, encoded.Length);
            Marshal.WriteInt16(header, (short)(encoded.Length - 2));
            return 0;
        });
        Check(bufferResult == (0, unicodeBox), "native box response preserves Unicode and excludes terminator");
        Check(SandboxDriverScope.QueryBuffer(1234, _ => SandboxDriverScope.NotSandboxed) == (SandboxDriverScope.NotSandboxed, ""),
            "native not-sandboxed response never reads an unfilled buffer");
        foreach (short invalidLength in new short[] { 1, 70 })
        {
            try { SandboxDriverScope.QueryBuffer(1234, arguments => { Marshal.WriteInt16((nint)arguments[2], invalidLength); return 0; }); }
            catch (IOException) { cases.Add("invalid native string length rejected: " + invalidLength); continue; }
            throw new Exception("Malformed buffer accepted.");
        }
        if (!before)
        {
            var native = SandboxDriverScope.Query(Environment.ProcessId);
            Check(native.Status != 0 && native.Box == "" && !WasLoaded(), "read-only native query never loads Sandboxie into an ordinary tool");
            // Optional live OS-level comparison against the installed official API.
            // Only self and Sandboxie's existing service processes are inspected;
            // no sandbox is created and no game is touched.
            if (native.Status == SandboxDriverScope.NotSandboxed && new SandboxInstallation().Read() is { Error: null } installation)
            {
                nint apiLibrary = NativeLibrary.Load(Path.Combine(installation.Directory, "SbieDll.dll"));
                try
                {
                    var official = Marshal.GetDelegateForFunctionPointer<NativeQuery>(NativeLibrary.GetExport(apiLibrary, "SbieApi_QueryProcess"));
                    var candidates = Process.GetProcessesByName("SbieSvc").Append(Process.GetCurrentProcess()).ToArray();
                    int compared = 0, isolatedCount = 0;
                    try
                    {
                        foreach (var process in candidates)
                        {
                            var name = new StringBuilder(34);
                            int expectedStatus = official(process.Id, name, 0, 0, 0);
                            var result = SandboxDriverScope.Query(process.Id);
                            Check(result.Status == expectedStatus && result.Box == name.ToString(), "native process query agrees with official API " + (++compared));
                            if (result.Box.Length != 0) isolatedCount++;
                        }
                    }
                    finally { foreach (var process in candidates) process.Dispose(); }
                    Check(SandboxProcessScope.CurrentBox == "", "ordinary host stays ordinary after explicit optional configuration library load");
                    Dustweave.DailyJson.Write(Path.Combine(output, "sandbox-native-comparison.json"), new { compared, isolatedCount, realGameTouched = false, configurationChanged = false });
                }
                finally { NativeLibrary.Free(apiLibrary); }
            }
        }
        return Task.CompletedTask;
    }
}

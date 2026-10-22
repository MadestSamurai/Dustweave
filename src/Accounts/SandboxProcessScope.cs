using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Dustweave.Accounts;

// An ordinary host does not load the optional Sandboxie installation to start.
// Box names still come from the driver, never from a child-supplied environment.
public static class SandboxProcessScope
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int QueryProcess(nint pid, StringBuilder box, nint image, nint sid, nint session);
    private static readonly SandboxScopeResolver scope = new(Environment.ProcessId, HasSandboxModule, QueryBox);
    private static readonly VerifiedProcessScopes verified = new(pid => { using var process = Process.GetProcessById(pid); return process.StartTime.ToUniversalTime().Ticks; });
    internal static void RememberVerified(int pid, long started, string box) => verified.Remember(pid, started, box);
    public static string CurrentBox => scope.CurrentBox;
    public static string BoxOf(int pid)
    {
        if (verified.Read(pid) is { } box) return box;
        return scope.BoxOf(pid);
    }
    // Only explicit isolation operations require a working installation/driver.
    // Do not use this gate for normal startup, account reads or ordinary games.
    public static void RequireAvailable()
    {
        _ = CurrentBox;
        _ = QueryBox(Environment.ProcessId);
    }
    private static string QueryBox(int pid)
    {
        try
        {
            // An injected process must use its already loaded Sandboxie API.
            // Ordinary hosts use the read-only driver protocol and never load
            // an optional, possibly damaged third-party DLL into normal tools.
            nint library = GetModuleHandle("SbieDll.dll");
            string box;
            int status;
            if (library != 0)
            {
                var api = Marshal.GetDelegateForFunctionPointer<QueryProcess>(NativeLibrary.GetExport(library, "SbieApi_QueryProcess"));
                var name = new StringBuilder(34);
                status = api(pid, name, 0, 0, 0);
                box = name.ToString();
            }
            else (status, box) = SandboxDriverScope.Query(pid);
            if (status == unchecked((int)0xC000000B)) return ""; // STATUS_INVALID_CID: not sandboxed
            if (status != 0) throw new IOException($"Sandboxie process query failed (0x{status:X8}).");
            if (box.Length is < 1 or > 32 || box.Any(char.IsControl))
                throw new IOException("Sandboxie returned an invalid process scope.");
            return box;
        }
        catch (Exception error) when (SandboxScopeResolver.IsQueryFailure(error))
        { throw new SandboxScopeException(error); }
    }
    public static bool Contains(int pid) => string.Equals(CurrentBox, BoxOf(pid), StringComparison.OrdinalIgnoreCase);
    public static Process[] Find(string name)
    {
        var found = new List<Process>();
        string box = CurrentBox;
        SandboxScopeException? unavailable = null;
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                bool retained = false;
                try { if (string.Equals(box, BoxOf(p.Id), StringComparison.OrdinalIgnoreCase)) { found.Add(p); retained = true; } }
                // An unreadable foreign sandbox must not hide a verified ordinary game.
                // If no eligible game exists, retain the actionable error instead of
                // reporting 'game not running' and accidentally launching another one.
                catch (SandboxScopeException error) { unavailable ??= error; }
                catch (ArgumentException) { } // Exited between enumeration and inspection.
                catch (InvalidOperationException) when (p.HasExited) { }
                finally { if (!retained) p.Dispose(); }
            }
            if (found.Count == 0 && unavailable != null) throw unavailable;
            return found.ToArray();
        }
        catch { foreach (var p in found) p.Dispose(); throw; }
    }
    public static void Require(string expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(CurrentBox, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前进程不在指定的隔离实例内，未修改账号或启动游戏。");
    }
    private static bool HasSandboxModule(int pid)
    {
        if (pid == Environment.ProcessId) return GetModuleHandle("SbieDll.dll") != 0;
        // Used only when the optional driver query failed. An inaccessible or
        // injected process is never silently classified as an ordinary game.
        using var process = Process.GetProcessById(pid);
        var modules = process.Modules;
        if (modules.Count == 0) throw new IOException("Process modules are not ready for scope inspection.");
        foreach (ProcessModule module in modules)
            if (module.ModuleName.Equals("SbieDll.dll", StringComparison.OrdinalIgnoreCase)) return true;
        if (process.HasExited) throw new ArgumentException("Process exited during scope inspection.", nameof(pid));
        return false;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
}

internal class SandboxScopeException : IOException
{
    internal SandboxScopeException(Exception inner) : this("sandbox.unavailable", inner) { }
    protected SandboxScopeException(string message, Exception inner) : base(message, inner) { }
}
internal sealed class SandboxScopeAccessException(int pid, Exception inner) : SandboxScopeException("connection.scope_permission", inner)
{
    internal int ProcessId { get; } = pid;
}

// Only a reply from our authenticated read-only elevation helper can populate
// this cache. A PID alone is never proof: recheck the exact process generation.
internal sealed class VerifiedProcessScopes(Func<int, long> started)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (long Started, string Box)> values = new();
    internal void Remember(int pid, long generation, string box)
    {
        if (pid <= 0 || generation <= 0 || box.Length > 32 || box.Any(char.IsControl) || started(pid) != generation)
            throw new IOException("connection.scope_changed");
        values[pid] = (generation, box);
    }
    internal string? Read(int pid)
    {
        if (!values.TryGetValue(pid, out var proof)) return null;
        try { if (started(pid) == proof.Started) return proof.Box; }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        values.TryRemove(pid, out _); return null;
    }
}

// Dependencies are explicit so disabled/broken Sandboxie can be tested without
// changing the user's driver, services, accounts or running games.
internal sealed class SandboxScopeResolver(int currentPid, Func<int, bool> hasModule, Func<int, string> query)
{
    // Injection precedes managed startup and scope cannot change within a process.
    // Resolve before any optional configuration/query library is loaded; remember
    // ordinary host identity even after the user attempts an isolation operation.
    private readonly Lazy<string> current = new(() => hasModule(currentPid) ? query(currentPid) : "");
    internal string CurrentBox => current.Value;
    internal string BoxOf(int pid)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        string ownBox = CurrentBox;
        if (pid == currentPid) return ownBox;
        try { return query(pid); }
        catch (Exception error) when (IsQueryFailure(error))
        {
            try { if (!hasModule(pid)) return ""; }
            catch (System.ComponentModel.Win32Exception inspection) when (inspection.NativeErrorCode == 5)
            { throw new SandboxScopeAccessException(pid, new AggregateException(error, inspection)); }
            catch (Exception inspection) when (inspection is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            { throw new SandboxScopeException(new AggregateException(error, inspection)); }
            throw new SandboxScopeException(error);
        }
    }
    internal static bool IsQueryFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Dustweave.Accounts;

// Process scope is obtained from Sandboxie's driver, never from an environment
// variable supplied by a child. A normal host only owns normal-host games.
public static class SandboxProcessScope
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int QueryProcess(nint pid, StringBuilder box, nint image, nint sid, nint session);
    private static readonly Lazy<QueryProcess?> query = new(Load);
    private static nint library;
    public static string CurrentBox => BoxOf(Environment.ProcessId);
    public static string BoxOf(int pid)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        var api = query.Value;
        if (api == null) return "";
        var name = new StringBuilder(34);
        int status = api(pid, name, 0, 0, 0);
        if (status == unchecked((int)0xC000000B)) return ""; // STATUS_INVALID_CID: not sandboxed
        if (status != 0) throw new IOException($"无法确认进程的沙箱归属（0x{status:X8}），已停止连接。");
        return name.ToString();
    }
    public static bool Contains(int pid) => string.Equals(CurrentBox, BoxOf(pid), StringComparison.OrdinalIgnoreCase);
    public static Process[] Find(string name)
    {
        var found = new List<Process>();
        string box = CurrentBox;
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                bool retained = false;
                try { if (string.Equals(box, BoxOf(p.Id), StringComparison.OrdinalIgnoreCase)) { found.Add(p); retained = true; } }
                finally { if (!retained) p.Dispose(); }
            }
            return found.ToArray();
        }
        catch { foreach (var p in found) p.Dispose(); throw; }
    }
    public static void Require(string expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(CurrentBox, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前进程不在指定的隔离实例内，未修改账号或启动游戏。");
    }
    private static QueryProcess? Load()
    {
        library = GetModuleHandle("SbieDll.dll");
        if (library == 0)
        {
            foreach (string folder in new[] { "Sandboxie-Plus", "Sandboxie" })
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), folder, "SbieDll.dll");
                if (!File.Exists(path)) continue;
                // Dependencies are resolved beside the installed library or in System32.
                library = LoadLibraryEx(path, 0, 0x100 | 0x800);
                if (library == 0) throw new IOException("已安装 Sandboxie，但无法读取隔离状态，请检查它的服务是否运行。");
                break;
            }
        }
        return library == 0 ? null : Marshal.GetDelegateForFunctionPointer<QueryProcess>(NativeLibrary.GetExport(library, "SbieApi_QueryProcess"));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
}

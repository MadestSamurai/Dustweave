using System.Runtime.InteropServices;
using System.Text;

namespace Dustweave.Accounts;

// Read UTF-16 values directly. SbieIni's printf("%S") output can lose path
// characters before any stdout decoder gets to see them.
public static class SandboxConfiguration
{
    private const uint NoExpand = 0x20000000;
    private const uint NoTemplates = 0x10000000;
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal delegate int Query(string section, string setting, uint index, StringBuilder output, uint bytes);
    private static readonly Dictionary<string, Query> queries = new(StringComparer.OrdinalIgnoreCase);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Unicode)]
    private delegate int EnumBoxes(int index,StringBuilder name,[MarshalAs(UnmanagedType.U1)]bool allSessions);
    private static readonly Dictionary<string, EnumBoxes> enumerators = new(StringComparer.OrdinalIgnoreCase);
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    private delegate int EnumProcesses(string box,[MarshalAs(UnmanagedType.U1)]bool allSessions,uint session,[Out]int[] pids,nint count);
    private static readonly Dictionary<string,EnumProcesses> processEnumerators=new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();

    public static int[] Processes(string installation,string box)
    {
        if(string.IsNullOrEmpty(box)||box.Length>32||box.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Invalid sandbox name");
        _=SandboxProcessScope.CurrentBox;
        string path=Path.GetFullPath(Path.Combine(installation,"SbieDll.dll"));
        EnumProcesses enumerate;
        lock(gate) {
            if(!processEnumerators.TryGetValue(path,out enumerate!)) {
                var library=LoadLibraryEx(path,0,0x100|0x800);
                if(library==0)throw new IOException("sandbox.manage.process_unknown");
                enumerate=Marshal.GetDelegateForFunctionPointer<EnumProcesses>(NativeLibrary.GetExport(library,"SbieApi_EnumProcessEx"));
                processEnumerators.Add(path,enumerate);
            }
        }
        // Sandboxie's supported legacy buffer: count at [0], up to 511 PIDs.
        // No Start.exe startup, plugin discovery, redirected pipes or wait timer.
        return ReadProcesses(buffer=>enumerate(box,true,uint.MaxValue,buffer,0));
    }
    internal static int[] ReadProcesses(Func<int[],int> query)
    {
        var buffer=new int[512];int status=query(buffer),count=buffer[0];
        if(status!=0||count<0||count>=511)throw new IOException("sandbox.manage.process_unknown");
        var pids=buffer.Skip(1).Take(count).ToArray();
        if(pids.Any(pid=>pid<=0)||pids.Distinct().Count()!=count)throw new IOException("sandbox.manage.process_unknown");
        return pids;
    }

    public static string[] Boxes(string installation)
    {
        // Capture native process scope before loading a query-only SbieDll into
        // the ordinary host. Installing/reading configuration is not isolation.
        _ = SandboxProcessScope.CurrentBox;
        string path=Path.GetFullPath(Path.Combine(installation,"SbieDll.dll"));
        EnumBoxes enumerate;
        lock(gate)
        {
            if(!enumerators.TryGetValue(path,out enumerate!))
            {
                var library=LoadLibraryEx(path,0,0x100|0x800);
                if(library==0)throw new IOException("无法读取 Sandboxie 配置，未修改隔离空间。");
                enumerate=Marshal.GetDelegateForFunctionPointer<EnumBoxes>(NativeLibrary.GetExport(library,"SbieApi_EnumBoxesEx"));
                enumerators.Add(path,enumerate);
            }
        }
        var names=new List<string>(); int index=-1;
        for(int count=0;count<4096;count++)
        {
            var name=new StringBuilder(34); int next=enumerate(index,name,true);
            if(next==-1)return names.ToArray();
            if(next<=index)throw new IOException("Sandboxie 配置条目超出读取范围，未修改隔离空间。");
            names.Add(name.ToString()); index=next;
        }
        throw new IOException("Sandboxie 配置条目超出读取范围，未修改隔离空间。");
    }

    public static string Read(string installation, string section, string setting, bool includeTemplates = false)
    {
        _ = SandboxProcessScope.CurrentBox;
        string path = Path.GetFullPath(Path.Combine(installation, "SbieDll.dll"));
        Query query;
        lock (gate)
        {
            if (!queries.TryGetValue(path, out query!))
            {
                var library = LoadLibraryEx(path, 0, 0x100 | 0x800);
                if (library == 0) throw new IOException($"无法读取 Sandboxie 配置（{Marshal.GetLastWin32Error()}），未修改隔离空间。");
                // Keep the library loaded while its delegate can be used.
                query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "SbieApi_QueryConf"));
                queries.Add(path, query);
            }
        }
        return ReadSetting(query, section, setting, includeTemplates);
    }

    internal static string ReadSetting(Query query, string section, string setting, bool includeTemplates = false)
    {
        // Box_InitPaths uses Conf_Get(box, "FileRootPath", 0): the first
        // effective entry wins, including template/global fallback. Enumerating
        // and joining roots would turn a valid override plus a global default
        // into one invalid path. Keep raw Unicode for managed absolute roots.
        bool singleRoot = setting.Equals("FileRootPath", StringComparison.OrdinalIgnoreCase);
        uint flags = NoExpand | (singleRoot || includeTemplates ? 0 : NoTemplates);
        var values = new List<string>();
        for (uint index = 0; index < 4096; index++)
        {
            var value = new StringBuilder(8192);
            // List settings retain the existing raw/no-template enumeration.
            // In particular, do not truncate shared-path permission checks.
            int status = query(section, setting, index | flags, value, 16384);
            if (status == unchecked((int)0xC000008B)) return string.Join('\n', values); // STATUS_RESOURCE_NAME_NOT_FOUND
            if (status != 0) throw new IOException($"无法读取 Sandboxie 配置（0x{status:X8}），未修改隔离空间。");
            if (singleRoot) return value.ToString();
            values.Add(value.ToString());
        }
        throw new IOException("Sandboxie 配置条目超出读取范围，未修改隔离空间。");
    }

    public static bool SameRoot(string actual, string expected)
    {
        if (!Path.IsPathFullyQualified(actual) || !Path.IsPathFullyQualified(expected)) return false;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
}

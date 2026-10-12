using System.Runtime.InteropServices;
using System.Text;

namespace Dustweave.Accounts;

// Read UTF-16 values directly. SbieIni's printf("%S") output can lose path
// characters before any stdout decoder gets to see them.
public static class SandboxConfiguration
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal delegate int Query(string section, string setting, uint index, StringBuilder output, uint bytes);
    private static readonly Dictionary<string, Query> queries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();

    public static string Read(string installation, string section, string setting)
    {
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
        return ReadValues(query, section, setting);
    }

    internal static string ReadValues(Query query, string section, string setting)
    {
        var values = new List<string>();
        for (uint index = 0; index < 4096; index++)
        {
            var value = new StringBuilder(8192);
            // Same raw-value/no-template semantics as SbieIni query.
            int status = query(section, setting, index | 0x30000000, value, 16384);
            if (status == unchecked((int)0xC000008B)) return string.Join('\n', values); // STATUS_RESOURCE_NAME_NOT_FOUND
            if (status != 0) throw new IOException($"无法读取 Sandboxie 配置（0x{status:X8}），未修改隔离空间。");
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

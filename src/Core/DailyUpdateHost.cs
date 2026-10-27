using System.Diagnostics;
using System.Reflection;
using Dustweave.Accounts;
namespace Dustweave;

// Shared by the application and the independent updater. These identities are
// deliberately identical to the existing account and automation ownership locks.
public static class DailyUpdateHost
{
    public static bool SelfUpdating => System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
        .Any(a => a.Key == "Dustweave.SelfUpdate" && a.Value == "true") == true;
    public static string InstallRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    public static string DataRoot => DailyStoragePath.ResolveRoot(Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant"));
    // Shared with cache maintenance; hold across downloads and the whole update/recovery transaction.
    public static IDisposable AcquireStorage(string root, bool exclusive = false)
    {
        root = DailyStoragePath.ResolveRoot(root);
        string path = Path.Combine(root, "cache-storage.lock");
        DailyStoragePath.RequireNoLinks(path);
        Directory.CreateDirectory(root);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
    }
    public static void RequireHost()
    {
        if (SandboxProcessScope.CurrentBox.Length != 0) throw new InvalidOperationException("updates.isolated_open");
    }
    public static bool HasIsolatedWindows()
    {
        RequireHost();
        var processes = Process.GetProcessesByName("Dustweave");
        try
        {
            foreach (var process in processes)
            {
                try { if (SandboxProcessScope.BoxOf(process.Id).Length != 0) return true; }
                catch (IOException) when (process.HasExited) { }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static IDisposable AcquireControl(string root)
    {
        string folder = Path.Combine(root, "tools"); Directory.CreateDirectory(folder);
        try { return new FileStream(Path.Combine(folder, "control.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new InvalidOperationException("其他自动化正在运行或等待结算，请先停止该功能；无需关闭窗口。", error); }
    }
}

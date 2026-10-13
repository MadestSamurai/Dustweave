using System.Diagnostics;
using Dustweave.Accounts;
namespace Dustweave;

// Shared by the application and the independent updater. These identities are
// deliberately identical to the existing account and automation ownership locks.
public static class DailyUpdateHost
{
    public static string InstallRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    public static string DataRoot => Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Dustweave.Bootstrap;

// Native AOT keeps this entry usable when the application's CLR, launcher DLL
// or business assemblies are absent. The application still runs in this process
// with the original Dustweave.exe path and all existing ownership identities.
internal static class Program
{
    internal static string InstallRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    internal static string DataRoot => Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
    internal static string Text(string cn, string tw, string en)
    {
        string name = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? name is "zh-TW" or "zh-HK" or "zh-MO" || name.Contains("Hant") ? tw : cn : en;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        bool interactive = args.Length == 0 || args.Length == 1 && args[0] is "--desktop-session" or "--repair";
        try
        {
            if (Recovery.TryRun(args)) return 0;
            if (args is ["--repair"]) return LaunchRepair();
            int result = RunApplication(args);
            if (result == 0 || !interactive) return result;
            return OfferRepair(new IOException(Text("主程序未能正常启动。", "主程式未能正常啟動。", "The application could not start.")), result);
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception error)
        {
            WriteFailure(error);
            return interactive ? OfferRepair(error, 1) : 1;
        }
    }
    private static int OfferRepair(Exception error, int code)
    {
        WriteFailure(error);
        string question = Text("织尘未能启动。是否检查并修复程序文件？\n账号、设置和插件会保留。",
            "織塵未能啟動。是否檢查並修復程式檔案？\n帳號、設定和外掛會保留。",
            "Dustweave could not start. Check and repair its program files?\nAccounts, settings and plugins will be preserved.");
        if (MessageBox(0, question, "Dustweave", 0x24 | 0x100) != 6) return code;
        try { return LaunchRepair(approved: true); }
        catch (OperationCanceledException) { return 0; }
        catch (Exception repairError)
        {
            WriteFailure(repairError);
            MessageBox(0, Text("无法启动修复。请使用官方完整包恢复程序文件。\n记录：", "無法啟動修復。請使用官方完整包恢復程式檔案。\n記錄：",
                "Repair could not start. Restore program files from the official package.\nLog: ") +
                Path.Combine(DataRoot, "startup", "bootstrap-error.txt"), "Dustweave", 0x10);
            return 1;
        }
    }
    private static int LaunchRepair(bool approved = false)
    {
        if (!approved && MessageBox(0, Text("检查并修复织尘？账号、设置和插件会保留。", "檢查並修復織塵？帳號、設定和外掛會保留。",
            "Check and repair Dustweave? Accounts, settings and plugins will be preserved."), "Dustweave", 0x24 | 0x100) != 6) return 0;
        var helper = RepairBootstrap.VerifiedHelper();
        var start = new ProcessStartInfo(helper.Path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = InstallRoot };
        start.ArgumentList.Add("--repair-approved"); start.ArgumentList.Add(InstallRoot);
        start.ArgumentList.Add(helper.Version); start.ArgumentList.Add(helper.Flavor);
        using var process = Process.Start(start) ?? throw new IOException("Repair helper did not start.");
        return 0;
    }
    private static void WriteFailure(Exception error)
    {
        try
        {
            string folder = Path.Combine(DataRoot, "startup"); Recovery.NoLinks(folder); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "bootstrap-error.txt"), DateTimeOffset.UtcNow + "\n" + error);
        }
        catch { /* Reporting cannot prevent the independent repair prompt. */ }
    }
    private static (string Library, string Root) Runtime()
    {
        string app = Path.Combine(InstallRoot, "app"), local = Path.Combine(app, "hostfxr.dll");
        // The compiled edition wins over leftover files from an overlaid ZIP.
        if (RepairBootstrap.IsPortable) return (local, app);
        var roots = new List<string?> { Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"), Environment.GetEnvironmentVariable("DOTNET_ROOT") };
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
            roots.Add(key?.GetValue("InstallLocation") as string);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        foreach (string root in roots.Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(root)) continue;
            string fxr = Path.Combine(root, "host", "fxr");
            if (!Directory.Exists(fxr)) continue;
            string? found = Directory.GetDirectories(fxr).Where(p => Version.TryParse(Path.GetFileName(p), out _))
                .OrderByDescending(p => Version.Parse(Path.GetFileName(p)))
                .Select(p => Path.Combine(p, "hostfxr.dll")).FirstOrDefault(File.Exists);
            if (found != null) return (found, root);
        }
        throw new FileNotFoundException("The .NET host could not be located.");
    }
    private static int RunApplication(string[] args)
    {
        RepairBootstrap.VerifyStartup();
        var runtime = Runtime();
        nint library = NativeLibrary.Load(runtime.Library);
        var entry = Marshal.GetDelegateForFunctionPointer<HostMain>(NativeLibrary.GetExport(library, "hostfxr_main_startupinfo"));
        var errors = new StringBuilder();
        ErrorWriter writer = message => { if (errors.Length < 16384) errors.AppendLine(Marshal.PtrToStringUni(message)); };
        var setWriter = Marshal.GetDelegateForFunctionPointer<SetErrorWriter>(NativeLibrary.GetExport(library, "hostfxr_set_error_writer"));
        nint previous = setWriter(Marshal.GetFunctionPointerForDelegate(writer));
        nint[] values = new[] { Environment.ProcessPath! }.Concat(args).Select(Marshal.StringToHGlobalUni).ToArray();
        nint argv = Marshal.AllocHGlobal(values.Length * IntPtr.Size); Marshal.Copy(values, 0, argv, values.Length);
        Environment.SetEnvironmentVariable("DUSTWEAVE_NATIVE_BOOTSTRAP", "2");
        try
        {
            int result = entry(values.Length, argv, Environment.ProcessPath!, runtime.Root, Path.Combine(InstallRoot, "app", "Dustweave.Launcher.dll"));
            if (result != 0 && errors.Length > 0) WriteFailure(new IOException(errors.ToString()));
            return result;
        }
        finally
        {
            setWriter(previous); GC.KeepAlive(writer);
            foreach (nint value in values) Marshal.FreeHGlobal(value); Marshal.FreeHGlobal(argv);
            // CoreCLR owns hostfxr for the process lifetime; do not unload it here.
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int HostMain(int argc, nint argv, string hostPath, string dotnetRoot, string appPath);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ErrorWriter(nint message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint SetErrorWriter(nint writer);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBox(nint window, string text, string title, uint type);
}

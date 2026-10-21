using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Launcher;

internal static class Program
{
    // No references to application assemblies: a partially replaced Core cannot
    // prevent recovery. The process path/name stays Dustweave.exe for all tools.
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (Recover(args)) return 0;
            return LoadApplication(args);
        }
        catch (Exception error)
        {
            string folder = Path.Combine(DataRoot, "startup");
            try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "launcher-error.txt"), DateTimeOffset.UtcNow + "\n" + error); } catch { }
            string message = "织尘未能启动。请解压完整软件包后重试；账号设置不会被删除。\nDustweave could not start. Extract the complete package and try again. Your saved accounts are retained.\n\n" + error.Message;
            if (args.Length == 0 || args[0] is "--desktop-session") MessageBox(0, message, "Dustweave", 0x10);
            return 1;
        }
    }
    private static string DataRoot => Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
    private static string InstallRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() ?? throw new InvalidDataException("Invalid recovery record");
    private static bool SamePath(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static void NoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update path");
    }
    private static bool Recover(string[] args)
    {
        string target = Environment.ProcessPath!, markerPath = Path.Combine(InstallRoot, ".dustweave-update.json");
        if (!File.Exists(markerPath)) return false;
        NoLinks(markerPath);
        using var markerDocument = JsonDocument.Parse(File.ReadAllBytes(markerPath)); var marker = markerDocument.RootElement;
        string jobPath = Text(marker, "JobPath"), nonce = Text(marker, "Nonce"), digest = Text(marker, "HelperSha256");
        if (nonce.Length != 32 || !nonce.All(Uri.IsHexDigit) || digest.Length != 64 || !digest.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid recovery record");
        string prefix = Path.GetFullPath(Path.Combine(DataRoot, "updates")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(jobPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(jobPath) != "job.json") throw new InvalidDataException("Invalid recovery path");
        NoLinks(jobPath);
        using var jobDocument = JsonDocument.Parse(File.ReadAllBytes(jobPath)); var job = jobDocument.RootElement;
        string directory = Text(job, "Directory"), version = Text(job.GetProperty("Release"), "Version"), flavor = Text(job.GetProperty("Asset"), "Flavor");
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$") || flavor is not ("Portable" or "Lite")) throw new InvalidDataException("Invalid recovery identity");
        string attempt = Path.Combine(DataRoot, "updates", version + "-" + flavor, "attempts", nonce);
        if (!SamePath(target, Text(job, "Target")) || Text(job, "Nonce") != nonce || !SamePath(directory, Path.Combine(DataRoot, "updates", version + "-" + flavor)) || !SamePath(jobPath, Path.Combine(attempt, "job.json"))) throw new InvalidDataException("Invalid recovery ownership");
        string journalPath = Path.Combine(attempt, "backup", "transaction.json"); NoLinks(journalPath);
        using var journalDocument = JsonDocument.Parse(File.ReadAllBytes(journalPath)); var journal = journalDocument.RootElement;
        if (!SamePath(Text(journal, "Target"), InstallRoot) || !SamePath(Text(journal, "Backup"), Path.Combine(attempt, "backup"))) throw new InvalidDataException("Invalid recovery journal ownership");
        string state = Text(journal, "State");
        if (state is "completed" or "rolled_back") { File.Delete(markerPath); return false; }
        if (args.Length == 2 && args[0] == "--updated" && args[1] == nonce && state == "awaiting_start") return false;
        string mutexName = @"Local\Dustweave-Updater-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(target).ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName); bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return true;
        try
        {
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"); NoLinks(helper);
            if (!Hash(helper).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery helper failed verification");
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
            start.ArgumentList.Add("--recover-update"); start.ArgumentList.Add(jobPath);
            using var process = Process.Start(start) ?? throw new IOException("Recovery helper did not start");
        }
        finally { mutex.ReleaseMutex(); }
        return true;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int LoadApplication(string[] args)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Dustweave.dll");
        var resolver = new AssemblyDependencyResolver(path);
        AssemblyLoadContext.Default.Resolving += (_, name) => resolver.ResolveAssemblyToPath(name) is { } file ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => resolver.ResolveUnmanagedDllToPath(name) is { } file ? NativeLibrary.Load(file) : 0;
        var application = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        // WPF generated component URIs explicitly name Dustweave; the process entry assembly remains the stable launcher.
        try { return (int)(application.EntryPoint?.Invoke(null, [args]) ?? throw new MissingMethodException("Missing application entry point")); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string title, uint type);
}

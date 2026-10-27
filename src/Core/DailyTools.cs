using System.Diagnostics;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>One shipped executable; helper modes still execute in separately bounded processes.</summary>
public static class DailyTools
{
    public const int Protocol = 1;
    public const string Marker = "utility-host.json";
    public static string ExecutablePath(string package)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        // The nested distribution has one launchable file at the installation root.
        if (Path.GetFileName(directory).Equals("app", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(directory, Marker)) && Directory.GetParent(directory) is { } parent)
            return Path.Combine(parent.FullName, DailyApplication.ExecutableName);
        return Path.Combine(directory, DailyApplication.ExecutableName);
    }
    private static readonly IReadOnlyDictionary<string, string> DevelopmentExecutables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["live"] = "connection/Dustweave.Connection.exe",
        ["minigame"] = "fishing-helper/Dustweave.ToolHost.exe",
        ["exporter"] = "trade-data/Dustweave.TableExporter.exe",
        ["diagnostics"] = "diagnostics/Dustweave.RuntimeCheck.exe"
    };
    public static bool Unified(string package)
    {
        string marker = Path.Combine(package, Marker);
        if (!File.Exists(marker))
            return false;
        var value = DailyJson.TryRead<JsonObject>(marker);
        if (value?["protocol"]?.GetValue<int>() != Protocol || !File.Exists(ExecutablePath(package)))
            throw new InvalidDataException("日常组件目录不完整，请重新完整解压新版工具。");
        return true;
    }
    public static bool Available(string package, string tool)
    {
        package = PackageDirectory(package);
        if (!DevelopmentExecutables.TryGetValue(tool, out var relative))
            throw new ArgumentException("Unknown daily utility: " + tool);
        return Unified(package) || File.Exists(Path.Combine(package, relative)) || File.Exists(Path.Combine(package, Path.GetFileName(relative)));
    }
    public static string PackageDirectory(string directory)
    {
        var current = Path.GetFullPath(directory);
        string? Resolve(string root)
        {
            // Installing over an older flat package can leave its marker and
            // specifications beside the executable. The current app payload
            // must win, including when called from an old connection directory.
            string payload = Path.Combine(root, "app");
            if (Unified(payload)) return payload;
            return Unified(root) ? root : null;
        }
        if (Resolve(current) is { } found)
            return found;
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(current));
        if (parent != null && Resolve(parent.FullName) is { } inherited)
            return inherited;
        return current;
    }
    public static ProcessStartInfo StartInfo(string package, string tool)
    {
        package = PackageDirectory(package);
        if (!DevelopmentExecutables.TryGetValue(tool, out var relative))
            throw new ArgumentException("Unknown daily utility: " + tool);
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = package, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Unified(package))
        {
            start.FileName = ExecutablePath(package);
            start.ArgumentList.Add("--utility");
            start.ArgumentList.Add(tool);
        }
        else
        {
            start.FileName = Path.Combine(package, relative);
            if (!File.Exists(start.FileName))
                start.FileName = Path.Combine(package, Path.GetFileName(relative));
            if (!File.Exists(start.FileName))
                throw new FileNotFoundException("缺少日常执行组件，请使用完整解压的新版工具。", start.FileName);
        }
        // Plugin changes apply after the host restarts. Preparation helpers must
        // use that host's pinned selection, not re-read a newer disk selection
        // and reject the daily module the still-running host just activated.
        var plugin = DailyPlugin.Current;
        start.Environment["DUSTWEAVE_PLUGIN"] = plugin.Available ? plugin.Root : "none";
        return start;
    }
    public static string EvidenceSpec(string directory, string name)
    {
        string package = PackageDirectory(directory);
        return Path.Combine(package, Unified(package) ? "connection/specs" : "specs", name);
    }
}

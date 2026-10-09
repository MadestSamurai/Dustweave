using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Dustweave.Desktop;

// This boundary deliberately uses only the runtime: it works before WPF/resources load.
// Never log command-line arguments, environment contents or account/session data.
internal static class StartupDiagnostics
{
    private static bool enabled;
    private static string stage = "managed-entry";
    internal static void Begin() { enabled = true; Mark("managed-entry"); }
    internal static void Mark(string value)
    {
        stage = value;
        if (enabled) Record(null);
    }
    internal static string Fail(Exception error) => Record(error);

    private static string Record(Exception? error)
    {
        try
        {
            string root = Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
            string path = Save(root, stage, error);
            if (path.Length > 0) return path;
        }
        catch { }
        try { return Save(Path.Combine(Path.GetTempPath(), "Dustweave-startup"), stage, error); }
        catch { return ""; }
    }
    internal static string Save(string root, string stage, Exception? error)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, error == null ? "startup-state.json" : "startup-error.json");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow,
                processId = Environment.ProcessId,
                version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                stage,
                status = error == null ? "progress" : "failed",
                error = error?.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
            return path;
        }
        catch { return ""; } // Failure reporting must not become another startup failure.
        finally { try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}
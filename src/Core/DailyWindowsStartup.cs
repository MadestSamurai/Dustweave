using Microsoft.Win32;

namespace Dustweave;

/// <summary>Per-user startup registration. Tests supply an in-memory store.</summary>
public sealed class DailyWindowsStartup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly Func<string?> read;
    private readonly Action<string?> write;
    public DailyWindowsStartup() : this(
        () => { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("Dustweave") as string; },
        value => { using var key = Registry.CurrentUser.CreateSubKey(Key, true); if (value == null) key.DeleteValue("Dustweave", false); else key.SetValue("Dustweave", value, RegistryValueKind.String); }) { }
    public DailyWindowsStartup(Func<string?> read, Action<string?> write) { this.read = read; this.write = write; }
    public bool Enabled => !string.IsNullOrWhiteSpace(read());
    public void Set(bool enabled, string executable)
    {
        if (!enabled) { write(null); return; }
        if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(['"', '\r', '\n']) >= 0 ||
            !Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("software.startup_path_invalid");
        write("\"" + Path.GetFullPath(executable) + "\"");
    }
    // An existing opt-in follows a moved/replaced portable installation; never enable a new one.
    public bool RefreshEnabledPath(string executable)
    {
        string? previous = read();
        if (string.IsNullOrWhiteSpace(previous)) return false;
        string expected = "\"" + Path.GetFullPath(executable) + "\"";
        if (string.Equals(previous, expected, StringComparison.OrdinalIgnoreCase)) return false;
        Set(true, executable);
        if (!string.Equals(read(), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("software.startup_path_invalid");
        return true;
    }
}

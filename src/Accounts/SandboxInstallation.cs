using System.Text.Json;
using Microsoft.Win32;

namespace Dustweave.Accounts;

public sealed record SandboxInstallationState(string Directory, bool IsManual, string? Error = null);

/// <summary>Optional installation used only by explicit sandbox launch/configuration operations.</summary>
public sealed class SandboxInstallation
{
    public const string DownloadUrl = "https://sandboxie-plus.com/downloads/";
    public const string Missing = "sandbox_path.missing";
    public const string Invalid = "sandbox_path.invalid";
    public const string Unreadable = "sandbox_path.unreadable";
    private sealed record Preference(int Schema, string? Directory);
    private readonly string file;
    private readonly Func<string?> detect;

    public SandboxInstallation(string? directory = null) : this(directory, DetectAutomatic) { }
    internal SandboxInstallation(string? directory, Func<string?> detect)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SessionConstants.VaultDirectoryName), "sandbox-installation.json");
        this.detect = detect;
    }
    public SandboxInstallationState Read()
    {
        string? selected = null;
        try
        {
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 65536) return new("", true, Unreadable);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var preference = JsonSerializer.Deserialize<Preference>(stream);
                if (preference?.Schema != 1 || preference.Directory is "") return new("", true, Unreadable);
                selected = preference.Directory;
            }
            string? directory = selected ?? detect();
            if (directory == null) return new("", false, Missing);
            try { return new(Validate(directory), selected != null); }
            catch (SessionManagerException error) { return new(directory, selected != null, error.Message); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.SecurityException)
        { return new(selected ?? "", true, Unreadable); }
    }
    public string? ResolveOptional()
    {
        var state = Read();
        if (state.Error == Missing && !state.IsManual) return null;
        if (state.Error != null) throw new SessionManagerException(state.Error);
        return state.Directory;
    }
    public void Select(string directory) => Save(new(1, Validate(directory)));
    public void UseAutomatic() => Save(new(1, null));
    private void Save(Preference value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, file, true); break; }
                catch (Exception error) when (attempt < 5 && error is IOException or UnauthorizedAccessException && (error.HResult & 0xffff) is 5 or 32 or 33)
                { Thread.Sleep(20 << attempt); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string Validate(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new SessionManagerException(Invalid);
        string full = Path.GetFullPath(directory);
        if (!new[] { "Start.exe", "SbieIni.exe", "SbieDll.dll" }.All(name => File.Exists(Path.Combine(full, name))))
            throw new SessionManagerException(Invalid);
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static string? DetectAutomatic()
    {
        foreach (string candidate in Candidates().Distinct(StringComparer.OrdinalIgnoreCase))
            try { return Validate(candidate); }
            catch (Exception error) when (error is SessionManagerException or ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }
    private static IEnumerable<string> Candidates()
    {
        // Official installers record the actual path, including custom drives.
        var candidates = new List<string>();
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SbieSvc");
            if (service?.GetValue("ImagePath") is string command)
            {
                command = Environment.ExpandEnvironmentVariables(command.Trim());
                int executable = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                int end = command.StartsWith('"') ? command.IndexOf('"', 1) : executable >= 0 ? executable + 4 : -1;
                if (end > 0)
                {
                    string image = command.StartsWith('"') ? command[1..end] : command[..end];
                    if (Path.IsPathFullyQualified(image) && Path.GetDirectoryName(image) is string directory) candidates.Add(directory);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames().Where(n => n.Contains("Sandboxie", StringComparison.OrdinalIgnoreCase)) ?? [])
                {
                    using var key = uninstall!.OpenSubKey(name);
                    if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location)) candidates.Add(location.Trim('"'));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        foreach (string name in new[] { "Sandboxie-Plus", "Sandboxie" })
            candidates.Add(Path.Combine(Environment.GetFolderPath(folder), name));
        return candidates;
    }
}

using System.Text.Json;
using Microsoft.Win32;

namespace Dustweave.Accounts;

public sealed record GameInstallationState(string Executable, bool IsManual, string? Error = null);

/// <summary>One per-user installation choice, independent of accounts and the app's install directory.</summary>
public sealed class GameInstallation
{
    public const string Missing = "未找到游戏，请在日常设置的「游戏启动位置」中选择 BrownDust II.exe。";
    public const string Invalid = "请选择游戏本体 BrownDust II.exe，不是启动器或快捷方式。";
    public const string Incomplete = "游戏文件不存在或不完整，请重新选择 BrownDust II.exe，或先完成游戏更新。";
    public const string Unreadable = "游戏路径设置无法读取，请重新选择游戏，或恢复自动查找。";
    private sealed record Preference(int Schema, string? Executable);
    private readonly string file;
    private readonly Func<string?> detect;

    public GameInstallation(string? directory = null) : this(directory, DetectAutomatic) { }
    internal GameInstallation(string? directory, Func<string?> detect)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SessionConstants.VaultDirectoryName), "game-installation.json");
        this.detect = detect;
    }

    public GameInstallationState Read()
    {
        string? selected = null;
        try
        {
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 65536) return new("", true, Unreadable);
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var value = JsonSerializer.Deserialize<Preference>(input);
                if (value?.Schema != 1 || value.Executable is "") return new("", true, Unreadable);
                selected = value.Executable;
            }
            string? executable = selected ?? detect();
            if (executable == null) return new("", false, Missing);
            try { return new(Validate(executable), selected != null); }
            catch (SessionManagerException error) { return new(executable, selected != null, error.Message); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.SecurityException)
        {
            return new(selected ?? "", true, Unreadable);
        }
    }

    public string Resolve()
    {
        var state = Read();
        if (state.Error != null) throw new SessionManagerException(state.Error);
        return state.Executable;
    }

    public void Select(string executable) => Save(new(1, Validate(executable)));
    public void UseAutomatic() => Save(new(1, null));

    private void Save(Preference value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }
            // Readers and file scanners can briefly deny replacement on Windows.
            // Keep the old complete preference intact and retry only bounded sharing/access failures.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, file, overwrite: true); break; }
                catch (Exception error) when (attempt < 5 && IsTemporaryAccess(error))
                {
                    Thread.Sleep(20 << attempt);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool IsTemporaryAccess(Exception error) => OperatingSystem.IsWindows()
        && error is IOException or UnauthorizedAccessException
        && (error.HResult & 0xffff) is 5 or 32 or 33;

    private static string Validate(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetFileName(executable), "BrownDust II.exe", StringComparison.OrdinalIgnoreCase))
            throw new SessionManagerException(Invalid);
        string full;
        try { full = Path.GetFullPath(executable); }
        catch (ArgumentException) { throw new SessionManagerException(Invalid); }
        string managed = Path.Combine(Path.GetDirectoryName(full)!, "BrownDust II_Data", "Managed");
        if (!File.Exists(full) || !File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")) || !File.Exists(Path.Combine(managed, "mscorlib.dll")))
            throw new SessionManagerException(Incomplete);
        return full;
    }

    private static string? DetectAutomatic()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\NEOWIZ\Browndust2Starter\10000001", false);
            if (key?.GetValue("path") is string directory && key.GetValue("execute") is string name)
                return Validate(Path.Combine(directory, name));
        }
        catch (Exception error) when (error is SessionManagerException or ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        const string fallback = @"C:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II.exe";
        try { return Validate(fallback); }
        catch (SessionManagerException) { return null; }
    }
}

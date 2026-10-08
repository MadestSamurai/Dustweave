using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyInstalledPlugin(string Id, string Version, string Fingerprint, DateTimeOffset InstalledUtc);
public sealed record DailyPluginSelection(int Schema, string? Active, string? Previous, DailyInstalledPlugin[] Installed, bool OverrideLocal = true);
public sealed class DailyPluginPackage : IDisposable
{
    public DailyPluginInfo Info { get; }
    internal DailyPluginPackage(DailyPluginInfo info) => Info = info;
    public void Dispose() { try { Directory.Delete(Info.Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

/// <summary>Immutable payloads and an atomic selection. A running process pins DailyPlugin.Current.</summary>
public sealed class DailyPluginStore
{
    public const long MaxArchiveBytes = 256L * 1024 * 1024;
    private const long MaxExpandedBytes = 768L * 1024 * 1024;
    private readonly string root;
    public DailyPluginStore(string dataRoot) => root = Path.Combine(Path.GetFullPath(dataRoot), "extensions");
    private string SelectionPath => Path.Combine(root, "selection.json");
    public static bool HasSelection(string dataRoot)
    {
        try { return new DailyPluginStore(dataRoot).Read().OverrideLocal; }
        catch { return true; }
    }
    public DailyPluginSelection Read()
    {
        if (!File.Exists(SelectionPath)) return new(1, null, null, [], false);
        NoLinks(SelectionPath);
        var state = JsonSerializer.Deserialize<DailyPluginSelection>(File.ReadAllBytes(SelectionPath));
        if (state == null || state.Schema != 1 || state.Installed == null || state.Installed.Any(x => x == null || !DailyPlugin.ValidId(x.Id) || !Version.TryParse(x.Version, out _) || !ValidHash(x.Fingerprint)) ||
            state.Installed.Select(x => x.Fingerprint).Distinct().Count() != state.Installed.Length ||
            new[] { state.Active, state.Previous }.Any(x => x != null && !state.Installed.Any(p => p.Fingerprint == x)))
            throw new InvalidDataException("plugins.invalid_state");
        return state;
    }
    public string SelectedRoot()
    {
        // A broken selection disables plugins, never silently loads another version.
        try
        {
            var state = Read();
            if (state.Active is not { } hash) return "";
            var info = Inspect(state.Installed.Single(x => x.Fingerprint == hash));
            return info.Available ? info.Root : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return ""; }
    }
    public string PathFor(string hash)
    {
        if (!ValidHash(hash)) throw new InvalidDataException("plugins.invalid_state");
        return Path.Combine(root, "versions", hash);
    }
    public DailyPluginInfo Inspect(DailyInstalledPlugin plugin, string? hostVersion = null)
    {
        var info = DailyPlugin.Inspect(PathFor(plugin.Fingerprint), hostVersion);
        return info.Fingerprint == plugin.Fingerprint && info.Id == plugin.Id && info.Version == plugin.Version
            ? info : info with { Available = false, State = info.Available ? "invalid_hash" : info.State };
    }
    public DailyPluginPackage Prepare(string archivePath)
    {
        DailySandbox.RequireHost();
        if (new FileInfo(archivePath).Length > MaxArchiveBytes) throw new InvalidDataException("plugins.package_too_large");
        string staging = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
        NoLinks(staging); Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is 0 or > 4096) throw new InvalidDataException("plugins.invalid_package");
            long total = 0;
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string relative = entry.FullName;
                bool directory = relative.EndsWith('/');
                if (!ValidRelativePath(directory ? relative[..^1] : relative) || !entries.Add(relative.TrimEnd('/')) ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("plugins.invalid_package");
                if (directory) continue;
                total = checked(total + entry.Length);
                if (total > MaxExpandedBytes) throw new InvalidDataException("plugins.package_too_large");
                string target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920]; long written = 0; int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    written += count;
                    if (written > entry.Length) throw new InvalidDataException("plugins.invalid_package");
                    output.Write(buffer, 0, count);
                }
                if (written != entry.Length) throw new InvalidDataException("plugins.invalid_package");
            }
            var info = DailyPlugin.Inspect(staging);
            if (!info.Available) throw new InvalidDataException("plugins." + info.State);
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(staging, "plugin.json")));
            var listed = manifest.RootElement.GetProperty("files").EnumerateArray().Select(x => x.GetProperty("path").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            listed.Add("plugin.json");
            var actual = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(staging, x).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(listed)) throw new InvalidDataException("plugins.unlisted_files");
            return new(info);
        }
        catch { Directory.Delete(staging, true); throw; }
    }
    public DailyInstalledPlugin Install(DailyPluginPackage package)
    {
        using var guard = Lock();
        var info = DailyPlugin.Inspect(package.Info.Root);
        if (!info.Available || info.Fingerprint != package.Info.Fingerprint) throw new InvalidDataException("plugins.invalid_hash");
        var state = Read();
        var installed = state.Installed.SingleOrDefault(x => x.Fingerprint == info.Fingerprint);
        if (installed != null) return installed;
        string destination = PathFor(info.Fingerprint); NoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(destination))
        {
            var existing = DailyPlugin.Inspect(destination);
            if (!existing.Available || existing.Fingerprint != info.Fingerprint) throw new InvalidDataException("plugins.invalid_hash");
        }
        else Directory.Move(info.Root, destination);
        installed = new(info.Id, info.Version, info.Fingerprint, DateTimeOffset.UtcNow);
        Save(state with { Installed = [..state.Installed, installed] });
        return installed;
    }
    public async Task ActivateAsync(string fingerprint, Func<DailyPluginInfo, CancellationToken, Task> probe, CancellationToken token = default)
    {
        var before = Read();
        var record = before.Installed.SingleOrDefault(x => x.Fingerprint == fingerprint) ?? throw new InvalidDataException("plugins.invalid_state");
        var info = await Task.Run(() => Inspect(record), token);
        if (!info.Available) throw new InvalidDataException("plugins." + info.State);
        // Probe in a short-lived child process, never in the UI or in the game.
        await probe(info, token); token.ThrowIfCancellationRequested();
        var verified = await Task.Run(() => Inspect(record), token);
        using var guard = Lock();
        var state = Read();
        if (!state.Installed.Contains(record) || state.Active != before.Active || !verified.Available) throw new InvalidDataException("plugins.changed");
        if (state.Active != fingerprint) Save(state with { Active = fingerprint, Previous = state.Active, OverrideLocal = true });
    }
    public void Disable()
    {
        using var guard = Lock(); var state = Read();
        Save(state with { Active = null, Previous = state.Active ?? state.Previous, OverrideLocal = true });
    }
    public void Remove(string fingerprint)
    {
        using var guard = Lock(); var state = Read();
        Save(state with { Installed = state.Installed.Where(x => x.Fingerprint != fingerprint).ToArray(), Active = state.Active == fingerprint ? null : state.Active, Previous = state.Previous == fingerprint ? null : state.Previous });
        // Payload stays until an explicit offline cleanup: running helpers may still use it.
    }
    private FileStream Lock()
    {
        DailySandbox.RequireHost(); NoLinks(root); Directory.CreateDirectory(root); NoLinks(Path.Combine(root, "write.lock"));
        return new FileStream(Path.Combine(root, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void Save(DailyPluginSelection state)
    {
        NoLinks(SelectionPath); string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, state); stream.Flush(true); }
            File.Move(temporary, SelectionPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool ValidHash(string value) => value != null && value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool ValidRelativePath(string value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains('\\') &&
        value.Split('/').All(part => part.Length > 0 && part is not "." and not ".." && !part.EndsWith('.') && !part.EndsWith(' ') && !part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)) &&
            !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(part.Split('.')[0], StringComparer.OrdinalIgnoreCase));
    private static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("plugins.invalid_package");
    }
}

using System.Security.Cryptography;
using System.Text.Json;
namespace BD2Daily;

/// <summary>Optional local extension, validated before any code is loaded.</summary>
public sealed record DailyPluginInfo(bool Available, string State, string Root, string Version, string Fingerprint, string[] HookSources,
    string EntryAssembly = "", string EntryType = "", string[]? Capabilities = null)
{
    public bool Supports(string stage) => Available && (Capabilities?.Contains(stage, StringComparer.Ordinal) ?? false);
}
public static class DailyPlugin
{
    public const int ApiVersion = 3;
    private static readonly Lazy<DailyPluginInfo> loaded = new(() => Inspect(ResolveRoot()));
    public static DailyPluginInfo Current => loaded.Value;
    public static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN");
        if (configured != null)
            return configured.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : configured;
        var current = Path.GetFullPath(AppContext.BaseDirectory);
        if (new[] { "connection", "diagnostics" }.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(current)), StringComparer.OrdinalIgnoreCase))
            current = Directory.GetParent(current)!.FullName;
        return Path.Combine(current, "plugins", "extension");
    }
    public static DailyPluginInfo Inspect(string directory)
    {
        DailyPluginInfo Missing(string state) => new(false, state, directory, "", "", []);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return Missing("missing");
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            byte[] bytes = File.ReadAllBytes(SafePath(root, "plugin.json"));
            using var json = JsonDocument.Parse(bytes);
            var manifest = json.RootElement;
            if (string.IsNullOrWhiteSpace(manifest.GetProperty("id").GetString()) || manifest.GetProperty("apiVersion").GetInt32() != ApiVersion || manifest.GetProperty("bridgeExtensionApi").GetInt32() != 1)
                return Missing("incompatible");
            if (manifest.GetProperty("runtime").GetString() != "net8.0-windows-x64") return Missing("incompatible_runtime");
            var capabilities = manifest.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (capabilities.Length == 0 || capabilities.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) || capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Length)
                return Missing("invalid_capabilities");
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.GetProperty("files").EnumerateArray())
            {
                string relative = file.GetProperty("path").GetString()!;
                var data = File.ReadAllBytes(SafePath(root, relative));
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase) || !files.TryAdd(relative, data))
                    return Missing("invalid_hash");
            }
            var hooks = manifest.GetProperty("hookSources").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (hooks.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hooks.Length || hooks.Any(p => p == null || !p.StartsWith("hook/", StringComparison.Ordinal) || !p.EndsWith(".cs", StringComparison.Ordinal) || !files.ContainsKey(p)))
                return Missing("invalid_hook");
            var entry = manifest.GetProperty("entryAssembly").GetString();
            var type = manifest.GetProperty("entryType").GetString();
            if (string.IsNullOrWhiteSpace(entry) || !entry.StartsWith("managed/", StringComparison.Ordinal) || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(entry) || string.IsNullOrWhiteSpace(type))
                return Missing("invalid_entrypoint");
            return new(true, "ready", root, manifest.GetProperty("version").GetString()!, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hooks.Select(p => System.Text.Encoding.UTF8.GetString(files[p])).ToArray(), entry, type, capabilities);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        { return Missing("invalid_manifest"); }
    }
    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("Invalid plugin path");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin path outside root");
        for (var path = full; path.Length >= root.Length; path = Path.GetDirectoryName(path)!)
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Plugin link outside inventory");
        return full;
    }
}
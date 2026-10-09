using System.Security.Cryptography;
using System.Text.Json;
namespace Dustweave;

/// <summary>Optional local extension, validated before any code is loaded.</summary>
public sealed record DailyPluginInfo(bool Available, string State, string Root, string Version, string Fingerprint, string[] HookSources,
    string EntryAssembly = "", string EntryType = "", string[]? Capabilities = null)
{
    public string Id { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string MinHostVersion { get; init; } = "";
    public string MaxHostVersion { get; init; } = "";
    public string BindingContract { get; init; } = "";
    public Dictionary<string, string> Names { get; init; } = new();
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en-US") ?? Id;
    public bool Supports(string stage) => Available && (Capabilities?.Contains(stage, StringComparer.Ordinal) ?? false);
}
public static class DailyPlugin
{
    public const int ApiVersion = 4;
    public static string HostVersion => typeof(DailyPlugin).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(x => x.Key == "DustweaveVersion").Value!;
    private static readonly Lazy<DailyPluginInfo> loaded = new(() => Inspect(ResolveRoot()));
    public static DailyPluginInfo Current => loaded.Value;
    public static string ResolveRoot() => ResolveRoot(DailyIdentity.DataRoot);
    internal static string ResolveRoot(string dataRoot)
    {
        var configured = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN");
        if (configured != null)
            return configured.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : configured;
        if (DailyPluginStore.HasSelection(dataRoot))
            return new DailyPluginStore(dataRoot).SelectedRoot();
        var current = Path.GetFullPath(AppContext.BaseDirectory);
        if (new[] { "connection", "diagnostics" }.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(current)), StringComparer.OrdinalIgnoreCase))
            current = Directory.GetParent(current)!.FullName;
        return Path.Combine(current, "plugins", "extension");
    }
    // A fallback directory is not an installed plugin. Only a currently usable,
    // selected extension can constrain the next host version. Resolve selection
    // afresh so disabling an extension takes effect before the app restarts.
    public static string? HostUpdateBlockReason(string dataRoot, string hostVersion)
    {
        var current = Inspect(ResolveRoot(dataRoot));
        if (!current.Available) return null;
        var next = Inspect(current.Root, hostVersion);
        return next.Available ? null : "plugins." + next.State;
    }
    public static DailyPluginInfo Inspect(string directory, string? hostVersion = null)
    {
        DailyPluginInfo Missing(string state) => new(false, state, directory, "", "", []);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return Missing("missing");
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            byte[] bytes = File.ReadAllBytes(SafePath(root, "plugin.json"));
            using var json = JsonDocument.Parse(bytes);
            var manifest = json.RootElement;
            string id = manifest.GetProperty("id").GetString() ?? "";
            string version = manifest.GetProperty("version").GetString() ?? "";
            if (!ValidId(id) || !Version.TryParse(version, out _)) return Missing("invalid_manifest");
            if (string.IsNullOrWhiteSpace(manifest.GetProperty("id").GetString()) || manifest.GetProperty("apiVersion").GetInt32() != ApiVersion || manifest.GetProperty("bridgeExtensionApi").GetInt32() != 1)
                return Missing("incompatible");
            if (manifest.GetProperty("runtime").GetString() != "net8.0-windows-x64") return Missing("incompatible_runtime");
            string min = manifest.TryGetProperty("minHostVersion", out var minValue) ? minValue.GetString() ?? "" : "";
            string max = manifest.TryGetProperty("maxHostVersion", out var maxValue) ? maxValue.GetString() ?? "" : "";
            if (!CompatibleHost(min, max, hostVersion ?? HostVersion)) return Missing("incompatible_host");
            var capabilities = manifest.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (capabilities.Length == 0 || capabilities.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) || capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Length)
                return Missing("invalid_capabilities");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.GetProperty("files").EnumerateArray())
            {
                string relative = file.GetProperty("path").GetString()!;
                string path = SafePath(root, relative);
                using var data = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase) || !files.TryAdd(relative, path))
                    return Missing("invalid_hash");
            }
            var hooks = manifest.GetProperty("hookSources").EnumerateArray().Select(x => x.GetString()!).ToArray();
            string bindings = manifest.TryGetProperty("bindingContract", out var bindingsValue) ? bindingsValue.GetString() ?? "" : "";
            if (bindings.Length != 0 && (!bindings.EndsWith(".json", StringComparison.Ordinal) || !files.ContainsKey(bindings))) return Missing("invalid_hook");
            if (hooks.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hooks.Length || hooks.Any(p => p == null || !p.StartsWith("hook/", StringComparison.Ordinal) || !p.EndsWith(".cs", StringComparison.Ordinal) || !files.ContainsKey(p)))
                return Missing("invalid_hook");
            var entry = manifest.GetProperty("entryAssembly").GetString();
            var type = manifest.GetProperty("entryType").GetString();
            if (string.IsNullOrWhiteSpace(entry) || !entry.StartsWith("managed/", StringComparison.Ordinal) || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(entry) || string.IsNullOrWhiteSpace(type))
                return Missing("invalid_entrypoint");
            var names = manifest.TryGetProperty("names", out var nameValue)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(nameValue.GetRawText()) ?? new() : new();
            return new(true, "ready", root, version, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hooks.Select(p => File.ReadAllText(files[p], System.Text.Encoding.UTF8)).ToArray(), entry, type, capabilities)
            { Id = id, Names = names, MinHostVersion = min, MaxHostVersion = max, BindingContract = bindings,
              Publisher = manifest.TryGetProperty("publisher", out var publisher) ? publisher.GetString() ?? "" : "" };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        { return Missing("invalid_manifest"); }
    }
    internal static bool ValidId(string? value) => value is { Length: > 0 and <= 100 } && char.IsAsciiLetterOrDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool CompatibleHost(string min, string max, string current)
    {
        try
        {
            var host = DailyVersion.Parse(current);
            if (min.Length != 0 && host < DailyVersion.Parse(min)) return false;
            if (max.Length != 0 && host > DailyVersion.Parse(max)) return false;
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
    private static string SafePath(string root, string relative)
    {
        if (!DailyPluginStore.ValidRelativePath(relative))
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

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
    public bool SupportsSolutionSharing { get; init; }
    public string ToolExecutable { get; init; } = "";
    public string ToolAssembly { get; init; } = "";
    public string ToolType { get; init; } = "";
    public int ToolApiVersion { get; init; } = 1;
    public bool IsTool => ToolExecutable.Length != 0 || ToolAssembly.Length != 0;
    public DailyPluginInfo[] Members { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public DailyPluginInfo[] AllSelections => Members.Length>0 ? Members : Available ? [this] : [];
    [System.Text.Json.Serialization.JsonIgnore]
    public DailyPluginInfo[] Selections => AllSelections.Where(p=>!p.IsTool).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public DailyPluginInfo[] HookMembers => AllSelections.Where(p=>p.HookSources.Length>0).ToArray();
    public Dictionary<string, string> Names { get; init; } = new();
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en-US") ?? Id;
    public bool Supports(string stage) => Selections.Any(p=>p.Capabilities?.Contains(stage, StringComparer.Ordinal)==true);
}
public static class DailyPlugin
{
    public const int ApiVersion = 4;
    public static string HostVersion => typeof(DailyPlugin).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(x => x.Key == "DustweaveVersion").Value!;
    private static readonly Lazy<DailyPluginInfo> loaded = new(() => Selected(DailyIdentity.DataRoot));
    public static DailyPluginInfo Current => loaded.Value;
    public static DailyPluginInfo Combine(IEnumerable<DailyPluginInfo> input)
    {
        var items=input.Where(p=>p.Available).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        if(items.Length==0)return new(false,"missing","","","",[]);
        if(items.Length==1)return items[0];
        if(items.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=items.Length||items.Where(p=>!p.IsTool).SelectMany(p=>p.Capabilities??[]).GroupBy(x=>x,StringComparer.Ordinal).Any(g=>g.Count()>1))throw new InvalidDataException("plugins.task_conflict");
        return items[0] with { Members=items, Fingerprint=DailyIdentity.Hash(string.Join("|",items.Select(p=>p.Fingerprint))), HookSources=items.SelectMany(p=>p.HookSources).ToArray(), Capabilities=items.SelectMany(p=>p.Capabilities??[]).ToArray() };
    }
    internal static DailyPluginInfo Selected(string root)
    {
        try
        {
        var pinned=Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGINS");
        if(pinned!=null)return Combine((JsonSerializer.Deserialize<string[]>(pinned)??[]).Select(p=>Inspect(p)));
        if(Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN")!=null)return Inspect(ResolveRoot(root));
        if(!DailyPluginStore.HasSelection(root))return Combine(new[]{Inspect(ResolveRoot(root))}.Concat(new DailyToolPlugins(root).Enabled()));
        var store=new DailyPluginStore(root);var state=store.Read();
        return Combine(state.Installed.Where(p=>state.ActiveFingerprints.Contains(p.Fingerprint)).Select(p=>store.Inspect(p)).Concat(new DailyToolPlugins(root).Enabled()));
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return new(false,"invalid_state","","","",[]); }
    }
    public static void PinEnvironment(System.Diagnostics.ProcessStartInfo start,DailyPluginInfo info)
    {
        start.Environment["DUSTWEAVE_PLUGIN"]=info.Available ? info.Root : "none";
        if(info.Members.Length>0)start.Environment["DUSTWEAVE_PLUGINS"]=JsonSerializer.Serialize(info.Members.Select(p=>p.Root));
        else start.Environment.Remove("DUSTWEAVE_PLUGINS");
    }
    public static string ResolveRoot() => ResolveRoot(DailyIdentity.DataRoot);
    internal static string ResolveRoot(string dataRoot)
    {
        var configured = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN");
        if (configured != null)
            return configured.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : configured;
        if (DailyPluginStore.HasSelection(dataRoot))
            return new DailyPluginStore(dataRoot).SelectedRoot();
        var current = Path.GetFullPath(AppContext.BaseDirectory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(current)).Equals("app", StringComparison.OrdinalIgnoreCase) &&
            Directory.GetParent(Path.TrimEndingDirectorySeparator(current)) is { } package && File.Exists(Path.Combine(package.FullName, DailyApplication.ExecutableName)))
            current = package.FullName;
        if (new[] { "connection", "diagnostics" }.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(current)), StringComparer.OrdinalIgnoreCase))
            current = Directory.GetParent(current)!.FullName;
        return Path.Combine(current, "plugins", "extension");
    }
    // A fallback directory is not an installed plugin. Only a currently usable,
    // selected extension can constrain the next host version. Resolve selection
    // afresh so disabling an extension takes effect before the app restarts.
    public static string? HostUpdateBlockReason(string dataRoot, string hostVersion)
    {
        var current = Selected(dataRoot);
        if (!current.Available) return null;
        var next = current.AllSelections.Select(p=>Inspect(p.Root,hostVersion)).FirstOrDefault(p=>!p.Available);
        return next==null ? null : "plugins." + next.State;
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
            bool tool = manifest.TryGetProperty("kind", out var kind) && kind.GetString() == "tool";
            if (kind.ValueKind != JsonValueKind.Undefined && kind.GetString() is not ("tool" or "daily")) return Missing("invalid_manifest");
            int toolVersion = tool && manifest.TryGetProperty("toolApiVersion", out var toolApi) ? toolApi.GetInt32() : 0;
            if (tool && toolVersion is not (1 or 2)) return Missing("incompatible");
            if (manifest.GetProperty("runtime").GetString() != (tool ? toolVersion == 2 ? "host-net8.0-windows-x64" : "standalone-windows-x64" : "net8.0-windows-x64")) return Missing("incompatible_runtime");
            if (toolVersion == 2 && (!manifest.TryGetProperty("uiApiVersion", out var uiApi) || uiApi.GetInt32() != 1)) return Missing("incompatible");
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
            var entry = tool ? "" : manifest.GetProperty("entryAssembly").GetString();
            var type = tool ? "" : manifest.GetProperty("entryType").GetString();
            string toolExecutable = toolVersion == 1 ? manifest.GetProperty("toolExecutable").GetString() ?? "" : "";
            string toolAssembly = toolVersion == 2 ? manifest.GetProperty("toolAssembly").GetString() ?? "" : "";
            string toolType = toolVersion == 2 ? manifest.GetProperty("toolType").GetString() ?? "" : "";
            string toolEntry = toolVersion == 2 ? toolAssembly : toolExecutable;
            if (tool && (!toolEntry.StartsWith("tool/", StringComparison.Ordinal) || !toolEntry.EndsWith(toolVersion == 2 ? ".dll" : ".exe", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(toolEntry) || capabilities.Any(c=>c is not ("inventory_read" or "tool_operations")) || capabilities.Contains("tool_operations") != (hooks.Length>0) || toolVersion == 2 && string.IsNullOrWhiteSpace(toolType)))
                return Missing("invalid_entrypoint");
            if (!tool && (string.IsNullOrWhiteSpace(entry) || !entry.StartsWith("managed/", StringComparison.Ordinal) || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(entry) || string.IsNullOrWhiteSpace(type)))
                return Missing("invalid_entrypoint");
            var names = manifest.TryGetProperty("names", out var nameValue)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(nameValue.GetRawText()) ?? new() : new();
            return new(true, "ready", root, version, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hooks.Select(p => File.ReadAllText(files[p], System.Text.Encoding.UTF8)).ToArray(), entry, type, capabilities)
            { Id = id, Names = names, MinHostVersion = min, MaxHostVersion = max, BindingContract = bindings, ToolExecutable = toolExecutable, ToolAssembly = toolAssembly, ToolType = toolType, ToolApiVersion = toolVersion,
              SupportsSolutionSharing = manifest.TryGetProperty("solutionSharing", out var sharing) && sharing.GetBoolean(),
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

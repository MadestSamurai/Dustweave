namespace Dustweave;

/// <summary>Why the part of a category that cannot be removed now is kept.</summary>
public enum DailyStorageState { Recent, Regenerable, InUse, Recovery, Account, Kept }

/// <summary>Display-only accounting. Reclaimable bytes come from the cleanup plans that
/// DailyStorageMaintenance.CleanAsync applies; classification never makes a file removable.</summary>
public sealed record DailyStorageCategory(string Key, DailyStorageState Retained, long Bytes, long ReclaimableBytes, long DeferredBytes, int Files)
{
    /// <summary>Sandbox-local Dustweave data is grouped below "dustweave".</summary>
    public string Group => Key.StartsWith(DailyStorageBreakdown.DustweavePrefix, StringComparison.Ordinal) ? "dustweave" : "";
    public string Name => Group.Length == 0 ? Key : Key[DailyStorageBreakdown.DustweavePrefix.Length..];
}

/// <summary>One storage location: main data, relocated cache, a previous location, an account sandbox, an extraction folder or crash records.</summary>
public sealed record DailyStorageArea(string Kind, string Path, int Skipped, DailyStorageCategory[] Categories)
{
    public string? Account { get; init; }
    /// <summary>The owner is running or could not be confirmed idle. Expired files are reported as deferred, not reclaimable.</summary>
    public bool Locked { get; init; }
    /// <summary>The location could not be enumerated. Its size is unknown, not zero.</summary>
    public bool Unknown { get; init; }
    /// <summary>Left at a location used before storage migration.</summary>
    public bool Previous { get; init; }
    public long Bytes => Categories.Sum(c => c.Bytes);
    public long ReclaimableBytes => Categories.Sum(c => c.ReclaimableBytes);
    public long DeferredBytes => Categories.Sum(c => c.DeferredBytes);
    public string Disk => System.IO.Path.GetPathRoot(Path) is { Length: > 0 } disk ? disk : Path;
}

internal static class DailyStorageBreakdown
{
    internal const string DustweavePrefix = "dustweave/";
    static readonly Dictionary<string, DailyStorageState> Retention = new(StringComparer.Ordinal)
    {
        ["logs"] = DailyStorageState.Recent, ["steps"] = DailyStorageState.Recent, ["tests"] = DailyStorageState.Recent,
        ["progress"] = DailyStorageState.Recovery, ["components"] = DailyStorageState.InUse, ["rules"] = DailyStorageState.InUse,
        ["trade"] = DailyStorageState.InUse, ["tools"] = DailyStorageState.Account, ["plugins"] = DailyStorageState.InUse,
        ["update-download"] = DailyStorageState.Recent, ["update-temp"] = DailyStorageState.InUse, ["update-recovery"] = DailyStorageState.Recovery,
        ["account"] = DailyStorageState.Account,
        ["shader"] = DailyStorageState.Regenerable, ["windows"] = DailyStorageState.Kept, ["game-tables"] = DailyStorageState.InUse,
        ["game-files"] = DailyStorageState.InUse, ["game-recovery"] = DailyStorageState.Recovery, ["crash"] = DailyStorageState.Recent,
        ["login"] = DailyStorageState.Account, ["extract-current"] = DailyStorageState.InUse, ["extract-old"] = DailyStorageState.Recent,
    };
    internal static IReadOnlyCollection<string> Keys => Retention.Keys;
    static readonly string[] ShaderCaches =
    [
        "AppData/Local/NVIDIA/DXCache", "AppData/Local/NVIDIA/GLCache", "AppData/Local/NVIDIA/OptixCache",
        "AppData/Local/NVIDIA Corporation/NV_Cache", "AppData/LocalLow/NVIDIA/PerDriverVersion/DXCache",
        "AppData/LocalLow/NVIDIA/PerDriverVersion/GLCache", "AppData/Local/D3DSCache", "AppData/Local/AMD/DxCache",
        "AppData/Local/AMD/DxcCache", "AppData/Local/AMD/GLCache", "AppData/Local/AMD/VkCache", "AppData/Local/AMD/OglCache",
        "AppData/Local/Intel/ShaderCache", "AppData/LocalLow/Intel/ShaderCache",
    ];
    static string Relative(string basis, string path) => System.IO.Path.GetRelativePath(basis, path).Replace(System.IO.Path.DirectorySeparatorChar, '/');
    static bool Under(string relative, string prefix) => relative.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    // Inventory paths are absolute; parents are normalized once by the caller.
    static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>A Dustweave data root (host or sandbox-private).</summary>
    internal static string Data(string basis, string path)
    {
        string relative = Relative(basis, path);
        if (Under(relative, "updates"))
        {
            var parts = relative.Split('/');
            bool attempt = parts.Length >= 4 && parts[2].Equals("attempts", StringComparison.OrdinalIgnoreCase);
            // The backup and the job receipt are what an interrupted install recovers from.
            if (attempt && (parts.Length >= 6 && parts[4].Equals("backup", StringComparison.OrdinalIgnoreCase)
                || parts.Length == 5 && parts[4].Equals("job.json", StringComparison.OrdinalIgnoreCase))) return "update-recovery";
            if (attempt || parts.Length >= 4 && parts[2].Equals("repair-staging", StringComparison.OrdinalIgnoreCase)) return "update-temp";
            return "update-download";
        }
        if (DailyLogCleanup.Managed(basis, path)) return "logs";
        if (Under(relative, "live/steps")) return "steps";
        if (Under(relative, "suite-cache") || Under(relative, "suite") || Under(relative, "live/prepared") || Under(relative, "live/binding-cache")) return "components";
        if (Under(relative, "rules")) return "rules";
        if (Under(relative, "trade")) return "trade";
        if (Under(relative, "tools")) return "tools";
        if (DailyHistoricalCache.Areas.Any(a => Under(relative, a))) return "tests";
        if (Under(relative, "extensions")) return "plugins";
        if (Under(relative, "live") || Under(relative, "queue-history") || Under(relative, "parallel-progress") || Under(relative, "parallel-workers")) return "progress";
        return "account";
    }

    /// <summary>A complete account sandbox directory: registration, Sandboxie root and recovery copies.</summary>
    internal static string Sandbox(string directory, string path)
    {
        string relative = Relative(directory, path);
        if (Under(relative, "game-table-recovery") || Under(relative, "game-program-recovery")) return "game-recovery";
        const string user = "root/user/current/";
        if (relative.StartsWith(user, StringComparison.OrdinalIgnoreCase))
        {
            string inside = relative[user.Length..];
            if (Under(inside, "AppData/Local/BD2DailyAssistant"))
                return DustweavePrefix + Data(System.IO.Path.Combine(directory, "root", "user", "current", "AppData", "Local", "BD2DailyAssistant"), path);
            if (Under(inside, "AppData/Local/CrashDumps")) return "crash";
            if (ShaderCaches.Any(p => Under(inside, p))) return "shader";
            if (Under(inside, "AppData/LocalLow/Gamfs/BrownDust II/Data/t")) return "game-tables";
            if (Under(inside, "AppData/Local/Microsoft") || Under(inside, "AppData/Roaming/Microsoft")) return "windows";
            return "login";
        }
        if (relative.StartsWith("root/drive/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = relative.Split('/');
            if (parts.Length > 4 && (parts[3].Equals("Windows", StringComparison.OrdinalIgnoreCase) || parts[3].Equals("ProgramData", StringComparison.OrdinalIgnoreCase))) return "windows";
            if (parts.Length > 4 && parts[3].Equals("Users", StringComparison.OrdinalIgnoreCase)) return "login";
            return "game-files";
        }
        return "login";
    }

    static DailyStorageCategory[] Categories(IEnumerable<DailyStorageItem> files, Func<DailyStorageItem, string> classify, ISet<string> removable, bool locked) =>
        files.GroupBy(classify, StringComparer.Ordinal).Select(g =>
        {
            string key = g.Key; string name = key.StartsWith(DustweavePrefix, StringComparison.Ordinal) ? key[DustweavePrefix.Length..] : key;
            long bytes = g.Sum(f => f.DiskBytes), expired = g.Where(f => removable.Contains(f.Path)).Sum(f => f.DiskBytes);
            return new DailyStorageCategory(key, Retention.TryGetValue(name, out var state) ? state : DailyStorageState.Kept, bytes,
                locked ? 0 : expired, locked ? expired : 0, g.Count());
        }).OrderByDescending(c => c.Bytes).ToArray();

    /// <summary>Main data, the relocated cache and locations left before migration. Each file belongs to its most specific root.</summary>
    internal static IEnumerable<DailyStorageArea> Main(string root, string cache, IEnumerable<string> known, IReadOnlyList<DailyStorageItem> files, ISet<string> removable, int skipped)
    {
        static string Normal(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        string main = Normal(root), relocated = Normal(cache);
        var bases = new[] { main, relocated }.Concat(known.Select(Normal)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(r => r.Length).ToArray();
        var owned = files.GroupBy(f => bases.FirstOrDefault(b => Within(f.Path, b)) ?? main, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mainFiles = owned.GetValueOrDefault(main, Array.Empty<DailyStorageItem>());
        yield return new("main", main, skipped, Categories(mainFiles, f => Data(main, f.Path), removable, false)) { Unknown = mainFiles.Length == 0 && skipped > 0 };
        if (!relocated.Equals(main, StringComparison.OrdinalIgnoreCase) && owned.TryGetValue(relocated, out var moved))
            yield return new("cache", relocated, 0, Categories(moved, f => Data(relocated, f.Path), removable, false));
        foreach (var pair in owned.Where(p => !p.Key.Equals(main, StringComparison.OrdinalIgnoreCase) && !p.Key.Equals(relocated, StringComparison.OrdinalIgnoreCase)))
        {
            string basis = pair.Key;
            yield return new("cache", basis, 0, Categories(pair.Value, f => Data(basis, f.Path), removable, false)) { Previous = true };
        }
    }

    internal static DailyStorageArea Sandbox(string directory, IEnumerable<DailyStorageItem> files, ISet<string> removable, bool locked, int skipped)
    {
        var categories = Categories(files, f => Sandbox(directory, f.Path), removable, locked);
        return new("sandbox", directory, skipped, categories) { Locked = locked, Unknown = categories.Length == 0 && skipped > 0 };
    }

    internal static DailyStorageArea Extraction(string folder, IEnumerable<DailyStorageItem> files, ISet<string> removable, bool locked, int skipped, Func<string, bool> inactive)
    {
        var current = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string Classify(DailyStorageItem file)
        {
            var parts = Relative(folder, file.Path).Split('/');
            if (parts.Length < 2) return "extract-old";
            string generation = System.IO.Path.Combine(folder, parts[0]);
            if (!current.TryGetValue(generation, out bool active)) current[generation] = active = !inactive(generation);
            return active ? "extract-current" : "extract-old";
        }
        var categories = Categories(files, Classify, removable, locked);
        return new("extraction", folder, skipped, categories) { Locked = locked, Unknown = categories.Length == 0 && skipped > 0 };
    }

    internal static DailyStorageArea Crashes(string folder, IEnumerable<DailyStorageItem> files, ISet<string> removable, int skipped)
    {
        var categories = Categories(files, _ => "crash", removable, false);
        return new("crashes", folder, skipped, categories) { Unknown = categories.Length == 0 && skipped > 0 };
    }

    internal static DailyStorageArea Unreadable(string kind, string path, int skipped) => new(kind, path, skipped, []) { Unknown = true };
}

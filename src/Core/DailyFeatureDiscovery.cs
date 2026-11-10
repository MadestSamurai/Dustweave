namespace Dustweave;

/// <summary>Viewed features are preferences, not cache. Versions and translated names are not identities.</summary>
public sealed class DailyFeatureDiscovery
{
    // Frozen migration baseline. Never add future tools here: unknown IDs must announce themselves.
    private static readonly string[] ExistingTools = ["fishing", "territory", "equipment", "sichuan", "rhythm",
        "apostle-defense", "secret-vision", "fiend-hunter", "mansion-runaway", "pandora", "infinite-gacha"];
    private sealed record Receipt(int Schema, string[] Viewed);
    private static readonly object Gate = new();
    private readonly string path;
    private HashSet<string> viewed;
    public DailyFeatureDiscovery(string root)
    {
        path = Path.Combine(root, "feature-discovery.json");
        viewed = Read();
    }
    public static string Tool(string id) => id.StartsWith("plugin:", StringComparison.Ordinal) ? id : "tool:" + id;
    public static string Plugin(string id) => "plugin:" + id;
    private HashSet<string> Read()
    {
        var result = ExistingTools.Select(Tool).ToHashSet(StringComparer.Ordinal);
        if (DailyJson.TryRead<Receipt>(path) is { Schema: 1, Viewed: { } entries })
            result.UnionWith(entries.Where(Valid));
        return result;
    }
    private static bool Valid(string? key) => key is { Length: > 5 and <= 180 } &&
        (key.StartsWith("tool:", StringComparison.Ordinal) || key.StartsWith("plugin:", StringComparison.Ordinal)) &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.');
    public bool IsNew(string key) => Valid(key) && !viewed.Contains(key);
    public void MarkViewed(string key)
    {
        if (!IsNew(key)) return;
        lock (Gate)
        {
            var merged = Read(); merged.UnionWith(viewed); merged.Add(key);
            DailyJson.Write(path, new Receipt(1, merged.Order(StringComparer.Ordinal).ToArray()));
            viewed = merged; // A failed save leaves the reminder visible; it never blocks a feature.
        }
    }
}

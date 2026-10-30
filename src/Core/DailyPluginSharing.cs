namespace Dustweave;

public sealed record DailyPluginSharingChoice(int Schema, bool Enabled, DateTimeOffset ChosenUtc);

/// <summary>Permission belongs to a plugin ID, not an account or a release version. Missing or unreadable permission means local only.</summary>
public static class DailyPluginSharing
{
    private static string PathFor(string root, string id)
    {
        if (!DailyPlugin.ValidId(id)) throw new InvalidDataException("plugins.invalid_manifest");
        return Path.Combine(root, "extensions", "preferences", id, "solution-sharing.json");
    }
    public static DailyPluginSharingChoice? Read(string root, string id)
    {
        try
        {
            string path = PathFor(root, id); DailyPluginStore.NoLinks(path);
            var state = DailyJson.TryRead<DailyPluginSharingChoice>(path);
            return state?.Schema == 1 ? state : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }
    public static bool Enabled(string root, string id) => Read(root, id)?.Enabled == true;
    public static void Write(string root, string id, bool enabled)
    {
        DailySandbox.RequireHost();
        string path = PathFor(root, id); DailyPluginStore.NoLinks(path);
        DailyJson.Write(path, new DailyPluginSharingChoice(1, enabled, DateTimeOffset.UtcNow));
    }
}

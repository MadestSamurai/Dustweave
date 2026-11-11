namespace Dustweave;

// This small routing record stays beside the host accounts and update journals.
// It is read-only in isolated processes (unlike their private cache-location.json).
public sealed record DailyStorageLocation(string Cache, string Sandboxes, string Id)
{
    public string[] PreviousCaches { get; init; } = [];
    public string[] PreviousSandboxes { get; init; } = [];
}
public static class DailyStorageLocations
{
    public static string FileName(string root) => Path.Combine(root, "storage-location.json");
    public static DailyStorageLocation? Read(string root)
    {
        var value=ReadConfigured(root);
        if(value==null)return null;
        // A disconnected drive must never silently create an empty account store.
        if(!Matches(DailyJson.TryRead<DailyStorageLocation>(Path.Combine(value.Cache,"storage-owner.json")),value))
            throw new IOException("storage.location_unavailable");
        return value;
    }
    // For displaying/reconnecting a missing destination only. Never use this
    // record as an active cache or sandbox location without verifying ownership.
    public static DailyStorageLocation? ReadConfigured(string root)
    {
        string path=FileName(root);
        if(!File.Exists(path))return null;
        var value=DailyJson.TryRead<DailyStorageLocation>(path);
        if(value==null || !Guid.TryParseExact(value.Id,"N",out _) || string.IsNullOrWhiteSpace(value.Cache) || string.IsNullOrWhiteSpace(value.Sandboxes) || !Path.IsPathFullyQualified(value.Cache)
            || !Accounts.SandboxConfiguration.SameRoot(value.Sandboxes,Path.Combine(value.Cache,"instances")))
            throw new IOException("storage.location_unavailable");
        return value;
    }
    internal static bool Matches(DailyStorageLocation? a,DailyStorageLocation b)=>a!=null&&a.Id==b.Id&&Accounts.SandboxConfiguration.SameRoot(a.Cache,b.Cache)&&Accounts.SandboxConfiguration.SameRoot(a.Sandboxes,b.Sandboxes);
    public static string LegacySandboxes => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Dustweave","instances");
    public static string Sandboxes(string root) => Read(root)?.Sandboxes ?? LegacySandboxes;
}

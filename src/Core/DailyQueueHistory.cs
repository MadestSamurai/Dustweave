using System.Text.Json;
namespace BD2Daily;

public static class DailyQueueHistory
{
    private sealed record Pointer(string account, string record);
    private static string Index(string root, string account) => Path.Combine(root, "queue-history", account + ".json");
    private static string Text(JsonElement d, string key) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    public static string RecordAccount(JsonElement record) => record.ValueKind == JsonValueKind.Object && record.TryGetProperty("context", out var c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 5 && a[3].ValueKind == JsonValueKind.String ? a[3].GetString() ?? "" : "";
    private static bool Safe(string root, string record)
    {
        try
        {
            return Path.GetFullPath(record).StartsWith(Path.GetFullPath(Path.Combine(root, "live", "queues")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(record) == "result.json";
        }
        catch { return false; }
    }
    private static bool Matches(string root, string account, string record, bool legacyHeader = false)
    {
        if (!Safe(root, record))
            return false;
        using var d = DailyJson.TryRead<JsonDocument>(record);
        if (d == null)
            return false;
        string owner = RecordAccount(d.RootElement);
        return owner == account || (legacyHeader && owner.Length == 0);
    }
    public static void Remember(string root, string account, string record)
    {
        if (!DailyProfiles.ValidKey(account) || !Safe(root, record))
            throw new InvalidDataException("Invalid queue history identity/path");
        DailyJson.Write(Index(root, account), new Pointer(account, record));
    }
    public static string Resolve(string root, string account)
    {
        if (!DailyProfiles.ValidKey(account))
            return "";
        using var last = DailyJson.TryRead<JsonDocument>(Path.Combine(root, "queue-ui.json"));
        if (last != null && Text(last.RootElement, "account") == account)
        {
            var p = Text(last.RootElement, "record");
            if (Matches(root, account, p, true))
                return p;
        }
        string index = Index(root, account);
        var saved = DailyJson.TryRead<Pointer>(index);
        if (saved?.account == account && (saved.record.Length == 0 || Matches(root, account, saved.record)))
            return saved.record;
        string folder = Path.Combine(root, "live", "queues");
        if (Directory.Exists(folder))
            foreach (var file in new DirectoryInfo(folder).EnumerateDirectories().Select(d => new FileInfo(Path.Combine(d.FullName, "result.json"))).Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc))
            {
                if (!Matches(root, account, file.FullName))
                    continue;
                Remember(root, account, file.FullName);
                return file.FullName;
            }
        DailyJson.Write(index, new Pointer(account, ""));
        return "";
    }
}

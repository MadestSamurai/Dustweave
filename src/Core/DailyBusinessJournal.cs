using System.Text.Json.Nodes;
namespace BD2Daily;

public sealed partial class DailyManagedBusiness
{
    // Retain only routing/state metadata, not the large captured frames in each record.
    // Every check enumerates the directory again; new files and changed files are not hidden.
    private readonly Dictionary<string, JournalHeader> journalHeaders = new(StringComparer.OrdinalIgnoreCase);
    private sealed record JournalStamp(long Length, long Written, long Created);
    private sealed record JournalHeader(JournalStamp Stamp, JsonObject Summary);
    private static JournalStamp Stamp(string path)
    {
        var info = new FileInfo(path);
        return new(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
    }
    private static JsonObject Summary(JsonObject op)
    {
        var summary = new JsonObject();
        foreach (string key in new[] { "id", "role", "account", "player", "server", "cycle", "state", "presentation", "presentation_closed" })
            summary[key] = op[key]?.DeepClone();
        // Only dispatch.start uses scope to distinguish a trade operation.
        summary["trade"] = DailyTradeJournal.IsTrade(op);
        return summary;
    }
    private void RememberHeader(string path, JsonObject op) => journalHeaders[path] = new(Stamp(path), Summary(op));
    private JsonObject ReadHeader(string path)
    {
        var stamp = Stamp(path);
        if (journalHeaders.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Summary;
        var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
        if (Stamp(path) != stamp)
            throw new StageHostException("pending", "业务记录正在更新；稍后重新核对，未重复提交。");
        var summary = Summary(op);
        journalHeaders[path] = new(stamp, summary);
        return summary;
    }
    private JsonObject ReadJournalRecord(string path)
    {
        var stamp = Stamp(path);
        if (!journalHeaders.TryGetValue(path, out var header) || header.Stamp != stamp)
            throw new StageHostException("pending", "业务记录正在更新；稍后重新核对，未重复提交。");
        var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
        if (Stamp(path) != stamp)
            throw new StageHostException("pending", "业务记录正在更新；稍后重新核对，未重复提交。");
        return op;
    }
    private bool HasUnresolvedRecord(JsonObject context, string role)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { "managed-business", "business" })
        {
            string folder = Path.Combine(root, "live", directory);
            if (!Directory.Exists(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
            {
                Stop();
                paths.Add(path);
                var op = ReadHeader(path);
                if (op["role"]?.GetValue<string>() != role) continue;
                DailyManagedReconciliation.ValidateRecord(path, op);
                string id = op["id"]!.GetValue<string>();
                if (seen.TryGetValue(id, out var previous))
                {
                    // Conflicting copies remain an error even if their headers match.
                    var a = DailyJson.TryRead<JsonObject>(previous) ?? throw new InvalidDataException("Unreadable business record");
                    var b = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
                    if (!JsonNode.DeepEquals(a, b))
                        throw new StageHostException("pending", "发现同一操作的冲突记录，未覆盖或重复提交：" + id);
                    continue;
                }
                seen.Add(id, path);
                recordPaths[id] = path;
                if (Pending(op) && op["trade"]?.GetValue<bool>() != true
                    && JsonNode.DeepEquals(op["account"], context["actor"]![3])
                    && JsonNode.DeepEquals(op["player"], context["actor"]![4])
                    && JsonNode.DeepEquals(op["server"], context["server"])) return true;
            }
        }
        foreach (string removed in journalHeaders.Keys.Where(p => !paths.Contains(p)).ToArray()) journalHeaders.Remove(removed);
        return false;
    }
}
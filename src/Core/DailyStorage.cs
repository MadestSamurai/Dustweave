using System.Text.Json;
namespace BD2Daily;

public static class DailyJson
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Writes = new(StringComparer.OrdinalIgnoreCase);
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = true };
    public static T? TryRead<T>(string path) where T : class
    {
        if (BD2.LocalIpc.DesktopFiles.Handles(path))
        {
            try
            {
                BD2.LocalIpc.DesktopFiles.Read(path, out var bytes);
                return bytes == null ? null : JsonSerializer.Deserialize<T>(bytes, Options);
            }
            catch (Exception e) when (e is IOException or TimeoutException or JsonException) { return null; }
        }
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<T>(f, Options);
            }
            catch (JsonException) { return null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                int code = e.HResult & 0xffff;
                if (attempt == 5 || code is not (2 or 5 or 32 or 33))
                    return null;
                // ReplaceFile can briefly reject a *new* reader while exchanging
                // names. Existing readers remain valid; never turn this into defaults.
                Thread.Sleep(5 * (attempt + 1));
            }
        }
        return null;
    }
    public static void Write<T>(string path, T value)
    {
        if (BD2.LocalIpc.DesktopFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(value, Options), Path.GetFileName(path) == "guild-command.json"))
            return;
        path = Path.GetFullPath(path);
        lock (Writes.GetOrAdd(path, _ => new object()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool ready = false;
            try
            {
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(f, value, Options);
                    f.Flush(true);
                }
                ready = true;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                int delay = 10;
                while (true)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Replace(tmp, path, null);
                        else
                            File.Move(tmp, path);
                        break;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        int code = e.HResult & 0xffff;
                        // ReplaceFile ERROR_UNABLE_TO_REMOVE_REPLACED (1175) keeps
                        // both original names intact, so retry the same flushed file.
                        // 1176/1177 can alter names and must NOT be retried this way.
                        if (code is not (5 or 32 or 33 or 1175) || elapsed.ElapsedMilliseconds >= 2000)
                            throw new IOException($"无法保存执行记录 {path}；待写入内容保留于 {tmp}：{e.Message}", e);
                        Thread.Sleep(delay);
                        delay = Math.Min(100, delay * 2);
                    }
                }
            }
            finally { if (!ready && File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
public sealed class DailyAccountProfile
{
    public string AccountKey { get; set; } = "";
    public int SlotNumber
    {
        get; set;
    }
    public bool Selected
    {
        get; set;
    }
    public int Order
    {
        get; set;
    }
    public string PlayerKey { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public DateTimeOffset? LastVerifiedUtc
    {
        get; set;
    }
}
public sealed class DailyProfiles
{
    private readonly string path; private readonly object gate = new();
    public DailyProfiles(string root)
    {
        path = Path.Combine(root, "accounts.json");
    }
    public List<DailyAccountProfile> Read()
    {
        lock (gate)
        {
            if (!File.Exists(path))
                return new();
            var all = DailyJson.TryRead<List<DailyAccountProfile>>(path) ?? throw new InvalidDataException("账号偏好暂时无法读取；已保留原文件，请关闭占用它的程序后重试。");
            if (all.Any(p => p == null || !ValidKey(p.AccountKey) || (p.PlayerKey.Length > 0 && !ValidKey(p.PlayerKey))) || all.Select(p => p.AccountKey).Distinct().Count() != all.Count)
                throw new InvalidDataException("账号偏好中的身份记录无效；已保留原文件。");
            return all;
        }
    }
    public void Update(DailyAccountProfile profile)
    {
        lock (gate)
        {
            if (!ValidKey(profile.AccountKey))
                throw new InvalidDataException("Invalid account identity");
            var all = Read();
            all.RemoveAll(p => p.AccountKey == profile.AccountKey);
            all.Add(profile);
            DailyJson.Write(path, all);
        }
    }
    public static bool ValidKey(string? key) => key?.Length == 64 && key.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
}
public sealed record DailyTaskEntry(string AccountKey, string PlayerKey, string Cycle, string TaskId, string State, DateTimeOffset AtUtc, string Detail);
public sealed class DailyTaskLedger
{
    private readonly string root;
    public DailyTaskLedger(string root)
    {
        this.root = Path.Combine(root, "accounts");
    }
    public void Record(DailyTaskEntry entry)
    {
        Validate(entry.AccountKey, entry.PlayerKey, entry.Cycle, entry.TaskId);
        DailyJson.Write(Path.Combine(root, entry.AccountKey, entry.PlayerKey, entry.Cycle, entry.TaskId + ".json"), entry);
    }
    public DailyTaskEntry? Read(string account, string player, string cycle, string task)
    {
        Validate(account, player, cycle, task);
        return DailyJson.TryRead<DailyTaskEntry>(Path.Combine(root, account, player, cycle, task + ".json"));
    }
    private static void Validate(string account, string player, string cycle, string task)
    {
        if (!DailyProfiles.ValidKey(account) || !DailyProfiles.ValidKey(player) || !System.Text.RegularExpressions.Regex.IsMatch(cycle, @"^\d{4}-\d{2}-\d{2}$") || !DateOnly.TryParseExact(cycle, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) || !System.Text.RegularExpressions.Regex.IsMatch(task, @"^[a-z][a-z0-9_.-]{0,63}$"))
            throw new InvalidDataException("Invalid task ledger scope");
    }
}

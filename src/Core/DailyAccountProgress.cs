using System.Text.Json.Nodes;
using Dustweave.Accounts;
using static Dustweave.DailyData;

namespace Dustweave;

// Transfer account progress, never game sessions, command journals or control locks.
// Only the ordinary host reads the registered sandbox's physical files.
public static class DailyAccountProgress
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, DateTimeOffset> imported = [];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> scheduled = new();
    public static string SandboxRoot(string account) => Path.Combine(DailySandbox.StateDirectory(account), "root", "user", "current", "AppData", "Local", "BD2DailyAssistant");
    public static void ScheduleImport(string root, string account, bool force = false)
    {
        if (!DailyProfiles.ValidKey(account)) return;
        string key = Path.GetFullPath(root) + "|" + account;
        if (!scheduled.TryAdd(key, 0)) return;
        _ = Task.Run(() => { try { ImportSandbox(root, account, force); } catch (Exception e) when (Recoverable(e)) { Report(root, e); } finally { scheduled.TryRemove(key, out _); } });
    }
    public static void ImportSandbox(string root, string account, bool force = false)
    {
        // Isolated tests/custom data roots must never discover real account files.
        string ordinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
        if (!DailyProfiles.ValidKey(account) || !Path.GetFullPath(root).Equals(ordinary, StringComparison.OrdinalIgnoreCase)
            || SandboxProcessScope.CurrentBox.Length != 0) return;
        lock (gate)
        {
            if (!force && imported.TryGetValue(account, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(30)) return;
            imported[account] = DateTimeOffset.UtcNow;
            var marker = DailyJson.TryRead<DailySandboxRequest>(Path.Combine(DailySandbox.StateDirectory(account), "instance.json"));
            if (marker?.Account != account) return;
            try { Copy(SandboxRoot(account), root, account); }
            catch (Exception e) when (Recoverable(e)) { Report(root, e); }
        }
    }
    public static void ExportSandbox(string root, string account)
    {
        try { Copy(root, SandboxRoot(account), account); }
        catch (Exception e) when (Recoverable(e)) { Report(root, e); }
    }
    private static bool Recoverable(Exception e) => e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException;
    internal static void Report(string root, Exception e)
    {
        try { DailyJson.Write(Path.Combine(root, "progress-sync-error.json"), new { at = DateTimeOffset.UtcNow, message = e.Message }); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
    public static void Copy(string source, string target, string account)
    {
        if (!DailyProfiles.ValidKey(account)) throw new InvalidDataException("Invalid progress account");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        try { DailyAbsorptionLog.Copy(source, target, account); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        { Report(target, e); }
        DailyParallelProgress.Import(target, source, account);
        string history = DailyQueueHistory.Resolve(source, account);
        var context = history.Length > 0 ? DailyJson.TryRead<JsonObject>(history)?["context"] as JsonObject : null;
        string targetHistory = DailyQueueHistory.Resolve(target, account);
        var targetContext = targetHistory.Length > 0 ? DailyJson.TryRead<JsonObject>(targetHistory)?["context"] as JsonObject : null;
        string directory = Path.Combine(source, "live", "server-collection", account);
        if (!Directory.Exists(directory)) return;
        // The two newest server weeks cover rollover without scanning historic logs.
        foreach (var week in new DirectoryInfo(directory).EnumerateDirectories().Where(d => d.Name.Length > 0 && d.Name.All(char.IsAsciiDigit)).OrderByDescending(d => d.Name, StringComparer.Ordinal).Take(2))
        foreach (var file in week.EnumerateFiles("*.json"))
        {
            var data = Read(file.FullName, account, week.Name, context);
            if (data == null) continue;
            string destination = Path.Combine(target, "live", "server-collection", account, week.Name, file.Name);
            lock (DailyCollectionCache.Gate)
            {
                var old = Read(destination, account, week.Name, targetContext);
                if (old != null && (!JsonNode.DeepEquals(old["player"], data["player"]) || !JsonNode.DeepEquals(old["server"], data["server"]))) continue;
                var merged = DailyCollectionCache.Merge(old, data);
                if (!JsonNode.DeepEquals(old, merged)) DailyJson.Write(destination, merged);
            }
        }
    }
    private static double D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : 0;
    private static JsonObject? Read(string path, string account, string week, JsonObject? context)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = DailyJson.TryRead<JsonObject>(path);
            if (data == null || N(data["schema"]) != 1 || S(data["source"]) != "FieldObjectInfoResponse+MonsterInfoResponse"
                || S(data["account"]) != account || S(data["player"]).Length == 0 || D(data["at"]) <= 0
                || data["network"] is not JsonObject network || network["Maps"] is not JsonArray maps || S(network["Week"]) != week
                || !long.TryParse(Path.GetFileNameWithoutExtension(path), out long pack) || N(network["Pack"]) != pack) return null;
            if (S(data["server"]).Length == 0 && context?["actor"] is JsonArray { Count: 5 } actor
                && S(actor[3]) == account && JsonNode.DeepEquals(actor[4], data["player"])) data["server"] = DailyData.Copy(context["server"]);
            if (S(data["server"]).Length == 0) return null;
            DailyCollectionProgress.Classify(network, pack, maps.Select(N).ToArray(), false, false);
            return data;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { return null; }
    }
}

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
namespace Dustweave;

// The workflow, not a successful button dispatch, decides when diagnostic frames
// can be discarded. Files remain crash-safe while a stage is running.
public sealed class DailyLogEvidence : IDisposable
{
    static readonly AsyncLocal<DailyLogEvidence?> Current = new();
    readonly DailyLogEvidence? parent;
    readonly string root;
    readonly ConcurrentDictionary<string, byte> written = new(StringComparer.OrdinalIgnoreCase);
    bool complete, disposed;
    public DailyLogEvidence(string root)
    {
        this.root = Path.GetFullPath(root);
        parent = Current.Value;
        Current.Value = this;
    }
    public void Complete() => complete = true;
    internal static void Written(string path)
    {
        var scope = Current.Value;
        if (scope != null && !scope.disposed && path.StartsWith(scope.root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            scope.written.TryAdd(path, 0);
    }
    internal static string Text(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    internal static bool Settled(JsonObject value) => Text(value["state"]) == "completed"
        && Text(value["presentation"]) is "" or "settled" or "completed"
        && (Text(value["role"]) is not (DailyFreeDrawProof.Role or DailyFriendshipProof.Role or DailyManagementProof.Role
            or DailyManagementProof.Helpers or DailyManagementProof.Cafeteria or DailyManagementProof.Fishing)
            || value["presentation_closed"] is JsonValue closed && closed.TryGetValue<bool>(out var yes) && yes);

    // Keep native values, identity, request/response ordering and business proof.
    // Only UI trees and redundant snapshots are removed from settled recovery data.
    internal static void RemoveUiTrees(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("ProcessId") && obj.ContainsKey("Instance"))
            {
                obj.Remove("Surfaces");
                obj.Remove("Targets");
                obj.Remove("SquareNavigation");
            }
            foreach (var pair in obj.ToArray()) RemoveUiTrees(pair.Value);
        }
        else if (node is JsonArray array) foreach (var child in array) RemoveUiTrees(child);
    }
    static void Summary(JsonObject value)
    {
        foreach (string key in new[] { "before", "after", "opening_frame", "last_observed", "transport", "events", "last_observation", "preview_frame", "preview_opening_frame", "presentation_frame" }) value.Remove(key);
        value["diagnostic_storage"] = "summary-v1";
    }
    static bool SafePath(string root, string path)
    {
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (string? p = path; p != null; p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    internal static bool CompactRecord(string root, string path, bool workflowCompleted, bool expired = false)
    {
        if (!workflowCompleted || !SafePath(root, path)) return false;
        string tree = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        var value = DailyJson.TryRead<JsonObject>(path);
        if (value == null || Text(value["diagnostic_storage"]) == "summary-v1" || value.ContainsKey("diagnostic_storage") && !expired) return false;
        if (tree is "weekly-npc-queries" or "reward-queries")
        {
            if (tree == "weekly-npc-queries" && Text(value["state"]) != "completed") return false;
            // Query results may themselves contain an entire inventory. Keep a count,
            // not another full response; the workflow consumes the original in memory.
            value["result_fields"] = value["result"] is JsonObject result ? result.Count : 0;
            value.Remove("result"); Summary(value);
        }
        else if (tree is "business" or "managed-business")
        {
            // Conflicting/duplicated journals are deliberately compared byte-for-
            // value by recovery. Do not rewrite one side of that comparison.
            string peer = Path.Combine(root,"live",tree == "business" ? "managed-business" : "business",Path.GetFileName(path));
            if(File.Exists(peer))return false;
            if (expired ? Text(value["state"]) is not ("completed" or "rejected" or "server_rejected" or "superseded") : !Settled(value)) return false;
            if (expired)
            {
                // Historical ownership checks still inspect before.Frame. Keep its
                // identity and configuration, but not inventories or UI snapshots.
                var snapshots = new Dictionary<string,JsonObject>();
                foreach (string key in new[] { "before", "after" })
                    if (value[key] is JsonObject snapshot)
                    {
                        snapshots[key] = snapshot.DeepClone().AsObject();
                        snapshots[key].Remove("Readings"); snapshots[key].Remove("Taps");
                        RemoveUiTrees(snapshots[key]);
                    }
                Summary(value);
                foreach (var pair in snapshots) value[pair.Key] = pair.Value;
                RemoveUiTrees(value);
            }
            else
            {
                value.Remove("last_observation"); value.Remove("preview_frame"); value.Remove("opening_frame"); value.Remove("preview_opening_frame"); value.Remove("presentation_frame");
                RemoveUiTrees(value);
                value["diagnostic_storage"] = "recovery-facts-v1";
            }
        }
        else if (tree == "event-journal" && string.IsNullOrEmpty(Text(value["Error"])) && Text(value["Kind"]) is "request" or "response")
        {
            RemoveUiTrees(value);
            value["diagnostic_storage"] = "recovery-facts-v1";
        }
        else if (tree == "field-talents" && (expired || Text(value["state"]) == "completed")
            && value["transport"]?["command"] is JsonObject command && BusinessResolved(root, command))
        {
            if (expired) Summary(value);
            else { RemoveUiTrees(value); value["diagnostic_storage"] = "recovery-facts-v1"; }
        }
        else return false;
        var writtenAt = File.GetLastWriteTimeUtc(path);
        DailyJson.Write(path, value);
        File.SetLastWriteTimeUtc(path, writtenAt); // Compaction is not new evidence.
        return true;
    }
    internal static bool BusinessResolved(string root, JsonObject? intent)
    {
        string reason = Text(intent?["Reason"]);
        int start = reason.IndexOf("business:", StringComparison.Ordinal);
        if (start < 0) return true;
        string id = reason[(start + 9)..].Split('|')[0];
        if (!GuildStore.ValidId(id)) return false;
        bool found = false;
        foreach (string tree in new[] { "business", "managed-business" })
        {
            string path = Path.Combine(root, "live", tree, id + ".json");
            if (!File.Exists(path)) continue;
            if (!SafePath(root, path)) return false;
            var record = DailyJson.TryRead<JsonObject>(path);
            if (record == null || Text(record["state"]) is not ("completed" or "rejected" or "server_rejected" or "superseded")) return false;
            found = true;
        }
        return found;
    }
    internal static bool CompactStep(string root, string folder, bool workflowCompleted)
    {
        var intent = DailyJson.TryRead<JsonObject>(Path.Combine(folder, "intent.json"));
        string path = Path.Combine(folder, "result.json");
        var result = DailyJson.TryRead<JsonObject>(path);
        if (result == null || result.ContainsKey("diagnostic_storage") || !BusinessResolved(root, intent)
            || Text(result["state"]) != "observed_expected_ui" && !(workflowCompleted && Text(result["state"]) == "dispatched_only")) return false;
        if (!SafePath(root, path)) return false;
        Summary(result);
        // Retain the receipt for pending talent/route recovery; the full command is
        // still in intent.json. No in-memory result or native receipt is altered.
        RemoveUiTrees(result);
        var writtenAt = File.GetLastWriteTimeUtc(path);
        DailyJson.Write(path, result);
        File.SetLastWriteTimeUtc(path, writtenAt);
        foreach (string name in new[] { "before.json", "transport.json" })
        {
            string old = Path.Combine(folder, name);
            if (!File.Exists(old) || !SafePath(root, old)) continue;
            var f = new FileInfo(old);
            DailyLogFileRemoval.Delete(new(old, f.Length, f.LastWriteTimeUtc, f.CreationTimeUtc));
        }
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Current.Value = parent;
        if (written.IsEmpty) return;
        // Only enqueue metadata on the execution thread. File compaction runs on
        // the next idle tick and survives restart, without delaying the next task.
        Try(() =>
        {
            DailyJson.Write(Path.Combine(root,"live","log-compaction",Guid.NewGuid().ToString("N")+".json"),
                new { complete, paths = written.Keys.Select(p => Path.GetRelativePath(root,p)).ToArray() });
            return true;
        });
    }
    static void CompactWritten(string root, string[] written, bool complete, CancellationToken token)
    {
        // Diagnostics must never turn an otherwise completed stage into a failure.
        // Compaction is bounded to files produced by this stage, never a whole-root scan.
        var steps = written.Where(p => string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(p)), Path.Combine(root, "live", "steps"),StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetDirectoryName(p)!).Distinct().OrderBy(p => File.GetLastWriteTimeUtc(Path.Combine(p, "intent.json"))).ToArray();
        var preserve = new HashSet<int>();
        for (int i = 0; i < steps.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var result = DailyJson.TryRead<JsonObject>(Path.Combine(steps[i], "result.json"));
            if (Text(result?["state"]) is not ("observed_expected_ui" or "dispatched_only") || !complete && i >= steps.Length - 3)
                for (int j = Math.Max(0, i - 3); j <= Math.Min(steps.Length - 1, i + 2); j++) preserve.Add(j);
        }
        for (int i = 0; i < steps.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!preserve.Contains(i)) CompactStep(root, steps[i], complete);
        }
        if (complete) foreach (string path in written)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) CompactRecord(root, path, true);
        }
    }
    public static bool HasPending(string root)
    {
        try { return Directory.Exists(Path.Combine(root,"live","log-compaction"))
            && Directory.EnumerateFiles(Path.Combine(root,"live","log-compaction"),"*.json").Any(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { return false; }
    }
    public static Task FlushAsync(string root, CancellationToken token = default) => Task.Run(() =>
    {
        root=Path.GetFullPath(root);string folder=Path.Combine(root,"live","log-compaction");
        if(!Directory.Exists(folder)||!SafePath(root,folder))return;
        foreach(string file in Directory.EnumerateFiles(folder,"*.json"))
        {
            token.ThrowIfCancellationRequested();
            Try(() =>
            {
                if(!SafePath(root,file))return false;
                var value=DailyJson.TryRead<JsonObject>(file);
                if(value?["paths"] is not JsonArray paths || paths.Count>100000 || value["complete"] is not JsonValue finished || !finished.TryGetValue<bool>(out var complete))return false;
                string[] records=paths.Select(p=>Text(p)).Select(p=>Path.GetFullPath(Path.Combine(root,p)))
                    .Where(p=>p.StartsWith(Path.Combine(root,"live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(p)&&SafePath(root,p)).ToArray();
                CompactWritten(root,records,complete,token);
                var f=new FileInfo(file);
                return DailyLogFileRemoval.Delete(new(file,f.Length,f.LastWriteTimeUtc,f.CreationTimeUtc));
            });
        }
    },token);
    // Legacy successful journals receive the same policy on the next idle sweep.
    // Pending operations stay independent of diagnostic retention: deleting their
    // proof would make safe recovery impossible. They are never classified as success.
    public static Task MaintainAsync(string root, CancellationToken token = default) => Task.Run(() =>
    {
        root = Path.GetFullPath(root);
        foreach (string tree in new[] { "business", "managed-business", "field-talents" })
        {
            string folder = Path.Combine(root, "live", tree);
            if (!Directory.Exists(folder) || !SafePath(root, folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
            {
                token.ThrowIfCancellationRequested();
                if (File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-7)) continue;
                Try(() => CompactRecord(root, path, true, expired: true));
            }
        }
        string recovery=Path.Combine(root,"live","step-recovery");
        if(Directory.Exists(recovery)&&SafePath(root,recovery))foreach(string path in Directory.EnumerateFiles(recovery,"*.json"))
        {
            token.ThrowIfCancellationRequested();
            Try(() =>
            {
                if(!SafePath(root,path))return false;
                var value=DailyJson.TryRead<JsonObject>(path);
                if(value?["intent"] is not JsonObject intent || !BusinessResolved(root,intent))return false;
                var f=new FileInfo(path);
                return DailyLogFileRemoval.Delete(new(path,f.Length,f.LastWriteTimeUtc,f.CreationTimeUtc));
            });
        }
    }, token);
    static void Try(Func<bool> action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException) { }
    }
}

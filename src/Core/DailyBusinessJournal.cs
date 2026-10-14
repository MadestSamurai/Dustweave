using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Dustweave;

public sealed partial class DailyManagedBusiness
{
    // Retain only routing/state metadata, not the large captured frames in each record.
    // Discover history once. Kernel change notifications refresh only changed
    // paths; a cached lookup never opens every historical file for its stamp.
    private readonly Dictionary<string, JournalHeader> journalHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DailyDirectoryChanges> journalChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> journalPolling = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> journalChanged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (JournalStamp First, JournalStamp Second)> journalDuplicates = new(StringComparer.OrdinalIgnoreCase);
    private bool journalLoaded, journalDisposed;
    private bool journalRefresh;
    internal int JournalFullScans { get; private set; }
    internal int JournalMetadataReads { get; private set; }
    private sealed record JournalStamp(long Length, long Written, long Created, uint Volume, ulong FileId);
    private sealed record JournalHeader(JournalStamp Stamp, JsonObject Summary);
    [StructLayout(LayoutKind.Sequential)]
    private struct JournalFileInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJournalMetadata(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out JournalFileInfo info);
    private JournalStamp Stamp(string path)
    {
        JournalMetadataReads++;
        // Atomic replacement can preserve length and timestamps on a coarse Windows clock.
        // Query file identity without requesting data access; existing readers/writers stay shared.
        using var handle = OpenJournalMetadata(path, 0, 7, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            throw new IOException("无法读取业务记录的文件身份：" + Path.GetFileName(path), new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        static long Ticks(System.Runtime.InteropServices.ComTypes.FILETIME time) => ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
        return new(((long)info.SizeHigh << 32) | info.SizeLow, Ticks(info.Written), Ticks(info.Created), info.Volume, ((ulong)info.IdHigh << 32) | info.IdLow);
    }
    private void SyncJournalHeaders(bool interruptible)
    {
        ObjectDisposedException.ThrowIf(journalDisposed, this);
        bool rescan = !journalLoaded;
        try
        {
            foreach (string name in new[] { "managed-business", "business" })
            {
                string folder = Path.Combine(root, "live", name);
                if (!journalChanges.ContainsKey(folder)) rescan = true;
                CaptureJournalChanges(folder);
            }
            rescan |= journalRefresh;
            var changed = journalChanged.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (rescan)
            {
                JournalFullScans++;
                var paths = journalChanges.Keys.Concat(journalPolling).SelectMany(d => Directory.EnumerateFiles(d, "*.json")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string removed in journalHeaders.Keys.Where(p => !paths.Contains(p)).ToArray()) journalHeaders.Remove(removed);
                changed.UnionWith(paths);
            }
            int visited = 0;
            foreach (string path in changed)
            {
                if (interruptible && visited++ % 64 == 0) Stop();
                // File.Exists treats access errors as absence. Only actual
                // deletion may retire a cached intent; unreadable records retry.
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                        ReadHeader(path, journalRefresh || journalChanged.Contains(path));
                    else journalHeaders.Remove(path);
                }
                catch (FileNotFoundException) { journalHeaders.Remove(path); }
                catch (DirectoryNotFoundException) { journalHeaders.Remove(path); }
            }
            journalLoaded = true;
            journalRefresh = false;
            journalChanged.Clear();
        }
        catch { journalLoaded = false; journalRefresh = true; throw; } // Never lose an unreadable changed intent.
    }
    private void CaptureJournalChanges(string folder)
    {
        if (journalPolling.Contains(folder)) { Directory.CreateDirectory(folder); journalRefresh = true; return; }
        try
        {
            if (!journalChanges.TryGetValue(folder, out var watcher))
            {
                Directory.CreateDirectory(folder);
                // Arm BEFORE enumeration so writes during discovery are retained.
                journalChanges.Add(folder, watcher = new DailyDirectoryChanges(folder));
            }
            var delta = watcher.Poll();
            journalRefresh |= delta.Rescan;
            foreach (string file in delta.Names) journalChanged.Add(Path.Combine(folder, file));
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // Unsupported notifications must not strand a queue or trust
            // a stale index. Fall back to fresh reads for this session.
            if (journalChanges.Remove(folder, out var broken)) broken.Dispose();
            journalPolling.Add(folder);
            Directory.CreateDirectory(folder);
            journalRefresh = true;
        }
    }
    private KeyValuePair<string, JournalHeader>[] JournalEntries(string? role = null, bool includeLegacy = true, bool interruptible = false)
    {
        SyncJournalHeaders(interruptible);
        return journalHeaders.Where(p => (includeLegacy || Path.GetFileName(Path.GetDirectoryName(p.Key)) == "managed-business")
            && (role == null || p.Value.Summary["role"]?.GetValue<string>() == role)).ToArray();
    }
    public void Dispose()
    {
        if (journalDisposed) return;
        journalDisposed = true;
        foreach (var watcher in journalChanges.Values) watcher.Dispose();
        journalChanges.Clear();
        journalPolling.Clear();
        journalChanged.Clear();
        journalHeaders.Clear();
    }
    private static JsonObject Summary(JsonObject op)
    {
        var summary = new JsonObject();
        foreach (string key in new[] { "id", "role", "account", "player", "server", "cycle", "state", "presentation", "presentation_closed", "confirmation_not_submitted", "preview_cleanup" })
            summary[key] = op[key]?.DeepClone();
        // Only dispatch.start uses scope to distinguish a trade operation.
        summary["trade"] = DailyTradeJournal.IsTrade(op);
        if (op["role"]?.GetValue<string>() == "dispatch.start" && op["scope"] is JsonObject scope)
            summary["talent_kind"] = scope["kind"]?.DeepClone();
        // Completed journals may contain only compact snapshots. Location is
        // needed to guard unfinished work, never to re-prove a settled action.
        if (Pending(op))
        {
            if (DailyFieldTalentProof.AbsorptionLocation(op) is { } location)
                summary["absorption_map"] = location.Map;
            if (DailyPickupRecovery.Location(op) is { } pickup)
                summary["pickup_map"] = pickup.Map;
        }
        return summary;
    }
    private void RememberHeader(string path, JsonObject op)
    {
        // Consume our own already-known atomic save, retaining every other
        // changed path. Later external edits get their own notification.
        string folder = Path.GetDirectoryName(path)!;
        if (journalChanges.ContainsKey(folder)) CaptureJournalChanges(folder);
        journalChanged.Remove(path);
        journalHeaders[path] = new(Stamp(path), Summary(op));
        journalDuplicates.Remove(op["id"]!.GetValue<string>());
    }
    private JsonObject ReadHeader(string path, bool changed = false)
    {
        var stamp = Stamp(path);
        if (!changed && journalHeaders.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Summary;
        var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
        if (Stamp(path) != stamp)
            throw new StageHostException("pending", "业务记录正在更新；稍后重新核对，未重复提交。");
        var summary = Summary(op);
        journalHeaders[path] = new(stamp, summary);
        journalDuplicates.Remove(op["id"]?.GetValue<string>() ?? "");
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
    private bool HasUnresolvedRecord(JsonObject context, string role, JsonObject? requestedScope = null)
    {
        Stop();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in JournalEntries(role, interruptible: true))
        {
            string path = entry.Key;
            var op = entry.Value.Summary;
            DailyManagedReconciliation.ValidateRecord(path, op);
            string id = op["id"]!.GetValue<string>();
            if (seen.TryGetValue(id, out var previous))
            {
                // Conflicting copies remain an error even if their headers match.
                CheckJournalDuplicate(id, previous, path);
                continue;
            }
            seen.Add(id, path);
            recordPaths[id] = path;
            if (Pending(op) && op["trade"]?.GetValue<bool>() != true
                && JsonNode.DeepEquals(op["account"], context["actor"]![3])
                && JsonNode.DeepEquals(op["player"], context["actor"]![4])
                && JsonNode.DeepEquals(op["server"], context["server"]))
            {
                // Stealing uses the same network endpoint as research/absorption.
                if (role == "mainline.pickup" && op["pickup_map"] is JsonValue pickupMap && requestedScope?["map"] != null
                    && DailyData.N(requestedScope["map"]) != DailyData.N(pickupMap)) continue;
                // Absorption uncertainty is likewise local to its map. A new
                // cast there remains blocked until a fresh server map query.
                if (role == "dispatch.start" && op["absorption_map"] is JsonValue absorptionMap
                    && requestedScope?["kind"] != null && requestedScope["map"] != null
                    && (DailyData.N(requestedScope["kind"]) != 4 || DailyData.N(requestedScope["map"]) != DailyData.N(absorptionMap))) continue;
                // An unknown steal still blocks more theft, not unrelated talents.
                if (role == "dispatch.start" && DailyData.N(op["talent_kind"]) == 1 && requestedScope?["kind"] is JsonValue kind &&
                    kind.TryGetValue<int>(out var requested) && requested is 2 or 3 or 4 or 6 or 17 or 20) continue;
                return true;
            }
        }
        return false;
    }
    private void CheckJournalDuplicate(string id, string first, string second)
    {
        var stamps = (journalHeaders[first].Stamp, journalHeaders[second].Stamp);
        if (journalDuplicates.TryGetValue(id, out var prior) && prior == stamps) return;
        if (!JsonNode.DeepEquals(ReadJournalRecord(first), ReadJournalRecord(second)))
            throw new StageHostException("pending", "发现同一操作的冲突记录，未覆盖或重复提交：" + id);
        journalDuplicates[id] = stamps;
    }
}

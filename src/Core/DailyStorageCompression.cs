using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Dustweave;

/// <summary>Byte-preserving, bounded NTFS maintenance. No archive format or recovery-reader migration.</summary>
public static class DailyStorageCompression
{
    const int MinimumBytes = 16 * 1024, MaximumFileBytes = 32 * 1024 * 1024;
    static readonly HashSet<string> LiveTrees = new(StringComparer.OrdinalIgnoreCase)
    {
        "steps", "business", "managed-business", "field-talents", "weekly-npc-queries", "reward-queries",
        "event-journal", "diagnostics", "events", "receipts", "claimed", "queues", "workflows",
        "event-battles", "trade-closes", "trade-previews", "trade-quote-reads", "equipment-plans", "tactics"
    };
    static readonly HashSet<string> TradeTrees = new(StringComparer.OrdinalIgnoreCase)
        { "executions", "diagnostics", "plans", "capture", "data-preflights" };
    static readonly JsonSerializerOptions Compact = new(DailyJson.Options) { WriteIndented = false };
    static readonly ConcurrentDictionary<string,int> BatchOffsets = new(StringComparer.OrdinalIgnoreCase);

    internal static bool MachineRecord(string path)
    {
        var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar);
        for (int i = 0; i + 2 < parts.Length; i++)
            if (parts[i].Equals("live", StringComparison.OrdinalIgnoreCase) && LiveTrees.Contains(parts[i + 1])
                || parts[i].Equals("trade", StringComparison.OrdinalIgnoreCase) && TradeTrees.Contains(parts[i + 1])) return true;
        return false;
    }
    internal static JsonSerializerOptions WriteOptions(string path) => MachineRecord(path) ? Compact : DailyJson.Options;
    static bool RecordInRoot(string root, string path)
    {
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        return parts.Length >= 3 && (parts[0].Equals("live", StringComparison.OrdinalIgnoreCase) && LiveTrees.Contains(parts[1])
            || parts[0].Equals("trade", StringComparison.OrdinalIgnoreCase) && TradeTrees.Contains(parts[1]));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetCompressedFileSizeW(string path, out uint high);
    [StructLayout(LayoutKind.Sequential)] struct FileInfoNative
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle Open(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetFileTime(SafeFileHandle handle, IntPtr created, IntPtr accessed,
        ref System.Runtime.InteropServices.ComTypes.FILETIME written);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle file, uint code, ref ushort input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    // Ordinary files retain the existing logical-size convention. Compressed/sparse
    // files must use their stored size, otherwise cleanup appears to save nothing.
    public static long StoredBytes(string path, long length, FileAttributes attributes)
    {
        if (!OperatingSystem.IsWindows() || (attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) == 0) return length;
        Marshal.SetLastPInvokeError(0);
        uint low = GetCompressedFileSizeW(path, out uint high);
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0) return length;
        ulong value = ((ulong)high << 32) | low;
        return value <= long.MaxValue ? (long)value : length;
    }
    static bool Within(string path, string root) => path.Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar)
        .StartsWith(Path.TrimEndingDirectorySeparator(root.Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static DailyStorageItem[] Plan(string root, IEnumerable<DailyStorageItem> files, DateTime now)
    {
        root = Path.GetFullPath(root);
        if (!Supported(root)) return [];
        return files.Where(f => f.Bytes >= MinimumBytes && f.Bytes <= MaximumFileBytes && f.Written < now.AddDays(-1)
            && Within(f.Path, root) && (RecordInRoot(root, f.Path) && Path.GetExtension(f.Path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                || DailyCacheStorage.Areas.Any(area => Within(f.Path, Path.Combine(root, area)))
                    && Path.GetExtension(f.Path).ToLowerInvariant() is ".json" or ".dll"))
            .Where(f => EligibleAttributes(f.Path))
            .OrderByDescending(f => f.Bytes).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Supported(string path)
    {
        try { return OperatingSystem.IsWindows() && !Path.GetFullPath(path).StartsWith(@"\\",StringComparison.Ordinal)
            && new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    static bool EligibleAttributes(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Compressed | FileAttributes.Encrypted | FileAttributes.ReadOnly | FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.SparseFile)) == 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    public static DailyLogCleanupResult Apply(IEnumerable<DailyStorageItem> candidates, CancellationToken token = default, string? scope = null)
    {
        var files = candidates.ToArray();
        string? key = scope == null ? null : Path.GetFullPath(scope);
        int offset = key != null && BatchOffsets.TryGetValue(key, out int previous) && files.Length > 0 ? previous % files.Length : 0;
        int attempted = 0, compressed = 0, skipped = 0; long processed = 0, saved = 0;
        bool more = false;
        for (int visited = 0; visited < files.Length; visited++)
        {
            token.ThrowIfCancellationRequested();
            if (attempted >= 128 || processed >= 32L * 1024 * 1024) { more = true; break; }
            var file = files[(offset + visited) % files.Length];
            attempted++; processed += file.Bytes;
            try
            {
                DailyCacheStorage.NoLinks(file.Path);
                var info = new FileInfo(file.Path);
                if (file.Bytes < MinimumBytes || file.Bytes > MaximumFileBytes || file.Written >= DateTime.UtcNow.AddDays(-1)
                    || !EligibleAttributes(file.Path) || info.Length != file.Bytes || info.LastWriteTimeUtc != file.Written) { skipped++; continue; }
                // A reader/writer which cannot coexist defers this file; never wait on it.
                using var handle = Open(file.Path, 0xC0000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                if(handle.IsInvalid || !GetFileInformationByHandle(handle,out var native)
                    || native.Links!=1 || (native.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory))!=0) { skipped++; continue; }
                long written = (long)(uint)native.Written.dwHighDateTime << 32 | (uint)native.Written.dwLowDateTime;
                if (((long)native.SizeHigh<<32|native.SizeLow) != file.Bytes || written != file.Written.ToFileTimeUtc()) { skipped++; continue; }
                ushort format = 1; // COMPRESSION_FORMAT_DEFAULT; normal File APIs transparently decompress.
                if (!DeviceIoControl(handle, 0x0009C040, ref format, 2, IntPtr.Zero, 0, out _, IntPtr.Zero)) { skipped++; continue; }
                // Compression is not a new observation and must not restart log retention.
                if (!SetFileTime(handle, IntPtr.Zero, IntPtr.Zero, ref native.Written))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                info.Refresh();
                long after = StoredBytes(file.Path, info.Length, info.Attributes);
                saved += Math.Max(0, file.DiskBytes - after); compressed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { skipped++; }
        }
        // A busy prefix must not starve later files. The cursor is process-local and is
        // unrelated to task recovery; a restart can safely scan the candidates again.
        // After a complete pass with no progress, return to the normal maintenance
        // interval instead of retrying permanently unsupported/busy files every five minutes.
        if (compressed == 0 && offset + attempted >= files.Length) more = false;
        if (key != null)
        {
            if (more) BatchOffsets[key] = (offset + attempted) % files.Length;
            else BatchOffsets.TryRemove(key, out _);
        }
        return new(0, saved, skipped) { CompressedFiles = compressed, MoreWork = more };
    }
}

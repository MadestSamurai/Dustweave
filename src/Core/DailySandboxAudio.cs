using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Dustweave;

/// <summary>Remove an idle sandbox's byte-identical bank overlay, never the host bank or a catalog.
/// Uses ordinary Sandboxie copy-on-write afterwards; no shared writable paths, links or read-only rules.</summary>
internal static class DailySandboxAudio
{
    internal sealed record Candidate(string Relative, long Bytes, long StoredBytes, DateTime Written);
    internal static readonly string RelativeRoot = Path.Combine("user", "current", "AppData", "LocalLow", "Gamfs", "BrownDust II", "SoundData");
    internal static bool Bank(string relative)
    {
        string[] p = relative.Replace('\\', '/').Split('/');
        if (p.Length != 3 || p[0] != "Patched" || p[2].Length != 40 || !p[2].All(char.IsAsciiHexDigit)) return false;
        string group = p[1];
        if (group == "Sound" || group == "SoundMaster" || group == "LocalSound") return true;
        if (group.StartsWith('P'))
        {
            int end = group.IndexOf('_');
            if (end < 2 || !group[1..end].All(char.IsAsciiDigit)) return false;
            group = group[(end + 1)..];
        }
        return group.StartsWith("LocalVoice_", StringComparison.Ordinal) && group.Length > 11 && group[11..].All(char.IsAsciiLetter);
    }
    internal static Candidate[] Candidates(string overlay, CancellationToken token)
    {
        int skipped = 0;
        return DailyCacheStorage.Files(overlay, token, ref skipped).Where(f => Bank(Path.GetRelativePath(overlay, f.Path)))
            .Select(f => new Candidate(Path.GetRelativePath(overlay, f.Path), f.Bytes, f.DiskBytes, f.Written))
            .OrderBy(f => f.Relative, StringComparer.Ordinal).ToArray();
    }
    // Sandboxie's V1 deletion marker can live on an ancestor directory. A real
    // bank beneath that directory does not imply the host bank is visible there.
    internal const long DeletedCreationTime = 0x01B01234DEAD44A0;
    internal static bool HostVisible(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetCreationTimeUtc(current).ToFileTimeUtc() == DeletedCreationTime) return false;
        return true;
    }
    internal static string Native(string path) => Path.GetFullPath(path).StartsWith(@"\\", StringComparison.Ordinal)
        ? @"\\?\UNC\" + Path.GetFullPath(path)[2..] : @"\\?\" + Path.GetFullPath(path);
    [StructLayout(LayoutKind.Sequential)] struct Info
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle Open(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref uint value, uint size);
    static SafeFileHandle FileHandle(string path, bool remove)
    {
        DailyCacheStorage.NoLinks(path);
        var handle = Open(Native(path), 0x80000000u | (remove ? 0x10000u : 0), 1, 0, 3, 0x00200000 | 0x08000000, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("resource.file_busy"); }
        if (!GetFileInformationByHandle(handle, out var info) || info.Links != 1
            || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Encrypted)) != 0)
        { handle.Dispose(); throw new IOException("resource.file_changed"); }
        return handle;
    }
    static bool Riff(FileStream file)
    {
        Span<byte> header = stackalloc byte[4]; file.Position = 0;
        bool ok = file.Read(header) == 4 && header.SequenceEqual("RIFF"u8); file.Position = 0; return ok;
    }
    static byte[] Hash(FileStream file, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024]; int count;
        while ((count = file.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return hash.GetHashAndReset();
    }
    // Hold both catalogs against writes/deletion for the complete comparison and deletion.
    // Missing/new/unequal catalogs mean retain banks until a later successful comparison.
    static List<FileStream>? Catalogs(string overlay, string source)
    {
        var leases = new List<FileStream>();
        try
        {
            foreach (string name in new[] { "catalog_sound_hd.json", "catalog_sound_sd.json" })
            {
                string local = Path.Combine(overlay, name), host = Path.Combine(source, name);
                if (!File.Exists(local)) continue;
                foreach (string file in new[] { local, host })
                {
                    var stream = new FileStream(FileHandle(file, false), FileAccess.Read); leases.Add(stream);
                    if (stream.Length > 1024 * 1024) return Close();
                }
                var a = leases[^2]; var b = leases[^1];
                using var catalog = JsonDocument.Parse(a);
                if (!catalog.RootElement.TryGetProperty("catalogVersion", out var version) || version.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(version.GetString())) return Close();
                a.Position = 0;
                if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b))) return Close();
            }
            return leases.Count > 0 ? leases : Close();
        }
        catch { Close(); throw; }
        List<FileStream>? Close() { foreach (var file in leases) file.Dispose(); return null; }
    }
    internal static long? Compare(string overlay, string source, Candidate candidate, bool remove, Func<bool> idle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !Bank(candidate.Relative) || !idle()) return null;
        DailyCacheStorage.NoLinks(overlay); DailyCacheStorage.NoLinks(source);
        string localPath = Path.Combine(overlay, candidate.Relative);
        if (!HostVisible(localPath)) return null;
        var catalogs = Catalogs(overlay, source);
        if (catalogs == null) return null;
        try
        {
            using var host = new FileStream(FileHandle(Path.Combine(source, candidate.Relative), false), FileAccess.Read);
            using var copy = new FileStream(FileHandle(Path.Combine(overlay, candidate.Relative), remove), FileAccess.Read);
            if (host.Length != candidate.Bytes || copy.Length != candidate.Bytes || !Riff(host) || !Riff(copy)) return null;
            if (!Hash(host, token).AsSpan().SequenceEqual(Hash(copy, token))) return null;
            token.ThrowIfCancellationRequested();
            if (!idle() || !HostVisible(localPath)) return null;
            var current = new FileInfo(Path.Combine(overlay, candidate.Relative));
            long stored = DailyStorageCompression.StoredBytes(current.FullName, current.Length, current.Attributes);
            if (!remove) return stored;
            // Delete the already-open, verified file, not whatever may occupy its pathname later.
            uint disposition = 1;
            if (!SetFileInformationByHandle(copy.SafeFileHandle, 4, ref disposition, 4)) throw new IOException("resource.delete_deferred");
            return stored;
        }
        finally { foreach (var file in catalogs) file.Dispose(); }
    }
    // Unknown access rules are scoped to this optional optimization. Never alter the rules or block launch.
    internal static bool NormalView(string source, Func<string, string> read)
    {
        if (new[] { "UseFileImage", "UseFileDeleteV2" }.Any(k =>
            read(k).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() is { Length: > 0 } value
                && !value.Equals("n", StringComparison.OrdinalIgnoreCase))) return false;
        foreach (string key in new[] { "OpenFilePath", "OpenPipePath", "WriteFilePath", "ReadFilePath", "ClosedFilePath", "NormalFilePath" })
            foreach (string value in read(key).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int comma = value.LastIndexOf(',');
                if (comma > 0)
                {
                    string process = value[..comma].Trim();
                    if (process.IndexOfAny(['*', '?', '!', '<', '>']) < 0
                        && !process.Equals("BrownDust II.exe", StringComparison.OrdinalIgnoreCase)) continue;
                }
                string pattern = value[(value.LastIndexOf(',') + 1)..].Trim().Replace('/', '\\');
                if (pattern.Length == 0) continue;
                // Resolve the common environment macros. Sandboxie-specific or unresolved macros cannot prove the view.
                pattern = Environment.ExpandEnvironmentVariables(pattern);
                if (pattern.Contains('%')) return false;
                int wildcard = pattern.IndexOfAny(['*', '?']);
                string prefix = wildcard < 0 ? pattern.TrimEnd('\\') : pattern[..wildcard].TrimEnd('\\');
                string path = Path.GetFullPath(source).TrimEnd('\\');
                if (prefix.Length == 0 || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return false;
            }
        return true;
    }
}

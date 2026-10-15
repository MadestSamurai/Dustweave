using System.Security.Cryptography;
using System.Text;

namespace Dustweave.Maintenance;

// Shared source: the host also imports logs when the equipment window is never opened.
// The old tool root is still the IPC/recovery root; it must never be deleted wholesale.
internal static class EquipmentLogs
{
    static readonly string[] Folders = ["jobs", "receipts", "live/steps", "diagnostics"];
    static readonly string[] Files = ["capture-evidence.json", "connection-cleanup.log"];
    static bool OrdinaryPath(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    static bool Log(string path) => Path.GetExtension(path).ToLowerInvariant() is ".json" or ".jsonl" or ".log"
        || path.EndsWith(".log.previous", StringComparison.OrdinalIgnoreCase);

    public static bool HasLegacy(string source)
    {
        try { return OrdinaryPath(source) && (Folders.Any(p => Directory.Exists(Path.Combine(source, p))) || Files.Any(p => File.Exists(Path.Combine(source, p)))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void Import(string source, string hostRoot, CancellationToken token = default)
    {
        source = Path.GetFullPath(source);
        string destination = Path.GetFullPath(Path.Combine(hostRoot, "live", "diagnostics", "equipment"));
        if (!Directory.Exists(source) || !OrdinaryPath(source) || !OrdinaryPath(destination)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;

        void Move(string path)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path) || !Log(path) || !OrdinaryPath(path) || !OrdinaryPath(destination)) return;
                Directory.CreateDirectory(destination);
                string relative = Path.GetRelativePath(source, path);
                string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16];
                string target = Path.Combine(destination, "legacy-" + id + "-" + Path.GetFileName(path));
                if (File.Exists(target)) target = Path.Combine(destination, "legacy-" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
                // Move preserves the original age, so importing never restarts retention.
                File.Move(path, target, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Active files are retried on the next idle pass. */ }
        }
        void Walk(string folder, int depth)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(folder) || !OrdinaryPath(folder)) return;
                foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (item is FileInfo) Move(item.FullName);
                    else if (depth > 0) Walk(item.FullName, depth - 1);
                }
                // Non-recursive: unknown files, links, and concurrent writes prevent removal.
                if (OrdinaryPath(folder)) Directory.Delete(folder, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (string file in Files) Move(Path.Combine(source, file));
        foreach (string folder in Folders) Walk(Path.Combine(source, folder), 8);
    }
}

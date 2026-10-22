using System.Security.Cryptography;

namespace Dustweave;

// The launcher updates code, while the game downloads design databases to
// LocalLow/Data/t. Old copy-on-write tables can mask a newer host download even
// when the patch index (which has no overlay) already says the download is done.
internal static class DailySandboxGameData
{
    internal static string HostTables => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Gamfs", "BrownDust II", "Data", "t");
    internal static string BoxTables(string root) => Path.Combine(root, "user", "current", "AppData", "LocalLow", "Gamfs", "BrownDust II", "Data", "t");
    internal static bool TableName(string name) => name.Length == 40 && name.All(char.IsAsciiHexDigit);
    private sealed record Replacement(string Name, string Hash, long Length, DateTime HostTime, string OldHash);

    internal static async Task<int> SynchronizeAsync(string source, string destination, string backup,
        Func<Task<bool>> idle, Action<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(source) || !Directory.Exists(destination)) return 0;
        DailyCacheStorage.NoLinks(source); DailyCacheStorage.NoLinks(destination); DailyCacheStorage.NoLinks(backup);
        string list = Path.Combine(source, ".gamedata_filelist");
        if (!File.Exists(list)) return 0; // Let the game's ordinary downloader initialize new installs.
        DailyCacheStorage.NoLinks(list);
        var names = File.ReadAllLines(list).Select(n => n.Trim()).Where(TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacements = new List<Replacement>();
        string Hash(string path)
        {
            DailyCacheStorage.NoLinks(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(input));
        }
        foreach (var local in Directory.EnumerateFiles(destination))
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(local);
            if (!TableName(name) || !names.Contains(name)) continue;
            var host = new FileInfo(Path.Combine(source, name)); var box = new FileInfo(local);
            if (!host.Exists || host.Length == 0 || host.LastWriteTimeUtc < box.LastWriteTimeUtc) continue;
            // A sandbox may already have a newer hot update. Never roll it back
            // merely because the normal game hasn't been opened recently.
            string hostHash = Hash(host.FullName), oldHash = Hash(local);
            if (hostHash == oldHash) continue;
            replacements.Add(new(name, hostHash, host.Length, host.LastWriteTimeUtc, oldHash));
        }
        if (replacements.Count == 0) return 0;
        if (!await idle()) throw new InvalidOperationException("client_update.sync_busy");
        progress("client_update.sync_tables");
        Directory.CreateDirectory(backup);
        foreach (var entry in replacements)
        {
            token.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, entry.Name), previous = Path.Combine(backup, entry.Name);
            string temporary = Path.Combine(destination, ".dustweave-" + Guid.NewGuid().ToString("N"));
            DailyCacheStorage.NoLinks(target); DailyCacheStorage.NoLinks(previous);
            try
            {
                using (var input = new FileStream(Path.Combine(source, entry.Name), FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await input.CopyToAsync(output, token);
                if (new FileInfo(temporary).Length != entry.Length || Hash(temporary) != entry.Hash ||
                    Hash(target) != entry.OldHash || !await idle())
                    throw new IOException("client_update.sync_changed");
                token.ThrowIfCancellationRequested();
                File.SetLastWriteTimeUtc(temporary, entry.HostTime);
                // Keep one previous copy per table, outside the game's search path.
                // Replacement is atomic; cancellation/partial failure is retryable.
                File.Replace(temporary, target, previous);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        DailyJson.Write(Path.Combine(backup, "last-sync.json"), new { atUtc = DateTimeOffset.UtcNow,
            tables = replacements.Select(x => new { x.Name, x.Hash, x.OldHash, x.Length }), credentialsTouched = false });
        return replacements.Count;
    }
}

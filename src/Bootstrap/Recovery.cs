using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Bootstrap;

internal static class Recovery
{
    internal static void NoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked recovery path.");
    }
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static string Text(JsonElement root, string key) => root.GetProperty(key).GetString() ?? throw new InvalidDataException("Invalid recovery record.");
    private static bool Same(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static JsonDocument Read(string path)
    {
        NoLinks(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Oversized recovery record.");
        return JsonDocument.Parse(stream);
    }
    internal static bool TryRun(string[] args, string target, string dataRoot)
    {
        string installRoot = Path.GetDirectoryName(Path.GetFullPath(target))!;
        string markerPath = Path.Combine(installRoot, ".dustweave-update.json");
        if (!File.Exists(markerPath)) return false;
        using var markerDocument = Read(markerPath); var marker = markerDocument.RootElement;
        string jobPath = Text(marker, "JobPath"), nonce = Text(marker, "Nonce"), digest = Text(marker, "HelperSha256");
        if (nonce.Length != 32 || !nonce.All(Uri.IsHexDigit) || digest.Length != 64 || !digest.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid recovery identity.");
        string prefix = Path.GetFullPath(Path.Combine(dataRoot, "updates")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(jobPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(jobPath) != "job.json") throw new InvalidDataException("Invalid recovery ownership.");
        using var jobDocument = Read(jobPath); var job = jobDocument.RootElement;
        string directory = Text(job, "Directory"), version = Text(job.GetProperty("Release"), "Version"), flavor = Text(job.GetProperty("Asset"), "Flavor");
        if (version.Length is < 5 or > 100 || version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-')) || flavor is not ("Lite" or "Portable")) throw new InvalidDataException("Invalid recovery version.");
        string attempt = Path.Combine(dataRoot, "updates", version + "-" + flavor, "attempts", nonce);
        if (!Same(target, Text(job, "Target")) || Text(job, "Nonce") != nonce || !Same(directory, Path.Combine(dataRoot, "updates", version + "-" + flavor)) || !Same(jobPath, Path.Combine(attempt, "job.json"))) throw new InvalidDataException("Invalid recovery ownership.");
        using var journalDocument = Read(Path.Combine(attempt, "backup", "transaction.json")); var journal = journalDocument.RootElement;
        if (!Same(Text(journal, "Target"), installRoot) || !Same(Text(journal, "Backup"), Path.Combine(attempt, "backup"))) throw new InvalidDataException("Invalid recovery journal.");
        string state = Text(journal, "State");
        if (state is "completed" or "rolled_back") { File.Delete(markerPath); return false; }
        if (state is not ("prepared" or "replacing" or "awaiting_start" or "rolling_back")) throw new InvalidDataException("Unknown recovery state.");
        if (args is ["--updated", var incoming] && incoming == nonce && state == "awaiting_start") return false;
        string mutexName = @"Local\Dustweave-Updater-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(target).ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName); bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return true;
        try
        {
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"); NoLinks(helper);
            if (!Hash(helper).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery helper failed verification.");
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
            start.ArgumentList.Add("--recover-update"); start.ArgumentList.Add(jobPath);
            using var process = Process.Start(start) ?? throw new IOException("Recovery helper did not start.");
        }
        finally { mutex.ReleaseMutex(); }
        return true;
    }
}

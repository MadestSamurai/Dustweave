using System.Security.Cryptography;
namespace Dustweave;

public sealed record DailyUpdateBackupFile(string Name, bool Existed, string? Sha256);
public sealed record DailyUpdateJournal(string Target, string Backup, string Staging, DailyUpdateBackupFile[] Files, string State);
public sealed record DailyUpdateMarker(string JobPath, string Nonce, string HelperSha256);

public static class DailyUpdateTransaction
{
    public const string MarkerName = ".dustweave-update.json";
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static void CopyDurable(string source, string target)
    {
        DailyUpdates.EnsureNoLinks(source); DailyUpdates.EnsureNoLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) && new FileInfo(source).Length == new FileInfo(target).Length && Hash(source) == Hash(target)) return;
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".update";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                    break;
                }
                catch (IOException e) when ((e.HResult & 0xffff) is 5 or 32 or 33 or 1175 && watch.Elapsed < TimeSpan.FromSeconds(5))
                { Thread.Sleep(100); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new IOException($"Cannot replace {target} (0x{error.HResult:X8})", error); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static DailyUpdateJournal Prepare(string staging, string target, string backup, string[] names)
    {
        foreach (var path in new[] { staging, target, backup }) DailyUpdates.EnsureNoLinks(path);
        if (Directory.Exists(backup)) throw new IOException("updates.backup_exists");
        var previous = DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(target, "update-package.json"));
        string[] obsolete = (previous?.Files ?? []).Except(names, StringComparer.OrdinalIgnoreCase).ToArray();
        var all = names.Concat(obsolete).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (all.Length > 10000 || all.Any(n => !DailyUpdates.AllowedFile(n))) throw new InvalidDataException("updates.invalid_package");
        Directory.CreateDirectory(backup);
        var files = new List<DailyUpdateBackupFile>();
        foreach (var name in all.OrderBy(n => n == "Dustweave.exe" ? 1 : 0))
        {
            string destination = Path.Combine(target, name), saved = Path.Combine(backup, name);
            DailyUpdates.EnsureNoLinks(destination);
            bool existed = File.Exists(destination);
            string? digest = null;
            if (existed) { CopyDurable(destination, saved); digest = Hash(saved); }
            files.Add(new(name, existed, digest));
        }
        // ALL backups are durable before any destination is touched.
        var journal = new DailyUpdateJournal(Path.GetFullPath(target), Path.GetFullPath(backup), Path.GetFullPath(staging), files.ToArray(), "prepared");
        Save(journal); return journal;
    }
    public static DailyUpdateJournal Apply(DailyUpdateJournal journal, Action<int>? afterFile = null)
    {
        journal = journal with { State = "replacing" }; Save(journal);
        int count = 0;
        foreach (var file in journal.Files)
        {
            if (!DailyUpdates.AllowedFile(file.Name)) throw new InvalidDataException("updates.invalid_package");
            string source = Path.Combine(journal.Staging, file.Name), destination = Path.Combine(journal.Target, file.Name);
            DailyUpdates.EnsureNoLinks(source); DailyUpdates.EnsureNoLinks(destination);
            if (File.Exists(source)) CopyDurable(source, destination);
            else if (File.Exists(destination)) File.Delete(destination);
            afterFile?.Invoke(++count);
        }
        journal = journal with { State = "awaiting_start" }; Save(journal); return journal;
    }
    public static DailyUpdateJournal Restore(DailyUpdateJournal journal)
    {
        // Verify every backup before beginning; repeated recovery is idempotent after a second crash.
        foreach (var file in journal.Files)
        {
            if (!DailyUpdates.AllowedFile(file.Name)) throw new InvalidDataException("updates.invalid_package");
            if (file.Existed && !string.Equals(Hash(Path.Combine(journal.Backup, file.Name)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("updates.backup_damaged");
        }
        journal = journal with { State = "rolling_back" }; Save(journal);
        foreach (var file in journal.Files.OrderBy(f => f.Name == "Dustweave.exe" ? 1 : 0))
        {
            string destination = Path.Combine(journal.Target, file.Name);
            DailyUpdates.EnsureNoLinks(destination);
            if (file.Existed) CopyDurable(Path.Combine(journal.Backup, file.Name), destination);
            else if (File.Exists(destination)) File.Delete(destination);
        }
        journal = journal with { State = "rolled_back" }; Save(journal); return journal;
    }
    public static void Save(DailyUpdateJournal journal) => DailyJson.Write(Path.Combine(journal.Backup, "transaction.json"), journal);
    public static DailyUpdateJournal Read(string backup, string target)
    {
        var journal = DailyJson.TryRead<DailyUpdateJournal>(Path.Combine(backup, "transaction.json")) ?? throw new IOException("updates.recovery_required");
        if (!Path.GetFullPath(journal.Target).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(journal.Backup).Equals(Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.invalid_package");
        return journal;
    }
}

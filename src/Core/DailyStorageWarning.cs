namespace Dustweave;

public sealed record DailyDiskSpace(string Volume, long AvailableBytes);
public sealed record DailyStorageWarningPreference(string IgnoredDay);

/// <summary>Cheap volume checks precede directory scans. The reminder follows the user's local clock.</summary>
public static class DailyStorageWarning
{
    public const long ThresholdBytes = 1024L * 1024 * 1024;
    public const int DayStartsAtHour = 8;
    private static string FileName(string root) => Path.Combine(root, "storage-warning.json");
    public static string Day(DateTime localTime) => localTime.AddHours(-DayStartsAtHour).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static bool IsIgnored(string root, DateTime localTime) => DailyJson.TryRead<DailyStorageWarningPreference>(FileName(root))?.IgnoredDay == Day(localTime);
    public static void IgnoreToday(string root, DateTime localTime) => DailyJson.Write(FileName(root), new DailyStorageWarningPreference(Day(localTime)));
    public static DailyDiskSpace[] LowVolumes(IEnumerable<string> directories, Func<string, long?>? readAvailable = null)
    {
        readAvailable ??= volume => { var drive = new DriveInfo(volume); return drive.IsReady ? drive.AvailableFreeSpace : null; };
        var result = new List<DailyDiskSpace>();
        foreach (string volume in directories.Select(Path.GetPathRoot).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>())
        {
            try { if (readAvailable(volume) is long bytes && bytes >= 0 && bytes < ThresholdBytes) result.Add(new(volume, bytes)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* An unreadable drive is not evidence of low space. */ }
        }
        return result.ToArray();
    }
}

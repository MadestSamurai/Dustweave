using Dustweave;
internal static class StorageWarningCases
{
    public static Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); cases.Add("storage-warning: " + label); }
        string root = Path.Combine(output, "storage-warning"); Directory.CreateDirectory(root);
        DateTime before = new(2026, 10, 16, 7, 59, 59), after = before.AddSeconds(1);
        Check(DailyStorageWarning.Day(before) == "2026-10-15" && DailyStorageWarning.Day(after) == "2026-10-16", "local 08:00 boundary, not midnight");
        Check(!DailyStorageWarning.IsIgnored(root, before), "first run does not suppress reminder");
        DailyStorageWarning.IgnoreToday(root, before);
        Check(DailyStorageWarning.IsIgnored(root, before) && !DailyStorageWarning.IsIgnored(root, after), "persisted ignore expires exactly at the next local boundary");
        DailyStorageWarning.IgnoreToday(root, after);
        Check(DailyStorageWarning.IsIgnored(root, after.AddHours(16)) && !DailyStorageWarning.IsIgnored(root, after.AddDays(1)), "midnight retains preference across restart; next morning resets it");
        var foreignClock = new DateTimeOffset(2026, 10, 16, 7, 59, 59, TimeSpan.FromHours(-7));
        Check(DailyStorageWarning.Day(foreignClock.DateTime) == "2026-10-15", "day uses supplied user clock, independent of UTC or game timezone");
        int reads = 0;
        var spaces = DailyStorageWarning.LowVolumes([@"C:\data", @"c:\data\cache", @"E:\cache"], v => { reads++; return v.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? DailyStorageWarning.ThresholdBytes - 1 : DailyStorageWarning.ThresholdBytes; });
        Check(reads == 2 && spaces.Length == 1 && spaces[0].AvailableBytes == DailyStorageWarning.ThresholdBytes - 1, "deduplicates volumes and warns strictly below 1 GiB");
        Check(DailyStorageWarning.LowVolumes([@"C:\data", @"E:\cache"], _ => 0).Length == 2, "both source and moved cache volumes are monitored, including completely full drives");
        Check(DailyStorageWarning.LowVolumes([@"C:\data"], _ => null).Length == 0 && DailyStorageWarning.LowVolumes([@"C:\data"], _ => -1).Length == 0, "missing or invalid readings never fabricate a full drive");
        Check(DailyStorageWarning.LowVolumes([@"C:\data", @"E:\cache"], v => v.StartsWith("C") ? throw new IOException("unavailable") : 10).Length == 1, "unavailable source does not prevent checking the cache drive");
        File.WriteAllText(Path.Combine(root, "storage-warning.json"), "{broken");
        Check(!DailyStorageWarning.IsIgnored(root, after), "corrupt preference does not permanently suppress warnings");
        return Task.CompletedTask;
    }
}

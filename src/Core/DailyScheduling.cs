using System.Globalization;

namespace Dustweave;

public sealed record DailySchedulePlan
{
    public bool Enabled { get; init; }
    public int Hour { get; init; } = 9;
    public int Minute { get; init; }
    // Retained only to recognize and migrate schedules saved by 0.9.0/0.9.1.
    public int[] Days { get; init; } = [0, 1, 2, 3, 4, 5, 6];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDaily => Days is { Length: 7 } && Days.Order().SequenceEqual(Enumerable.Range(0, 7));
    public string[] Accounts { get; init; } = [];
    public DateTimeOffset SavedUtc { get; init; }
    public void Validate()
    {
        if (Hour is < 0 or > 23 || Minute is < 0 or > 59 || Days is null || Days.Length == 0 || Days.Any(d => d is < 0 or > 6) || Days.Distinct().Count() != Days.Length)
            throw new InvalidDataException("schedule.invalid_time");
        if (Accounts is null || Accounts.Length == 0 || Accounts.Any(k => !DailyProfiles.ValidKey(k)) || Accounts.Distinct().Count() != Accounts.Length)
            throw new InvalidDataException("schedule.invalid_accounts");
    }
}

public sealed record DailyScheduleRun(string Occurrence, string State, DateTimeOffset AtUtc, string Detail = "");

public sealed class DailyScheduleStore(string root)
{
    public string PlanPath => Path.Combine(root, "schedule.json");
    public DailySchedulePlan Read()
    {
        if (!File.Exists(PlanPath)) return new();
        return DailyJson.TryRead<DailySchedulePlan>(PlanPath) ?? throw new InvalidDataException("schedule.unreadable");
    }
    public void Save(DailySchedulePlan plan) => DailyJson.Write(PlanPath, plan);
    public async Task<bool> MigrateToDailyAsync(Func<DailySchedulePlan, Task> register, DateTimeOffset now)
    {
        var previous = Read();
        if (previous.IsDaily) return false;
        var daily = previous with { Days = [0, 1, 2, 3, 4, 5, 6], SavedUtc = now };
        // A crash or registration failure leaves the schedule disabled, without changing account order.
        Save(daily with { Enabled = false });
        if (daily.Enabled)
        {
            daily.Validate();
            await register(daily);
        }
        Save(daily);
        return true;
    }
    public DailyScheduleRun? Last => DailyJson.TryRead<DailyScheduleRun>(Path.Combine(root, "schedule-run.json"));
    public void Record(DailyScheduleRun run) => DailyJson.Write(Path.Combine(root, "schedule-run.json"), run);
    public bool IsClaimed(DateTimeOffset occurrence) => File.Exists(ClaimPath(occurrence));
    private string ClaimPath(DateTimeOffset occurrence) => Path.Combine(root, "schedule-claims", occurrence.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".json");
    // Claim before connecting to the game. A crash must not replay consuming actions.
    public bool Claim(DateTimeOffset occurrence, DateTimeOffset now)
    {
        string path = ClaimPath(occurrence);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            System.Text.Json.JsonSerializer.Serialize(file, new { occurrence, claimedUtc = now });
            file.Flush(true);
            return true;
        }
        catch (IOException) when (File.Exists(path)) { return false; }
    }
}

public static class DailyScheduleClock
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);
    public static DateTimeOffset? Occurrence(DailySchedulePlan plan, DateTime date, TimeZoneInfo zone)
    {
        if (!plan.Enabled || !plan.Days.Contains((int)date.DayOfWeek)) return null;
        var local = DateTime.SpecifyKind(date.Date.AddHours(plan.Hour).AddMinutes(plan.Minute), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return null;
        // Once per local day even during an autumn clock rollback.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
    public static DateTimeOffset? Due(DailySchedulePlan plan, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!plan.Enabled) return null;
        plan.Validate();
        var day = TimeZoneInfo.ConvertTime(now, zone).Date;
        return new[] { Occurrence(plan, day, zone), Occurrence(plan, day.AddDays(-1), zone) }
            .Where(t => t != null && t >= plan.SavedUtc && t <= now && now - t <= Grace).OrderByDescending(t => t).FirstOrDefault();
    }
    public static DateTimeOffset? Next(DailySchedulePlan plan, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!plan.Enabled) return null;
        plan.Validate();
        var day = TimeZoneInfo.ConvertTime(now, zone).Date;
        return Enumerable.Range(0, 15).Select(i => Occurrence(plan, day.AddDays(i), zone))
            .FirstOrDefault(t => t > now && t >= plan.SavedUtc);
    }
}

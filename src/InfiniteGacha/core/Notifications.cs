namespace BD2InfiniteGacha;

public sealed class NotificationOptions
{
    public bool Windows { get; set; } = true;
    public bool Popup { get; set; }
    public bool Sound { get; set; }
    public bool Any => Windows || Popup || Sound;
}

public sealed class NotificationPreferences(string root)
{
    private readonly string path = Path.Combine(root, "notifications.json");
    public NotificationOptions Load() => !File.Exists(path) ? new() :
        JsonFiles.Read<NotificationOptions>(path) ?? throw new InvalidDataException("提醒设置暂时无法读取，原文件未改动。");
    public void Save(NotificationOptions options) => JsonFiles.Write(path, options);
}

public sealed record MatchNotice(string Owner, int Pool, int Rolls, int A, int B)
{
    public static MatchNotice? From(Control active, Snapshot snapshot)
    {
        if (snapshot.State != "matched" || snapshot.Owner != active.Owner || snapshot.Account != active.Account ||
            snapshot.PoolId != active.PoolId || !snapshot.ResultReady || snapshot.Result.Length != 10) return null;
        var pool = snapshot.Pools.FirstOrDefault(p => p.Id == active.PoolId && p.Key == active.PoolKey);
        if (pool == null) return null;
        try
        {
            var match = Rules.Evaluate(active.Rules, snapshot.Result, pool.Costumes);
            return match.Matched ? new(active.Owner, pool.Id, snapshot.Rolls, match.A, match.B) : null;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException) { return null; }
    }
}

public enum NotificationChannel { Windows, Sound, Popup }
public interface INotificationSink { void Send(NotificationChannel channel, string title, string message); }
public sealed record NotificationFailure(NotificationChannel Channel, string Error);
public static class NotificationDelivery
{
    // Each channel is independent. A suppressed/failed system notification cannot block the popup or sound.
    public static NotificationFailure[] Send(NotificationOptions options, INotificationSink sink, string title, string message)
    {
        var failures = new List<NotificationFailure>();
        foreach (var (enabled, channel) in new[] { (options.Windows, NotificationChannel.Windows),
            (options.Sound, NotificationChannel.Sound), (options.Popup, NotificationChannel.Popup) })
        {
            if (!enabled) continue;
            try { sink.Send(channel, title, message); }
            catch (Exception e) { failures.Add(new(channel, e.Message)); }
        }
        return failures.ToArray();
    }
}

public static class ApplicationInfo
{
    // UI releases do not alter the embedded Runtime5 source or force a game restart.
    public static string Version => typeof(ApplicationInfo).Assembly.GetName().Version!.ToString(3);
}

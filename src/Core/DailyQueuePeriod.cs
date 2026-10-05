using System.Globalization;
using System.Text.Json;
namespace BD2Daily;

// Cycle is the game's next daily reset in server-clock ticks, not local midnight.
public sealed record QueuePeriod(string Server, string Cycle, string Player, long ResetUtcTicks = 0);
public static class DailyQueuePeriod
{
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
    public static QueuePeriod? Read(JsonElement record)
    {
        if (!record.TryGetProperty("context", out var c) || c.ValueKind != JsonValueKind.Object)
            return null;
        string player = c.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 5 && a[4].ValueKind == JsonValueKind.String ? a[4].GetString() ?? "" : "";
        long reset = record.TryGetProperty("resetUtcTicks", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var t) ? t : 0;
        return new(Text(c, "server"), Text(c, "cycle"), player, reset);
    }
    public static long Deadline(QueuePeriod? period, DailySnapshot? frame, long now)
    {
        if (period == null)
            return 0;
        // Calibrate server time to UTC using an existing observation. Never connect to
        // the game just to refresh this screen; timezone/local midnight is irrelevant.
        if (frame?.Guild is { } g && g.ServerKey == period.Server && g.ServerTicks > 0 && g.ServerTicks <= DateTime.MaxValue.Ticks
          && frame.FrameUtcTicks > 0 && frame.FrameUtcTicks <= now + TimeSpan.FromSeconds(5).Ticks
          && long.TryParse(period.Cycle, NumberStyles.None, CultureInfo.InvariantCulture, out long reset)
          && reset > 0 && reset <= DateTime.MaxValue.Ticks)
        {
            long value = frame.FrameUtcTicks + (reset - g.ServerTicks);
            if (value > 0 && value <= DateTime.MaxValue.Ticks)
                return value;
        }
        if (period.ResetUtcTicks > 0 && period.ResetUtcTicks <= DateTime.MaxValue.Ticks)
            return period.ResetUtcTicks;
        // Historical CycleKey uses TimerManager's UTC+9 DateTime convention
        // (UnixTimeStampToDateTime). This is the recorded reset instant, not a
        // hard-coded daily reset hour. New observed/calibrated clocks take priority.
        long offset = TimeSpan.FromHours(9).Ticks;
        return long.TryParse(period.Cycle, NumberStyles.None, CultureInfo.InvariantCulture, out long legacy)
            && legacy > offset && legacy <= DateTime.MaxValue.Ticks ? legacy - offset : 0;
    }
    public static bool IsExpired(QueuePeriod? period, DailySnapshot? frame, long now)
    {
        long deadline = Deadline(period, frame, now);
        return deadline > 0 && now >= deadline;
    }
    private static string ClockPath(string root, string server) => Path.Combine(root, "queue-clocks", DailyIdentity.Hash(server) + ".json");
    private static DailySnapshot? ReadFile(string path)
    {
        // This is persisted clock metadata, never an IPC read from a UI refresh.
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<DailySnapshot>(stream, DailyJson.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public static DailySnapshot? Snapshot(string root, string server)
    {
        var saved = ReadFile(ClockPath(root, server));
        // Older versions persisted their observation here. Read it without routing
        // through the live pipe so history still rolls over while disconnected.
        return saved ?? ReadFile(Path.Combine(root, "snapshot.json"));
    }
    public static void Remember(string root, DailySnapshot? frame)
    {
        if (frame?.Guild is not { } g || g.ServerKey.Length == 0 || g.ServerTicks <= 0 || frame.FrameUtcTicks <= 0 || g.ResetTicks <= g.ServerTicks)
            return;
        string path = ClockPath(root, g.ServerKey);
        var old = ReadFile(path);
        if (old?.Guild?.CycleKey == g.CycleKey && frame.FrameUtcTicks - old.FrameUtcTicks < TimeSpan.FromMinutes(5).Ticks)
            return;
        DailyJson.Write(path, new DailySnapshot { FrameUtcTicks = frame.FrameUtcTicks, Guild = new() { ServerKey = g.ServerKey, ServerTicks = g.ServerTicks, ResetTicks = g.ResetTicks, CycleKey = g.CycleKey } });
    }
}

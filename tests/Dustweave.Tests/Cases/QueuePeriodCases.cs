using BD2Daily;
using System.Text.Json.Nodes;
static class QueuePeriodCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
            cases.Add(message);
        }
        long now = new DateTime(2026, 9, 28, 23, 59, 0, DateTimeKind.Utc).Ticks;
        // Intentionally use a non-UTC server clock and a non-midnight reset.
        long server = now + TimeSpan.FromHours(9).Ticks, reset = server + TimeSpan.FromHours(8).Ticks;
        var frame = new DailySnapshot { FrameUtcTicks = now, AccountKey = new('a', 64), PlayerKey = "player", Guild = new() { ServerKey = "server", ServerTicks = server, ResetTicks = reset, CycleKey = reset.ToString() } };
        var period = new QueuePeriod("server", reset.ToString(), "player");
        Check(DailyQueuePeriod.Deadline(period, frame, now) == now + TimeSpan.FromHours(8).Ticks, "server reset calibrated to UTC instead of local midnight");
        Check(!DailyQueuePeriod.IsExpired(period, frame, now + TimeSpan.FromMinutes(2).Ticks), "crossing calendar midnight does not prematurely reset game day");
        Check(!DailyQueuePeriod.IsExpired(period, frame, now + TimeSpan.FromHours(8).Ticks - 1), "unfinished same-cycle progress remains resumable");
        Check(DailyQueuePeriod.IsExpired(period, frame, now + TimeSpan.FromHours(8).Ticks), "exact game reset expires report even with an offline snapshot");
        Check(DailyQueuePeriod.IsExpired(period, frame, now + TimeSpan.FromDays(3).Ticks), "multiple offline days expire old history");
        var saved = period with
        {
            ResetUtcTicks = now + TimeSpan.FromHours(8).Ticks
        };
        Check(DailyQueuePeriod.IsExpired(saved, null, now + TimeSpan.FromDays(1).Ticks), "stored deadline works without a game or snapshot");
        frame.Guild.ServerKey = "other";
        Check(DailyQueuePeriod.Deadline(period, frame, now) == saved.ResetUtcTicks, "another server never calibrates old record clock");
        Check(DailyQueuePeriod.IsExpired(saved, frame, now + TimeSpan.FromDays(1).Ticks), "other server does not override persisted deadline");
        frame.Guild.ServerKey = "server";
        frame.FrameUtcTicks = now + TimeSpan.FromDays(1).Ticks;
        Check(DailyQueuePeriod.Deadline(period, frame, now) == saved.ResetUtcTicks, "future observation is not used as valid clock evidence");
        frame.FrameUtcTicks = now + TimeSpan.FromDays(2).Ticks;
        frame.Guild.ServerTicks = server + TimeSpan.FromDays(2).Ticks;
        Check(DailyQueuePeriod.Deadline(period, frame, frame.FrameUtcTicks) == saved.ResetUtcTicks, "new-day observation dates the original historical cycle correctly");
        Check(DailyQueuePeriod.Deadline(period with
        {
            Cycle = "unknown"
        }, frame, frame.FrameUtcTicks) == 0, "unknown reset is not guessed from file modification time");

        Check(DailyQueuePeriod.IsExpired(period, null, now + TimeSpan.FromDays(1).Ticks), "legacy cycle expires offline without requiring a new connection");
        var shifted = period with
        {
            Cycle = (reset - TimeSpan.FromHours(6).Ticks).ToString()
        };
        var differentClock = new DailySnapshot { FrameUtcTicks = now, Guild = new() { ServerKey = "server", ServerTicks = server - TimeSpan.FromHours(6).Ticks, ResetTicks = reset - TimeSpan.FromHours(6).Ticks, CycleKey = shifted.Cycle } };
        Check(DailyQueuePeriod.Deadline(shifted, differentClock, now) == saved.ResetUtcTicks, "observed server convention overrides legacy UTC offset");
        string clocks = Path.Combine(output, "clock-cache");
        DailyQueuePeriod.Remember(clocks, differentClock);
        Check(DailyQueuePeriod.Snapshot(clocks, "server")?.Guild?.ServerTicks == differentClock.Guild.ServerTicks, "clock cache survives disconnected reload");
        differentClock.Guild.ServerKey = "second";
        DailyQueuePeriod.Remember(clocks, differentClock);
        Check(DailyQueuePeriod.Snapshot(clocks, "server")?.Guild?.ServerKey == "server" && DailyQueuePeriod.Snapshot(clocks, "second")?.Guild?.ServerKey == "second", "clock caches remain separated by server");

        string root = Path.Combine(output, "period"), id = new('e', 32), account = new('a', 64);
        string path = DailyQueueEngine.RecordPath(root, id);
        long clock = now;
        var record = new JsonObject { ["state"] = "paused", ["context"] = new JsonObject { ["actor"] = new JsonArray(1, 2, "bridge", account, "player"), ["server"] = "server", ["cycle"] = reset.ToString() }, ["resetUtcTicks"] = saved.ResetUtcTicks, ["items"] = new JsonArray(new JsonObject { ["task"] = "mail", ["state"] = "pending" }) };
        DailyJson.Write(path, record);
        DailyJson.Write(Path.Combine(root, "queue-ui.json"), new
        {
            account,
            record = path
        });
        var fake = new RejectExecutor();
        var session = new DailyQueueSession(root, fake, () => clock);
        var before = File.ReadAllText(path);
        Check(!session.ReadView().Expired, "same-day historical report retained");
        clock = saved.ResetUtcTicks;
        var expired = session.ReadView();
        Check(expired.Expired && expired.Stages.Count == 1 && expired.Record == path, "expired report remains readable with durable stages");
        foreach (bool resume in new[] { true, false })
        {
            bool rejected = false;
            try
            {
                await session.RunAsync(account, resume: resume, retry: resume ? null : new(account, path, ["mail"]));
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && fake.Calls == 0, resume ? "expired resume rejected before connecting" : "expired retry rejected before connecting");
        }
        Check(File.ReadAllText(path) == before, "rollover never rewrites or deletes original operation evidence");
        // Legacy records also expire using the saved game clock, without being migrated in-place.
        record.Remove("resetUtcTicks");
        DailyJson.Write(path, record);
        DailyJson.Write(Path.Combine(root, "snapshot.json"), frame);
        clock = frame.FrameUtcTicks;
        Check(session.ReadView().Expired, "legacy queue automatically expires using game cycle and observation");
    }
    private sealed class RejectExecutor : IDailyQueueExecutor
    {
        public int Calls;
        public Task<int> PrepareAsync(Action<string> report)
        {
            Calls++;
            throw new Exception("must not connect");
        }
        public Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null) => throw new Exception("must not execute");
    }
}

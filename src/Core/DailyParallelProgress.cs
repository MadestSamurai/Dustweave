namespace Dustweave;

// Keep account progress after the temporary batch view closes, without copying
// sandbox paths or turning uncertain worker transactions into a resumable queue.
public static class DailyParallelProgress
{
    private sealed record Entry(string Account, DateTimeOffset AtUtc, QueueView View)
    {
        public Dictionary<string, DateTimeOffset> Observed { get; init; } = [];
    }
    private static string PathFor(string root, string account) => Path.Combine(root, "parallel-progress", account + ".json");
    private static bool SamePeriod(QueuePeriod? a, QueuePeriod? b) => a != null && b != null && a.Server == b.Server && a.Cycle == b.Cycle && a.Player == b.Player;
    private static bool CanReplace(QueueStage old, QueueStage next) => next.State is not ("pending" or "waiting" or "preparing" or "idle")
        && !(old.State == "completed" && next.State == "skipped" && next.Detail is "disabled" or "已关闭");
    private static QueueStage[] Combine(IEnumerable<QueueStage> older, IEnumerable<QueueStage> newer) => older.Concat(newer).GroupBy(s => s.Task, StringComparer.Ordinal)
        .Select(g => g.Aggregate((old, next) => CanReplace(old, next) ? next : old)).ToArray();
    public static void Remember(string root, DailyParallelItem item)
    {
        var status = item.Status;
        var view = status?.Queue;
        string account = item.Account.AccountKey;
        if (!DailyProfiles.ValidKey(account) || status == null || status.Id != item.Job.Id || status.Account != account
            || view == null || view.Account != account || view.Period == null || view.Stages.Count == 0) return;
        string path = PathFor(root, account);
        var old = DailyJson.TryRead<Entry>(path);
        if (old?.Account == account && old.AtUtc > status.AtUtc) return;
        var observed = view.Stages.Select(s => !DailyParallelSession.Occupies(item.State) && s.State == "running" ? s with { State = "stopped" } : s).ToArray();
        var stages = old?.Account == account && SamePeriod(old.View.Period, view.Period) ? Combine(old.View.Stages, observed) : observed;
        if (old?.Account == account && old.AtUtc == status.AtUtc && System.Text.Json.JsonSerializer.Serialize(old.View.Stages) == System.Text.Json.JsonSerializer.Serialize(stages)) return;
        var times = old?.Account == account && SamePeriod(old.View.Period, view.Period)
            ? old.View.Stages.ToDictionary(s => s.Task, s => old.Observed.GetValueOrDefault(s.Task, s.FinishedAt ?? old.AtUtc)) : new Dictionary<string, DateTimeOffset>();
        foreach (var stage in observed)
            if (stages.Any(s => ReferenceEquals(s, stage))) times[stage.Task] = stage.FinishedAt ?? status.AtUtc;
        DailyJson.Write(path, new Entry(account, status.AtUtc, view with { Record = "", Stages = stages, PlanStages = null }) { Observed = times });
    }
    public static QueueView Read(string root, string account, QueueView local, DateTimeOffset? now = null)
    {
        if (!DailyProfiles.ValidKey(account) || local.Account != account) return local;
        DailyAccountProgress.ScheduleImport(root, account);
        var saved = DailyJson.TryRead<Entry>(PathFor(root, account));
        if (saved == null || saved.Account != account || saved.View.Account != account) return local;
        long ticks = (now ?? DateTimeOffset.UtcNow).UtcTicks;
        bool expired = DailyQueuePeriod.IsExpired(saved.View.Period, DailyQueuePeriod.Snapshot(root, saved.View.Period?.Server ?? ""), ticks);
        DateTime updated = local.Record.Length > 0 && File.Exists(local.Record) ? File.GetLastWriteTimeUtc(local.Record) : DateTime.MinValue;
        if (expired)
            return local.Stages.Count > 0 ? local : new("idle", "", "", [], account, saved.View.Period, true);
        bool same = !local.Expired && SamePeriod(local.Period, saved.View.Period);
        var combined = saved.View.Stages.ToDictionary(s => s.Task);
        if (same)
            foreach (var stage in local.Stages)
            {
                DateTimeOffset at = stage.FinishedAt ?? new DateTimeOffset(updated, TimeSpan.Zero);
                if (!combined.TryGetValue(stage.Task, out var oldStage)
                    || CanReplace(oldStage, stage) && at >= saved.Observed.GetValueOrDefault(stage.Task, oldStage.FinishedAt ?? saved.AtUtc)) combined[stage.Task] = stage;
            }
        if (updated >= saved.AtUtc.UtcDateTime)
            return same ? local with { PlanStages = combined.Values.ToArray() } : local;
        return saved.View with { Record = "", Stages = combined.Values.ToArray(), PlanStages = combined.Values.ToArray(), Expired = false };
    }
    internal static void Import(string target, string source, string account)
    {
        Capture(target, account);
        Capture(source, account);
        var incoming = DailyJson.TryRead<Entry>(PathFor(source, account));
        if (incoming != null) Merge(target, account, incoming);
    }
    internal static void Capture(string root, string account)
    {
        try { CaptureCore(root, account); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException)
        { DailyAccountProgress.Report(root, e); }
    }
    private static void CaptureCore(string root, string account)
    {
        string record = DailyQueueHistory.Resolve(root, account);
        if (record.Length == 0) return;
        QueueView view;
        try { view = DailyQueueSession.ReadRecord(root, account, record, DateTime.UtcNow.Ticks); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException) { return; }
        if (view.Period == null || view.Stages.Count == 0) return;
        var at = new DateTimeOffset(File.GetLastWriteTimeUtc(record));
        Merge(root, account, new(account, at, view));
    }
    private static void Merge(string root, string account, Entry incoming)
    {
        if (incoming.Account != account || incoming.View.Account != account || incoming.View.Period is not { Server.Length: > 0, Player.Length: > 0, Cycle.Length: > 0 }) return;
        string path = PathFor(root, account);
        var old = DailyJson.TryRead<Entry>(path);
        if (old?.Account != account) old = null;
        if (old != null && !SamePeriod(old.View.Period, incoming.View.Period) && old.AtUtc >= incoming.AtUtc) return;
        bool same = old != null && SamePeriod(old.View.Period, incoming.View.Period);
        var rows = same ? old!.View.Stages.ToDictionary(s => s.Task) : new Dictionary<string, QueueStage>();
        var times = same ? old!.View.Stages.ToDictionary(s => s.Task, s => old.Observed.GetValueOrDefault(s.Task, s.FinishedAt ?? old.AtUtc)) : new Dictionary<string, DateTimeOffset>();
        foreach (var stage in incoming.View.Stages)
        {
            var at = incoming.Observed.GetValueOrDefault(stage.Task, stage.FinishedAt ?? incoming.AtUtc);
            if (rows.TryGetValue(stage.Task, out var existing) && !CanReplace(existing, stage)) continue;
            if (times.TryGetValue(stage.Task, out var prior) && prior >= at) continue;
            rows[stage.Task] = stage with { State = stage.State == "running" ? "stopped" : stage.State };
            times[stage.Task] = at;
        }
        var result = new Entry(account, same && old!.AtUtc > incoming.AtUtc ? old.AtUtc : incoming.AtUtc,
            incoming.View with { Record = "", State = "partial", Stages = rows.Values.ToArray(), PlanStages = null }) { Observed = times };
        if (rows.Values.All(s => s.State is "completed" or "skipped")) result = result with { View = result.View with { State = "completed" } };
        if (System.Text.Json.JsonSerializer.Serialize(old) != System.Text.Json.JsonSerializer.Serialize(result)) DailyJson.Write(path, result);
    }
}

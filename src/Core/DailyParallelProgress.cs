namespace Dustweave;

// Keep account progress after the temporary batch view closes, without copying
// sandbox paths or turning uncertain worker transactions into a resumable queue.
public static class DailyParallelProgress
{
    private sealed record Entry(string Account, DateTimeOffset AtUtc, QueueView View);
    private static string PathFor(string root, string account) => Path.Combine(root, "parallel-progress", account + ".json");
    private static bool SamePeriod(QueuePeriod? a, QueuePeriod? b) => a != null && b != null && a.Server == b.Server && a.Cycle == b.Cycle && a.Player == b.Player;
    private static QueueStage[] Combine(IEnumerable<QueueStage> older, IEnumerable<QueueStage> newer) => older.Concat(newer).GroupBy(s => s.Task, StringComparer.Ordinal).Select(g => g.Last()).ToArray();
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
        DailyJson.Write(path, new Entry(account, status.AtUtc, view with { Record = "", Stages = stages, PlanStages = null }));
    }
    public static QueueView Read(string root, string account, QueueView local, DateTimeOffset? now = null)
    {
        if (!DailyProfiles.ValidKey(account) || local.Account != account) return local;
        var saved = DailyJson.TryRead<Entry>(PathFor(root, account));
        if (saved == null || saved.Account != account || saved.View.Account != account) return local;
        long ticks = (now ?? DateTimeOffset.UtcNow).UtcTicks;
        bool expired = DailyQueuePeriod.IsExpired(saved.View.Period, DailyQueuePeriod.Snapshot(root, saved.View.Period?.Server ?? ""), ticks);
        DateTime updated = local.Record.Length > 0 && File.Exists(local.Record) ? File.GetLastWriteTimeUtc(local.Record) : DateTime.MinValue;
        if (expired)
            return local.Stages.Count > 0 ? local : new("idle", "", "", [], account, saved.View.Period, true);
        bool same = !local.Expired && SamePeriod(local.Period, saved.View.Period);
        if (updated >= saved.AtUtc.UtcDateTime)
            return same ? local with { PlanStages = Combine(saved.View.Stages, local.Stages) } : local;
        var combined = same ? Combine(local.Stages, saved.View.Stages) : saved.View.Stages;
        return saved.View with { Record = "", Stages = combined, PlanStages = combined, Expired = false };
    }
}

using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private readonly DailySchedulePanel schedulePanel = new();
    private DailyScheduleStore scheduleStore = null!;
    private bool scheduleChecking;
    private DateTime nextScheduleCheck;
    private void InitializeSchedules()
    {
        scheduleStore = new(root); ScheduleTab.Content = schedulePanel;
        try { schedulePanel.Show(scheduleStore.Read(), scheduleStore.Last); }
        catch (Exception e) { schedulePanel.Feedback(e.Message, true); }
        schedulePanel.SaveRequested += async plan =>
        {
            if (smoke != null) { schedulePanel.Feedback("schedule.preview"); return; }
            if (Unavailable) return;
            schedulePanel.Busy(true);
            try
            {
                if (!preferencesPanel.SavePending()) return;
                // Persist disabled before changing Windows registration. Partial failure cannot run the wrong queue.
                scheduleStore.Save(plan with { Enabled = false });
                await Task.Run(() => DailyWindowsSchedule.Register(plan, Environment.ProcessPath!));
                scheduleStore.Save(plan);
                schedulePanel.Show(plan, scheduleStore.Last);
                schedulePanel.Feedback("schedule.saved");
            }
            catch (Exception e) { schedulePanel.Show(plan with { Enabled = false }, scheduleStore.Last); schedulePanel.Feedback(e.Message.StartsWith("schedule.") ? e.Message : "schedule.register_failed", true); DailyJson.Write(Path.Combine(root, "schedule-error.json"), new { error = e.ToString() }); }
            finally { schedulePanel.Busy(false); }
        };
    }
    private async Task CheckScheduleAsync()
    {
        if (smoke != null || scheduleChecking || DateTime.UtcNow < nextScheduleCheck || finalClose) return;
        nextScheduleCheck = DateTime.UtcNow.AddSeconds(10); scheduleChecking = true;
        DateTimeOffset? due = null;
        try
        {
            var plan = scheduleStore.Read(); var now = DateTimeOffset.UtcNow;
            schedulePanel.RefreshStatus(plan, scheduleStore.Last);
            due = DailyScheduleClock.Due(plan, now, TimeZoneInfo.Local);
            if (due == null)
            {
                if (scheduleStore.Last is { State: "waiting" } waiting && DateTimeOffset.TryParse(waiting.Occurrence, out var missed) && now - missed > DailyScheduleClock.Grace)
                    scheduleStore.Record(waiting with { State = "missed", AtUtc = now });
                return;
            }
            if (scheduleStore.IsClaimed(due.Value)) return;
            if (Unavailable || updateInstalling || releaseDialogOpen)
            {
                scheduleStore.Record(new(due.Value.ToString("O"), "waiting", now)); return;
            }
            // Validate all fixed identities before the first account switch. Never fall back to checkbox/current account.
            var catalogNow = sessions.Read();
            var targets = plan.Accounts.Select(key => catalogNow.Accounts.SingleOrDefault(a => a.Valid && a.AccountKey == key)
                ?? throw new InvalidDataException("schedule.account_missing")).ToArray();
            if (!preferencesPanel.SavePending()) throw new InvalidDataException("schedule.unsaved_settings");
            if (!scheduleStore.Claim(due.Value, now)) return;
            scheduleStore.Record(new(due.Value.ToString("O"), "running", now));
            WorkspaceTabs.SelectedItem = RunTab;
            string state = await ExecuteAccountQueue(targets);
            scheduleStore.Record(new(due.Value.ToString("O"), state, DateTimeOffset.UtcNow));
        }
        catch (Exception e)
        {
            if (due != null) { scheduleStore.Claim(due.Value, DateTimeOffset.UtcNow); scheduleStore.Record(new(due.Value.ToString("O"), "failed", DateTimeOffset.UtcNow, e.Message)); }
            schedulePanel.Feedback(e.Message.StartsWith("schedule.") ? e.Message : "schedule.failed", true);
        }
        finally { scheduleChecking = false; }
    }
    private async Task<string> ExecuteAccountQueue(DailyAccount[] targets)
    {
        string state = "failed";
        dailyStopping = false;
        await OperateAsync(async () =>
        {
            try
            {
                state = "completed";
                foreach (var account in targets)
                {
                    if (dailyStopping) { state = "paused"; break; }
                    await coordinator.InspectAsync([account]); RefreshAccounts();
                    if (dailyStopping) { state = "paused"; break; }
                    var result = await dailyQueue.RunAsync(account.AccountKey);
                    if (result.State == "paused" || result.Stages.Any(s => s.State == "recovery_required")) { state = "paused"; break; }
                    if (result.State is not ("completed" or "skipped")) state = "partial";
                }
            }
            catch { state = "failed"; throw; }
        }, connectsGame: true);
        return state;
    }
    private async void RunAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || Unavailable || !preferencesPanel.SavePending()) return;
        var targets = rows.Where(r => r.Selected && r.Account.Valid).Select(r => r.Account).ToArray();
        if (targets.Length == 0 || !Confirm(L.Get("run.multi_confirm", targets.Length))) return;
        WorkspaceTabs.SelectedItem = RunTab;
        await ExecuteAccountQueue(targets);
    }
}

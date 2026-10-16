using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private readonly DailySchedulePanel schedulePanel = new();
    private DailyScheduleStore scheduleStore = null!;
    private bool scheduleChecking;
    private bool scheduleToolDraining;
    private bool scheduleToolNeedsSettlement;
    private GameInstance? scheduleToolGame;
    private DateTime nextScheduleCheck;
    private void InitializeSchedules()
    {
        scheduleStore = new(root); ScheduleTab.Content = schedulePanel;
        schedulePanel.SandboxSetupRequested += () => { if (!Unavailable && !IsSandboxWindow) softwareSandboxPath.OpenEditor(); };
        try { schedulePanel.Show(scheduleStore.Read(), scheduleStore.Last); }
        catch (Exception e) { schedulePanel.Feedback(e.Message, true); }
        schedulePanel.SaveRequested += async plan =>
        {
            if (IsSandboxWindow) return;
            if (smoke != null) { schedulePanel.Feedback("schedule.preview"); return; }
            if (Unavailable || scheduleChecking) return;
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
        if (IsSandboxWindow || smoke != null || scheduleChecking || DateTime.UtcNow < nextScheduleCheck || finalClose) return;
        nextScheduleCheck = DateTime.UtcNow.AddSeconds(10); scheduleChecking = true;
        DateTimeOffset? due = null;
        try
        {
            var plan = scheduleStore.Read(); var now = DateTimeOffset.UtcNow;
            if (!plan.IsDaily)
            {
                schedulePanel.Busy(true);
                try
                {
                    await scheduleStore.MigrateToDailyAsync(p => Task.Run(() => DailyWindowsSchedule.Register(p, Environment.ProcessPath!)), now);
                    schedulePanel.Show(scheduleStore.Read(), scheduleStore.Last);
                    schedulePanel.Feedback("schedule.migrated_daily");
                }
                catch (Exception error)
                {
                    schedulePanel.Show(scheduleStore.Read(), scheduleStore.Last);
                    schedulePanel.Feedback("schedule.register_failed", true);
                    DailyJson.Write(Path.Combine(root, "schedule-error.json"), new { operation = "migrate_daily", error = error.ToString() });
                }
                finally { schedulePanel.Busy(false); }
                return;
            }
            schedulePanel.RefreshStatus(plan, scheduleStore.Last);
            due = DailyScheduleClock.Due(plan, now, TimeZoneInfo.Local);
            if (due == null)
            {
                ResetScheduleHandoff();
                if (scheduleStore.Last is { State: "waiting" } waiting && DateTimeOffset.TryParse(waiting.Occurrence, out var missed) && now - missed > DailyScheduleClock.Grace)
                    scheduleStore.Record(waiting with { State = "missed", AtUtc = now });
                return;
            }
            if (scheduleStore.IsClaimed(due.Value)) return;
            // A running tool is the one optional handoff. Never interrupt another daily queue,
            // an update, onboarding or a user's modal operation.
            if (onboardingOpen || busy || toolChanging || updateInstalling || ParallelBusy || pluginChanging || releaseDialogOpen || DailyDialogs.ModalDepth > 0 || softwareSandboxPath.Preparing)
            {
                scheduleStore.Record(new(due.Value.ToString("O"), "waiting", now)); return;
            }
            // Validate all fixed identities before the first account switch. Never fall back to checkbox/current account.
            var catalogNow = sessions.Read();
            var targets = plan.Accounts.Select(key => catalogNow.Accounts.SingleOrDefault(a => a.Valid && a.AccountKey == key)
                ?? throw new InvalidDataException("schedule.account_missing")).ToArray();
            if (!preferencesPanel.SavePending()) throw new InvalidDataException("schedule.unsaved_settings");
            if (plan.Execution.Enabled && !await softwareSandboxPath.EnsureReadyAsync(scheduled: true))
            {
                // Claim this occurrence so the ten-second poll cannot reopen setup.
                // Validate before stopping tools, switching accounts or creating workers.
                scheduleStore.Claim(due.Value, now);
                scheduleStore.Record(new(due.Value.ToString("O"), "failed", now, "sandbox_setup.schedule_missing"));
                schedulePanel.RefreshStatus(plan, scheduleStore.Last);
                schedulePanel.Feedback("sandbox_setup.schedule_missing", true);
                return;
            }
            if (ToolRunning || scheduleToolDraining || plan.TakeOverTools)
            {
                scheduleStore.Record(new(due.Value.ToString("O"), "waiting", now,
                    plan.TakeOverTools ? "schedule.stopping_tool" : "schedule.tool_busy"));
                if (!plan.TakeOverTools || !await StopToolForScheduleAsync()) return;
            }
            if (!scheduleStore.Claim(due.Value, now)) return;
            scheduleStore.Record(new(due.Value.ToString("O"), "running", now));
            WorkspaceTabs.SelectedItem = RunTab;
            string state = await ExecuteAccountQueue(targets, plan.Execution);
            scheduleStore.Record(new(due.Value.ToString("O"), state, DateTimeOffset.UtcNow));
        }
        catch (Exception e)
        {
            ResetScheduleHandoff();
            if (due != null) { scheduleStore.Claim(due.Value, DateTimeOffset.UtcNow); scheduleStore.Record(new(due.Value.ToString("O"), "failed", DateTimeOffset.UtcNow, e.Message)); }
            schedulePanel.Feedback(e.Message.StartsWith("schedule.") ? e.Message : "schedule.failed", true);
        }
        finally { scheduleChecking = false; }
    }
    private async Task<bool> StopToolForScheduleAsync()
    {
        // Close only the registered tool and the exact game captured before it
        // stopped. The daily account queue relaunches and verifies the target.
        toolChanging = true; SetBusy(busy);
        try
        {
            if (!scheduleToolDraining)
            {
                scheduleToolGame = host.Find();
                scheduleToolNeedsSettlement = ToolRunning;
                scheduleToolDraining = true;
            }
            if (!await CloseToolWindowsAsync()) return false;
            var game = scheduleToolGame;
            var until = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < until)
            {
                if (finalClose) return false;
                bool idle = await Task.Run(() =>
                {
                    if (DailyToolControl.IsOccupied(root)) return false;
                    if (game == null || host.Find() != game) return true;
                    if (!scheduleToolNeedsSettlement) return true;
                    var state = DailySuite.Read(root, game);
                    return state != null && state.At >= DateTime.UtcNow.AddSeconds(-5).Ticks && !DailyActivityRules.ExecutionPending(state.Pending);
                });
                if (idle)
                {
                    schedulePanel.Feedback("schedule.restarting_game");
                    await DailyScheduleTakeover.CloseGameAsync(host, game, CancellationToken.None);
                    ResetScheduleHandoff();
                    return true;
                }
                await Task.Delay(250);
            }
            schedulePanel.Feedback("schedule.takeover_pending", true);
            return false;
        }
        finally { toolChanging = false; SetBusy(busy); }
    }
    private void ResetScheduleHandoff()
    {
        scheduleToolDraining = false; scheduleToolNeedsSettlement = false; scheduleToolGame = null;
    }
    private async Task<string> ExecuteAccountQueue(DailyAccount[] targets, DailyParallelOptions? execution = null)
    {
        var presets = new DailyTaskPresetStore(root);
        var selections = targets.ToDictionary(a => a.AccountKey, a => presets.Selection(a.AccountKey, new DailyPreferenceStore(root).Read(a.AccountKey)));
        execution ??= parallelOptions with { CloseGame = DailyDesktopBehavior.Read(root).CloseGameAfterRun };
        if (execution.Enabled && !IsSandboxWindow)
        {
            if (!await StartParallelAsync(targets, execution)) return "failed";
            while (ParallelBusy) await Task.Delay(500);
            return parallel!.Current!.Items.All(x => x.State == "completed") ? "completed" : "partial";
        }
        string state = "failed";
        var results = new List<DailyAccountRunResult>();
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
                    var result = await RunDailyQueueAsync(account.AccountKey, execution.CloseGame,
                        () => dailyQueue.RunAsync(account.AccountKey, selection: selections[account.AccountKey]));
                    results.Add(new(account.Name, result));
                    if (result.State == "paused" || result.Stages.Any(s => s.State == "recovery_required")) { state = "paused"; break; }
                    if (result.State is not ("completed" or "skipped")) state = "partial";
                }
            }
            catch { state = "failed"; throw; }
        }, connectsGame: true);
        ShowRunResults(results);
        return state;
    }
    private async void RunAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || Unavailable || !preferencesPanel.SavePending()) return;
        var targets = rows.Where(r => r.Selected && r.Account.Valid).Select(r => r.Account).ToArray();
        if (targets.Length == 0 || !Confirm(L.Get(parallelOptions.Enabled ? "parallel.confirm" : "run.multi_confirm", targets.Length))) return;
        WorkspaceTabs.SelectedItem = RunTab;
        try { if (parallelOptions.Enabled && !IsSandboxWindow) await StartParallelAsync(targets); else await ExecuteAccountQueue(targets); }
        catch (Exception error) { ShowError(error); }
    }
}

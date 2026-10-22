using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckUpdateDiscoveryForSmoke()
    {
        var fixture = (DemoEnvironment)host;
        int calls = fixture.Calls.Count, checks = 0, fetches = 0, downloads = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); checks++; }
        static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T item) yield return item;
                foreach (var nested in Find<T>(child)) yield return nested;
            }
        }
        DailyVerifiedUpdate Feed(string version)
        {
            var notes = DailyLanguage.Codes.ToDictionary(x => x, x => new[] { "Synthetic update notice" });
            var asset = new DailyUpdateAsset("Portable", $"Dustweave-{version}-Portable-win-x64.zip", 100, new string('a', 64));
            var release = new DailyUpdateRelease(version, null, 2, notes, [asset], RuntimeVersion: "8.0.21");
            return new(new(3, "Dustweave", "stable", [release]), new(1, "fixture", "fixture", "", ""), "fixture");
        }
        var feed = Feed("99.0.0");
        Exception? fetchError = null, downloadError = null;
        var fetchGate = new TaskCompletionSource<DailyVerifiedUpdate>();
        Task<DailyVerifiedUpdate> Fetch()
        {
            fetches++;
            return fetchError != null ? Task.FromException<DailyVerifiedUpdate>(fetchError) : Task.FromResult(feed);
        }
        Task<DailyUpdateJob> Prepare(DailyVerifiedUpdate verified, DailyUpdateRelease release, IProgress<int> progress)
        {
            downloads++;
            if (downloadError != null) return Task.FromException<DailyUpdateJob>(downloadError);
            string directory = Path.Combine(root, "updates", release.Version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "signed-feed.json"), "synthetic download marker");
            return Task.FromResult(new DailyUpdateJob(directory, "fixture.exe", 0, 0, release, release.Assets[0]));
        }
        Button DownloadButton() => Find<Button>(updatePanel).Single(b => b.Content?.ToString() == L.Get("updates.download"));
        Button InstallButton() => Find<Button>(updatePanel).Single(b => b.Content?.ToString() == L.Get("updates.install"));
        try
        {
            L.Audit();
            updatePanel.Automatic = false;
            WorkspaceTabs.SelectedItem = UpdatesTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var pending = CheckUpdatesCoreAsync(false, () => { fetches++; return fetchGate.Task; }, Prepare);
            Check(updateChecking && !DownloadButton().IsVisible && !InstallButton().IsVisible && UpdateAvailableBadge.Visibility == Visibility.Collapsed,
                "An unknown update was advertised before the verified feed arrived");
            fetchGate.SetResult(feed); await pending;
            Check(fetches == 1 && downloads == 0 && availableUpdateVersion == "99.0.0", "Disabling downloads disabled discovery or downloaded a payload");
            Check(UpdateAvailableBadge.Visibility == Visibility.Visible && readyUpdate == null && updateNotice == null,
                "Discovery did not expose the badge without an interrupting notice");
            Check(DownloadButton().IsVisible && DownloadButton().IsEnabled && !InstallButton().IsVisible, "Manual download action missing");
            Check(nextUpdateCheck > DateTime.UtcNow.AddHours(5), "Background discovery stopped its periodic schedule");
            VersionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(WorkspaceTabs.SelectedItem == UpdatesTab, "Version entry did not open updates");

            Width = 920; Height = 650;
            foreach (string language in DailyLanguage.Codes)
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                L.Select(language); theme.Select(appearance);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(VersionText.Text == DailyProductVersion.Current, "Version entry still has a permanent update label");
                Check(Find<TextBlock>(UpdateAvailableBadge).Single().Text == L.Get("updates.available_badge"), "Badge language stale");
                Check(AutomationProperties.GetName(VersionButton).Contains("99.0.0"), "Accessible update entry omits target version");
                Rect bounds = UpdateAvailableBadge.TransformToAncestor(SidebarPreferences).TransformBounds(new Rect(UpdateAvailableBadge.RenderSize));
                Check(bounds.Right <= SidebarPreferences.ActualWidth && bounds.Bottom <= SidebarPreferences.ActualHeight,
                    "Badge overflows the sidebar at minimum window size");
                Capture("updates-available-" + language + "-" + appearance);
            }
            fetchError = new IOException("Synthetic offline");
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(UpdateAvailableBadge.Visibility == Visibility.Visible && availableUpdateVersion == "99.0.0" && downloads == 0,
                "A failed check hid a known update");
            fetchError = null;
            // Explicit downloads work even with automatic downloads disabled.
            downloadError = new IOException("Synthetic interrupted download");
            await CheckUpdatesCoreAsync(true, Fetch, Prepare);
            Check(downloads == 1 && DownloadButton().IsEnabled && DownloadButton().IsVisible && UpdateAvailableBadge.IsVisible,
                "Download failure lost discovery or the retry action");
            downloadError = null;
            await CheckUpdatesCoreAsync(true, Fetch, Prepare);
            Check(downloads == 2 && readyUpdate?.Release.Version == "99.0.0" && InstallButton().IsVisible && !DownloadButton().IsVisible,
                "Explicit download did not become ready");
            fetchError = new IOException("Synthetic offline");
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(readyUpdate != null && InstallButton().IsEnabled && UpdateAvailableBadge.IsVisible, "Recheck failure discarded a verified package");
            fetchError = null;
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(downloads == 2 && readyUpdate != null, "Unchanged release was downloaded again");

            feed = Feed("99.0.1");
            updatePanel.Automatic = true;
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(downloads == 3 && readyUpdate?.Release.Version == "99.0.1" && UpdateAvailableBadge.IsVisible, "Automatic download did not follow the newer release");
            feed = Feed("99.0.2"); downloadError = new InvalidDataException("updates.manual_layout");
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(manualUpdateVersion == "99.0.2" && UpdateAvailableBadge.IsVisible && !DownloadButton().IsVisible && !InstallButton().IsVisible,
                "A manual upgrade lost its badge or advertised an invalid action");
            manualUpdateReason = "updates.manual_delta";
            var repairNotice = CreateManualUpdateWindow();
            ShowUpdateNotice(repairNotice);
            Find<Button>(repairNotice).Single(b => b.Content?.ToString() == L.Get("repair.goto"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(updateNotice == null && WorkspaceTabs.SelectedItem == UpdatesTab,
                "A damaged update baseline did not lead back to installation repair");
            feed = Feed(DailyProductVersion.Current); downloadError = null;
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(availableUpdateVersion == null && UpdateAvailableBadge.Visibility == Visibility.Collapsed && readyUpdate == null && manualUpdateVersion == null,
                "A verified current version retained an update badge");
            Check(!DownloadButton().IsVisible && !InstallButton().IsVisible && downloads == 4, "Up-to-date state still advertises a payload");
            L.Select("zh-CN"); theme.Select(DailyAppearance.Light);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("updates-current");
            foreach (string language in new[] { "zh-CN", "zh-TW", "en-US" })
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                L.Select(language); theme.Select(appearance);
                updatePanel.Repair.Problems([new("app/Dustweave.Core.dll", false, 4000000), new("app/coreclr.dll", true, 5000000)]);
                updatePanel.Repair.State("repair.found", "repair.download", false, 2, "2.13");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(Find<Button>(updatePanel.Repair).Any(b => b.Content?.ToString() == L.Get("repair.download") && b.IsEnabled), "Repair action did not localize");
                Check(Find<TextBlock>(updatePanel.Repair).All(t => !t.Text.StartsWith("repair.")), "Raw repair key displayed");
                Capture("repair-found-" + language + "-" + appearance);
            }
            updatePanel.Repair.State("repair.preparing", "repair.check", true, 12, 600);
            Check(Find<Button>(updatePanel.Repair).Any(b => b.Content?.ToString() == L.Get("repair.cancel") && b.IsVisible), "Repair cancellation missing");
            updatePanel.Repair.State("repair.ready", "repair.restart", false);
            Check(Find<Button>(updatePanel.Repair).Any(b => b.Content?.ToString() == L.Get("repair.restart") && b.IsEnabled), "Repair restart action missing");
            updatePanel.Repair.Problems([]); updatePanel.Repair.State("repair.idle", "repair.check", false);
            await CheckUpdateSchedulePriorityForSmoke(Check, Find<Button>);
            await CheckUpdateRestartForSmoke(Check, Find<Button>, Find<TextBlock>);
            foreach (string language in DailyLanguage.Codes)
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                L.Select(language); theme.Select(appearance);
                var failure = new DailyUpdateEngine.Result("rolled_back", "99.0.3", DateTimeOffset.UtcNow, "synthetic startup failure");
                DailyJson.Write(Path.Combine(root, "update-result.json"), failure);
                Check(ShowUpdateFailure() && updateNotice != null, "Rollback must be explained without running daily tasks");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(Find<TextBlock>(updateNotice!).Any(t => t.Text.Contains("99.0.3") && t.Text.Contains(DailyProductVersion.Current)), "Rollback notice must distinguish requested and running versions");
                Check(DailyDialogs.ModalDepth == 0, "Rollback notice must not block the dispatcher");
                Capture("update-rollback-" + language + "-" + appearance, updateNotice);
                Find<Button>(updateNotice!).Single(b => b.Content?.ToString() == L.Get("updates.rollback_review")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(WorkspaceTabs.SelectedItem == UpdatesTab && !ShowUpdateFailure(), "Acknowledged rollback is shown once and leads to update recovery");
            }
            File.Delete(Path.Combine(root, "update-result.json"));
            File.Delete(Path.Combine(root, "update-failure-seen.json"));
            Check(fixture.Calls.Count == calls && DailyDialogs.ModalDepth == 0, "Discovery touched the game or opened a modal");
            DailyJson.Write(Path.Combine(smoke!, "update-discovery.json"), new { status = "passed", checks, fetches, downloads, realGameTouched = false });
        }
        finally
        {
            readyUpdate = null; manualUpdateVersion = null; manualUpdateReason = null; availableUpdateVersion = null;
            CloseUpdateNotice();
        }
    }

    private async Task CheckUpdateRestartForSmoke(Action<bool, string> check,
        Func<DependencyObject, IEnumerable<Button>> buttons, Func<DependencyObject, IEnumerable<TextBlock>> texts)
    {
        var previousUpdate = readyUpdate;
        var previousStore = scheduleStore;
        var previousConnection = connectionUpdate;
        bool previousReading = connectionReading, previousAccounts = readingAccounts, previousChecking = updateChecking, previousBusy = busy;
        int starts = 0;
        var pendingObservation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asset = new DailyUpdateAsset("Portable", "fixture.zip", 100, new string('a', 64));
        var release = new DailyUpdateRelease("99.1.0", null, 2, DailyLanguage.Codes.ToDictionary(x => x, _ => new[] { "Fixture" }), [asset]);
        var job = new DailyUpdateJob(root, "fixture.exe", 0, 0, release, asset);
        Task? request = null;
        Task Start(DailyUpdateJob selected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            check(selected.Release.Version == job.Release.Version && UpdateRestarting && !ConnectButton.IsEnabled,
                "Restart did not reserve the host before launching the updater");
            starts++; return Task.CompletedTask;
        }
        Window Show(Func<DailyUpdateJob, CancellationToken, Task>? launch = null)
        {
            var notice = CreateUpdateReadyWindow(job.Release.Version, (version, token) => request = RequestUpdateInstallCoreAsync(readyUpdate, version, launch ?? Start, token));
            notice.WindowStartupLocation = WindowStartupLocation.Manual; notice.Left = -12000; notice.Top = 0;
            ShowUpdateNotice(notice); return notice;
        }
        Button Restart(Window notice) => buttons(notice).Single(b => b.Content?.ToString() == L.Get("updates.install"));
        string Feedback(Window notice) => texts(notice).Single(t => t.Name == "UpdateRestartFeedback").Text;
        try
        {
            scheduleStore = new(Path.Combine(root, "update-restart-probe"));
            readyUpdate = job; busy = false;
            // Reproduce an update arriving while the fixed startup observations
            // are still pending, with no RunOnOpen task involved.
            connectionReading = readingAccounts = true;
            connectionUpdate = pendingObservation.Task;
            updateChecking = true;
            var notice = Show();
            Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(request != null && !request.IsCompleted && updateRestartPending && starts == 0 && notice.IsVisible,
                "An in-flight check discarded the restart click or closed its notice");
            check(Feedback(notice) == L.Get("updates.restart_waiting") && !Restart(notice).IsEnabled,
                "Pending restart has no visible explanation or accepts duplicate clicks");
            await RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
            check(starts == 0, "A duplicate click bypassed the pending restart");
            UpdateConnection(); await ReadConnectionAsync(); await RefreshAccountIdentityAsync();
            check(ReferenceEquals(connectionUpdate, pendingObservation.Task), "A restart request launched another connection observation");
            updateChecking = false;
            await request!.WaitAsync(TimeSpan.FromSeconds(3));
            check(starts == 1 && !UpdateRestarting && !pendingObservation.Task.IsCompleted && notice.IsVisible,
                "Passive connection/account polling blocked installation or the accepted request was lost");
            CloseUpdateNotice();

            busy = true; notice = Show(); Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await request!;
            check(starts == 1 && notice.IsVisible && Feedback(notice) == L.Get("updates.restart_task"),
                "A real task was interrupted or its restart block was silent");
            busy = false; CloseUpdateNotice();

            readyUpdate = job with { Release = release with { Version = "99.1.1" } };
            notice = Show(); Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await request!;
            check(starts == 1 && notice.IsVisible && Feedback(notice) == L.Get("updates.restart_changed"),
                "An outdated notice silently closed or installed the wrong version");
            CloseUpdateNotice(); readyUpdate = job;

            updateChecking = true; notice = Show(); Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            readyUpdate = job with { Release = release with { Version = "99.1.1" } }; updateChecking = false;
            await request!.WaitAsync(TimeSpan.FromSeconds(3));
            check(starts == 1 && notice.IsVisible && Feedback(notice) == L.Get("updates.restart_changed"),
                "A version change during the background wait was not revalidated");
            CloseUpdateNotice(); readyUpdate = job;

            var now = DateTimeOffset.UtcNow; var local = now.ToLocalTime();
            scheduleStore.Save(new DailySchedulePlan { Enabled = true, Hour = local.Hour, Minute = local.Minute,
                Accounts = [new string('f', 64)], SavedUtc = now.AddDays(-1) });
            notice = Show(); Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await request!;
            check(starts == 1 && notice.IsVisible && !UpdateRestarting && Feedback(notice) == L.Get("updates.schedule_first"),
                "Restart overtook a due schedule or silently discarded its priority decision");
            CloseUpdateNotice(); scheduleStore.Save(new DailySchedulePlan());

            // Closing or deferring an accepted but waiting request cancels it.
            updateChecking = true; notice = Show(); Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons(notice).Single(b => b.Content?.ToString() == L.Get("updates.later")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await request!.WaitAsync(TimeSpan.FromSeconds(3)); updateChecking = false;
            check(starts == 1 && !UpdateRestarting && updateNotice == null, "Deferring a waiting restart still launched the updater or left controls locked");

            notice = Show((_, _) => Task.FromException(new IOException("Synthetic helper failure")));
            Restart(notice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await request!;
            check(notice.IsVisible && !UpdateRestarting && Restart(notice).IsEnabled && Feedback(notice) == L.Get("updates.start_failed"),
                "Updater startup failure disappeared with the notice or prevented retry");
            foreach (string language in DailyLanguage.Codes)
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                L.Select(language); theme.Select(appearance);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                check(Feedback(notice) == L.Get("updates.start_failed"), "Restart failure feedback did not follow language changes");
                Capture("update-restart-failure-" + language + "-" + appearance, (FrameworkElement)notice.Content);
            }
            CloseUpdateNotice();
            await CheckUpdateMaintenanceForSmoke(check, job);
        }
        finally
        {
            CloseUpdateNotice(); readyUpdate = previousUpdate; scheduleStore = previousStore;
            connectionUpdate = previousConnection; connectionReading = previousReading; readingAccounts = previousAccounts;
            updateChecking = previousChecking; busy = previousBusy; updateInstalling = updateRestartPending = false; SetBusy(busy);
        }
    }

    private async Task CheckUpdateMaintenanceForSmoke(Action<bool, string> check, DailyUpdateJob job)
    {
        int starts = 0;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? status = null;
        void Report(string key, object?[] args) => status = key;
        UpdateInstallStatusChanged += Report;
        Task Start(DailyUpdateJob selected, CancellationToken token)
        {
            // Use real fixture file leases, not just simulated busy flags.
            using var control = DailyUpdateHost.AcquireControl(root);
            using var storage = DailyUpdateHost.AcquireStorage(root, exclusive: true);
            check(!logCleanupRunning && !cacheChanging && UpdateRestarting,
                "Updater launched before background maintenance released its files");
            starts++; return Task.CompletedTask;
        }
        Task? maintenance = null;
        try
        {
            readyUpdate = job;
            maintenance = RunAutomaticMaintenanceAsync(async token =>
            {
                SetAutomaticCacheChanging(true);
                try
                {
                    using var control = DailyUpdateHost.AcquireControl(root);
                    using var storage = DailyUpdateHost.AcquireStorage(root, exclusive: true);
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch (OperationCanceledException) { cancelled.TrySetResult(); await release.Task; throw; }
                }
                finally { SetAutomaticCacheChanging(false); }
            });
            check(ToolRunning && cacheChanging && UpdateRestartBlockReason(true) == null,
                "Automatic cleanup is incorrectly presented as a running tool");
            var request = RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(UpdateRestarting && starts == 0 && !request.IsCompleted && status == "updates.restart_maintenance",
                "Cleanup swallowed the restart click or did not receive cancellation");
            var pending = logCleanupCompletion;
            nextLogCleanup = DateTime.MinValue;
            await CheckLogCleanupAsync();
            await RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
            check(ReferenceEquals(pending, logCleanupCompletion) && starts == 0,
                "A restart allowed new maintenance or duplicate installation");
            release.TrySetResult();
            await request.WaitAsync(TimeSpan.FromSeconds(3));
            await maintenance;
            check(starts == 1 && !UpdateRestarting, "Restart did not automatically continue after cleanup stopped");

            // An unrelated tool may own the shared lease while maintenance is
            // still flushing evidence. Recheck that lease after the drain.
            using (DailyUpdateHost.AcquireControl(root))
            {
                maintenance = RunAutomaticMaintenanceAsync(token => Task.Delay(Timeout.Infinite, token));
                await RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
                await maintenance;
                check(starts == 1 && status == "updates.restart_tool_running", "Maintenance cancellation bypassed a real tool's lease");
            }
            SetCacheChanging(true);
            await RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
            check(starts == 1 && status == "updates.restart_storage", "Update interrupted a user's cache migration");
            SetCacheChanging(false);
            await RequestUpdateInstallCoreAsync(job, job.Release.Version, Start);
            check(starts == 2, "Host did not recover after maintenance blocking conditions ended");
            string trace = File.ReadAllText(Path.Combine(root, "updates", "restart-state.json"));
            check(trace.Contains("updates.restart_maintenance") && trace.Contains("updates.restart_tool_running") && trace.Contains("updates.restart_storage"),
                "Restart diagnostics do not distinguish automatic maintenance, actual tools and manual storage changes");
        }
        finally
        {
            release.TrySetResult(); logCleanupCancellation?.Cancel();
            if (maintenance != null) await maintenance;
            SetCacheChanging(false); UpdateInstallStatusChanged -= Report;
        }
    }
}

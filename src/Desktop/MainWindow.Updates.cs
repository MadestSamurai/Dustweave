using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dustweave.Desktop;

public sealed class DailyUpdatePanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) };
    private readonly Button install = new() { Margin = new(0, 0, 10, 6), Visibility = Visibility.Collapsed };
    private readonly Button download = new() { Margin = new(0, 0, 10, 6), Visibility = Visibility.Collapsed };
    private readonly Button check = new() { Margin = new(0, 0, 10, 6) };
    private readonly CheckBox automatic = new() { Margin = new(0, 12, 0, 12) };
    private readonly StackPanel notes = new();
    public DailyRepairPanel Repair { get; } = new();
    public event Action? CheckRequested;
    public event Action? DownloadRequested;
    public event Action? InstallRequested;
    public event Action<bool>? AutomaticChanged;
    public bool Automatic { get => automatic.IsChecked == true; set => automatic.IsChecked = value; }
    public DailyUpdatePanel()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new(0, 0, 8, 0) };
        var card = new StackPanel(); var title = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Text = "Dustweave " + DailyProductVersion.Current }; card.Children.Add(title);
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; L.Text(help, "updates.help"); help.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); card.Children.Add(help);
        L.Bind(automatic, ContentControl.ContentProperty, "updates.automatic"); card.Children.Add(automatic);
        var actions = new WrapPanel(); L.Bind(check, ContentControl.ContentProperty, "updates.check"); actions.Children.Add(check);
        L.Bind(download, ContentControl.ContentProperty, "updates.download"); download.Style = (Style)Application.Current.FindResource("PrimaryButton"); actions.Children.Add(download);
        L.Bind(install, ContentControl.ContentProperty, "updates.install"); install.Style = (Style)Application.Current.FindResource("PrimaryButton"); actions.Children.Add(install); card.Children.Add(actions); card.Children.Add(status);
        stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(20), Margin = new(0, 0, 0, 16), Child = card });
        stack.Children.Add(DailyDownloadLinks.Create()); stack.Children.Add(Repair); stack.Children.Add(notes);
        var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition {MaxWidth=880});layout.Children.Add(stack);Content = layout;
        check.Click += (_, _) => CheckRequested?.Invoke(); install.Click += (_, _) => InstallRequested?.Invoke();
        download.Click += (_, _) => DownloadRequested?.Invoke();
        automatic.Click += (_, _) => AutomaticChanged?.Invoke(Automatic);
        L.Text(status, "updates.idle");
    }
    public void Status(string key, params object?[] args) => L.Text(status, key, args);
    public void Busy(bool network, bool ready, bool occupied, bool available = false)
    {
        check.IsEnabled = !network;
        download.Visibility = available && !ready ? Visibility.Visible : Visibility.Collapsed;
        download.IsEnabled = !network;
        install.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        install.IsEnabled = ready && !occupied && !network;
    }
    public void ShowNotes(IEnumerable<DailyReleaseNote> releases)
    {
        notes.Children.Clear();
        bool first=true;
        foreach (var release in releases)
        {
            var body = new StackPanel { Margin=new(0,12,0,0) };
            foreach (int i in Enumerable.Range(0, release.Notes["zh-CN"].Length))
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10), FontSize = 14 };
                L.Bind(text, TextBlock.TextProperty, () => "• " + release.Notes[L.Code][i]); body.Children.Add(text);
            }
            var heading=new TextBlock {Text=release.Version+"  ·  "+release.Date,FontSize=16,FontWeight=FontWeights.SemiBold};
            var expander=new Expander { Header=heading,Content=body,IsExpanded=first,Style=(Style)Application.Current.FindResource("DisclosureExpander"),Margin=new(0,16,0,16) };
            first=false;
            var section=new Border { BorderThickness=new(0,0,0,1),Child=expander };section.SetResourceReference(Border.BorderBrushProperty,"Line");notes.Children.Add(section);
        }
    }
}

public sealed record DailyUpdatePreference(bool Automatic = true);
public sealed record DailySeenRelease(string Version, string[]? SeenVersions = null);

public static class DailyReleaseHistory
{
    public static DailyReleaseNote[] Read()
    {
        using var stream = typeof(DailyReleaseHistory).Assembly.GetManifestResourceStream("Dustweave.UI.releases.json")!;
        return System.Text.Json.JsonSerializer.Deserialize<DailyReleaseNote[]>(stream, DailyJson.Options)!;
    }
    public static bool ShouldShow(string root, string version)
    {
        var seen = DailyJson.TryRead<DailySeenRelease>(Path.Combine(root, "seen-release.json"));
        return seen?.Version != version && seen?.SeenVersions?.Contains(version) != true;
    }
    public static void MarkSeen(string root, string version)
    {
        string path = Path.Combine(root, "seen-release.json");
        var seen = DailyJson.TryRead<DailySeenRelease>(path);
        DailyJson.Write(path, new DailySeenRelease(version, (seen?.SeenVersions ?? []).Append(seen?.Version ?? version).Append(version).Distinct().ToArray()));
    }
}

public partial class MainWindow
{
    private readonly DailyUpdatePanel updatePanel = new();
    private readonly CancellationTokenSource updateLifetime = new();
    private bool updateChecking, updateInstalling, updatePromptShown, releaseDialogOpen;
    private bool updateRestartPending;
    private bool UpdateRestarting => updateRestartPending || updateInstalling;
    private event Action<string, object?[]>? UpdateInstallStatusChanged;
    private event Action? UpdateInstallControlsChanged;
    private readonly Queue<object> updateRestartEvents = new();
    private void UpdateInstallStatus(string key, params object?[] args)
    {
        updatePanel.Status(key, args);
        UpdateInstallStatusChanged?.Invoke(key, args);
        // One bounded, non-sensitive trace survives a later successful update.
        // A generic busy flag alone cannot identify the startup task involved.
        try
        {
            updateRestartEvents.Enqueue(new { atUtc = DateTimeOffset.UtcNow, state = key,
                version = DailyProductVersion.Current, target = readyUpdate?.Release.Version,
                busy, manualCacheChanging, automaticCacheChanging, logCleanupRunning,
                updateChecking, scheduleChecking, connectionReading, readingAccounts,
                toolChanging, toolRunning = ToolRunning, parallel = ParallelBusy,
                pluginChanging, onboardingOpen, modalDepth = DailyDialogs.ModalDepth });
            while (updateRestartEvents.Count > 32) updateRestartEvents.Dequeue();
            DailyJson.Write(Path.Combine(root, "updates", "restart-state.json"), new { events = updateRestartEvents.ToArray() });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private string? UpdateRestartBlockReason(bool drainingMaintenance = false)
    {
        if (busy || ParallelBusy) return "updates.restart_task";
        if (manualCacheChanging) return "updates.restart_storage";
        if (toolChanging) return "updates.restart_tool_connecting";
        if (onboardingOpen || pluginChanging || softwareSandboxPath?.Preparing == true || releaseDialogOpen || DailyDialogs.ModalDepth > 0)
            return "updates.restart_settings_open";
        // Automatic retention owns the same control lease as a tool. Drain it
        // before deciding whether an actual tool is running; never ignore that
        // lease at installer handoff.
        if (!drainingMaintenance && ToolRunning) return "updates.restart_tool_running";
        return null;
    }
    private Window? updateNotice;
    private DailyUpdateJob? readyUpdate;
    private string? availableUpdateVersion;
    private string? manualUpdateVersion, manualUpdateReason;
    private DateTime nextUpdateCheck = DateTime.UtcNow.AddHours(6);
    private void Updates_Click(object sender, RoutedEventArgs e) { if (!IsSandboxWindow) WorkspaceTabs.SelectedItem = UpdatesTab; else L.Text(ProgressText, "sandbox.host_only"); }
    private void InitializeUpdates()
    {
        UpdatesTab.Content = updatePanel;
        updatePanel.Automatic = DailyJson.TryRead<DailyUpdatePreference>(Path.Combine(root, "updates-preference.json"))?.Automatic ?? true;
        updatePanel.ShowNotes(DailyReleaseHistory.Read());
        updatePanel.AutomaticChanged += async value =>
        {
            if (smoke == null) DailyJson.Write(Path.Combine(root, "updates-preference.json"), new DailyUpdatePreference(value));
            if (value && availableUpdateVersion != null && readyUpdate == null) await CheckUpdatesAsync();
        };
        updatePanel.CheckRequested += async () => await CheckUpdatesAsync();
        updatePanel.DownloadRequested += async () => await CheckUpdatesAsync(download: true);
        updatePanel.InstallRequested += async () => await InstallUpdateAsync();
        InitializeRepair();
        Closed += (_, _) => updateLifetime.Cancel();
    }
    private void ShowReleaseNotes()
    {
        if (smoke != null || !DailyReleaseHistory.ShouldShow(root, DailyProductVersion.Current) || Unavailable) return;
        ShowUpdateNotice(CreateReleaseNotesWindow());
    }
    private void ShowUpdateNotice(Window dialog)
    {
        // Informational updates must never occupy the queue or enter a nested modal loop.
        CloseUpdateNotice();
        updateNotice = dialog;
        dialog.ShowActivated = false;
        dialog.ShowInTaskbar = false;
        dialog.Closed += (_, _) => { if (ReferenceEquals(updateNotice, dialog)) updateNotice = null; };
        DailyDialogs.Prepare(dialog);
        dialog.Show();
    }
    private void CloseUpdateNotice() => updateNotice?.Close();
    private Window CreateReleaseNotesWindow()
    {
            var panel = new StackPanel { Margin = new(24) };
            var title = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }; L.Text(title, "updates.whats_new", DailyProductVersion.Current); panel.Children.Add(title);
            panel.Children.Add(DailyDownloadLinks.Create());
            foreach (var release in DailyReleaseHistory.Read().Where(r => r.Version == DailyProductVersion.Current))
                foreach (var i in Enumerable.Range(0, release.Notes["zh-CN"].Length))
                {
                    var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0), FontSize = 14 };
                    L.Bind(text, TextBlock.TextProperty, () => "• " + release.Notes[L.Code][i]); panel.Children.Add(text);
                }
            var button = new Button { Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0), IsDefault = true }; L.Bind(button, ContentControl.ContentProperty, "updates.got_it"); panel.Children.Add(button);
            var dialog = new Window { Owner = this, Width = 580, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 80), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            L.Bind(dialog, Window.TitleProperty, "updates.notes");
            button.Click += (_, _) => dialog.Close();
            dialog.ContentRendered += (_, _) => DailyReleaseHistory.MarkSeen(root, DailyProductVersion.Current);
            DailyDialogs.Prepare(dialog);
            return dialog;
    }
    private async Task CheckUpdatesAsync(bool download = false)
    {
        if (IsSandboxWindow || smoke != null || cacheChanging || updateChecking || UpdateRestarting) return;
        using var client = DailyUpdates.Client();
        var transport = DailyUpdateTransport.Production();
        await CheckUpdatesCoreAsync(download,
            () => transport.FetchAsync(client, root, updateLifetime.Token),
            async (verified, release, progress) =>
            {
                var installed = DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(DailyUpdateHost.InstallRoot, "update-package.json"));
                string flavor = installed?.Flavor ?? throw new InvalidDataException("updates.packaged_only");
                string? reason = DailyUpdates.ManualUpdateReason(verified.Feed, release, installed);
                if (reason != null) throw new InvalidDataException(reason);
                var asset = release.Assets.SingleOrDefault(a => a.Flavor == flavor) ?? throw new InvalidDataException("updates.flavor_missing");
                string directory = await transport.DownloadAsync(client, verified, release.Version, flavor, root, progress, updateLifetime.Token, DailyUpdateHost.InstallRoot);
                using var process = Process.GetCurrentProcess();
                return new DailyUpdateJob(directory, Environment.ProcessPath!, process.Id, process.StartTime.ToUniversalTime().Ticks, release, asset,
                    DeltaFileName: DailyJson.TryRead<DailyUpdateDownload>(Path.Combine(directory, "download-choice.json"))?.DeltaFileName)
                {
                    InventoryUpgrade = release.InstallerProtocol == DailyUpdateInventory.Protocol,
                    FromVersion = installed!.Version
                };
            });
    }
    private async Task CheckUpdatesCoreAsync(bool download, Func<Task<DailyVerifiedUpdate>> fetch,
        Func<DailyVerifiedUpdate, DailyUpdateRelease, IProgress<int>, Task<DailyUpdateJob>> prepare)
    {
        nextUpdateCheck = DateTime.UtcNow.AddHours(6);
        updateChecking = true; updatePanel.Status("updates.checking"); UpdateUpdateControls();
        string? availableVersion = null;
        try
        {
            // Discovery is independent of automatic downloads. Keep the last verified
            // update visible during network failures; only a successful check clears it.
            var verified = await fetch();
            var feed = verified.Feed;
            var release = DailyUpdates.Latest(feed, DailyProductVersion.Current);
            updatePanel.ShowNotes(feed.Releases.OrderByDescending(r => DailyUpdates.VersionOf(r.Version)).Select(r => new DailyReleaseNote(r.Version, "", r.Notes)));
            if (release == null)
            {
                if (availableUpdateVersion != null && !updateRestartPending) CloseUpdateNotice();
                availableUpdateVersion = null; readyUpdate = null; manualUpdateVersion = null; manualUpdateReason = null;
                updatePromptShown = false;
                updatePanel.Status("updates.current"); return;
            }
            availableVersion = release.Version;
            if (availableUpdateVersion != release.Version)
            {
                if (availableUpdateVersion != null && !updateRestartPending) CloseUpdateNotice();
                readyUpdate = null; manualUpdateVersion = null; manualUpdateReason = null;
                updatePromptShown = false;
            }
            availableUpdateVersion = release.Version;
            UpdateUpdateControls();
            if (readyUpdate != null && File.Exists(Path.Combine(readyUpdate.Directory, "signed-feed.json")))
            {
                updatePanel.Status("updates.ready", release.Version); return;
            }
            readyUpdate = null;
            if (!download && !updatePanel.Automatic)
            {
                if (manualUpdateReason != null) updatePanel.Status(manualUpdateReason);
                else updatePanel.Status("updates.available", release.Version);
                return;
            }
            manualUpdateVersion = null; manualUpdateReason = null;
            var progress = new Progress<int>(p => updatePanel.Status("updates.downloading", release.Version, p));
            readyUpdate = await prepare(verified, release, progress);
            updatePanel.Status("updates.ready", release.Version); updatePromptShown = false;
            nextLogCleanup = DateTime.UtcNow; // Retire superseded downloads at the next idle interval.
        }
        catch (OperationCanceledException) { if (!updateLifetime.IsCancellationRequested) updatePanel.Status("updates.unavailable"); }
        catch (Exception e)
        {
            if (e.Message.StartsWith("updates.manual_", StringComparison.Ordinal)) { manualUpdateVersion = availableVersion; manualUpdateReason = e.Message; }
            updatePanel.Status(e.Message.StartsWith("updates.") ? e.Message : "updates.unavailable");
            DailyJson.Write(Path.Combine(root, "updates", "check-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = e.ToString() });
        }
        finally { updateChecking = false; UpdateUpdateControls(); }
    }
    private void UpdateUpdateControls()
    {
        updatePanel.Busy(updateChecking || UpdateRestarting, readyUpdate != null, UpdateRestartBlockReason(logCleanupRunning) != null || scheduleChecking,
            availableUpdateVersion != null && manualUpdateVersion == null);
        UpdateAvailableBadge.Visibility = availableUpdateVersion != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstallControlsChanged?.Invoke();
        string description() => availableUpdateVersion != null
            ? L.Get("updates.available_hint", availableUpdateVersion)
            : L.Get("nav.updates");
        L.Bind(VersionButton, ToolTipProperty, description);
        L.Bind(VersionButton, System.Windows.Automation.AutomationProperties.NameProperty, description);
    }
    private sealed record ManualUpdateNotice(string Version);
    private Task OfferUpdateAsync()
    {
        OfferUpdateNotice();
        return Task.CompletedTask;
    }
    private void OfferUpdateNotice()
    {
        if (IsSandboxWindow || smoke != null || finalClose || updateChecking || updatePromptShown || UpdateRestarting || UpdateRestartBlockReason(logCleanupRunning) != null || scheduleChecking) return;
        if (manualUpdateVersion == null && readyUpdate == null || HasPendingScheduledRun(DateTimeOffset.UtcNow)) return;
        if (manualUpdateVersion != null)
        {
            string seen = Path.Combine(root, "updates", "manual-notice.json");
            if (DailyJson.TryRead<ManualUpdateNotice>(seen)?.Version == manualUpdateVersion) return;
            DailyJson.Write(seen, new ManualUpdateNotice(manualUpdateVersion));
            ShowUpdateNotice(CreateManualUpdateWindow());
            return;
        }
        if (readyUpdate == null) return;
        updatePromptShown = true;
        ShowUpdateNotice(CreateUpdateReadyWindow(readyUpdate.Release.Version));
    }
    private Window CreateManualUpdateWindow()
    {
            var panel = new StackPanel { Margin = new(24) };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20) };
            L.Text(text, manualUpdateReason ?? "updates.manual_layout"); panel.Children.Add(text);
            if (manualUpdateReason == "updates.manual_delta")
            {
                var repair = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 16) };
                L.Bind(repair, ContentControl.ContentProperty, "repair.goto");
                repair.Click += (_, _) =>
                {
                    CloseUpdateNotice();
                    WorkspaceTabs.SelectedItem = UpdatesTab;
                    updatePanel.Repair.BringIntoView();
                };
                panel.Children.Add(repair);
            }
            panel.Children.Add(DailyDownloadLinks.Create());
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0), IsDefault = true };
            L.Bind(button, ContentControl.ContentProperty, "updates.got_it"); panel.Children.Add(button);
            var dialog = new Window { Owner = this, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
            L.Bind(dialog, Window.TitleProperty, "updates.manual_title"); button.Click += (_, _) => dialog.Close();
            DailyDialogs.Prepare(dialog); return dialog;
    }
    private Window CreateUpdateReadyWindow(string version, Func<string, CancellationToken, Task>? requestRestart = null)
    {
        var panel = new StackPanel { Margin = new(24) };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        L.Text(text, "updates.confirm", version); panel.Children.Add(text);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
        L.Text(hint, "updates.nonblocking"); hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); panel.Children.Add(hint);
        var feedback = new TextBlock { Name = "UpdateRestartFeedback", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) };
        var later = new Button { IsDefault = true, Margin = new(0, 0, 10, 0) };
        var install = new Button { Style = (Style)FindResource("PrimaryButton") };
        L.Bind(later, ContentControl.ContentProperty, "updates.later"); L.Bind(install, ContentControl.ContentProperty, "updates.install");
        actions.Children.Add(later); actions.Children.Add(install); panel.Children.Add(actions);
        var dialog = new Window { Owner = this, Width = 560, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 80), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        L.Bind(dialog, Window.TitleProperty, "updates.title");
        var lifetime = new CancellationTokenSource();
        void Report(string key, object?[] args) { L.Text(feedback, key, args); feedback.Visibility = Visibility.Visible; }
        void Refresh() => install.IsEnabled = !UpdateRestarting;
        UpdateInstallStatusChanged += Report;
        UpdateInstallControlsChanged += Refresh;
        dialog.Closed += (_, _) => { lifetime.Cancel(); UpdateInstallStatusChanged -= Report; UpdateInstallControlsChanged -= Refresh; lifetime.Dispose(); };
        later.Click += (_, _) => dialog.Close();
        install.Click += async (_, _) =>
        {
            // Keep feedback visible until the updater has actually been started.
            if (requestRestart != null) await requestRestart(version, lifetime.Token);
            else await InstallUpdateAsync(expectedVersion: version, cancellation: lifetime.Token);
        };
        DailyDialogs.Prepare(dialog); return dialog;
    }
    private async Task InstallUpdateAsync(DailyUpdateJob? repair = null, string? expectedVersion = null, CancellationToken cancellation = default)
    {
        if (smoke != null) return;
        await RequestUpdateInstallCoreAsync(repair ?? readyUpdate, expectedVersion, StartUpdateInstallerAsync, cancellation);
    }
    private async Task RequestUpdateInstallCoreAsync(DailyUpdateJob? installation, string? expectedVersion,
        Func<DailyUpdateJob, CancellationToken, Task> startInstaller, CancellationToken cancellation = default)
    {
        if (UpdateRestarting) return; // The accepted request owns the pending transition.
        if (IsSandboxWindow) { UpdateInstallStatus("updates.isolated_open"); return; }
        if (installation == null || expectedVersion != null && installation.Release.Version != expectedVersion)
        { UpdateInstallStatus("updates.restart_changed"); return; }
        if (UpdateRestartBlockReason(logCleanupRunning) is {} initialBlock) { UpdateInstallStatus(initialBlock); return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, updateLifetime.Token);
        bool scheduledFirst = false;
        updateRestartPending = true;
        try
        {
            SetBusy(busy);
            UpdateInstallStatus("updates.restart_preparing");
            if (logCleanupRunning)
            {
                UpdateInstallStatus("updates.restart_maintenance");
                logCleanupCancellation?.Cancel();
                // Cancellation is cooperative: do not hand the updater files
                // while the maintenance worker still owns its write locks.
                try { await logCleanupCompletion.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token); }
                catch (TimeoutException) { UpdateInstallStatus("updates.restart_maintenance_timeout"); return; }
            }
            // A background update/schedule check may already be in flight. Preserve
            // this click while it finishes; do not start new background work meanwhile.
            // Connection/account observations are read-only and never gate a restart.
            var wait = Stopwatch.StartNew();
            if (updateChecking || scheduleChecking) UpdateInstallStatus("updates.restart_waiting");
            while (updateChecking || scheduleChecking)
            {
                if (wait.Elapsed > TimeSpan.FromSeconds(15)) { UpdateInstallStatus("updates.restart_check_timeout"); return; }
                await Task.Delay(50, lifetime.Token);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (UpdateRestartBlockReason() is {} finalBlock) { UpdateInstallStatus(finalBlock); return; }
            if (!installation.Repair && readyUpdate?.Release.Version != installation.Release.Version)
            { UpdateInstallStatus("updates.restart_changed"); return; }
            if (!preferencesPanel.SavePending()) { UpdateInstallStatus("updates.restart_settings"); return; }
            // A due schedule still has priority; it cannot silently swallow the request.
            if (HasPendingScheduledRun(DateTimeOffset.UtcNow))
            { UpdateInstallStatus("updates.schedule_first"); scheduledFirst = true; return; }
            updateInstalling = true; SetBusy(busy); UpdateInstallStatus("updates.restart_preparing");
            await startInstaller(installation, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { UpdateInstallStatus("updates.restart_cancelled"); }
        catch (Exception e)
        {
            UpdateInstallStatus(e is UnauthorizedAccessException ? "updates.permission" : "updates.start_failed");
            try { DailyJson.Write(Path.Combine(root, "updates", "install-error.json"), new { error = e.ToString() }); }
            catch (Exception diagnosticError) when (diagnosticError is IOException or UnauthorizedAccessException) { /* Keep the on-screen failure and retry usable. */ }
        }
        finally
        {
            updateInstalling = false; updateRestartPending = false;
            if (!finalClose) SetBusy(busy);
            if (scheduledFirst)
            {
                WorkspaceTabs.SelectedItem = UpdatesTab;
                nextScheduleCheck = DateTime.MinValue;
                await CheckScheduleAsync();
            }
        }
    }
    private async Task StartUpdateInstallerAsync(DailyUpdateJob installation, CancellationToken cancellation)
    {
        string helperSource = Path.Combine(AppContext.BaseDirectory, "support", "Dustweave.Updater.exe");
        if (!installation.Repair && (DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(DailyUpdateHost.InstallRoot, "update-package.json"))?.Layout != DailyUpdates.CurrentLayout || !File.Exists(helperSource))) { UpdateInstallStatus("updates.packaged_only"); return; }
        {
            using var storage = DailyUpdateHost.AcquireStorage(root);
            // A manual download can expire while this window stays open for days.
            if (!File.Exists(Path.Combine(installation.Directory, "signed-feed.json")))
            {
                if (installation.Repair) throw new InvalidDataException("updates.repair_changed");
                readyUpdate = null;
                UpdateInstallStatus("updates.restart_changed");
                return;
            }
            if (DailySandbox.HasIsolatedWindows()) { UpdateInstallStatus("updates.isolated_open"); return; }
            string? pluginBlock = await Task.Run(() => DailyPlugin.HostUpdateBlockReason(root, installation.Release.Version));
            if (pluginBlock != null) { UpdateInstallStatus(pluginBlock); return; }
            if (ToolOpen && !await CloseToolWindowsAsync()) { UpdateInstallStatus("updates.tool_open"); return; }
            if (installation.Repair || installation.InventoryUpgrade)
            {
                helperSource = await Task.Run(() =>
                {
                    var manifest = DailyUpdateRepair.ReadVerified(installation.Directory, installation.Release, installation.Asset);
                    string verifiedHelper = DailyUpdateRepair.VerifiedHelper(manifest, DailyUpdateHost.InstallRoot, DailyUpdateRepair.Staging(installation.Directory));
                    DailyUpdateRepair.VerifyStaging(manifest, DailyUpdateHost.InstallRoot, DailyUpdateRepair.Staging(installation.Directory), out _);
                    return verifiedHelper;
                });
            }
            string probe = Path.Combine(Path.GetDirectoryName(installation.Target)!, ".dustweave-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            string nonce = Guid.NewGuid().ToString("N");
            string attempt = Path.Combine(installation.Directory, "attempts", nonce);
            DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), job = Path.Combine(attempt, "job.json");
            using var parent = Process.GetCurrentProcess();
            installation = installation with { Nonce = nonce, Target = Environment.ProcessPath!, ParentPid = parent.Id,
                ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks,
                OriginalSha256 = await Task.Run(() => DailyUpdateTransaction.Hash(Environment.ProcessPath!)) };
            await Task.Run(() => DailyUpdateTransaction.CopyDurable(helperSource, helper));
            cancellation.ThrowIfCancellationRequested();
            DailyJson.Write(job, installation);
            DailyUpdateInstaller.ValidateJob(installation, job);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = installation.Directory };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job);
            _ = Process.Start(start) ?? throw new IOException("updates.start_failed");
            UpdateInstallStatus("updates.restart_handoff");
            finalClose = true; timer.Stop(); Close();
        }
    }
}

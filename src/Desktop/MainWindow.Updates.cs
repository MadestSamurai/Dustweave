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
    private readonly Button check = new() { Margin = new(0, 0, 10, 6) };
    private readonly CheckBox automatic = new() { Margin = new(0, 12, 0, 12) };
    private readonly StackPanel notes = new();
    public event Action? CheckRequested;
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
        L.Bind(install, ContentControl.ContentProperty, "updates.install"); install.Style = (Style)Application.Current.FindResource("PrimaryButton"); actions.Children.Add(install); card.Children.Add(actions); card.Children.Add(status);
        stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(20), Margin = new(0, 0, 0, 16), Child = card });
        stack.Children.Add(DailyDownloadLinks.Create()); stack.Children.Add(notes);
        var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition {MaxWidth=880});layout.Children.Add(stack);Content = layout;
        check.Click += (_, _) => CheckRequested?.Invoke(); install.Click += (_, _) => InstallRequested?.Invoke();
        automatic.Click += (_, _) => AutomaticChanged?.Invoke(Automatic);
        L.Text(status, "updates.idle");
    }
    public void Status(string key, params object?[] args) => L.Text(status, key, args);
    public void Busy(bool network, bool ready, bool occupied) { check.IsEnabled = !network; install.Visibility = ready ? Visibility.Visible : Visibility.Collapsed; install.IsEnabled = ready && !occupied && !network; }
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
    private Window? updateNotice;
    private DailyUpdateJob? readyUpdate;
    private string? manualUpdateVersion, manualUpdateReason;
    private DateTime nextUpdateCheck = DateTime.UtcNow.AddHours(6);
    private void Updates_Click(object sender, RoutedEventArgs e) { if (!IsSandboxWindow) WorkspaceTabs.SelectedItem = UpdatesTab; else L.Text(ProgressText, "sandbox.host_only"); }
    private void InitializeUpdates()
    {
        UpdatesTab.Content = updatePanel;
        updatePanel.Automatic = DailyJson.TryRead<DailyUpdatePreference>(Path.Combine(root, "updates-preference.json"))?.Automatic ?? true;
        updatePanel.ShowNotes(DailyReleaseHistory.Read());
        updatePanel.AutomaticChanged += value => { if (smoke == null) DailyJson.Write(Path.Combine(root, "updates-preference.json"), new DailyUpdatePreference(value)); };
        updatePanel.CheckRequested += async () => await CheckUpdatesAsync(true);
        updatePanel.InstallRequested += async () => await InstallUpdateAsync();
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
    private async Task CheckUpdatesAsync(bool manual = false)
    {
        if (IsSandboxWindow || smoke != null || cacheChanging || updateChecking || updateInstalling || !manual && !updatePanel.Automatic) return;
        nextUpdateCheck = DateTime.UtcNow.AddHours(6);
        readyUpdate = null; manualUpdateVersion = null; updatePromptShown = false; updateChecking = true; updatePanel.Status("updates.checking"); UpdateUpdateControls();
        string? availableVersion = null;
        try
        {
            using var client = DailyUpdates.Client();
            var transport = DailyUpdateTransport.Production();
            var verified = await transport.FetchAsync(client, root, updateLifetime.Token);
            var feed = verified.Feed;
            var release = DailyUpdates.Latest(feed, DailyProductVersion.Current);
            if (release == null) { updatePanel.Status("updates.current"); return; }
            availableVersion = release.Version;
            updatePanel.ShowNotes(feed.Releases.OrderByDescending(r => DailyUpdates.VersionOf(r.Version)).Select(r => new DailyReleaseNote(r.Version, "", r.Notes)));
            var installed = DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(DailyUpdateHost.InstallRoot, "update-package.json"));
            string flavor = installed?.Flavor ?? throw new InvalidDataException("updates.packaged_only");
            manualUpdateReason = DailyUpdates.ManualUpdateReason(feed, release, installed);
            if (manualUpdateReason != null) { manualUpdateVersion = release.Version; updatePanel.Status(manualUpdateReason); return; }
            var asset = release.Assets.SingleOrDefault(a => a.Flavor == flavor) ?? throw new InvalidDataException("updates.flavor_missing");
            var progress = new Progress<int>(p => updatePanel.Status("updates.downloading", release.Version, p));
            string directory = await transport.DownloadAsync(client, verified, release.Version, flavor, root, progress, updateLifetime.Token, DailyUpdateHost.InstallRoot);
            using var process = Process.GetCurrentProcess();
            readyUpdate = new(directory, Environment.ProcessPath!, process.Id, process.StartTime.ToUniversalTime().Ticks, release, asset, DeltaFileName: DailyJson.TryRead<DailyUpdateDownload>(Path.Combine(directory, "download-choice.json"))?.DeltaFileName);
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
    private void UpdateUpdateControls() => updatePanel.Busy(updateChecking, readyUpdate != null, Unavailable || updateInstalling || scheduleChecking);
    private sealed record ManualUpdateNotice(string Version);
    private Task OfferUpdateAsync()
    {
        OfferUpdateNotice();
        return Task.CompletedTask;
    }
    private void OfferUpdateNotice()
    {
        if (IsSandboxWindow || smoke != null || finalClose || updateChecking || updatePromptShown || Unavailable || scheduleChecking || releaseDialogOpen || DailyDialogs.ModalDepth > 0 || updateInstalling) return;
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
            panel.Children.Add(DailyDownloadLinks.Create());
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0), IsDefault = true };
            L.Bind(button, ContentControl.ContentProperty, "updates.got_it"); panel.Children.Add(button);
            var dialog = new Window { Owner = this, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
            L.Bind(dialog, Window.TitleProperty, "updates.manual_title"); button.Click += (_, _) => dialog.Close();
            DailyDialogs.Prepare(dialog); return dialog;
    }
    private Window CreateUpdateReadyWindow(string version)
    {
        var panel = new StackPanel { Margin = new(24) };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        L.Text(text, "updates.confirm", version); panel.Children.Add(text);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
        L.Text(hint, "updates.nonblocking"); hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); panel.Children.Add(hint);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) };
        var later = new Button { IsDefault = true, Margin = new(0, 0, 10, 0) };
        var install = new Button { Style = (Style)FindResource("PrimaryButton") };
        L.Bind(later, ContentControl.ContentProperty, "updates.later"); L.Bind(install, ContentControl.ContentProperty, "updates.install");
        actions.Children.Add(later); actions.Children.Add(install); panel.Children.Add(actions);
        var dialog = new Window { Owner = this, Width = 560, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 80), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        L.Bind(dialog, Window.TitleProperty, "updates.title");
        later.Click += (_, _) => dialog.Close();
        install.Click += async (_, _) =>
        {
            dialog.Close();
            // A background check may have replaced the version named in this notice.
            if (readyUpdate?.Release.Version == version) await InstallUpdateAsync();
        };
        DailyDialogs.Prepare(dialog); return dialog;
    }
    private async Task InstallUpdateAsync()
    {
        if (IsSandboxWindow || smoke != null || readyUpdate == null || Unavailable || scheduleChecking || updateInstalling || !preferencesPanel.SavePending()) return;
        // A click just before the next timer tick must not let installation overtake a due run.
        if (HasPendingScheduledRun(DateTimeOffset.UtcNow))
        {
            updatePanel.Status("updates.schedule_first");
            nextScheduleCheck = DateTime.MinValue;
            await CheckScheduleAsync();
            return;
        }
        string helperSource = Path.Combine(AppContext.BaseDirectory, "support", "Dustweave.Updater.exe");
        if (DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(DailyUpdateHost.InstallRoot, "update-package.json"))?.Layout != DailyUpdates.CurrentLayout || !File.Exists(helperSource)) { updatePanel.Status("updates.packaged_only"); return; }
        updateInstalling = true; UpdateUpdateControls();
        try
        {
            using var storage = DailyUpdateHost.AcquireStorage(root);
            // A manual download can expire while this window stays open for days.
            if (!File.Exists(Path.Combine(readyUpdate.Directory, "signed-feed.json")))
            {
                readyUpdate = null;
                updateInstalling = false;
                await CheckUpdatesAsync(manual: true);
                return;
            }
            if (DailySandbox.HasIsolatedWindows()) { updatePanel.Status("updates.isolated_open"); return; }
            string? pluginBlock = await Task.Run(() => DailyPlugin.HostUpdateBlockReason(root, readyUpdate.Release.Version));
            if (pluginBlock != null) { updatePanel.Status(pluginBlock); return; }
            if (ToolOpen && !await CloseToolWindowsAsync()) { updatePanel.Status("updates.tool_open"); return; }
            string probe = Path.Combine(Path.GetDirectoryName(readyUpdate.Target)!, ".dustweave-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            string nonce = Guid.NewGuid().ToString("N");
            string attempt = Path.Combine(readyUpdate.Directory, "attempts", nonce);
            DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), job = Path.Combine(attempt, "job.json");
            readyUpdate = readyUpdate with { Nonce = nonce, OriginalSha256 = await Task.Run(() => DailyUpdateTransaction.Hash(Environment.ProcessPath!)) };
            await Task.Run(() => DailyUpdateTransaction.CopyDurable(helperSource, helper));
            DailyJson.Write(job, readyUpdate);
            DailyUpdateInstaller.ValidateJob(readyUpdate, job);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = readyUpdate.Directory };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job);
            _ = Process.Start(start) ?? throw new IOException("updates.start_failed");
            finalClose = true; timer.Stop(); Close();
        }
        catch (Exception e) { updatePanel.Status(e is UnauthorizedAccessException ? "updates.permission" : "updates.start_failed"); DailyJson.Write(Path.Combine(root, "updates", "install-error.json"), new { error = e.ToString() }); }
        finally { updateInstalling = false; UpdateUpdateControls(); }
    }
}

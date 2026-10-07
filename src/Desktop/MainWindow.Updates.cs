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
        stack.Children.Add(notes); Content = stack;
        check.Click += (_, _) => CheckRequested?.Invoke(); install.Click += (_, _) => InstallRequested?.Invoke();
        automatic.Click += (_, _) => AutomaticChanged?.Invoke(Automatic);
        L.Text(status, "updates.idle");
    }
    public void Status(string key, params object?[] args) => L.Text(status, key, args);
    public void Busy(bool network, bool ready, bool occupied) { check.IsEnabled = !network; install.Visibility = ready ? Visibility.Visible : Visibility.Collapsed; install.IsEnabled = ready && !occupied && !network; }
    public void ShowNotes(IEnumerable<DailyReleaseNote> releases)
    {
        notes.Children.Clear();
        foreach (var release in releases)
        {
            var body = new StackPanel(); body.Children.Add(new TextBlock { Text = release.Version + "  ·  " + release.Date, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
            foreach (int i in Enumerable.Range(0, release.Notes["zh-CN"].Length))
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10), FontSize = 14 };
                L.Bind(text, TextBlock.TextProperty, () => "• " + release.Notes[L.Code][i]); body.Children.Add(text);
            }
            notes.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(20), Margin = new(0, 0, 0, 16), Child = body });
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
    private DailyUpdateJob? readyUpdate;
    private DateTime nextUpdateCheck = DateTime.UtcNow.AddHours(6);
    private void Updates_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedItem = UpdatesTab;
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
        // Scheduled starts show notes when the run has finished, never in front of the queue.
        releaseDialogOpen = true;
        try
        {
            DailyDialogs.ShowModal(CreateReleaseNotesWindow());
        }
        finally { releaseDialogOpen = false; }
    }
    private Window CreateReleaseNotesWindow()
    {
            var panel = new StackPanel { Margin = new(24) };
            var title = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }; L.Text(title, "updates.whats_new", DailyProductVersion.Current); panel.Children.Add(title);
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
        if (smoke != null || updateChecking || updateInstalling || !manual && !updatePanel.Automatic) return;
        nextUpdateCheck = DateTime.UtcNow.AddHours(6);
        readyUpdate = null; updateChecking = true; updatePanel.Status("updates.checking"); UpdateUpdateControls();
        try
        {
            using var client = DailyUpdates.Client();
            var transport = DailyUpdateTransport.Production();
            var verified = await transport.FetchAsync(client, root, updateLifetime.Token);
            var feed = verified.Feed;
            var release = DailyUpdates.Latest(feed, DailyProductVersion.Current);
            if (release == null) { updatePanel.Status("updates.current"); return; }
            updatePanel.ShowNotes(feed.Releases.OrderByDescending(r => DailyUpdates.VersionOf(r.Version)).Select(r => new DailyReleaseNote(r.Version, "", r.Notes)));
            string flavor = DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(AppContext.BaseDirectory, "update-package.json"))?.Flavor ?? throw new InvalidDataException("updates.packaged_only");
            var asset = release.Assets.SingleOrDefault(a => a.Flavor == flavor) ?? throw new InvalidDataException("updates.flavor_missing");
            var progress = new Progress<int>(p => updatePanel.Status("updates.downloading", release.Version, p));
            string directory = await transport.DownloadAsync(client, verified, release.Version, flavor, root, progress, updateLifetime.Token, AppContext.BaseDirectory);
            using var process = Process.GetCurrentProcess();
            readyUpdate = new(directory, Environment.ProcessPath!, process.Id, process.StartTime.ToUniversalTime().Ticks, release, asset, DeltaFileName: DailyJson.TryRead<DailyUpdateDownload>(Path.Combine(directory, "download-choice.json"))?.DeltaFileName);
            updatePanel.Status("updates.ready", release.Version); updatePromptShown = false;
        }
        catch (OperationCanceledException) { if (!updateLifetime.IsCancellationRequested) updatePanel.Status("updates.unavailable"); }
        catch (Exception e)
        {
            updatePanel.Status(e.Message.StartsWith("updates.") ? e.Message : "updates.unavailable");
            DailyJson.Write(Path.Combine(root, "updates", "check-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = e.ToString() });
        }
        finally { updateChecking = false; UpdateUpdateControls(); }
    }
    private void UpdateUpdateControls() => updatePanel.Busy(updateChecking, readyUpdate != null, Unavailable || updateInstalling || scheduleChecking);
    private async Task OfferUpdateAsync()
    {
        if (smoke != null || readyUpdate == null || updateChecking || updatePromptShown || Unavailable || scheduleChecking || releaseDialogOpen || DailyDialogs.ModalDepth > 0 || updateInstalling) return;
        updatePromptShown = true;
        if (Confirm(L.Get("updates.confirm", readyUpdate.Release.Version))) await InstallUpdateAsync();
    }
    private async Task InstallUpdateAsync()
    {
        if (smoke != null || readyUpdate == null || Unavailable || scheduleChecking || updateInstalling || !preferencesPanel.SavePending()) return;
        // Development outputs are not standalone; never copy a loose apphost as an updater.
#pragma warning disable IL3000 // Empty Location is the intentional single-file detection.
        if (!string.IsNullOrEmpty(typeof(App).Assembly.Location)) { updatePanel.Status("updates.packaged_only"); return; }
#pragma warning restore IL3000
        updateInstalling = true; UpdateUpdateControls();
        try
        {
            if (ToolOpen && !await toolSession.CloseAsync()) { updatePanel.Status("updates.tool_open"); return; }
            string probe = Path.Combine(Path.GetDirectoryName(readyUpdate.Target)!, ".dustweave-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            string nonce = Guid.NewGuid().ToString("N");
            string attempt = Path.Combine(readyUpdate.Directory, "attempts", nonce);
            DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), job = Path.Combine(attempt, "job.json");
            readyUpdate = readyUpdate with { Nonce = nonce, OriginalSha256 = await Task.Run(() => DailyUpdateTransaction.Hash(Environment.ProcessPath!)) };
            await Task.Run(() => DailyUpdateTransaction.CopyDurable(Environment.ProcessPath!, helper));
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

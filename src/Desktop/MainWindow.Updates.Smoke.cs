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
            feed = Feed(DailyProductVersion.Current); downloadError = null;
            await CheckUpdatesCoreAsync(false, Fetch, Prepare);
            Check(availableUpdateVersion == null && UpdateAvailableBadge.Visibility == Visibility.Collapsed && readyUpdate == null && manualUpdateVersion == null,
                "A verified current version retained an update badge");
            Check(!DownloadButton().IsVisible && !InstallButton().IsVisible && downloads == 4, "Up-to-date state still advertises a payload");
            L.Select("zh-CN"); theme.Select(DailyAppearance.Light);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("updates-current");
            await CheckUpdateSchedulePriorityForSmoke(Check, Find<Button>);
            Check(fixture.Calls.Count == calls && DailyDialogs.ModalDepth == 0, "Discovery touched the game or opened a modal");
            DailyJson.Write(Path.Combine(smoke!, "update-discovery.json"), new { status = "passed", checks, fetches, downloads, realGameTouched = false });
        }
        finally
        {
            readyUpdate = null; manualUpdateVersion = null; manualUpdateReason = null; availableUpdateVersion = null;
            CloseUpdateNotice();
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckLaunchPathsForSmoke()
    {
        if (smoke == null) throw new InvalidOperationException("Isolated smoke only.");
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        var initialLanguage = L.Code; var initialTheme = theme.Preference;
        double oldWidth = Width, oldHeight = Height;
        var fixture = (DemoEnvironment)host; int calls = fixture.Calls.Count;
        try
        {
            Width = 920; Height = 650;
            foreach (var language in DailyLanguage.Codes)
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                L.Select(language); theme.Select(appearance);
                WorkspaceTabs.SelectedItem = RunTab;
                startupPathRefreshed = false;
                PresentLaunchPathResult(new("unchanged"));
                Check(LaunchPathNotice.Visibility == Visibility.Collapsed, "Same-path OTA displayed a relocation reminder");
                PresentLaunchPathResult(new("skipped"));
                Check(LaunchPathNotice.Visibility == Visibility.Collapsed, "First launch/disabled plan displayed a reminder");
                PresentLaunchPathResult(new("repaired", @"C:\Old & App\Dustweave.exe", @"D:\新目录\Dustweave.exe"));
                Check(LaunchPathNoticeText.Text == L.Get("launch_paths.schedule_updated") && LaunchPathRetry.Visibility == Visibility.Collapsed, "Relocation success text or actions are incorrect");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("launch-paths-success-" + language + "-" + appearance, this);
                LaunchPathReview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(WorkspaceTabs.SelectedItem == ScheduleTab, "Review does not open the schedule page");
                DismissLaunchPathNotice_Click(this, new RoutedEventArgs());
                Check(LaunchPathNotice.Visibility == Visibility.Collapsed, "Dismiss did not hide the non-modal notice");
                WorkspaceTabs.SelectedItem = RunTab;
                PresentLaunchPathResult(new("failed"));
                Check(LaunchPathRetry.Visibility == Visibility.Visible && LaunchPathReview.Visibility == Visibility.Visible && ConnectButton.IsEnabled,
                    "Repair failure blocks unrelated controls or lacks recovery actions");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("launch-paths-failure-" + language + "-" + appearance, this);
                PresentLaunchPathResult(new("unchanged"));
                Check(LaunchPathNotice.Visibility == Visibility.Collapsed, "Successful retry did not clear a stale error");
                startupPathRefreshed = true;
                PresentLaunchPathResult(new("repaired"));
                Check(LaunchPathNoticeText.Text == L.Get("launch_paths.both_updated"), "Combined startup/schedule reminder missing");
                PresentLaunchPathResult(new("skipped"));
                Check(LaunchPathNoticeText.Text == L.Get("launch_paths.startup_updated") && LaunchPathReview.Visibility == Visibility.Collapsed, "Startup-only repair incorrectly requests schedule setup");
                DismissLaunchPathNotice_Click(this, new RoutedEventArgs());
                Check(!startupPathRefreshed, "Dismissed startup reminder persists in session");
                L.Audit();
            }
            await RefreshLaunchPathsAsync(); // smoke guard must prevent actual Windows access
            Check(fixture.Calls.Count == calls && !launchPathChecking && !File.Exists(Path.Combine(root, "schedule-path-repair.json")), "Isolated test touched game or Windows scheduling");
            DailyJson.Write(Path.Combine(smoke, "launch-paths.json"), new { status = "passed", checks, realGameTouched = false, windowsTaskChanged = false });
        }
        finally { DismissLaunchPathNotice_Click(this, new RoutedEventArgs()); L.Select(initialLanguage); theme.Select(initialTheme); Width = oldWidth; Height = oldHeight; }
    }
}

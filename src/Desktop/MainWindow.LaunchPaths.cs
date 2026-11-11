using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private readonly SemaphoreSlim scheduleRegistrationGate = new(1, 1);
    private bool startupPathRefreshed, launchPathChecking;

    private async Task RefreshLaunchPathsAsync()
    {
        if (IsSandboxWindow || smoke != null || CaptureSessionId.Length > 0 || finalClose || UpdateRestarting || launchPathChecking) return;
        if (!await scheduleRegistrationGate.WaitAsync(0)) return;
        launchPathChecking = true;
        LaunchPathRetry.IsEnabled = false;
        DailySchedulePathResult result;
        try
        {
            result = await Task.Run(() => DailyWindowsSchedule.RepairPath(scheduleStore.Read(), Environment.ProcessPath!, () => finalClose || UpdateRestarting));
        }
        catch (Exception error) { result = new("failed", Current: Environment.ProcessPath ?? "", Error: error.ToString()); }
        finally
        {
            launchPathChecking = false; scheduleRegistrationGate.Release();
            LaunchPathRetry.IsEnabled = true;
        }
        // One bounded diagnostic, no user/account contents or recurring task writes.
        if (result.State is not ("unchanged" or "skipped"))
        {
            try { DailyJson.Write(Path.Combine(root, "schedule-path-repair.json"), new { atUtc = DateTimeOffset.UtcNow, result }); }
            catch (Exception) { /* Optional diagnostics must not change startup or repair success. */ }
        }
        if (!finalClose && !UpdateRestarting) PresentLaunchPathResult(result);
    }

    private void PresentLaunchPathResult(DailySchedulePathResult result)
    {
        string? key = result.NeedsAttention ? "launch_paths." + result.State : result.State == "repaired"
            ? startupPathRefreshed ? "launch_paths.both_updated" : "launch_paths.schedule_updated"
            : startupPathRefreshed ? "launch_paths.startup_updated" : null;
        LaunchPathNotice.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
        if (key == null) return;
        L.Text(LaunchPathNoticeText, key);
        LaunchPathNoticeText.SetResourceReference(TextBlock.ForegroundProperty, result.NeedsAttention ? "Warning" : "Ink");
        LaunchPathNotice.SetResourceReference(Border.BackgroundProperty, result.NeedsAttention ? "WarningSoft" : "PrimarySoft");
        LaunchPathNoticeText.ToolTip = string.IsNullOrEmpty(result.Previous) ? result.Current : result.Previous + "\n→ " + result.Current;
        LaunchPathRetry.Visibility = result.State == "failed" ? Visibility.Visible : Visibility.Collapsed;
        LaunchPathReview.Visibility = result.NeedsAttention || result.State == "repaired" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RetryLaunchPaths_Click(object sender, RoutedEventArgs e) => await RefreshLaunchPathsAsync();
    private void ReviewLaunchPaths_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceTabs.SelectedItem = ScheduleTab;
        schedulePanel.ScrollToTop();
    }
    private void DismissLaunchPathNotice_Click(object sender, RoutedEventArgs e)
    {
        LaunchPathNotice.Visibility = Visibility.Collapsed;
        startupPathRefreshed = false;
    }
}

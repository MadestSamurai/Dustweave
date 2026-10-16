using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DateTime nextStorageCheck = DateTime.UtcNow.AddSeconds(10);
    private bool storageCheckRunning;
    private bool storageWindowClosed;
    private string? storageReminderDay;
    private DateTime storageReminderAfter;
    private readonly CancellationTokenSource storageWarningLifetime = new();

    private void InitializeStorageWarning() => Closed += (_, _) => { storageWindowClosed = true; storageWarningLifetime.Cancel(); };

    private async Task CheckStorageWarningAsync(Func<DailyDiskSpace[]>? syntheticProbe = null)
    {
        // Child windows share the host's data and must never display duplicate reminders.
        if (storageWindowClosed || storageCheckRunning || IsSandboxWindow || DateTime.UtcNow < nextStorageCheck) return;
        nextStorageCheck = DateTime.UtcNow.AddMinutes(5);
        storageCheckRunning = true;
        try
        {
            DailyDiskSpace[] ReadSpace() => smoke != null && syntheticProbe != null ? syntheticProbe() : DailyStorageWarning.LowVolumes([root, DailyCacheStorage.Location(root)]);
            string day = DailyStorageWarning.Day(DateTime.Now);
            if (DailyStorageWarning.IsIgnored(root, DateTime.Now)) return;
            if (storageReminderDay == day && DateTime.UtcNow < storageReminderAfter) return;
            var volumes = await Task.Run(ReadSpace, storageWarningLifetime.Token);
            if (volumes.Length == 0) { storageReminderDay = null; return; }
            // Do not cover an existing dialog. The advisory itself is modeless so running/scheduled work continues.
            bool CanShow() => !storageWindowClosed && IsVisible && WindowState != WindowState.Minimized && !onboardingOpen && !cacheChanging && !updateInstalling && DailyDialogs.ModalDepth == 0;
            if (!CanShow()) { nextStorageCheck = DateTime.UtcNow.AddSeconds(30); return; }
            var report = await new DailyStorageMaintenance(root).ScanAsync(logPolicy, storageWarningLifetime.Token);
            if (!CanShow() || DailyStorageWarning.IsIgnored(root, DateTime.Now)) return;
            // Storage may have been freed or moved while the asynchronous scan was in progress.
            volumes = await Task.Run(ReadSpace, storageWarningLifetime.Token);
            if (volumes.Length == 0 || !CanShow()) return;
            storageReminderDay = DailyStorageWarning.Day(DateTime.Now);
            storageReminderAfter = DateTime.UtcNow.AddHours(1);
            var dialog = CreateStorageWarningWindow(volumes, report, out var action);
            var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => dismissed.TrySetResult();
            dialog.Show();
            await dismissed.Task;
            if (!storageWindowClosed && action() is { } requested)
                DailyDialogs.ShowModal(CreateLogCleanupWindow(requested == "move"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.SecurityException)
        { /* This advisory must not change the connection or task state. Retry at the next check. */ }
        finally { storageCheckRunning = false; }
    }

    private Window CreateStorageWarningWindow(DailyDiskSpace[] volumes, DailyStorageOverview report, out Func<string?> action)
    {
        string? requested = null; action = () => requested;
        var dialog = new Window { Name = "StorageWarningWindow", Owner = this, Width = 600, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 60), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        L.Bind(dialog, Window.TitleProperty, "storage.low.title");
        var body = new StackPanel { Margin = new(24, 8, 24, 24) };
        TextBlock Text(string key, double top, params object[] args) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, top, 0, 0) }; L.Text(text, key, args); body.Children.Add(text); return text; }
        Text("storage.low.body", 0);
        foreach (var disk in volumes) Text("storage.low.volume", 12, disk.Volume, Math.Round(disk.AvailableBytes / 1048576d, 1)).FontWeight = FontWeights.SemiBold;
        Text("storage.low.total", 18, Math.Round(report.TotalBytes / 1048576d, 1)).FontSize = 18;
        Text("storage.low.scope", 6).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        if (report.Skipped > 0) Text("storage.low.partial", 6, report.Skipped).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        Text("storage.low.advice", 18);
        var ignore = new CheckBox { Name = "StorageWarningIgnore", Margin = new(0, 22, 0, 0) };
        L.Bind(ignore, ContentControl.ContentProperty, "storage.low.ignore", DailyStorageWarning.DayStartsAtHour); body.Children.Add(ignore);
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), Visibility = Visibility.Collapsed }; errorText.SetResourceReference(TextBlock.ForegroundProperty, "Error"); body.Children.Add(errorText);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 22, 0, 0) };
        void Button(string name, string key, string? value, bool primary = false)
        {
            var button = new Button { Name = name, Margin = new(8, 4, 0, 0) }; L.Bind(button, ContentControl.ContentProperty, key);
            if (primary) button.Style = (Style)FindResource("PrimaryButton");
            button.Click += (_, _) => { requested = value; dialog.Close(); }; actions.Children.Add(button);
        }
        Button("StorageWarningLater", "storage.low.later", null);
        Button("StorageWarningMove", "storage.move", "move");
        Button("StorageWarningClean", "storage.low.clean", "clean", true);
        body.Children.Add(actions);
        dialog.Closing += (_, e) =>
        {
            if (ignore.IsChecked != true) return;
            try { DailyStorageWarning.IgnoreToday(root, DateTime.Now); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Full disks can also prevent saving the dismissal. Keep cleanup/migration accessible.
                ignore.IsChecked = false; errorText.Visibility = Visibility.Visible; L.Text(errorText, "storage.low.save_failed");
                requested = null; e.Cancel = true;
            }
        };
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DailyDialogs.Prepare(dialog); return dialog;
    }
}

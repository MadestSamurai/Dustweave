using System.IO;
using System.Windows;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    internal bool ScheduledStartup { get; init; }
    internal bool UpdatedStartup { get; init; }
    internal bool DirectedStartup { get; init; }
    internal string UpdateNonce { get; init; } = "";
    private bool updateStartupAcknowledged, updateStartupFailed;
    private bool AcknowledgeUpdateStartup()
    {
        if (!UpdatedStartup || updateStartupAcknowledged) return !updateStartupFailed;
        try
        {
            StartupDiagnostics.Mark("update-acknowledgement");
            DailyUpdateInstaller.Acknowledge(UpdateNonce);
            updateStartupAcknowledged = true;
            StartupDiagnostics.Mark("update-acknowledged");
            return true;
        }
        catch (Exception error)
        {
            updateStartupFailed = true;
            StartupDiagnostics.Fail(error);
            // Do not leave a seemingly healthy window waiting for a two-minute
            // timeout or permit it to run tasks before the transaction commits.
            finalClose = true; Application.Current.Shutdown(1);
            return false;
        }
    }
    private async Task InitializeStartupFeaturesAsync()
    {
        if (scheduleStore.Last is { State: "running" } previous)
            scheduleStore.Record(previous with { State = "interrupted", AtUtc = DateTimeOffset.UtcNow });
        if (ShowUpdateFailure()) { _ = CheckUpdatesAsync(); return; }
        if (ScheduledStartup) await CheckScheduleAsync();
        bool guided = ShowInitialConnectionGuide();
        if (!guided) ShowReleaseNotes();
        _ = CheckUpdatesAsync();
        await RunOnOpenAsync(guided);
    }
    private bool ShowUpdateFailure()
    {
        var result = DailyJson.TryRead<DailyUpdateEngine.Result>(Path.Combine(root, "update-result.json"));
        if (result is not { State: "failed" or "rolled_back" or "recovery_required" } || UpdatedStartup) return false;
        updatePanel.Status("updates.previous_failed");
        if (DailyJson.TryRead<UpdateFailureSeen>(Path.Combine(root, "update-failure-seen.json"))?.AtUtc == result.AtUtc) return false;
        // A rollback restart should not silently execute daily tasks, display
        // unrelated first-run notes or immediately ask for the same restart.
        updatePromptShown = true;
        var panel = new System.Windows.Controls.StackPanel { Margin = new(24) };
        var text = new System.Windows.Controls.TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20) };
        L.Text(text, "updates.rollback_body", result.Version, DailyProductVersion.Current);
        panel.Children.Add(text);
        panel.Children.Add(DailyDownloadLinks.Create());
        var button = new System.Windows.Controls.Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        L.Bind(button, System.Windows.Controls.ContentControl.ContentProperty, "updates.rollback_review"); panel.Children.Add(button);
        var dialog = new Window { Owner = this, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        L.Bind(dialog, Window.TitleProperty, "updates.rollback_title");
        button.Click += (_, _) => { dialog.Close(); WorkspaceTabs.SelectedItem = UpdatesTab; };
        dialog.Closed += (_, _) => DailyJson.Write(Path.Combine(root, "update-failure-seen.json"), new UpdateFailureSeen(result.AtUtc));
        ShowUpdateNotice(dialog);
        return true;
    }
    private sealed record UpdateFailureSeen(DateTimeOffset AtUtc);
}

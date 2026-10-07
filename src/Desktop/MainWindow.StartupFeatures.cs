using System.IO;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    internal bool ScheduledStartup { get; init; }
    internal bool UpdatedStartup { get; init; }
    internal string UpdateNonce { get; init; } = "";
    private async Task InitializeStartupFeaturesAsync()
    {
        if (UpdatedStartup) DailyUpdateInstaller.Acknowledge(UpdateNonce);
        if (scheduleStore.Last is { State: "running" } previous)
            scheduleStore.Record(previous with { State = "interrupted", AtUtc = DateTimeOffset.UtcNow });
        if (DailyJson.TryRead<DailyUpdateInstaller.Result>(Path.Combine(root, "update-result.json")) is { State: "failed" or "rolled_back" or "recovery_required" })
            updatePanel.Status("updates.previous_failed");
        if (ScheduledStartup) await CheckScheduleAsync();
        ShowReleaseNotes();
        _ = CheckUpdatesAsync();
    }
}

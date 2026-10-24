using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public sealed class DailyRepairPanel : StackPanel
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    private readonly Button action = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 10, 0) };
    private readonly Button cancel = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel files = new();
    private readonly Expander details;
    public event Action? Requested;
    public event Action? CancelRequested;
    public DailyRepairPanel()
    {
        Margin = new(0, 24, 0, 16);
        var heading = new DockPanel { LastChildFill = true };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(action); actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        L.Text(title, DailyUpdateHost.SelfUpdating ? "repair.manual_title" : "repair.title"); heading.Children.Add(title); Children.Add(heading);
        var description = new TextBlock { Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        description.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Text(description, DailyUpdateHost.SelfUpdating ? "repair.manual_help" : "repair.help"); Children.Add(description);
        Children.Add(status);
        details = new Expander { Style = (Style)Application.Current.FindResource("DisclosureExpander"),
            Content = new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = files },
            Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 12) };
        L.Bind(details, HeaderedContentControl.HeaderProperty, "repair.details"); Children.Add(details);
        L.Bind(cancel, ContentControl.ContentProperty, "repair.cancel");
        action.Click += (_, _) => Requested?.Invoke();
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        State("repair.idle", "repair.check", false);
        if (DailyUpdateHost.SelfUpdating)
        {
            actions.Visibility = Visibility.Collapsed; status.Visibility = Visibility.Collapsed;
            var links = DailyDownloadLinks.Create(); links.Margin = new(0, 12, 0, 0); Children.Add(links);
        }
    }
    public void State(string message, string button, bool busy, params object?[] args)
    {
        L.Text(status, message, args); L.Bind(action, ContentControl.ContentProperty, button);
        action.IsEnabled = !busy;
        cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        action.Style = (Style)Application.Current.FindResource(button == "repair.restart" ? "PrimaryButton" : typeof(Button));
    }
    public void Progress(DailyRepairProgress value) => L.Text(status,
        value.Stage == "checking" ? "repair.checking" : "repair.preparing", value.Completed, value.Total);
    public void Problems(DailyRepairProblem[] problems)
    {
        files.Children.Clear();
        foreach (var problem in problems)
        {
            var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 12, 4) };
            L.Text(row, problem.Missing ? "repair.file_missing" : "repair.file_changed", problem.Name);
            files.Children.Add(row);
        }
        details.Visibility = problems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

public partial class MainWindow
{
    private DailyRepairPlan? repairPlan;
    private DailyUpdateJob? readyRepair;
    private CancellationTokenSource? repairCancellation;
    private void InitializeRepair()
    {
        updatePanel.Repair.Requested += async () => await RepairActionAsync();
        updatePanel.Repair.CancelRequested += () => repairCancellation?.Cancel();
        Closed += (_, _) => repairCancellation?.Cancel();
    }
    internal void OpenRepairPage() => WorkspaceTabs.SelectedItem = UpdatesTab;
    private async Task RepairActionAsync()
    {
        if (DailyUpdateHost.SelfUpdating || IsSandboxWindow || smoke != null || updateChecking || UpdateRestarting || cacheChanging || repairCancellation != null) return;
        if (readyRepair != null)
        {
            if (Unavailable || scheduleChecking || HasPendingScheduledRun(DateTimeOffset.UtcNow))
            { updatePanel.Repair.State("repair.idle_required", "repair.restart", false); return; }
            await InstallUpdateAsync(readyRepair);
            return;
        }
        // Determine the edition from the loaded runtime, rather than trusting the
        // descriptor that the user may have overwritten with another package.
        string installRoot = DailyUpdateHost.InstallRoot;
        if (!File.Exists(Path.Combine(installRoot, "Dustweave.exe")) || !Directory.Exists(Path.Combine(installRoot, "app")))
        { updatePanel.Repair.State("updates.packaged_only", "repair.check", false); return; }
        // Bundled runtime assemblies have no Location. The edition compiled
        // into this host cannot be changed by overlaying package metadata.
        string? packagedFlavor = typeof(MainWindow).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .SingleOrDefault(a => a.Key == "Dustweave.PackageFlavor")?.Value;
        string flavor = packagedFlavor is "Portable" or "Lite" ? packagedFlavor :
            typeof(object).Assembly.Location.Length == 0 || Path.GetFullPath(typeof(object).Assembly.Location)
                .StartsWith(Path.Combine(installRoot, "app") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "Portable" : "Lite";
        repairCancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        updateChecking = true; UpdateUpdateControls();
        updatePanel.Repair.State(repairPlan == null ? "repair.checking" : "repair.preparing", "repair.check", true, 0, 0);
        var token = repairCancellation.Token;
        try
        {
            using var client = DailyUpdates.Client();
            var repair = DailyUpdateRepair.Production();
            var progress = new Progress<DailyRepairProgress>(updatePanel.Repair.Progress);
            if (repairPlan == null)
            {
                var verified = await DailyUpdateTransport.Production().FetchAsync(client, root, token);
                repairPlan = await repair.InspectAsync(client, verified, DailyProductVersion.Current, flavor, installRoot, root, progress, token);
                updatePanel.Repair.Problems(repairPlan.Problems);
                if (repairPlan.Problems.Length == 0)
                { repairPlan = null; updatePanel.Repair.State("repair.healthy", "repair.check", false); }
                else updatePanel.Repair.State("repair.found", "repair.download", false, repairPlan.Problems.Length,
                    (repairPlan.DownloadBytes / 1048576d).ToString("0.##"));
            }
            else
            {
                await Task.Run(() => repair.PrepareAsync(client, repairPlan, root, progress, token), token);
                var release = repairPlan.Verified.Feed.Releases.Single(r => r.Version == repairPlan.Manifest.Version);
                using var process = Process.GetCurrentProcess();
                readyRepair = new(repairPlan.Directory, Environment.ProcessPath!, process.Id, process.StartTime.ToUniversalTime().Ticks,
                    release, release.Assets.Single(a => a.Flavor == flavor)) { Repair = true };
                updatePanel.Repair.State("repair.ready", "repair.restart", false);
            }
        }
        catch (OperationCanceledException) { updatePanel.Repair.State("repair.cancelled", repairPlan == null ? "repair.check" : "repair.download", false); }
        catch (Exception error)
        {
            repairPlan = null; readyRepair = null;
            string message = error.Message.StartsWith("updates.repair_") ? error.Message : error is UnauthorizedAccessException ? "updates.permission" : "repair.failed";
            updatePanel.Repair.State(message, "repair.check", false);
            DailyJson.Write(Path.Combine(root, "updates", "repair-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() });
        }
        finally { repairCancellation.Dispose(); repairCancellation = null; updateChecking = false; UpdateUpdateControls(); }
    }
}

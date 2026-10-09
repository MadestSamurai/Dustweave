using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private bool onboardingOpen;
    private PageTour? firstRunTour;
    private AdornerLayer? firstRunLayer;
    private int firstRunTourIndex;
    private string FirstRunPath => Path.Combine(root, "first-run.json");
    private bool ShowInitialConnectionGuide()
    {
        if (smoke != null || ScheduledStartup || IsSandboxWindow || Unavailable || catalog == null) return false;
        var state = DailyJson.TryRead<FirstRunRecord>(FirstRunPath);
        if (!DailyFirstRun.ShouldOffer(state, catalog.Accounts.Any(a => a.Valid))) return false;
        if (state?.State == "tour" && catalog.Accounts.Any(a => a.Valid)) BeginPageTour(state.TourIndex);
        else ShowConnectionGuide();
        return true;
    }
    private void ConnectionGuide_Click(object sender, RoutedEventArgs e) => ShowConnectionGuide();
    private void ShowConnectionGuide()
    {
        if (Unavailable || IsSandboxWindow) return;
        onboardingOpen = true; SetBusy(busy);
        try
        {
            using (DailyToolControl.Acquire(root))
            {
                DailyJson.Write(FirstRunPath, new FirstRunRecord());
                var window = CreateConnectionGuideWindow();
                DailyDialogs.ShowModal(window);
                RefreshAccounts();
                if (!window.Saved || DailyJson.TryRead<FirstRunRecord>(FirstRunPath)?.State != "tour") return;
            }
            BeginPageTour(0);
        }
        catch (Exception error) { ShowError(error); }
        finally { if (firstRunTour == null) { onboardingOpen = false; SetBusy(busy); } }
    }
    private FirstRunWindow CreateConnectionGuideWindow()
        => new(this, new DailyFirstRun(sessions, host, root), new GameInstallation(smoke == null ? null : Path.Combine(root, "game-path-preview")), root);

    private (TabItem? Tab, FrameworkElement Focus, string Title, string Description)[] TourPages =>
    [
        (RunTab, dailyPanel, "nav.daily", "onboarding.page.daily"),
        (SettingsTab, preferencesPanel, "nav.settings", "onboarding.page.settings"),
        (ToolsTab, toolPanel, "nav.tools", "onboarding.page.tools"),
        (AccountsTab, CurrentLoginCard, "nav.accounts", "onboarding.page.accounts"),
        (ScheduleTab, schedulePanel, "nav.schedule", "onboarding.page.schedule"),
        (PluginsTab, pluginPanel, "plugins.title", "onboarding.page.plugins"),
        (DiagnosticsTab, (FrameworkElement)DiagnosticsTab.Content, "nav.diagnostics", "onboarding.page.diagnostics"),
        (null, VersionButton, "nav.updates", "onboarding.page.updates")
    ];
    private void BeginPageTour(int index)
    {
        if (firstRunTour != null) return;
        onboardingOpen = true; SetBusy(busy);
        var content = (UIElement)Content;
        firstRunLayer = AdornerLayer.GetAdornerLayer(content) ?? throw new InvalidOperationException("Onboarding overlay is unavailable.");
        firstRunTour = new(content);
        firstRunTour.Move += delta =>
        {
            if (firstRunTourIndex + delta >= TourPages.Length) EndPageTour(true);
            else ShowTourPage(firstRunTourIndex + delta);
        };
        firstRunTour.Skip += () => { if (Confirm(L.Get("onboarding.skip_tour"))) EndPageTour(false); };
        firstRunLayer.Add(firstRunTour); ShowTourPage(index);
        SizeChanged += ResizeFirstRunTour;
    }
    private void ResizeFirstRunTour(object sender, SizeChangedEventArgs e) => firstRunTour?.InvalidateVisual();
    private void ShowTourPage(int index)
    {
        firstRunTourIndex = Math.Clamp(index, 0, TourPages.Length - 1);
        var page = TourPages[firstRunTourIndex];
        if (page.Tab != null) WorkspaceTabs.SelectedItem = page.Tab;
        UpdateLayout();
        firstRunTour!.Show(page.Tab ?? (FrameworkElement)VersionButton, page.Focus, page.Title, page.Description, firstRunTourIndex, TourPages.Length);
        DailyJson.Write(FirstRunPath, new FirstRunRecord(State: "tour", TourIndex: firstRunTourIndex));
    }
    private void EndPageTour(bool complete)
    {
        if (firstRunTour == null) return;
        firstRunLayer?.Remove(firstRunTour); firstRunTour = null; SizeChanged -= ResizeFirstRunTour;
        DailyJson.Write(FirstRunPath, new FirstRunRecord(State: complete ? "completed" : "skipped"));
        onboardingOpen = false; SetBusy(busy); WorkspaceTabs.SelectedItem = RunTab;
        L.Text(ProgressText, complete ? "onboarding.complete" : "onboarding.reopen");
        ConnectionGuideButton.Focus();
    }
}

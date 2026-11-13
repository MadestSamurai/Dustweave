using System.Windows;
using System.IO;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    async Task CheckBargainForSmoke()
    {
        int checks = 0;
        foreach (string locale in DailyLanguage.Codes)
        foreach (int appearance in new[] { 1, 2 })
        {
            L.Select(locale); ThemeSelector.SelectedIndex = appearance;
            var dialog = CreatePracticeWindow(new string('a', 64), allowGame:false);
            dialog.ShowActivated = false; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -12000;
            dialog.PreviewSample(); dialog.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("bargain-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            if (dialog.Title != L.Get("tool.talent-practice") + " · Dustweave" || dialog.ActualWidth != 660 || ActiveToolId != "talent-practice" || !ToolOpen)
                throw new Exception("Practice tool presentation or lifecycle failed");
            if (discovery.IsNew(DailyFeatureDiscovery.Tool("talent-practice"))) throw new Exception("Rendered practice tool did not acknowledge its new-feature marker");
            if (SandboxElements<System.Windows.Controls.Button>(preferencesPanel).Any(b => b.Content as string == L.Get("bargain.title")))
                throw new Exception("Obsolete trading settings entry remains");
            checks++;
            dialog.Width = 540; dialog.Height = 520; dialog.PreviewSample(120);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("bargain-shortage-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            dialog.CheckPreviewForSmoke(); checks++;
            dialog.PreviewFieldForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("talent-practice-field-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            checks++;
            dialog.PreviewQueueForSmoke();
            dialog.Width=660;dialog.Height=740;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("talent-practice-achievement-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);checks++;
            dialog.PreviewLevelRefreshForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("talent-practice-level-refresh-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);checks++;
            dialog.CheckRestartForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("talent-practice-restart-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);checks++;
            if (!await CloseToolWindowsAsync() || practiceWindow != null || ToolOpen) throw new Exception("Practice tool failed to close through host lifecycle");
            checks++;
        }
        DailyJson.Write(Path.Combine(smoke!, "bargain.json"), new { status = "passed", checks, realGameTouched = false });
    }
}

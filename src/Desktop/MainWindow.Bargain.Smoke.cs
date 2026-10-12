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
            var dialog = new DailyBargainWindow(root, new string('a', 64), false) { Owner = this };
            dialog.PreviewSample(); DailyDialogs.Prepare(dialog); dialog.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("bargain-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            if (dialog.Title != L.Get("bargain.title") || dialog.ActualWidth != 660) throw new Exception("Training dialog presentation failed");
            checks++;
            dialog.Width = 540; dialog.Height = 520; dialog.PreviewSample(120);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("bargain-shortage-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            dialog.CheckPreviewForSmoke(); checks++;
            dialog.Close();
        }
        DailyJson.Write(Path.Combine(smoke!, "bargain.json"), new { status = "passed", checks, realGameTouched = false });
    }
}

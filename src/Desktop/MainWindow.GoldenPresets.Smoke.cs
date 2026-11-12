using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dustweave.Golden;
namespace Dustweave.Desktop;
public partial class MainWindow
{
    async Task CheckGoldenPresetsForSmoke()
    {
        int checks = 0;
        var code = FormationCode.Encode(new Formation { Season = 50, Width = 6, Height = 5, Units = Enumerable.Range(0, 10).Select(i => new FormationUnit { Costume = 10001 + i * 100, Position = i * 2 }).ToArray(), Attack = [1, 2], Defense = [3] });
        foreach (string locale in DailyLanguage.Codes)
        foreach (int appearance in new[] { 1, 2 })
        {
            L.Select(locale); ThemeSelector.SelectedIndex = appearance;
            var dialog = new GoldenPresetWindow(smoke!, new string('a', 64), false) { Owner = this, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000 };
            DailyDialogs.Prepare(dialog, toolWindow:true); dialog.PreviewForSmoke(code); dialog.Show();
            checks += dialog.VerifyPreviewForSmoke(code);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("golden-presets-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            if (dialog.Title != L.Get("tool.golden-presets") + " · Dustweave") throw new Exception("Golden preset tool lost themed title");
            dialog.Width = 560; dialog.Height = 520;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("golden-presets-compact-" + locale + "-" + appearance, (FrameworkElement)dialog.Content);
            foreach (var button in SandboxElements<System.Windows.Controls.Button>((FrameworkElement)dialog.Content).Where(b => b.Content as string == L.Get("goldencode.import") || b.Content as string == L.Get("goldencode.read")))
            {
                var p = button.TransformToAncestor(dialog).Transform(new Point(0, 0));
                if (p.Y < 0 || p.Y + button.ActualHeight > dialog.ActualHeight || p.X < 0 || p.X + button.ActualWidth > dialog.ActualWidth) throw new Exception("Golden action clipped at minimum window size");
            }
            if (!await dialog.CloseSafelyAsync()) throw new Exception("Golden dialog cannot close"); checks += 3;
        }
        DailyJson.Write(Path.Combine(smoke!, "golden-presets.json"), new { status = "passed", checks, realGameTouched = false });
    }
}

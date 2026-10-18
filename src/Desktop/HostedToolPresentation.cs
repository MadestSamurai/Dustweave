using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// Reuse the product's actual controls and palette, not a second copy of its styles.
internal static class HostedToolPresentation
{
    internal static void Install(Application app,bool deferChrome=false)
    {
        var controls = new ResourceDictionary { Source = new Uri("/Dustweave;component/SharedControls.xaml", UriKind.Relative) };
        foreach (var key in controls.Keys) app.Resources[key] = controls[key];
        var theme = new DailyTheme(DailyIdentity.DataRoot);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { var setting = DailyTheme.ReadPreference(DailyIdentity.DataRoot); if (theme.Preference != setting) theme.Select(setting); };
        timer.Start();
        AppDomain.CurrentDomain.SetData("BD2Daily.ShowModal", (Func<Window, bool?>)DailyDialogs.ShowModal);
        AppDomain.CurrentDomain.SetData("BD2Daily.ShowMessage", (Action<Window, string, string>)((owner, title, text) => DailyDialogs.ShowModal(DailyDialogs.Message(owner, title, text, false))));
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(() => {
            if (app.MainWindow is not { } window) return;
            bool showInTaskbar = window.ShowInTaskbar;
            if(!deferChrome)DailyDialogs.Prepare(window,toolWindow:true);
            window.ShowInTaskbar = showInTaskbar;
        }), DispatcherPriority.Loaded);
        app.Exit += (_, _) => { timer.Stop(); theme.Dispose(); AppDomain.CurrentDomain.SetData("BD2Daily.ShowModal", null); AppDomain.CurrentDomain.SetData("BD2Daily.ShowMessage", null); };
    }
}

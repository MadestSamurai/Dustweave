using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dustweave.Desktop;
public partial class MainWindow
{
    private async Task CheckEventPluginSettingsForSmoke()
    {
        if (smoke == null) throw new InvalidOperationException("Isolated settings required");
        var plugin = new DailyPluginInfo(true,"ready","","1.0.0",new string('a',64),[],Capabilities:["tactics"]);
        var panel = new DailyPreferencesPanel(Path.Combine(smoke,"event-settings"),false,plugin);
        var surface = new Border { Child = panel, Padding = new(20) };
        surface.SetResourceReference(Border.BackgroundProperty,"Surface");
        var window = new Window { Owner=this,Width=960,Height=780,Content=surface,
            Style=(Style)FindResource(typeof(Window)),ShowActivated=false,ShowInTaskbar=false,
            WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=0 };
        int checks=0;
        try
        {
            window.Show();
            foreach(string language in DailyLanguage.Codes)
            foreach(int theme in new[]{1,2})
            {
                L.Select(language); ThemeSelector.SelectedIndex=theme;
                panel.CheckEventPresetForSmoke();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                Capture("event-preset-"+language+"-"+theme,surface); checks++;
                panel.ShowTacticsForSmoke();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var labels=SandboxElements<Expander>(panel).SelectMany(e=>SandboxElements<TextBlock>(e)).Where(t=>t.IsVisible&&t.Text.Length>0).ToArray();
                if (!labels.Any(t=>t.Text==L.Get("plugins.tactics_settings_hint"))) throw new Exception("Weekly task instruction is missing");
                if (theme==2 && labels.Any(t=>t.Foreground is SolidColorBrush b && b.Color.R<50&&b.Color.G<50&&b.Color.B<50))
                    throw new Exception("Daily settings contain dark text in dark appearance");
                Capture("tactics-settings-"+language+"-"+theme,surface); checks++;
            }
            DailyJson.Write(Path.Combine(smoke,"event-plugin-settings.json"),new{status="passed",checks,realGameTouched=false});
        }
        finally{window.Close();}
    }
}

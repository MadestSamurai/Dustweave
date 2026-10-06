using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// One language setting for the shared product. Tool preference files are never rewritten.
internal sealed class HostedToolLocaleBridge : IDisposable
{
    private readonly Application app;
    private readonly Dictionary<Window,string> applied=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private string? lastError;
    public HostedToolLocaleBridge(Application app)
    {
        this.app=app;
        timer.Tick+=(_,_)=>TryRefresh();timer.Start();
        app.Exit+=(_,_)=>Dispose();
        TryRefresh();
    }
    private void TryRefresh()
    {
        try { Refresh(); lastError=null; }
        catch(Exception error)
        {
            // A presentation failure must never terminate automation or repeatedly open dialogs.
            string detail=error.GetBaseException().ToString();
            if(lastError!=detail)Trace.TraceError("Hosted language refresh: {0}",detail);
            lastError=detail;
        }
    }
    internal void Refresh()
    {
        var locale=DailyLanguage.Current;
        locale.RefreshFromDisk();
        AppDomain.CurrentDomain.SetData(HostedToolLocale.Key,locale.Code);
        app.Resources["SuiteRefresh"]=locale.Get("tools.refresh_inventory");
        foreach(var window in app.Windows.Cast<Window>().ToArray())
        {
            if(!window.IsLoaded)continue;
            if(applied.TryGetValue(window,out string? code)&&code==locale.Code)continue;
            var selector=(window.FindName("LanguageBox")??window.FindName("LanguageChoice")) as ComboBox;
            if(selector!=null)
            {
                // The equipment editor temporarily disables changes while replacing its inventory view.
                selector.Visibility=Visibility.Collapsed;
                HideLanguageLabel(window);
                if(!selector.IsEnabled)continue;
                if(selector.Items.Count==2)selector.Items.Add(new ComboBoxItem{Content="繁體中文",Tag="zh-TW"});
                selector.SelectedIndex=locale.Code=="en-US"?1:locale.Code=="zh-TW"?2:0;
            }
            else window.GetType().GetMethod("ApplyHostedLanguage")?.Invoke(window,[locale.Code]);
            applied[window]=locale.Code;
        }
        foreach(var closed in applied.Keys.Where(w=>!app.Windows.Cast<Window>().Contains(w)).ToArray())applied.Remove(closed);
    }
    private static void HideLanguageLabel(DependencyObject root)
    {
        if(root is TextBlock label && label.Text is "语言 / Language" or "Language / 语言" or "語言 / Language" or "Language / 語言")label.Visibility=Visibility.Collapsed;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)HideLanguageLabel(VisualTreeHelper.GetChild(root,i));
    }
    public void Dispose(){timer.Stop();applied.Clear();}
}


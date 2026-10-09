using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dustweave.Desktop;
public partial class MainWindow
{
    private async Task CheckSupportForSmoke()
    {
        if(smoke==null||host is not DemoEnvironment demo)throw new InvalidOperationException("Isolated fixture required");
        int calls=demo.Calls.Count, checks=0;timer.Stop();
        IEnumerable<T> Nodes<T>(DependencyObject node) where T:DependencyObject {
            if(node is T typed)yield return typed;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Nodes<T>(VisualTreeHelper.GetChild(node,i)))yield return child;
        }
        try {
            if(Icon==null)throw new Exception("Application icon missing");
            WorkspaceTabs.SelectedItem=DiagnosticsTab;
            foreach(int language in new[]{0,1,2})foreach(int appearance in new[]{1,2}) {
                LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;Width=1180;Height=900;
                ShowError(new InvalidOperationException("suite.owner-exited"));
                if(!ProgressText.Text.Contains(L.Get("issue.action.owner-ended"))||!DiagnosticIssueCategory.Text.Contains(L.Get("issue.category.connection")))throw new Exception("Session issue lost its actionable category");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"support-{language}-{appearance}");
                using var shade=DailyDialogs.Dim(this);
                var dialog=CreateDiagnosticExportWindow();dialog.Show();
                try {
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var texts=Nodes<TextBlock>(dialog).Select(t=>t.Text).ToArray();
                    if(!texts.Any(t=>t.Contains(DailyDiagnosticExport.WeChat))||!texts.Any(t=>t.Contains(DailyDiagnosticExport.QQ)))throw new Exception("Developer contacts missing");
                    var start=Nodes<Button>(dialog).Single(b=>b.Name=="DiagnosticExportStart");
                    var path=Nodes<TextBox>(dialog).Single(b=>b.Name=="DiagnosticExportPath");
                    if(start.TransformToAncestor(dialog).TransformBounds(new Rect(start.RenderSize)).Bottom>dialog.ActualHeight)throw new Exception("Export action clipped");
                    Capture($"support-export-{language}-{appearance}",(FrameworkElement)dialog.Content);
                    if(language==0&&appearance==1){
                        path.Text=Path.Combine(smoke,"ui-export.zip");start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        for(int i=0;i<150&&!start.IsEnabled;i++)await Task.Delay(20);
                        if(!File.Exists(path.Text)||!start.IsEnabled)throw new Exception("UI export did not complete");
                    }
                    checks+=3;
                }finally{dialog.Close();}
                WorkspaceTabs.SelectedItem=UpdatesTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var destinations=Nodes<Button>(updatePanel).Select(b=>b.Tag as string).ToArray();
                if(!destinations.Contains(DailyDownloadLinks.Release)||!destinations.Contains(DailyDownloadLinks.Mirror))throw new Exception("Download routes missing");
                Capture($"updates-links-{language}-{appearance}");WorkspaceTabs.SelectedItem=DiagnosticsTab;checks++;
            }
            Width=920;Height=650;WorkspaceTabs.SelectedItem=DiagnosticsTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(!ExportDiagnosticButton.IsVisible)throw new Exception("Compact diagnostics lost export entry");
            Capture("support-compact");checks++;
            if(demo.Calls.Count!=calls)throw new Exception("Support workflow touched the game");
            DailyJson.Write(Path.Combine(smoke,"support-ui.json"),new{status="passed",checks,realGameTouched=false,exportUsesIsolatedData=true});
        }finally{Width=1180;Height=900;LanguageSelector.SelectedIndex=0;ThemeSelector.SelectedIndex=1;DiagnosticIssueCategory.Visibility=Visibility.Collapsed;timer.Start();}
    }
}

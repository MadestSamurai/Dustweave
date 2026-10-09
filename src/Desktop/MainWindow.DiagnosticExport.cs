using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e) => DailyDialogs.ShowModal(CreateDiagnosticExportWindow());
    private Window CreateDiagnosticExportWindow()
    {
        var dialog = new Window { Owner=this, Width=650, MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-60), SizeToContent=SizeToContent.Height,
            ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner };
        L.Bind(dialog,Window.TitleProperty,"diagnostics.export.title");
        var panel=new StackPanel { Margin=new(24,8,24,24) };
        TextBlock Text(string key, double margin=10) { var t=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,margin,0,0)};L.Text(t,key);panel.Children.Add(t);return t; }
        Text("diagnostics.export.body",0).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        Text("diagnostics.export.contact",22).FontWeight=FontWeights.SemiBold;
        var contact=new StackPanel{Margin=new(0,8,0,0)};
        var wechat=new TextBlock{FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};L.Text(wechat,"diagnostics.export.wechat",DailyDiagnosticExport.WeChat);contact.Children.Add(wechat);
        var qq=new TextBlock{FontSize=16,Margin=new(0,5,0,0)};L.Text(qq,"diagnostics.export.qq",DailyDiagnosticExport.QQ);contact.Children.Add(qq);panel.Children.Add(contact);
        var copied=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0)};copied.SetResourceReference(TextBlock.ForegroundProperty,"Primary");
        var copy=new Button{HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,0)};L.Bind(copy,ContentControl.ContentProperty,"diagnostics.export.copy");panel.Children.Add(copy);panel.Children.Add(copied);
        copy.Click+=(_,_)=>{try{Clipboard.SetText("WeChat: "+DailyDiagnosticExport.WeChat+"\nQQ: "+DailyDiagnosticExport.QQ);L.Text(copied,"diagnostics.export.contact_copied");}catch{L.Text(copied,"diagnostics.copy_failed");}};
        Text("diagnostics.export.path",18).FontWeight=FontWeights.SemiBold;
        var location=new Grid{Margin=new(0,8,0,0)};location.ColumnDefinitions.Add(new());location.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var path=new TextBox{Name="DiagnosticExportPath",IsReadOnly=true,Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Dustweave-diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"),MinWidth=200,VerticalContentAlignment=VerticalAlignment.Center,Margin=new(0,0,10,0)};
        System.Windows.Automation.AutomationProperties.SetName(path,L.Get("diagnostics.export.path"));location.Children.Add(path);
        var browse=new Button();L.Bind(browse,ContentControl.ContentProperty,"diagnostics.export.browse");Grid.SetColumn(browse,1);location.Children.Add(browse);panel.Children.Add(location);
        browse.Click+=(_,_)=>{var picker=new SaveFileDialog{Title=L.Get("diagnostics.export.path"),Filter="ZIP (*.zip)|*.zip",DefaultExt=".zip",AddExtension=true,FileName=Path.GetFileName(path.Text),InitialDirectory=Path.GetDirectoryName(path.Text),OverwritePrompt=false};if(picker.ShowDialog(dialog)==true)path.Text=picker.FileName;};
        Text("diagnostics.export.privacy",12).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var status=new TextBlock{Name="DiagnosticExportStatus",TextWrapping=TextWrapping.Wrap,Margin=new(0,14,0,0)};panel.Children.Add(status);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,18,0,0)};
        var open=new Button{Visibility=Visibility.Collapsed,Margin=new(0,0,10,0)};L.Bind(open,ContentControl.ContentProperty,"diagnostics.export.open");actions.Children.Add(open);
        var export=new Button{Name="DiagnosticExportStart",Style=(Style)FindResource("PrimaryButton"),IsDefault=true};L.Bind(export,ContentControl.ContentProperty,"diagnostics.export.start");actions.Children.Add(export);panel.Children.Add(actions);
        string? exported=null;var lifetime=new CancellationTokenSource();bool exporting=false;
        open.Click+=(_,_)=>{try{if(exported!=null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(exported)!){UseShellExecute=true});}catch(Exception e){DailyUiText.Error(status,e);}};
        dialog.Closed+=(_,_)=>{lifetime.Cancel();if(!exporting)lifetime.Dispose();};
        export.Click+=async(_,_)=>{
            exporting=true;export.IsEnabled=false;browse.IsEnabled=false;open.Visibility=Visibility.Collapsed;L.Text(status,"diagnostics.export.working");
            try {
                var sources=smoke==null ? await Task.Run(()=>DailyDiagnosticExport.RelatedSources(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DailySandbox.Store),lifetime.Token) : null;
                var result=await DailyDiagnosticExport.CreateAsync(root,path.Text,DiagnosticSummary(),DailyProductVersion.Current,lifetime.Token,sources);
                exported=result.Path;L.Text(status,"diagnostics.export.done",result.Files,result.Skipped);open.Visibility=Visibility.Visible;
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { DailyUiText.Error(status,e); }
            finally {exporting=false;if(dialog.IsLoaded){export.IsEnabled=true;browse.IsEnabled=true;}else lifetime.Dispose();}
        };
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DailyDialogs.Prepare(dialog);return dialog;
    }
}

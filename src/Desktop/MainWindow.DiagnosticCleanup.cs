using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyLogPolicy logPolicy = new();
    private CancellationTokenSource? logCleanupCancellation;
    private DateTime nextLogCleanup = DateTime.UtcNow.AddMinutes(1);
    private bool logCleanupRunning;
    private sealed record LogCleanupRun(DateTime At, int Files, long Bytes, int Skipped);
    private void InitializeLogCleanup()
    {
        logPolicy=DailyLogCleanup.Load(root);
        var last=DailyJson.TryRead<LogCleanupRun>(Path.Combine(root,"log-cleanup-last.json"));
        if(last!=null && last.At>DateTime.UtcNow.AddHours(-12) && last.At<=DateTime.UtcNow)nextLogCleanup=last.At.AddHours(12);
        Closed+=(_,_)=>logCleanupCancellation?.Cancel();
    }
    private async Task CheckLogCleanupAsync()
    {
        if(Unavailable || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0)
        { logCleanupCancellation?.Cancel();return; }
        if(logCleanupRunning || !logPolicy.Automatic || DateTime.UtcNow<nextLogCleanup)return;
        logCleanupRunning=true;nextLogCleanup=DateTime.UtcNow.AddMinutes(5);
        using var lifetime=new CancellationTokenSource();logCleanupCancellation=lifetime;
        try {
            var plan=await DailyLogCleanup.ScanAsync(root,logPolicy,lifetime.Token);
            if(Unavailable || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0)return;
            var result=await DailyLogCleanup.ApplyAsync(plan,lifetime.Token);
            DailyJson.Write(Path.Combine(root,"log-cleanup-last.json"),new LogCleanupRun(DateTime.UtcNow,result.Files,result.Bytes,result.Skipped));
            nextLogCleanup=DateTime.UtcNow.AddHours(12);
        }
        catch(OperationCanceledException) { }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { /* Never stop automation for retention. Retry on the next idle interval. */ }
        finally {logCleanupCancellation=null;logCleanupRunning=false;}
    }
    private void CleanupDiagnostics_Click(object sender,RoutedEventArgs e)
    {
        logCleanupCancellation?.Cancel();
        DailyDialogs.ShowModal(CreateLogCleanupWindow());
    }
    private Window CreateLogCleanupWindow()
    {
        var dialog=new Window{Owner=this,Width=620,MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-60),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        L.Bind(dialog,Window.TitleProperty,"logs.title");
        var panel=new StackPanel{Margin=new(24,8,24,24)};
        TextBlock Text(string key,double margin=12){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,margin,0,0)};L.Text(text,key);panel.Children.Add(text);return text;}
        Text("logs.body",0).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var automatic=new CheckBox{Name="AutomaticLogCleanup",IsChecked=logPolicy.Automatic,Margin=new(0,22,0,14)};L.Bind(automatic,ContentControl.ContentProperty,"logs.automatic");panel.Children.Add(automatic);
        var settings=new Grid();settings.ColumnDefinitions.Add(new());settings.ColumnDefinitions.Add(new());
        ComboBox Choice(string label,string name,int[] options,int value,int column){var row=new StackPanel{Margin=new(0,0,column==0?16:0,0)};var caption=new TextBlock{Margin=new(0,0,0,8)};L.Text(caption,label);row.Children.Add(caption);var choice=new ComboBox{Name=name,MinWidth=150};foreach(int n in options){var item=new ComboBoxItem{Tag=n};L.Bind(item,ContentControl.ContentProperty,label+".value",n);choice.Items.Add(item);if(n==value)choice.SelectedItem=item;}if(choice.SelectedIndex<0)choice.SelectedIndex=1;row.Children.Add(choice);Grid.SetColumn(row,column);settings.Children.Add(row);return choice;}
        var days=Choice("logs.days","LogRetentionDays",[2,7,14,30,90],logPolicy.Days,0);
        var size=Choice("logs.size","LogRetentionSize",[128,512,1024,4096],logPolicy.LimitMiB,1);panel.Children.Add(settings);
        Text("logs.protection",16).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var status=Text("logs.scanning",20);status.Name="LogCleanupStatus";status.FontSize=16;status.FontWeight=FontWeights.SemiBold;
        var detail=Text("logs.scope",10);detail.FontSize=12;detail.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,20,0,0)};
        var scan=new Button{Name="LogCleanupScan",Margin=new(0,0,10,0)};L.Bind(scan,ContentControl.ContentProperty,"logs.scan");actions.Children.Add(scan);
        var clean=new Button{Name="LogCleanupStart",Style=(Style)FindResource("PrimaryButton"),IsEnabled=false};L.Bind(clean,ContentControl.ContentProperty,"logs.clean");actions.Children.Add(clean);panel.Children.Add(actions);
        DailyLogPlan? plan=null;bool working=false,closed=false;var lifetime=new CancellationTokenSource();
        static double MiB(long bytes)=>Math.Round(bytes/1048576d,1);
        void Controls(bool enabled){scan.IsEnabled=enabled;days.IsEnabled=enabled;size.IsEnabled=enabled;automatic.IsEnabled=enabled;clean.IsEnabled=enabled&&plan?.Files>0;}
        void Save(){try{logPolicy=new(automatic.IsChecked==true,(int)((ComboBoxItem)days.SelectedItem).Tag,(int)((ComboBoxItem)size.SelectedItem).Tag);DailyLogCleanup.Save(root,logPolicy);nextLogCleanup=DateTime.UtcNow.AddMinutes(1);plan=null;clean.IsEnabled=false;L.Text(status,"logs.changed");}catch(Exception e){DailyUiText.Error(status,e);}}
        automatic.Click+=(_,_)=>Save();days.SelectionChanged+=(_,_)=>Save();size.SelectionChanged+=(_,_)=>Save();
        async Task Scan(){working=true;plan=null;Controls(false);L.Text(status,"logs.scanning");try{plan=await DailyLogCleanup.ScanAsync(root,logPolicy,lifetime.Token,new Progress<DailyLogScanProgress>(p=>{if(!closed&&working)L.Text(status,"logs.progress",p.Entries,MiB(p.Bytes),p.Examined);}));if(!closed){L.Text(status,"logs.preview",MiB(plan.ManagedBytes),MiB(plan.ReclaimableBytes),plan.Files);L.Text(detail,"logs.skipped",plan.Skipped);}}catch(OperationCanceledException){}catch(Exception e){if(!closed){if(e.Message=="logs.rescan")L.Text(status,"logs.rescan");else DailyUiText.Error(status,e);}}finally{working=false;if(!closed)Controls(true);else lifetime.Dispose();}}
        scan.Click+=async(_,_)=>await Scan();
        clean.Click+=async(_,_)=>{
            if(plan==null)return;
            if(Unavailable||dailyQueue.IsRunning){L.Text(status,"logs.busy");return;}
            working=true;Controls(false);L.Text(status,"logs.cleaning");
            try{var result=await DailyLogCleanup.ApplyAsync(plan,lifetime.Token);plan=null;if(!closed)L.Text(status,"logs.done",result.Files,MiB(result.Bytes),result.Skipped);}
            catch(OperationCanceledException){}catch(Exception e){if(!closed){if(e.Message=="logs.rescan")L.Text(status,"logs.rescan");else DailyUiText.Error(status,e);}}
            finally{working=false;if(!closed)Controls(true);else lifetime.Dispose();}
        };
        dialog.Loaded+=async(_,_)=>await Scan();dialog.Closed+=(_,_)=>{closed=true;lifetime.Cancel();if(!working)lifetime.Dispose();};
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DailyDialogs.Prepare(dialog);return dialog;
    }
}

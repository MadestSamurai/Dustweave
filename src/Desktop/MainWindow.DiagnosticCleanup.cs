using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyLogPolicy logPolicy = new();
    private CancellationTokenSource? logCleanupCancellation;
    private DateTime nextLogCleanup = DateTime.UtcNow.AddMinutes(1);
    private DateTime nextEvidenceCleanup = DateTime.UtcNow.AddSeconds(15);
    private bool logCleanupRunning;
    private bool cacheChanging;
    private void SetCacheChanging(bool value){cacheChanging=value;SetBusy(busy);}
    private sealed record LogCleanupRun(DateTime At, int Files, long Bytes, int Skipped, string? Version = null);
    private string? LegacyEquipmentLogRoot => smoke == null
        ? Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant")
        : null;
    private void InitializeLogCleanup()
    {
        logPolicy=DailyLogCleanup.Load(root);
        var last=DailyJson.TryRead<LogCleanupRun>(Path.Combine(root,"log-cleanup-last.json"));
        if(last?.Version==DailyProductVersion.Current && last.At>DateTime.UtcNow.AddHours(-12) && last.At<=DateTime.UtcNow)nextLogCleanup=last.At.AddHours(12);
        if(LegacyEquipmentLogRoot is { } legacy && DailyLogCleanup.HasLegacyEquipmentLogs(legacy))nextLogCleanup=DateTime.UtcNow.AddMinutes(1);
        Closed+=(_,_)=>logCleanupCancellation?.Cancel();
    }
    private async Task CheckLogCleanupAsync()
    {
        if(cacheChanging)return;
        if(Unavailable || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0)
        { logCleanupCancellation?.Cancel();return; }
        bool retentionDue=logPolicy.Automatic&&DateTime.UtcNow>=nextLogCleanup;
        if(logCleanupRunning || !retentionDue && (DateTime.UtcNow<nextEvidenceCleanup || !DailyLogEvidence.HasPending(root)))return;
        logCleanupRunning=true;nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(1);
        if(retentionDue)nextLogCleanup=DateTime.UtcNow.AddMinutes(5);
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(30));logCleanupCancellation=lifetime;
        try {
            await DailyLogEvidence.FlushAsync(root,lifetime.Token);
            if(!retentionDue)return;
            if(LegacyEquipmentLogRoot is { } legacy)await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root,legacy,lifetime.Token);
            if(Unavailable || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0 || updateChecking || updateInstalling)return;
            // The ownership lock protects active tools; an idle tool window is not a running task.
            SetCacheChanging(true);
            DailyLogCleanupResult result;
            try { result=await new DailyStorageMaintenance(root).CleanAsync(logPolicy,lifetime.Token); }
            finally {SetCacheChanging(false);}
            DailyJson.Write(Path.Combine(root,"log-cleanup-last.json"),new LogCleanupRun(DateTime.UtcNow,result.Files,result.Bytes,result.Skipped,DailyProductVersion.Current));
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
    private Window CreateLogCleanupWindow(bool moveWhenReady = false)
    {
        var dialog=new Window{Owner=this,Width=680,MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-60),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        L.Bind(dialog,Window.TitleProperty,"logs.title");
        var panel=new StackPanel{Margin=new(24,8,24,24)};
        TextBlock Text(string key,double margin=12){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,margin,0,0)};L.Text(text,key);panel.Children.Add(text);return text;}
        Text("logs.body",0).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var status=Text("logs.scanning",20);status.Name="LogCleanupStatus";status.FontSize=18;status.FontWeight=FontWeights.SemiBold;
        var detail=Text("logs.scope",10);detail.FontSize=12;detail.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var locationTitle=Text("storage.location",20);locationTitle.FontWeight=FontWeights.SemiBold;
        var locationRow=new DockPanel{Margin=new(0,8,0,8)};
        var move=new Button{Name="CacheStorageMove",Margin=new(10,0,0,0)};L.Bind(move,ContentControl.ContentProperty,"storage.move");DockPanel.SetDock(move,Dock.Right);locationRow.Children.Add(move);
        var location=new TextBox{IsReadOnly=true,Text=DailyCacheStorage.Location(root)};locationRow.Children.Add(location);panel.Children.Add(locationRow);
        Text("storage.move_scope",0).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var automatic=new CheckBox{Name="AutomaticLogCleanup",IsChecked=logPolicy.Automatic,Margin=new(0,22,0,14)};L.Bind(automatic,ContentControl.ContentProperty,"logs.automatic");panel.Children.Add(automatic);
        var settings=new Grid();settings.ColumnDefinitions.Add(new());settings.ColumnDefinitions.Add(new());
        ComboBox Choice(string label,string name,int[] options,int value,int column){var row=new StackPanel{Margin=new(0,0,column==0?16:0,0)};var caption=new TextBlock{Margin=new(0,0,0,8)};L.Text(caption,label);row.Children.Add(caption);var choice=new ComboBox{Name=name,MinWidth=150};foreach(int n in options){var item=new ComboBoxItem{Tag=n};L.Bind(item,ContentControl.ContentProperty,label+".value",n);choice.Items.Add(item);if(n==value)choice.SelectedItem=item;}if(choice.SelectedIndex<0)choice.SelectedIndex=1;row.Children.Add(choice);Grid.SetColumn(row,column);settings.Children.Add(row);return choice;}
        var days=Choice("logs.days","LogRetentionDays",[2,3,7],logPolicy.Days,0);
        var size=Choice("logs.size","LogRetentionSize",[128,512,1024,4096],logPolicy.LimitMiB,1);panel.Children.Add(settings);
        Text("logs.protection",16).SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,20,0,0)};
        var scan=new Button{Name="LogCleanupScan",Margin=new(0,0,10,0)};L.Bind(scan,ContentControl.ContentProperty,"logs.scan");actions.Children.Add(scan);
        var clean=new Button{Name="LogCleanupStart",Style=(Style)FindResource("PrimaryButton"),IsEnabled=false};L.Bind(clean,ContentControl.ContentProperty,"logs.clean");actions.Children.Add(clean);panel.Children.Add(actions);
        DailyStorageOverview? storage=null;bool working=false,closed=false;var lifetime=new CancellationTokenSource();
        static double MiB(long bytes)=>Math.Round(bytes/1048576d,1);
        void Controls(bool enabled){scan.IsEnabled=enabled;days.IsEnabled=enabled;size.IsEnabled=enabled;automatic.IsEnabled=enabled;move.IsEnabled=enabled&&!IsSandboxWindow;clean.IsEnabled=enabled&&storage?.ReclaimableBytes>0;}
        void Save(){try{logPolicy=new(automatic.IsChecked==true,(int)((ComboBoxItem)days.SelectedItem).Tag,(int)((ComboBoxItem)size.SelectedItem).Tag);DailyLogCleanup.Save(root,logPolicy);nextLogCleanup=DateTime.UtcNow.AddMinutes(1);storage=null;clean.IsEnabled=false;L.Text(status,"logs.changed");}catch(Exception e){DailyUiText.Error(status,e);}}
        automatic.Click+=(_,_)=>Save();days.SelectionChanged+=(_,_)=>Save();size.SelectionChanged+=(_,_)=>Save();
        void Error(Exception error){if(error.Message.StartsWith("storage.",StringComparison.Ordinal))L.Text(status,error.Message);else DailyUiText.Error(status,error);}
        async Task Scan(){working=true;storage=null;Controls(false);L.Text(status,"logs.scanning");try{storage=await new DailyStorageMaintenance(root).ScanAsync(logPolicy,lifetime.Token);if(!closed){L.Text(status,"storage.preview",MiB(storage.TotalBytes),MiB(storage.ReclaimableBytes));L.Text(detail,"storage.breakdown",MiB(storage.LogBytes),MiB(storage.CacheBytes),MiB(Math.Max(0,storage.TotalBytes-storage.LogBytes-storage.CacheBytes)),storage.Skipped,MiB(storage.SandboxBytes),MiB(storage.ExtractionBytes));}}catch(OperationCanceledException){}catch(Exception e){if(!closed)Error(e);}finally{working=false;if(!closed)Controls(true);else lifetime.Dispose();}}
        bool BusyStorage(bool moving=false)=>Unavailable||dailyQueue.IsRunning||updateChecking||updateInstalling||moving&&(ToolOpen||(!IsSandboxWindow&&smoke==null&&DailyUpdateHost.HasIsolatedWindows()));
        scan.Click+=async(_,_)=>await Scan();
        clean.Click+=async(_,_)=>{
            if(storage==null)return;
            if(BusyStorage()){L.Text(status,"storage.busy");return;}
            working=true;Controls(false);SetCacheChanging(true);L.Text(status,"logs.cleaning");
            try{if(LegacyEquipmentLogRoot is { } legacy)await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root,legacy,lifetime.Token);var result=await new DailyStorageMaintenance(root).CleanAsync(logPolicy,lifetime.Token);storage=null;if(!closed){L.Text(status,"logs.done",result.Files,MiB(result.Bytes),result.Skipped);L.Text(detail,"storage.rescan");}}
            catch(OperationCanceledException){}catch(Exception e){if(!closed){if(e.Message=="logs.rescan")L.Text(status,"logs.rescan");else DailyUiText.Error(status,e);}}
            finally{SetCacheChanging(false);working=false;if(!closed)Controls(true);else lifetime.Dispose();}
        };
        move.Click+=async(_,_)=>{
            if(BusyStorage(true)){L.Text(status,"storage.busy");return;}
            var picker=new Microsoft.Win32.OpenFolderDialog{Title=L.Get("storage.pick")};
            if(picker.ShowDialog(dialog)!=true)return;
            string destination=Path.Combine(picker.FolderName,"Dustweave-cache");
            if(DailyDialogs.ShowModal(DailyDialogs.Message(dialog,L.Get("storage.move"),L.Get("storage.confirm",destination),true))!=true)return;
            working=true;Controls(false);SetCacheChanging(true);L.Text(status,"storage.moving",0);
            try{using var control=DailyUpdateHost.AcquireControl(root);var result=await DailyCacheStorage.MoveAsync(root,destination,lifetime.Token,new Progress<long>(n=>{if(!closed&&working)L.Text(status,"storage.moving",MiB(n));}));if(!closed){location.Text=result.Path;L.Text(status,"storage.moved",MiB(result.Bytes),result.RetainedFiles);L.Text(detail,"storage.rescan");}storage=null;}
            catch(OperationCanceledException){}catch(Exception error){if(!closed)Error(error);}
            finally{SetCacheChanging(false);working=false;if(!closed)Controls(true);else lifetime.Dispose();}
        };
        dialog.Loaded+=async(_,_)=>{await Scan();if(moveWhenReady&&!closed&&move.IsEnabled)move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};dialog.Closed+=(_,_)=>{closed=true;lifetime.Cancel();if(!working)lifetime.Dispose();};
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DailyDialogs.Prepare(dialog);return dialog;
    }
}

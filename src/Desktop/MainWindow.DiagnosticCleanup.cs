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
    private bool compressionPending;
    private DateTime lastRetentionUtc=DateTime.MinValue;
    private bool manualCacheChanging, automaticCacheChanging;
    private bool maintenanceYielding;
    private bool MaintenanceWindowOpen => ToolOpen || OwnedWindows.Count > 0;
    private bool cacheChanging => manualCacheChanging || automaticCacheChanging;
    private Task logCleanupCompletion = Task.CompletedTask;
    private void SetCacheChanging(bool value){manualCacheChanging=value;SetBusy(busy);}
    private void SetAutomaticCacheChanging(bool value){automaticCacheChanging=value;SetBusy(busy);}
    private async Task PauseAutomaticMaintenanceAsync(CancellationToken token=default)
    {
        // Reserve a quiet interval before awaiting: no timer may start a second
        // maintenance pass between cancellation and the foreground lease.
        var retry=DateTime.UtcNow.AddMinutes(5);
        if(nextLogCleanup<retry)nextLogCleanup=retry;
        nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(1);
        token.ThrowIfCancellationRequested();
        if(!logCleanupRunning)return;
        maintenanceYielding=true;SetBusy(busy);
        L.Text(ProgressText,"logs.yielding");
        try
        {
            logCleanupCancellation?.Cancel();
            await logCleanupCompletion.WaitAsync(token);
        }
        finally {maintenanceYielding=false;SetBusy(busy);}
    }
    private async Task<bool> PrepareSettingsToolAsync()
    {
        if(Unavailable || finalClose)return false;
        await PauseAutomaticMaintenanceAsync();
        return !finalClose && !Unavailable;
    }
    private sealed record LogCleanupRun(DateTime At, int Files, long Bytes, int Skipped, string? Version = null, bool MoreWork = false, int CompressedFiles = 0, DateTime? RetentionAt = null);
    private string? LegacyEquipmentLogRoot => smoke == null
        ? Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant")
        : null;
    private void InitializeLogCleanup()
    {
        logPolicy=DailyLogCleanup.Load(root);
        var last=DailyJson.TryRead<LogCleanupRun>(Path.Combine(root,"log-cleanup-last.json"));
        compressionPending=last?.Version==DailyProductVersion.Current && last.MoreWork;
        if(last?.Version==DailyProductVersion.Current)lastRetentionUtc=last.RetentionAt??last.At;
        if(!compressionPending && lastRetentionUtc>DateTime.UtcNow.AddHours(-12) && lastRetentionUtc<=DateTime.UtcNow)nextLogCleanup=lastRetentionUtc.AddHours(12);
        if(LegacyEquipmentLogRoot is { } legacy && DailyLogCleanup.HasLegacyEquipmentLogs(legacy))nextLogCleanup=DateTime.UtcNow.AddMinutes(1);
        Closed+=(_,_)=>logCleanupCancellation?.Cancel();
    }
    private void SaveLogCleanupResult(DailyLogCleanupResult result,bool retention)
    {
        var now=DateTime.UtcNow;
        if(retention)lastRetentionUtc=now;
        compressionPending=result.MoreWork;
        DailyJson.Write(Path.Combine(root,"log-cleanup-last.json"),new LogCleanupRun(now,result.Files,result.Bytes,result.Skipped,DailyProductVersion.Current,result.MoreWork,result.CompressedFiles,lastRetentionUtc));
        var retentionDue=lastRetentionUtc.AddHours(12);
        nextLogCleanup=result.MoreWork&&now.AddMinutes(5)<retentionDue?now.AddMinutes(5):retentionDue;
    }
    private async Task CheckLogCleanupAsync()
    {
        // Maintenance yields to an accepted restart, including while it owns
        // the storage/control leases. It must not cancel itself for those leases.
        if(UpdateRestarting || finalClose){logCleanupCancellation?.Cancel();return;}
        if(Unavailable || MaintenanceWindowOpen || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0)
        { logCleanupCancellation?.Cancel();return; }
        if(logCleanupRunning)return;
        bool retentionDue=logPolicy.Automatic&&DateTime.UtcNow>=nextLogCleanup;
        if(!retentionDue && (DateTime.UtcNow<nextEvidenceCleanup || !DailyLogEvidence.HasPending(root)))return;
        nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(1);
        if(retentionDue)nextLogCleanup=DateTime.UtcNow.AddMinutes(5);
        await RunAutomaticMaintenanceAsync(async token => {
            await DailyLogEvidence.FlushAsync(root,token);
            if(!retentionDue)return;
            bool continuing=compressionPending&&lastRetentionUtc>DateTime.UtcNow.AddHours(-12)&&lastRetentionUtc<=DateTime.UtcNow;
            if(!continuing&&LegacyEquipmentLogRoot is { } legacy)await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root,legacy,token);
            if(Unavailable || MaintenanceWindowOpen || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0 || updateChecking || updateInstalling)return;
            // Open tool windows may start independently of the host. Defer
            // maintenance until they close, while retaining the real file locks.
            SetAutomaticCacheChanging(true);
            DailyLogCleanupResult result;
            try {
                var maintenance=new DailyStorageMaintenance(root);
                result=continuing?await maintenance.ContinueCompressionAsync(token):await maintenance.CleanAsync(logPolicy,token);
            }
            finally {SetAutomaticCacheChanging(false);}
            SaveLogCleanupResult(result,!continuing);
        });
    }
    private async Task RunAutomaticMaintenanceAsync(Func<CancellationToken,Task> maintain)
    {
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logCleanupRunning=true;logCleanupCompletion=completed.Task;
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(30));logCleanupCancellation=lifetime;
        try { await maintain(lifetime.Token); }
        catch(OperationCanceledException) { }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { /* Never stop automation for retention. Retry on the next idle interval. */ }
        finally {logCleanupCancellation=null;logCleanupRunning=false;completed.TrySetResult();}
    }
    private async void CleanupDiagnostics_Click(object sender,RoutedEventArgs e)
    {
        try { await PauseAutomaticMaintenanceAsync(); if(!finalClose)DailyDialogs.ShowModal(CreateLogCleanupWindow()); }
        catch(Exception error){ShowError(error);}
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
        var optimization=Text("storage.compression",8);optimization.FontSize=12;optimization.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");optimization.Visibility=Visibility.Collapsed;
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
        void Controls(bool enabled){scan.IsEnabled=enabled;days.IsEnabled=enabled;size.IsEnabled=enabled;automatic.IsEnabled=enabled;move.IsEnabled=enabled&&!IsSandboxWindow;clean.IsEnabled=enabled&&storage!=null&&(storage.ReclaimableBytes>0||storage.OptimizableFiles>0);}
        void Save(){try{logPolicy=new(automatic.IsChecked==true,(int)((ComboBoxItem)days.SelectedItem).Tag,(int)((ComboBoxItem)size.SelectedItem).Tag);DailyLogCleanup.Save(root,logPolicy);compressionPending=false;nextLogCleanup=DateTime.UtcNow.AddMinutes(1);storage=null;clean.IsEnabled=false;optimization.Visibility=Visibility.Collapsed;L.Text(status,"logs.changed");}catch(Exception e){DailyUiText.Error(status,e);}}
        automatic.Click+=(_,_)=>Save();days.SelectionChanged+=(_,_)=>Save();size.SelectionChanged+=(_,_)=>Save();
        void Error(Exception error){if(error.Message.StartsWith("storage.",StringComparison.Ordinal))L.Text(status,error.Message);else DailyUiText.Error(status,error);}
        async Task Scan(){working=true;storage=null;Controls(false);L.Text(status,"logs.scanning");optimization.Visibility=Visibility.Collapsed;try{storage=await new DailyStorageMaintenance(root).ScanAsync(logPolicy,lifetime.Token);if(!closed){L.Text(status,"storage.preview",MiB(storage.TotalBytes),MiB(storage.ReclaimableBytes));L.Text(detail,"storage.breakdown",MiB(storage.LogBytes),MiB(storage.CacheBytes),MiB(Math.Max(0,storage.TotalBytes-storage.LogBytes-storage.CacheBytes)),storage.Skipped,MiB(storage.SandboxBytes),MiB(storage.ExtractionBytes));if(storage.OptimizableFiles>0){L.Text(optimization,"storage.compression",storage.OptimizableFiles);optimization.Visibility=Visibility.Visible;}}}catch(OperationCanceledException){}catch(Exception e){if(!closed)Error(e);}finally{working=false;if(!closed)Controls(true);else lifetime.Dispose();}}
        bool BusyStorage(bool moving=false)=>Unavailable||dailyQueue.IsRunning||updateChecking||updateInstalling||moving&&(ToolOpen||(!IsSandboxWindow&&smoke==null&&DailyUpdateHost.HasIsolatedWindows()));
        scan.Click+=async(_,_)=>await Scan();
        clean.Click+=async(_,_)=>{
            if(storage==null)return;
            if(BusyStorage()){L.Text(status,"storage.busy");return;}
            working=true;Controls(false);SetCacheChanging(true);L.Text(status,"logs.cleaning");
            try{if(LegacyEquipmentLogRoot is { } legacy)await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root,legacy,lifetime.Token);var result=await new DailyStorageMaintenance(root).CleanAsync(logPolicy,lifetime.Token);storage=null;SaveLogCleanupResult(result,true);if(!closed){L.Text(status,"storage.optimized",result.Files,result.CompressedFiles,MiB(result.Bytes),result.Skipped);L.Text(detail,"storage.rescan");optimization.Visibility=Visibility.Collapsed;}}
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

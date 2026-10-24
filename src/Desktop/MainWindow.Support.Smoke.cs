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
            if(Icon is not System.Windows.Media.Imaging.BitmapSource bitmap || bitmap.PixelWidth<16 || bitmap.PixelHeight<16)throw new Exception("Application icon did not decode");
            var iconPixels=new byte[bitmap.PixelHeight*((bitmap.PixelWidth*bitmap.Format.BitsPerPixel+7)/8)];
            bitmap.CopyPixels(iconPixels,(bitmap.PixelWidth*bitmap.Format.BitsPerPixel+7)/8,0);
            if(!iconPixels.Any(p=>p!=0))throw new Exception("Application icon is empty");
            var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if(DesktopWindowIdentity.Read(handle,5)!=DesktopWindowIdentity.AppId || DesktopWindowIdentity.Read(handle,3)!=Environment.ProcessPath+",0")throw new Exception("Taskbar identity or icon resource missing");
            if(TaskbarGetIcon(handle,127,new IntPtr(1),IntPtr.Zero)==IntPtr.Zero)throw new Exception("Native large window icon missing");
            foreach(int language in new[]{0,1,2})foreach(int appearance in new[]{1,2})
            {
                LanguageSelector.SelectedIndex=language; ThemeSelector.SelectedIndex=appearance;
                Width=920;Height=650;WorkspaceTabs.SelectedItem=AccountsTab;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var repairBounds=SandboxRepairButton.TransformToAncestor(this).TransformBounds(new Rect(SandboxRepairButton.RenderSize));
                if(!SandboxRepairButton.IsVisible || repairBounds.Right>ActualWidth || repairBounds.Bottom>ActualHeight)throw new Exception("Account binding entry clipped");
                Capture($"sandbox-accounts-{language}-{appearance}");
                var bindingDialog=new SandboxManagementWindow(this,new DailyAccount(1,"Example account",new string('a',64),"…1234",true,false,""),
                    new("Dustweave_Test",@"C:\Sandbox\Test",true,"sandbox.repair.required",[]),[
                    new("MyRenamedBox",@"C:\Users\Example\AppData\Local\Dustweave\instances\Dustweave_aaaaaaaaaaaaaaaaaaaa\root",""),
                    new("OtherBox",@"C:\Sandbox\Other","sandbox.repair.other_root")]);
                bindingDialog.Show();
                try {
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var manual=Nodes<Button>(bindingDialog).Single(b=>b.Name=="SandboxBindingManual");
                    var choose=Nodes<ComboBox>(bindingDialog).Single(b=>b.Name=="SandboxBindingChoice");
                    if(choose.IsVisible || manual.IsVisible || choose.Items.Count!=2)throw new Exception("Manual selector shown before automatic match");
                    Capture($"sandbox-match-{language}-{appearance}",(FrameworkElement)bindingDialog.Content);
                    Nodes<Button>(bindingDialog).Single(b=>b.Name=="SandboxBindingWrong").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    if(!manual.IsEnabled || !choose.IsVisible)throw new Exception("Manual correction unavailable");
                    Capture($"sandbox-binding-{language}-{appearance}",(FrameworkElement)bindingDialog.Content);
                    choose.SelectedIndex=1;if(manual.IsEnabled)throw new Exception("Foreign box can be bound from UI");
                    if(manual.TransformToAncestor(bindingDialog).TransformBounds(new Rect(manual.RenderSize)).Bottom>bindingDialog.ActualHeight)throw new Exception("Binding action clipped");
                    checks+=3;
                } finally {bindingDialog.Close();}
            }
            WorkspaceTabs.SelectedItem=DiagnosticsTab;
            foreach(int language in new[]{0,1,2})foreach(int appearance in new[]{1,2}) {
                LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;Width=1180;Height=900;
                ShowError(new InvalidOperationException("suite.owner-exited"));
                if(ActionNotice.Visibility!=Visibility.Visible || !ActionNoticeText.Text.Contains(L.Get("issue.action.owner-ended")))throw new Exception("Session issue lost its visible actionable category");
                WorkspaceTabs.SelectedItem=AccountsTab;
                ShowError(new InvalidOperationException("suite.owner-exited"));
                if(ActionNotice.Visibility!=Visibility.Visible || ProgressText.Text.Length!=0)throw new Exception("Account error duplicated into operation footer");
                DismissActionNotice_Click(this,new RoutedEventArgs());
                if(ActionNotice.Visibility!=Visibility.Collapsed || ProgressText.Text.Length!=0)throw new Exception("Dismissed account error remains visible");
                WorkspaceTabs.SelectedItem=DiagnosticsTab;
                ShowError(new InvalidOperationException("suite.owner-exited"));
                checks+=3;
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
                string compressionSample=Path.Combine(root,"live","diagnostics","compression-smoke.json");
                if(DailyStorageCompression.Supported(root))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(compressionSample)!);
                    File.WriteAllText(compressionSample,System.Text.Json.JsonSerializer.Serialize(new{state="failed",text=new string('x',65536)}));
                    File.SetLastWriteTimeUtc(compressionSample,DateTime.UtcNow.AddDays(-3));
                }
                var cleanup=CreateLogCleanupWindow();cleanup.Show();
                try {
                    var rescan=Nodes<Button>(cleanup).Single(b=>b.Name=="LogCleanupScan");
                    for(int i=0;i<200&&!rescan.IsEnabled;i++)await Task.Delay(20);
                    var start=Nodes<Button>(cleanup).Single(b=>b.Name=="LogCleanupStart");
                    var status=Nodes<TextBlock>(cleanup).Single(b=>b.Name=="LogCleanupStatus");
                    if(!rescan.IsEnabled||status.Text.StartsWith("logs."))throw new Exception("Cleanup preview failed");
                    if(DailyStorageCompression.Supported(root) && (!start.IsEnabled || !Nodes<TextBlock>(cleanup).Any(t=>t.Visibility==Visibility.Visible&&t.Text==L.Get("storage.compression",1))))throw new Exception("Compression-only maintenance is not understandable or available");
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Capture($"support-cleanup-{language}-{appearance}",(FrameworkElement)cleanup.Content);
                    if(start.TransformToAncestor(cleanup).TransformBounds(new Rect(start.RenderSize)).Bottom>cleanup.ActualHeight)throw new Exception("Cleanup action clipped");
                    if(language==0&&appearance==1){
                        string obsolete=Path.Combine(root,"connection-cleanup.log");File.WriteAllText(obsolete,"isolated old log");File.SetLastWriteTimeUtc(obsolete,DateTime.UtcNow.AddDays(-20));
                        rescan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<200&&!rescan.IsEnabled;i++)await Task.Delay(20);
                        if(!start.IsEnabled)throw new Exception("Manual cleanup could not be selected");
                        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<200&&!rescan.IsEnabled;i++)await Task.Delay(20);
                        if(File.Exists(obsolete)||!rescan.IsEnabled)throw new Exception("Manual cleanup did not finish");
                    }
                    checks+=2;
                }finally{cleanup.Close();if(File.Exists(compressionSample))File.Delete(compressionSample);}
                var warning=CreateStorageWarningWindow([new(@"C:\",512*1048576L),new(@"E:\",384*1048576L)],new([new("main",root,2662*1048576L,2100*1048576L,0,800*1048576L,0)]),out var warningAction);warning.Show();
                try {
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var ignore=Nodes<CheckBox>(warning).Single(c=>c.Name=="StorageWarningIgnore");
                    var cleanButton=Nodes<Button>(warning).Single(b=>b.Name=="StorageWarningClean");
                    var moveButton=Nodes<Button>(warning).Single(b=>b.Name=="StorageWarningMove");
                    if(cleanButton.TransformToAncestor(warning).TransformBounds(new Rect(cleanButton.RenderSize)).Bottom>warning.ActualHeight)throw new Exception("Low-space actions clipped");
                    var warningText=Nodes<TextBlock>(warning).Select(t=>t.Text).ToArray();
                    if(!warningText.Any(t=>t==L.Get("storage.low.total",2662d)) || warningText.Any(t=>t.StartsWith("storage.low.")))throw new Exception("Full storage size or localization missing");
                    Capture($"storage-warning-{language}-{appearance}",(FrameworkElement)warning.Content);
                    if(language==0&&appearance==1){ignore.IsChecked=true;cleanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(warningAction()!="clean" || !DailyStorageWarning.IsIgnored(root,DateTime.Now))throw new Exception("Ignore checkbox or cleanup route failed");}
                    else if(language==1&&appearance==1){moveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(warningAction()!="move")throw new Exception("Migration entry failed");}
                    else {Nodes<Button>(warning).Single(b=>b.Name=="StorageWarningLater").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(warningAction()!=null)throw new Exception("Dismissal requested maintenance");}
                    checks+=4;
                } finally {if(warning.IsVisible)warning.Close();}
                WorkspaceTabs.SelectedItem=UpdatesTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var destinations=Nodes<Button>(updatePanel).Select(b=>b.Tag as string).ToArray();
                if(!destinations.Contains(DailyDownloadLinks.Release)||!destinations.Contains(DailyDownloadLinks.Mirror))throw new Exception("Download routes missing");
                Capture($"updates-links-{language}-{appearance}");WorkspaceTabs.SelectedItem=DiagnosticsTab;checks++;
            }
            int probes=0;nextStorageCheck=DateTime.MinValue;
            DailyDiskSpace[] LowSpace(){probes++;return [new(@"C:\",128*1048576L)];}
            await CheckStorageWarningAsync(LowSpace);
            if(probes!=0)throw new Exception("Persisted ignore still probed or opened a reminder");
            DailyJson.Write(Path.Combine(root,"storage-warning.json"),new DailyStorageWarningPreference("2000-01-01"));
            nextStorageCheck=DateTime.MinValue;busy=true;
            var automaticWarning=CheckStorageWarningAsync(LowSpace);
            Window? warningWindow=null;
            for(int i=0;i<200&&warningWindow==null;i++){await Task.Delay(20);warningWindow=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Name=="StorageWarningWindow");}
            if(warningWindow==null||DailyDialogs.ModalDepth!=0||!busy)throw new Exception("Automatic warning missing or blocked active work");
            Nodes<Button>(warningWindow).Single(b=>b.Name=="StorageWarningLater").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await automaticWarning;busy=false;
            int previousProbes=probes;nextStorageCheck=DateTime.MinValue;await CheckStorageWarningAsync(LowSpace);
            if(probes!=previousProbes||storageCheckRunning)throw new Exception("Unignored shortage repeated before cooldown or left running state");checks+=3;
            Width=920;Height=650;WorkspaceTabs.SelectedItem=DiagnosticsTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(!ExportDiagnosticButton.IsVisible)throw new Exception("Compact diagnostics lost export entry");
            Capture("support-compact");checks++;
            string automaticLog=Path.Combine(root,"connection-cleanup.log");File.WriteAllText(automaticLog,"automatic cleanup fixture");File.SetLastWriteTimeUtc(automaticLog,DateTime.UtcNow.AddDays(-20));
            logPolicy=new(false);nextLogCleanup=DateTime.UtcNow.AddMinutes(-1);
            await CheckLogCleanupAsync();if(!File.Exists(automaticLog))throw new Exception("Disabled retention still ran");
            logPolicy=new();busy=true;
            await CheckLogCleanupAsync();if(!File.Exists(automaticLog))throw new Exception("Retention ran while automation was busy");
            busy=false;await CheckLogCleanupAsync();
            if(File.Exists(automaticLog)||nextLogCleanup<DateTime.UtcNow.AddHours(11))throw new Exception("Idle retention did not run or persist its schedule");
            var retainedAt=lastRetentionUtc;
            SaveLogCleanupResult(new(0,0,0){MoreWork=true,CompressedFiles=128},false);
            if(lastRetentionUtc!=retainedAt || nextLogCleanup>DateTime.UtcNow.AddMinutes(6) || !compressionPending)throw new Exception("Compression continuation changed the retention clock");
            SaveLogCleanupResult(new(0,0,0),false);
            if(lastRetentionUtc!=retainedAt || nextLogCleanup!=retainedAt.AddHours(12) || compressionPending)throw new Exception("Completed compression postponed the retention clock");
            checks+=2;
            if(!File.Exists(Path.Combine(root,"log-cleanup-last.json")))throw new Exception("Cleanup schedule was not persisted");checks+=4;
            string stepId=Guid.NewGuid().ToString("N"),stepFolder=Path.Combine(root,"live","steps",stepId);
            using(var scope=new DailyLogEvidence(root))
            {
                DailyJson.Write(Path.Combine(stepFolder,"intent.json"),new{Id=stepId,Reason="fixture"});
                DailyJson.Write(Path.Combine(stepFolder,"before.json"),new{fixture="full snapshot"});
                DailyJson.Write(Path.Combine(stepFolder,"result.json"),new{id=stepId,state="dispatched_only",engine="dotnet-driver-v1"});
                scope.Complete();
            }
            logPolicy=new(false);nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(-1);busy=true;
            await CheckLogCleanupAsync();
            if(!DailyLogEvidence.HasPending(root)||!File.Exists(Path.Combine(stepFolder,"before.json")))throw new Exception("Log compaction ran during automation");
            busy=false;await CheckLogCleanupAsync();
            if(DailyLogEvidence.HasPending(root)||File.Exists(Path.Combine(stepFolder,"before.json")))throw new Exception("Idle compaction did not drain completed work independently of retention");
            checks+=2;
            if(demo.Calls.Count!=calls)throw new Exception("Support workflow touched the game");
            DailyJson.Write(Path.Combine(smoke,"support-ui.json"),new{status="passed",checks,realGameTouched=false,exportUsesIsolatedData=true});
        }finally{Width=1180;Height=900;LanguageSelector.SelectedIndex=0;ThemeSelector.SelectedIndex=1;timer.Start();}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern IntPtr TaskbarGetIcon(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}

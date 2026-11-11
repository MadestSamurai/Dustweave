using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private Window CreateStorageRebindWindow(Window owner,
        Func<string,CancellationToken,Task<DailyStorageMigration.RebindPreview>>? inspectForSmoke=null,
        Func<string,CancellationToken,Task<DailyStorageMigration.RebindPreview>>? applyForSmoke=null)
    {
        if(smoke==null&&(inspectForSmoke!=null||applyForSmoke!=null))throw new InvalidOperationException("Storage fixtures require an isolated smoke window.");
        var window=new Window {Owner=owner,Width=640,MinWidth=480,MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-60),
            SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        L.Bind(window,Window.TitleProperty,"storage.rebind");
        var panel=new StackPanel {Margin=new(24,8,24,24)};
        TextBlock Note(string key) {var text=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};L.Text(text,key);panel.Children.Add(text);return text;}
        Note("storage.rebind.instructions");
        var recover=new Button{Name="StorageRepair",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,16)};
        L.Bind(recover,ContentControl.ContentProperty,"storage.repair.open");panel.Children.Add(recover);
        var row=new DockPanel();panel.Children.Add(row);
        var browse=new Button {Name="StorageRebindBrowse",Margin=new(10,0,0,0)};L.Bind(browse,ContentControl.ContentProperty,"storage.rebind.browse");DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);
        var path=new TextBox {Name="StorageRebindPath",VerticalContentAlignment=VerticalAlignment.Center};row.Children.Add(path);
        L.Bind(path,System.Windows.Automation.AutomationProperties.NameProperty,"storage.rebind.browse");
        var status=Note("storage.rebind.select");status.Name="StorageRebindStatus";status.Margin=new(0,14,0,12);
        var progress=new ProgressBar {Height=4,IsIndeterminate=SystemParameters.ClientAreaAnimation,Value=50,Visibility=Visibility.Collapsed,Margin=new(0,0,0,16)};panel.Children.Add(progress);
        var actions=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,4,0,0)};panel.Children.Add(actions);
        var cancel=new Button {IsCancel=true,Margin=new(0,0,10,0)};L.Bind(cancel,ContentControl.ContentProperty,"common.cancel");actions.Children.Add(cancel);
        var apply=new Button {Name="StorageRebindApply",Style=(Style)FindResource("PrimaryButton"),IsEnabled=false};L.Bind(apply,ContentControl.ContentProperty,"storage.rebind.check");actions.Children.Add(apply);
        bool working=false,closed=false,closingBlocked=false;string? verified=null;var lifetime=new CancellationTokenSource();
        var service=new DailyStorageMigration(root);
        recover.Click+=(_,_)=>{if(!working&&DailyDialogs.ShowModal(CreateStorageRepairWindow(window))==true)window.DialogResult=true;};
        void Error(Exception error) {
            try {DailyJson.Write(Path.Combine(root,"storage-operation-error.json"),new {atUtc=DateTimeOffset.UtcNow,operation="rebind",error=error.ToString()});}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
            if(error.Message.StartsWith("storage.",StringComparison.Ordinal))L.Text(status,error.Message);else DailyUiText.Error(status,error);
            status.SetResourceReference(TextBlock.ForegroundProperty,"Error");
        }
        path.TextChanged+=(_,_)=>{verified=null;apply.IsEnabled=!working&&!string.IsNullOrWhiteSpace(path.Text);L.Text(status,"storage.rebind.select");status.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");L.Bind(apply,ContentControl.ContentProperty,"storage.rebind.check");};
        browse.Click+=(_,_)=>{
            var picker=new Microsoft.Win32.OpenFolderDialog {Title=L.Get("storage.rebind.browse")};
            if(picker.ShowDialog(window)==true)path.Text=picker.FolderName;
        };
        apply.Click+=async (_,_)=>
        {
            if(working)return;
            if(Unavailable||dailyQueue.IsRunning||updateChecking||updateInstalling||ToolOpen||IsSandboxWindow||smoke==null&&DailyUpdateHost.HasIsolatedWindows()) {L.Text(status,"storage.busy");return;}
            bool committing=verified==path.Text;string selected=path.Text;
            working=true;closingBlocked=true;recover.IsEnabled=path.IsEnabled=browse.IsEnabled=apply.IsEnabled=cancel.IsEnabled=false;progress.Visibility=Visibility.Visible;
            L.Text(status,committing?"storage.rebind.applying":"storage.rebind.checking");status.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
            try
            {
                // The second click confirms the inspected directory. The core rechecks
                // ownership/bindings under locks, so a stale preview grants nothing.
                if(committing)
                {
                    SetCacheChanging(true);
                    try {await (applyForSmoke?.Invoke(selected,lifetime.Token)??service.RebindAsync(selected,lifetime.Token));}
                    finally {SetCacheChanging(false);}
                    if(!closed){closingBlocked=working=false;window.DialogResult=true;}
                }
                else
                {
                    DailyStorageMigration.RebindPreview result;
                    if(inspectForSmoke!=null){closingBlocked=false;cancel.IsEnabled=true;result=await inspectForSmoke(selected,lifetime.Token);}
                    else {await service.RecoverAsync(lifetime.Token);closingBlocked=false;cancel.IsEnabled=true;result=await service.InspectRebindAsync(selected,lifetime.Token);}
                    if(!closed) {verified=selected;L.Text(status,"storage.rebind.preview",result.Previous,result.Selected,result.Sandboxes);L.Bind(apply,ContentControl.ContentProperty,"storage.rebind.apply");}
                }
            }
            catch(OperationCanceledException){}
            catch(Exception error){verified=null;if(!closed){Error(error);L.Bind(apply,ContentControl.ContentProperty,"storage.rebind.check");}}
            finally {closingBlocked=working=false;if(!closed){recover.IsEnabled=path.IsEnabled=browse.IsEnabled=cancel.IsEnabled=true;apply.IsEnabled=!string.IsNullOrWhiteSpace(path.Text);progress.Visibility=Visibility.Collapsed;}else lifetime.Dispose();}
        };
        window.Closing+=(_,e)=>{if(closingBlocked)e.Cancel=true;};
        window.Closed+=(_,_)=>{closed=true;lifetime.Cancel();if(!working)lifetime.Dispose();};
        window.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        DailyDialogs.Prepare(window);return window;
    }
}

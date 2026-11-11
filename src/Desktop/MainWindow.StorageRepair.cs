using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private Window CreateStorageRepairWindow(Window owner,
        Func<string,CancellationToken,Task<DailyStorageMigration.RepairPreview>>? inspectForSmoke=null,
        Func<string,DailyStorageMigration.RepairPreview,CancellationToken,Task<DailyStorageMigration.RepairPreview>>? applyForSmoke=null)
    {
        if(smoke==null&&(inspectForSmoke!=null||applyForSmoke!=null))throw new InvalidOperationException("Storage fixtures require an isolated smoke window.");
        var window=new Window{Owner=owner,Width=660,MinWidth=480,MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-60),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        L.Bind(window,Window.TitleProperty,"storage.repair.title");
        var panel=new StackPanel{Margin=new(24,8,24,24)};
        TextBlock Note(string key){var t=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};L.Text(t,key);panel.Children.Add(t);return t;}
        Note("storage.repair.instructions");
        var row=new DockPanel();panel.Children.Add(row);
        var browse=new Button{Name="StorageRepairBrowse",Margin=new(10,0,0,0)};L.Bind(browse,ContentControl.ContentProperty,"storage.repair.browse");DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);
        var path=new TextBox{Name="StorageRepairPath",VerticalContentAlignment=VerticalAlignment.Center};row.Children.Add(path);L.Bind(path,System.Windows.Automation.AutomationProperties.NameProperty,"storage.repair.path");
        var status=Note("storage.repair.select");status.Name="StorageRepairStatus";status.Margin=new(0,16,0,12);
        var progress=new ProgressBar{Height=4,Visibility=Visibility.Collapsed,Margin=new(0,0,0,16)};panel.Children.Add(progress);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(actions);
        var cancel=new Button{Name="StorageRepairCancel",Margin=new(0,0,10,0)};L.Bind(cancel,ContentControl.ContentProperty,"common.cancel");actions.Children.Add(cancel);
        var apply=new Button{Name="StorageRepairApply",IsEnabled=false,Style=(Style)FindResource("PrimaryButton")};L.Bind(apply,ContentControl.ContentProperty,"storage.repair.check");actions.Children.Add(apply);
        var service=new DailyStorageMigration(root);var lifetime=new CancellationTokenSource();
        bool working=false,closed=false,done=false;DailyStorageMigration.RepairPreview? preview=null;
        path.TextChanged+=(_,_)=>{preview=null;apply.IsEnabled=!working&&!string.IsNullOrWhiteSpace(path.Text);L.Text(status,"storage.repair.select");L.Bind(apply,ContentControl.ContentProperty,"storage.repair.check");};
        browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title=L.Get("storage.repair.browse")};if(picker.ShowDialog(window)==true)path.Text=Path.Combine(picker.FolderName,"Dustweave-recovered-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));};
        cancel.Click+=(_,_)=>{if(working){lifetime.Cancel();cancel.IsEnabled=false;}else if(done)window.DialogResult=true;else window.Close();};
        apply.Click+=async(_,_)=>
        {
            if(working)return;
            if(Unavailable||dailyQueue.IsRunning||updateChecking||updateInstalling||ToolOpen||IsSandboxWindow||smoke==null&&DailyUpdateHost.HasIsolatedWindows()){L.Text(status,"storage.busy");return;}
            bool committing=preview!=null;string selected=path.Text;
            working=true;path.IsEnabled=browse.IsEnabled=apply.IsEnabled=false;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=SystemParameters.ClientAreaAnimation;
            L.Text(status,committing?"storage.repair.copying":"storage.repair.checking");status.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
            try
            {
                if(committing)
                {
                    SetCacheChanging(true);DailyStorageMigration.RepairPreview result;
                    try
                    {
                        var phases=new Progress<DailyStorageMigration.MoveProgress>(p=>{
                            if(closed||!working||done)return;
                            progress.IsIndeterminate=p.Total<=0&&SystemParameters.ClientAreaAnimation;
                            progress.Maximum=Math.Max(1,p.Total);progress.Value=p.Completed;
                            if(p.Stage==DailyStorageMigration.MoveStage.Copying)L.Text(status,"storage.repair.progress",StorageSize(p.Completed),StorageSize(p.Total));
                            else L.Text(status,"storage.repair.finishing");
                        });
                        result=applyForSmoke!=null?await applyForSmoke(selected,preview!,lifetime.Token):await service.RepairAsync(selected,(await Task.Run(sessions.Read)).Accounts,preview!,lifetime.Token,phases);
                    }
                    finally{SetCacheChanging(false);}
                    done=true;L.Text(status,"storage.repair.done",result.Directory,result.SandboxDirectory,result.Preserved,result.Rebuild,result.Unresolved);status.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
                    apply.Visibility=Visibility.Collapsed;L.Bind(cancel,ContentControl.ContentProperty,"storage.repair.close");
                }
                else
                {
                    if(inspectForSmoke!=null)preview=await inspectForSmoke(selected,lifetime.Token);
                    else {await service.RecoverAsync(lifetime.Token);preview=await service.InspectRepairAsync(selected,(await Task.Run(sessions.Read)).Accounts,lifetime.Token);}
                    if(!closed){L.Text(status,"storage.repair.preview",preview.Directory,preview.SandboxDirectory,preview.Preserved,preview.Rebuild,preview.Unresolved,StorageSize(preview.Bytes));L.Bind(apply,ContentControl.ContentProperty,"storage.repair.apply");}
                }
            }
            catch(OperationCanceledException){preview=null;L.Text(status,"storage.repair.cancelled");}
            catch(Exception error)
            {
                preview=null;
                try{DailyJson.Write(Path.Combine(root,"storage-operation-error.json"),new{atUtc=DateTimeOffset.UtcNow,operation="repair",error=error.ToString()});}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
                if(error.Message.StartsWith("storage.",StringComparison.Ordinal))L.Text(status,error.Message);else DailyUiText.Error(status,error);
                status.SetResourceReference(TextBlock.ForegroundProperty,"Error");
            }
            finally
            {
                working=false;if(!closed){if(lifetime.IsCancellationRequested){lifetime.Dispose();lifetime=new();}path.IsEnabled=browse.IsEnabled=!done;cancel.IsEnabled=true;apply.IsEnabled=!done&&!string.IsNullOrWhiteSpace(path.Text);progress.Visibility=Visibility.Collapsed;if(preview==null&&!done)L.Bind(apply,ContentControl.ContentProperty,"storage.repair.check");}
            }
        };
        window.Closing+=(_,e)=>{if(working){lifetime.Cancel();e.Cancel=true;}};
        window.Closed+=(_,_)=>{closed=true;lifetime.Cancel();lifetime.Dispose();};
        window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};DailyDialogs.Prepare(window);return window;
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

internal sealed partial class SandboxManagementWindow : Window
{
    private static DailyLanguage L=>DailyLanguage.Current;
    private readonly DailyAccount account;
    private readonly DailySandboxInfo? fixture;
    private readonly Func<CancellationToken,Task<DailySandboxInfo>> inspect;
    private readonly TextBlock status=new(){Name="SandboxSpaceStatus",TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,16)};
    private readonly TextBlock feedback=new(){Name="SandboxSpaceFeedback",TextWrapping=TextWrapping.Wrap,Margin=new(0,14,0,0)};
    private readonly TextBlock path=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};
    private readonly Button create=new(){Name="SandboxSpaceCreate"},remove=new(){Name="SandboxSpaceRemove"},refresh=new(){Name="SandboxSpaceRefresh"},fix=new(){Name="SandboxSpaceRepair"},browse=new(){Name="SandboxSpaceBrowse"};
    private readonly Button settings=new(){Name="SandboxSpaceSettings",Visibility=Visibility.Collapsed};
    private readonly Button preferences=new(){Name="SandboxSpacePreferences"};
    private readonly CancellationTokenSource lifetime=new();
    private Task<DailySandboxInfo>? inspection;
    private DailySandboxInfo? info;
    private bool reading,mutating,closed,removed;
    internal bool OpenSoftwareSettings { get; private set; }
    private bool Working=>reading||mutating;
    internal SandboxManagementWindow(Window owner,DailyAccount account,DailySandboxInfo? fixture=null,
        DailySandboxCandidate[]? bindingFixture=null,Func<CancellationToken,Task<DailySandboxInfo>>? inspect=null)
    {
        Owner=owner;this.account=account;this.fixture=fixture;this.bindingFixture=bindingFixture;
        this.inspect=inspect??(token=>DailySandbox.InspectSpaceAsync(account.AccountKey,token));
        Width=680;MinWidth=460;SizeToContent=SizeToContent.Height;MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-40);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;L.Bind(this,TitleProperty,"sandbox.manage.title");
        var body=new StackPanel{Margin=new(24,8,24,24)};
        body.Children.Add(new TextBlock{Text=account.Name,FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        L.Text(status,"sandbox.manage.loading");body.Children.Add(status);
        path.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");body.Children.Add(path);
        var actions=new WrapPanel();
        foreach(var pair in new[]{(create,"sandbox.manage.create"),(fix,"sandbox.repair.title"),(preferences,"sandbox.preferences.title"),(browse,"sandbox.manage.open"),(refresh,"common.refresh")}) {
            pair.Item1.Margin=new(0,0,8,8);L.Bind(pair.Item1,ContentControl.ContentProperty,pair.Item2);actions.Children.Add(pair.Item1);
        }
        create.Style=(Style)Application.Current.FindResource("PrimaryButton");body.Children.Add(actions);
        body.Children.Add(BuildBindingPanel());
        body.Children.Add(feedback);L.Bind(settings,ContentControl.ContentProperty,"nav.software");
        settings.HorizontalAlignment=HorizontalAlignment.Left;settings.Margin=new(0,10,0,0);body.Children.Add(settings);
        settings.Click+=(_,_)=>{OpenSoftwareSettings=true;Close();};
        var footer=new DockPanel{Margin=new(0,24,0,0)};
        remove.Style=(Style)Application.Current.FindResource("DangerButton");L.Bind(remove,ContentControl.ContentProperty,"sandbox.manage.remove");DockPanel.SetDock(remove,Dock.Left);footer.Children.Add(remove);
        var close=new Button{Name="SandboxSpaceClose",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right};L.Bind(close,ContentControl.ContentProperty,"window.close");close.Click+=(_,_)=>Close();footer.Children.Add(close);body.Children.Add(footer);
        Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        refresh.Click+=async(_,_)=>await RefreshAsync();
        preferences.Click+=async(_,_)=>{
            if(Working||fixture!=null)return;
            try {
                var dialog=new GamePreferenceSyncWindow(this,account);DailyDialogs.ShowModal(dialog);
                if(dialog.Completed)DailySandboxPreferenceTransfer.DismissSuggestion(account.AccountKey);
            }catch(Exception error){ShowError(error);}
            await RefreshAsync();
        };
        create.Click+=async(_,_)=>await Change(()=>DailySandbox.CreateSpaceAsync(account,Environment.ProcessPath!,Progress,lifetime.Token),"sandbox.manage.created");
        fix.Click+=async(_,_)=>await ReadBindingsAsync();
        browse.Click+=(_,_)=>{if(info==null||!System.IO.Directory.Exists(info.Root))return;try{Process.Start(new ProcessStartInfo(info.Root){UseShellExecute=true});}catch(Exception error){ShowError(error);}};
        remove.Click+=async(_,_)=>{
            if(Working||info?.CanRemove!=true||fixture!=null)return;
            if(DailyDialogs.ShowModal(DailyDialogs.Message(this,L.Get("sandbox.manage.remove"),L.Get("sandbox.manage.confirm_remove",account.Name),true,safeDefault:true))!=true)return;
            await Change(()=>DailySandbox.RemoveSpaceAsync(account.AccountKey,Environment.ProcessPath!,Progress,lifetime.Token),"sandbox.manage.removed");
        };
        ContentRendered+=async(_,_)=>await RefreshAsync();
        Closing+=(_,e)=>{if(mutating){e.Cancel=true;lifetime.Cancel();L.Text(feedback,"sandbox.manage.canceling");}};
        Closed+=(_,_)=>{closed=true;lifetime.Cancel();};UpdateActions();DailyDialogs.Prepare(this);
    }
    internal async Task RefreshAsync()
    {
        if(Working||closed)return;reading=true;UpdateActions();L.Text(status,"sandbox.manage.loading");
        settings.Visibility=Visibility.Collapsed;bindingPanel.Visibility=Visibility.Collapsed;
        bool repairRequired=false;
        try {
            if(fixture!=null)info=fixture;
            else {
                // Keep one pending probe per window if a native driver is slow.
                // Closing does not wait for its completion.
                inspection??=Task.Run(()=>inspect(lifetime.Token));
                info=await inspection.WaitAsync(TimeSpan.FromSeconds(8),lifetime.Token);
            }
            if(closed)return;Present();repairRequired=info.Issue=="sandbox.repair.required";
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested) { }
        catch(Exception error) {
            info=null;if(closed)return;
            if(error is TimeoutException){feedback.SetResourceReference(TextBlock.ForegroundProperty,"Error");L.Text(feedback,"sandbox.manage.slow");}
            else ShowError(error);
            L.Text(status,"sandbox.manage.unavailable");settings.Visibility=Visibility.Visible;
        }
        finally{if(inspection?.IsCompleted==true)inspection=null;reading=false;if(!closed)UpdateActions();}
        if(repairRequired&&!closed)await ReadBindingsAsync();
    }
    private void Present()
    {
        if(info==null||closed)return;
        L.Text(status,info.Running?"sandbox.manage.running":info.Issue=="sandbox.repair.required"?"sandbox.manage.binding_help":info.Issue.Length>0?info.Issue:info.Exists?"sandbox.manage.ready":removed?"sandbox.manage.rebuild_prompt":"sandbox.manage.empty");
        if(info.Running)status.Text+="\n"+string.Join(" · ",info.Processes.Select(p=>p.Name+" ("+p.Id+")"));
        path.Text=info.Exists?info.Box+"\n"+info.Root:"";path.Visibility=info.Exists?Visibility.Visible:Visibility.Collapsed;
        L.Bind(create,ContentControl.ContentProperty,removed&&!info.Exists?"sandbox.manage.rebuild":"sandbox.manage.create");
    }
    internal void PresentRemoved()
    {
        removed=true;info=new(info?.Box??"",info?.Root??"",false,"",[]);
        bindingPanel.Visibility=Visibility.Collapsed;feedback.Text="";Present();UpdateActions();
    }
    private async Task Change(Func<Task> action,string success)
    {
        if(Working||closed||fixture!=null)return;mutating=true;UpdateActions();feedback.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");feedback.Text="";
        try{await Task.Run(action);if(!closed){if(success=="sandbox.manage.removed")PresentRemoved();else{removed=false;L.Text(feedback,success);}}}
        catch(OperationCanceledException){if(!closed)L.Text(feedback,"sandbox.manage.canceled");}
        catch(Exception error){ShowError(error);}
        finally{mutating=false;if(!closed){UpdateActions();if(!lifetime.IsCancellationRequested)await RefreshAsync();else Close();}}
    }
    private void Progress(string text)=>Dispatcher.Invoke(()=>{if(!closed){feedback.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");feedback.Text=L.Translate(text);}});
    private void ShowError(Exception error){if(!closed){feedback.SetResourceReference(TextBlock.ForegroundProperty,"Error");feedback.Text=DailyUserText.Error(error,L.Translate);}}
    private void UpdateActions()
    {
        bool idle=!Working;
        refresh.IsEnabled=idle;settings.IsEnabled=idle;
        preferences.IsEnabled=idle&&account.Valid&&info is{Running:false,Issue:""};
        create.IsEnabled=idle&&info is{Exists:false,Issue:""}&&account.Valid;
        create.Visibility=info?.Exists==true?Visibility.Collapsed:Visibility.Visible;
        fix.IsEnabled=idle&&info is{Exists:true,Running:false,Issue:not "sandbox.manage.removal_pending"};
        fix.Visibility=info?.Exists==true&&bindingPanel.Visibility!=Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
        browse.IsEnabled=idle&&info?.Exists==true;browse.Visibility=info?.Exists==true?Visibility.Visible:Visibility.Collapsed;
        remove.IsEnabled=idle&&info?.CanRemove==true;
        remove.Visibility=info?.Exists==true?Visibility.Visible:Visibility.Collapsed;UpdateBindingActions();
    }
}

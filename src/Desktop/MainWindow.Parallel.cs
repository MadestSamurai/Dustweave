using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyParallelOptions parallelOptions=new();
    private DailyParallelSession? parallel;
    private DailyParallelRuntime? parallelRuntime;
    private IDisposable? parallelControl;
    private readonly DailyParallelPanel parallelPanel=new();
    private readonly DailyParallelPanel sequentialPanel=new(true);
    private DailyParallelRun? sequentialRun;
    private string sequentialAccount="";
    private bool parallelPolling,parallelClosing,parallelWasBusy;
    private bool ParallelBusy=>parallel?.HasWork==true;
    private void InitializeParallel()
    {
        sequentialPanel.ControlRequested+=(_,action)=> { if(action=="stop") {dailyStopping=true;dailyQueue.Stop();coordinator.Stop();} };
        sequentialPanel.BackRequested+=()=> {
            if(busy)return;
            sequentialRun=null;sequentialAccount="";
            RunTab.Content=dailyPanel;RefreshTaskAccount();dailyPanel.ShowCurrentPlan();
        };
        parallelOptions=DailyJson.TryRead<DailyParallelOptions>(Path.Combine(root,"parallel-options.json"))??new();parallelOptions.Validate();
        parallelRuntime=new(Environment.ProcessPath!);
        Closed += (_,_) => { parallelControl?.Dispose(); parallelControl=null; };
        if(!IsSandboxWindow&&smoke==null)parallel=new(root,parallelRuntime);
        parallelPanel.ControlRequested+=(account,action)=>{parallel?.Control(account,action);_=PollParallelAsync();};
        parallelPanel.GameRequested+=item=>{if(smoke==null)try{parallelRuntime.ShowGame(item);}catch(Exception e){ShowError(e);}};
        parallelPanel.BackRequested+=async ()=>
        {
            if (ParallelBusy) return;
            try { await ReturnFromParallelAsync(); }
            catch(Exception error) { parallelPanel.Feedback(error.Message); }
        };
        ExecutionModeButton.Visibility=IsSandboxWindow?Visibility.Collapsed:Visibility.Visible;RefreshParallelMode();
        // A saved batch provides account progress, not a permanent navigation page.
    }
    private void RefreshParallelMode()=>L.Bind(ExecutionModeButton,ContentControl.ContentProperty,parallelOptions.Enabled?"parallel.mode_parallel":"parallel.mode_sequential");
    private void StartSequentialProgress(DailyAccount[] accounts)
    {
        // Display-only rows. They never enter the sandbox scheduler or persist jobs.
        sequentialRun=new(Guid.NewGuid().ToString("N"),new(false,1),DateTimeOffset.UtcNow,
            accounts.Select(a=>new DailyParallelItem(a,new(Guid.NewGuid().ToString("N"),a.AccountKey,a.Name,"",false))).ToArray());
        RunTab.Content=sequentialPanel;WorkspaceTabs.SelectedItem=RunTab;sequentialPanel.Show(sequentialRun);
    }
    private void UpdateSequentialProgress(QueueView? view=null,string state="running",string detail="")
    {
        if(sequentialRun==null || sequentialAccount.Length==0 || view is { Account.Length: > 0 } && view.Account!=sequentialAccount)return;
        sequentialRun=sequentialRun with { Items=sequentialRun.Items.Select(item=>item.Account.AccountKey==sequentialAccount
            ? item with { State=state,Detail=detail,Status=new(item.Job.Id,item.Account.AccountKey,state,detail,DateTimeOffset.UtcNow,Environment.ProcessId,0,Queue:view??item.Status?.Queue) } : item).ToArray() };
        sequentialPanel.Show(sequentialRun);
    }
    private void FinishSequentialProgress(string state)
    {
        if(sequentialRun==null)return;
        sequentialRun=sequentialRun with { Items=sequentialRun.Items.Select(item=>item.State is "waiting" or "running" or "connecting"
            ? item with { State=item.State=="waiting"?"stopped":state=="failed"?"failed":"stopped" } : item).ToArray() };
        sequentialAccount="";sequentialPanel.Show(sequentialRun);
    }
    private async Task PollParallelAsync()
    {
        if(parallel==null||parallelPolling||UpdateRestarting)return;parallelPolling=true;
        try
        {
            await Task.Run(() => parallel.TickAsync());parallelPanel.Show(parallel.Current);
            bool active=ParallelBusy; bool finished=parallelWasBusy && !active;
            if(!active){parallelControl?.Dispose();parallelControl=null;}if(parallelWasBusy!=active){parallelWasBusy=active;SetBusy(busy);}
            if (finished && parallel.Current is {} run)
                ShowRunResults(run.Items.Select(item => new DailyAccountRunResult(item.Account.Name,
                    item.Status?.Queue is {} view ? view with { Message = view.Message.Length > 0 ? view.Message : item.State is "failed" or "interrupted" or "stopped" ? item.Detail : "" }
                        : new QueueView(item.State, item.Detail, "", [], item.Account.AccountKey))).ToArray());
            if (parallel.Current == null && ReferenceEquals(RunTab.Content, parallelPanel)) await ReturnFromParallelAsync();
        }
        catch(Exception error){ShowError(error);}
        finally{parallelPolling=false;}
    }
    private async Task ReturnFromParallelAsync()
    {
        if (ParallelBusy) return;
        if (parallel != null) await parallel.DismissAsync();
        if (!DailyProfiles.ValidKey(TaskAccountKey)) viewedAccount = catalog?.Accounts.FirstOrDefault(a => a.Valid)?.AccountKey ?? "";
        RunTab.Content = dailyPanel;
        WorkspaceTabs.SelectedItem = RunTab;
        RefreshTaskAccount();
        dailyPanel.ShowCurrentPlan();
    }
    private async Task<bool> StartParallelAsync(DailyAccount[] targets, DailyParallelOptions? execution = null)
    {
        if(parallel==null)throw new InvalidOperationException("parallel.host_only");
        if (!await softwareSandboxPath.EnsureReadyAsync())
        {
            L.Text(ProgressText, "sandbox_setup.not_started");
            return false;
        }
        if (finalClose || Unavailable) return false;
        await PauseAutomaticMaintenanceAsync();
        if (finalClose || Unavailable) return false;
        parallelPanel.Feedback("");
        parallelControl=DailyToolControl.Acquire(root);
        try { await parallel.StartAsync(targets,execution ?? parallelOptions with { CloseGame = DailyDesktopBehavior.Read(root).CloseGameAfterRun },new DailyPreferenceStore(root).Read, new DailyTaskPresetStore(root).Selection); }
        catch { parallelControl.Dispose();parallelControl=null;throw; }
        RunTab.Content=parallelPanel;WorkspaceTabs.SelectedItem=RunTab;parallelPanel.Show(parallel.Current);SetBusy(busy);await PollParallelAsync();
        return true;
    }
    private async Task StopParallelForCloseAsync()
    {
        if(parallel==null)return;parallel.Control("","stop");
        var deadline=DateTimeOffset.UtcNow.AddSeconds(35);
        while(parallel.HasWork&&DateTimeOffset.UtcNow<deadline){await PollParallelAsync();await Task.Delay(250);}
    }
    private void ExecutionMode_Click(object sender,RoutedEventArgs e)
    {
        if(Unavailable||IsSandboxWindow)return;
        var body=new StackPanel { Margin=new(20) };
        var enabled=new CheckBox { IsChecked=parallelOptions.Enabled,Margin=new(0,0,0,14) };L.Bind(enabled,ContentControl.ContentProperty,"parallel.enabled");body.Children.Add(enabled);
        var row=new WrapPanel { Margin=new(0,0,0,14) };
        var label=new TextBlock { VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0) };L.Text(label,"parallel.maximum");row.Children.Add(label);
        var maximum=new ComboBox { Width=90,ItemsSource=new[]{1,2,3,4},SelectedItem=parallelOptions.Maximum };row.Children.Add(maximum);body.Children.Add(row);
        var help=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16) };L.Text(help,"parallel.setup_help");body.Children.Add(help);
        var setup=new Button { HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,16) };
        L.Bind(setup,ContentControl.ContentProperty,"sandbox_setup.help");body.Children.Add(setup);
        void EnableOptions(){maximum.IsEnabled=enabled.IsChecked==true;}
        enabled.Checked+=(_,_)=>EnableOptions();enabled.Unchecked+=(_,_)=>EnableOptions();EnableOptions();
        var save=new Button { HorizontalAlignment=HorizontalAlignment.Right,Style=(Style)FindResource("PrimaryButton") };L.Bind(save,ContentControl.ContentProperty,"parallel.save");body.Children.Add(save);
        var window=new Window { Owner=this,Width=520,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Content=body,WindowStartupLocation=WindowStartupLocation.CenterOwner };L.Bind(window,TitleProperty,"parallel.settings");
        setup.Click+=(_,_)=>softwareSandboxPath.OpenEditor(window);
        save.Click+=(_,_)=>{var options=parallelOptions with { Enabled=enabled.IsChecked==true,Maximum=(int)(maximum.SelectedItem??2) };options.Validate();DailyJson.Write(Path.Combine(root,"parallel-options.json"),options);parallelOptions=options;RefreshParallelMode();window.DialogResult=true;};
        DailyDialogs.ShowModal(window);
    }
}

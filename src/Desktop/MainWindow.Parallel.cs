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
    private bool parallelPolling,parallelClosing,parallelWasBusy;
    private bool ParallelBusy=>parallel?.HasWork==true;
    private void InitializeParallel()
    {
        parallelOptions=DailyJson.TryRead<DailyParallelOptions>(Path.Combine(root,"parallel-options.json"))??new();parallelOptions.Validate();
        parallelRuntime=new(Environment.ProcessPath!);
        Closed += (_,_) => { parallelControl?.Dispose(); parallelControl=null; };
        if(!IsSandboxWindow&&smoke==null)parallel=new(root,parallelRuntime);
        parallelPanel.ControlRequested+=(account,action)=>{parallel?.Control(account,action);_=PollParallelAsync();};
        parallelPanel.GameRequested+=item=>{if(smoke==null)try{parallelRuntime.ShowGame(item);}catch(Exception e){ShowError(e);}};
        parallelPanel.BackRequested+=()=>WorkspaceTabs.SelectedItem=AccountsTab;
        ExecutionModeButton.Visibility=IsSandboxWindow?Visibility.Collapsed:Visibility.Visible;RefreshParallelMode();
        if(parallel?.Current!=null){RunTab.Content=parallelPanel;parallelPanel.Show(parallel.Current);}
    }
    private void RefreshParallelMode()=>L.Bind(ExecutionModeButton,ContentControl.ContentProperty,parallelOptions.Enabled?"parallel.mode_parallel":"parallel.mode_sequential");
    private async Task PollParallelAsync()
    {
        if(parallel==null||parallelPolling)return;parallelPolling=true;
        try
        {
            await Task.Run(() => parallel.TickAsync());parallelPanel.Show(parallel.Current);
            bool active=ParallelBusy;if(!active){parallelControl?.Dispose();parallelControl=null;}if(parallelWasBusy!=active){parallelWasBusy=active;SetBusy(busy);}
        }
        catch(Exception error){ShowError(error);}
        finally{parallelPolling=false;}
    }
    private async Task StartParallelAsync(DailyAccount[] targets)
    {
        if(parallel==null)throw new InvalidOperationException("parallel.host_only");
        if(DailySandbox.Installation()==null)throw new InvalidOperationException("parallel.install_required");
        parallelPanel.Feedback("");
        parallelControl=DailyToolControl.Acquire(root);
        try { await parallel.StartAsync(targets,parallelOptions,new DailyPreferenceStore(root).Read); }
        catch { parallelControl.Dispose();parallelControl=null;throw; }
        RunTab.Content=parallelPanel;WorkspaceTabs.SelectedItem=RunTab;parallelPanel.Show(parallel.Current);SetBusy(busy);await PollParallelAsync();
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
        var close=new CheckBox { IsChecked=parallelOptions.CloseGame,Margin=new(0,0,0,14) };L.Bind(close,ContentControl.ContentProperty,"parallel.close_game");body.Children.Add(close);
        var help=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16) };L.Text(help,"parallel.setup_help");body.Children.Add(help);
        var address=new TextBox { Text="https://sandboxie-plus.com/downloads/",IsReadOnly=true,Margin=new(0,0,0,16) };
        L.Bind(address,FrameworkElement.ToolTipProperty,"parallel.install");body.Children.Add(address);
        void EnableOptions(){maximum.IsEnabled=close.IsEnabled=enabled.IsChecked==true;}
        enabled.Checked+=(_,_)=>EnableOptions();enabled.Unchecked+=(_,_)=>EnableOptions();EnableOptions();
        var save=new Button { HorizontalAlignment=HorizontalAlignment.Right,Style=(Style)FindResource("PrimaryButton") };L.Bind(save,ContentControl.ContentProperty,"parallel.save");body.Children.Add(save);
        var window=new Window { Owner=this,Width=520,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Content=body,WindowStartupLocation=WindowStartupLocation.CenterOwner };L.Bind(window,TitleProperty,"parallel.settings");
        save.Click+=(_,_)=>{var options=new DailyParallelOptions(enabled.IsChecked==true,(int)(maximum.SelectedItem??2),close.IsChecked==true);options.Validate();DailyJson.Write(Path.Combine(root,"parallel-options.json"),options);parallelOptions=options;RefreshParallelMode();window.DialogResult=true;};
        DailyDialogs.ShowModal(window);
    }
}

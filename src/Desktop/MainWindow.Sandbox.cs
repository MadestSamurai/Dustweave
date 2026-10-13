using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private bool IsSandboxWindow => smoke == null && SandboxProcessScope.CurrentBox.Length != 0;
    private void InitializeSandboxPresentation()
    {
        if (!IsSandboxWindow) return;
        SandboxRepairButton.Visibility = SandboxLaunchButton.Visibility = Visibility.Collapsed;
        LoginActions.Visibility = RecoverButton.Visibility = Visibility.Collapsed;
        SandboxHelpText.Visibility = Visibility.Visible;
        L.Text(SandboxHelpText, "sandbox.bound_help");
        ScheduleTab.Visibility = UpdatesTab.Visibility = Visibility.Collapsed;
        if (DailySandbox.Current is {} binding)
            L.Bind(this, TitleProperty, () => L.Get("sandbox.window_title", binding.Name));
    }
    private async void SandboxLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || IsSandboxWindow || Unavailable || Selected is not { Account.Valid: true } row) return;
        // Launching a separate account does not claim or replace the host game's connection.
        SetBusy(true);
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位日常助手");
            await DailySandbox.LaunchAsync(row.Account, executable, message => DailyUiText.Set(ProgressText, message), CancellationToken.None);
            L.Text(ProgressText, "sandbox.opened", row.Name);
        }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); }
    }
    private void SandboxRepair_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || IsSandboxWindow || Unavailable || Selected is not { Account.Valid:true } row) return;
        SetBusy(true);
        try { DailyDialogs.ShowModal(CreateSandboxRepairWindow(row.Account.AccountKey,row.Name)); }
        finally { SetBusy(false); }
    }
    internal Window CreateSandboxRepairWindow(string account, string name, DailySandboxCandidate[]? fixture = null)
    {
        var body = new StackPanel { Margin = new(24,8,24,24) };
        var heading = new TextBlock { TextWrapping=TextWrapping.Wrap, FontWeight=FontWeights.SemiBold };
        L.Text(heading,"sandbox.repair.account",name); body.Children.Add(heading);
        var help = new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new(0,8,0,16) };
        L.Text(help,"sandbox.repair.help"); body.Children.Add(help);
        var choices = new ComboBox { Name="SandboxBindingChoice", MinWidth=260, DisplayMemberPath="Label" };
        L.Bind(choices,System.Windows.Automation.AutomationProperties.NameProperty,"sandbox.repair.choose"); body.Children.Add(choices);
        var detail = new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new(0,8,0,8) };
        detail.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk"); body.Children.Add(detail);
        var state = new TextBlock { Name="SandboxBindingStatus", TextWrapping=TextWrapping.Wrap, Margin=new(0,8,0,0) }; body.Children.Add(state);
        var actions = new WrapPanel { Margin=new(0,20,0,0) }; body.Children.Add(actions);
        var refresh = new Button { Margin=new(0,0,8,8) }; L.Bind(refresh,ContentControl.ContentProperty,"common.refresh"); actions.Children.Add(refresh);
        var automatic = new Button { Name="SandboxBindingAuto", Margin=new(0,0,8,8) }; L.Bind(automatic,ContentControl.ContentProperty,"sandbox.repair.auto"); actions.Children.Add(automatic);
        var manual = new Button { Name="SandboxBindingManual", Margin=new(0,0,0,8) }; L.Bind(manual,ContentControl.ContentProperty,"sandbox.repair.bind"); actions.Children.Add(manual);
        var window = new Window { Owner=this, Title=L.Get("sandbox.repair.title"), Width=640, MinWidth=440,
            SizeToContent=SizeToContent.Height, MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-40), WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new ScrollViewer { Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto } };
        bool working=false;
        var lifetime = new CancellationTokenSource(); window.Closed += (_,_)=>lifetime.Cancel();
        void PresentSelection()
        {
            var candidate = (choices.SelectedItem as SandboxBindingRow)?.Candidate;
            detail.Text = candidate == null ? "" : candidate.Root;
            manual.IsEnabled = !working && candidate?.CanBind == true;
            if (!working && candidate != null) L.Text(state,candidate.CanBind ? "sandbox.repair.verified" : candidate.Issue);
        }
        void Working(bool value) { working=value; refresh.IsEnabled=automatic.IsEnabled=choices.IsEnabled=!value; manual.IsEnabled=!value && (choices.SelectedItem as SandboxBindingRow)?.Candidate.CanBind==true; }
        async Task Refresh()
        {
            Working(true); L.Text(state,"sandbox.repair.scanning");
            try {
                var candidates=fixture ?? await Task.Run(()=>DailySandboxRepair.InspectAsync(account,lifetime.Token));
                choices.ItemsSource=candidates.Select(c=>new SandboxBindingRow(c,c.Box+(c.CanBind ? "" : " · "+L.Get(c.Issue)))).ToArray();
                choices.SelectedIndex=candidates.Length>0?0:-1;
                L.Text(state,candidates.Length==0?"sandbox.repair.empty":"sandbox.repair.help");
            } catch(OperationCanceledException) { choices.ItemsSource=null; } catch(Exception error) { choices.ItemsSource=null; state.Text=DailyUserText.Error(error,L.Translate); }
            finally { Working(false); PresentSelection(); }
        }
        async Task Repair(string? box)
        {
            if(working || fixture != null)return;
            if(box != null && DailyDialogs.ShowModal(DailyDialogs.Message(window,L.Get("sandbox.repair.title"),L.Get("sandbox.repair.confirm",name,box),true,safeDefault:true))!=true)return;
            Working(true);
            try { string selected=await Task.Run(()=>DailySandboxRepair.RepairAsync(account,box,lifetime.Token)); L.Text(state,"sandbox.repair.done",selected); L.Text(ProgressText,"sandbox.repair.done",selected); }
            catch(OperationCanceledException) { } catch(Exception error) { state.Text=DailyUserText.Error(error,L.Translate); }
            finally { Working(false); }
        }
        choices.SelectionChanged+=(_,_)=>PresentSelection(); refresh.Click+=async(_,_)=>await Refresh();
        automatic.Click+=async(_,_)=>await Repair(null); manual.Click+=async(_,_)=>await Repair((choices.SelectedItem as SandboxBindingRow)?.Candidate.Box);
        window.Loaded+=async(_,_)=>await Refresh(); DailyDialogs.Prepare(window); return window;
    }
    private sealed record SandboxBindingRow(DailySandboxCandidate Candidate,string Label);
}

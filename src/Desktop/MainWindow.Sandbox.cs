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
        SandboxRepairButton.Visibility = ConnectionGuideButton.Visibility = Visibility.Collapsed;
        LoginActions.Visibility = RecoverButton.Visibility = Visibility.Collapsed;
        SandboxHelpText.Visibility = Visibility.Visible;
        L.Text(SandboxHelpText, "sandbox.bound_help");
        ScheduleTab.Visibility = UpdatesTab.Visibility = Visibility.Collapsed;
        if (DailySandbox.Current is {} binding)
            L.Bind(this, TitleProperty, () => L.Get("sandbox.window_title", binding.Name));
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
        L.Text(help,"sandbox.repair.intro"); body.Children.Add(help);
        var match = new TextBlock { Name="SandboxBindingMatch", FontSize=20, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, Margin=new(0,4,0,8) }; body.Children.Add(match);
        var choices = new ComboBox { Name="SandboxBindingChoice", MinWidth=260, DisplayMemberPath="Label", Visibility=Visibility.Collapsed };
        L.Bind(choices,System.Windows.Automation.AutomationProperties.NameProperty,"sandbox.repair.choose"); body.Children.Add(choices);
        var detail = new TextBlock { TextWrapping=TextWrapping.Wrap, FontSize=12, Margin=new(0,8,0,8), Visibility=Visibility.Collapsed };
        detail.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk"); body.Children.Add(detail);
        var state = new TextBlock { Name="SandboxBindingStatus", TextWrapping=TextWrapping.Wrap, Margin=new(0,8,0,0) }; body.Children.Add(state);
        var actions = new WrapPanel { Margin=new(0,20,0,0) }; body.Children.Add(actions);
        var automatic = new Button { Name="SandboxBindingAuto", Margin=new(0,0,8,8), Style=(Style)FindResource("PrimaryButton") }; L.Bind(automatic,ContentControl.ContentProperty,"sandbox.repair.use_match"); actions.Children.Add(automatic);
        var manual = new Button { Name="SandboxBindingManual", Margin=new(0,0,8,8), Visibility=Visibility.Collapsed, Style=(Style)FindResource("PrimaryButton") }; L.Bind(manual,ContentControl.ContentProperty,"sandbox.repair.bind"); actions.Children.Add(manual);
        var wrong = new Button { Name="SandboxBindingWrong", Margin=new(0,0,8,8) }; L.Bind(wrong,ContentControl.ContentProperty,"sandbox.repair.not_this"); actions.Children.Add(wrong);
        var refresh = new Button { Margin=new(0,0,0,8) }; L.Bind(refresh,ContentControl.ContentProperty,"common.refresh"); actions.Children.Add(refresh);
        var window = new Window { Owner=this, Title=L.Get("sandbox.repair.title"), Width=640, MinWidth=440,
            SizeToContent=SizeToContent.Height, MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-40), WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new ScrollViewer { Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto } };
        bool working=false, manualMode=false, completed=false;
        DailySandboxCandidate? matched=null;
        var lifetime = new CancellationTokenSource(); window.Closed += (_,_)=>lifetime.Cancel();
        void PresentSelection()
        {
            var candidate = (choices.SelectedItem as SandboxBindingRow)?.Candidate;
            detail.Text = candidate == null ? "" : candidate.Root;
            manual.IsEnabled = !working && candidate?.CanBind == true;
            if (!working && manualMode && candidate != null) L.Text(state,candidate.CanBind ? "sandbox.repair.verified" : candidate.Issue);
        }
        void Working(bool value) { working=value; refresh.IsEnabled=wrong.IsEnabled=choices.IsEnabled=!value&&!completed; automatic.IsEnabled=!value&&!completed&&matched!=null; manual.IsEnabled=!value&&!completed && (choices.SelectedItem as SandboxBindingRow)?.Candidate.CanBind==true; }
        void ShowManual()
        {
            manualMode=true; choices.Visibility=detail.Visibility=manual.Visibility=Visibility.Visible;
            match.Visibility=automatic.Visibility=wrong.Visibility=Visibility.Collapsed;
            PresentSelection(); choices.Focus();
        }
        async Task Refresh()
        {
            Working(true); L.Text(state,"sandbox.repair.scanning");
            try {
                var candidates=fixture ?? await Task.Run(()=>DailySandboxRepair.InspectAsync(account,lifetime.Token));
                choices.ItemsSource=candidates.Select(c=>new SandboxBindingRow(c,c.Box+(c.CanBind ? "" : " · "+L.Get(c.Issue)))).ToArray();
                var eligible=candidates.Where(c=>c.CanBind).ToArray(); matched=eligible.Length==1?eligible[0]:null;
                choices.SelectedIndex=matched==null?-1:Array.IndexOf(candidates,matched);
                match.Text=matched?.Box??"";
                L.Text(state,matched!=null?"sandbox.repair.verified":candidates.Length==0?"sandbox.repair.empty":"sandbox.repair.choose_hint");
                if(matched==null)ShowManual();
            } catch(OperationCanceledException) { choices.ItemsSource=null; matched=null; } catch(Exception error) { choices.ItemsSource=null; matched=null; match.Text=""; state.Text=DailyUserText.Error(error,L.Translate); }
            finally { Working(false); PresentSelection(); }
        }
        async Task Repair(string? box)
        {
            if(working || completed || fixture != null || box==null)return;
            if(manualMode && DailyDialogs.ShowModal(DailyDialogs.Message(window,L.Get("sandbox.repair.title"),L.Get("sandbox.repair.confirm",name,box),true,safeDefault:true))!=true)return;
            Working(true);
            try { string selected=await Task.Run(()=>DailySandboxRepair.RepairAsync(account,box,lifetime.Token)); completed=true; L.Text(state,"sandbox.repair.done",selected); L.Text(ProgressText,"sandbox.repair.done",selected); actions.Children.Clear(); var done=new Button { IsDefault=true, Style=(Style)FindResource("PrimaryButton") };L.Bind(done,ContentControl.ContentProperty,"common.close");done.Click+=(_,_)=>window.Close();actions.Children.Add(done); }
            catch(OperationCanceledException) { } catch(Exception error) { state.Text=DailyUserText.Error(error,L.Translate); }
            finally { Working(false); }
        }
        choices.SelectionChanged+=(_,_)=>PresentSelection(); refresh.Click+=async(_,_)=>await Refresh();
        wrong.Click+=(_,_)=>ShowManual();
        automatic.Click+=async(_,_)=>await Repair(matched?.Box); manual.Click+=async(_,_)=>await Repair((choices.SelectedItem as SandboxBindingRow)?.Candidate.Box);
        window.Loaded+=async(_,_)=>await Refresh(); DailyDialogs.Prepare(window); return window;
    }
    private sealed record SandboxBindingRow(DailySandboxCandidate Candidate,string Label);
}

using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

internal sealed partial class SandboxManagementWindow
{
    private sealed record BindingRow(DailySandboxCandidate Candidate,string Label);
    private readonly DailySandboxCandidate[]? bindingFixture;
    private readonly StackPanel bindingPanel=new(){Name="SandboxBindingPanel",Visibility=Visibility.Collapsed,Margin=new(0,16,0,0)};
    private readonly TextBlock match=new(){Name="SandboxBindingMatch",FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};
    private readonly ComboBox choices=new(){Name="SandboxBindingChoice",MinWidth=260,DisplayMemberPath="Label",Visibility=Visibility.Collapsed};
    private readonly TextBlock bindingStatus=new(){Name="SandboxBindingStatus",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12)};
    private readonly Button automatic=new(){Name="SandboxBindingAuto",Margin=new(0,0,8,0)},manual=new(){Name="SandboxBindingManual",Visibility=Visibility.Collapsed},wrong=new(){Name="SandboxBindingWrong"};
    private DailySandboxCandidate? matched;
    private Task<DailySandboxCandidate[]>? bindingInspection;
    private bool manualMode;
    private StackPanel BuildBindingPanel()
    {
        var title=new TextBlock{FontWeight=FontWeights.SemiBold};L.Text(title,"sandbox.repair.title");bindingPanel.Children.Add(title);
        bindingPanel.Children.Add(match);bindingPanel.Children.Add(choices);bindingPanel.Children.Add(bindingStatus);
        var actions=new WrapPanel();bindingPanel.Children.Add(actions);
        foreach(var pair in new[]{(automatic,"sandbox.repair.use_match"),(manual,"sandbox.repair.bind"),(wrong,"sandbox.repair.not_this")}) {
            L.Bind(pair.Item1,ContentControl.ContentProperty,pair.Item2);actions.Children.Add(pair.Item1);
        }
        automatic.Style=manual.Style=(Style)Application.Current.FindResource("PrimaryButton");
        L.Bind(choices,System.Windows.Automation.AutomationProperties.NameProperty,"sandbox.repair.choose");
        choices.SelectionChanged+=(_,_)=>PresentBinding();
        wrong.Click+=(_,_)=>ShowManualBinding();
        automatic.Click+=async(_,_)=>await BindAsync(matched?.Box);
        manual.Click+=async(_,_)=>await BindAsync((choices.SelectedItem as BindingRow)?.Candidate.Box);
        return bindingPanel;
    }
    private void UpdateBindingActions()
    {
        choices.IsEnabled=wrong.IsEnabled=!Working;
        automatic.IsEnabled=!Working&&matched!=null;
        manual.IsEnabled=!Working&&(choices.SelectedItem as BindingRow)?.Candidate.CanBind==true;
    }
    private void PresentBinding()
    {
        if(manualMode&&(choices.SelectedItem as BindingRow)?.Candidate is{} selected)
            L.Text(bindingStatus,selected.CanBind?"sandbox.repair.verified":selected.Issue);
        UpdateBindingActions();
    }
    private void ShowManualBinding()
    {
        manualMode=true;choices.Visibility=manual.Visibility=Visibility.Visible;
        match.Visibility=automatic.Visibility=wrong.Visibility=Visibility.Collapsed;PresentBinding();choices.Focus();
    }
    internal async Task ReadBindingsAsync()
    {
        if(Working||closed)return;reading=true;bindingPanel.Visibility=Visibility.Visible;UpdateActions();L.Text(bindingStatus,"sandbox.repair.scanning");
        try {
            DailySandboxCandidate[] candidates;
            if(bindingFixture!=null)candidates=bindingFixture;
            else {
                bindingInspection??=Task.Run(()=>DailySandboxRepair.InspectAsync(account.AccountKey,lifetime.Token));
                candidates=await bindingInspection.WaitAsync(TimeSpan.FromSeconds(8),lifetime.Token);
            }
            if(closed)return;
            choices.ItemsSource=candidates.Select(c=>new BindingRow(c,c.Box+(c.CanBind?"":" · "+L.Get(c.Issue)))).ToArray();
            var eligible=candidates.Where(c=>c.CanBind).ToArray();matched=eligible.Length==1?eligible[0]:null;
            choices.SelectedIndex=matched==null?-1:Array.IndexOf(candidates,matched);match.Text=matched?.Box??"";
            manualMode=false;choices.Visibility=manual.Visibility=Visibility.Collapsed;match.Visibility=automatic.Visibility=wrong.Visibility=Visibility.Visible;
            L.Text(bindingStatus,matched!=null?"sandbox.repair.verified":candidates.Length==0?"sandbox.repair.empty":"sandbox.repair.choose_hint");
            if(matched==null)ShowManualBinding();
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested) { }
        catch(Exception error) { matched=null;choices.ItemsSource=null;if(!closed){if(error is TimeoutException)L.Text(bindingStatus,"sandbox.manage.slow");else bindingStatus.Text=DailyUserText.Error(error,L.Translate);}}
        finally{if(bindingInspection?.IsCompleted==true)bindingInspection=null;reading=false;if(!closed)UpdateActions();}
    }
    private async Task BindAsync(string? box)
    {
        if(box==null||fixture!=null||bindingFixture!=null)return;
        // The inline button confirms the selection. Core rechecks ownership,
        // idle aliases and shared-path rules immediately before binding.
        await Change(async()=>{await DailySandboxRepair.RepairAsync(account.AccountKey,box,lifetime.Token);},"sandbox.manage.bound");
    }
}

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
        SandboxLaunchButton.Visibility = SandboxRepairButton.Visibility = ConnectionGuideButton.Visibility = Visibility.Collapsed;
        LoginActions.Visibility = RecoverButton.Visibility = Visibility.Collapsed;
        SandboxHelpText.Visibility = Visibility.Visible;
        L.Text(SandboxHelpText, "sandbox.bound_help");
        ScheduleTab.Visibility = UpdatesTab.Visibility = Visibility.Collapsed;
        if (DailySandbox.Current is {} binding)
            L.Bind(this, TitleProperty, () => L.Get("sandbox.window_title", binding.Name));
    }
    private async void SandboxLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || IsSandboxWindow || Unavailable || Selected is not { Account.Valid:true } row) return;
        if (ReadDesktopBehavior()?.HideIsolationReminder != true && !ConfirmIsolation()) return;
        if (!await softwareSandboxPath.EnsureReadyAsync() || finalClose || Unavailable) return;
        SuggestGamePreferences(row.Account);
        if(finalClose||Unavailable)return;
        // Launch follows the highlighted account, independent of queue checkboxes.
        await OperateAsync(async () => {
            await DailySandbox.LaunchAsync(row.Account,Environment.ProcessPath!,message=>Dispatcher.Invoke(()=>ProgressText.Text=L.Translate(message)),CancellationToken.None);
        });
    }
    private void SuggestGamePreferences(params DailyAccount[] accounts)
    {
        if(smoke!=null||IsSandboxWindow)return;
        try {
            var pending=accounts.Where(a=>DailySandboxPreferenceTransfer.ShouldSuggest(a.AccountKey)).ToArray();
            if(pending.Length==0)return;
            DailyDialogs.ShowModal(new GamePreferenceSyncWindow(this,pending[0],firstUse:true,choices:pending));
            foreach(var account in pending)DailySandboxPreferenceTransfer.DismissSuggestion(account.AccountKey);
        }catch(Exception error){ShowError(error);} // An optional suggestion must never prevent launching.
    }
    internal Window IsolationReminderWindow(Action<bool> accepted)
    {
        var body=new StackPanel { Margin=new(24,8,24,24) };
        var description=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,20) };L.Text(description,"sandbox.launch_description");body.Children.Add(description);
        var remember=new CheckBox { Name="IsolationRemember",Margin=new(0,0,0,20) };L.Bind(remember,ContentControl.ContentProperty,"sandbox.remember");body.Children.Add(remember);
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { IsCancel=true,Margin=new(0,0,8,0) };L.Bind(cancel,ContentControl.ContentProperty,"common.cancel");actions.Children.Add(cancel);
        var open=new Button { Name="IsolationConfirm",Style=(Style)FindResource("PrimaryButton"),IsDefault=true };L.Bind(open,ContentControl.ContentProperty,"sandbox.launch");actions.Children.Add(open);body.Children.Add(actions);
        var dialog=new Window { Owner=this,Width=540,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=body };
        L.Bind(dialog,TitleProperty,"sandbox.launch");
        open.Click+=(_,_)=>{accepted(remember.IsChecked==true);dialog.DialogResult=true;};
        cancel.Click+=(_,_)=>dialog.Close();DailyDialogs.Prepare(dialog);return dialog;
    }
    private bool ConfirmIsolation()
    {
        bool remember=false;
        if(DailyDialogs.ShowModal(IsolationReminderWindow(value=>remember=value))!=true)return false;
        if(remember)try{(DailyDesktopBehavior.Read(root) with {HideIsolationReminder=true}).Save(root);}catch(Exception error){ShowError(error);return false;}
        return true;
    }
    private async void SandboxRepair_Click(object sender, RoutedEventArgs e)
    {
        if (smoke != null || IsSandboxWindow || Unavailable || Selected is not { Account.Valid:true } row) return;
        // Paint first. Read-only management neither prepares a connection nor
        // refreshes all task/tool panels through SetBusy. The modal owns input.
        try
        {
            await PauseAutomaticMaintenanceAsync();
            if(finalClose || Unavailable)return;
            var window=new SandboxManagementWindow(this,row.Account);
            DailyDialogs.ShowModal(window);
            if(window.OpenSoftwareSettings)WorkspaceTabs.SelectedItem=SoftwareSettingsTab;
        }
        catch(Exception error){ShowError(error);}
    }
}

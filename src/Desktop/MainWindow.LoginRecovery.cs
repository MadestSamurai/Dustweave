using System.Windows;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async void Relogin_Click(object sender, RoutedEventArgs e)
    {
        if (Unavailable || IsSandboxWindow || Selected is not { Account.Valid: true } row) return;
        if (!Confirm(L.Get("account.relogin.confirm", row.Name))) return;
        await OperateAsync(() => { sessions.BeginLoginRecovery(row.Account.SlotNumber, row.Account.AccountKey); return Task.CompletedTask; }, connectsGame: true);
    }
    private void CancelLoginRecovery_Click(object sender, RoutedEventArgs e)
        => Manage(sessions.CancelLoginRecovery);

    private async void FinishLoginRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (Unavailable || IsSandboxWindow || sessions.LoginRecovery == null) return;
        bool isolated = LoginRecoveryIsolation.IsChecked == true;
        if (isolated && (!await softwareSandboxPath.EnsureReadyAsync() || finalClose || Unavailable)) return;
        await OperateAsync(async () =>
        {
            var recovered = sessions.LoginRecovery ?? throw new InvalidOperationException("account.relogin.no_pending");
            RefreshAccounts();
            var account = catalog!.Accounts.First(a => a.Valid && a.AccountKey == recovered.Account);
            if (isolated)
            {
                sessions.FinishLoginRecovery();
                await DailySandbox.LaunchAsync(account, Environment.ProcessPath!, message => Dispatcher.Invoke(() => ProgressText.Text = L.Translate(message)), CancellationToken.None);
            }
            else
                await coordinator.InspectRecoveredAsync(account);
        }, connectsGame: true);
    }
}

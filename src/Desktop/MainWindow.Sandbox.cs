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
        SandboxLaunchButton.Visibility = Visibility.Collapsed;
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
}

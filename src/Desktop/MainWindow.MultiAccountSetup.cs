using System.IO;
using System.Windows;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private static int SavedAccountCount(DailyAccountCatalog value) => value.Accounts.Where(a => a.Valid).DistinctBy(a => a.AccountKey).Count();
    private void PresentMultiAccountSetup()
    {
        var installation = new SandboxInstallation(smoke == null ? null : Path.Combine(root, "sandbox-path-preview"));
        DailyDialogs.ShowModal(new MultiAccountSetupWindow(this, root, installation));
        parallelOptions = DailyJson.TryRead<DailyParallelOptions>(Path.Combine(root, "parallel-options.json")) ?? new();
        parallelOptions.Validate(); RefreshParallelMode();
    }
    private async Task ShowMultiAccountSetupAsync()
    {
        if (Unavailable || IsSandboxWindow || catalog == null || !DailyMultiAccountSetup.Pending(root, SavedAccountCount(catalog))) return;
        onboardingOpen = true; SetBusy(busy);
        try
        {
            await PauseAutomaticMaintenanceAsync();
            if (!finalClose && !UpdateRestarting) PresentMultiAccountSetup();
        }
        catch (Exception error) { ShowError(error); }
        finally { onboardingOpen = false; SetBusy(busy); }
    }
}

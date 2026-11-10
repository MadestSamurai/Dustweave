using System.Windows;
namespace Dustweave.Desktop;
public partial class MainWindow
{
    private bool resourceReviewReady, resourceReviewChecked;
    private void OfferResourceReview()
    {
        if (!resourceReviewReady || resourceReviewChecked || smoke != null || IsSandboxWindow || ScheduledStartup || DirectedStartup ||
            CaptureSessionId.Length > 0 || finalClose || !IsVisible || WindowState == WindowState.Minimized || Unavailable ||
            updateNotice != null || updateChecking || scheduleChecking || catalog == null) return;
        var accounts = catalog.Accounts.Where(a => a.Valid).ToArray();
        if (accounts.Length == 0) return;
        var store = new DailyResourceReview(root);
        if (!store.Needed) { resourceReviewChecked = true; return; }
        if (!preferencesPanel.SavePending()) return;
        resourceReviewChecked = true;
        try
        {
            var dialog = new DailyResourceReviewWindow(root, accounts, TaskAccountKey) { Owner = this, BeforeSave = preferencesPanel.SavePending };
            dialog.Closed += (_, _) =>
            {
                if (!finalClose)
                {
                    try { store.Dismiss(); }
                    catch (Exception error) { StartupDiagnostics.Fail(error); }
                }
                if (dialog.Saved) { preferencesPanel.ReloadCurrent(); RefreshDailyHistory(refreshPlan: true); }
            };
            ShowUpdateNotice(dialog);
        }
        catch (Exception error) { StartupDiagnostics.Fail(error); }
    }
}

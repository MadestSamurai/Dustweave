using System.Windows;
namespace Dustweave.Desktop;
public partial class MainWindow
{
    private bool resourceReviewReady, resourceReviewChecked;
    private async Task OfferResourceReviewAsync()
    {
        if (!resourceReviewReady || resourceReviewChecked || smoke != null || IsSandboxWindow || !DailyResourceReview.InteractiveStartup(ScheduledStartup, DirectedStartup, UpdatedStartup) ||
            CaptureSessionId.Length > 0 || finalClose || !IsVisible || WindowState == WindowState.Minimized || Unavailable ||
            updateNotice != null || updateChecking || scheduleChecking || catalog == null) return;
        var accounts = catalog.Accounts.Where(a => a.Valid).ToArray();
        if (accounts.Length == 0) return;
        var store = new DailyResourceReview(root);
        if (!store.Needed) { resourceReviewChecked = true; return; }
        if (!preferencesPanel.SavePending()) return;
        await ShowResourceReviewAsync(accounts, store);
    }
    private async Task ShowResourceReviewAsync(DailyAccount[] accounts, DailyResourceReview store)
    {
        try
        {
            var dialog = new DailyResourceReviewWindow(root, accounts, TaskAccountKey) { Owner = this, BeforeSave = preferencesPanel.SavePending };
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) =>
            {
                try
                {
                    // Replacement by an update notice or shutdown is not consent.
                    if (dialog.Acknowledged) store.Dismiss();
                    if (dialog.Saved && !finalClose) { preferencesPanel.ReloadCurrent(); RefreshDailyHistory(refreshPlan: true); }
                }
                catch (Exception error) { StartupDiagnostics.Fail(error); }
                finally { completed.TrySetResult(); }
            };
            ShowUpdateNotice(dialog);
            resourceReviewChecked = true;
            await completed.Task;
        }
        catch (Exception error) { StartupDiagnostics.Fail(error); }
    }
    private async Task CheckResourceReviewLifecycleForSmoke(DailyAccount[] accounts)
    {
        if (smoke == null) throw new InvalidOperationException("Synthetic review requires smoke isolation");
        var store = new DailyResourceReview(root);
        var pending = ShowResourceReviewAsync(accounts, store);
        if (updateNotice is not DailyResourceReviewWindow || pending.IsCompleted) throw new Exception("Review must be visible before auto-run can continue");
        CloseUpdateNotice(); await pending;
        if (!store.Needed) throw new Exception("A replaced notice was incorrectly marked as acknowledged");
        foreach(var account in accounts) new DailyPreferenceStore(root).Save(account.AccountKey,new());
        pending = ShowResourceReviewAsync(accounts, store);
        if (updateNotice is not DailyResourceReviewWindow review) throw new Exception("Unconfirmed review cannot reopen");
        review.VerifyForSmoke(); await pending;
        if (store.Needed || updateNotice != null) throw new Exception("Confirmed review must persist and release startup continuation");
    }
}

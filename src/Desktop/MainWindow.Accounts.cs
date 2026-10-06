using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private void UpdateAccountPresentation()
    {
        if (catalog == null || SaveAccountButton == null) return;
        bool idle = !Unavailable;
        bool closed = !catalog.GameRunning && !catalog.StarterRunning;
        bool identified = catalog.SessionComplete && DailyProfiles.ValidKey(catalog.CurrentKey);
        var current = catalog.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == catalog.CurrentKey);
        bool hasSlot = catalog.OccupiedSlots.Concat(catalog.Accounts.Select(a => a.SlotNumber)).Distinct().Count() < 100;
        string hint = !closed ? "account.login_running" :
            !identified ? "account.login_needed" :
            current == null ? "account.login_unsaved" : "account.login_saved";
        L.Bind(LoginAccountText, TextBlock.TextProperty, () => current?.Name ?? L.Get(identified ? "account.unsaved" : "account.not_signed_in"));
        L.Text(LoginHintText, hint);
        L.Bind(SaveAccountButton, ContentControl.ContentProperty, current == null ? "account.save" : "account.update_saved");
        SaveAccountButton.IsEnabled = idle && closed && identified && (current != null || hasSlot);
        LoginNewButton.IsEnabled = idle && closed && hasSlot && (!catalog.SessionComplete || current != null);
        RecoverButton.IsEnabled = idle && closed && catalog.HasRecovery;
        L.Bind(SaveAccountButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            !identified ? "account.login_needed" : current != null ? "account.update_help" : hasSlot ? "account.save_help" : "account.slots_full");
        L.Bind(LoginNewButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            !hasSlot ? "account.slots_full" : catalog.SessionComplete && current == null ? "account.login_unsaved" : "account.new_help");
        L.Bind(RecoverButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            catalog.HasRecovery ? "account.restore_help" : "account.no_recovery");

        var row = Selected;
        // Editing actions only affect the highlighted row, never the checked queue.
        RenameAccountButton.IsEnabled = DeleteAccountButton.IsEnabled = idle && row?.Account.Valid == true;
        MoveUpButton.IsEnabled = idle && row != null && rows.IndexOf(row) > 0;
        MoveDownButton.IsEnabled = idle && row != null && rows.IndexOf(row) < rows.Count - 1;
        var visible = rows.Where(r => r.Account.Valid && MatchesSearch(r)).ToArray();
        SelectAll.IsEnabled = idle && visible.Length > 0;
        SelectAll.IsChecked = visible.Length == 0 || visible.All(r => !r.Selected) ? false :
            visible.All(r => r.Selected) ? true : null;
        EmptyText.Visibility = rows.Any(MatchesSearch) ? Visibility.Collapsed : Visibility.Visible;
        L.Text(EmptyText, rows.Count == 0 ? "account.empty" : "account.no_matches");
    }
}

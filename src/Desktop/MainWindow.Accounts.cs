using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private bool refreshingAccountPresets;
    private static TaskPresetChoice[] PresetOptions(DailyTaskPresetData data) => new[] { new TaskPresetChoice("", "") }.Concat(data.Presets.Select(p => new TaskPresetChoice(p.Id, p.Name))).ToArray();
    private void RefreshAccountPresets()
    {
        var data = new DailyTaskPresetStore(root).Read();
        refreshingAccountPresets = true;
        try { foreach (var row in rows) { row.PresetOptions = PresetOptions(data); row.PresetId = data.Accounts.GetValueOrDefault(row.Account.AccountKey, ""); row.RefreshLanguage(); } }
        finally { refreshingAccountPresets = false; }
    }
    private void UpdateAccountPresentation()
    {
        if (catalog == null || SaveAccountButton == null) return;
        bool idle = !Unavailable;
        RunOnOpen.IsEnabled = idle && !IsSandboxWindow;
        bool closed = !catalog.GameRunning && !catalog.StarterRunning;
        bool identified = catalog.SessionComplete && DailyProfiles.ValidKey(catalog.CurrentKey);
        var current = catalog.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == catalog.CurrentKey);
        bool recoverable = catalog.Accounts.Count(a => !a.Valid && a.AccountKey == catalog.CurrentKey) == 1;
        bool hasSlot = catalog.OccupiedSlots.Concat(catalog.Accounts.Select(a => a.SlotNumber)).Distinct().Count() < 100;
        string hint = !closed ? "account.login_running" :
            !identified ? "account.login_needed" :
            current == null ? "account.login_unsaved" :
            !IsSandboxWindow && sessions.LoginRecovery == null && SavedAccountCount(catalog) == 1 ? "multiaccount.add_second" : "account.login_saved";
        L.Bind(LoginAccountText, TextBlock.TextProperty, () => current?.Name ?? L.Get(identified ? "account.unsaved" : "account.not_signed_in"));
        L.Text(LoginHintText, hint);
        // Once an account is saved, Getting started is a reference rather than the next step: keep it reachable but quiet.
        var guideStyle = (Style)FindResource(catalog.Accounts.Any(a => a.Valid) ? "QuietButton" : typeof(Button));
        if (!ReferenceEquals(ConnectionGuideButton.Style, guideStyle)) ConnectionGuideButton.Style = guideStyle;
        // While a sign-in recovery is pending its numbered steps replace the ordinary hint, so only one instruction shows.
        LoginHintText.Visibility = !IsSandboxWindow && sessions.LoginRecovery != null ? Visibility.Collapsed : Visibility.Visible;
        L.Bind(SaveAccountButton, ContentControl.ContentProperty, current == null ? "account.save" : "account.update_saved");
        SaveAccountButton.IsEnabled = idle && closed && identified && (current != null || recoverable || hasSlot);
        LoginNewButton.IsEnabled = idle && closed && hasSlot && (!catalog.SessionComplete || current != null);
        RecoverButton.IsEnabled = idle && closed && catalog.HasRecovery;
        L.Bind(SaveAccountButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            !identified ? "account.login_needed" : recoverable ? "account.recover_credentials" : current != null ? "account.update_help" : hasSlot ? "account.save_help" : "account.slots_full");
        L.Bind(LoginNewButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            !hasSlot ? "account.slots_full" : catalog.SessionComplete && current == null ? "account.login_unsaved" : "account.new_help");
        L.Bind(RecoverButton, FrameworkElement.ToolTipProperty, !closed ? "account.login_running" :
            catalog.HasRecovery ? "account.restore_help" : "account.no_recovery");

        idle &= !IsSandboxWindow;
        if (IsSandboxWindow) SaveAccountButton.IsEnabled = LoginNewButton.IsEnabled = RecoverButton.IsEnabled = false;
        var row = Selected;
        SandboxRepairItem.IsEnabled = idle && row?.Account.Valid == true;
        SandboxLaunchItem.IsEnabled = idle && row?.Account.Valid == true;
        // Editing actions only affect the highlighted row, never the checked queue.
        RenameAccountItem.IsEnabled = idle && row?.Account.Valid == true;
        DeleteAccountItem.IsEnabled = idle && row != null &&
            (row.Account.Valid || row.Account.UnreadableRevision.Length == 64);
        MoveUpItem.IsEnabled = idle && row != null && rows.IndexOf(row) > 0;
        MoveDownItem.IsEnabled = idle && row != null && rows.IndexOf(row) < rows.Count - 1;
        ReloginItem.Visibility = IsSandboxWindow ? Visibility.Collapsed : Visibility.Visible;
        SessionActionsSeparator.Visibility = ReloginItem.Visibility; // sandbox windows hide the whole session group
        ReloginItem.IsEnabled = idle && closed && row?.Account.Valid == true;
        var recovery = IsSandboxWindow ? null : sessions.LoginRecovery;
        LoginRecoveryPanel.Visibility = recovery == null ? Visibility.Collapsed : Visibility.Visible;
        if (recovery != null)
        {
            L.Text(LoginRecoveryText, "account.relogin.pending", recovery.Name);
            FinishLoginRecoveryButton.IsEnabled = idle && closed && identified && catalog.CurrentKey == recovery.Account;
            CancelLoginRecoveryButton.IsEnabled = idle;
            LoginRecoveryIsolation.IsEnabled = idle;
            SaveAccountButton.IsEnabled = LoginNewButton.IsEnabled = false;
            if (closed && identified && catalog.CurrentKey != recovery.Account)
                L.Text(LoginRecoveryText, "account.relogin.pending_wrong", recovery.Name);
        }
        var visible = rows.Where(r => r.Account.Valid && MatchesSearch(r)).ToArray();
        SelectAll.IsEnabled = idle && visible.Length > 0;
        SelectAll.IsChecked = visible.Length == 0 || visible.All(r => !r.Selected) ? false :
            visible.All(r => r.Selected) ? true : null;
        EmptyText.Visibility = rows.Any(MatchesSearch) ? Visibility.Collapsed : Visibility.Visible;
        L.Text(EmptyText, rows.Count == 0 ? "account.empty" : "account.no_matches");
    }

    private void DeleteAccount(DailyAccount account, Func<string, bool> confirm)
    {
        if (!confirm(account.Valid ? L.Get("account.delete_confirm", account.Name) :
            L.Get("account.delete_unreadable_confirm", account.Name, account.SlotNumber))) return;
        if (account.Valid) sessions.Delete(account.SlotNumber, account.AccountKey);
        else sessions.DeleteUnreadable(account.SlotNumber, account.UnreadableRevision);
    }

    // Row-owned management menu. Every item still acts on the highlighted row through the existing handlers, so the
    // ··· button highlights its own row before opening; it never changes the checked daily queue.
    private bool accountMenuFromRow;
    private void AccountActions_Click(object sender, RoutedEventArgs e)
    {
        if (Unavailable || sender is not Button { DataContext: AccountRow row } button) return;
        if (!ReferenceEquals(AccountsGrid.SelectedItem, row)) AccountsGrid.SelectedItem = row;
        AccountActionsMenu.PlacementTarget = button;
        AccountActionsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        AccountActionsMenu.CustomPopupPlacementCallback = (popup, target, _) =>
        [
            new(new Point(target.Width - popup.Width, target.Height + 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical),
            new(new Point(target.Width - popup.Width, -popup.Height - 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical)
        ];
        button.SetResourceReference(Control.BackgroundProperty, "PrimarySoft");
        AccountActionsMenu.Closed += ResetAccountMenu;
        AccountActionsMenu.IsOpen = true;
        void ResetAccountMenu(object? _, RoutedEventArgs __)
        {
            AccountActionsMenu.Closed -= ResetAccountMenu;
            button.ClearValue(Control.BackgroundProperty);
            // Right-click and the menu key then use the context-menu service's own placement again.
            AccountActionsMenu.ClearValue(ContextMenu.PlacementTargetProperty);
            AccountActionsMenu.ClearValue(ContextMenu.PlacementProperty);
            AccountActionsMenu.ClearValue(ContextMenu.CustomPopupPlacementCallbackProperty);
        }
    }
    private void AccountsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Right-clicking a row highlights it first, matching Explorer-style lists.
        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not DataGridRow)
            node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        accountMenuFromRow = node is DataGridRow { Item: AccountRow };
        if (node is DataGridRow { Item: AccountRow row } && !ReferenceEquals(AccountsGrid.SelectedItem, row)) AccountsGrid.SelectedItem = row;
    }
    private void AccountsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        bool keyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        // Headers and empty space have no account; the menu key uses the highlighted row.
        if (Unavailable || Selected == null || !keyboard && !accountMenuFromRow) e.Handled = true;
        accountMenuFromRow = false;
    }
    private void AccountsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Alt+Up/Down reorders the highlighted account without reopening the menu; open dropdowns keep their own Alt+arrows.
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Alt
            || System.Windows.Input.Keyboard.FocusedElement is ComboBox or ComboBoxItem) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var item = key == System.Windows.Input.Key.Up ? MoveUpItem : key == System.Windows.Input.Key.Down ? MoveDownItem : null;
        if (item == null) return;
        e.Handled = true;
        if (!item.IsEnabled) return;
        if (item == MoveUpItem) Up_Click(item, e); else Down_Click(item, e);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (Selected is not { } moved) return;
            AccountsGrid.ScrollIntoView(moved);
            (AccountsGrid.ItemContainerGenerator.ContainerFromItem(moved) as DataGridRow)?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckAccountsForSmoke()
    {
        if (sessions is not DemoEnvironment fixture) throw new Exception("Account UI checks require isolated sessions.");
        var before = fixture.Read();
        var saved = profiles.Read();
        int calls = fixture.Calls.Count, checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        try
        {
            WorkspaceTabs.SelectedItem = AccountsTab;
            Width = 1180; Height = 800;
            RefreshAccounts(before);
            foreach (int language in new[] { 0, 1, 2 })
            {
                LanguageSelector.SelectedIndex = language;
                RefreshAccounts(before);
                Check(LoginAccountText.Text == before.Accounts[0].Name && LoginHintText.Text == L.Get("account.login_running"),
                    "Current-login card lost its account or running-state explanation.");
                Check(!SaveAccountButton.IsEnabled && !LoginNewButton.IsEnabled && !RecoverButton.IsEnabled,
                    "Running game permits login-session mutation.");
                RefreshAccounts(before with { GameRunning = false, StarterRunning = false });
                Check(SaveAccountButton.IsEnabled && LoginNewButton.IsEnabled && RecoverButton.IsEnabled &&
                    (string)SaveAccountButton.Content == L.Get("account.update_saved"),
                    "Saved account cannot be updated after exiting the game.");
                RefreshAccounts(before with { GameRunning = false, CurrentKey = new string('a', 64), CurrentSlot = null });
                Check(SaveAccountButton.IsEnabled && !LoginNewButton.IsEnabled && LoginHintText.Text == L.Get("account.login_unsaved"),
                    "Unsaved current account can be overwritten by signing in to another.");
                RefreshAccounts(before with { GameRunning = false, SessionComplete = false, CurrentKey = "", CurrentSlot = null, HasRecovery = false });
                Check(!SaveAccountButton.IsEnabled && LoginNewButton.IsEnabled && !RecoverButton.IsEnabled &&
                    LoginAccountText.Text == L.Get("account.not_signed_in"), "Missing sign-in or backup offers an invalid action.");
                RefreshAccounts(before);
                AccountsGrid.SelectedItem = rows[1];
                Check(!ReloginButton.IsEnabled, "Manual sign-in recovery must not replace a running game's login.");
                var selectedAccount = rows[1].Account;
                RefreshAccounts(before with { GameRunning = false });
                Check(ReloginButton.IsEnabled, "Closed game must offer a recovery route independent of sandbox export.");
                fixture.LoginRecovery = new(selectedAccount.SlotNumber, selectedAccount.AccountKey, selectedAccount.Name, new string('c', 64), DateTimeOffset.UtcNow);
                RefreshAccounts(before with { GameRunning = false, CurrentKey = selectedAccount.AccountKey });
                Check(LoginRecoveryPanel.IsVisible && FinishLoginRecoveryButton.IsEnabled && !SaveAccountButton.IsEnabled && !LoginNewButton.IsEnabled,
                    "Recovery must expose the selected-account save flow without an ambiguous second save action.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("accounts-login-recovery-" + L.Code);
                RefreshAccounts(before with { GameRunning = false, CurrentKey = new string('f', 64) });
                Check(!FinishLoginRecoveryButton.IsEnabled && LoginRecoveryText.Text == L.Get("account.relogin.pending_wrong", selectedAccount.Name),
                    "Wrong-account recovery must explain why saving is unavailable.");
                RefreshAccounts(before with { GameRunning = true, CurrentKey = selectedAccount.AccountKey });
                Check(!FinishLoginRecoveryButton.IsEnabled, "Recovery must wait for normal game exit before saving.");
                fixture.LoginRecovery = null;
                RefreshAccounts(before);
                AccountsGrid.SelectedItem = rows[1];
                Check(SandboxRepairButton.IsEnabled && SandboxRepairButton.Visibility == Visibility.Visible && (string)SandboxRepairButton.Content == L.Get("sandbox.manage.title"), "Isolated launch must follow the highlighted saved account in every language.");
                var selections = rows.Select(r => r.Selected).ToArray();
                AccountsGrid.SelectedItem = rows[2];
                Check(rows.Select(r => r.Selected).SequenceEqual(selections), "Highlighting an account changed its daily queue selection.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var enter = Descendants<Button>(AccountsGrid).Where(b => b.Name == "EnterAccountButton").ToArray();
                Check(enter.Length == 3 && enter.All(b => b.DataContext is AccountRow row &&
                    new ButtonAutomationPeer(b).GetName() == L.Get("account.enter_named", row.Name)),
                    "Row entry actions are missing or do not identify their own account.");

                var unreadable = before.Accounts[1] with { Valid = false, UnreadableRevision = new string('a', 64), Error = "DPAPI" };
                RefreshAccounts(before with { Accounts = [before.Accounts[0], unreadable, before.Accounts[2]],
                    SessionComplete = false, CurrentKey = "", CurrentSlot = null, GameRunning = false });
                AccountsGrid.SelectedItem = rows.Single(r => r.Account.SlotNumber == unreadable.SlotNumber);
                Check(DeleteAccountButton.IsEnabled && !RenameAccountButton.IsEnabled && !SandboxLaunchButton.IsEnabled &&
                    !SandboxRepairButton.IsEnabled, "Unreadable accounts must be removable without permitting launch or credential editing.");
                int deleteCalls = fixture.Calls.Count;
                DeleteAccount(unreadable, text => {
                    Check(text == L.Get("account.delete_unreadable_confirm", unreadable.Name, unreadable.SlotNumber),
                        "Unreadable removal must explain the selected record and local-only scope in each language.");
                    return false;
                });
                Check(fixture.Calls.Count == deleteCalls, "Cancelling unreadable removal invoked a session command.");
                DeleteAccount(unreadable, _ => true);
                Check(fixture.Calls.Skip(deleteCalls).SequenceEqual(new[] { "delete-unreadable:" + unreadable.SlotNumber + ":" + unreadable.UnreadableRevision }),
                    "Confirmed unreadable removal used the healthy-account identity path or lost the selected record revision.");
                fixture.Calls.RemoveRange(deleteCalls, fixture.Calls.Count - deleteCalls);
                SetBusy(true);
                Check(!DeleteAccountButton.IsEnabled, "Unreadable removal must stay locked during automation.");
                SetBusy(false);
                RefreshAccounts(before);
            }

            LanguageSelector.SelectedIndex = 0;
            foreach (var row in rows) row.Selected = false;
            rows[1].Selected = true;
            Check(SelectAll.IsChecked == null, "Partial daily selection has no mixed select-all state.");
            Search.Text = rows[0].Name;
            SelectAll.IsChecked = true;
            SelectAll_Click(SelectAll, new RoutedEventArgs());
            Check(rows[0].Selected && rows[1].Selected && !rows[2].Selected,
                "Filtered select-all changed hidden accounts.");
            SelectAll.IsChecked = false;
            SelectAll_Click(SelectAll, new RoutedEventArgs());
            Check(!rows[0].Selected && rows[1].Selected && !rows[2].Selected && RunAccountsButton.IsEnabled &&
                SelectionText.Text == L.Get("account.selected", 1), "Clearing filtered accounts lost a hidden checked account.");
            Search.Text = "no-such-account";
            Check(EmptyText.Visibility == Visibility.Visible && EmptyText.Text == L.Get("account.no_matches") &&
                !SelectAll.IsEnabled && !RenameAccountButton.IsEnabled, "Empty search retains a misleading action.");
            Capture("accounts-no-matches");
            Search.Clear();
            RefreshAccounts(before with { GameRunning = false });
            AccountsGrid.SelectedItem = rows[0];
            Check(!MoveUpButton.IsEnabled && MoveDownButton.IsEnabled, "First account offers invalid reorder controls.");
            AccountsGrid.SelectedItem = rows[^1];
            Check(MoveUpButton.IsEnabled && !MoveDownButton.IsEnabled, "Last account offers invalid reorder controls.");
            SetBusy(true);
            Check(!AccountsGrid.IsEnabled && !SaveAccountButton.IsEnabled && !LoginNewButton.IsEnabled &&
                !RunAccountsButton.IsEnabled && StopButton.IsVisible && StopButton.IsEnabled, "Busy account page is not safely stoppable.");
            SetBusy(false);
            RefreshAccounts(before with { GameRunning = false });
            Width = 1180; Height = 800;
            foreach (int themeIndex in new[] { 1, 2 })
            {
                ThemeSelector.SelectedIndex = themeIndex;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("accounts-" + (themeIndex == 1 ? "light" : "dark"));
            }
            // A custom row template must retain selection, keyboard focus and native scrolling.
            Width = 920; Height = 650;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            AccountsGrid.SelectedItem = rows[0];
            var firstRow = (DataGridRow)AccountsGrid.ItemContainerGenerator.ContainerFromItem(rows[0]);
            var lastRow = (DataGridRow)AccountsGrid.ItemContainerGenerator.ContainerFromItem(rows[2]);
            var firstSurface = (Border)firstRow.Template.FindName("RowSurface", firstRow);
            var lastSurface = (Border)lastRow.Template.FindName("RowSurface", lastRow);
            Check(firstSurface.Background == FindResource("PrimarySoft") && lastSurface.Background != FindResource("PrimarySoft"),
                "The account highlight is missing or applied to an unselected row.");
            var cell = Descendants<DataGridCell>(firstRow).First();
            cell.Focus();
            Check(firstRow.IsKeyboardFocusWithin && firstSurface.BorderBrush == FindResource("Focus"),
                "Custom account row lost keyboard focus visibility.");
            AccountsGrid.SelectedItem = rows[2];
            Check(lastSurface.Background == FindResource("PrimarySoft") && firstSurface.Background != FindResource("PrimarySoft"),
                "Moving selection left an old row highlighted.");
            Check(lastRow.TransformToAncestor(AccountsGrid).TransformBounds(new Rect(lastRow.RenderSize)).Bottom <= AccountsGrid.ActualHeight + 1,
                "Compact account list clips its third row.");
            var large = Enumerable.Range(1, 100).Select(i => new DailyAccount(i, $"Account {i}",
                DailyIdentity.MemberKey((9000 + i).ToString()), "***" + i, true, false, "")).ToArray();
            RefreshAccounts(before with { Accounts = large, CurrentKey = "", CurrentSlot = null, GameRunning = false });
            AccountsGrid.SelectedItem = rows[^1];
            AccountsGrid.ScrollIntoView(rows[^1]);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var endRow = AccountsGrid.ItemContainerGenerator.ContainerFromItem(rows[^1]) as DataGridRow;
            Check(endRow?.IsVisible == true && Descendants<Button>(endRow).Any(b => b.Name == "EnterAccountButton" &&
                b.DataContext is AccountRow r && r.Account.SlotNumber == 100), "Last account cannot be reached by scrolling.");
            Check(Descendants<DataGridRow>(AccountsGrid).Count() < 30, "Account row virtualization was lost.");
            RefreshAccounts(before);
            Width = 1180; Height = 800;
            RefreshAccounts(before with { Accounts = [], CurrentKey = "", CurrentSlot = null, SessionComplete = false, GameRunning = false, HasRecovery = false });
            Check(EmptyText.IsVisible && !RunAccountsButton.IsEnabled && !RenameAccountButton.IsEnabled &&
                !DeleteAccountButton.IsEnabled && !MoveUpButton.IsEnabled && !MoveDownButton.IsEnabled && !SandboxRepairButton.IsEnabled,
                "Empty account list exposes management actions.");
            Capture("accounts-empty");
            Check(fixture.Calls.Count == calls, "Account presentation issued a game or session command.");
            DailyJson.Write(Path.Combine(smoke!, "accounts-presentation.json"), new { status = "passed", checks,
                rowAndQueueSelectionSeparate = true, loginStatesVerified = true, filteredSelectionPreserved = true,
                realGameTouched = false });
        }
        finally
        {
            fixture.LoginRecovery = null;
            Search.Clear();
            DailyJson.Write(Path.Combine(root, "accounts.json"), saved);
            RefreshAccounts(before);
            Width = 1180; Height = 800; LanguageSelector.SelectedIndex = 0; ThemeSelector.SelectedIndex = 1;
            WorkspaceTabs.SelectedItem = RunTab;
        }
    }
}

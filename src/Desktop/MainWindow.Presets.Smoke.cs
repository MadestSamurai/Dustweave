using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dustweave.Desktop;
public partial class MainWindow
{
    private async Task CheckPresetsForSmoke()
    {
        var fixture = (DemoEnvironment)sessions;
        int calls = fixture.Calls.Count, checks = 0;
        string previousViewedAccount = viewedAccount;
        double previousWidth = Width, previousHeight = Height;
        int previousLanguage = LanguageSelector.SelectedIndex, previousTheme = ThemeSelector.SelectedIndex;
        object previousTab = WorkspaceTabs.SelectedItem;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        static IEnumerable<T> Find<T>(DependencyObject node) where T : DependencyObject {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var next in Find<T>(child)) yield return next;
            }
        }
        string a = fixture.Accounts[0].AccountKey, b = fixture.Accounts[1].AccountKey;
        var store = new DailyTaskPresetStore(root);
        var old = store.Read(); var preferences = new DailyPreferenceStore(root);
        var first = preferences.Read(a); var second = preferences.Read(b);
        try {
            WorkspaceTabs.SelectedItem = AccountsTab; RefreshAccounts();
            foreach (int language in new[] {0, 1, 2}) {
                LanguageSelector.SelectedIndex = language;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (var control in Find<ComboBox>(AccountsGrid).Where(c => c.SelectedValuePath == "Id")) {
                    var checkedRow = (AccountRow)control.DataContext;
                    Check(control.SelectedItem is TaskPresetChoice choice && choice.Id == checkedRow.PresetId && Find<TextBlock>(control).Any(t => t.Text == choice.Label), "Visible account preset lost its selection or localized label");
                }
            }
            RefreshAccounts(); WorkspaceTabs.SelectedItem = RunTab; ViewAccountTasks(a); dailyPanel.ShowCurrentPlan();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Find<Button>(dailyPanel).Count(b => b.IsVisible && b.Content?.ToString() == L.Get("presets.save")) == 1,
                "Daily task selection has duplicate save buttons");
            Check(!Find<Button>(dailyPanel).Any(b => b.IsVisible && b.ToolTip?.ToString() == L.Get("presets.manage")),
                "Empty presets expose a redundant management/save menu");
            var p = store.Save("Mail only", ["mail"]); var q = store.Save("Mirror + Mail", ["mirror", "mail"]);
            store.Assign(a, p.Id); store.Assign(b, q.Id); RefreshAccountPresets(); dailyPanel.RefreshPresets(true);
            Check(dailyPanel.SelectedPlanTasks.SequenceEqual(["mail"]), "Saved selection was not applied to the visible account");
            var selector = Find<ComboBox>(dailyPanel).Single(c => c.SelectedValuePath == "Id");
            selector.SelectedValue = q.Id;
            Check(store.Read().Accounts[a] == q.Id && dailyPanel.SelectedPlanTasks.ToHashSet().SetEquals(["mirror", "mail"]), "Daily dropdown does not apply and persist the selected preset");
            var manage = Find<Button>(dailyPanel).Single(b => b.ToolTip?.ToString() == L.Get("presets.manage"));
            manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(manage.ContextMenu.Items.OfType<MenuItem>().All(i => i.Header?.ToString() != L.Get("presets.save")),
                "Preset management repeats the visible save action");
            manage.ContextMenu.IsOpen = false;
            WorkspaceTabs.SelectedItem = AccountsTab;
            var row = rows.Single(r => r.Account.AccountKey == a); row.PresetId = p.Id;
            Check(store.Read().Accounts[a] == p.Id && store.Read().Accounts[b] == q.Id, "Account preset edit affected the wrong identity");
            RefreshAccountPresets();
            Check(store.Read().Accounts[a] == p.Id && store.Read().Accounts[b] == q.Id, "Binding refresh cleared account presets");
            preferencesPanel.CheckMirrorAccountForSmoke(a, b);
            WorkspaceTabs.SelectedItem = RunTab; ViewAccountTasks(b); WorkspaceTabs.SelectedItem = SettingsTab;
            preferencesPanel.SelectAccount(b);
            Check(preferences.Read(b).Mirror.Multiplier == 4 && preferences.Read(a).Mirror.Multiplier == 1, "Account scoped settings lost the requested mirror multiplier");
            store.Save(new string('长', 60), ["mail"]);
            void DeleteFromMenu(bool accept, string expectedName, bool capture = false)
            {
                manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var delete = manage.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == L.Get("presets.delete"));
                manage.ContextMenu.IsOpen = false;
                Dispatcher.BeginInvoke(new Action(() => {
                    var dialog = OwnedWindows.Cast<Window>().Single(w => w.Title == L.Get("presets.delete"));
                    dialog.UpdateLayout();
                    Check(Find<TextBlock>(dialog).Any(t => t.Text.Contains(expectedName, StringComparison.Ordinal)),
                        "Preset deletion confirmation does not name its selected target");
                    var confirm = Find<Button>(dialog).Single(button => button.Content?.ToString() == L.Get("presets.delete"));
                    Check(!confirm.IsDefault && Find<Button>(dialog).Any(button => button.IsCancel && button.IsDefault),
                        "Preset deletion should default to cancellation");
                    if (capture) Capture("presets-delete-" + L.Code + "-" + ThemeSelector.SelectedIndex, (FrameworkElement)dialog.Content);
                    if (accept) confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else dialog.Close();
                }), DispatcherPriority.ApplicationIdle);
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            foreach (int language in new[] {0, 1, 2}) foreach (int appearance in new[] {1, 2}) {
                LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance;
                Width = 1180; Height = 900; WorkspaceTabs.SelectedItem = AccountsTab;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Capture("presets-accounts-" + L.Code + "-" + appearance);
                var defaultSelector = Find<ComboBox>(AccountsGrid).Single(c => c.SelectedValuePath == "Id" && c.SelectedValue is string { Length: 0 });
                Check(Find<TextBlock>(defaultSelector).Any(t => t.Text == L.Get("presets.default")), "Account preset label did not follow the selected language");
                WorkspaceTabs.SelectedItem = RunTab; ViewAccountTasks(a); dailyPanel.ShowCurrentPlan();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); Capture("presets-tasks-" + L.Code + "-" + appearance);
                Check(dailyPanel.SelectedPlanTasks.SequenceEqual(["mail"]), "Language/theme refresh overwrote the preset selection");
                Activate();
                manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var menu = manage.ContextMenu;
                Check(menu.IsOpen && menu.Style == FindResource("ActionMenu") && menu.Background == FindResource("Surface"),
                    "Preset menu did not adopt the shared theme");
                Check(menu.Items.OfType<Separator>().Count() == 1 && menu.ActualWidth <= 360 && menu.ActualHeight <= 400,
                    "Preset menu grouping or long-name bounds are incorrect");
                var items = menu.Items.OfType<MenuItem>().ToArray();
                Check(items[0].Header?.ToString() == L.Get("presets.replace") && items.Where(i => i.Tag?.ToString() == "destructive").All(i => i.Foreground == FindResource("Error")),
                    "Preset actions lost their language or destructive styling");
                Capture("presets-menu-" + L.Code + "-" + appearance, menu);
                Check(items[0].Focus(), $"The themed menu item cannot receive keyboard focus: focusable={items[0].Focusable}, enabled={items[0].IsEnabled}, visible={items[0].IsVisible}, active={IsActive}");
                items[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu), 0, Key.Down)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Check(items[1].IsKeyboardFocused, "The themed menu lost arrow-key navigation");
                var menuClosed = new TaskCompletionSource();
                menu.Closed += (_, _) => menuClosed.TrySetResult();
                items[1].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu), 0, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                await menuClosed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(!menu.IsOpen && manage.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue,
                    $"Escape did not dismiss the menu and restore its trigger: open={menu.IsOpen}, background={manage.ReadLocalValue(Control.BackgroundProperty)}");
                DeleteFromMenu(false, p.Name, capture: true);
                Check(store.Read().Presets.Any(x => x.Id == p.Id) && store.Read().Accounts[a] == p.Id &&
                    dailyPanel.SelectedPlanTasks.SequenceEqual(["mail"]), "Cancelled deletion changed selected preset or tasks");
            }
            store.Assign(b, p.Id); RefreshAccountPresets();
            DeleteFromMenu(true, p.Name);
            Check(store.Read().Presets.All(x => x.Id != p.Id) && selector.SelectedValue?.ToString() == "" &&
                dailyPanel.SelectedPlanTasks.ToHashSet().SetEquals(DailyStageCatalog.Selectable.Where(s => s.Enabled(preferences.Read(a))).Select(s => s.Id)),
                "Deleting the current preset requires switching away or does not select all enabled tasks");
            Check(rows.Where(r => r.Account.AccountKey == a || r.Account.AccountKey == b).All(r =>
                r.PresetId == "" && r.SelectedPreset?.Label == L.Get("presets.default")),
                "Account preset selectors do not return to the default");
            ViewAccountTasks(b); dailyPanel.ShowCurrentPlan();
            Check(dailyPanel.SelectedPlanTasks.ToHashSet().SetEquals(DailyStageCatalog.Selectable.Where(s => s.Enabled(preferences.Read(b))).Select(s => s.Id)) &&
                new DailyTaskPresetStore(root).Selection(b, preferences.Read(b)) == null,
                "Switching account or restarting restores the deleted preset's stale selections");
            ViewAccountTasks(a); dailyPanel.ShowCurrentPlan();
            SetBusy(true); Check(!selector.IsEnabled, "Preset may change during queue execution"); SetBusy(false);
            Width = 920; Height = 650; UpdateLayout(); dailyPanel.CheckViewportForSmoke(); Capture("presets-compact");
            LanguageSelector.SelectedIndex = 0; ThemeSelector.SelectedIndex = 2;
            Dispatcher.BeginInvoke(new Action(() => {
                var dialog = OwnedWindows.Cast<Window>().Single(w => w.Title == L.Get("desktop.close_title"));
                dialog.UpdateLayout(); Capture("close-choice-dark", (FrameworkElement)dialog.Content);
                Find<CheckBox>(dialog).Single().IsChecked = true;
                Find<Button>(dialog).Single(x => x.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }), DispatcherPriority.ApplicationIdle);
            Check(ChooseCloseAction() == "tray" && DailyDesktopBehavior.Read(root).CloseAction == "tray", "Close choice failed to persist remembered tray action");
            using (var icon = new DesktopTray(this, () => { })) { icon.Hide(); Check(!IsVisible, "Tray did not hide the window"); icon.Restore(); Check(IsVisible, "Tray could not restore the window"); }
            Check(fixture.Calls.Count == calls, "Settings or tray smoke performed a game operation");
            DailyJson.Write(Path.Combine(smoke!, "presets-ui.json"), new { status = "passed", checks, realGameTouched = false });
        }
        finally {
            DailyJson.Write(Path.Combine(root, "task-presets.json"), old);
            preferences.Save(a, first); preferences.Save(b, second);
            new DailyDesktopBehavior().Save(root);
            RefreshCloseAction();
            viewedAccount = previousViewedAccount;
            Width = previousWidth; Height = previousHeight;
            LanguageSelector.SelectedIndex = previousLanguage; ThemeSelector.SelectedIndex = previousTheme;
            WorkspaceTabs.SelectedItem = previousTab;
            RefreshAccounts(); RefreshAccountPresets();
        }
    }
}

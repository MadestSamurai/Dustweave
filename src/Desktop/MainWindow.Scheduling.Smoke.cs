using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckSchedulingForSmoke()
    {
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var item in Descendants<T>(child)) yield return item;
            }
        }
        var fixture = (DemoEnvironment)host; int before = fixture.Calls.Count;
        string locale = L.Code; double width = Width, height = Height; var initialAppearance = theme.Preference;
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); checks++; }
        try
        {
            WorkspaceTabs.SelectedItem = AccountsTab;
            foreach (var row in rows) row.Selected = true;
            UpdateSelection();
            Check(RunAccountsButton.IsEnabled && RunAccountsButton.Style == FindResource("PrimaryButton"), "Account queue primary action missing");
            SetBusy(true); Check(!RunAccountsButton.IsEnabled, "Queue start enabled during another operation"); SetBusy(false);
            Check(!Descendants<Button>(dailyPanel).Any(b => b.Content?.ToString() == L.Get("run.multi", rows.Count)), "Multi-account action still in Today");
            var plan = new DailySchedulePlan { Enabled = true, Hour = 9, Minute = 15, Accounts = rows.Where(r => r.Account.Valid).Take(2).Select(r => r.Account.AccountKey).ToArray(), Days = [1, 3, 5], SavedUtc = DateTimeOffset.UtcNow };
            schedulePanel.Show(plan, new("fixture", "completed", DateTimeOffset.UtcNow));
            schedulePanel.SetAccounts(rows.Select(r => r.Account));
            WorkspaceTabs.SelectedItem = ScheduleTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var cards = Descendants<DailyScheduleAccounts>(schedulePanel).Single();
            var catalog = rows.Select(r => r.Account).ToArray();
            var original = cards.SelectedKeys;
            var reorderKeys = catalog.Where(a => a.Valid).Select(a => a.AccountKey).Take(3).ToArray();
            cards.Select(reorderKeys); Width = 920; Height = 650;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); schedulePanel.ScrollToBottom();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var tiles = cards.Children.OfType<Border>().ToArray();
            Point At(FrameworkElement tile, double x, double y) => tile.TranslatePoint(new Point(x, y), cards);
            Check(tiles.Length == 3 && tiles[2].TranslatePoint(new Point(), cards).Y > tiles[0].TranslatePoint(new Point(), cards).Y, "Reorder fixture did not wrap");
            Check(cards.InsertionAt(At(tiles[0], 4, 44)) == 0 && cards.InsertionAt(At(tiles[2], 240, 44)) == 3, "Wrapped insertion targets are incorrect");
            var addTile = cards.Children.OfType<Button>().Single();
            Check(cards.InsertionAt(At(addTile, 100, 44)) == 3 && cards.InsertionAt(new Point(-20, -20)) == -1, "Add tile/outside drop targets are incorrect");
            var removeControl = Descendants<Button>(tiles[0]).Single();
            Check(!DailyScheduleAccounts.CanDragFrom((DependencyObject)removeControl.Content, tiles[0]) && DailyScheduleAccounts.CanDragFrom(tiles[0].Child, tiles[0]), "Remove control starts dragging");
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            {
                ThemeSelector.SelectedIndex = (int)appearance;
                cards.ShowInsertion(2); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("schedule-reorder-" + appearance, this); cards.ClearInsertion();
            }
            Check(cards.SelectedKeys.SequenceEqual(reorderKeys), "Previewing or cancelling drop changed the draft");
            var dragFirst = cards.BeginAccountDrag(reorderKeys[0])!;
            Check(!cards.ApplyAccountDrop(new DataObject("Text", "unrelated account"), new Point(10, 10)), "External drag changed the schedule");
            cards.SetAccounts(catalog);
            Check(ReferenceEquals(cards.Children.OfType<Border>().First(), tiles[0]), "Catalog refresh replaced active drag visuals");
            Check(cards.ApplyAccountDrop(dragFirst, At(addTile, 100, 44)), "Native drag payload was not accepted at end slot");
            cards.EndAccountDrag(reorderKeys[0]);
            Check(cards.SelectedKeys.SequenceEqual(new[] { reorderKeys[1], reorderKeys[2], reorderKeys[0] }), "Drag drop order was not applied");
            cards.MoveAccount(reorderKeys[0], 0);
            var cancelledDrag = cards.BeginAccountDrag(reorderKeys[0]); cards.EndAccountDrag(reorderKeys[0]);
            Check(cards.SelectedKeys.SequenceEqual(reorderKeys) && !cards.ApplyAccountDrop(cancelledDrag!, new Point(10, 10)), "Cancelled drag remains active");
            Check(cards.MoveAccount(reorderKeys[0], 3) && cards.SelectedKeys.SequenceEqual(new[] { reorderKeys[1], reorderKeys[2], reorderKeys[0] }), "Forward cross-row move failed");
            Check(cards.MoveAccount(reorderKeys[0], 0) && cards.SelectedKeys.SequenceEqual(reorderKeys), "Backward move failed");
            Check(!cards.MoveAccount(reorderKeys[0], 1) && !cards.MoveAccount("unknown", 0) && !cards.MoveAccount(reorderKeys[0], -1), "Invalid/self drop changes order");
            cards.MoveAccount(reorderKeys[2], 0); var reordered = cards.SelectedKeys;
            cards.SetAccounts(catalog.Reverse());
            Check(cards.SelectedKeys.SequenceEqual(reordered), "Refresh lost reordered draft");
            schedulePanel.Busy(true);
            Check(!cards.MoveAccount(reordered[0], 3), "Disabled selector allows reordering");
            schedulePanel.Busy(false);
            DailySchedulePlan? reorderedPlan = null; void ObserveOrder(DailySchedulePlan value) => reorderedPlan = value;
            schedulePanel.SaveRequested += ObserveOrder;
            Descendants<Button>(schedulePanel).Single(b => b.Content?.ToString() == L.Get("schedule.save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            schedulePanel.SaveRequested -= ObserveOrder;
            Check(reorderedPlan != null && reorderedPlan.Accounts.SequenceEqual(reordered), "Save lost the reordered account sequence");
            schedulePanel.Show(reorderedPlan!, null); cards.SetAccounts(catalog);
            Check(cards.SelectedKeys.SequenceEqual(reordered), "Reload lost saved account order");
            cards.Select(original);
            cards.SetAccounts(catalog.Reverse());
            Check(cards.SelectedKeys.SequenceEqual(original), "Catalog refresh reordered scheduled identities");
            Descendants<Button>(cards).First(b => b.Tag is string).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var removed = cards.SelectedKeys;
            cards.SetAccounts(catalog);
            Check(cards.SelectedKeys.SequenceEqual(removed) && !cards.SelectedKeys.Contains(original[0]), "Refresh resurrected removed account");
            foreach (bool accept in new[] { false, true })
            {
                var picker = cards.CreatePicker(this);
                picker.ShowActivated = false; picker.WindowStartupLocation = WindowStartupLocation.Manual; picker.Left = -12000; picker.Top = 0;
                string? picked = null;
                var prior = cards.SelectedKeys;
                Dispatcher.BeginInvoke(() =>
                {
                    var choice = Descendants<CheckBox>(picker).First(); picked = ((DailyAccount)choice.Tag).AccountKey; choice.IsChecked = true;
                    var search = Descendants<TextBox>(picker).Single(); search.Text = "no-match-fixture";
                    Check(choice.Visibility == Visibility.Collapsed && choice.IsChecked == true, "Filtering lost pending selection");
                    search.Text = "";
                    var commit = Descendants<Button>(picker).Single(b => b.IsDefault);
                    Check(commit.IsEnabled, "Multi-add cannot be confirmed");
                    picker.UpdateLayout(); Capture("schedule-add-accounts-" + accept, (FrameworkElement)picker.Content);
                    if (accept) commit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else picker.Close();
                }, DispatcherPriority.ApplicationIdle);
                DailyDialogs.ShowModal(picker);
                Check(accept ? cards.SelectedKeys.SequenceEqual(prior.Append(picked!)) : cards.SelectedKeys.SequenceEqual(prior), "Add/cancel changed the wrong account set");
            }
            cards.SetAccounts([]);
            Check(cards.HasUnavailable && cards.SelectedKeys.Length == original.Length, "Missing saved identity was silently discarded");
            cards.SetAccounts(catalog); cards.Select([]); cards.SetAccounts(catalog);
            Check(cards.SelectedKeys.Length == 0, "Empty draft was replaced by saved selection");
            schedulePanel.Show(plan with { Enabled = false }, null);
            Descendants<Button>(cards).First(b => b.Tag is string).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DailySchedulePlan? captured = null; void Observe(DailySchedulePlan value) => captured = value;
            schedulePanel.SaveRequested += Observe;
            Descendants<Button>(schedulePanel).Single(b => b.Content?.ToString() == L.Get("schedule.save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            schedulePanel.SaveRequested -= Observe;
            Check(captured is { Enabled: false } && captured.Accounts.SequenceEqual(cards.SelectedKeys), "Disabled schedule failed to save edited accounts");
            schedulePanel.Show(plan, new("fixture", "completed", DateTimeOffset.UtcNow));
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            foreach (string language in DailyLanguage.Codes)
            {
                ThemeSelector.SelectedIndex = (int)appearance; L.Select(language); Width = 920; Height = 650;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                schedulePanel.ScrollToBottom(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("schedule-accounts-" + language + "-" + appearance);
                Check(cards.Children.OfType<FrameworkElement>().All(c => c.TransformToAncestor(this).TransformBounds(new Rect(c.RenderSize)).Right <= ActualWidth), "Account cards overflow minimum window width");
            }
            schedulePanel.ScrollToTop(); L.Select(locale);
            foreach (string language in DailyLanguage.Codes)
            {
                L.Select(language); Width = 1180; Height = 800; WorkspaceTabs.SelectedItem = ScheduleTab;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("schedule-" + language);
                Check(Descendants<TextBlock>(schedulePanel).Any(t => t.Text == L.Get("schedule.accounts")), "Schedule locale stale");
                WorkspaceTabs.SelectedItem = UpdatesTab;
                updatePanel.Status("updates.ready", "99.0.0"); updatePanel.Busy(false, true, false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("updates-" + language);
                Check(Descendants<Button>(updatePanel).Any(b => b.Content?.ToString() == L.Get("updates.install")), "Update action locale stale");
            }
            L.Select("en-US"); Width = 920; Height = 650;
            WorkspaceTabs.SelectedItem = AccountsTab; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("accounts-queue-compact");
            Check(RunAccountsButton.TransformToAncestor(this).TransformBounds(new Rect(RunAccountsButton.RenderSize)).Right <= ActualWidth, "Queue action clipped");
            WorkspaceTabs.SelectedItem = ScheduleTab; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("schedule-compact");
            Check(!File.Exists(scheduleStore.PlanPath), "Smoke persisted a real schedule");
            Check(fixture.Calls.Count == before, "Schedule/update UI called the game");
            Check(DailyReleaseHistory.ShouldShow(root, DailyProductVersion.Current), "New version notice suppressed");
            var oldTheme = theme.Preference;
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            foreach (string language in DailyLanguage.Codes)
            {
                L.Select(language); ThemeSelector.SelectedIndex = (int)appearance;
                var notice = CreateReleaseNotesWindow();
                notice.ShowActivated = false; notice.ShowInTaskbar = false;
                notice.WindowStartupLocation = WindowStartupLocation.Manual; notice.Left = -12000; notice.Top = 0;
                using (DailyDialogs.Dim(this))
                {
                    notice.Show();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    notice.UpdateLayout();
                    Check(DailyDialogs.ModalDepth == 1 && System.Windows.Shell.WindowChrome.GetWindowChrome(notice)?.CaptionHeight == 48, "Release notice has no modal shade/custom chrome");
                    Check(ReferenceEquals(notice.Background, FindResource("Surface")), "Release notice ignores theme");
                    Capture("release-owner-" + language + "-" + appearance, this);
                    Capture("release-notice-" + language + "-" + appearance, (FrameworkElement)notice.Content);
                    Check(notice.ActualHeight <= SystemParameters.WorkArea.Height && Descendants<Button>(notice).All(b => b.ActualHeight >= 36), "Dialog action clipped or too small");
                    notice.Close();
                }
                Check(DailyDialogs.ModalDepth == 0, "Modal shade leaked after close");
            }
            // Exercise real ShowDialog nested dispatch, default/cancel result and shade cleanup.
            foreach (bool accept in new[] { false, true })
            {
                var confirm = DailyDialogs.Message(this, L.Get("app.title"), L.Get("updates.confirm", "99.0.0"), true);
                confirm.ShowActivated = false; confirm.WindowStartupLocation = WindowStartupLocation.Manual; confirm.Left = -12000; confirm.Top = 0;
                Dispatcher.BeginInvoke(() =>
                {
                    Check(DailyDialogs.ModalDepth == 1 && confirm.IsVisible, "Confirmation is not modal");
                    if (accept) Descendants<Button>(confirm).Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else confirm.Close();
                }, DispatcherPriority.ApplicationIdle);
                Check((DailyDialogs.ShowModal(confirm) == true) == accept && DailyDialogs.ModalDepth == 0, "Modal result/cleanup failed");
            }
            ThemeSelector.SelectedIndex = (int)oldTheme;
            Check(!DailyReleaseHistory.ShouldShow(root, DailyProductVersion.Current) && DailyReleaseHistory.ShouldShow(root, "99.0.0"), "Version notice repeat logic failed");

            DailyReleaseHistory.MarkSeen(root, "99.0.0");
            Check(!DailyReleaseHistory.ShouldShow(root, DailyProductVersion.Current), "Rolling back repeated already read notes");
            DailyJson.Write(Path.Combine(smoke!, "scheduling-updates.json"), new { status = "passed", checks, gameCommands = 0, windowsTasksRegistered = 0, realGameTouched = false });
        }
        finally { ThemeSelector.SelectedIndex = (int)initialAppearance; L.Select(locale); Width = width; Height = height; WorkspaceTabs.SelectedItem = RunTab; updatePanel.Busy(false, false, false); }
    }
}

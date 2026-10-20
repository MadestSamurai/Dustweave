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
            Check(Descendants<CheckBox>(schedulePanel).Count() == 3, "Schedule should expose enable, takeover and parallel close options without weekdays");
            var takeover = Descendants<CheckBox>(schedulePanel).Single(x => x.Content?.ToString() == L.Get("schedule.takeover"));
            Check(takeover.IsChecked == false, "Tool takeover must default to off");
            schedulePanel.Show(plan with { TakeOverTools = true, Execution = new(true, 3, false) }, null);
            DailySchedulePlan? executionPlan = null; void CaptureExecution(DailySchedulePlan value) => executionPlan = value;
            schedulePanel.SaveRequested += CaptureExecution;
            Descendants<Button>(schedulePanel).Single(b => b.Content?.ToString() == L.Get("schedule.save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            schedulePanel.SaveRequested -= CaptureExecution;
            Check(executionPlan is { TakeOverTools: true, Execution.Enabled: true, Execution.Maximum: 3, Execution.CloseGame: false }, "Schedule UI lost the independent execution mode or takeover choice");
            schedulePanel.Show(plan, null);
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
            Check(reorderedPlan!.IsDaily, "Saving a legacy weekday plan did not produce a daily schedule");
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
            Check(captured is { Enabled: false, IsDaily: true } && captured.Accounts.SequenceEqual(cards.SelectedKeys), "Disabled schedule failed to save edited accounts");
            plan = plan with { Days = [0, 1, 2, 3, 4, 5, 6] };
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
                Check(Descendants<TextBlock>(schedulePanel).Any(t => t.Text == L.Get("schedule.time")), "Daily time label locale stale");
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
            var parkedGame = fixture.Game;
            int restartCalls = fixture.Calls.Count;
            try
            {
                Check(await StopToolForScheduleAsync(), "Scheduled restart did not accept an idle tool/game");
                Check(fixture.Game == null && fixture.Calls.Skip(restartCalls).SequenceEqual(new[] { "close" }), "Takeover reused a game parked in a minigame or attempted UI navigation");
                Check(!scheduleToolDraining && scheduleToolGame == null, "Successful restart handoff retained an old process");
            }
            finally
            {
                fixture.Game = parkedGame;
                fixture.Calls.RemoveRange(restartCalls, fixture.Calls.Count - restartCalls);
            }
            Check(DailyReleaseHistory.ShouldShow(root, DailyProductVersion.Current), "New version notice suppressed");
            var oldTheme = theme.Preference;
            foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
            foreach (string language in DailyLanguage.Codes)
            {
                L.Select(language); ThemeSelector.SelectedIndex = (int)appearance;
                var notice = CreateReleaseNotesWindow();
                notice.ShowActivated = false; notice.ShowInTaskbar = false;
                notice.WindowStartupLocation = WindowStartupLocation.Manual; notice.Left = -12000; notice.Top = 0;
                ShowUpdateNotice(notice);
                try
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    notice.UpdateLayout();
                    Check(DailyDialogs.ModalDepth == 0 && IsEnabled && !releaseDialogOpen && System.Windows.Shell.WindowChrome.GetWindowChrome(notice)?.CaptionHeight == 48, "Release notice blocks the host or lost custom chrome");
                    Check(ReferenceEquals(notice.Background, FindResource("Surface")), "Release notice ignores theme");
                    Capture("release-owner-" + language + "-" + appearance, this);
                    Capture("release-notice-" + language + "-" + appearance, (FrameworkElement)notice.Content);
                    Check(notice.ActualHeight <= SystemParameters.WorkArea.Height && Descendants<Button>(notice).All(b => b.ActualHeight >= 36), "Dialog action clipped or too small");
                }
                finally { CloseUpdateNotice(); }
                Check(DailyDialogs.ModalDepth == 0 && updateNotice == null, "Update notice leaked after close");
            }
            await CheckUpdateSchedulePriorityForSmoke(Check, Descendants<Button>);
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
            DailyJson.Write(Path.Combine(smoke!, "scheduling-updates.json"), new { status = "passed", checks, gameCommands = 0, simulatedRestartCommands = 1, windowsTasksRegistered = 0, realGameTouched = false });
        }
        finally { CloseUpdateNotice(); ThemeSelector.SelectedIndex = (int)initialAppearance; L.Select(locale); Width = width; Height = height; WorkspaceTabs.SelectedItem = RunTab; updatePanel.Busy(false, false, false); }
    }

    private async Task CheckUpdateSchedulePriorityForSmoke(Action<bool, string> check, Func<DependencyObject, IEnumerable<Button>> buttons)
    {
        var originalStore = scheduleStore;
        var originalChecking = updateChecking;
        var originalBusy = busy;
        var originalReason = manualUpdateReason;
        var fixture = (DemoEnvironment)host;
        int calls = fixture.Calls.Count;
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        // A missing synthetic identity stops just after the scheduling gates, before any game action.
        var plan = new DailySchedulePlan { Enabled = true, Hour = local.Hour, Minute = local.Minute,
            Accounts = [new string('f', 64)], SavedUtc = now.AddDays(-1) };
        try
        {
            foreach (string kind in new[] { "ready", "manual", "notes", "downloading" })
            {
                scheduleStore = new(Path.Combine(root, "update-schedule-probe", kind));
                scheduleStore.Save(plan);
                check(HasPendingScheduledRun(now), "Due schedule does not take priority over installation");
                check(!HasPendingScheduledRun(now.AddMinutes(-2)), "Future schedule blocks an idle update");
                manualUpdateReason = "updates.manual_layout";
                var notice = kind switch { "manual" => CreateManualUpdateWindow(), "notes" => CreateReleaseNotesWindow(), _ => CreateUpdateReadyWindow("99.0.0") };
                notice.WindowStartupLocation = WindowStartupLocation.Manual; notice.Left = -12000; notice.Top = 0;
                ShowUpdateNotice(notice);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                check(notice.IsVisible && IsEnabled && DailyDialogs.ModalDepth == 0 && !releaseDialogOpen, kind + " notice blocks scheduled execution");
                if (kind == "ready")
                {
                    foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
                    foreach (string language in DailyLanguage.Codes)
                    {
                        ThemeSelector.SelectedIndex = (int)appearance; L.Select(language);
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        Capture("update-ready-" + language + "-" + appearance, (FrameworkElement)notice.Content);
                        check(buttons(notice).Any(b => b.Content?.ToString() == L.Get("updates.later") && b.IsDefault), "Update prompt lost its safe default");
                        check(notice.ActualHeight <= SystemParameters.WorkArea.Height, "Update prompt exceeds the screen");
                    }
                }
                updateChecking = kind == "downloading";
                await CheckScheduleCoreAsync(now);
                check(!notice.IsVisible && updateNotice == null, kind + " notice was not dismissed at scheduled time");
                check(scheduleStore.Last is { State: "failed", Detail: "schedule.account_missing" }, kind + " stopped the scheduler before identity validation");
                check(!HasPendingScheduledRun(now), "Claimed occurrence keeps blocking updates");
            }
            // A user-owned modal and a running queue remain protected. Only update notices yield.
            foreach (bool userModal in new[] { true, false })
            {
                scheduleStore = new(Path.Combine(root, "update-schedule-probe", userModal ? "user-modal" : "queue"));
                scheduleStore.Save(plan);
                using (userModal ? DailyDialogs.Dim(this) : null)
                {
                    busy = !userModal;
                    await CheckScheduleCoreAsync(now);
                    check(scheduleStore.Last?.State == "waiting" && HasPendingScheduledRun(now), "Scheduled work interrupted a user operation");
                }
                busy = false;
                await CheckScheduleCoreAsync(now.AddSeconds(10));
                check(scheduleStore.Last?.Detail == "schedule.account_missing", "Schedule did not resume after user operation");
            }
            var later = CreateUpdateReadyWindow("99.0.0");
            later.WindowStartupLocation = WindowStartupLocation.Manual; later.Left = -12000; later.Top = 0;
            ShowUpdateNotice(later);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            buttons(later).Single(b => b.Content?.ToString() == L.Get("updates.later")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(updateNotice == null && !updateInstalling && IsEnabled, "Deferring an update starts installation or leaves the host disabled");
            check(fixture.Calls.Count == calls, "Update/scheduling regression touched the game");
        }
        finally
        {
            CloseUpdateNotice(); scheduleStore = originalStore; updateChecking = originalChecking; busy = originalBusy; manualUpdateReason = originalReason;
        }
    }
}

using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace BD2Daily.Desktop;

public sealed partial class DailyRunPanel : UserControl
{
    private static DailyLanguage L => DailyLanguage.Current;
    public event Action<bool, bool>? StartRequested;
    public event Action<QueueRetryRequest>? RetryRequested;
    public event Action<QueuePlanRequest>? PlanRequested;
    public event Action? StopRequested;
    public event Action? SettingsRequested;
    public event Action? AccountsRequested;
    public event Action? SyncCollectionRequested;
    private readonly Button syncCollection = new() { Content = "手动检查收集进度", ToolTip = "按已保存的地图范围读取游戏服务器；会切换卡带，可能需要数分钟，只同步、不采集。" };
    private readonly Button current = new() { Content = "开始日常" }, selected = new() { Content = "多账号运行" }, resume = new() { Content = "接续原队列" }, stop = new() { Content = "停止", IsEnabled = false, Visibility = Visibility.Collapsed };
    private readonly Button selectUnfinished = new() { Content = "勾选未完成" }, clearSelection = new() { Content = "清空勾选" }, retry = new() { Content = "补跑勾选环节（0）", IsEnabled = false };
    private readonly ObservableCollection<StageRow> rows = new();
    private readonly Dictionary<string, bool> planChoices = new(StringComparer.Ordinal);
    private DailyPreferences plan = new(); private bool showingReport; private bool rowsAreReport; private bool chooseInitialView = true;
    private readonly Button planButton = new() { Content = "当前计划" }, reportButton = new() { Content = "上次记录" };
    private QueueView currentView = new("idle", "", "", []); private bool isBusy; private int selectedAccounts; private string activeAccount = "";
    private readonly TextBlock status = new() { Text = "未运行", FontSize = 18, FontWeight = FontWeights.SemiBold }, detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    private readonly ListBox stages = new() { BorderThickness = new(0), MinHeight = 120, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0) };
    private readonly TextBlock counts = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("MutedInk") };
    private readonly ProgressBar progress = new() { Height = 3, Margin = new(0, 10, 0, 12), Maximum = 1, Foreground = (Brush)Application.Current.FindResource("Primary"), Background = (Brush)Application.Current.FindResource("Line") };
    private readonly WrapPanel views = new() { Margin = new(0, 0, 0, 2) };
    private readonly WrapPanel choices = new() { Margin = new(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    private readonly Button settings = new() { Content = "调整日常设置" }, accounts = new() { Content = "管理账号" };
    public DailyRunPanel(string root)
    {
        L.Bind(syncCollection, ContentControl.ContentProperty, "run.sync");
        L.Bind(syncCollection, FrameworkElement.ToolTipProperty, "run.sync_help");
        L.Bind(stop, ContentControl.ContentProperty, "common.stop");
        L.Bind(resume, ContentControl.ContentProperty, "run.resume");
        L.Bind(clearSelection, ContentControl.ContentProperty, "run.clear");
        L.Bind(planButton, ContentControl.ContentProperty, "run.plan");
        L.Bind(settings, ContentControl.ContentProperty, "run.settings");
        L.Bind(accounts, ContentControl.ContentProperty, "nav.accounts");
        L.Bind(selected, ContentControl.ContentProperty, "run.multi", 0);
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        stages.SetResourceReference(Control.BackgroundProperty, "Surface");
        stages.ItemTemplate = (DataTemplate)Application.Current.FindResource("DailyTimelineItem");
        stages.ItemContainerStyle = (Style)Application.Current.FindResource("TimelineContainer");
        stages.ItemsSource = rows;
        L.Bind(stages, System.Windows.Automation.AutomationProperties.NameProperty, "run.timeline_accessible");
        ScrollViewer.SetHorizontalScrollBarVisibility(stages, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(stages, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(stages, true);
        VirtualizingPanel.SetVirtualizationMode(stages, VirtualizationMode.Recycling);

        var layout = new DockPanel { Margin = new(20) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        var heading = new WrapPanel();
        status.FontSize = 24;
        heading.Children.Add(status);
        counts.Margin = new(16, 6, 0, 0);
        counts.FontSize = 13;
        heading.Children.Add(counts);
        top.Children.Add(heading);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        detail.FontSize = 13;
        detail.Margin = new(0, 8, 0, 12);
        top.Children.Add(new ScrollViewer { Content = detail, MaxHeight = 96,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });


        var viewSwitch = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { planButton, reportButton })
        {
            b.Style = (Style)Application.Current.FindResource("ViewButton");
            b.Margin = new(1);
            viewSwitch.Children.Add(b);
        }
        views.Children.Add(new Border { Background = (Brush)Application.Current.FindResource("SurfaceMuted"), CornerRadius = new(10), Padding = new(2), Margin = new(0, 0, 8, 4), Child = viewSwitch });
        settings.Style = (Style)Application.Current.FindResource("QuietButton");
        settings.Margin = new(0, 3, 0, 4);
        views.Children.Add(settings);
        top.Children.Add(views);
        planButton.Click += (_, _) => ShowCurrentPlan();
        reportButton.Click += (_, _) => ShowHistory();
        top.Children.Add(progress);
        selectUnfinished.Style = clearSelection.Style = (Style)Application.Current.FindResource("QuietButton");
        foreach (var b in new[] { selectUnfinished, clearSelection, retry, resume })
        {
            b.Margin = new(0, 0, 8, 4);
            choices.Children.Add(b);
        }
        top.Children.Add(choices);
        L.Bind(retry, FrameworkElement.ToolTipProperty, "run.retry_help");
        L.Bind(resume, FrameworkElement.ToolTipProperty, "run.resume_help");
        var foot = new StackPanel { Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(foot, Dock.Bottom);
        layout.Children.Add(foot);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        current.Style = (Style)Application.Current.FindResource("PrimaryButton");
        foreach (var b in new[] { selected, stop, current })
        {
            b.Margin = new(8, 0, 0, 4);
            actions.Children.Add(b);
        }
        foot.Children.Add(actions);
        var records = new Button { Content = "执行记录", Margin = new(0, 0, 8, 0) };
        L.Bind(records, ContentControl.ContentProperty, "run.records");
        var secondary = new WrapPanel { Margin = new(0, 8, 0, 0) };
        secondary.Children.Add(records);
        secondary.Children.Add(syncCollection);
        var recordsExpander = new Expander { Content = secondary, Style = (Style)Application.Current.FindResource("DisclosureExpander"), Margin = new(0, 8, 0, 0), Foreground = (Brush)Application.Current.FindResource("MutedInk"), FontSize = 12 };
        L.Bind(recordsExpander, HeaderedContentControl.HeaderProperty, "run.records_more");
        foot.Children.Add(recordsExpander);
        records.Click += (_, _) => { Directory.CreateDirectory(root); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true }); };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        accounts.Click += (_, _) => AccountsRequested?.Invoke();
        selectUnfinished.Click += (_, _) => { if (showingReport) SelectUnfinished(); else SelectPlanAll(); };
        clearSelection.Click += (_, _) => { foreach (var r in rows) r.Selected = false; };
        retry.Click += (_, _) => { if (!isBusy && SelectedTasks.Count > 0) RetryRequested?.Invoke(new(currentView.Account, currentView.Record, SelectedTasks)); };
        layout.Children.Add(stages);
        Content = new Border { Style = (Style)Application.Current.FindResource("Panel"), Child = layout };
        syncCollection.Click += (_, _) => SyncCollectionRequested?.Invoke();
        current.Click += (_, _) => StartCurrentSelection();
        selected.Click += (_, _) => StartRequested?.Invoke(true, false);
        resume.Click += (_, _) => StartRequested?.Invoke(false, true);
        stop.Click += (_, _) => StopRequested?.Invoke();
        Show(new("idle", "开始前可调整各环节设置；已完成的日常会按游戏进度跳过。", "", []));
    }
    public void Busy(bool busy, bool canStop = true)
    {
        isBusy = busy;
        views.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        current.Visibility = selected.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        stop.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        current.IsEnabled = syncCollection.IsEnabled = settings.IsEnabled = !busy;
        stop.IsEnabled = busy && canStop;
        selected.IsEnabled = !busy && selectedAccounts > 0;
        if (!busy && currentView.Expired)
            ShowCurrentPlan();
        else
            UpdateChoices();
    }
    public void SetSelection(int count)
    {
        selectedAccounts = count;
        L.Bind(selected, ContentControl.ContentProperty, "run.multi", count);
        selected.IsEnabled = !isBusy && count > 0;
    }
    public void SetAccount(string name, string key)
    {
        if (activeAccount != key)
        {
            ClearSelection();
            planChoices.Clear();
            showingReport = false;
            chooseInitialView = true;
        }
        if (activeAccount != key) ClearOperationError();
        activeAccount = key;
        L.Bind(current, FrameworkElement.ToolTipProperty, "run.account_help", name);
        UpdateChoices();
    }
    public void ShowPlan(DailyPreferences preferences)
    {
        plan = preferences;
        SyncPlanChoices();
        if (!isBusy && !showingReport)
            ShowCurrentPlan();
    }
    private void SyncPlanChoices()
    {
        var enabled = DailyStageCatalog.All.Where(s => s.Enabled(plan)).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in planChoices.Keys.Where(id => !enabled.Contains(id)).ToArray())
            planChoices.Remove(id);
        foreach (var id in enabled)
            planChoices.TryAdd(id, true);
    }
    public IReadOnlyList<string> SelectedPlanTasks => DailyStageCatalog.All.Where(s => s.Enabled(plan) && planChoices.GetValueOrDefault(s.Id)).Select(s => s.Id).ToArray();
    public bool StartEnabled => current.IsEnabled;
    public void SelectPlanAll()
    {
        if (isBusy || showingReport)
            return;
        foreach (var row in rows)
            row.Selected = true;
    }
    public void SelectPlanTask(string id, bool value)
    {
        if (isBusy || showingReport)
            return;
        var row = rows.FirstOrDefault(r => r.Task == id);
        if (row is { CanEdit: true })
            row.Selected = value;
    }

    public void StartCurrentSelection()
    {
        if (isBusy)
            return;
        if (showingReport)
        {
            ShowCurrentPlan();
            return;
        }
        if (DailyProfiles.ValidKey(activeAccount) && SelectedPlanTasks.Count > 0)
            PlanRequested?.Invoke(new(activeAccount, SelectedPlanTasks));
    }
    public void ShowCurrentPlan()
    {
        if (isBusy)
            return;
        showingReport = false;
        rowsAreReport = false;
        SyncPlanChoices();
        rows.Clear();
        foreach (var definition in DailyStageCatalog.All.Where(s => s.Enabled(plan)))
        {
            var row = new StageRow(new(definition.Id, "pending", "按游戏进度执行，已完成则跳过")) { Selected = planChoices[definition.Id], IsPlan = true };
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StageRow.Selected)) { planChoices[row.Task] = row.Selected; UpdateChoices(); } };
            rows.Add(row);
        }
        L.Text(status, "run.title");
        L.Bind(detail, TextBlock.TextProperty, () => currentView.Expired ? L.Get("run.new_day", L.Get("run.plan_hint")) : L.Get("run.plan_hint"));
        L.Bind(detail, FrameworkElement.ToolTipProperty, () => detail.Text);
        RenderOperationError();
        progress.Maximum = 1;
        progress.Value = 0;
        UpdateChoices();
    }
    private string? operationError, operationErrorTranslation;
    public string DisplayedMessage => detail.Text;
    public void ClearOperationError() => operationError = null;
    public void ShowOperationError(string message, string? translated = null)
    {
        operationError = message;
        operationErrorTranslation = translated;
        RenderOperationError();
    }
    private void RenderOperationError()
    {
        if (operationError == null) return;
        L.Text(status, "run.start_failed");
        var error = operationError;
        var translation = operationErrorTranslation;
        L.Bind(detail, TextBlock.TextProperty, () => translation != null ? L.Translate(translation) : Describe(error));
        L.Bind(detail, FrameworkElement.ToolTipProperty, () => L.Diagnostic(error, detail.Text));

    }
    public void LoadHistory(QueueView view)
    {
        if (isBusy)
            return;
        bool crossed = view.Expired && (!currentView.Expired || currentView.Record != view.Record);
        if (showingReport && (currentView.Record != view.Record || currentView.Account != view.Account))
        {
            ClearSelection();
            rows.Clear();
        }
        currentView = view;
        if (chooseInitialView && view.Account == activeAccount)
        {
            chooseInitialView = false;
            if (!view.Expired && view.Record.Length > 0 && view.Stages.Any(DailyQueueRetry.CanSelect))
                showingReport = true;
        }
        if (crossed)
        {
            if (showingReport)
                ClearSelection();
            planChoices.Clear();
            ShowCurrentPlan();
        }
        else if (showingReport)
            RenderHistory();
        else
            UpdateChoices();
        RenderOperationError();
    }
    public void ShowHistory()
    {
        if (isBusy || currentView.Record.Length == 0)
            return;
        showingReport = true;
        RenderHistory();
    }
    private void RenderHistory()
    {
        Show(currentView);
        L.Text(status, currentView.Expired ? "run.history_expired" : "run.history_title");
        if (currentView.Expired)
            L.Text(detail, "run.history_expired_hint");
        else if (currentView.Account != activeAccount)
            L.Text(detail, "run.history_other");
        else
            L.Bind(detail, TextBlock.TextProperty, () => L.Get("run.history_hint") + (currentView.Message.Length > 0 ? " " + Describe(currentView.Message) : ""));
        L.Bind(detail, FrameworkElement.ToolTipProperty, () => L.Diagnostic(currentView.Message, detail.Text));
        RenderOperationError();
    }
    public bool ShowingPlan => !showingReport;
    public IReadOnlyList<string> VisibleTasks => rows.Select(r => r.Task).ToArray();
    public bool ResumeEnabled => resume.IsEnabled;
    public IReadOnlyList<string> SelectedTasks => showingReport && !currentView.Expired ? rows.Where(r => r.Selected && r.Eligible).Select(r => r.Task).ToArray() : [];
    public void SelectUnfinished()
    {
        if (isBusy || !showingReport || currentView.Expired)
            return;
        foreach (var r in rows)
            r.Selected = r.Eligible;
        UpdateChoices();
    }
    public void ClearSelection()
    {
        foreach (var r in rows)
            r.Selected = false;
    }
    public bool RetryEnabled => retry.IsEnabled;
    public void ToggleFirstPlanCheckboxForSmoke()
    {
        if (showingReport || rows.Count == 0)
            throw new InvalidOperationException("Plan checkbox fixture is missing");
        stages.ScrollIntoView(rows[0]);
        stages.UpdateLayout();
        var container = stages.ItemContainerGenerator.ContainerFromItem(rows[0]) as ListBoxItem ?? throw new Exception("Plan row is not rendered");
        CheckBox? Find(DependencyObject item)
        {
            if (item is CheckBox check)
                return check;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            {
                var found = Find(VisualTreeHelper.GetChild(item, i));
                if (found != null)
                    return found;
            }
            return null;
        }
        var checkbox = Find(container) ?? throw new Exception("Plan checkbox is missing");
        var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(checkbox);
        ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
    }
    public void CheckMessagesForSmoke()
    {
        const string raw = "导航未能安全恢复：Waypoint map not available";
        Show(new QueueView("partial", raw, "fixture/chinese.json", [
            new QueueStage("weekly_fishing", "skipped", "weekly_fishing_complete"),
            new QueueStage("mail", "skipped", "mailbox_empty"),
            new QueueStage("trade", "recovery_required", "Native response is still pending"),
            new QueueStage("pass_rewards", "blocked", "Requested pass is not active for this account")], activeAccount));
        if (status.Text != "部分完成" || !detail.Text.Contains("目标地图") || !(detail.ToolTip as string)!.Contains(raw))
            throw new Exception("Translated queue lost meaning or original diagnostics");
        if (rows[0].Detail != "本周钓鱼任务已完成" || rows[0].State != "已跳过" || !rows[0].DiagnosticDetail.Contains("weekly_fishing_complete"))
            throw new Exception("Translated stage lost original result or status");
        if (rows[3].Name != "通行证奖励" || !rows[3].Detail.Contains("通行证"))
            throw new Exception("Legacy stage or native error remains untranslated");
        if (!rows[2].Eligible || rows[0].Eligible)
            throw new Exception("Translation changed retry eligibility");
        ShowOperationError("rejected: screen_changed");
        if (!detail.Text.Contains("游戏页面已变化") || !(detail.ToolTip as string)!.Contains("rejected: screen_changed"))
            throw new Exception("Operation error lost translation or diagnostics");
        ClearOperationError();
        Show(currentView);
    }
    internal Action LanguageProbeForSmoke()
    {
        var identities = rows.ToArray();
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(new {
            showingReport, isBusy, activeAccount, Selected = rows.Select(r => new { r.Task, r.Selected, r.CanEdit }),
            SelectedPlanTasks, RetryEnabled, StartEnabled, ResumeEnabled, VisibleTasks
        });
        string before = Snapshot();
        return () => {
            if (before != Snapshot() || !identities.SequenceEqual(rows))
                throw new Exception("Language change reset task selection, row identity or queue controls");
            if (rows.Any(r => r.Name != L.Stage(r.Task))) throw new Exception("Stage labels did not refresh");
        };
    }
    public void CheckViewportForSmoke()
    {
        var bounds = stages.TransformToAncestor(this).TransformBounds(new Rect(stages.RenderSize));
        if (stages.ActualHeight < 120 || bounds.Bottom > ActualHeight - 36)
            throw new Exception("Daily stage list is clipped by controls");
    }
    private void UpdateChoices()
    {
        bool hasReport = showingReport && currentView.Record.Length > 0;
        planButton.IsEnabled = !isBusy;
        reportButton.IsEnabled = !isBusy && currentView.Record.Length > 0;
        planButton.Tag = showingReport ? null : "selected";
        reportButton.Tag = showingReport ? "selected" : null;
        progress.Visibility = showingReport || isBusy ? Visibility.Visible : Visibility.Collapsed;
        L.Bind(reportButton, ContentControl.ContentProperty, currentView.Expired ? "run.expired_report" : "run.report");
        bool available = !isBusy && DailyProfiles.ValidKey(currentView.Account) && currentView.Account == activeAccount && hasReport && !currentView.Expired;
        bool planAvailable = !isBusy && !showingReport && DailyProfiles.ValidKey(activeAccount);
        L.Bind(current, ContentControl.ContentProperty, showingReport ? "run.choose" : "run.start_selected", SelectedPlanTasks.Count);
        current.IsEnabled = !isBusy && DailyProfiles.ValidKey(activeAccount) && (showingReport || SelectedPlanTasks.Count > 0);
        L.Bind(selected, FrameworkElement.ToolTipProperty, "run.multi_help");
        if (!showingReport)
            L.Text(counts, "run.selected_count", SelectedPlanTasks.Count, rows.Count);
        L.Bind(selectUnfinished, ContentControl.ContentProperty, showingReport ? "run.select_unfinished" : "run.select_all");
        retry.Visibility = resume.Visibility = hasReport ? Visibility.Visible : Visibility.Collapsed;

        if (hasReport)
        {
            int done = currentView.Stages.Count(s => s.State is "completed" or "skipped");
            L.Text(counts, currentView.Account != activeAccount ? "run.other_count" : "run.complete_count", done, currentView.Stages.Count);
        }
        choices.Visibility = !isBusy && (!showingReport || hasReport && !currentView.Expired) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in rows)
            r.CanEdit = (available || planAvailable) && r.Eligible;
        selectUnfinished.IsEnabled = (available || planAvailable) && rows.Any(r => r.Eligible);
        clearSelection.IsEnabled = (available || planAvailable) && rows.Any(r => r.Selected);
        bool needsCheck = currentView.Stages.Any(s => s.State == "recovery_required" && SelectedTasks.Contains(s.Task));
        L.Bind(retry, ContentControl.ContentProperty, needsCheck ? "run.reconcile" : "run.retry", SelectedTasks.Count);
        L.Bind(retry, FrameworkElement.ToolTipProperty, "run.retry_help");
        retry.IsEnabled = available && SelectedTasks.Count > 0;
        resume.IsEnabled = available && currentView.Stages.Any(s => s.State is "pending" or "recovery_required" && !s.Carried);
    }
    private sealed class StageRow : INotifyPropertyChanged
    {
        public string Task
        {
            get;
        }
        private QueueStage snapshot = new("", "pending", "");
        public string Name => L.Stage(Task);
        public string State => L.State(snapshot.State);
        public string AccessibleName => Name + " · " + State;
        public string Detail
        {
            get
            {
                var text = Describe(snapshot.Detail);
                if (snapshot.State == "recovery_required") text = L.Get("run.needs_check", text);
                return snapshot.Carried ? L.Get("run.carried", text) : text;
            }
        }
        public string DiagnosticDetail => L.Diagnostic(snapshot.Detail, Detail);
        public bool IsPlan { get; init; }
        public void RefreshLanguage() => Changed("");
        public bool Compact { get; private set; }
        public bool Active { get; private set; }
        public bool NeedsAttention { get; private set; }
        public Geometry Icon { get; private set; } = Geometry.Empty;
        public Brush StateBrush { get; private set; } = Brushes.Gray;
        public bool Eligible
        {
            get; private set;
        }
        private bool selected, canEdit;
        public bool Selected
        {
            get => selected; set
            {
                value &= Eligible;
                if (selected == value)
                    return;
                selected = value;
                Changed(nameof(Selected));
            }
        }
        public bool CanEdit
        {
            get => canEdit; set
            {
                if (canEdit == value)
                    return;
                canEdit = value;
                Changed(nameof(CanEdit));
            }
        }
        public StageRow(QueueStage stage)
        {
            Task = stage.Task;
            Update(stage);
        }
        public void Update(QueueStage stage)
        {
            snapshot = stage;
            Compact = stage.State is "completed" or "skipped";
            Active = stage.State == "running";
            NeedsAttention = stage.State is "blocked" or "recovery_required";
            string symbol = stage.State switch
            {
                "completed" => "Check", "skipped" => "Skip", "running" => "Play",
                "blocked" or "recovery_required" => "Warning", "partial" => "Partial", "stopped" or "paused" => "Pause",
                _ => Task switch
                {
                    "guild" => "Flag", "room" or "weekly_room_likes" => "Home", "management" or "trade" => "Shop",
                    "free_draws" => "Sparkle", "equipment" or "weekly_equipment" => "Tool",
                    "weekly_mainline" or "weekly_steal" => "Map", "friendship" or "weekly_npc" => "Chat",
                    "hunting" or "mirror" or "monster_hunt" or "event_battle" => "Battle",
                    "daily_dispatch" => "Send", "square" or "rewards" or "event_rewards" => "Gift",
                    "weekly_book" or "tactics" => "Book", "weekly_fishing" => "Fish", "weekly_sichuan" => "Game",
                    "mail" => "Mail", _ => "Tasks"
                }
            };
            Icon = (Geometry)Application.Current.FindResource("Icon." + symbol);
            Eligible = DailyQueueRetry.CanSelect(stage);

            StateBrush = (Brush)Application.Current.FindResource(stage.State switch
            {
                "completed" => "Success",
                "running" => "Primary",
                "blocked" or "recovery_required" => "Error",
                "partial" => "Warning",
                _ => "MutedInk"
            });


            if (!Eligible)
                selected = false;
            Changed("");
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
    }
    private void LanguageChanged(object? sender, EventArgs args)
    {
        foreach (var row in rows) row.RefreshLanguage();
    }
    private static string Describe(string text) => L.Describe(text);
    public void Show(QueueView view)
    {
        L.Bind(status, TextBlock.TextProperty, () => L.State(view.State));

        string message = view.Message.Contains("无法保存执行记录") || (view.Message.Contains("WinError 5") && view.Message.Contains("result.json")) ? "执行进度文件保存失败。可在下方勾选未完成环节补跑；完整错误见执行记录。" : view.Message;
        L.Bind(detail, TextBlock.TextProperty, () => message.Length > 0 ? Describe(message) : L.Get(view.State == "completed" ? "run.finished" : view.State == "partial" ? "run.partial" : "run.stop_hint"));
        L.Bind(detail, FrameworkElement.ToolTipProperty, () => L.Diagnostic(view.Message, detail.Text));
        // Preparing/connection errors keep the last readable report on screen.
        if (view.Record.Length > 0 && view.Stages.Count > 0)
        {
            bool same = rowsAreReport && showingReport && currentView.Record == view.Record && currentView.Account == view.Account;
            showingReport = true;
            rowsAreReport = true;
            currentView = view;
            int done = view.Stages.Count(s => s.State is "completed" or "skipped");
            L.Text(counts, "run.complete_count", done, view.Stages.Count);
            progress.Maximum = Math.Max(1, view.Stages.Count);
            progress.Value = done;
            if (!same || !rows.Select(r => r.Task).SequenceEqual(view.Stages.Select(r => r.Task)))
            {
                rows.Clear();
                foreach (var stage in view.Stages)
                {
                    var row = new StageRow(stage);
                    row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StageRow.Selected)) UpdateChoices(); };
                    rows.Add(row);
                }
            }
            else
                for (int i = 0; i < rows.Count; i++)
                    rows[i].Update(view.Stages[i]);
        }
        UpdateChoices();
    }
}

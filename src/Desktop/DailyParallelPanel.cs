using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public sealed class DailyParallelPanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly StackPanel rows = new();
    private readonly Dictionary<string, AccountView> accounts = new();
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 12) };
    public void Feedback(string message) { notice.Text = L.Describe(message); notice.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    private readonly TextBlock summary = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly Button pause, resume, stop, back;
    private DailyParallelRun? displayed;
    private Window? detailsWindow;
    private readonly bool sequential;
    public event Action<string, string>? ControlRequested;
    public event Action<DailyParallelItem>? GameRequested;
    public event Action? BackRequested;
    public DailyParallelPanel(bool sequential = false)
    {
        this.sequential = sequential;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var layout = new StackPanel { Margin = new(0, 0, 8, 0) };
        var header = new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var introduction = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; introduction.Children.Add(summary);
        header.Children.Add(introduction);
        back = Button("parallel.back", () => { if (back?.IsEnabled == true) BackRequested?.Invoke(); });
        back.Style = (Style)Application.Current.FindResource("PrimaryButton"); back.Margin = new(18, 0, 0, 0); back.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(back, 1); header.Children.Add(back);
        layout.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(18, 16, 18, 16), Margin = new(0, 0, 0, 14), Child = header });
        var actions = new WrapPanel { Margin = new(0, 0, 0, 12) };
        pause = Button("parallel.pause_all", () => ControlRequested?.Invoke("", "pause"));
        resume = Button("parallel.resume_all", () => ControlRequested?.Invoke("", "resume"));
        stop = Button("parallel.stop_all", () => ControlRequested?.Invoke("", "stop"));
        if (!sequential) { actions.Children.Add(pause); actions.Children.Add(resume); } actions.Children.Add(stop);
        layout.Children.Add(actions); notice.SetResourceReference(TextBlock.ForegroundProperty, "Error"); layout.Children.Add(notice); layout.Children.Add(rows); Content = layout;
        CommandBindings.Add(new CommandBinding(DailyRunPanel.TaskDetailsCommand, (_, e) =>
        {
            if (e.Parameter is DailyRunPanel.StageRow row && row.HasTaskDetails)
                OpenDetails(row.Draws is { Draws: > 0 } draw ? DailyResultWindow.DrawDetails(draw)
                    : new DailyTaskDetailsWindow(row.Task, row.PendingTasks, row.FinishedAt));
        }, (_, e) => { e.CanExecute = e.Parameter is DailyRunPanel.StageRow { HasTaskDetails: true }; e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(DailyRunPanel.AbsorptionDetailsCommand, (_, e) =>
        {
            if (e.Parameter is DailyRunPanel.StageRow { Absorptions: { Maps: > 0 } day }) OpenDetails(DailyResultWindow.AbsorptionDetails(day));
        }, (_, e) => { e.CanExecute = e.Parameter is DailyRunPanel.StageRow { HasAbsorptions: true }; e.Handled = true; }));
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), (_, _) => Show(displayed));
    }
    private void OpenDetails(Window window)
    {
        detailsWindow?.Close(); detailsWindow = window;
        window.Owner = Window.GetWindow(this); window.Show();
    }
    private static TextBlock Text(string key)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; L.Text(text, key); return text;
    }
    private static Button Button(string key, Action action)
    {
        var button = new Button { Margin = new(0, 0, 8, 6), MinHeight = 32 }; L.Bind(button, ContentControl.ContentProperty, key);
        button.Click += (_, _) => action(); return button;
    }
    public void Show(DailyParallelRun? run)
    {
        if (displayed?.Id != run?.Id) { accounts.Clear(); rows.Children.Clear(); ScrollToTop(); }
        displayed = run;
        var items = run?.Items ?? [];
        int finished = items.Count(x => x.State is "completed" or "partial" or "failed" or "stopped" or "interrupted");
        L.Text(summary, sequential ? "work.sequential_summary" : "parallel.summary", finished, items.Count, run?.Options.Maximum ?? 2);
        pause.IsEnabled = items.Any(x => x.State is "waiting" or "starting" or "connecting" or "running");
        resume.IsEnabled = items.Any(x => x.State is "paused" or "held");
        stop.IsEnabled = items.Any(x => DailyParallelSession.Occupies(x.State) || x.State is "waiting" or "held");
        back.IsEnabled = !stop.IsEnabled;
        L.Bind(back, FrameworkElement.ToolTipProperty, back.IsEnabled ? "parallel.return_help" : "parallel.return_busy");
        foreach (var missing in accounts.Keys.Except(items.Select(x => x.Job.Id)).ToArray())
        { rows.Children.Remove(accounts[missing].Surface); accounts.Remove(missing); }
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!accounts.TryGetValue(item.Job.Id, out var view)) accounts[item.Job.Id] = view = new(this, item);
            if (rows.Children.IndexOf(view.Surface) != index)
            { rows.Children.Remove(view.Surface); rows.Children.Insert(index, view.Surface); }
            view.Update(item);
        }
    }
    // Keep controls and rows alive across polls so updates preserve focus and scrolling.
    private sealed class AccountView
    {
        internal DailyParallelItem Item { get; private set; }
        internal Border Surface { get; }
        private readonly TextBlock title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 190, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        private readonly TextBlock counts = new() { FontSize = 12, Margin = new(0, 6, 0, 0) };
        private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(64, 4, 0, 8) };
        private readonly TextBlock current = new() { TextWrapping = TextWrapping.Wrap, Margin = new(64, 8, 0, 4) };
        private readonly ProgressBar ring = new() { Width = 52, Height = 52, Style = (Style)Application.Current.FindResource("CircularProgress"), Maximum = 100, Margin = new(0, 0, 12, 0) };
        private readonly Button game, toggle, end, disclosure;
        private readonly ListBox timeline = new() { BorderThickness = new(0), Padding = new(0), Margin = new(42, 8, 0, 0), MaxHeight = 280, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly ObservableCollection<DailyRunPanel.StageRow> stages = new();
        private bool? expanded;
        private string followed = "", language = "";
        private Brush? theme;
        private QueueStage[] snapshots = [];
        private bool showing;
        internal AccountView(DailyParallelPanel owner, DailyParallelItem item)
        {
            Item = item;
            var body = new StackPanel { Margin = new(0, 16, 0, 20) };
            var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(64) }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; heading.Children.Add(title); heading.Children.Add(counts);
            counts.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
            header.Children.Add(ring); Grid.SetColumn(heading, 1); header.Children.Add(heading); Grid.SetColumn(state, 2); header.Children.Add(state);
            body.Children.Add(header);
            detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); body.Children.Add(detail);
            current.SetResourceReference(TextBlock.ForegroundProperty, "Primary"); body.Children.Add(current);
            var controls = new WrapPanel { Margin = new(64, 8, 0, 0) };
            game = Button("parallel.game", () => owner.GameRequested?.Invoke(Item));
            toggle = Button("parallel.pause", () => owner.ControlRequested?.Invoke(Item.Account.AccountKey, Item.State is "paused" or "held" ? "resume" : "pause"));
            end = Button("parallel.stop", () => owner.ControlRequested?.Invoke(Item.Account.AccountKey, "stop"));
            if (!owner.sequential) { controls.Children.Add(game); controls.Children.Add(toggle); controls.Children.Add(end); }
            disclosure = Button("parallel.details", () => { expanded = !showing; Update(Item); });
            controls.Children.Add(disclosure); body.Children.Add(controls);
            timeline.SetResourceReference(Control.BackgroundProperty, "AppBackground");
            timeline.ItemTemplate = (DataTemplate)Application.Current.FindResource("DailyTimelineItem");
            timeline.ItemContainerStyle = (Style)Application.Current.FindResource("TimelineContainer");
            timeline.ItemsSource = stages;
            if (!owner.sequential) timeline.MaxHeight = 220;
            Grid.SetIsSharedSizeScope(timeline, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(timeline, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(timeline, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(timeline, false);
            VirtualizingPanel.SetIsVirtualizing(timeline, false);
            body.Children.Add(timeline);
            Surface = new Border { BorderThickness = new(0, 0, 0, 1), Child = body };
            Surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        }
        internal void Update(DailyParallelItem item)
        {
            Item = item;
            var queue = item.Status?.Queue?.Stages ?? [];
            title.Text = item.Account.Name; title.ToolTip = title.Text;
            bool interrupted = item.State == "paused" && !string.IsNullOrWhiteSpace(item.Detail) && !item.Detail.StartsWith("parallel.");
            state.Text = L.Get("parallel.state." + (interrupted ? "interrupted" : item.State));
            state.SetResourceReference(TextBlock.ForegroundProperty, interrupted || item.State is "failed" or "interrupted" ? "Error" : "MutedInk");
            state.ToolTip = null;
            if (item.Status is { } final && !DailyParallelSession.Occupies(item.State) && item.State is not ("waiting" or "held"))
            { state.Text += "  ·  " + final.AtUtc.LocalDateTime.ToString("HH:mm"); state.ToolTip = final.AtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"); }
            ring.Value = DailyWorkProgress.Percent(queue);
            counts.Text = L.Get("run.complete_count", DailyWorkProgress.HandledCount(queue), DailyWorkProgress.Count(queue));
            counts.Visibility = queue.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ring.ToolTip = L.Get("run.work_progress_help", DailyWorkProgress.HandledCount(queue), DailyWorkProgress.Count(queue));
            AutomationProperties.SetName(ring, title.Text + " · " + counts.Text);
            // Task explanations live in the timeline, not in a duplicate account paragraph.
            string description = item.Detail.StartsWith("parallel.") ? L.Get(item.Detail) : L.Describe(item.Detail);
            if (interrupted && DailyIssues.Classify(item.Detail).Code != "unknown") description = DailyIssuePresentation.WithAdvice(item.Detail, description);
            bool accountMessage = queue.Count == 0 || interrupted || item.State is "failed" or "interrupted" or "starting" or "connecting" or "waiting" or "held"
                || item.State != "running" && !item.Detail.StartsWith("parallel.") && !queue.Any(s => DailyResultText.Detail(s) == description);
            detail.Text = description;
            detail.Visibility = accountMessage && description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            game.IsEnabled = item.Status?.GameId > 0;
            L.Bind(toggle, ContentControl.ContentProperty, item.State is "paused" or "held" ? "parallel.resume" : "parallel.pause");
            toggle.IsEnabled = item.State is "waiting" or "starting" or "connecting" or "running" or "paused" or "held";
            end.IsEnabled = DailyParallelSession.Occupies(item.State) || item.State is "waiting" or "held";
            bool wasShowing = showing;
            showing = queue.Count > 0 && (expanded ?? (item.State is "running" or "paused" or "partial" or "failed" or "interrupted"));
            disclosure.IsEnabled = queue.Count > 0;
            L.Bind(disclosure, ContentControl.ContentProperty, showing ? "parallel.hide_details" : "parallel.details");
            AutomationProperties.SetName(disclosure, title.Text + " · " + disclosure.Content);
            timeline.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(timeline, title.Text + " · " + L.Get("run.timeline_accessible"));
            var active = queue.FirstOrDefault(s => s.Work != null || s.State == "running");
            current.Text = active == null ? "" : L.Stage(active.Task) + " · " + DailyResultText.CurrentWork(active);
            current.Visibility = !showing && item.State == "running" && active != null ? Visibility.Visible : Visibility.Collapsed;
            bool refresh = language != L.Code || !ReferenceEquals(theme, Application.Current.FindResource("Ink"));
            language = L.Code; theme = (Brush)Application.Current.FindResource("Ink");
            var display = queue.Select(s => item.State != "running" && (s.State == "running" || s.Work != null)
                ? s with { State = item.State is "paused" or "held" ? "paused" : item.State, Work = null, RecordedWork = s.Work ?? s.RecordedWork } : s).ToArray();
            if (!stages.Select(s => s.Task).SequenceEqual(display.Select(s => s.Task)))
            { stages.Clear(); foreach (var stage in display) stages.Add(new(stage, overview: true)); snapshots = []; followed = ""; }
            for (int i = 0; i < display.Length; i++)
                if (refresh || i >= snapshots.Length || snapshots[i] != display[i]) stages[i].Update(display[i]);
            snapshots = display;
            // Follow task transitions only; counters must not steal manual scroll or focus.
            string target = active?.Task ?? display.FirstOrDefault(s => s.State is "blocked" or "recovery_required" or "failed" or "partial")?.Task ?? "";
            if (showing && (target != followed || !wasShowing))
            {
                followed = target;
                var row = stages.FirstOrDefault(s => s.Task == target);
                if (row != null) timeline.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!showing || followed != row.Task) return;
                    // Scroll only this account's list. ScrollIntoView also sends
                    // BringIntoView to the outer overview and hides account headers.
                    timeline.UpdateLayout();
                    if (timeline.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem item) return;
                    var viewer = Descendant<ScrollViewer>(timeline);
                    var viewport = viewer == null ? null : Descendant<ScrollContentPresenter>(viewer);
                    if (viewer != null && viewport != null)
                        viewer.ScrollToVerticalOffset(viewer.VerticalOffset + item.TranslatePoint(new Point(), viewport).Y);
                }));
            }
        }
        private static T? Descendant<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T found) return found;
                if (Descendant<T>(child) is { } nested) return nested;
            }
            return null;
        }
    }
}

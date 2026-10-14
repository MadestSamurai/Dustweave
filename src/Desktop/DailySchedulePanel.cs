using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dustweave.Desktop;

public sealed class DailySchedulePanel : UserControl
{
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    public void ScrollToTop() => scroll.ScrollToTop();
    public void ScrollToBottom() => scroll.ScrollToBottom();
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly CheckBox enabled = new();
    private readonly CheckBox takeover = new(), closeGame = new();
    private readonly ComboBox mode = new() { MinWidth = 180 }, maximum = new() { Width = 80, ItemsSource = new[] { 1, 2, 3, 4 } };
    private readonly WrapPanel parallelSettings = new() { Margin = new(0, 10, 0, 0) };
    private readonly TextBox time = new() { Width = 105, MaxLength = 5 };
    private readonly DailyScheduleAccounts accountList = new();
    private readonly TextBlock next = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button save = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0) };
    private DailySchedulePlan saved = new();
    private bool showing;
    public event Action<DailySchedulePlan>? SaveRequested;
    public event Action? SandboxSetupRequested;
    public DailySchedulePanel()
    {
        var layout=new DockPanel();
        var content = new StackPanel { Margin = new(0, 0, 12, 16) };
        var form = new StackPanel();
        var summary = new DockPanel { Margin=new(0,4,0,20) };
        var upcoming = new StackPanel { HorizontalAlignment=HorizontalAlignment.Right };
        upcoming.Children.Add(Text("schedule.saved_next",12));upcoming.Children.Add(next); DockPanel.SetDock(upcoming,Dock.Right);summary.Children.Add(upcoming);
        L.Bind(enabled, ContentControl.ContentProperty, "schedule.enabled"); enabled.FontSize=17;enabled.FontWeight=FontWeights.SemiBold;enabled.VerticalAlignment=VerticalAlignment.Center;summary.Children.Add(enabled);form.Children.Add(summary);
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = Text("schedule.time"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new(0, 0, 16, 0); row.Children.Add(label);
        L.Bind(time, System.Windows.Automation.AutomationProperties.NameProperty, "schedule.time"); row.Children.Add(time);
        var local = new TextBlock { Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 }; L.Bind(local, TextBlock.TextProperty, () => L.Get("schedule.timezone", "UTC" + (DateTimeOffset.Now.Offset < TimeSpan.Zero ? "-" : "+") + DateTimeOffset.Now.Offset.Duration().ToString(@"hh\:mm"))); local.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); row.Children.Add(local); form.Children.Add(row);
        var requirements = Text("schedule.requirements", 12); requirements.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");requirements.Margin = new(0, 10, 0, 0); form.Children.Add(requirements); content.Children.Add(form);
        content.Children.Add(Divider());
        var selection = new StackPanel(); var selectionTitle=Text("schedule.accounts",17);selectionTitle.FontWeight=FontWeights.SemiBold;selection.Children.Add(selectionTitle);
        var hint = Text("schedule.account_help", 12); hint.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");hint.Margin = new(0, 6, 0, 12); selection.Children.Add(hint); selection.Children.Add(accountList);content.Children.Add(selection);
        content.Children.Add(Divider());
        var execution = new StackPanel(); execution.Children.Add(Text("schedule.execution", 16));
        foreach (string key in new[] { "schedule.serial", "schedule.parallel" })
        { var item = new ComboBoxItem(); L.Bind(item, ContentControl.ContentProperty, key); mode.Items.Add(item); }
        mode.Margin = new(0, 12, 0, 0); mode.HorizontalAlignment = HorizontalAlignment.Left;
        L.Bind(mode, System.Windows.Automation.AutomationProperties.NameProperty, "schedule.execution"); execution.Children.Add(mode);
        var limit = Text("parallel.maximum"); limit.VerticalAlignment = VerticalAlignment.Center; limit.Margin = new(0, 0, 12, 0);
        parallelSettings.Children.Add(limit); parallelSettings.Children.Add(maximum);
        L.Bind(maximum, System.Windows.Automation.AutomationProperties.NameProperty, "parallel.maximum");
        closeGame.Margin = new(18, 0, 0, 0); closeGame.VerticalAlignment = VerticalAlignment.Center;
        L.Bind(closeGame, ContentControl.ContentProperty, "parallel.close_game"); parallelSettings.Children.Add(closeGame); execution.Children.Add(parallelSettings);
        var setup = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 0) };
        L.Bind(setup, ContentControl.ContentProperty, "sandbox_setup.help");
        setup.Click += (_, _) => SandboxSetupRequested?.Invoke(); execution.Children.Add(setup);
        mode.SelectionChanged += (_, _) => setup.Visibility = parallelSettings.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        takeover.Margin = new(0, 16, 0, 0); L.Bind(takeover, ContentControl.ContentProperty, "schedule.takeover"); execution.Children.Add(takeover);
        var takeoverHint = Text("schedule.takeover_hint", 12); takeoverHint.Margin = new(24, 6, 0, 0); takeoverHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); execution.Children.Add(takeoverHint);
        content.Children.Add(execution);
        var executionHint=Text("schedule.mode_hint",12);executionHint.Margin=new(0,6,0,0);executionHint.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");execution.Children.Insert(2,executionHint);
        L.Bind(save, ContentControl.ContentProperty, "schedule.save"); save.Style = (Style)Application.Current.FindResource("PrimaryButton");save.Margin=new(0);
        var footer=new DockPanel();DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);result.FontSize=12;result.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");result.Margin=new(0,0,16,0);result.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(result);
        var footerBody=new StackPanel();footerBody.Children.Add(footer);feedback.Visibility=Visibility.Collapsed;footerBody.Children.Add(feedback);
        var footerLine=new Border {BorderThickness=new(0,1,0,0),Padding=new(0,12,12,0),Child=Constrain(footerBody)};footerLine.SetResourceReference(Border.BorderBrushProperty,"Line");DockPanel.SetDock(footerLine,Dock.Bottom);layout.Children.Add(footerLine);
        scroll.Content=Constrain(content);layout.Children.Add(scroll);Content = layout;
        save.Click += (_, _) => Save();
        time.TextChanged += (_, _) => DraftChanged();
        foreach (var check in new[] { enabled, closeGame, takeover })
        { check.Checked += (_, _) => DraftChanged(); check.Unchecked += (_, _) => DraftChanged(); }
        mode.SelectionChanged += (_, _) => DraftChanged();
        maximum.SelectionChanged += (_, _) => DraftChanged();
        accountList.SelectionChanged += DraftChanged;
        Show(new(), null);
    }
    private static TextBlock Text(string key, double size = 13) { var t = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap }; L.Text(t, key); return t; }
    private static Border Divider() { var line=new Border { BorderThickness=new(0,0,0,1),Margin=new(0,22,0,22) };line.SetResourceReference(Border.BorderBrushProperty,"Line");return line; }
    private static Grid Constrain(UIElement content) {var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {MaxWidth=880});grid.Children.Add(content);return grid;}
    public void SetAccounts(IEnumerable<DailyAccount> source) => accountList.SetAccounts(source);
    public void Show(DailySchedulePlan plan, DailyScheduleRun? last)
    {
        showing = true;
        try
        {
        saved = plan; enabled.IsChecked = plan.Enabled; time.Text = $"{plan.Hour:00}:{plan.Minute:00}";
        mode.SelectedIndex = plan.Execution.Enabled ? 1 : 0; maximum.SelectedItem = plan.Execution.Maximum;
        closeGame.IsChecked = plan.Execution.CloseGame; takeover.IsChecked = plan.TakeOverTools;
        accountList.Select(plan.Accounts);
        RefreshStatus(plan, last);
        }
        finally { showing = false; }
    }
    private void DraftChanged() { if (!showing) Feedback("schedule.unsaved"); }
    public void RefreshStatus(DailySchedulePlan plan, DailyScheduleRun? last)
    {
        var upcoming = DailyScheduleClock.Next(plan, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        L.Bind(next, TextBlock.TextProperty, () => upcoming is null ? L.Get("schedule.off") : upcoming.Value.LocalDateTime.ToString("ddd HH:mm", System.Globalization.CultureInfo.GetCultureInfo(L.Code)));
        L.Bind(result, TextBlock.TextProperty, () => last == null ? "" : L.Get("schedule.last", last.AtUtc.LocalDateTime.ToString("MM-dd HH:mm"), L.Get("schedule.state." + last.State)) +
            (last.State is "failed" or "waiting" or "missed" && last.Detail.Length > 0 ? "\n" + L.Describe(last.Detail) : ""));
        result.ToolTip = last?.Detail.Length > 0 ? L.Describe(last.Detail) : null;
    }
    public void Busy(bool value) { save.IsEnabled = enabled.IsEnabled = time.IsEnabled = mode.IsEnabled = maximum.IsEnabled = closeGame.IsEnabled = takeover.IsEnabled = accountList.IsEnabled = !value; }
    public void Feedback(string message, bool error = false)
    {
        feedback.Visibility=string.IsNullOrWhiteSpace(message)?Visibility.Collapsed:Visibility.Visible;
        L.Bind(feedback, TextBlock.TextProperty, () => message.StartsWith("schedule.") ? L.Get(message) : L.Describe(message));
        feedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Primary");
    }
    private void Save()
    {
        if (!TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)) { if (enabled.IsChecked == true) { Feedback("schedule.invalid_time", true); return; } parsed = new TimeOnly(saved.Hour, saved.Minute); }
        if (enabled.IsChecked == true && accountList.HasUnavailable) { Feedback("schedule.account_missing", true); return; }
        var plan = new DailySchedulePlan { Enabled = enabled.IsChecked == true, Hour = parsed.Hour, Minute = parsed.Minute, Accounts = accountList.SelectedKeys, SavedUtc = DateTimeOffset.UtcNow,
            Execution = new(mode.SelectedIndex == 1, (int)(maximum.SelectedItem ?? 2), closeGame.IsChecked == true), TakeOverTools = takeover.IsChecked == true };
        try { if (plan.Enabled) plan.Validate(); SaveRequested?.Invoke(plan); }
        catch (Exception e) { Feedback(e.Message, true); }
    }
}

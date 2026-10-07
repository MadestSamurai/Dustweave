using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dustweave.Desktop;

public sealed class DailySchedulePanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly CheckBox enabled = new();
    private readonly TextBox time = new() { Width = 105, MaxLength = 5 };
    private readonly CheckBox[] days = Enumerable.Range(0, 7).Select(_ => new CheckBox { Margin = new(0, 0, 14, 8) }).ToArray();
    private readonly DailyScheduleAccounts accountList = new();
    private readonly TextBlock next = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button save = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0) };
    private DailySchedulePlan saved = new();
    public event Action<DailySchedulePlan>? SaveRequested;
    public DailySchedulePanel()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var content = new StackPanel { Margin = new(0, 0, 8, 0) };
        var summary = new StackPanel(); summary.Children.Add(Text("schedule.next", 12)); summary.Children.Add(next); summary.Children.Add(result);
        content.Children.Add(Card(summary));
        var form = new StackPanel();
        L.Bind(enabled, ContentControl.ContentProperty, "schedule.enabled"); enabled.Margin = new(0, 0, 0, 16); form.Children.Add(enabled);
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = Text("schedule.time"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new(0, 0, 16, 0); row.Children.Add(label);
        L.Bind(time, System.Windows.Automation.AutomationProperties.NameProperty, "schedule.time"); row.Children.Add(time);
        var local = new TextBlock { Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 }; L.Bind(local, TextBlock.TextProperty, () => L.Get("schedule.timezone", "UTC" + (DateTimeOffset.Now.Offset < TimeSpan.Zero ? "-" : "+") + DateTimeOffset.Now.Offset.Duration().ToString(@"hh\:mm"))); local.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); row.Children.Add(local); form.Children.Add(row);
        var weekdays = new WrapPanel { Margin = new(0, 18, 0, 6) };
        foreach (int i in new[] { 1, 2, 3, 4, 5, 6, 0 }) { L.Bind(days[i], ContentControl.ContentProperty, "schedule.day" + i); weekdays.Children.Add(days[i]); }
        form.Children.Add(weekdays); form.Children.Add(Text("schedule.requirements", 12)); content.Children.Add(Card(form));
        var selection = new StackPanel(); selection.Children.Add(Text("schedule.accounts", 16));
        var hint = Text("schedule.account_help", 12); hint.Margin = new(0, 6, 0, 12); selection.Children.Add(hint); selection.Children.Add(accountList);
        L.Bind(save, ContentControl.ContentProperty, "schedule.save"); save.Style = (Style)Application.Current.FindResource("PrimaryButton"); selection.Children.Add(save); selection.Children.Add(feedback); selection.Margin = new Thickness(0, 4, 0, 16); content.Children.Add(selection);
        Content = content;
        save.Click += (_, _) => Save();
        Show(new(), null);
    }
    private static TextBlock Text(string key, double size = 13) { var t = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap }; L.Text(t, key); return t; }
    private static Border Card(UIElement child) => new() { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(20), Margin = new(0, 0, 0, 16), Child = child };
    public void SetAccounts(IEnumerable<DailyAccount> source) => accountList.SetAccounts(source);
    public void Show(DailySchedulePlan plan, DailyScheduleRun? last)
    {
        saved = plan; enabled.IsChecked = plan.Enabled; time.Text = $"{plan.Hour:00}:{plan.Minute:00}";
        for (int i = 0; i < 7; i++) days[i].IsChecked = plan.Days.Contains(i);
        accountList.Select(plan.Accounts);
        RefreshStatus(plan, last);
    }
    public void RefreshStatus(DailySchedulePlan plan, DailyScheduleRun? last)
    {
        var upcoming = DailyScheduleClock.Next(plan, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        L.Bind(next, TextBlock.TextProperty, () => upcoming is null ? L.Get("schedule.off") : upcoming.Value.LocalDateTime.ToString("ddd HH:mm", System.Globalization.CultureInfo.GetCultureInfo(L.Code)));
        L.Bind(result, TextBlock.TextProperty, () => last == null ? L.Get("schedule.no_result") : L.Get("schedule.last", last.AtUtc.LocalDateTime.ToString("MM-dd HH:mm"), L.Get("schedule.state." + last.State)) + (last.Detail.Length == 0 ? "" : " · " + L.Describe(last.Detail)));
    }
    public void Busy(bool value) { save.IsEnabled = !value; accountList.IsEnabled = !value; }
    public void Feedback(string message, bool error = false)
    {
        L.Bind(feedback, TextBlock.TextProperty, () => message.StartsWith("schedule.") ? L.Get(message) : L.Describe(message));
        feedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Primary");
    }
    private void Save()
    {
        if (!TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)) { if (enabled.IsChecked == true) { Feedback("schedule.invalid_time", true); return; } parsed = new TimeOnly(saved.Hour, saved.Minute); }
        if (enabled.IsChecked == true && accountList.HasUnavailable) { Feedback("schedule.account_missing", true); return; }
        var plan = new DailySchedulePlan { Enabled = enabled.IsChecked == true, Hour = parsed.Hour, Minute = parsed.Minute, Days = Enumerable.Range(0, 7).Where(i => days[i].IsChecked == true).ToArray(), Accounts = accountList.SelectedKeys, SavedUtc = DateTimeOffset.UtcNow };
        try { if (plan.Enabled) plan.Validate(); SaveRequested?.Invoke(plan); }
        catch (Exception e) { Feedback(e.Message, true); }
    }
}

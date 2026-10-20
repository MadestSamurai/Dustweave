using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dustweave.Desktop;

internal sealed record DailyAccountRunResult(string Name, QueueView View);

internal static class DailyResultText
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal static string CurrentWork(QueueStage stage)
    {
        var p = stage.Work;
        string key = "work." + (string.IsNullOrEmpty(p?.Step) ? "execute" : p.Step);
        string text = L.Get(key);
        if (text == key) text = L.Get("work.execute");
        if (p is { Total: > 0 }) text += " · " + L.Get(p.Unit switch
        {
            "maps" => "work.maps", "quests" => "work.quests", "absorptions" => "work.absorptions",
            "tickets" => "work.tickets", "potatoes" => "work.potatoes", _ => "work.units"
        }, p.Completed, p.Total);
        return text;
    }
    internal static string Draws(DailyDrawSummary draw) => L.Get("draw.summary", draw.Draws, draw.FiveStarCostumes, draw.FiveStarUrEquipment)
        + (draw.Unknown > 0 ? "\n" + L.Get("draw.unknown", draw.Unknown) : "");
    internal static string Detail(QueueStage stage)
    {
        string detail = DailyLocationLabels.Format(L.Describe(stage.Detail), stage.Locations, L.Code);
        if (stage.Draws is { Draws: > 0 } draw) detail = Draws(draw) + (detail.Length > 0 ? "\n" + detail : "");
        if (stage.Absorptions is { Maps: > 0 } day) detail += (detail.Length > 0 ? "\n" : "") + Absorptions(day);
        return detail;
    }
    internal static string Absorptions(DailyAbsorptionDay day) => L.Get("absorption.summary", day.Maps,
        day.Visits.Where(v => v.AlongNpc).Select(v => (v.Pack, v.Map)).Distinct().Count());
}

// Reports are read-only modeless windows: finishing one account never blocks the
// next account or a scheduled queue. All surfaces use the shared dialog color.
internal sealed class DailyResultWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly StackPanel rows = new();
    internal ScrollViewer ResultsScroll { get; }
    private DailyResultWindow(string title, Func<string> summary)
    {
        Width = 700; Height = 590; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, title);
        var body = new DockPanel { Margin = new(24, 12, 24, 24) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) };
        heading.Children.Add(Text(() => L.Get(title), 22, true));
        heading.Children.Add(Text(summary, 13, false, true, new(0, 8, 0, 0)));
        DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        var close = new Button { IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        L.Bind(close, ContentControl.ContentProperty, "run.details_close"); close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
        ResultsScroll = new() { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 10, 0), Background = Brushes.Transparent };
        body.Children.Add(ResultsScroll); Content = body; DailyDialogs.Prepare(this);
    }
    private static TextBlock Text(Func<string> text, double size = 14, bool bold = false, bool muted = false, Thickness? margin = null)
    {
        var control = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = margin ?? new Thickness() };
        control.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedInk" : "Ink");
        L.Bind(control, TextBlock.TextProperty, text); return control;
    }
    private void Row(UIElement content)
    {
        var row = new Border { Child = content, Padding = new(0, 13, 0, 15), BorderThickness = new(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "Line"); rows.Children.Add(row);
    }
    internal static DailyResultWindow DrawDetails(DailyDrawSummary summary)
    {
        var window = new DailyResultWindow("draw.title", () => DailyResultText.Draws(summary));
        foreach (var group in summary.Items.GroupBy(i => i.Category))
        {
            window.rows.Children.Add(Text(() => L.Get(group.Key == "Costume" ? "draw.costumes" : "draw.equipment"), 13, true, true, new(0, 12, 0, 0)));
            foreach (var item in group)
            {
                var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var body = new StackPanel { Margin = new(0, 0, 18, 0) };
                body.Children.Add(Text(() => item.Name.In(L.Code).Trim(' ', '·') is { Length: > 0 } name ? name : L.Get("draw.item_unknown", item.Id), 14, true));
                body.Children.Add(Text(() => (item.Known ? (item.Stars + "★ " + item.Rarity).Trim() : L.Get("draw.grade_unknown"))
                    + (item.Converted ? " · " + L.Get("draw.converted") : ""), 12, false, true, new(0, 5, 0, 0)));
                grid.Children.Add(body); var count = Text(() => "×" + item.Count, 14); Grid.SetColumn(count, 1); grid.Children.Add(count); window.Row(grid);
            }
        }
        if (summary.Items.Count == 0) window.rows.Children.Add(Text(() => L.Get("draw.no_details"), 14, false, true));
        return window;
    }
    internal static DailyResultWindow AbsorptionDetails(DailyAbsorptionDay day)
    {
        var window = new DailyResultWindow("absorption.title", () => DailyResultText.Absorptions(day) + "\n" + L.Get("absorption.help"));
        foreach (var group in day.Visits.GroupBy(v => (v.Pack, v.Map)).OrderBy(g => g.Min(v => v.AtUtcTicks)))
        {
            var first = group.OrderBy(v => v.AtUtcTicks).First();
            string Name(DailyDisplayName name, string fallback, long id) => name.In(L.Code) is { Length: > 0 } text ? text : L.Get(fallback, id);
            var body = new StackPanel();
            body.Children.Add(Text(() => Name(first.PackName, "absorption.pack", first.Pack) + " · " + Name(first.MapName, "absorption.map", first.Map), 14, true));
            foreach (var visit in group.OrderBy(v => v.AtUtcTicks))
                body.Children.Add(Text(() => new DateTime(visit.AtUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm:ss")
                    + " · " + L.Get(visit.AlongNpc ? "absorption.npc" : "absorption.collection"), 12, false, true, new(0, 5, 0, 0)));
            window.Row(body);
        }
        return window;
    }
    internal static Button AbsorptionButton(DailyAbsorptionDay day, Window owner)
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0), MinHeight = 30, Padding = new(10, 4, 10, 4) };
        L.Bind(button, ContentControl.ContentProperty, () => L.Get("absorption.button", day.Maps));
        button.Click += (_, _) => { var popup = AbsorptionDetails(day); popup.Owner = owner; popup.Show(); };
        return button;
    }
    internal static DailyResultWindow RunResults(IReadOnlyList<DailyAccountRunResult> accounts)
    {
        int issues = accounts.Sum(a => DailyRunReport.Relevant(a.View).Count(s => s.State != "completed" || s.HasNotice || s.PendingTasks?.Count > 0)
            + (a.View.CompletionIssue.Length > 0 ? 1 : 0) + (a.View.Message.Length > 0 ? 1 : 0));
        var window = new DailyResultWindow("report.title", () => L.Get(issues == 0 ? "report.success" : "report.issues", accounts.Count, issues));
        foreach (var account in accounts)
        {
            var relevant = DailyRunReport.Relevant(account.View);
            if (relevant.Count == 0 && account.View.CompletionIssue.Length == 0 && account.View.Message.Length == 0) continue;
            window.rows.Children.Add(Text(() => account.Name, 15, true, false, new(0, 12, 0, 0)));
            foreach (var stage in relevant)
            {
                var body = new StackPanel();
                body.Children.Add(Text(() => L.Stage(stage.Task) + " · " + L.State(stage.State), 14, true));
                body.Children.Add(Text(() => DailyResultText.Detail(stage) is { Length: > 0 } detail ? detail : L.Get("report.not_finished"), 13, false, true, new(0, 6, 0, 0)));
                if (stage.Draws is { Draws: > 0 } || stage.PendingTasks?.Count > 0)
                {
                    var details = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 0), MinHeight = 30, Padding = new(10, 4, 10, 4) };
                    L.Bind(details, ContentControl.ContentProperty, "run.details");
                    details.Click += (_, _) => {
                        Window popup = stage.Draws is { Draws: > 0 } draw ? DrawDetails(draw) : new DailyTaskDetailsWindow(stage.Task, stage.PendingTasks ?? [], stage.FinishedAt);
                        popup.Owner = window; popup.Show();
                    }; body.Children.Add(details);
                }
                if (stage.Absorptions is { Maps: > 0 } day) body.Children.Add(AbsorptionButton(day, window));
                window.Row(body);
            }
            if (account.View.Message.Length > 0) window.Row(Text(() => L.Describe(account.View.Message), 13, false, true));
            if (account.View.CompletionIssue.Length > 0) window.Row(Text(() => L.Describe(account.View.CompletionIssue), 13, false, true));
        }
        return window;
    }
}

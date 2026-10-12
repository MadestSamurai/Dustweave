using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dustweave.Desktop;

public sealed class DailyBargainWindow : Window
{
    static DailyLanguage L => DailyLanguage.Current;
    readonly DailyBargainSession session;
    readonly bool allowGame;
    readonly ComboBox characters = new() { MinHeight = 36, DisplayMemberPath = nameof(DailyBargainCharacter.Name) };
    readonly ProgressBar progress = new() { Height = 8, Maximum = 100, Margin = new(0, 10, 0, 0) };
    readonly TextBlock identity = Text(), experience = Text(), estimate = Text(22), stock = Text(), count = Text(), status = Text();
    readonly Button read = Button("bargain.refresh"), start = Button("bargain.start"), stop = Button("bargain.stop");
    long potions, spent;
    int rounds;
    bool busy, closing;
    string message = "bargain.intro";
    Exception? error;
    static TextBlock Text(double size = 14)
    {
        var t = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); return t;
    }
    static Button Button(string key)
    {
        var b = new Button { MinHeight = 36, Padding = new(16, 6, 16, 6) };
        L.Bind(b, ContentControl.ContentProperty, key); return b;
    }
    static Border Card(UIElement child)
    {
        var b = new Border { Padding = new(20), Margin = new(0, 0, 0, 16), CornerRadius = new(12), BorderThickness = new(1), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "Surface"); b.SetResourceReference(Border.BorderBrushProperty, "Line"); return b;
    }
    public DailyBargainWindow(string root, string account, bool allowGame = true)
    {
        this.allowGame = allowGame;
        stop.IsEnabled = false;
        progress.SetResourceReference(ForegroundProperty, "Primary");
        progress.SetResourceReference(BackgroundProperty, "Line");
        progress.BorderThickness = new(0);
        session = new(root, account);
        Width = 660; Height = 650; MinWidth = 540; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "bargain.title");
        var layout = new DockPanel { Margin = new(24) };
        var actions = new Grid { Margin = new(0, 16, 0, 0) };
        actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        start.SetResourceReference(StyleProperty, "PrimaryButton"); start.HorizontalAlignment = HorizontalAlignment.Stretch;
        stop.Margin = new(12, 0, 0, 0); Grid.SetColumn(stop, 1); actions.Children.Add(start); actions.Children.Add(stop);
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        status.Margin = new(0, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        var body = new StackPanel();
        var header = new DockPanel { Margin = new(0, 0, 0, 16) };
        DockPanel.SetDock(read, Dock.Right); read.Margin = new(16, 0, 0, 0); header.Children.Add(read); header.Children.Add(identity); body.Children.Add(header);
        var choice = new StackPanel(); var label = Text(); L.Text(label, "bargain.character"); choice.Children.Add(label);
        characters.Margin = new(0, 8, 0, 10); choice.Children.Add(characters); choice.Children.Add(experience); choice.Children.Add(progress); body.Children.Add(Card(choice));
        L.Bind(characters, System.Windows.Automation.AutomationProperties.NameProperty, "bargain.character");
        var budget = new StackPanel(); var costLabel = Text(); L.Text(costLabel, "bargain.estimate_label"); budget.Children.Add(costLabel);
        budget.Children.Add(estimate); budget.Children.Add(stock); budget.Children.Add(count); body.Children.Add(Card(budget));
        var hint = Text(); hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); L.Text(hint, "bargain.hint"); body.Children.Add(hint);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        session.Report = key => Dispatcher.Invoke(() => SetMessage(key));
        session.Progress = p => Dispatcher.Invoke(() =>
        {
            potions = p.Potions; rounds = p.Rounds; spent = p.Spent;
            var items = ((DailyBargainCharacter[])characters.ItemsSource).Select(c => c.Character == p.Character.Character ? p.Character : c).ToArray();
            characters.ItemsSource = items; characters.SelectedItem = items.Single(c => c.Character == p.Character.Character); Render();
        });
        characters.SelectionChanged += (_, _) => Render();
        read.Click += async (_, _) => await Refresh();
        start.Click += async (_, _) =>
        {
            if (characters.SelectedItem is not DailyBargainCharacter c || !new DailyBargainPlan(c, potions).CanStart) return;
            rounds = 0; spent = 0;
            await Work(async () => SetMessage(await Task.Run(() => session.Train(c))), true);
        };
        stop.Click += (_, _) => { session.Stop(); stop.IsEnabled = false; SetMessage("bargain.stopping"); };
        Closing += OnClosing;
        L.Changed += LanguageChanged;
        Closed += (_, _) => L.Changed -= LanguageChanged;
        Loaded += async (_, _) => { if (allowGame) await Refresh(); };
        Render();
    }
    void SetMessage(string key) { message = key; error = null; Render(); }
    void LanguageChanged(object? sender, EventArgs e) => Render();
    async Task Refresh() => await Work(async () =>
    {
        var data = await Task.Run(session.Read);
        potions = data.Potions; rounds = 0; spent = 0;
        L.Text(identity, "bargain.account", data.Player);
        characters.ItemsSource = data.Characters; characters.SelectedIndex = data.Characters.Length > 0 ? 0 : -1;
        SetMessage(data.Characters.Length == 0 ? "bargain.none" : "bargain.ready");
    });
    async Task Work(Func<Task> action, bool training = false)
    {
        if (busy || !allowGame) return;
        session.Begin(); busy = true; error = null; stop.IsEnabled = training; Render();
        try { await action(); }
        catch (StageHostException e) when (e.Kind == "stopped") { SetMessage("bargain.stopped"); }
        catch (Exception e) { error = e; session.RecordFailure(e); }
        finally { busy = false; stop.IsEnabled = false; Render(); if (closing) Close(); }
    }
    void Render()
    {
        read.IsEnabled = allowGame && !busy; characters.IsEnabled = !busy;
        start.IsEnabled = false;
        L.Text(count, "bargain.count", rounds, spent);
        if (characters.SelectedItem is DailyBargainCharacter c)
        {
            L.Text(experience, "bargain.experience", c.Level, c.BarValue, c.BarMax);
            progress.Value = c.Full ? 100 : c.BarMax > 0 ? 100d * c.BarValue / c.BarMax : 0;
            L.Text(estimate, c.Full ? "bargain.full" : "bargain.estimate", c.Full || c.PerUse <= 0 ? 0 : c.Estimate, c.Full || c.PerUse <= 0 ? 0 : c.Uses);
            L.Text(stock, "bargain.stock", potions, c.Cost);
            start.IsEnabled = allowGame && !busy && new DailyBargainPlan(c, potions).CanStart;
            if (!busy && error == null)
            {
                if (c.Full) { L.Text(status, c.Level >= c.MaxLevel ? "bargain.maximum" : "bargain.upgrade"); return; }
                if (c.PerUse <= 0) { L.Text(status, "bargain.no_growth"); return; }
                if (c.Estimate > potions) { L.Text(status, "bargain.shortage", c.Estimate, potions, c.Estimate - potions); return; }
            }
        }
        else { L.Text(identity, "bargain.intro"); estimate.Text = "—"; }
        if (error is DailyBargainFault fault) L.Text(status, fault.Code, fault.Values);
        else if (error?.Message.StartsWith("bargain.", StringComparison.Ordinal) == true) L.Text(status, error.Message);
        else if (error != null) DailyUiText.Error(status, error);
        else L.Text(status, message);
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true; closing = true; session.Stop(); stop.IsEnabled = false; SetMessage("bargain.stopping");
    }
    internal void PreviewSample(long stockCount = 390)
    {
        if (allowGame) throw new InvalidOperationException("Preview cannot access the game");
        potions = stockCount;
        characters.ItemsSource = new[] { new DailyBargainCharacter(10, 100, "Silvia", 2, 5, 605, 6052, 155, 100, 300, 5, 10) };
        characters.SelectedIndex = 0; L.Text(identity, "bargain.account", "Preview"); SetMessage("bargain.ready");
    }
    internal void CheckPreviewForSmoke()
    {
        if (allowGame || start.IsEnabled || read.IsEnabled || stop.IsEnabled || status.Text != L.Get("bargain.shortage", 290, 120, 170))
            throw new InvalidOperationException("Isolated training preview can start or lost its shortage explanation");
        var point = start.TransformToAncestor(this).Transform(new Point(0, 0));
        if (point.Y < 0 || point.Y + start.ActualHeight > ActualHeight) throw new InvalidOperationException("Training actions clipped in compact dialog");
    }
}

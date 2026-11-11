using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dustweave.Desktop;

public sealed partial class DailyBargainWindow : Window
{
    static DailyLanguage L => DailyLanguage.Current;
    readonly DailyBargainSession session;
    readonly bool allowGame;
    readonly Func<bool> canOperate;
    readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action? ActivityChanged;
    internal bool IsWorking => busy;
    internal string AccountKey { get; }
    readonly ComboBox skills = new() { MinHeight = 36, Margin = new(0, 8, 0, 10) };
    DailyBargainCharacter[] allCharacters = [];
    DailyPracticeTalent[] talentNames = [];
    readonly ComboBox characters = new() { MinHeight = 36, DisplayMemberPath = nameof(DailyBargainCharacter.Name) };
    readonly ProgressBar progress = new() { Height = 8, Maximum = 100, Margin = new(0, 10, 0, 0) };
    readonly TextBlock identity = Text(), experience = Text(), estimate = Text(18), stock = Text(), count = Text(), status = Text(), hint = Text(), costLabel = Text();
    readonly Button read = Button("practice.refresh"), start = Button("practice.start"), stop = Button("bargain.stop");
    long potions, spent;
    int rounds;
    bool busy, closing;
    string message = "bargain.intro";
    object[] messageValues = [];
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
    static Border Section(UIElement child)
    {
        var b = new Border { Padding = new(0, 16, 0, 0), Margin = new(0, 0, 0, 16), BorderThickness = new(0, 1, 0, 0), Child = child };
        b.SetResourceReference(Border.BorderBrushProperty, "Line"); return b;
    }
    public DailyBargainWindow(string root, string account, bool allowGame = true, Func<bool>? canOperate = null)
    {
        this.allowGame = allowGame;
        this.canOperate = canOperate ?? (() => true);
        AccountKey = account;
        stop.IsEnabled = false;
        progress.SetResourceReference(ForegroundProperty, "Primary");
        progress.SetResourceReference(BackgroundProperty, "Line");
        progress.BorderThickness = new(0);
        session = new(root, account);
        Width = 660; Height = 740; MinWidth = 540; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "tool.talent-practice");
        var layout = new DockPanel { Margin = new(24, 0, 24, 24) };
        var actions = new Grid { Margin = new(0, 16, 0, 0) };
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); actions.ColumnDefinitions.Add(new());
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        start.SetResourceReference(StyleProperty, "PrimaryButton"); start.Margin = new(12, 0, 0, 0);
        stop.Margin = new(12, 0, 0, 0); Grid.SetColumn(stop, 2); Grid.SetColumn(start, 3);
        actions.Children.Add(read); actions.Children.Add(stop); actions.Children.Add(start);
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        status.Margin = new(0, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        var body = new StackPanel();
        identity.Margin = new(0, 0, 0, 16); body.Children.Add(identity);
        var selection = new Grid(); selection.ColumnDefinitions.Add(new()); selection.ColumnDefinitions.Add(new());
        var skillChoice = new StackPanel { Margin = new(0, 0, 8, 0) };
        var skill = Text(); L.Text(skill, "practice.skill"); skillChoice.Children.Add(skill);
        foreach (int kind in new[] { 16, 6, 13, 2, 12, 17 })
        {
            var item = new ComboBoxItem { Tag = kind }; skills.Items.Add(item);
        }
        RefreshTalentNames();
        L.Bind(skills, System.Windows.Automation.AutomationProperties.NameProperty, "practice.skill");
        skills.SelectedIndex = 0; skillChoice.Children.Add(skills); selection.Children.Add(skillChoice);
        var characterChoice = new StackPanel { Margin = new(8, 0, 0, 0) }; Grid.SetColumn(characterChoice, 1);
        var label = Text(); L.Text(label, "bargain.character"); characterChoice.Children.Add(label);
        characters.Margin = new(0, 8, 0, 10); characterChoice.Children.Add(characters); selection.Children.Add(characterChoice);
        var choice = new StackPanel(); choice.Children.Add(BuildMode()); choice.Children.Add(selection); choice.Children.Add(experience); choice.Children.Add(progress); body.Children.Add(Section(choice));
        L.Bind(characters, System.Windows.Automation.AutomationProperties.NameProperty, "bargain.character");
        body.Children.Add(BuildQueue());
        var budget = new StackPanel(); budget.Children.Add(costLabel);
        budget.Children.Add(estimate); budget.Children.Add(stock); budget.Children.Add(count); body.Children.Add(Section(budget));
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); body.Children.Add(hint);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        session.Report = key => Dispatcher.Invoke(() => SetMessage(key));
        session.Snapshot = s => Dispatcher.Invoke(() =>
        {
            allCharacters=s.Characters;achievements=s.Achievements;potions=s.Potions;
            FilterCharacters();Render();
        });
        session.Progress = p => Dispatcher.Invoke(() =>
        {
            potions = p.Potions; rounds = p.Rounds; spent = p.Spent;
            if(p.Achievements is {} goals)achievements=goals;
            if(p.Achievement is {} goal) achievements=achievements.Where(a=>a.Kind!=goal.Kind).Append(goal).ToArray();
            allCharacters = allCharacters.Select(c => c.Character == p.Character.Character ? p.Character : c).ToArray();
            skills.SelectedItem=skills.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>(int)item.Tag==p.Character.Kind);
            mode.SelectedIndex=p.Achievement==null?0:1;
            FilterCharacters(p.Character.Character); Render();
        });
        skills.SelectionChanged += (_, _) => { FilterCharacters(); Render(); };
        characters.SelectionChanged += (_, _) => Render();
        read.Click += async (_, _) => await Refresh();
        start.Click += async (_, _) =>
        {
            error=null;
            if(jobs.All(j=>j.State=="completed")) EditSafely(AddSelected);
            if(error!=null)return;
            if(jobs.All(j=>j.State=="completed")) return;
            rounds = 0; spent = 0;
            await Work(async () => {
                await Task.Run(()=>session.TrainQueue(jobs,()=>Dispatcher.Invoke(()=>{RefreshQueue();Render();})));
                SetMessage(jobs.All(j=>j.State=="completed")?"practice.queue_complete":"practice.queue_attention");
            }, true);
        };
        stop.Click += (_, _) => { session.Stop(); stop.IsEnabled = false; SetMessage("bargain.stopping"); };
        Closing += OnClosing;
        L.Changed += LanguageChanged;
        Closed += (_, _) => { L.Changed -= LanguageChanged; closed.TrySetResult(); };
        Loaded += async (_, _) =>
        {
            // Let the host release its window-switch reservation before reading.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (allowGame && IsLoaded && !closing) await Refresh();
        };
        Render();
    }
    void SetMessage(string key, params object[] values) { message = key; messageValues = values; error = null; Render(); }
    void LanguageChanged(object? sender, EventArgs e) { RefreshTalentNames(); FilterCharacters(); RefreshQueue(); Render(); }
    void RefreshTalentNames()
    {
        foreach (ComboBoxItem item in skills.Items)
        {
            int kind = (int)item.Tag;
            item.Content = talentNames.FirstOrDefault(t => t.Kind == kind)?.Names.In(L.Code) is { Length: > 0 } name
                ? name : L.Get("practice.skill_number", kind);
        }
    }
    int Kind => skills.SelectedItem is ComboBoxItem { Tag: int kind } ? kind : 16;
    void FilterCharacters(long? selected = null)
    {
        selected ??= (characters.SelectedItem as DailyBargainCharacter)?.Character;
        var filtered = allCharacters.Where(c => c.Kind == Kind).Select(c => c with { Name = c.NameIn(L.Code) }).ToArray();
        characters.ItemsSource = filtered;
        characters.SelectedItem = AchievementMode ? DailyPracticeQueue.Cheapest(filtered,Kind) : filtered.FirstOrDefault(c => c.Character == selected) ?? filtered.FirstOrDefault();
    }
    async Task Refresh() => await Work(async () =>
    {
        long? selected = (characters.SelectedItem as DailyBargainCharacter)?.Character;
        var data = await Task.Run(session.Read);
        var removed = RefreshQueueLevels(data.Characters);
        potions = data.Potions; rounds = 0; spent = 0;
        L.Text(identity, "bargain.account", data.Player);
        allCharacters = data.Characters; talentNames = data.Talents; achievements=data.Achievements; RefreshTalentNames(); FilterCharacters(selected);RefreshQueue();
        ShowRefreshResult(removed);
    });
    async Task Work(Func<Task> action, bool training = false)
    {
        if (busy || !allowGame || !canOperate()) return;
        session.Begin(); busy = true; error = null; stop.IsEnabled = training; Render(); ActivityChanged?.Invoke();
        try { await action(); }
        catch (StageHostException e) when (e.Kind == "stopped") { SetMessage("bargain.stopped"); }
        catch (Exception e) { error = e; session.RecordFailure(e); }
        finally { busy = false; stop.IsEnabled = false; Render(); ActivityChanged?.Invoke(); if (closing) Close(); }
    }
    void Render()
    {
        bool available = allowGame && !busy && canOperate();
        read.IsEnabled = available; characters.IsEnabled = !busy&&!AchievementMode; skills.IsEnabled = !busy;
        mode.IsEnabled=!busy; add.IsEnabled=!busy&&CanAdd; queue.IsEnabled=!busy;
        foreach(var button in queueActions) button.IsEnabled=!busy&&queue.SelectedIndex>=0;
        L.Text(hint, Kind == 16 ? "bargain.hint" : "practice.field_hint");
        start.IsEnabled = false;
        L.Text(count, "bargain.count", rounds, spent);
        L.Text(costLabel,AchievementMode?"practice.achievement_cost_label":"bargain.estimate_label");
        if(AchievementMode){RenderAchievement(available);RenderQueueBudget();return;}
        if (characters.SelectedItem is DailyBargainCharacter c)
        {
            L.Text(experience, "bargain.experience", c.Level, c.BarValue, c.BarMax);
            progress.Value = c.Full ? 100 : c.BarMax > 0 ? 100d * c.BarValue / c.BarMax : 0;
            L.Text(estimate, c.Full ? "bargain.full" : "bargain.estimate", c.Full || c.PerUse <= 0 ? 0 : c.Estimate, c.Full || c.PerUse <= 0 ? 0 : c.Uses);
            L.Text(stock, "bargain.stock", potions, c.Cost);
            start.IsEnabled = available && new DailyBargainPlan(c, potions).CanStart;
            if(jobs.Any(j=>j.State!="completed"))start.IsEnabled=available;
            if (!busy && error == null && jobs.Count==0 && message!="practice.queue_levels_changed")
            {
                if (c.Full) { L.Text(status, c.Level >= c.MaxLevel ? "bargain.maximum" : "bargain.upgrade"); return; }
                if (c.PerUse <= 0) { L.Text(status, "bargain.no_growth"); return; }
                if (c.Estimate > potions) { L.Text(status, "bargain.shortage", c.Estimate, potions, c.Estimate - potions); return; }
            }
        }
        else { estimate.Text = "—"; experience.Text = ""; stock.Text = ""; progress.Value = 0; }
        RenderQueueBudget();
        if(jobs.Any(j=>j.State!="completed"))start.IsEnabled=available;
        if (error is DailyBargainFault fault) L.Text(status, fault.Code, fault.Values);
        else if (error?.Message.StartsWith("bargain.", StringComparison.Ordinal) == true || error?.Message.StartsWith("practice.", StringComparison.Ordinal) == true) L.Text(status, error!.Message);
        else if (error != null) DailyUiText.Error(status, error);
        else if(!busy && allCharacters.Length > 0 && characters.SelectedItem == null && message!="practice.queue_levels_changed") L.Text(status,"practice.none");
        else L.Text(status, message, messageValues);
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true; closing = true; session.Stop(); stop.IsEnabled = false; SetMessage("bargain.stopping");
    }
    internal void RefreshAvailability() => Render();
    internal void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    internal async Task<bool> CloseSafelyAsync()
    {
        if (closed.Task.IsCompleted) return true;
        Close();
        try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true; }
        catch (TimeoutException) { return false; }
    }
    internal void PreviewSample(long stockCount = 390)
    {
        if (allowGame) throw new InvalidOperationException("Preview cannot access the game");
        potions = stockCount;
        allCharacters = [new DailyBargainCharacter(10, 100, "Silvia", 2, 5, 605, 6052, 155, 100, 300, 5, 10)];
        talentNames = [new(16, new("样例砍价", "範例殺價", "Example bargain"))]; RefreshTalentNames();
        FilterCharacters(); L.Text(identity, "bargain.account", "Preview"); SetMessage("bargain.ready");
    }
    internal void CheckPreviewForSmoke()
    {
        if (allowGame || start.IsEnabled || read.IsEnabled || stop.IsEnabled || status.Text != L.Get("bargain.shortage", 290, 120, 170))
            throw new InvalidOperationException("Isolated training preview can start or lost its shortage explanation");
        var point = start.TransformToAncestor(this).Transform(new Point(0, 0));
        if (point.Y < 0 || point.Y + start.ActualHeight > ActualHeight) throw new InvalidOperationException("Training actions clipped in compact dialog");
    }
    internal void PreviewFieldForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Preview cannot access the game");
        potions = 1000;
        allCharacters = [
            new DailyBargainCharacter(10, 44, "Gray", 1, 5, 1205, 1, 5, 0, 17, 1, 5) { Kind = 12 },
            new DailyBargainCharacter(11, 6074, "Rafina", 1, 5, 1215, 1, 2, 0, 17, 1, 5) { Kind = 12 },
            new DailyBargainCharacter(12, 124, "Liatris", 2, 5, 1315, 1, 26, 22, 67, 2, 5) { Kind = 13 },
            new DailyBargainCharacter(13, 94, "Native current language", 2, 5, 1705, 1, 26, 22, 67, 2, 10) { Kind = 17, Names = new("游戏角色简体", "遊戲角色繁體", "Game character") }];
        talentNames = [new(12, new("游戏监测", "遊戲偵測", "Game detection")), new(17, new("游戏藏身", "遊戲藏身", "Game stealth"))];
        RefreshTalentNames();
        skills.SelectedIndex = 5;
        var expectedName = L.Code switch { "en-US" => "Game character", "zh-TW" => "遊戲角色繁體", _ => "游戏角色简体" };
        var expectedTalent = L.Code switch { "en-US" => "Game stealth", "zh-TW" => "遊戲藏身", _ => "游戏藏身" };
        if (characters.Items.Count != 1 || (characters.SelectedItem as DailyBargainCharacter)?.Name != expectedName ||
            (skills.SelectedItem as ComboBoxItem)?.Content as string != expectedTalent)
            throw new InvalidOperationException("Stealth selection or game-provided localized names were lost");
        string language = L.Code; L.Select(language == "en-US" ? "zh-TW" : "en-US"); L.Select(language);
        if ((characters.SelectedItem as DailyBargainCharacter)?.Character != 13 ||
            (characters.SelectedItem as DailyBargainCharacter)?.Name != expectedName)
            throw new InvalidOperationException("Language switch lost the selected character or game name");
        skills.SelectedIndex = 4;
        if (characters.Items.Count != 2 || (characters.SelectedItem as DailyBargainCharacter)?.Kind != 12)
            throw new InvalidOperationException("Practice character selection crossed skill classes");
        characters.SelectedIndex = 1; FilterCharacters();
        if ((characters.SelectedItem as DailyBargainCharacter)?.Character != 11)
            throw new InvalidOperationException("Practice refresh lost the selected character");
        skills.SelectedIndex = 2;
        if (characters.Items.Count != 1 || (characters.SelectedItem as DailyBargainCharacter)?.Character != 12)
            throw new InvalidOperationException("Practice skill switch retained an unrelated character");
        skills.SelectedIndex = 1;
        if (characters.Items.Count != 0 || status.Text != L.Get("practice.none"))
            throw new InvalidOperationException("Missing practice skill has no usable explanation");
        skills.SelectedIndex = 4; SetMessage("bargain.ready");
        if (hint.Text != L.Get("practice.field_hint") || start.IsEnabled)
            throw new InvalidOperationException("Practice preview has wrong route guidance or can submit");
    }
}

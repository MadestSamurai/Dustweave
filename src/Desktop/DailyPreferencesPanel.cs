using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dustweave.Desktop;

/// <summary>Edits account-scoped policy only. Saving never starts game actions.</summary>
public sealed class DailyPreferencesPanel : UserControl
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly bool allowGame;
    internal GameInstallationPanel GamePath { get; }
    internal SandboxInstallationPanel SandboxPath { get; }
    private readonly bool extensionTasks = DailyPlugin.Current.Supports("tactics");
    private readonly bool extensionEvents = DailyPlugin.Current.Supports("event_battle");
    private readonly DailyPreferenceStore store;
    private readonly string dataRoot;
    private readonly ComboBox accounts = new() { MinWidth = 230, DisplayMemberPath = "Name", SelectedValuePath = "AccountKey" };
    private readonly CheckBox eventBattle = Check("活动战斗"), eventChallenge = Check("普通全部通关后继续挑战战斗");
    private readonly ComboBox eventSearch = new();
    private readonly CheckBox tactics = Check("战术教材");
    private readonly ComboBox tacticsSearch = new();
    private readonly ComboBox chapter = new(), stone = new(), enhance = new(), multiplier = new();
    private readonly ListBox priority = new() { Height = 94, MinWidth = 240, DisplayMemberPath = "Label" };
    private readonly CheckBox hunt = Check("自动分配免费米饭"), gold = Check("金币加成日刷金币"), slime = Check("史莱姆加成日刷史莱姆");
    private readonly CheckBox stones = Check("每天刷圣石"), recycle = Check("处理本次免费抽到的装备"), keepSr = Check("保留五星角色的 SR 专武");
    private readonly CheckBox fallback = Check("分解任务未完成且无可分解装备时，制作一把 N 剑"), refine = Check("有精炼任务时精炼一次");
    private readonly CheckBox pass = Check("检查并领取通行证任务与等级奖励"), dispatch = Check("领取到期派遣并继续派遣");
    private readonly CheckBox goddess = Check("领取女神像奖励"), ranking = Check("检查广场排行榜周期奖励");
    private readonly CheckBox mirror = Check("镜中之战"), guild = Check("公会签到"), room = Check("小屋奖励"),
        income = Check("经营一键领取"), guests = Check("餐厅特殊顾客与气泡"), draws = Check("每日免费抽取"),
        dailyRewards = Check("日常任务奖励"), weeklyRewards = Check("周常任务奖励"), mail = Check("邮箱物品");
    private readonly CheckBox friendship = Check("每日亲密度咨询"), quickFriendship = Check("prefs.friendship_quick");
    private readonly CheckBox weeklyBook = Check("周常末日之书"), weeklyCraft = Check("周常制作装备"), weeklySichuan = Check("周常连连看");
    private readonly CheckBox mainline = Check("周收集"), weeklyNpc = Check("周NPC任务"), npcHunting = Check("允许接取必须去狩猎场的任务"), weeklySteal = Check("每周偷窃"), walkCollect = Check("步行收集（不使用吸收）"), monsterHunt = Check("魔兽最高档快速战斗"), fishing = Check("周常钓鱼"), roomLikes = Check("周常小屋点赞");
    private readonly CheckBox events = Check("活动奖励"), eventMissions = Check("领取活动任务与活动代币"), roulette = Check("使用免费次数和已有转盘券"), exchange = Check("活动代币最大批量兑换"), quiz = Check("自动完成已开放答题"), dice = Check("自动使用现有活动骰子"), puzzle = Check("拼图板使用现有代币批量翻开");
    private readonly CheckBox trade = Check("自动跑商、料理与120%售卖");
    private readonly TextBox tradeReserve = new(), tradeMaximum = new();
    private readonly TextBox potionReserve = new(), potionTarget = new();
    private readonly CheckBox potionRefill = Check("药不足时自动补药");
    private readonly CheckBox tradeBreakEven = Check("购买零收益商品（金币成就）");
    private StackPanel section = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly ComboBox refineChoice = new() { DisplayMemberPath = "Label", SelectedValuePath = "Instance", MinWidth = 210, MaxWidth = 660 };
    private readonly TextBlock inventoryState = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    private readonly StackPanel form = new();
    private readonly TextBox filter = new() { MinWidth = 240, ToolTip = "按环节名称或说明筛选", Margin = new(8, 0, 0, 0) };
    private readonly List<(Border View, string Title, string Summary)> sections = new();
    private readonly TextBlock filterCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0) };
    private DailyPreferences? loaded;
    private string account = "";
    private bool updating, dirty;
    private sealed class Choice(string key, string source) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Key => key;
        public string Label => L.Translate(source);
        public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class RefinementView(string instance, Func<string> label) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Instance => instance;
        public string Label => label();
        public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    private static TextBlock Label(string source)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        L.Literal(text, source); return text;
    }
    private static Button LocalButton(string source)
    {
        var button = new Button();
        L.Bind(button, ContentControl.ContentProperty, () => L.Translate(source));
        return button;
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in stone.Items.Cast<Choice>().Concat(priority.Items.Cast<Choice>())) item.Refresh();
        foreach (var item in refineChoice.Items.Cast<RefinementView>()) item.Refresh();
        FilterSections();
    }
    public DailyPreferencesPanel(string root, bool allowGame = true)
    {
        this.allowGame = allowGame;
        store = new(root);
        dataRoot = root;
        var layout = new DockPanel { Margin = new(20) };
        DockPanel.SetDock(status, Dock.Bottom);
        layout.Children.Add(status);
        var installation = new Dustweave.Accounts.GameInstallation(allowGame ? null : Path.Combine(root, "game-path-preview"));
        GamePath = new GameInstallationPanel(installation, allowGame && Dustweave.Accounts.SandboxProcessScope.CurrentBox.Length != 0);
        DockPanel.SetDock(GamePath, Dock.Top);
        layout.Children.Add(GamePath);
        SandboxPath = new SandboxInstallationPanel(new Dustweave.Accounts.SandboxInstallation(allowGame ? null : Path.Combine(root, "sandbox-path-preview")),
            allowGame && Dustweave.Accounts.SandboxProcessScope.CurrentBox.Length != 0);
        DockPanel.SetDock(SandboxPath, Dock.Top);
        layout.Children.Add(SandboxPath);
        var header = new DockPanel { Margin = new(0, 0, 0, 16) };
        var save = LocalButton("prefs.save"); save.Style = (Style)Application.Current.FindResource("PrimaryButton");
        save.Click += (_, _) => Save();
        DockPanel.SetDock(save, Dock.Right);
        header.Children.Add(save);
        var accountGroup = new Grid { Margin = new(0, 0, 16, 0), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        accountGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accountGroup.ColumnDefinitions.Add(new ColumnDefinition());
        var accountLabel = Label("prefs.account"); accountLabel.VerticalAlignment = VerticalAlignment.Center; accountLabel.Margin = new(0, 0, 12, 0); accountGroup.Children.Add(accountLabel);
        accounts.MinWidth = 160;
        accounts.MaxWidth = 280;
        Grid.SetColumn(accounts, 1);
        accounts.VerticalAlignment = VerticalAlignment.Center;
        accountGroup.Children.Add(accounts);
        header.Children.Add(accountGroup);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var searchBar = new WrapPanel { Margin = new(0, 0, 0, 16) };
        var searchLabel = Label("查找环节"); searchLabel.VerticalAlignment = VerticalAlignment.Center; searchBar.Children.Add(searchLabel);
        L.Bind(filter, System.Windows.Automation.AutomationProperties.NameProperty, "prefs.search_accessible");
        L.Bind(filter, FrameworkElement.ToolTipProperty, "prefs.search_help");
        searchBar.Children.Add(filter);
        searchBar.Children.Add(filterCount);
        DockPanel.SetDock(searchBar, Dock.Top);
        layout.Children.Add(searchBar);
        filter.TextChanged += (_, _) => FilterSections();
        chapter.ItemsSource = Enumerable.Range(1, 10).ToArray();
        enhance.ItemsSource = Enumerable.Range(0, 10).ToArray();
        stone.ItemsSource = new[] { new Choice("least", "库存数量最少"), new Choice("fire", "火"), new Choice("water", "水"), new Choice("wind", "风"), new Choice("light", "光"), new Choice("dark", "暗") };
        stone.DisplayMemberPath = "Label";
        stone.SelectedValuePath = "Key";
        section = form;
        var generalBody = new StackPanel { Margin = new(24, 8, 12, 16) };
        var generalTitle = Label("整体设置"); generalTitle.FontWeight = FontWeights.SemiBold;
        var general = new Border { BorderBrush = (Brush)Application.Current.FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = new Expander { Header = generalTitle, Content = generalBody, IsExpanded = true, Margin = new(0, 8, 0, 8), Style = (Style)Application.Current.FindResource("SettingsExpander") } };
        form.Children.Add(general); sections.Add((general, "整体设置", "天赋药保留下限与自动补药")); section = generalBody;
        Row("天赋药保留下限", potionReserve);
        Note("默认0。只允许消耗保留量以外的天赋药；不足时在队列中说明受限任务，不影响其他可执行任务。仅适用于日常队列。");
        section.Children.Add(potionRefill);
        Row("补药至", potionTarget);
        Note("默认不购买。开启后，药不足时临时加入补药环节；须已完成本周至少一条NPC任务，并确认广场商人的天赋药享九折。补至目标数量后接续任务。");
        potionRefill.Checked += (_, _) => potionTarget.IsEnabled = true;
        potionRefill.Unchecked += (_, _) => potionTarget.IsEnabled = false;
        L.Bind(potionReserve, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Translate("天赋药保留下限"));
        L.Bind(potionTarget, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Translate("补药至"));
        Stage(guild, "公会签到", "签到与领取可用奖励", showSummary: false);
        Stage(room, "小屋奖励", "领取可用的小屋奖励", showSummary: false);
        Stage(income, "经营一键领取", "prefs.business_hint");
        Stage(guests, "餐厅特殊顾客与气泡", "领取属性圣石和限时装饰币");
        Stage(draws, "每日免费抽取", "prefs.draws_hint");
        Stage(recycle, "装备分解", "只处理本日免费抽取的可分解装备");
        section.Children.Add(keepSr);
        Row("分解前强化等级", enhance);
        Note("prefs.enhance_note");
        section.Children.Add(fallback);
        Stage(weeklyCraft, "周常制作装备", "prefs.craft_hint");
        Note("prefs.craft_note");
        Stage(refine, "装备精炼", "prefs.refine_hint");
        section.Children.Add(refineChoice);
        section.Children.Add(inventoryState);
        var refreshInventory = LocalButton("刷新可选装备"); refreshInventory.HorizontalAlignment = HorizontalAlignment.Left; refreshInventory.Margin = new(0, 4, 0, 8);
        refreshInventory.Click += (_, _) => RefreshInventory();
        section.Children.Add(refreshInventory);
        Note("prefs.refine_note");
        Stage(hunt, "普通狩猎与奖励日", "prefs.hunt_hint");
        Row("普通狩猎关卡", chapter);
        section.Children.Add(gold);
        section.Children.Add(slime);
        Note("prefs.priority_note");
        var rankingRow = new StackPanel { Orientation = Orientation.Horizontal };
        rankingRow.Children.Add(priority);
        var moves = new StackPanel { Margin = new(8, 0, 0, 0) };
        foreach (var spec in new[] { ("上移", -1), ("下移", 1) })
        {
            var b = LocalButton(spec.Item1); b.Margin = new(0, 0, 0, 6);
            int delta = spec.Item2;
            b.Click += (_, _) => Move(delta);
            moves.Children.Add(b);
        }
        rankingRow.Children.Add(moves);
        section.Children.Add(rankingRow);
        Stage(stones, "圣石狩猎", "按所选属性用完剩余免费火炬");
        Row("圣石属性", stone);
        Note("prefs.stones_note");
        Stage(mirror, "镜中之战", "用完实际剩余免费次数", expanded: true);
        multiplier.ItemsSource = Enumerable.Range(1, 40).ToArray();
        Row("每轮倍率", multiplier);
        Note("prefs.multiplier_note");
        Stage(dispatch, "每日派遣", "prefs.dispatch_hint");
        Stage(goddess, "女神像奖励", "进入广场领取当日可用奖励", showSummary: false);
        Stage(ranking, "广场排行榜奖励", "prefs.ranking_hint");
        tacticsSearch.ItemsSource = new[] { 15, 30, 45, 60, 120 };
        if (extensionTasks)
        {
            Stage(tactics, "战术教材", "按当前配置执行");
            Row("每关搜索秒数", tacticsSearch);
        }
        Stage(eventBattle, extensionEvents ? "活动战斗" : "活动每日挑战15", extensionEvents ? "按当前配置执行" : "prefs.event_sweep_hint", expanded: true);
        eventSearch.ItemsSource = new[] { 5, 15, 30, 60, 120 };
        if (extensionEvents)
        {
            section.Children.Add(eventChallenge);
            Row("每关搜索秒数", eventSearch);
        }
        if (extensionEvents) Note("仅使用免费次数，完成后保留游戏进度。");
        Stage(weeklyBook, "周常末日之书", "prefs.book_hint");
        Stage(friendship, "亲密度咨询", "prefs.friendship_hint");
        section.Children.Add(quickFriendship);
        Stage(monsterHunt, "魔兽快速战斗", "prefs.monster_hint");
        Stage(mainline, "周收集", "prefs.collection_hint", expanded: true);
        section.Children.Add(walkCollect);
        Note("prefs.collection_note");
        Stage(weeklyNpc, "周NPC任务", "prefs.npc_hint", expanded: true);
        section.Children.Add(npcHunting);
        Note("prefs.npc_note");
        Stage(weeklySteal, "每周偷窃", "prefs.steal_hint");
        Stage(roomLikes, "周常小屋点赞", "prefs.likes_hint");
        Stage(fishing, "周常钓鱼", "prefs.fishing_hint");
        Stage(weeklySichuan, "周常连连看", "prefs.tiles_hint");
        Stage(trade, "跑商、料理与高价售卖", "prefs.trade_hint", expanded: true, editableWhenOff: true);
        Row("保留金币", tradeReserve);
        Row("本轮最多垫资", tradeMaximum);
        L.Bind(tradeReserve, System.Windows.Automation.AutomationProperties.NameProperty, "trade.reserve");
        L.Bind(tradeMaximum, System.Windows.Automation.AutomationProperties.NameProperty, "trade.budget");
        Note("prefs.trade_budget_help");
        section.Children.Add(tradeBreakEven);
        Note("trade.break_even_hint");
        Note("prefs.trade_note");
        var tradeButton = LocalButton("预览跑商计划"); tradeButton.HorizontalAlignment = HorizontalAlignment.Left;
        tradeButton.Click += (_, _) => { if (account.Length == 0 || !Save()) return; var trade = new Window { Title = L.Translate("跑商计划"), Width = 1060, Height = 780, MinWidth = 760, MinHeight = 560, Owner = Window.GetWindow(this), Content = new TradePlanPanel(dataRoot, account, allowGame), WindowStartupLocation = WindowStartupLocation.CenterOwner }; DailyDialogs.Prepare(trade); trade.Show(); };
        L.Bind(tradeButton, FrameworkElement.ToolTipProperty, "prefs.trade_preview_help");
        section.Children.Add(tradeButton);
        var bargainButton = LocalButton("bargain.title");
        bargainButton.HorizontalAlignment = HorizontalAlignment.Left;
        bargainButton.Margin = new(0, 8, 0, 0);
        bargainButton.Click += (_, _) =>
        {
            if (account.Length == 0 || !SavePending()) return;
            var dialog = new DailyBargainWindow(dataRoot, account, allowGame) { Owner = Window.GetWindow(this) };
            DailyDialogs.ShowModal(dialog);
        };
        section.Children.Add(bargainButton);
        Stage(events, "活动游戏与领奖", "prefs.events_hint");
        section.Children.Add(eventMissions);
        section.Children.Add(quiz);
        section.Children.Add(dice);
        section.Children.Add(puzzle);
        section.Children.Add(roulette);
        section.Children.Add(exchange);
        Note("prefs.events_note");
        Stage(dailyRewards, "日常任务奖励", "在行动结束后领取新完成的日常任务");
        Stage(weeklyRewards, "周常任务奖励", "在行动结束后领取新完成的周常任务");
        Stage(pass, "通行证奖励", "检查各通行证的任务与等级奖励", showSummary: false);
        Stage(mail, "邮箱物品", "prefs.mail_hint");
        // Stages without parameters need only a switch, not an empty expansion affordance.
        foreach (var border in form.Children.OfType<Border>())
            if (border.Child is Expander exp && exp.Content is StackPanel body && body.Children.Count == 0 && exp.Header is StackPanel heading)
            {
                exp.Header = null;
                heading.Margin = new(24, 8, 0, 8);
                border.Child = heading;
            }
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = new Border { Style = (Style)Application.Current.FindResource("Panel"), Child = layout };
        accounts.SelectionChanged += ChangedAccount;
        foreach (var c in new[] { weeklyBook, weeklyCraft, weeklySichuan, quickFriendship, friendship, tactics, eventBattle, eventChallenge, hunt, gold, slime, stones, recycle, keepSr, fallback, refine, pass, dispatch, goddess, ranking, mirror, guild, room, income, guests, draws, dailyRewards, weeklyRewards, mail, mainline, weeklyNpc, npcHunting, weeklySteal, walkCollect, monsterHunt, fishing, roomLikes, trade, events, eventMissions, quiz, dice, puzzle, roulette, exchange })
        {
            c.Checked += (_, _) => MarkDirty();
            c.Unchecked += (_, _) => MarkDirty();
        }
        foreach (var c in new[] { tacticsSearch, eventSearch, chapter, stone, enhance, refineChoice, multiplier })
            c.SelectionChanged += (_, _) => MarkDirty();
        tradeReserve.TextChanged += (_, _) => MarkDirty();
        tradeMaximum.TextChanged += (_, _) => MarkDirty();
        potionReserve.TextChanged += (_, _) => MarkDirty();
        potionTarget.TextChanged += (_, _) => MarkDirty();
        potionRefill.Checked += (_, _) => MarkDirty();
        potionRefill.Unchecked += (_, _) => MarkDirty();
        tradeBreakEven.Checked += (_, _) => MarkDirty();
        tradeBreakEven.Unchecked += (_, _) => MarkDirty();
        form.IsEnabled = false;
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
    }
    public void CheckWeeklySettingsForSmoke()
    {
        var previous = mainline.IsChecked;
        try
        {
            mainline.IsChecked = false;
            if (!weeklySteal.IsEnabled || !weeklyNpc.IsEnabled)
                throw new Exception("Independent weekly tasks are locked when collection is disabled");
            mainline.IsChecked = true;
            if (walkCollect.Parent is not StackPanel body || body.Children.OfType<Control>().Single() != walkCollect || !walkCollect.IsEnabled)
                throw new Exception("Weekly collection should expose only its walking preference");
            if (loaded == null || walkCollect.IsChecked != loaded.Weekly.WalkCollect)
                throw new Exception("Walking collection choice differs from saved account preference");
        }
        finally { mainline.IsChecked = previous; }
    }
    private static CheckBox Check(string label)
    {
        // Wrapping keeps long translated options readable at compact widths.
        return new CheckBox { Content = Label(label), Margin = new(0, 6, 0, 6) };
    }
    private void Stage(CheckBox toggle, string title, string summary, bool expanded = false, bool editableWhenOff = false, bool showSummary = true)
    {
        toggle.Content = Label(title);
        toggle.FontWeight = FontWeights.SemiBold;
        toggle.MinWidth = 200;
        var header = new StackPanel();
        header.Children.Add(toggle);
        if (showSummary)
        {
            var summaryText = Label(summary);
            summaryText.Margin = new(24, 0, 12, 4);
            summaryText.FontSize = 13;
            summaryText.Foreground = (Brush)Application.Current.FindResource("MutedInk");
            header.Children.Add(summaryText);
        }
        var body = new StackPanel { Margin = new(48, 8, 12, 12), IsEnabled = editableWhenOff || toggle.IsChecked == true };
        toggle.Checked += (_, _) => body.IsEnabled = true;
        toggle.Unchecked += (_, _) => body.IsEnabled = editableWhenOff;
        var expander = new Expander
        {
            Style = (Style)Application.Current.FindResource("SettingsExpander"),
            Header = header,
            Content = body,
            IsExpanded = expanded,
            Margin = new(0, 8, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        L.Bind(expander, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Translate(title));
        var border = new Border { BorderBrush = (Brush)Application.Current.FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = expander };
        form.Children.Add(border);
        sections.Add((border, title, summary));
        section = body;
    }
    private void FilterSections()
    {
        string query = filter.Text.Trim();
        int count = 0;
        foreach (var (view, title, summary) in sections)
        {
            bool show = (title + " " + summary + " " + L.Translate(title) + " " + L.Translate(summary)).Contains(query, StringComparison.OrdinalIgnoreCase);
            view.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
                count++;
        }
        L.Bind(filterCount, TextBlock.TextProperty, () => query.Length == 0 ? "" : count == 0 ? L.Get("prefs.none") : L.Get("prefs.count", count));
    }
    private void Note(string text)
    {
        var note = Label(text); note.MaxWidth = 720; note.FontSize = 13; note.HorizontalAlignment = HorizontalAlignment.Left;
        note.Foreground = (Brush)Application.Current.FindResource("MutedInk"); note.Margin = new(0, 3, 0, 8);
        section.Children.Add(note);
    }
    private void Row(string label, Control field)
    {
        var row = new Grid { Margin = new(0, 6, 0, 6), MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        var text = Label(label); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new(0, 0, 16, 0); row.Children.Add(text);
        field.MaxWidth = 320;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        section.Children.Add(row);
    }
    private void MarkDirty()
    {
        if (!updating && loaded != null)
        {
            dirty = true;
            L.Text(status, "prefs.dirty");
        }
    }
    public void SetAccounts(IEnumerable<DailyAccount> rows)
    {
        var values = rows.Where(a => a.Valid).ToArray();
        // Keep unsaved edits when other tabs refresh. Names update without changing the scope.
        if (accounts.ItemsSource is DailyAccount[] old && old.SequenceEqual(values))
            return;
        updating = true;
        accounts.ItemsSource = values;
        accounts.SelectedValue = account;
        updating = false;
        if (accounts.SelectedItem == null && values.Length > 0)
            accounts.SelectedIndex = 0;
        if (values.Length == 0)
        {
            form.IsEnabled = false;
            L.Text(status, "prefs.no_account");
        }
        else if (loaded != null && accounts.SelectedValue as string == account && !form.IsEnabled)
        {
            // A temporarily empty catalog can return with the same account and no
            // SelectionChanged event. Restore editing without discarding unsaved settings.
            form.IsEnabled = true;
            L.Text(status, dirty ? "prefs.dirty" : "prefs.loaded");
        }
    }
    private void ChangedAccount(object sender, SelectionChangedEventArgs e)
    {
        if (updating)
            return;
        string next = accounts.SelectedValue as string ?? "";
        if (next == account)
            return;
        if (dirty && !Save())
        {
            updating = true;
            accounts.SelectedValue = account;
            updating = false;
            return;
        }
        account = next;
        try
        {
            loaded = store.Read(account);
            updating = true;
            potionReserve.Text = loaded.General.TalentPotionReserve.ToString(CultureInfo.InvariantCulture);
            potionTarget.Text = loaded.General.TalentPotionTarget.ToString(CultureInfo.InvariantCulture);
            potionRefill.IsChecked = loaded.General.RefillTalentPotions;
            potionTarget.IsEnabled = loaded.General.RefillTalentPotions;
            hunt.IsChecked = loaded.Hunt.Enabled;
            gold.IsChecked = loaded.Hunt.FarmGold;
            slime.IsChecked = loaded.Hunt.FarmSlime;
            chapter.SelectedItem = loaded.Hunt.OrdinaryChapter;
            stones.IsChecked = loaded.Hunt.StonesEnabled;
            stone.SelectedValue = loaded.Hunt.StoneElement;
            priority.ItemsSource = loaded.Hunt.Priority.Select(k => new Choice(k, k == "gold" ? "金币加成" : k == "slime" ? "史莱姆加成" : "普通狩猎")).ToList();
            priority.SelectedIndex = 0;
            recycle.IsChecked = loaded.Equipment.Enabled;
            keepSr.IsChecked = loaded.Equipment.KeepFiveStarSr;
            enhance.SelectedItem = loaded.Equipment.EnhanceLevel;
            fallback.IsChecked = loaded.Equipment.CraftFallback;
            refine.IsChecked = loaded.Equipment.RefineEnabled;
            mirror.IsChecked = loaded.Mirror.Enabled;
            multiplier.SelectedItem = loaded.Mirror.Multiplier;
            guild.IsChecked = loaded.Stages.Guild;
            room.IsChecked = loaded.Stages.Room;
            income.IsChecked = loaded.Stages.CafeteriaIncome;
            guests.IsChecked = loaded.Stages.CafeteriaGuests;
            draws.IsChecked = loaded.Stages.FreeDraws;
            weeklySichuan.IsChecked = loaded.Weekly.Sichuan;
            weeklyBook.IsChecked = loaded.Weekly.Book;
            weeklyCraft.IsChecked = loaded.Weekly.EquipmentCraft;
            dailyRewards.IsChecked = loaded.Stages.DailyRewards;
            weeklyRewards.IsChecked = loaded.Stages.WeeklyRewards;
            mail.IsChecked = loaded.Stages.Mail;
            mainline.IsChecked = loaded.Weekly.Mainline;
            weeklySteal.IsChecked = loaded.Weekly.Steal;
            weeklyNpc.IsChecked = loaded.Weekly.Npc;
            npcHunting.IsChecked = loaded.Weekly.NpcHunting;
            walkCollect.IsChecked = loaded.Weekly.WalkCollect;
            tactics.IsChecked = loaded.Tactics.Enabled;
            tacticsSearch.SelectedItem = loaded.Tactics.SearchSeconds;
            eventBattle.IsChecked = loaded.EventBattle.Enabled;
            eventChallenge.IsChecked = loaded.EventBattle.Challenge;
            eventSearch.SelectedItem = loaded.EventBattle.SearchSeconds;
            quickFriendship.IsChecked = loaded.Friendship.Quick;
            friendship.IsChecked = loaded.Friendship.Enabled;
            trade.IsChecked = loaded.Trade.Enabled;
            var tradeSettings = DailyTradePlan.ReadSettings(dataRoot, account);
            tradeReserve.Text = DailyData.N(tradeSettings["reserve_gold"]).ToString(CultureInfo.InvariantCulture);
            tradeMaximum.Text = tradeSettings["max_spend"] == null ? "" : DailyData.N(tradeSettings["max_spend"]).ToString(CultureInfo.InvariantCulture);
            tradeBreakEven.IsChecked = tradeSettings["include_break_even_resales"]!.GetValue<bool>();
            monsterHunt.IsChecked = loaded.MonsterHunt.Enabled;
            fishing.IsChecked = loaded.Weekly.Fishing;
            roomLikes.IsChecked = loaded.Weekly.RoomLikes;
            RefreshInventory();
            events.IsChecked = loaded.Events.Enabled;
            eventMissions.IsChecked = loaded.Events.Missions;
            quiz.IsChecked = loaded.Events.Quiz;
            dice.IsChecked = loaded.Events.Dice;
            puzzle.IsChecked = loaded.Events.Puzzle;
            roulette.IsChecked = loaded.Events.Roulette;
            exchange.IsChecked = loaded.Events.Exchange;
            pass.IsChecked = loaded.Tasks.Pass;
            dispatch.IsChecked = loaded.Tasks.Dispatch;
            goddess.IsChecked = loaded.Tasks.Goddess;
            ranking.IsChecked = loaded.Tasks.SquareRanking;
            form.IsEnabled = true;
            dirty = false;
            L.Text(status, "prefs.loaded");
        }
        catch (Exception ex) { loaded = null; form.IsEnabled = false; DailyUiText.Error(status, ex, "无法读取偏好："); }
        finally { updating = false; }
    }
    private void RefreshInventory()
    {
        bool previousUpdating = updating;
        updating = true;
        try
        {
            string selected = refineChoice.SelectedValue as string ?? loaded?.Equipment.RefineInstance ?? "";
            // An account switch must not retain the previous account's selection.
            if (previousUpdating)
                selected = loaded?.Equipment.RefineInstance ?? "";
            var choices = new List<RefinementView> { new("", () => L.Get("prefs.refine_auto")) };
            var inventory = RefinementChoices.Read(dataRoot, account);
            if (inventory != null)
                choices.AddRange(inventory.Items.Select(item => new RefinementView(item.Instance, () => item.Label)));
            if (selected.Length > 0 && !choices.Any(r => r.Instance == selected))
                choices.Add(new(selected, () => L.Get("prefs.gear_unavailable", selected)));
            refineChoice.ItemsSource = choices;
            refineChoice.SelectedValue = selected;
            if (inventory == null) L.Text(inventoryState, "prefs.inventory_missing");
            else L.Text(inventoryState, "prefs.inventory_time", DateTimeOffset.Parse(inventory.CapturedUtc).ToLocalTime().ToString("MM-dd HH:mm"));
        }
        catch (Exception ex) { string selected = loaded?.Equipment.RefineInstance ?? ""; refineChoice.ItemsSource = new[] { new RefinementView(selected, () => selected.Length == 0 ? L.Get("prefs.refine_auto") : L.Get("prefs.gear_unavailable", selected)) }; refineChoice.SelectedValue = selected; DailyUiText.Error(inventoryState, ex); }
        finally { updating = previousUpdating; }
    }
    private void Move(int delta)
    {
        if (priority.ItemsSource is not List<Choice> items)
            return;
        int i = priority.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= items.Count)
            return;
        (items[i], items[j]) = (items[j], items[i]);
        priority.Items.Refresh();
        priority.SelectedIndex = j;
        MarkDirty();
    }
    public bool Save()
    {
        if (loaded == null || account.Length == 0)
            return false;
        try
        {
            var general = new GeneralPreferences { TalentPotionReserve = PotionAmount(potionReserve), TalentPotionTarget = PotionAmount(potionTarget), RefillTalentPotions = potionRefill.IsChecked == true };
            general.Validate();
            long keep = TradeAmount(tradeReserve);
            long? limit = tradeMaximum.Text.Trim().Length == 0 ? null : TradeAmount(tradeMaximum);
            // Merge the visible fields only: retain existing ingredient reserves and other policy.
            var tradeSettings = DailyTradePlan.ReadSettings(dataRoot, account);
            var previousTradeSettings = tradeSettings.ToJsonString();
            tradeSettings["reserve_gold"] = keep;
            tradeSettings["max_spend"] = limit;
            tradeSettings["include_break_even_resales"] = tradeBreakEven.IsChecked == true;
            loaded.General = general;
            loaded.Hunt.Enabled = hunt.IsChecked == true;
            loaded.Hunt.FarmGold = gold.IsChecked == true;
            loaded.Hunt.FarmSlime = slime.IsChecked == true;
            loaded.Hunt.OrdinaryChapter = (int)chapter.SelectedItem;
            loaded.Hunt.StonesEnabled = stones.IsChecked == true;
            loaded.Hunt.StoneElement = (string)stone.SelectedValue;
            loaded.Hunt.Priority = priority.Items.Cast<Choice>().Select(c => c.Key).ToArray();
            loaded.Equipment.Enabled = recycle.IsChecked == true;
            loaded.Equipment.KeepFiveStarSr = keepSr.IsChecked == true;
            loaded.Equipment.EnhanceLevel = (int)enhance.SelectedItem;
            loaded.Equipment.CraftFallback = fallback.IsChecked == true;
            loaded.Equipment.RefineEnabled = refine.IsChecked == true;
            loaded.Equipment.RefineInstance = refineChoice.SelectedValue as string ?? loaded.Equipment.RefineInstance;
            loaded.Tasks.Pass = pass.IsChecked == true;
            loaded.Tasks.Dispatch = dispatch.IsChecked == true;
            loaded.Tasks.Goddess = goddess.IsChecked == true;
            loaded.Tasks.SquareRanking = ranking.IsChecked == true;
            loaded.Mirror.Enabled = mirror.IsChecked == true;
            loaded.Mirror.Multiplier = (int)multiplier.SelectedItem;
            loaded.Stages.Guild = guild.IsChecked == true;
            loaded.Stages.Room = room.IsChecked == true;
            loaded.Stages.CafeteriaIncome = income.IsChecked == true;
            loaded.Stages.CafeteriaGuests = guests.IsChecked == true;
            loaded.Stages.FreeDraws = draws.IsChecked == true;
            loaded.Stages.DailyRewards = dailyRewards.IsChecked == true;
            loaded.Weekly.Sichuan = weeklySichuan.IsChecked == true;
            loaded.Weekly.Book = weeklyBook.IsChecked == true;
            loaded.Weekly.EquipmentCraft = weeklyCraft.IsChecked == true;
            loaded.Stages.WeeklyRewards = weeklyRewards.IsChecked == true;
            loaded.Stages.Mail = mail.IsChecked == true;
            loaded.Weekly.Mainline = mainline.IsChecked == true;
            loaded.Weekly.Steal = weeklySteal.IsChecked == true;
            loaded.Weekly.Npc = weeklyNpc.IsChecked == true;
            loaded.Weekly.NpcHunting = npcHunting.IsChecked == true;
            loaded.Weekly.WalkCollect = walkCollect.IsChecked == true;
            loaded.Weekly.Fishing = fishing.IsChecked == true;
            loaded.Weekly.RoomLikes = roomLikes.IsChecked == true;
            if (extensionTasks)
            {
                loaded.Tactics.Enabled = tactics.IsChecked == true;
                loaded.Tactics.SearchSeconds = tacticsSearch.SelectedItem is int tacticSeconds ? tacticSeconds : 45;
            }
            loaded.EventBattle.Enabled = eventBattle.IsChecked == true;
            if (extensionEvents)
            {
                loaded.EventBattle.Challenge = eventChallenge.IsChecked == true;
                loaded.EventBattle.SearchSeconds = eventSearch.SelectedItem is int seconds ? seconds : 15;
            }
            loaded.Friendship.Quick = quickFriendship.IsChecked == true;
            loaded.Friendship.Enabled = friendship.IsChecked == true;
            loaded.MonsterHunt.Enabled = monsterHunt.IsChecked == true;
            loaded.Trade.Enabled = trade.IsChecked == true;
            loaded.Events.Enabled = events.IsChecked == true;
            loaded.Events.Missions = eventMissions.IsChecked == true;
            loaded.Events.Quiz = quiz.IsChecked == true;
            loaded.Events.Dice = dice.IsChecked == true;
            loaded.Events.Puzzle = puzzle.IsChecked == true;
            loaded.Events.Roulette = roulette.IsChecked == true;
            loaded.Events.Exchange = exchange.IsChecked == true;
            store.Save(account, loaded);
            if (tradeSettings.ToJsonString() != previousTradeSettings)
                DailyJson.Write(DailyTradePlan.SettingsPath(dataRoot, account), tradeSettings);
            dirty = false;
            L.Text(status, "prefs.saved");
            return true;
        }
        catch (Exception ex) { DailyUiText.Error(status, ex, "保存失败："); return false; }
    }
    private static long PotionAmount(TextBox field)
    {
        if (!long.TryParse(field.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value > int.MaxValue)
        {
            field.BringIntoView(); field.Focus();
            throw new InvalidDataException("天赋药数量须为非负整数；原输入已保留。");
        }
        return value;
    }
    private static long TradeAmount(TextBox field)
    {
        if (!long.TryParse(field.Text.Trim().Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value > 1_000_000_000_000)
        {
            field.BringIntoView();
            field.Focus();
            throw new InvalidDataException("金币设置须为0至一万亿之间的整数；原输入已保留。");
        }
        return value;
    }
    internal void CheckSearchForSmoke()
    {
        filter.Text = "镜中";
        if (sections.Count(s => s.View.Visibility == Visibility.Visible) != 1)
            throw new Exception("Settings search did not narrow to Mirror");
        filter.Text = "没有这个环节";
        if (sections.Any(s => s.View.Visibility == Visibility.Visible))
            throw new Exception("Unknown settings query has visible matches");
        filter.Text = "";
        if (sections.Any(s => s.View.Visibility != Visibility.Visible))
            throw new Exception("Clearing settings search did not restore sections");
    }
    internal void ShowWeeklySettingsForSmoke()
    {
        filter.Text = "周";
        var exp = form.Children.OfType<Border>().Select(b => b.Child).OfType<Expander>().Single(e => e.Content is StackPanel body && body.Children.Contains(npcHunting));
        exp.IsExpanded = true;
        UpdateLayout();
        var first = form.Children.OfType<Border>().Single(b => b.Child is Expander e && e.Header is StackPanel heading && heading.Children.Contains(mainline));
        for (DependencyObject? node = form; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll)
            {
                scroll.ScrollToVerticalOffset(first.TranslatePoint(new Point(), form).Y);
                break;
            }
        if (!weeklySteal.IsEnabled || !weeklyNpc.IsEnabled)
            throw new Exception("Independent weekly switches are inaccessible");
    }
    internal void CheckOptionalSettingsForSmoke()
    {
        if (!extensionTasks && (tactics.Parent != null || tacticsSearch.Parent != null || sections.Any(s => s.Title == "战术教材")))
            throw new Exception("Unavailable optional task remains visible");
        if (!extensionEvents && (eventChallenge.Parent != null || eventSearch.Parent != null))
            throw new Exception("Unavailable optional settings remain visible");
        if (loaded == null || string.IsNullOrWhiteSpace(account)) throw new Exception("Settings fixture was not loaded");
        var oldTactics = (loaded.Tactics.Enabled, loaded.Tactics.SearchSeconds);
        var oldEvent = (loaded.EventBattle.Challenge, loaded.EventBattle.SearchSeconds);
        try
        {
            if (!extensionTasks) { loaded.Tactics.Enabled = true; loaded.Tactics.SearchSeconds = 120; }
            if (!extensionEvents) { loaded.EventBattle.Challenge = true; loaded.EventBattle.SearchSeconds = 120; }
            if (!Save()) throw new Exception("Could not save settings fixture");
            var saved = store.Read(account);
            if (!extensionTasks && (!saved.Tactics.Enabled || saved.Tactics.SearchSeconds != 120))
                throw new Exception("Saving visible settings erased hidden task configuration");
            if (!extensionEvents && (!saved.EventBattle.Challenge || saved.EventBattle.SearchSeconds != 120))
                throw new Exception("Saving visible settings erased hidden options");
        }
        finally
        {
            (loaded.Tactics.Enabled, loaded.Tactics.SearchSeconds) = oldTactics;
            (loaded.EventBattle.Challenge, loaded.EventBattle.SearchSeconds) = oldEvent;
            store.Save(account, loaded);
        }
    }
    internal void ShowEventSettingsForSmoke()
    {
        var exp = form.Children.OfType<Border>().Select(b => b.Child).OfType<Expander>()
            .Single(e => e.Content is StackPanel body && body.Children.Contains(dice));
        if (puzzle.Parent == null || loaded == null || puzzle.IsChecked != loaded.Events.Puzzle) throw new Exception("Puzzle event option is inaccessible or not loaded");
        exp.IsExpanded = true;
        UpdateLayout();
        exp.BringIntoView();
    }
    internal void ShowMirrorSettingsForSmoke()
    {
        for (DependencyObject? node = multiplier; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is Expander exp)
            {
                exp.BringIntoView();
                return;
            }
    }
    internal (Action Verify, Action Restore) BeginLanguageProbeForSmoke()
    {
        var oldMultiplier = multiplier.SelectedItem;
        var oldGold = gold.IsChecked; var oldFilter = filter.Text; bool oldDirty = dirty;
        multiplier.SelectedItem = 17; gold.IsChecked = !oldGold; filter.Text = "镜中";
        var originalItems = priority.ItemsSource; var originalGear = refineChoice.ItemsSource;
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(new {
            account, dirty, multiplier.SelectedItem, Gold = gold.IsChecked, Stone = stone.SelectedValue,
            Gear = refineChoice.SelectedValue, Priority = priority.Items.Cast<Choice>().Select(c => c.Key),
            Filter = filter.Text, Count = sections.Count(s => s.View.Visibility == Visibility.Visible)
        });
        string before = Snapshot();
        return (() => {
            if (before != Snapshot() || !ReferenceEquals(originalItems, priority.ItemsSource) || !ReferenceEquals(originalGear, refineChoice.ItemsSource))
                throw new Exception("Language change lost unsaved preferences, filter or stable choice IDs");
        }, () => { updating = true; multiplier.SelectedItem = oldMultiplier; gold.IsChecked = oldGold; filter.Text = oldFilter; dirty = oldDirty; updating = false; });
    }
    internal Action TradeSettingsProbeForSmoke()
    {
        if (allowGame) throw new Exception("Trade settings checks require an isolated panel");
        string first = new string('a', 64), second = new string('b', 64);
        DailyJson.Write(DailyTradePlan.SettingsPath(dataRoot, first), new {
            enabled = true, reserve_gold = 12000L, max_spend = (long?)null,
            reserve_items = new Dictionary<string, long> { ["777"] = 18 }
        }); // Legacy settings without the optional strategy must still load.
        DailyJson.Write(DailyTradePlan.SettingsPath(dataRoot, second), new {
            enabled = false, reserve_gold = 700L, max_spend = (long?)null, reserve_items = new Dictionary<string, long>()
        });
        SetAccounts([new(1, "Demo trader", first, "***001", true, true, ""), new(2, "Second account", second, "***002", true, false, "")]);
        if (tradeReserve.Text != "12000" || tradeMaximum.Text != "" || tradeBreakEven.IsChecked == true)
            throw new Exception("Legacy trade settings did not load in daily preferences");
        if (trade.IsChecked == true || !tradeReserve.IsEnabled || !tradeMaximum.IsEnabled || !tradeBreakEven.IsEnabled)
            throw new Exception("Trade preferences require enabling automation before editing");
        tradeReserve.Text = "42,000"; tradeMaximum.Text = "210000"; tradeBreakEven.IsChecked = true;
        if (!Save()) throw new Exception("Trade settings save failed");
        var saved = DailyTradePlan.ReadSettings(dataRoot, first);
        if (DailyData.N(saved["reserve_gold"]) != 42000 || DailyData.N(saved["max_spend"]) != 210000 ||
            !DailyData.B(saved["include_break_even_resales"]) || !DailyData.B(saved["enabled"]) || DailyData.N(saved["reserve_items"]?["777"]) != 18)
            throw new Exception("Trade settings save lost budget, strategy or existing policy");
        tradeReserve.Text = "43000";
        accounts.SelectedValue = second; // Account navigation saves the previous draft.
        if (DailyData.N(DailyTradePlan.ReadSettings(dataRoot, first)["reserve_gold"]) != 43000 || tradeReserve.Text != "700" || tradeMaximum.Text != "" || tradeBreakEven.IsChecked == true)
            throw new Exception("Switching account mixed trade budgets or lost the draft");
        tradeMaximum.Text = "0";
        if (!Save() || DailyTradePlan.ReadSettings(dataRoot, second)["max_spend"] == null)
            throw new Exception("Zero spending was incorrectly treated as unlimited");
        string prior = File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, second));
        tradeMaximum.Text = "invalid";
        accounts.SelectedValue = first;
        if (account != second || tradeMaximum.Text != "invalid" || File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, second)) != prior)
            throw new Exception("Invalid trade budget was discarded or persisted while changing account");
        tradeMaximum.Text = "";
        accounts.SelectedValue = first;
        if (DailyTradePlan.ReadSettings(dataRoot, second)["max_spend"] != null || tradeReserve.Text != "43000" || tradeMaximum.Text != "210000" || tradeBreakEven.IsChecked != true)
            throw new Exception("Saved trade settings did not round-trip by account");
        tradeReserve.Text = "44000";
        filter.Text = "跑商";
        var firstFile = File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first));
        return () => {
            if (!dirty || tradeReserve.Text != "44000" || tradeMaximum.Text != "210000" || tradeBreakEven.IsChecked != true || firstFile != File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first)))
                throw new Exception("Language or theme change overwrote a trade draft");
        };
    }
    public bool SavePending() => !dirty || Save();
    internal void CheckMirrorAccountForSmoke(string first, string second)
    {
        SelectAccount(first); multiplier.SelectedItem = 1;
        if (!Save()) throw new Exception("First account mirror settings not saved");
        SelectAccount(second); multiplier.SelectedItem = 4;
        if (!Save()) throw new Exception("Second account mirror settings not saved");
        SelectAccount(first);
        if ((int)multiplier.SelectedItem != 1) throw new Exception("Second account overwrote first account multiplier");
        SelectAccount(second);
        if ((int)multiplier.SelectedItem != 4 || store.Read(second).Mirror.Multiplier != 4) throw new Exception("Mirror multiplier did not survive settings navigation");
    }
    public void SelectAccount(string key)
    {
        if (account == key || !SavePending()) return;
        if (accounts.Items.OfType<DailyAccount>().Any(a => a.AccountKey == key)) accounts.SelectedValue = key;
    }
}

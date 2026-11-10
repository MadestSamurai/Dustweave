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
    private readonly System.Windows.Threading.DispatcherTimer autoSave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBlock saveState = new() { FontSize = 12, MaxWidth = 155, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private bool backgroundSaving;
    internal Func<Task<bool>>? BeforeGameToolOpen {get;init;}
    private async Task OpenGameToolAsync(Action open)
    {
        if(account.Length==0 || !SavePending())return;
        try {if(BeforeGameToolOpen==null || await BeforeGameToolOpen())open();}
        catch(Exception error){DailyUiText.Error(status,error);}
    }
    private readonly bool extensionTasks;
    private readonly bool extensionEvents;
    private readonly DailyPreferenceStore store;
    private readonly string dataRoot;
    private readonly DailyCollectionMapPicker collectionMaps = new();
    private readonly ComboBox accounts = new() { MinWidth = 230, DisplayMemberPath = "Name", SelectedValuePath = "AccountKey" };
    private readonly CheckBox eventBattle = Check("prefs.event_battle"), eventChallenge = Check("prefs.event_challenge");
    private readonly CheckBox eventFailureSweep = Check("prefs.event_failure_sweep"), salvation = Check("stage.salvation");
    private readonly TextBox salvationReserve = new(), salvationCount = new();
    private readonly ComboBox salvationMode = new() { DisplayMemberPath = "Label", SelectedValuePath = "Key" };
    private Grid salvationCountRow = null!;
    private readonly ComboBox eventSearch = new();
    private readonly TextBox eventPreset = new() { Name = "EventBattlePresetSlot" };
    private readonly CheckBox tactics = Check("stage.tactics");
    private readonly ComboBox tacticsSearch = new();
    private readonly ComboBox chapter = new(), stone = new(), multiplier = new(), saffronRounds = new();
    private readonly ComboBox[] enhance = Enumerable.Range(0,4).Select(_ => new ComboBox()).ToArray();
    private readonly ListBox priority = new() { Height = 94, MinWidth = 240, DisplayMemberPath = "Label" };
    private readonly CheckBox hunt = Check("notice.settings.hunting"), gold = Check("prefs.gold"), slime = Check("prefs.slime");
    private readonly CheckBox stones = Check("notice.settings.stones"), recycle = Check("notice.settings.draw_equipment"), keepSr = Check("prefs.keep_sr");
    private readonly CheckBox fallback = Check("prefs.craft_fallback"), refine = Check("notice.settings.refine");
    private readonly CheckBox pass = Check("notice.settings.pass"), dispatch = Check("notice.settings.dispatch");
    private readonly CheckBox goddess = Check("notice.settings.goddess"), ranking = Check("notice.settings.plaza");
    private readonly CheckBox mirror = Check("prefs.mirror"), guild = Check("stage.guild"), room = Check("stage.room"),
        saffron = Check("stage.territory_saffron"), income = Check("prefs.business"), guests = Check("prefs.guests"), draws = Check("stage.free_draws"),
        dailyRewards = Check("stage.daily_rewards"), weeklyRewards = Check("stage.weekly_rewards"), mail = Check("prefs.mail");
    private readonly CheckBox friendship = Check("notice.settings.friendship");
    private readonly CheckBox weeklyBook = Check("stage.weekly_book"), weeklyCraft = Check("stage.weekly_equipment"), weeklySichuan = Check("stage.weekly_sichuan");
    private readonly CheckBox mainline = Check("stage.weekly_mainline"), weeklyNpc = Check("stage.weekly_npc"), weeklySteal = Check("stage.weekly_steal"), walkCollect = Check("prefs.walk"), fishing = Check("stage.weekly_fishing"), roomLikes = Check("stage.weekly_room_likes");
    private readonly CheckBox events = Check("prefs.events"), eventMissions = Check("prefs.events_missions"), roulette = Check("prefs.events_roulette"), exchange = Check("prefs.events_exchange"), quiz = Check("prefs.events_quiz"), dice = Check("prefs.events_dice"), puzzle = Check("prefs.event_puzzle");
    private readonly CheckBox trade = Check("prefs.trade_purchase");
    private readonly CheckBox tradeBoxes = Check("prefs.trade_boxes");
    private readonly CheckBox tradeProcessing = Check("prefs.trade_processing");
    private readonly CheckBox cookingRefill = Check("prefs.cooking_refill");
    private readonly CheckBox tradeFavorites = Check("prefs.trade_favorites"), saffronProcessing = Check("prefs.saffron_processing");
    private readonly StackPanel normalTrade = new();
    private readonly TextBox tradeReserve = new(), tradeMaximum = new();
    private readonly TextBox potionReserve = new(), potionTarget = new();
    private readonly CheckBox potionRefill = Check("prefs.potion_refill");
    // Talent Pills are spent by several tasks, so their policy is an always-open card with a live summary.
    private readonly TextBlock potionState = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 12) };
    // Tasks with options are listed first with their current values; plain switches follow in a compact grid.
    private readonly StackPanel tunedHeader = new() { Margin = new(0, 12, 0, 4) }, switchHeader = new() { Margin = new(0, 24, 0, 8) };
    private readonly WrapPanel switchGrid = new() { ItemWidth = 340 };
    private readonly List<Border> tunedViews = new(), switchViews = new();
    private readonly List<(StackPanel Body, TextBlock Line)> currents = new();
    private Border potionCard = new();
    private System.Windows.Threading.DispatcherTimer? potionHighlight;
    private readonly CheckBox fastTravel = Check("prefs.fast_travel");
    private readonly Button cookingButton = LocalButton("cooking.title");
    private readonly CheckBox preserveBloodCocktailMail = Check("prefs.mail_preserve_cocktail");
    private readonly CheckBox tradeBreakEven = Check("trade.break_even");
    private StackPanel section = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly ComboBox refineChoice = new() { DisplayMemberPath = "Label", SelectedValuePath = "Instance", MinWidth = 210, MaxWidth = 660 };
    private readonly TextBlock inventoryState = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) };
    private readonly StackPanel form = new();
    private readonly ScrollViewer formScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBox filter = new() { MinWidth = 240, ToolTip = "按环节名称或说明筛选", Margin = new(8, 0, 0, 0) };
    private readonly List<(Border View, string Title, string Summary)> sections = new();
    private readonly ListBox categories = new() { BorderThickness=new(0), Background=Brushes.Transparent, Padding=new(0), Margin=new(0,0,0,12) };
    private string category = "all";
    private readonly TextBlock filterCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0) };
    private DailyPreferences? loaded;
    private string account = "";
    private bool updating, dirty;
    private sealed class Choice(string key, string source) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Key => key;
        public string Label => L.Get(source);
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
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        L.Text(text, source); return text;
    }
    private static Button LocalButton(string source)
    {
        var button = new Button();
        L.Bind(button, ContentControl.ContentProperty, source);
        return button;
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in stone.Items.Cast<Choice>().Concat(priority.Items.Cast<Choice>()).Concat(salvationMode.Items.Cast<Choice>())) item.Refresh();
        foreach (var item in refineChoice.Items.Cast<RefinementView>()) item.Refresh();
        FilterSections();
    }
    public DailyPreferencesPanel(string root, bool allowGame = true) : this(root, allowGame, DailyPlugin.Current) { }
    internal DailyPreferencesPanel(string root, bool allowGame, DailyPluginInfo plugin)
    {
        if (allowGame && !ReferenceEquals(plugin, DailyPlugin.Current)) throw new InvalidOperationException("Plugin fixtures require an offline settings panel");
        extensionTasks = plugin.Supports("tactics");
        extensionEvents = plugin.Supports("event_battle");
        this.allowGame = allowGame;
        store = new(root);
        dataRoot = root;
        var layout = new DockPanel { Margin = new(0,0,8,0) };
        var statusStyle = new Style(typeof(TextBlock));
        var emptyStatus = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        emptyStatus.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        statusStyle.Triggers.Add(emptyStatus);
        status.Style = statusStyle;
        DockPanel.SetDock(status, Dock.Bottom);
        layout.Children.Add(status);

        var header = new Grid { Margin = new(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        saveState.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Text(saveState, "prefs.autosave");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(saveState, System.Windows.Automation.AutomationLiveSetting.Polite);
        Grid.SetColumn(saveState, 2);
        header.Children.Add(saveState);
        autoSave.Tick += (_, _) =>
        {
            autoSave.Stop();
            if (!dirty || updating || loaded == null) return;
            backgroundSaving = true;
            try { Save(); } finally { backgroundSaving = false; }
        };
        var accountGroup = new Grid { MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        accountGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accountGroup.ColumnDefinitions.Add(new ColumnDefinition());
        var accountLabel = Label("prefs.account"); accountLabel.VerticalAlignment = VerticalAlignment.Center; accountLabel.Margin = new(0, 0, 12, 0); accountGroup.Children.Add(accountLabel);
        accounts.MinWidth = 160;
        accounts.MaxWidth = 200;
        Grid.SetColumn(accounts, 1);
        accounts.VerticalAlignment = VerticalAlignment.Center;
        accountGroup.Children.Add(accounts);
        header.Children.Add(accountGroup);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var searchBar = new Grid { Margin = new(16, 0, 16, 0), MaxWidth = 300 };
        filter.MinWidth = 140;
        filter.Margin = new(0);
        L.Bind(filter, System.Windows.Automation.AutomationProperties.NameProperty, "prefs.search_accessible");
        L.Bind(filter, FrameworkElement.ToolTipProperty, "prefs.search_help");
        searchBar.Children.Add(filter);
        var searchPlaceholder = Label("prefs.search_placeholder");
        searchPlaceholder.IsHitTestVisible = false;
        searchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        searchPlaceholder.Margin = new(11, 0, 11, 0);
        searchPlaceholder.TextWrapping = TextWrapping.NoWrap; searchPlaceholder.TextTrimming = TextTrimming.CharacterEllipsis; // Match the 14 DIP input text so typing does not change the field's scale.
        searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        searchBar.Children.Add(searchPlaceholder);
        Grid.SetColumn(searchBar, 1);
        header.Children.Add(searchBar);
        categories.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        categories.ItemContainerStyle=(Style)Application.Current.FindResource("FilterChoice");
        L.Bind(categories,System.Windows.Automation.AutomationProperties.NameProperty,"prefs.categories");
        foreach(string key in new[]{"all","daily","battle","weekly","resources"})
        {
            var item=new ListBoxItem { Tag=key };L.Bind(item,ContentControl.ContentProperty,"prefs.category."+key);categories.Items.Add(item);
        }
        categories.SelectedIndex=0;
        categories.SelectionChanged += (_, _) => { category = (categories.SelectedItem as ListBoxItem)?.Tag as string ?? "all"; FilterSections(); formScroll.ScrollToTop(); };
        DockPanel.SetDock(categories,Dock.Top);layout.Children.Add(categories);
        filter.TextChanged += (_, _) => {
            searchPlaceholder.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (filter.Text.Trim().Length > 0) categories.SelectedIndex = 0;
            FilterSections(); formScroll.ScrollToTop();
        };
        filter.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && filter.Text.Length > 0) { filter.Clear(); e.Handled = true; } };
        filterCount.Margin = new(0, 0, 0, 8);
        DockPanel.SetDock(filterCount, Dock.Top); layout.Children.Add(filterCount);
        chapter.ItemsSource = Enumerable.Range(1, 10).ToArray();
        foreach (var choice in enhance) choice.ItemsSource = Enumerable.Range(0, 10).ToArray();
        stone.ItemsSource = new[] { new Choice("least", "prefs.least"), new Choice("fire", "element.fire"), new Choice("water", "element.water"), new Choice("wind", "element.wind"), new Choice("light", "element.light"), new Choice("dark", "element.dark") };
        stone.DisplayMemberPath = "Label";
        stone.SelectedValuePath = "Key";
        section = form;
        var generalBody = new StackPanel();
        var general = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(20, 16, 20, 6), Margin = new(0, 4, 0, 8), Child = generalBody };
        general.SetResourceReference(Border.BackgroundProperty, "Surface"); general.SetResourceReference(Border.BorderBrushProperty, "Line"); potionCard = general;
        form.Children.Add(general); sections.Add((general, "prefs.general", "prefs.potions_summary")); section = generalBody;
        var potionsTitle = Label("prefs.potions_title"); potionsTitle.FontSize = 18; potionsTitle.FontWeight = FontWeights.SemiBold;
        section.Children.Add(potionsTitle); section.Children.Add(potionState);
        // Automatic refill is the decision that changes outcomes most, so it leads the card.
        potionRefill.FontWeight = FontWeights.SemiBold; potionRefill.FontSize = 15;
        section.Children.Add(potionRefill);
        var refillBody = new StackPanel { Margin = new(28, 0, 0, 0) }; section.Children.Add(refillBody); section = refillBody;
        Row("prefs.potion_target", potionTarget);
        Note("prefs.potion_refill_hint");
        section = generalBody;
        Divider();
        section.Children.Add(cookingRefill);
        Note("prefs.cooking_refill_hint");
        Divider();
        Row("prefs.potion_reserve", potionReserve);
        Note("prefs.potion_reserve_hint");
        Divider();
        section.Children.Add(fastTravel);
        Note("prefs.fast_travel_hint");
        potionRefill.Checked += (_, _) => potionTarget.IsEnabled = true;
        potionRefill.Unchecked += (_, _) => potionTarget.IsEnabled = false;
        potionRefill.Checked += (_, _) => ShowPotionState();
        potionRefill.Unchecked += (_, _) => ShowPotionState();
        potionReserve.TextChanged += (_, _) => ShowPotionState();
        potionTarget.TextChanged += (_, _) => ShowPotionState();
        L.Bind(potionReserve, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Get("prefs.potion_reserve"));
        L.Bind(potionTarget, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Get("prefs.potion_target"));
        Stage(guild, "stage.guild", "prefs.guild_hint", showSummary: false);
        Stage(room, "stage.room", "prefs.room_hint", showSummary: false);
        Stage(income, "prefs.business", "prefs.business_hint");
        Stage(guests, "prefs.guests", "prefs.guests_hint");
        Stage(saffron, "stage.territory_saffron", "prefs.saffron_hint");
        saffronRounds.ItemsSource = new[] { 1, 2, 3 };
        Row("prefs.saffron_rounds", saffronRounds);
        L.Bind(saffronRounds, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Get("prefs.saffron_rounds"));
        section.Children.Add(saffronProcessing);
        Note("prefs.saffron_details");
        Stage(draws, "stage.free_draws", "prefs.draws_hint");
        Stage(recycle, "stage.equipment_recycle", "prefs.recycle_hint");
        section.Children.Add(keepSr);
        var enhanceLabel = Label("prefs.enhance"); enhanceLabel.Margin = new(0,12,0,0); section.Children.Add(enhanceLabel);
        var enhanceRow = new WrapPanel();
        foreach (var (rarity, index) in new[] { "N", "R", "SR", "UR" }.Select((name,index)=>(name,index)))
        {
            var column = new StackPanel { Width=104, Margin=new(0,8,16,8) };
            var caption = new TextBlock { Text=rarity, Margin=new(0,0,0,6) }; column.Children.Add(caption);
            enhance[index].Name = "EquipmentEnhance" + rarity;
            L.Bind(enhance[index], System.Windows.Automation.AutomationProperties.NameProperty, "prefs.enhance_rarity", rarity);
            column.Children.Add(enhance[index]); enhanceRow.Children.Add(column);
        }
        enhanceRow.Tag = (Func<string>)(() => L.Get("prefs.enhance") + " " + string.Join(" ", new[] { "N", "R", "SR", "UR" }.Select((rarity, index) => rarity + "+" + enhance[index].SelectedItem)));
        section.Children.Add(enhanceRow);
        Note("prefs.enhance_note");
        section.Children.Add(fallback);
        Stage(weeklyCraft, "stage.weekly_equipment", "prefs.craft_hint");
        Stage(refine, "stage.equipment_refine", "prefs.refine_hint");
        section.Children.Add(refineChoice);
        section.Children.Add(inventoryState);
        var refreshInventory = LocalButton("prefs.refresh_gear"); refreshInventory.HorizontalAlignment = HorizontalAlignment.Left; refreshInventory.Margin = new(0, 4, 0, 8);
        refreshInventory.Click += (_, _) => RefreshInventory();
        section.Children.Add(refreshInventory);
        Note("prefs.refine_note");
        Stage(hunt, "prefs.hunt", "prefs.hunt_hint");
        Row("prefs.hunt_chapter", chapter);
        section.Children.Add(gold);
        section.Children.Add(slime);
        Note("prefs.priority_note");
        var rankingRow = new StackPanel { Orientation = Orientation.Horizontal };
        rankingRow.Children.Add(priority);
        var moves = new StackPanel { Margin = new(8, 0, 0, 0) };
        foreach (var spec in new[] { ("account.move_up", -1), ("account.move_down", 1) })
        {
            var b = LocalButton(spec.Item1); b.Margin = new(0, 0, 0, 6);
            int delta = spec.Item2;
            b.Click += (_, _) => Move(delta);
            moves.Children.Add(b);
        }
        rankingRow.Children.Add(moves);
        rankingRow.Tag = (Func<string>)(() => string.Join(" > ", priority.Items.Cast<Choice>().Select(c => c.Label)));
        section.Children.Add(rankingRow);
        Stage(stones, "stage.stones", "prefs.stones_hint");
        Row("prefs.stones_element", stone);
        Note("prefs.stones_note");
        Stage(mirror, "prefs.mirror", "prefs.mirror_hint", expanded: true);
        multiplier.ItemsSource = Enumerable.Range(1, 40).ToArray();
        Row("prefs.multiplier", multiplier);
        Note("prefs.multiplier_note");
        Stage(dispatch, "stage.daily_dispatch", "prefs.dispatch_hint");
        Stage(goddess, "stage.goddess", "prefs.goddess_hint", showSummary: false);
        Stage(ranking, "stage.square_ranking", "prefs.ranking_hint");
        tacticsSearch.ItemsSource = new[] { 15, 30, 45, 60, 120 };
        if (extensionTasks)
        {
            Stage(tactics, "stage.tactics", "plugins.tactics_settings_hint", expanded: true);
            Row("prefs.search_seconds", tacticsSearch);
            Note("plugins.tactics_compute_hint");
        }
        Stage(eventBattle, "prefs.event_battle", extensionEvents ? "prefs.extension_hint" : "prefs.event_sweep_hint", expanded: true);
        Row("prefs.event_preset_slot", eventPreset);
        Note("prefs.event_preset_hint");
        eventPreset.TextChanged += (_, _) => MarkDirty();
        if (!extensionEvents) { section.Children.Add(eventFailureSweep); Note("prefs.event_failure_hint"); }
        section.Children.Add(eventChallenge);
        eventSearch.ItemsSource = new[] { 5, 15, 30, 60, 120 };
        if (extensionEvents)
        {
            Row("prefs.search_seconds", eventSearch);
        }
        if (extensionEvents) Note("prefs.attempts_note");
        Stage(salvation, "stage.salvation", "prefs.salvation_hint", expanded: true);
        salvationMode.ItemsSource = new[] { new Choice("growth", "prefs.salvation_growth"), new Choice("fixed", "prefs.salvation_fixed") };
        Row("prefs.salvation_mode", salvationMode);
        salvationCountRow = Row("prefs.salvation_count", salvationCount);
        salvationCountRow.Visibility = Visibility.Collapsed;
        Row("prefs.salvation_reserve", salvationReserve);
        Note("prefs.salvation_budget");
        Stage(weeklyBook, "stage.weekly_book", "prefs.book_hint");
        Stage(friendship, "stage.friendship", "prefs.friendship_hint");
        Stage(mainline, "stage.weekly_mainline", "prefs.collection_hint", expanded: true);
        section.Children.Add(walkCollect);
        section.Children.Add(collectionMaps);
        collectionMaps.Changed += (_, _) => MarkDirty();
        Note("prefs.collection_note");
        Stage(weeklyNpc, "stage.weekly_npc", "prefs.npc_hint");
        Stage(weeklySteal, "stage.weekly_steal", "prefs.steal_hint");
        Stage(roomLikes, "stage.weekly_room_likes", "prefs.likes_hint");
        Stage(fishing, "stage.weekly_fishing", "prefs.fishing_hint");
        Stage(weeklySichuan, "stage.weekly_sichuan", "prefs.tiles_hint");
        Stage(trade, "prefs.trade_purchase", "prefs.trade_hint", expanded: true, editableWhenOff: true);
        section.Children.Add(tradeBoxes);
        Note("prefs.trade_boxes_hint");
        section.Children.Add(tradeFavorites);
        Note("prefs.trade_favorites_hint");
        cookingButton.HorizontalAlignment = HorizontalAlignment.Left;
        cookingButton.Margin = new(0, 4, 0, 12);
        cookingButton.Click += (_, _) =>
        {
            if (loaded == null || account.Length == 0) return;
            var dialog = new DailyCookingWindow(loaded.Trade.CookingRecipes, currentSales: loaded.Trade.Sales) { Owner = Window.GetWindow(this) };
            if (DailyDialogs.ShowModal(dialog) == true && dialog.Result != null && dialog.SalesResult != null)
            { loaded.Trade.CookingRecipes = dialog.Result; loaded.Trade.Sales = dialog.SalesResult; MarkDirty(); }
        };
        section.Children.Add(cookingButton);
        var tradeSection = section;
        section.Children.Add(normalTrade);
        section = normalTrade;
        section.Children.Add(tradeProcessing);
        Note("prefs.trade_processing_hint");
        Row("trade.reserve", tradeReserve);
        Row("trade.budget", tradeMaximum);
        L.Bind(tradeReserve, System.Windows.Automation.AutomationProperties.NameProperty, "trade.reserve");
        L.Bind(tradeMaximum, System.Windows.Automation.AutomationProperties.NameProperty, "trade.budget");
        Note("prefs.trade_budget_help");
        section.Children.Add(tradeBreakEven);
        Note("trade.break_even_hint");
        Note("prefs.trade_note");
        var tradeButton = LocalButton("prefs.trade_open"); tradeButton.HorizontalAlignment = HorizontalAlignment.Left;
        tradeButton.Click += async (_, _) => await OpenGameToolAsync(() => { var trade = new Window { Title = L.Get("prefs.trade_title"), Width = 1060, Height = 780, MinWidth = 760, MinHeight = 560, Owner = Window.GetWindow(this), Content = new TradePlanPanel(dataRoot, account, allowGame), WindowStartupLocation = WindowStartupLocation.CenterOwner }; DailyDialogs.Prepare(trade); trade.Show(); });
        L.Bind(tradeButton, FrameworkElement.ToolTipProperty, "prefs.trade_preview_help");
        section.Children.Add(tradeButton);
        var bargainButton = LocalButton("bargain.title");
        bargainButton.HorizontalAlignment = HorizontalAlignment.Left;
        bargainButton.Margin = new(0, 8, 0, 0);
        bargainButton.Click += async (_, _) => await OpenGameToolAsync(() =>
        {
            var dialog = new DailyBargainWindow(dataRoot, account, allowGame) { Owner = Window.GetWindow(this) };
            DailyDialogs.ShowModal(dialog);
        });
        section.Children.Add(bargainButton);
        section = tradeSection;
        tradeFavorites.Checked += (_, _) => { normalTrade.IsEnabled = false; normalTrade.Opacity = .5; cookingButton.IsEnabled = true; };
        tradeFavorites.Unchecked += (_, _) => { normalTrade.IsEnabled = true; normalTrade.Opacity = 1; cookingButton.IsEnabled = false; };
        Stage(events, "prefs.events", "prefs.events_hint");
        section.Children.Add(eventMissions);
        section.Children.Add(quiz);
        section.Children.Add(dice);
        section.Children.Add(puzzle);
        section.Children.Add(roulette);
        section.Children.Add(exchange);
        Note("prefs.events_note");
        Stage(dailyRewards, "stage.daily_rewards", "prefs.daily_hint");
        Stage(weeklyRewards, "stage.weekly_rewards", "prefs.weekly_hint");
        Stage(pass, "stage.pass_rewards", "prefs.pass_hint", showSummary: false);
        Stage(mail, "prefs.mail", "prefs.mail_hint", editableWhenOff: true);
        section.Children.Add(preserveBloodCocktailMail);
        Note("prefs.mail_preserve_hint");
        // Stages without parameters need only a switch, not an empty expansion affordance.
        foreach (var border in form.Children.OfType<Border>().ToList())
            if (border.Child is Expander exp && exp.Content is StackPanel body && body.Children.Count == 0 && exp.Header is StackPanel heading)
            {
                exp.Header = null;
                heading.Margin = new(12, 8, 12, 8);
                border.Child = heading;
                border.BorderThickness = new(1); border.CornerRadius = new(8); border.Margin = new(0, 0, 12, 10);
                border.SetResourceReference(Border.BackgroundProperty, "Surface");
                form.Children.Remove(border); switchGrid.Children.Add(border); switchViews.Add(border);
            }
            else if (border != general && border.Child is Expander task && task.Content is StackPanel options && task.Header is StackPanel title)
            {
                var line = new TextBlock { Margin = new(24, 0, 12, 4), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
                line.SetResourceReference(TextBlock.ForegroundProperty, "Focus");
                line.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { RelativeSource = System.Windows.Data.RelativeSource.Self });
                title.Children.Add(line); currents.Add((options, line)); tunedViews.Add(border);
            }
        GroupHeader(tunedHeader, "prefs.tuned_title", "prefs.tuned_hint");
        GroupHeader(switchHeader, "prefs.switch_title", "prefs.switch_hint");
        form.Children.Insert(form.Children.IndexOf(general) + 1, tunedHeader);
        form.Children.Add(switchHeader); form.Children.Add(switchGrid);
        RefreshCurrent();
        formScroll.Content = form;
        layout.Children.Add(formScroll);
        Content = layout;
        FilterSections();
        accounts.SelectionChanged += ChangedAccount;
        foreach (var c in new[] { salvation, eventFailureSweep, weeklyBook, weeklyCraft, weeklySichuan, friendship, tactics, eventBattle, eventChallenge, hunt, gold, slime, stones, recycle, keepSr, fallback, refine, pass, dispatch, goddess, ranking, mirror, guild, room, income, guests, saffron, draws, dailyRewards, weeklyRewards, mail, preserveBloodCocktailMail, mainline, weeklyNpc, weeklySteal, walkCollect, fishing, roomLikes, trade, events, eventMissions, quiz, dice, puzzle, roulette, exchange })
        {
            c.Checked += (_, _) => MarkDirty();
            c.Unchecked += (_, _) => MarkDirty();
        }
        // Same dirty rule as before, but the unsaved notice must appear for this field too.
        salvationReserve.TextChanged += (_, _) => MarkDirty();
        salvationCount.TextChanged += (_, _) => MarkDirty();
        salvationMode.SelectionChanged += (_, _) =>
        {
            salvationCountRow.Visibility = (string?)salvationMode.SelectedValue == "fixed" ? Visibility.Visible : Visibility.Collapsed;
            MarkDirty();
        };
        foreach (var c in new[] { tacticsSearch, eventSearch, chapter, stone, refineChoice, multiplier, saffronRounds }.Concat(enhance))
            c.SelectionChanged += (_, _) => MarkDirty();
        tradeReserve.TextChanged += (_, _) => MarkDirty();
        tradeMaximum.TextChanged += (_, _) => MarkDirty();
        potionReserve.TextChanged += (_, _) => MarkDirty();
        potionTarget.TextChanged += (_, _) => MarkDirty();
        potionRefill.Checked += (_, _) => MarkDirty();
        potionRefill.Unchecked += (_, _) => MarkDirty();
        tradeBreakEven.Checked += (_, _) => MarkDirty();
        tradeBreakEven.Unchecked += (_, _) => MarkDirty();
        tradeProcessing.Checked += (_, _) => MarkDirty();
        tradeProcessing.Unchecked += (_, _) => MarkDirty();
        cookingRefill.Checked += (_, _) => MarkDirty();
        cookingRefill.Unchecked += (_, _) => MarkDirty();
        foreach (var option in new[] { tradeFavorites, tradeBoxes, saffronProcessing, fastTravel })
        { option.Checked += (_, _) => MarkDirty(); option.Unchecked += (_, _) => MarkDirty(); }
        form.IsEnabled = false;
        ShowPotionState();
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
    }
    public void CheckWeeklySettingsForSmoke()
    {
        foreach (var toggle in new[] { weeklyCraft, weeklyNpc, friendship })
            if (!switchViews.Any(b => b.Child is StackPanel heading && heading.Children.Contains(toggle)))
                throw new Exception("A stage without settings still exposes an expansion affordance");
        var previous = mainline.IsChecked;
        try
        {
            mainline.IsChecked = false;
            if (!weeklySteal.IsEnabled || !weeklyNpc.IsEnabled)
                throw new Exception("Independent weekly tasks are locked when collection is disabled");
            mainline.IsChecked = true;
            if (walkCollect.Parent is not StackPanel body || !body.Children.Contains(collectionMaps) || !walkCollect.IsEnabled || !collectionMaps.IsEnabled)
                throw new Exception("Weekly collection should expose walking and map selection");
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
            summaryText.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
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
            IsExpanded = false,
            Margin = new(0, 8, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        L.Bind(expander, System.Windows.Automation.AutomationProperties.NameProperty, title);
        var border = new Border { BorderBrush = (Brush)Application.Current.FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = expander };
        form.Children.Add(border);
        sections.Add((border, title, summary));
        section = body;
    }
    /// <summary>Opened from a task that stopped for lack of Talent Pills: show the refill switch and mark the card briefly.</summary>
    internal void ShowTalentPotions(string key)
    {
        SelectAccount(key);
        // A rejected account switch leaves the invalid draft and its validation
        // focus intact. Never highlight another account's refill policy.
        if (account != key) return;
        ShowAllSettings();
        UpdateLayout();
        potionCard.BringIntoView();
        if (potionRefill.IsEnabled) potionRefill.Focus();
        potionHighlight?.Stop();
        potionCard.BorderThickness = new(2); potionCard.Padding = new(19, 15, 19, 5); potionCard.SetResourceReference(Border.BorderBrushProperty, "Focus");
        potionHighlight = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        potionHighlight.Tick += (_, _) => { potionHighlight?.Stop(); potionCard.BorderThickness = new(1); potionCard.Padding = new(20, 16, 20, 6); potionCard.SetResourceReference(Border.BorderBrushProperty, "Line"); };
        potionHighlight.Start();
    }
    internal void ShowAllSettings()
    {
        filter.Clear();
        categories.SelectedIndex = 0;
        FilterSections();
        formScroll.ScrollToTop();
    }
    internal void CheckPotionNavigationForSmoke(string previous, string target, Action open)
    {
        if (allowGame) throw new InvalidOperationException("Isolated settings required");
        SelectAccount(previous);
        string reserve = potionReserve.Text;
        try
        {
            potionReserve.Text = "invalid";
            open();
            if (account != previous || potionReserve.Text != "invalid" || !potionReserve.IsVisible || potionCard.BorderThickness.Left != 1)
                throw new Exception("Refill shortcut discarded an invalid draft or highlighted another account");
        }
        finally { potionReserve.Text = reserve; }
        if (!SavePending()) throw new Exception("Corrected settings cannot be saved");
        open(); UpdateLayout();
        if (account != target || !potionRefill.IsKeyboardFocused || category != "all" || filter.Text.Length != 0 || potionCard.BorderThickness.Left != 2)
            throw new Exception("Refill shortcut did not select and focus the task account");
        bool enabled = potionRefill.IsChecked == true;
        string amount = potionTarget.Text;
        try
        {
            potionRefill.IsChecked = true; potionTarget.Text = "1234";
            if (!potionState.Text.Contains("1234") || !potionTarget.IsEnabled)
                throw new Exception("Refill summary did not follow edited controls");
            potionRefill.IsChecked = false;
            if (potionState.Text != L.Get("prefs.potions_off") || potionTarget.IsEnabled)
                throw new Exception("Disabled refill summary does not match the policy");
        }
        finally { potionTarget.Text = amount; potionRefill.IsChecked = enabled; SavePending(); }
    }
    internal void ReviewSwitchGridForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Isolated settings required");
        ShowAllSettings(); UpdateLayout(); formScroll.ScrollToVerticalOffset(switchHeader.TranslatePoint(new Point(), form).Y);
        if (switchViews.Any(v => v.Child is Expander || v.ActualWidth > formScroll.ViewportWidth + 1))
            throw new Exception("Simple-task cards overflow the settings viewport or retain an expander");
    }
    internal async Task CheckPotionSupplyForSmoke(bool cooking)
    {
        if (allowGame || loaded == null) throw new InvalidOperationException("Isolated settings required");
        bool refill = potionRefill.IsChecked == true, fund = cookingRefill.IsChecked == true;
        try
        {
            potionRefill.IsChecked = false; cookingRefill.IsChecked = true;
            await Task.Delay(750);
            if (dirty) throw new Exception("Potion funding settings did not save automatically");
            var saved = store.Read(account);
            if (saved.General.RefillTalentPotions || !saved.Trade.RefillCookingPotions || !cookingRefill.IsEnabled)
                throw new Exception("Cooking funding must remain independent of fixed refills");
        }
        finally { potionRefill.IsChecked = refill; cookingRefill.IsChecked = fund; SavePending(); }
        ReviewCategoryForSmoke("resources");
        UpdateLayout();
        if (cooking) cookingRefill.BringIntoView(); else formScroll.ScrollToTop();
    }
    internal async Task CheckAutoSaveForSmoke(string first, string second)
    {
        if (allowGame) throw new InvalidOperationException("Isolated settings required");
        var firstSaved = store.Read(first); var secondSaved = store.Read(second);
        try
        {
            SelectAccount(first);
            guild.IsChecked = !firstSaved.Stages.Guild;
            await Task.Delay(750);
            if (dirty || store.Read(first).Stages.Guild == firstSaved.Stages.Guild || saveState.Text != L.Get("prefs.autosaved"))
                throw new Exception("Changing a task switch did not save automatically");
            string before = File.ReadAllText(store.PathFor(first));
            potionReserve.Text = ""; potionReserve.Focus();
            var scope = System.Windows.Input.FocusManager.GetFocusScope(potionReserve);
            var focus = System.Windows.Input.FocusManager.GetFocusedElement(scope);
            await Task.Delay(750);
            if (!dirty || File.ReadAllText(store.PathFor(first)) != before || System.Windows.Input.FocusManager.GetFocusedElement(scope) != focus || potionReserve.Text != "")
                throw new Exception($"Incomplete input was written, erased or interrupted by automatic validation: dirty={dirty}, sameFile={File.ReadAllText(store.PathFor(first)) == before}, beforeFocus={focus}, afterFocus={System.Windows.Input.FocusManager.GetFocusedElement(scope)}, reserve='{potionReserve.Text}', account={account}");
            potionReserve.Text = "42";
            SelectAccount(second); // Flush before the debounce expires; never save A's draft under B.
            if (account != second || store.Read(first).General.TalentPotionReserve != 42 || store.Read(second).General.TalentPotionReserve != secondSaved.General.TalentPotionReserve)
                throw new Exception("Immediate account navigation lost or misrouted an automatic save");
            guild.IsChecked = !secondSaved.Stages.Guild;
            if (!SavePending() || store.Read(second).Stages.Guild == secondSaved.Stages.Guild)
                throw new Exception("Close/start flush lost the pending task setting");
            before = File.ReadAllText(store.PathFor(second));
            string inMemory = System.Text.Json.JsonSerializer.Serialize(loaded);
            eventPreset.Text = "-1"; guild.IsChecked = secondSaved.Stages.Guild;
            await Task.Delay(750);
            if (File.ReadAllText(store.PathFor(second)) != before || System.Text.Json.JsonSerializer.Serialize(loaded) != inMemory)
                throw new Exception("Invalid late field partially changed the saved account policy");
            eventPreset.Text = secondSaved.EventBattle.PresetSlot.ToString(CultureInfo.InvariantCulture);
            await Task.Delay(750);
            if (dirty || store.Read(second).Stages.Guild != secondSaved.Stages.Guild)
                throw new Exception("Corrected input did not recover automatic saving");
        }
        finally
        {
            autoSave.Stop(); dirty = false;
            store.Save(first, firstSaved); store.Save(second, secondSaved);
            SelectAccount(first); SelectAccount(second);
        }
    }
    private void FilterSections()
    {
        string query = filter.Text.Trim();
        int count = 0;
        foreach (var (view, title, summary) in sections)
        {
            bool show = (category=="all" || CategoryFor(title)==category) &&
                (query.Length == 0 || (L.SearchText(title) + " " + L.SearchText(summary) + " " + SearchableLabels(view)).Contains(query, StringComparison.OrdinalIgnoreCase));
            view.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show && query.Length > 0 && view.Child is Expander found) found.IsExpanded = true;
            if(show && title=="prefs.general" && category=="resources" && view.Child is Expander general)general.IsExpanded=true;
            if (show)
                count++;
        }
        L.Bind(filterCount, TextBlock.TextProperty, () => query.Length == 0 ? "" : count == 0 ? L.Get("prefs.none") : L.Get("prefs.count", count));
        tunedHeader.Visibility = tunedViews.Any(v => v.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        switchHeader.Visibility = switchGrid.Visibility = switchViews.Any(v => v.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        filterCount.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private static string SearchableLabels(DependencyObject owner)
    {
        IEnumerable<string> Labels(DependencyObject node)
        {
            if (node is TextBlock text) yield return text.Text;
            if (node is ContentControl { Content: string caption }) yield return caption;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (string label in Labels(child)) yield return label;
        }
        return string.Join(" ", Labels(owner));
    }
    private static string CategoryFor(string titleKey) => DailySettingsCatalog.Category(titleKey);
    internal void ReviewCategoryForSmoke(string key)
    {
        if(allowGame)throw new InvalidOperationException("Isolated settings required");
        filter.Clear();categories.SelectedItem=categories.Items.Cast<ListBoxItem>().Single(i=>(string)i.Tag==key);
        if(!sections.Any(s=>s.View.IsVisible) && IsVisible)throw new Exception("Settings category has no visible tasks");
        if(sections.Any(s=>s.View.Visibility==Visibility.Visible && key!="all" && CategoryFor(s.Title)!=key))throw new Exception("Unrelated settings in category");
    }
    internal void ShowNativeBattleSettingsForSmoke(bool tower)
    {
        if (allowGame || extensionEvents) throw new InvalidOperationException("Isolated public settings required");
        ReviewCategoryForSmoke("battle");
        filter.Text = L.Get(tower ? "stage.salvation" : "prefs.event_battle");
        foreach (var item in sections.Where(s => s.View.Visibility == Visibility.Visible))
            if (item.View.Child is Expander exp) exp.IsExpanded = true;
        UpdateLayout();
    }
    internal void CheckFixedTowerSettingsForSmoke()
    {
        if (allowGame || loaded == null) throw new InvalidOperationException("Isolated settings required");
        salvation.IsChecked = true;
        salvationMode.SelectedValue = "growth";
        if (salvationCountRow.Visibility != Visibility.Collapsed) throw new Exception("Growth mode shows an irrelevant count");
        salvationMode.SelectedValue = "fixed"; salvationCount.Text = "3";
        if (salvationCountRow.Visibility != Visibility.Visible || !Save()) throw new Exception("Fixed sweep settings unavailable");
        var saved = store.Read(account);
        if (!saved.Salvation.UseFixedCount || saved.Salvation.SweepCount != 3) throw new Exception("Fixed tower policy did not persist");
        ShowNativeBattleSettingsForSmoke(true);
    }
    internal void CheckCategorySearchForSmoke()
    {
        if(allowGame)throw new InvalidOperationException("Isolated settings required");
        var before=(potionReserve.Text,potionTarget.Text,potionRefill.IsChecked,mirror.IsChecked,mainline.IsChecked);
        ReviewCategoryForSmoke("weekly");filter.Text=L.Get("prefs.mirror");
        if(category!="all" || sections.Single(s=>s.Title=="prefs.mirror").View.Visibility!=Visibility.Visible)throw new Exception("Search cannot find settings outside selected category");
        filter.Text = L.Get("prefs.multiplier");
        var mirrorSection = sections.Single(s => s.Title == "prefs.mirror").View;
        if (mirrorSection.Visibility != Visibility.Visible || mirrorSection.Child is not Expander { IsExpanded: true })
            throw new Exception("Search cannot reveal a setting inside a collapsed task");
        if(before!=(potionReserve.Text,potionTarget.Text,potionRefill.IsChecked,mirror.IsChecked,mainline.IsChecked))throw new Exception("Filtering changed saved task inputs");
        ReviewCategoryForSmoke("daily");
    }
    internal void ShowEquipmentSettingsForSmoke()
    {
        if (allowGame || loaded == null || account.Length == 0) throw new InvalidOperationException("Isolated account settings required");
        ReviewCategoryForSmoke("daily");
        filter.Text = L.Get("stage.equipment_recycle");
        foreach (var item in sections.Where(s => s.View.Visibility == Visibility.Visible))
            if (item.View.Child is Expander exp) exp.IsExpanded = true;
        int[] levels = [0, 7, 8, 9];
        for (int i = 0; i < enhance.Length; i++) enhance[i].SelectedItem = levels[i];
        if (!Save()) throw new Exception("Rarity settings could not be saved");
        var saved = store.Read(account).Equipment;
        if (!Enumerable.Range(1, 4).Select(saved.EnhancementTarget).SequenceEqual(levels))
            throw new Exception("Rarity settings did not persist independently");
        UpdateLayout();
    }
    internal Action ObserveCategorySurfaceForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Isolated settings required");
        categories.UpdateLayout();
        // Inspect the rendered template, not the ListBox.Background property:
        // Windows' default disabled trigger can paint its inner border differently.
        var surface = VisualTreeHelper.GetChild(categories, 0) as Border
            ?? throw new Exception("Settings category surface is not rendered");
        var color = (surface.Background as SolidColorBrush)?.Color;
        var selected = categories.SelectedItem;
        var inputs = (potionReserve.Text, potionTarget.Text, potionRefill.IsChecked, mirror.IsChecked, mainline.IsChecked);
        return () =>
        {
            categories.UpdateLayout();
            if ((surface.Background as SolidColorBrush)?.Color != color)
                throw new Exception("Disabling settings replaced the category background");
            if (categories.SelectedItem != selected || inputs != (potionReserve.Text, potionTarget.Text, potionRefill.IsChecked, mirror.IsChecked, mainline.IsChecked))
                throw new Exception("Settings availability changed selection or unsaved inputs");
        };
    }
    internal void CheckHiddenFieldValidationForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Isolated settings required");
        string value = potionReserve.Text;
        bool wasDirty = dirty;
        try
        {
            potionReserve.Text = "invalid";
            ReviewCategoryForSmoke("weekly");
            if (Save() || !potionReserve.IsVisible || potionReserve.Text != "invalid")
                throw new Exception("Invalid settings were hidden, accepted or erased after filtering");
        }
        finally { potionReserve.Text = value; dirty = wasDirty; L.Bind(status, TextBlock.TextProperty, () => ""); ReviewCategoryForSmoke("daily"); }
    }
    private void Note(string text)
    {
        var note = Label(text); note.MaxWidth = 720; note.FontSize = 13; note.HorizontalAlignment = HorizontalAlignment.Left;
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); note.Margin = new(0, 4, 0, 12);
        section.Children.Add(note);
    }
    private Grid Row(string label, Control field)
    {
        var row = new Grid { Margin = new(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "row" };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        var text = Label(label); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new(0, 0, 16, 0); row.Children.Add(text);
        field.MaxWidth = 240;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        section.Children.Add(row);
        return row;
    }
    private void Divider()
    {
        var line = new Border { Height = 1, Margin = new(0, 6, 0, 10) };
        line.SetResourceReference(Border.BackgroundProperty, "Line");
        section.Children.Add(line);
    }
    private static void GroupHeader(StackPanel host, string title, string hint)
    {
        var heading = Label(title); heading.FontSize = 15; heading.FontWeight = FontWeights.SemiBold; host.Children.Add(heading);
        var note = Label(hint); note.FontSize = 13; note.Margin = new(0, 2, 0, 0); note.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); host.Children.Add(note);
    }
    private void ShowPotionState()
    {
        bool on = potionRefill.IsChecked == true;
        string target = potionTarget.Text.Trim(), reserve = potionReserve.Text.Trim();
        L.Bind(potionState, TextBlock.TextProperty, () => on ? L.Get("prefs.potions_on", target, reserve) : L.Get("prefs.potions_off"));
        potionState.SetResourceReference(TextBlock.ForegroundProperty, on ? "Success" : "Warning");
    }
    private static string ChoiceText(object? item) => item switch { Choice c => c.Label, RefinementView r => r.Label, null => "", _ => item.ToString() ?? "" };
    // Reads the visible option controls of a task, so the collapsed row shows what it will do.
    private string CurrentSettings(Panel body)
    {
        var parts = new List<string>();
        void Visit(Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                switch (child)
                {
                    case { Tag: Func<string> custom }: parts.Add(custom()); break;
                    case CheckBox box: if (box.IsChecked == true && box.Content is TextBlock caption) parts.Add(caption.Text); break;
                    case Grid { Tag: "row" } row when row.Children.Count >= 2 && row.Children[0] is TextBlock label:
                        string value = row.Children[1] switch { TextBox text => text.Text.Trim(), ComboBox combo => ChoiceText(combo.SelectedItem), _ => "" };
                        parts.Add(label.Text + " " + (value.Length == 0 ? "—" : value)); break;
                    case ComboBox combo: parts.Add(ChoiceText(combo.SelectedItem)); break;
                    case Panel nested when !(ReferenceEquals(nested, normalTrade) && tradeFavorites.IsChecked == true): Visit(nested); break;
                }
            }
        }
        Visit(body);
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }
    private void RefreshCurrent()
    {
        foreach (var (body, line) in currents)
            L.Bind(line, TextBlock.TextProperty, () => { string text = CurrentSettings(body); return L.Get("prefs.current", text.Length == 0 ? L.Get("prefs.current_none") : text); });
    }
    private void MarkDirty()
    {
        RefreshCurrent();
        if (!updating && loaded != null)
        {
            dirty = true;
            L.Text(saveState, "prefs.autosave_pending");
            L.Bind(status, TextBlock.TextProperty, () => "");
            autoSave.Stop(); autoSave.Start();
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
            if (!dirty) L.Bind(status, TextBlock.TextProperty, () => "");
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
            fastTravel.IsChecked = loaded.General.FastTravel;
            potionTarget.IsEnabled = loaded.General.RefillTalentPotions;
            hunt.IsChecked = loaded.Hunt.Enabled;
            gold.IsChecked = loaded.Hunt.FarmGold;
            slime.IsChecked = loaded.Hunt.FarmSlime;
            chapter.SelectedItem = loaded.Hunt.OrdinaryChapter;
            stones.IsChecked = loaded.Hunt.StonesEnabled;
            stone.SelectedValue = loaded.Hunt.StoneElement;
            priority.ItemsSource = loaded.Hunt.Priority.Select(k => new Choice(k, k == "gold" ? "prefs.priority_gold" : k == "slime" ? "prefs.priority_slime" : "prefs.priority_chapter")).ToList();
            priority.SelectedIndex = 0;
            recycle.IsChecked = loaded.Equipment.Enabled;
            keepSr.IsChecked = loaded.Equipment.KeepFiveStarSr;
            for (int i=0;i<enhance.Length;i++) enhance[i].SelectedItem = loaded.Equipment.EnhancementTarget(i+1);
            fallback.IsChecked = loaded.Equipment.CraftFallback;
            refine.IsChecked = loaded.Equipment.RefineEnabled;
            mirror.IsChecked = loaded.Mirror.Enabled;
            multiplier.SelectedItem = loaded.Mirror.Multiplier;
            guild.IsChecked = loaded.Stages.Guild;
            room.IsChecked = loaded.Stages.Room;
            income.IsChecked = loaded.Stages.CafeteriaIncome;
            guests.IsChecked = loaded.Stages.CafeteriaGuests;
            saffron.IsChecked = loaded.Stages.Saffron;
            saffronRounds.SelectedItem = loaded.SaffronPotatoRounds;
            saffronProcessing.IsChecked = loaded.SaffronProcessing;
            draws.IsChecked = loaded.Stages.FreeDraws;
            weeklySichuan.IsChecked = loaded.Weekly.Sichuan;
            weeklyBook.IsChecked = loaded.Weekly.Book;
            weeklyCraft.IsChecked = loaded.Weekly.EquipmentCraft;
            dailyRewards.IsChecked = loaded.Stages.DailyRewards;
            weeklyRewards.IsChecked = loaded.Stages.WeeklyRewards;
            mail.IsChecked = loaded.Stages.Mail;
            preserveBloodCocktailMail.IsChecked = loaded.Stages.PreserveBloodCocktailMail;
            mainline.IsChecked = loaded.Weekly.Mainline;
            weeklySteal.IsChecked = loaded.Weekly.Steal;
            weeklyNpc.IsChecked = loaded.Weekly.Npc;
            walkCollect.IsChecked = loaded.Weekly.WalkCollect;
            collectionMaps.SetSelection(loaded.Weekly.CollectionMaps);
            tactics.IsChecked = loaded.Tactics.Enabled;
            tacticsSearch.SelectedItem = loaded.Tactics.SearchSeconds;
            eventBattle.IsChecked = loaded.EventBattle.Enabled;
            salvation.IsChecked = loaded.Salvation.Enabled;
            salvationReserve.Text = loaded.Salvation.ReserveKeys.ToString(CultureInfo.InvariantCulture);
            salvationCount.Text = loaded.Salvation.SweepCount.ToString(CultureInfo.InvariantCulture);
            salvationMode.SelectedValue = loaded.Salvation.UseFixedCount ? "fixed" : "growth";
            eventFailureSweep.IsChecked = loaded.EventBattle.SweepAfterFailure;
            eventChallenge.IsChecked = loaded.EventBattle.Challenge;
            eventSearch.SelectedItem = loaded.EventBattle.SearchSeconds;
            eventPreset.Text = loaded.EventBattle.PresetSlot.ToString(CultureInfo.InvariantCulture);
            friendship.IsChecked = loaded.Friendship.Enabled;
            trade.IsChecked = loaded.Trade.Enabled;
            tradeBoxes.IsChecked = loaded.Trade.OpenBoxesBeforeTrade;
            tradeProcessing.IsChecked = loaded.Trade.AutoCookAndSell;
            cookingRefill.IsChecked = loaded.Trade.RefillCookingPotions;
            tradeFavorites.IsChecked = loaded.Trade.FavoritesOnly;
            cookingButton.IsEnabled = loaded.Trade.FavoritesOnly;
            normalTrade.IsEnabled = !loaded.Trade.FavoritesOnly;
            var tradeSettings = DailyTradePlan.ReadSettings(dataRoot, account);
            tradeReserve.Text = DailyData.N(tradeSettings["reserve_gold"]).ToString(CultureInfo.InvariantCulture);
            tradeMaximum.Text = tradeSettings["max_spend"] == null ? "" : DailyData.N(tradeSettings["max_spend"]).ToString(CultureInfo.InvariantCulture);
            tradeBreakEven.IsChecked = tradeSettings["include_break_even_resales"]!.GetValue<bool>();
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
            autoSave.Stop();
            L.Text(saveState, "prefs.autosave");
            L.Bind(status, TextBlock.TextProperty, () => "");
        }
        catch (Exception ex) { loaded = null; form.IsEnabled = false; DailyUiText.Error(status, ex, "prefs.read_error"); }
        finally { updating = false; RefreshCurrent(); ShowPotionState(); }
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
        autoSave.Stop();
        if (loaded == null || account.Length == 0)
            return false;
        try
        {
            var general = new GeneralPreferences { FastTravel = fastTravel.IsChecked == true, TalentPotionReserve = PotionAmount(potionReserve), TalentPotionTarget = PotionAmount(potionTarget), RefillTalentPotions = potionRefill.IsChecked == true };
            try { general.Validate(); }
            catch { RevealField(potionTarget); throw; }
            // Merge the visible fields only: retain existing ingredient reserves and other policy.
            var tradeSettings = DailyTradePlan.ReadSettings(dataRoot, account);
            var previousTradeSettings = tradeSettings.ToJsonString();
            if (tradeFavorites.IsChecked != true)
            {
                tradeSettings["reserve_gold"] = TradeAmount(tradeReserve);
                tradeSettings["max_spend"] = tradeMaximum.Text.Trim().Length == 0 ? null : TradeAmount(tradeMaximum);
                tradeSettings["include_break_even_resales"] = tradeBreakEven.IsChecked == true;
            }
            var next = System.Text.Json.JsonSerializer.Deserialize<DailyPreferences>(System.Text.Json.JsonSerializer.Serialize(loaded))!;
            next.General = general;
            next.Hunt.Enabled = hunt.IsChecked == true;
            next.Hunt.FarmGold = gold.IsChecked == true;
            next.Hunt.FarmSlime = slime.IsChecked == true;
            next.Hunt.OrdinaryChapter = (int)chapter.SelectedItem;
            next.Hunt.StonesEnabled = stones.IsChecked == true;
            next.Hunt.StoneElement = (string)stone.SelectedValue;
            next.Hunt.Priority = priority.Items.Cast<Choice>().Select(c => c.Key).ToArray();
            next.Equipment.Enabled = recycle.IsChecked == true;
            next.Equipment.KeepFiveStarSr = keepSr.IsChecked == true;
            next.Equipment.EnhanceLevelN = (int)enhance[0].SelectedItem;
            next.Equipment.EnhanceLevelR = (int)enhance[1].SelectedItem;
            next.Equipment.EnhanceLevelSr = (int)enhance[2].SelectedItem;
            next.Equipment.EnhanceLevelUr = (int)enhance[3].SelectedItem;
            next.Equipment.CraftFallback = fallback.IsChecked == true;
            next.Equipment.RefineEnabled = refine.IsChecked == true;
            next.Equipment.RefineInstance = refineChoice.SelectedValue as string ?? next.Equipment.RefineInstance;
            next.Tasks.Pass = pass.IsChecked == true;
            next.Tasks.Dispatch = dispatch.IsChecked == true;
            next.Tasks.Goddess = goddess.IsChecked == true;
            next.Tasks.SquareRanking = ranking.IsChecked == true;
            next.Mirror.Enabled = mirror.IsChecked == true;
            next.Mirror.Multiplier = (int)multiplier.SelectedItem;
            next.Stages.Guild = guild.IsChecked == true;
            next.Stages.Room = room.IsChecked == true;
            next.Stages.CafeteriaIncome = income.IsChecked == true;
            next.Stages.CafeteriaGuests = guests.IsChecked == true;
            next.Stages.Saffron = saffron.IsChecked == true;
            next.SaffronPotatoRounds = (int)saffronRounds.SelectedItem;
            next.SaffronProcessing = saffronProcessing.IsChecked == true;
            next.Stages.FreeDraws = draws.IsChecked == true;
            next.Stages.DailyRewards = dailyRewards.IsChecked == true;
            next.Weekly.Sichuan = weeklySichuan.IsChecked == true;
            next.Weekly.Book = weeklyBook.IsChecked == true;
            next.Weekly.EquipmentCraft = weeklyCraft.IsChecked == true;
            next.Stages.WeeklyRewards = weeklyRewards.IsChecked == true;
            next.Stages.Mail = mail.IsChecked == true;
            next.Stages.PreserveBloodCocktailMail = preserveBloodCocktailMail.IsChecked == true;
            next.Weekly.Mainline = mainline.IsChecked == true;
            next.Weekly.Steal = weeklySteal.IsChecked == true;
            next.Weekly.Npc = weeklyNpc.IsChecked == true;
            next.Weekly.WalkCollect = walkCollect.IsChecked == true;
            next.Weekly.CollectionMaps = collectionMaps.Selection;
            next.Weekly.Fishing = fishing.IsChecked == true;
            next.Weekly.RoomLikes = roomLikes.IsChecked == true;
            if (extensionTasks)
            {
                next.Tactics.Enabled = tactics.IsChecked == true;
                next.Tactics.SearchSeconds = tacticsSearch.SelectedItem is int tacticSeconds ? tacticSeconds : 45;
            }
            if (!int.TryParse(salvationReserve.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var keys) || keys is < 0 or > 999) { RevealField(salvationReserve); throw new InvalidDataException("native.salvation_keys_error"); }
            next.Salvation.Enabled = salvation.IsChecked == true; next.Salvation.ReserveKeys = keys;
            bool fixedSweeps = (string?)salvationMode.SelectedValue == "fixed";
            if (fixedSweeps)
            {
                if (!int.TryParse(salvationCount.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 999)
                { RevealField(salvationCount); throw new InvalidDataException("native.salvation_count_error"); }
                next.Salvation.SweepCount = count;
            }
            next.Salvation.UseFixedCount = fixedSweeps;
            next.EventBattle.SweepAfterFailure = eventFailureSweep.IsChecked == true;
            next.EventBattle.Enabled = eventBattle.IsChecked == true;
            if (!int.TryParse(eventPreset.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var presetSlot) || presetSlot is < 0 or > 999)
            { RevealField(eventPreset); throw new InvalidDataException("prefs.event_preset_invalid"); }
            next.EventBattle.PresetSlot = presetSlot;
            next.EventBattle.Challenge = eventChallenge.IsChecked == true;
            if (extensionEvents)
            {
                next.EventBattle.SearchSeconds = eventSearch.SelectedItem is int seconds ? seconds : 15;
            }
            next.Friendship.Enabled = friendship.IsChecked == true;
            next.MonsterHunt.Enabled = false;
            next.Trade.Enabled = trade.IsChecked == true;
            next.Trade.OpenBoxesBeforeTrade = tradeBoxes.IsChecked == true;
            next.Trade.AutoCookAndSell = tradeProcessing.IsChecked == true;
            next.Trade.RefillCookingPotions = cookingRefill.IsChecked == true;
            next.Trade.FavoritesOnly = tradeFavorites.IsChecked == true;
            next.Events.Enabled = events.IsChecked == true;
            next.Events.Missions = eventMissions.IsChecked == true;
            next.Events.Quiz = quiz.IsChecked == true;
            next.Events.Dice = dice.IsChecked == true;
            next.Events.Puzzle = puzzle.IsChecked == true;
            next.Events.Roulette = roulette.IsChecked == true;
            next.Events.Exchange = exchange.IsChecked == true;
            store.Save(account, next);
            if (tradeSettings.ToJsonString() != previousTradeSettings)
                DailyJson.Write(DailyTradePlan.SettingsPath(dataRoot, account), tradeSettings);
            loaded = next;
            dirty = false;
            L.Text(saveState, "prefs.autosaved");
            L.Bind(status, TextBlock.TextProperty, () => "");
            return true;
        }
        catch (Exception ex) { L.Text(saveState, "prefs.autosave_failed"); DailyUiText.Error(status, ex, "common.save_error"); return false; }
    }
    private void RevealField(TextBox field)
    {
        // A debounced save must not steal focus, select text or move the page
        // while the user is still entering a number. Explicit navigation may.
        if (backgroundSaving) return;
        filter.Clear();
        categories.SelectedIndex = 0;
        // Collapsed Expander content is not in the visual tree yet. Follow its
        // logical owner first so filtered validation can open the correct section.
        for (DependencyObject? node = field; node != null; node = LogicalTreeHelper.GetParent(node) ?? (node is Visual ? VisualTreeHelper.GetParent(node) : null))
            if (node is Expander expander) expander.IsExpanded = true;
        UpdateLayout();
        field.BringIntoView();
        field.Focus();
        field.SelectAll();
    }
    private long PotionAmount(TextBox field)
    {
        if (!long.TryParse(field.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value > int.MaxValue)
        {
            RevealField(field);
            throw new InvalidDataException("prefs.potion_number_invalid");
        }
        return value;
    }
    private long TradeAmount(TextBox field)
    {
        if (!long.TryParse(field.Text.Trim().Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value > 1_000_000_000_000)
        {
            RevealField(field);
            throw new InvalidDataException("trade.invalid_gold");
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
    internal void ShowSaffronSettingsForSmoke()
    {
        ReviewCategoryForSmoke("resources");
        filter.Text="藏红花";
        if(loaded==null||saffronRounds.Items.Count!=3)throw new Exception("Saffron round settings unavailable");
        int previous=loaded.SaffronPotatoRounds;bool enabled=loaded.Stages.Saffron,processing=loaded.SaffronProcessing;
        try
        {
            saffron.IsChecked=true;saffronRounds.SelectedItem=2;saffronProcessing.IsChecked=false;
            if(!Save()||store.Read(account).SaffronPotatoRounds!=2||store.Read(account).SaffronProcessing)throw new Exception("Saffron round count or processing switch did not save for the selected account");
        }
        finally {saffronRounds.SelectedItem=previous;saffron.IsChecked=enabled;saffronProcessing.IsChecked=processing;if(!Save())throw new Exception("Saffron fixture restore failed");}
        UpdateLayout();saffronRounds.BringIntoView();
    }
    internal void ShowWeeklySettingsForSmoke()
    {
        filter.Text = "周";
        var exp = form.Children.OfType<Border>().Select(b => b.Child).OfType<Expander>().Single(e => e.Content is StackPanel body && body.Children.Contains(walkCollect));
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
    internal int CheckCollectionMapsForSmoke(string directory)
    {
        if (allowGame || loaded == null) throw new InvalidOperationException("Isolated preferences required");
        var choices = DailyCollectionMapPicker.ReadCatalog(directory);
        if (choices.Length < 50 || choices.Any(m => DailyFieldRoute.Excluded.Contains(m.Map)))
            throw new Exception("Collection map picker lost its catalog or included excluded maps");
        collectionMaps.CheckForSmoke(choices);
        var original = loaded.Weekly.CollectionMaps;
        collectionMaps.SetSelection([choices[0].Map]);
        if (!Save() || store.Read(account).Weekly.CollectionMaps is not { Length: 1 } selected || selected[0] != choices[0].Map)
            throw new Exception("Map selection was not saved with account preferences");
        collectionMaps.SetSelection(original);
        if (!Save()) throw new Exception("Map preference fixture could not be restored");
        mainline.IsChecked = true;
        ReviewCategoryForSmoke("all");
        ShowWeeklySettingsForSmoke();
        CheckWeeklySettingsForSmoke();
        return choices.Length;
    }
    internal void ShowSimpleSettingForSmoke(string title)
    {
        ReviewCategoryForSmoke("all");
        filter.Text = L.Get(title);
        var visible = sections.Where(s => s.View.Visibility == Visibility.Visible).ToArray();
        if (!visible.Any(s => s.Title == title && s.View.Child is StackPanel))
            throw new Exception("A setting-free task must be searchable without an expander");
        formScroll.ScrollToTop();
    }
    internal void CheckOptionalSettingsForSmoke()
    {
        if (!extensionTasks && (tactics.Parent != null || tacticsSearch.Parent != null || sections.Any(s => s.Title == "stage.tactics")))
            throw new Exception("Unavailable optional task remains visible");
        if (!extensionEvents && eventSearch.Parent != null)
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
        ReviewCategoryForSmoke("daily");
        var exp = form.Children.OfType<Border>().Select(b => b.Child).OfType<Expander>()
            .Single(e => e.Content is StackPanel body && body.Children.Contains(dice));
        if (puzzle.Parent == null || loaded == null || puzzle.IsChecked != loaded.Events.Puzzle) throw new Exception("Puzzle event option is inaccessible or not loaded");
        exp.IsExpanded = true;
        UpdateLayout();
        exp.BringIntoView();
    }
    internal void ShowMirrorSettingsForSmoke()
    {
        ReviewCategoryForSmoke("battle");
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
        string oldReserve = potionReserve.Text;
        // An incomplete edit remains a draft with autosave; language changes
        // must preserve it without persisting or revealing the hidden field.
        potionReserve.Text = "";
        multiplier.SelectedItem = 17; gold.IsChecked = !oldGold; filter.Text = "镜中";
        var originalItems = priority.ItemsSource; var originalGear = refineChoice.ItemsSource;
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(new {
            account, dirty, multiplier.SelectedItem, Reserve = potionReserve.Text, Gold = gold.IsChecked, Stone = stone.SelectedValue,
            Gear = refineChoice.SelectedValue, Priority = priority.Items.Cast<Choice>().Select(c => c.Key),
            Filter = filter.Text, Count = sections.Count(s => s.View.Visibility == Visibility.Visible)
        });
        string before = Snapshot();
        return (() => {
            if (before != Snapshot() || !ReferenceEquals(originalItems, priority.ItemsSource) || !ReferenceEquals(originalGear, refineChoice.ItemsSource))
                throw new Exception("Language change lost unsaved preferences, filter or stable choice IDs");
        }, () => { autoSave.Stop(); updating = true; potionReserve.Text = oldReserve; multiplier.SelectedItem = oldMultiplier; gold.IsChecked = oldGold; filter.Text = oldFilter; dirty = oldDirty; updating = false; });
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
        var originalPolicy = File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first));
        tradeMaximum.Text = "ignored while disabled";
        tradeFavorites.IsChecked = true;
        if (normalTrade.IsEnabled || tradeReserve.IsEnabled || tradeBreakEven.IsEnabled || !Save() || !store.Read(first).Trade.FavoritesOnly ||
            originalPolicy != File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first)))
            throw new Exception("Favorites-only mode did not disable and preserve ordinary policy");
        tradeFavorites.IsChecked = false; tradeMaximum.Text = "210000";
        if (!normalTrade.IsEnabled || !Save() || store.Read(first).Trade.FavoritesOnly)
            throw new Exception("Ordinary trading was not restored after favorites-only mode");
        potionReserve.Text = ""; // Keep a genuinely incomplete draft pending under autosave.
        tradeReserve.Text = "44000";
        filter.Text = "跑商";
        var firstFile = File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first));
        return () => {
            if (!dirty || tradeReserve.Text != "44000" || tradeMaximum.Text != "210000" || tradeBreakEven.IsChecked != true || firstFile != File.ReadAllText(DailyTradePlan.SettingsPath(dataRoot, first)))
                throw new Exception("Language or theme change overwrote a trade draft");
        };
    }
    public bool SavePending() => !dirty || Save();
    internal void CheckEventPresetForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Preset checks require an isolated panel");
        string first = new string('a',64), second = new string('b',64);
        SetAccounts([new(1,"First account",first,"***001",true,true,""),new(2,"Second account",second,"***002",true,false,"")]);
        eventBattle.IsChecked = true; eventPreset.Text = "6";
        if (!Save() || store.Read(first).EventBattle.PresetSlot != 6) throw new Exception("Event slot did not persist");
        accounts.SelectedValue = second;
        if (eventPreset.Text != store.Read(second).EventBattle.PresetSlot.ToString(CultureInfo.InvariantCulture)) throw new Exception("Account inherited another account's preset");
        eventPreset.Text = "2"; accounts.SelectedValue = first;
        if (eventPreset.Text != "6" || store.Read(second).EventBattle.PresetSlot != 2) throw new Exception("Account switch lost preset settings");
        filter.Text = "";
        ReviewCategoryForSmoke("battle");
        var exp = (Expander)sections.Single(s => s.Title == "prefs.event_battle").View.Child;
        exp.IsExpanded = true; UpdateLayout(); exp.BringIntoView();
    }
    internal void ShowTacticsForSmoke()
    {
        if (allowGame || !extensionTasks) throw new InvalidOperationException("Tactics checks require an offline plugin fixture");
        ReviewCategoryForSmoke("battle");
        tactics.IsChecked = true;
        var exp = (Expander)sections.Single(s => s.Title == "stage.tactics").View.Child;
        exp.IsExpanded = true; UpdateLayout(); exp.BringIntoView();
    }
    internal void ShowFavoritesForSmoke(bool enabled) { tradeFavorites.IsChecked = enabled; UpdateLayout(); }
    internal void CheckTradeProcessingForSmoke(Action<string, FrameworkElement> capture)
    {
        if (allowGame) throw new Exception("Processing smoke requires an isolated panel");
        bool original = tradeProcessing.IsChecked == true;
        tradeProcessing.IsChecked = false;
        tradeProcessing.IsChecked = true;
        if (Application.Current.Windows.OfType<Window>().Any(w => w.Title == L.Get("prefs.trade_processing")))
            throw new Exception("Automatic strategy must not ask users to opt in again");
        capture("trade-strategy-" + L.Code, this);
        ShowFavoritesForSmoke(true);
        if (tradeProcessing.IsEnabled || !cookingButton.IsEnabled) throw new Exception("Favorites must retain custom cooking independently of automatic processing");
        ShowFavoritesForSmoke(false);
        tradeProcessing.IsChecked = original;
    }
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
    internal void ReloadCurrent() { if (account.Length == 0 || !SavePending()) return; account = ""; ChangedAccount(accounts, null!); }
    public void SelectAccount(string key)
    {
        if (account == key || !SavePending()) return;
        if (accounts.Items.OfType<DailyAccount>().Any(a => a.AccountKey == key)) accounts.SelectedValue = key;
    }
}

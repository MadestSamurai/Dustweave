using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public sealed class AccountRow : INotifyPropertyChanged
{
    public required DailyAccount Account
    {
        get; init;
    }
    public required DailyAccountProfile Profile
    {
        get; init;
    }
    public string Name => Account.Name;
    public string DetailsLabel => $"{Name}\n{PlayerLabel}\n{IdentityLabel}\n{DailyLanguage.Current.Get("account.verified")}: {VerifiedLabel}";
    public string EnterLabel => DailyLanguage.Current.Get("account.enter_named", Name);
    public string QueueLabel => DailyLanguage.Current.Get("account.queue_named", Name);
    public TaskPresetChoice[] PresetOptions { get; set; } = [];
    private string presetId = "";
    public string PresetId { get => presetId; set { if (value == null || presetId == value) return; presetId = value; PropertyChanged?.Invoke(this, new(nameof(PresetId))); PropertyChanged?.Invoke(this, new(nameof(SelectedPreset))); } }
    public TaskPresetChoice? SelectedPreset { get => PresetOptions.FirstOrDefault(p => p.Id == presetId); set { if (value != null) PresetId = value.Id; } }
    public required int DisplayOrder
    {
        get; init;
    }
    public string OrderLabel => DisplayOrder.ToString();
    public string IdentityLabel => DailyLanguage.Current.Get("account.slot", Account.SlotNumber, Account.MaskedMemberId);
    public string PlayerLabel => string.IsNullOrEmpty(Profile.PlayerName) ? DailyLanguage.Current.Get("account.wait_game") : Profile.PlayerName;
    public string VerifiedLabel => Profile.LastVerifiedUtc?.ToLocalTime().ToString("MM-dd HH:mm:ss") ?? DailyLanguage.Current.Get("account.not_verified");
    public string StatusLabel => DailyLanguage.Current.Get(!Account.Valid ? "account.invalid" : Account.IsCurrent ? "account.signed_in" : "account.saved");
    public void RefreshLanguage() => PropertyChanged?.Invoke(this, new(""));
    public bool Selected
    {
        get => Profile.Selected; set
        {
            if (Profile.Selected == value)
                return;
            Profile.Selected = value;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly DailyTheme theme;
    private bool appearanceReady;
    private readonly bool designPreview;
    private readonly IAccountSessions sessions; private readonly IGameHost host; private readonly DailyCoordinator coordinator;
    private readonly GuildSession guild; private readonly GuildStore guildStore;
    private readonly DailyProfiles profiles; private readonly string root; private readonly string? smoke;
    private readonly ObservableCollection<AccountRow> rows = new(); private readonly DispatcherTimer timer;
    private readonly DailyPreferencesPanel preferencesPanel;
    private readonly DailyRunPanel dailyPanel; private readonly DailyQueueSession dailyQueue; private bool dailyStopping;
    private Task? operation; private CancellationTokenSource? operationPreparation; private bool busy; private bool finalClose; private DailyAccountCatalog? catalog;
    public MainWindow(IAccountSessions sessions, IGameHost host, string root, string? smoke, bool automatedSmoke = true)
    {
        DailyLanguage.Current.Initialize(root);
        designPreview = !automatedSmoke;
        theme = new DailyTheme(root);
        InitializeComponent();
        if(smoke==null){Height=Math.Min(900,SystemParameters.WorkArea.Height-32);MinHeight=Math.Min(MinHeight,Height);}
        InitializeWindowChrome();
        L.Bind(ConnectionGuideButton, ContentControl.ContentProperty, "onboarding.title");
        ThemeSelector.SelectedIndex = (int)theme.Preference;
        LanguageSelector.SelectedIndex = Array.IndexOf(DailyLanguage.Codes, DailyLanguage.Current.Code);
        appearanceReady = true;
        Closed += (_, _) => theme.Dispose();
        DailyLanguage.Current.Text(VersionText, automatedSmoke ? "updates.version_link" : "app.preview", DailyProductVersion.Current);
        this.sessions = sessions;
        this.host = host;
        this.root = root;
        this.smoke = smoke;
        preferencesPanel = new DailyPreferencesPanel(root, allowGame: smoke == null) { BeforeGameToolOpen=PrepareSettingsToolAsync };
        SettingsTab.Content = preferencesPanel;
        InitializeSoftwareSettings(); InitializeSchedules(); InitializeUpdates(); InitializeSandboxPresentation(); InitializeLogCleanup(); InitializeStorageWarning();
        dailyPanel = new DailyRunPanel(root);
        dailyQueue = new(root, smoke != null && host is DemoEnvironment demo ? demo : new PackagedDailyQueueExecutor(AppContext.BaseDirectory));
        RunTab.Content = dailyPanel; InitializeParallel(); InitializeDesktopBehavior();
        dailyPanel.PresetsChanged += RefreshAccountPresets;
        toolSession = new(root, Environment.ProcessPath!);
        if(smoke==null)toolSession.ConfigureLaunch=(start,id)=>{
            var game=host.Find();
            if(DailySuite.PreparesInWindow(id)){DailySuite.SetPendingToolEnvironment(start,game,id);return;}
            if(game==null)throw new InvalidOperationException("游戏已退出。");
            DailySuite.SetToolEnvironment(start,DailySuite.Read(root,game)??throw new InvalidOperationException("统一会话未就绪。"),game,id);
        };
        toolPanel = new DailyToolPanel();
        ToolsTab.Content = toolPanel;
        InitializePlugins();
        toolPanel.OpenRequested += async id => { toolMessage = null; await OpenTool(id); };
        toolPanel.CloseRequested += async () => await CloseTool();
        dailyPanel.AccountsRequested += () => WorkspaceTabs.SelectedItem = AccountsTab;
        dailyPanel.AccountRequested += key => { try { ViewAccountTasks(key); } catch (Exception error) { ShowError(error); } };

        dailyPanel.StartRequested += async (multi, resume) => await StartDaily(multi, resume);
        dailyPanel.RetryRequested += async request => await StartDaily(false, false, retry: request);
        dailyPanel.PlanRequested += async request => await StartDaily(false, false, selection: request);
        dailyPanel.SyncCollectionRequested += async () => await StartDaily(false, false, true);
        if (!automatedSmoke)
        {
            dailyPanel.PlanRequested += _ => dailyPanel.ShowOperationError("这是隔离界面预览，不会操作游戏。可查看设置、调整勾选和切换外观。");
            dailyPanel.StartRequested += (_, _) => dailyPanel.ShowOperationError("这是隔离界面预览，不会操作游戏。");
            dailyPanel.SyncCollectionRequested += () => dailyPanel.ShowOperationError("界面预览不会读取游戏或切换地图。");
        }
        dailyPanel.StopRequested += () => { dailyStopping = true; dailyQueue.Stop(); coordinator?.Stop(); };
        dailyQueue.Progress += v => Dispatcher.Invoke(() =>
        {
            dailyPanel.Show(v);
            UpdateSequentialProgress(v, v.State == "preparing" ? "connecting" : "running", v.Message);
            DailyUiText.Set(ProgressText, v.Message);
            ProgressText.Foreground = (Brush)FindResource(v.State is "blocked" or "recovery_required" ? "Error" : "Ink");
        });
        profiles = new(root);
        coordinator = new(sessions, host, root, smoke == null ? null : new DailyOptions { PollInterval = TimeSpan.FromMilliseconds(10), LoginTimeout = TimeSpan.FromSeconds(3) }, smoke == null ? token => DailySuite.ActivateAsync(host, root, "daily", ReportPreparation, token) : null);
        guild = new(sessions, host, root, smoke == null ? null : TimeSpan.FromMilliseconds(10));
        guildStore = new(root);
        guild.Progress += m => Dispatcher.Invoke(() => { DailyUiText.Set(ProgressText, m); ProgressText.Foreground = (Brush)FindResource("Ink"); });
        AccountsGrid.ItemsSource = rows;
        CollectionViewSource.GetDefaultView(rows).Filter = MatchesSearch;
        coordinator.Progress += p => Dispatcher.Invoke(() => { if (dailyQueue.IsRunning || WorkspaceTabs.SelectedItem == RunTab && busy) dailyPanel.Show(new("preparing", p.Message, "", [])); DailyUiText.Set(ProgressText, p.Message); ProgressText.Foreground = (Brush)FindResource(p.State == "error" ? "Error" : "Ink"); });
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { _ = PollParallelAsync(); L.RefreshFromDisk(); RefreshTools(); UpdateConnection(); if (smoke == null && !busy) { _ = RefreshAccountIdentityAsync(); RefreshDailyHistory(); } CheckCaptureLifetime(); if (smoke == null && CaptureSessionId.Length == 0) { _ = CheckLogCleanupAsync(); _ = CheckStorageWarningAsync(); _ = CheckScheduleAsync(); _ = OfferUpdateAsync(); if (DateTime.UtcNow >= nextUpdateCheck) { nextUpdateCheck = DateTime.UtcNow.AddHours(6); _ = CheckUpdatesAsync(); } } };
        Loaded += async (_, _) => {
            // Loaded proves the real WPF shell/resources constructed successfully.
            // Commit before optional account/game reads and background maintenance.
            // ContentRendered/ContextIdle may never arrive for a minimized or
            // occluded window; that must not roll a healthy installation back.
            if (!AcknowledgeUpdateStartup()) return;
            RefreshAccounts(); var last = DailyJson.TryRead<DailyRunStatus>(Path.Combine(root, "run.json")); if (last != null) { DailyUiText.History(ProgressText, "history.previous", last.AtUtc.LocalDateTime, last.Progress.Message); } if (smoke == null) { var previous = ReadTaskHistory(TaskAccountKey); dailyPanel.LoadHistory(previous); } timer.Start(); if (smoke != null && automatedSmoke) await SmokeAsync();
        };
        HostedToolPreparation.AfterFirstLayout(this, async () => {
            if (smoke != null || updateStartupFailed) return;
            try { await InitializeStartupFeaturesAsync(); } catch (Exception error) { ShowError(error); }
        });
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), OnLanguageChanged);
        Closing += OnClosing;
        WorkspaceTabs.SelectedItem = RunTab;
        WorkspaceTabs.SelectionChanged += (_, e) => { if (smoke == null && e.Source == WorkspaceTabs && WorkspaceTabs.SelectedItem == RunTab && !busy && preferencesPanel.SavePending()) RefreshDailyHistory(refreshPlan: true); };
        WorkspaceTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != WorkspaceTabs || WorkspaceTabs.SelectedItem != SettingsTab) return;
            preferencesPanel.ShowAllSettings();
            if (!busy && DailyProfiles.ValidKey(TaskAccountKey)) preferencesPanel.SelectAccount(TaskAccountKey);
        };
    }
    private void OnLanguageChanged(object? sender, EventArgs e) { bool ready=appearanceReady;appearanceReady=false;LanguageSelector.SelectedIndex=Array.IndexOf(DailyLanguage.Codes,L.Code);appearanceReady=ready;foreach (var row in rows) row.RefreshLanguage(); }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!appearanceReady || LanguageSelector.SelectedIndex < 0) return;
        try { DailyLanguage.Current.Select(DailyLanguage.Codes[LanguageSelector.SelectedIndex]); }
        catch (Exception error) { DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get("app.title"), L.Get("language.error", DailyUserText.Error(error, L.Translate)), false)); }
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!appearanceReady || ThemeSelector.SelectedIndex < 0) return;
        try { theme.Select((DailyAppearance)ThemeSelector.SelectedIndex); }
        catch (Exception error) { DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get("appearance.label"), L.Get("appearance.error", DailyUserText.Error(error, L.Translate)), false)); }
    }
    private readonly DailyToolPanel toolPanel;
    private readonly DailyToolSession toolSession;
    private bool toolChanging;
    private ApostleToolWindow? apostleWindow;
    private string? ActiveToolId => apostleWindow != null ? "apostle-defense" : toolSession.Current?.ToolId;
    private bool ToolOpen => apostleWindow != null || toolSession.Current != null;
    private bool ToolConnecting => apostleWindow?.Panel.Connecting == true;
    private bool ToolRunning => !busy && parallelControl == null && DailyToolControl.IsAutomationOccupied(root);
    private bool Unavailable => onboardingOpen || busy || manualCacheChanging || maintenanceYielding || toolChanging || ToolRunning || UpdateRestarting || ParallelBusy || pluginChanging || softwareSandboxPath?.Preparing == true;
    private async Task OpenTool(string id)
    {
        if (smoke != null || busy || manualCacheChanging || maintenanceYielding || toolChanging || UpdateRestarting || ParallelBusy || pluginChanging || softwareSandboxPath?.Preparing == true) return;
        toolChanging = true; SetBusy(busy);
        try {
            if(id=="apostle-defense" && apostleWindow!=null){apostleWindow.BringForward();return;}
            if(toolSession.Current?.ToolId==id){toolSession.Show();return;}
            await PauseAutomaticMaintenanceAsync();
            if(finalClose || UpdateRestarting)return;
            using var activityLease=DailyToolControl.Acquire(root);
            if(!await CloseToolWindowsAsync())return;
            await host.PrepareAccessAsync(m=>toolMessage=m,CancellationToken.None);
            if(id=="apostle-defense")
            {
                var session=new ApostleToolSession(root,async (progress,cancel)=>{
                    await host.PrepareAccessAsync(progress,cancel);
                    var selected=host.Find()??throw new InvalidOperationException("请先启动游戏。");
                    await host.ConnectAsync(selected,progress,cancel);
                    await HostedToolPreparation.WaitForIdentityAsync(host,selected,progress,cancel);
                    await DailySuite.ActivateAsync(host,root,id,progress,cancel);
                    if(host.Find()!=selected)throw new InvalidOperationException("游戏已退出或重启，功能切换已停止。");
                    var identity=host.ReadSnapshot(selected)??throw new InvalidOperationException("游戏身份尚未就绪。");
                    return new ApostleToolPort(host,root,selected,identity.AccountKey,identity.PlayerKey);
                });
                var window=new ApostleToolWindow(this,session,()=>!Unavailable);
                apostleWindow=window;
                window.Panel.ActivityChanged+=()=>{RefreshTools();SetBusy(busy);};
                window.Closed+=(_,_)=>{if(apostleWindow==window)apostleWindow=null;RefreshTools();SetBusy(busy);};
                window.Show();toolMessage="apostle.ready";return;
            }
            if(DailySuite.PreparesInWindow(id)){
                await toolSession.OpenAsync(id,controlReserved:true);
                toolMessage="tools.preparing_window";return;
            }
            var game=host.Find()??throw new InvalidOperationException("请先启动游戏。");
            await host.ConnectAsync(game,m=>toolMessage=m,CancellationToken.None);
            for(int i=0;i<20&&host.ReadSnapshot()==null;i++)await Task.Delay(250);
            await DailySuite.ActivateAsync(host,root,id,m=>toolMessage=m,CancellationToken.None);
            await toolSession.OpenAsync(id, controlReserved:true);toolMessage="已共用游戏连接 · "+DailyToolCatalog.Find(id).Name+"。请在工具窗口确认设置后开始。";
        }
        catch (Exception e) { toolMessage = e.Message; }
        finally { toolChanging = false; RefreshTools(); SetBusy(busy); }
    }
    private async Task CloseTool()
    {
        if (smoke != null || busy || toolChanging || UpdateRestarting || ParallelBusy || pluginChanging || softwareSandboxPath?.Preparing == true) return;
        toolChanging = true; toolMessage = null; SetBusy(busy);
        // Closing releases the tool; the next requested operation prepares its own component.
        // Neither closing the child nor this explicit action changes the user's current page.
        try { await CloseToolWindowsAsync(); }
        catch (Exception e) { toolMessage = e.Message; }
        finally { toolChanging = false; RefreshTools(); SetBusy(busy); }
    }
    private string? toolMessage;
    private async Task<bool> CloseToolWindowsAsync()
    {
        if(apostleWindow is { } window)
        {
            if(!await window.CloseSafelyAsync())return false;
        }
        return await toolSession.CloseAsync();
    }
    private string? previousTool;
    private bool previousRunning;
    private void RefreshTools()
    {
        var id = ActiveToolId;
        apostleWindow?.Panel.Render();
        bool running=ToolRunning;
        toolPanel.Refresh(busy || toolChanging || ParallelBusy || pluginChanging || softwareSandboxPath?.Preparing == true, id, ToolConnecting ? "apostle.connection_lock" : running ? "tools.active_notice" : toolMessage ?? (busy ? "日常正在执行；工具设置窗口可以保留。" : toolSession.Message),running);
        if(previousRunning!=running){previousRunning=running;SetBusy(busy);}
        if(previousTool!=id){previousTool=id;SetBusy(busy);}
    }
    private string viewedAccount = "";
    private string TaskAccountKey => viewedAccount.Length > 0 ? viewedAccount : catalog?.CurrentKey ?? "";
    private void RefreshTaskAccount()
    {
        if (catalog == null) return;
        if (busy || viewedAccount.Length > 0 && viewedAccount != catalog.CurrentKey && !catalog.Accounts.Any(a => a.Valid && a.AccountKey == viewedAccount)) viewedAccount = "";
        string key = TaskAccountKey;
        var account = catalog.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == key);
        string name = account?.Name ?? L.Get("account.unsaved");
        var available = catalog.Accounts.Where(a => !IsSandboxWindow || a.AccountKey == catalog.CurrentKey).ToList();
        if (DailyProfiles.ValidKey(catalog.CurrentKey) && available.All(a => a.AccountKey != catalog.CurrentKey))
            available.Add(new(0, L.Get("account.unsaved"), catalog.CurrentKey, "", true, true, ""));
        dailyPanel.SetAccounts(available, key);
        L.Bind(dailyPanel.CurrentAccountText, TextBlock.TextProperty, () => DailyProfiles.ValidKey(key) ? L.Get("run.viewing_account", name) : L.Get("account.not_signed_in"));
        dailyPanel.SetAccount(name, key);
        if (DailyProfiles.ValidKey(key)) dailyPanel.ShowPlan(new DailyPreferenceStore(root).Read(key));
        if (!busy) dailyPanel.LoadHistory(ReadTaskHistory(key));
    }
    internal void ShowTaskNavigationError(Exception error) => ShowError(error);
    public void ViewAccountTasks(string key)
    {
        if (Unavailable || !preferencesPanel.SavePending()) return;
        DailySandbox.RequireBoundAccount(key);
        var current = sessions.Read();
        if (!DailyProfiles.ValidKey(key) || key != current.CurrentKey && !current.Accounts.Any(a => a.Valid && a.AccountKey == key))
            throw new InvalidOperationException("run.account_unavailable");
        viewedAccount = key;
        RefreshAccounts(current);
        RunTab.Content = dailyPanel;
        WorkspaceTabs.SelectedItem = RunTab;
    }
    private QueueView ReadTaskHistory(string key) => DailyParallelProgress.Read(root, key, dailyQueue.ReadView(key));
    private DateTime nextHistoryRefresh;
    private void RefreshDailyHistory(bool refreshPlan = false)
    {
        if (!refreshPlan && DateTime.UtcNow < nextHistoryRefresh)
            return;
        nextHistoryRefresh = DateTime.UtcNow.AddSeconds(5);
        try
        {
            dailyPanel.LoadHistory(ReadTaskHistory(TaskAccountKey));
            if (refreshPlan && DailyProfiles.ValidKey(TaskAccountKey))
                dailyPanel.ShowPlan(new DailyPreferenceStore(root).Read(TaskAccountKey));
        }
        catch (Exception e) { DailyUiText.Error(ProgressText, e, "读取历史记录失败："); }
    }
    private bool MatchesSearch(object item)
    {
        if (item is not AccountRow row)
            return false;
        string q = Search.Text.Trim();
        return q.Length == 0 || (row.Name + row.PlayerLabel + row.IdentityLabel).Contains(q, StringComparison.OrdinalIgnoreCase);
    }
    private DateTime nextAccountRefresh;
    private bool readingAccounts;
    private async Task RefreshAccountIdentityAsync()
    {
        if (readingAccounts || busy || toolChanging || UpdateRestarting || finalClose || DateTime.UtcNow < nextAccountRefresh) return;
        nextAccountRefresh = DateTime.UtcNow.AddSeconds(5);
        readingAccounts = true;
        try
        {
            // Reading/decrypting the saved directory is never performed on the UI thread.
            var current = await Task.Run(sessions.Read);
            if (!busy && !toolChanging && !UpdateRestarting && !finalClose && !DailyAccountIdentity.SameCatalog(catalog, current))
                RefreshAccounts(current);
        }
        catch (Exception e) { DailyUiText.Error(ProgressText, e, "账号目录暂时无法刷新："); }
        finally { readingAccounts = false; }
    }

    private void RefreshAccounts(DailyAccountCatalog? observed = null)
    {
        try
        {
            var old = (AccountsGrid.SelectedItem as AccountRow)?.Account.AccountKey;
            catalog = observed ?? sessions.Read();
            var saved = profiles.Read();
            var presets = new DailyTaskPresetStore(root).Read();
            rows.Clear();
            foreach (var entry in DailyAccountOrder.Arrange(catalog.Accounts, saved))
            {
                var row = new AccountRow { Account = entry.Account, Profile = entry.Profile, DisplayOrder = rows.Count + 1,
                    PresetOptions = PresetOptions(presets), PresetId = presets.Accounts.GetValueOrDefault(entry.Account.AccountKey, "") };
                row.PropertyChanged += RowChanged;
                rows.Add(row);
            }
            CountText.Text = $"{rows.Count} / 100";
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AccountsGrid.SelectedItem = rows.FirstOrDefault(r => r.Account.AccountKey == old) ?? rows.FirstOrDefault();
            preferencesPanel.SetAccounts(catalog.Accounts); schedulePanel.SetAccounts(catalog.Accounts);
            RefreshTaskAccount();
            UpdateSelection();
            UpdateConnection();
        }
        catch (Exception e) { ShowError(e); }
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!refreshingAccountPresets && e.PropertyName == nameof(AccountRow.PresetId) && sender is AccountRow presetRow)
        {
            try { new DailyTaskPresetStore(root).Assign(presetRow.Account.AccountKey, presetRow.PresetId); dailyPanel.RefreshPresets(); }
            catch (Exception error) { ShowError(error); RefreshAccounts(); }
            return;
        }
        if (e.PropertyName == nameof(AccountRow.Selected) && sender is AccountRow row)
            try
            {
                profiles.Update(row.Profile);
                UpdateSelection();
            }
            catch (Exception ex) { ShowError(ex); }
    }
    private void UpdateSelection()
    {
        if (catalog == null || dailyPanel == null || ActionFooter == null) return;
        int count = rows.Where(r => r.Selected && r.Account.Valid).Select(r => r.Account.AccountKey).Distinct().Count();
        bool accounts = WorkspaceTabs.SelectedItem == AccountsTab;
        if (ActionFooter != null)
            ActionFooter.Visibility = accounts ? Visibility.Visible : Visibility.Collapsed;
        L.Text(SelectionText, "account.selected", count);
        UpdateAccountPresentation();
        RunAccountsButton.IsEnabled = !Unavailable && count > 0;
        L.Bind(RunAccountsButton, ContentControl.ContentProperty, count == 1 ? "run.start_account" : "account.run_queue_count", count);
        ExecutionModeButton.Visibility = !IsSandboxWindow && count > 1 ? Visibility.Visible : Visibility.Collapsed;

        RunAccountsButton.Visibility = busy || ParallelBusy ? Visibility.Collapsed : Visibility.Visible;
    }
    private bool connectionReading;
    private Task connectionUpdate = Task.CompletedTask;
    private void UpdateConnection()
    {
        if (!UpdateRestarting && connectionUpdate.IsCompleted) connectionUpdate = ReadConnectionAsync();
    }
    private async Task ReadConnectionAsync()
    {
        if (connectionReading || UpdateRestarting || finalClose) return;
        if (ParallelBusy)
        {
            L.Text(ConnectionText,"parallel.connection");ConnectionText.ToolTip=ConnectionText.Text;
            return;
        }
        if (designPreview)
        {
            L.Text(ConnectionText, "connection.preview");
            ConnectionText.ToolTip = ConnectionText.Text;
            PresentDiagnostics(null, null, false);
            return;
        }
        try
        {
            connectionReading = true;
            (GameInstance? game, DailySnapshot? s) Read() { var found = host.Find(); return (found, host.ReadSnapshot(found)); }
            var (game, s) = smoke != null ? Read() : await Task.Run(Read);
            if (finalClose || UpdateRestarting || ParallelBusy) return;
            L.Bind(ConnectionText, TextBlock.TextProperty, () => game == null ? L.Get("connection.closed") : s != null && s.ProcessId == game.ProcessId && s.ProcessStartTicks == game.StartTicks && s.FrameUtcTicks > DateTimeOffset.UtcNow.AddSeconds(-5).UtcTicks
                ? L.Get("connection.ready", s.State == "identified" ? s.PlayerName : L.Get("connection.login"), L.Translate(DailyUserText.Scene(s.Scene)))
                : L.Get("connection.loading"));
            L.Bind(ConnectionText, FrameworkElement.ToolTipProperty, () => s == null ? ConnectionText.Text : L.Diagnostic(s.Scene, ConnectionText.Text));
            if (ToolRunning && ActiveToolId is { } toolId)
                L.Bind(ConnectionText, TextBlock.TextProperty, () => L.Get("connection.tool", L.Get("tool." + toolId)));
            bool fresh = game != null && s != null && s.Runtime == DailyIdentity.RuntimeName && s.ProcessId == game.ProcessId && s.ProcessStartTicks == game.StartTicks && s.FrameUtcTicks > DateTimeOffset.UtcNow.AddSeconds(-5).UtcTicks;
            PresentDiagnostics(game, s, fresh);
            if (fresh)
                DailyQueuePeriod.Remember(root, s);

        }
        catch (Exception e) { DailyUiText.Error(ConnectionText, e); PresentDiagnostics(null, null, false, readFailed: true); }
        finally { connectionReading = false; }
    }
    private void SetBusy(bool value)
    {
        if(value)ActionNotice.Visibility=Visibility.Collapsed;
        busy = value;
        schedulePanel.Busy(value || ParallelBusy); ExecutionModeButton.IsEnabled = !Unavailable; UpdateUpdateControls();
        var unavailable = Unavailable;
        pluginPanel.Busy(unavailable || IsSandboxWindow);
        dailyPanel.Busy(unavailable, value);
        preferencesPanel.IsEnabled = !unavailable;
        UpdateSoftwareSettingsAvailability();
        AccountsGrid.IsEnabled = !unavailable;
        ManageBar.IsEnabled = !unavailable;
        RefreshButton.IsEnabled = !unavailable;
        ConnectButton.IsEnabled = !unavailable;
        StopButton.IsEnabled = value || ParallelBusy; L.Bind(StopButton,ContentControl.ContentProperty,ParallelBusy?"parallel.pause_all":"common.stop_operation");
        toolPanel.Refresh(value || manualCacheChanging || maintenanceYielding || toolChanging || ParallelBusy || pluginChanging || softwareSandboxPath?.Preparing == true, ActiveToolId, ToolConnecting ? "apostle.connection_lock" : ToolRunning ? "工具自动化正在运行或等待结算；停止后释放，无需关闭窗口。" : toolMessage ?? (value ? "日常正在执行；工具设置窗口可以保留。" : toolSession.Message),ToolRunning);
        SelectAll.IsEnabled = !unavailable;
        UpdateSelection();
        UpdateConnection();
    }
    private void ReportPreparation(string message)
    {
        dailyPanel.Show(new("preparing", message, "", []));
        DailyUiText.Set(ProgressText, message);
    }
    private async Task OperateAsync(Func<Task> action, bool connectsGame = false)
    {
        if (Unavailable)
            return;
        RunTab.Content = dailyPanel; dailyPanel.ClearOperationError();
        SetBusy(true);
        ReportPreparation("正在连接并核对当前账号…");
        using var preparation = new CancellationTokenSource();
        operationPreparation = preparation;
        try
        {
            await PauseAutomaticMaintenanceAsync(preparation.Token);
            preparation.Token.ThrowIfCancellationRequested();
            if(finalClose || UpdateRestarting)return;
            using var toolsLease = DailyToolControl.Acquire(root);
            if(!connectsGame && smoke==null && host.Find() is {} currentGame && host.ReadSnapshot() is {} snapshot && snapshot.State=="identified" && snapshot.AccountKey.Length>0 && snapshot.PlayerKey.Length>0 && snapshot.ProcessId==currentGame.ProcessId && snapshot.FrameUtcTicks>DateTime.UtcNow.AddSeconds(-5).Ticks)
                await DailySuite.ActivateAsync(host,root,"daily",ReportPreparation,preparation.Token);
            preparation.Token.ThrowIfCancellationRequested();
            operation = action();
            await operation;
        }
        catch (OperationCanceledException e) { ShowOperationStopped(e is DailyGameExitedException ? e.Message : "已停止。保留游戏当前账号和已完成的检查记录。"); }
        catch (Exception e) { ShowError(e); }
        finally { operationPreparation = null; operation = null; SetBusy(false); RefreshAccounts(); }
    }
    private void ShowOperationStopped(string message)
    {
        dailyPanel.ClearOperationError();
        dailyPanel.Show(new("stopped", message, "", []));
        DailyUiText.Set(ProgressText, message);
        ProgressText.Foreground = (Brush)FindResource("MutedInk");
    }
    private void ShowError(Exception e)
    {
        if (e is DailyGameExitedException) { ShowOperationStopped(e.Message); return; }
        if (e.Message.StartsWith("account.relogin.", StringComparison.Ordinal) && !IsSandboxWindow)
            WorkspaceTabs.SelectedItem = AccountsTab;
        if (RunTab.Content == parallelPanel) parallelPanel.Feedback(e.Message);
        if (WorkspaceTabs.SelectedItem == RunTab)
            dailyPanel.ShowOperationError(e.Message, DailyUserText.Error(e));
        DailyJson.Write(Path.Combine(root, "ui-operation-error.json"), new { atUtc = DateTimeOffset.UtcNow, version = DailyProductVersion.Current, issue = DailyIssues.Classify(e), error = e.ToString() });
        // The current page owns the error. Do not leave a second, stale copy in
        // the account-operation footer after the notice is dismissed.
        ProgressText.Text = "";
        ProgressText.Foreground = (Brush)FindResource("MutedInk");
        if(WorkspaceTabs.SelectedItem!=RunTab) {DailyUiText.Error(ActionNoticeText,e);ActionNotice.Visibility=Visibility.Visible;}
    }
    private void DismissActionNotice_Click(object sender,RoutedEventArgs e) => ActionNotice.Visibility=Visibility.Collapsed;
    private AccountRow? Selected => AccountsGrid.SelectedItem as AccountRow;
    private bool Confirm(string message)
    {
        bool prior = releaseDialogOpen; releaseDialogOpen = true;
        try { return DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get("app.title"), L.Translate(message), true)) == true; }
        finally { releaseDialogOpen = prior; }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await OperateAsync(coordinator.ConnectCurrentAsync, connectsGame: true);
    private async void EnterAccount_Click(object sender, RoutedEventArgs e)
    {
        // The row button owns its target; highlighted rows and queue checkboxes are unrelated.
        if (Unavailable || sender is not Button { DataContext: AccountRow { Account.Valid: true } row })
            return;
        if (Confirm(L.Get("account.enter_confirm", row.Name)))
        {
            AccountsGrid.SelectedItem = row;
            await OperateAsync(() => coordinator.InspectAsync([row.Account]), connectsGame: true);
        }
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (ParallelBusy) { parallel!.Control("", "pause"); _ = PollParallelAsync(); return; }
        dailyStopping = true;
        operationPreparation?.Cancel();
        try
        {
            if (dailyQueue.IsRunning)
            {
                dailyStopping = true;
                dailyQueue.Stop();
                L.Text(ProgressText, "run.stop_requested");
            }
            else if (guild.IsRunning)
            {
                guild.Stop();
                L.Text(ProgressText, "run.stop_clicks");
            }
            else
            {
                coordinator.Stop();
                L.Text(ProgressText, "run.stop_connect");
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenRecords_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(root, "guild");
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void WorkspaceTab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if(e.Source==WorkspaceTabs && ActionNotice!=null)ActionNotice.Visibility=Visibility.Collapsed;
        if (RunAccountsButton != null && WorkspaceTabs != null && e.Source == WorkspaceTabs)
            UpdateSelection();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAccounts();
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (AccountsGrid?.ItemsSource != null)
        {
            CollectionViewSource.GetDefaultView(rows).Refresh();
            UpdateSelection();
        }
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RunAccountsButton != null)
            UpdateSelection();
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool value = SelectAll.IsChecked == true;
        foreach (var row in rows.Where(r => r.Account.Valid && MatchesSearch(r)).ToArray())
            row.Selected = value;
        UpdateSelection();
    }
    private void Manage(Action action)
    {
        if (Unavailable)
            return;
        try
        {
            action();
            RefreshAccounts();
        }
        catch (Exception e) { ShowError(e); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        bool offer = false;
        Manage(() =>
        {
            var before = sessions.Read(); var plan = DailyAccountIdentity.SavePlan(before);
            string? name = Prompt(L.Get(plan.Existing ? "account.slot_update" : "account.slot_save", plan.SlotNumber),
                plan.ReplacesUnreadable ? "account.recover_credentials" : plan.Existing ? "account.update_hint" : "为这个账号取一个容易区分的名字。", plan.Name);
            if (name == null) return;
            sessions.Save(plan.SlotNumber, name, plan.AccountKey);
            L.Text(ProgressText, plan.Existing ? "account.updated" : "account.new_saved", name);
            offer = DailyMultiAccountSetup.AfterSave(root, SavedAccountCount(before), SavedAccountCount(sessions.Read()), !plan.Existing);
        });
        if (offer) await ShowMultiAccountSetupAsync();
    }
    private void Rename_Click(object sender, RoutedEventArgs e) => Manage(() => { if (Selected is not { } row) return; string? name = Prompt("重命名账号", "别名只保存在本机，不会修改游戏角色名称。", row.Name); if (name != null) sessions.Rename(row.Account.SlotNumber, name, row.Account.AccountKey); });
    private void Delete_Click(object sender, RoutedEventArgs e) => Manage(() => { if (Selected is { } row) DeleteAccount(row.Account, Confirm); });
    private void LoginNew_Click(object sender, RoutedEventArgs e) => Manage(() => { if (Confirm("登录新账号前，请正常退出游戏和启动器。当前登录账号必须已经保存。\n\n接下来会备份当前登录会话，并启动游戏供你登录新账号；登录完成后退出游戏，再保存到空槽位。")) sessions.LoginNew(); });
    private void Recover_Click(object sender, RoutedEventArgs e) => Manage(() => { if (Confirm("游戏和启动器关闭后，可恢复上一次切换前备份的本机会话。\n\n这不会启动游戏，也不会删除已保存账号。")) sessions.Recover(); });
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta) => Manage(() => { if (Selected is not { } row) return; int from = rows.IndexOf(row), to = from + delta; if (to < 0 || to >= rows.Count) return; rows.Move(from, to); for (int i = 0; i < rows.Count; i++) { rows[i].Profile.Order = i; if (rows[i].Account.Valid) profiles.Update(rows[i].Profile); } });
    private string? Prompt(string title, string description, string initial)
    {
        var window = new Window { Owner = this, Title = L.Translate(title), Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("AppBackground") };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var text = new TextBox { Text = initial, MaxLength = 64, Margin = new Thickness(0, 14, 0, 18) };
        panel.Children.Add(new TextBlock { Text = L.Translate(description), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.Get("common.cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = L.Get("common.save"), IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) window.DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        window.Content = panel;
        window.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return DailyDialogs.ShowModal(window) == true ? text.Text.Trim() : null;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (finalClose)
            return;
        try { if (KeepWindowInTray()) { e.Cancel = true; return; } }
        catch (Exception error) { e.Cancel = true; ShowError(error); return; }
        if (firstRunTour != null)
        {
            if (!Confirm(L.Get("onboarding.skip_tour"))) { e.Cancel = true; return; }
            EndPageTour(false);
        }
        if (ParallelBusy || parallelClosing)
        {
            e.Cancel=true;if(parallelClosing)return;parallelClosing=true;
            try { await StopParallelForCloseAsync(); } finally { parallelClosing=false;finalClose=true;timer.Stop();Close(); }
            return;
        }
        if(toolChanging || pluginChanging){e.Cancel=true;return;}
        if(ToolOpen){e.Cancel=true;toolChanging=true;try{if(await CloseToolWindowsAsync()){finalClose=true;timer.Stop();Close();}}finally{toolChanging=false;}return;}
        if (!preferencesPanel.SavePending())
        {
            e.Cancel = true;
            return;
        }
        if (busy)
        {
            e.Cancel = true;
            try
            {
                dailyStopping = true;
                operationPreparation?.Cancel();
                dailyQueue.Stop();
                guild.Stop();
                coordinator.Stop();
                if (operation != null)
                    await operation;
            }
            catch { }
            finalClose = true;
            timer.Stop();
            Close();
        }
        else
        {
            timer.Stop();
            try
            {
                guild.Stop();
                coordinator.Stop();
            }
            catch { }
        }
    }
    public Task InspectAccountAsync(string accountKey) => OperateAsync(async () =>
    {
        var target = sessions.Read().Accounts.SingleOrDefault(a => a.Valid && a.AccountKey == accountKey)
            ?? throw new InvalidOperationException("未找到指定账号，未启动游戏。");
        await coordinator.InspectAsync([target]);
    }, connectsGame: true);
    public async Task ResumeQueueAsync(string accountKey)
    {
        try
        {
            DailySandbox.RequireBoundAccount(accountKey);
            if (sessions.Read().CurrentKey != accountKey)
                throw new InvalidOperationException("当前登录账号与接续队列不一致，未开始执行。");
            ViewAccountTasks(accountKey);
            await StartDaily(false, true);
        }
        catch (Exception error) { ShowError(error); }
    }
    public Task RunSelectedAsync(QueuePlanRequest request) => StartDaily(false, false, selection: request);
    private async Task ConnectTaskAccountAsync(string targetKey)
    {
        DailySandbox.RequireBoundAccount(targetKey);
        await host.PrepareAccessAsync(ReportPreparation, CancellationToken.None);
        var before = sessions.Read();
        if (!DailyProfiles.ValidKey(targetKey)) throw new InvalidOperationException("run.account_unavailable");
        if (before.CurrentKey != targetKey || host.Find() == null)
        {
            var target = before.Accounts.SingleOrDefault(a => a.Valid && a.AccountKey == targetKey)
                ?? throw new InvalidOperationException("run.account_unavailable");
            await coordinator.InspectAsync([target]);
        }
        else await coordinator.ConnectCurrentAsync();
        if (!dailyStopping && sessions.Read().CurrentKey != targetKey) throw new InvalidOperationException("run.account_unavailable");
    }
    private async Task StartDaily(bool multi, bool resume, bool syncCollection = false, QueueRetryRequest? retry = null, QueuePlanRequest? selection = null)
    {
        if (smoke != null || Unavailable || !preferencesPanel.SavePending())
            return;
        if (multi) { RunAccounts_Click(this, new RoutedEventArgs()); return; }
        string targetKey = retry?.Account ?? selection?.Account ?? TaskAccountKey;
        try
        {
            DailySandbox.RequireBoundAccount(targetKey);
            var current = sessions.Read();
            if (!DailyProfiles.ValidKey(targetKey) || targetKey != current.CurrentKey && !current.Accounts.Any(a => a.Valid && a.AccountKey == targetKey))
                throw new InvalidOperationException("run.account_unavailable");
            if (retry != null) DailyQueueRetry.Validate(dailyQueue.ReadView(targetKey), targetKey, retry);
            selection?.Validate(targetKey, new DailyPreferenceStore(root).Read(targetKey));
        }
        catch (Exception error) { ShowError(error); return; }
        dailyStopping = false;
        QueueView? completedRun = null;
        await OperateAsync(async () =>
        {
                await ConnectTaskAccountAsync(targetKey);
                RefreshAccounts();
                if (dailyStopping)
                    return;
                var current = sessions.Read().CurrentKey;
                if (current != targetKey) throw new InvalidOperationException("run.account_unavailable");
                completedRun = await RunDailyQueueAsync(current, !syncCollection && DailyDesktopBehavior.Read(root).CloseGameAfterRun,
                    () => dailyQueue.RunAsync(current, resume, syncCollection, retry, selection));

        }, connectsGame: true);
        if (!syncCollection && completedRun != null) ShowRunResults([AccountResult(completedRun)]);
    }
    private async Task<QueueView> RunDailyQueueAsync(string account, bool closeGame, Func<Task<QueueView>> execute)
    {
        var target = closeGame ? DailyQueueCompletion.Capture(host, account) : null;
        var result = await execute();
        if (closeGame && DailyQueueCompletion.Eligible(result, account, dailyStopping))
        {
            L.Text(ProgressText, "completion.closing");
            try { await DailyQueueCompletion.CloseAsync(host, target, account, result, dailyStopping); }
            catch (Exception error) { result = result with { CompletionIssue = error.Message }; }
            RefreshAccounts();
        }
        return result;
    }
    private DailyResultWindow? runResultsWindow;
    private DailyAccountRunResult AccountResult(QueueView view) => new(
        sessions.Read().Accounts.FirstOrDefault(a => a.AccountKey == view.Account)?.Name ?? L.Get("report.current_account"), view);
    private void ShowRunResults(IReadOnlyList<DailyAccountRunResult> results)
    {
        if (finalClose || results.Count == 0) return;
        void ShowResults()
        {
            Show(); if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
            runResultsWindow?.Close();
            runResultsWindow = DailyResultWindow.RunResults(results);
            runResultsWindow.Owner = this;
            runResultsWindow.Show();
        }
        // A stopped queue still gets its result panel, but is not announced as finished.
        bool finished = results.All(r => r.View.State is "completed" or "partial" or "failed");
        if (!finished) { ShowResults(); return; }
        var options = ReadDesktopBehavior()?.CompletionNotice ?? new();
        var errors = options.Deliver(channel => {
            if(channel=="popup")ShowResults();
            else if(channel=="sound")System.Media.SystemSounds.Asterisk.Play();
            else { tray ??= new DesktopTray(this, () => { exitChosen=true;Close(); }); tray.Notify(L.Get("report.title"),L.Get("completion.notification",results.Count),ShowResults); }
        });
        if(errors.Length>0)try { DailyJson.Write(Path.Combine(root,"completion-notification-error.json"),new { atUtc=DateTimeOffset.UtcNow, errors }); } catch { }
    }
    private async Task CheckThemesForSmoke()
    {
        var appearancePath = Path.Combine(root, "appearance.json");
        string? originalAppearance = File.Exists(appearancePath) ? File.ReadAllText(appearancePath) : null;
        var planKey = new string('a', 64);
        dailyPanel.SetAccount("演示账号", planKey);
        dailyPanel.ShowPlan(new DailyPreferences());
        dailyPanel.ShowCurrentPlan();
        dailyPanel.SelectPlanAll();
        dailyPanel.SelectPlanTask(dailyPanel.VisibleTasks.First(), false);
        var selectedBefore = dailyPanel.SelectedPlanTasks.ToArray();
        var rowsBefore = dailyPanel.VisibleTasks.ToArray();
        var sharedInk = (SolidColorBrush)FindResource("Ink");
        // Exercise the actual selector, including persistence and a live shared brush.
        ThemeSelector.SelectedIndex = 1;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var lightInk = sharedInk.Color;
        Capture("timeline-plan-light");
        ThemeSelector.SelectedIndex = 2;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (DailyTheme.ReadPreference(root) != DailyAppearance.Dark || !ReferenceEquals(sharedInk, FindResource("Ink"))
            || !SystemParameters.HighContrast && lightInk == sharedInk.Color)
            throw new Exception("Theme selector did not persist or update existing controls");
        if (!dailyPanel.SelectedPlanTasks.SequenceEqual(selectedBefore) || !dailyPanel.VisibleTasks.SequenceEqual(rowsBefore) || !dailyPanel.ShowingPlan)
            throw new Exception("Changing appearance changed plan selection");
        Capture("timeline-plan-dark");
        await dailyPanel.CheckWorkFollowForSmoke();

        var view = new QueueView("running", "", "fixture/theme-running.json",
            [new("guild", "completed", "公会签到已完成", FinishedAt: new DateTimeOffset(DateTime.Today.AddHours(19).AddMinutes(2))), new("management", "completed", "餐厅、鱼笼与领地收益已领取", FinishedAt: new DateTimeOffset(DateTime.Today.AddHours(19).AddMinutes(3))),
             new("weekly_mainline", "running", "血之骑士 · 晨曦之森") { Work = new("absorb", 12, 40, "maps") }, new("mirror", "pending", "使用剩余免费次数"),
             new("trade", "pending", "购买、料理与售卖"), new("mail", "pending", "最后收取本次奖励")], planKey);
        dailyPanel.Show(view);
        dailyPanel.Busy(true);
        dailyPanel.Show(view);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("timeline-running-dark");
        ThemeSelector.SelectedIndex = 1;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (dailyPanel.ShowingPlan || dailyPanel.RetryEnabled || dailyPanel.StartEnabled || dailyPanel.VisibleTasks.Count != 6)
            throw new Exception("Theme change reset the active queue or enabled conflicting actions");
        Capture("timeline-running-light");
        // README screenshots use real controls and a synthetic queue in both languages.
        LanguageSelector.SelectedIndex = 2;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("timeline-running-en-light");
        ThemeSelector.SelectedIndex = 2;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("timeline-running-en-dark");
        LanguageSelector.SelectedIndex = 0;
        ThemeSelector.SelectedIndex = 2;
        WorkspaceTabs.SelectedItem = SettingsTab;
        preferencesPanel.ShowMirrorSettingsForSmoke();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("preferences-dark");
        WorkspaceTabs.SelectedItem = AccountsTab;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("accounts-dark");
        WorkspaceTabs.SelectedItem = ToolsTab;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture("tools-dark");
        WorkspaceTabs.SelectedItem = RunTab;
        Width = 920; Height = 650;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        dailyPanel.CheckViewportForSmoke();
        Capture("timeline-dark-compact");
        Width = 1180; Height = 800;
        ThemeSelector.SelectedIndex = 0;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (DailyTheme.ReadPreference(root) != DailyAppearance.System) throw new Exception("System appearance not persisted");
        dailyPanel.Busy(false);
        dailyPanel.ShowCurrentPlan();
        ThemeSelector.SelectedIndex = 1;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (originalAppearance == null) File.Delete(appearancePath);
        else File.WriteAllText(appearancePath, originalAppearance);
    }
    private async Task CheckRecoveryDialogsForSmoke(int language, int appearance)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (bool sync in new[] { false, true })
        {
            var dialog = DailyDialogs.Message(this, L.Get(sync ? "run.sync_confirm_title" : "run.recovery_title"),
                sync ? L.Get("run.sync_confirm_body") : L.Get("run.recovery_body", L.Stage("weekly_mainline"), L.Get("run.recovery_talent_4")),
                true, sync ? "run.sync_confirm_start" : "run.recovery_continue", safeDefault: true);
            dialog.ShowActivated = false; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -12000; dialog.Top = 0;
            Dispatcher.BeginInvoke(() => {
                try
                {
                    dialog.UpdateLayout();
                    if (DailyDialogs.ModalDepth != 1 || dialog.ActualHeight > SystemParameters.WorkArea.Height)
                        throw new Exception("Recovery dialog is not safely cancellable or exceeds the screen");
                    Capture($"recovery-dialog-{sync}-{language}-{appearance}", (FrameworkElement)dialog.Content);
                }
                finally { dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            if (DailyDialogs.ShowModal(dialog) == true || DailyDialogs.ModalDepth != 0)
                throw new Exception("Closing recovery confirmation accepted work or leaked its shade");
        }
    }
    private async Task SmokeAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "maintenance-priority")
            {
                await CheckMaintenancePriorityForSmoke();
                await CheckUpdateDiscoveryForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status="passed", scope="maintenance-priority", realGameTouched=false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "update-discovery")
            {
                await CheckUpdateDiscoveryForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status="passed", scope="update-discovery", languages=3, themes=2, realGameTouched=false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "single-account-routing")
            {
                await CheckSingleAccountRoutingForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status="passed", scope="single-account-routing", realGameTouched=false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "work-progress")
            {
                await CheckExecutionProgressForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status="passed", scope="work-progress", languages=3, themes=2, realGameTouched=false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "simple-settings")
            {
                L.Audit(); CheckRuntimeLanguagesForSmoke();
                WorkspaceTabs.SelectedItem = SettingsTab;
                preferencesPanel.CheckWeeklySettingsForSmoke();
                foreach (int language in new[] { 0, 1, 2 })
                foreach (int appearance in new[] { 1, 2 })
                foreach (var (title, name) in new[] { ("周常制作装备", "craft"), ("周NPC任务", "npc"), ("亲密度咨询", "friendship") })
                {
                    LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance;
                    preferencesPanel.ShowSimpleSettingForSmoke(title);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture($"simple-settings-{name}-{language}-{appearance}");
                }
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "simple-settings", languages = 3, themes = 2, realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "collection-maps")
            {
                L.Audit();
                WorkspaceTabs.SelectedItem = SettingsTab;
                string directory = Environment.GetEnvironmentVariable("DUSTWEAVE_UI_MAP_CATALOG") ?? AppContext.BaseDirectory;
                int maps = 0;
                foreach (int language in new[] { 0, 1, 2 })
                foreach (int appearance in new[] { 1, 2 })
                {
                    LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance;
                    maps = preferencesPanel.CheckCollectionMapsForSmoke(directory);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture($"collection-maps-{language}-{appearance}");
                }
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "collection-maps", maps, languages = 3, themes = 2, realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="tool-menu")
            {
                L.Select("zh-CN"); WorkspaceTabs.SelectedItem=ToolsTab;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                toolPanel.VerifyFiltersForSmoke();
                Capture("tools-single-column");
                DailyJson.Write(Path.Combine(smoke!,"tool-menu-checks.json"),new{status="passed",singleColumn=true,realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="tool-preparation")
            {
                await CheckToolPreparationForSmoke();
                DailyJson.Write(Path.Combine(smoke!,"smoke.json"),new{status="passed",scope="tool-preparation",languages=3,themes=2,realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="cooking")
            {
                await CheckCookingForSmoke();
                DailyJson.Write(Path.Combine(smoke!,"smoke.json"),new{status="passed",scope="cooking",languages=3,themes=2,realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="run-report")
            {
                await CheckRunReportForSmoke();
                DailyJson.Write(Path.Combine(smoke!,"smoke.json"),new{status="passed",scope="run-report",languages=3,themes=2,realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="tool-activity")
            {
                await CheckToolActivityForSmoke();
                DailyJson.Write(Path.Combine(smoke!,"smoke.json"),new{status="passed",scope="tool-activity",realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if(Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE")=="apostle")
            {
                await CheckApostleForSmoke();
                DailyJson.Write(Path.Combine(smoke!,"smoke.json"),new{status="passed",scope="apostle",languages=3,themes=2,realGameTouched=false});
                Application.Current.Shutdown();return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "outcomes")
            {
                await CheckNoticesForSmoke();
                WorkspaceTabs.SelectedItem = RunTab;
                dailyPanel.ShowOutcomeExplanationsForSmoke();
                foreach (int language in new[] { 0, 1, 2 }) foreach (int appearance in new[] { 1, 2 })
                {
                    LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance;
                    Width = language == 2 ? 920 : 1180; Height = language == 2 ? 750 : 850;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    dailyPanel.CheckOutcomeExplanationsForSmoke();
                    Capture($"outcome-explanations-{language}-{appearance}");
                }
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "outcomes", languages = 3, themes = 2, realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "sandbox-management")
            {
                await CheckSandboxManagementForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "sandbox-management", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "game-preferences")
            {
                await CheckGamePreferencesForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "game-preferences", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "sandbox-setup")
            {
                await CheckSandboxSetupForSmoke();
                await CheckGameLocationForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "sandbox-setup", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "multi-account-setup")
            {
                await CheckMultiAccountSetupForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "multi-account-setup", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "recovery")
            {
                L.Audit();
                DailyRunPanel.CheckRecoveryRoutingForSmoke(Path.Combine(root, "recovery-actions-probe"));
                WorkspaceTabs.SelectedItem = RunTab;
                string recoveryAccount = new('a', 64);
                dailyPanel.SetAccount("Fixture", recoveryAccount);
                dailyPanel.ShowPlan(new DailyPreferences());
                dailyPanel.Show(new("paused", "", "fixture/recovery.json", [
                    new QueueStage("weekly_mainline", "recovery_required", "") { Recovery = [new("dispatch.start", 4)] },
                    new("mail", "pending", "")], recoveryAccount));
                foreach (int language in new[] { 0, 1, 2 }) foreach (int appearance in new[] { 1, 2 })
                {
                    LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance;
                    Width = language == 2 ? 920 : 1180; Height = language == 2 ? 650 : 800;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    dailyPanel.CheckRecoveryActionsForSmoke();
                    Capture($"recovery-actions-{language}-{appearance}");
                    await CheckRecoveryDialogsForSmoke(language, appearance);
                }
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "recovery", languages = 3, themes = 2, realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "bargain")
            {
                L.Audit();
                await CheckBargainForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "bargain", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "presets")
            {
                await CheckPresetsForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "presets", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "trade")
            {
                L.Audit();
                await CheckTradeLanguageForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "trade", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "salvation")
            {
                L.Audit(); WorkspaceTabs.SelectedItem = SettingsTab;
                foreach (int language in new[] { 0, 1, 2 })
                foreach (int theme in new[] { 1, 2 })
                {
                    LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = theme;
                    preferencesPanel.CheckFixedTowerSettingsForSmoke();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture($"salvation-{language}-{theme}");
                }
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "salvation", languages = 3, themes = 2, realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "software-settings")
            {
                L.Audit();
                await CheckSoftwareSettingsForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "software-settings", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "accounts")
            {
                L.Audit();
                await CheckAccountsForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "accounts", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "plugins")
            {
                L.Audit(); await SmokePluginsAsync();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "plugins", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "ux")
            {
                L.Audit();
                await CheckAccountsForSmoke();
                await CheckPresentationForSmoke();
                await CheckThemesForSmoke();
                await CheckSchedulingForSmoke();
                await CheckSupportForSmoke();
                await CheckGameLocationForSmoke();
                await CheckSoftwareSettingsForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "ux", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "support")
            {
                await CheckSupportForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "support", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "task-navigation")
            {
                await CheckTaskNavigationForSmoke();
                await CheckParallelForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "task-navigation", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "scheduling")
            {
                await CheckSchedulingForSmoke();
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "scheduling", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            await CheckGameLocationForSmoke();
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "game-location")
            {
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "game-location", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            await CheckFirstRunForSmoke();
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_SCOPE") == "first-run")
            {
                DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "first-run", realGameTouched = false });
                Application.Current.Shutdown(); return;
            }
            await CheckThemesForSmoke();
            await CheckMultiAccountSetupForSmoke();
            await CheckLanguagesForSmoke();
            await CheckPresentationForSmoke();
            // The preceding checks leave another tab selected. Materialize the
            // account page before asserting visibility of its recovery controls.
            WorkspaceTabs.SelectedItem = AccountsTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await CheckAccountsForSmoke(); await CheckSchedulingForSmoke(); await CheckParallelForSmoke();
            await CheckPresetsForSmoke();
            Capture("normal");
            WorkspaceTabs.SelectedItem = ToolsTab;
            toolPanel.VerifyFiltersForSmoke();
            string toolJournal=Path.Combine(root,"tools","window.json");
            using(var currentProcess=System.Diagnostics.Process.GetCurrentProcess())
            {
                try {
                    DailyJson.Write(toolJournal,new DailyToolProcessState("fishing",currentProcess.Id,currentProcess.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!));
                    SetBusy(false);
                    if(!ConnectButton.IsEnabled||!preferencesPanel.IsEnabled)throw new InvalidOperationException("Idle tool window blocks daily controls");
                    using(DailyToolControl.Acquire(root)) { SetBusy(false);if(ConnectButton.IsEnabled||preferencesPanel.IsEnabled)throw new InvalidOperationException("Active tool leaves daily controls enabled"); }
                    SetBusy(false);if(!ConnectButton.IsEnabled)throw new InvalidOperationException("Stopped tool did not release daily controls");
                } finally { File.Delete(toolJournal);SetBusy(false); }
            }
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("tools");
            CheckAccountOrderForSmoke();
            await CheckAccountIdentityForSmoke();
            WorkspaceTabs.SelectedItem = RunTab;
            var retryView = new QueueView("paused", "上次中断于进度保存；已完成环节保留，可勾选剩余环节补跑。", "fixture/result.json", new[] { new QueueStage("mirror", "completed", "免费次数已用完"), new QueueStage("daily_dispatch", "pending", "尚未执行"), new QueueStage("weekly_mainline", "partial", "吸收次数已用完，次日接续"), new QueueStage("trade", "blocked", "当前存在未处理弹窗"), new QueueStage("mail", "pending", "尚未执行"), new QueueStage("event_rewards", "recovery_required", "上次答题已中断，先核对完成记录") }, new string('a', 64));
            dailyPanel.SetAccount("冒烟测试账号", new string('a', 64));
            dailyPanel.ShowPlan(new DailyPreferences());
            dailyPanel.LoadHistory(retryView);
            if (dailyPanel.ShowingPlan)
                throw new Exception("Restart hid unfinished current-account report");
            dailyPanel.ShowCurrentPlan();
            if (!dailyPanel.ShowingPlan || !dailyPanel.VisibleTasks.Contains("weekly_book") || !dailyPanel.VisibleTasks.Contains("weekly_equipment"))
                throw new Exception("Old history hid new weekly stages on startup");
            dailyPanel.ShowOperationError("fixture: component fingerprint mismatch");
            dailyPanel.LoadHistory(retryView);
            dailyPanel.ShowPlan(new DailyPreferences());
            if (!dailyPanel.DisplayedMessage.Contains(DailyUserText.Describe("fixture: component fingerprint mismatch"))
                || !dailyPanel.DisplayedMessage.Contains(L.Get("issue.action.unknown")))
                throw new Exception("History refresh erased queue startup failure or next action");
            RunTab.Content = dailyPanel; dailyPanel.ClearOperationError();
            dailyPanel.ShowCurrentPlan();
            CheckPlanSelectionForSmoke(retryView);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("current-plan");
            dailyPanel.ShowHistory();
            dailyPanel.SelectUnfinished();
            dailyPanel.LoadHistory(retryView);
            if (dailyPanel.SelectedTasks.Count != 5)
                throw new Exception("Idle refresh lost same-day selection");
            dailyPanel.SetAccount("其他账号", new string('b', 64));
            dailyPanel.LoadHistory(retryView);
            dailyPanel.ShowHistory();
            dailyPanel.SelectUnfinished();
            if (dailyPanel.RetryEnabled)
                throw new Exception("Cross-account history was actionable");
            dailyPanel.SetAccount("冒烟测试账号", new string('a', 64));
            dailyPanel.LoadHistory(retryView);
            dailyPanel.SelectUnfinished();
            if (dailyPanel.ShowingPlan || !dailyPanel.RetryEnabled || dailyPanel.SelectedTasks.Count != 5)
                throw new Exception("Account switch did not restore unfinished history");
            dailyPanel.LoadHistory(retryView with
            {
                Expired = true
            });
            if (!dailyPanel.ShowingPlan || dailyPanel.SelectedTasks.Count != 0 || dailyPanel.RetryEnabled || dailyPanel.ResumeEnabled)
                throw new Exception("Daily rollover did not reset plan and retry selection");
            dailyPanel.ShowHistory();
            dailyPanel.SelectUnfinished();
            if (dailyPanel.RetryEnabled || dailyPanel.ResumeEnabled || dailyPanel.SelectedTasks.Count != 0)
                throw new Exception("Expired history remains actionable");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("expired-history");
            dailyPanel.ShowCurrentPlan();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("reset-plan");
            dailyPanel.Busy(true);
            dailyPanel.Show(retryView);
            dailyPanel.LoadHistory(retryView with
            {
                Expired = true
            });
            if (dailyPanel.ShowingPlan)
                throw new Exception("Timer reset an active queue");
            dailyPanel.Busy(false);
            dailyPanel.Show(retryView);
            dailyPanel.SelectUnfinished();
            if (dailyPanel.SelectedTasks.Count != 5 || !dailyPanel.RetryEnabled)
                throw new Exception("Incomplete selection is unavailable");
            dailyPanel.Show(retryView);
            if (dailyPanel.SelectedTasks.Count != 5)
                throw new Exception("Progress refresh cleared selection");
            dailyPanel.Busy(true);
            if (dailyPanel.RetryEnabled)
                throw new Exception("Retry is enabled during execution");
            dailyPanel.Busy(false);
            dailyPanel.ClearSelection();
            dailyPanel.Show(retryView);
            if (dailyPanel.SelectedTasks.Count != 0 || dailyPanel.RetryEnabled)
                throw new Exception("Refresh changed a manual deselection");
            dailyPanel.SelectUnfinished();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("daily-run");
            DailyRunPanel.CheckRecoveryRoutingForSmoke(Path.Combine(root,"recovery-actions-probe"));
            var recoveryTheme=ThemeSelector.SelectedIndex;var recoveryLanguage=LanguageSelector.SelectedIndex;
            double recoveryWidth=Width,recoveryHeight=Height;
            try {
                foreach(int language in new[]{0,1,2}) foreach(int appearance in new[]{1,2}) {
                    LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;
                    Width=language==2?920:1180;Height=language==2?650:800;
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    dailyPanel.CheckRecoveryActionsForSmoke();
                    Capture($"recovery-actions-{language}-{appearance}");
                    await CheckRecoveryDialogsForSmoke(language, appearance);
                }
                dailyPanel.ClearSelection();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                dailyPanel.CheckRecoveryActionsForSmoke();
                Capture("recovery-actions-empty");
                dailyPanel.SelectUnfinished();
            } finally { LanguageSelector.SelectedIndex=recoveryLanguage;ThemeSelector.SelectedIndex=recoveryTheme;Width=recoveryWidth;Height=recoveryHeight; }
            try {
                dailyPanel.ShowOutcomeExplanationsForSmoke();
                foreach(int language in new[]{0,1,2}) foreach(int appearance in new[]{1,2}) {
                    LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;
                    Width=language==2?920:1180;Height=language==2?650:800;
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    dailyPanel.CheckOutcomeExplanationsForSmoke();
                    Capture($"outcome-explanations-{language}-{appearance}");
                }
            } finally {
                LanguageSelector.SelectedIndex=recoveryLanguage;ThemeSelector.SelectedIndex=recoveryTheme;
                Width=recoveryWidth;Height=recoveryHeight;dailyPanel.Show(retryView);
            }
            try {
                foreach(int language in new[]{0,1,2}) foreach(int appearance in new[]{1,2}) {
                    LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;
                    var taskWindow=dailyPanel.OpenTaskDetailsForSmoke();
                    try {
                        if(language==2){taskWindow.Width=480;taskWindow.Height=420;}
                        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        if(taskWindow.TaskCount!=3 || taskWindow.TaskTitles[0].Text!="每日登录" || taskWindow.TaskScroll.ActualHeight<120)
                            throw new Exception("Saved task progress is missing from the details window");
                        var closeBounds=taskWindow.CloseAction.TransformToAncestor(taskWindow).TransformBounds(new Rect(taskWindow.CloseAction.RenderSize));
                        if(closeBounds.Bottom>taskWindow.ActualHeight || closeBounds.Right>taskWindow.ActualWidth)
                            throw new Exception("The task details close action is clipped");
                        Capture($"pending-task-details-{language}-{appearance}",(FrameworkElement)taskWindow.Content);
                        Capture($"pending-task-button-{language}-{appearance}");
                    } finally {taskWindow.Close();}
                }
                var many=dailyPanel.OpenTaskDetailsForSmoke(true);
                try {
                    many.Width=480;many.Height=420;
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    if(many.TaskScroll.ScrollableHeight<=0)throw new Exception("Long task details cannot scroll");
                    many.TaskScroll.ScrollToBottom();
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Capture("pending-task-details-scroll",(FrameworkElement)many.Content);
                    many.CloseAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(many.IsVisible)throw new Exception("Task details close button does not close the window");
                } finally {if(many.IsVisible)many.Close();}
            } finally {LanguageSelector.SelectedIndex=recoveryLanguage;ThemeSelector.SelectedIndex=recoveryTheme;dailyPanel.Show(retryView);}
            dailyPanel.Show(new QueueView("running", "共享路线", "fixture/shared.json", new[] { new QueueStage("weekly_mainline", "running", "第2章 · 地图21 · 已核对3/24张地图"), new QueueStage("weekly_npc", "running", "第2章 · 地图21 · 本周完成1/3 · 追悼米莎的灵魂"), new QueueStage("weekly_steal", "running", "第2章 · 地图21 · 已核对2/12张地图") }, new string('a', 64)));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("daily-weekly-progress");
            dailyPanel.CheckMessagesForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("daily-chinese-messages");
            dailyPanel.Show(retryView);
            WorkspaceTabs.SelectedItem = SettingsTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences");
            preferencesPanel.ShowSaffronSettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-saffron");
            preferencesPanel.ReviewCategoryForSmoke("all");
            preferencesPanel.ShowWeeklySettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-weekly");
            preferencesPanel.CheckWeeklySettingsForSmoke();
            preferencesPanel.CheckSearchForSmoke();
            preferencesPanel.ShowMirrorSettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-mirror");
            preferencesPanel.ShowEventSettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-events");
            preferencesPanel.CheckOptionalSettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-available");
            WorkspaceTabs.SelectedItem = AccountsTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("accounts");
            await SmokePluginsAsync();
            WorkspaceTabs.SelectedItem = DiagnosticsTab;
            Width = 920;
            Height = 650;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("compact");
            WorkspaceTabs.SelectedItem = ToolsTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("tools-compact");
            WorkspaceTabs.SelectedItem = RunTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("daily-run-compact");
            dailyPanel.CheckViewportForSmoke();
            dailyPanel.ShowCurrentPlan();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("current-plan-compact");
            dailyPanel.CheckViewportForSmoke();
            WorkspaceTabs.SelectedItem = SettingsTab;
            preferencesPanel.CheckOptionalSettingsForSmoke();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture("preferences-available-compact");
            WorkspaceTabs.SelectedItem = DiagnosticsTab;
            if (host is DemoEnvironment fixture)
                fixture.TitleVisible = true;
            await coordinator.ConnectCurrentAsync();
            if (host is DemoEnvironment checkedFixture && checkedFixture.TitleClicks != 1)
                throw new Exception("Packaged title entry did not run exactly once");
            await coordinator.InspectAsync(rows.Take(2).Select(r => r.Account));
            RefreshAccounts();
            if (profiles.Read().Count(p => p.LastVerifiedUtc != null) != 2)
                throw new Exception("Smoke login queue did not verify two accounts");
            Capture("verified");
            var observe = guild.ObserveAsync();
            for(int i=0;i<100&&!File.Exists(guild.LastTrace)&&!observe.IsCompleted;i++)await Task.Delay(20);
            guild.Stop();
            await observe;
            if (!File.Exists(guild.LastTrace))
                throw new Exception("Observer did not record isolated snapshots");
            await CheckSupportForSmoke();
            await CheckSoftwareSettingsForSmoke();
            await CheckTaskNavigationForSmoke();
            DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new
            {
                status = "passed",
                accounts = 2,
                realGameTouched = false
            });
            Application.Current.Shutdown();
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "failed", error = e.ToString() }); Application.Current.Shutdown(1); }
    }
    private void CheckPlanSelectionForSmoke(QueueView history)
    {
        var defaults = new DailyPreferences();
        var enabled = DailyStageCatalog.All.Where(s => s.Enabled(defaults)).Select(s => s.Id).ToArray();
        if (!dailyPanel.SelectedPlanTasks.SequenceEqual(enabled) || !dailyPanel.StartEnabled)
            throw new Exception("Fresh plan must select configured stages by default");
        dailyPanel.ToggleFirstPlanCheckboxForSmoke();
        if (dailyPanel.SelectedPlanTasks.Contains(enabled[0]))
            throw new Exception("Rendered checkbox cannot deselect a planned stage");
        dailyPanel.ToggleFirstPlanCheckboxForSmoke();
        if (!dailyPanel.SelectedPlanTasks.Contains(enabled[0]))
            throw new Exception("Rendered checkbox cannot reselect a planned stage");
        QueuePlanRequest? sent = null;
        void CapturePlan(QueuePlanRequest request) => sent = request;
        dailyPanel.PlanRequested += CapturePlan;
        try
        {
            dailyPanel.ClearSelection();
            dailyPanel.StartCurrentSelection();
            if (dailyPanel.StartEnabled || sent != null)
                throw new Exception("Empty selection dispatched whole queue");
            if (!DailyStageCatalog.All.Any(s => s.Id == "weekly_npc") || !DailyStageCatalog.All.Any(s => s.Id == "weekly_steal"))
                throw new Exception("Weekly route stages missing");
            dailyPanel.Busy(true);
            dailyPanel.Busy(false);
            dailyPanel.SelectPlanTask("mail", true);
            dailyPanel.SelectPlanTask("free_draws", true);
            dailyPanel.ShowPlan(new DailyPreferences());
            dailyPanel.LoadHistory(history);
            dailyPanel.ShowCurrentPlan();
            if (!dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "free_draws", "mail" }))
                throw new Exception("Refresh lost current plan selection");
            dailyPanel.Busy(true);
            dailyPanel.SelectPlanTask("mirror", true);
            dailyPanel.StartCurrentSelection();
            if (dailyPanel.StartEnabled || sent != null || dailyPanel.SelectedPlanTasks.Contains("mirror"))
                throw new Exception("Busy plan can be edited or dispatched");
            dailyPanel.Busy(false);
            dailyPanel.StartCurrentSelection();
            if (sent?.Account != new string('a', 64) || !sent.Tasks.SequenceEqual(new[] { "free_draws", "mail" }))
                throw new Exception("Plan event lost account or subset");
            sent = null;
            // Identical row IDs across plan/history must still have separate checkbox state.
            var matching = history with
            {
                Stages = enabled.Select(id => new QueueStage(id, "pending", "")).ToArray()
            };
            dailyPanel.LoadHistory(matching);
            dailyPanel.ShowHistory();
            dailyPanel.SelectUnfinished();
            dailyPanel.ClearSelection();
            dailyPanel.StartCurrentSelection();
            if (sent != null || !dailyPanel.ShowingPlan || !dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "free_draws", "mail" }))
                throw new Exception("History action changed or dispatched temporary plan");
            defaults.Stages.Mail = false;
            dailyPanel.ShowPlan(defaults);
            if (dailyPanel.SelectedPlanTasks.Contains("mail") || dailyPanel.VisibleTasks.Contains("mail"))
                throw new Exception("Disabled stage remained selectable");
            defaults.Stages.Mail = true;
            dailyPanel.ShowPlan(defaults);
            if (!dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "free_draws", "mail" }))
                throw new Exception("Newly enabled stage or prior choice was lost");
            dailyPanel.ClearSelection();
            dailyPanel.ShowPlan(defaults);
            dailyPanel.LoadHistory(history);
            if (dailyPanel.SelectedPlanTasks.Count != 0 || dailyPanel.StartEnabled)
                throw new Exception("Refresh reversed manual clear-all");
            dailyPanel.SetAccount("其他账号", new string('b', 64));
            dailyPanel.ShowPlan(new DailyPreferences());
            if (!dailyPanel.SelectedPlanTasks.SequenceEqual(enabled))
                throw new Exception("Temporary plan leaked between accounts");
            dailyPanel.SetAccount("冒烟测试账号", new string('a', 64));
            dailyPanel.ShowPlan(new DailyPreferences());
            dailyPanel.LoadHistory(history);
            dailyPanel.ShowCurrentPlan();
        }
        finally { dailyPanel.PlanRequested -= CapturePlan; }
    }
    private async Task CheckAccountIdentityForSmoke()
    {
        if (sessions is not DemoEnvironment fixture) throw new Exception("Identity smoke requires isolated sessions");
        var original = fixture.CurrentKey;
        try
        {
            fixture.CurrentKey = fixture.Accounts[1].AccountKey;
            nextAccountRefresh = DateTime.MinValue;
            await RefreshAccountIdentityAsync();
            if (catalog?.CurrentKey != fixture.CurrentKey || !dailyPanel.CurrentAccountText.Text.Contains(fixture.Accounts[1].Name))
                throw new Exception("Idle account change did not refresh the visible catalog");
            if (fixture.Calls.Any()) throw new Exception("Account identity refresh triggered gameplay or account writes");
        }
        finally { fixture.CurrentKey = original; RefreshAccounts(); }
    }

    private void CheckAccountOrderForSmoke()
    {
        if (sessions is not DemoEnvironment fixture)
            throw new Exception("Account order smoke requires isolated sessions");
        var original = fixture.Accounts.ToArray();
        var saved = profiles.Read();
        for (int i = 4; i <= 10; i++)
            fixture.Accounts.Add(new(i, $"测试账号 {i}", DailyIdentity.MemberKey((1000 + i).ToString()), $"***{1000 + i}", true, false, ""));
        try
        {
            profiles.Update(DailyAccountOrder.ProfileFor(fixture.Accounts[0], saved));
            profiles.Update(DailyAccountOrder.ProfileFor(fixture.Accounts[9], saved));
            RefreshAccounts();
            void CheckSlots(IEnumerable<int> expected)
            {
                if (!rows.Select(r => r.Account.SlotNumber).SequenceEqual(expected) || !rows.Select(r => r.OrderLabel).SequenceEqual(Enumerable.Range(1, rows.Count).Select(n => n.ToString())))
                    throw new Exception("Account grid order or row labels changed");
            }
            CheckSlots(Enumerable.Range(1, 10));
            rows[1].Selected = true;
            RefreshAccounts();
            CheckSlots(Enumerable.Range(1, 10));
            AccountsGrid.SelectedItem = rows[9];
            Move(-1);
            CheckSlots(Enumerable.Range(1, 8).Concat(new[] { 10, 9 }));
            RefreshAccounts();
            CheckSlots(Enumerable.Range(1, 8).Concat(new[] { 10, 9 }));
            Move(1);
            CheckSlots(Enumerable.Range(1, 10));
            if (fixture.Calls.Any())
                throw new Exception("Account sorting touched the session service");
        }
        finally
        {
            fixture.Accounts.Clear();
            fixture.Accounts.AddRange(original);
            DailyJson.Write(Path.Combine(root, "accounts.json"), saved);
            RefreshAccounts();
        }
    }
    private void Capture(string name, FrameworkElement? visual = null)
    {
        UpdateLayout();
        visual ??= (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(visual);
        var target = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(smoke!);
        using var file = File.Create(Path.Combine(smoke!, name + ".png"));
        encoder.Save(file);
    }
}

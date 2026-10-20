extern alias apostle;
extern alias toolBD2ApostleDefense;
using A = apostle::BD2ApostleDefense;
using Board = toolBD2ApostleDefense::BD2ApostleDefense.Desktop.BattleBoard;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class ApostleToolPanel : UserControl, IDisposable
{
    readonly ApostleToolSession session;
    readonly Board board = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly List<string> recent = new();
    readonly IDisposable locale;
    readonly Func<bool> canStart;
    readonly apostle::BD2ApostleDefense.Localization.LanguageCatalog messages = new();
    bool initialized, stopping, disposed;
    string error = "";
    static DailyLanguage L => DailyLanguage.Current;
    public event Action? ActivityChanged;
    internal bool Connecting => session.Starting;
    internal ApostleToolPanel(ApostleToolSession session, Func<bool> canStart)
    {
        this.session = session; this.canStart = canStart;
        locale = HostedToolLocale.Begin(L.Code);
        InitializeComponent(); BoardHost.Content = board;
        var p = session.LoadSettings(); ClearGoal.IsChecked = p.Clear50; RareGoal.IsChecked = p.Rare;
        AutoNext.IsChecked = p.AutoNext; StopRound.IsChecked = p.StopAfterRound; Interval.Text = p.IntervalMs.ToString();
        initialized = true; session.Diagnostic += OnDiagnostic;
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        timer.Tick += async (_, _) => { await session.PollAsync(); if (!disposed) Render(); }; timer.Start();
        LanguageChanged(null, EventArgs.Empty);
    }
    A.Settings ReadSettings()
    {
        if (!int.TryParse(Interval.Text, out int interval) || interval < 100 || interval > 3000) throw new ArgumentException("apostle.invalid_interval");
        var value = new A.Settings { Clear50 = ClearGoal.IsChecked == true, Rare = RareGoal.IsChecked == true, AutoNext = AutoNext.IsChecked == true, StopAfterRound = StopRound.IsChecked == true, IntervalMs = interval };
        if (value.Validate() is { Length: > 0 } reason) throw new ArgumentException(reason);
        return value;
    }
    void SettingsChanged(object? sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        try { session.SaveSettings(ReadSettings()); error = ""; } catch (Exception ex) { error = ex.Message; }
        Render();
    }
    async void StartClicked(object sender, RoutedEventArgs e)
    {
        if (!canStart() || stopping) return;
        if (!session.Connected) { await ConnectAsync(); return; }
        try { var settings = ReadSettings(); var task = session.StartAsync(settings); ActivityChanged?.Invoke();Render(); await task; error = ""; }
        catch (Exception ex) { error = ex.GetBaseException().Message; }
        if (!disposed) {ActivityChanged?.Invoke();Render();}
    }
    internal async Task ConnectAsync()
    {
        if (disposed || stopping || !canStart() || session.Starting || session.Occupied) return;
        try
        {
            var task=session.ConnectAsync();ActivityChanged?.Invoke();Render();
            await task;error="";
        }
        catch(Exception ex) { error=ex.GetBaseException().Message; }
        if(!disposed){ActivityChanged?.Invoke();Render();}
    }
    async void PauseClicked(object sender, RoutedEventArgs e) => await StopAsync();
    async void ConnectClicked(object sender, RoutedEventArgs e) => await ConnectAsync();
    internal async Task<bool> StopAsync()
    {
        if (stopping) return false;
        stopping = true;ActivityChanged?.Invoke(); Render();
        try { return await session.StopAsync(); }
        finally { stopping = false; if (!disposed) {ActivityChanged?.Invoke();Render();} }
    }
    void LanguageChanged(object? sender, EventArgs e)
    {
        AppDomain.CurrentDomain.SetData(HostedToolLocale.Key, L.Code); messages.Select(L.Code); board.ApplyHostedLanguage(L.Code); Render();
    }
    string Text(string source) => source.StartsWith("apostle.", StringComparison.Ordinal) ? L.Get(source) : messages.Text(L.Translate(source));
    void OnDiagnostic(string source) => Dispatcher.BeginInvoke(() => {
        if (disposed) return;
        recent.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + source); if (recent.Count > 12) recent.RemoveAt(recent.Count - 1);
    });
    internal void Render()
    {
        ConnectButton.IsEnabled = !stopping && !session.Occupied && !session.Starting && canStart();
        StartButton.IsEnabled = ConnectButton.IsEnabled && session.Connected;
        PauseButton.IsEnabled = !stopping && (session.Starting || session.Occupied);
        PauseButton.Content = L.Get(session.Starting ? "apostle.cancel_connection" : "tools.action_stop");
        StatusText.Text = L.Get(stopping ? "apostle.stopping" : session.Starting ? "apostle.preparing" : session.Running ? "apostle.running" : session.Occupied ? "apostle.settling" : session.Connected ? "apostle.connected" : "apostle.disconnected");
        bool loading=session.Starting || stopping;
        ConnectionProgress.Visibility=loading?Visibility.Visible:Visibility.Collapsed;
        ConnectionProgress.IsIndeterminate=loading && SystemParameters.ClientAreaAnimation;
        ConnectionProgress.Value=loading?50:0;
        DecisionText.Text = Text(session.Message); SettingsError.Text = Text(error);
        RecentList.ItemsSource = recent.Select(Text).ToArray();
        var s = session.Snapshot;
        ClearProgress.Text = s?.AchievementsKnown == true ? L.Get("apostle.clear_progress", s.ClearProgress, s.ClearTarget) : L.Get("apostle.wait_progress");
        RareProgress.Text = s?.AchievementsKnown == true ? L.Get("apostle.rare_progress", s.RareProgress, s.RareTarget) : L.Get("apostle.wait_progress");
        PendingProgress.Text = s == null ? "" : L.Get("apostle.pending", s.RoundRare);
        WaveText.Text = s == null ? L.Get("apostle.board") : L.Get("apostle.wave", s.Wave, Math.Max(50, s.Catalog.Waves.Length));
        Metrics.Text = s == null ? L.Get("apostle.board_hint") : L.Get("apostle.metrics", s.Gold, s.EnemyCount, s.Catalog.GameOverCount, s.SecondsLeft);
        if (IsVisible && s != null) board.Update(s, session.Decision);
        board.SetFresh(s != null && DateTime.UtcNow.Ticks - s.At < TimeSpan.FromSeconds(5).Ticks);
    }
    public void Dispose()
    {
        disposed = true; timer.Stop(); session.Diagnostic -= OnDiagnostic;
        WeakEventManager<DailyLanguage, EventArgs>.RemoveHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        board.ReleaseHostedLanguage(); locale.Dispose(); session.Dispose();
    }
    internal void CheckForSmoke()
    {
        UpdateLayout();
        if(BoardHost.ActualWidth<300 || BoardHost.ActualHeight<540)throw new Exception("Tool board has insufficient room");
        if(Interval.Text!="350" || StopRound.IsChecked!=true)throw new Exception("UI state was not retained");
        if(StatusText.Text!=L.Get(session.Running?"apostle.running":"apostle.connected"))throw new Exception("Status not localized");
        if(StartButton.IsEnabled==session.Running || PauseButton.IsEnabled!=session.Running)throw new Exception("Start/pause availability mismatch");
        if(L.Code=="en-US" && (WaveText.Text.Contains('波') || ClearProgress.Text.Contains('次')))throw new Exception("English progress not translated");
    }
    internal void CheckLoadingForSmoke()
    {
        Render();
        if(!session.Starting || StartButton.IsEnabled || !PauseButton.IsEnabled || !ConnectionProgress.IsVisible ||
            PauseButton.Content.ToString()!=L.Get("apostle.cancel_connection"))throw new Exception("Loading must show progress, disable start and allow cancellation");
        ConnectionProgress.ApplyTemplate();ConnectionProgress.UpdateLayout();
        if(ConnectionProgress.IsIndeterminate!=SystemParameters.ClientAreaAnimation)throw new Exception("Loading must respect reduced motion");
        if(SystemParameters.ClientAreaAnimation &&
            (ConnectionProgress.Template.FindName("LoadingSweep",ConnectionProgress) is not Border sweep ||
             sweep.OpacityMask?.RelativeTransform.HasAnimatedProperties!=true))
            throw new Exception("Tool must use the shared animated loading indicator");
    }
    internal void CheckInvalidSettingsForSmoke()
    {
        Interval.Text="50";SettingsChanged(null,new());
        if(Interval.Text!="50" || SettingsError.Text!=L.Get("apostle.invalid_interval") || session.LoadSettings().IntervalMs!=350)
            throw new Exception("Invalid input was lost or saved");
        Interval.Text="350";SettingsChanged(null,new());
    }
    internal void CheckDisconnectedForSmoke()
    {
        Render();
        if(!ConnectButton.IsEnabled||StartButton.IsEnabled||PauseButton.IsEnabled||
            ConnectionProgress.IsVisible||string.IsNullOrWhiteSpace(DecisionText.Text))
            throw new Exception("Connection failure must show a reason and an available reconnect action");
    }
}

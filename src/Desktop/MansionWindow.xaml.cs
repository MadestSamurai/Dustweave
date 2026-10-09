using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;
public partial class MansionWindow : Window
{
    readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    readonly bool smoke;
    MansionClient? client;
    JsonObject? snapshot;
    bool polling, closing, closeReady;
    string status = "ready";
    sealed record Preferences(string Mode = "Normal", bool Retry = true, bool Chain80 = false);
    static string PreferencesPath => Path.Combine(DailyIdentity.DataRoot, "tools", "mansion-runaway-settings.json");
    public bool HostedAutomationEnabled => client?.Enabled == true;
    static DailyLanguage L => DailyLanguage.Current;

    public MansionWindow(bool smoke = false)
    {
        this.smoke = smoke;
        InitializeComponent();
        DailyDialogs.Prepare(this);
        ShowInTaskbar = true;
        if (!smoke && DailyJson.TryRead<Preferences>(PreferencesPath)is { } preferences)
        {
            ChallengeMode.IsChecked = preferences.Mode == "Challenge";
            NormalMode.IsChecked = preferences.Mode != "Challenge";
            RetryBox.IsChecked = preferences.Retry;
            Goal80Box.IsChecked = preferences.Chain80;
        }

        timer.Tick += async (_, _) => await Poll();
        L.Changed += LanguageChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            timer.Stop();
            L.Changed -= LanguageChanged;
        };
        Render();
    }

    async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (smoke || polling)
            return;
        polling = true;
        status = "connecting";
        Render();
        try
        {
            client = await Task.Run(() => new MansionClient(DailyIdentity.DataRoot, int.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID") ?? "0"), long.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START") ?? "0"), Environment.GetEnvironmentVariable("BD2_DAILY_TOOL_FINGERPRINT") ?? ""));
            status = "ready";
            timer.Start();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            polling = false;
            Render();
        }
    }

    async void StartClicked(object sender, RoutedEventArgs e)
    {
        if (e.Handled || client == null || polling)
            return;
        polling = true;
        client.Mode = ChallengeMode.IsChecked == true ? "Challenge" : "Normal";
        client.Retry = RetryBox.IsChecked == true;
        client.Chain80 = Goal80Box.IsChecked == true;
        try
        {
            DailyJson.Write(PreferencesPath, new Preferences(client.Mode, client.Retry, client.Chain80));
            await Task.Run(client.Start);
            status = "entering";
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            polling = false;
            Render();
        }
    }

    async void StopClicked(object sender, RoutedEventArgs e) => await Stop();
    async Task Stop()
    {
        timer.Stop();
        while (polling)
            await Task.Delay(15);
        polling = true;
        try
        {
            if (client != null)
                await Task.Run(client.Stop);
            status = "paused";
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            polling = false;
            if (!closing && client != null)
                timer.Start();
            Render();
        }
    }

    async Task Poll()
    {
        if (polling || client == null || closing)
            return;
        polling = true;
        try
        {
            var value = await Task.Run(client.Poll);
            if (value != null)
            {
                snapshot = value;
                status = value["Reason"]?.ToString() ?? "ready";
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            polling = false;
            Render();
        }
    }

    void Fail(Exception error)
    {
        timer.Stop();
        var failedClient = client;
        client = null;
        status = "disconnected";
        if (failedClient != null)
            _ = Task.Run(() =>
            {
                try
                {
                    failedClient.Stop();
                }
                catch
                { /* The native lease also expires without heartbeats. */
                }
            });
        DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "tools", "mansion-runaway-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() });
    }

    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady)
            return;
        e.Cancel = true;
        if (closing)
            return;
        closing = true;
        await Stop();
        closeReady = true;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) Close(); }));
    }

    void LanguageChanged(object? sender, EventArgs e) => Render();
    internal void SetPreview(JsonObject value, bool chain80 = false)
    {
        snapshot = value;
        Goal80Box.IsChecked = chain80;
        status = value["Reason"]?.ToString() ?? "collecting";
        Render();
    }

    void GoalChanged(object sender, RoutedEventArgs e)
    {
        if (GoalHint == null) return;
        if (Goal80Box.IsChecked == true) ChallengeMode.IsChecked = true;
        Render();
    }

    void Render()
    {
        bool active = HostedAutomationEnabled;
        ConnectButton.IsEnabled = !active && !polling;
        ConnectButton.Visibility = !smoke && status == "disconnected" ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = client != null && !active && !polling;
        StopButton.IsEnabled = active;
        bool inRound = snapshot?["Result"] is null && snapshot?["State"]?.ToString()is "Playing" or "Caught" or "StageClear";
        if (inRound && snapshot?["Mode"]?.ToString()is { } nativeMode)
        {
            ChallengeMode.IsChecked = nativeMode == "Challenge";
            NormalMode.IsChecked = nativeMode != "Challenge";
        }

        if (inRound && snapshot?["Mode"]?.ToString() == "Normal") Goal80Box.IsChecked = false;
        bool goal80 = Goal80Box.IsChecked == true;
        NormalMode.IsEnabled = ChallengeMode.IsEnabled = !active && !inRound && !goal80;
        Goal80Box.IsEnabled = !active && (!inRound || snapshot?["Mode"]?.ToString() == "Challenge");
        GoalHint.Visibility = GoalProgress.Visibility = goal80 ? Visibility.Visible : Visibility.Collapsed;
        RetryBox.Visibility = goal80 ? Visibility.Collapsed : Visibility.Visible;
        RetryBox.IsEnabled = !active;
        StatusText.Text = L.Get("mansion.state." + (States.Contains(status) ? status : "waiting-game"));
        if (snapshot?["ItemWarning"]?.GetValue<bool>() == true)
            StatusText.Text += " · " + L.Get("mansion.item_warning");
        var goal = snapshot?["Objective"];
        StageText.Text = goal?["Stage"]?.ToString()is { Length: > 0 } stage ? stage : "—";
        CandyText.Text = goal?["OrdinaryRemaining"]?.ToString() ?? "—";
        TimeText.Text = goal?["Seconds"] is { } seconds ? TimeSpan.FromSeconds(Math.Max(0, seconds.GetValue<double>())).ToString(@"mm\:ss") : "—";
        HealthText.Text = L.Get("mansion.health", snapshot?["Board"]?["Health"]?.ToString() ?? "—");
        ScoreText.Text = L.Get("mansion.score", snapshot?["Result"]?["Score"]?.ToString() ?? goal?["Score"]?.ToString() ?? "—");
        ChainText.Text = L.Get("mansion.chain", goal?["Chain"]?.ToString() ?? "—", goal?["MaxChain"]?.ToString() ?? "—");
        int maximum = snapshot?["Result"]?["MaxChain"]?.GetValue<int>() ?? goal?["MaxChain"]?.GetValue<int>() ?? 0;
        GoalProgress.Text = L.Get("mansion.goal80_progress", maximum);
        if (active && goal80 && maximum >= 80 && snapshot?["Result"] == null)
            StatusText.Text = L.Get("mansion.state.goal-finishing");
        Map.Frame = snapshot;
        EmptyText.Visibility = snapshot?["Board"] is null ? Visibility.Visible : Visibility.Collapsed;
    }

    static readonly HashSet<string> States = ["goal-completed", "goal-restarting", "ready", "connecting", "disconnected", "paused", "collecting", "heading-exit", "next-stage", "entering", "selecting-mode", "mode-unavailable", "recovering-life", "retrying", "waiting-result", "waiting-game", "completed", "defeated", "retry-unavailable", "heartbeat-expired", "runtime-error", "no-route", "avoiding-enemy"];
}



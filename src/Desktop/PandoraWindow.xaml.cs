using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;
public partial class PandoraWindow : Window
{
    readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    readonly bool smoke;
    PandoraClient? client;
    JsonObject? snapshot;
    bool polling, closing, closeReady;
    string status = "ready";
    int preferredStage, preferredGroup;
    string stageSignature = "";
    sealed record StageChoice(int Id, int Group, string Label);
    sealed record Preferences(string Mode = "Currency", int StageId = 0, int StageGroup = 0, bool AutoSelectEquipment = true);
    static string PreferencesPath => Path.Combine(DailyIdentity.DataRoot, "tools", "pandora-settings.json");
    public bool HostedAutomationEnabled => client?.Enabled == true;
    static DailyLanguage L => DailyLanguage.Current;

    public PandoraWindow(bool smoke = false)
    {
        this.smoke = smoke;
        InitializeComponent();
        DailyDialogs.Prepare(this);
        ShowInTaskbar = true;
        if (!smoke && DailyJson.TryRead<Preferences>(PreferencesPath)is { } preferences)
        {
            ChallengeMode.IsChecked = preferences.Mode == "Challenge";
            CurrencyMode.IsChecked = preferences.Mode != "Challenge";
            preferredStage = preferences.StageId;
            preferredGroup = preferences.StageGroup;
            AutoEquipment.IsChecked = preferences.AutoSelectEquipment;
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
            client = await Task.Run(() => new PandoraClient(DailyIdentity.DataRoot, int.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID") ?? "0"), long.Parse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START") ?? "0"), Environment.GetEnvironmentVariable("BD2_DAILY_TOOL_FINGERPRINT") ?? ""));
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
        client.Mode = ChallengeMode.IsChecked == true ? "Challenge" : "Currency";
        client.StageId = (StageBox.SelectedItem as StageChoice)?.Id ?? 0;
        client.StageGroup = (StageBox.SelectedItem as StageChoice)?.Group ?? 0;
        client.AutoSelectEquipment = AutoEquipment.IsChecked == true;
        try
        {
            DailyJson.Write(PreferencesPath, new Preferences(client.Mode, client.StageId, client.StageGroup, client.AutoSelectEquipment));
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
        try { DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "tools", "pandora-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    void LanguageChanged(object? sender, EventArgs e) { stageSignature = ""; Render(); }
    void ModeChanged(object sender, RoutedEventArgs e) { if (StagePanel != null) Render(); }
    void StageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StageBox.SelectedItem is StageChoice choice) { preferredStage = choice.Id; preferredGroup = choice.Group; }
        if (StatusText != null) Render();
    }
    internal void SetPreview(JsonObject value, bool challenge, bool autoEquipment = true)
    {
        snapshot = value; status = challenge && !autoEquipment ? "manual-equipment" : "playing";
        AutoEquipment.IsChecked = autoEquipment;
        ChallengeMode.IsChecked = challenge; CurrencyMode.IsChecked = !challenge;
        Render();
    }
    void Render()
    {
        bool active = HostedAutomationEnabled, challenge = ChallengeMode.IsChecked == true;
        StagePanel.Visibility = challenge ? Visibility.Collapsed : Visibility.Visible;
        ChallengeNotice.Visibility = challenge ? Visibility.Visible : Visibility.Collapsed;
        EquipmentPanel.Visibility = challenge ? Visibility.Visible : Visibility.Collapsed;
        var nativeStages = snapshot?["Stages"] as JsonArray;
        string signature = nativeStages?.ToJsonString() ?? "";
        if (stageSignature != signature)
        {
            stageSignature = signature;
            var choices = nativeStages?.Where(s => s?["Unlocked"]?.GetValue<bool>() == true && s?["Challenge"]?.GetValue<bool>() != true)
                .Select(s => new StageChoice(s!["Id"]!.GetValue<int>(), s["Group"]!.GetValue<int>(), (s["Name"]?.ToString() ?? "") + " · " + L.Get("pandora.difficulty." + s["Difficulty"]))).ToArray() ?? [];
            StageBox.ItemsSource = choices;
            StageBox.SelectedItem = choices.FirstOrDefault(s => s.Id == preferredStage && s.Group == preferredGroup) ?? choices.FirstOrDefault();
        }
        CurrencyMode.IsEnabled = ChallengeMode.IsEnabled = StageBox.IsEnabled = !active && !polling;
        AutoEquipment.IsEnabled = !active && !polling;
        ConnectButton.IsEnabled = !active && !polling;
        ConnectButton.Visibility = !smoke && status == "disconnected" ? Visibility.Visible : Visibility.Collapsed;
        bool hasChallenge = nativeStages?.Any(s => s?["Unlocked"]?.GetValue<bool>() == true && s?["Challenge"]?.GetValue<bool>() == true) == true;
        StartButton.IsEnabled = client != null && !active && !polling && (challenge ? hasChallenge : StageBox.SelectedItem != null);
        StopButton.IsEnabled = active;
        StageHint.Text = L.Get(StageBox.Items.Count == 0 ? "pandora.entry" : "pandora.repeat");
        StatusText.Text = L.Get("pandora.state." + (States.Contains(status) ? status : "ready"));
        RoundsText.Text = snapshot?["Completed"]?.ToString() ?? "0";
        double seconds = snapshot?["Seconds"]?.GetValue<double>() ?? 0;
        TimeText.Text = TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss");
        double hp = snapshot?["Hp"]?.GetValue<double>() ?? 0, max = snapshot?["MaxHp"]?.GetValue<double>() ?? 0;
        HealthText.Text = max > 0 ? (100 * hp / max).ToString("0") + "%" : "—";
    }
    static readonly HashSet<string> States = ["ready", "connecting", "disconnected", "paused", "entering", "selecting", "settling", "retrying", "upgrading", "manual-equipment", "playing", "game-paused", "stage-unavailable", "wrong-stage", "open-game", "entry-blocked", "confirm-manually", "upgrade-timeout", "transition-timeout", "runtime-error", "lease-expired"];
}


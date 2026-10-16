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
    bool polling, closing, closeReady, connecting, starting, rendering;
    string status = "ready";
    int preferredStage, preferredGroup;
    int preferredCharacterId, preferredCharacterGroup;
    string preferredCharacterName = "", characterSignature = "!";
    string stageSignature = "";
    sealed record StageChoice(int Id, int Group, string Label);
    internal sealed record CharacterOption(int Id, int Group, string Name, string Label, bool Available);
    internal sealed record Preferences(string Mode = "Currency", int StageId = 0, int StageGroup = 0, bool AutoSelectEquipment = true,
        int CharacterGroup = 0, int CharacterId = 0, string CharacterName = "");
    static string PreferencesPath => Path.Combine(DailyIdentity.DataRoot, "tools", "pandora-settings.json");
    public bool HostedAutomationEnabled => client?.Enabled == true || connecting || starting;
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
            preferredCharacterGroup = preferences.CharacterGroup;
            preferredCharacterId = preferences.CharacterId;
            preferredCharacterName = preferences.CharacterName;
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
        connecting = true;
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
            connecting = false;
            polling = false;
            Render();
        }
    }

    async void StartClicked(object sender, RoutedEventArgs e)
    {
        if (e.Handled || client == null || polling)
            return;
        polling = true;
        starting = true;
        client.Mode = ChallengeMode.IsChecked == true ? "Challenge" : "Currency";
        client.StageId = (StageBox.SelectedItem as StageChoice)?.Id ?? 0;
        client.StageGroup = (StageBox.SelectedItem as StageChoice)?.Group ?? 0;
        client.AutoSelectEquipment = AutoEquipment.IsChecked == true;
        client.CharacterGroup = preferredCharacterGroup;
        client.CharacterId = preferredCharacterId;
        try
        {
            DailyJson.Write(PreferencesPath, new Preferences(client.Mode, client.StageId, client.StageGroup, client.AutoSelectEquipment,
                client.CharacterGroup, client.CharacterId, preferredCharacterName));
            await Task.Run(client.Start);
            status = "entering";
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            starting = false;
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
        catch (DailyObservationPendingException)
        {
            // The guard has submitted no heartbeat/command. Keep the existing run
            // and retry observation on the next tick; the native lease still stops
            // inputs if the gap lasts. A real account/process change goes to Fail.
            status = "observation-wait";
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

    void LanguageChanged(object? sender, EventArgs e) { stageSignature = ""; characterSignature = "!"; Render(); }
    void ModeChanged(object sender, RoutedEventArgs e) { if (StagePanel != null) Render(); }
    void StageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StageBox.SelectedItem is StageChoice choice) { preferredStage = choice.Id; preferredGroup = choice.Group; }
        if (StatusText != null) Render();
    }
    void CharacterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (rendering) return;
        if (CharacterBox.SelectedItem is CharacterOption choice)
        {
            preferredCharacterId = choice.Id; preferredCharacterGroup = choice.Group; preferredCharacterName = choice.Name;
        }
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
        if (rendering) return;
        rendering = true;
        try
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
        var nativeCharacters = snapshot?["Characters"] as JsonArray;
        string charSignature = nativeCharacters?.ToJsonString() ?? "";
        if (characterSignature != charSignature)
        {
            characterSignature = charSignature;
            var choices = new List<CharacterOption> { new(0, 0, "", L.Get("pandora.character_default"), true) };
            foreach (var item in nativeCharacters ?? [])
            {
                int id = item?["Id"]?.GetValue<int>() ?? 0, group = item?["Group"]?.GetValue<int>() ?? 0;
                if (id <= 0 || group <= 0) continue;
                string name = item?["Name"]?.ToString() ?? "";
                bool available = item?["Unlocked"]?.GetValue<bool>() == true;
                choices.Add(new(id, group, name, available ? name : name + " · " + L.Get("pandora.character_locked"), available));
            }
            var wanted = choices.FirstOrDefault(c => c.Id == preferredCharacterId && c.Group == preferredCharacterGroup);
            if (wanted == null)
            {
                wanted = new(preferredCharacterId, preferredCharacterGroup, preferredCharacterName,
                    string.IsNullOrWhiteSpace(preferredCharacterName) ? L.Get("pandora.character_missing") : preferredCharacterName + " · " + L.Get("pandora.character_locked"), false);
                choices.Add(wanted);
            }
            CharacterBox.ItemsSource = choices;
            CharacterBox.SelectedItem = wanted;
        }
        CurrencyMode.IsEnabled = ChallengeMode.IsEnabled = StageBox.IsEnabled = CharacterBox.IsEnabled = !active && !polling;
        AutoEquipment.IsEnabled = !active && !polling;
        ConnectButton.IsEnabled = !active && !polling;
        ConnectButton.Visibility = !smoke && status == "disconnected" ? Visibility.Visible : Visibility.Collapsed;
        bool hasChallenge = nativeStages?.Any(s => s?["Unlocked"]?.GetValue<bool>() == true && s?["Challenge"]?.GetValue<bool>() == true) == true;
        bool characterAvailable = CharacterBox.SelectedItem is CharacterOption { Available: true };
        StartButton.IsEnabled = client != null && !active && !polling && characterAvailable && (challenge ? hasChallenge : StageBox.SelectedItem != null);
        StopButton.IsEnabled = active;
        StageHint.Text = L.Get(StageBox.Items.Count == 0 ? "pandora.entry" : "pandora.repeat");
        CharacterHint.Text = L.Get(nativeCharacters?.Count > 0 ? characterAvailable ? "pandora.character_hint" : "pandora.state.character-unavailable" : "pandora.character_loading");
        StatusText.Text = L.Get("pandora.state." + (States.Contains(status) ? status : "ready"));
        RoundsText.Text = snapshot?["Completed"]?.ToString() ?? "0";
        double seconds = snapshot?["Seconds"]?.GetValue<double>() ?? 0;
        TimeText.Text = TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss");
        double hp = snapshot?["Hp"]?.GetValue<double>() ?? 0, max = snapshot?["MaxHp"]?.GetValue<double>() ?? 0;
        HealthText.Text = max > 0 ? (100 * hp / max).ToString("0") + "%" : "—";
        }
        finally { rendering = false; }
    }
    static readonly HashSet<string> States = ["ready", "connecting", "disconnected", "observation-wait", "paused", "entering", "selecting", "settling", "retrying", "upgrading", "build-rerolling", "build-restarting", "build-restart-blocked", "manual-equipment", "playing", "corner-hold", "character-unavailable", "game-paused", "stage-unavailable", "wrong-stage", "open-game", "entry-blocked", "confirm-manually", "upgrade-timeout", "transition-timeout", "runtime-error", "lease-expired"];
}


using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

// Explicit owned surfaces, never guessed from localized captions or arbitrary windows.
internal static class ToolWindowDesign
{
    internal static string? Id(Window window) => window.GetType().FullName switch
    {
        "BD2Fishing.Desktop.FishingWindow" => "fishing",
        "BD2Territory.Desktop.TerritoryWindow" => "territory",
        "BD2Equipment.MainWindow" => "equipment",
        "BD2Sichuan.Desktop.SichuanWindow" => "sichuan",
        "BD2Rhythm.Desktop.RhythmWindow" => "rhythm",
        "Dustweave.Desktop.ApostleToolWindow" => "apostle-defense",
        "Dustweave.Desktop.GoldenPresetWindow" => "golden-presets",
        "Dustweave.Desktop.DailyBargainWindow" => "talent-practice",
        "BD2SecretVision.Desktop.MainWindow" => "secret-vision",
        "BD2FiendHunter.Desktop.MainWindow" => "fiend-hunter",
        "Dustweave.Desktop.MansionWindow" => "mansion-runaway",
        "Dustweave.Desktop.PandoraWindow" => "pandora",
        "BD2InfiniteGacha.Desktop.MainWindow" => "infinite-gacha",
        _ => null
    };

    // A disabled primary action shows the tool's current status line as its ToolTip, so the missing
    // step is one hover away. An empty status shows no ToolTip at all.
    internal static void ExplainDisabled(Button action, TextBlock status)
    {
        ToolTipService.SetShowOnDisabled(action, true);
        var text = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        void Apply(object? sender, EventArgs e) => action.ToolTip = action.IsEnabled || status.Text.Length == 0 ? null : status.Text;
        action.IsEnabledChanged += (_, _) => Apply(null, EventArgs.Empty);
        // Listen only while the status line is in a live tree, so a closed window is not kept alive;
        // shared chrome may re-parent the content after it was first loaded.
        void Listen(object? sender, RoutedEventArgs e) { text.RemoveValueChanged(status, Apply); text.AddValueChanged(status, Apply); Apply(null, EventArgs.Empty); }
        status.Loaded += Listen;
        status.Unloaded += (_, _) => text.RemoveValueChanged(status, Apply);
        Listen(null, new RoutedEventArgs());
    }

    internal static void ApplyActions(Window window)
    {
        var id = Id(window);
        if (id == null) return;
        window.Resources[typeof(Expander)] = Application.Current.FindResource("DisclosureExpander");
        window.Resources[typeof(Separator)] = Application.Current.FindResource("ToolSeparator");
        DailyLanguage.Current.Bind(window, Window.TitleProperty, () => DailyLanguage.Current.Get("tool." + id) + " · Dustweave");
        if (id is "apostle-defense" or "talent-practice" or "golden-presets") return; // These host windows own their action labels.
        void Label(string name, string key)
        {
            if (window.FindName(name) is not Button button) return;
            button.MinWidth = 88;
            DailyLanguage.Current.Bind(button, ContentControl.ContentProperty, key);
            DailyLanguage.Current.Bind(button, System.Windows.Automation.AutomationProperties.NameProperty, key);
        }
        Label("ConnectButton", id == "equipment" ? "tools.refresh_inventory" : "tools.action_connect");
        // Equipment's primary action intentionally changes from Calculate plan to Run.
        if (id != "equipment") Label("StartButton", "tools.action_start");
        // A disabled primary action keeps explaining itself: tools put the missing step in its ToolTip.
        foreach (string name in new[] { "StartButton", "ConnectButton", "AutoButton" })
            if (window.FindName(name) is Button action) ToolTipService.SetShowOnDisabled(action, true);
        // A tool marks ConnectButton.Tag = "next" while connecting is the user's next step
        // (not connected, connection lost). It then looks like the primary action; Start stays put.
        if (window.FindName("ConnectButton") is Button connect)
        {
            var tag = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(FrameworkElement.TagProperty, typeof(Button));
            void Emphasize(object? sender, EventArgs e)
                => connect.Style = (Style)Application.Current.FindResource(Equals(connect.Tag, "next") ? "PrimaryButton" : typeof(Button));
            tag.AddValueChanged(connect, Emphasize);
            window.Closed += (_, _) => tag.RemoveValueChanged(connect, Emphasize);
            Emphasize(connect, EventArgs.Empty);
        }
        Label("StopButton", "tools.action_stop");
        Label("PauseButton", "tools.action_stop");
        if (id == "sichuan") Label("AutoButton", "tools.action_round");
    }
}

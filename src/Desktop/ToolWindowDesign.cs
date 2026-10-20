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
        "BD2SecretVision.Desktop.MainWindow" => "secret-vision",
        "BD2FiendHunter.Desktop.MainWindow" => "fiend-hunter",
        "Dustweave.Desktop.MansionWindow" => "mansion-runaway",
        "Dustweave.Desktop.PandoraWindow" => "pandora",
        "BD2InfiniteGacha.Desktop.MainWindow" => "infinite-gacha",
        _ => null
    };

    internal static void ApplyActions(Window window)
    {
        var id = Id(window);
        if (id == null) return;
        window.Resources[typeof(Expander)] = Application.Current.FindResource("DisclosureExpander");
        window.Resources[typeof(Separator)] = Application.Current.FindResource("ToolSeparator");
        DailyLanguage.Current.Bind(window, Window.TitleProperty, () => DailyLanguage.Current.Get("tool." + id) + " · Dustweave");
        if (id == "apostle-defense") return; // Tower owns its changing connection/cancel action.
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
        Label("StopButton", "tools.action_stop");
        Label("PauseButton", "tools.action_stop");
        if (id == "sichuan") Label("AutoButton", "tools.action_round");
    }
}

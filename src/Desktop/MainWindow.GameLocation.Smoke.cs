using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckGameLocationForSmoke()
    {
        if (sessions is not DemoEnvironment) throw new InvalidOperationException("Game location UI checks require isolated sessions.");
        string directory = Path.Combine(root, "game-path-preview");
        string client = Path.Combine(root, "示例游戏 Game", "BrownDust II.exe");
        string managed = Path.Combine(Path.GetDirectoryName(client)!, "BrownDust II_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.WriteAllText(client, "fixture only");
        File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), "fixture only");
        File.WriteAllText(Path.Combine(managed, "mscorlib.dll"), "fixture only");
        var panel = preferencesPanel.GamePath;
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var previousTab = WorkspaceTabs.SelectedItem;
        double width = Width, height = Height; string locale = L.Code; int appearance = ThemeSelector.SelectedIndex;
        try
        {
            WorkspaceTabs.SelectedItem = SettingsTab;
            await panel.SelectAsync(client);
            Check(panel.DisplayedPath == client && new GameInstallation(directory).Resolve() == client, "Chooser did not save and display the selection");
            await panel.SelectAsync(Path.Combine(root, "BD2Starter.exe"));
            Check(new GameInstallation(directory).Resolve() == client, "Invalid choice replaced the previous path");
            await panel.RefreshAsync();
            var sandboxPanel = new GameInstallationPanel(new GameInstallation(directory), true);
            Check(!sandboxPanel.CanChoose, "Isolated window must use the host setting");
            SetBusy(true); Check(!panel.CanChoose, "Location can change during an active task"); SetBusy(false);
            foreach (var code in DailyLanguage.Codes)
            {
                L.Select(code);
                foreach (int mode in new[] { 1, 2 })
                {
                    Width = 960; Height = 700; ThemeSelector.SelectedIndex = mode;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    UpdateLayout();
                    Check(panel.ActualWidth > 0 && panel.ActualHeight > 0 && panel.CanChoose, "Location panel not usable");
                    Capture("game-location-" + code + "-" + (mode == 1 ? "light" : "dark"));
                }
            }
            File.Move(client, client + ".moved");
            await panel.RefreshAsync();
            Check(panel.DisplayedPath == client, "Missing path is hidden instead of remaining editable");
            Capture("game-location-missing");
            DailyJson.Write(Path.Combine(smoke!, "game-location.json"), new { status = "passed", checks, locales = DailyLanguage.Codes, realGameTouched = false });
        }
        finally
        {
            if (File.Exists(client + ".moved")) File.Move(client + ".moved", client);
            await panel.RefreshAsync(); L.Select(locale); ThemeSelector.SelectedIndex = appearance;
            Width = width; Height = height; WorkspaceTabs.SelectedItem = previousTab;
        }
    }
}

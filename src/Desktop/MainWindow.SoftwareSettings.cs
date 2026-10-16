using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private GameInstallationPanel softwareGamePath = null!;
    private SandboxInstallationPanel softwareSandboxPath = null!;
    private DailyWindowsStartup windowsStartup = null!;
    private bool softwareSettingsReady;
    private void InitializeSoftwareSettings()
    {
        softwareGamePath = new(new Accounts.GameInstallation(smoke == null ? null : Path.Combine(root, "game-path-preview")), IsSandboxWindow);
        softwareSandboxPath = new(new Accounts.SandboxInstallation(smoke == null ? null : Path.Combine(root, "sandbox-path-preview")), IsSandboxWindow);
        softwareSandboxPath.PreparingChanged += () => SetBusy(busy);
        SoftwarePaths.Children.Add(softwareGamePath); SoftwarePaths.Children.Add(softwareSandboxPath);
        string? isolatedStartup = null;
        windowsStartup = smoke == null ? new() : new(() => isolatedStartup, value => isolatedStartup = value);
        try
        {
            if (!IsSandboxWindow && smoke == null) windowsStartup.RefreshEnabledPath(Environment.ProcessPath!);
            StartWithWindows.IsChecked = windowsStartup.Enabled;
            CloseGameAfterRun.IsChecked = ReadDesktopBehavior()?.CloseGameAfterRun == true;
            var notices = ReadDesktopBehavior()?.CompletionNotice ?? new();
            CompletionWindows.IsChecked=notices.Windows;CompletionPopup.IsChecked=notices.Popup;CompletionSound.IsChecked=notices.Sound;
            RefreshCloseAction();
        }
        catch (Exception error) { SoftwareSettingsError(error); }
        softwareSettingsReady = true;
        UpdateSoftwareSettingsAvailability();
    }
    private void UpdateSoftwareSettingsAvailability()
    {
        if (!softwareSettingsReady) return;
        bool editable = !Unavailable && !IsSandboxWindow;
        softwareGamePath.IsEnabled = softwareSandboxPath.IsEnabled = StartWithWindows.IsEnabled = editable;
        CloseActionChoice.IsEnabled = RunOnOpen.IsEnabled = CloseGameAfterRun.IsEnabled = editable && desktopBehaviorValid;
        CompletionNoticeChoices.IsEnabled = !Unavailable && desktopBehaviorValid;
        ConnectionGuideButton.IsEnabled = !Unavailable && !IsSandboxWindow;
    }
    private void RefreshCloseAction()
    {
        changingDesktopBehavior = true;
        try { CloseActionChoice.SelectedIndex = ReadDesktopBehavior()?.CloseAction switch { "tray" => 1, "exit" => 2, _ => 0 }; }
        finally { changingDesktopBehavior = false; }
    }
    private void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        if (!softwareSettingsReady || IsSandboxWindow) return;
        try { windowsStartup.Set(StartWithWindows.IsChecked == true, Environment.ProcessPath!); ClearSoftwareSettingsError(); }
        catch (Exception error) { SoftwareSettingsError(error); }
        finally { try { StartWithWindows.IsChecked = windowsStartup.Enabled; } catch { StartWithWindows.IsChecked = false; } }
    }
    private void CloseAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!softwareSettingsReady || changingDesktopBehavior || IsSandboxWindow) return;
        try
        {
            (DailyDesktopBehavior.Read(root) with { CloseAction = CloseActionChoice.SelectedIndex switch { 1 => "tray", 2 => "exit", _ => "ask" } }).Save(root);
            ClearSoftwareSettingsError();
        }
        catch (Exception error) { RefreshCloseAction(); SoftwareSettingsError(error); }
    }
    private void CloseGameAfterRun_Click(object sender, RoutedEventArgs e)
    {
        if (!softwareSettingsReady || IsSandboxWindow) return;
        try
        {
            (DailyDesktopBehavior.Read(root) with { CloseGameAfterRun = CloseGameAfterRun.IsChecked == true }).Save(root);
            ClearSoftwareSettingsError();
        }
        catch (Exception error) { CloseGameAfterRun.IsChecked = ReadDesktopBehavior()?.CloseGameAfterRun == true; SoftwareSettingsError(error); }
    }
    private void CompletionNotice_Click(object sender, RoutedEventArgs e)
    {
        if (!softwareSettingsReady) return;
        try { (DailyDesktopBehavior.Read(root) with { CompletionNotice = new(CompletionWindows.IsChecked==true,CompletionPopup.IsChecked==true,CompletionSound.IsChecked==true) }).Save(root); ClearSoftwareSettingsError(); }
        catch(Exception error) { SoftwareSettingsError(error); }
    }
    private void SoftwareSettingsError(Exception error)
    {
        SoftwareSettingsStatus.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        L.Text(SoftwareSettingsStatus, error.Message.StartsWith("desktop.", StringComparison.Ordinal) || error.Message == "software.startup_path_invalid"
            ? error.Message : "software.save_failed");
        try { DailyJson.Write(Path.Combine(root, "software-settings-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); } catch { }
        SoftwareSettingsStatus.Visibility=Visibility.Visible;
    }
    private void ClearSoftwareSettingsError()
    {
        if (!desktopBehaviorValid) return;
        SoftwareSettingsStatus.Text = "";
        SoftwareSettingsStatus.Visibility = Visibility.Collapsed;
    }
    private void OpenAccountsSettings_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedItem = AccountsTab;
    private void OpenSoftwareSettings_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedItem = SoftwareSettingsTab;
}

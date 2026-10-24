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
    private bool sandboxModeReading=true,sandboxModeSaving;
    private bool? savedSandboxCompatible;
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
        ContentRendered+=async(_,_)=>await ReadSandboxModeAsync();
    }
    private void UpdateSoftwareSettingsAvailability()
    {
        if (!softwareSettingsReady) return;
        bool editable = !Unavailable && !IsSandboxWindow;
        softwareGamePath.IsEnabled = softwareSandboxPath.IsEnabled = StartWithWindows.IsEnabled = editable;
        SandboxCompatibility.IsEnabled=editable&&!sandboxModeReading&&!sandboxModeSaving;
        CloseActionChoice.IsEnabled = RunOnOpen.IsEnabled = CloseGameAfterRun.IsEnabled = editable && desktopBehaviorValid;
        CompletionNoticeChoices.IsEnabled = !Unavailable && desktopBehaviorValid;
        ConnectionGuideButton.IsEnabled = !Unavailable && !IsSandboxWindow;
    }
    private async Task ReadSandboxModeAsync()
    {
        if(IsSandboxWindow)return;
        sandboxModeReading=true;UpdateSoftwareSettingsAvailability();
        try {
            var preference=await Task.Run(()=>DailySandboxPreferences.Read(root,smoke==null?null:Path.Combine(root,"fixture-instances")));
            savedSandboxCompatible=preference.Mode==DailySandboxFileMode.Compatible;
            SandboxCompatibility.IsChecked=savedSandboxCompatible;
            SandboxModeStatus.Visibility=Visibility.Collapsed;
        } catch(Exception error){SandboxModeError(error);}
        finally{sandboxModeReading=false;UpdateSoftwareSettingsAvailability();}
    }
    private async void SandboxCompatibility_Click(object sender,RoutedEventArgs e)=>await SaveSandboxModeAsync();
    private async Task SaveSandboxModeAsync()
    {
        if(!softwareSettingsReady||IsSandboxWindow||Unavailable||sandboxModeReading||sandboxModeSaving)return;
        bool compatible=SandboxCompatibility.IsChecked==true;sandboxModeSaving=true;UpdateSoftwareSettingsAvailability();
        try {
            await Task.Run(()=>new DailySandboxPreferences(Mode:compatible?DailySandboxFileMode.Compatible:DailySandboxFileMode.Synchronized).Save(root));
            savedSandboxCompatible=compatible;SandboxModeStatus.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");
            L.Text(SandboxModeStatus,"sandbox.manage.mode_saved");SandboxModeStatus.Visibility=Visibility.Visible;
        }catch(Exception error){SandboxCompatibility.IsChecked=savedSandboxCompatible??false;SandboxModeError(error);}
        finally{sandboxModeSaving=false;UpdateSoftwareSettingsAvailability();}
    }
    private void SandboxModeError(Exception error)
    {
        SandboxModeStatus.SetResourceReference(TextBlock.ForegroundProperty,"Error");
        SandboxModeStatus.Text=DailyUserText.Error(error,L.Translate);SandboxModeStatus.Visibility=Visibility.Visible;
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

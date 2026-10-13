using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DesktopTray? tray;
    private bool exitChosen, closeDialogOpen, changingDesktopBehavior;
    private bool desktopBehaviorValid = true;
    private DailyDesktopBehavior? ReadDesktopBehavior()
    {
        try { var value = DailyDesktopBehavior.Read(root); desktopBehaviorValid = true; return value; }
        catch (Exception error) { desktopBehaviorValid = false; SoftwareSettingsError(error); return null; }
    }
    private void InitializeDesktopBehavior()
    {
        changingDesktopBehavior = true;
        try { RunOnOpen.IsChecked = ReadDesktopBehavior()?.RunOnOpen == true; }
        finally { changingDesktopBehavior = false; }
        RunOnOpen.IsEnabled = !IsSandboxWindow && desktopBehaviorValid;
        Application.Current.SessionEnding += DesktopSessionEnding;
        Closed += (_, _) => { tray?.Dispose(); tray = null; };
        Closed += (_, _) => Application.Current.SessionEnding -= DesktopSessionEnding;
    }
    private void DesktopSessionEnding(object sender, SessionEndingCancelEventArgs e) => exitChosen = true;
    private bool KeepWindowInTray()
    {
        if (smoke != null || exitChosen || updateInstalling || IsSandboxWindow) return false;
        if (closeDialogOpen) return true;
        var settings = ReadDesktopBehavior() ?? new();
        string? action = settings.CloseAction;
        if (action == "ask")
        {
            closeDialogOpen = true;
            try { action = ChooseCloseAction(); }
            finally { closeDialogOpen = false; }
        }
        if (action == null) return true;
        if (action == "exit") { exitChosen = true; return false; }
        tray ??= new(this, () => { exitChosen = true; Close(); });
        tray.Hide(); return true;
    }
    private string? ChooseCloseAction()
    {
        string? selected = null;
        var body = new StackPanel { Margin = new(24, 8, 24, 24) };
        var minimize = new RadioButton { IsChecked = true, Margin = new(0, 0, 0, 12) };
        var exit = new RadioButton { Margin = new(0, 0, 0, 18) };
        minimize.SetResourceReference(Control.ForegroundProperty, "Ink");
        exit.SetResourceReference(Control.ForegroundProperty, "Ink");
        var remember = new CheckBox { Margin = new(0, 0, 0, 20) };
        remember.IsEnabled = desktopBehaviorValid;
        if (!desktopBehaviorValid) L.Bind(remember, ToolTipProperty, "desktop.invalid");
        L.Bind(minimize, ContentControl.ContentProperty, "desktop.minimize");
        L.Bind(exit, ContentControl.ContentProperty, "desktop.exit");
        L.Bind(remember, ContentControl.ContentProperty, "desktop.remember_close");
        body.Children.Add(minimize); body.Children.Add(exit); body.Children.Add(remember);
        var confirm = new Button { HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        L.Bind(confirm, ContentControl.ContentProperty, "common.confirm"); body.Children.Add(confirm);
        var window = new Window { Owner = this, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
        L.Bind(window, TitleProperty, "desktop.close_title");
        confirm.Click += (_, _) => {
            try {
                var action = minimize.IsChecked == true ? "tray" : "exit";
                if (remember.IsChecked == true) { (DailyDesktopBehavior.Read(root) with { CloseAction = action }).Save(root); RefreshCloseAction(); }
                selected = action; window.DialogResult = true;
            } catch (Exception error) { ShowError(error); }
        };
        DailyDialogs.ShowModal(window); return selected;
    }
    private void RunOnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (changingDesktopBehavior) return;
        try {
            if (RunOnOpen.IsChecked == true && !rows.Any(r => r.Selected && r.Account.Valid)) throw new InvalidOperationException("desktop.select_accounts");
            (DailyDesktopBehavior.Read(root) with { RunOnOpen = RunOnOpen.IsChecked == true }).Save(root);
            ClearSoftwareSettingsError();
        } catch (Exception error) { RunOnOpen.IsChecked = ReadDesktopBehavior()?.RunOnOpen == true; SoftwareSettingsError(error); }
    }
    private async Task RunOnOpenAsync(bool guided)
    {
        if (ReadDesktopBehavior()?.ShouldRun(ScheduledStartup, UpdatedStartup, IsSandboxWindow, guided, Unavailable, DirectedStartup) != true) return;
        // Capture the saved checked identities once. No fallback to the current login.
        var targets = rows.Where(r => r.Selected).Select(r => r.Account).ToArray();
        if (targets.Length == 0 || targets.Any(a => !a.Valid)) throw new InvalidOperationException("desktop.select_accounts");
        if (!preferencesPanel.SavePending()) throw new InvalidOperationException("schedule.unsaved_settings");
        WorkspaceTabs.SelectedItem = RunTab;
        await ExecuteAccountQueue(targets);
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;
using Microsoft.Win32;

namespace Dustweave.Desktop;

internal enum SandboxSetupPurpose { Settings, Launch, Schedule }

// One editor is shared by installation settings and launch preflight. Downloading
// never counts as installation, and closing never authorizes a pending launch.
internal sealed class SandboxSetupWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly SandboxInstallation store;
    private readonly bool readOnly;
    private readonly SandboxSetupPurpose purpose;
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox path = new() { IsReadOnly = true, Margin = new(0, 6, 0, 0) };
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) };
    private readonly StackPanel installHelp = new() { Margin = new(0, 0, 0, 20) };
    private readonly Button browse = new() { Name = "SandboxChoose", Margin = new(0, 0, 8, 8) };
    private readonly Button automatic = new() { Name = "SandboxAutomatic", Margin = new(0, 0, 0, 8) };
    private readonly Button verify = new() { Name = "SandboxVerify", IsDefault = true };
    private bool working, closed, canEdit = true;
    internal Task<bool> Completion => completion.Task;
    internal bool Ready { get; private set; }
    internal bool DownloadVisible => installHelp.Visibility == Visibility.Visible;
    internal string DisplayedPath => path.Text;
    internal bool CanChoose => browse.IsEnabled;

    internal SandboxSetupWindow(Window owner, SandboxInstallation store, SandboxSetupPurpose purpose, bool readOnly = false)
    {
        this.store = store; this.purpose = purpose; this.readOnly = readOnly;
        Owner = owner; Width = 620; MinWidth = 440; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, purpose == SandboxSetupPurpose.Settings ? "sandbox_path.title" : "sandbox_setup.title");
        var body = new StackPanel { Margin = new(24, 8, 24, 24) };
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) };
        L.Text(help, readOnly ? "sandbox_path.inherited" : purpose switch {
            SandboxSetupPurpose.Launch => "sandbox_setup.launch_help",
            SandboxSetupPurpose.Schedule => "sandbox_setup.schedule_help",
            _ => "sandbox_path.help"
        }); body.Children.Add(help);
        var install = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        L.Text(install, "sandbox_setup.install_help"); installHelp.Children.Add(install);
        var download = new Button { Name = "SandboxDownload", HorizontalAlignment = HorizontalAlignment.Left };
        L.Bind(download, ContentControl.ContentProperty, "sandbox_setup.download");
        download.ToolTip = SandboxInstallation.DownloadUrl;
        download.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo(SandboxInstallation.DownloadUrl) { UseShellExecute = true }); }
            catch { L.Text(state, "sandbox_setup.browser_failed"); state.SetResourceReference(TextBlock.ForegroundProperty, "Error"); }
        };
        installHelp.Children.Add(download);
        var url = new TextBox { Text = SandboxInstallation.DownloadUrl, IsReadOnly = true, BorderThickness = new(0),
            Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), MinHeight = 0, Margin = new(0, 6, 0, 0) };
        url.SetResourceReference(ForegroundProperty, "MutedInk");
        L.Bind(url, System.Windows.Automation.AutomationProperties.NameProperty, "sandbox_setup.download");
        installHelp.Children.Add(url); body.Children.Add(installHelp);
        var pathTitle = new TextBlock { FontWeight = FontWeights.SemiBold };
        L.Text(pathTitle, "sandbox_path.title"); body.Children.Add(pathTitle); body.Children.Add(path);
        L.Bind(path, System.Windows.Automation.AutomationProperties.NameProperty, "sandbox_path.title");
        path.TextChanged += (_, _) => path.ToolTip = path.Text;
        var choices = new WrapPanel { Margin = new(0, 12, 0, 0) };
        L.Bind(browse, ContentControl.ContentProperty, "sandbox_setup.choose");
        L.Bind(automatic, ContentControl.ContentProperty, "sandbox_path.auto");
        choices.Children.Add(browse); choices.Children.Add(automatic); body.Children.Add(choices); body.Children.Add(state);
        browse.Click += async (_, _) => {
            if (working || readOnly || !canEdit) return;
            var picker = new OpenFolderDialog { Title = L.Get("sandbox_path.choose"), Multiselect = false };
            if (picker.ShowDialog(this) == true) await SelectAsync(picker.FolderName);
        };
        automatic.Click += async (_, _) => await ChangeAsync(store.UseAutomatic);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 22, 0, 0) };
        var cancel = new Button { Name = "SandboxCancel", Margin = new(0, 0, 10, 0) };
        L.Bind(cancel, ContentControl.ContentProperty, purpose == SandboxSetupPurpose.Launch ? "common.cancel" : "window.close");
        cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        verify.Style = (Style)Application.Current.FindResource("PrimaryButton");
        L.Bind(verify, ContentControl.ContentProperty, purpose == SandboxSetupPurpose.Launch ? "sandbox_setup.continue" : "sandbox_setup.recheck");
        verify.Click += async (_, _) => await VerifyAsync(); actions.Children.Add(verify); body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closed += (_, _) => { closed = true; completion.TrySetResult(Ready); };
        Loaded += async (_, _) => await RefreshAsync();
        SetWorking(false); DailyDialogs.Prepare(this);
    }

    internal async Task RefreshAsync()
    {
        if (working || closed) return;
        SetWorking(true);
        try { Present(await Task.Run(store.Read)); }
        catch (Exception error) { PresentError(error); }
        finally { SetWorking(false); }
    }
    internal Task SelectAsync(string directory) => ChangeAsync(() => store.Select(directory));
    internal Task DetectAsync() => ChangeAsync(store.UseAutomatic);
    private async Task ChangeAsync(Action save)
    {
        if (working || readOnly || !canEdit || closed) return;
        SetWorking(true);
        try { await Task.Run(save); Present(await Task.Run(store.Read)); }
        catch (Exception error) { PresentError(error); }
        finally { SetWorking(false); }
    }
    internal async Task VerifyAsync()
    {
        if (working || closed) return;
        SetWorking(true);
        try {
            var value = await Task.Run(store.Read); Present(value);
            if (!closed && value.Error == null) { Ready = true; Close(); }
        }
        catch (Exception error) { PresentError(error); }
        finally { SetWorking(false); }
    }
    private void Present(SandboxInstallationState value)
    {
        if (closed) return;
        path.Text = value.Directory;
        installHelp.Visibility = value.Error != null ? Visibility.Visible : Visibility.Collapsed;
        L.Bind(state, TextBlock.TextProperty, () => value.Error != null ? L.Describe(value.Error) : L.Get(value.IsManual ? "sandbox_path.manual" : "sandbox_path.detected"));
        state.SetResourceReference(TextBlock.ForegroundProperty, value.Error != null ? "Error" : "MutedInk");
        L.Bind(verify, ContentControl.ContentProperty, purpose == SandboxSetupPurpose.Launch ? "sandbox_setup.continue"
            : value.Error == null ? "sandbox_setup.done" : "sandbox_setup.recheck");
    }
    private void PresentError(Exception error)
    {
        if (closed) return;
        L.Bind(state, TextBlock.TextProperty, () => DailyUserText.Error(error, L.Translate));
        state.SetResourceReference(TextBlock.ForegroundProperty, "Error");
    }
    private void SetWorking(bool value)
    {
        working = value; browse.IsEnabled = automatic.IsEnabled = !value && !readOnly && canEdit; verify.IsEnabled = !value;
    }
    internal void SetCanEdit(bool value) { canEdit = value; SetWorking(working); }
}

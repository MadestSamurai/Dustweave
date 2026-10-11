using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;
using Microsoft.Win32;

namespace Dustweave.Desktop;

internal sealed class SandboxInstallationPanel : Border
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly SandboxInstallation store;
    private readonly bool readOnly;
    private readonly TextBox path = new() { IsReadOnly = true, Margin = new(0, 10, 0, 0) };
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 0) };
    private readonly Button browse = new() { Margin = new(0, 0, 8, 0) };
    private readonly Button automatic = new();
    private readonly Button edit = new();
    private readonly TextBlock summary = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    private Window? editor;
    private bool working;
    internal string DisplayedPath => path.Text;
    internal bool CanChoose => IsEnabled && !working && !readOnly;

    internal SandboxInstallationPanel(SandboxInstallation store, bool readOnly)
    {
        this.store = store; this.readOnly = readOnly;
        Padding = new(0, 0, 0, 12); Margin = new(0, 0, 0, 12);
        BorderThickness = new(0, 0, 0, 1); SetResourceReference(BorderBrushProperty, "Line");
        var header = new DockPanel();
        L.Bind(edit, ContentControl.ContentProperty, () => L.Translate("更改位置"));
        edit.Click += (_, _) => OpenEditor(); DockPanel.SetDock(edit, Dock.Right); header.Children.Add(edit);
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        L.Text(title, "sandbox_path.title"); DockPanel.SetDock(title, Dock.Left); header.Children.Add(title);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); header.Children.Add(summary); Child = header;
        L.Bind(browse, ContentControl.ContentProperty, "sandbox_path.choose");
        L.Bind(automatic, ContentControl.ContentProperty, "sandbox_path.auto");
        L.Bind(path, System.Windows.Automation.AutomationProperties.NameProperty, "sandbox_path.title");
        path.TextChanged += (_, _) => path.ToolTip = path.Text;
        browse.Click += async (_, _) =>
        {
            if (working || readOnly) return;
            var picker = new OpenFolderDialog { Multiselect = false, Title = L.Get("sandbox_path.choose") };
            if (picker.ShowDialog(editor ?? Window.GetWindow(this)) == true) await SelectAsync(picker.FolderName);
        };
        automatic.Click += async (_, _) => await ChangeAsync(store.UseAutomatic);
        Loaded += async (_, _) => await RefreshAsync();
        SetWorking(false);
    }

    internal Window CreateEditor()
    {
        var body = new StackPanel { Margin = new(24) };
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
        L.Text(help, readOnly ? "sandbox_path.inherited" : "sandbox_path.help"); body.Children.Add(help);
        body.Children.Add(path); body.Children.Add(state);
        var actions = new WrapPanel { Margin = new(0, 16, 0, 0) };
        actions.Children.Add(browse); actions.Children.Add(automatic); body.Children.Add(actions);
        var window = new Window { Title = L.Get("sandbox_path.title"), Width = 640, MinWidth = 440, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40), Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.Closed += (_, _) => { body.Children.Remove(path); body.Children.Remove(state); actions.Children.Clear(); editor = null; };
        DailyDialogs.Prepare(window); return window;
    }
    private void OpenEditor()
    {
        if (working || !IsEnabled) return;
        if (editor != null) { editor.Activate(); return; }
        editor = CreateEditor(); DailyDialogs.ShowModal(editor);
    }
    internal async Task RefreshAsync()
    {
        var value = await Task.Run(store.Read);
        Present(value);
    }
    internal Task SelectAsync(string value) => ChangeAsync(() => store.Select(value));
    private async Task ChangeAsync(Action save)
    {
        if (working || readOnly || !IsEnabled) return;
        SetWorking(true);
        try { await Task.Run(save); Present(await Task.Run(store.Read)); }
        catch (Exception error) { L.Bind(state, TextBlock.TextProperty, () => DailyUserText.Error(error, L.Translate)); state.SetResourceReference(TextBlock.ForegroundProperty, "Error"); }
        finally { SetWorking(false); }
    }
    private void Present(SandboxInstallationState value)
    {
        path.Text = value.Directory;
        L.Bind(summary, TextBlock.TextProperty, () => value.Directory.Length > 0 ? value.Directory : L.Get("sandbox_path.choose"));
        summary.ToolTip = value.Directory;
        summary.SetResourceReference(TextBlock.ForegroundProperty, value.Error != null ? "Error" : "MutedInk");
        L.Bind(state, TextBlock.TextProperty, () => value.Error != null ? L.Describe(value.Error) : L.Get(value.IsManual ? "sandbox_path.manual" : "sandbox_path.detected"));
        state.SetResourceReference(TextBlock.ForegroundProperty, value.Error != null ? "Error" : "MutedInk");
    }
    private void SetWorking(bool value)
    {
        working = value; browse.IsEnabled = automatic.IsEnabled = !value && !readOnly; edit.IsEnabled = !value;
    }
}

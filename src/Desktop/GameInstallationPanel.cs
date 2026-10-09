using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;
using Microsoft.Win32;

namespace Dustweave.Desktop;

internal sealed class GameInstallationPanel : Border
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly GameInstallation store;
    private readonly bool readOnly;
    private readonly TextBox path = new() { IsReadOnly = true, Margin = new(0, 10, 0, 0) };
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 0) };
    private readonly Button browse = new() { Margin = new(0, 0, 8, 0) };
    private readonly Button automatic = new();
    private bool working;
    internal string DisplayedPath => path.Text;
    internal bool CanChoose => browse.IsEnabled;

    internal GameInstallationPanel(GameInstallation store, bool readOnly)
    {
        this.store = store; this.readOnly = readOnly;
        Style = (Style)Application.Current.FindResource("Panel"); Padding = new(16); Margin = new(0, 0, 0, 16);
        var body = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        L.Bind(browse, ContentControl.ContentProperty, "game_path.choose");
        L.Bind(automatic, ContentControl.ContentProperty, "game_path.auto");
        actions.Children.Add(browse); actions.Children.Add(automatic);
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
        L.Text(title, "game_path.title"); header.Children.Add(title); body.Children.Add(header);
        L.Bind(path, System.Windows.Automation.AutomationProperties.NameProperty, "game_path.title");
        path.TextChanged += (_, _) => path.ToolTip = path.Text;
        body.Children.Add(path); body.Children.Add(state);
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0), FontSize = 12 };
        help.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Text(help, readOnly ? "game_path.inherited" : "game_path.help"); body.Children.Add(help); Child = body;
        browse.Click += async (_, _) =>
        {
            if (working || readOnly) return;
            var picker = new OpenFileDialog { Filter = "BrownDust II.exe|BrownDust II.exe", CheckFileExists = true, Multiselect = false, Title = L.Get("game_path.choose") };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) await SelectAsync(picker.FileName);
        };
        automatic.Click += async (_, _) => await ChangeAsync(store.UseAutomatic);
        Loaded += async (_, _) => await RefreshAsync();
        SetWorking(false);
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
    private void Present(GameInstallationState value)
    {
        path.Text = value.Executable;
        L.Bind(state, TextBlock.TextProperty, () => value.Error != null ? L.Describe(value.Error) : L.Get(value.IsManual ? "game_path.manual" : "game_path.detected"));
        state.SetResourceReference(TextBlock.ForegroundProperty, value.Error != null ? "Error" : "MutedInk");
    }
    private void SetWorking(bool value)
    {
        working = value; browse.IsEnabled = automatic.IsEnabled = !value && !readOnly;
    }
}

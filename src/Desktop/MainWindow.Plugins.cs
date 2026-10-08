using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Dustweave.Desktop;

public sealed class DailyPluginPanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly StackPanel cards = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button import = ActionButton("plugins.import");
    private readonly Button restart = ActionButton("plugins.restart");
    private readonly Button disable = ActionButton("plugins.disable");
    private bool occupied;
    public event Action<string?>? ImportRequested;
    public event Action<string>? EnableRequested;
    public event Action<string>? RemoveRequested;
    public event Action? DisableRequested;
    public event Action? RestartRequested;
    public DailyPluginPanel()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var content = new StackPanel { Margin = new(0, 0, 8, 0) };
        var overview = new StackPanel();
        var title = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold }; L.Text(title, "plugins.title"); overview.Children.Add(title);
        overview.Children.Add(Text("plugins.help"));
        var actions = new WrapPanel { Margin = new(0, 16, 0, 0) };
        import.Style = (Style)Application.Current.FindResource("PrimaryButton");
        actions.Children.Add(import); actions.Children.Add(disable); actions.Children.Add(restart); overview.Children.Add(actions); overview.Children.Add(status);
        content.Children.Add(Card(overview)); content.Children.Add(cards); Content = content;
        import.Click += (_, _) => ImportRequested?.Invoke(null); disable.Click += (_, _) => DisableRequested?.Invoke(); restart.Click += (_, _) => RestartRequested?.Invoke();
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = !occupied && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (!occupied && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) ImportRequested?.Invoke(paths[0]); e.Handled = true; };
    }
    public void Status(string key, params object?[] args) => L.Text(status, key, args);
    public void Busy(bool value) { occupied = value; import.IsEnabled = disable.IsEnabled = restart.IsEnabled = cards.IsEnabled = !value; }
    public void Refresh(DailyPluginSelection state, Dictionary<string, DailyPluginInfo> inspected, DailyPluginInfo current)
    {
        cards.Children.Clear();
        bool external = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN") != null;
        string selected = state.OverrideLocal ? state.Active ?? "" : current.Fingerprint;
        bool pending = selected != current.Fingerprint;
        bool compatible = selected.Length == 0 || !state.OverrideLocal || inspected.GetValueOrDefault(selected)?.Available == true;
        restart.Visibility = pending && !external && compatible ? Visibility.Visible : Visibility.Collapsed;
        disable.Visibility = state.Active != null || current.Available ? Visibility.Visible : Visibility.Collapsed;
        Status(external ? "plugins.environment_override" : !compatible ? "plugins.unavailable" : pending ? "plugins.pending" : "plugins.stable");
        foreach (var record in state.Installed.OrderByDescending(x => x.InstalledUtc))
        {
            var info = inspected[record.Fingerprint];
            var body = new StackPanel();
            var heading = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            L.Bind(heading, TextBlock.TextProperty, () => (info.Available ? info.Name(L.Code) : record.Id) + "  " + record.Version); body.Children.Add(heading);
            string stateKey = !info.Available ? "plugins.unavailable" : record.Fingerprint == selected ? (pending ? "plugins.next_start" : "plugins.active") : record.Fingerprint == current.Fingerprint ? "plugins.current_until_restart" : "plugins.installed";
            body.Children.Add(Text(stateKey));
            if (!info.Available) body.Children.Add(Text("plugins." + info.State));
            else body.Children.Add(Text("plugins.details", info.Publisher.Length == 0 ? record.Id : info.Publisher, info.MinHostVersion.Length == 0 ? "—" : info.MinHostVersion, info.MaxHostVersion.Length == 0 ? "—" : info.MaxHostVersion));
            var actions = new WrapPanel { Margin = new(0, 12, 0, 0) };
            if (info.Available && record.Fingerprint != selected)
            {
                var enable = ActionButton(record.Fingerprint == state.Previous ? "plugins.rollback" : "plugins.enable");
                enable.Click += (_, _) => EnableRequested?.Invoke(record.Fingerprint); actions.Children.Add(enable);
            }
            var remove = ActionButton("plugins.remove"); remove.Click += (_, _) => RemoveRequested?.Invoke(record.Fingerprint); actions.Children.Add(remove);
            body.Children.Add(actions); cards.Children.Add(Card(body));
        }
        if (current.Available && !state.Installed.Any(x => x.Fingerprint == current.Fingerprint))
        {
            var text = Text("plugins.local_active", current.Name(L.Code), current.Version); cards.Children.Add(Card(text));
        }
        if (state.Installed.Length == 0 && !current.Available) cards.Children.Add(Card(Text("plugins.empty")));
        Busy(occupied);
    }
    private static Button ActionButton(string key) { var button = new Button { Margin = new(0, 0, 10, 6) }; L.Bind(button, ContentControl.ContentProperty, key); return button; }
    private static TextBlock Text(string key, params object?[] args) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; text.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); L.Text(text, key, args); return text; }
    private static Border Card(UIElement body) => new() { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(20), Margin = new(0, 0, 0, 16), Child = body };
}

public partial class MainWindow
{
    private readonly DailyPluginPanel pluginPanel = new();
    private bool pluginChanging;
    private DailyPluginStore PluginStore => new(root);
    private void InitializePlugins()
    {
        PluginsTab.Content = pluginPanel;
        pluginPanel.ImportRequested += async path => await ChangePluginAsync(async () =>
        {
            if (path == null)
            {
                var picker = new OpenFileDialog { Filter = "Dustweave (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
                if (picker.ShowDialog(this) != true) return; path = picker.FileName;
            }
            pluginPanel.Status("plugins.checking");
            using var package = await Task.Run(() => PluginStore.Prepare(path));
            if (!PluginConfirm("plugins.import", L.Get("plugins.trust", package.Info.Name(L.Code), package.Info.Version, package.Info.Publisher.Length == 0 ? "—" : package.Info.Publisher))) return;
            var installed = await Task.Run(() => PluginStore.Install(package));
            await PluginStore.ActivateAsync(installed.Fingerprint, ProbePluginAsync);
        });
        pluginPanel.EnableRequested += async fingerprint => await ChangePluginAsync(async () =>
        {
            if (!PluginConfirm("plugins.enable", L.Get("plugins.activate_confirm"))) return;
            await PluginStore.ActivateAsync(fingerprint, ProbePluginAsync);
        });
        pluginPanel.DisableRequested += async () => await ChangePluginAsync(() => { PluginStore.Disable(); return Task.CompletedTask; });
        pluginPanel.RemoveRequested += async fingerprint => await ChangePluginAsync(() =>
        {
            if (PluginConfirm("plugins.remove", L.Get("plugins.remove_confirm"))) PluginStore.Remove(fingerprint);
            return Task.CompletedTask;
        });
        pluginPanel.RestartRequested += async () => await RestartForPluginsAsync();
        _ = RefreshPluginsAsync();
    }
    private async Task RefreshPluginsAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => { var store = PluginStore; var state = store.Read(); return (state, inspected: state.Installed.ToDictionary(x => x.Fingerprint, x => store.Inspect(x))); });
            pluginPanel.Refresh(snapshot.state, snapshot.inspected, DailyPlugin.Current);
        }
        catch (Exception e) { PluginError(e); }
        pluginPanel.Busy(Unavailable || IsSandboxWindow);
    }
    private async Task ChangePluginAsync(Func<Task> change)
    {
        if (smoke != null || Unavailable || IsSandboxWindow) return;
        pluginChanging = true; SetBusy(busy);
        try { await change(); await RefreshPluginsAsync(); }
        catch (Exception error) { await RefreshPluginsAsync(); PluginError(error); }
        finally { pluginChanging = false; SetBusy(busy); }
    }
    private void PluginError(Exception error)
    {
        string key = error.Message.StartsWith("plugins.", StringComparison.Ordinal) ? error.Message : "plugins.failed";
        pluginPanel.Status(key);
        try { DailyJson.Write(Path.Combine(root, "extensions", "last-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Keep the on-screen error usable even if diagnostics are not writable. */ }
    }
    private async Task ProbePluginAsync(DailyPluginInfo info, CancellationToken token)
    {
        pluginPanel.Status("plugins.probing");
        await DailyPluginProbe.RunAsync(Environment.ProcessPath!, root, info, token);
    }
    private async Task RestartForPluginsAsync()
    {
        if (smoke != null || Unavailable || IsSandboxWindow || !preferencesPanel.SavePending()) return;
        if (!PluginConfirm("plugins.restart", L.Get("plugins.restart_confirm"))) return;
        pluginChanging = true; SetBusy(busy);
        try
        {
            if (DailySandbox.HasIsolatedWindows()) throw new IOException("plugins.isolated_open");
            if (ToolOpen && !await toolSession.CloseAsync()) throw new IOException("plugins.tool_open");
            using var process = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.GetCurrentDirectory() };
            start.ArgumentList.Add("--restart-after"); start.ArgumentList.Add(process.Id.ToString()); start.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString());
            start.Environment.Remove("DUSTWEAVE_PLUGIN");
            _ = Process.Start(start) ?? throw new IOException("plugins.failed");
            finalClose = true; timer.Stop(); Close();
        }
        catch (Exception error) { PluginError(error); }
        finally { pluginChanging = false; SetBusy(busy); }
    }
    private bool PluginConfirm(string key, string message) => DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get(key), message, true)) == true;
    private async Task SmokePluginsAsync()
    {
        WorkspaceTabs.SelectedItem = PluginsTab;
        string? prior = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN");
        string language = L.Code;
        try
        {
            Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", null);
            var first = new DailyInstalledPlugin("example.extension", "1.1.0", new string('a', 64), DateTimeOffset.UtcNow);
            var old = first with { Version = "1.0.0", Fingerprint = new string('b', 64), InstalledUtc = DateTimeOffset.UtcNow.AddDays(-1) };
            var info = new DailyPluginInfo(true, "ready", "", first.Version, first.Fingerprint, [], Capabilities: ["sample"])
            { Id = first.Id, Publisher = "Example", MinHostVersion = "0.9.12", MaxHostVersion = "0.9.99", Names = new() { ["zh-CN"] = "示例扩展", ["zh-TW"] = "範例擴充", ["en-US"] = "Example extension" } };
            var state = new DailyPluginSelection(1, first.Fingerprint, old.Fingerprint, [first, old]);
            pluginPanel.Refresh(state, new() { [first.Fingerprint] = info, [old.Fingerprint] = info with { Version = old.Version, Fingerprint = old.Fingerprint } }, info);
            foreach (var code in DailyLanguage.Codes)
            {
                L.Select(code);
                foreach (var appearance in new[] { 1, 2 })
                {
                    ThemeSelector.SelectedIndex = appearance; Width = 920; Height = 650;
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Capture("plugins-" + code + "-" + appearance);
                }
            }
            pluginPanel.Busy(true);
            // Disabled page actions must not be re-enabled by a theme or language refresh.
            pluginPanel.Refresh(state, new() { [first.Fingerprint] = info, [old.Fingerprint] = info with { Version = old.Version, Fingerprint = old.Fingerprint } }, info);
            pluginPanel.Busy(false);
        }
        finally { Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", prior); L.Select(language); await RefreshPluginsAsync(); }
    }
}

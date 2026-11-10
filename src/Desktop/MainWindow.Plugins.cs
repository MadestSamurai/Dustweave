using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Dustweave.Desktop;

public sealed partial class DailyPluginPanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly StackPanel cards = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button import = ActionButton("plugins.import");
    private readonly Button restart = ActionButton("plugins.restart");
    private readonly Button disable = ActionButton("plugins.disable_daily");
    private Border? emptyCard;
    private bool occupied;
    private Func<string, bool> isNew = _ => false;
    public event Action<string, string, string, bool>? DetailsRequested;
    public event Action? DiscoveryChanged;
    public bool HasNewFeatures => (marketSnapshot?.Catalog.Plugins.Select(p => p.Id) ?? [])
        .Concat(marketSelection.Installed.Select(p => p.Id)).Any(id => isNew(DailyFeatureDiscovery.Plugin(id)));
    public void SetDiscovery(Func<string, bool> value) { isNew = value; RefreshDiscovery(); }
    private Border FeatureCard(string id, StackPanel body, Func<string> name, Func<string> description, bool tactics)
    {
        var badge = FeatureDiscoveryUi.Badge(); badge.Margin = new(0);
        var details = ActionButton("discovery.view"); details.Name = "FeatureDetailsButton";
        details.HorizontalAlignment = HorizontalAlignment.Right; details.Margin = new(12, 0, 0, 0);
        details.Click += (_, _) => DetailsRequested?.Invoke(id, name(), description(), tactics);
        var heading = new DockPanel { Name = "FeatureNewBadge", Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(details, Dock.Right); heading.Children.Add(details);
        badge.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(badge); body.Children.Insert(0, heading);
        var card = Card(body); card.Tag = DailyFeatureDiscovery.Plugin(id); return card;
    }
    public void RefreshDiscovery()
    {
        foreach (var list in new[] { cards, marketCards })
        {
            foreach (var card in list.Children.OfType<Border>())
                if (card.Tag is string key && card.Child is StackPanel body)
                    foreach (var marker in body.Children.OfType<FrameworkElement>().Where(c => c.Name is "FeatureNewBadge" or "FeatureDetailsButton"))
                        marker.Visibility = isNew(key) ? Visibility.Visible : Visibility.Collapsed;
            var ordered = list.Children.Cast<UIElement>().OrderByDescending(c => c is Border { Tag: string key } && isNew(key)).ToArray();
            for (int i = 0; i < ordered.Length; i++)
                if (!ReferenceEquals(list.Children[i], ordered[i])) { list.Children.Remove(ordered[i]); list.Children.Insert(i, ordered[i]); }
        }
        DiscoveryChanged?.Invoke();
    }
    public event Action<string?>? ImportRequested;
    public event Action<string>? EnableRequested;
    public event Action<string>? RemoveRequested;
    public event Action? DisableRequested;
    public event Action<string>? DisableOneRequested;
    public event Action? RestartRequested;
    public event Action<string, string>? SharingRequested;
    public event Action<string, string, string>? ToolActionRequested;
    public void AppendTools(IReadOnlyList<(DailyPluginSelection Selection, DailyInstalledPlugin Record, DailyPluginInfo Info)> tools)
    {
        var loadedTools=DailyPlugin.Current.AllSelections.Where(p=>p.IsTool&&p.HookSources.Length>0).Select(p=>p.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var selectedTools=tools.Where(t=>t.Info.Available&&t.Info.HookSources.Length>0&&t.Selection.Active==t.Record.Fingerprint).Select(t=>t.Info.Fingerprint).ToHashSet(StringComparer.Ordinal);
        if(!loadedTools.SetEquals(selectedTools)){restart.Visibility=Visibility.Visible;Status("plugins.pending");}
        if(tools.Count>0 && emptyCard!=null)cards.Children.Remove(emptyCard);
        marketSelection=marketSelection with {Installed=[..marketSelection.Installed,..tools.Select(t=>t.Record)]};
        RenderMarket();
        foreach (var (state, record, info) in tools)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = (info.Available ? info.Name(L.Code) : record.Id) + "  " + record.Version, FontSize = 18, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(Text(info.Available ? "plugins.tool_where" : "plugins." + info.State));
            if(info.HookSources.Length>0&&state.Active==record.Fingerprint&&!loadedTools.Contains(record.Fingerprint))body.Children.Add(Text("plugins.next_start"));
            var actions = new WrapPanel { Margin = new(0,12,0,0) };
            void Add(string key, string operation) { var button = ActionButton(key); button.Click += (_,_) => ToolActionRequested?.Invoke(record.Id, record.Fingerprint, operation); actions.Children.Add(button); }
            if(info.Available) { if(state.Active == record.Fingerprint) { Add("tools.open", "open"); Add("plugins.disable", "disable"); } else Add("plugins.enable", "enable"); }
            Add("plugins.remove", "remove"); body.Children.Add(actions); cards.Children.Add(FeatureCard(record.Id, body,
                () => info.Available ? info.Name(L.Code) : record.Id, () => L.Get("plugins.tool_where"), false));
        }
        RefreshDiscovery();
    }
    public DailyPluginPanel()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var content = new StackPanel { Margin = new(0, 0, 8, 0) };
        var overview = new StackPanel();
        overview.Children.Add(Text("plugins.help"));
        var actions = new WrapPanel { Margin = new(0, 16, 0, 0) };
        import.Style = (Style)Application.Current.FindResource("PrimaryButton");
        actions.Children.Add(import); actions.Children.Add(disable); actions.Children.Add(restart); overview.Children.Add(actions); overview.Children.Add(status);
        overview.Children.Remove(status);
        InitializeMarket(content, overview, cards, status);
        var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition{MaxWidth=880});layout.Children.Add(content);Content=layout;
        import.Click += (_, _) => ImportRequested?.Invoke(null); disable.Click += (_, _) => DisableRequested?.Invoke(); restart.Click += (_, _) => RestartRequested?.Invoke();
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = !occupied && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (!occupied && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) ImportRequested?.Invoke(paths[0]); e.Handled = true; };
    }
    public void Status(string key, params object?[] args) => L.Text(status, key, args);
    public void Busy(bool value) { occupied = value; import.IsEnabled = disable.IsEnabled = restart.IsEnabled = cards.IsEnabled = !value; MarketBusy(value); }
    public void Refresh(DailyPluginSelection state, Dictionary<string, DailyPluginInfo> inspected, DailyPluginInfo current)
    {
        marketSelection = state;
        cards.Children.Clear();
        bool external = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN") != null;
        var selected = (state.OverrideLocal ? state.ActiveFingerprints : current.Selections.Select(p=>p.Fingerprint)).ToHashSet(StringComparer.Ordinal);
        var running = current.Selections.Select(p=>p.Fingerprint).ToHashSet(StringComparer.Ordinal);
        bool pending = !selected.SetEquals(running);
        bool compatible = !state.OverrideLocal || selected.All(h=>inspected.GetValueOrDefault(h)?.Available==true);
        restart.Visibility = pending && !external && compatible ? Visibility.Visible : Visibility.Collapsed;
        disable.Visibility = state.Active != null || current.Available ? Visibility.Visible : Visibility.Collapsed;
        Status(external ? "plugins.environment_override" : !compatible ? "plugins.unavailable" : pending ? "plugins.pending" : "plugins.stable");
        foreach (var record in state.Installed.OrderByDescending(x => x.InstalledUtc))
        {
            var info = inspected[record.Fingerprint];
            var body = new StackPanel();
            var heading = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            L.Bind(heading, TextBlock.TextProperty, () => (info.Available ? info.Name(L.Code) : record.Id) + "  " + record.Version); body.Children.Add(heading);
            string stateKey = !info.Available ? "plugins.unavailable" : selected.Contains(record.Fingerprint) ? (running.Contains(record.Fingerprint) ? "plugins.active" : "plugins.next_start") : running.Contains(record.Fingerprint) ? "plugins.current_until_restart" : "plugins.installed";
            body.Children.Add(Text(stateKey));
            if (!info.Available) body.Children.Add(Text("plugins." + info.State));
            else body.Children.Add(Text("plugins.details", info.Publisher.Length == 0 ? record.Id : info.Publisher, info.MinHostVersion.Length == 0 ? "—" : info.MinHostVersion, info.MaxHostVersion.Length == 0 ? "—" : info.MaxHostVersion));
            if (info.Available && info.Supports("tactics")) body.Children.Add(TacticsGuide());
            var actions = new WrapPanel { Margin = new(0, 12, 0, 0) };
            if (info.Available && info.SupportsSolutionSharing)
            {
                var sharing = ActionButton("plugins.sharing_settings");
                sharing.Click += (_, _) => SharingRequested?.Invoke(info.Id, info.Name(L.Code)); actions.Children.Add(sharing);
            }
            if(selected.Contains(record.Fingerprint)) { var off=ActionButton("plugins.disable");off.Click+=(_,_)=>DisableOneRequested?.Invoke(record.Fingerprint);actions.Children.Add(off); }
            if (info.Available && !selected.Contains(record.Fingerprint))
            {
                var enable = ActionButton(record.Fingerprint == state.Previous ? "plugins.rollback" : "plugins.enable");
                enable.Click += (_, _) => EnableRequested?.Invoke(record.Fingerprint); actions.Children.Add(enable);
            }
            var remove = ActionButton("plugins.remove"); remove.Click += (_, _) => RemoveRequested?.Invoke(record.Fingerprint); actions.Children.Add(remove);
            body.Children.Add(actions); cards.Children.Add(FeatureCard(record.Id, body,
                () => info.Available ? info.Name(L.Code) : record.Id, () => L.Get(stateKey), info.Available && info.Supports("tactics")));
        }
        if (current.Available && !state.Installed.Any(x => x.Fingerprint == current.Fingerprint))
        {
            var text = Text("plugins.local_active", current.Name(L.Code), current.Version); cards.Children.Add(Card(text));
        }
        emptyCard=null;
        if (state.Installed.Length == 0 && !current.Available) { emptyCard=Card(Text("plugins.empty"));cards.Children.Add(emptyCard); }
        Busy(occupied);
        RenderMarket();
    }
    private static Button ActionButton(string key) { var button = new Button { Margin = new(0, 0, 10, 6) }; L.Bind(button, ContentControl.ContentProperty, key); return button; }
    internal static FrameworkElement TacticsGuide()
    {
        var guide = new StackPanel { Name = "TacticsUsageGuide", Margin = new(0, 14, 0, 4) };
        var heading = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        L.Text(heading, "plugins.tactics_where"); guide.Children.Add(heading);
        guide.Children.Add(Text("plugins.tactics_howto"));
        return guide;
    }
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
            var choice = ConfirmPluginInstall(package.Info.Name(L.Code), package.Info.Version, package.Info.Publisher, package.Info.Id, package.Info.SupportsSolutionSharing, package.Info.Supports("tactics"), package.Info.IsTool);
            if (!choice.Confirmed) return;
            var store = StoreFor(package.Info);
            var installed = await Task.Run(() => store.Install(package));
            await store.ActivateAsync(installed.Fingerprint, ProbePluginAsync);
            if (choice.Share is { } share) DailyPluginSharing.Write(root, package.Info.Id, share);
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
        pluginPanel.SharingRequested += (id, name) =>
        {
            if (smoke != null || IsSandboxWindow) return;
            var choice = new PluginSharingWindow(this, name, DailyPluginSharing.Enabled(root, id), false);
            if (DailyDialogs.ShowModal(choice) != true) return;
            try { DailyPluginSharing.Write(root, id, choice.SharingEnabled); pluginPanel.Status("plugins.sharing_saved"); }
            catch (Exception error) { PluginError(error); }
        };
        InitializePluginMarket();
        pluginPanel.DisableOneRequested += async hash => await ChangePluginAsync(()=>{PluginStore.Disable(hash);return Task.CompletedTask;});
        InitializeToolPlugins();
        _ = RefreshPluginsAsync();
    }
    private async Task RefreshPluginsAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => { var store = PluginStore; var state = store.Read(); return (state, inspected: state.Installed.ToDictionary(x => x.Fingerprint, x => store.Inspect(x))); });
            pluginPanel.Refresh(snapshot.state, snapshot.inspected, DailyPlugin.Current);
            var tools = await Task.Run(() => new DailyToolPlugins(root).Installed());
            enabledToolPlugins = tools.Where(t => t.Info.Available && t.Selection.Active == t.Record.Fingerprint).Select(t=>t.Info).ToArray();
            pluginPanel.AppendTools(tools); toolPanel.SetPlugins(enabledToolPlugins); RefreshDiscovery();
        }
        catch (Exception e) { PluginError(e); }
        pluginPanel.Busy(Unavailable || IsSandboxWindow);
    }
    private async Task ChangePluginAsync(Func<Task> change)
    {
        if (smoke != null || Unavailable || IsSandboxWindow) return;
        pluginChanging = true; SetBusy(busy);
        try { await PauseAutomaticMaintenanceAsync(); if(finalClose || UpdateRestarting)return; await change(); await RefreshPluginsAsync(); }
        catch (OperationCanceledException) when (marketCancellation?.IsCancellationRequested == true) { await RefreshPluginsAsync(); pluginPanel.Status("plugins.market_cancelled"); }
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
        // Share the normal connection's scoped process selection; do not let
        // the check child independently guess a different game installation.
        var game = info.HookSources.Length == 0 ? null : await Task.Run(host.Find, token);
        await DailyPluginProbe.RunAsync(Environment.ProcessPath!, root, info, game?.Executable, token);
    }
    private async Task RestartForPluginsAsync()
    {
        if (smoke != null || Unavailable || IsSandboxWindow || !preferencesPanel.SavePending()) return;
        if (!PluginConfirm("plugins.restart", L.Get("plugins.restart_confirm"))) return;
        pluginChanging = true; SetBusy(busy);
        try
        {
            await PauseAutomaticMaintenanceAsync();
            if(finalClose || UpdateRestarting)return;
            if (DailySandbox.HasIsolatedWindows()) throw new IOException("plugins.isolated_open");
            if (ToolOpen && !await CloseToolWindowsAsync()) throw new IOException("plugins.tool_open");
            using var process = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Directory.GetCurrentDirectory() };
            start.ArgumentList.Add("--restart-after"); start.ArgumentList.Add(process.Id.ToString()); start.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString());
            start.Environment.Remove("DUSTWEAVE_PLUGIN");
            start.Environment.Remove("DUSTWEAVE_PLUGINS");
            _ = Process.Start(start) ?? throw new IOException("plugins.failed");
            finalClose = true; timer.Stop(); Close();
        }
        catch (Exception error) { PluginError(error); }
        finally { pluginChanging = false; SetBusy(busy); }
    }
    private bool PluginConfirm(string key, string message) => DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get(key), message, true)) == true;
    private (bool Confirmed, bool? Share) ConfirmPluginInstall(string name, string version, string publisher, string id, bool sharing, bool tactics, bool tool = false)
    {
        if (sharing && DailyPluginSharing.Read(root, id) == null)
        {
            var dialog = new PluginSharingWindow(this, name + "  " + version + " · " + publisher, false, true, tactics);
            dialog.ContentRendered += (_, _) => FeatureViewed(DailyFeatureDiscovery.Plugin(id));
            return (DailyDialogs.ShowModal(dialog) == true, dialog.SharingEnabled);
        }
        var confirmation = DailyDialogs.Message(this, L.Get("plugins.market_install"), L.Get("plugins.trust", name, version, publisher) + "\n\n" +
            (tactics ? L.Get("plugins.tactics_where") + "\n" + L.Get("plugins.tactics_howto") + "\n\n" : "") + (tool ? L.Get("plugins.tool_where") : L.Get("plugins.market_install_confirm", name, version)), true);
        confirmation.ContentRendered += (_, _) => FeatureViewed(DailyFeatureDiscovery.Plugin(id));
        return (DailyDialogs.ShowModal(confirmation) == true, null);
    }
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
            var info = new DailyPluginInfo(true, "ready", "", first.Version, first.Fingerprint, [], Capabilities: ["tactics"])
            { Id = first.Id, Publisher = "Example", MinHostVersion = "0.9.12", MaxHostVersion = "0.9.99", Names = new() { ["zh-CN"] = "示例扩展", ["zh-TW"] = "範例擴充", ["en-US"] = "Example extension" } };
            var second = new DailyInstalledPlugin("example.second", "1.0.0", new string('e',64),DateTimeOffset.UtcNow);
            var extra = info with {Id=second.Id,Version=second.Version,Fingerprint=second.Fingerprint,Capabilities=["other_task"]};
            var current=DailyPlugin.Combine([info,extra]);
            var state = new DailyPluginSelection(1, first.Fingerprint, old.Fingerprint, [first, old,second]) { Enabled=[first.Fingerprint,second.Fingerprint] };
            var inspected=new Dictionary<string,DailyPluginInfo> { [first.Fingerprint] = info, [old.Fingerprint] = info with { Version = old.Version, Fingerprint = old.Fingerprint },[second.Fingerprint]=extra };
            pluginPanel.Refresh(state, inspected,current);
            var tool=info with {Id="example.tool",Fingerprint=new string('d',64),ToolExecutable="tool/Example.exe",Capabilities=["inventory_read"]};
            var toolRecord=new DailyInstalledPlugin(tool.Id,tool.Version,tool.Fingerprint,DateTimeOffset.UtcNow);
            pluginPanel.AppendTools([(new(1,tool.Fingerprint,null,[toolRecord]),toolRecord,tool)]);
            pluginPanel.SelectMarket(false);
            foreach (var code in DailyLanguage.Codes)
            {
                L.Select(code);
                foreach (var appearance in new[] { 1, 2 })
                {
                    ThemeSelector.SelectedIndex = appearance; Width = 920; Height = 650;
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (!SandboxElements<FrameworkElement>(pluginPanel).Any(e => e.Name == "TacticsUsageGuide" && e.IsVisible)) throw new Exception("Installed plugin lacks its weekly task guide");
                    if(SandboxElements<Button>(pluginPanel).Count(b=>Equals(b.Content,L.Get("plugins.disable")))!=3)throw new Exception("Each enabled daily/tool plugin needs its own disable action");
                    Capture("plugins-" + code + "-" + appearance);
                }
            }
            pluginPanel.Busy(true);
            // Disabled page actions must not be re-enabled by a theme or language refresh.
            pluginPanel.Refresh(state,inspected,current);
            pluginPanel.Busy(false);
            await CheckPluginMarketForSmoke();
        }
        finally { Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", prior); L.Select(language); await RefreshPluginsAsync(); }
    }
}

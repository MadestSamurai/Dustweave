using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public sealed partial class DailyPluginPanel
{
    private readonly StackPanel marketCards = new(), marketBody = new(), installedBody = new();
    private readonly TextBlock marketStatus = Text("plugins.market_ready");
    private readonly Button marketRefresh = ActionButton("plugins.market_refresh"), marketCancel = ActionButton("plugins.market_cancel");
    private readonly Button marketTab = ActionButton("plugins.market"), installedTab = ActionButton("plugins.installed_tab");
    private DailyMarketSnapshot? marketSnapshot;
    private DailyPluginSelection marketSelection = new(1, null, null, []);
    private bool marketReading, marketDownloading;
    public event Action? MarketRefreshRequested;
    public event Action? MarketCancelRequested;
    public event Action<DailyMarketPlugin>? MarketInstallRequested;
    private void InitializeMarket(StackPanel content, StackPanel overview, StackPanel installed, TextBlock state)
    {
        var tabs = new WrapPanel { Margin = new(0, 0, 0, 14) };
        tabs.Children.Add(marketTab); tabs.Children.Add(installedTab); content.Children.Add(tabs);
        installedBody.Children.Add(overview); installedBody.Children.Add(installed);
        overview.Margin = new(0, 0, 0, 20);
        var toolbar = new WrapPanel(); toolbar.Children.Add(marketRefresh); toolbar.Children.Add(marketCancel);
        marketBody.Children.Add(toolbar); marketBody.Children.Add(marketStatus); marketBody.Children.Add(marketCards);
        marketCards.Margin = new(0, 16, 0, 0); state.Margin = new(0, 10, 0, 16);
        content.Children.Add(state); content.Children.Add(marketBody); content.Children.Add(installedBody);
        marketTab.Click += (_, _) => SelectMarket(true); installedTab.Click += (_, _) => SelectMarket(false);
        marketRefresh.Click += (_, _) => MarketRefreshRequested?.Invoke();
        marketCancel.Click += (_, _) => MarketCancelRequested?.Invoke();
        SelectMarket(true); MarketBusy(false);
    }
    public void SelectMarket(bool value)
    {
        marketBody.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        installedBody.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        marketTab.Style = (Style)Application.Current.FindResource(value ? (object)"PrimaryButton" : typeof(Button));
        installedTab.Style = (Style)Application.Current.FindResource(value ? typeof(Button) : (object)"PrimaryButton");
    }
    public void MarketStatus(string key, params object?[] values) => L.Text(marketStatus, key, values);
    public void MarketProgress(bool reading, bool downloading)
    { marketReading = reading; marketDownloading = downloading; MarketBusy(occupied); }
    private void MarketBusy(bool value)
    {
        marketRefresh.IsEnabled = !marketReading && !marketDownloading;
        marketCards.IsEnabled = !value && !marketDownloading;
        marketCancel.Visibility = marketReading || marketDownloading ? Visibility.Visible : Visibility.Collapsed;
    }
    public void Market(DailyMarketSnapshot snapshot)
    {
        marketSnapshot = snapshot; RenderMarket();
        MarketStatus(snapshot.Cached ? "plugins.market_cached" : "plugins.market_updated");
    }
    private void RenderMarket()
    {
        marketCards.Children.Clear();
        if (marketSnapshot == null) return;
        foreach (var item in marketSnapshot.Catalog.Plugins)
        {
            var body = new StackPanel();
            var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            L.Bind(title, TextBlock.TextProperty, () => item.Name(L.Code)); body.Children.Add(title);
            var description = Text("plugins.market_ready");
            L.Bind(description, TextBlock.TextProperty, () => item.Description(L.Code)); body.Children.Add(description);
            body.Children.Add(Text("plugins.market_details", item.Version, item.Publisher, (item.Bytes / 1048576d).ToString("0.0")));
            bool installed = marketSelection.Installed.Any(p => p.Fingerprint.Equals(item.ManifestSha256, StringComparison.OrdinalIgnoreCase));
            if (!item.Compatible) body.Children.Add(Text("plugins.market_requires_host", item.MinHostVersion));
            var button = ActionButton(installed ? "plugins.market_view_installed" : item.Compatible ? "plugins.market_install" : "plugins.incompatible_host");
            button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new(0, 14, 0, 0);
            button.IsEnabled = installed || item.Compatible;
            if (!installed && item.Compatible) button.Style = (Style)Application.Current.FindResource("PrimaryButton");
            button.Click += (_, _) => { if (installed) SelectMarket(false); else MarketInstallRequested?.Invoke(item); };
            body.Children.Add(button); marketCards.Children.Add(Card(body));
        }
        if (marketSnapshot.Catalog.Plugins.Length == 0) marketCards.Children.Add(Text("plugins.market_empty"));
    }
}

public partial class MainWindow
{
    private CancellationTokenSource? marketCancellation;
    private bool marketRequested;
    private void InitializePluginMarket()
    {
        pluginPanel.MarketRefreshRequested += async () => await RefreshPluginMarketAsync();
        pluginPanel.MarketCancelRequested += () => marketCancellation?.Cancel();
        pluginPanel.MarketInstallRequested += async item => await InstallMarketPluginAsync(item);
        pluginPanel.IsVisibleChanged += async (_, _) =>
        { if (pluginPanel.IsVisible && !marketRequested && smoke == null) { marketRequested = true; await RefreshPluginMarketAsync(); } };
        Closed += (_, _) => marketCancellation?.Cancel();
    }
    private async Task RefreshPluginMarketAsync()
    {
        if (smoke != null || marketCancellation != null) return;
        using var operation = new CancellationTokenSource(); marketCancellation = operation;
        using var market = new DailyPluginMarket(root);
        DailyMarketSnapshot? cached = null;
        pluginPanel.MarketProgress(true, false); pluginPanel.MarketStatus("plugins.market_loading");
        try
        {
            cached = await Task.Run(market.Cached, operation.Token);
            if (cached != null) pluginPanel.Market(cached);
            pluginPanel.Market(await market.RefreshAsync(operation.Token));
        }
        catch (OperationCanceledException) { pluginPanel.MarketStatus(operation.IsCancellationRequested ? "plugins.market_cancelled" : "plugins.market_offline"); }
        catch (Exception error)
        {
            pluginPanel.MarketStatus(cached == null ? "plugins.market_offline" : "plugins.market_cached_offline");
            RecordMarketError(error);
        }
        finally { marketCancellation = null; pluginPanel.MarketProgress(false, false); }
    }
    private async Task InstallMarketPluginAsync(DailyMarketPlugin item)
    {
        if (marketCancellation != null || smoke != null || Unavailable || IsSandboxWindow) return;
        var choice = ConfirmPluginInstall(item.Name(L.Code), item.Version, item.Publisher, item.Id, item.SupportsSolutionSharing);
        if (!choice.Confirmed) return;
        using var operation = new CancellationTokenSource(); marketCancellation = operation;
        pluginPanel.MarketProgress(false, true);
        try
        {
            using var market = new DailyPluginMarket(root);
            pluginPanel.MarketStatus("plugins.market_downloading", 0);
            using var package = await market.DownloadAsync(item, PluginStore, new Progress<int>(n => pluginPanel.MarketStatus("plugins.market_downloading", n)), operation.Token);
            // Downloading does not lock the rest of the application. Recheck before selecting executable code.
            if (Unavailable || IsSandboxWindow || finalClose || UpdateRestarting) { pluginPanel.MarketStatus("plugins.market_now_busy"); return; }
            await ChangePluginAsync(async () =>
            {
                operation.Token.ThrowIfCancellationRequested();
                var installed = await Task.Run(() => PluginStore.Install(package), operation.Token);
                await PluginStore.ActivateAsync(installed.Fingerprint, ProbePluginAsync, operation.Token);
                if (choice.Share is { } share) DailyPluginSharing.Write(root, item.Id, share);
                pluginPanel.SelectMarket(false);
            });
        }
        catch (OperationCanceledException) { pluginPanel.MarketStatus(operation.IsCancellationRequested ? "plugins.market_cancelled" : "plugins.market_offline"); }
        catch (Exception error) { PluginError(error); RecordMarketError(error); }
        finally { marketCancellation = null; pluginPanel.MarketProgress(false, false); }
    }
    private void RecordMarketError(Exception error)
    {
        try { DailyJson.Write(Path.Combine(root, "extensions", "market-error.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

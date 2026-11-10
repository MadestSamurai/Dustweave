using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckFeatureDiscoveryForSmoke()
    {
        int checks = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); checks++; }
        string soul = DailyFeatureDiscovery.Tool("soul"), tactics = DailyFeatureDiscovery.Plugin("dustweave.tactics");
        var entry = new DailyMarketPlugin("dustweave.tactics", "1.0.0",
            new() { ["zh-CN"] = "战术教材", ["zh-TW"] = "戰術教材", ["en-US"] = "Tactical textbook" },
            new() { ["zh-CN"] = "本地计算教材解法，完成战斗并领取奖励。", ["zh-TW"] = "本機計算教材解法，完成戰鬥並領取獎勵。", ["en-US"] = "Solve textbook stages locally, complete battles and claim rewards." },
            "Example", "1.5.9", "", "https://example.invalid/tactics.zip", 2000000, new('a', 64), new('b', 64), ["tactics"]);
        DailyMarketSnapshot Snapshot(params DailyMarketPlugin[] plugins) => new(new(1, DailyPluginMarket.Purpose, 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), plugins), true);
        foreach (string language in DailyLanguage.Codes)
        foreach (int appearance in new[] { 1, 2 })
        {
            string folder = Path.Combine(root, "discovery-smoke-" + language + "-" + appearance);
            enabledToolPlugins = []; toolPanel.SetPlugins(enabledToolPlugins);
            discovery = new(folder); discovery.MarkViewed(DailyFeatureDiscovery.Plugin("example.old"));
            toolPanel.SetDiscovery(discovery.IsNew); pluginPanel.SetDiscovery(discovery.IsNew);
            L.Select(language); ThemeSelector.SelectedIndex = appearance; Width = 920; Height = 650;
            var old = entry with { Id = "example.old" };
            pluginPanel.Market(Snapshot(old, entry));
            var record = new DailyInstalledPlugin(entry.Id, entry.Version, entry.ManifestSha256, DateTimeOffset.UtcNow.AddDays(-1));
            var other = record with { Id = old.Id, Fingerprint = new('c', 64), InstalledUtc = DateTimeOffset.UtcNow };
            var info = new DailyPluginInfo(true, "ready", "", entry.Version, record.Fingerprint, [], Capabilities: ["tactics"])
            { Id = entry.Id, Names = entry.Names };
            pluginPanel.Refresh(new(1, null, null, [other, record]), new() { [record.Fingerprint] = info,
                [other.Fingerprint] = info with { Id = old.Id, Fingerprint = other.Fingerprint } }, DailyPlugin.Current);
            pluginPanel.Busy(false);
            WorkspaceTabs.SelectedItem = ToolsTab;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(FeatureDiscoveryUi.GetHasNew(ToolsTab) && FeatureDiscoveryUi.GetHasNew(PluginsTab), "Both sidebar badges required");
            Check(toolPanel.VisibleTools.First() == "soul", "Unread Soul is pinned first");
            Check(discovery.IsNew(soul), "Visiting Tools must not acknowledge Soul");
            toolPanel.VerifyDiscoveryForSmoke();
            Check(discovery.IsNew(soul), "Filtering or a failed open must not acknowledge Soul");
            Check(((FrameworkElement)ToolsTab.Template.FindName("NewFeatureDot", ToolsTab)).IsVisible, "Actual sidebar dot is visible");
            Capture("discovery-tools-" + language + "-" + appearance);
            WorkspaceTabs.SelectedItem = PluginsTab;
            foreach (bool market in new[] { true, false })
            {
                pluginPanel.SelectMarket(market);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var cards = SandboxElements<Border>(pluginPanel).Where(c => c.IsVisible && c.Tag is string key && key.StartsWith("plugin:")).ToArray();
                Check(cards.Length == 2 && (string)cards[0].Tag == tactics, "Unread plugin first in market and installed lists");
                Check(SandboxElements<FrameworkElement>(cards[0]).Any(e => e.Name == "FeatureNewBadge" && e.IsVisible), "Specific plugin row marked");
                Check(discovery.IsNew(tactics), "Plugin tab and card visibility are not viewing receipts");
            }
            pluginPanel.SelectMarket(true); Capture("discovery-plugins-" + language + "-" + appearance);
            pluginPanel.MarketStatus("plugins.market_offline");
            Check(discovery.IsNew(tactics) && FeatureDiscoveryUi.GetHasNew(PluginsTab), "Network failure cannot dismiss known unread plugins");
            var dialog = FeatureDetails(tactics, entry.Name(language), entry.Description(language), true);
            Check(discovery.IsNew(tactics), "Creating an unopened dialog is not viewing");
            dialog.ShowActivated = false; dialog.Left = -12000; dialog.Top = 0; dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            try
            {
                dialog.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(!discovery.IsNew(tactics), "Rendered plugin details acknowledge only that plugin");
                Check(SandboxElements<FrameworkElement>(dialog).Any(e => e.Name == "TacticsUsageGuide" && e.IsVisible), "Details explain the added weekly task");
                Capture("discovery-details-" + language + "-" + appearance, (FrameworkElement)dialog.Content);
            }
            finally { dialog.Close(); }
            Check(!FeatureDiscoveryUi.GetHasNew(PluginsTab) && FeatureDiscoveryUi.GetHasNew(ToolsTab), "Other unread category survives");
            FeatureViewed(soul);
            Check(FeatureDiscoveryUi.GetHasNew(ToolsTab) && discovery.IsNew(DailyFeatureDiscovery.Tool("talent-practice")), "Talent practice remains unread after viewing Soul");
            FeatureViewed(DailyFeatureDiscovery.Tool("talent-practice"));
            Check(!FeatureDiscoveryUi.GetHasNew(ToolsTab), "Last viewed tool clears Tools indicator");
            var reopened = new DailyFeatureDiscovery(folder);
            Check(!reopened.IsNew(tactics) && !reopened.IsNew(soul), "Actual UI view receipts survive restart");
            var future = entry with { Id = "example.future", Version = "2.0.0" };
            pluginPanel.Market(Snapshot(entry with { Version = "2.0.0" }, future));
            Check(FeatureDiscoveryUi.GetHasNew(PluginsTab) && discovery.IsNew(DailyFeatureDiscovery.Plugin(future.Id)), "Future catalog ID automatically alerts");
            Check(!discovery.IsNew(tactics), "Updating an existing plugin does not re-alert");
            FeatureViewed(DailyFeatureDiscovery.Plugin(future.Id));
            var tool = info with { Id = "example.future-tool", ToolExecutable = "tool.exe", Capabilities = ["inventory_read"], Fingerprint = new('d', 64) };
            var toolRecord = record with { Id = tool.Id, Fingerprint = tool.Fingerprint };
            enabledToolPlugins = [tool]; toolPanel.SetPlugins(enabledToolPlugins);
            pluginPanel.AppendTools([(new(1, tool.Fingerprint, null, [toolRecord]), toolRecord, tool)]);
            Check(toolPanel.VisibleTools.First() == "plugin:" + tool.Id, "Future plugin tool automatically pinned above built-ins");
            Check(FeatureDiscoveryUi.GetHasNew(ToolsTab) && FeatureDiscoveryUi.GetHasNew(PluginsTab), "Plugin tool announces on both pages");
            FeatureViewed(DailyFeatureDiscovery.Plugin(tool.Id));
            Check(!FeatureDiscoveryUi.GetHasNew(ToolsTab) && !FeatureDiscoveryUi.GetHasNew(PluginsTab), "Viewing shared tool removes both markers");
        }
        L.Audit();
        Check(marketCancellation == null, "No live market request in isolated UI checks");
        DailyJson.Write(Path.Combine(smoke!, "smoke.json"), new { status = "passed", scope = "feature-discovery", checks,
            languages = 3, themes = 2, realGameTouched = false, networkTouched = false });
    }
}

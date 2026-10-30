using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;
public partial class MainWindow
{
    private async Task CheckPluginMarketForSmoke()
    {
        if (smoke == null) throw new InvalidOperationException("Isolated UI test required");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        var entry = new DailyMarketPlugin("dustweave.tactics", "1.0.0",
            new() { ["zh-CN"] = "战术教材", ["zh-TW"] = "戰術教材", ["en-US"] = "Tactical textbook" },
            new() { ["zh-CN"] = "本地计算教材解法，并接续完成战斗与奖励领取。可选择参与解法共享。", ["zh-TW"] = "本機計算教材解法，並接續完成戰鬥與獎勵領取。可選擇參與解法共享。", ["en-US"] = "Solve textbook stages locally, then complete battles and claim rewards. Shared solutions are optional." },
            "MadestSamurai", "1.5.9", "", "https://example.invalid/tactics.zip", 2300000, new string('a', 64), new string('c', 64), ["tactics"])
            { SupportsSolutionSharing = true };
        foreach (string language in DailyLanguage.Codes)
        foreach (int theme in new[] { 1, 2 })
        {
            L.Select(language); ThemeSelector.SelectedIndex = theme; Width = 920; Height = 650;
            pluginPanel.Market(new(new(1, DailyPluginMarket.Purpose, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), [entry]), false));
            pluginPanel.SelectMarket(true); pluginPanel.Busy(false);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(SandboxElements<Button>(pluginPanel).Any(b => b.IsVisible && b.IsEnabled && b.Content?.ToString() == L.Get("plugins.market_install")), "Market has no visible install action");
            Capture("plugin-market-" + language + "-" + theme);
            var dialog = new PluginSharingWindow(this, entry.Name(language) + " 1.0.0", false, true) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0 };
            try
            {
                dialog.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(!dialog.SharingEnabled, "First installation must default to local-only");
                var options = SandboxElements<RadioButton>(dialog).ToArray(); Check(options.Length == 2, "Sharing must be one two-way choice");
                options.Single(r => r.Name == "PluginShareSolutions").IsChecked = true;
                Check(dialog.SharingEnabled, "Community choice was not reflected");
                Check(SandboxElements<Button>(dialog).Any(b => b.IsDefault && b.IsVisible && b.ActualWidth > 0), "Install confirmation is not reachable");
                Capture("plugin-sharing-" + language + "-" + theme, (FrameworkElement)dialog.Content);
                Check(DailyPluginSharing.Read(root, entry.Id) == null, "Choosing a radio button before confirming installed a preference");
            }
            finally { dialog.Close(); }
            pluginPanel.MarketProgress(true, false); pluginPanel.MarketStatus("plugins.market_offline");
            Check(SandboxElements<Button>(pluginPanel).Any(b => b.IsVisible && b.Content?.ToString() == L.Get("plugins.market_cancel")), "Market request has no cancel action");
            pluginPanel.MarketProgress(false, false);
        }
        Check(marketCancellation == null, "Synthetic UI opened a live market request");
        DailyJson.Write(Path.Combine(smoke, "plugin-market.json"), new { status = "passed", checks, realGameTouched = false, networkTouched = false });
    }
}

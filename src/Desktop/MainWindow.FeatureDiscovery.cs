using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Dustweave.Desktop;

public static class FeatureDiscoveryUi
{
    public static readonly DependencyProperty HasNewProperty = DependencyProperty.RegisterAttached(
        "HasNew", typeof(bool), typeof(FeatureDiscoveryUi), new PropertyMetadata(false));
    public static bool GetHasNew(DependencyObject value) => (bool)value.GetValue(HasNewProperty);
    public static void SetHasNew(DependencyObject value, bool state) => value.SetValue(HasNewProperty, state);
    internal static FrameworkElement Badge()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 6) };
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, "Primary"); row.Children.Add(dot);
        var text = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Primary");
        DailyLanguage.Current.Bind(text, TextBlock.TextProperty, "discovery.new"); row.Children.Add(text);
        return row;
    }
}

public partial class MainWindow
{
    private DailyFeatureDiscovery discovery = null!;
    private void InitializeFeatureDiscovery()
    {
        discovery = new(root);
        toolPanel.SetDiscovery(discovery.IsNew);
        pluginPanel.SetDiscovery(discovery.IsNew);
        toolPanel.DetailsRequested += row => ShowFeatureDetails(DailyFeatureDiscovery.Tool(row.Id), row.Name, row.Description, false);
        pluginPanel.DetailsRequested += (id, name, description, tactics) =>
            ShowFeatureDetails(DailyFeatureDiscovery.Plugin(id), name, description, tactics);
        pluginPanel.DiscoveryChanged += RefreshDiscovery;
        RefreshDiscovery();
    }
    private void RefreshDiscovery()
    {
        if (discovery == null) return;
        bool tools = DailyToolCatalog.All.Any(t => discovery.IsNew(DailyFeatureDiscovery.Tool(t.Id))) ||
            enabledToolPlugins.Any(p => discovery.IsNew(DailyFeatureDiscovery.Plugin(p.Id)));
        bool plugins = pluginPanel.HasNewFeatures;
        FeatureDiscoveryUi.SetHasNew(ToolsTab, tools); FeatureDiscoveryUi.SetHasNew(PluginsTab, plugins);
        L.Bind(ToolsTab, AutomationProperties.HelpTextProperty, tools ? "discovery.tools" : "nav.tools");
        L.Bind(PluginsTab, AutomationProperties.HelpTextProperty, plugins ? "discovery.plugins" : "plugins.title");
    }
    private void FeatureViewed(string key)
    {
        try { discovery.MarkViewed(key); }
        catch (Exception error) { StartupDiagnostics.Fail(error); }
        toolPanel.RefreshDiscovery(); pluginPanel.RefreshDiscovery(); RefreshDiscovery();
    }
    private Window FeatureDetails(string key, string name, string description, bool tactics)
    {
        var content = new StackPanel { Margin = new(24, 12, 24, 24) };
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        if (tactics) content.Children.Add(DailyPluginPanel.TacticsGuide());
        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0), IsCancel = true };
        L.Bind(close, ContentControl.ContentProperty, "discovery.close"); content.Children.Add(close);
        var window = new Window { Owner = this, Title = name, Width = 520, SizeToContent = SizeToContent.Height,
            MaxHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        close.Click += (_, _) => window.Close();
        DailyDialogs.Prepare(window);
        // Deliberate detail opening, after rendering, counts as viewed. Merely visiting a list never does.
        window.ContentRendered += (_, _) => FeatureViewed(key);
        return window;
    }
    private void ShowFeatureDetails(string key, string name, string description, bool tactics) =>
        DailyDialogs.ShowModal(FeatureDetails(key, name, description, tactics));
    private async Task ObserveToolWindow(string id)
    {
        string key = DailyFeatureDiscovery.Tool(id);
        if (!discovery.IsNew(key) || toolSession.Current is not { } state || state.ToolId != id) return;
        try
        {
            using var process = Process.GetProcessById(state.ProcessId);
            for (int i = 0; i < 40 && !finalClose; i++)
            {
                process.Refresh();
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != state.StartTicks) return;
                if (process.MainWindowHandle != IntPtr.Zero) { FeatureViewed(key); return; }
                await Task.Delay(250);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}

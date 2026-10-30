using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

internal sealed class PluginSharingWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly RadioButton local = new() { Name = "PluginLocalOnly", GroupName = "Sharing", Margin = new(0, 0, 0, 18) };
    private readonly RadioButton sharing = new() { Name = "PluginShareSolutions", GroupName = "Sharing" };
    internal bool SharingEnabled => sharing.IsChecked == true;
    internal PluginSharingWindow(Window owner, string name, bool current, bool installation)
    {
        Owner = owner; Width = 580; MinWidth = 440; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, installation ? "plugins.market_install" : "plugins.sharing_settings");
        var body = new StackPanel { Margin = new(24, 8, 24, 24) };
        body.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 20) });
        void Choice(RadioButton button, string title, string explanation)
        {
            var content = new StackPanel();
            var heading = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
            L.Text(heading, title); L.Text(detail, explanation); content.Children.Add(heading); content.Children.Add(detail);
            button.Content = content; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            L.Bind(button, System.Windows.Automation.AutomationProperties.NameProperty, title); body.Children.Add(button);
        }
        Choice(local, "plugins.sharing_local", "plugins.sharing_local_help");
        Choice(sharing, "plugins.sharing_community", "plugins.sharing_community_help");
        local.IsChecked = !current; sharing.IsChecked = current;
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 20, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Text(note, installation ? "plugins.sharing_install_note" : "plugins.sharing_change_note"); body.Children.Add(note);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 22, 0, 0) };
        var cancel = new Button { IsCancel = true, Margin = new(0, 0, 10, 0) }; L.Bind(cancel, ContentControl.ContentProperty, "plugins.market_cancel");
        var confirm = new Button { IsDefault = true, Style = (Style)FindResource("PrimaryButton") }; L.Bind(confirm, ContentControl.ContentProperty, installation ? "plugins.market_install" : "plugins.sharing_save");
        confirm.Click += (_, _) => { DialogResult = true; }; actions.Children.Add(cancel); actions.Children.Add(confirm); body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DailyDialogs.Prepare(this);
    }
}

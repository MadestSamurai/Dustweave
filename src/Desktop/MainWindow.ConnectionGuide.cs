using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private sealed record ConnectionGuideSeen(int Revision);
    private bool ShowInitialConnectionGuide()
    {
        if (smoke != null || ScheduledStartup || IsSandboxWindow || Unavailable || catalog == null ||
            catalog.Accounts.Any(a => a.Valid) || DailyJson.TryRead<ConnectionGuideSeen>(Path.Combine(root, "connection-guide.json"))?.Revision >= 1)
            return false;
        ShowConnectionGuide();
        return true;
    }
    private void ConnectionGuide_Click(object sender, RoutedEventArgs e) => ShowConnectionGuide();
    private void ShowConnectionGuide()
    {
        releaseDialogOpen = true;
        try
        {
            DailyDialogs.ShowModal(CreateConnectionGuideWindow());
            if (smoke == null) DailyJson.Write(Path.Combine(root, "connection-guide.json"), new ConnectionGuideSeen(1));
        }
        finally { releaseDialogOpen = false; }
    }
    private Window CreateConnectionGuideWindow()
    {
        var dialog = new Window { Owner = this, Width = Math.Min(620, SystemParameters.WorkArea.Width - 64),
            Height = Math.Min(664, SystemParameters.WorkArea.Height - 80), ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        L.Bind(dialog, Window.TitleProperty, "guide.title");
        TextBlock Text(string key, double size, string brush = "Ink")
        {
            var text = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush); L.Text(text, key); return text;
        }
        var layout = new DockPanel { Margin = new Thickness(24, 8, 24, 24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var later = new Button { IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        L.Bind(later, ContentControl.ContentProperty, "guide.later"); later.Click += (_, _) => dialog.Close(); actions.Children.Add(later);
        var open = new Button { IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        L.Bind(open, ContentControl.ContentProperty, "guide.accounts");
        open.Click += (_, _) => { WorkspaceTabs.SelectedItem = AccountsTab; dialog.Close(); ConnectionGuideButton.Focus(); };
        actions.Children.Add(open); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var body = new StackPanel();
        var heading = Text("guide.heading", 24); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 22); body.Children.Add(heading);
        for (int i = 1; i <= 3; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var marker = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = i.ToString(), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            marker.SetResourceReference(Border.BackgroundProperty, "PrimarySoft");
            ((TextBlock)marker.Child).SetResourceReference(TextBlock.ForegroundProperty, "Primary");
            row.Children.Add(marker);
            var copy = new StackPanel(); var title = Text("guide.step" + i + ".title", 16); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 2, 0, 6);
            copy.Children.Add(title); copy.Children.Add(Text("guide.step" + i + ".body", 14, "MutedInk")); Grid.SetColumn(copy, 1); row.Children.Add(copy); body.Children.Add(row);
        }
        var note = new StackPanel();var noteTitle = Text("guide.permissions.title", 14);noteTitle.FontWeight = FontWeights.SemiBold;noteTitle.Margin = new Thickness(0,0,0,6);
        note.Children.Add(noteTitle);note.Children.Add(Text("guide.permissions.body", 13, "MutedInk"));
        var notice = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = note }; notice.SetResourceReference(Border.BackgroundProperty, "SurfaceMuted");body.Children.Add(notice);
        var reopen=Text("guide.reopen",12,"MutedInk");reopen.Margin=new Thickness(0,14,0,0);body.Children.Add(reopen);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Content = layout;DailyDialogs.Prepare(dialog);return dialog;
    }
}

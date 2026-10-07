using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dustweave.Desktop;

internal sealed partial class DailyScheduleAccounts : WrapPanel
{
    private static DailyLanguage L => DailyLanguage.Current;
    private DailyAccount[] available = [];
    private readonly Dictionary<string, DailyAccount> known = new();
    private readonly List<string> selected = [];
    internal string[] SelectedKeys => selected.ToArray();
    internal bool HasUnavailable => selected.Any(key => !available.Any(a => a.Valid && a.AccountKey == key));

    internal void SetAccounts(IEnumerable<DailyAccount> source)
    {
        available = source.DistinctBy(a => a.AccountKey).ToArray();
        foreach (var account in available) known[account.AccountKey] = account;
        Render();
    }
    internal void Select(IEnumerable<string> keys)
    {
        selected.Clear(); selected.AddRange(keys.Distinct()); Render();
    }
    private void Render()
    {
        if (dragging) { deferredRender = true; return; }
        pressedKey = null; ClearInsertion();
        Children.Clear();
        foreach (var key in selected)
        {
            known.TryGetValue(key, out var account);
            bool valid = available.Any(a => a.Valid && a.AccountKey == key);
            var body = new Grid { Margin = new Thickness(14, 12, 10, 12) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var number = new TextBlock { Text = (selected.IndexOf(key) + 1).ToString(), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            number.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); body.Children.Add(number);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            if (account != null) { name.Text = account.Name; name.ToolTip = account.Name; }
            else L.Text(name, "schedule.account_unavailable");
            labels.Children.Add(name);
            var detail = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            detail.SetResourceReference(TextBlock.ForegroundProperty, valid ? "MutedInk" : "Error");
            if (!valid) L.Text(detail, "schedule.account_unavailable");
            else if (!string.IsNullOrWhiteSpace(account?.MaskedMemberId)) detail.Text = account.MaskedMemberId;
            else L.Text(detail, "schedule.account_slot", account!.SlotNumber);
            labels.Children.Add(detail); Grid.SetColumn(labels, 1); body.Children.Add(labels);
            var remove = new Button { Width = 32, Height = 32, MinWidth = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Tag = key, Style = (Style)FindResource("QuietButton") };
            var cross = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("Icon.WindowClose"), Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.5 };
            cross.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedInk"); remove.Content = cross;
            L.Bind(remove, ToolTipService.ToolTipProperty, () => L.Get("schedule.remove_account", account?.Name ?? L.Get("schedule.account_unavailable")));
            L.Bind(remove, AutomationProperties.NameProperty, () => L.Get("schedule.remove_account", account?.Name ?? L.Get("schedule.account_unavailable")));
            remove.Click += (_, _) => { selected.Remove(key); Render(); Children.OfType<Button>().Last().Focus(); };
            Grid.SetColumn(remove, 2); body.Children.Add(remove);
            var card = new Border { Width = 244, Height = 88, Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body, Tag = key };
            card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, valid ? "Line" : "Error"); PrepareReorder(card, account?.Name ?? L.Get("schedule.account_unavailable")); Children.Add(card);
        }
        var add = new Button { Width = 244, Height = 88, Margin = new Thickness(0, 0, 12, 12), Style = (Style)FindResource("ScheduleAddAccount") };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plus = new System.Windows.Shapes.Path { Data = Geometry.Parse("M8,2 V14 M2,8 H14"), Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 10, 0) };
        plus.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedInk"); addContent.Children.Add(plus);
        var caption = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; L.Text(caption, "schedule.add_account"); addContent.Children.Add(caption); add.Content = addContent;
        L.Bind(add, AutomationProperties.NameProperty, "schedule.add_account");
        add.IsEnabled = available.Any(a => a.Valid && !selected.Contains(a.AccountKey));
        if (!add.IsEnabled) { L.Bind(add, ToolTipService.ToolTipProperty, available.Any(a => a.Valid) ? "schedule.all_added" : "schedule.no_accounts"); ToolTipService.SetShowOnDisabled(add, true); }
        add.Click += (_, _) => { if (Window.GetWindow(this) is { } owner) DailyDialogs.ShowModal(CreatePicker(owner)); };
        Children.Add(add);
        if (selected.Count == 0)
        {
            var hint = new TextBlock { MaxWidth = 320, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); L.Text(hint, available.Any(a => a.Valid) ? "schedule.add_help" : "schedule.no_accounts"); Children.Add(hint);
        }
    }
    internal Window CreatePicker(Window owner)
    {
        var dialog = new Window { Owner = owner, Width = 520, Height = Math.Min(Math.Clamp(210 + 65 * available.Count(a => a.Valid && !selected.Contains(a.AccountKey)), 320, 520), SystemParameters.WorkArea.Height - 64), ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        L.Bind(dialog, Window.TitleProperty, "schedule.add_account");
        var body = new DockPanel { Margin = new Thickness(24, 8, 24, 24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var label = new TextBlock { Margin = new Thickness(0, 0, 0, 8) }; L.Text(label, "schedule.search_accounts"); header.Children.Add(label);
        var search = new TextBox(); L.Bind(search, AutomationProperties.NameProperty, "schedule.search_accounts"); header.Children.Add(search); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }; L.Bind(cancel, ContentControl.ContentProperty, "common.cancel"); buttons.Children.Add(cancel);
        var confirm = new Button { IsDefault = true, IsEnabled = false, Style = (Style)FindResource("PrimaryButton") }; buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        var list = new StackPanel(); body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list });
        var choices = new Dictionary<string, CheckBox>();
        void Count() { var count = choices.Values.Count(c => c.IsChecked == true); confirm.IsEnabled = count > 0; L.Bind(confirm, ContentControl.ContentProperty, "schedule.add_selected", count); }
        foreach (var account in available.Where(a => a.Valid && !selected.Contains(a.AccountKey)))
        {
            var box = new CheckBox { Tag = account, Padding = new Thickness(0, 10, 8, 10), Margin = new Thickness(0, 2, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = account.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, ToolTip = account.Name });
            var id = new TextBlock { Text = account.MaskedMemberId, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) }; id.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); text.Children.Add(id); box.Content = text;
            AutomationProperties.SetName(box, account.Name + " " + account.MaskedMemberId); box.Checked += (_, _) => Count(); box.Unchecked += (_, _) => Count(); choices[account.AccountKey] = box; list.Children.Add(box);
        }
        var empty = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed }; L.Text(empty, "schedule.search_empty"); list.Children.Add(empty);
        search.TextChanged += (_, _) => {
            foreach (var box in choices.Values) { var account = (DailyAccount)box.Tag; box.Visibility = (account.Name + " " + account.MaskedMemberId).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; }
            empty.Visibility = choices.Values.Any(c => c.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        };
        confirm.Click += (_, _) => {
            foreach (var (key, box) in choices) if (box.IsChecked == true && !selected.Contains(key) && available.Any(a => a.Valid && a.AccountKey == key)) selected.Add(key);
            Render(); dialog.DialogResult = true;
        };
        Count(); dialog.Content = body; DailyDialogs.Prepare(dialog); dialog.ContentRendered += (_, _) => search.Focus(); return dialog;
    }
}

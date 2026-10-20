using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using static Dustweave.DailyData;
namespace Dustweave.Desktop;

internal sealed class DailyCookingWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal sealed record Recipe(long Id, string Name, string Materials, string SearchText = "");
    private readonly ObservableCollection<Recipe> chosen = [];
    private Recipe[] catalog = [];
    private readonly DataGrid available = Table(), selected = Table();
    private readonly TextBox search = new() { Margin = new(0, 10, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button save = Button("common.save"), add = Button("cooking.add"), remove = Button("cooking.remove"), up = Button("cooking.up"), down = Button("cooking.down");
    public long[]? Result { get; private set; }
    internal DailyCookingWindow(IEnumerable<long> current, JsonObject? fixture = null)
    {
        Width = 1000; Height = 680; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "cooking.title");
        var layout = new DockPanel { Margin = new(24, 8, 24, 24) };
        var intro = Text("cooking.hint"); intro.Margin = new(0, 0, 0, 12); DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("common.cancel"); cancel.IsCancel = true; cancel.Click += (_, _) => Close();
        save.SetResourceReference(StyleProperty, "PrimaryButton"); save.IsDefault = true;
        save.Click += (_, _) => { Result = chosen.Select(r => r.Id).ToArray(); DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new DockPanel(); var right = new DockPanel();
        var leftTitle = Text("cooking.available"); leftTitle.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(leftTitle, Dock.Top); left.Children.Add(leftTitle);
        L.Bind(search, System.Windows.Automation.AutomationProperties.NameProperty, "cooking.search"); L.Bind(search, ToolTipProperty, "cooking.search");
        DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); left.Children.Add(available);
        var rightTitle = Text("cooking.selected"); rightTitle.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(rightTitle, Dock.Top); right.Children.Add(rightTitle);
        var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 10, 0, 10) }; order.Children.Add(up); order.Children.Add(down); DockPanel.SetDock(order, Dock.Top); right.Children.Add(order); right.Children.Add(selected);
        var transfer = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) }; transfer.Children.Add(add); transfer.Children.Add(remove);
        Grid.SetColumn(transfer, 1); Grid.SetColumn(right, 2); columns.Children.Add(left); columns.Children.Add(transfer); columns.Children.Add(right); layout.Children.Add(columns); Content = layout;
        foreach (var grid in new[] { available, selected })
        {
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "Line");
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
            grid.CellStyle = cellStyle;
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
            textStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            textStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("Text") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }));
            var name = new DataGridTextColumn { Binding = new Binding(nameof(Recipe.Name)), Width = new(1, DataGridLengthUnitType.Star) }; L.Bind(name, DataGridColumn.HeaderProperty, "cooking.recipe"); grid.Columns.Add(name);
            var materials = new DataGridTextColumn { Binding = new Binding(nameof(Recipe.Materials)), Width = new(1.35, DataGridLengthUnitType.Star) }; L.Bind(materials, DataGridColumn.HeaderProperty, "cooking.materials"); grid.Columns.Add(materials);
            name.ElementStyle = materials.ElementStyle = textStyle;
            grid.SelectionChanged += (_, _) => Controls();
        }
        selected.ItemsSource = chosen;
        search.TextChanged += (_, _) => Filter();
        add.Click += (_, _) => Add(); remove.Click += (_, _) => Remove();
        available.MouseDoubleClick += (_, _) => Add();
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        selected.PreviewKeyDown += (_, e) => { if (e.Key == Key.Delete) { Remove(); e.Handled = true; } else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.Up or Key.Down) { Move(e.Key == Key.Up ? -1 : 1); e.Handled = true; } };
        save.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                var data = fixture ?? await Task.Run(() => DailyTradeCatalog.Read(DailyTradeData.CatalogPath(AppContext.BaseDirectory)));
                catalog = DailyCustomCooking.Choices(data, L.Code).Select(r => new Recipe(r.Id, r.Name, r.Materials, r.SearchText))
                    .OrderBy(r => r.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(L.Code), true)).ToArray();
                foreach (long id in current) chosen.Add(catalog.SingleOrDefault(r => r.Id == id) ?? new(id, "#" + id, L["cooking.unavailable"]));
                save.IsEnabled = true; Filter(); search.Focus();
            }
            catch (Exception error) { DailyUiText.Error(status, error); }
        };
    }
    private void Filter()
    {
        var ids = chosen.Select(r => r.Id).ToHashSet(); string term = search.Text.Trim();
        available.ItemsSource = catalog.Where(r => !ids.Contains(r.Id) && (r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) || r.Materials.Contains(term, StringComparison.CurrentCultureIgnoreCase) || r.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        L.Bind(status, TextBlock.TextProperty, () => chosen.Count == 0 ? L["cooking.empty"] : L.Get("cooking.count", chosen.Count)); Controls();
    }
    private void Add() { foreach (Recipe row in available.SelectedItems.Cast<Recipe>().ToArray()) if (!chosen.Any(r => r.Id == row.Id)) chosen.Add(row); Filter(); }
    private void Remove() { foreach (Recipe row in selected.SelectedItems.Cast<Recipe>().ToArray()) chosen.Remove(row); Filter(); }
    private void Move(int delta) { int i = selected.SelectedIndex; if (selected.SelectedItems.Count != 1 || i + delta < 0 || i + delta >= chosen.Count) return; var row = chosen[i]; chosen.Move(i, i + delta); selected.SelectedItem = row; selected.ScrollIntoView(row); Controls(); }
    private void Controls() { add.IsEnabled = available.SelectedItems.Count > 0; remove.IsEnabled = selected.SelectedItems.Count > 0; up.IsEnabled = selected.SelectedItems.Count == 1 && selected.SelectedIndex > 0; down.IsEnabled = selected.SelectedItems.Count == 1 && selected.SelectedIndex < chosen.Count - 1; }
    internal void VerifyForSmoke(string expectedName, string expectedMaterials)
    {
        if (!save.IsEnabled || catalog.Length < 3) throw new Exception("Recipe catalog did not load");
        var translated = catalog.Single(r => r.Id == 2);
        if (translated.Name != expectedName || translated.Materials != expectedMaterials) throw new Exception("Recipe or ingredient locale was lost");
        var old = chosen.ToArray(); chosen.Clear(); Filter();
        search.Text = "蜂蜜奶油杏仁";
        if (available.Items.Count != 1 || ((Recipe)available.Items[0]).Id != 2) throw new Exception("Cross-language recipe search lost identity");
        search.Clear();
        available.SelectedItems.Add(available.Items[0]); available.SelectedItems.Add(available.Items[1]); Add();
        if (chosen.Count != 2 || available.Items.Count != catalog.Length - 2) throw new Exception("Recipe transfer duplicated or lost rows");
        var first = chosen[0]; selected.SelectedItem = chosen[1]; Move(-1);
        if (chosen[1] != first) throw new Exception("Recipe order did not move");
        Remove(); if (chosen.Count != 1) throw new Exception("Recipe removal failed");
        search.Text = "__no_such_recipe__"; if (available.Items.Count != 0 || chosen.Count != 1) throw new Exception("Search altered the cooking order");
        search.Clear(); chosen.Clear(); foreach (var row in old) chosen.Add(row); Filter();
    }
    private static DataGrid Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, BorderThickness = new(1), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static TextBlock Text(string key) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; L.Bind(text, TextBlock.TextProperty, key); return text; }
    private static Button Button(string key) { var b = new Button { Margin = new(0, 0, 8, 6), MinHeight = 36 }; L.Bind(b, ContentProperty, key); return b; }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static Dustweave.DailyData;
namespace Dustweave.Desktop;

// Cooking order, dish sales and ingredient sales are edited together because they run in
// that order after favorite purchases. Only the saved IDs and sale rules leave this window.
internal sealed class DailyCookingWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal sealed record Recipe(long Id, string Name, string Materials, string SearchText = "", long Output = 0);
    internal sealed class Entry(Recipe recipe) : INotifyPropertyChanged
    {
        private int position;
        private bool canUp, canDown, sold;
        public Recipe Recipe { get; } = recipe;
        public long Id => Recipe.Id;
        public string Name => Recipe.Name;
        public string Materials => Recipe.Materials;
        public string Description => Name + "\n" + Materials;
        public int Position { get => position; set => Set(ref position, value); }
        public bool CanUp { get => canUp; set => Set(ref canUp, value); }
        public bool CanDown { get => canDown; set => Set(ref canDown, value); }
        // Display only: the dish is checked under dish sales.
        public bool Sold { get => sold; set => Set(ref sold, value); }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    private sealed record DragPayload(DataGrid Source, object Item);
    private const string PlusIcon = "M8,3 V13 M3,8 H13", UpIcon = "M4,10 L8,6 L12,10", DownIcon = "M4,6 L8,10 L12,6", RemoveIcon = "M4,4 L12,12 M12,4 L4,12";
    private static readonly string[] pageKeys = ["customsales.cooking", "customsales.food", "customsales.materials"];
    private static Style? iconStyle;
    private static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(DailyCookingWindow), new PropertyMetadata(false));
    public bool Compact { get => (bool)GetValue(CompactProperty); private set => SetValue(CompactProperty, value); }
    private readonly ObservableCollection<Entry> chosen = [];
    private Recipe[] catalog = [];
    private readonly DataGrid available = Table(), selected = Table();
    private readonly TextBox search = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) };
    private readonly TextBlock catalogEmpty = DailySalePanel.Overlay(), orderEmpty = DailySalePanel.Overlay();
    private readonly Button save = Button("common.save");
    public long[]? Result { get; private set; }
    public DailySalePreference[]? SalesResult { get; private set; }
    private readonly DailySalePanel foodSales = new(true), materialSales = new(false);
    private readonly TabControl tabs = new() { BorderThickness = new(0), Background = Brushes.Transparent };
    private readonly TabItem[] pages = new TabItem[3];
    private readonly InsertionLine line;
    private bool loaded, lineAttached;
    private Point? dragStart;
    private object? dragItem;
    private DataGrid? dragSource;
    internal DailyCookingWindow(IEnumerable<long> current, JsonObject? fixture = null, DailySalePreference[]? currentSales = null)
    {
        Width = 1000; Height = 680; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "cooking.title");
        var layout = new DockPanel { Margin = new(24, 8, 24, 24) };
        var intro = Text("cooking.hint"); intro.Margin = new(0, 0, 0, 12); intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro);

        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("common.cancel"); cancel.IsCancel = true; cancel.Click += (_, _) => Close();
        // Enter belongs to search and list editing here, so Save is not the default button.
        save.SetResourceReference(StyleProperty, "PrimaryButton"); save.Margin = new(0);
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);

        // Two aligned lists: catalog on the left, the cooking order on the right. Row actions
        // replace the former transfer column.
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1.15, GridUnitType.Star) });
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            columns.RowDefinitions.Add(new RowDefinition { Height = height });
        void Place(UIElement element, int row, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); columns.Children.Add(element); }
        Place(Heading("cooking.available"), 0, 0);
        Place(Heading("cooking.selected"), 0, 2);
        var searchField = DailySalePanel.SearchField(search, "cooking.search"); searchField.Margin = new(0, 8, 0, 10); Place(searchField, 1, 0);
        var orderHint = Text("cooking.order_hint"); orderHint.FontSize = 12; orderHint.VerticalAlignment = VerticalAlignment.Center;
        orderHint.Margin = new(0, 8, 0, 10); orderHint.MinHeight = 0; orderHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var hintRow = new Grid { MinHeight = 36, Margin = new(0, 8, 0, 10) }; orderHint.Margin = new(0); hintRow.Children.Add(orderHint); Place(hintRow, 1, 2);
        Place(new Grid { Children = { available, catalogEmpty } }, 2, 0);
        Place(new Grid { Children = { selected, orderEmpty } }, 2, 2);
        L.Bind(orderEmpty, TextBlock.TextProperty, "cooking.order_empty");

        DailySalePanel.StyleTable(available, selectable: true); DailySalePanel.StyleTable(selected, selectable: true);
        available.Columns.Add(TextColumn("cooking.recipe", nameof(Recipe.Name), 1));
        available.Columns.Add(TextColumn("cooking.materials", nameof(Recipe.Materials), 1.35));
        available.Columns.Add(ActionColumn(48, IconButton(PlusIcon, "cooking.add_one", (s, _) => { if (Item<Recipe>(s) is { } recipe) AddRecipes([recipe], chosen.Count); }, null)));
        selected.Columns.Add(PositionColumn());
        var nameCell = new FrameworkElementFactory(typeof(DockPanel));
        nameCell.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        nameCell.AppendChild(DailySalePanel.Badge("cooking.sold_tag", "cooking.sold_help", nameof(Entry.Sold)));
        nameCell.AppendChild(DailySalePanel.NameText(nameof(Entry.Name)));
        var details = new FrameworkElementFactory(typeof(StackPanel));
        details.SetValue(MarginProperty, new Thickness(8, 0, 8, 0));
        details.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        details.SetBinding(ToolTipProperty, new Binding(nameof(Entry.Description)));
        details.AppendChild(nameCell);
        var compactMaterials = DailySalePanel.NameText(nameof(Entry.Materials));
        compactMaterials.SetValue(TextBlock.FontSizeProperty, 12.0);
        compactMaterials.SetValue(MarginProperty, new Thickness(0, 3, 0, 0));
        compactMaterials.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        compactMaterials.SetBinding(VisibilityProperty, new Binding(nameof(Compact)) { Source = this, Converter = new BooleanToVisibilityConverter() });
        details.AppendChild(compactMaterials);
        var name = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = details }, Width = new(1, DataGridLengthUnitType.Star) };
        L.Bind(name, DataGridColumn.HeaderProperty, "cooking.recipe"); selected.Columns.Add(name);
        var materialColumn = TextColumn("cooking.materials", nameof(Entry.Materials), 1.2);
        selected.Columns.Add(materialColumn);
        SizeChanged += (_, _) => { Compact = ActualWidth < 920; materialColumn.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible; selected.RowHeight = Compact ? 58 : 40; };
        selected.Columns.Add(ActionColumn(108,
            IconButton(UpIcon, "cooking.up", (s, _) => { if (Item<Entry>(s) is { } entry) Move(entry, -1); }, nameof(Entry.CanUp)),
            IconButton(DownIcon, "cooking.down", (s, _) => { if (Item<Entry>(s) is { } entry) Move(entry, 1); }, nameof(Entry.CanDown)),
            IconButton(RemoveIcon, "cooking.remove_one", (s, _) => { if (Item<Entry>(s) is { } entry) Remove([entry]); }, null)));
        selected.ItemsSource = chosen;

        var contents = new FrameworkElement[] { columns, foodSales, materialSales };
        for (int i = 0; i < contents.Length; i++)
        { contents[i].Margin = new(0, 12, 0, 0); pages[i] = new TabItem { Content = contents[i] }; tabs.Items.Add(pages[i]); }
        layout.Children.Add(tabs); Content = layout;

        chosen.CollectionChanged += (_, _) => Refresh();
        foodSales.Changed += (_, _) => Refresh(); materialSales.Changed += (_, _) => Refresh();
        search.TextChanged += (_, _) => Filter();
        search.PreviewKeyDown += (_, e) => { if (e.Key is Key.Down or Key.Enter && available.Items.Count > 0) { FocusRow(available, 0); e.Handled = true; } };
        available.PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Enter && DailySalePanel.Ancestor<ButtonBase>(e.OriginalSource) == null) { AddSelected(); e.Handled = true; }
        };
        available.MouseDoubleClick += (_, e) =>
        {
            if (DailySalePanel.Ancestor<ButtonBase>(e.OriginalSource) == null && DailySalePanel.Ancestor<DataGridRow>(e.OriginalSource) is { Item: Recipe recipe })
                AddRecipes([recipe], chosen.Count);
        };
        selected.MouseDoubleClick += (_, e) =>
        {
            if (DailySalePanel.Ancestor<ButtonBase>(e.OriginalSource) == null && DailySalePanel.Ancestor<DataGridRow>(e.OriginalSource) is { Item: Entry entry })
                Remove([entry]);
        };
        selected.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Delete) { Remove(selected.SelectedItems.Cast<Entry>().ToArray()); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Up or Key.Down && selected.SelectedItems.Count == 1 && selected.SelectedItem is Entry entry)
            { Move(entry, key == Key.Up ? -1 : 1); e.Handled = true; }
        };

        // Drag from the catalog into a position, reorder within the order, or drag back to remove.
        line = new InsertionLine(selected);
        selected.Loaded += (_, _) => { if (!lineAttached && AdornerLayer.GetAdornerLayer(selected) is { } layer) { layer.Add(line); lineAttached = true; } };
        DragSource(available); DragSource(selected);
        selected.AllowDrop = available.AllowDrop = true;
        selected.DragOver += (_, e) =>
        {
            if (Payload(e) != null) { e.Effects = DragDropEffects.Move; line.Show(InsertY(e, out int _)); }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        selected.DragLeave += (_, _) => line.Show(null);
        selected.Drop += (_, e) =>
        {
            line.Show(null);
            if (Payload(e) is not { } payload) return;
            InsertY(e, out int index);
            if (payload.Item is Entry entry) MoveTo(entry, index); else AddRecipes(DraggedRecipes(payload), index);
            e.Handled = true;
        };
        available.DragOver += (_, e) => { e.Effects = Payload(e)?.Item is Entry ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        available.Drop += (_, e) => { if (Payload(e)?.Item is Entry entry) Remove(DraggedEntries(entry)); e.Handled = true; };

        save.IsEnabled = false;
        Refresh();
        Loaded += async (_, _) =>
        {
            try
            {
                var data = fixture ?? await Task.Run(() => DailyTradeCatalog.Read(DailyTradeData.CatalogPath(AppContext.BaseDirectory)));
                var outputs = new Dictionary<long, long>();
                foreach (var recipe in DailyCustomCooking.Recipes(data)) outputs.TryAdd(N(recipe["id"]), N(recipe["output"]));
                catalog = DailyCustomCooking.Choices(data, L.Code).Select(r => new Recipe(r.Id, r.Name, r.Materials, r.SearchText, outputs.GetValueOrDefault(r.Id)))
                    .OrderBy(r => r.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(L.Code), true)).ToArray();
                foreach (long id in current) chosen.Add(new Entry(catalog.FirstOrDefault(r => r.Id == id) ?? new(id, "#" + id, L["cooking.unavailable"])));
                foodSales.Load(data, currentSales ?? []); materialSales.Load(data, currentSales ?? []);
                loaded = true; save.IsEnabled = true; Filter(); search.Focus();
            }
            catch (Exception error) { status.SetResourceReference(TextBlock.ForegroundProperty, "Error"); DailyUiText.Error(status, error); }
        };
    }
    private void Save()
    {
        try
        {
            SalesResult = foodSales.Collect().Concat(materialSales.Collect()).ToArray();
            Result = chosen.Select(e => e.Id).ToArray(); DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            tabs.SelectedIndex = 2; tabs.UpdateLayout(); materialSales.RevealInvalid();
            status.SetResourceReference(TextBlock.ForegroundProperty, "Error"); DailyUiText.Error(status, error);
        }
        catch (Exception error) { status.SetResourceReference(TextBlock.ForegroundProperty, "Error"); DailyUiText.Error(status, error); }
    }
    private void Filter()
    {
        var ids = chosen.Select(e => e.Id).ToHashSet(); string term = search.Text.Trim();
        var rows = catalog.Where(r => !ids.Contains(r.Id) && (r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) || r.Materials.Contains(term, StringComparison.CurrentCultureIgnoreCase) || r.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        available.ItemsSource = rows;
        L.Bind(catalogEmpty, TextBlock.TextProperty, term.Length > 0 ? "cooking.no_match" : "cooking.catalog_done");
        catalogEmpty.Visibility = loaded && rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }
    // Positions, cross-page labels, tab counts and the footer summary follow every change.
    private void Refresh()
    {
        var sold = foodSales.CheckedIds;
        for (int i = 0; i < chosen.Count; i++)
        {
            var entry = chosen[i];
            entry.Position = i + 1; entry.CanUp = i > 0; entry.CanDown = i < chosen.Count - 1; entry.Sold = sold.Contains(entry.Recipe.Output);
        }
        foodSales.SetPlanned(chosen.Select(e => e.Recipe.Output).ToHashSet());
        orderEmpty.Visibility = loaded && chosen.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int[] counts = [chosen.Count, foodSales.CheckedCount, materialSales.CheckedCount];
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] == null) continue;
            string key = pageKeys[i]; int n = counts[i];
            L.Bind(pages[i], HeaderedContentControl.HeaderProperty, () => n > 0 ? L.Get("customsales.tab_count", L[key], n) : L[key]);
        }
        if (!loaded) return;
        bool invalid = materialSales.HasInvalid;
        int cook = counts[0], dishes = counts[1], ingredients = counts[2];
        status.SetResourceReference(TextBlock.ForegroundProperty, invalid ? "Error" : "MutedInk");
        BindingOperations.ClearBinding(status, ToolTipProperty);
        L.Bind(status, TextBlock.TextProperty, () => invalid ? L["customsales.invalid_inline"] : L.Get("customsales.summary", cook, dishes, ingredients));
    }
    private void AddSelected() => AddRecipes(available.SelectedItems.Cast<Recipe>().OrderBy(r => available.Items.IndexOf(r)).ToArray(), chosen.Count);
    private void AddRecipes(IEnumerable<Recipe> recipes, int index)
    {
        int focus = available.SelectedIndex;
        bool retainFocus = available.IsKeyboardFocusWithin;
        index = Math.Clamp(index, 0, chosen.Count);
        foreach (var recipe in recipes.ToArray())
            if (!chosen.Any(e => e.Id == recipe.Id)) chosen.Insert(index++, new Entry(recipe));
        Filter();
        // Keep the catalog cursor in place so several dishes can be added in a row.
        if (focus >= 0 && available.Items.Count > 0) {
            int next = Math.Min(focus, available.Items.Count - 1);
            if (retainFocus) FocusRow(available, next); else available.SelectedIndex = next;
        }
    }
    private void Remove(IEnumerable<Entry> entries)
    {
        int focus = selected.SelectedIndex;
        bool retainFocus = selected.IsKeyboardFocusWithin;
        foreach (var entry in entries.ToArray()) chosen.Remove(entry);
        Filter();
        if (focus >= 0 && chosen.Count > 0) {
            int next = Math.Min(focus, chosen.Count - 1);
            if (retainFocus) FocusRow(selected, next); else selected.SelectedIndex = next;
        }
    }
    private void Move(Entry entry, int delta) => MoveTo(entry, chosen.IndexOf(entry) + (delta > 0 ? delta + 1 : delta));
    // index is an insertion point in the current list, as a drop between rows reports it.
    private void MoveTo(Entry entry, int index)
    {
        int old = chosen.IndexOf(entry);
        if (old < 0) return;
        if (index > old) index--;
        index = Math.Clamp(index, 0, chosen.Count - 1);
        if (index != old) chosen.Move(old, index);
        selected.SelectedItem = entry; selected.ScrollIntoView(entry);
    }
    private void DragSource(DataGrid grid)
    {
        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = null;
            if (DailySalePanel.Ancestor<ButtonBase>(e.OriginalSource) != null || DailySalePanel.Ancestor<ScrollBar>(e.OriginalSource) != null) return;
            if (DailySalePanel.Ancestor<DataGridRow>(e.OriginalSource) is not { } row) return;
            dragStart = e.GetPosition(grid); dragItem = row.Item; dragSource = grid;
        };
        grid.PreviewMouseMove += (_, e) =>
        {
            if (dragStart is not Point start || dragSource != grid || dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(grid);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;
            DragDrop.DoDragDrop(grid, new DataObject(typeof(DragPayload), new DragPayload(grid, dragItem)), DragDropEffects.Move);
            line.Show(null);
        };
        grid.PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
    }
    private static DragPayload? Payload(DragEventArgs e) => e.Data.GetDataPresent(typeof(DragPayload)) ? e.Data.GetData(typeof(DragPayload)) as DragPayload : null;
    private Recipe[] DraggedRecipes(DragPayload payload)
    {
        if (payload.Source.SelectedItems.Contains(payload.Item) && payload.Source.SelectedItems.Count > 1)
            return payload.Source.SelectedItems.Cast<Recipe>().OrderBy(r => available.Items.IndexOf(r)).ToArray();
        return payload.Item is Recipe recipe ? new[] { recipe } : System.Array.Empty<Recipe>();
    }
    private Entry[] DraggedEntries(Entry entry)
    {
        if (selected.SelectedItems.Contains(entry) && selected.SelectedItems.Count > 1) return selected.SelectedItems.Cast<Entry>().ToArray();
        return new[] { entry };
    }
    private double? InsertY(DragEventArgs e, out int index)
    {
        index = chosen.Count;
        if (DailySalePanel.Ancestor<DataGridRow>(e.OriginalSource) is { Item: Entry entry } row)
        {
            bool after = e.GetPosition(row).Y > row.ActualHeight / 2;
            index = chosen.IndexOf(entry) + (after ? 1 : 0);
            return row.TranslatePoint(new Point(0, after ? row.ActualHeight : 0), selected).Y;
        }
        if (chosen.Count > 0 && selected.ItemContainerGenerator.ContainerFromItem(chosen[^1]) is DataGridRow last && last.IsVisible)
            return last.TranslatePoint(new Point(0, last.ActualHeight), selected).Y;
        return null;
    }
    private static void FocusRow(DataGrid grid, int index)
    {
        grid.SelectedIndex = index; grid.ScrollIntoView(grid.Items[index]); grid.UpdateLayout();
        if (grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row && DailySalePanel.Descendant<DataGridCell>(row) is { } cell) cell.Focus();
    }
    private static T? Item<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;
    private sealed class InsertionLine : Adorner
    {
        private double? y;
        public InsertionLine(UIElement owner) : base(owner) { IsHitTestVisible = false; }
        internal void Show(double? value) { if (y == value) return; y = value; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            if (y is not double at) return;
            var brush = TryFindResource("Primary") as Brush ?? Brushes.SeaGreen;
            drawing.DrawRectangle(brush, null, new Rect(4, at - 1, Math.Max(0, AdornedElement.RenderSize.Width - 8), 2));
        }
    }

    internal void VerifyForSmoke(string expectedName, string expectedMaterials)
    {
        if (!save.IsEnabled || catalog.Length < 3) throw new Exception("Recipe catalog did not load: " + status.ToolTip);
        var translated = catalog.Single(r => r.Id == 2);
        if (translated.Name != expectedName || translated.Materials != expectedMaterials) throw new Exception("Recipe or ingredient locale was lost");
        var old = chosen.Select(e => e.Recipe).ToArray(); chosen.Clear(); Filter();
        if (orderEmpty.Visibility != Visibility.Visible) throw new Exception("Empty cooking order lacks guidance");
        if (save.IsDefault) throw new Exception("Search Enter must not save the dialog");
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        search.RaiseEvent(down);
        if (!down.Handled || available.SelectedIndex != 0 || chosen.Count != 0) throw new Exception("Search Down did not enter the results");
        available.UpdateLayout();
        var second = (Recipe)available.Items[1];
        var secondRow = (DataGridRow)available.ItemContainerGenerator.ContainerFromItem(second);
        var plus = DailySalePanel.Descendant<Button>(secondRow)!;
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        plus.RaiseEvent(enter);
        if (enter.Handled || chosen.Count != 0) throw new Exception("Catalog Enter hijacked the focused row action");
        plus.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (chosen.Count != 1 || chosen[0].Id != second.Id) throw new Exception("Row action added a different selected recipe");
        chosen.Clear(); Filter();
        search.Text = "蜂蜜奶油杏仁";
        if (available.Items.Count != 1 || ((Recipe)available.Items[0]).Id != 2) throw new Exception("Cross-language recipe search lost identity");
        search.Text = "__no_such_recipe__";
        if (available.Items.Count != 0 || catalogEmpty.Visibility != Visibility.Visible) throw new Exception("Recipe search lacks an empty state");
        search.Clear();
        available.SelectedItems.Add(available.Items[0]); available.SelectedItems.Add(available.Items[1]); AddSelected();
        if (chosen.Count != 2 || available.Items.Count != catalog.Length - 2 || orderEmpty.Visibility == Visibility.Visible) throw new Exception("Recipe transfer duplicated or lost rows");
        if (chosen[0].Position != 1 || chosen[1].Position != 2 || chosen[0].CanUp || !chosen[0].CanDown || chosen[1].CanDown) throw new Exception("Cooking positions are wrong");
        var first = chosen[0]; Move(chosen[1], -1);
        if (chosen[1] != first || chosen[0].Position != 1 || chosen[1].Position != 2) throw new Exception("Recipe order did not move");
        var inserted = (Recipe)available.Items[0]; AddRecipes([inserted], 0);
        if (chosen.Count != 3 || chosen[0].Recipe != inserted) throw new Exception("Recipe insertion position was ignored");
        MoveTo(chosen[0], chosen.Count);
        if (chosen[^1].Recipe != inserted) throw new Exception("Recipe move to the end failed");
        long output = chosen[0].Recipe.Output;
        if (foodSales.ContainsForSmoke(output))
        {
            bool before = foodSales.CheckedForSmoke(output);
            if (!foodSales.PlannedForSmoke(output)) throw new Exception("Planned dish is not marked in dish sales");
            foodSales.CheckForSmoke(output, true);
            if (!chosen[0].Sold) throw new Exception("Sold dish is not marked in the cooking order");
            foodSales.CheckForSmoke(output, before);
        }
        Remove([chosen[0]]);
        if (chosen.Count != 2) throw new Exception("Recipe removal failed");
        if (pages[0].Header is not string header || !header.Contains('2')) throw new Exception("Tab count is missing");
        search.Text = "__no_such_recipe__";
        if (available.Items.Count != 0 || chosen.Count != 2) throw new Exception("Search altered the cooking order");
        search.Clear(); chosen.Clear(); foreach (var recipe in old) chosen.Add(new Entry(recipe)); Filter();
        foodSales.VerifyForSmoke(); materialSales.VerifyForSmoke();
        var restoreInvalid = materialSales.InvalidForSmoke();
        try {
            tabs.SelectedIndex = 0; Save();
            if (tabs.SelectedIndex != 2 || Result != null || SalesResult != null) throw new Exception("Save did not locate the invalid ingredient reserve");
        } finally { restoreInvalid(); tabs.SelectedIndex = 0; }
    }
    internal void ShowSaleForSmoke(int tab) { tabs.SelectedIndex = tab; UpdateLayout(); }
    internal void VerifySaleInputForSmoke(int tab) => (tab == 1 ? foodSales : materialSales).VerifyInputForSmoke();
    private static DataGrid Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
        CanUserReorderColumns = false, CanUserResizeRows = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
        HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, BorderThickness = new(1), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static DataGridTextColumn TextColumn(string header, string path, double star)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(MarginProperty, new Thickness(8, 0, 8, 0)));
        text.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        text.Setters.Add(new Setter(ToolTipProperty, new Binding("Text") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }));
        var column = new DataGridTextColumn { Binding = new Binding(path), Width = new(star, DataGridLengthUnitType.Star), ElementStyle = text };
        L.Bind(column, DataGridColumn.HeaderProperty, header);
        return column;
    }
    private static DataGridTemplateColumn PositionColumn()
    {
        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(Entry.Position)));
        number.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        number.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        number.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var column = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = number }, Width = 56, CanUserResize = false };
        L.Bind(column, DataGridColumn.HeaderProperty, "cooking.position");
        return column;
    }
    private static DataGridTemplateColumn ActionColumn(double width, params FrameworkElementFactory[] buttons)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        panel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        foreach (var button in buttons) panel.AppendChild(button);
        return new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = panel }, Width = width, CanUserResize = false };
    }
    private static FrameworkElementFactory IconButton(string geometry, string key, RoutedEventHandler click, string? enabledPath)
    {
        iconStyle ??= IconStyle();
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(StyleProperty, iconStyle);
        button.SetValue(ToolTipProperty, L[key]);
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Recipe.Name)) { StringFormat = L[key] + ": {0}" });
        if (enabledPath != null) button.SetBinding(IsEnabledProperty, new Binding(enabledPath));
        button.AddHandler(ButtonBase.ClickEvent, click);
        var data = Geometry.Parse(geometry); data.Freeze();
        var icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        icon.SetValue(StyleProperty, Application.Current.FindResource("LineIcon"));
        icon.SetValue(System.Windows.Shapes.Path.DataProperty, data);
        button.AppendChild(icon);
        return button;
    }
    private static Style IconStyle()
    {
        var style = new Style(typeof(Button), (Style)Application.Current.FindResource("QuietButton"));
        style.Setters.Add(new Setter(WidthProperty, 30.0));
        style.Setters.Add(new Setter(HeightProperty, 30.0));
        style.Setters.Add(new Setter(MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(1, 0, 1, 0)));
        return style;
    }
    private static TextBlock Heading(string key) { var text = Text(key); text.FontWeight = FontWeights.SemiBold; return text; }
    private static TextBlock Text(string key) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; L.Bind(text, TextBlock.TextProperty, key); return text; }
    private static Button Button(string key) { var b = new Button { Margin = new(0, 0, 8, 0), MinHeight = 36 }; L.Bind(b, ContentProperty, key); return b; }
}

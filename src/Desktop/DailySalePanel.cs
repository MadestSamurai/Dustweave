using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace Dustweave.Desktop;

internal sealed class DailySalePanel : DockPanel
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal sealed class Row : INotifyPropertyChanged, IDataErrorInfo
    {
        private bool enabled, planned;
        private string reserve;
        public Row(long id, string name, string search, bool enabled, long reserve)
        { Id = id; Name = name; Search = search; this.enabled = enabled; this.reserve = reserve.ToString(CultureInfo.InvariantCulture); }
        public long Id { get; }
        public string Name { get; }
        public string Search { get; }
        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                // An unchecked row cannot be edited, so it must not keep an unfixable invalid reserve.
                if (!value && !TryReserve(out _)) reserve = "0";
                Raise(nameof(Enabled)); Raise(nameof(Reserve));
            }
        }
        public string Reserve { get => reserve; set { value ??= ""; if (reserve == value) return; reserve = value; Raise(nameof(Reserve)); } }
        // Display only: the dish is in the current cooking order.
        public bool Planned { get => planned; set { if (planned == value) return; planned = value; Raise(nameof(Planned)); } }
        internal bool Invalid => enabled && !TryReserve(out _);
        internal bool TryReserve(out long value)
        {
            string text = reserve.Trim();
            if (text.Length == 0) { value = 0; return true; }
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= int.MaxValue;
        }
        string IDataErrorInfo.Error => "";
        string IDataErrorInfo.this[string column] => column == nameof(Reserve) && Invalid ? L["customsales.invalid_inline"] : "";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    private readonly DataGrid table = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
        CanUserSortColumns = false, CanUserReorderColumns = false, CanUserResizeRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, SelectionMode = DataGridSelectionMode.Single, BorderThickness = new(1),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBox search = new();
    private readonly CheckBox onlyChecked = new() { Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 8, 0) };
    private readonly Button clear = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock empty = Overlay();
    private Row[] rows = [];
    private readonly bool cooked;
    internal event EventHandler? Changed;
    internal int CheckedCount => rows.Count(r => r.Enabled);
    internal bool HasInvalid => rows.Any(r => r.Invalid);
    internal IReadOnlySet<long> CheckedIds => rows.Where(r => r.Enabled).Select(r => r.Id).ToHashSet();
    public DailySalePanel(bool cooked)
    {
        this.cooked = cooked;
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        help.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Bind(help, TextBlock.TextProperty, cooked ? "customsales.food_brief" : "customsales.material_brief");
        SetDock(help, Dock.Top); Children.Add(help);

        // Search, a checked-only view and the checked count share one toolbar line.
        var toolbar = new Grid { Margin = new(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star), MaxWidth = 340 });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(SearchField(search, "customsales.search"));
        L.Bind(onlyChecked, ContentControl.ContentProperty, "customsales.only_checked");
        count.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        clear.SetResourceReference(StyleProperty, "QuietButton");
        L.Bind(clear, ContentControl.ContentProperty, "customsales.clear");
        Grid.SetColumn(onlyChecked, 2); Grid.SetColumn(count, 3); Grid.SetColumn(clear, 4);
        toolbar.Children.Add(onlyChecked); toolbar.Children.Add(count); toolbar.Children.Add(clear);
        SetDock(toolbar, Dock.Top); Children.Add(toolbar);

        // Selection has no meaning here; only the check box and reserve decide a sale.
        StyleTable(table, selectable: false);
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Row.Enabled)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        check.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Row.Name)));
        var enabled = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = check }, Width = 84, CanUserResize = false };
        L.Bind(enabled, DataGridColumn.HeaderProperty, "customsales.sell"); table.Columns.Add(enabled);

        var nameCell = new FrameworkElementFactory(typeof(DockPanel));
        nameCell.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
        nameCell.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        if (cooked) nameCell.AppendChild(Badge("customsales.planned_tag", "customsales.planned_help", nameof(Row.Planned)));
        nameCell.AppendChild(NameText(nameof(Row.Name)));
        var name = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = nameCell }, Width = new(1, DataGridLengthUnitType.Star) };
        L.Bind(name, DataGridColumn.HeaderProperty, "customsales.item"); table.Columns.Add(name);
        if (!cooked)
        {
            var input = new FrameworkElementFactory(typeof(TextBox));
            input.SetValue(StyleProperty, ReserveStyle());
            input.SetValue(MarginProperty, new Thickness(10, 2, 10, 2));
            input.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            input.SetValue(TextBox.MaxLengthProperty, 10);
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Reserve)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnDataErrors = true });
            input.SetBinding(IsEnabledProperty, new Binding(nameof(Row.Enabled)));
            input.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Row.Name)) { StringFormat = L["customsales.reserve"] + ": {0}" });
            var header = new TextBlock();
            L.Bind(header, TextBlock.TextProperty, "customsales.reserve");
            L.Bind(header, ToolTipProperty, "customsales.reserve_help");
            table.Columns.Add(new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = input }, Width = 190, CanUserResize = false, Header = header });
        }
        Children.Add(new Grid { Children = { table, empty } });

        search.TextChanged += (_, _) => Filter();
        onlyChecked.Click += (_, _) => Filter();
        clear.Click += (_, _) => { foreach (var row in rows) row.Enabled = false; Filter(); };
        table.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || Ancestor<TextBox>(e.OriginalSource) != null || Ancestor<ToggleButton>(e.OriginalSource) != null || (table.CurrentItem ?? table.SelectedItem) is not Row row) return;
            row.Enabled = !row.Enabled; e.Handled = true;
        };
        Refresh();
    }
    internal void Load(JsonObject catalog, DailySalePreference[] current)
    {
        foreach (var row in rows) row.PropertyChanged -= RowChanged;
        var choices = DailyCustomSales.Choices(catalog, L.Code);
        var visible = choices.Where(c => c.Cooked == cooked).ToList();
        if (!cooked) foreach (var missing in current.Where(r => choices.All(c => c.Id != r.Item)))
            visible.Add(new(missing.Item, "#" + missing.Item + " · " + L["cooking.unavailable"], false, "#" + missing.Item));
        var names = StringComparer.Create(CultureInfo.GetCultureInfo(L.Code), true);
        // Checked items lead once, when the list opens; checking later never reorders rows.
        rows = visible.Select(c => {
            var saved = current.SingleOrDefault(r => r.Item == c.Id);
            return new Row(c.Id, c.Name, c.SearchText, saved?.Enabled ?? false, saved?.Reserve ?? 0);
        }).OrderByDescending(r => r.Enabled).ThenBy(r => r.Name, names).ToArray();
        foreach (var row in rows) row.PropertyChanged += RowChanged;
        Filter(); Refresh();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Row.Enabled) or nameof(Row.Reserve)) Refresh();
    }
    private void Refresh()
    {
        L.Bind(count, TextBlock.TextProperty, () => L.Get("customsales.checked_count", CheckedCount));
        clear.IsEnabled = CheckedCount > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Filter()
    {
        table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
        string term = search.Text.Trim(); bool only = onlyChecked.IsChecked == true;
        var visible = rows.Where(r => (!only || r.Enabled) && (r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) || r.Search.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        table.ItemsSource = visible;
        L.Bind(empty, TextBlock.TextProperty, only && term.Length == 0 ? "customsales.none_checked" : "customsales.no_match");
        empty.Visibility = visible.Length == 0 && rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void SetPlanned(IReadOnlySet<long> items) { foreach (var row in rows) row.Planned = items.Contains(row.Id); }
    internal DailySalePreference[] Collect()
    {
        table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
        var result = new List<DailySalePreference>();
        foreach (var row in rows)
        {
            if (!row.TryReserve(out long reserve))
            {
                if (!row.Enabled) continue;
                Reveal(row); throw new InvalidDataException("customsales.invalid");
            }
            if (row.Enabled || reserve > 0) result.Add(new(row.Id, row.Enabled, reserve));
        }
        return result.ToArray();
    }
    internal void RevealInvalid() { if (rows.FirstOrDefault(r => r.Invalid) is { } row) Reveal(row); }
    private void Reveal(Row row)
    {
        search.Clear(); onlyChecked.IsChecked = false; Filter();
        table.SelectedItem = row; table.ScrollIntoView(row); table.UpdateLayout();
        if (table.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow container && Descendant<TextBox>(container) is { } box)
        { box.Focus(); box.SelectAll(); }
    }

    // Shared presentation for the cooking and sale lists.
    internal static Grid SearchField(TextBox box, string key)
    {
        var field = new Grid();
        box.Margin = new(0); field.Children.Add(box);
        var hint = new TextBlock { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new(11, 0, 11, 0),
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        L.Bind(hint, TextBlock.TextProperty, key); field.Children.Add(hint);
        L.Bind(box, AutomationProperties.NameProperty, key);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return field;
    }
    internal static TextBlock Overlay()
    {
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(24, 72, 24, 0),
            MaxWidth = 340, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        return text;
    }
    internal static void StyleTable(DataGrid grid, bool selectable)
    {
        grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "Line");
        var cell = new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cell.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")));
        cell.Triggers.Add(selectedCell);
        grid.CellStyle = cell;
        var row = new Style(typeof(DataGridRow));
        row.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Surface")));
        row.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        row.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        row.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceMuted"))); row.Triggers.Add(hover);
        if (selectable)
        {
            var active = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            active.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("PrimarySoft"))); row.Triggers.Add(active);
        }
        var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Focus"))); row.Triggers.Add(focus);
        grid.RowStyle = row;
    }
    internal static FrameworkElementFactory NameText(string path)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        text.SetBinding(ToolTipProperty, new Binding(path));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        return text;
    }
    // A quiet label that links the cooking order and the dish sale list.
    internal static FrameworkElementFactory Badge(string key, string help, string visiblePath)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(DockProperty, Dock.Right);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new Thickness(6, 1, 6, 1));
        border.SetValue(MarginProperty, new Thickness(8, 0, 0, 0));
        border.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.SetResourceReference(Border.BackgroundProperty, "PrimarySoft");
        border.SetValue(ToolTipProperty, L[help]);
        border.SetBinding(VisibilityProperty, new Binding(visiblePath) { Converter = new BooleanToVisibilityConverter() });
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, L[key]);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        border.AppendChild(text);
        return border;
    }
    internal static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    internal static T? Ancestor<T>(object? source) where T : DependencyObject
    {
        var node = source as DependencyObject;
        while (node != null && node is not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
    private static Style ReserveStyle()
    {
        var style = new Style(typeof(TextBox), (Style)Application.Current.FindResource(typeof(TextBox)));
        // The shared field template draws the error border; suppress the default red adorner.
        style.Setters.Add(new Setter(Validation.ErrorTemplateProperty, null));
        style.Setters.Add(new Setter(ToolTipProperty, L["customsales.reserve_help"]));
        var invalid = new Trigger { Property = Validation.HasErrorProperty, Value = true };
        invalid.Setters.Add(new Setter(ToolTipProperty, new Binding { RelativeSource = RelativeSource.Self, Path = new PropertyPath("(0)[0].ErrorContent", Validation.ErrorsProperty) }));
        style.Triggers.Add(invalid);
        return style;
    }

    internal bool ContainsForSmoke(long id) => rows.Any(r => r.Id == id);
    internal bool PlannedForSmoke(long id) => rows.Any(r => r.Id == id && r.Planned);
    internal bool CheckedForSmoke(long id) => rows.Any(r => r.Id == id && r.Enabled);
    internal void CheckForSmoke(long id, bool value) { foreach (var row in rows.Where(r => r.Id == id)) row.Enabled = value; }
    internal Action InvalidForSmoke()
    {
        var row=rows[0]; bool enabled=row.Enabled; string reserve=row.Reserve;
        row.Enabled=true; row.Reserve="-1";
        return ()=> { row.Enabled=enabled; row.Reserve=reserve; };
    }
    internal void VerifyInputForSmoke()
    {
        var row=rows[0]; var original=rows.Select(r=>(r.Enabled,r.Reserve)).ToArray();
        try
        {
            row.Enabled=true; Reveal(row);
            var container=(DataGridRow)table.ItemContainerGenerator.ContainerFromItem(row);
            if (!cooked)
            {
                var box=Descendant<TextBox>(container) ?? throw new Exception("Reserve input was not rendered");
                box.Text="-1"; box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                if(!Validation.GetHasError(box)||!row.Invalid)throw new Exception("Actual reserve input accepts an invalid value");
                try { Collect(); throw new Exception("Invalid visible input saved"); } catch(InvalidDataException) { }
                // Revealing a validation error refreshes the view; use its current controls.
                container=(DataGridRow)table.ItemContainerGenerator.ContainerFromItem(row);
                box=Descendant<TextBox>(container)!;
                var check=Descendant<CheckBox>(container)!;
                check.IsChecked=false; check.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
                if(row.Enabled||box.IsEnabled||row.Reserve!="0"||Validation.GetHasError(box))throw new Exception("Disabling sale did not clear an invalid visible reserve");
                check.IsChecked=true; check.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
                box.Text=""; box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                if(!Collect().Any(r=>r.Item==row.Id&&r.Enabled&&r.Reserve==0)||Validation.GetHasError(box))throw new Exception("Blank visible reserve does not save zero");
            }
            onlyChecked.IsChecked=true; Filter();
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(CheckedCount!=0||table.Items.Count!=0||empty.Visibility!=Visibility.Visible)throw new Exception("Clear checks leaves a stale checked-only view");
        }
        finally { for(int i=0;i<rows.Length;i++){rows[i].Enabled=original[i].Enabled;rows[i].Reserve=original[i].Reserve;} onlyChecked.IsChecked=false; Filter(); }
    }
    internal void VerifyForSmoke()
    {
        if (rows.Length == 0) throw new Exception("Sale list is empty");
        var original = rows.Select(r => (r.Enabled, r.Reserve)).ToArray();
        try
        {
            var first = rows[0];
            first.Enabled = true; first.Reserve = cooked ? "0" : "123";
            if (!count.Text.Contains(CheckedCount.ToString(CultureInfo.CurrentCulture)) || !clear.IsEnabled) throw new Exception("Checked sale count is not shown");
            search.Text = "__not_visible__";
            if (table.Items.Count != 0 || empty.Visibility != Visibility.Visible) throw new Exception("Sale search lacks an empty state");
            if (Collect().All(r => r.Item != first.Id || !r.Enabled || r.Reserve != (cooked ? 0 : 123))) throw new Exception("Filtering changed saved sale choices");
            search.Clear();
            onlyChecked.IsChecked = true; Filter();
            if (table.Items.Cast<Row>().Any(r => !r.Enabled) || !table.Items.Contains(first)) throw new Exception("Checked-only view is wrong");
            first.Enabled = false;
            if (!table.Items.Contains(first)) throw new Exception("Unchecking moved a row out of view");
            onlyChecked.IsChecked = false; Filter();
            if (!cooked)
            {
                first.Enabled = true; first.Reserve = " ";
                if (!Collect().Any(r => r.Item == first.Id && r.Enabled && r.Reserve == 0)) throw new Exception("Blank reserve is not zero");
                first.Reserve = "-1";
                if (!HasInvalid || ((IDataErrorInfo)first)[nameof(Row.Reserve)].Length == 0) throw new Exception("Invalid reserve is not marked");
                try { Collect(); throw new Exception("Invalid sale reserve accepted"); } catch (InvalidDataException) { }
                first.Enabled = false;
                if (HasInvalid || first.Reserve != "0") throw new Exception("Unchecked row kept an unfixable reserve");
            }
        }
        finally
        {
            for (int i = 0; i < rows.Length; i++) { rows[i].Enabled = original[i].Enabled; rows[i].Reserve = original[i].Reserve; }
            onlyChecked.IsChecked = false; search.Clear(); Filter();
        }
    }
}

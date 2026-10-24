using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Dustweave.DailyData;

namespace Dustweave.Desktop;

// Uses the same collection catalog as execution. Transit maps, buildings and
// hunting grounds are deliberately not choices. No game connection is needed.
internal sealed class DailyCollectionMapPicker : UserControl
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal sealed record MapChoice(long Pack, long Map, DailyDisplayName PackName, DailyDisplayName MapName);
    private readonly Expander expander = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel groups = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) };
    private readonly Dictionary<long, CheckBox> mapChecks = [];
    private readonly Dictionary<long, CheckBox> packChecks = [];
    private readonly Dictionary<long, TextBlock> packSummaries = [];
    private MapChoice[]? catalog;
    private long[]? selection;
    private bool refreshing, loading;
    public event EventHandler? Changed;
    public long[]? Selection => selection?.ToArray();

    public DailyCollectionMapPicker()
    {
        Margin = new(0, 8, 0, 4);
        expander.SetResourceReference(StyleProperty, "DisclosureExpander");
        expander.Header = summary;
        L.Bind(expander, System.Windows.Automation.AutomationProperties.NameProperty, () => L.Get("prefs.maps_title"));
        var body = new StackPanel { Margin = new(0, 6, 0, 0) };
        foreach (var (key, all) in new[] { ("prefs.maps_all", true), ("prefs.maps_none", false) })
        {
            var button = new Button { Margin = new(0, 0, 8, 0) };
            L.Bind(button, ContentControl.ContentProperty, () => L.Get(key));
            button.Click += (_, _) => { SetSelection(all ? null : []); Changed?.Invoke(this, EventArgs.Empty); };
            actions.Children.Add(button);
        }
        actions.IsEnabled = false;
        body.Children.Add(actions);
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        status.Margin = new(0, 0, 0, 8);
        body.Children.Add(status);
        body.Children.Add(new ScrollViewer { Content = groups, MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        expander.Content = body;
        expander.Expanded += async (_, _) => await LoadAsync();
        Content = expander;
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        Refresh();
    }

    public void SetSelection(long[]? value)
    {
        selection = value?.ToArray();
        Refresh();
    }

    private void LanguageChanged(object? sender, EventArgs args)
    {
        if (catalog != null) Render();
        else Refresh();
    }

    internal static MapChoice[] ReadCatalog(string directory)
    {
        var catalog = new DailyCollectionCatalog(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "flows", "collection-catalog.json")))!.AsObject(), new JsonObject { ["packs"] = new JsonArray() });
        JsonObject[] Table(string name) => Rows(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "data", "locations", "locations", name + ".json"))));
        var maps = Table("MapTable");
        var labels = DailyLocationLabels.Build(Table("PackTable"), maps, Table("NameTextTable"));
        var available = maps.Select(m => (Pack: N(m["packId"]), Map: N(m["id"]))).ToHashSet();
        return catalog.Packs().SelectMany(p => catalog.Maps(p).Where(m => available.Contains((p, m)))
            .Where(m => labels.ContainsKey("p" + p) && labels.ContainsKey("m" + m))
            .Select(m => new MapChoice(p, m, labels["p" + p], labels["m" + m]))).ToArray();
    }

    private async Task LoadAsync()
    {
        if (catalog != null || loading) return;
        loading = true;
        status.Text = L.Get("prefs.maps_loading");
        try
        {
            var choices = await Task.Run(() => ReadCatalog(AppContext.BaseDirectory));
            if (choices.Length == 0) throw new InvalidDataException("Empty collection map catalog");
            catalog = choices;
            Render();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            // Keep the previous account selection. A missing package file must
            // never save an empty selection or trigger component preparation.
            status.Text = L.Get("prefs.maps_unavailable");
        }
        finally { loading = false; }
    }

    private void Render()
    {
        var expanded = groups.Children.OfType<Expander>().Where(e => e.IsExpanded).Select(e => (long)e.Tag).ToHashSet();
        groups.Children.Clear(); mapChecks.Clear(); packChecks.Clear(); packSummaries.Clear();
        foreach (var pack in catalog!.GroupBy(m => m.Pack))
        {
            var toggle = new CheckBox { Content = new TextBlock { Text = pack.First().PackName.In(L.Code), TextWrapping = TextWrapping.Wrap },
                FontWeight = FontWeights.SemiBold, Margin = new(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) => ChangeMaps(pack.Select(m => m.Map), toggle.IsChecked == true);
            packChecks.Add(pack.Key, toggle);
            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
            packSummaries.Add(pack.Key, count);
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(count, 1);
            heading.Children.Add(toggle); heading.Children.Add(count);
            var list = new StackPanel { Margin = new(24, 4, 0, 8) };
            foreach (var map in pack)
            {
                var check = new CheckBox { Content = new TextBlock { Text = map.MapName.In(L.Code), TextWrapping = TextWrapping.Wrap },
                    Margin = new(0, 5, 8, 5), ToolTip = map.PackName.In(L.Code) + " · " + map.MapName.In(L.Code) };
                check.Click += (_, _) => ChangeMaps([map.Map], check.IsChecked == true);
                mapChecks.Add(map.Map, check); list.Children.Add(check);
            }
            var group = new Expander { Header = heading, Content = list, Margin = new(0, 2, 0, 2), Tag = pack.Key, IsExpanded = expanded.Contains(pack.Key),
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            group.SetResourceReference(StyleProperty, "SettingsExpander");
            System.Windows.Automation.AutomationProperties.SetName(group, pack.First().PackName.In(L.Code));
            groups.Children.Add(group);
        }
        actions.IsEnabled = true;
        Refresh();
    }

    private void ChangeMaps(IEnumerable<long> maps, bool enabled)
    {
        if (refreshing || catalog == null) return;
        var selected = (selection ?? catalog.Select(m => m.Map)).ToHashSet();
        if (enabled) selected.UnionWith(maps); else selected.ExceptWith(maps);
        // Only the explicit All action restores future-map inclusion.
        selection = selected.Order().ToArray();
        Refresh(); Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            bool Selected(long id) => selection == null || selection.Contains(id);
            int count = catalog?.Count(m => Selected(m.Map)) ?? selection?.Length ?? 0;
            summary.Text = selection == null ? L.Get("prefs.maps_default") : L.Get("prefs.maps_selected", count);
            if (catalog == null) return;
            status.Text = count == 0 ? L.Get("prefs.maps_empty") : L.Get("prefs.maps_hint");
            foreach (var pair in mapChecks) pair.Value.IsChecked = Selected(pair.Key);
            foreach (var pack in catalog.GroupBy(m => m.Pack))
            {
                int n = pack.Count(m => Selected(m.Map));
                packChecks[pack.Key].IsChecked = n == pack.Count() ? true : n == 0 ? false : null;
                packSummaries[pack.Key].Text = $"{n}/{pack.Count()}";
            }
        }
        finally { refreshing = false; }
    }

    internal void CheckForSmoke(MapChoice[] choices)
    {
        var original = Selection;
        catalog = choices; Render();
        SetSelection(null);
        ChangeMaps([choices[0].Map], false);
        if (Selection == null || Selection.Contains(choices[0].Map) || Selection.Length != choices.Length - 1)
            throw new Exception("Map deselection did not preserve other maps");
        SetSelection([]);
        if (Selection is not { Length: 0 } || mapChecks.Values.Any(c => c.IsChecked != false))
            throw new Exception("Empty collection map selection became the default");
        ChangeMaps(choices.Where(m => m.Pack == choices[0].Pack).Select(m => m.Map), true);
        if (!Selection!.Order().SequenceEqual(choices.Where(m => m.Pack == choices[0].Pack).Select(m => m.Map).Order()))
            throw new Exception("Cartridge toggle crossed cartridge boundaries");
        SetSelection(original);
        expander.IsExpanded = true;
        if (groups.Children[0] is Expander first) first.IsExpanded = true;
    }
}

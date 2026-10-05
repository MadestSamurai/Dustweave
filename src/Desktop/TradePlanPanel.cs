using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace BD2Daily.Desktop;

/// <summary>File planning and opt-in read-only capture from an existing live connection.</summary>
public sealed class TradePlanPanel : UserControl
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly bool allowGame;
    private readonly string root;
    private readonly string? expectedAccount;
    private readonly TextBlock identity = Text("在游戏广场商店的购买页读取当前账号，或导入已保存的快照。"), warning = Text(""), status = Text("仅预览计划，不执行采购、料理或出售。");
    private readonly TextBlock totals = Text("导入后可查看预计增益、垫资和回款日期。");
    private readonly TextBox reserve = new() { Text = "0", Width = 130 }, maximum = new() { Text = "", Width = 130 };
    private readonly Button capture = new() { Content = "读取当前游戏" }, import = new() { Content = "导入离线快照" }, compute = new() { Content = "计算计划", IsEnabled = false }, export = new() { Content = "导出明细", IsEnabled = false };
    private readonly Button saveSettings = new() { Content = "保存资金设置" };
    private readonly DataGrid buys = Grid(), cooks = Grid(), today = Grid(), future = Grid();
    private JsonElement? snapshot;
    private string? output, scope;
    private Dictionary<string, long> itemReserves = new();
    private bool busy;
    private string? preparedDirectory;
    public bool PlanReady => export.IsEnabled;
    public TradePlanPanel(string dataRoot, string? account = null, bool allowGame = true)
    {
        root = dataRoot;
        this.allowGame = allowGame;
        capture.IsEnabled = allowGame;
        if (!allowGame) L.Bind(capture, FrameworkElement.ToolTipProperty, "preview.no_read");
        foreach (var button in new[] { capture, import, compute, export, saveSettings }) {
            string source = (string)button.Content;
            L.Bind(button, ContentControl.ContentProperty, () => L.Translate(source));
        }
        expectedAccount = account;
        var layout = new DockPanel { Margin = new(20) };
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var title = Text("跑商计划"); title.FontSize = 23; title.FontWeight = FontWeights.SemiBold; title.Margin = new(0,0,0,8); heading.Children.Add(title);
        heading.Children.Add(identity);
        heading.Children.Add(warning);
        warning.Visibility = Visibility.Collapsed;
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        heading.Children.Add(Text("按当前供货统一分配原料和料理。回款约需30天；尚未售出的收入不会计入采购预算。"));
        var settings = new WrapPanel { Margin = new(0, 8, 0, 10) };
        AddField(settings, "保留金币", reserve);
        AddField(settings, "本轮最多垫资", maximum);
        settings.Children.Add(Text("留空表示不限额"));
        heading.Children.Add(settings);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 10) };
        foreach (var button in new[] { capture, import, compute, export, saveSettings })
        {
            button.Margin = new(0, 0, 10, 0);
            actions.Children.Add(button);
        }
        compute.Style = (Style)Application.Current.FindResource("PrimaryButton");
        heading.Children.Add(actions);
        heading.Children.Add(new Border { Child = totals, Padding = new(12, 8, 12, 8), CornerRadius = new(6), Background = (Brush)Application.Current.FindResource("SurfaceMuted") });
        heading.Children.Add(status);
        var tabs = new TabControl { Margin = new(0, 12, 0, 0), BorderThickness = new(0), Background = (Brush)Application.Current.FindResource("Surface"), BorderBrush = (Brush)Application.Current.FindResource("Line") };
        foreach (var (label, grid) in new[] { ("采购", buys), ("料理分配", cooks), ("今日待售", today), ("后续售卖", future) })
            { var tab = new TabItem { Content = grid }; L.Bind(tab, HeaderedContentControl.HeaderProperty, () => L.Translate(label)); tabs.Items.Add(tab); }
        layout.Children.Add(tabs);
        Content = layout;
        capture.Click += async (_, _) => await Capture();
        import.Click += (_, _) => Import();
        compute.Click += async (_, _) => await Compute();
        export.Click += (_, _) => Export();
        saveSettings.Click += (_, _) => { try { SaveSettings(); L.Literal(status, "本账号资金设置已保存，下次开始跑商时生效；没有执行交易。"); } catch (Exception ex) { DailyUiText.Error(status, ex); } };
        if (expectedAccount != null && DailyProfiles.ValidKey(expectedAccount))
        {
            scope = Path.Combine(root, "trade", "plans", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedAccount))).ToLowerInvariant());
            ReadSettings();
        }
        saveSettings.IsEnabled = scope != null;
        reserve.TextChanged += (_, _) => Changed();
        maximum.TextChanged += (_, _) => Changed();
    }
    private void ReadSettings()
    {
        var path = Path.Combine(scope!, "settings.json");
        itemReserves = new();
        reserve.Text = "0";
        maximum.Text = "";
        if (!File.Exists(path))
            return;
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        var s = saved.RootElement;
        reserve.Text = s.GetProperty("reserve_gold").GetInt64().ToString();
        maximum.Text = s.GetProperty("max_spend").ValueKind == JsonValueKind.Null ? "" : s.GetProperty("max_spend").GetInt64().ToString();
        foreach (var entry in s.GetProperty("reserve_items").EnumerateObject())
            itemReserves[entry.Name] = entry.Value.GetInt64();
    }
    private void SaveSettings()
    {
        if (busy || scope == null)
            throw new InvalidOperationException("请先选择账号。");
        var keep = Amount(reserve);
        long? max = maximum.Text.Trim().Length == 0 ? null : Amount(maximum);
        Directory.CreateDirectory(scope);
        DailyJson.Write(Path.Combine(scope, "settings.json"), new
        {
            enabled = false,
            reserve_gold = keep,
            max_spend = max,
            reserve_items = itemReserves
        });
    }
    private static TextBlock Text(string value)
    { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 5) }; L.Literal(text, value); return text; }
    private static DataGrid Grid()
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
            RowHeight = 34,
            ColumnHeaderHeight = 36,
            Background = (Brush)Application.Current.FindResource("Surface"),
            BorderBrush = (Brush)Application.Current.FindResource("Line"),
            HorizontalGridLinesBrush = (Brush)Application.Current.FindResource("Line"),
            AlternatingRowBackground = (Brush)Application.Current.FindResource("SurfaceMuted")
        };
        grid.AutoGeneratingColumn += (_, e) =>
        {
            if (e.PropertyName == "今日报价已确认" || e.PropertyName == "商品")
            {
                e.Cancel = true;
                return;
            }
            L.Bind(e.Column, DataGridColumn.HeaderProperty, () => L.Translate(e.PropertyName));
            bool label = e.PropertyName is "物品" or "料理";
            e.Column.Width = label ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(e.PropertyName == "预计售日" ? 140 : 100);
            e.Column.MinWidth = label ? 180 : 65;
            if (e.Column is DataGridTextColumn text)
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(10, 0, 10, 0)));
                style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
                text.ElementStyle = style;
            }
        };
        return grid;
    }
    private static void AddField(Panel row, string label, Control control)
    {
        var name = Text(label); name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new(0, 0, 8, 0); row.Children.Add(name);
        control.Margin = new(0, 0, 18, 0);
        row.Children.Add(control);
    }
    private void Changed()
    {
        if (snapshot != null && !busy)
        {
            export.IsEnabled = false;
            L.Literal(status, "设置已变更，请重新计算。");
        }
    }
    private void ClearResult()
    {
        output = null;
        export.IsEnabled = false;
        buys.ItemsSource = cooks.ItemsSource = today.ItemsSource = future.ItemsSource = null;
        L.Literal(totals, "尚未计算。");
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Title = L.Get("trade.import_title"), Filter = "JSON 快照|*.json" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            LoadSnapshot(dialog.FileName);
        }
        catch (Exception ex) { snapshot = null; compute.IsEnabled = false; ClearResult(); DailyUiText.Error(status, ex, "无法导入："); }
    }
    public void LoadSnapshot(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var value = doc.RootElement;
        if (value.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException("快照版本不支持。");
        var context = value.GetProperty("context");
        string key = context.GetProperty("account").GetString() ?? "";
        if (key.Length == 0 || (expectedAccount != null && key != expectedAccount))
            throw new InvalidDataException("快照与所选账号不一致。");
        foreach (var field in new[] { "inventory_complete", "offers_complete", "recipes_complete" })
            if (!value.GetProperty(field).GetBoolean())
                throw new InvalidDataException("快照尚未完整采集。");
        snapshot = value.Clone();
        scope = Path.Combine(root, "trade", "plans", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
        Directory.CreateDirectory(scope);
        ClearResult();
        string player = context.TryGetProperty("player_name", out var displayName) ? displayName.GetString() ?? "当前账号" : "账号 " + key[..Math.Min(8, key.Length)];
        L.Text(identity, "trade.identity", player, value.GetProperty("game_date").GetString(), value.GetProperty("captured_utc").GetString());
        long gold = value.GetProperty("gold").GetInt64();
        L.Text(warning, "trade.low_gold");
        warning.Visibility = gold < 10_000_000 ? Visibility.Visible : Visibility.Collapsed;
        ReadSettings();
        saveSettings.IsEnabled = true;
        compute.IsEnabled = true;
        L.Text(status, "trade.loaded", gold);
    }
    public async Task Capture()
    {
        if (!allowGame) { L.Text(status, "preview.no_read"); return; }
        if (busy)
            return;
        string temporary = Path.Combine(root, "trade", "capture", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            busy = true;
            capture.IsEnabled = import.IsEnabled = compute.IsEnabled = export.IsEnabled = reserve.IsEnabled = maximum.IsEnabled = false;
            L.Literal(status, "正在读取已连接游戏的库存和商店；不会购买、制作或出售。");
            var game = new DailyGameHost(root).Find() ?? throw new InvalidOperationException("游戏未运行。");
            string account = expectedAccount ?? DailyJson.TryRead<DailySnapshot>(Path.Combine(root, "snapshot.json"))?.AccountKey ?? "";
            var observation = DailyStageObservation.Attach(root, account);
            var before = await observation.ReadFrameAsync();
            var data = await DailyTradeData.PrepareAsync(AppContext.BaseDirectory, root, game, before, () => false, _ => { }, allowCompleted: false);
            data.AssertReady();
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(before.Context, (await observation.ReadFrameAsync()).Context))
                throw new InvalidOperationException("准备期间游戏身份变化；没有读取旧账号库存。");
            preparedDirectory = data.Directory;
            using var driver = new DailyCommandDriver(root, new DailyPipeMailbox(root, game), observation.ReadFrameAsync, () => false);
            driver.EnsureBound(before.Context);
            driver.Acquire("live");
            var navigation = new DailyStageNavigation(observation.ReadFrameAsync, () => false);
            var proofs = DailyWorkflowRegistry.Proofs();
            var business = new DailyManagedBusiness(root, driver, proofs, () => false);
            var workflow = new DailyWorkflow(root, AppContext.BaseDirectory, before.Context, driver, business, navigation, proofs, () => false, (_, _, _) => throw new InvalidOperationException("只读采集不可执行导航。"));
            await new DailyTradeExecution(workflow, data.VerifiedCatalog ?? throw new InvalidDataException("缺少已校验的跑商规则。")).Capture(temporary);
            LoadSnapshot(temporary);
        }
        catch (Exception ex) { DailyUiText.Error(status, ex, "无法读取："); return; }
        finally { busy = false; import.IsEnabled = reserve.IsEnabled = maximum.IsEnabled = true; capture.IsEnabled = allowGame; compute.IsEnabled = snapshot != null; }
        await Compute();
    }
    private static long Amount(TextBox control)
    {
        if (!long.TryParse(control.Text.Trim().Replace(",", ""), out var value) || value < 0 || value > 1_000_000_000_000)
            throw new InvalidDataException("金币设置须为0至一万亿之间的整数；原输入已保留。");
        return value;
    }
    public async Task Compute()
    {
        if (busy || snapshot == null || scope == null)
            return;
        try
        {
            var keep = Amount(reserve);
            long? max = maximum.Text.Trim().Length == 0 ? null : Amount(maximum);
            var catalog = DailyTradeCatalog.Read(DailyTradeData.CatalogPath(preparedDirectory ?? AppContext.BaseDirectory));
            busy = true;
            capture.IsEnabled = import.IsEnabled = compute.IsEnabled = export.IsEnabled = reserve.IsEnabled = maximum.IsEnabled = false;
            L.Literal(status, "正在计算原料、料理与资金的共同最优分配……");
            ClearResult();
            string input = Path.Combine(scope, "snapshot.json"), settings = Path.Combine(scope, "settings.json");
            output = Path.Combine(scope, "latest.json");
            using var lease = new FileStream(Path.Combine(scope, "planning.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.WriteAllText(input, snapshot.Value.GetRawText());
            File.WriteAllText(settings, JsonSerializer.Serialize(new
            {
                enabled = false,
                reserve_gold = keep,
                max_spend = max,
                reserve_items = itemReserves
            }));
            var state = System.Text.Json.Nodes.JsonNode.Parse(snapshot.Value.GetRawText())!.AsObject();
            var preferences = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
            string account = snapshot.Value.GetProperty("context").GetProperty("account").GetString()!;
            await Task.Run(() => DailyTradePlan.Generate(catalog, state, preferences, output, account));
            Display(output);
            export.IsEnabled = true;
        }
        catch (Exception ex) { DailyUiText.Error(status, ex, "未生成新计划："); }
        finally { busy = false; import.IsEnabled = reserve.IsEnabled = maximum.IsEnabled = true; capture.IsEnabled = allowGame; compute.IsEnabled = snapshot != null; }
    }
    public void Display(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var p = doc.RootElement;
        var s = p.GetProperty("summary");
        if (snapshot != null && p.GetProperty("context").GetProperty("account").GetString() != snapshot.Value.GetProperty("context").GetProperty("account").GetString())
            throw new InvalidDataException("计划账号不匹配。");
        L.Text(totals, "trade.totals", s.GetProperty("incremental_profit").GetInt64(), s.GetProperty("cash_required").GetInt64(), s.GetProperty("eventual_sale_value").GetInt64(), s.GetProperty("cash_remaining").GetInt64(), s.GetProperty("potions_used").GetInt64(), s.GetProperty("potions_to_buy").GetInt64());
        buys.ItemsSource = p.GetProperty("purchases").EnumerateArray().Select(r => new { 物品 = r.GetProperty("name").GetString(), 数量 = r.GetProperty("count").GetInt64(), 单价 = r.GetProperty("price").GetInt64(), 金额 = r.GetProperty("cost").GetInt64(), 商店 = r.GetProperty("shop").GetInt32(), 商品 = r.GetProperty("product").GetInt32() }).ToArray();
        cooks.ItemsSource = p.GetProperty("cooking").EnumerateArray().Select(r => new { 料理 = r.GetProperty("name").GetString(), 份数 = r.GetProperty("count").GetInt64(), 天赋药 = r.GetProperty("potions").GetInt64(), 预计售日 = r.GetProperty("sale_date").GetString() }).ToArray();
        var sales = p.GetProperty("sales").EnumerateArray().Select(r => new { 物品 = r.GetProperty("name").GetString(), 数量 = r.GetProperty("count").GetInt64(), 单价 = r.GetProperty("price").GetInt64(), 商店 = r.GetProperty("shop").GetInt32(), 预计售日 = r.GetProperty("date").GetString(), 今日报价已确认 = r.GetProperty("today_quote_confirmed").GetBoolean() }).ToArray();
        today.ItemsSource = sales.Where(r => r.今日报价已确认).ToArray();
        future.ItemsSource = sales.Where(r => !r.今日报价已确认).ToArray();
        bool optimal = p.GetProperty("solver").GetProperty("optimal").GetBoolean();
        L.Bind(status, TextBlock.TextProperty, () => L.Get(optimal ? "trade.optimal" : "trade.best") + " " + L.Get("trade.quantity_note"));
        output = path;
    }
    internal Action LanguageProbeForSmoke()
    {
        reserve.Text = "12345"; maximum.Text = "invalid input retained";
        var rows = buys.ItemsSource;
        string value = totals.Text;
        string code = L.Code;
        return () => {
            if (reserve.Text != "12345" || maximum.Text != "invalid input retained" || !ReferenceEquals(rows, buys.ItemsSource))
                throw new Exception("Locale change reset trade budget input or plan rows");
            if (capture.IsEnabled || allowGame) throw new Exception("Preview can access the real game");
            if (L.Code != code && totals.Text == value) throw new Exception("Trade totals were not localized");
            if (!Equals(buys.Columns[0].Header, L.Get("trade.item"))) throw new Exception("Trade column label was not localized");
        };
    }
    private void Export()
    {
        if (output == null || !File.Exists(Path.ChangeExtension(output, ".md")))
            return;
        var dialog = new SaveFileDialog { Title = L.Get("trade.export_title"), FileName = L.Code == "en-US" ? "trade-plan.md" : "跑商计划.md", Filter = "Markdown 明细|*.md" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            File.Copy(Path.ChangeExtension(output, ".md"), dialog.FileName, true);
            L.Literal(status, "已导出完整采购、料理和售卖日期明细。");
        }
        catch (Exception ex) { DailyUiText.Error(status, ex, "导出失败："); }
    }
}

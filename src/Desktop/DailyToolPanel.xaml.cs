using System.Windows;
using System.Windows.Controls;

namespace BD2Daily.Desktop;

public sealed record ToolMenuRow(string Id, string Name, string Detail, string Description, string Action, bool Enabled)
{
    public string AccessibleName => Action + " · " + Name;
}
public partial class DailyToolPanel : UserControl
{
    private static DailyLanguage L => DailyLanguage.Current;
    private sealed class ToolCategory(string key, string source, string textKey) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Key => key;
        public string Source => source;
        public string Label => L.Get(textKey);
        public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    private string? active;
    private bool busy, running;
    public event Action<string>? OpenRequested;
    public event Action? CloseRequested;
    public IReadOnlyList<string> VisibleTools => ((ToolMenuRow[])ToolsList.ItemsSource).Select(t => t.Id).ToArray();
    public DailyToolPanel()
    {
        InitializeComponent();
        Category.DisplayMemberPath = "Label"; Category.SelectedValuePath = "Key";
        Category.ItemsSource = new ToolCategory[] { new("all", "", "tools.all"), new("games", "小游戏", "tools.category_games"), new("business", "经营与装备", "tools.category_business"), new("draws", "抽取", "tools.category_draws") };
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        Category.SelectedIndex = 0;
        Refresh(false, null, "工具共用日常助手的连接，不会自动开始操作。仅在自动化启用或等待结算时占用控制权。");
    }
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (ToolsList == null) return;
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }
    private void OpenClicked(object sender, RoutedEventArgs e)
    {
        if (!busy && sender is Button { Tag: string id } && (!running || id==active)) OpenRequested?.Invoke(id);
    }
    private void CloseClicked(object sender, RoutedEventArgs e) { if (!busy) CloseRequested?.Invoke(); }
    private void Render()
    {
        var category = Category.SelectedItem as ToolCategory;
        string query = SearchBox.Text.Trim();
        var rows = DailyToolCatalog.All.Where(t => query.Length == 0 || (t.Name + " " + t.Repository + " " + L.Get("tool." + t.Id) + " " + L.Get("tool." + t.Id + ".description")).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(t => category == null || category.Key == "all" || t.Category == category.Source)
            .Select(t => new ToolMenuRow(t.Id, L.Get("tool." + t.Id), L.Translate(t.Category) + " · " + ToolApplicationHost.Version(t), L.Get("tool." + t.Id + ".description"),
                L.Get(t.Id == active ? "tools.show" : active != null ? "tools.switch" : "tools.open"), !busy && (!running || t.Id==active))).ToArray();
        ToolsList.ItemsSource = rows;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CloseToolButton.Visibility = active == null ? Visibility.Collapsed : Visibility.Visible;
        CloseToolButton.IsEnabled = !busy;
    }
    public void Refresh(bool busy, string? active, string message, bool running=false)
    {
        bool changed = this.busy != busy || this.running != running || this.active != active || ToolsList.ItemsSource == null;
        this.busy = busy; this.running=running; this.active = active; DailyUiText.Set(Status, message);
        if (changed) Render();
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in (ToolCategory[])Category.ItemsSource) item.Refresh();
        Render();
    }
    internal (Action Verify, Action Restore) BeginLanguageProbeForSmoke()
    {
        var originalCategory = Category.SelectedValue; string originalSearch = SearchBox.Text;
        Category.SelectedValue = "games"; SearchBox.Text = "secret";
        return (() => {
            if ((string)Category.SelectedValue != "games" || SearchBox.Text != "secret" || !VisibleTools.SequenceEqual(new[] { "secret-vision" }))
                throw new Exception("Locale change lost the tool category or search");
            if (((ToolMenuRow[])ToolsList.ItemsSource).Single().Name != L.Get("tool.secret-vision"))
                throw new Exception("Tool labels did not update");
        }, () => { Category.SelectedValue = originalCategory; SearchBox.Text = originalSearch; });
    }
    internal void VerifyFiltersForSmoke()
    {
        if (VisibleTools.Count != 9) throw new InvalidOperationException("工具清单缺失");
        SearchBox.Text = "连连看"; if (!VisibleTools.SequenceEqual(new[] { "sichuan" })) throw new InvalidOperationException("中文搜索失败");
        SearchBox.Text = "secret"; if (!VisibleTools.SequenceEqual(new[] { "secret-vision" })) throw new InvalidOperationException("英文搜索失败");
        SearchBox.Text = ""; Category.SelectedValue = "games"; if (VisibleTools.Count != 5) throw new InvalidOperationException("工具分类错误");
        Category.SelectedIndex = 0;
        Refresh(true, null, "日常正在执行，停止后可以打开工具。");
        if (((ToolMenuRow[])ToolsList.ItemsSource).Any(t => t.Enabled)) throw new InvalidOperationException("日常执行时仍可启动工具");
        Refresh(false, "sichuan", "连连看窗口已打开");
        if (((ToolMenuRow[])ToolsList.ItemsSource).Single(t => t.Id == "sichuan").Action != "显示窗口") throw new InvalidOperationException("工具状态未更新");
        Refresh(false,"sichuan","自动化已启用",true);
        if(((ToolMenuRow[])ToolsList.ItemsSource).Any(t=>t.Id!="sichuan"&&t.Enabled))throw new InvalidOperationException("其他自动化仍可启动");
        if(!CloseToolButton.IsEnabled)throw new InvalidOperationException("运行中不能正常停止关闭");
        Refresh(false,"sichuan","窗口闲置");
        if(((ToolMenuRow[])ToolsList.ItemsSource).Any(t=>!t.Enabled))throw new InvalidOperationException("闲置窗口仍占用其他工具");
        Refresh(false, null, "工具共用日常助手的连接，不会自动开始操作。仅在自动化启用或等待结算时占用控制权。");
    }
}

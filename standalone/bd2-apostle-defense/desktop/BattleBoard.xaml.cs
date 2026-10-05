using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WPoint = System.Windows.Point;
using GamePoint = BD2ApostleDefense.Point;

namespace BD2ApostleDefense.Desktop;

// Read-only inspection. No client port or game commands belong in this control.
public partial class BattleBoard : UserControl
{
 private Snapshot? state;
 private Decision? action;
 private string geometry = "", contents = "", match = "", selectionKey = "";
 private Planner coverage = new();
 private readonly Dictionary<int, Button> tiles = new();
 private int? selectedGrid, selectedInstance;
 private double scale = 1, midX, midZ, tileSize = 40;
 private bool fresh = true;
 private static readonly string[] Fills = { "#E8F1FC", "#FCECE6", "#E7F3EA", "#FCF3D6", "#F0EAF9" };
 private static readonly string[] Inks = { "#234E7E", "#8B3923", "#245D38", "#755409", "#654186" };
 internal int TileCount => tiles.Count;
 internal int? SelectedGrid => selectedGrid;
 internal string SelectedTitle => SelectionTitle.Text;
 internal IReadOnlyList<Rect> TileBounds => tiles.Values.Select(b => new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.Width, b.Height)).ToArray();
 internal Size MapSize => new(Viewport.ActualWidth, Viewport.ActualHeight);
 internal double TileSize => tileSize;
 internal bool HasRange => RangeLayer.Children.Count > 0;
 internal bool HasAction => ActionText.Visibility == Visibility.Visible;
 internal int EnemyMarkers => LiveLayer.Children.OfType<Ellipse>().Count();
 public BattleBoard() { InitializeComponent(); }
 private static SolidColorBrush Paint(string value)
 { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }
 private WPoint Map(GamePoint p) => new(Viewport.ActualWidth / 2 + (p.X - midX) * scale, Viewport.ActualHeight / 2 - (p.Z - midZ) * scale);
 private Unit? Occupant(int grid) => state?.Units.FirstOrDefault(u => u.Grid == grid);
 private UnitDef? Definition(Unit? u) => u == null ? null : state?.Catalog.Units.FirstOrDefault(d => d.Id == u.Id);
 private static string Label(UnitDef d) => string.IsNullOrWhiteSpace(d.Name) ? (Ui.English?$"{Ui.Text(Names.Element(d.Element))} unit · Tier {d.Grade}":$"{Names.Element(d.Element)}属性使徒 · {d.Grade}阶") : d.Name;
 private int Level(int element) => state != null && element >= 0 && element < state.Levels.Length ? state.Levels[element] : 0;

 public void RefreshLanguage() { geometry="";contents="";selectionKey="";if(state!=null)Update(state,action); }
 public void Update(Snapshot s, Decision? pending = null)
 {
  string session = $"{s.ProcessId}|{s.ProcessStart}|{s.Account}|{s.Room}";
  if (match != session) { selectedGrid = selectedInstance = null; match = session; }
  state = s; action = pending; SetFresh(true);
  // Geometry and catalog can change without a unit mutation. Do not rely on BoardKey alone.
  string newGeometry = string.Join(";", s.Boards.Cast<GamePoint>().Concat(s.Path).Select(p => $"{p.Id}:{p.X:R}:{p.Y:R}:{p.Z:R}"));
  string newContents = Guards.BoardKey(s) + "|" + s.MyView + "|" + string.Join(";", s.Catalog.Units.Select(d => $"{d.Id}:{d.Element}:{d.Grade}:{d.Range:R}:{d.Name}"));
  bool redraw = geometry != newGeometry || contents != newContents;
  if (geometry != newGeometry) coverage = new();
  geometry = newGeometry; contents = newContents;
  if (selectedInstance is int index)
  {
   var followed = s.Units.FirstOrDefault(u => u.Index == index);
   if (followed != null) selectedGrid = followed.Grid;
   else { selectedGrid = selectedInstance = null; } // A sold unit must not silently become its replacement.
  }
  if (selectedGrid is int id && !s.Boards.Any(b => b.Id == id)) selectedGrid = null;
  Levels.Text = "元素升级：" + string.Join("    ", Enumerable.Range(0, 5).Select(i => $"{Names.Element(i)} {Level(i)}"));
  CountText.Text = s.MyView ? $"使徒 {s.Units.Length} / {s.Boards.Length}" : "等待我的盘面";
  EmptyState.Visibility = s.Boards.Length == 0 || !s.MyView ? Visibility.Visible : Visibility.Collapsed;
  EmptyTitle.Text = !s.MyView && s.Boards.Length > 0 ? "当前不是我的盘面" : "等待游戏盘面";
  EmptyHint.Text = !s.MyView && s.Boards.Length > 0 ? "切回我的棋盘后继续显示，避免把对手的布阵当作自己的。" : "连接游戏并进入对局后，这里会显示每个使徒的位置、属性与阶数。";
  if (redraw) RenderBoard(); else { RenderSelection(); RenderLive(); }
 }
 public void SetFresh(bool value)
 {
  fresh = value;
  StaleBanner.Visibility = !value && tiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  if (!value) { LiveLayer.Children.Clear(); ActionText.Visibility = Visibility.Hidden; }
 }
 private void ViewportChanged(object sender, SizeChangedEventArgs e) => RenderBoard();
 private void OptionsChanged(object sender, RoutedEventArgs e) { if (IsInitialized) RenderBoard(); }
 private void RenderBoard()
 {
  int? focused = tiles.FirstOrDefault(x => x.Value.IsKeyboardFocused).Value?.Tag as int?;
  RouteLayer.Children.Clear(); TileLayer.Children.Clear(); RangeLayer.Children.Clear(); LiveLayer.Children.Clear(); tiles.Clear(); selectionKey = "";
  if (state == null || !state.MyView || state.Boards.Length == 0 || Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1)
  { RenderSelection(); ActionText.Visibility = Visibility.Hidden; return; }
  // One world-unit scale on both axes keeps cells, path and range consistent.
  double spacing = state.Boards.Length < 2 ? 1 : state.Boards.Min(a => state.Boards.Where(b => b.Id != a.Id).Min(b => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2))));
  spacing = Math.Max(.01, spacing);
  var bounds = state.Boards.Select(b => (p: (GamePoint)b, margin: spacing / 2)).Concat(state.Path.Select(p => (p, margin: spacing * .15))).ToArray();
  double left = bounds.Min(b => b.p.X - b.margin), right = bounds.Max(b => b.p.X + b.margin), bottom = bounds.Min(b => b.p.Z - b.margin), top = bounds.Max(b => b.p.Z + b.margin);
  midX = (left + right) / 2; midZ = (bottom + top) / 2;
  scale = Math.Max(.01, Math.Min(64, Math.Min((Viewport.ActualWidth - 24) / Math.Max(spacing * 2, right - left), (Viewport.ActualHeight - 24) / Math.Max(spacing * 2, top - bottom))));
  tileSize = Math.Min(58, spacing * scale * .9);
  if (state.Path.Length > 1)
  {
   var route = new Polyline { Stroke = Paint("#DEE4E8"), StrokeThickness = Math.Clamp(scale * .32, 8, 16), StrokeLineJoin = PenLineJoin.Round };
   foreach (var p in state.Path.Concat(new[] { state.Path[0] })) route.Points.Add(Map(p));
   RouteLayer.Children.Add(route);
  }
  foreach (var b in state.Boards.OrderByDescending(b => Math.Round(b.Z, 2)).ThenBy(b => b.X))
  {
   var u = Occupant(b.Id); var d = Definition(u); int el = Math.Clamp(d?.Element ?? 0, 0, 4);
   double cov = d == null ? 0 : coverage.Coverage(d, b, state.Path);
   var label = d == null ? (u == null ? Ui.Text("空") : "?") : Ui.English ? $"{Ui.ShortElement(d.Element)}{d.Grade}" : $"{Names.Element(d.Element)}·{d.Grade}";
   var content = new Grid();
   content.Children.Add(new TextBlock { Text = b.Id.ToString(), FontSize = Math.Clamp(tileSize * .22, 9, 11), Foreground = Paint(d == null ? "#5B6670" : Inks[el]), Margin = new(4, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
   content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = Ui.English ? Math.Clamp(tileSize * .28, 9, 16) : Math.Clamp(tileSize * .36, 12, 20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, Math.Max(2, tileSize * .10)), Foreground = Paint(d == null ? "#65717A" : Inks[el]) });
   if (d != null && state.Path.Length > 1 && cov <= 1e-6 && GapToggle.IsChecked == true)
    content.Children.Add(new TextBlock { Text = "!", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Paint("#8B5600"), Margin = new(0, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
   var tile = new Button { Style = (Style)FindResource("BoardTile"), Width = tileSize, Height = tileSize, Tag = b.Id, Content = content, Background = Paint(d == null ? "#F2F5F7" : Fills[el]), BorderBrush = Paint("#BBC9D1") };
   string description = Ui.English ? (d != null ? $"Cell {b.Id}, {Label(d)}, unit #{u!.Index}, range {d.Range:0.##}, estimated route coverage {cov:P0}" : $"Cell {b.Id}, {(u==null?"empty":"unit details pending")}") : d != null ? $"格 {b.Id}，{Label(d)}，实例 {u!.Index}，射程 {d.Range:0.##}，路线覆盖估计 {cov:P0}" : u == null ? $"格 {b.Id}，空位" : $"格 {b.Id}，使徒资料待同步";
   AutomationProperties.SetName(tile, description); tile.ToolTip = description; ToolTipService.SetInitialShowDelay(tile, 350);
   tile.Click += (_, _) => SelectGrid(b.Id); tile.PreviewKeyDown += Navigate;
   var at = Map(b); Canvas.SetLeft(tile, at.X - tileSize / 2); Canvas.SetTop(tile, at.Y - tileSize / 2); TileLayer.Children.Add(tile); tiles.Add(b.Id, tile);
  }
  RenderSelection(); RenderLive();
  if (focused is int focus && tiles.TryGetValue(focus, out var target)) target.Focus();
 }
 internal void SelectGrid(int id)
 {
  if (state == null || !state.Boards.Any(b => b.Id == id)) return;
  selectedGrid = id; selectedInstance = Occupant(id)?.Index; RenderSelection();
 }
 private void Navigate(object sender, KeyEventArgs e)
 {
  if (state == null || sender is not Button tile || tile.Tag is not int id) return;
  if (e.Key == Key.Escape) { selectedGrid = selectedInstance = null; RenderSelection(); e.Handled = true; return; }
  if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
  int dx = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0, dz = e.Key == Key.Up ? 1 : e.Key == Key.Down ? -1 : 0;
  var origin = state.Boards.First(b => b.Id == id);
  var next = state.Boards.Where(b => (b.X - origin.X) * dx + (b.Z - origin.Z) * dz > .1)
   .OrderBy(b => Math.Abs((b.X - origin.X) * dz - (b.Z - origin.Z) * dx) * 10 + Planner.Distance(b, origin)).FirstOrDefault();
  if (next != null) { SelectGrid(next.Id); tiles[next.Id].Focus(); } e.Handled = true;
 }
 private void RenderSelection()
 {
  string key = $"{contents}|{selectedGrid}|{selectedInstance}|{RangeToggle.IsChecked}";
  if (selectionKey == key) return;
  selectionKey = key;
  RangeLayer.Children.Clear();
  foreach (var tile in tiles) { tile.Value.BorderThickness = new(tile.Key == selectedGrid ? 3 : 1); tile.Value.BorderBrush = Paint(tile.Key == selectedGrid ? "#176B57" : "#BBC9D1"); }
  var b = state?.MyView == true ? state.Boards.FirstOrDefault(b => b.Id == selectedGrid) : null;
  var u = b == null ? null : Occupant(b.Id); var d = Definition(u);
  if (b == null) { SelectionTitle.Text = Ui.Text("点选一个使徒"); SelectionIdentity.Text = ""; SelectionStats.Text = "在盘面上查看射程，了解它能攻击哪段路线。"; CoverageText.Text = "射程与覆盖为位置示意；覆盖比例不是伤害占比。"; CoverageText.Foreground = Paint("#5B6670"); return; }
  SelectionTitle.Text = d == null ? Ui.Text(u == null ? "空位" : "使徒资料待同步") : Label(d);
  SelectionIdentity.Text = $"格 {b.Id}" + (u == null ? "" : $" · 实例 {u.Index}");
  if (d == null) { SelectionStats.Text = u == null ? "召唤由游戏填充空格；点击盘面只查看信息。" : "已识别占位，正在等待使徒属性。"; CoverageText.Text = ""; return; }
  SelectionStats.Text = $"攻击 {d.Attack + (double)d.UpAttack * Level(d.Element):N0}    攻击间隔 {d.Interval:0.##}s    射程 {d.Range:0.##}    出售价 {d.Sell}";
  double cov = coverage.Coverage(d, b, state!.Path);
  CoverageText.Text = state.Path.Length < 2 ? "等待路线数据，暂不判断覆盖。" : cov <= 1e-6 ? "! 当前射程覆盖不到路线；覆盖为位置估计，不代表伤害占比。" : $"路线覆盖约 {cov:P0} · 绿色路段位于射程内，覆盖比例不是伤害占比。";
  CoverageText.Foreground = Paint(state.Path.Length > 1 && cov <= 1e-6 ? "#8B5600" : "#5B6670");
  if (RangeToggle.IsChecked != true || tiles.Count == 0) return;
  var center = Map(b); double radius = Math.Max(0, d.Range * scale);
  var disk = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Paint("#176B57"), StrokeThickness = 1.5, StrokeDashArray = new() { 4, 3 }, Fill = Paint("#10176B57") };
  Canvas.SetLeft(disk, center.X - radius); Canvas.SetTop(disk, center.Y - radius); RangeLayer.Children.Add(disk);
  for (int i = 0; i < state.Path.Length; i++)
  {
   var a = state.Path[i]; var z = state.Path[(i + 1) % state.Path.Length]; int steps = Math.Max(8, (int)Math.Ceiling(Planner.Distance(a, z) * 12));
   GamePoint Along(double t) => new() { X = a.X + (z.X - a.X) * t, Y = a.Y + (z.Y - a.Y) * t, Z = a.Z + (z.Z - a.Z) * t };
   for (int n = 0; n < steps; n++) if (Planner.Distance(Along((n + .5) / steps), b) <= d.Range)
   { var start = Map(Along((double)n / steps)); var end = Map(Along((double)(n + 1) / steps)); RangeLayer.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = Paint("#176B57"), StrokeThickness = 6 }); }
  }
 }
 private void RenderLive()
 {
  LiveLayer.Children.Clear(); ActionText.Visibility = Visibility.Hidden;
  if (state == null || !state.MyView || tiles.Count == 0 || !fresh) return;
  foreach (var enemy in state.Enemies.Where(e => e.Hp > 0))
  { var at = Map(enemy); var dot = new Ellipse { Width = 7, Height = 7, Fill = Paint("#963B47"), Stroke = Brushes.White, StrokeThickness = 1 }; Canvas.SetLeft(dot, at.X - 3.5); Canvas.SetTop(dot, at.Y - 3.5); LiveLayer.Children.Add(dot); }
  if (action?.Kind != "move") return;
  var source = state.Boards.FirstOrDefault(b => b.Id == action.Grid); var destination = state.Boards.FirstOrDefault(b => b.Id == action.TargetGrid);
  if (source == null || destination == null) return;
  var a = Map(source); var z = Map(destination); var vector = z - a; if (vector.Length < 1) return; vector.Normalize(); var normal = new Vector(-vector.Y, vector.X); z -= vector * (tileSize / 2 + 3); a += vector * (tileSize / 2 + 2);
  LiveLayer.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = z.X, Y2 = z.Y, Stroke = Brushes.White, StrokeThickness = 7 });
  LiveLayer.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = z.X, Y2 = z.Y, Stroke = Paint("#176B57"), StrokeThickness = 3 });
  LiveLayer.Children.Add(new Polygon { Points = new() { z, z - vector * 10 + normal * 5, z - vector * 10 - normal * 5 }, Fill = Paint("#176B57") });
  ActionText.Text = (action.Policy==BossChase.Policy?"BOSS追击 · ":"")+$"正在{(action.TargetIndex >= 0 ? "交换" : "移动")}：格 {action.Grid} → 格 {action.TargetGrid} · 等待游戏确认"; ActionText.Visibility = Visibility.Visible;
 }
}

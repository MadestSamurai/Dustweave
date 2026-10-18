using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BD2ApostleDefense.Desktop;
public partial class MainWindow
{
 private static void Require(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
 private async Task SettleLayout()
 { UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout(); }
 private void Capture(string name)
 {
  Directory.CreateDirectory(root);
  var visual=(FrameworkElement)Content;
  var drawing=new DrawingVisual();
  using(var dc=drawing.RenderOpen())
  {dc.DrawRectangle((Brush)FindResource("AppBackground"),null,new Rect(0,0,visual.ActualWidth+36,visual.ActualHeight+36));dc.DrawRectangle(new VisualBrush(visual),null,new Rect(18,18,visual.ActualWidth,visual.ActualHeight));}
  var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth+36),(int)Math.Ceiling(visual.ActualHeight+36),96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(root,name));encoder.Save(f);
 }
 private void CheckBoardBounds()
 {
  var rects=BoardView.TileBounds;var bounds=new Rect(new System.Windows.Point(),BoardView.MapSize);
  Require(rects.Count==68,"Not all 68 board cells rendered");
  Require(BoardView.TileSize>=28,$"Board cells unreadable: {BoardView.TileSize:0.0}, viewport {BoardView.MapSize}, window {ActualWidth}x{ActualHeight}");
  Require(rects.All(bounds.Contains),"A board cell was clipped");
  for(int i=0;i<rects.Count;i++)for(int j=i+1;j<rects.Count;j++)Require(!rects[i].IntersectsWith(rects[j]),"Board cells overlap");
 }
 private async Task BoardSmoke()
 {
  var original=JsonFiles.Clone(snapshot!);original.MyView=true;BoardView.Update(original);await SettleLayout();
  // Exercise real routed click and key events instead of only calling selection helpers.
  var tile=(Button)BoardView.TileLayer.Children[0];tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  Require(BoardView.SelectedGrid==(int)tile.Tag&&BoardView.SelectedTitle.Length>0&&BoardView.HasRange,"Click did not inspect the selected unit");
  foreach(var (w,h,file) in new[]{(1220,900,"board-default.png"),(1000,760,"board-minimum.png"),(1540,1000,"board-wide.png")})
  {Width=w;Height=h;await SettleLayout();Capture(file);CheckBoardBounds();}
  Width=1220;Height=900;await SettleLayout();
  var selected=original.Units.First(u=>u.Grid==BoardView.SelectedGrid);var next=original.Boards.First(b=>b.Id!=selected.Grid);
  var moved=JsonFiles.Clone(original);var other=moved.Units.FirstOrDefault(u=>u.Grid==next.Id);if(other!=null)other.Grid=selected.Grid;moved.Units.First(u=>u.Index==selected.Index).Grid=next.Id;
  BoardView.Update(moved);Require(BoardView.SelectedGrid==next.Id,"Selection failed to follow moved instance");
  moved.Units=moved.Units.Where(u=>u.Index!=selected.Index).ToArray();BoardView.Update(moved);Require(BoardView.SelectedGrid==null,"Sold instance selection silently switched to replacement");
  var scene=DemoPort.BoardDemo();BoardView.Update(scene);BoardView.SelectGrid(29);
  Require(BoardView.SelectedTitle.Contains("使徒"),"Missing localization leaves blank title");
  BoardView.RangeToggle.IsChecked=false;Require(!BoardView.HasRange,"Range toggle failed");BoardView.RangeToggle.IsChecked=true;Require(BoardView.HasRange,"Range toggle failed to restore");
  BoardView.GapToggle.IsChecked=false;BoardView.GapToggle.IsChecked=true;
  var move=new Decision{Kind="move",Grid=29,TargetGrid=1,TargetIndex=1};BoardView.Update(scene,move);await SettleLayout();Require(BoardView.HasAction,"Pending move is invisible");Capture("board-action.png");
  var before=BoardView.EnemyMarkers;scene.Enemies=scene.Enemies.Take(2).ToArray();BoardView.Update(scene,move);Require(before>2&&BoardView.EnemyMarkers==2,"Enemy markers stale when unit layout unchanged");
  BoardView.SetFresh(false);Require(!BoardView.HasAction&&BoardView.EnemyMarkers==0,"Stale board claims live action");BoardView.Update(scene);
  var keyTile=(Button)BoardView.TileLayer.Children[0];keyTile.Focus();keyTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));int? from=BoardView.SelectedGrid;
  var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(keyTile),0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent};keyTile.RaiseEvent(key);Require(key.Handled&&BoardView.SelectedGrid!=from,"Directional keyboard navigation failed");
  scene.MyView=false;BoardView.Update(scene);Require(BoardView.TileCount==0&&!BoardView.HasRange,"Spectator board presented as own");
  scene=DemoPort.BoardDemo();scene.Boards=Array.Empty<Board>();scene.Units=Array.Empty<Unit>();scene.Path=Array.Empty<BD2ApostleDefense.Point>();BoardView.Update(scene);Require(BoardView.TileCount==0&&!BoardView.HasAction,"Empty scene retained obsolete cells");
  Capture("board-empty.png");BoardView.Update(original);BoardView.SelectGrid(original.Units.OrderBy(u=>u.Grid).First().Grid);await SettleLayout();
 }
}

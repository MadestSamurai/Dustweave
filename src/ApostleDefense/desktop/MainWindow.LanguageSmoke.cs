using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BD2ApostleDefense.Localization;
namespace BD2ApostleDefense.Desktop;
public partial class MainWindow
{
 private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
 {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;}
 private async Task LanguageSmoke()
 {
  var original=JsonFiles.Clone(snapshot!);snapshot!.Name="水風玩家";
  controller.Start(snapshot,ReadSettings(),DateTime.UtcNow.Ticks);
  string settingsBefore=File.ReadAllText(Path.Combine(root,"settings.json"));
  var demo=(DemoPort)port;string commandBefore=JsonSerializer.Serialize(demo.LastControl,JsonFiles.Options);
  Interval.Text="bad-input";
  LanguageChoice.SelectedIndex=1;await SettleLayout();
  Require(language.Catalog.Language=="en-US"&&ConnectButton.Content.ToString()=="Connect game"&&ClearGoal.Content.ToString()=="Clear wave 50","English static labels missing");
  Require(controller.Running&&JsonSerializer.Serialize(demo.LastControl,JsonFiles.Options)==commandBefore,"Language switch altered live controller command");
  Require(Interval.Text=="bad-input"&&File.ReadAllText(Path.Combine(root,"settings.json"))==settingsBefore,"Language switch altered input or settings");
  Require(LanguagePreference.Read(root)=="en-US","Language preference did not persist");
  Require(AccountText.Text.StartsWith("水風玩家")&&!AccountText.Text.Contains("组件"),"Game account name was translated");
  DecisionText.Text="BOSS追击：保持当前输出，监测离开射程的时机";
  Require(DecisionText.Text=="Boss pursuit: holding position; watching range","Dynamic decision not translated");
  var scene=DemoPort.BoardDemo();var selected=scene.Units.First();scene.Catalog.Units.First(d=>d.Id==selected.Id).Name="水風使徒";
  BoardView.Update(scene);BoardView.SelectGrid(selected.Grid);await SettleLayout();
  Require(BoardView.SelectedTitle=="水風使徒","Game unit name was translated");
  foreach(var (w,h,file) in new[]{(1220,900,"english-default.png"),(1000,760,"english-minimum.png")})
  {
   Width=w;Height=h;await SettleLayout();CheckBoardBounds();
   foreach(var tile in BoardView.TileLayer.Children.OfType<Button>())
   foreach(var label in Visuals(tile).OfType<TextBlock>())
   {var text=new FormattedText(label.Text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,Brushes.Black,1);Require(text.Width<=tile.Width,"English tile label clipped");}
   Capture(file);
  }
  Width=1220;Height=900;await SettleLayout();int tracked=language.TrackedControlCount;
  for(int i=0;i<12;i++){BoardView.RefreshLanguage();await SettleLayout();}
  Require(language.TrackedControlCount<=tracked+20,"Board rebuild leaked translation listeners");
  LanguageChoice.SelectedIndex=0;await SettleLayout();
  Require(ConnectButton.Content.ToString()=="连接游戏"&&DecisionText.Text=="BOSS追击：保持当前输出，监测离开射程的时机","Chinese roundtrip lost source text");
  Require(JsonSerializer.Serialize(demo.LastControl,JsonFiles.Options)==commandBefore,"Roundtrip changed command");
  controller.Stop();Interval.Text="500";SettingsChanged(null,new());snapshot=original;BoardView.Update(original);RenderAccount();
 }
}

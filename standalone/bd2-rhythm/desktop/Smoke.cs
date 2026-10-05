using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BD2Rhythm.Localization;
namespace BD2Rhythm.Desktop;
public partial class RhythmWindow {
 public async Task SmokeAsync(string output){
  timer.Stop();TestTransport.Start(root,RhythmIdentity.LiveEntries);var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  LanguageBox.SelectedIndex=0;Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
  Check(JitterBox.Text=="50"&&OffsetBox.Text=="0","default jitter and calibration");Check(!StartButton.IsEnabled,"no stale start");
  var s=new RhythmSnapshot{ProcessId=12345,CapturedUtcTicks=DateTime.UtcNow.Ticks,State="waiting"};TestTransport.Publish(Path.Combine(root,"latest.json"),s);Refresh();Check(StartButton.IsEnabled,"ready start");
  StartClick(this,new());Check(link.Enabled,"arm");var command=RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!;Check(command.Valid(DateTime.UtcNow.Ticks,12345)&&command.JitterMs==50,"lease and default");
  JitterBox.Text="-1";Check(!ApplySettings()&&JitterBox.Text=="-1","invalid input preserved");JitterBox.Text="0";Check(ApplySettings(),"disable jitter");JitterBox.Text="50";ApplySettings();
  s.State="playing";s.Song="Synthetic demo · Hard";s.ClockMs=53120;s.DurationMs=129000;s.Sent=145;s.Total=390;s.Reason="按谱面演奏中";s.LastErrorMs=-24;
  TestTransport.Publish(Path.Combine(root,"latest.json"),s);Refresh();UpdateLayout();Check(StateText.Text=="正在演奏"&&SongText.Text.Contains("Hard"),"Chinese playing status");Capture("window-zh-CN.png");
  string owner=link.OwnerId;LanguageBox.SelectedIndex=1;UpdateLayout();
  Check(StateText.Text=="Playing"&&ReasonText.Text=="Playing the song chart","English dynamic status");
  Check((string)ConnectButton.Content=="Connect"&&(string)StartButton.Content=="Enable auto-play","English actions");
  Check(LanguagePreference.Read(root)=="en-US"&&link.Enabled&&link.OwnerId==owner,"persist language without resetting playback");
  Check(JitterBox.Text=="50"&&OffsetBox.Text=="0","switch retains settings");Capture("window-en-US.png");
  s.State="paused";s.Reason="游戏暂停或倒计时，等待继续";TestTransport.Publish(Path.Combine(root,"latest.json"),s);Refresh();Check(link.Enabled&&StopButton.IsEnabled&&StateText.Text=="Game paused","pause preserves enabled stop");
  Width=600;Height=460;UpdateLayout();Scroll.ScrollToBottom();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();Capture("window-small-en-US.png");Check(Scroll.ScrollableHeight>0,"small window scroll");
  LanguageBox.SelectedIndex=0;UpdateLayout();Capture("window-small-zh-CN.png");Check(StateText.Text=="游戏已暂停","switch back");
  StopClick(this,new());Check(!RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!.Enabled,"stop lease");
  s.Runtime="BD2Rhythm.Runtime2";TestTransport.Publish(Path.Combine(root,"latest.json"),s);Refresh();Check(!StartButton.IsEnabled,"reject previous runtime");
  Close();Check(!RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!.Enabled,"close lease");RhythmJson.Write(Path.Combine(output,"results.json"),new{status="pass",checks});
  void Capture(string file){var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,file));encoder.Save(stream);}
 }
}

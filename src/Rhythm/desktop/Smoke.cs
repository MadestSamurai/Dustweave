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
  string ActionLabel(System.Windows.Controls.Button button,string standalone,string cn,string tw,string en){
   var host=System.Windows.Data.BindingOperations.GetBinding(button,System.Windows.Controls.ContentControl.ContentProperty)?.Source;
   return host?.GetType().GetProperty("Code")?.GetValue(host) as string switch {"zh-CN"=>cn,"zh-TW"=>tw,"en-US"=>en,_=>standalone};
  }
  async Task Settled(){for(int i=0;i<100&&(connecting||refreshing);i++)await Task.Delay(25);Check(!connecting&&!refreshing,"bounded asynchronous operation");}
  LanguageBox.SelectedIndex=0;Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
  Check(JitterBox.Text=="50"&&OffsetBox.Text=="0","default jitter and calibration");Check(!StartButton.IsEnabled&&!connected,"no transport polling or start before preparation");
  var s=new RhythmSnapshot{ProcessId=12345,CapturedUtcTicks=DateTime.UtcNow.Ticks,State="waiting"};TestTransport.Publish(Path.Combine(root,"latest.json"),s);connected=true;await RefreshAsync();Check(StartButton.IsEnabled,"ready start");
  StartClick(this,new());await Settled();Check(link.Enabled,"arm");var command=RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!;Check(command.Valid(DateTime.UtcNow.Ticks,12345)&&command.JitterMs==50,"lease and default");
  JitterBox.Text="-1";Check(ParseSettings()==null&&JitterBox.Text=="-1","invalid input preserved");JitterBox.Text="0";Check(ParseSettings()!.JitterMs==0,"disable jitter");JitterBox.Text="50";link.Configure(ParseSettings()!);
  s.State="playing";s.Song="Synthetic demo · Hard";s.ClockMs=53120;s.DurationMs=129000;s.Sent=145;s.Total=390;s.Reason="按谱面演奏中";s.LastErrorMs=-24;
  TestTransport.Publish(Path.Combine(root,"latest.json"),s);await RefreshAsync();UpdateLayout();Check(StateText.Text=="正在演奏"&&SongText.Text.Contains("Hard"),"Chinese playing status");Capture("window-zh-CN.png");
  string owner=link.OwnerId;LanguageBox.SelectedIndex=1;UpdateLayout();
  Check(StateText.Text=="Playing"&&ReasonText.Text=="Playing the song chart","English dynamic status");
  Check((string)ConnectButton.Content==ActionLabel(ConnectButton,"Connect","连接游戏","連接遊戲","Connect game")&&(string)StartButton.Content==ActionLabel(StartButton,"Enable auto-play","开始","開始","Start"),"actions follow the owning window language");
  Check(LanguagePreference.Read(data)=="en-US"&&link.Enabled&&link.OwnerId==owner,"persist language without resetting playback");
  Check(JitterBox.Text=="50"&&OffsetBox.Text=="0","switch retains settings");Capture("window-en-US.png");
  s.State="paused";s.Reason="游戏暂停或倒计时，等待继续";s.CapturedUtcTicks=DateTime.UtcNow.Ticks;TestTransport.Publish(Path.Combine(root,"latest.json"),s);await RefreshAsync();Check(link.Enabled&&StopButton.IsEnabled&&StateText.Text=="Game paused","pause preserves enabled stop");
  Width=560;Height=460;UpdateLayout();Scroll.ScrollToBottom();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();Capture("window-small-en-US.png");Check(Scroll.ScrollableHeight>0&&StopButton.IsVisible,"small window scroll retains action bar");
  LanguageBox.SelectedIndex=0;UpdateLayout();Capture("window-small-zh-CN.png");Check(StateText.Text=="游戏已暂停","switch back");
  StopClick(this,new());await Settled();Check(!RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!.Enabled,"stop lease");
  s.Runtime="BD2Rhythm.Runtime2";TestTransport.Publish(Path.Combine(root,"latest.json"),s);await RefreshAsync();Check(!StartButton.IsEnabled,"reject previous runtime");
  Close();for(int i=0;i<100&&IsVisible;i++)await Task.Delay(25);Check(!IsVisible&&!RhythmJson.Read<RhythmControl>(Path.Combine(root,"control.json"))!.Enabled,"close lease");RhythmJson.Write(Path.Combine(output,"results.json"),new{status="pass",checks});
  void Capture(string file){var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,file));encoder.Save(stream);}
 }
}

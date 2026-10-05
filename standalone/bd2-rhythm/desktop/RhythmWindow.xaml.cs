using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BD2Rhythm.Localization;
namespace BD2Rhythm.Desktop;
public partial class RhythmWindow:Window {
 public bool HostedAutomationEnabled => link.Enabled || connecting;

 readonly string root;
 readonly RhythmControlLink link;
 readonly LanguageCatalog language;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
 bool connecting,initialized;
 public RhythmWindow(string? dataRoot=null){
  InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageBox);root=dataRoot??RhythmIdentity.DataRoot;link=new(root);language=new(LanguagePreference.Read(root));
  var s=RhythmJson.Read<RhythmSettings>(Path.Combine(root,"settings.json"))??new();
  if(!RhythmControl.ValidSettings(s.JitterMs,s.OffsetMs))s=new();
  JitterBox.Text=s.JitterMs.ToString();OffsetBox.Text=s.OffsetMs.ToString();
  LanguageBox.SelectedIndex=language.Language=="zh-CN"?0:1;ApplyLanguage();
  initialized=true;timer.Tick+=(_,_)=>Refresh();timer.Start();
 }
 void Set(TextBlock target,string text){target.Tag=text;target.Text=language.Text(text);}
 void ApplyLanguage(){
  foreach(var label in UiLabels.All)Resources[label.Key]=language.Text(label.Value);
  foreach(var block in new[]{StateText,ReasonText,SongText,ProgressText,MetricsText,SettingsHint})
   if(block.Tag is string source)block.Text=language.Text(source);
 }
 void LanguageChanged(object sender,SelectionChangedEventArgs e){
  if(!initialized)return;
  try{var selected=(string)((ComboBoxItem)LanguageBox.SelectedItem).Tag;LanguagePreference.Save(root,selected);language.Select(selected);ApplyLanguage();Refresh();}
  catch(Exception ex){Set(ReasonText,ex.Message);}
 }
 async void ConnectClick(object sender,RoutedEventArgs e){
  connecting=true;ConnectButton.IsEnabled=false;Set(ReasonText,"正在识别本机音游接口…");
  try{var text=await Task.Run(()=>new RhythmConnection(root).Connect(message=>Dispatcher.Invoke(()=>Set(ReasonText,message))));Set(ReasonText,text);}
  catch(Exception ex){Set(StateText,"连接未完成");Set(ReasonText,ex.GetBaseException().Message);}
  finally{connecting=false;ConnectButton.IsEnabled=true;}
 }
 bool ApplySettings(){
  if(!int.TryParse(JitterBox.Text,out int j)||!int.TryParse(OffsetBox.Text,out int o)||!RhythmControl.ValidSettings(j,o)){
   Set(SettingsHint,"随机偏移填写0–200；整体校准填写-500–500毫秒。");SettingsHint.Foreground=Brushes.Firebrick;return false;
  }
  try{link.Configure(new(){JitterMs=j,OffsetMs=o});Set(SettingsHint,"每个音符独立随机；0 表示关闭随机偏移。修改在下一曲生效。");SettingsHint.Foreground=(Brush)FindResource("MutedBrush");return true;}
  catch(Exception ex){Set(SettingsHint,ex.Message);SettingsHint.Foreground=Brushes.Firebrick;return false;}
 }
 void SettingsChanged(object sender,RoutedEventArgs e){if(initialized)ApplySettings();}
 void StartClick(object sender,RoutedEventArgs e){
  try{if(!ApplySettings())return;var s=RhythmJson.Read<RhythmSnapshot>(Path.Combine(root,"latest.json"));if(!RhythmControlLink.Fresh(s,DateTime.UtcNow))throw new InvalidOperationException("没有新鲜的游戏连接状态，请先连接。");link.Start(s!.ProcessId);Refresh();}
  catch(Exception ex){Set(ReasonText,ex.Message);}
 }
 void StopClick(object sender,RoutedEventArgs e){try{link.Stop();Set(StateText,"已停止");Set(ReasonText,"自动演奏已停止。");Refresh();}catch(Exception ex){Set(ReasonText,ex.Message);}}
 void Refresh(){
  if(connecting)return;var s=RhythmJson.Read<RhythmSnapshot>(Path.Combine(root,"latest.json"));bool fresh=RhythmControlLink.Fresh(s,DateTime.UtcNow);StartButton.IsEnabled=fresh&&!link.Enabled;StopButton.IsEnabled=link.Enabled;
  if(!fresh){if(link.Enabled){Set(StateText,"等待游戏恢复");Set(ReasonText,"游戏暂无新状态，等待恢复。");}return;}
  Set(StateText,link.Enabled?(s!.State switch{"playing"=>"正在演奏","paused"=>"游戏已暂停","error"=>"演奏已暂停","stopping"=>"正在释放输入",_=>"已开启 · 等待曲目"}):"已连接 · 自动演奏关闭");
  Set(ReasonText,link.Enabled?s!.Reason:"开启自动演奏后，在游戏中选曲并开始。");
  SongText.Tag=null;SongText.Text=language.Text("曲目：")+(string.IsNullOrEmpty(s!.Song)?language.Text("尚未识别"):s.Song);
  Progress.Value=s.DurationMs>0?Math.Clamp(100.0*s.ClockMs/s.DurationMs,0,100):0;
  Set(ProgressText,s.DurationMs>0?$"{TimeSpan.FromMilliseconds(Math.Max(0,s.ClockMs)):mm\\:ss} / {TimeSpan.FromMilliseconds(s.DurationMs):mm\\:ss}":"等待歌曲时间轴");
  Set(MetricsText,string.Format(language.Text("已操作 {0}/{1}　跳过 {2}　完成 {3} 曲　最近时差 {4} ms"),s.Sent,s.Total,s.Skipped,s.CompletedSongs,s.LastErrorMs.ToString("+0;-0;0")));
  if(link.Error!="")Set(ReasonText,"控制同步失败："+link.Error);
 }
 void FolderClick(object sender,RoutedEventArgs e){try{Directory.CreateDirectory(root);Process.Start(new ProcessStartInfo(root){UseShellExecute=true});}catch(Exception ex){Set(ReasonText,ex.Message);}}
 void OnClosing(object? sender,CancelEventArgs e){timer.Stop();link.Dispose();}
}

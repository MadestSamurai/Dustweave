using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BD2Rhythm.Localization;
namespace BD2Rhythm.Desktop;
public partial class RhythmWindow:Window {
 public bool HostedAutomationEnabled => link.Enabled || connecting;
 readonly string root,data;
 readonly RhythmControlLink link;
 readonly LanguageCatalog language;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 readonly SemaphoreSlim gate=new(1,1);
 bool connecting,connected,initialized,refreshing,closing;
 RhythmSnapshot? snapshot;
 public RhythmWindow(string? dataRoot=null){
  InitializeComponent();root=dataRoot??RhythmIdentity.DataRoot;data=dataRoot??RhythmStorage.Data;
  link=new(root,data);language=new(LanguagePreference.Read(data));
  var s=RhythmStorage.LoadSettings(data,dataRoot??RhythmIdentity.DataRoot);
  JitterBox.Text=s.JitterMs.ToString();OffsetBox.Text=s.OffsetMs.ToString();
  LanguageBox.SelectedIndex=language.Language=="en-US"?1:0;ApplyLanguage();
  initialized=true;timer.Tick+=async(_,_)=>await RefreshAsync();timer.Start();
 }
 void Set(TextBlock target,string text){target.Tag=text;target.Text=language.Text(text);}
 void ApplyLanguage(){
  foreach(var label in UiLabels.All)Resources[label.Key]=language.Text(label.Value);
  foreach(var block in new[]{StateText,ReasonText,SongText,ProgressText,MetricsText,SettingsHint})if(block.Tag is string source)block.Text=language.Text(source);
 }
 void LanguageChanged(object sender,SelectionChangedEventArgs e){
  if(!initialized)return;
  try{language.Select((string)((ComboBoxItem)LanguageBox.SelectedItem).Tag);LanguagePreference.Save(data,language.Language);ApplyLanguage();Render();}
  catch(Exception ex){Set(ReasonText,ex.Message);}
 }
 async void ConnectClick(object sender,RoutedEventArgs e){
  await Act(async()=>{
   connected=false;snapshot=null;Set(ReasonText,"正在识别本机音游接口…");
   string message=await Task.Run(()=>{RhythmStorage.ImportLegacyLogs(root,Path.Combine(data,"diagnostics"));return new RhythmConnection(root).Connect(text=>Dispatcher.Invoke(()=>Set(ReasonText,text)));});
   connected=true;Set(ReasonText,message);
  });
 }
 RhythmSettings? ParseSettings(){
  if(!int.TryParse(JitterBox.Text,out int j)||!int.TryParse(OffsetBox.Text,out int o)||!RhythmControl.ValidSettings(j,o)){
   Set(SettingsHint,"随机偏移填写0–200；整体校准填写-500–500毫秒。");SettingsHint.SetResourceReference(TextBlock.ForegroundProperty,"Error");return null;
  }
  Set(SettingsHint,"每个音符独立随机；0 表示关闭随机偏移。修改在下一曲生效。");SettingsHint.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");return new(){JitterMs=j,OffsetMs=o};
 }
 async void SettingsChanged(object sender,RoutedEventArgs e){
  if(!initialized||closing||connecting)return;
  var s=ParseSettings();if(s==null)return;
  await Act(()=>Task.Run(()=>link.Configure(s)));
 }
 async void StartClick(object sender,RoutedEventArgs e){
  var settings=ParseSettings();if(settings==null)return;
  await Act(()=>Task.Run(()=>{
   if(!connected)throw new InvalidOperationException("没有新鲜的游戏连接状态，请先连接。");
   var s=RhythmJson.Read<RhythmSnapshot>(Path.Combine(root,"latest.json"));
   if(!RhythmControlLink.Fresh(s,DateTime.UtcNow))throw new InvalidOperationException("没有新鲜的游戏连接状态，请先连接。");
   link.Configure(settings);link.Start(s!.ProcessId);
  }));
 }
 async void StopClick(object sender,RoutedEventArgs e){await Act(()=>Task.Run(()=>link.Stop()));}
 async Task Act(Func<Task> action){
  if(connecting||closing)return;
  string? failure=null;connecting=true;UpdateControls();await gate.WaitAsync();
  try{await action();}
  catch(Exception ex){failure=ex.GetBaseException().Message;Set(StateText,"操作未完成");Set(ReasonText,failure);try{await Task.Run(()=>RhythmJson.Write(Path.Combine(data,"diagnostics","desktop-error.json"),new{atUtc=DateTimeOffset.UtcNow,error=ex.ToString()}));}catch(IOException){}catch(UnauthorizedAccessException){}}
  finally{gate.Release();connecting=false;UpdateControls();}
  await RefreshAsync();if(failure!=null){Set(StateText,"操作未完成");Set(ReasonText,failure);}
 }
 async Task RefreshAsync(){
  if(!connected||connecting||refreshing||closing)return;
  refreshing=true;await gate.WaitAsync();
  try{var s=await Task.Run(()=>RhythmJson.Read<RhythmSnapshot>(Path.Combine(root,"latest.json")));if(!closing){snapshot=s;Render();}}
  finally{gate.Release();refreshing=false;}
 }
 void UpdateControls(){
  bool ready=!connecting&&!closing;
  ConnectButton.IsEnabled=ready&&!link.Enabled;StartButton.IsEnabled=ready&&connected&&RhythmControlLink.Fresh(snapshot,DateTime.UtcNow)&&!link.Enabled;StopButton.IsEnabled=ready&&link.Enabled;
 }
 void Render(){
  UpdateControls();var s=snapshot;
  if(!RhythmControlLink.Fresh(s,DateTime.UtcNow)){
   if(connected){Set(StateText,link.Enabled?"等待游戏恢复":"连接未就绪");Set(ReasonText,link.Enabled?"游戏暂无新状态，等待恢复。":"没有新鲜的游戏连接状态，请先连接。");}return;
  }
  Set(StateText,link.Enabled?(s!.State switch{"playing"=>"正在演奏","paused"=>"游戏已暂停","error"=>"演奏已暂停","stopping"=>"正在释放输入",_=>"已开启 · 等待曲目"}):"已连接 · 自动演奏关闭");
  Set(ReasonText,link.Enabled?s!.Reason:"开启自动演奏后，在游戏中选曲并开始。");
  SongText.Tag=null;SongText.Text=language.Text("曲目：")+(string.IsNullOrEmpty(s!.Song)?language.Text("尚未识别"):s.Song);
  Progress.Value=s.DurationMs>0?Math.Clamp(100.0*s.ClockMs/s.DurationMs,0,100):0;
  Set(ProgressText,s.DurationMs>0?$"{TimeSpan.FromMilliseconds(Math.Max(0,s.ClockMs)):mm\\:ss} / {TimeSpan.FromMilliseconds(s.DurationMs):mm\\:ss}":"等待歌曲时间轴");
  MetricsText.Tag=null;MetricsText.Text=string.Format(language.Text("已操作 {0}/{1}　跳过 {2}　完成 {3} 曲　最近时差 {4} ms"),s.Sent,s.Total,s.Skipped,s.CompletedSongs,s.LastErrorMs.ToString("+0;-0;0"));
  if(link.Error!="")Set(ReasonText,"控制同步失败："+link.Error);
 }
 async void OnClosing(object? sender,CancelEventArgs e){
  if(closing)return;e.Cancel=true;closing=true;timer.Stop();UpdateControls();await gate.WaitAsync();
  try{await Task.Run(link.Dispose);}finally{gate.Release();Dispatcher.BeginInvoke(new Action(Close),DispatcherPriority.Background);}
 }
}

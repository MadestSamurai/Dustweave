using System.Collections.ObjectModel;
using BD2ApostleDefense.Localization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace BD2ApostleDefense.Desktop;
public partial class MainWindow:Window
{
 public bool HostedAutomationEnabled => controller.Running || starting || connecting;

 private readonly WindowLanguage language;
 private bool connecting,closing,starting;private int connectionEpoch;
 private readonly CancellationTokenSource lifetime=new();
 private Task controlQueue=Task.CompletedTask,shutdownTask=Task.CompletedTask,refreshWork=Task.CompletedTask;
 private Task QueueControl(Action action)
 {
  var previous=controlQueue;
  return controlQueue=Task.Run(async()=>{try{await previous.ConfigureAwait(false);}catch{}action();});
 }
 private readonly IClientPort port;private readonly Controller controller;private readonly string root;private readonly bool smoke;
 private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
 private readonly ObservableCollection<string> logs=new();private readonly object logLock=new();private Snapshot? snapshot;private bool busy,initialized;private string lastRefreshError="";private long lastRefreshErrorAt;
 public MainWindow(IClientPort port,string root,bool smoke=false,bool connectionSmoke=false)
 {
  this.port=port;this.root=root;this.smoke=smoke;controller=new(port);InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageChoice);language=new WindowLanguage(this,LanguagePreference.Read(root));Ui.Catalog=language.Catalog;LanguageChoice.SelectedIndex=language.Catalog.Language=="zh-CN"?0:1;LogList.ItemsSource=logs;
  var p=JsonFiles.Read<Settings>(System.IO.Path.Combine(root,"settings.json"))??new();ClearGoal.IsChecked=p.Clear50;RareGoal.IsChecked=p.Rare;AutoNext.IsChecked=p.AutoNext;StopRound.IsChecked=p.StopAfterRound;Interval.Text=p.IntervalMs.ToString();
  controller.Diagnostic+=Log;initialized=true;timer.Tick+=async(_,_)=>await Refresh();Loaded+=async(_,_)=>{if(connectionSmoke)return;await Refresh();timer.Start();if(smoke)await Smoke();};
  Closing+=OnClosing;
 }
 private void LanguageChanged(object sender,SelectionChangedEventArgs e)
 {
  if(language==null||!initialized)return;
  string selected=AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") as string ?? (LanguageChoice.SelectedIndex==0?"zh-CN":"en-US");
  try{LanguagePreference.Save(root,selected);language.Select(selected);BoardView.RefreshLanguage();RenderAccount();}
  catch(Exception ex){SettingsError.Text=ex.Message;}
 }
 private void RenderAccount()
 {
  if(snapshot==null){AccountText.Text=Ui.Text("进入使徒运气防守后连接游戏。");return;}
  AccountText.Text=(snapshot.Name.Length>0?snapshot.Name:Ui.Text("当前账号"))+Ui.Text("  ·  组件 ")+Identity.Runtime.Replace("BD2ApostleDefense.","")+Ui.Text("  ·  实时状态已同步");
 }
 private Settings ReadSettings()
 {
  if(!int.TryParse(Interval.Text,out int ms))throw new ArgumentException("请输入有效的整数间隔，不会重置你的输入");
  var p=new Settings{Clear50=ClearGoal.IsChecked==true,Rare=RareGoal.IsChecked==true,AutoNext=AutoNext.IsChecked==true,StopAfterRound=StopRound.IsChecked==true,IntervalMs=ms};
  if(p.Validate()!="")throw new ArgumentException(p.Validate());return p;
 }
 private async void SettingsChanged(object? sender,RoutedEventArgs e)
 {
  if(!initialized||closing)return;try{var p=ReadSettings();controller.Update(p);await QueueControl(()=>JsonFiles.Write(System.IO.Path.Combine(root,"settings.json"),p));if(!closing)SettingsError.Text="";}catch(Exception ex){if(!closing)SettingsError.Text=ex.Message;}
 }
 private async void Connect(object sender,RoutedEventArgs e)=>await ConnectAsync();
 private async Task ConnectAsync()
 {
  if(connecting||closing)return;connecting=true;connectionEpoch++;ConnectButton.IsEnabled=false;StartButton.IsEnabled=false;
  ConnectionDiagnostics.Write(root,"ui.connect.clicked");var running=controller.Running;controller.RequestStop();
  try
  {
   await refreshWork;
   if(running)await QueueControl(controller.Flush);
   await port.ConnectAsync(t=>Dispatcher.BeginInvoke(()=>{if(!closing&&connecting)StatusText.Text=t;}),lifetime.Token);
   if(!closing)Log("连接已请求，等待组件首次心跳");
  }
  catch(OperationCanceledException){ConnectionDiagnostics.Write(root,"connect.cancelled");}
  catch(Exception ex){ConnectionDiagnostics.Write(root,"connect.failed",error:ex);if(!closing){StatusText.Text=ex.Message;Log(ex.ToString());}}
  finally{connecting=false;if(!closing){ConnectButton.IsEnabled=true;await Refresh();}}
 }
 private async void Start(object sender,RoutedEventArgs e)
 {
  if(starting||connecting||closing)return;starting=true;StartButton.IsEnabled=false;var stopped=controller.StopVersion;
  try
  {
   if(snapshot==null)throw new InvalidOperationException("尚未读取游戏状态");var s=snapshot;var p=ReadSettings();
   await QueueControl(()=>{if(closing)throw new OperationCanceledException();controller.Start(s,p,DateTime.UtcNow.Ticks,stopped);});
   if(!closing){SettingsChanged(null,e);await Refresh();}
  }
  catch(OperationCanceledException){}
  catch(Exception ex){if(!closing)SettingsError.Text=ex.Message;ConnectionDiagnostics.Write(root,"start.failed",error:ex);}
  finally{starting=false;}
 }
 private async void Pause(object sender,RoutedEventArgs e)=>await PauseAsync();
 private async Task PauseAsync()
 {
  controller.RequestStop();DecisionText.Text=controller.Message;PauseButton.IsEnabled=false;StartButton.IsEnabled=!connecting&&!closing;
  try{await QueueControl(controller.Flush);}catch(Exception ex){ConnectionDiagnostics.Write(root,"stop.failed",error:ex);if(!closing)Log(ex.Message);}
 }
 private void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
 {
  if(closing)return;e.Cancel=true;closing=true;connectionEpoch++;lifetime.Cancel();timer.Stop();controller.RequestStop();
  ConnectionDiagnostics.Write(root,"ui.closing");shutdownTask=FinishCloseAsync();
 }
 private async Task FinishCloseAsync()
 {
  var stopped=QueueControl(()=>{try{controller.Flush();}catch(Exception ex){ConnectionDiagnostics.Write(root,"close.stop",error:ex);}});
  if(await Task.WhenAny(stopped,Task.Delay(2000))!=stopped)ConnectionDiagnostics.Write(root,"ui.close.lease-expiry-fallback");
  language.Dispose();Close();
 }
 private async Task Refresh()
 {
  if(busy||connecting||closing)return;busy=true;var epoch=connectionEpoch;
  try
  {
   var work=Task.Run(()=>{var state=port.Read();controller.Poll(state,DateTime.UtcNow.Ticks);var game=port.Find();
    var status=state==null||game==null||!Guards.Fresh(state,game.Id,game.Start,DateTime.UtcNow.Ticks)?JsonFiles.Read<RuntimeStatus>(System.IO.Path.Combine(root,"runtime.json")):null;
    return (state,game,status);});refreshWork=work;var read=await work;
   if(closing||connecting||epoch!=connectionEpoch)return;
   var s=read.state;snapshot=s;var game=read.game;bool fresh=s!=null&&game!=null&&Guards.Fresh(s,game.Id,game.Start,DateTime.UtcNow.Ticks);
   StartButton.IsEnabled=fresh&&s!.Account.Length==64&&!controller.Running&&!starting;PauseButton.IsEnabled=controller.Running;
   if(!fresh)
   {
    StatusText.Text=game==null?"等待游戏启动":"等待组件心跳";var status=read.status;
    if(status?.State=="error"&&game!=null&&status.ProcessId==game.Id&&status.ProcessStart==game.Start)StatusText.Text=status.Error;
    DecisionText.Text=controller.Running?controller.Message:"连接游戏后进入使徒运气防守大厅";BoardView.SetFresh(false);return;
   }
   StatusText.Text=(controller.Running?"自动化已开启 · ":"已连接 · ")+Names.Stage(s!.Stage);
   if(s.NetworkState!="inactive")StatusText.Text+=s.NetworkHold?" · 网络恢复中":s.NetworkState=="probe-degraded"?" · 已保留游戏连接":" · 网络正常";
   RenderAccount();
   ClearProgress.Text=s.AchievementsKnown?(s.ClearProgress>=s.ClearTarget?"已完成 · 服务器确认":$"{s.ClearProgress} / {s.ClearTarget} 次通关"):"等待服务器核对";
   RareProgress.Text=s.AchievementsKnown?$"{s.RareProgress} / {s.RareTarget} 次 · 服务器确认":"等待服务器核对";
   PendingProgress.Text=$"本局待结算：{s.RoundRare} 次";WaveText.Text=$"第 {s.Wave} / {Math.Max(50,s.Catalog.Waves.Length)} 波";
   Metrics.Text=$"金币 {s.Gold:N0}     敌人 {s.EnemyCount}/{s.Catalog.GameOverCount}     剩余 {s.SecondsLeft:0}s";
   FocusText.Text=controller.Running?controller.Focus:"当前决策";DecisionText.Text=controller.Running?controller.Message:s.State=="error"?s.Message:"已暂停；可调整目标后开启。";
   BoardView.Update(s,controller.PendingDecision);
  }
  catch(Exception ex){if(closing||connecting||epoch!=connectionEpoch)return;StatusText.Text=ex.Message;string key=ex.GetType().Name+"|"+ex.Message;long now=DateTime.UtcNow.Ticks;IoDiagnostics.Record(root,"desktop-refresh",System.IO.Path.Combine(root,"control.json"),ex);if(key!=lastRefreshError||now-lastRefreshErrorAt>TimeSpan.FromSeconds(15).Ticks){lastRefreshError=key;lastRefreshErrorAt=now;Log(ex.ToString());}}
  finally{busy=false;}
 }
 private void Log(string text)
 {
  string line=DateTime.Now.ToString("HH:mm:ss")+"  "+text;Dispatcher.BeginInvoke(()=>{logs.Insert(0,line);while(logs.Count>100)logs.RemoveAt(logs.Count-1);});
  try{lock(logLock){Directory.CreateDirectory(root);File.AppendAllText(System.IO.Path.Combine(root,"decisions-"+DateTime.Now.ToString("yyyyMMdd")+".log"),line+Environment.NewLine);}}catch{}
 }
 private void OpenLogs(object sender,RoutedEventArgs e){Directory.CreateDirectory(root);Process.Start(new ProcessStartInfo("explorer.exe",root){UseShellExecute=true,WorkingDirectory=Environment.CurrentDirectory});}
 private void CopyLog(object sender,System.Windows.Input.MouseButtonEventArgs e){if(LogList.SelectedItem is string text)Clipboard.SetText(text);}
 private async Task Smoke()
 {
  try
  {
   timer.Stop();LanguageChoice.SelectedIndex=0;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
   if(snapshot==null||!StartButton.IsEnabled||BoardView.TileCount!=68)throw new InvalidOperationException("UI snapshot not rendered");
   Interval.Text="50";SettingsChanged(null,new());if(SettingsError.Text.Length==0||Interval.Text!="50")throw new InvalidOperationException("Invalid settings not preserved");Interval.Text="500";SettingsChanged(null,new());
   controller.Start(snapshot,ReadSettings(),DateTime.UtcNow.Ticks);if(!controller.Running)throw new InvalidOperationException("Start failed");controller.Stop();
   var large=DemoPort.Demo();large.Boards=Enumerable.Range(0,36).Select(i=>new Board{Id=100+i*3,X=(i%6-2.5)*1.2,Z=(i/6-2.5)*1.2}).ToArray();large.Units=Array.Empty<Unit>();large.CanSummon=true;large.Wave=1;
   large.Catalog.Waves=new[]{new WaveDef{Id=1,Element=1,Count=50,Hp=100,Duration=30,Speed=1,SpawnInterval=.5}};
   if(new Planner().Decide(large,new()).Kind!="summon")throw new InvalidOperationException("Packaged planner blocks 36-slot opening");
   var opening=JsonFiles.Clone(large);opening.Gold=20;opening.CanSummon=false;opening.CanUpgrade=Enumerable.Repeat(true,5).ToArray();opening.UpgradeCosts=Enumerable.Repeat(12,5).ToArray();
   opening.Catalog.Units=new[]{new UnitDef{Id=501,Element=4,Grade=1,Attack=12,UpAttack=12,Interval=1.5,Range=100,Weight=1}};
   opening.Catalog.Waves=new[]{new WaveDef{Id=1,Element=0,Hp=9,Count=60,Duration=30,Gold=1},new WaveDef{Id=50,Element=3,Hp=100000,Count=1,Duration=60,Boss=true}};
   opening.Units=new[]{new Unit{Id=501,Index=1,Grid=opening.Boards[0].Id,Ready=true}};
   if(new Planner().Decide(opening,new()).Kind!="wait")throw new InvalidOperationException("Packaged opening spends second-summon gold on upgrades");
   opening.Gold=25;opening.CanSummon=true;
   if(new Planner().Decide(opening,new()).Kind!="summon")throw new InvalidOperationException("Packaged opening fails to summon second unit when affordable");
   var melee=DemoPort.Demo();melee.Boards=new[]{new Board{Id=10},new Board{Id=11,Z=4.5},new Board{Id=12,X=3}};
   melee.Units=new[]{new Unit{Id=2,Index=1,Grid=10,Ready=true},new Unit{Id=1,Index=2,Grid=12,Ready=true}};
   melee.Catalog.Units.Single(d=>d.Id==1).Attack=1000000;melee.Catalog.Units.Single(d=>d.Id==1).Range=100;
   melee.Catalog.Waves=new[]{new WaveDef{Id=1,Element=1,Count=50,Hp=1000000,Duration=30,Speed=1,SpawnInterval=.5}};melee.Wave=1;
   var rescue=new Planner().Decide(melee,new());if(rescue.Kind!="move"||rescue.UnitIndex!=1||rescue.TargetGrid!=11)throw new InvalidOperationException("Packaged planner suppresses melee rescue beside strong teammate");
   melee.Boards=melee.Boards.Take(2).ToArray();melee.Units[1].Grid=11;var swap=new Planner().Decide(melee,new());
   if(swap.Kind!="move"||swap.UnitIndex!=1||swap.TargetIndex!=2)throw new InvalidOperationException("Packaged planner suppresses melee swap with unchanged ranged coverage");
   var late=JsonFiles.Clone(melee);late.Boards[1].Z=0;late.Boards[1].X=.5;late.Catalog.Units.Single(d=>d.Id==2).Range=1;
   late.Catalog.Waves[0].Hp=10000000;late.Catalog.Waves[0].Count=40;late.Catalog.Waves[0].Duration=30;
   var reroll=new Planner().Decide(late,new());if(reroll.Kind!="sell"||reroll.UnitIndex!=1)throw new InvalidOperationException("Packaged planner locks zero-output reroll behind army DPS requirement");
   var boss=DemoPort.Demo();boss.At=DateTime.UtcNow.Ticks;boss.Room="boss-smoke";boss.Wave=50;boss.SecondsLeft=120;boss.Gold=0;
   boss.Boards=new[]{new Board{Id=10,X=3,Z=-3},new Board{Id=20,X=-3,Z=3}};
   boss.Path=new[]{new BD2ApostleDefense.Point{Id=1,X=-5,Z=5},new BD2ApostleDefense.Point{Id=2,X=-5,Z=-5},new BD2ApostleDefense.Point{Id=3,X=5,Z=-5},new BD2ApostleDefense.Point{Id=4,X=5,Z=5}};
   boss.Units=new[]{new Unit{Id=2,Index=1,Grid=10,Ready=true}};boss.Catalog.Units.Single(d=>d.Id==2).Range=3.75;
   boss.Catalog.Waves=new[]{new WaveDef{Id=50,EnemyTable=1050,Boss=true,Count=1,Hp=2100000,Duration=120,Speed=3}};
   boss.Enemies=new[]{new Enemy{Id=901,Table=1050,Hp=2100000,Speed=3,X=-5,Z=5,NextPoint=2}};boss.BoardKey=Guards.BoardKey(boss);
   var chase=new Planner().Decide(boss,new(),boss.At);
   if(chase.Policy!=BossChase.Policy||chase.Kind!="move"||chase.TargetGrid!=20||chase.TargetEnemy!=901)throw new InvalidOperationException("Packaged boss pursuit failed");
   var bossCommand=new Control{Enabled=true,Owner=new('b',32),Account=boss.Account,Room=boss.Room,ProcessId=boss.ProcessId,ProcessStart=boss.ProcessStart,SnapshotAt=boss.At,Expires=boss.At+TimeSpan.FromSeconds(10).Ticks,BoardKey=boss.BoardKey,Action=chase};
   if(Guards.Reject(bossCommand,boss,boss.At)!=""||Guards.Reject(bossCommand,boss,boss.At+TimeSpan.FromSeconds(1).Ticks)=="")throw new InvalidOperationException("Packaged boss freshness guard failed");
   var result=DemoPort.Demo();result.Stage="result";result.Dead=true;
   var lobby=JsonFiles.Clone(result);lobby.Stage="lobby";lobby.Room="";lobby.Dead=false;lobby.Blocker="EventPopupUI";lobby.EventPopupOpen=true;lobby.CanDismissEvent=true;
   if(!GameFlow.Receipt("settle",result,lobby)||new Planner().Decide(lobby,new()).Kind!="close-event")throw new InvalidOperationException("Packaged result-to-lobby popup recovery failed");
   if(!GameFlow.Superseded("match-start",boss,lobby))throw new InvalidOperationException("Packaged cancelled match does not recover");
   var recovery=new RecoveryBackoff();recovery.Sync(boss);recovery.Record(chase,false,boss.At);
   if(recovery.CanMove(chase.UnitIndex,boss.At)||!recovery.Ready(new(){Kind="summon"},boss.At))throw new InvalidOperationException("Packaged failed move blocks summoning");
   await BoardSmoke();await LanguageSmoke();
   FocusText.Text="50波通关：输出、覆盖与首领准备";DecisionText.Text="准备水属性升级，强化现有2名使徒；等待游戏确认后继续。";Log("演示盘面，不连接或操作游戏");
   await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();Directory.CreateDirectory(root);
   Capture("ui.png");
   JsonFiles.Write(System.IO.Path.Combine(root,"smoke.json"),new{status="passed",version=AppVersion.Current,checks=new[]{"board","goals","invalid-input-preserved","start-pause","snapshot-render","36-slot-packaged-planner","opening-summon-savings","opening-second-unit","melee-rescue-with-strong-teammate","melee-swap-with-strong-occupant","zero-output-reroll","68-slot-default-minimum-wide-layout","nonoverlap-in-bounds","selection-range-and-fallback-name","selection-follows-unit-not-replacement","enemy-layer-live-update","pending-move-and-stale-state","keyboard-navigation","spectator-and-empty-board","boss-chase-native-command","boss-chase-freshness-guard","english-static-and-dynamic","language-running-state-preserved","language-settings-preserved","english-minimum-layout","english-tile-width","source-names-preserved","settlement-popup-recovery","cancelled-match-recovery","move-recovery-keeps-summon","language-roundtrip","language-listener-cleanup"}});Application.Current.Shutdown();
  }catch(Exception e){JsonFiles.Write(System.IO.Path.Combine(root,"smoke.json"),new{status="failed",error=e.ToString()});Application.Current.Shutdown(1);}
 }
}

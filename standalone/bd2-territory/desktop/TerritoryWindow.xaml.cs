using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace BD2Territory.Desktop;
public partial class TerritoryWindow : Window
{
 public bool HostedAutomationEnabled => link.Enabled || starting || !controlQueue.IsCompleted || connecting;

 private readonly string root;
 private readonly TerritoryControlLink link;
 private readonly DispatcherTimer timer;
 private TerritorySnapshot? snapshot;
 private bool initialized,connecting,closing,smoke,refreshing,starting,diagnosticsBusy,layoutOpen;
 private readonly CancellationTokenSource lifetime=new();
 private Task controlQueue=Task.CompletedTask,refreshWork=Task.CompletedTask,shutdownTask=Task.CompletedTask;
 private int connectionEpoch,settingsRevision;
 private Task QueueControl(Action action){var previous=controlQueue;return controlQueue=Task.Run(async()=>{try{await previous.ConfigureAwait(false);}catch{}action();});}
 private async Task InitializeControlAsync(TerritorySettings settings){try{await QueueControl(()=>link.Configure(settings));}catch(Exception ex){TerritoryDiagnostics.Write(root,"settings.initialize.failed",error:ex);}}
 private Func<TerritorySnapshot?>? testSnapshotReader;
 private Func<Action<string>,CancellationToken,Task<string>>? testConnector;
 private DateTime lastDiagnostic;private int selectedRecipe=3,selectedSeed;private string seedCatalogKey="";private bool refreshingRecipes;private string recipeCatalogKey="";
 public TerritoryWindow(string? dataRoot=null)
 {
  root=dataRoot??TerritoryIdentity.DataRoot;link=new(root);InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageBox);InitializeLanguage();
  Set(VersionText,typeof(TerritoryWindow).Assembly.GetName().Version!.ToString(3));
  var settings=TerritoryJson.Read<TerritorySettings>(Path.Combine(root,"settings.json"))??new();
  if(!settings.ValidSettings())settings=new();
  selectedSeed=settings.FixedSeedId;PlantingModeBox.SelectedIndex=settings.FixedCrop?1:0;
  if(selectedSeed>0){FixedSeedBox.ItemsSource=new[]{new RecipeOption{Id=selectedSeed,Name=ui.Language.Text("作物 ")+selectedSeed,Available=true}};FixedSeedBox.SelectedValue=selectedSeed;}
  selectedRecipe=settings.RecipeId;RecipeBox.ItemsSource=new[]{new RecipeOption{Id=selectedRecipe,Name=selectedRecipe==3?"活力面疙瘩":"配方 "+selectedRecipe,Available=true}};RecipeBox.SelectedValue=selectedRecipe;
  SellThresholdBox.Text=settings.SellThreshold.ToString();CookingBox.IsChecked=settings.Cooking;CookingBatchBox.Text=settings.CookingBatch.ToString();LoggingBox.IsChecked=settings.Logging;MiningBox.IsChecked=settings.Mining;FarmingBox.IsChecked=settings.Farming;
  NavMeshBox.IsChecked=settings.UseNavMesh;VehicleBox.IsChecked=settings.UseVehicle;DashBox.IsChecked=settings.DashRecovery;
  Set(IntervalBox,settings.IntervalMs.ToString());Set(BudgetBox,settings.PlantingBudget.ToString());Set(DataPathBox,root);
  _ = InitializeControlAsync(settings);initialized=true;UpdatePlantingControls();timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=async(_,_)=>await RefreshAsync();timer.Start();
  Closing+=OnClosing;
 }
 private TerritorySettings Settings()
 {
  if(!int.TryParse(CookingBatchBox.Text,out int cookingBatch)||!int.TryParse(IntervalBox.Text,out int interval)||!long.TryParse(BudgetBox.Text,out long budget))throw new InvalidOperationException("间隔和预算请输入整数。");
  if(!int.TryParse(SellThresholdBox.Text,out int threshold)||!SurplusSales.ValidThreshold(threshold))throw new InvalidOperationException("售卖保留数量必须是 100–9900 的整数。");
  var s=new TerritorySettings{AutoSell=true,SellThreshold=threshold,FixedCrop=PlantingModeBox.SelectedIndex==1,FixedSeedId=selectedSeed,Cooking=CookingBox.IsChecked==true,CookingBatch=cookingBatch,RecipeId=selectedRecipe,Logging=LoggingBox.IsChecked==true,Mining=MiningBox.IsChecked==true,Farming=FarmingBox.IsChecked==true,UseNavMesh=NavMeshBox.IsChecked==true,UseVehicle=VehicleBox.IsChecked==true,DashRecovery=DashBox.IsChecked==true,IntervalMs=interval,PlantingBudget=budget};
  if(!s.ValidSettings())throw new InvalidOperationException("间隔范围 100–60000 毫秒，预算范围 0–10000000；料理每批 1–1000 份。");return s;
 }
 private async void SettingsChanged(object sender,RoutedEventArgs e)
 {
  if(!initialized||closing)return;UpdatePlantingControls();var revision=++settingsRevision;
  try{var settings=Settings();if(link.Enabled&&settings.Farming&&settings.FixedCrop&&settings.FixedSeedId<=0)throw new InvalidOperationException("请选择要固定种植的作物。");await QueueControl(()=>{if(!closing)link.Configure(settings);});if(closing||revision!=settingsRevision)return;Set(SettingsHint,"设置已保存。预算 0 表示不限；暂停后重新开始会重置本次预算。");SettingsHint.Foreground=(Brush)FindResource("MutedBrush");}
  catch(Exception ex){TerritoryDiagnostics.Write(root,"settings.failed",error:ex);if(closing||revision!=settingsRevision)return;Set(SettingsHint,ex.GetBaseException().Message);SettingsHint.Foreground=Brushes.Firebrick;}
 }
 private void UpdatePlantingControls()
 {
  bool fixedCrop=PlantingModeBox.SelectedIndex==1;FixedCropPanel.Visibility=fixedCrop?Visibility.Visible:Visibility.Collapsed;
  RecipePanel.Visibility=!fixedCrop||CookingBox.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
 }
 private void PlantingChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
 {if(!initialized||refreshingRecipes)return;if(FixedSeedBox.SelectedValue is int seed)selectedSeed=seed;SettingsChanged(sender,e);}
 private void RecipeChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
 {if(!initialized||refreshingRecipes)return;if(RecipeBox.SelectedItem is RecipeOption r){selectedRecipe=r.Id;SettingsChanged(sender,e);Set(RecipeHint,r.Available?r.Ratio+"。按实际空田分批播种；正在播种时，本批回执确认后切换。":r.Reason);}}
 private async void LayoutClick(object sender,RoutedEventArgs e)
 {
  if(link.Enabled||connecting||starting||closing||layoutOpen){ShowError("请先暂停采集和种植，再打开布局工具。");return;}
  layoutOpen=true;timer.Stop();
  try{await QueueControl(()=>{});if(!closing)new LayoutWindow(root,link){Owner=this}.ShowDialog();}
  catch(Exception ex){TerritoryDiagnostics.Write(root,"layout.open.failed",error:ex);if(!closing)ShowError(ex.GetBaseException().Message);}
  finally{layoutOpen=false;if(!closing){timer.Start();await RefreshAsync();}}
 }
 private void ShowError(string text){Set(ErrorText,text);ErrorText.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;}
 private async void ConnectClick(object sender,RoutedEventArgs e)=>await ConnectAsync();
 private async Task ConnectAsync()
 {
  if(connecting||closing||layoutOpen)return;connecting=true;connectionEpoch++;ConnectButton.IsEnabled=false;StartButton.IsEnabled=false;ShowError("");Set(StatusText,"正在连接领地组件");
  TerritoryDiagnostics.Write(root,"ui.connect.clicked");var wasEnabled=link.Enabled;link.RequestStop();
  try {
   await refreshWork;if(wasEnabled)await QueueControl(link.Flush);
   Action<string> progress=text=>Dispatcher.InvokeAsync(()=>{if(!closing&&connecting)Set(ReasonText,text);});
   var message=testConnector==null?await Task.Run(()=>new TerritoryConnection(root).Connect(progress,lifetime.Token)):await testConnector(progress,lifetime.Token);
   if(!closing)Set(ReasonText,message);
  }catch(OperationCanceledException){TerritoryDiagnostics.Write(root,"connect.cancelled");}
  catch(Exception ex){TerritoryDiagnostics.Write(root,"ui.connect.failed",error:ex);if(!closing){Set(StatusText,"连接未完成");ShowError(ex.GetBaseException().Message);}}
  finally{connecting=false;if(!closing){ConnectButton.IsEnabled=true;await RefreshAsync();}}
 }
 private async void StartClick(object sender,RoutedEventArgs e)=>await StartAsync();
 private async Task StartAsync()
 {
  if(starting||connecting||closing||layoutOpen)return;starting=true;StartButton.IsEnabled=false;var stopped=link.StopVersion;
  try
  {
   var settings=Settings();if((settings.Farming&&!settings.FixedCrop||settings.Cooking)&&snapshot?.Recipes?.Length>0&&snapshot.Recipes.FirstOrDefault(r=>r.Id==settings.RecipeId)?.Available!=true)throw new InvalidOperationException("当前配方尚不可自动种植，请选择已解锁配方或取消种植项目。");if(!settings.Logging&&!settings.Mining&&!settings.Farming&&!settings.Cooking&&!settings.AutoSell)throw new InvalidOperationException("请至少选择一项执行项目。");
   if(settings.Farming&&settings.FixedCrop&&(settings.FixedSeedId<=0||snapshot?.Seeds?.FirstOrDefault(v=>v.Id==settings.FixedSeedId)?.Available!=true))throw new InvalidOperationException("请选择已解锁的固定作物；连接后会读取完整作物列表。");
   if(!TerritoryControlLink.Fresh(snapshot,DateTime.UtcNow)||!snapshot!.Ready)throw new InvalidOperationException("请先连接游戏并进入可走动的领地，等待实时状态。");
   int pid=snapshot.ProcessId;
   await QueueControl(()=>{
   if(closing||link.StopVersion!=stopped)throw new OperationCanceledException();
   if(!smoke)
   {
    using var game=TerritoryConnection.FindGame();
    new TerritoryConnection(root).ValidateStart(pid,game.Id,game.StartTime.ToUniversalTime().Ticks);
   }
   link.Configure(settings);link.Start(pid,expectedStopVersion:stopped);});if(!closing){ShowError("");await RefreshAsync();}
  }
  catch(OperationCanceledException){}
  catch(Exception ex){TerritoryDiagnostics.Write(root,"start.failed",error:ex);if(!closing)ShowError(ex.GetBaseException().Message);}
  finally{starting=false;if(!closing)StartButton.IsEnabled=!link.Enabled&&!connecting&&!starting&&TerritoryControlLink.Fresh(snapshot,DateTime.UtcNow)&&snapshot!.Ready;}
 }
 private async void StopClick(object sender,RoutedEventArgs e)=>await StopAsync();
 private async Task StopAsync(bool clearError=true)
 {
  link.RequestStop();if(clearError)ShowError("");Set(StatusText,"自动化已暂停");Set(ReasonText,"批次进度会保留；已提交的播种等待服务器确认。");
  StartButton.IsEnabled=!closing&&!connecting&&TerritoryControlLink.Fresh(snapshot,DateTime.UtcNow)&&snapshot!.Ready;StopButton.IsEnabled=false;ConnectButton.IsEnabled=!connecting&&!closing;
  try{await QueueControl(link.Flush);}catch(Exception ex){TerritoryDiagnostics.Write(root,"stop.failed",error:ex);if(!closing)ShowError(ex.GetBaseException().Message+"；若停止文件未写入，控制租约会在 10 秒内失效。");}
 }
 private async Task RefreshAsync()
 {
  if(refreshing||connecting||closing||layoutOpen)return;refreshing=true;var epoch=connectionEpoch;
  try{var work=Task.Run(()=>testSnapshotReader!=null?testSnapshotReader():TerritoryJson.Read<TerritorySnapshot>(Path.Combine(root,"latest.json")));refreshWork=work;var value=await work;
   if(closing||connecting||epoch!=connectionEpoch)return;snapshot=value;RenderSnapshot();}
  catch(Exception ex){TerritoryDiagnostics.Throttled(root,"refresh.failed",ex);if(!closing)StartButton.IsEnabled=false;}
  finally{refreshing=false;}
 }
 private void RenderSnapshot()
 {
  if(closing)return;
  bool fresh=TerritoryControlLink.Fresh(snapshot,DateTime.UtcNow);StartButton.IsEnabled=fresh&&snapshot!.Ready&&!link.Enabled&&!connecting&&!starting;StopButton.IsEnabled=link.Enabled;ConnectButton.IsEnabled=!connecting&&!link.Enabled;
  if(!fresh)
  {
   if(!connecting){Set(StatusText,link.Enabled?"等待游戏恢复实时状态":"尚未连接领地实时状态");Set(ReasonText,link.Enabled?"请检查游戏是否正在加载。当前设置保持，恢复领地状态后继续。":"连接游戏并进入可走动的领地后，即可开始。");}
   _ = UpdateDiagnosticsAsync();return;
  }
  var s=snapshot!;
  if(s.Seeds?.Length>0)
  {
   var key=ui.Language.Language+"|"+string.Join("|",s.Seeds.Select(v=>v.Id+v.Label));
   if(!FixedSeedBox.IsDropDownOpen&&key!=seedCatalogKey){refreshingRecipes=true;FixedSeedBox.ItemsSource=s.Seeds.Select(v=>new RecipeOption{Id=v.Id,Name=v.Name,Reason=ui.Language.Text(v.Reason),Available=v.Available}).ToArray();FixedSeedBox.SelectedValue=selectedSeed;seedCatalogKey=key;refreshingRecipes=false;}
   var seed=s.Seeds.FirstOrDefault(v=>v.Id==selectedSeed);Set(FixedCropHint,seed==null?"请选择要固定种植的作物。":seed.Available?"按实际空田分批种植，收获后继续种植此作物；切换时先完成当前播种回执。":seed.Reason);
  }
  if(s.Recipes?.Length>0){var key=string.Join("|",s.Recipes.Select(v=>v.Id+v.Label+v.Ratio));if(!RecipeBox.IsDropDownOpen&&key!=recipeCatalogKey){refreshingRecipes=true;RecipeBox.ItemsSource=s.Recipes.Select(v=>new RecipeOption{Id=v.Id,Name=v.Name,Ratio=v.Ratio,Reason=ui.Language.Text(v.Reason),Available=v.Available}).ToArray();RecipeBox.SelectedValue=selectedRecipe;recipeCatalogKey=key;refreshingRecipes=false;}var r=s.Recipes.FirstOrDefault(v=>v.Id==selectedRecipe);Set(RecipeHint,r==null?"当前客户端已无此配方，请重新选择。":r.Available?r.Ratio+"。按实际空田分批播种；切换时先完成当前播种回执。":r.Reason);}
  if(link.Enabled&&s.OwnerId==link.OwnerId&&!string.IsNullOrEmpty(s.Error))
  {_ = StopAsync(false);ShowError(s.Error);}
  bool ours=s.OwnerId==link.OwnerId;
  Set(StatusText,link.Enabled?"自动化运行中":ErrorText.Visibility==Visibility.Visible?"自动化已暂停":s.Ready?"领地已识别":"等待进入领地");
  Set(ReasonText,link.Enabled?(ours?s.Reason:"等待游戏接收开始指令"):s.Ready?"设置完成后点击「开始自动化」。":"请进入可走动的 Fantasia Territory 领地。");
  if(!string.IsNullOrEmpty(link.Error))ShowError(link.Error);
  string phase=s.WorkPhase switch{"Harvesting"=>"集中收获作物","Gathering"=>"采矿／砍树","Planting"=>"集中播种","Processing"=>"料理／售卖",_=>"等待下一轮"};
  Set(TargetText,link.Enabled&&ours?phase+(s.WorkRemaining>0?$" · 本轮剩余 {s.WorkRemaining}":"")+(s.Target.Length>0?" · "+s.Target:""):"");
  Set(BatchText,s.BatchSeedId>0?$"当前作物：{s.Crops?.FirstOrDefault(c=>c.SeedId==s.BatchSeedId)?.Name??s.BatchSeedId.ToString()}　{(s.BatchTotal>0?$"已确认 {s.BatchPlanted} / {s.BatchTotal}":"等待读取本片农田")}　完成 {s.CompletedBatches} 批":"开启后显示当前批次");
  Set(CookingText,$"已确认制作 {s.Cooked} 份"+(s.CookingState=="pending"?" · 等待服务器确认":""));
  Set(SalesText,$"已确认售卖 {s.SoldItems} 个，获得领地币 {s.SaleCurrency}"+(s.SalesState=="pending"?" · 等待服务器确认":""));
  BatchBar.Maximum=Math.Max(1,s.BatchTotal);BatchBar.Value=Math.Clamp(s.BatchPlanted,0,Math.Max(0,s.BatchTotal));
  CropsGrid.Columns[1].Visibility=s.ActiveFixedSeedId>0?Visibility.Collapsed:Visibility.Visible;
  CropsGrid.ItemsSource=(s.Crops??Array.Empty<CropStock>()).Select(c=>new CropRow(c.Name,c.Required,c.Inventory,c.Growing,ui.Language.Text(c.GrowthSeconds>=60?$"{c.GrowthSeconds/60} 分钟":$"{c.GrowthSeconds} 秒"))).ToArray();
  Set(StatsText,$"耕地 {s.Fields}，空地 {s.EmptyFields}，可收作物 {s.Mature} · 成熟树 {s.Trees} · 成熟矿点 {s.Ores}\n已确认采集 {s.GatherReplies} 次，物品 {s.ReceivedItems} 个 · 本次播种已用 {s.Spent}");
  StartButton.IsEnabled=s.Ready&&!link.Enabled&&!connecting&&!starting;StopButton.IsEnabled=link.Enabled;ConnectButton.IsEnabled=!connecting&&!link.Enabled;_ = UpdateDiagnosticsAsync();
 }
 private void DiagnosticsExpanded(object sender,RoutedEventArgs e){if(initialized){lastDiagnostic=DateTime.MinValue;_ = UpdateDiagnosticsAsync();}}
 private async Task UpdateDiagnosticsAsync()
 {
  if(diagnosticsBusy||connecting||closing||!DiagnosticsPanel.IsExpanded||DateTime.UtcNow-lastDiagnostic<TimeSpan.FromSeconds(1))return;lastDiagnostic=DateTime.UtcNow;diagnosticsBusy=true;var epoch=connectionEpoch;var snapshot=this.snapshot;
  try{var status=await Task.Run(()=>{
  var runtime=TerritoryJson.Read<TerritoryRuntimeStatus>(Path.Combine(root,"runtime.json"));
  string status=$"组件：{runtime?.Runtime??TerritoryIdentity.RuntimeName}\n连接：{runtime?.State??"未连接"}　PID：{runtime?.ProcessId??0}\n最近心跳：{(snapshot==null?"无":new DateTime(Math.Clamp(snapshot.CapturedUtcTicks,0,DateTime.MaxValue.Ticks),DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss"))}\n场景：{snapshot?.Scene}\n农田：{snapshot?.FarmState}\n网络：{snapshot?.Network}\n{runtime?.Error}\n{link.Error}\n";
  try
  {
   var path=Path.Combine(root,"runtime.log");if(File.Exists(path)){using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);stream.Seek(Math.Max(0,stream.Length-16000),SeekOrigin.Begin);using var reader=new StreamReader(stream);status+="\n最近日志\n"+reader.ReadToEnd();}
  }
  catch(IOException){status+="\n日志正在更新，请稍候。";}catch(UnauthorizedAccessException){status+="\n无法读取日志，请检查目录权限。";}
  return status;});if(!closing&&!connecting&&epoch==connectionEpoch)Set(DiagnosticsBox,status);
  }catch(Exception ex){TerritoryDiagnostics.Throttled(root,"diagnostics.failed",ex);}finally{diagnosticsBusy=false;}
 }
 private void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
 {
  if(closing)return;e.Cancel=true;closing=true;connectionEpoch++;lifetime.Cancel();timer.Stop();link.RequestStop();TerritoryDiagnostics.Write(root,"ui.closing");shutdownTask=FinishCloseAsync();
 }
 private async Task FinishCloseAsync(){var stopped=QueueControl(link.Dispose);if(await Task.WhenAny(stopped,Task.Delay(2000))!=stopped)TerritoryDiagnostics.Write(root,"ui.close.lease-expiry-fallback");Close();}
 private sealed record CropRow(string Name,int Required,long Inventory,int Growing,string Growth);
}

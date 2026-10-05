using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BD2Equipment;
public partial class MainWindow:Window
{
 public bool HostedAutomationEnabled => executing || busy;

 readonly WorkerClient worker=new();JsonObject? stock,plan,resumeDisplay;bool ready,busy,executing;List<GearRow> gear=[];JsonArray? rawGear;
 static string S(JsonNode? n)=>L.T(n?.GetValue<string>()??"");
 static long N(JsonNode? n)=>n==null?0:long.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
 internal MainWindow(bool refine=false){L.Initialize();InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageBox);LanguageBox.SelectedIndex=L.Language=="en-US"?1:0;LevelInput.ItemsSource=Enumerable.Range(1,9);LevelInput.SelectedItem=7;TargetInput.ItemsSource=Enumerable.Range(1,24);TargetInput.SelectedItem=24;InitializeFilters();ModeTabs.SelectedIndex=refine?1:0;ready=true;UpdateRefineHint();}
 void Invalidate(){if(!ready||busy)return;plan=null;resumeDisplay=null;ExecuteButton.IsEnabled=false;SummaryText.Text=stock==null?L.T("尚未读取库存"):L.T("设置已变化，请重新计算计划");}
 void SettingsChanged(object sender,RoutedEventArgs e){Invalidate();if(ready){UpdateRefineHint();FilterGear();}}
 void UpdateRefineHint(){int target=(int)TargetInput.SelectedItem;RefineHint.Text=HigherInput.IsChecked==true?L.F("目标 {0} 级；游戏按24级运行整批，结算后达到 {1} 级或更高即停。可能多耗资源，争取更高结果。预算不足自动缩小批量。", new object?[]{target,target}):L.F("游戏内目标设为 {0} 级，按原生达标规则提前结束本批。预算不足自动缩小批量；仅精炼已强化+9的装备。", new object?[]{target});}
 static string RefineMode(JsonObject p)=>S(p["batch_mode"])=="higher"?L.F("整批争取更高（游戏上限24，批后检查≥{0}）", new object?[]{N(p["target"])}):L.F("原生达标即停（游戏目标{0}）", new object?[]{N(p["target"])});
 void ModeChanged(object sender,SelectionChangedEventArgs e){if(e.Source==ModeTabs)Invalidate();}
 void SearchChanged(object sender,TextChangedEventArgs e){if(!ready)return;FilterGear();}

 void Busy(bool value){busy=value;LanguageBox.IsEnabled=!value;ConnectButton.IsEnabled=!value;ModeTabs.IsEnabled=!value;CalculateButton.IsEnabled=!value&&stock!=null;ExecuteButton.IsEnabled=!value&&CanExecute();StopButton.IsEnabled=value&&executing;}
 bool CanExecute()=>plan!=null&&(S(plan["kind"])=="powder"?N(plan["count"])>0:plan["rows"]!.AsArray().Any(r=>N(r!["score"])<N(plan["target"]))&&N(plan["gold"])>0&&N(plan["powder"])>0);
 async Task Do(Func<Task> action){if(busy)return;Busy(true);try{await action();}catch(Exception ex){StatusText.Text=L.T("已暂停：")+L.T(ex.Message);MessageBox.Show(this,L.T(ex.Message),L.T("操作已暂停"),MessageBoxButton.OK,MessageBoxImage.Information);}finally{executing=false;Busy(false);}}
 async void ReadClick(object sender,RoutedEventArgs e){Invalidate();await Do(async()=>{StatusText.Text=L.T("正在连接并读取库存…");var result=await worker.Run(new(){["operation"]="capture"});LoadStock(result["stock"]!.AsObject(),result["gear"]!.AsArray());StatusText.Text=L.T("库存已读取，尚未消耗资源。");if(result["resume"] is JsonObject saved){ModeTabs.SelectedIndex=0;plan=saved["plan"]!.AsObject();resumeDisplay=saved["display"]!.AsObject();ShowPlan();StatusText.Text=L.F("发现未完成计划：已完成 {0:N0} 件，下方仅列剩余 {1:N0} 件。点击确认后接续，不会重做。", new object?[]{N(saved["completed"]?["count"]),N(resumeDisplay["count"])});}});}
 void LoadStock(JsonObject value,JsonArray? views=null){bool sameAccount=S(stock?["account"])==S(value["account"]);stock=value;string account=S(stock["account"]);AccountText.Text=L.T("当前账号 · ")+account[..Math.Min(account.Length,12)];InventoryText.Text=L.F("金币 {0:N0}    天赋药 {1:N0}    精炼粉 {2:N0}", new object?[]{N(stock["gold"]),N(stock["potions"]),N(stock["materials"]?["10"])});
  if(views!=null){rawGear=(JsonArray)views.DeepClone();LoadGear(views,sameAccount);}
 }
 static long? Input(TextBox box,bool optional=false){if(optional&&string.IsNullOrWhiteSpace(box.Text))return null;if(!long.TryParse(box.Text.Trim(),out long n)||n<0||n>2000000000)throw new InvalidOperationException(L.T("请输入0至20亿的整数，不要输入小数或逗号。"));return n;}
 internal JsonObject BuildJob(){if(stock==null)throw new InvalidOperationException(L.T("请先连接并读取库存。"));JsonObject settings;string op;
  if(ModeTabs.SelectedIndex==0){op="powder_plan";settings=new(){["level"]=(int)LevelInput.SelectedItem,["include_five"]=FiveInput.IsChecked==true,["count_limit"]=Input(CountInput,true),["gold_budget"]=Input(GoldInput,true),["potion_budget"]=Input(PotionInput,true)};}
  else{op="refine_plan";settings=new(){["instances"]=new JsonArray(gear.Where(g=>g.Chosen).Select(g=>JsonValue.Create(g.Instance)).ToArray<JsonNode?>()),["target"]=(int)TargetInput.SelectedItem,["gold_budget"]=Input(RefineGoldInput),["powder_budget"]=Input(RefinePowderInput),["batch_mode"]=HigherInput.IsChecked==true?"higher":"target",["batch_size"]=Input(BatchInput)};}
  return new(){["operation"]=op,["stock"]=stock.DeepClone(),["settings"]=settings};
 }
 async void CalculateClick(object sender,RoutedEventArgs e){await Do(async()=>{var job=BuildJob();StatusText.Text=L.T("正在计算资源计划…");resumeDisplay=null;plan=await worker.Run(job);ShowPlan();});}
 string MaterialName(string id)=>S(BD2Equipment.Core.EquipmentPlanner.Catalog["materials"]?[id]) is {Length:>0} name?name:id;
 string Materials(JsonObject p)=>string.Join("\n",p["materials"]!.AsObject().Select(k=>L.F("{0}：消耗 {1:N0}，剩余 {2:N0}", new object?[]{MaterialName(k.Key),N(k.Value),N(p["stock"]!["materials"]![k.Key])-N(k.Value)})));
 void ShowPlan(){if(this.plan==null)return;var plan=resumeDisplay??this.plan;
  if(S(plan["kind"])=="powder"){
   RecipeGrid.ItemsSource=plan["rows"]!.AsArray().Select(r=>new{Name=S(r!["name"]),Count=N(r["count"]).ToString("N0"),Gold=N(r["gold"]).ToString("N0"),Potions=N(r["potions"]).ToString("N0"),Powder=N(r["powder"]).ToString("N0")}).ToList();PlanEmpty.Visibility=N(plan["count"])>0?Visibility.Collapsed:Visibility.Visible;PlanEmpty.Text=L.T("当前资源或预算不足以制作一件。");
   SummaryText.Text=L.F("制作 {0:N0} 件 · 金币 {1:N0} · 用药 {2:N0} · 产粉 {3:N0}", new object?[]{N(plan["count"]),N(plan["gold"]),N(plan["potions"]),N(plan["powder"])});MaterialText.Text=Materials(plan)+L.F("\n天赋药剩余：{0:N0}", new object?[]{N(plan["stock"]!["potions"])-N(plan["potions"])});
  }else SummaryText.Text=L.F("{0} 件装备，目标 {1}级 · 预算 {2:N0} 金币 / {3:N0} 粉 · {4} · 单批≤{5:N0}次", new object?[]{plan["rows"]!.AsArray().Count,N(plan["target"]),N(plan["gold"]),N(plan["powder"]),RefineMode(plan),N(plan["batch_size"])});
  StatusText.Text=L.T("计划已保存，确认后才消耗。库存或账号变化时停止并要求重新核对。");ExecuteButton.IsEnabled=!busy&&CanExecute();
 }
 internal string Confirmation(){var plan=resumeDisplay??this.plan;if(plan==null)throw new InvalidOperationException(L.T("尚无计划"));if(S(plan["kind"])=="powder")return L.F("制作并分解 {0:N0} 件 N 装，强化至 +{1}。\n\n金币：{2:N0}\n天赋药：{3:N0}\n预计精炼粉：{4:N0}\n\n", new object?[]{N(plan["count"]),N(plan["level"]),N(plan["gold"]),N(plan["potions"]),N(plan["powder"])})+Materials(plan)+L.T("\n\n同一计划重试时，只接续尚未完成的数量。");
  return L.F("目标精炼：{0}级\n{1}\n单批最多：{2:N0}次（按剩余预算缩小）\n总金币上限：{3:N0}\n总精炼粉上限：{4:N0}\n\n", new object?[]{N(plan["target"]),RefineMode(plan),N(plan["batch_size"]),N(plan["gold"]),N(plan["powder"])})+string.Join("\n",plan["rows"]!.AsArray().Select(r=>gear.FirstOrDefault(g=>g.Instance==S(r!["instance"]))?.Detail??L.F("{0}  #{1}（当前 {2}级）", new object?[]{S(r!["name"]),S(r["instance"]),N(r["score"])})))+L.T("\n\n全部达标或预算不足即停。精炼随机，不保证预算内达标。停止按钮会等本批完成核账，不再开下一批。")+(S(plan["batch_mode"])=="higher"?L.T("\n已选择游戏上限24：不会在批内刚达到较低目标时停止，可能继续消耗至本批结束。"):"");
 }
 bool Confirm(){var dialog=new Window{Owner=this,Title=L.T("确认资源消耗"),Width=550,Height=540,MinWidth=440,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(Brush)FindResource("AppBackground")};var panel=new DockPanel{Margin=new Thickness(22)};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=new Button{Content=L.T("返回检查"),IsCancel=true,Margin=new Thickness(0,0,10,0)};var ok=new Button{Content=L.T("确认并执行"),Style=(Style)FindResource("PrimaryButton")};ok.Click+=(_,_)=>dialog.DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(ok);DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);panel.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock{Text=Confirmation(),TextWrapping=TextWrapping.Wrap,LineHeight=24}});dialog.Content=panel;return dialog.ShowDialog()==true;}
 async void ExecuteClick(object sender,RoutedEventArgs e){if(busy||!CanExecute()||!Confirm())return;var approved=plan!.DeepClone();Directory.CreateDirectory(WorkerClient.Data);File.Delete(WorkerClient.StopPath);executing=true;
  await Do(async()=>{StatusText.Text=L.T("正在执行，停止按钮只阻止后续批次。");var result=await worker.Run(new(){["operation"]="execute",["plan"]=approved,["approved_id"]=S(approved["id"])},p=>StatusText.Text=L.F("已完成 {0:N0} 件 / 精炼 {1:N0} 次（{2:N0} 批）；金币 {3:N0}，用药 {4:N0}，粉 {5:N0}", new object?[]{N(p["count"]),N(p["attempts"]),N(p["batches"]),N(p["gold"]),N(p["potions"]),N(p["powder"])}));LoadStock(result["expected"]!.AsObject());plan=null;SummaryText.Text=L.T("执行已结束，请重新读取库存以生成下一份计划。");gear=[];GearGrid.ItemsSource=gear;StatusText.Text=S(result["state"]) switch {"completed"=>L.T("本次计划完成。"),"native_stopped"=>L.T("游戏提前结束了本批，已核账并暂停后续操作，请查看记录。"),_=>L.T("预算不足，已停止。")};});
 }
 void StopClick(object sender,RoutedEventArgs e){Directory.CreateDirectory(WorkerClient.Data);File.WriteAllText(WorkerClient.StopPath,"stop");StatusText.Text=L.T("已请求停止，等待当前批次核账，不再提交下一批。");StopButton.IsEnabled=false;}
 void RecordsClick(object sender,RoutedEventArgs e){Directory.CreateDirectory(WorkerClient.Data);File.WriteAllText(Path.Combine(WorkerClient.Data,"HiGHS-LICENSE.txt"),BD2Equipment.Core.J.ResourceText("Highs.LICENSE.txt"));Process.Start(new ProcessStartInfo(WorkerClient.Data){UseShellExecute=true});}
 void WindowClosing(object? sender,CancelEventArgs e){if(!busy)return;e.Cancel=true;if(executing)StopClick(this,new());else StatusText.Text=L.T("正在完成读取或计算，请稍后关闭。");}
 internal async Task Smoke(string folder){worker.OfflineChecks=true;Directory.CreateDirectory(folder);var bridge=await Task.Run(()=>WorkerClient.Invoke(["self-test"]));var result=await worker.Run(new(){["operation"]="self_test"});if(S(result["status"])!="passed")throw new Exception("Worker smoke failed");
  var catalog=BD2Equipment.Core.EquipmentPlanner.Catalog;
  int equipmentId=943032;
  var demoGear=new JsonArray(Enumerable.Range(1,12).Select(i=>(JsonNode)new JsonObject{["instance"]=(10000+i).ToString(),["equipment_id"]=equipmentId,["level"]=i==3?0:9,["ranks"]=i==3?new JsonArray(0,0,0):new JsonArray(4,i%3+2,4),["locked"]=i%2==0,["kept"]=i==4,["equipped"]=i%2!=0}).ToArray());
  var demoStock=new JsonObject{["account"]="demo-account",["player"]="demo",["gold"]=100000,["potions"]=300,["materials"]=new JsonObject{["201"]=300,["204"]=300,["10"]=5000},["protected"]=new JsonObject(),["equipment"]=demoGear};
  var demoDetails=new JsonObject{["characters"]=new JsonArray(new JsonObject{["InvenIndex"]=17,["Id"]=324}),["equipment"]=new JsonArray(demoGear.Select((g,i)=>(JsonNode)new JsonObject{["InvenIndex"]=N(g!["instance"]),["UseChar"]=g["equipped"]!.GetValue<bool>()?17:0,["BaseInfo"]=new JsonObject{["id"]=equipmentId,["level"]=N(g["level"]),["rank"]=g["ranks"]!.DeepClone(),["mainOption"]=new JsonArray(new JsonObject{["groupId"]=1943110,["id"]=5},new JsonObject{["groupId"]=2943110,["id"]=i%2==0?5:6}),["subOption"]=new JsonArray(new JsonObject{["groupId"]=943100,["id"]=10},new JsonObject{["groupId"]=943100,["id"]=i%2==0?10:1},new JsonObject{["groupId"]=943100,["id"]=i%2==0?10:7}),["privateOption"]=new JsonObject{["groupId"]=3943032,["id"]=6}}}).ToArray())};
  var views=await worker.Run(new(){["operation"]="gear_views",["stock"]=demoStock.DeepClone(),["details"]=demoDetails});
  LoadStock(demoStock,views["gear"]!.AsArray());
  CountInput.Text="100";ModeTabs.SelectedIndex=0;plan=await worker.Run(BuildJob());ShowPlan();if(!CanExecute()||!Confirmation().Contains("29,000"))throw new Exception("Powder confirmation incomplete");
  Busy(true);if(ModeTabs.IsEnabled||ConnectButton.IsEnabled||ExecuteButton.IsEnabled)throw new Exception("Busy UI was editable");Busy(false);
  await Render(folder,"powder",1040,800);ModeTabs.SelectedIndex=1;gear[0].Chosen=true;SearchInput.Text="no match";if(!gear[0].Chosen)throw new Exception("Filter lost selection");SearchInput.Text="";plan=await worker.Run(BuildJob());ShowPlan();if(!CanExecute()||plan["rows"]!.AsArray().Count!=1||!Confirmation().Contains("10001"))throw new Exception("Refinement confirmation incomplete");
  TargetInput.SelectedItem=22;HigherInput.IsChecked=true;plan=await worker.Run(BuildJob());ShowPlan();
  if(N(plan["native_target"])!=24||N(plan["batch_size"])!=5000||!Confirmation().Contains(L.T("批内")))throw new Exception("Higher batch mode missing");
  HigherInput.IsChecked=false;TargetInput.SelectedItem=20;plan=await worker.Run(BuildJob());ShowPlan();
  if(N(plan["native_target"])!=20||N(plan["target"])!=20||!Confirmation().Contains(L.T("游戏目标20")))throw new Exception("Native target mode missing");
  HigherInput.IsChecked=true;TargetInput.SelectedItem=22;plan=await worker.Run(BuildJob());ShowPlan();
  await CheckGearBrowser();await CheckLanguagePreservation(folder);await Render(folder,"refine",1240,960);await Render(folder,"refine-small",900,720);ModeTabs.SelectedIndex=0;await Render(folder,"powder-small",820,650);if(RecipeGrid.ActualHeight<72)throw new Exception("Powder recipe list collapsed");
  CountInput.Text="-1";bool rejected=false;try{BuildJob();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Negative budget accepted");
  await File.WriteAllTextAsync(Path.Combine(folder,"smoke.json"),new JsonObject{["status"]="passed",["realGameTouched"]=false,["screenshots"]=5,["engine"]="C# in-process",["bridgeChecks"]=bridge,["worker"]=result.DeepClone(),["checks"]="confirmation, busy lock, filter selection, input validation, narrow footer, exact instance filters, owner, substat count, selected-only, range validation, language round-trip, bilingual search, preference/journal format"}.ToJsonString());
 }
 async Task Render(string folder,string name,int width,int height){Width=width;Height=height;
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
  var body=(FrameworkElement)Content;var button=ExecuteButton.TransformToAncestor(body).Transform(new Point(0,0));if(button.Y+ExecuteButton.ActualHeight>body.ActualHeight+1)throw new Exception("Footer clipped");
  var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(folder,name+".png"));encoder.Save(stream);
 }
}

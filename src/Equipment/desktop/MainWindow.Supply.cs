using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using BD2Equipment.Core;
namespace BD2Equipment;
public partial class MainWindow
{
 bool pendingSupply;
 void SetPendingSupply(bool value){pendingSupply=value;ResolveSupplyButton.Visibility=value?Visibility.Visible:Visibility.Collapsed;}
 async void ResolveSupplyClick(object sender,RoutedEventArgs e){
  if(busy||stock==null)return;
  if(MessageBox.Show(this,L.K("equipment.supply.resolveConfirm"),L.K("equipment.supply.resolve"),MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
  File.Delete(WorkerClient.StopPath);executing=true;
  await Do(async()=>{var result=await worker.Run(new(){["operation"]="resolve_supply",["stock"]=stock.DeepClone()});LoadStock(result["stock"]!.AsObject());SetPendingSupply(false);plan=null;resumeDisplay=null;SummaryText.Text=L.K("equipment.supply.resolved");StatusText.Text=SummaryText.Text;});
 }
 string SupplyPreferencesPath=>Path.Combine(WorkerClient.Data,"supply-preferences.json");
 void LoadSupplySettings(){
  bool wasReady=ready;ready=false;
  try{JsonObject saved=File.Exists(SupplyPreferencesPath)?J.Read(SupplyPreferencesPath):new();var values=saved[J.S(stock?["account"])];SupplyInput.IsChecked=J.B(values?["enabled"]);WoodReserveInput.Text=N(values?["wood_reserve"]).ToString();RiceReserveInput.Text=N(values?["rice_reserve"]).ToString();}
  catch(Exception ex)when(ex is IOException or System.Text.Json.JsonException or InvalidOperationException){SupplyInput.IsChecked=false;WoodReserveInput.Text="0";RiceReserveInput.Text="0";}
  finally{ready=wasReady;}
 }
 void SaveSupplySettings(){
  var values=new JsonObject{["enabled"]=SupplyInput.IsChecked==true,["wood_reserve"]=Input(WoodReserveInput),["rice_reserve"]=Input(RiceReserveInput)};
  var saved=File.Exists(SupplyPreferencesPath)?J.Read(SupplyPreferencesPath):new JsonObject();saved[J.S(stock?["account"])]=values;J.Write(SupplyPreferencesPath,saved);
 }
 string SupplySummary(JsonObject p){
  string missing=string.Join("\n",J.A(p["supply_unavailable"]).Select(r=>L.K("equipment.supply.unavailableSummary",L.K(N(r["typeGroupId"])==0?"equipment.supply.woodRoute":"equipment.supply.goldRoute"))));
  if(p["supply"] is not JsonObject supply)return missing.Length==0?"":"\n"+missing;
  var lines=J.A(supply["routes"]).Where(r=>N(r["count"])>0).Select(r=>L.K("equipment.supply.route",L.K(N(r["table"]?["typeGroupId"])==0?"equipment.supply.woodRoute":"equipment.supply.goldRoute"),N(r["table"]?["difficulty"])+1,N(r["count"]),N(r["count"])*N(r["ap"]),N(r["count"])*N(r["wood"]),N(r["count"])*N(r["gold"])));
  return "\n"+string.Join("\n",lines)+"\n"+L.K("equipment.supply.total",N(supply["rice"]),N(supply["free_rice"]),N(supply["bonus_rice"]),N(supply["reserve"]),N(supply["wood_reserve"]))+"\n"+L.K("equipment.supply.estimate")+(missing.Length==0?"":"\n"+missing);
 }
 string SupplyProgress(JsonObject p)=>N(p["sweeps"])>0?L.K("equipment.supply.progress",N(p["sweeps"]),N(p["rice"]),N(p["wood_gain"]),N(p["gold_gain"]))+"\n":"";
 internal async Task CheckLayoutForSmoke(string folder){
  // Synthetic rendering only; never connect, plan or dispatch game actions.
  Directory.CreateDirectory(folder);L.AuditKeys();
  ModeTabs.SelectedIndex=0;SupplyInput.IsChecked=false;
  WoodReserveInput.Text="41";RiceReserveInput.Text="13";
  foreach(var input in new[]{CountInput,GoldInput,PotionInput})input.Text="";
  await Render(folder,"powder-off",900,720);
  if(RiceReserveInput.IsVisible||!WoodReserveInput.IsVisible)throw new Exception("Supply folding hides wood reserve or exposes rice settings");
  foreach(var input in new[]{CountInput,GoldInput,PotionInput}){
   var holder=(System.Windows.Controls.Grid)input.Parent;var hint=holder.Children.OfType<System.Windows.Controls.TextBlock>().Single();
   if(!hint.IsVisible||hint.Text!=L.K("equipment.ui.unlimited")||hint.IsHitTestVisible)throw new Exception("Blank limit placeholder missing or intercepting input");
   input.Text="123";await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
   if(hint.IsVisible||input.Text!="123")throw new Exception("Limit placeholder overlaps input or changes its value");input.Text="";
  }
  SupplyInput.IsChecked=true;await Render(folder,"powder-on",900,720);
  if(!RiceReserveInput.IsVisible||!RiceReserveInput.IsEnabled)throw new Exception("Enabled supply has no editable rice reserve");
  RiceReserveInput.BringIntoView();await Render(folder,"powder-on-details",900,720);
  SupplyInput.IsChecked=false;SupplyInput.IsChecked=true;
  if(WoodReserveInput.Text!="41"||RiceReserveInput.Text!="13")throw new Exception("Folding supply resets reserves");
  SupplyInput.IsChecked=false;WoodReserveInput.Text=RiceReserveInput.Text="0";
  ModeTabs.SelectedIndex=1;
  var sample=new GearRow{Instance="10001",Name=L.T("罗安")+" · UR",Rarity="UR",Owner=L.T("罗安"),Ranks="S / A / S",Level=9,Score=22,Cost=L.T("80金币 / 20粉"),Eligible=true};
  GearGrid.ItemsSource=new[]{sample};GearGrid.SelectedItem=sample;
  await Render(folder,"refine-cost",1040,800);
  if(GearDetailExpander.IsExpanded||!GearDetail.Text.Contains(sample.Cost))throw new Exception("Folded details lose equipment costs");
  var row=(System.Windows.Controls.DataGridRow)GearGrid.ItemContainerGenerator.ContainerFromItem(sample);
  IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
  var cost=Descendants(row).OfType<System.Windows.Controls.TextBlock>().Single(x=>x.Text==sample.CostLabel);
  var origin=cost.TransformToAncestor(row).Transform(new Point(0,0));
  if(origin.Y<0||origin.Y+cost.ActualHeight>row.ActualHeight+1)throw new Exception("Refinement cost clipped by row height");
  ((System.Windows.Controls.ScrollViewer)((System.Windows.Controls.Grid)GearGrid.Parent).Parent).ScrollToEnd();
  await Render(folder,"refine-cost-visible",900,720);
  GearDetailExpander.IsExpanded=true;await Render(folder,"refine-details",900,720);
  GearDetailExpander.IsExpanded=false;GearGrid.ItemsSource=null;ModeTabs.SelectedIndex=0;
 }
 void CheckSupplyUi(){
  L.AuditKeys();var initial=BuildJob()["settings"]!;
  if(J.B(initial["supply_enabled"])||N(initial["wood_reserve"])!=0||N(initial["rice_reserve"])!=0)throw new Exception("Supply must start opt-in with zero reserves");
  SupplyInput.IsChecked=true;WoodReserveInput.Text="41";RiceReserveInput.Text="13";var job=BuildJob();
  if(plan!=null||!J.B(job["settings"]!["supply_enabled"])||N(job["settings"]!["wood_reserve"])!=41||N(job["settings"]!["rice_reserve"])!=13)throw new Exception("Supply inputs do not invalidate/bind the current plan");
  SetPendingSupply(true);UpdatePrimary();if(ExecuteButton.IsEnabled||ResolveSupplyButton.Visibility!=Visibility.Visible)throw new Exception("Pending supply lacks a reachable recovery action");
  SetPendingSupply(false);SupplyInput.IsChecked=false;WoodReserveInput.Text="0";RiceReserveInput.Text="0";SaveSupplySettings();UpdatePrimary();
 }
}

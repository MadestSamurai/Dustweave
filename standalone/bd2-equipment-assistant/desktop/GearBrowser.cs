using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace BD2Equipment;
public partial class MainWindow
{
 bool filtering,changingSelection;
 List<GearRow> visibleGear=[];
 static string Pick(ComboBox box)=>(box.SelectedItem?.ToString()??box.Text).Trim();
 static string NormalizeSearch(string text)=>text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
 static bool Contains(string text,string query)=>NormalizeSearch(text).Contains(NormalizeSearch(query));
 static bool Matches(string actual,ComboBox choice)=>string.IsNullOrWhiteSpace(Pick(choice))||Pick(choice)==L.T("全部")||Contains(actual,Pick(choice));
 void Choices(ComboBox box,IEnumerable<string> values,string fallback="全部")
 {
  fallback=L.T(fallback);string prior=Pick(box);var choices=new[]{L.T("全部")}.Concat(values).Distinct().ToArray();box.ItemsSource=choices;box.SelectedItem=choices.Contains(prior)?prior:choices.Contains(fallback)?fallback:choices[0];
 }
 void InitializeFilters()
 {
  filtering=true;
  Choices(SlotFilter,[L.T("武器"),L.T("护甲"),L.T("头盔"),L.T("饰品"),L.T("手套")]);Choices(KindFilter,[L.T("普通装备"),L.T("专属装备"),L.T("魔兽装备")]);Choices(RarityFilter,["UR","SR","R","N"]);Choices(QualityFilter,["I","II","III","IV"]);
  Choices(WornFilter,[L.T("已穿戴"),L.T("未穿戴")]);Choices(LockFilter,[L.T("已锁定"),L.T("未锁定")]);Choices(KeepFilter,[L.T("保留中"),L.T("未保留")]);Choices(LevelFilter,Enumerable.Range(0,10).Select(i=>"+"+i));
  Choices(OwnerFilter,[]);Choices(ExclusiveFilter,[]);Choices(MainStatFilter,[]);Choices(SubStatFilter,[]);
  SubCountFilter.ItemsSource=new[]{L.T("1条"),L.T("2条"),L.T("3条")};SubCountFilter.SelectedIndex=0;
  ScoreMinFilter.ItemsSource=ScoreMaxFilter.ItemsSource=Enumerable.Range(0,25).ToArray();ScoreMinFilter.SelectedItem=0;ScoreMaxFilter.SelectedItem=24;
  SortFilter.ItemsSource=new[]{L.T("当前穿戴优先"),L.T("精炼等级从低到高"),L.T("精炼等级从高到低"),L.T("装备名称"),L.T("强化等级从低到高"),L.T("实例ID从大到小")};SortFilter.SelectedIndex=0;
  filtering=false;
 }
 void LoadGear(JsonArray views,bool sameAccount)
 {
  var selected=sameAccount?gear.Where(g=>g.Chosen).Select(g=>g.Instance).ToHashSet():[];
  filtering=true;
  gear=views.Select(GearRow.From).ToList();
  foreach(var g in gear){g.Chosen=g.Eligible&&selected.Contains(g.Instance);g.PropertyChanged+=(_,_)=>{if(changingSelection)return;Invalidate();FilterGear();};}
  Choices(OwnerFilter,gear.Where(g=>g.Equipped).Select(g=>g.Owner).Order());Choices(ExclusiveFilter,gear.Where(g=>g.Exclusive!=L.T("无")).Select(g=>g.Exclusive).Order());
  Choices(MainStatFilter,gear.SelectMany(g=>g.MainStats).Order());Choices(SubStatFilter,gear.SelectMany(g=>g.SubStats).Order());
  filtering=false;FilterGear();
 }
 void FilterChanged(object sender,RoutedEventArgs e){if(ready&&!filtering)FilterGear();}
 void FilterTextChanged(object sender,TextChangedEventArgs e)=>FilterChanged(sender,e);
 void FilterGear()
 {
  if(!ready||filtering)return;
  string? focused=(GearGrid.SelectedItem as GearRow)?.Instance;
  int min=ScoreMinFilter.SelectedItem is int lo?lo:0,max=ScoreMaxFilter.SelectedItem is int hi?hi:24;
  var tokens=SearchInput.Text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
  IEnumerable<GearRow> found=gear;
  if(OnlySelectedFilter.IsChecked==true)found=found.Where(g=>g.Chosen);
  else found=found.Where(g=>tokens.All(t=>Contains(g.Search,t))&&Matches(g.Slot,SlotFilter)&&Matches(g.Kind,KindFilter)&&Exact(g.Rarity,RarityFilter)&&Exact(g.Quality,QualityFilter)&&Matches(g.Owner,OwnerFilter)&&Matches(g.Exclusive,ExclusiveFilter)&&Exact(g.Equipped?L.T("已穿戴"):L.T("未穿戴"),WornFilter)&&Exact(g.Locked?L.T("已锁定"):L.T("未锁定"),LockFilter)&&Exact(g.Kept?L.T("保留中"):L.T("未保留"),KeepFilter)&&Exact(g.Enhancement,LevelFilter)&&g.Score>=min&&g.Score<=max&&(BelowTargetFilter.IsChecked!=true||g.Score<(TargetInput.SelectedItem is int target?target:24))&&(Pick(MainStatFilter)==L.T("全部")||g.MainStats.Contains(Pick(MainStatFilter)))&&(Pick(SubStatFilter)==L.T("全部")||g.SubStats.Count(x=>x==Pick(SubStatFilter))>=SubCountFilter.SelectedIndex+1));
  // Stable instance tie-breakers keep same-name copies separate and deterministic.
  IOrderedEnumerable<GearRow> ordered=found.OrderByDescending(g=>PinSelectedFilter.IsChecked==true&&g.Chosen);
  ordered=SortFilter.SelectedIndex switch{1=>ordered.ThenBy(g=>g.Score),2=>ordered.ThenByDescending(g=>g.Score),3=>ordered.ThenBy(g=>g.BaseName),4=>ordered.ThenBy(g=>g.Level),5=>ordered.ThenByDescending(g=>long.Parse(g.Instance)),_=>ordered.ThenByDescending(g=>g.Equipped).ThenBy(g=>g.Owner).ThenBy(g=>g.BaseName)};
  visibleGear=ordered.ThenBy(g=>g.BaseName).ThenBy(g=>g.Score).ThenBy(g=>g.Instance,StringComparer.Ordinal).ToList();
  GearGrid.ItemsSource=visibleGear;
  GearGrid.SelectedItem=visibleGear.FirstOrDefault(g=>g.Instance==focused)??visibleGear.FirstOrDefault();
  int selected=gear.Count(g=>g.Chosen),hidden=selected-visibleGear.Count(g=>g.Chosen);
  GearHint.Foreground=(Brush)FindResource(min>max&&OnlySelectedFilter.IsChecked!=true?"Error":"MutedInk");
  GearHint.Text=min>max&&OnlySelectedFilter.IsChecked!=true?L.T("精炼最低等级高于最高等级，请调整范围；已保留输入和勾选。"):L.F("显示 {0} / {1} 件 · 已选 {2} 件", new object?[]{visibleGear.Count,gear.Count,selected})+(hidden>0?L.F("（其中 {0} 件不在当前筛选内，可点「只看已选」）", new object?[]{hidden}):"")+(visibleGear.Count==0?L.T("。没有匹配装备，请减少筛选条件。"):L.T("。筛选不改变勾选；按库存列表顺序精炼。"));
 }
 static bool Exact(string actual,ComboBox box)=>Pick(box)==L.T("全部")||Pick(box)==actual;
 void ResetFiltersClick(object sender,RoutedEventArgs e)
 {
  filtering=true;SearchInput.Text="";
  foreach(var box in new[]{SlotFilter,KindFilter,RarityFilter,QualityFilter,OwnerFilter,ExclusiveFilter,WornFilter,LockFilter,KeepFilter,LevelFilter,MainStatFilter,SubStatFilter})box.SelectedIndex=0;
  ScoreMinFilter.SelectedItem=0;ScoreMaxFilter.SelectedItem=24;SubCountFilter.SelectedIndex=0;OnlySelectedFilter.IsChecked=BelowTargetFilter.IsChecked=false;SortFilter.SelectedIndex=0;filtering=false;FilterGear();
 }
 void SelectVisibleClick(object sender,RoutedEventArgs e){if(busy)return;changingSelection=true;foreach(var g in visibleGear.Where(g=>g.Eligible))g.Chosen=true;changingSelection=false;Invalidate();FilterGear();}
 void ClearSelectedClick(object sender,RoutedEventArgs e){if(busy)return;changingSelection=true;foreach(var g in gear)g.Chosen=false;changingSelection=false;Invalidate();FilterGear();}
 void GearFocused(object sender,SelectionChangedEventArgs e){GearDetail.Text=GearGrid.SelectedItem is GearRow g?g.Detail:L.T("没有匹配装备。清除筛选可重新查看；已勾选的装备不会丢失。");}
 async Task CheckGearBrowser()
 {
  ResetFiltersClick(this,new());
  if(visibleGear.Count!=12||gear[2].Eligible)throw new Exception("Missing unenhanced equipment");
  SearchInput.Text=L.T("Loen 魔攻");if(visibleGear.Count!=12)throw new Exception("Multilingual multi-term search failed");
  OwnerFilter.SelectedItem=L.T("罗安");if(visibleGear.Count!=6)throw new Exception("Wearer filter failed");
  SubStatFilter.SelectedItem=L.T("暴伤");SubCountFilter.SelectedIndex=2;if(visibleGear.Count!=6)throw new Exception("Repeated substat filter failed");
  LockFilter.SelectedItem=L.T("已锁定");if(visibleGear.Count!=0)throw new Exception("Lock filter failed");
  OnlySelectedFilter.IsChecked=true;if(visibleGear.Count!=1||visibleGear[0].Instance!="10001")throw new Exception("Selected view lost hidden gear");
  ResetFiltersClick(this,new());ScoreMinFilter.SelectedItem=24;ScoreMaxFilter.SelectedItem=20;if(visibleGear.Count!=0||!GearHint.Text.Contains(L.T("高于")))throw new Exception("Inverted range accepted");
  ResetFiltersClick(this,new());RarityFilter.SelectedItem="R";if(visibleGear.Count!=0)throw new Exception("R incorrectly matches UR");
  ResetFiltersClick(this,new());QualityFilter.SelectedItem="I";if(visibleGear.Count!=0)throw new Exception("I incorrectly matches IV");
  ResetFiltersClick(this,new());SelectVisibleClick(this,new());if(gear.Count(g=>g.Chosen)!=11)throw new Exception("Mass select included unenhanced gear");
  ClearSelectedClick(this,new());gear[0].Chosen=true;
  plan=await worker.Run(BuildJob());ShowPlan();if(!Confirmation().Contains(L.T("罗安"))||!Confirmation().Contains(L.T("副词条")))throw new Exception("Instance confirmation lacks detail");
 }
 internal sealed class GearRow:INotifyPropertyChanged
 {
  public event PropertyChangedEventHandler? PropertyChanged;bool chosen;
  public bool Chosen{get=>chosen;set{if(chosen==value)return;chosen=value;PropertyChanged?.Invoke(this,new(nameof(Chosen)));}}
  public string Instance{get;init;}="";public string Name{get;init;}="";public string BaseName{get;init;}="";public string Rarity{get;init;}="";public string Quality{get;init;}="";public string Slot{get;init;}="";public string Kind{get;init;}="";public string Owner{get;init;}="";public string Exclusive{get;init;}=L.T("无");public string Ranks{get;init;}="";public string Main{get;init;}="";public string Private{get;init;}="";public string Sub{get;init;}="";public string Cost{get;init;}="";public string State{get;init;}="";public string Search{get;init;}="";
  public string[] MainStats{get;init;}=[];public string[] SubStats{get;init;}=[];
  public long Score{get;init;}public long Level{get;init;}public bool Locked{get;init;}public bool Kept{get;init;}public bool Equipped{get;init;}public bool Eligible{get;init;}
  public string IdLabel=>"#"+Instance;public string ScoreLabel=>Score+L.T("级");public string Enhancement=>"+"+Level;public string Category=>$"{Slot} · {Kind}";public string ExclusiveLabel=>Exclusive==L.T("无")?L.T("通用"):L.T("专属：")+Exclusive;
  public string Flags=>string.Join(" · ",new[]{Locked?L.T("已锁定"):L.T("未锁定"),Kept?L.T("保留中"):""}.Where(x=>x.Length>0));
  public string MainLines=>Main.Replace(" · ","\n")+(Private.Length>0?L.T("\n专属 ")+Private:"");public string SubLines=>Sub.Replace(" · ","\n");
  public string Identity=>$"{Name} · {Owner} · {ScoreLabel} · {Ranks} · #{Instance}";
  public string EligibilityText=>Eligible?L.T("勾选此件装备；筛选后仍保留。"):L.T("未强化至+9，当前仅供查看。");
  public string Detail=>L.F("{0}   {1} / {2} / 强化{3}   {4}   {5}\n主词条：{6}", new object?[]{Identity,Slot,Kind,Enhancement,ExclusiveLabel,Flags,Main})+(Private.Length>0?L.F("；专属：{0}", new object?[]{Private}):"")+L.F("\n副词条：{0}   每次精炼：{1}", new object?[]{Sub,Cost});
  public static GearRow From(JsonNode? v)=>new(){Instance=S(v?["instance"]),Name=S(v?["name"]),BaseName=S(v?["base_name"]),Rarity=S(v?["rarity"]),Quality=S(v?["quality"]),Slot=S(v?["slot"]),Kind=S(v?["kind"]),Owner=S(v?["owner"]),Exclusive=S(v?["exclusive"]),Ranks=S(v?["ranks"]),Main=S(v?["main"]),Private=S(v?["private"]),Sub=S(v?["sub"]),Cost=S(v?["cost"]),State=S(v?["state"]),Search=(v?["search"]?.GetValue<string>()??"")+" "+S(v?["search"]),Score=N(v?["score"]),Level=N(v?["level"]),Eligible=v?["eligible"]?.GetValue<bool>()??false,Equipped=v?["equipped"]?.GetValue<bool>()??false,Locked=v?["locked"]?.GetValue<bool>()??false,Kept=v?["kept"]?.GetValue<bool>()??false,MainStats=v?["main_stats"]?.AsArray().Select(S).ToArray()??[],SubStats=v?["sub_stats"]?.AsArray().Select(S).ToArray()??[]};
 }
}

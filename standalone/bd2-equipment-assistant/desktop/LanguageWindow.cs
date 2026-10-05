using System.Windows;
using System.Windows.Controls;
namespace BD2Equipment;
public partial class MainWindow {
 void LanguageChanged(object sender,SelectionChangedEventArgs e){
  if(!ready||busy)return;string previous=L.Language;string next=AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") as string ?? (LanguageBox.SelectedIndex==1?"en-US":"zh-CN");if(previous==next)return;
  var boxes=new[]{SlotFilter,KindFilter,RarityFilter,QualityFilter,OwnerFilter,ExclusiveFilter,WornFilter,LockFilter,KeepFilter,LevelFilter,MainStatFilter,SubStatFilter};var selections=boxes.Select(Pick).ToArray();
  int sort=SortFilter.SelectedIndex,sub=SubCountFilter.SelectedIndex;var min=ScoreMinFilter.SelectedItem;var max=ScoreMaxFilter.SelectedItem;
  ready=false;L.Select(next);InitializeFilters();if(stock!=null)LoadStock(stock,rawGear);
  for(int i=0;i<boxes.Length;i++){string value=L.ConvertToCurrent(selections[i],previous);if(boxes[i].Items.Contains(value))boxes[i].SelectedItem=value;else boxes[i].Text=value;}
  SortFilter.SelectedIndex=sort;SubCountFilter.SelectedIndex=sub;ScoreMinFilter.SelectedItem=min;ScoreMaxFilter.SelectedItem=max;
  ready=true;UpdateRefineHint();FilterGear();if(plan!=null)ShowPlan();else SummaryText.Text=L.T(stock==null?"尚未读取库存":"尚未计算");
  StatusText.Text=L.T("语言已切换，计划和勾选已保留。");
 }
 async Task CheckLanguagePreservation(string folder){
  string original=L.Language;var saved=plan?.ToJsonString();var selected=gear.Where(g=>g.Chosen).Select(g=>g.Instance).ToArray();
  LanguageBox.SelectedIndex=original=="en-US"?0:1;
  if(plan?.ToJsonString()!=saved||!gear.Where(g=>g.Chosen).Select(g=>g.Instance).SequenceEqual(selected))throw new Exception("Language switch changed the plan or selection");
  await Render(folder,"language-switched",1240,960);LanguageBox.SelectedIndex=original=="en-US"?1:0;
  if(plan?.ToJsonString()!=saved||!gear.Where(g=>g.Chosen).Select(g=>g.Instance).SequenceEqual(selected))throw new Exception("Language round-trip changed the plan");
  foreach(var path in System.IO.Directory.EnumerateFiles(WorkerClient.Data,"*.json"))BD2Equipment.Core.J.Read(path);
  SearchInput.Text="罗安";if(visibleGear.Count!=12)throw new Exception("Chinese search lost in English mode");SearchInput.Text="Loen";if(visibleGear.Count!=12)throw new Exception("English search lost in Chinese mode");SearchInput.Text="";
 }

}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace BD2InfiniteGacha.Desktop;
public partial class AddCostumesWindow:Window
{
    private readonly ListCollectionView view;private readonly WindowLanguage language;
    public int[] SelectedIds{get;private set;}=[];
    public int TargetGroup{get;private set;}=2;
    public AddCostumesWindow(IEnumerable<CostumeRow> source,ResourceDictionary resources)
    {
        InitializeComponent();language=new WindowLanguage(this,Ui.Catalog.Language);Closed+=(_,_)=>language.Dispose();Choices.Style=(Style)resources["TargetList"];
        view=new ListCollectionView(source.ToArray()){Filter=Matches};Choices.ItemsSource=view;Refresh();
        Loaded+=(_,_)=>Query.Focus();
    }
    public void ApplyHostedLanguage(string code){language.Select(code);Selection();}
    private bool Matches(object value)
    {
        var row=(CostumeRow)value;string q=Query.Text.Trim();
        return (q.Length==0||(row.Character+row.Name+row.Costume.Id).Contains(q,StringComparison.OrdinalIgnoreCase))
            && (Enhancement.SelectedIndex switch{1=>row.Costume.Level>=-1&&row.Costume.Level<5,2=>row.Costume.Level==5,_=>true});
    }
    private void Refresh(){if(view==null)return;view.Refresh();Empty.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed;Selection();}
    private void Selection(){if(view==null)return;Count.Text=$"{view.Count} 件 · 已选 {Choices.SelectedItems.Count}";AddB.IsEnabled=AddA.IsEnabled=Choices.SelectedItems.Count>0;}
    private void Query_Changed(object sender,TextChangedEventArgs e)=>Refresh();
    private void Enhancement_Changed(object sender,SelectionChangedEventArgs e)=>Refresh();
    private void Choices_Changed(object sender,SelectionChangedEventArgs e)=>Selection();
    private void Accept(int group){if(Choices.SelectedItems.Count==0)return;SelectedIds=Choices.SelectedItems.Cast<CostumeRow>().Select(c=>c.Costume.Id).ToArray();TargetGroup=group;DialogResult=true;}
    private void AddB_Click(object sender,RoutedEventArgs e)=>Accept(2);
    private void AddA_Click(object sender,RoutedEventArgs e)=>Accept(1);
    internal void SmokeSelection(int id,int group)
    {
        if(Choices.Items.Cast<CostumeRow>().Any(c=>c.Group!=0))throw new Exception("Grouped item in picker");
        Query.Text=id.ToString();var item=Choices.Items.Cast<CostumeRow>().Single(c=>c.Costume.Id==id);Choices.SelectedItem=item;
        if(!AddB.IsEnabled||!AddA.IsEnabled)throw new Exception("Picker selection not enabled");Accept(group);
    }
}


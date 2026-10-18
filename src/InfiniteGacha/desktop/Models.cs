using System.ComponentModel;
namespace BD2InfiniteGacha.Desktop;
public sealed class CostumeRow:INotifyPropertyChanged
{
    public required Costume Costume{get;init;}public bool Available{get;init;}=true;
    public string Character=>string.IsNullOrWhiteSpace(Costume.Character)?Ui.Text("服装 #")+Costume.Id:Costume.Character;public string Name=>string.IsNullOrWhiteSpace(Costume.Name)?Ui.Text("名称尚未读取"):Costume.Name;
    public string LevelLabel=>!Available?Ui.Text("不可抽"):Costume.Level==-2?Ui.Text("待读取"):Costume.Level==-1?Ui.Text("未拥有"):"+"+Costume.Level;
    private int group;
    public int Group{get=>group;set{if(value==group)return;group=value;PropertyChanged?.Invoke(this,new(nameof(Group)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class RuleRowModel:INotifyPropertyChanged
{
    public required RuleRow Row{get;init;}public string Label=>Ui.Text($"A = {Row.A} 时，B 至少");
    public bool Enabled{get=>Row.Enabled;set{Row.Enabled=value;Changed();}}
    public string B{get=>Row.B;set{Row.B=value;Changed();}}
    public string Error=>Enabled?Ui.Text(Rules.RowError(Row.A,Row.B)):"";
    public bool HasError=>Error.Length>0;
    public void Refresh()=>Changed();
    private void Changed(){PropertyChanged?.Invoke(this,new(""));}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record ResultCard(string Title,string Name,System.Windows.Media.Brush Background);

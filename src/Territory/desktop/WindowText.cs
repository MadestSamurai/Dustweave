using System.Windows;
using System.Windows.Controls;
using BD2Territory.Localization;
namespace BD2Territory.Desktop;
internal sealed class WindowText
{
 internal readonly LanguageCatalog Language;
 readonly Window window; readonly Dictionary<TextBlock,string> texts=new();
 internal WindowText(Window window,string root){this.window=window;Language=new(LanguagePreference.Read(root));Apply();}
 internal void Set(FrameworkElement target,string source){if(target is TextBlock block){texts[block]=source;block.Text=Language.Text(source);}else if(target is TextBox box)box.Text=source;}
 internal void Apply(){foreach(var label in UiLabels.All){window.Resources[label.Key]=Language.Text(label.Value);Application.Current.Resources[label.Key]=Language.Text(label.Value);}foreach(var pair in texts)pair.Key.Text=Language.Text(pair.Value);window.Title=Language.Text(window is TerritoryWindow?"领地自动化":"布局工具");}
}
public partial class TerritoryWindow
{
 WindowText ui=null!;
 void InitializeLanguage(){ui=new(this,TerritoryStorage.Preferences(root));LanguageBox.SelectedIndex=ui.Language.Language=="zh-CN"?0:1;}
 void Set(FrameworkElement target,string source)=>ui.Set(target,source);
 void LanguageChanged(object sender,SelectionChangedEventArgs e){if(!initialized)return;try{var selected=(string)((ComboBoxItem)LanguageBox.SelectedItem).Tag;LanguagePreference.Save(TerritoryStorage.Preferences(root),selected);ui.Language.Select(selected);ui.Apply();RefreshTreeStages();recipeCatalogKey="";if(connected)_ = RefreshAsync();}catch(Exception ex){ShowError(ex.Message);}}
}
public partial class LayoutWindow
{
 WindowText ui=null!;
 void InitializeLanguage()=>ui=new(this,root);
 public void ApplyHostedLanguage(string language){ui.Language.Select(language);ui.Apply();}
 void Set(FrameworkElement target,string source)=>ui.Set(target,source);
}

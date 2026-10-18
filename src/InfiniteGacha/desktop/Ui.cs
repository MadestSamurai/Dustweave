using BD2InfiniteGacha.Localization;
namespace BD2InfiniteGacha.Desktop;
internal static class Ui
{
 internal static LanguageCatalog Catalog=new("zh-CN");
 internal static string Text(string value)=>Catalog.Text(value);
}

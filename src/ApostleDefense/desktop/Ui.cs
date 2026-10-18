using BD2ApostleDefense.Localization;
namespace BD2ApostleDefense.Desktop;
internal static class Ui
{
 public static LanguageCatalog Catalog {get;set;}=new();
 public static bool English=>Catalog.Language=="en-US";
 public static string Text(string value)=>Catalog.Text(value);
 public static string ShortElement(int element)=>new[]{"Wa","Fi","Wi","Li","Da"}[Math.Clamp(element,0,4)];
}

using System.Text.Json;
namespace BD2SecretVision.Localization;
public sealed class Catalog
{
    public string Language { get; private set; }
    private Dictionary<string,string> messages;
    public Catalog(string language){Language=language;messages=Read(language);}
    public void Select(string language){messages=Read(language);Language=language;}
    public static Dictionary<string,string> Read(string language)
    {
        if(language=="zh-TW" && AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText") is Func<string,string> traditional)return Read("zh-CN").ToDictionary(p=>p.Key,p=>traditional(p.Value));
        using var stream=typeof(Catalog).Assembly.GetManifestResourceStream("Language."+language+".json")??throw new InvalidOperationException("Missing language: "+language);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    public string this[string key]=>messages.TryGetValue(key,out var text)?text:messages["failure"];
    public string Format(string key,params object[] args)=>string.Format(this[key],args);
    public string Error(Exception error)=>ErrorKind.Status(error) is "file-access" or "permission"?this[ErrorKind.Status(error)]:this[error.Message];
}

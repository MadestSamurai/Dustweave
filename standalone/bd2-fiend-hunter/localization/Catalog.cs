using System.Globalization;
using System.Text.Json;
namespace BD2FiendHunter.Localization;
public sealed class Catalog {
 public string Language{get;private set;} public Dictionary<string,string> Messages{get;private set;}
 public Catalog(string language){Language=language;Messages=Read(language);}
 public static Dictionary<string,string> Read(string language){if(language=="zh-TW" && AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText") is Func<string,string> traditional)return Read("zh-CN").ToDictionary(p=>p.Key,p=>traditional(p.Value));using var s=typeof(Catalog).Assembly.GetManifestResourceStream("Language."+language+".json")??throw new InvalidDataException("Unknown language");return JsonSerializer.Deserialize<Dictionary<string,string>>(s)!;}
 public void Select(string language){var next=Read(language);Language=language;Messages=next;}
 public string this[string key]=>Messages.TryGetValue(key,out var value)?value:Messages["error"];
 public string Reason(string reason)=>Messages.TryGetValue("reason:"+reason,out var text)?text:reason.StartsWith("等待战斗开始")?this["waiting"]:this["running"];
 public string Error(Exception ex){if(Messages.ContainsKey(ex.Message))return this[ex.Message];if(ex is UnauthorizedAccessException||ex.Message.Contains("Access")||ex.Message.Contains("拒绝"))return this["permission"];if(ex.Message.Contains("Unsupported client")||ex.Message.Contains("Ambiguous client")||ex.Message.Contains("CS0"))return this["adapt-error"];if(ex.Message.Contains("请先启动游戏"))return this["not-running"];return this["error"];}
 public static string LoadLanguage(string? root=null){if(AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US")return hosted;try{var p=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root??Shared.Client.Root,"preferences.json")));if(p?.GetValueOrDefault("language") is string l&&l is "zh-CN" or "en-US")return l;}catch(IOException){}catch(JsonException){}return CultureInfo.CurrentUICulture.Name.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en-US";}
 public void SaveLanguage(string? root=null){if(AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US"){return;}Directory.CreateDirectory(root??Shared.Client.Root);var path=Path.Combine(root??Shared.Client.Root,"preferences.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new{language=Language}));File.Move(temp,path,true);}
}

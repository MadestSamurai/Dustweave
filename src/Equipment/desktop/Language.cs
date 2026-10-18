using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
namespace BD2Equipment;
internal static class L {
 static Dictionary<string,string> Load(string name){using var s=typeof(L).Assembly.GetManifestResourceStream("Localization."+name+".json")!;return JsonSerializer.Deserialize<Dictionary<string,string>>(s)!;}
 static readonly Dictionary<string,string> english=Load("en-US"),names=Load("game-names");
 internal static readonly Dictionary<string,string> Labels=Load("labels");
 static Regex atoms=new(string.Join("|",names.Keys.Concat(new[]{"生命值","生命%","物攻值","物攻%","魔攻值","魔攻%","物防","魔防","暴击率","暴伤","未读取","未解锁","已锁定","未穿戴","保留中","金币","粉"}).Distinct().OrderByDescending(s=>s.Length).Select(Regex.Escape)),RegexOptions.Compiled);
 static IReadOnlyDictionary<string,string>? currentNames;
 static void UpdateNames(){var next=BD2Equipment.Core.GearView.EnglishNames;if(ReferenceEquals(next,currentNames))return;currentNames=next;if(next.Count==0)return;foreach(var pair in next)names[pair.Key]=pair.Value;atoms=new Regex(string.Join("|",names.Keys.Concat(new[]{"生命值","生命%","物攻值","物攻%","魔攻值","魔攻%","物防","魔防","暴击率","暴伤","未读取","未解锁","已锁定","未穿戴","保留中","金币","粉"}).Distinct().OrderByDescending(s=>s.Length).Select(Regex.Escape)),RegexOptions.Compiled);}
 internal static string Language{get;private set;}=CultureInfo.CurrentUICulture.Name.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en-US";
 internal static string T(string text){UpdateNames();if(Language=="zh-TW" && AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText") is Func<string,string> traditional)return traditional(text);if(Language=="zh-CN"||string.IsNullOrEmpty(text))return text;if(english.TryGetValue(text,out var en))return en;if(names.TryGetValue(text,out en))return en;return atoms.Replace(text,m=>english.GetValueOrDefault(m.Value,names.GetValueOrDefault(m.Value,m.Value=="粉"?"powder":m.Value)));}
 internal static string F(string text,params object?[] values)=>string.Format(CultureInfo.InvariantCulture,T(text),values);
 internal static void Select(string language,bool save=true){Language=language=="en-US"?"en-US":language=="zh-TW" && AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText") is Func<string,string>?"zh-TW":"zh-CN";if(save && AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US"){}else if(save){Directory.CreateDirectory(WorkerClient.Data);File.WriteAllText(Path.Combine(WorkerClient.Data,"language.json"),JsonSerializer.Serialize(new { language=Language }));}Apply();}
 internal static void Initialize(){if(AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US"){Select(hosted,false);return;}try{string path=Path.Combine(WorkerClient.Data,"language.json");if(File.Exists(path)){using var value=JsonDocument.Parse(File.ReadAllText(path));bool legacy=value.RootElement.ValueKind==JsonValueKind.String;Select(legacy?value.RootElement.GetString()!:value.RootElement.GetProperty("language").GetString()!,legacy);}}catch(IOException){}catch(JsonException){}Apply();}
 internal static void Apply(){if(Application.Current!=null)foreach(var p in Labels)Application.Current.Resources[p.Key]=T(p.Value);}
 internal static string ConvertToCurrent(string value,string previous){if(previous==Language)return value;if(previous=="zh-TW" && AppDomain.CurrentDomain.GetData("BD2Daily.SourceText") is Func<string,string> source)return T(source(value));if(previous=="en-US"){var found=english.FirstOrDefault(p=>p.Value==value);if(found.Key!=null)return T(found.Key);found=names.FirstOrDefault(p=>p.Value==value);if(found.Key!=null)return T(found.Key);}return T(value);}
}

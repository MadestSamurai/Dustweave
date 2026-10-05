using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BD2Daily.Desktop;

// Uses the actual bundled preference adapters with isolated files. Does not construct a game connection or window.
internal static class HostedToolLocaleProbe
{
    private record Adapter(Func<string> Read, Action<string> Save);
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var checks=new List<string>();
        string culture=CultureInfo.CurrentCulture.Name, uiCulture=CultureInfo.CurrentUICulture.Name;
        object? before=AppDomain.CurrentDomain.GetData(HostedToolLocale.Key);
        string? equipmentRoot=Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT");
        void Check(bool ok,string message) { if(!ok)throw new Exception(message); checks.Add(message); }
        try
        {
            AppDomain.CurrentDomain.SetData(HostedToolLocale.Key,null);
            foreach(var tool in DailyToolCatalog.All)
            {
                string root=Path.Combine(output,"isolated",tool.Id);
                Directory.CreateDirectory(root);
                var adapter=Open(tool,root);
                adapter.Save("zh-CN");
                string[] files=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().ToArray();
                Check(files.Length>0,tool.Id+": standalone preference persisted");
                string FilesState() => string.Join("|",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order()
                    .Select(p=>p+":"+File.GetLastWriteTimeUtc(p).Ticks+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
                string original=FilesState();
                Check(adapter.Read()=="zh-CN",tool.Id+": standalone preference read");
                using(HostedToolLocale.Begin("en-US"))
                {
                    Check(adapter.Read()=="en-US",tool.Id+": host overrides saved language");
                    adapter.Save("zh-CN");
                    Check(adapter.Read()=="en-US",tool.Id+": child cannot override the global language");
                    adapter.Save("en-US");
                    Check(adapter.Read()=="en-US",tool.Id+": window language remains consistent");
                    Check(original==FilesState(),tool.Id+": hosted choices do not write preferences");
                    if(tool.Id=="secret-vision")
                    {
                        // This tool stores language and business settings together.
                        var type=Assembly.Load("BD2SecretVision.Core").GetType("BD2SecretVision.UserPreferences")!;
                        var prefs=type.GetMethod("Load")!.Invoke(null,[root])!;
                        Check((string)type.GetProperty("Language")!.GetValue(prefs)! =="zh-CN",tool.Id+": stored model remains independent");
                        type.GetMethod("Save")!.Invoke(prefs,[root]);
                        using var saved=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"settings.json")));
                        Check(saved.RootElement.GetProperty("Language").GetString()=="zh-CN" && !saved.RootElement.TryGetProperty("DisplayLanguage",out _),tool.Id+": settings save cannot leak the host locale");
                    }
                }
                Check(adapter.Read()=="zh-CN",tool.Id+": standalone preference restored after host scope");
                using(HostedToolLocale.Begin("zh-TW")){Check(adapter.Read()=="zh-TW",tool.Id+": Traditional follows the host");CheckRendering(tool,Check);}
                adapter.Save("en-US");
                Check(adapter.Read()=="en-US",tool.Id+": standalone save still works");
                AppDomain.CurrentDomain.SetData(HostedToolLocale.Key,"invalid");
                Check(adapter.Read()=="en-US",tool.Id+": invalid host hint ignored");
                AppDomain.CurrentDomain.SetData(HostedToolLocale.Key,null);
            }
            Check(CultureInfo.CurrentCulture.Name==culture&&CultureInfo.CurrentUICulture.Name==uiCulture,"Execution culture unchanged");
            bool rejected=false;try{using var invalid=HostedToolLocale.Begin("unknown");}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Invalid host locale rejected");
            DailyJson.Write(Path.Combine(output,"result.json"),new {status="passed",count=checks.Count,checks,tools=9,gameCommands=0,realGameTouched=false});
        }
        finally
        {
            AppDomain.CurrentDomain.SetData(HostedToolLocale.Key,before);
            Environment.SetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT",equipmentRoot);
        }
    }
    private static void CheckRendering(DailyToolDefinition tool,Action<bool,string> check)
    {
        var converter=new HostedTraditionalText();
        const string sample="连接游戏 · 角色 🐟 ID-5035";
        string expected=converter.Traditional(sample);
        check(expected=="連接遊戲 · 角色 🐟 ID-5035",tool.Id+": Traditional conversion preserves Unicode and identifiers");
        const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var assembly=Assembly.Load(tool.AssemblyName+ (tool.Id=="equipment"?"":".Core"));
        if(tool.Id=="equipment")
        {
            var type=assembly.GetType("BD2Equipment.L")!;
            check((string)type.GetMethod("T",flags)!.Invoke(null,[sample])! == expected,tool.Id+": UI renders Traditional");
            type.GetMethod("Select",flags)!.Invoke(null,["en-US",false]);
            string traditional=(string)AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText")!.GetType().GetMethod("Invoke")!.Invoke(AppDomain.CurrentDomain.GetData("BD2Daily.TraditionalText"),["已锁定"])!;
            string restored=(string)type.GetMethod("ConvertToCurrent",flags)!.Invoke(null,[traditional,"zh-TW"])!;
            check(restored==(string)type.GetMethod("T",flags)!.Invoke(null,["已锁定"])!,tool.Id+": filter label survives Traditional to English switch");
        }
        else if(tool.Id is "fiend-hunter" or "secret-vision")
        {
            var type=assembly.GetType(tool.AssemblyName+".Localization.Catalog")!;
            var cn=(Dictionary<string,string>)type.GetMethod("Read")!.Invoke(null,["zh-CN"])!;
            var tw=(Dictionary<string,string>)type.GetMethod("Read")!.Invoke(null,["zh-TW"])!;
            check(cn.All(p=>tw[p.Key]==converter.Traditional(p.Value)),tool.Id+": complete Traditional catalog renders");
        }
        else
        {
            var type=assembly.GetType(tool.AssemblyName+".Localization.LanguageCatalog")??Assembly.Load(tool.AssemblyName).GetType(tool.AssemblyName+".Localization.LanguageCatalog")!;
            var catalog=Activator.CreateInstance(type,["zh-TW"]);
            check((string)type.GetMethod("Text")!.Invoke(catalog,[sample])! == expected,tool.Id+": UI renders Traditional");
        }
    }
    private static Adapter Open(DailyToolDefinition tool,string root)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        if(tool.Id=="equipment")
        {
            Environment.SetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT",root);
            var type=Assembly.Load(tool.AssemblyName).GetType("BD2Equipment.L")!;
            return new(() => {type.GetMethod("Initialize",all)!.Invoke(null,null);return (string)type.GetProperty("Language",all)!.GetValue(null)!;},
                code=>type.GetMethod("Select",all)!.Invoke(null,[code,true]));
        }
        var assembly=Assembly.Load(tool.AssemblyName+".Core");
        if(tool.Id=="secret-vision")
        {
            var type=assembly.GetType("BD2SecretVision.UserPreferences")!;
            object Read()=>type.GetMethod("Load")!.Invoke(null,[root])!;
            return new(()=>(string)type.GetProperty("DisplayLanguage")!.GetValue(Read())!,
                code=>type.GetMethod("SelectLanguage")!.Invoke(Read(),[root,code]));
        }
        if(tool.Id=="fiend-hunter")
        {
            var type=assembly.GetType("BD2FiendHunter.Localization.Catalog")!;
            return new(()=>(string)type.GetMethod("LoadLanguage")!.Invoke(null,[root])!,
                code=>type.GetMethod("SaveLanguage")!.Invoke(Activator.CreateInstance(type,[code]),[root]));
        }
        var preference=assembly.GetType(tool.AssemblyName+".Localization.LanguagePreference") ?? Assembly.Load(tool.AssemblyName).GetType(tool.AssemblyName+".Localization.LanguagePreference",true)!;
        return new(()=>(string)preference.GetMethod("Read")!.Invoke(null,[root])!,
            code=>preference.GetMethod("Save")!.Invoke(null,[root,code]));
    }
}


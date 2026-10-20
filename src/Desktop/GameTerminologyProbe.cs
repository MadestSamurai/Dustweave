using System.Reflection;

namespace Dustweave.Desktop;

internal static class GameTerminologyProbe
{
    internal static void Check(Action<bool,string> check)
    {
        var language=DailyLanguage.Current;
        language.Audit();
        foreach(var code in DailyLanguage.Codes)
        {
            language.Select(code);
            string sentinel="天赋药 / 卡带 / Talent Pill / Account-5035";
            check(language.Get("stage.unknown",sentinel).Contains(sentinel,StringComparison.Ordinal),
                "Game terms: user-supplied argument is preserved in "+code);
            check(language.Translate("压制当前地图怪物")==language.Get("work.suppress"),
                "Game terms: historical source label still resolves in "+code);
        }
        language.Select("zh-CN");
        check(language.Get("trade.pills")=="天赋神药","Game terms: official Simplified item name");
        check(language.Get("run.recovery_talent_3")=="制伏","Game terms: official Simplified talent name");
        language.Select("zh-TW");
        check(language.Get("trade.pills")=="天賦特效藥","Game terms: official Traditional item name");
        check(language.Get("tool.apostle-defense")=="使者運氣防守","Game terms: official Traditional minigame name");
        language.Select("en-US");
        check(language.Get("stage.friendship")=="Rapport consultations","Game terms: Rapport is distinct from NPC affinity");
        check(language.Get("tool.fiend-hunter")=="Fiend Hunter: Edge of Fury","Game terms: minigame is distinct from Fiend Hunter");
        check(language.Get("tool.apostle-defense")=="Apostle Random Defense","Game terms: official English tower name");
        check(GameTerminology.Template("Ability Pills / 副卡带","en-US")=="Ability Pills / 副卡带",
            "Game terms: locale isolation and plural are preserved");
        check(GameTerminology.Template("SuppressionId / suppression / unsuppressed","en-US")=="SuppressionId / Overpower / unsuppressed",
            "Game terms: complete English words only");

        var converter=new HostedTraditionalText();
        const string source="格兰希特 · 藏红花 · 土豆 · 魅惑炒饭 · 爱欲浓汤 · 精炼粉";
        string traditional=converter.Traditional(source);
        check(traditional=="葛蘭西特 · 番紅花 · 馬鈴薯 · 雙峰炒飯 · 魅影濃湯 · 強化粉末",
            "Game terms: Traditional names use game wording, not character substitution");
        check(converter.Source(traditional)==source,"Game terms: reverse lookup keeps original filter identities");
        foreach(var (locale,text) in new[]{("zh-CN","游戏卡 / 天赋神药 / 精炼粉末"),("zh-TW","遊戲卡帶 / 使者運氣防守 / 天賦特效藥"),("en-US","Apostle Random Defense / Ability Pills")})
            check(GameTerminology.Template(GameTerminology.Template(text,locale),locale)==text,
                "Game terms: canonical terms stay stable in "+locale);

        using var scope=HostedToolLocale.Begin("zh-CN");
        var fiend=Assembly.Load("BD2FiendHunter.Core").GetType("BD2FiendHunter.Localization.Catalog")!;
        var cn=(Dictionary<string,string>)fiend.GetMethod("Read")!.Invoke(null,["zh-CN"])!;
        var tw=(Dictionary<string,string>)fiend.GetMethod("Read")!.Invoke(null,["zh-TW"])!;
        check(cn["difficulty-3"]=="极难"&&tw["difficulty-3"]=="超困難"&&tw["character-3"]=="葛蘭西特",
            "Game terms: actual hunter catalog follows game difficulty and character names");
        var equipment=Assembly.Load("BD2EquipmentAssistant").GetType("BD2Equipment.L")!;
        const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        foreach(string code in DailyLanguage.Codes)
        {
            equipment.GetMethod("Select",flags)!.Invoke(null,[code,false]);
            string actual=(string)equipment.GetMethod("T",flags)!.Invoke(null,["金币 {0:N0}    天赋药 {1:N0}    精炼粉 {2:N0}"])!;
            string item=code=="zh-CN"?"天赋神药":code=="zh-TW"?"天賦特效藥":"Ability";
            check(actual.Contains(item)&&actual.Contains("{1:N0}"),"Game terms: equipment formats after terminology in "+code);
        }
    }
}

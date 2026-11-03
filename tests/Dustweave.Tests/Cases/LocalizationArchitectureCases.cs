using System.Text.Json;
using System.Text.RegularExpressions;
using Dustweave;
using Dustweave.Desktop;

internal static class LocalizationArchitectureCases
{
    public static void Run(List<string> cases)
    {
        void Check(string name, bool condition) { if (!condition) throw new Exception(name); cases.Add(name); }
        string folder = Path.Combine(TestPaths.SourceRoot, "src", "Desktop", "Localization");
        LocalizedTextCatalog Load() => new(name => File.OpenRead(Path.Combine(folder, name + ".json")));
        var catalog = Load();
        catalog.Audit();
        Check("editable resource keys are independent of Chinese copy", catalog.Entries.Keys.All(k => !Regex.IsMatch(k, @"\p{IsCJKUnifiedIdeographs}")));
        foreach (string language in LocalizedTextCatalog.Codes)
        {
            Check("same Chinese keeps Close and Off distinct: " + language,
                catalog.Get("window.close", language) == catalog.Entries["window.close"][language]
                && catalog.Get("trade.strategy_off", language) == catalog.Entries["trade.strategy_off"][language]);
        }
        string english = catalog.Get("prefs.business", "en-US");
        string traditional = catalog.Get("prefs.business", "zh-TW");
        catalog.Entries["prefs.business"]["zh-CN"] = catalog.Get("stage.management", "zh-CN");
        Check("Chinese rename and collision do not change English setting text", catalog.Get("prefs.business", "en-US") == english);
        Check("Chinese rename and collision do not change Traditional setting text", catalog.Get("prefs.business", "zh-TW") == traditional);
        Check("legacy source mapping does not follow edited Chinese", catalog.LegacyInputs["经营一键领取"] == "prefs.business");
        Check("legacy aggregate label retains its original scope", catalog.LegacyInputs["经营与餐厅"] == "stage.management");
        catalog.Entries["stage.weekly_fishing"]["zh-CN"] = "全新名称";
        Check("renamed weekly task retains category", DailySettingsCatalog.Category("stage.weekly_fishing") == "weekly");
        Check("resource setting retains category", DailySettingsCatalog.Category("prefs.trade_purchase") == "resources");
        Check("battle setting retains category", DailySettingsCatalog.Category("prefs.event_battle") == "battle");
        Check("different sub-operation retains its own category", DailySettingsCatalog.Category("prefs.business") == "daily");
        Check("all tool categories have stable identifiers", DailyToolCatalog.All.All(t => t.CategoryId is "games" or "business" or "draws"));
        var renamedTool = DailyToolCatalog.Find("soul") with { Category = "renamed display category", Name = "renamed tool" };
        Check("tool copy does not change category identity", renamedTool.CategoryId == "games");
        Check("search follows edited display text", catalog.SearchText("stage.weekly_fishing").Contains("全新名称"));
        Check("search retains all supported languages", catalog.SearchText("prefs.business").Contains(english) && catalog.SearchText("prefs.business").Contains(traditional));
        // Entry order can no longer decide which resource a runtime input means.
        var reversed = catalog.Entries.Reverse().ToArray();
        catalog.Entries.Clear();
        foreach (var (key, entry) in reversed) catalog.Entries.Add(key, entry);
        Check("reordering resources leaves legacy meaning unchanged", catalog.LegacyInputs["经营一键领取"] == "prefs.business" && catalog.Get("prefs.business", "en-US") == english);
        string RuntimeEnglish(string input) => catalog.Entries.ContainsKey(input) ? catalog.Get(input, "en-US")
            : catalog.LegacyInputs.TryGetValue(input, out var key) ? catalog.Get(key, "en-US") : input;
        string expectedPreparing = catalog.Get("message.preparing", "en-US");
        catalog.Entries["message.preparing"]["zh-CN"] = "修改后的显示措辞";
        Check("Core message code does not depend on Chinese fallback equality", DailyUserText.Describe("preparing", RuntimeEnglish) == expectedPreparing);
        catalog.Audit();

        var missing = Load();
        missing.Entries["prefs.business"].Remove("en-US");
        Check("missing translation has readable runtime fallback", missing.Get("prefs.business", "en-US") == missing.Entries["prefs.business"]["zh-CN"]);
        Check("missing UI key does not throw into execution", missing.Get("future.missing", "en-US") == "[future.missing]");
        _ = missing.Get("notice.pattern.account_launch", "en-US", "1");
        Check("format failure is recorded without stopping execution", missing.Problems.Any(p => p.Contains("Invalid text arguments")));
        bool rejected = false;
        try { missing.Audit(); } catch (InvalidDataException) { rejected = true; }
        Check("development audit rejects missing translations", rejected);

        const string nickname = "正在准备 · C:\\用户\\{0}\\账号";
        var text = DailyText.Of("notice.pattern.account_launch", "original diagnostic", "1", "3", nickname);
        var progress = new DailyProgress("launching", text.Fallback, 1, 3) { Display = text };
        string serialized = JsonSerializer.Serialize(progress);
        var restored = JsonSerializer.Deserialize<DailyProgress>(serialized)!;
        Check("structured message survives serialization with opaque arguments", restored.Display?.Arguments.SequenceEqual(text.Arguments) == true);
        Check("structured message leaves progress and original diagnostics unchanged", restored.Message == "original diagnostic" && restored.State == "launching" && restored.Completed == 1 && restored.Total == 3);
        foreach (string language in LocalizedTextCatalog.Codes)
            Check("formatting preserves account names paths braces: " + language,
                catalog.Get(text.Key, language, text.Arguments.Cast<object>().ToArray()).Contains(nickname));
        var old = JsonSerializer.Deserialize<DailyProgress>("{\"State\":\"running\",\"Message\":\"原始记录\",\"Completed\":1,\"Total\":3}")!;
        Check("old progress remains readable without structured metadata", old.Display == null && old.Message == "原始记录" && old.Completed == 1);
        Check("unknown historical strings need no migration", !catalog.LegacyInputs.ContainsKey("未知旧缓存记录"));

        foreach (string line in File.ReadLines(Path.Combine(TestPaths.SourceRoot, "src", "Core", "Localization", "Messages.zh-CN.txt")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            string code = line[..line.IndexOf('=')].Trim();
            string key = "message." + code;
            if (!catalog.Entries.ContainsKey(key)) key = catalog.LegacyInputs.GetValueOrDefault(key, key);
            Check("runtime code has stable display resource: " + code, catalog.Entries.ContainsKey(key));
        }
        using var notices = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "notices.json")));
        foreach (var item in notices.RootElement.EnumerateObject())
            if (item.Value.TryGetProperty("parameters", out _) && item.Name.StartsWith("notice.pattern.", StringComparison.Ordinal))
                Check("legacy grammar is explicit and independent of display: " + item.Name, item.Value.TryGetProperty("source", out var source) && !string.IsNullOrWhiteSpace(source.GetString()));

        string panel = File.ReadAllText(Path.Combine(TestPaths.SourceRoot, "src", "Desktop", "DailyPreferencesPanel.cs"));
        Check("settings cannot silently return to Chinese reverse lookup", !panel.Contains("L.Translate(") && !panel.Contains("L.Literal("));
        foreach (Match match in Regex.Matches(panel, "\\b(?:Check|Note|Row|LocalButton|Label)\\(\\s*\"([^\"]+)\""))
            Check("settings bind an existing explicit key: " + match.Groups[1].Value, catalog.Entries.ContainsKey(match.Groups[1].Value));
        foreach (Match match in Regex.Matches(panel, "\\bStage\\([^,\\r\\n]+,\\s*\"([^\"]+)\",\\s*\"([^\"]+)\""))
            foreach (int part in new[] { 1, 2 })
                Check("settings section references a key: " + match.Groups[part].Value, catalog.Entries.ContainsKey(match.Groups[part].Value));
        string core = File.ReadAllText(Path.Combine(TestPaths.SourceRoot, "src", "Core", "DailyCoordinator.cs"));
        Check("coordinator static reports cannot regress to prose lookup", !Regex.IsMatch(core, "Report\\(\"[^\"]+\",\\s*\\$?\"[^\"]*\\p{IsCJKUnifiedIdeographs}"));
        string desktop = Path.Combine(TestPaths.SourceRoot, "src", "Desktop");
        foreach (string file in Directory.EnumerateFiles(desktop, "*.xaml"))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{\w+:Tr\s+([^}\s]+)\s*\}"))
                Check("XAML binds a real localization key: " + Path.GetFileName(file) + ":" + match.Groups[1].Value,
                    catalog.Entries.ContainsKey(match.Groups[1].Value));
        foreach (string file in Directory.EnumerateFiles(desktop, "*.cs"))
        {
            string source = File.ReadAllText(file);
            if (!Path.GetFileName(file).Contains("Smoke", StringComparison.Ordinal))
                Check("static UI text must not use legacy prose lookup: " + Path.GetFileName(file),
                    !Regex.IsMatch(source, "\\bL\\.(?:Translate|Literal)\\([^\\r\\n]*?\"[^\"\\r\\n]*\\p{IsCJKUnifiedIdeographs}"));
            Check("dialog callers provide explicit localized text: " + Path.GetFileName(file),
                !Regex.IsMatch(source, "\\b(?:Confirm|Prompt)\\(\"[^\"\\r\\n]*\\p{IsCJKUnifiedIdeographs}"));
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\\bL\\.Get\\(\"([^\"]+)\"\\s*[,)]"))
                Check("static UI lookup has a resource: " + Path.GetFileName(file) + ":" + match.Groups[1].Value,
                    catalog.Entries.ContainsKey(match.Groups[1].Value));
        }
    }
}

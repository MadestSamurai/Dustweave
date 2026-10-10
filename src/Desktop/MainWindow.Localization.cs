using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// Exercises real WPF bindings with isolated accounts. No connection to the game.
public partial class MainWindow
{
    private async Task CheckLanguagesForSmoke()
    {
        L.Audit();
        CheckRuntimeLanguagesForSmoke();
        await CheckDynamicTextForSmoke();
        await CheckNoticesForSmoke();
        DailyTheme.CheckContrastForSmoke();
        string initialCulture = CultureInfo.CurrentCulture.Name, initialUiCulture = CultureInfo.CurrentUICulture.Name;
        string accountFile = Path.Combine(root, "accounts.json");
        string FileState(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + ":" + File.ReadAllText(path) : "";
        string savedAccounts = FileState(accountFile);
        string AccountState() => JsonSerializer.Serialize(rows.Select(r => new { r.Account.AccountKey, r.Name, r.Selected }));
        string accountsBefore = AccountState();
        string PrefFiles() => Directory.Exists(Path.Combine(root,"preferences"))
            ? string.Join("|", Directory.GetFiles(Path.Combine(root,"preferences"), "*.json", SearchOption.AllDirectories).Order().Select(FileState)) : "";
        var prefFiles = PrefFiles();
        var prefs = preferencesPanel.BeginLanguageProbeForSmoke();
        var tools = toolPanel.BeginLanguageProbeForSmoke();
        dailyPanel.ClearSelection();
        dailyPanel.SelectPlanTask("mirror", true);
        dailyPanel.SelectPlanTask("mail", true);
        var verifyPlan = dailyPanel.LanguageProbeForSmoke();
        try
        {
            foreach (int language in new[] { 2, 1, 0, 2 })
            {
                LanguageSelector.SelectedIndex = language;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                verifyPlan(); prefs.Verify(); tools.Verify();
                if (savedAccounts != FileState(accountFile) || prefFiles != PrefFiles() || accountsBefore != AccountState())
                    throw new Exception("Changing locale wrote account profiles or task preferences");
                if (CultureInfo.CurrentCulture.Name != initialCulture || CultureInfo.CurrentUICulture.Name != initialUiCulture)
                    throw new Exception("UI locale changed execution culture");
                if (DailyJson.TryRead<DailyLanguage.LanguageSetting>(Path.Combine(root,"language.json"))?.Language != DailyLanguage.Codes[language])
                    throw new Exception("Language choice was not persisted");
                if ((string)RunTab.Header != L.Get("nav.daily") || (string)ConnectButton.Content != L.Get("connection.connect"))
                    throw new Exception("XAML translation binding is stale");

                WorkspaceTabs.SelectedItem = RunTab;
                ThemeSelector.SelectedIndex = language == 1 ? 1 : 2;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("locale-" + L.Code + "-plan");
                if (language == 2)
                {
                    WorkspaceTabs.SelectedItem = SettingsTab;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture("locale-en-settings");
                    Width = 920; Height = 650;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture("locale-en-settings-compact");
                    WorkspaceTabs.SelectedItem = ToolsTab;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture("locale-en-tools-compact");
                    Width = 1180; Height = 800;
                }
            }
            dailyPanel.Show(new QueueView("running", "", "fixture/language-running.json",
                [new("guild","completed",""),new("mirror","running",""),new("mail","pending","")],new string('a',64)));
            dailyPanel.Busy(true);
            var verifyRunning = dailyPanel.LanguageProbeForSmoke();
            foreach (int language in new[] { 1, 2, 0 })
            {
                LanguageSelector.SelectedIndex = language;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                verifyRunning();
            }
            const string raw = "fixture: unchanged protocol evidence";
            if (!L.Diagnostic(raw, "Message").EndsWith(raw, StringComparison.Ordinal))
                throw new Exception("Localized diagnostics lost raw evidence");
            await CheckTradeLanguageForSmoke();
            DailyJson.Write(Path.Combine(smoke!, "localization.json"), new {
                status = "passed", languages = DailyLanguage.Codes, resourceCount = L.Count,
                invariantTaskIds = true, unsavedPreferencesPreserved = true, activeQueuePreserved = true,
                accountFilesUnchanged = true, cultureUnchanged = true, realGameTouched = false
            });
        }
        finally
        {
            LanguageSelector.SelectedIndex = 0; ThemeSelector.SelectedIndex = 1;
            Width = 1180; Height = 800; prefs.Restore(); tools.Restore();
            dailyPanel.Busy(false); dailyPanel.ShowCurrentPlan(); dailyPanel.SelectPlanAll();
            WorkspaceTabs.SelectedItem = RunTab;
        }
    }

    private async Task CheckTradeLanguageForSmoke()
    {
        string folder = Path.Combine(smoke!, "trade-language");
        Directory.CreateDirectory(folder);
        string account = new string('a',64);
        DailyJson.Write(Path.Combine(folder,"snapshot.json"), new {
            schema = 1, context = new { account, player_name = "Demo trader" }, inventory_complete = true,
            offers_complete = true, recipes_complete = true, game_date = "2026-10-05",
            captured_utc = "2026-10-05T10:00:00Z", gold = 100000000
        });
        DailyJson.Write(Path.Combine(folder,"plan.json"), new {
            context = new { account },
            settings = new { include_break_even_resales = true },
            summary = new { incremental_profit=2000000, cash_required=8000000, eventual_sale_value=10000000,
                cash_remaining=92000000, potions_used=400, potions_to_buy=0, break_even_count=100, break_even_value=20000 },
            solver = new { optimal=true },
            purchases = new[] { new { name="Demo ingredient", count=100, price=200, cost=20000, shop=1, product=1 } },
            cooking = new[] { new { name="Demo dish", count=10, potions=40, sale_date="2026-10-10" } },
            sales = new[] { new { name="Demo dish", count=10, price=4000, shop=1, date="2026-10-10", today_quote_confirmed=false } }
        });
        var panel = new TradePlanPanel(folder, account, allowGame:false);
        panel.LoadSnapshot(Path.Combine(folder,"snapshot.json"));
        panel.Display(Path.Combine(folder,"plan.json"));
        var surface = new System.Windows.Controls.Border { Child=panel, Background=(System.Windows.Media.Brush)FindResource("Surface") };
        var window = new System.Windows.Window { Owner=this, Width=960, Height=700, Content=surface,
            Style=(System.Windows.Style)FindResource(typeof(System.Windows.Window)), WindowStartupLocation=System.Windows.WindowStartupLocation.CenterOwner };
        window.Show();
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var verify = panel.LanguageProbeForSmoke();
            await panel.Capture(); // Must short-circuit before any live host or pipe access.
            foreach (int language in new[] { 0, 2, 1 })
            {
                LanguageSelector.SelectedIndex=language;
                ThemeSelector.SelectedIndex=language==2 ? 2 : 1;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                verify();
                Capture("locale-"+L.Code+"-trade", surface);
            }
        }
        finally { window.Close(); LanguageSelector.SelectedIndex=0; ThemeSelector.SelectedIndex=1; }
    }

    private void CheckRuntimeLanguagesForSmoke()
    {
        using var input = typeof(DailyUserText).Assembly.GetManifestResourceStream("Dustweave.Messages.zh-CN.txt")!;
        using var reader = new StreamReader(input);
        var rows = reader.ReadToEnd().Replace("\r","").Split('\n').Where(s => s.Length > 0 && !s.StartsWith('#'))
            .Select(s => { int i=s.IndexOf('='); return (Code:s[..i].Trim(),Chinese:s[(i+1)..].Trim()); }).ToArray();
        LanguageSelector.SelectedIndex=0;
        foreach (var row in rows)
            if (L.Get("message."+row.Code)!=row.Chinese) throw new Exception("Runtime source translation drift: "+row.Code);
        LanguageSelector.SelectedIndex=2;
        foreach (var row in rows)
        {
            string value=L.Describe(row.Code);
            if (string.IsNullOrWhiteSpace(value) || System.Text.RegularExpressions.Regex.IsMatch(value, @"\p{IsCJKUnifiedIdeographs}"))
                throw new Exception("Missing runtime English: "+row.Code);
        }
        foreach (var source in new[] { "rejected: screen_changed", "Command outcome is uncertain; no replay: screen_changed", "导航未能安全恢复：Waypoint map not available", ".NET queue: partial" })
            if (System.Text.RegularExpressions.Regex.IsMatch(L.Describe(source), @"\p{IsCJKUnifiedIdeographs}"))
                throw new Exception("Composite runtime message remains untranslated: "+source);
        if (!L.Describe("Missing native state: daily.dispatch.$self").Contains("daily.dispatch.$self"))
            throw new Exception("Translated runtime message lost its evidence identifier");
        LanguageSelector.SelectedIndex=1;
        foreach (var row in rows)
        {
            string value=L.Get("message."+row.Code);
            if(value.Length!=row.Chinese.Length || value.Any(c => char.IsSurrogate(c) || c=='\uFFFD' || (char.IsControl(c) && c!='\n' && c!='\r')))
                throw new Exception("Invalid Traditional Chinese resource: "+row.Code);
        }
        if(L.Get("message.preparing")!="正在準備") throw new Exception("Traditional preparation label differs");
        if (!L.Describe("weekly_fishing_complete").Contains("釣魚")) throw new Exception("Traditional runtime label missing");
        LanguageSelector.SelectedIndex=0;
        DailyJson.Write(Path.Combine(smoke!,"runtime-languages.json"), new {status="passed",registeredMessages=rows.Length,rawEvidencePreserved=true,realGameTouched=false});
    }
}

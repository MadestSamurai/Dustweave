using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckNoticesForSmoke()
    {
        using var stream = typeof(DailyLanguage).Assembly.GetManifestResourceStream("Dustweave.UI.notices.json")!;
        using var document = JsonDocument.Parse(stream);
        var status = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };
        var failure = new TextBlock();
        const string nickname = "正在准备 · 主账号";
        const string raw = "2/3 · 切换并启动 " + nickname;
        const string nested = "卡带 14 · 地图 141 · 周收集已核对 12/24 张地图";
        DailyUiText.Set(status, raw);
        DailyUiText.Error(failure, new InvalidOperationException("组件启动失败：rejected: screen_changed"));
        int checks = 0;
        try
        {
            foreach (int language in new[] { 2, 1, 0, 2 })
            {
                LanguageSelector.SelectedIndex = language;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    string source = entry.Value.GetProperty("zh-CN").GetString()!;
                    if (entry.Value.TryGetProperty("parameters", out var kinds))
                    {
                        if (entry.Value.TryGetProperty("source", out var alternate)) source = alternate.GetString()!;
                        var input = kinds.EnumerateArray().Select((kind, i) => (object)(kind.GetString() switch {
                            "number" => (i + 12).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "stage" => "weekly_mainline", "message" => "screen_changed", _ => "Alice:C:/sample.json"
                        })).ToArray();
                        source = string.Format(System.Globalization.CultureInfo.InvariantCulture, source, input);
                        var expected = kinds.EnumerateArray().Select((kind, i) => (object)(kind.GetString() switch {
                            "stage" => L.Stage((string)input[i]), "message" => L.Describe((string)input[i]), _ => (string)input[i]
                        })).ToArray();
                        if (L.Describe(source) != L.Get(entry.Name, expected)) throw new Exception("Notice format did not render: " + entry.Name);
                    }
                    string text = L.Describe(source);
                    if (language == 2 && HasHan(text)) throw new Exception("Notice remains Chinese: " + entry.Name);
                    if (language == 1 && !entry.Value.TryGetProperty("parameters", out _) && L.Translate(source) == source
                        && entry.Value.GetProperty("zh-TW").GetString() != source)
                        throw new Exception("Notice remains Simplified Chinese: " + entry.Name);
                    checks++;
                }
                string expectedAccount = L.Get("notice.pattern.account_launch", "2", "3", nickname);
                if (status.Text != expectedAccount || !(status.ToolTip as string)!.EndsWith(raw, StringComparison.Ordinal))
                    throw new Exception("Dynamic notice changed the account name or raw diagnostic");
                if (language == 2 && (HasHan(L.Describe(nested)) || HasHan(failure.Text)))
                    throw new Exception("Nested route/error notice is not localized");
                string path = @"C:\用户\正在准备\报告.json";
                if (L.Translate(path) != path || L.Describe(nickname + " · screen_changed") != nickname + " · " + L.Describe("screen_changed"))
                    throw new Exception("Notice translation changed an opaque path or account name");
                if (L.Describe("卡带 14 · 地图 141 · 本周完成 1/3 · 某位玩家的任务") !=
                    L.Get("notice.pattern.route_detail", "14", "141", L.Get("notice.pattern.weekly_named", "1", "3", "某位玩家的任务")))
                    throw new Exception("Nested notice changed the game-provided quest name");
                dailyPanel.ShowOperationError("组件启动失败：rejected: screen_changed", DailyUserText.Describe("组件启动失败：rejected: screen_changed"));
                if (language == 2 && HasHan(dailyPanel.DisplayedMessage)) throw new Exception("Operation error retains a pre-rendered Chinese wrapper");
                if (language is 1 or 2) Capture("locale-" + L.Code + "-notice");
                checks += 5;
            }
            DailyJson.Write(Path.Combine(smoke!, "notice-languages.json"), new {
                status="passed", checks, registeredNotices=document.RootElement.EnumerateObject().Count(),
                languages=DailyLanguage.Codes, accountNamesAndPathsPreserved=true, rawDiagnosticsPreserved=true,
                realGameTouched=false, gameCommands=0
            });
        }
        finally { dailyPanel.ClearOperationError(); dailyPanel.ShowCurrentPlan(); LanguageSelector.SelectedIndex = 0; }
    }
}

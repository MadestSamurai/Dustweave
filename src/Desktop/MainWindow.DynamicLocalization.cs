using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckDynamicTextForSmoke()
    {
        var previous = new TextBlock();
        var local = new TextBlock();
        var failure = new TextBlock();
        var toolText = new TextBlock();
        var structured = new TextBlock();
        var networkWaiting = new TextBlock();
        var networkStopped = new TextBlock();
        var at = new DateTime(2026, 10, 5, 13, 24, 56);
        const string nickname = "正在准备";
        const string raw = nickname + " · rejected: screen_changed";
        (string Raw, string Key, string Name)[] cases = DailyToolCatalog.All.SelectMany(t => new[] {
            ("已共用游戏连接 · " + t.Name + "。请在工具窗口确认设置后开始。", "tools.shared_ready", "tool." + t.Id),
            (t.Name + "的独立窗口已经打开，请先使用或关闭原窗口。", "tools.existing_window", "tool." + t.Id),
            (t.Name + "未能启动，请查看工具诊断记录。", "tools.start_failed", "tool." + t.Id),
            (t.Name + "窗口已打开。未启用自动化时，可以直接运行日常或切换工具。", "tools.window_ready", "tool." + t.Id),
            ("复用已准备组件 · " + t.Name, "suite.reuse", "tool." + t.Id),
            ("首次准备 · " + t.Name, "suite.prepare", "tool." + t.Id)
        }).Concat(new[] {
            ("复用已准备组件 · 日常执行", "suite.reuse", "suite.daily"),
            ("首次准备 · 日常执行", "suite.prepare", "suite.daily"),
            ("连接一次即可切换工具；打开工具后不会自动开始操作。", "tools.idle_notice", ""),
            ("工具共用日常助手的连接，不会自动开始操作。仅在自动化启用或等待结算时占用控制权。", "tools.idle_notice", "")
        }).ToArray();
        DailyUiText.History(previous, "history.previous", at, raw);
        DailyUiText.History(local, "history.local", at, "screen_changed");
        DailyUiText.Error(failure, new InvalidOperationException("rejected: screen_changed"), "读取历史记录失败：");
        DailyUiText.Set(toolText, cases[0].Raw);
        const string opaque = "正在准备 · C:\\用户\\{0}\\账号";
        var structuredText = DailyText.Of("notice.pattern.account_launch", "unchanged raw diagnostic", "1", "3", opaque);
        DailyUiText.Set(structured, structuredText);
        DailyUiText.Set(networkWaiting, DailyText.Of("network.recovery_wait", DailyNetworkRecovery.Waiting));
        const string recoveryDiagnostic = DailyNetworkRecovery.Manual + " (second_timeout; no retry)";
        DailyUiText.Set(networkStopped, DailyText.Of("network.recovery_manual", recoveryDiagnostic));
        string chinese = previous.Text;
        int checks = 0;
        try
        {
            foreach (int language in new[] { 2, 1, 0, 2 })
            {
                LanguageSelector.SelectedIndex = language;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (previous.Text != L.Get("history.previous", at, L.Describe(raw))
                    || !previous.Text.Contains("10-05 13:24") || !previous.Text.Contains(nickname + " · ")
                    || !(previous.ToolTip as string)!.EndsWith(raw, StringComparison.Ordinal))
                    throw new Exception("History translation changed timestamp, nickname or raw diagnostic");
                if (language == 2 && (previous.Text == chinese || !previous.Text.StartsWith("Previous run")))
                    throw new Exception("History text did not switch language");
                if (local.Text != L.Get("history.local", at, L.Describe("screen_changed"))
                    || toolText.Text != L.Get(cases[0].Key, L.Get(cases[0].Name)))
                    throw new Exception("Dynamic WPF binding was not refreshed");
                if (language == 2 && (HasHan(local.Text) || HasHan(failure.Text)))
                    throw new Exception("Local history or error prefix remains Chinese");
                if (structured.Text != L.Get(structuredText.Key, "1", "3", opaque)
                    || !(structured.ToolTip as string)!.EndsWith(structuredText.Fallback, StringComparison.Ordinal))
                    throw new Exception("Structured text changed opaque arguments or diagnostic evidence");
                if (L.Describe(DailyText.Of("future.unknown.message", "screen_changed")) != L.Describe("screen_changed"))
                    throw new Exception("Newer display metadata blocked legacy fallback");
                string waiting = language == 2 ? "once" : "一次";
                string stopped = language == 2 ? "Automatic login has stopped" : language == 1 ? "已停止自動登入" : "已停止自动登录";
                if (!networkWaiting.Text.Contains(waiting) || !networkStopped.Text.Contains(stopped)
                    || (language == 2 && (HasHan(networkWaiting.Text) || HasHan(networkStopped.Text)))
                    || !(networkStopped.ToolTip as string)!.EndsWith(recoveryDiagnostic, StringComparison.Ordinal))
                    throw new Exception("Network recovery binding lost its one-attempt limit, manual next step or raw reason");
                foreach (var item in cases)
                {
                    string expected = item.Name.Length == 0 ? L.Get(item.Key) : L.Get(item.Key, L.Get(item.Name));
                    if (L.Describe(item.Raw) != expected) throw new Exception("Tool message did not use its localized template: " + item.Key);
                    if (language == 2 && HasHan(L.Describe(item.Raw))) throw new Exception("Untranslated tool message: " + item.Raw);
                    checks++;
                }
                const string path = @"C:\用户\正在准备\evidence.json";
                if (L.Translate(path) != path || L.Translate(nickname + " is my name") != nickname + " is my name")
                    throw new Exception("Translation rewrote an unregistered value");
                checks += 8;
            }
            DailyJson.Write(Path.Combine(smoke!, "dynamic-languages.json"), new {
                status="passed", checks, toolMessages=cases.Length, timestampAndNicknamePreserved=true,
                rawDiagnosticsPreserved=true, gameCommands=0, realGameTouched=false
            });
        }
        finally { LanguageSelector.SelectedIndex=0; }
    }
    private static bool HasHan(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"\p{IsCJKUnifiedIdeographs}");
}

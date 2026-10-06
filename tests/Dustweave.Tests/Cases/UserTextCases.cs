using System.Text.Json;
using System.Text.RegularExpressions;
using Dustweave;

internal static class UserTextCases
{
    public static void Run(List<string> cases)
    {
        void Check(string name, bool condition) { if (!condition) throw new Exception(name); cases.Add(name); }
        Check("weekly completion is meaningful Chinese", DailyUserText.Describe("weekly_fishing_complete") == "本周钓鱼任务已完成");
        Check("normal skip is not presented as an error", DailyUserText.Describe("mailbox_empty") == "邮箱没有可领取的奖励");
        Check("partial result explains sweep prerequisite", DailyUserText.Describe("no_fully_cleared_sweep_stage").Contains("全部通关条件"));
        Check("account progress keeps nickname while translating reason", DailyUserText.Describe("Alice · Game not running") == "Alice · 游戏未运行，请先启动游戏。");
        Check("nested unknown error still has Chinese summary", DailyUserText.Describe("日常未继续：Future adapter rejected operation 3819").Contains(DailyUserText.Untranslated));
        Check("compound cancellation keeps no-replay meaning", DailyUserText.Describe("Command outcome is uncertain; no replay: screen_changed").Contains("未重复提交"));
        Check("dispatch settlement error is translated specifically", DailyUserText.Describe("Global claim did not cover all due slots") == "一键领取未覆盖全部已到期派遣");
        Check("paid ticket mismatch is not hidden as generic failure", DailyUserText.Describe("Mirror ticket debit differs; paid tickets must not change").Contains("付费票券"));
        Check("nested command rejection identifies changed scene", DailyUserText.Describe("rejected: screen_changed") == "操作未被接受：游戏页面已变化，请重新确认当前界面。");
        Check("Chinese wrapper retains specific English failure meaning", DailyUserText.Describe("导航未能安全恢复：Waypoint map not available") == "导航未能安全恢复：目标地图当前不在传送列表中");
        Check("exception type wrapper does not leak to normal message", DailyUserText.Describe("System.InvalidOperationException: Native navigation did not start").StartsWith("游戏未开始移动"));
        Check("module output status is translated", DailyUserText.Describe(".NET queue: partial") == "日常队列：部分完成");
        Check("popup name has a useful Chinese explanation", DailyUserText.Describe("Foreground popup needs handling: MessagePopupUI") == "请先处理游戏弹窗：游戏提示框");
        Check("structured read error keeps diagnostic ID", DailyUserText.Describe("Missing native state: daily.dispatch.$self").Contains("daily.dispatch.$self") || DailyUserText.Details("Missing native state: daily.dispatch.$self").Contains("daily.dispatch.$self"));
        const string mixed = "账号 Alice · 今日已完成3次咨询；文件：C:\\Users\\Alice\\pending.json";
        Check("Chinese prose account names and paths are untouched", DailyUserText.Describe(mixed) == mixed);
        Check("Chinese stopped reason does not get rewritten", DailyUserText.Describe("已停止，当前操作等待结算。") == "已停止，当前操作等待结算。");
        const string unknown = "Future adapter rejected operation 3819";
        Check("unknown English gets honest Chinese fallback", DailyUserText.Describe(unknown) == DailyUserText.Untranslated);
        Check("unknown diagnostics remain available verbatim", DailyUserText.Details(unknown).EndsWith(unknown) && DailyUserText.Details(unknown).Contains("原始信息"));
        Check("raw timeout is actionable in Chinese", DailyUserText.Error(new TimeoutException("An unknown wait elapsed")).Contains("等待超时"));
        Check("raw permission exception is actionable in Chinese", DailyUserText.Error(new UnauthorizedAccessException("denied by host")).Contains("访问被拒绝"));
        Check("raw JSON error is actionable in Chinese", DailyUserText.Error(new JsonException("bad document")).Contains("数据文件"));
        Check("empty details do not invent a diagnostic", DailyUserText.Details("", "正在准备") == "正在准备");
        Check("all compatibility stages have Chinese names", DailyStageCatalog.CompatibilityAdapters.All(id => Regex.IsMatch(DailyStageCatalog.Name(id), @"\p{IsCJKUnifiedIdeographs}")));
        Check("unknown state never claims success", DailyUserText.State("future_state") == "未识别状态");
        Check("scene display is Chinese", DailyUserText.Scene("Field") == "地图");
        // The serialized queue is operational evidence. Rendering must never localize it in-place.
        var raw = new QueueView("partial", "navigation_no_progress", "fixture/result.json",
            [new QueueStage("weekly_fishing", "skipped", "weekly_fishing_complete"), new QueueStage("trade", "recovery_required", "Native response is still pending")]);
        string before = JsonSerializer.Serialize(raw);
        _ = DailyUserText.Describe(raw.Message);
        foreach (var item in raw.Stages) { _ = DailyUserText.State(item.State); _ = DailyUserText.Describe(item.Detail); }
        Check("rendering leaves queue and retry evidence unchanged", JsonSerializer.Serialize(raw) == before);

        string Translate(string text) => text switch {
            "操作未被接受：" => "Rejected: ",
            "游戏页面已变化，请重新确认当前界面。" => "The screen changed.",
            "未读到游戏状态：" => "Missing game state: ",
            "导航未能安全恢复：" => "Navigation recovery failed: ",
            "目标地图当前不在传送列表中" => "The destination is not listed.",
            "此提示暂未收录中文说明，请查看原始信息。" => "See original details.",
            "等待超时，请检查游戏加载、网络或弹窗状态。" => "Timed out. Check the game.",
            _ => text
        };
        Check("locale callback translates nested status components", DailyUserText.Describe("rejected: screen_changed", Translate) == "Rejected: The screen changed.");
        Check("locale callback preserves protocol identifiers", DailyUserText.Describe("Missing native state: daily.dispatch.$self", Translate) == "Missing game state: daily.dispatch.$self");
        Check("locale callback handles nested Chinese wrapper", DailyUserText.Describe("导航未能安全恢复：Waypoint map not available", Translate) == "Navigation recovery failed: The destination is not listed.");
        Check("locale callback translates unknown diagnostic explanation", DailyUserText.Describe(unknown, Translate) == "See original details.");
        Check("locale callback localizes exception fallback", DailyUserText.Error(new TimeoutException("Unknown future wait"), Translate) == "Timed out. Check the game.");
        Check("locale callback preserves user names and paths", DailyUserText.Describe(mixed, Translate) == mixed);
        Check("account nickname matching a status is preserved", DailyUserText.Describe("游戏页面已变化，请重新确认当前界面。 · screen_changed", Translate) == "游戏页面已变化，请重新确认当前界面。 · The screen changed.");
        Check("default locale remains unchanged after alternate rendering", DailyUserText.Describe("rejected: screen_changed") == "操作未被接受：游戏页面已变化，请重新确认当前界面。");

        var core = Path.Combine(TestPaths.SourceRoot, "src", "Core");
        var reasons = new Regex("(?:DailyWorkflow|DailyWeeklyMission)\\.(?:Skipped|Partial)\\(\"([^\"]+)\"");
        foreach (var code in Directory.EnumerateFiles(core, "*.cs").SelectMany(f => reasons.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().Where(code => !Regex.IsMatch(code, @"\p{IsCJKUnifiedIdeographs}")))
            Check("workflow result has Chinese explanation: " + code, DailyUserText.Describe(code) != code && DailyUserText.Describe(code) != DailyUserText.Untranslated);
    }
}


using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace BD2Daily;

/// <summary>Presentation only. Never use localized text for protocol, recovery or persisted evidence.</summary>
public static class DailyUserText
{
    public const string Untranslated = "此提示暂未收录中文说明，请查看原始信息。";
    private static readonly FrozenDictionary<string, string> Messages = Load();
    private static readonly Regex Han = new(@"[\p{IsCJKUnifiedIdeographs}]", RegexOptions.CultureInvariant);
    private static readonly Regex ExceptionPrefix = new(@"^(?:[\w.]+\.)?\w*(?:Exception|Error):\s*(.+)$", RegexOptions.CultureInvariant);
    private static readonly (string Source, string Chinese)[] Prefixes =
    [
        (".NET queue: ", "日常队列："),
        ("Progress read: ", "读取运行进度："),
        ("Command outcome is uncertain; no replay: ", "操作结果尚未确认，未重复提交："),
        ("UI observation interrupted after dispatch; no replay: ", "提交后游戏状态读取中断，未重复操作："),
        ("Talent result requires reconciliation; original command preserved: ", "天赋使用结果需先核对，原操作已保留："),
        ("Native request rejected: ", "游戏拒绝了操作："),
        ("Native rejection: ", "游戏拒绝了操作："),
        ("Native chain incomplete: ", "游戏连续操作尚未完整结束："),
        ("Native response count differs: ", "服务器返回数量与预期不一致："),
        ("Monster Hunt response/cache differs: ", "魔兽结算与游戏状态不一致："),
        ("Hunt debit differs: ", "狩猎资源消耗与预期不一致："),
        ("Sweep popup changed: ", "扫荡确认弹窗已变化："),
        ("Messenger repair aborted: ", "派遣恢复已中止："),
        ("Equipment protected/unavailable: ", "所选装备受到保护或当前不可用："),
        ("No verified transition for: ", "尚未适配这条界面切换路径："),
        ("NPC progress query could not be verified; no NPC mutation submitted: ", "周NPC进度无法确认，未执行任务操作："),
        ("Unknown weekly mission condition: ", "暂未适配这类周常任务条件："),
        ("Weekly mission definition missing: ", "缺少周常任务规则："),
        ("Weekly mission identity differs: ", "周常任务标识不匹配："),
        ("Unresolved guild operation: ", "上次公会操作的结果尚未确认："),
        ("Missing observation: ", "未读到所需游戏状态："),
        ("Mirror surface unavailable: ", "镜中页面尚未就绪："),
        ("Missing trade collection ", "缺少交易数据列表："),
        ("Truncated trade collection ", "交易数据列表不完整："),
        ("Incomplete daily export: ", "日常规则导出不完整："),
        ("Duplicate daily row key: ", "日常规则存在重复项："),
        ("Need one enabled observed field ", "需要一个可用的操作入口："),
        ("Business scope must be an object for role: ", "操作范围格式无效："),
        ("Empty trace line ", "执行轨迹中有空记录："),
        ("Incomplete decision line ", "决策记录不完整："),
        ("Invalid integer: ", "数值参数无效："),
        ("rejected: ", "操作未被接受："),
        ("Foreground popup needs handling: ", "请先处理游戏弹窗："),
        ("Unrecognized popup while preparing menu: ", "准备主菜单时遇到未识别弹窗："),
        ("Reading unavailable: ", "暂时无法读取游戏数据："),
        ("Evidence observer is not ready: ", "游戏状态读取组件尚未就绪："),
        ("Missing native state: ", "未读到游戏状态："),
        ("Missing cache: ", "缺少游戏数据："),
        ("Incomplete native cache: ", "游戏数据不完整："),
        ("Required native response observer unavailable: ", "操作结果读取组件未就绪："),
        ("Connection preparation timed out: ", "连接准备超时："),
        ("Connection preparation failed: ", "连接准备失败："),
        ("Shop supply is missing or expired: ", "商店供货数据缺失或已过期："),
        ("Merchant has no reachable interaction approach: ", "暂未找到可接近商人的路线："),
        ("travel_unreachable:", "暂时无法到达目标："),
        ("Free draw table export incomplete: ", "免费抽取资料导出不完整："),
        ("Incomplete free draw table export: ", "免费抽取资料导出不完整："),
        ("Unsupported response shape: ", "当前游戏返回格式尚未适配："),
        ("Missing read member: ", "当前游戏缺少所需的数据字段："),
        ("Missing declared response variant: ", "缺少游戏返回类型："),
        ("Unknown weekly goal: ", "未识别的周常目标："),
        ("Unknown daily rules: ", "未识别的日常规则："),
        ("Unknown daily utility: ", "未识别的日常功能："),
        ("Missing module loader: ", "工具连接组件缺失："),
        ("Missing module load: ", "工具连接组件缺少启动入口："),
        ("Could not load file or assembly ", "无法加载所需组件，请检查工具文件是否完整。"),
        ("Failed to resolve assembly: ", "无法解析游戏组件，请重新准备与当前游戏配套的连接组件。"),
        ("Access to the path ", "无法访问所需文件，请检查文件权限或是否被其他程序占用。"),
        ("The process cannot access the file ", "文件被其他程序占用，请等待占用结束后重试。"),
        ("Could not find file ", "所需文件不存在，请检查工具文件是否完整。")
    ];
    private static readonly FrozenDictionary<string, string> Surfaces = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MenuUI"] = "主菜单", ["MessagePopupUI"] = "游戏提示框", ["EventPopupUI"] = "活动弹窗",
        ["PackListUI"] = "卡带选择", ["QuickMenuUI"] = "快捷菜单", ["CurrencyUI"] = "货币界面",
        ["SichuanBoardUI"] = "连连看盘面", ["RewardReceivePopupUI"] = "奖励领取弹窗",
        ["RewardUI"] = "奖励界面", ["RewardPopupUI"] = "奖励弹窗", ["PVPClassUpUI"] = "镜中段位提示",
        ["UpdateUI"] = "更新介绍", ["MiniGameDiceUI"] = "骰子活动", ["EventMainUI"] = "活动大厅",
        ["EventBattleUI"] = "活动战斗", ["TarosTacticsBingoUI"] = "战术教材大厅",
        ["TarosTacticsSeasonFinishUI"] = "战术教材赛季提示", ["TotalWarUI"] = "公会战大厅",
        ["FriendshipManageUI"] = "亲密度管理", ["FriendshipUI"] = "亲密度咨询",
        ["Title"] = "标题页", ["TitleScene"] = "标题页", ["Lobby"] = "大厅", ["LobbyScene"] = "大厅",
        ["Field"] = "地图", ["FieldScene"] = "地图", ["Battle"] = "战斗", ["BattleScene"] = "战斗",
        ["Login"] = "登录页", ["Loading"] = "加载中"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static FrozenDictionary<string, string> Load()
    {
        using var source = typeof(DailyUserText).Assembly.GetManifestResourceStream("BD2Daily.Messages.zh-CN.txt")
            ?? throw new InvalidDataException("缺少日常助手中文提示资源。");
        using var reader = new StreamReader(source);
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            int split = line.IndexOf('=');
            if (split < 1 || !messages.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim()))
                throw new InvalidDataException("日常助手中文提示资源包含重复或无效条目。");
        }
        return messages.ToFrozenDictionary(StringComparer.Ordinal);
    }
    public static string State(string? state) => state != null && Messages.TryGetValue(state, out var label) ? label : "未识别状态";
    public static string Scene(string? scene) => string.IsNullOrWhiteSpace(scene) ? "场景加载中" : Surfaces.GetValueOrDefault(scene, Han.IsMatch(scene) ? scene : "游戏场景已载入");
    public static string Describe(string? message) => Describe(message ?? "", 0, static text => text);
    public static string Describe(string? message, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        return translate(Describe(message ?? "", 0, translate)).Replace(Untranslated, translate(Untranslated), StringComparison.Ordinal);
    }
    private static string Describe(string raw, int depth, Func<string, string> translate)
    {
        if (depth > 8) return Untranslated;
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        string text = raw.Trim();
        if (Messages.TryGetValue(text, out var chinese)) return translate(chinese);
        if (Surfaces.TryGetValue(text, out chinese)) return translate(chinese);
        var exception = ExceptionPrefix.Match(text);
        if (exception.Success) return Describe(exception.Groups[1].Value, depth + 1, translate);
        foreach (var (source, translated) in Prefixes)
            if (text.StartsWith(source, StringComparison.Ordinal))
            {
                if (translated.EndsWith('。')) return translate(translated);
                string suffix = text[source.Length..];
                string rendered = Describe(suffix, depth + 1, translate);
                // IDs and paths are diagnostic values, not instructions. Keep them verbatim
                // when a recognized message needs them to identify the affected item.
                return translate(translated) + (rendered == Untranslated && Regex.IsMatch(suffix, @"^[A-Za-z0-9_$./\\:\-]+$") ? suffix : rendered);
            }
        int separator = text.LastIndexOf(" · ", StringComparison.Ordinal);
        if (separator > 0)
        {
            string tail = Describe(text[(separator + 3)..], depth + 1, translate);
            if (tail != Untranslated) return text[..separator] + " · " + tail;
        }
        if (text.Contains('\n'))
            return string.Join(Environment.NewLine, text.Replace("\r", "").Split('\n').Select(s => Describe(s, depth + 1, translate)));
        // Translate a nested engine error without rewriting arbitrary account names,
        // filesystem paths, quantities or protocol fields embedded in Chinese prose.
        for (int i = 0; i < text.Length; i++)
            if (text[i] is ':' or '：' && Han.IsMatch(text[..i]))
            {
                string suffix = text[(i + 1)..].TrimStart();
                string rendered = Describe(suffix, depth + 1, translate);
                if (rendered != Untranslated || Regex.IsMatch(text[..i], "失败|错误|未能|未继续|异常|未返回") && Regex.IsMatch(suffix, @"^[A-Za-z][A-Za-z ]{3,} ") && !Regex.IsMatch(suffix, @"^[A-Za-z]:[\\/]"))
                    return translate(text[..(i + 1)]) + rendered;
            }
        if (Han.IsMatch(text)) return translate(text);
        if (text.StartsWith("Stopped ", StringComparison.Ordinal) || text.StartsWith("Paused ", StringComparison.Ordinal))
            return translate("已停止后续操作，请核对当前游戏状态后继续。");
        return Untranslated;
    }
    public static string Error(Exception error) => Error(error, static text => text);
    public static string Error(Exception error, Func<string, string> translate)
    {
        string message = Describe(error.Message, 0, translate);
        if (message != Untranslated) return message.Replace(Untranslated, translate(Untranslated), StringComparison.Ordinal);
        return translate(error switch
        {
            OperationCanceledException => "操作已取消，已完成的进度会保留。",
            UnauthorizedAccessException => "访问被拒绝，请检查文件权限或当前游戏连接权限。",
            FileNotFoundException or DirectoryNotFoundException => "所需文件不存在，请检查工具文件和选择的路径。",
            TimeoutException => "等待超时，请检查游戏加载、网络或弹窗状态。",
            System.Text.Json.JsonException => "数据文件无法读取，请检查文件是否完整、格式是否正确。",
            IOException => "文件或连接暂不可用，请检查文件占用和游戏连接。",
            _ => "未能完成操作，请查看原始信息确认原因。"
        });
    }
    public static string Details(string raw, string? translated = null)
    {
        translated ??= Describe(raw);
        return string.IsNullOrEmpty(raw) || translated == raw ? translated : translated + "\n\n原始信息（诊断用）：\n" + raw;
    }
}


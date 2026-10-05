using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace BD2Daily;

public sealed record DailyHomePlan(string Kind, string Reason, JsonObject? Action = null);
/// <summary>Startup recovery uses observed native controls only. Active business is handled separately with proof.</summary>
public static class DailyHomeDecision
{
    // These native lobby entries were observed in the running client. Keep the
    // managed navigation; the recorded policy is covered by regression fixtures.
    private static readonly IReadOnlyDictionary<string, string> LobbyMenus = new Dictionary<string, string>
    {
        ["AvatarFishingHarborUI"] = "_objHomeMenuButton"
    };
    private static readonly IReadOnlyDictionary<string, string?> Pages = new Dictionary<string, string?>(DailyNavigationDecision.Closeable)
    {
        ["MyRoomUI"] = null,
        ["GuildUI"] = null,
        ["EventUI"] = null,
        ["ShopUI"] = null,
        ["EquipmentMakingUI"] = null,
        ["EquipmentUpgradePopupUI"] = null,
        ["SichuanStagePopupUI"] = null,
        ["HuntDispatchPopupUI"] = null,
        ["TarosTacticsBingoUI"] = "_objBackButton",
        ["EventBattleUI"] = null,
        ["EventMainUI"] = null,
        ["TarosTacticsSeasonFinishUI"] = "_btnBack",
        ["PVPClassUpUI"] = "_objCancelButton"
    };
    private static readonly IReadOnlyDictionary<string, string?> Results = new Dictionary<string, string?>
    {
        ["ItemGetPopupUI"] = "_objBackButton",
        ["EquipmentUpgradeResultPopupUI"] = "_objOKButton",
        ["EquipmentBatchUpgradeResultPopupUI"] = null,
        ["SichuanStageClearPopupUI"] = "_buttonExit"
    };
    private static string Text(JsonObject o, string key) => DailyNavigationDecision.Text(o, key);
    private static bool Flag(JsonObject o, string key) => o[key]?.GetValue<bool>() ?? false;
    private static IEnumerable<JsonObject> Targets(JsonObject o) => o["Targets"]?.AsArray().Select(n => n!.AsObject()) ?? [];
    private static bool Enabled(JsonObject o, string field) => Targets(o).Count(t => Text(t, "Field") == field && Flag(t, "Enabled")) == 1;
    public static bool Transitioning(JsonObject frame)
    {
        var types = DailyNavigationDecision.Types(frame);
        return types.Contains("LoadingUI") || (DailyNavigationDecision.SceneLoading(frame) || types.Contains("EntranceBackgroundCoverUI")) && !types.Contains("UpdateUI");
    }
    public static bool MenuReady(JsonObject frame, DailyNavigationPolicy policy) => DailyNavigationDecision.Rows(frame).Count(r => Text(r, "Type") == "MenuUI") == 1
        && DailyNavigationDecision.Rows(frame).Any(r => Text(r, "Type") == "MenuUI" && DailyNavigationDecision.ReadyInput(r, false) && DailyNavigationDecision.Blockers(frame, "MenuUI", policy).Length == 0);
    public static string Signature(JsonObject frame) => new JsonObject
    {
        ["Scene"] = frame["Scene"]?.DeepClone(),
        ["UiToken"] = frame["UiToken"]?.DeepClone(),
        ["Surfaces"] = new JsonArray(DailyNavigationDecision.Rows(frame).OrderBy(r => Text(r, "Type")).ThenBy(r => r["Id"]?.ToJsonString()).Select(r => (JsonNode)r.DeepClone()).ToArray())
    }.ToJsonString();
    // Menu counters and rotating management/news links update every second. They
    // do not restart the same menu's native readiness gate. Actual input still
    // binds the full fresh UiToken and target in the shared command driver.
    public static string MenuSignature(JsonObject frame, DailyNavigationPolicy policy) => new JsonObject
    {
        ["Scene"] = frame["Scene"]?.DeepClone(),
        ["Surfaces"] = new JsonArray(DailyNavigationDecision.Rows(frame)
            .Where(r => !policy.Background.Contains(Text(r, "Type"))).OrderBy(r => Text(r, "Type")).ThenBy(r => r["Id"]?.ToJsonString())
            .Select(r => (JsonNode)new JsonObject(r.Where(p => p.Key is "Type" or "Id" or "Path" or "Popup" or "Order" or "InputReady")
                .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())))).ToArray())
    }.ToJsonString();
    public static bool ResetContext(JsonObject frame, DailyNavigationPolicy policy)
    {
        var rows = DailyNavigationDecision.Rows(frame).Where(r => Text(r, "Type") == "MessagePopupUI").ToArray();
        if (rows.Length != 1)
            return false;
        var row = rows[0];
        return Text(row, "Path").Contains("ErrorMessagePopupUI", StringComparison.Ordinal)
            && row["Text"] is JsonArray texts && texts.Any(n => Regex.IsMatch(n!.GetValue<string>(), @"error\s*:\s*112003(?:\D|$)", RegexOptions.CultureInvariant))
            && DailyNavigationDecision.Blockers(frame, "MessagePopupUI", policy).Length == 0 && !Targets(row).Any(t => Text(t, "Field") == "_buttonCancel" && Flag(t, "Enabled"));
    }
    public static bool ResetPopup(JsonObject frame, DailyNavigationPolicy policy) => ResetContext(frame, policy) && DailyNavigationDecision.Rows(frame).Any(r => Text(r, "Type") == "MessagePopupUI" && DailyNavigationDecision.ReadyInput(r, false) && Enabled(r, "_buttonOK"));
    private static DailyHomePlan Wait(string reason) => new("waiting", reason);
    private static DailyHomePlan Block(string reason) => new("blocked", reason);
    private static DailyHomePlan Special(string kind) => new("recovery", kind);
    private static DailyHomePlan Step(JsonObject action) => new("action", Text(action, "reason"), action);
    private static JsonObject Close(string ui, string? field, string reason, bool absent = true)
    {
        var action = new JsonObject { ["ui"] = ui, ["reason"] = reason };
        if (field == null)
            action["back"] = true;
        else
            action["field"] = field;
        if (absent)
            action["absent"] = ui;
        return action;
    }
    public static DailyHomePlan Inspect(JsonObject frame, DailyNavigationPolicy policy)
    {
        var rows = DailyNavigationDecision.Rows(frame).ToArray();
        var types = DailyNavigationDecision.Types(frame);
        if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)) || types.Contains("BattleResultUI"))
            return Block("战斗或战斗结算仍由所属环节接续，保留现场。");
        if (types.Overlaps(["IntroUI", "DownloadPopupUI"]) || Text(frame, "Scene") is "Splash" or "ReGame")
            return Block("尚未完成账号登录或资源下载，请先完成日常连接。");
        if (Transitioning(frame))
            return Wait("等待原生加载或进场遮罩结束");
        var duplicates = rows.Where(r => !policy.Background.Contains(Text(r, "Type"))).GroupBy(r => Text(r, "Type")).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            return Block("页面不唯一，保留现场：" + string.Join("、", duplicates));
        var talent = DailyNavigationDecision.TalentAction(frame, policy);
        if (talent != null)
            return Step(talent);
        var notice = DailyNavigationDecision.Notice(frame, policy, out var noticeRow);
        if (noticeRow != null)
            return notice == null ? Wait("公告仍在加载或选项未就绪") : Step(notice);
        if (types.Contains("BalloonScriptUI"))
            return types.Overlaps(["SichuanBoardUI", "MiniGameDiceUI"]) ? Block("活动小游戏仍在进行，保留现场。") : Special("quiz");
        var story = DailyNavigationDecision.StoryAction(frame, policy);
        if (story != null)
            return Step(story);
        var weekly = DailyNavigationDecision.WeeklyResult(frame, policy);
        if (weekly != null)
            return Step(weekly);
        if (types.Contains("RewardReceivePopupUI") && DailyNavigationDecision.Blockers(frame, "RewardReceivePopupUI", policy).Length == 0)
            return Special("reward");
        foreach (var row in rows.OrderByDescending(r => r["Order"]?.GetValue<int>() ?? 0))
        {
            string type = Text(row, "Type");
            if (!Results.TryGetValue(type, out string? field) || DailyNavigationDecision.Blockers(frame, type, policy).Length > 0)
                continue;
            if (!DailyNavigationDecision.ReadyInput(row, false) || field != null && !Enabled(row, field))
                return Wait("等待结果展示的原生返回按钮：" + type);
            return Step(Close(type, field, "关闭已生成的结果展示，继续准备主菜单"));
        }
        if (types.Contains("MessagePopupUI"))
        {
            if (rows.Any(r => Text(r, "NativeContext") == "talent_inactive_ack_only"))
                return Wait("等待探查失效提示的原生确认按钮");
            return ResetContext(frame, policy) ? ResetPopup(frame, policy) ? Special("mission_reset") : Wait("等待任务重置提示的原生确认按钮") : Block("未知确认框，未确认购买、领取或重试；保留现场。");
        }
        if (types.Contains("UpdateUI"))
        {
            var row = rows.Single(r => Text(r, "Type") == "UpdateUI");
            if (DailyNavigationDecision.Blockers(frame, "UpdateUI", policy).Length > 0)
                return Block("更新介绍被其他弹窗遮挡，保留现场。");
            if (!DailyNavigationDecision.ReadyInput(row, false) || !Enabled(row, "_objBackButton"))
                return Wait("等待更新介绍的原生返回按钮");
            return Step(Close("UpdateUI", "_objBackButton", "Advance native update introduction", false));
        }
        if (types.Overlaps(["StorySkipUI", "StoryPopupUI", "ScriptUI"]))
            return rows.Any(r => Text(r, "NativeContext") is "story_skip" or "story_dialogue") ? Wait("等待已识别剧情的原生继续或跳过入口") : Block("对话没有已识别的原生剧情上下文，保留现场。");
        if (types.Overlaps(["MiniEventQuizUI", "BalloonScriptUI", "MiniEventMainUI"]))
            return types.Overlaps(["SichuanBoardUI", "MiniGameDiceUI"]) ? Block("活动小游戏仍在进行，保留现场。") : Special("quiz");
        if (types.Contains("QuestClearPopupUI"))
            return Wait("等待周任务结算的返回按钮");
        if (types.Contains("GachaResultUI"))
        {
            var row = rows.Single(r => Text(r, "Type") == "GachaResultUI");
            if (DailyNavigationDecision.Blockers(frame, "GachaResultUI", policy).Length > 0)
                return Block("抽卡结果被未知弹窗遮挡，保留现场。");
            foreach (string field in DailyNavigationDecision.ReadyInput(row, false) ? new[] { "_objBackButton", "_objSkipButton" } : new[] { "_objSkipButton" })
                if (Enabled(row, field) && (DailyNavigationDecision.ReadyInput(row, false) || frame["BridgeVersion"]?.GetValue<int>() >= 32))
                    return Step(Close("GachaResultUI", field, "结束已有抽卡结果展示，不再次抽取", false));
            return Wait("等待抽卡动画的原生跳过或返回入口");
        }
        if (types.Overlaps(["SichuanBoardUI", "SichuanStageFailedPopupUI", "FishingGameFieldDefaultUI", "MiniGameDiceUI"]))
            return Block("正在进行的小游戏由所属环节接续，未通用退出。");
        if (types.Contains("EventPopupUI") && !types.Contains("MenuUI"))
            return Block("活动弹窗不在登录菜单，保留给所属环节。");
        foreach (var row in rows.OrderByDescending(r => r["Order"]?.GetValue<int>() ?? 0))
        {
            string type = Text(row, "Type");
            if (!Pages.TryGetValue(type, out string? field) || DailyNavigationDecision.Blockers(frame, type, policy).Length > 0)
                continue;
            if (!DailyNavigationDecision.ReadyInput(row, false) || field != null && !Enabled(row, field))
                return Wait("等待已识别页面的原生返回按钮：" + type);
            return Step(Close(type, field, "离开已识别的非业务页面，继续日常"));
        }
        var unknown = rows.Where(r => Flag(r, "Popup") && !policy.Background.Contains(Text(r, "Type")) && Text(r, "Type") != "MenuUI").Select(r => Text(r, "Type")).ToArray();
        if (unknown.Length > 0)
            return Block("未识别的前台弹窗，保留现场：" + string.Join("、", unknown));
        if (types.Contains("MenuUI"))
            return MenuReady(frame, policy) ? new("ready", "主菜单已就绪") : Wait("等待主菜单输入就绪");
        if (!types.Any(t => !policy.Background.Contains(t)))
            return Wait("等待原生页面出现");
        var fields = policy.FieldMenu.Concat(LobbyMenus.Where(p => !policy.FieldMenu.ContainsKey(p.Key))).Where(p => types.Contains(p.Key)).ToArray();
        if (fields.Length != 1)
            return Block("没有唯一可观察的场域菜单入口，保留现场：" + string.Join("、", types.Order(StringComparer.Ordinal)));
        if (!DailyNavigationDecision.MapSceneReady(frame))
            return Wait("等待原生加载或进场遮罩结束");
        var selected = fields[0];
        var surface = rows.Single(r => Text(r, "Type") == selected.Key);
        if (!DailyNavigationDecision.ReadyInput(surface, false) || !Enabled(surface, selected.Value))
            return Wait("等待场域菜单的原生按钮启用");
        var action = Close(selected.Key, selected.Value, "Prepare menu for daily queue", false);
        action["expect"] = "MenuUI";
        return Step(action);
    }
}

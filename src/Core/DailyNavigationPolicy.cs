using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Shared read-only UI policy. The same source is embedded for the managed route.</summary>
public sealed class DailyNavigationPolicy
{
    public HashSet<string> Background
    {
        get;
    }
    public HashSet<string> SafeDismiss
    {
        get;
    }
    public IReadOnlyDictionary<string, string> FieldMenu
    {
        get;
    }
    public double SettleSeconds
    {
        get;
    }
    public double TotalSeconds
    {
        get;
    }
    public double RequestSeconds
    {
        get;
    }
    public string Fingerprint
    {
        get;
    }
    public DailyNavigationPolicy(JsonObject value)
    {
        if (value["schema"]?.GetValue<int>() != 1)
            throw new InvalidDataException("不支持的导航策略。");
        Fingerprint = DailyIdentity.Hash(value.ToJsonString());
        HashSet<string> Names(string key) => value[key]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Background = Names("background_surfaces");
        SafeDismiss = Names("safe_dismiss");
        FieldMenu = value["field_menu"]!.AsObject().ToDictionary(x => x.Key, x => x.Value!.GetValue<string>(), StringComparer.Ordinal);
        RequestSeconds = value["request_timeout_seconds"]!.GetValue<double>();
        if (RequestSeconds <= 0 || RequestSeconds > 120 || !double.IsFinite(RequestSeconds))
            throw new InvalidDataException("请求时限无效。");
        SettleSeconds = value["settle_seconds"]!.GetValue<double>();
        TotalSeconds = value["total_timeout_seconds"]!.GetValue<double>();
        if (SettleSeconds < 0 || !double.IsFinite(SettleSeconds) || TotalSeconds <= 0 || TotalSeconds > 600 || !double.IsFinite(TotalSeconds))
            throw new InvalidDataException("导航时限无效。");
    }
    public static DailyNavigationPolicy Load()
    {
        using var stream = typeof(DailyNavigationPolicy).Assembly.GetManifestResourceStream("BD2Daily.ui-policy.json") ?? throw new IOException("缺少共享导航策略。");
        return new(JsonNode.Parse(stream)!.AsObject());
    }
}

/// <summary>Native page decisions only. No command, journal or queue writes.</summary>
public static class DailyNavigationDecision
{
    public static readonly HashSet<string> Transitions = ["LoadingUI", "EntranceBackgroundCoverUI"];
    public static readonly HashSet<string> FieldPack = ["GameFieldDefaultUI", "CafeteriaFieldDefaultUI", "AvatarLifeGameFieldDefaultUI", "AvatarFishingHarborUI"];
    public static readonly HashSet<string> FieldStages = ["weekly_mainline", "weekly_npc", "weekly_steal", "collection_sync", "square", "goddess", "square_ranking", "trade", "weekly_fishing", "mirror", "weekly_book", "cafeteria_guests"];
    public static readonly HashSet<string> Overlays = ["MenuUI", "PackListUI", "PackInfoUI", "PackCollectionUI", "QuickMenuUI"];
    public static readonly HashSet<string> Suppressible = ["NewsPopupEventUI", "PackagePopupUI", "AttendanceSpecialPackageUI", "BundlePackageUI", "BundleGroupPackageUI", "BundleGroupRelayPackageUI", "BonusBundleGroupPackageUI", "ClearPackageUI", "LoginPassPackageUI", "SkinPackageUI"];
    public static IReadOnlyDictionary<string, string?> Closeable
    {
        get;
    } = new Dictionary<string, string?>
    {
        ["SichuanMainUI"] = null,
        ["MiniGameHubUI"] = null,
        ["PackInfoUI"] = "_objBackButton",
        ["PackCollectionUI"] = null,
        ["PackListUI"] = null,
        ["QuickMenuUI"] = null,
        ["InventoryManageUI"] = null,
        ["MissionUI"] = null,
        ["PassUI"] = null,
        ["MailUI"] = null,
        ["GachaMainUI"] = null,
        ["WorldMapUI"] = null,
        ["HuntOrAirwayUI"] = null,
        ["ManagementRewardPopupUI"] = null,
        ["TotalWarUI"] = null,
        ["FriendshipUI"] = null,
        ["FriendshipManageUI"] = null
    };
    public static IReadOnlyDictionary<string, HashSet<string>> ResumeSurfaces
    {
        get;
    } = new Dictionary<string, HashSet<string>>
    {
        ["weekly_npc"] = ["QuestBoardUI", "QuestPopupUI"],
        ["weekly_mainline"] = ["QuestBoardUI", "QuestPopupUI"],
        ["weekly_steal"] = ["QuestBoardUI", "QuestPopupUI"],
        ["weekly_sichuan"] = ["MiniGameHubUI", "SichuanMainUI", "SichuanStagePopupUI", "SichuanBoardUI", "SichuanStageClearPopupUI"],
        ["friendship"] = ["FriendshipUI", "FriendshipManageUI"],
        ["trade"] = ["ShopUI", "ShopPopupUI", "BuyFavoritePopupUI", "DiscountPopupUI", "CookingUI", "CookingSelectUI", "QuickMenuUI"],
        ["daily_dispatch"] = ["GameFieldDefaultUI"],
        ["free_draws"] = ["GachaMainUI", "GachaResultUI"],
        ["event_battle"] = ["EventMainUI", "EventBattleUI"],
        ["tactics"] = ["TarosTacticsBingoUI"],
        ["weekly_book"] = ["TotalWarUI", "BattleResultUI"],
        ["weekly_fishing"] = ["FishingGameFieldDefaultUI", "AvatarFishingWorldMapUI"],
        ["mirror"] = ["BattleUI_PVP", "BattleAutoSettingPopupUI", "BattleAutoCurrencyAccelSettingPopupUI", "PVPAutoHistoryPopupUI", "BattleResultUI"],
        ["hunting"] = ["HuntOrAirwayUI", "HuntDispatchPopupUI"],
        ["daily_hunt"] = ["HuntOrAirwayUI", "HuntDispatchPopupUI"],
        ["stones"] = ["HuntOrAirwayUI", "HuntDispatchPopupUI"]
    };
    public static IEnumerable<JsonObject> Rows(JsonObject f) => f["Surfaces"]!.AsArray().Select(x => x!.AsObject());
    public static string Text(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";
    public static bool ReadyInput(JsonObject o, bool fallback = true) => o["InputReady"]?.GetValue<bool>() ?? fallback;
    private static bool Popup(JsonObject o) => o["Popup"]?.GetValue<bool>() ?? false;
    private static int Order(JsonObject o) => o["Order"]?.GetValue<int>() ?? 0;
    public static HashSet<string> Types(JsonObject f) => Rows(f).Select(r => Text(r, "Type")).ToHashSet(StringComparer.Ordinal);
    private static IEnumerable<JsonObject> Targets(JsonObject o) => o["Targets"]?.AsArray().Select(x => x!.AsObject()) ?? [];
    private static bool Enabled(JsonObject t) => t["Enabled"]?.GetValue<bool>() ?? false;
    private static bool HasTarget(JsonObject o, string field) => Targets(o).Any(t => Text(t, "Field") == field && Enabled(t));
    public static string[] Blockers(JsonObject f, string type, DailyNavigationPolicy policy)
    {
        var rows = Rows(f).ToArray();
        var matches = rows.Where(r => Text(r, "Type") == type).ToArray();
        var target = matches.Length == 1 ? matches[0] : null;
        bool InFront(JsonObject r)
        {
            if (type == "UpdateUI" && Text(r, "Type") == "EntranceBackgroundCoverUI")
                return false;
            string parent = r["Path"]?.GetValue<string>() ?? "\0";
            if (target != null && Text(target, "Path").StartsWith(parent + "/", StringComparison.Ordinal))
                return false;
            if (target != null && type == "ItemGetPopupUI" && Text(r, "Type") == "EquipmentInfoPopupUI" && Text(r, "Path").StartsWith((target["Path"]?.GetValue<string>() ?? "\0") + "/", StringComparison.Ordinal))
                return false;
            return !(target != null && Popup(target) && target.ContainsKey("Order") && r.ContainsKey("Order") && Order(r) < Order(target));
        }
        return rows.Where(r => Popup(r) && Text(r, "Type") != type && !policy.Background.Contains(Text(r, "Type")) && InFront(r)).Select(r => Text(r, "Type")).ToArray();
    }
    // Empty is the native home-menu scene, also used between map loads. It is
    // not itself a loading signal. Map arrival remains a separate requirement.
    public static bool SceneLoading(JsonObject f) => f.ContainsKey("Scene") && string.IsNullOrWhiteSpace(Text(f, "Scene"));
    public static bool MapSceneReady(JsonObject f) => !string.IsNullOrWhiteSpace(Text(f, "Scene")) && Text(f, "Scene") is not ("Empty" or "Splash" or "ReGame");
    public static bool SurfaceSceneReady(JsonObject f, string ui) => !(FieldPack.Contains(ui) || ui == "FishingGameFieldDefaultUI" || ui.StartsWith("BattleUI", StringComparison.Ordinal)) || MapSceneReady(f);
    public static string Phase(JsonObject f, string? stage = null)
    {
        var types = Types(f);
        if (SceneLoading(f) || types.Overlaps(Transitions))
            return "loading";
        var battles = types.Where(t => t.StartsWith("BattleUI", StringComparison.Ordinal)).ToHashSet();
        if (battles.Count > 0 && !MapSceneReady(f))
            return "loading";
        if (battles.Count == 0)
            return "page";
        DailyStageObservation.OwnedBattles.TryGetValue(stage ?? "", out var owner);
        if (battles.Count != 1 || owner == null || !battles.Contains(owner))
            return "foreign_battle";
        if (types.Contains("BattleResultUI") || stage == "mirror" && types.Contains("PVPAutoHistoryPopupUI"))
            return "owned_result";
        if (stage == "mirror")
        {
            var row = Rows(f).First(r => Text(r, "Type") == owner);
            if (Targets(row).Any(t => Enabled(t) && Text(t, "Field").EndsWith("/Button - AutoSetting", StringComparison.Ordinal)) || types.Overlaps(["BattleAutoSettingPopupUI", "BattleAutoCurrencyAccelSettingPopupUI"]))
                return "owned_lobby";
        }
        return "owned_battle";
    }
    public static HashSet<string> Accepted(string stage)
    {
        HashSet<string> names = ["MenuUI"];
        if (FieldStages.Contains(stage))
            names.UnionWith(FieldPack);
        if (ResumeSurfaces.TryGetValue(stage, out var resume))
            names.UnionWith(resume);
        return names;
    }
    public static bool Ready(JsonObject f, string stage, DailyNavigationPolicy p)
    {
        string state = Phase(f, stage);
        if (state is "loading" or "foreign_battle")
            return false;
        if (state.StartsWith("owned_", StringComparison.Ordinal))
            return true;
        var accepted = Accepted(stage);
        return Rows(f).Any(r => accepted.Contains(Text(r, "Type")) && SurfaceSceneReady(f, Text(r, "Type")) && ReadyInput(r) && Blockers(f, Text(r, "Type"), p).Length == 0);
    }
    private static JsonObject Back(string ui, string reason, bool absent = true) => new() { ["ui"] = ui, ["back"] = true, ["reason"] = reason, ["absent"] = absent ? ui : null };
    private static JsonObject Click(string ui, string field, string reason) => new() { ["ui"] = ui, ["field"] = field, ["absent"] = ui, ["reason"] = reason };
    public static JsonObject? CloseAction(JsonObject f, DailyNavigationPolicy p)
    {
        if (Phase(f) != "page" || Rows(f).Any(r => Text(r, "Type") == "MenuUI" && ReadyInput(r) && Blockers(f, "MenuUI", p).Length == 0))
            return null;
        var row = Rows(f).Where(r => Closeable.ContainsKey(Text(r, "Type")) && ReadyInput(r) && Blockers(f, Text(r, "Type"), p).Length == 0).OrderByDescending(Order).FirstOrDefault();
        if (row == null)
            return null;
        string ui = Text(row, "Type"), reason = "离开已识别的非业务页面，继续日常";
        return Closeable[ui] is string field ? Click(ui, field, reason) : Back(ui, reason);
    }
    public static JsonObject? OverlayAction(JsonObject f, DailyNavigationPolicy p)
    {
        var types = Types(f);
        if (types.Overlaps(Transitions) || types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            return null;
        var row = Rows(f).Where(r => Overlays.Contains(Text(r, "Type")) && ReadyInput(r) && Blockers(f, Text(r, "Type"), p).Length == 0).OrderByDescending(Order).FirstOrDefault();
        if (row == null)
            return null;
        string ui = Text(row, "Type");
        return ui == "PackInfoUI" ? Click(ui, "_objBackButton", "关闭当前卡带说明，复用已加载场景") : Back(ui, "返回已加载的目标卡带，不重复进入");
    }
    public static JsonObject? TalentAction(JsonObject f, DailyNavigationPolicy p)
    {
        if (f["BridgeVersion"]?.GetValue<int>() < 101)
            return null;
        var rows = Rows(f).Where(r => Text(r, "Type") == "MessagePopupUI").ToArray();
        if (rows.Length != 1 || Text(rows[0], "NativeContext") != "talent_inactive_ack_only" || !ReadyInput(rows[0], false) || Blockers(f, "MessagePopupUI", p).Length > 0 || !HasTarget(rows[0], "_buttonOK"))
            return null;
        return new()
        {
            ["ui"] = "MessagePopupUI",
            ["operation"] = "talent_error_ack",
            ["value"] = 100005,
            ["absent"] = "MessagePopupUI",
            ["reason"] = "关闭探查失效提示并重新观察；不重放领取或天赋请求"
        };
    }
    public static JsonObject? WeeklyResult(JsonObject f, DailyNavigationPolicy p)
    {
        var rows = Rows(f).Where(r => Text(r, "Type") == "QuestClearPopupUI").ToArray();
        if (rows.Length != 1 || !ReadyInput(rows[0], false) || Blockers(f, "QuestClearPopupUI", p).Length > 0 || !HasTarget(rows[0], "_objTodayBackButton"))
            return null;
        return Click("QuestClearPopupUI", "_objTodayBackButton", "关闭已完成周任务的结果展示，回读服务器周次数");
    }
    public static JsonObject? StoryAction(JsonObject f, DailyNavigationPolicy p)
    {
        if (Types(f).Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            return null;
        foreach (string ui in new[] { "StoryPopupUI", "StorySkipUI" })
        {
            var rows = Rows(f).Where(r => Text(r, "Type") == ui && Text(r, "NativeContext") == "story_skip" && ReadyInput(r)).ToArray();
            if (rows.Length != 1 || Blockers(f, ui, p).Length > 0)
                continue;
            string field = ui == "StoryPopupUI" ? "_objOKButton" : "_objSkipButton";
            if (!HasTarget(rows[0], field))
                continue;
            if (ui == "StoryPopupUI")
            {
                var target = Targets(rows[0]).First(t => Text(t, "Field") == field && Enabled(t));
                var pointers = Targets(rows[0]).Where(t => JsonNode.DeepEquals(t["Id"], target["Id"]) && Text(t, "Route") == "pointer" && Enabled(t)).ToArray();
                return pointers.Length == 1 ? Click(ui, Text(pointers[0], "Field"), "确认跳过场域剧情，接续当前路线") : null;
            }
            return new()
            {
                ["ui"] = ui,
                ["operation"] = "story_skip",
                // The toolbar may remain while the native skip confirmation opens.
                // Re-observe the next state instead of waiting for the whole dialogue to disappear.
                ["reason"] = "跳过场域入场剧情，接续当前路线"
            };
        }
        var dialogues = Rows(f).Where(r => Text(r, "Type") == "ScriptUI" && Text(r, "NativeContext") == "story_dialogue" && ReadyInput(r)).ToArray();
        if (dialogues.Length == 1 && Blockers(f, "ScriptUI", p).Length == 0 && Targets(dialogues[0]).Any(t => Enabled(t) && Text(t, "Field") is "_buttonStart" or "_objButtonTouch"))
            return new()
            {
                ["ui"] = "ScriptUI",
                ["operation"] = "story_advance",
                ["reason"] = "推进原生可继续的场域旁白，等待跳过入口"
            };
        return null;
    }
    public static JsonObject? Notice(JsonObject f, DailyNavigationPolicy p, out JsonObject? surface)
    {
        var types = Types(f);
        surface = Rows(f).Where(r => (p.SafeDismiss.Contains(Text(r, "Type")) || Suppressible.Contains(Text(r, "Type")) || Text(r, "Type") == "GameQuitPopupUI") && (Text(r, "Type") != "EventPopupUI" || types.Contains("MenuUI"))).OrderByDescending(Order).FirstOrDefault();
        if (surface == null || Blockers(f, Text(surface, "Type"), p).Length > 0)
        {
            surface = null;
            return null;
        }
        return ReadyInput(surface) ? NoticeAction(surface) : null;
    }
    private static JsonObject? NoticeAction(JsonObject surface)
    {
        string ui = Text(surface, "Type");
        if (ui == "GameQuitPopupUI")
            return HasTarget(surface, "_objCloseButton") ? Click(ui, "_objCloseButton", "取消退出游戏确认，继续当前日常") : null;
        if (Suppressible.Contains(ui))
        {
            string state = surface["NoticeSuppression"]?.GetValue<string>() ?? "unavailable";
            if (state is "loading" or "unavailable")
                return null;
            if (state == "unchecked")
                return new()
                {
                    ["ui"] = ui,
                    ["operation"] = "notice_suppress",
                    ["reason"] = "勾选7天内不再显示"
                };
            if (state is not ("checked" or "hidden" or "other_period"))
                throw new StageHostException("adapter", "Unknown notice checkbox state");
        }
        var action = Back(ui, "关闭已处理的活动公告", false);
        action.Remove("absent");
        return action;
    }
    public static JsonObject? Home(JsonObject f, DailyNavigationPolicy p)
    {
        var recovery = TalentAction(f, p);
        if (recovery != null)
            return recovery;
        var close = CloseAction(f, p);
        if (close != null)
            return close;
        var rows = Rows(f).ToArray();
        var types = Types(f);
        StageHostException Fault(string message) => new("adapter", message);
        JsonObject One(string name) => rows.First(r => Text(r, "Type") == name);
        if (types.Contains("QuickMenuUI"))
        {
            if (Blockers(f, "QuickMenuUI", p).Length > 0)
                throw Fault("Quick menu has a foreground popup");
            return Back("QuickMenuUI", "Close idle field quick menu");
        }
        if (types.Contains("PackListUI"))
        {
            if (Blockers(f, "PackListUI", p).Length > 0)
                throw Fault("Cartridge selector has a foreground popup");
            return Back("PackListUI", "Close idle cartridge selector");
        }
        if (types.Contains("TarosTacticsSeasonFinishUI"))
        {
            if (Blockers(f, "TarosTacticsSeasonFinishUI", p).Length > 0)
                throw Fault("Tactics season notice has an unrelated foreground popup");
            if (!HasTarget(One("TarosTacticsSeasonFinishUI"), "_btnBack"))
                throw Fault("Tactics season notice is not ready");
            return Click("TarosTacticsSeasonFinishUI", "_btnBack", "Acknowledge new tactics season");
        }
        if (types.Contains("PVPClassUpUI"))
        {
            if (Blockers(f, "PVPClassUpUI", p).Length > 0)
                throw Fault("Rank notice has an unrelated foreground popup");
            return Click("PVPClassUpUI", "_objCancelButton", "Confirm native season rank notice");
        }
        if (types.Contains("UpdateUI"))
        {
            if (Blockers(f, "UpdateUI", p).Length > 0)
                throw Fault("Update slideshow has an unrelated foreground popup");
            if (!HasTarget(One("UpdateUI"), "_objBackButton"))
                throw Fault("Update slideshow is not ready");
            var a = Click("UpdateUI", "_objBackButton", "Advance native update introduction");
            a.Remove("absent");
            return a;
        }
        var unknown = rows.Where(r => Popup(r) && !p.Background.Contains(Text(r, "Type")) && !p.SafeDismiss.Contains(Text(r, "Type")) && Text(r, "Type") != "MenuUI").ToArray();
        if (types.Contains("EventPopupUI") && !types.Contains("MenuUI"))
            throw Fault("Event popup is outside the login menu; leave it for its owning task");
        if (unknown.Length > 0)
            throw Fault("Unrecognized popup while preparing menu: " + string.Join(", ", unknown.Select(r => Text(r, "Type"))));
        var dismiss = rows.Where(r => p.SafeDismiss.Contains(Text(r, "Type"))).OrderByDescending(Order).FirstOrDefault();
        if (dismiss != null)
        {
            string name = Text(dismiss, "Type");
            if (Suppressible.Contains(name))
                return NoticeAction(dismiss);
            if (name == "EventPopupUI")
            {
                var a = Back(name, "Advance recognized login attendance notice", false);
                a.Remove("absent");
                return a;
            }
            return Back(name, "Close recognized login notice");
        }
        if (types.Contains("MenuUI") && Blockers(f, "MenuUI", p).Length == 0)
            return null;
        if (types.Contains("TarosTacticsBingoUI"))
        {
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
                throw Fault("Tactics battle still active; preserve its progress");
            if (!HasTarget(One("TarosTacticsBingoUI"), "_objBackButton"))
                throw Fault("Tactics lobby back button is not ready");
            var a = Click("TarosTacticsBingoUI", "_objBackButton", "Return from tactics lobby before daily queue");
            a.Remove("absent");
            a["expect"] = "MenuUI";
            return a;
        }
        if (!types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            foreach (string name in new[] { "TotalWarUI", "FriendshipManageUI", "FriendshipUI", "EventBattleUI", "EventMainUI" })
                if (types.Contains(name) && Blockers(f, name, p).Length == 0)
                    return Back(name, "Return from activity lobby for daily queue");
        if (!MapSceneReady(f))
            return null;
        var choices = p.FieldMenu.Where(row => types.Contains(row.Key)).ToArray();
        if (choices.Length != 1)
            throw Fault("No unambiguous observed field menu; no input sent");
        var selected = choices[0];
        if (rows.Where(r => Text(r, "Type") == selected.Key).SelectMany(Targets).Count(t => Text(t, "Field") == selected.Value && Enabled(t)) != 1)
            throw Fault("Native menu button is not ready");
        var result = Click(selected.Key, selected.Value, "Prepare menu for daily queue");
        result.Remove("absent");
        result["expect"] = "MenuUI";
        return result;
    }
}

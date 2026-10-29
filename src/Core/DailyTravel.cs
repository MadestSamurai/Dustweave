using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Target-aware cartridge routing shared by stages; never re-enters a cartridge already loaded.</summary>
public static class DailyTravel
{
    public const string CurrentPack = "ὪὩὣὪὭὩὪὬὣὦὣ", EntryPack = "ὦὡὪὦὥὫὦὡὬὠὢ";
    public static readonly string[] Fields = ["GameFieldDefaultUI", "CafeteriaFieldDefaultUI", "AvatarLifeGameFieldDefaultUI", "AvatarFishingHarborUI", "FishingGameFieldDefaultUI"];
    public static readonly HashSet<string> Overlays = ["MenuUI", "PackListUI", "PackInfoUI", "PackCollectionUI", "QuickMenuUI"];
    private static readonly DailyNavigationPolicy Policy = DailyNavigationPolicy.Load();
    // Native menus can stay on Empty indefinitely; only overlays or a missing scene imply transit.
    private static bool InTransit(JsonObject frame) => string.IsNullOrWhiteSpace(S(frame["Scene"])) || DailyNavigationDecision.Types(frame).Overlaps(DailyNavigationDecision.Transitions);
    private static bool UnsentTransition(DailyStepException error, ref int observationRetries) =>
        error.PresentationChanged || error.ObservationPending && ++observationRetries <= 3;
    internal static bool SquareBookLobby(JsonObject frame) =>
        S(frame["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && !InTransit(frame) &&
        !DailyNavigationDecision.Types(frame).Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)) &&
        DailyNavigationDecision.Rows(frame).Count(r => S(r["Type"]) == "TotalWarUI") == 1 &&
        DailyNavigationDecision.Rows(frame).Any(r => S(r["Type"]) == "TotalWarUI" && DailyNavigationDecision.ReadyInput(r)) &&
        DailyNavigationDecision.Blockers(frame, "TotalWarUI", Policy).Length == 0;
    // Only callers that currently own travel to the square may leave this lobby.
    // This deliberately does not run in generic presentation/Book recovery.
    internal static async Task<bool> LeaveSquareBookLobby(DailyWorkflow w, JsonObject frame)
    {
        if (!SquareBookLobby(frame)) return false;
        await w.Step("TotalWarUI", back: true, absent: "TotalWarUI", reason: "退出广场入口自动打开的末日之书大厅，继续原路线");
        DailyJson.Write(Path.Combine(w.Root, "live", "travel-diagnostics", "square-book-" + w.Driver.UtcTicks + ".json"),
            O(("reason", "square_book_lobby"), ("at", w.Driver.UtcTicks), ("scene", frame["Scene"]), ("surfaces", new JsonArray(DailyNavigationDecision.Types(frame).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()))));
        return true;
    }
    public static async Task<bool> RecoverPresentation(DailyWorkflow w, JsonObject frame)
    {
        if (await DailyMirror.RecoverRankPresentation(w, frame)) return true;
        bool changed = false;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var action = DailyHomeDecision.BackgroundErrorAction(frame, Policy) ?? DailyNavigationDecision.TalentAction(frame, Policy) ?? DailyNavigationDecision.Notice(frame, Policy, out _) ?? DailyNavigationDecision.StoryAction(frame, Policy) ?? DailyNavigationDecision.WeeklyResult(frame, Policy);
            if (action == null && DailyHomeDecision.BookRecordPresentation(frame, Policy) is { } record)
            {
                if (record.Kind == "blocked") throw new StageHostException("adapter", record.Reason);
                if (record.Kind == "waiting") { await w.Delay(150); return true; }
                action = record.Action;
            }
            if (action == null) return changed;
            try { await w.Driver.NavigationAsync(action); return true; }
            catch (StageHostException e) when (e.Kind == "navigation_changed")
            {
                // No game input occurred. Re-select from the new frame, never replay
                // the old action into the next scene. ReadBound retains identity checks.
                changed = true;
                await w.Delay(150);
                frame = (await w.Observe()).Frame;
            }
        }
        throw new StageHostException("adapter", "页面持续切换，未能完成剧情或提示收尾；已保留当前路线进度。");
    }
    public static async Task Ready(DailyWorkflow w, string ui, double seconds = 60, IEnumerable<string>? absent = null)
    {
        using var waiting = DailyObservationWindow.Begin(seconds, () => w.Time);
        var excluded = (absent ?? []).Append("LoadingUI").Append("EntranceBackgroundCoverUI").ToHashSet();
        double end = w.Time + seconds;
        string? prior = null;
        double stable = 0;
        while (w.Time < end)
        {
            var f = (await w.Observe()).Frame;
            if (InTransit(f)) { prior = null; await w.Delay(150); continue; }
            if (await RecoverPresentation(w, f))
            {
                prior = null;
                continue;
            }
            var types = DailyNavigationDecision.Types(f);
            bool ready = !InTransit(f) && DailyNavigationDecision.SurfaceSceneReady(f, ui) && !types.Overlaps(excluded) && DailyNavigationDecision.Rows(f).Any(r => S(r["Type"]) == ui && DailyNavigationDecision.ReadyInput(r)) && DailyNavigationDecision.Blockers(f, ui, Policy).Length == 0;
            string key = S(f["Scene"]) + "|" + string.Join(",", DailyNavigationDecision.Rows(f).Where(r => S(r["Type"]) == ui).Select(r => S(r["Id"])));
            if (ready)
            {
                if (prior != key)
                {
                    prior = key;
                    stable = w.Time;
                }
                if (w.Time - stable >= .4)
                    return;
            }
            else
                prior = null;
            await w.Delay(150);
        }
        throw new StageHostException("adapter", "页面尚未就绪：" + ui);
    }
    public static async Task<bool> Reuse(DailyWorkflow w, int? packId = null, int? packType = null, string[]? surfaces = null, string[]? extra = null, bool waitForTarget = false, double seconds = 60)
    {
        using var waiting = DailyObservationWindow.Begin(seconds, () => w.Time);
        if (packId == null && packType == null)
            throw new ArgumentException("Target cartridge required");
        surfaces ??= Fields;
        var overlays = Overlays.Concat(extra ?? []).ToHashSet();
        double end = w.Time + seconds;
        string? stable = null, arrival = null;
        double since = 0, arrivedAt = 0;
        int bookReturns = 0, observationRetries = 0;
        while (w.Time < end)
        {
            var f = (await w.Observe()).Frame;
            var types = DailyNavigationDecision.Types(f);
            if (InTransit(f))
            {
                stable = arrival = null;
                await w.Delay(200);
                continue;
            }
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
                throw new StageHostException("adapter", "战斗中不能切换卡带。");
            if (await Insertion(w, packId, packType) || await RecoverPresentation(w, f))
            {
                stable = null;
                continue;
            }
            if (!types.Overlaps(Fields.Concat(overlays).Concat(surfaces).Append("TotalWarUI")) && !PackEntries(f).Any())
            {
                await w.Delay(200);
                continue;
            }
            var e = await w.Evidence("navigation.pack");
            // Do not combine the previous UI scene with a newly updated pack cache.
            var observed = e["Frame"]!.AsObject();
            if (InTransit(observed) || S(observed["Scene"]) != S(f["Scene"]))
            {
                stable = arrival = null;
                await w.Delay(200);
                continue;
            }
            f = observed;
            int id = I(R(e, "navigation.pack", CurrentPack + ".Id")), kind = I(R(e, "navigation.pack", CurrentPack + ".PackType"));
            Require(id > 0, "Current cartridge identity unavailable");
            bool matches = (!packId.HasValue || id == packId) && (!packType.HasValue || kind == packType);
            if (matches)
            {
                string arrivalKey = id + "|" + kind + "|" + S(f["Scene"]);
                if (arrival != arrivalKey) { arrival = arrivalKey; arrivedAt = w.Time; }
                if (w.Time - arrivedAt < .3) { await w.Delay(150); continue; }
                if (kind == 11 && surfaces.Contains("GameFieldDefaultUI") && SquareBookLobby(f))
                {
                    if (bookReturns++ >= 2) throw new StageHostException("adapter", "广场入口反复打开末日之书；请离开入口后重试此环节。");
                    try { await LeaveSquareBookLobby(w, f); }
                    catch (DailyStepException error) when (UnsentTransition(error, ref observationRetries)) { await w.Delay(150); }
                    stable = null;
                    continue;
                }
                var row = DailyNavigationDecision.Rows(f).Where(r => overlays.Contains(S(r["Type"])) && DailyNavigationDecision.ReadyInput(r) && DailyNavigationDecision.Blockers(f, S(r["Type"]), Policy).Length == 0).OrderByDescending(r => N(r["Order"])).FirstOrDefault();
                if (row != null)
                {
                    string ui = S(row["Type"]);
                    try { await w.Step(ui, ui == "PackInfoUI" ? "_objBackButton" : null, back: ui != "PackInfoUI", absent: ui, reason: "复用当前已加载卡带"); }
                    catch (DailyStepException error) when (UnsentTransition(error, ref observationRetries))
                    {
                        // Re-read bound identity and the current destination before selecting
                        // navigation again. Never reuse the rejected command or weaken its gate.
                        arrival = null;
                        await w.Delay(200);
                    }
                    stable = null;
                    continue;
                }
            }
            bool ready = matches && DailyNavigationDecision.MapSceneReady(f) && surfaces.Any(ui => DailyNavigationDecision.Rows(f).Any(r => S(r["Type"]) == ui && DailyNavigationDecision.ReadyInput(r)) && DailyNavigationDecision.Blockers(f, ui, Policy).Length == 0);
            if (!matches && !waitForTarget || ready)
            {
                string marker = id + "|" + kind + "|" + S(f["Scene"]) + "|" + matches;
                if (stable != marker)
                {
                    stable = marker;
                    since = w.Time;
                }
                if (w.Time - since >= .3)
                    return matches;
            }
            else
                stable = null;
            await w.Delay(200);
        }
        throw new StageHostException("adapter", "目标卡带尚未稳定，没有重复发送切换请求。");
    }
    private static async Task<bool> Insertion(DailyWorkflow w, int? packId, int? packType)
    {
        if (!await w.Has("PackInfoUI"))
            return false;
        var e = await w.Evidence("navigation.pack.entry");
        var rows = Readings(e, "navigation.pack.entry").ToArray();
        if (rows.Length != 1)
            return false;
        var r = rows[0];
        const string countPath = "ὬὤὤὥὠὯὠὥὣὯὦ.Count";
        int count = I(r[countPath]);
        if (packId.HasValue && I(r[EntryPack + ".Id"]) != packId || packType.HasValue && I(r[EntryPack + ".PackType"]) != packType || count < 1 || count > 32 || r["ὠὡὠὥὬὪὡὩὬὠὢ"] == null || B(r["ὠὡὠὥὬὪὡὩὬὠὢ"]) || !B(r["_objPackTouchRewardButton.activeInHierarchy"]))
            return false;
        await w.Step("PackInfoUI", "_objPackTouchRewardButton", reason: "完成当前目标的原生卡带插入动画");
        double end = w.Time + 4;
        using var waiting = DailyObservationWindow.Begin(4, () => w.Time);
        while (w.Time < end)
        {
            if (!await w.Has("PackInfoUI"))
                return true;
            var now = Readings(await w.Evidence("navigation.pack.entry"), "navigation.pack.entry").Single();
            Require(N(now[EntryPack + ".Id"]) == N(r[EntryPack + ".Id"]), "Cartridge changed during insertion");
            if (N(now[countPath]) == count - 1)
                return true;
            await w.Delay(150);
        }
        throw new StageHostException("adapter", "卡带插入动画没有推进，未盲目重复输入。");
    }
    // The native pack-list button is shared by fields and lobbies. MenuUI._buttonPack instead opens the collection guide.
    private static IEnumerable<JsonObject> PackEntries(JsonObject frame) => DailyNavigationDecision.Rows(frame).Where(r => DailyNavigationDecision.MapSceneReady(frame) && !B(r["Popup"]) && S(r["Type"]) != "FishingGameFieldDefaultUI" && !S(r["Type"]).StartsWith("BattleUI", StringComparison.Ordinal) && DailyNavigationDecision.ReadyInput(r, false) && (r["Targets"] as JsonArray)?.OfType<JsonObject>().Count(t => S(t["Field"]) == "_buttonPackList" && B(t["Enabled"])) == 1 && DailyNavigationDecision.Blockers(frame, S(r["Type"]), Policy).Length == 0);
    public static async Task PackList(DailyWorkflow w, double seconds = 30)
    {
        using var waiting = DailyObservationWindow.Begin(seconds, () => w.Time);
        double end = w.Time + seconds;
        var visited = new HashSet<string>();
        int observationRetries = 0;
        string lastScene = "", lastPages = "";
        while (w.Time < end)
        {
            var f = (await w.Observe()).Frame;
            if (InTransit(f)) { await w.Delay(150); continue; }
            var types = DailyNavigationDecision.Types(f);
            lastScene = S(f["Scene"]); lastPages = string.Join(", ", types.Order());
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
                throw new StageHostException("adapter", "战斗中不能切换卡带。");
            try
            {
                if (types.Contains("PackListUI"))
                {
                    await Ready(w, "PackListUI", Math.Max(.1, end - w.Time));
                    return;
                }
                if (await RecoverPresentation(w, f)) continue;
                var entries = PackEntries(f).ToArray();
                if (entries.Length == 1)
                {
                    string ui = S(entries[0]["Type"]);
                    Require(visited.Add(lastScene + "|" + N(entries[0]["Id"]) + "|" + ui + "|list"), "卡带入口未推进，已保留现场，没有重复点击。");
                    await w.Step(ui, "_buttonPackList", expect: "PackListUI", reason: "使用当前页面的原生卡带切换入口");
                    await Ready(w, "PackListUI", Math.Max(.1, end - w.Time));
                    return;
                }
                if (entries.Length > 1)
                    throw new StageHostException("adapter", "同时出现多个卡带入口，等待界面稳定后再试。");
                // Close only recognized return paths. Unknown dialogs remain untouched.
                var overlay = DailyNavigationDecision.Rows(f).Where(r => Overlays.Contains(S(r["Type"])) && DailyNavigationDecision.ReadyInput(r, false) && DailyNavigationDecision.Blockers(f, S(r["Type"]), Policy).Length == 0).OrderByDescending(r => N(r["Order"])).FirstOrDefault();
                if (overlay != null)
                {
                    string ui = S(overlay["Type"]);
                    Require(visited.Add(lastScene + "|" + N(overlay["Id"]) + "|" + ui + "|back"), "卡带返回路径循环，已保留现场，没有反复返回菜单。");
                    await w.Step(ui, ui == "PackInfoUI" ? "_objBackButton" : null, back: ui != "PackInfoUI", absent: ui, reason: "返回当前场景的卡带切换入口");
                }
            }
            catch (DailyStepException error) when (UnsentTransition(error, ref observationRetries))
            {
                // No native input ran; resolve the new page instead of failing downstream stages.
                visited.Clear();
            }
            await w.Delay(150);
        }
        throw new StageHostException("adapter", "当前场景没有就绪且唯一的卡带切换入口：" + lastScene + "；" + lastPages);
    }
    public static async Task<bool> Enter(DailyWorkflow w, int tab, string observation, int? packId = null, int? packType = null, string[]? surfaces = null)
    {
        surfaces ??= ["GameFieldDefaultUI"];
        if (await Reuse(w, packId, packType, surfaces))
            return true;
        await PackList(w);
        await w.Step("PackListUI", "$pointer/Parent/PackParent/TabButton/UIScrollView - Tap/Viewport/Content/Tab - Content - " + tab + "/Button - Tab - Content", reason: "选择目标卡带分类");
        var e = await w.WaitEvidence([observation], state => Readings(state, observation).Any(), 10, "卡带列表尚未载入");
        var choices = Readings(e, observation).Where(r => (!packId.HasValue || N(r[EntryPack + ".Id"]) == packId) && (!packType.HasValue || N(r[EntryPack + ".PackType"]) == packType)).ToArray();
        if (choices.Length == 0)
        {
            await w.Step("PackListUI", back: true, absent: "PackListUI");
            return false;
        }
        Require(choices.Length == 1, "Cartridge selection is ambiguous");
        await w.Step("PackListUI", "$pointer/Parent/PackParent/UIScrollView/UIViewport/UIContent/" + S(choices[0]["gameObject.name"]) + "/PackItem", reason: "进入已核对的目标卡带");
        return await Reuse(w, packId, packType, surfaces, waitForTarget: true);
    }
}

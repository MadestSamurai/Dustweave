using System.Diagnostics;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Managed navigation and startup recovery share one bounded route and the original command driver.</summary>
public sealed class DailyStageNavigation
{
    public Action<JsonObject>? DiagnosticObservation { get; set; }
    private readonly Func<Task<DailyStageFrame>> read;
    private readonly Func<bool> stopped;
    private readonly Func<double> clock;
    private readonly Func<TimeSpan, Task> delay;
    private readonly DailyNavigationPolicy policy;
    private readonly double recoverySeconds;
    private string? lastInputSignature, lastInputAction;
    private double lastInputAt;
    public DailyStageNavigation(Func<Task<DailyStageFrame>> read, Func<bool> stopped, DailyNavigationPolicy? policy = null,
        Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, double recoverySeconds = 25)
    {
        this.read = read;
        this.stopped = stopped;
        this.policy = policy ?? DailyNavigationPolicy.Load();
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.recoverySeconds = recoverySeconds;
    }
    private static void ValidateStage(string stage)
    {
        if (!DailyStageCatalog.Adapters.Contains(stage, StringComparer.Ordinal))
            throw new StageHostException("protocol", "未知日常环节。");
    }
    private async Task<DailyStageFrame> ReadObservedBound(JsonObject context)
    {
        var state = await read();
        if (!JsonNode.DeepEquals(state.Context, context))
            throw new StageHostException("identity", "导航期间账号、角色、连接或重置周期已变化。");
        DiagnosticObservation?.Invoke(state.Frame);
        return state;
    }
    private async Task<JsonObject> ReadBound(JsonObject context) => (await ReadObservedBound(context)).Frame;
    private Task Wait() => delay(TimeSpan.FromMilliseconds(200));
    private static JsonObject Result(bool safe, string reason, IEnumerable<string> actions) => new() { ["safe"] = safe, ["reason"] = reason, ["actions"] = new JsonArray(actions.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()) };
    private async Task Submit(string stage, JsonObject context, JsonObject frame, JsonObject action, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        if (stopped())
            throw new StageHostException("stopped", "导航已停止，未提交后续输入。");
        await relay("navigation_step", stage, new JsonObject { ["action"] = action.DeepClone() });
        lastInputSignature = DailyHomeDecision.Signature(frame);
        lastInputAction = action.ToJsonString();
        lastInputAt = clock();
        if (action["operation"]?.GetValue<string>() == "notice_suppress")
        {
            var notice = DailyNavigationDecision.Rows(frame).Single(r => DailyNavigationDecision.Text(r, "Type") == DailyNavigationDecision.Text(action, "ui"));
            double until = clock() + 5;
            while (clock() < until)
            {
                if (stopped())
                    throw new StageHostException("stopped", "确认公告选项时已停止。");
                var current = await ReadBound(context);
                var matches = DailyNavigationDecision.Rows(current).Where(r => JsonNode.DeepEquals(r["Id"], notice["Id"]) && DailyNavigationDecision.Text(r, "Type") == DailyNavigationDecision.Text(notice, "Type")).ToArray();
                if (matches.Length == 0 || matches.Length == 1 && DailyNavigationDecision.Text(matches[0], "NoticeSuppression") == "checked")
                    return;
                await Wait();
            }
            throw new StageHostException("adapter", "7天选项未确认，保留弹窗供检查");
        }
    }
    private bool AwaitProgress(JsonObject frame, JsonObject action)
    {
        if (lastInputAction != action.ToJsonString() || lastInputSignature != DailyHomeDecision.Signature(frame))
            return false;
        if (clock() - lastInputAt >= policy.RequestSeconds)
            throw new StageHostException("adapter", "navigation_no_progress：原输入后页面没有推进，未重发。");
        return true;
    }
    private static bool Presentation(JsonObject action, DailyNavigationPolicy policy) => DailyNavigationDecision.Text(action, "ui") is "GameQuitPopupUI" or "MessagePopupUI" or "ScriptUI" or "StorySkipUI" or "StoryPopupUI" or "QuestClearPopupUI" or "ItemGetPopupUI" or "EquipmentUpgradeResultPopupUI" or "EquipmentBatchUpgradeResultPopupUI" or "UpdateUI"
        || policy.SafeDismiss.Contains(DailyNavigationDecision.Text(action, "ui"));
    private JsonObject Ready(string stage, JsonObject frame, List<string> route)
    {
        lastInputSignature = null;
        lastInputAction = null;
        return new()
        {
            ["state"] = "ready",
            ["stage"] = stage,
            ["scene"] = frame["Scene"]?.DeepClone(),
            ["engine"] = "dotnet-navigation-v2",
            ["route"] = new JsonArray((route.Count == 0 ? new[] { "reuse" } : route.AsEnumerable()).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            ["surfaces"] = new JsonArray(DailyNavigationDecision.Rows(frame).Select(r => DailyNavigationDecision.Text(r, "Type")).Order(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray())
        };
    }
    public async Task<JsonObject> EnterAsync(string stage, JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay, Func<DailyStageFrame, bool>? owns = null)
    {
        ValidateStage(stage);
        double end = clock() + policy.TotalSeconds;
        var route = new List<string>();
        int changed = 0, steps = 0;
        double? menuAt = null;
        string? menuSignature = null, stable = null;
        double stableAt = 0;
        while (clock() < end)
        {
            if (stopped())
                throw new StageHostException("stopped", "导航已停止，未提交后续输入。");
            var observed = await ReadObservedBound(context);
            var frame = observed.Frame;
            var types = DailyNavigationDecision.Types(frame);
            string phase = DailyNavigationDecision.Phase(frame, stage);
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)))
            {
                if (phase == "foreign_battle")
                    throw new StageHostException("adapter", "当前战斗属于其他环节，保留现场");
                if (phase == "loading" || DailyHomeDecision.Transitioning(frame))
                {
                    menuAt = null;
                    menuSignature = null;
                    stable = null;
                    await Wait();
                    continue;
                }
                return Ready(stage, frame, route);
            }
            if (DailyHomeDecision.Transitioning(frame))
            {
                menuAt = null;
                menuSignature = null;
                stable = null;
                await Wait();
                continue;
            }
            var plan = DailyHomeDecision.Inspect(frame, policy);
            JsonObject? action = plan.Action;
            if (DailyNavigationDecision.FieldStages.Contains(stage) && action == null)
            {
                var overlay = DailyNavigationDecision.OverlayAction(frame, policy);
                if (overlay?["ui"]?.GetValue<string>() != "MenuUI")
                    action = overlay;
            }
            bool urgent = action != null && (Presentation(action, policy) || DailyNavigationDecision.FieldStages.Contains(stage) && DailyNavigationDecision.Overlays.Contains(DailyNavigationDecision.Text(action, "ui"))) || plan.Kind == "recovery";
            bool owned = owns?.Invoke(observed) ?? false;
            bool stageReady = DailyNavigationDecision.Ready(frame, stage, policy);
            if (types.Contains("MenuUI") && plan.Kind != "ready" && !(action != null && DailyNavigationDecision.Accepted(stage).Contains(DailyNavigationDecision.Text(action, "ui"))))
                stageReady = false;
            if (!urgent && (owned || stageReady))
            {
                if (types.Contains("MenuUI") && !owned)
                {
                    if (!DailyHomeDecision.MenuReady(frame, policy))
                    {
                        menuAt = null;
                        menuSignature = null;
                        await Wait();
                        continue;
                    }
                    string signature = DailyHomeDecision.MenuSignature(frame, policy);
                    if (menuSignature != signature)
                    {
                        menuSignature = signature;
                        menuAt = clock();
                    }
                    if (clock() - menuAt!.Value < policy.SettleSeconds)
                    {
                        await Wait();
                        continue;
                    }
                }
                return Ready(stage, frame, route);
            }
            menuAt = null;
            menuSignature = null;
            if (!urgent && stage == "daily_dispatch" && !types.Contains("GameFieldDefaultUI") && DailyNavigationDecision.Ready(frame, "square", policy))
            {
                // Server-based cartridge selection remains an untouched business adapter.
                var result = await relay("navigate", stage, null);
                await ReadBound(context);
                return result;
            }
            if (plan.Kind == "blocked")
                throw new StageHostException("adapter", plan.Reason);
            if (plan.Kind == "recovery")
            {
                if (++steps > 80)
                    throw new StageHostException("adapter", "主菜单恢复超过步骤上限，保留现场。");
                try
                {
                    await relay("home_recovery", stage, new JsonObject { ["kind"] = plan.Reason });
                    await ReadBound(context);
                    route.Add("home:" + plan.Reason);
                    stable = null;
                    changed = 0;
                }
                catch (StageHostException e) when (e.Kind == "navigation_changed") { if (++changed > 5) throw new StageHostException("adapter", "启动恢复现场持续变化，未派发原输入。"); await Wait(); }
                continue;
            }
            if (action == null)
            {
                stable = null;
                await Wait();
                continue;
            }
            if (AwaitProgress(frame, action))
            {
                await Wait();
                continue;
            }
            string marker = DailyHomeDecision.Signature(frame) + "|" + action.ToJsonString();
            if (stable != marker)
            {
                stable = marker;
                stableAt = clock();
            }
            if (clock() - stableAt < .4)
            {
                await Wait();
                continue;
            }
            try
            {
                if (++steps > 80)
                    throw new StageHostException("adapter", "主菜单恢复超过步骤上限，保留现场。");
                await Submit(stage, context, frame, action, relay);
                route.Add(DailyNavigationDecision.Text(action, "ui"));
                changed = 0;
                stable = null;
            }
            catch (StageHostException e) when (e.Kind == "navigation_changed")
            {
                if (++changed > 5)
                    throw new StageHostException("adapter", "导航页面持续变化，尚未提交原输入。");
                await Wait();
            }
        }
        throw new StageHostException("adapter", "准备主菜单或目标环节超时，已保留最后现场：" + stage);
    }
    public async Task<JsonObject> RecoverAsync(string stage, JsonObject context, string error, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        ValidateStage(stage);
        double end = clock() + recoverySeconds;
        var actions = new List<string>();
        int changed = 0;
        double? menuAt = null;
        string? menuSignature = null;
        while (clock() < end)
        {
            if (stopped())
                return Result(false, "paused", actions);
            var frame = await ReadBound(context);
            var types = DailyNavigationDecision.Types(frame);
            if (types.Any(t => t.StartsWith("BattleUI", StringComparison.Ordinal)) || types.Contains("BattleResultUI"))
                return Result(false, "active_or_unsettled_battle", actions);
            if (DailyHomeDecision.Transitioning(frame))
            {
                menuAt = null;
                menuSignature = null;
                await Wait();
                continue;
            }
            var plan = DailyHomeDecision.Inspect(frame, policy);
            if (plan.Kind == "ready")
            {
                string signature = DailyHomeDecision.MenuSignature(frame, policy);
                if (menuSignature != signature)
                {
                    menuSignature = signature;
                    menuAt = clock();
                }
                if (clock() - menuAt!.Value >= policy.SettleSeconds)
                {
                    lastInputSignature = null;
                    lastInputAction = null;
                    return Result(true, "safe_menu", actions);
                }
                await Wait();
                continue;
            }
            menuAt = null;
            menuSignature = null;
            if (plan.Kind == "blocked")
                return Result(false, plan.Reason, actions);
            if (plan.Kind == "recovery")
            {
                try
                {
                    await relay("home_recovery", stage, new JsonObject { ["kind"] = plan.Reason });
                    await ReadBound(context);
                    actions.Add("home:" + plan.Reason);
                    changed = 0;
                }
                catch (StageHostException e) when (e.Kind == "navigation_changed") { if (++changed > 5) return Result(false, "navigation_no_progress", actions); await Wait(); }
                continue;
            }
            var action = plan.Action;
            if (action == null)
            {
                await Wait();
                continue;
            }
            try
            {
                if (AwaitProgress(frame, action))
                {
                    await Wait();
                    continue;
                }
                if (actions.Count >= 80)
                    return Result(false, "navigation_no_progress", actions);
                await Submit(stage, context, frame, action, relay);
                actions.Add(DailyNavigationDecision.Text(action, "ui"));
                changed = 0;
            }
            catch (StageHostException e) when (e.Kind == "navigation_changed") { if (++changed > 5) return Result(false, "navigation_no_progress", actions); await Wait(); }
            catch (StageHostException e) when (e.Kind == "adapter" && e.Message.StartsWith("navigation_no_progress", StringComparison.Ordinal)) { return Result(false, "navigation_no_progress", actions); }
        }
        return Result(false, "recovery_timeout", actions);
    }
}


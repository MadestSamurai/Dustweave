using System.Diagnostics;
using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Only the native daily free-all route; previews and consuming confirmations have separate durable states.</summary>
public sealed class DailyFreeDrawStage : IDailyManagedStage
{
    private const string CategoryPrefix = "$pointer/UIRoot/Mask/BottomObject/Layout - MainCategory - Tab/Button - ";
    private readonly DailyCommandDriver driver; private readonly DailyManagedBusiness business; private readonly Func<bool> stopped; private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay;
    private DailyFreeDrawRules rules; private readonly Func<Task<DailyFreeDrawData>>? prepare; private DailyFreeDrawData? data; private readonly DailyNavigationPolicy policy = DailyNavigationPolicy.Load();
    public DailyFreeDrawStage(DailyCommandDriver driver, DailyManagedBusiness business, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, DailyFreeDrawRules? rules = null, Func<Task<DailyFreeDrawData>>? prepare = null)
    {
        this.prepare = prepare;
        this.driver = driver;
        this.business = business;
        this.stopped = stopped;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.rules = rules ?? DailyFreeDrawRules.Load();
    }
    private void Stop()
    {
        if (stopped())
            throw new StageHostException("stopped", "免费抽取已停止，原预览和回执保留。");
    }
    private static bool SameCycle(JsonObject op, DailyStageFrame frame) => JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]) && JsonNode.DeepEquals(op["server"], frame.Context["server"]) && DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), frame.Frame);
    public static bool OwnsPreview(JsonObject op, DailyStageFrame frame)
    {
        if (op["role"]?.GetValue<string>() != DailyFreeDrawProof.Role || op["state"]?.GetValue<string>() != "preview_ready" || op["preview_frame"] is not JsonObject saved || !SameCycle(op, frame) || !JsonNode.DeepEquals(saved["Scene"], frame.Frame["Scene"]) || !JsonNode.DeepEquals(saved["UiToken"], frame.Frame["UiToken"]))
            return false;
        var old = DailyNavigationDecision.Rows(saved).Where(r => DailyNavigationDecision.Text(r, "Type") == "MessagePopupUI").ToArray();
        var current = DailyNavigationDecision.Rows(frame.Frame).Where(r => DailyNavigationDecision.Text(r, "Type") == "MessagePopupUI").ToArray();
        return old.Length == 1 && current.Length == 1 && JsonNode.DeepEquals(old[0]["Id"], current[0]["Id"]) && DailyNavigationDecision.Blockers(frame.Frame, "MessagePopupUI", DailyNavigationPolicy.Load()).Length == 0;
    }
    private JsonObject? OwnsResult(DailyStageFrame frame)
    {
        foreach (var op in business.Records(frame.Context, DailyFreeDrawProof.Role, includeLegacy: false).Where(op => op["state"]?.GetValue<string>() == "completed" && op["presentation_closed"]?.GetValue<bool>() != true && SameCycle(op, frame)).OrderByDescending(op => DailyEvidence.Integer(op["at"])))
        {
            // Reconnection or a completed-looking journal alone cannot authorize animation inputs.
            var captured = op["presentation_frame"] as JsonObject ?? op["after"]!["Frame"]!.AsObject();
            var saved = DailyNavigationDecision.Rows(captured).Where(r => DailyNavigationDecision.Text(r, "Type") == "GachaResultUI").ToArray();
            var current = DailyNavigationDecision.Rows(frame.Frame).Where(r => DailyNavigationDecision.Text(r, "Type") == "GachaResultUI").ToArray();
            if (saved.Length != 1 || current.Length != 1 || !JsonNode.DeepEquals(saved[0]["Id"], current[0]["Id"]) || !JsonNode.DeepEquals(captured["Scene"], frame.Frame["Scene"]))
                continue;
            business.Verify(op, op["events"]!.AsArray(), op["after"]!.AsObject());
            return op;
        }
        return null;
    }
    public bool CanResume(DailyStageFrame frame)
    {
        var types = DailyNavigationDecision.Types(frame.Frame);
        if (types.Contains("MessagePopupUI"))
            return business.Records(frame.Context, DailyFreeDrawProof.Role, includeLegacy: false).Any(op => OwnsPreview(op, frame));
        if (types.Contains("GachaResultUI"))
            return OwnsResult(frame) != null;
        return types.Contains("GachaMainUI") && DailyNavigationDecision.Blockers(frame.Frame, "GachaMainUI", policy).Length == 0;
    }
    public static string? ResultTarget(JsonObject frame)
    {
        var rows = DailyNavigationDecision.Rows(frame).Where(r => DailyNavigationDecision.Text(r, "Type") == "GachaResultUI").ToArray();
        if (rows.Length != 1 || DailyNavigationDecision.Blockers(frame, "GachaResultUI", DailyNavigationPolicy.Load()).Length > 0)
            return null;
        bool ready = DailyNavigationDecision.ReadyInput(rows[0]);
        if (!ready && (frame["BridgeVersion"]?.GetValue<int>() ?? 0) < 32)
            return null;
        foreach (string field in ready ? new[] { "_objBackButton", "_objSkipButton" } : new[] { "_objSkipButton" })
            if (rows[0]["Targets"]!.AsArray().Any(t => t?["Field"]?.GetValue<string>() == field && t["Enabled"]?.GetValue<bool>() == true))
                return field;
        return null;
    }
    private async Task FinishResultAsync(JsonObject op)
    {
        double end = clock() + 120, last = -2;
        int actions = 0;
        bool resultSeen = op["presentation_seen"]?.GetValue<bool>() == true ||
            DailyNavigationDecision.Types((op["presentation_frame"] ?? op["after"]?["Frame"] ?? new JsonObject { ["Surfaces"] = new JsonArray() }).AsObject()).Contains("GachaResultUI");
        try
        {
            while (clock() < end)
            {
                Stop();
                var current = await driver.ObserveAsync();
                if (!SameCycle(op, current))
                    throw new StageHostException("identity", "抽取结果收尾期间身份或周期改变。");
                var types = DailyNavigationDecision.Types(current.Frame);
                if (types.Contains("GachaResultUI"))
                {
                    resultSeen = true;
                    if (op["presentation_seen"]?.GetValue<bool>() != true || !JsonNode.DeepEquals(op["presentation_frame"]?["UiToken"], current.Frame["UiToken"]))
                    {
                        op["presentation_seen"] = true;
                        op["presentation_frame"] = current.Frame.DeepClone();
                        business.Save(op);
                    }
                }
                if (resultSeen && types.Contains("GachaMainUI") && !types.Contains("GachaResultUI") && DailyNavigationDecision.Rows(current.Frame).Any(r => DailyNavigationDecision.Text(r, "Type") == "GachaMainUI" && DailyNavigationDecision.ReadyInput(r)) && DailyNavigationDecision.Blockers(current.Frame, "GachaMainUI", policy).Length == 0)
                {
                    op["presentation_closed"] = true;
                    op["presentation_after"] = current.Frame.DeepClone();
                    business.Save(op);
                    return;
                }
                string? target = ResultTarget(current.Frame);
                if (target != null && actions < 8 && clock() - last >= 2)
                {
                    try
                    {
                        await driver.SendObservedAsync(new()
                        {
                            ["ui"] = "GachaResultUI",
                            ["field"] = target,
                            ["reason"] = "结束已确认免费抽取的动画"
                        });
                        actions++;
                    }
                    catch (DailyStepException e) when (e.Kind == "rejected" && e.Message.StartsWith("Need one enabled observed field ", StringComparison.Ordinal)) { }
                    last = clock();
                }
                await delay(TimeSpan.FromMilliseconds(200));
            }
            throw new StageHostException("adapter", "免费抽取结果展示未结束；奖励已确认，不重新抽取。");
        }
        catch (Exception error) { op["presentation_error"] = error.Message; business.Save(op); throw; }
    }
    private async Task<JsonObject> ConfirmPreviewAsync(JsonObject op, JsonObject context)
    {
        Stop();
        var current = await driver.ObserveAsync();
        if (!OwnsPreview(op, current))
            throw new StageHostException("pending", "原免费抽取预览与当前弹窗不一致，保留现场。");
        var before = await driver.EvidenceAsync(["gacha", "missions.cache"]);
        var old = DailyFreeDrawProof.Users(op["before"]!.AsObject());
        var fresh = DailyFreeDrawProof.Users(before);
        if (old.Count != fresh.Count || old.Any(p => !fresh.TryGetValue(p.Key, out var value) || !JsonNode.DeepEquals(p.Value, value)))
            throw new StageHostException("pending", "免费预览期间抽取缓存改变，未提交确认。");
        if (!before["Taps"]!.AsArray().Any(t => t?.GetValue<string>() == DailyFreeDrawProof.Role))
            throw new StageHostException("adapter", "免费抽取回执观察未就绪，未提交确认。");
        op["before"] = before.DeepClone();
        business.Save(op);
        data?.AssertReady();
        return await business.CommitAsync(op, context);
    }
    private async Task<JsonObject> DrawAsync(string category, JsonObject context)
    {
        Stop();
        data?.AssertReady();
        business.RequireResolved(context, DailyFreeDrawProof.Role);
        var before = await driver.EvidenceAsync(["gacha", "missions.cache"]);
        if (DailyEvidence.Reading(before, "gacha.ui", "_goFreeGachaMacro.activeInHierarchy")?.GetValue<bool>() != true)
            return new()
            {
                ["category"] = category,
                ["state"] = "no_free_draws",
                ["actions"] = 0
            };
        if (DailyNavigationDecision.Types((await driver.ObserveAsync()).Frame).Contains("MessagePopupUI"))
            throw new StageHostException("adapter", "已有确认弹窗不属于本次免费抽取。");
        var op = business.Create(context, DailyFreeDrawProof.Role, before, new()
        {
            ["native_free_only"] = true,
            ["category"] = category
        }, new()
        {
            ["ui"] = "MessagePopupUI",
            ["field"] = "_buttonOK",
            ["absent"] = "MessagePopupUI",
            ["reason"] = "确认原生每日免费批次"
        });
        op["rules"] = rules.Projection.DeepClone();
        business.Save(op);
        await business.PreviewAsync(op, context, new()
        {
            ["ui"] = "GachaMainUI",
            ["field"] = "_goFreeGachaMacro",
            ["expect"] = "MessagePopupUI",
            ["reason"] = "预览原生每日免费批次"
        });
        await ConfirmPreviewAsync(op, context);
        await FinishResultAsync(op);
        return new()
        {
            ["category"] = category,
            ["state"] = "completed",
            ["id"] = op["id"]!.DeepClone(),
            ["result"] = op["result"]!.DeepClone()
        };
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        Stop();
        var initial = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(initial.Context, context))
            throw new StageHostException("identity", "免费抽取现场与队列不一致。");
        var pending = business.Records(context, DailyFreeDrawProof.Role).Where(DailyManagedBusiness.Pending).ToArray();
        JsonObject? preview = pending.Length == 1 && OwnsPreview(pending[0], initial) ? pending[0] : null;
        if (pending.Length > 0 && preview == null)
            throw new StageHostException("pending", "免费抽取仍有原操作待核对，不提交导航或新抽取。");
        var results = new JsonArray();
        if (prepare != null)
        {
            data = await prepare();
            rules = data.Rules;
            data.AssertReady();
            if (preview != null && !JsonNode.DeepEquals(preview["rules"]?["source"], rules.Projection["source"]))
                throw new StageHostException("pending", "原免费预览的数据身份已变化，未提交确认。");
        }
        if (preview != null)
        {
            await ConfirmPreviewAsync(preview, context);
            await FinishResultAsync(preview);
            results.Add(new JsonObject { ["category"] = preview["scope"]!["category"]!.DeepClone(), ["state"] = "completed", ["id"] = preview["id"]!.DeepClone(), ["result"] = preview["result"]!.DeepClone(), ["resumed_preview"] = true });
        }
        else if (DailyNavigationDecision.Types(initial.Frame).Contains("GachaResultUI"))
        {
            var owned = OwnsResult(initial) ?? throw new StageHostException("adapter", "当前抽取结果没有本队列的已确认回执，保留现场。");
            await FinishResultAsync(owned);
        }
        var current = await driver.ObserveAsync();
        var types = DailyNavigationDecision.Types(current.Frame);
        if (types.Contains("MessagePopupUI"))
            throw new StageHostException("adapter", "未知确认弹窗，未提交输入。");
        if (!types.Contains("GachaMainUI"))
            await driver.SendObservedAsync(new()
            {
                ["ui"] = "MenuUI",
                ["field"] = "_buttonGacha",
                ["expect"] = "GachaMainUI",
                ["reason"] = "检查每日免费抽取"
            });
        foreach (string category in new[] { "Costume", "Equipment" })
        {
            Stop();
            await driver.SendObservedAsync(new()
            {
                ["ui"] = "GachaMainUI",
                ["field"] = CategoryPrefix + category,
                ["reason"] = "检查该类别的每日免费次数"
            });
            results.Add(await DrawAsync(category, context));
        }
        Stop();
        await driver.SendObservedAsync(new()
        {
            ["ui"] = "GachaMainUI",
            ["field"] = "_objBackButton",
            ["expect"] = "MenuUI",
            ["reason"] = "免费抽取结束后返回主菜单"
        });
        return new()
        {
            ["state"] = results.Any(r => r?["state"]?.GetValue<string>() == "completed") ? "completed" : "skipped",
            ["operations"] = results,
            ["engine"] = "dotnet-free-draws-v1",
            ["data_preflight"] = data?.Proof.DeepClone()
        };
    }
}







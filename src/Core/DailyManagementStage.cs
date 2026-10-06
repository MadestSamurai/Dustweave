using System.Diagnostics;
using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Native all-settlement and helper-only settlement share managed transactions and the original assignment proof.</summary>
public sealed class DailyManagementStage : IDailyManagedStage
{
    private const string MenuPrefix = "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/";
    private const string HelperField = "$pointer/Button - background/Parent/Image - Backgrond/AvatarLifeSettlementInfo/EnableRoot/Reward/RewardItemsObject/Button - Settlement";
    private readonly string root, kind; private readonly DailyCommandDriver driver; private readonly DailyManagedBusiness business; private readonly Func<bool> stopped; private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay; private readonly Func<JsonObject, DailyPreferences> preferences; private readonly DailyNavigationPolicy policy = DailyNavigationPolicy.Load();
    public DailyManagementStage(string root, DailyCommandDriver driver, DailyManagedBusiness business, Func<bool> stopped, string kind = "management", Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, Func<JsonObject, DailyPreferences>? preferences = null)
    {
        if (kind is not ("management" or "cafeteria_income" or "life_helpers"))
            throw new ArgumentException("Unknown managed settlement stage");
        this.root = root;
        this.driver = driver;
        this.business = business;
        this.stopped = stopped;
        this.kind = kind;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.preferences = preferences ?? (context => new DailyPreferenceStore(root).Read(context["actor"]![3]!.GetValue<string>()));
    }
    private void Stop()
    {
        if (stopped() || driver.PauseRequested)
            throw new StageHostException("stopped", "经营收益领取已停止，原回执保留。");
    }
    private void RequireResolved(JsonObject context)
    {
        foreach (string role in new[] { DailyManagementProof.Role, DailyManagementProof.Helpers, DailyManagementProof.Cafeteria, DailyManagementProof.Fishing })
            business.RequireResolved(context, role);
    }
    private JsonObject? OwnedReward(DailyStageFrame frame)
    {
        foreach (var op in business.Records(frame.Context, includeLegacy: false).Where(op => op["role"]?.GetValue<string>() is DailyManagementProof.Role or DailyManagementProof.Helpers or DailyManagementProof.Cafeteria or DailyManagementProof.Fishing))
        {
            if (op["state"]?.GetValue<string>() != "completed" || op["presentation_closed"]?.GetValue<bool>() == true || !JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]) || !DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), frame.Frame))
                continue;
            var captured = op["presentation_frame"] as JsonObject ?? op["after"]!["Frame"]!.AsObject();
            var old = DailyNavigationDecision.Rows(captured).Where(r => DailyNavigationDecision.Text(r, "Type") == "RewardReceivePopupUI").ToArray();
            var current = DailyNavigationDecision.Rows(frame.Frame).Where(r => DailyNavigationDecision.Text(r, "Type") == "RewardReceivePopupUI").ToArray();
            if (old.Length != 1 || current.Length != 1 || !JsonNode.DeepEquals(old[0]["Id"], current[0]["Id"]) || !JsonNode.DeepEquals(captured["Scene"], frame.Frame["Scene"]))
                continue;
            if (op["confirmation"]?.GetValue<string>() != "native_claim_state")
                business.Verify(op, op["events"]!.AsArray(), op["after"]!.AsObject());
            return op;
        }
        return null;
    }
    private bool ManagementRewardVisible(JsonObject frame) =>
        DailyNavigationDecision.Rows(frame).Count(r => DailyNavigationDecision.Text(r, "Type") == "ManagementRewardPopupUI") == 1 &&
        DailyNavigationDecision.Rows(frame).Count(r => DailyNavigationDecision.Text(r, "Type") == "RewardReceivePopupUI") == 1 &&
        DailyNavigationDecision.Blockers(frame, "RewardReceivePopupUI", policy).Length == 0;

    // Accrued management income is a zero-cost claim. Retry by reading live
    // eligibility, never by replaying the old intent or assuming a fixed cooldown.
    private async Task<JsonObject> RetryFreeClaimAsync(JsonObject context, Func<Task<JsonObject>> run)
    {
        var origin = (await driver.ObserveAsync()).Frame;
        const int maxRetries = 3;
        for (int attempt = 0; ; attempt++)
        {
            Stop();
            var current = await driver.ObserveAsync();
            if (!JsonNode.DeepEquals(current.Context, context) || !DailyEvidence.SameActor(origin, current.Frame) ||
                !JsonNode.DeepEquals(origin["Scene"], current.Frame["Scene"]))
                throw new StageHostException("identity", "经营重试期间账号、场景或周期改变，已停止重试。");
            string reason;
            try
            {
                var result = await run();
                result["retries"] = attempt;
                if (result["reason"]?.GetValue<string>() != "free_claim_still_pending" || attempt == maxRetries)
                    return result;
                reason = "经营收益仍可领取，重新检查当前窗口与可领取状态";
            }
            catch (Exception error) when (error is StageHostException { Kind: "adapter" or "pending" } or
                DailyStepException { Kind: "rejected" or "pending" })
            {
                Stop();
                if (attempt == maxRetries) throw;
                reason = error.Message;
            }
            driver.Diagnostics.Event("management_claim_retry", DailyData.O(
                ("retry", attempt + 1), ("max_retries", maxRetries), ("reason", reason), ("cost", 0)));
            await delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public bool CanResume(DailyStageFrame frame)
    {
        var types = DailyNavigationDecision.Types(frame.Frame);
        if (!types.Contains("ManagementRewardPopupUI"))
            return false;
        if (types.Contains("RewardReceivePopupUI"))
            return ManagementRewardVisible(frame.Frame);
        return DailyNavigationDecision.Blockers(frame.Frame, "ManagementRewardPopupUI", policy).Length == 0;
    }
    private async Task OpenAsync(JsonObject context)
    {
        RequireResolved(context);
        var current = await driver.ObserveAsync();
        var types = DailyNavigationDecision.Types(current.Frame);
        if (types.Contains("ManagementRewardPopupUI"))
        {
            if (types.Contains("RewardReceivePopupUI"))
            {
                if (!ManagementRewardVisible(current.Frame))
                    throw new StageHostException("adapter", "经营奖励窗口被其他页面遮挡，等待可操作状态。");
                var op = OwnedReward(current);
                if (op != null) await FinishAsync(op, false);
                else
                {
                    // A visible native management reward is sufficient to resume
                    // presentation cleanup even when its network receipt was lost.
                    driver.Diagnostics.Event("management_reward_resume", DailyData.O(("receipt_required", false), ("cost", 0)));
                    await driver.DismissRewardAsync("ManagementRewardPopupUI", false);
                }
            }
            return;
        }
        await DailyManagementEntry.OpenAsync(driver, stopped);
    }
    private async Task FinishAsync(JsonObject op, bool wait)
    {
        try
        {
            var captured = await driver.ObserveAsync();
            if (DailyNavigationDecision.Types(captured.Frame).Contains("RewardReceivePopupUI"))
            {
                op["presentation_frame"] = captured.Frame.DeepClone();
                business.Save(op);
            }
            await driver.DismissRewardAsync("ManagementRewardPopupUI", wait);
            op["presentation_closed"] = true;
            op["presentation_after"] = (await driver.ObserveAsync()).Frame.DeepClone();
            business.Save(op);
        }
        catch (Exception error) { op["presentation_error"] = error.Message; business.Save(op); throw new StageHostException("adapter", "经营收益已确认，奖励展示尚未结束；不重复领取：" + error.Message); }
    }
    private async Task ReturnAsync() => await driver.SendObservedAsync(new() { ["ui"] = "ManagementRewardPopupUI", ["field"] = "_cancelButton", ["absent"] = "ManagementRewardPopupUI", ["expect"] = "MenuUI", ["reason"] = "经营收益检查完成" });
    private async Task<JsonObject> IncomeAsync(JsonObject context, bool returnMenu)
    {
        await OpenAsync(context);
        var before = await driver.EvidenceAsync(DailyManagementProof.Prefixes);
        var roles = DailyManagementProof.Eligible(before);
        var result = new JsonObject { ["state"] = "skipped", ["reason"] = "no_accrued_rewards", ["actions"] = 0 };
        if (roles.Length > 0)
        {
            if (DailyEvidence.Reading(before, "management.ui", "_settlementAllButtonEnableRoot.activeInHierarchy")?.GetValue<bool>() != true)
                throw new StageHostException("adapter", "经营原生一键领取尚未就绪，未提交输入。");
            var op = business.Create(context, DailyManagementProof.Role, before, new()
            {
                ["categories"] = new JsonArray(roles.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
            }, new()
            {
                ["ui"] = "ManagementRewardPopupUI",
                ["field"] = "_settlementAllButton",
                ["reason"] = "领取餐厅、钓鱼和领地已有收益"
            });
            await business.CommitAsync(op, context, 35);
            if (op["state"]?.GetValue<string>() != "completed") return op["result"]!.DeepClone().AsObject();
            await FinishAsync(op, true);
            result = new()
            {
                ["state"] = "completed",
                ["actions"] = 1,
                ["id"] = op["id"]!.DeepClone(),
                ["result"] = op["result"]!.DeepClone()
            };
        }
        if (returnMenu)
            await ReturnAsync();
        result["engine"] = "dotnet-management-v1";
        return result;
    }
    private async Task<JsonObject> HelpersAsync(JsonObject context)
    {
        await OpenAsync(context);
        var before = await driver.EvidenceAsync(["life"]);
        var result = new JsonObject { ["state"] = "skipped", ["reason"] = "no_accrued_rewards", ["actions"] = 0 };
        if (DailyEvidence.Reading(before, "life.helpers_ui", "IsDisable()")?.GetValue<bool>() == true)
            result["reason"] = "not_unlocked";
        else if (DailyManagementProof.Claimable(before, "life.helpers_ui"))
        {
            var op = business.Create(context, DailyManagementProof.Helpers, before, new()
            {
                ["accrued_only"] = true
            }, new()
            {
                ["ui"] = "ManagementRewardPopupUI",
                ["field"] = HelperField,
                ["reason"] = "领取领地助手已有收益"
            });
            await business.CommitAsync(op, context);
            if (op["state"]?.GetValue<string>() != "completed") return op["result"]!.DeepClone().AsObject();
            await FinishAsync(op, true);
            result = new()
            {
                ["state"] = "completed",
                ["id"] = op["id"]!.DeepClone(),
                ["result"] = op["result"]!.DeepClone()
            };
        }
        await ReturnAsync();
        result["engine"] = "dotnet-helpers-v1";
        return result;
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        Stop();
        var current = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(current.Context, context))
            throw new StageHostException("identity", "经营现场与队列身份不同。");
        RequireResolved(context);
        try
        {
            if (kind == "life_helpers")
                return await RetryFreeClaimAsync(context, () => HelpersAsync(context));
            if (kind == "cafeteria_income")
                return await RetryFreeClaimAsync(context, () => IncomeAsync(context, true));
            var settings = preferences(context);
            settings.Validate();
            var results = new JsonObject();
            if (settings.Stages.CafeteriaIncome)
                results["income"] = await RetryFreeClaimAsync(context, () => IncomeAsync(context, !settings.Stages.CafeteriaGuests));
            // Guest interactions share the same native driver and business journal.
            if (settings.Stages.CafeteriaGuests)
                results["guests"] = await relay("execute", "cafeteria_guests", null);
            return new()
            {
                ["state"] = results.All(r => r.Value?["state"]?.GetValue<string>() is "completed" or "skipped") ? "completed" : "partial",
                ["stages"] = results,
                ["engine"] = "dotnet-management-v1",
                ["guests_engine"] = settings.Stages.CafeteriaGuests ? "dotnet" : "disabled"
            };
        }
        catch (InvalidDataException error) { throw new StageHostException("adapter", error.Message); }
    }
}




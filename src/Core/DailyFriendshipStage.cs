using System.Diagnostics;
using System.Text.Json.Nodes;
namespace Dustweave;

public sealed class DailyFriendshipStage : IDailyManagedStage
{
    private readonly DailyCommandDriver driver; private readonly DailyManagedBusiness business; private readonly DailyStageNavigation navigation; private readonly Func<bool> stopped; private readonly Func<JsonObject, DailyPreferences> preferences; private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay;
    private readonly DailyNavigationPolicy policy = DailyNavigationPolicy.Load();
    public DailyFriendshipStage(string root, DailyCommandDriver driver, DailyManagedBusiness business, DailyStageNavigation navigation, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, Func<JsonObject, DailyPreferences>? preferences = null)
    {
        this.driver = driver;
        this.business = business;
        this.navigation = navigation;
        this.stopped = stopped;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.preferences = preferences ?? (context => new DailyPreferenceStore(root).Read(context["actor"]![3]!.GetValue<string>()));
    }
    private void Stop()
    {
        if (stopped())
            throw new StageHostException("stopped", "亲密度咨询已停止，原回执保留。");
    }
    private bool OwnsReward(JsonObject op, DailyStageFrame frame)
    {
        if (op["state"]?.GetValue<string>() != "completed" || !JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]) || !DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), frame.Frame))
            return false;
        var original = op["presentation_frame"] as JsonObject ?? op["after"]!["Frame"]!.AsObject();
        var saved = DailyNavigationDecision.Rows(original).Where(r => r["Type"]?.GetValue<string>() == "RewardReceivePopupUI").ToArray();
        var current = DailyNavigationDecision.Rows(frame.Frame).Where(r => r["Type"]?.GetValue<string>() == "RewardReceivePopupUI").ToArray();
        if (current.Length != 1 || !DailyNavigationDecision.Types(frame.Frame).Contains("FriendshipManageUI") || DailyNavigationDecision.Blockers(frame.Frame, "RewardReceivePopupUI", policy).Length != 0 || !JsonNode.DeepEquals(original["Scene"], frame.Frame["Scene"]))
            return false;
        // Older versions recorded completion before the asynchronous gift appeared.
        // An unseen popup can be recovered only after checking the current native target/quota below.
        if (saved.Length > 0 && (saved.Length != 1 || !JsonNode.DeepEquals(saved[0]["Id"], current[0]["Id"])))
            return false;
        business.Verify(op, op["events"]!.AsArray(), op["after"]!.AsObject());
        return true;
    }
    public bool CanResume(DailyStageFrame frame)
    {
        var types = DailyNavigationDecision.Types(frame.Frame);
        var records = business.Records(frame.Context, DailyFriendshipProof.Role, includeLegacy: false);
        if (types.Contains("FriendshipCounselingEnterPopupUI"))
            return records.Any(op => DailyFriendshipProof.OwnsPreview(op, frame));
        if (types.Contains("RewardReceivePopupUI"))
            return types.Contains("FriendshipManageUI") && records.Any(op => OwnsReward(op, frame));
        return types.Contains("FriendshipManageUI") && DailyNavigationDecision.Blockers(frame.Frame, "FriendshipManageUI", policy).Length == 0 || types.Contains("FriendshipUI") && DailyNavigationDecision.Blockers(frame.Frame, "FriendshipUI", policy).Length == 0;
    }
    private async Task<JsonObject> WaitSelectedAsync(long id)
    {
        double end = clock() + 25;
        while (clock() < end)
        {
            Stop();
            var evidence = await driver.EvidenceAsync(["friendship"]);
            if (DailyEvidence.Integer(DailyFriendshipProof.State(evidence)["Selected"]) == id)
                return evidence;
            await delay(TimeSpan.FromMilliseconds(250));
        }
        throw new StageHostException("adapter", "咨询角色选择没有推进，未提交消费。");
    }
    private async Task FinishAsync(JsonObject op)
    {
        try
        {
            var frame = await driver.ObserveAsync();
            if (DailyNavigationDecision.Types(frame.Frame).Contains("RewardReceivePopupUI"))
            {
                op["presentation_frame"] = frame.Frame.DeepClone();
                business.Save(op);
            }
            await driver.DismissRewardAsync(["FriendshipManageUI"], true, async () =>
            {
                // The native pending list survives asynchronous UI loading. A visible parent
                // alone is not proof that its level-up gift has finished presenting.
                var evidence = await driver.EvidenceAsync(["friendship"]);
                var state = DailyFriendshipProof.State(evidence);
                if (!MatchesResult(op, state))
                    throw new StageHostException("adapter", "咨询角色或剩余次数已变化，请返回咨询页面后继续。");
                if (state["NoticePending"] is not JsonValue pending || !pending.TryGetValue<bool>(out bool waiting))
                    throw new StageHostException("adapter", "咨询奖励状态尚未就绪，请重新连接游戏后继续。");
                var fresh = evidence["Frame"]!.AsObject();
                return !waiting && DailyNavigationDecision.Types(fresh).Contains("FriendshipManageUI") && DailyNavigationDecision.Blockers(fresh, "FriendshipManageUI", policy).Length == 0;
            });
            op["presentation_closed"] = true;
            op.Remove("presentation_error");
            business.Save(op);
        }
        catch (StageHostException e) when (e.Kind == "stopped") { throw; }
        catch (Exception error) { op["presentation_error"] = error.Message; business.Save(op); throw new StageHostException("adapter", "咨询已确认，奖励展示尚未结束；不重复咨询：" + error.Message); }
    }
    private static bool MatchesResult(JsonObject op, JsonObject state) =>
        state["Available"]?.GetValue<bool>() == true &&
        DailyEvidence.Integer(state["Selected"]) == DailyEvidence.Integer(op["result"]?["friendship"]) &&
        DailyEvidence.Integer(state["Free"]) == DailyEvidence.Integer(op["result"]?["free_remaining"]);
    private async Task LeaveAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        Stop();
        var recovered = await navigation.RecoverAsync("friendship", context, "本轮免费咨询结束", relay);
        if (recovered["safe"]?.GetValue<bool>() != true)
            throw new StageHostException("adapter", "咨询已核对，返回主菜单尚未确认；原回执保留。");
    }
    private async Task ConfirmAsync(JsonObject op, JsonObject context)
    {
        var current = await driver.ObserveAsync();
        if (!DailyFriendshipProof.OwnsPreview(op, current))
            throw new StageHostException("pending", "原快速咨询弹窗不属于当前现场，未确认。");
        var before = await driver.EvidenceAsync(["friendship"]);
        if (!JsonNode.DeepEquals(DailyFriendshipProof.State(before), DailyFriendshipProof.State(op["before"]!.AsObject())))
            throw new StageHostException("pending", "快速咨询预览期间免费次数或角色状态变化，未确认。");
        op["before"] = before.DeepClone();
        business.Save(op);
        await business.CommitAsync(op, context);
        await FinishAsync(op);
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        Stop();
        var initial = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(initial.Context, context))
            throw new StageHostException("identity", "亲密度现场与队列身份不同。");
        var settings = preferences(context);
        settings.Validate();
        if (!settings.Friendship.Enabled)
            return new()
            {
                ["state"] = "skipped",
                ["reason"] = "disabled",
                ["engine"] = "dotnet-friendship-v1"
            };
        var pending = business.Records(context, DailyFriendshipProof.Role).Where(DailyManagedBusiness.Pending).ToArray();
        var preview = pending.Length == 1 && DailyFriendshipProof.OwnsPreview(pending[0], initial) ? pending[0] : null;
        if (pending.Length > 0 && preview == null)
            throw new StageHostException("pending", "原咨询尚未核对，不提交导航或新咨询。");
        var completed = new JsonArray();
        try
        {
            if (preview != null)
            {
                await ConfirmAsync(preview, context);
                completed.Add(preview["result"]!.DeepClone());
            }
            else if (DailyNavigationDecision.Types(initial.Frame).Contains("RewardReceivePopupUI"))
            {
                var evidence = await driver.EvidenceAsync(["friendship"]);
                var live = new DailyStageFrame(evidence["Frame"]!.AsObject(), context);
                var op = business.Records(context, DailyFriendshipProof.Role, includeLegacy: false).FirstOrDefault(op => OwnsReward(op, live) && MatchesResult(op, DailyFriendshipProof.State(evidence))) ?? throw new StageHostException("adapter", "当前礼物无法对应到本次咨询，请领取礼物后继续；不会重复咨询。");
                await FinishAsync(op);
            }
            else if (DailyNavigationDecision.Types(initial.Frame).Contains("FriendshipManageUI"))
            {
                var evidence = await driver.EvidenceAsync(["friendship"]);
                var state = DailyFriendshipProof.State(evidence);
                var op = business.Records(context, DailyFriendshipProof.Role, includeLegacy: false).FirstOrDefault(o =>
                    o["state"]?.GetValue<string>() == "completed" && MatchesResult(o, state) &&
                    (o["presentation_closed"]?.GetValue<bool>() != true || state["NoticePending"]?.GetValue<bool>() == true));
                if (op != null)
                {
                    business.Verify(op, op["events"]!.AsArray(), op["after"]!.AsObject());
                    await FinishAsync(op);
                }
                else if (state["NoticePending"]?.GetValue<bool>() == true)
                    throw new StageHostException("adapter", "游戏仍在准备咨询礼物，请领取礼物后继续；未提交新咨询。");
            }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Stop();
                business.RequireResolved(context, DailyFriendshipProof.Role);
                var current = await driver.EvidenceAsync(["friendship"]);
                var decision = DailyFriendshipProof.Choose(DailyFriendshipProof.State(current), settings.Friendship.Quick);
                if (decision["state"]!.GetValue<string>() != "ready")
                {
                    await LeaveAsync(context, relay);
                    return new()
                    {
                        ["state"] = completed.Count > 0 && decision["state"]!.GetValue<string>() == "skipped" ? "completed" : decision["state"]!.DeepClone(),
                        ["reason"] = decision["reason"]!.DeepClone(),
                        ["completed"] = completed,
                        ["engine"] = "dotnet-friendship-v1"
                    };
                }
                var target = decision["target"]!.AsObject();
                long id = DailyEvidence.Integer(target["Id"]);
                var frame = (await driver.ObserveAsync()).Frame;
                string ui = new[] { "FriendshipManageUI", "FriendshipUI", "MenuUI" }.FirstOrDefault(name => DailyNavigationDecision.Types(frame).Contains(name)) ?? throw new StageHostException("adapter", "没有已观察的咨询入口。");
                await driver.SendObservedAsync(new()
                {
                    ["ui"] = ui,
                    ["operation"] = "friendship_select",
                    ["value"] = checked((int)id),
                    ["expect"] = "FriendshipManageUI",
                    ["reason"] = "选择好感度未满的免费咨询角色"
                });
                var before = await WaitSelectedAsync(id);
                var refreshed = DailyFriendshipProof.State(before);
                if (DailyFriendshipProof.Choose(refreshed)["state"]?.GetValue<string>() != "ready" || DailyEvidence.Integer(refreshed["Selected"]) != id || !DailyFriendshipProof.Candidates(refreshed).TryGetValue(id, out var selected) || selected["Max"]!.GetValue<bool>() || DailyEvidence.Integer(selected["Remaining"]) <= 0)
                    throw new StageHostException("adapter", "咨询资格在选择后变化，未提交消费。");
                bool quick = settings.Friendship.Quick && selected["Quick"]!.GetValue<bool>();
                var action = quick ? new JsonObject { ["ui"] = "FriendshipCounselingEnterPopupUI", ["field"] = "_okButton", ["absent"] = "FriendshipCounselingEnterPopupUI", ["reason"] = "每日免费快速咨询" } : new JsonObject { ["ui"] = "FriendshipManageUI", ["operation"] = "friendship_complete", ["value"] = checked((int)id), ["reason"] = "每日免费咨询，使用游戏原生正确答复" };
                var op = business.Create(context, DailyFriendshipProof.Role, before, new()
                {
                    ["friendship"] = id,
                    ["free"] = refreshed["Free"]!.DeepClone(),
                    ["quick"] = quick
                }, action);
                if (quick)
                {
                    await business.PreviewAsync(op, context, new()
                    {
                        ["ui"] = "FriendshipManageUI",
                        ["field"] = "_quickCounselingButton",
                        ["expect"] = "FriendshipCounselingEnterPopupUI",
                        ["reason"] = "预览免费快速咨询"
                    });
                    await ConfirmAsync(op, context);
                }
                else
                {
                    await business.CommitAsync(op, context);
                    await FinishAsync(op);
                }
                completed.Add(op["result"]!.DeepClone());
            }
            await LeaveAsync(context, relay);
            return new()
            {
                ["state"] = "completed",
                ["completed"] = completed,
                ["engine"] = "dotnet-friendship-v1"
            };
        }
        catch (InvalidDataException error) { throw new StageHostException("adapter", error.Message); }
    }
}


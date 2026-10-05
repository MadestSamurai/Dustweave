using System.Text.Json.Nodes;
namespace BD2Daily;

public static class DailyHomeProof
{
    public static bool SameGameAccount(JsonObject a, JsonObject b) => new[] { "ProcessId", "ProcessStartTicks", "AccountKey", "PlayerKey" }.All(k => a[k] != null && b[k] != null && JsonNode.DeepEquals(a[k], b[k]));
    public static JsonObject? ResetRejection(JsonObject before, JsonArray events)
    {
        var selected = events.Select(n => n!.AsObject()).Where(e => e["Role"]?.GetValue<string>() is "missions.clear" or "pass.reward").ToArray();
        if (selected.Length == 0 || before["Frame"] is not JsonObject frame || selected.Any(e => e["Frame"] is not JsonObject f || !DailyEvidence.SameActor(frame, f)))
            return null;
        var rejected = new JsonArray();
        var accepted = new JsonArray();
        foreach (string role in new[] { "missions.clear", "pass.reward" })
        {
            var rows = selected.Where(e => e["Role"]!.GetValue<string>() == role).OrderBy(e => DailyEvidence.Integer(e["Sequence"])).ToArray();
            var req = rows.Where(e => e["Kind"]?.GetValue<string>() == "request").ToArray();
            var res = rows.Where(e => e["Kind"]?.GetValue<string>() == "response").ToArray();
            if (rows.Length != req.Length + res.Length || req.Length != res.Length || rows.Select(e => DailyEvidence.Integer(e["Sequence"])).Distinct().Count() != rows.Length)
                return null;
            for (int i = 0; i < res.Length; i++)
            {
                var reply = res[i];
                if (DailyEvidence.Integer(req[i]["Sequence"]) >= DailyEvidence.Integer(reply["Sequence"]) || reply["Error"]?.GetValue<string>() != "")
                    return null;
                long code = DailyEvidence.Integer(reply["ErrorCode"]);
                bool ok = reply["Accepted"]?.GetValue<bool>() ?? false;
                var values = DailyEvidence.Values(reply);
                bool reward = values["RewardInfoBundle"] switch
                {
                    null => false,
                    JsonArray a => a.Count > 0,
                    JsonObject o => o.Count > 0,
                    _ => true
                };
                if (code == 3 && role == "missions.clear" && !ok && !reward)
                    rejected.Add(reply["Sequence"]!.DeepClone());
                else if (code == 0 && ok)
                    accepted.Add(reply["Sequence"]!.DeepClone());
                else
                    return null;
            }
        }
        if (rejected.Count != 1)
            return null;
        return new()
        {
            ["error_code"] = 112003,
            ["rejected_sequences"] = rejected,
            ["accepted_sequences"] = accepted,
            ["outcome"] = accepted.Count > 0 ? "partial_then_rejected" : "rejected_no_reward",
            ["replay"] = false
        };
    }
}
public sealed partial class DailyCommandDriver
{
    public async Task<JsonObject> HomeRecoveryAsync(string kind)
    {
        if (stopped() || mailbox.Read("live", "pause") != null)
            throw new StageHostException("stopped", "启动恢复已停止。");
        var observed = await ReadBound();
        var plan = DailyHomeDecision.Inspect(observed.Frame, policy);
        if (plan.Kind != "recovery" || plan.Reason != kind)
            throw new StageHostException("navigation_changed", "启动恢复现场已改变，未提交输入。");
        string id = Guid.NewGuid().ToString("N"), path = Path.Combine(root, "live", "home-recovery", id + ".json");
        var record = new JsonObject { ["id"] = id, ["engine"] = "dotnet-home-v1", ["kind"] = kind, ["state"] = "prepared", ["context"] = observed.Context.DeepClone(), ["before"] = observed.Frame.DeepClone(), ["business_replayed"] = false };
        DailyJson.Write(path, record);
        try
        {
            JsonObject result = kind switch
            {
                "mission_reset" => await DismissOwnedResetAsync(observed.Frame),
                "reward" => await DismissStartupRewardAsync(observed.Frame),
                "quiz" => await RecoverStartupQuizAsync(),
                _ => throw new StageHostException("protocol", "未知启动恢复分支。")
            };
            record["state"] = "closed";
            record["result"] = result.DeepClone();
            record["after"] = (await ReadBound()).Frame.DeepClone();
            DailyJson.Write(path, record);
            return result;
        }
        catch (Exception error)
        {
            record["state"] = "blocked";
            record["error"] = error.Message;
            if (error is DailyStepException step)
            {
                record["state"] = step.Submitted ? "unknown" : "blocked";
                record["command"] = step.Command?.DeepClone();
            }
            if (error is StageHostException fault && fault.Kind == "pending")
                record["state"] = "unknown";
            DailyJson.Write(path, record);
            if (error is DailyStepException e)
                throw new StageHostException(e.Kind == "rejected" ? "navigation_changed" : e.Kind, e.Message);
            if (error is InvalidDataException data)
                throw new StageHostException("adapter", data.Message);
            throw;
        }
    }
    private async Task<JsonObject> DismissOwnedResetAsync(JsonObject frame)
    {
        if (!DailyHomeDecision.ResetPopup(frame, policy))
            throw new StageHostException("navigation_changed", "任务重置提示已变化，未确认。");
        var candidates = new List<(string Path, JsonObject Op, JsonObject Proof)>();
        var folders = new[] { Path.Combine(root, "live", "business"), Path.Combine(root, "live", "managed-business") };
        foreach (string path in folders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.json")))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new StageHostException("adapter", "原业务记录无法读取，未关闭任务重置提示。");
            if (Text(op, "role") is not ("pass.claim_all" or "missions.clear" or "event_rewards.1") || Text(op, "state") is not ("dispatching" or "unknown" or "server_rejected") || op["recovery_cleanup"]?["popup_closed"]?.GetValue<bool>() == true)
                continue;
            if (op["before"] is not JsonObject before || before["Frame"] is not JsonObject old || !DailyHomeProof.SameGameAccount(old, frame))
                continue;
            if (op["events"] is not JsonArray events)
                continue;
            var proof = DailyHomeProof.ResetRejection(before, events);
            if (proof != null)
                candidates.Add((path, op, proof));
        }
        if (candidates.Count == 0)
            throw new StageHostException("adapter", "112003提示没有同游戏账号的完整拒绝回执，未自动确认。");
        var selected = candidates.OrderByDescending(c => DailyEvidence.Integer(c.Op["at"])).First();
        // Native callback is journaled before dispatch; an unknown outcome never marks the business as recovered.
        var done = await SendObservedAsync(new()
        {
            ["ui"] = "MessagePopupUI",
            ["field"] = "_buttonOK",
            ["absent"] = "MessagePopupUI",
            ["reason"] = "home-reset-proof|112003|Acknowledge owned mission reset rejection"
        });
        selected.Op["state"] = "server_rejected";
        selected.Op["rejection"] = selected.Proof.DeepClone();
        selected.Op["recovery_cleanup"] = new JsonObject { ["at"] = now(), ["popup_closed"] = true, ["engine"] = "dotnet-home-v1", ["command_id"] = done["id"]!.DeepClone() };
        DailyJson.Write(selected.Path, selected.Op);
        return new()
        {
            ["state"] = "closed",
            ["operation"] = selected.Op["id"]?.DeepClone(),
            ["command_id"] = done["id"]!.DeepClone(),
            ["business_replayed"] = false
        };
    }
    private async Task<JsonObject> DismissStartupRewardAsync(JsonObject bound)
    {
        double end = clock() + 40;
        while (clock() < end)
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "奖励展示收尾已停止。");
            var frame = (await ReadBound()).Frame;
            if (await RecoverPresentationAsync(frame, "RewardReceivePopupUI"))
                continue;
            var rows = DailyNavigationDecision.Rows(frame).Where(r => Text(r, "Type") == "RewardReceivePopupUI").ToArray();
            if (rows.Length == 0)
                return new()
                {
                    ["state"] = "already_closed",
                    ["business_replayed"] = false
                };
            if (rows.Length != 1 || DailyNavigationDecision.Blockers(frame, "RewardReceivePopupUI", policy).Length > 0)
                throw new StageHostException("adapter", "奖励展示被未知弹窗遮挡，保留现场。");
            if (Text(frame, "Scene") != Text(bound, "Scene"))
                throw new StageHostException("identity", "奖励展示期间场景改变，未确认原业务结果。");
            var evidence = await EvidenceAsync(["reward.presentation"]);
            bool ready = DailyNavigationDecision.ReadyInput(rows[0], false) && new[] { "ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ", "ὮὬὧὦὣὠὠὤὮὦὧ" }.All(p => DailyEvidence.Reading(evidence, "reward.presentation", p)?.GetValue<bool>() == true) && DailyEvidence.Reading(evidence, "reward.presentation", "ὦὡὮὫὧὠὮὡὭὭὬ")?.GetValue<bool>() == false;
            if (ready)
            {
                var done = await SendObservedAsync(new()
                {
                    ["ui"] = "RewardReceivePopupUI",
                    ["back"] = true,
                    ["absent"] = "RewardReceivePopupUI",
                    ["reason"] = "Close existing reward after native animation; no business replay"
                });
                return new()
                {
                    ["state"] = "closed",
                    ["command_id"] = done["id"]!.DeepClone(),
                    ["business_replayed"] = false
                };
            }
            await delay(TimeSpan.FromMilliseconds(100));
        }
        throw new StageHostException("adapter", "奖励动画未就绪，保留原业务回执；未重新领取。");
    }
}

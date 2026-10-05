using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Resume an already owned quiz only; startup never opens or starts another question.</summary>
public static class DailyQuizRecovery
{
    private static int Int(JsonObject o, string name) => checked((int)DailyEvidence.Integer(o[name]));
    private static bool Flag(JsonObject o, string name) => o[name]?.GetValue<bool>() ?? false;
    public static JsonObject Page(JsonObject state) => DailyEvidence.Reading(state, "minigames.quiz", "$self")?.AsObject() ?? throw new InvalidDataException("Quiz observation unavailable");
    public static int AnswerId(JsonObject page)
    {
        var rows = page["Talks"]!.AsArray().Select(n => JsonNode.Parse(n!.GetValue<string>())!.AsObject()).ToArray();
        int cursor = Int(page, "Cursor");
        if (!Flag(page, "Choosing") || cursor <= 0 || cursor > rows.Length || rows.Length > 10000)
            throw new InvalidDataException("Quiz choice is not ready");
        var nodes = new Dictionary<int, JsonObject>();
        foreach (var row in rows)
            if (!nodes.TryAdd(Int(row, "id"), row))
                throw new InvalidDataException("Duplicate dialogue identity");
        var selects = new Dictionary<int, JsonObject>();
        foreach (var node in page["Selects"]!.AsArray())
        {
            var row = JsonNode.Parse(node!.GetValue<string>())!.AsObject();
            if (!selects.TryAdd(Int(row, "id"), row))
                throw new InvalidDataException("Duplicate choice identity");
        }
        var edges = new Dictionary<int, int?[]>();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int id = Int(row, "id"), type = Int(row, "bubbleType"), sid = row["selectDialogId"] == null ? 0 : Int(row, "selectDialogId");
            if (type == 4)
            {
                if (sid == 0)
                    continue;
                if (!selects.TryGetValue(sid, out var choice))
                    throw new InvalidDataException("Missing choice table");
                edges[id] = choice["dialogTextId"]!.AsArray().Select(n => (int?)checked((int)DailyEvidence.Integer(n) + 1)).ToArray();
            }
            else
            {
                int ret = row["returnId"] == null ? 0 : Int(row, "returnId");
                edges[id] = ret == -1 ? [null] : ret > 0 ? [ret] : i + 1 < rows.Length ? [Int(rows[i + 1], "id")] : [null];
            }
            if (edges[id].Any(n => n.HasValue && !nodes.ContainsKey(n.Value)))
                throw new InvalidDataException("Missing dialogue branch target");
        }
        var current = rows[cursor - 1];
        int currentSid = current["selectDialogId"] == null ? 0 : Int(current, "selectDialogId");
        if (Int(current, "bubbleType") != 4 || !selects.TryGetValue(currentSid, out var select))
            throw new InvalidDataException("Dialogue is not a choice");
        var options = select["dialogTextId"]!.AsArray().Select(n => checked((int)DailyEvidence.Integer(n))).ToArray();
        var displayed = page["Displayed"]!.AsArray().Select(n => checked((int)DailyEvidence.Integer(n))).ToArray();
        if (options.Distinct().Count() != options.Length || !options.Order().SequenceEqual(displayed.Order()))
            throw new InvalidDataException("Displayed choices differ from current table");
        var valid = new List<int>();
        foreach (int option in options)
        {
            var pending = new Queue<int?>();
            pending.Enqueue(checked(option + 1));
            var seen = new HashSet<int> { Int(current, "id") };
            while (pending.TryDequeue(out var node))
            {
                if (node == null)
                {
                    valid.Add(option);
                    break;
                }
                if (!seen.Add(node.Value))
                    continue;
                if (!edges.TryGetValue(node.Value, out var next))
                    throw new InvalidDataException("Unsupported dialogue branch");
                foreach (var edge in next)
                    pending.Enqueue(edge);
            }
        }
        if (valid.Count != 1)
            throw new InvalidDataException("Quiz has no unique success branch");
        return valid[0];
    }
    public static bool ContinuationAllowed(JsonObject op, JsonObject state, JsonArray events)
    {
        if (op["role"]?.GetValue<string>() != "minigames.quiz_clear" || string.IsNullOrEmpty(op["command_id"]?.GetValue<string>()) || op["before"]?["Frame"] is not JsonObject old || state["Frame"] is not JsonObject frame || !DailyHomeProof.SameGameAccount(old, frame))
            return false;
        var page = Page(state);
        if (!Flag(page, "Playing") || !Flag(page, "Choosing") && !Flag(page, "Touch") || op["scope"] is not JsonObject scope)
            return false;
        if (!new[] { ("EventUid", "event"), ("Group", "group"), ("QuizId", "quiz") }.All(p => JsonNode.DeepEquals(page[p.Item1], scope[p.Item2])))
            return false;
        if (page["Quizzes"]!.AsArray().Any(n => JsonNode.DeepEquals(n!["Group"], page["Group"]) && JsonNode.DeepEquals(n["Id"], page["QuizId"]) && n["Complete"]?.GetValue<bool>() == true))
            return false;
        return !(op["events"]?.AsArray() ?? []).Concat(events).Any(n => n?["Role"]?.GetValue<string>() == "minigames.quiz_clear" && n["Kind"]?.GetValue<string>() is "request" or "response");
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, JsonArray key)
    {
        var b = Page(before);
        var a = Page(after);
        if (!JsonNode.DeepEquals(b["EventUid"], key[0]) || !JsonNode.DeepEquals(a["EventUid"], key[0]) || !JsonNode.DeepEquals(before["Config"], after["Config"]))
            throw new InvalidDataException("Quiz event or evidence configuration changed");
        var selected = events.Select(n => n!.AsObject()).Where(e => e["Role"]?.GetValue<string>() == "minigames.quiz_clear").ToArray();
        var req = selected.Where(e => e["Kind"]?.GetValue<string>() == "request").ToArray();
        var res = selected.Where(e => e["Kind"]?.GetValue<string>() == "response").ToArray();
        if (req.Length != 1 || res.Length != 1 || selected.Length != 2 || DailyEvidence.Integer(req[0]["Sequence"]) >= DailyEvidence.Integer(res[0]["Sequence"]) || selected.Any(e => !DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())) || !DailyEvidence.SameActor(after["Frame"]!.AsObject(), before["Frame"]!.AsObject()))
            throw new InvalidDataException("Quiz completion response identity or ordering mismatch");
        if (res[0]["Error"]?.GetValue<string>() != "" || DailyEvidence.Integer(res[0]["ErrorCode"]) != 0 || res[0]["Accepted"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Quiz completion response failed");
        var values = DailyEvidence.Values(res[0]);
        var info = values["ClearInfo"]?.AsObject() ?? throw new InvalidDataException("Quiz completion proof missing");
        if (!new[] { "eventUid", "groupId", "id" }.Select(k => DailyEvidence.Integer(info[k])).SequenceEqual(key.Select(DailyEvidence.Integer)))
            throw new InvalidDataException("Different quiz was completed");
        var found = a["Quizzes"]!.AsArray().Where(n => JsonNode.DeepEquals(n!["Group"], key[1]) && JsonNode.DeepEquals(n["Id"], key[2])).ToArray();
        if (found.Length != 1 || found[0]!["Complete"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Quiz completion cache not settled");
        return new()
        {
            ["event"] = key[0]!.DeepClone(),
            ["group"] = key[1]!.DeepClone(),
            ["quiz"] = key[2]!.DeepClone(),
            ["rewards"] = values["RewardInfoBundle"]?.DeepClone()
        };
    }
    public static string Progress(JsonObject page) => new JsonObject(new[] { "Playing", "Cursor", "Choosing", "Touch", "Displayed" }.Select(k => new KeyValuePair<string, JsonNode?>(k, page[k]?.DeepClone()))).ToJsonString();
}
public sealed partial class DailyCommandDriver
{
    private async Task<JsonObject> RecoverStartupQuizAsync()
    {
        var state = await EvidenceAsync(["minigames"]);
        var page = DailyQuizRecovery.Page(state);
        var frame = (await ReadBound()).Frame;
        // Native entry/exit animation may temporarily expose a balloon with no legal input. Observe only until it settles.
        double settleEnd = clock() + 20;
        while (page["Playing"]?.GetValue<bool>() == true && page["Choosing"]?.GetValue<bool>() != true && page["Touch"]?.GetValue<bool>() != true
            || page["Playing"]?.GetValue<bool>() != true && DailyNavigationDecision.Types(frame).Contains("BalloonScriptUI") && DailyNavigationDecision.StoryAction(frame, policy) == null)
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "等待问答对话就绪时已停止。");
            if (clock() >= settleEnd)
                throw new StageHostException("adapter", "问答/对话原生入口或退出动画未就绪，未确认、跳过或重新开题。");
            await delay(TimeSpan.FromMilliseconds(200));
            frame = (await ReadBound()).Frame;
            state = await EvidenceAsync(["minigames"]);
            page = DailyQuizRecovery.Page(state);
        }
        if (!(page["Playing"]?.GetValue<bool>() ?? false))
        {
            if (DailyNavigationDecision.Types(frame).Contains("BalloonScriptUI"))
            {
                var story = DailyNavigationDecision.StoryAction(frame, policy);
                if (story == null)
                    throw new StageHostException("adapter", "对话不是可核对的活动问答或已识别剧情，保留现场。");
                var advanced = await NavigationAsync(story);
                return new()
                {
                    ["state"] = "field_story_advanced",
                    ["command_id"] = advanced["id"]!.DeepClone(),
                    ["started_new_quiz"] = false
                };
            }
            string ui = DailyNavigationDecision.Types(frame).Contains("MiniEventQuizUI") ? "MiniEventQuizUI" : "MiniEventMainUI";
            var row = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => Text(r, "Type") == ui);
            if (row == null || DailyNavigationDecision.Blockers(frame, ui, policy).Length > 0)
                throw new StageHostException("adapter", "问答菜单被未知弹窗遮挡，保留现场。");
            JsonObject action = ui == "MiniEventQuizUI" ? new()
            {
                ["ui"] = ui,
                ["back"] = true,
                ["absent"] = ui,
                ["reason"] = "Close idle quiz list without starting another quiz"
            } : new()
            {
                ["ui"] = ui,
                ["operation"] = "quiz_leave",
                ["absent"] = ui,
                ["reason"] = "Leave idle mini event before daily queue"
            };
            var done = await SendObservedAsync(action);
            return new()
            {
                ["state"] = "closed_idle_menu",
                ["command_id"] = done["id"]!.DeepClone(),
                ["started_new_quiz"] = false
            };
        }
        var settings = new DailyPreferenceStore(root).Read(Text(frame, "AccountKey"));
        if (!settings.Events.Enabled || !settings.Events.Quiz)
            throw new StageHostException("adapter", "本账号未启用活动问答，保留进行中的问题。");
        var candidates = new List<(string Path, JsonObject Op)>();
        string folder = Path.Combine(root, "live", "business");
        foreach (string path in new[] { folder, Path.Combine(root, "live", "managed-business") }.Where(Directory.Exists).SelectMany(p => Directory.EnumerateFiles(p, "*.json")))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new StageHostException("adapter", "原问答业务记录不可读，未提交答案。");
            if (Text(op, "role") != "minigames.quiz_clear" || Text(op, "state") is not ("dispatching" or "unknown" or "rejected"))
                continue;
            var events = CollectEvents("minigames.quiz_clear", DailyEvidence.Integer(op["at"]));
            if (DailyQuizRecovery.ContinuationAllowed(op, state, events))
                candidates.Add((path, op));
        }
        if (candidates.Count != 1)
            throw new StageHostException("adapter", "进行中的问答没有唯一原操作或已发送完成请求，未重新进入或作答。");
        var selected = candidates[0];
        var key = new JsonArray(page["EventUid"]!.DeepClone(), page["Group"]!.DeepClone(), page["QuizId"]!.DeepClone());
        var window = selected.Op["continuation"]?.AsObject() ?? selected.Op;
        var before = window["before"]!.AsObject();
        long started = DailyEvidence.Integer(window["at"]);
        if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), frame) || !JsonNode.DeepEquals(before["Config"], state["Config"]))
        {
            if (selected.Op["continuation"] != null)
            {
                var history = selected.Op["previous_continuations"] as JsonArray ?? new();
                history.Add(selected.Op["continuation"]!.DeepClone());
                selected.Op["previous_continuations"] = history;
            }
            selected.Op["continuation"] = new JsonObject { ["before"] = state.DeepClone(), ["at"] = now(), ["reason"] = "same_game_active_quiz_after_observer_update" };
            before = state;
            started = now();
        }
        var recovery = selected.Op["recovery"] as JsonArray ?? new();
        recovery.Add(new JsonObject { ["previous_state"] = selected.Op["state"]?.DeepClone(), ["previous_error"] = selected.Op["error"]?.DeepClone(), ["at"] = now(), ["engine"] = "dotnet-home-v1" });
        selected.Op["recovery"] = recovery;
        selected.Op["state"] = "dispatching";
        DailyJson.Write(selected.Path, selected.Op);
        string? lastSent = null;
        if (selected.Op["continuation_input"] != null)
        {
            var savedInput = selected.Op["continuation_input"] as JsonObject ?? throw new StageHostException("adapter", "原问答输入检查点无法读取，未重答。");
            if (Text(savedInput, "state") is not ("prepared" or "observed" or "unknown" or "never_dispatched") || savedInput["frame"] is not JsonObject inputFrame || string.IsNullOrEmpty(Text(savedInput, "signature")))
                throw new StageHostException("adapter", "原问答输入检查点不完整，未重答。");
            if (Text(savedInput, "state") != "never_dispatched" && DailyHomeProof.SameGameAccount(inputFrame, frame))
                lastSent = Text(savedInput, "signature");
        }
        int count = 0;
        string? previous = null;
        double end = clock() + 180, progressAt = clock();
        JsonArray observedEvents = new();
        try
        {
            while (clock() < end)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new StageHostException("stopped", "问答接续已停止，原操作已保留。");
                frame = (await ReadBound()).Frame;
                if (await RecoverPresentationAsync(frame, "BalloonScriptUI"))
                    continue;
                state = await EvidenceAsync(["minigames"]);
                page = DailyQuizRecovery.Page(state);
                if (!JsonNode.DeepEquals(page["EventUid"], key[0]))
                    throw new StageHostException("identity", "活动问答身份改变，未继续作答。");
                observedEvents = CollectEvents("minigames.quiz_clear", started);
                var complete = page["Quizzes"]!.AsArray().Any(n => JsonNode.DeepEquals(n!["Group"], key[1]) && JsonNode.DeepEquals(n["Id"], key[2]) && n["Complete"]?.GetValue<bool>() == true);
                if (complete)
                {
                    JsonObject? result = null;
                    string proofError = "";
                    try
                    {
                        result = DailyQuizRecovery.Verify(before, observedEvents, state, key);
                    }
                    catch (InvalidDataException error) { proofError = error.Message; }
                    if (result != null)
                    {
                        selected.Op["state"] = "completed";
                        selected.Op["events"] = observedEvents.DeepClone();
                        selected.Op["after"] = state.DeepClone();
                        selected.Op["result"] = result;
                        selected.Op.Remove("error");
                        DailyJson.Write(selected.Path, selected.Op);
                        await DismissRewardAsync("MiniEventQuizUI", true);
                        return new()
                        {
                            ["state"] = "continued_owned_quiz",
                            ["operation"] = selected.Op["id"]?.DeepClone(),
                            ["actions"] = count,
                            ["started_new_quiz"] = false
                        };
                    }
                    if (clock() - progressAt >= 45)
                        throw new StageHostException("pending", "问答完成回执不完整，未重答：" + proofError);
                    await delay(TimeSpan.FromMilliseconds(200));
                    continue;
                }
                if (page["Playing"]?.GetValue<bool>() != true || !JsonNode.DeepEquals(page["Group"], key[1]) || !JsonNode.DeepEquals(page["QuizId"], key[2]))
                    throw new StageHostException("pending", "原问答对话离开或改变但完成未确认，保留原操作。");
                string signature = DailyQuizRecovery.Progress(page);
                if (previous != signature)
                {
                    previous = signature;
                    progressAt = clock();
                }
                var ui = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => Text(r, "Type") == "BalloonScriptUI");
                bool sentCompletion = observedEvents.Any(n => n?["Kind"]?.GetValue<string>() is "request" or "response");
                if (!sentCompletion && ui != null && DailyNavigationDecision.ReadyInput(ui, false) && DailyNavigationDecision.Blockers(frame, "BalloonScriptUI", policy).Length == 0 && lastSent != signature && (page["Choosing"]?.GetValue<bool>() == true || page["Touch"]?.GetValue<bool>() == true))
                {
                    if (count >= 80)
                        throw new StageHostException("adapter", "原问答接续超过80步，保留现场。");
                    int value = page["Choosing"]?.GetValue<bool>() == true ? DailyQuizRecovery.AnswerId(page) : 0;
                    var input = new JsonObject { ["state"] = "prepared", ["signature"] = signature, ["frame"] = frame.DeepClone(), ["at"] = now(), ["value"] = value };
                    selected.Op["continuation_input"] = input;
                    DailyJson.Write(selected.Path, selected.Op);
                    try
                    {
                        var sent = await SendObservedAsync(new()
                        {
                            ["ui"] = "BalloonScriptUI",
                            ["operation"] = "quiz_talk",
                            ["items"] = key.DeepClone(),
                            ["value"] = value,
                            ["reason"] = "Continue original owned quiz; never restart entry"
                        });
                        input["state"] = "observed";
                        input["command_id"] = sent["id"]!.DeepClone();
                        DailyJson.Write(selected.Path, selected.Op);
                        lastSent = signature;
                        count++;
                    }
                    catch (DailyStepException error) { input["state"] = error.Kind == "rejected" ? "never_dispatched" : "unknown"; input["command"] = error.Command?.DeepClone(); DailyJson.Write(selected.Path, selected.Op); throw; }
                    catch (Exception) { input["state"] = "unknown"; DailyJson.Write(selected.Path, selected.Op); throw; }
                }
                DailyJson.Write(Path.Combine(root, "live", "quiz-progress.json"), new
                {
                    state = "running",
                    quiz = key,
                    actions = count,
                    cursor = page["Cursor"],
                    at = now(),
                    engine = "dotnet-home-v1"
                });
                if (clock() - progressAt >= 45)
                    throw new StageHostException("adapter", "原问答45秒没有进展，未重答或重开。");
                await delay(TimeSpan.FromMilliseconds(350));
            }
            throw new StageHostException("adapter", "原问答接续超时，未重开问题。");
        }
        catch (Exception error)
        {
            // Preserve the entry and continuation window. A confirmed completion is never reverted by cleanup failure.
            if (Text(selected.Op, "state") != "completed")
            {
                selected.Op["state"] = "unknown";
                selected.Op["error"] = error.Message;
                selected.Op["events"] = observedEvents.DeepClone();
                DailyJson.Write(selected.Path, selected.Op);
            }
            throw;
        }
    }
}

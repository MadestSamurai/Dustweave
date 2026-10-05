using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using BD2.LocalIpc;
using static BD2Daily.DailyData;
namespace BD2Daily;

/// <summary>The daily queue borrows maintained mini-game components with bounded, expiring ownership.</summary>
public sealed class DailyMiniGameChannel
{
    private readonly PipeClient pipe;
    public DailyMiniGameChannel(string name, JsonArray actor)
    {
        pipe = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name), I(actor[0]), N(actor[1]));
    }
    public void Acquire()
    {
        string fingerprint = pipe.Fingerprint() ?? throw new IOException("小游戏连接组件未就绪");
        pipe.Open(fingerprint);
    }
    public JsonObject Read(string name) => pipe.Read(name) is byte[] bytes ? JsonNode.Parse(bytes)!.AsObject() : new();
    public void Write(string name, JsonNode value) => pipe.Write(name, Encoding.UTF8.GetBytes(value.ToJsonString()));
    public void Text(string name, string value) => pipe.Write(name, Encoding.UTF8.GetBytes(value));
    public static async Task Connect(DailyWorkflow w, string command)
    {
        var start = DailyTools.StartInfo(w.Directory, "minigame");
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new IOException("小游戏连接进程未启动");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            int code = await DailyHelperLifetime.WaitAsync(process, async () => { await w.Observe(); }, TimeSpan.FromSeconds(60));
            string normal = await output, failure = await error;
            Require(code == 0, "小游戏连接失败：" + (failure.Length > 0 ? failure : normal));
        }
        finally
        {
            // Output readers are drained after our helper has exited, even on stop.
            try
            {
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException) { }
        }
        await w.Observe();
    }
}
public static class DailyWeeklyFishing
{
    public static bool Active(JsonObject c, long now) => B(c["Enabled"]) && S(c["OwnerId"]).Length > 0 && N(c["UntilUtcTicks"]) > now && N(c["UntilUtcTicks"]) <= now + 150_000_000;
    public static bool Fresh(JsonObject s, int pid, long now) => N(s["ProcessId"]) == pid && now - N(s["CapturedUtcTicks"]) is >= -20_000_000 and <= 30_000_000;
    public static JsonObject Command(string owner, int pid, long ticks) => O(("OwnerId", owner), ("ProcessId", pid), ("Enabled", true), ("UntilUtcTicks", ticks + 30_000_000), ("NextCastMilliseconds", 1000), ("CastGauge", .9), ("PreferWeak", true), ("AutoApproach", true), ("AutoSell", false), ("AutoBait", false), ("AutoMapRenewal", false));
    private static Task<JsonObject> Mission(DailyWorkflow w) => DailyWeeklyMission.Read(w, DailyWeeklyMission.Fishing);
    public static async Task Enter(DailyWorkflow w)
    {
        Require(await DailyTravel.Enter(w, 2, "square.pack", packType: 12, surfaces: ["AvatarFishingHarborUI", "FishingGameFieldDefaultUI", "AvatarFishingWorldMapUI"]), "钓鱼卡带未开放");
        if (await w.Has("FishingGameFieldDefaultUI"))
            return;
        if (!await w.Has("AvatarFishingWorldMapUI"))
            await w.Step("AvatarFishingHarborUI", "_goBtnStart", expect: "AvatarFishingWorldMapUI");
        await w.Step(O(("ui", "AvatarFishingWorldMapUI"), ("field", "$pointer/Parent/Object- Right/UIParent/ButtonObject/Button - Move"), ("expect", "FishingGameFieldDefaultUI"), ("timeout", 45)));
        await DailyTravel.Ready(w, "FishingGameFieldDefaultUI");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        await w.Refresh();
        var before = await Mission(w);
        if (B(before["complete"]))
            return DailyWeeklyMission.Skipped("weekly_fishing_complete", before);
        var channel = new DailyMiniGameChannel("BD2Fishing", w.Context["actor"]!.AsArray().DeepClone().AsArray());
        var old = channel.Read("control.json");
        Require(!Active(old, w.Driver.UtcTicks), "另一钓鱼控制器正在运行，未接管");
        await DailyMiniGameChannel.Connect(w, "connect");
        channel.Acquire();
        await Enter(w);
        int pid = I(w.Context["actor"]![0]);
        JsonObject state = new();
        double end = w.Time + 5;
        while (true)
        {
            await w.Observe();
            state = channel.Read("latest.json");
            if (Fresh(state, pid, w.Driver.UtcTicks) && B(state["Ready"]))
                break;
            Require(w.Time < end, "钓鱼心跳尚未就绪");
            await w.Delay(100);
        }
        long baseline = N(state["Catches"]);
        string owner = Guid.NewGuid().ToString("N"), path = Path.Combine(w.Root, "live", "weekly-fishing", owner + ".json");
        var command = Command(owner, pid, w.Driver.UtcTicks);
        var record = O(("id", owner), ("state", "running"), ("actor", w.Context["actor"]), ("started", w.Driver.UtcTicks), ("before", state), ("mission_before", before));
        DailyJson.Write(path, record);
        try
        {
            end = w.Time + 180;
            JsonObject? success = null;
            bool settled = false;
            while (w.Time < end)
            {
                await w.Observe();
                var current = channel.Read("control.json");
                Require(!Active(current, w.Driver.UtcTicks) || S(current["OwnerId"]) == owner, "钓鱼控制权改变");
                state = channel.Read("latest.json");
                Require(Fresh(state, pid, w.Driver.UtcTicks), "钓鱼心跳已过期");
                Require(S(state["Error"]) == "", S(state["Error"]));
                Require(!B(state["BagFull"]), "鱼背包已满；周任务不会售卖已有鱼");
                if (N(state["Catches"]) > baseline)
                {
                    success ??= state.DeepClone().AsObject();
                    command["NextCastMilliseconds"] = 60000;
                    if (S(state["State"]) == "None" && !new[] { "ResultPopup", "LevelPopup", "NetworkPending", "Busy", "MapTravelBusy", "CastRunning" }.Any(k => B(state[k])))
                    {
                        settled = true;
                        break;
                    }
                }
                command["UntilUtcTicks"] = w.Driver.UtcTicks + 30_000_000;
                channel.Write("control.json", command);
                await w.Delay(150);
            }
            Require(settled, "周常钓鱼尚未结束，已停止后续抛竿");
            var after = await DailyWeeklyMission.Confirm(w, DailyWeeklyMission.Fishing);
            Require(success != null && B(after["complete"]), "钓鱼已结束但周任务未确认");
            record["state"] = "completed";
            record["after"] = state.DeepClone();
            record["catch"] = success!.DeepClone();
            record["mission_after"] = after.DeepClone();
            command["Enabled"] = false;
            command["UntilUtcTicks"] = 0;
            channel.Write("control.json", command);
            end = w.Time + 3;
            while (B(channel.Read("latest.json")["Enabled"]))
            {
                await w.Observe();
                Require(w.Time < end, "钓鱼控制器尚未释放输入");
                await w.Delay(100);
            }
            await w.Step("FishingGameFieldDefaultUI", "_objBackButton", expect: "AvatarFishingMessagePopupUI");
            await w.Step(O(("ui", "AvatarFishingMessagePopupUI"), ("field", "_objBtnConfirm"), ("expect", "AvatarFishingHarborUI"), ("timeout", 45)));
            await w.Step("AvatarFishingHarborUI", "_objHomeMenuButton", expect: "MenuUI");
            return O(("state", "completed"), ("id", owner), ("catches", N(state["Catches"]) - baseline), ("mission", after), ("runtime", state["Runtime"]));
        }
        catch (Exception e) { if (S(record["state"]) != "completed") record["state"] = "partial"; record["error"] = e.Message; throw; }
        finally { try { if (S(channel.Read("control.json")["OwnerId"]) == owner) { command["Enabled"] = false; command["UntilUtcTicks"] = 0; channel.Write("control.json", command); } } catch (Exception e) { record["cleanup_error"] = e.Message; } record["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, record); }
    }
}
public static class DailyWeeklySichuan
{
    public static int Choose(JsonObject p)
    {
        int max = I(p["Max"]);
        Require(B(p["Ready"]) && max >= 1 && max <= 10000, "常规关卡资料未就绪");
        var opened = p["Open"]!.AsArray().Select(I).ToHashSet();
        var cleared = p["Cleared"]!.AsArray().Select(I).ToHashSet();
        Require(opened.Concat(cleared).All(i => i >= 1 && i <= max), "连连看关卡资料不完整");
        var pending = opened.Except(cleared).Order().ToArray();
        if (pending.Length > 0)
            return pending[0];
        if (opened.Contains(1) && cleared.SetEquals(Enumerable.Range(1, max)))
            return 1;
        throw new InvalidDataException("尚有未通关关卡，但没有可进入的新关卡");
    }
    public static bool Fresh(JsonObject s, int pid, DateTimeOffset now) => N(s["SchemaVersion"]) == 1 && N(s["ExecutionProtocol"]) == 3 && S(s["Runtime"]) == "BD2Sichuan.Runtime2" && N(s["ProcessId"]) == pid && S(s["SessionId"]).Length > 0 && DateTimeOffset.TryParse(S(s["CapturedAtUtc"]), out var at) && (now - at).TotalSeconds is >= -2 and <= 3;
    private static bool Active(JsonObject lease, long now) => S(lease["OwnerId"]).Length > 0 && N(lease["UntilUtcTicks"]) > now && N(lease["UntilUtcTicks"]) <= now + 150_000_000;
    private static Task<JsonObject> Evidence(DailyWorkflow w) => w.Evidence("weekly.sichuan", "missions.cache");
    private static JsonObject Page(JsonObject e) => State(e, "weekly.sichuan", "$self");
    private static void Heartbeat(DailyWorkflow w, DailyMiniGameChannel channel, string owner)
    {
        var old = channel.Read("sichuan~execution-lease.json");
        Require(!Active(old, w.Driver.UtcTicks) || S(old["OwnerId"]) == owner, "连连看已由其他窗口接管");
        channel.Text("sichuan~enabled-until.txt", (w.Driver.UtcTicks + 50_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture));
        channel.Write("sichuan~execution-lease.json", O(("OwnerId", owner), ("UntilUtcTicks", w.Driver.UtcTicks + 30_000_000), ("StopReason", ""), ("IntervalMilliseconds", 1000)));
    }
    public static async Task Finish(DailyWorkflow w)
    {
        double end = w.Time + 30, clicked = 0;
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            var types = DailyNavigationDecision.Types(frame);
            if (types.Contains("SichuanMainUI") && !types.Contains("SichuanStageClearPopupUI"))
                return;
            Require(!types.Contains("SichuanStageFailedPopupUI"), "连连看未通关，保留失败界面");
            var popup = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == "SichuanStageClearPopupUI");
            if (popup != null && DailyNavigationDecision.ReadyInput(popup) && DailyNavigationDecision.Blockers(frame, "SichuanStageClearPopupUI", DailyNavigationPolicy.Load()).Length == 0 && w.Time - clicked > 2)
            {
                await w.Step("SichuanStageClearPopupUI", "_buttonExit");
                clicked = w.Time;
            }
            await w.Delay(200);
        }
        throw new StageHostException("pending", "连连看结算尚未退出，不重复开局。");
    }
    private static async Task Leave(DailyWorkflow w)
    {
        await w.Step("SichuanMainUI", back: true, absent: "SichuanMainUI");
        await DailyTravel.Ready(w, "MiniGameHubUI");
        await w.Step("MiniGameHubUI", back: true, absent: "MiniGameHubUI");
        await DailyTravel.Ready(w, "MenuUI");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        await w.Refresh();
        var before = await DailyWeeklyMission.Read(w, DailyWeeklyMission.MiniGame);
        var types = DailyNavigationDecision.Types((await w.Observe()).Frame);
        if (B(before["complete"]) && !types.Overlaps(["SichuanBoardUI", "SichuanStageClearPopupUI"]))
            return DailyWeeklyMission.Skipped("weekly_minigame_complete", before);
        await DailyMiniGameChannel.Connect(w, "connect-sichuan");
        var channel = new DailyMiniGameChannel("BD2Sichuan", w.Context["actor"]!.AsArray());
        channel.Acquire();
        if (!types.Overlaps(["SichuanMainUI", "SichuanStagePopupUI", "SichuanBoardUI", "SichuanStageClearPopupUI"]))
        {
            if (!await w.Has("MiniGameHubUI"))
            {
                await w.Enter("weekly_sichuan");
                if (!await w.Has("MiniGameHubUI"))
                    await w.Step("MenuUI", "_buttonMiniGame", expect: "MiniGameHubUI");
            }
            var hub = Rows(Page(await Evidence(w))["Hubs"]).OrderByDescending(h => N(h["Uid"])).FirstOrDefault();
            Require(hub != null, "当前没有开放的连连看入口");
            await w.Step(O(("ui", "MiniGameHubUI"), ("operation", "sichuan_open"), ("value", hub!["Uid"]), ("items", new JsonArray(Copy(hub["Event"]))), ("expect", "SichuanMainUI")));
            await DailyTravel.Ready(w, "SichuanMainUI");
        }
        if (await w.Has("SichuanStageClearPopupUI"))
        {
            await Finish(w);
            var after = await DailyWeeklyMission.Confirm(w, DailyWeeklyMission.MiniGame);
            Require(B(after["complete"]), "通关后周任务尚未确认");
            await Leave(w);
            return O(("state", "completed"), ("reason", "resumed_settlement"), ("mission", after));
        }
        var page = Page(await Evidence(w));
        int level;
        if (B(page["Playing"]))
        {
            Require(N(page["Group"]) == 1, "当前为挑战关卡，请先结束后运行周常常规关卡");
            level = I(page["Level"]);
        }
        else
        {
            if (!await w.Has("SichuanStagePopupUI"))
                await w.Step("SichuanMainUI", "_buttonEnter", expect: "SichuanStagePopupUI");
            page = Page(await Evidence(w));
            level = Choose(page);
            await w.Step("SichuanStagePopupUI", operation: "sichuan_select", value: level);
            page = Page(await Evidence(w));
            Require(N(page["Group"]) == 1 && N(page["Level"]) == level && B(page["StartEnabled"]), "常规关卡选择未就绪");
        }
        string owner = Guid.NewGuid().ToString("N"), path = Path.Combine(w.Root, "live", "weekly-sichuan", owner + ".json");
        var record = O(("id", owner), ("state", "running"), ("actor", w.Context["actor"]), ("level", level), ("mission_before", before), ("started", w.Driver.UtcTicks));
        DailyJson.Write(path, record);
        try
        {
            Heartbeat(w, channel, owner);
            if (!B(page["Playing"]))
                await w.Step("SichuanStagePopupUI", "_buttonStart");
            double end = w.Time + 300, stale = w.Time;
            bool submitted = false, complete = false;
            JsonNode? generation = null;
            string? session = null;
            while (w.Time < end)
            {
                await w.Observe();
                Heartbeat(w, channel, owner);
                var state = channel.Read("sichuan~latest.json");
                if (!Fresh(state, I(w.Context["actor"]![0]), DateTimeOffset.UtcNow))
                {
                    Require(w.Time - stale <= 8, "连连看盘面心跳未恢复");
                    await w.Delay(200);
                    continue;
                }
                stale = w.Time;
                Require(S(state["Error"]) == "", S(state["Error"]));
                Require(session == null || S(state["SessionId"]) == session && JsonNode.DeepEquals(state["Generation"], generation), "连连看局次变化");
                if (!submitted)
                {
                    if (S(state["State"]) != "ready" || state["Cells"] is not JsonArray cells || cells.Count == 0)
                    {
                        await w.Delay(200);
                        continue;
                    }
                    Require(N(state["LevelGroup"]) == 1 && N(state["Level"]) == level, "连连看盘面不是选定常规关卡");
                    session = S(state["SessionId"]);
                    generation = Copy(state["Generation"]);
                    channel.Write("sichuan~run-command.json", O(("Protocol", 3), ("OwnerId", owner), ("SessionId", session), ("ProcessId", w.Context["actor"]![0]), ("CreatedUtcTicks", w.Driver.UtcTicks), ("IntervalMilliseconds", 1000), ("Automatic", true)));
                    submitted = true;
                }
                var run = state["Run"] as JsonObject ?? new();
                if (S(run["OwnerId"]) == owner)
                {
                    if (S(run["State"]) == "completed" && S(run["Reason"]) == "board_cleared")
                    {
                        record["solver"] = run.DeepClone();
                        complete = true;
                        break;
                    }
                    Require(S(run["State"]) is not ("stopped" or "rejected" or "failed" or "completed"), "连连看执行停止：" + S(run["Reason"]));
                }
                await w.Delay(150);
            }
            Require(complete, "连连看尚未通关，已停止输入并保留现场");
            channel.Write("sichuan~execution-lease.json", O(("OwnerId", owner), ("UntilUtcTicks", 0), ("StopReason", "daily_completed"), ("IntervalMilliseconds", 1000)));
            await Finish(w);
            var evidence = await w.WaitEvidence(["weekly.sichuan", "missions.cache"], e => B(DailyWeeklyMission.Progress(e, DailyWeeklyMission.MiniGame, w.Table("policy/MissionTable.json"))["complete"]) && Page(e)["Cleared"]!.AsArray().Any(id => N(id) == level), 15, "本局已清空，等待服务器通关和周任务确认；不会重复开局");
            record["state"] = "completed";
            record["mission_after"] = DailyWeeklyMission.Progress(evidence, DailyWeeklyMission.MiniGame, w.Table("policy/MissionTable.json"));
            record["cleared"] = Copy(Page(evidence)["Cleared"]);
            await Leave(w);
            return O(("state", "completed"), ("level", level), ("group", 1), ("mission", record["mission_after"]), ("id", owner));
        }
        catch (Exception e) { if (S(record["state"]) != "completed") record["state"] = "partial"; record["error"] = e.Message; throw; }
        finally { try { if (S(channel.Read("sichuan~execution-lease.json")["OwnerId"]) == owner) { channel.Write("sichuan~execution-lease.json", O(("OwnerId", owner), ("UntilUtcTicks", 0), ("StopReason", "daily_finished"), ("IntervalMilliseconds", 1000))); channel.Text("sichuan~enabled-until.txt", "0"); } } catch (Exception e) { record["cleanup_error"] = e.Message; } record["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, record); }
    }
}

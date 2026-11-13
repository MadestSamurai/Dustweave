using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Resume the existing login once. Never launch, switch, restore or renew credentials.</summary>
public static class DailyNetworkRecovery
{
    public const string RestartContext = "network_timeout_return_title";
    public const string Manual = "网络恢复未完成，已停止自动登录并保留进度。请先在游戏中确认网络和登录状态，再继续上次进度。";
    public const string Waiting = "网络超时，正在尝试一次重新进入游戏；原进度已保留。";
    public static StageHostException Interrupted() => new("network", Waiting) { HoldQueue = true, Display = DailyText.Of("network.recovery_wait", Waiting) };
    private static StageHostException Failure(string detail) => new("network", Manual + "（" + detail + "）") { HoldQueue = true, Display = DailyText.Of("network.recovery_manual", Manual) };

    internal static async Task<JsonObject> ResumeQueueAsync(DailyQueueRequest request, Func<DailyQueueRequest, Task<JsonObject>> execute,
        Func<JsonObject, Task<bool>> recover, Func<bool> stopped)
    {
        var record = await execute(request);
        if (S(record["state"]) != "paused" || stopped()) return record;
        try
        {
            if (!await recover(record["context"]!.AsObject()) || stopped()) return record;
            // Resume by ID: keep task selection, completed work and pending proofs.
            record = await execute(new(request.Root, request.Account, request.Output, S(record["id"])));
            // No second recovery in this invocation, including a new error popup.
            if (S(record["state"]) == "paused" && S(record["error_kind"]) == "network")
                throw Failure("重新登录后再次断线，未再次登录");
        }
        catch (Exception error)
        {
            record["state"] = "paused"; record["error"] = error.Message; record["error_detail"] = error.ToString();
            record["display"] = System.Text.Json.JsonSerializer.SerializeToNode((error as StageHostException)?.Display);
            DailyJson.Write(DailyQueueEngine.RecordPath(request.Root, S(record["id"])), record);
        }
        return record;
    }

    public static JsonObject? Popup(JsonObject frame) => DailyNavigationDecision.Rows(frame)
        .SingleOrDefault(u => S(u["Type"]) == "MessagePopupUI" && S(u["NativeContext"]) == RestartContext);
    public static bool TimeoutPopup(JsonObject frame)
    {
        var popup = Popup(frame);
        return N(frame["BridgeVersion"]) >= 146 && popup != null && B(popup["InputReady"])
            && (popup["Targets"] as JsonArray)?.Any(t => S(t?["Field"]) == "_buttonOK" && B(t?["Enabled"])) == true
            && !(popup["Targets"] as JsonArray)!.Any(t => S(t?["Field"]) == "_buttonCancel" && B(t?["Enabled"]))
            && !DailyNavigationDecision.Rows(frame).Any(u => B(u["Popup"]) && N(u["Id"]) != N(popup["Id"])
                && N(u["Order"]) >= N(popup["Order"]) && !DailyNavigationDecision.PassiveSurface(S(u["Type"])));
    }
    private static string Journal(string root, JsonObject context)
    {
        var actor = context["actor"]!.AsArray();
        return Path.Combine(root, "live", "diagnostics", "network-recovery", DailyIdentity.Hash(actor.ToJsonString()) + ".json");
    }
    // Only an explicit run with a fresh, healthy identified game resets the budget.
    // Reopening a failed popup or restarting the host never grants another login.
    public static void BeginManualRun(string root, DailyStageFrame observed)
    {
        long at = DateTime.UtcNow.Ticks;
        if (observed.Daily is not { State: "identified", Loading: false } d || d.Startup?.Visible == true || !StartupPolicy.Fresh(d, at)
            || Popup(observed.Frame) != null || DailyNavigationDecision.Rows(observed.Frame).Any(u => B(u["Popup"]))
            || d.AccountKey != S(observed.Context["actor"]?[3]) || d.PlayerKey != S(observed.Context["actor"]?[4])
            || at - N(observed.Frame["AtUtcTicks"]) is < 0 or > 30000000) return;
        string path = Journal(root, observed.Context);
        if (File.Exists(path)) DailyJson.Write(path, O(("state", "manual_ready"), ("used", false), ("at", DateTime.UtcNow.Ticks)));
    }

    internal static void Validate(DailyStageFrame observed, JsonObject context, string dailyInstance, long now)
    {
        var f = observed.Frame; var d = observed.Daily ?? throw new IOException("等待账号观察");
        var a = context["actor"]!.AsArray();
        if (N(f["ProcessId"]) != N(a[0]) || N(f["ProcessStartTicks"]) != N(a[1]) || S(f["Instance"]) != S(a[2])
            || d.ProcessId != N(a[0]) || d.ProcessStartTicks != N(a[1]) || d.InstanceId != dailyInstance)
            throw Failure("游戏进程或控制连接已变化");
        if (N(f["Protocol"]) != 1 || N(f["BridgeVersion"]) != DailyBridgeVersion.Current)
            throw Failure("执行组件已变化");
        foreach (var key in new[] { S(f["AccountKey"]), d.AccountKey })
            if (key.Length > 0 && key != S(a[3])) throw Failure("当前账号已变化");
        foreach (var role in new[] { S(f["PlayerKey"]), d.PlayerKey })
            if (role.Length > 0 && role != S(a[4])) throw Failure("当前角色已变化");
        if (d.Guild is { } g && (!string.IsNullOrEmpty(g.ServerKey) && g.ServerKey != S(context["server"])
            || !string.IsNullOrEmpty(g.CycleKey) && g.CycleKey != S(context["cycle"]))) throw Failure("服务器或每日重置周期已变化");
        if (d.ErrorCode.Length > 0 || d.LeaseOwner.Length > 0
            || S(f["Error"]) is not ("" or "Waiting for fresh account identity")) throw Failure("登录识别失败或控制已被接管");
        if (!StartupPolicy.Fresh(d, now) || now - N(f["AtUtcTicks"]) is < -20000000 or > 30000000)
            throw new IOException("等待最新登录观察");
    }

    // Injectable state machine: one error acknowledgement, one pinned title and
    // two advancing identity observations. Delays are observation only, not retries.
    internal static async Task<bool> RecoverAsync(JsonObject context, Func<Task<DailyStageFrame>> read,
        Func<Task> acknowledge, Action<StartupPermit?> permit, Action claim, Func<bool> stopped,
        Action<string> report, Func<long>? now = null, Func<TimeSpan, Task>? delay = null)
    {
        now ??= () => DateTime.UtcNow.Ticks; delay ??= t => Task.Delay(t);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        long deadline = now() + TimeSpan.FromMinutes(3).Ticks;
        DailyStageFrame first = await read();
        if (Popup(first.Frame) == null) return false;
        string dailyInstance = first.Daily?.InstanceId ?? "";
        Validate(first, context, dailyInstance, now());
        void Stop() { if (stopped()) throw new StageHostException("stopped", "已停止网络恢复，原进度已保留。"); }
        long popupDeadline = now() + TimeSpan.FromSeconds(5).Ticks;
        // The observer can interrupt while the native popup is still animating.
        // Wait for its button without consuming an attempt or sending input.
        while (!TimeoutPopup(first.Frame))
        {
            Stop();
            if (now() >= popupDeadline || elapsed.Elapsed >= TimeSpan.FromSeconds(5))
                throw Failure("网络提示尚未就绪，未点击确认");
            await delay(TimeSpan.FromMilliseconds(350));
            try { var next = await read(); Validate(next, context, dailyInstance, now()); first = next; }
            catch (Exception e) when (e is IOException or TimeoutException) { continue; }
            if (Popup(first.Frame) == null) return false;
        }
        if (S(first.Frame["AccountKey"]) != S(context["actor"]![3]) || first.Daily!.AccountKey != S(context["actor"]![3]))
            throw Failure("尚未确认原登录账号");
        string owner = Guid.NewGuid().ToString("N");
        long popupId = N(Popup(first.Frame)!["Id"]), readySequence = 0;
        int title = 0; bool leftPopup = false, titleSeen = false, entered = false;
        try
        {
            Stop();
            claim(); // Durable BEFORE the first input. Even an uncertain receipt uses this attempt.
            report("network.recovery_wait");
            try { await acknowledge(); }
            catch (DailyStepException e) when (e.MayHaveDispatched) { /* Only observe; never send another confirmation. */ }
            long earliestStart = now() + TimeSpan.FromSeconds(3).Ticks;
            while (now() < deadline && elapsed.Elapsed < TimeSpan.FromMinutes(3))
            {
                Stop();
                DailyStageFrame current;
                try { current = await read(); Validate(current, context, dailyInstance, now()); }
                catch (Exception e) when (e is IOException or TimeoutException)
                { permit(null); await delay(TimeSpan.FromMilliseconds(350)); continue; }
                var f = current.Frame; var d = current.Daily!; var t = d.Startup;
                var popup = Popup(f);
                if (popup != null)
                {
                    permit(null);
                    if (leftPopup || N(popup["Id"]) != popupId) throw Failure("本次重新登录再次失败，未重复确认");
                    await delay(TimeSpan.FromMilliseconds(350)); continue;
                }
                leftPopup = true;
                bool ready = titleSeen && !d.Loading && d.State == "identified" && t?.Visible != true && S(f["Scene"]) is not ("Splash" or "ReGame")
                    && d.AccountKey == S(context["actor"]![3]) && d.PlayerKey == S(context["actor"]![4])
                    && S(f["AccountKey"]) == d.AccountKey && S(f["PlayerKey"]) == d.PlayerKey
                    && d.Guild?.ServerKey == S(context["server"]) && d.Guild.CycleKey == S(context["cycle"]);
                // Never accept another error/terms/account prompt. Loading without
                // observations is allowed to settle within the same fixed deadline.
                if (!ready && DailyNavigationDecision.Rows(f).Any(u => B(u["Popup"]) && !DailyNavigationDecision.PassiveSurface(S(u["Type"]))))
                    throw Failure("游戏弹窗需要手动处理");
                if (t?.Visible == true)
                {
                    titleSeen = true; readySequence = 0;
                    if (t.BlockReason.Contains("弹窗", StringComparison.Ordinal) || t.DownloadVisible)
                        throw Failure("启动页需要手动处理");
                    if (title != 0 && t.TitleInstanceId != 0 && title != t.TitleInstanceId)
                        throw Failure("已再次返回登录页，未重复登录");
                    if (t.Attempted) { entered = true; permit(null); }
                    else if (!entered && now() >= earliestStart && t.Supported && t.Ready && t.ClickEnabled && t.BlockReason.Length == 0
                        && t.TitleInstanceId != 0 && d.AccountKey == S(context["actor"]![3]))
                    {
                        title = t.TitleInstanceId;
                        Stop();
                        permit(new StartupPermit { Owner = owner, AccountKey = d.AccountKey, InstanceId = d.InstanceId,
                            ProcessId = d.ProcessId, ProcessStartTicks = d.ProcessStartTicks,
                            ExpiresUtcTicks = now() + TimeSpan.FromSeconds(5).Ticks, ResumeOnly = true, TitleInstanceId = title });
                    }
                    else permit(null);
                }
                else
                {
                    permit(null);
                    if (ready && readySequence > 0 && d.Sequence > readySequence) { Stop(); return true; }
                    readySequence = ready ? d.Sequence : 0;
                }
                await delay(TimeSpan.FromMilliseconds(350));
            }
            throw Failure("等待重新进入游戏超时，未再次登录");
        }
        finally { permit(null); }
    }

    public static async Task<bool> RunAsync(string root, JsonObject context, Func<bool> stopped, Action<string> report)
    {
        if (stopped()) return false;
        var game = new DailyGameHost(root).Find(); var actor = context["actor"]!.AsArray();
        if (game == null || game.ProcessId != N(actor[0]) || game.StartTicks != N(actor[1])) return false;
        var mailbox = new DailyPipeMailbox(root, game);
        string? dailyInstance = null;
        Task<DailyStageFrame> Read()
        {
            var bytes = mailbox.Read("live", "snapshot.json") ?? throw new IOException("等待网络恢复观察");
            var f = JsonNode.Parse(bytes)!.AsObject();
            var d = DailyJson.TryRead<DailySnapshot>(Path.Combine(root, "snapshot.json"));
            var observed = new DailyStageFrame(f, context, d);
            if (dailyInstance != null) Validate(observed, context, dailyInstance, DateTime.UtcNow.Ticks);
            return Task.FromResult(observed);
        }
        DailyStageFrame first;
        try { first = await Read(); }
        catch (Exception e) when (e is IOException or TimeoutException) { return false; }
        if (Popup(first.Frame) == null) return false;
        Validate(first, context, first.Daily?.InstanceId ?? "", DateTime.UtcNow.Ticks);
        dailyInstance = first.Daily!.InstanceId;
        string journal = Journal(root, context), permitPath = Path.Combine(root, "startup-permit.json"), owner = "";
        var previous = DailyJson.TryRead<StartupPermit>(permitPath);
        if (previous != null && previous.Owner.Length > 0 && previous.ExpiresUtcTicks > DateTime.UtcNow.Ticks)
            throw Failure("已有登录操作正在进行");
        using var driver = new DailyCommandDriver(root, mailbox, Read, stopped);
        driver.Bind(context); driver.Acquire("live"); driver.Acquire("daily");
        var saved = DailyJson.TryRead<JsonObject>(journal);
        if (saved == null && File.Exists(journal)) throw Failure("上次恢复记录无法读取；请手动进入游戏后继续");
        var record = saved ?? new JsonObject();
        void Permit(StartupPermit? value)
        {
            if (value != null)
            {
                if (stopped()) throw new StageHostException("stopped", "已停止网络恢复。");
                var active = DailyJson.TryRead<StartupPermit>(permitPath);
                if (active != null && active.Owner.Length > 0 && active.Owner != owner && active.ExpiresUtcTicks > DateTime.UtcNow.Ticks)
                    throw Failure("另一项登录操作已接管");
                owner = value.Owner; DailyJson.Write(permitPath, value);
            }
            else if (owner.Length > 0 && DailyJson.TryRead<StartupPermit>(permitPath)?.Owner == owner)
                DailyJson.Write(permitPath, new StartupPermit());
        }
        try
        {
            return await RecoverAsync(context, Read, async () =>
            {
                if (mailbox.Read("live", "command.json") != null) throw Failure("上一操作仍待确认，未重放");
                await driver.HandleAsync(O(("operation", "delete"), ("channel", "live"), ("name", "pause")));
                await driver.AcknowledgeNetworkTimeoutAsync();
            }, Permit, () =>
            {
                if (B(record["used"])) throw Failure("同一次运行已尝试重新登录，请手动处理后继续");
                record = O(("used", true), ("state", "recovering"), ("at", DateTime.UtcNow.Ticks), ("context", context));
                DailyJson.Write(journal, record);
            }, stopped, report);
        }
        catch (Exception error) { record["state"] = "paused"; record["error"] = error.ToString(); throw; }
        finally
        {
            Permit(null);
            if (S(record["state"]) == "recovering") record["state"] = "returned";
            DailyJson.Write(journal, record);
            // Keep gameplay paused until the original executor reacquires control
            // and reconciles its durable transactions against fresh server state.
            if (driver.HasControl)
                try { mailbox.Write("live", "pause", []); }
                catch (Exception e) when (e is IOException or TimeoutException) { report("network.recovery_manual"); }
        }
    }
}

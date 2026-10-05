using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BD2.LocalIpc;
namespace BD2Daily;

public sealed class DailyStepException(string kind, string message, bool submitted = false) : Exception(message)
{
    public string Kind { get; } = kind;
    public bool Submitted { get; } = submitted;
    public string RejectionCode { get; init; } = "";
    public JsonObject? Command
    {
        get; set;
    }
}
/// <summary>Common managed submit/receipt path. Business proof and UI recovery stay with their stage.</summary>
public sealed partial class DailyCommandDriver : IDisposable
{
    public DailyDiagnosticTrail Diagnostics { get; }
    public Action? SubmissionGuard
    {
        get; set;
    }
    public bool TradeStageActive
    {
        get; set;
    }
    private readonly string root;
    private readonly IDailyCommandMailbox mailbox;
    private readonly DailyControlOwner owner;
    private readonly Func<Task<DailyStageFrame>> read;
    private readonly Func<bool> stopped;
    private readonly Func<long> now;
    private readonly Func<double> clock;
    private readonly Func<TimeSpan, Task> delay;
    private readonly DailyNavigationPolicy policy;
    private JsonObject? context;
    private JsonObject? lastDiagnosticFrame;
    private bool closed;
    public double MonotonicTime => clock();
    public Task DelayAsync(TimeSpan duration) => delay(duration);
    public bool HasControl => !closed && owner.Held;
    internal bool PauseRequested => stopped() || mailbox.Read("live", "pause") != null;
    private static readonly string[] ActorKeys = ["ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey"];
    private static readonly HashSet<string> ActionKeys = ["ui", "field", "back", "expect", "absent", "timeout", "reason", "allow_guild", "target_id", "startup", "native", "mirror_entry", "dispatch_entry", "pass_init", "operation", "items", "value", "require_stealth"];
    public DailyCommandDriver(string root, IDailyCommandMailbox mailbox, Func<Task<DailyStageFrame>> read, Func<bool> stopped,
        Func<long>? now = null, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, DailyNavigationPolicy? policy = null)
    {
        this.root = root;
        this.mailbox = mailbox;
        owner = new(root);
        this.read = read;
        this.stopped = stopped;
        this.now = now ?? (() => DateTime.UtcNow.Ticks);
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.policy = policy ?? DailyNavigationPolicy.Load();
        Diagnostics = new(root, this.clock, this.now);
    }
    public void Bind(JsonObject value)
    {
        if (context != null || value["actor"] is not JsonArray a || a.Count != 5 || value["server"] is not JsonValue || value["cycle"] is not JsonValue)
            throw new StageHostException("protocol", "Invalid or repeated managed driver context");
        context = value.DeepClone().AsObject();
    }
    public void EnsureBound(JsonObject value)
    {
        if (context == null)
            Bind(value);
        else if (!JsonNode.DeepEquals(context, value))
            throw new StageHostException("identity", "Managed driver is already bound to another queue identity");
    }
    private void Active()
    {
        if (closed || context == null)
            throw new StageHostException("protocol", "Managed driver is not bound to a queue");
    }
    public void Acquire(string channel)
    {
        Active();
        if (stopped())
            throw new StageHostException("stopped", "Stopped before taking queue control");
        owner.Acquire();
        mailbox.Acquire(channel);
    }
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static bool Flag(JsonObject obj, string key) => obj[key]?.GetValue<bool>() ?? false;
    private static long Number(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value)
            return 0;
        if (value.TryGetValue<long>(out var n))
            return n;
        if (value.TryGetValue<int>(out var small))
            return small;
        throw new StageHostException("protocol", "Invalid integer: " + key);
    }
    private static string[] Entries(string channel) => channel switch
    {
        "live" => DailyStageObservation.LiveEntries.Split('|'),
        "daily" => DailyIdentity.LiveEntries.Split('|'),
        _ => throw new StageHostException("protocol", "Unknown managed mailbox channel")
    };
    private static void Entry(string channel, string name, bool list = false)
    {
        if (name.Length == 0 || name.Length > 160 || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new StageHostException("protocol", "Invalid managed mailbox entry");
        var entries = Entries(channel);
        bool allowed = list ? entries.Any(p => p.EndsWith('*') && p[..^1] == name) : entries.Any(p => p == name || p.EndsWith('*') && name.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            throw new StageHostException("protocol", "Unregistered managed mailbox entry");
    }
    public async Task<JsonObject> HandleAsync(JsonObject request)
    {
        Active();
        string op = Text(request, "operation");
        if (op == "submit")
            return await SubmitAsync(request["action"]?.AsObject() ?? throw new StageHostException("protocol", "Missing managed step"));
        if (op == "demand")
            return await DemandAsync(request["prefixes"]?.AsArray().Select(p => p!.GetValue<string>()) ?? throw new StageHostException("protocol", "Missing managed observation prefixes"));
        if (op == "release_demand")
        {
            ReleaseDemand(Text(request, "request_id"));
            return new()
            {
                ["state"] = "released"
            };
        }
        string channel = Text(request, "channel"), name = Text(request, "name");
        if (op == "open")
        {
            Acquire(channel);
            return new()
            {
                ["state"] = "owned"
            };
        }
        Entry(channel, name, op == "list");
        if (op == "read")
        {
            var data = mailbox.Read(channel, name);
            return new()
            {
                ["value"] = data == null ? null : Convert.ToBase64String(data)
            };
        }
        if (op == "list")
            return new()
            {
                ["names"] = new JsonArray(mailbox.List(channel, name).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray())
            };
        if (!owner.Held)
            throw new StageHostException("protocol", "Managed mailbox write without queue control");
        if (channel == "live" && name == "command.json")
            throw new StageHostException("protocol", "Native input must use the managed submit operation");
        if (op == "write")
        {
            byte[] data = Convert.FromBase64String(Text(request, "value"));
            if (data.Length > Wire.MaximumFrame)
                throw new StageHostException("protocol", "Managed mailbox payload is too large");
            if (stopped() && name != "pause")
                throw new StageHostException("stopped", "Stopped before managed mailbox write");
            mailbox.Write(channel, name, data, name.EndsWith("command.json", StringComparison.Ordinal));
            return new()
            {
                ["state"] = "written"
            };
        }
        if (op == "delete")
        {
            if (stopped() && name == "pause")
                throw new StageHostException("stopped", "Stopped before clearing queue pause");
            mailbox.Delete(channel, name);
            return new()
            {
                ["state"] = "deleted"
            };
        }
        throw new StageHostException("protocol", "Unknown managed driver operation");
    }
    private async Task<DailyStageFrame> ReadBound()
    {
        Active();
        mailbox.Read("live", "snapshot.json"); // Validate the exact writer lease, including handoff/revocation.
        var observed = await read();
        lastDiagnosticFrame = observed.Frame;
        if (!JsonNode.DeepEquals(observed.Context, context))
            throw new DailyStepException("identity", "Account, connection or reset changed; no replay");
        Diagnostics.Observe(observed.Frame);
        return observed;
    }
    private static double Seconds(JsonObject action)
    {
        if (action["timeout"] is not JsonValue value)
            return 20;
        if (value.TryGetValue<double>(out var d))
            return d;
        if (value.TryGetValue<int>(out var n))
            return n;
        if (value.TryGetValue<long>(out var wide))
            return wide;
        throw new StageHostException("protocol", "Invalid step deadline");
    }
    public static void ValidateAction(JsonObject action)
    {
        if (action.Any(p => !ActionKeys.Contains(p.Key)) || action["ui"] is not JsonValue || Text(action, "ui").Length == 0)
            throw new StageHostException("protocol", "Invalid managed step action");
        foreach (string key in new[] { "ui", "field", "expect", "absent", "reason", "operation" })
            if (action[key] != null && (action[key] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length > 1024))
                throw new StageHostException("protocol", "Invalid managed step string");
        foreach (string key in new[] { "back", "allow_guild", "startup", "native", "mirror_entry", "dispatch_entry", "pass_init", "require_stealth" })
            if (action[key] != null && (action[key] is not JsonValue value || !value.TryGetValue<bool>(out _)))
                throw new StageHostException("protocol", "Invalid managed step flag");
        if (action["value"] != null)
            _ = checked((int)Number(action, "value"));
        if (action["target_id"] != null)
            _ = checked((int)Number(action, "target_id"));
        if (action["items"] != null)
            foreach (var item in action["items"]!.AsArray())
                _ = DailyEvidence.Integer(item);
        double timeout = Seconds(action);
        if (!double.IsFinite(timeout) || timeout <= 0)
            throw new StageHostException("protocol", "Invalid managed step deadline");
    }
    private void GuildGuard(JsonObject action, DailyStageFrame observed, ref string reason)
    {
        if (Text(action, "ui") != "MenuUI" || Text(action, "field") != "_buttonGuild")
            return;
        if (!Flag(action, "allow_guild"))
            throw new DailyStepException("rejected", "Guild entry requires explicit attendance authorization");
        var daily = observed.Daily ?? throw new DailyStepException("rejected", "Daily guild observation unavailable");
        if (daily.FrameUtcTicks > now() || now() - daily.FrameUtcTicks > 3 * TimeSpan.TicksPerSecond)
            throw new DailyStepException("rejected", "Daily guild observation changed");
        string gate = GuildPolicy.Gate(daily, new GuildStore(root).Prior(daily), now());
        if (gate.Length > 0)
            throw new DailyStepException("rejected", gate);
        foreach (string name in mailbox.List("live", "receipts~"))
        {
            var raw = mailbox.Read("live", name);
            if (raw == null)
                continue;
            var receipt = JsonNode.Parse(raw)!.AsObject();
            var command = receipt["Command"]?.AsObject();
            if (command != null && Text(command, "AccountKey") == Text(observed.Frame, "AccountKey") && Text(command, "Reason").StartsWith("guild-once|" + daily.Guild.CycleKey + "|", StringComparison.Ordinal) && Flag(receipt, "MayHaveDispatched"))
                throw new DailyStepException("rejected", "Previous live guild dispatch exists; inspect before retry");
        }
        reason = "guild-once|" + daily.Guild.CycleKey + "|" + reason;
    }
    public static JsonObject BuildCommand(JsonObject frame, JsonObject surface, int target, JsonObject action, long at, string id, string reason, string route = "ui")
    {
        var command = new JsonObject();
        foreach (string key in ActorKeys.Concat(["Scene", "UiToken"]))
            command[key] = frame[key]?.DeepClone();
        string operation = Text(action, "operation");
        command["Scope"] = Flag(action, "startup") ? "startup" : "gameplay";
        command["Id"] = id;
        command["Kind"] = operation.Length > 0 ? operation : Flag(action, "pass_init") ? "pass_init" : Flag(action, "dispatch_entry") ? "dispatch_menu" : Flag(action, "mirror_entry") ? "mirror_ready" : Flag(action, "back") ? "back" : Flag(action, "native") ? "native_click" : route == "pointer" ? "pointer" : "click";
        command["SurfaceId"] = surface["Id"]?.DeepClone();
        command["TargetId"] = target;
        command["ObservedUtcTicks"] = frame["AtUtcTicks"]?.DeepClone();
        command["ExpiresUtcTicks"] = at + 10 * TimeSpan.TicksPerSecond;
        command["Reason"] = reason;
        command["Items"] = action["items"]?.DeepClone() ?? new JsonArray();
        command["Value"] = action["value"]?.DeepClone() ?? JsonValue.Create(0);
        command["RequireStealth"] = Flag(action, "require_stealth");
        return command;
    }
    public async Task<JsonObject> SubmitAsync(JsonObject action)
    {
        SubmissionGuard?.Invoke();
        if (TradeStageActive && Text(action, "operation") == "trade_talent")
            return await TradeTalentPreviewAsync(action);
        if (TradeStageActive && Text(action, "ui") == "ShopUI" && Text(action, "field") == "_objBackButton")
            return await TradeCloseAsync(action);
        collectionDeferral = null;
        if (Text(action, "operation") == "mainline_menu" && Number(action, "value") is 3 or 4 or 6 or 20)
            return await CollectionTalentMenuAsync(action);
        if (Text(action, "operation") == "mainline_talent")
            return await FieldTalentAsync(action);
        if (Text(action, "operation") == "weekly_npc_query")
            return await WeeklyNpcQueryAsync(action);
        return await SubmitRawAsync(action);
    }
    internal async Task<JsonObject> RetryStartupAsync()
    {
        var before = await ReadBound();
        if (DailyStartupRecovery.NetworkError(before.Frame) == null)
            throw new DailyStepException("rejected", "不属于已核对的资源下载错误");
        return await SubmitRawAsync(new JsonObject { ["ui"] = "MessagePopupUI", ["field"] = "_buttonOK", ["startup"] = true, ["timeout"] = 25, ["reason"] = "更新资源网络错误：有限次数原生重试" }, requiredFrame: before.Frame, startup: true);
    }
    public Task<JsonObject> SubmitOwnedAsync(JsonObject action, JsonObject ownedFrame)
    {
        SubmissionGuard?.Invoke();
        return SubmitRawAsync(action, ownedFrame: ownedFrame);
    }
    private async Task<JsonObject> SubmitRawAsync(JsonObject action, JsonObject? requiredFrame = null, JsonObject? ownedFrame = null, bool startup = false)
    {
        Active();
        action = DailyMenuNavigation.Normalize(action);
        ValidateAction(action);
        if (Flag(action, "startup") && !startup)
            throw new DailyStepException("rejected", "Startup control is outside the identified queue driver");
        if (stopped() || mailbox.Read("live", "pause") != null)
            throw new DailyStepException("rejected", "Paused by operator; no command sent");
        if (!owner.Held)
            Acquire("live");
        var observed = await ReadBound();
        var before = observed.Frame;
        string ui = Text(action, "ui"), field = Text(action, "field");
        if (ui == "GameQuitPopupUI" && (field != "_objCloseButton" || Flag(action, "back") || Text(action, "operation").Length > 0 || Flag(action, "native")))
            throw new DailyStepException("rejected", "日常流程只能取消退出游戏确认，不能确认退出。");
        if (ownedFrame != null && !DailyTradePreview.Matches(action, ownedFrame, before))
            throw new DailyStepException("rejected", "Owned confirmation changed before dispatch; no command sent");
        if (requiredFrame != null && (!DailyEvidence.SameActor(before, requiredFrame) || !JsonNode.DeepEquals(before["Scene"], requiredFrame["Scene"])))
            throw new DailyStepException("rejected", "Talent field changed before native input; no dispatch");
        var surfaces = DailyNavigationDecision.Rows(before).Where(s => Text(s, "Type") == ui).ToArray();
        if (surfaces.Length != 1)
            throw new DailyStepException("rejected", $"Need one observed {ui}; found {surfaces.Length}") { RejectionCode = surfaces.Length == 0 ? "surface_missing" : "surface_ambiguous" };
        var surface = surfaces[0];
        if (ownedFrame != null && DailyTradePreview.IsConfirmation(action) && !DailyNavigationDecision.ReadyInput(surface, fallback: false))
            throw new DailyStepException("rejected", "rejected: ui_not_ready");
        var blockers = DailyNavigationDecision.Blockers(before, ui, policy);
        if (blockers.Length > 0)
            throw new DailyStepException("rejected", "Foreground popup needs handling: " + string.Join(", ", blockers));
        int target = 0;
        string route = "ui";
        if (!Flag(action, "back") && !Flag(action, "mirror_entry") && !Flag(action, "pass_init") && Text(action, "operation").Length == 0)
        {
            var targets = surface["Targets"]!.AsArray().Select(t => t!.AsObject()).Where(t => Text(t, "Field") == field && Flag(t, "Enabled") && (action["target_id"] == null || Number(t, "Id") == Number(action, "target_id"))).ToArray();
            if (targets.Length != 1)
                throw new DailyStepException("rejected", $"Need one enabled observed field {field}; found {targets.Length}") { RejectionCode = targets.Length == 0 ? "target_missing" : "target_ambiguous" };
            target = checked((int)Number(targets[0], "Id"));
            route = Text(targets[0], "Route");
        }
        string reason = Text(action, "reason");
        GuildGuard(action, observed, ref reason);
        var refreshed = await ReadBound();
        var current = refreshed.Frame;
        if (ownedFrame != null && !DailyTradePreview.Matches(action, ownedFrame, current))
            throw new DailyStepException("rejected", "Owned confirmation changed during final preflight; no command sent");
        if (ActorKeys.Concat(["Scene"]).Any(k => !JsonNode.DeepEquals(current[k], before[k])))
            throw new DailyStepException("rejected", "Identity or scene changed before dispatch");
        var matching = DailyNavigationDecision.Rows(current).Where(s => JsonNode.DeepEquals(s["Id"], surface["Id"]) && Text(s, "Type") == ui).ToArray();
        if (matching.Length != 1 || target != 0 && !matching[0]["Targets"]!.AsArray().Any(t => Number(t!.AsObject(), "Id") == target && Flag(t.AsObject(), "Enabled")))
            throw new DailyStepException("rejected", "rejected: screen_changed");
        if (ownedFrame != null && DailyTradePreview.IsConfirmation(action) && !DailyNavigationDecision.ReadyInput(matching[0], fallback: false))
            throw new DailyStepException("rejected", "rejected: ui_not_ready");
        if (DailyNavigationDecision.Blockers(current, ui, policy).Length > 0)
            throw new DailyStepException("rejected", "rejected: foreground_popup");
        if (stopped() || mailbox.Read("live", "pause") != null)
            throw new DailyStepException("rejected", "Paused before dispatch; no command sent");
        if (mailbox.Read("live", "command.json") != null)
            throw new DailyStepException("rejected", "Another command is pending");
        // Recheck attendance against the observation actually used for this command.
        reason = Text(action, "reason");
        GuildGuard(action, refreshed, ref reason);
        if (reason.StartsWith("home-reset-proof|112003|", StringComparison.Ordinal) && !DailyHomeDecision.ResetPopup(current, policy))
            throw new DailyStepException("rejected", "rejected: screen_changed");
        if (startup && DailyStartupRecovery.NetworkError(current) == null)
            throw new DailyStepException("rejected", "启动错误窗口在派发前改变");
        before = current;
        string id = Guid.NewGuid().ToString("N"), session = Path.Combine(root, "live", "steps", id);
        var command = BuildCommand(before, surface, target, action, now(), id, reason, route);
        DailyJson.Write(Path.Combine(session, "before.json"), before);
        DailyJson.Write(Path.Combine(session, "intent.json"), command);
        Diagnostics.Event("command_prepared", DailyData.O(("id", id), ("action", action), ("command", command)));
        double started = clock(), end = started + (Seconds(action));
        JsonObject? receipt = null;
        try
        {
            mailbox.Write("live", "command.json", JsonSerializer.SerializeToUtf8Bytes(command), true);
            while (clock() < end)
            {
                var bytes = mailbox.Read("live", "receipts~" + id + ".json");
                if (bytes != null)
                {
                    receipt = JsonNode.Parse(bytes)!.AsObject();
                    var attached = receipt["Command"]?.AsObject();
                    if (attached == null || !JsonNode.DeepEquals(attached["Id"], command["Id"]) || command.Any(pair => !JsonNode.DeepEquals(attached[pair.Key], pair.Value)))
                        throw new DailyStepException("pending", "Receipt does not belong to the submitted command", true);
                    string state = Text(receipt, "State");
                    if (state == "rejected" && receipt["MayHaveDispatched"] is JsonValue dispatchFlag && dispatchFlag.TryGetValue<bool>(out var didDispatch) && !didDispatch)
                    {
                        DailyJson.Write(Path.Combine(session, "result.json"), receipt);
                        throw new DailyStepException("rejected", "rejected: " + Text(receipt, "Error"), true);
                    }
                    if (state is "rejected" or "unknown")
                        throw new DailyStepException("pending", state + ": " + Text(receipt, "Error"), true);
                    if (state == "observed_after_dispatch")
                    {
                        if (!Flag(receipt, "MayHaveDispatched"))
                            throw new DailyStepException("pending", "Observed receipt lacks dispatch evidence", true);
                        var result = new JsonObject { ["state"] = "observed_receipt", ["engine"] = "dotnet-driver-v1", ["id"] = id, ["before"] = before.DeepClone(), ["command"] = command.DeepClone(), ["receipt"] = receipt.DeepClone(), ["elapsed"] = clock() - started };
                        DailyJson.Write(Path.Combine(session, "transport.json"), result);
                        return result;
                    }
                }
                await delay(TimeSpan.FromMilliseconds(200));
            }
            throw new DailyStepException("pending", "No command receipt; evidence saved, no automatic replay", true);
        }
        catch (DailyStepException error)
        {
            error.Command = command.DeepClone().AsObject();
            if (error.Kind != "rejected")
                DailyJson.Write(Path.Combine(session, "result.json"), new
                {
                    state = "unknown_timeout",
                    receipt,
                    error = error.Message,
                    engine = "dotnet-driver-v1"
                });
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            DailyJson.Write(Path.Combine(session, "result.json"), new
            {
                state = "unknown_transport",
                receipt,
                error = error.Message,
                engine = "dotnet-driver-v1"
            });
            throw new DailyStepException("pending", "Command outcome is uncertain; no replay: " + error.Message, true) { Command = command.DeepClone().AsObject() };
        }
    }
    public void Dispose()
    {
        if (closed)
            return;
        try
        {
            if (owner.Held)
            {
                try
                {
                    if (demand?["Id"] is JsonValue id)
                        ReleaseDemand(id.GetValue<string>());
                    mailbox.Write("live", "pause", []);
                }
                catch (Exception e) when (e is IOException or StageHostException) { }
            }
        }
        finally { closed = true; owner.Dispose(); }
    }
}





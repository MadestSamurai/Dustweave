using System.Diagnostics;
using System.Text.Json.Nodes;
namespace Dustweave;

public sealed record DailyBusinessProof(string Role, string Stage, string[] Prefixes, Func<JsonObject, JsonArray, JsonObject, JsonObject> Verify, string[]? NativeRoles = null, string[]? AffectedStages = null, Func<JsonObject, DailyStageFrame, bool>? PreviewOwner = null, Func<JsonObject, DailyStageFrame, bool>? CanResume = null, Func<JsonObject, string[]>? ScopePrefixes = null, Func<JsonObject, string[]>? ScopeRoles = null, Func<JsonObject, string[]>? ScopeStages = null)
{
    public string[] EventRoles => NativeRoles ?? [Role];
    public string[] Stages => AffectedStages ?? [Stage];
    // Preserve the plugin API's object overloads. Only scope-dependent handlers
    // require an object; unrelated legacy roles legitimately used scalar scopes.
    public string[] StagesFor(JsonObject scope) => StagesFor((JsonNode?)scope);
    public string[] PrefixesFor(JsonObject scope) => PrefixesFor((JsonNode?)scope);
    public string[] RolesFor(JsonObject scope) => RolesFor((JsonNode?)scope);
    public string[] StagesFor(JsonNode? scope) => ScopeStages == null ? Stages : ScopeStages(ObjectScope(scope));
    public string[] PrefixesFor(JsonNode? scope) => ScopePrefixes == null ? Prefixes : ScopePrefixes(ObjectScope(scope));
    public string[] RolesFor(JsonNode? scope) => ScopeRoles == null ? EventRoles : ScopeRoles(ObjectScope(scope));
    private JsonObject ObjectScope(JsonNode? scope) => scope as JsonObject
        ?? throw new InvalidDataException("Business scope must be an object for role: " + Role);
}

/// <summary>Managed transactions share one command driver and a durable, account-scoped operation journal.</summary>
public sealed partial class DailyManagedBusiness : IDisposable
{
    internal Action<JsonObject>? WorkProgress { get; set; }
    internal event Action<JsonObject, JsonArray, JsonObject>? WorkObserved;
    private readonly Dictionary<string, string> recordPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> currentOperations = new(StringComparer.Ordinal);
    private readonly string root; private readonly DailyCommandDriver driver; private readonly Func<bool> stopped;
    private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay; private readonly Dictionary<string, DailyBusinessProof> proofs;
    private static readonly HashSet<string> PendingStates = ["prepared", "previewing", "preview_ready", "unknown_preview", "dispatching", "unknown"];
    public DailyManagedBusiness(string root, DailyCommandDriver driver, IEnumerable<DailyBusinessProof> proofs, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null)
    {
        this.root = root;
        this.driver = driver;
        this.stopped = stopped;
        this.proofs = proofs.ToDictionary(p => p.Role, StringComparer.Ordinal);
        this.clock = clock ?? (() => driver.MonotonicTime);
        this.delay = delay ?? driver.DelayAsync;
    }
    public static bool Pending(JsonObject op) => PendingStates.Contains(op["state"]?.GetValue<string>() ?? "");
    internal void BeginAttempt() => currentOperations.Clear();
    // Preserve the public record API used by extensions. Routing happens before loading evidence.
    public IEnumerable<JsonObject> Records(JsonObject context, string? role = null, bool includeLegacy = true) => ReadRecords(context, role, includeLegacy, null);
    // Predicates may use only fields retained by Summary; evidence checks still use the fresh full record.
    internal IEnumerable<JsonObject> MatchingRecords(JsonObject context, Func<JsonObject, bool> metadata, bool includeLegacy = true) => ReadRecords(context, null, includeLegacy, metadata);
    internal IEnumerable<JsonObject> MatchingRecords(JsonObject context, string role, Func<JsonObject, bool> metadata, bool includeLegacy = true) => ReadRecords(context, role, includeLegacy, metadata);
    private IEnumerable<JsonObject> ReadRecords(JsonObject context, string? role, bool includeLegacy, Func<JsonObject, bool>? metadata)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in JournalEntries(role, includeLegacy))
        {
            string file = entry.Key;
            var header = entry.Value.Summary;
            string name = header["role"]?.GetValue<string>() ?? "";
            if (!proofs.ContainsKey(name) || role != null && name != role) continue;
            DailyManagedReconciliation.ValidateRecord(file, header);
            string id = header["id"]!.GetValue<string>();
            if (seen.TryGetValue(id, out var prior))
            {
                // Compare full bodies even for excluded accounts or matching compact headers.
                CheckJournalDuplicate(id, prior, file);
                continue;
            }
            seen.Add(id, file);
            recordPaths[id] = file;
            if (JsonNode.DeepEquals(header["account"], context["actor"]![3]) && JsonNode.DeepEquals(header["player"], context["actor"]![4]) && JsonNode.DeepEquals(header["server"], context["server"])
                && (metadata == null || metadata(header)))
                yield return ReadJournalRecord(file);
        }
    }
    public void RequireResolved(JsonObject context, string role) => RequireResolved(context, role, null);
    private void RequireResolved(JsonObject context, string role, JsonObject? scope)
    {
        if (!DailyManagementProof.IsFreeClaim(role) && HasUnresolvedRecord(context, role, scope))
            throw new StageHostException("pending", "本次操作尚未结束，暂缓该环节；再次运行会按游戏当前状态重新检查。");
    }
    public void Save(JsonObject op)
    {
        string id = op["id"]!.GetValue<string>(), path = recordPaths.GetValueOrDefault(id) ?? Path.Combine(root, "live", "managed-business", id + ".json");
        DailyManagedReconciliation.ValidateRecord(path, op);
        if (!proofs.ContainsKey(op["role"]!.GetValue<string>()))
            throw new InvalidDataException("Unknown managed business role");
        journalHeaders.Remove(path);
        DailyJson.Write(path, op);
        RememberHeader(path, op);
        try { DailyAbsorptionLog.Record(root, op); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { driver.Diagnostics.Event("absorption_report_unavailable", new JsonObject { ["message"] = error.Message }); }
    }
    public JsonObject Create(JsonObject context, string role, JsonObject before, JsonObject scope, JsonObject action)
    {
        if (!DailyTradeJournal.IsTrade(role, scope)) RequireResolved(context, role, scope);
        if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), FrameActor(context)))
            throw new StageHostException("identity", "业务证据与队列身份不一致。");
        if (!DailyManagementProof.IsFreeClaim(role) && (before["Taps"] is not JsonArray taps || proofs[role].RolesFor(scope).Any(required => !taps.Any(t => t?.GetValue<string>() == required))))
            throw new StageHostException("adapter", "Required native response observer unavailable: " + role);
        var op = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["role"] = role, ["stage"] = proofs[role].Stage, ["engine"] = "dotnet-business-v1", ["proof_version"] = 1, ["scope"] = scope.DeepClone(), ["before"] = before.DeepClone(), ["action"] = action.DeepClone(), ["roles"] = new JsonArray(proofs[role].RolesFor(scope).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()), ["account"] = context["actor"]![3]!.DeepClone(), ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = context["server"]!.DeepClone(), ["cycle"] = context["cycle"]!.DeepClone(), ["state"] = "prepared", ["at"] = driver.UtcTicks };
        Save(op);
        currentOperations.Add(op["id"]!.GetValue<string>());
        return op;
    }
    // A journal describes the previous attempt, not a permanent authorization lock.
    // Only enter this boundary after normal navigation has established a usable
    // page. The adapter then reads current inventory/targets/budgets and builds a
    // NEW plan; it must never dispatch the saved action or assert its success.
    internal async Task ReplanInterruptedAsync(JsonObject context, string stage)
    {
        Stop();
        var current = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(current.Context, context))
            throw new StageHostException("identity", "恢复任务时账号或游戏连接发生变化。");
        if (DailyHomeDecision.Transitioning(current.Frame) ||
            DailyNavigationDecision.Phase(current.Frame, stage) is not ("page" or "owned_lobby")) return;
        var policy = DailyNavigationPolicy.Load();
        if (DailyNavigationDecision.Rows(current.Frame).Any(r => r["Popup"]?.GetValue<bool>() == true &&
            !DailyNavigationDecision.PassiveSurface(r["Type"]?.GetValue<string>() ?? ""))) return;
        if (!DailyNavigationDecision.Rows(current.Frame).Any(r => DailyNavigationDecision.ReadyInput(r, false) &&
            DailyNavigationDecision.Blockers(current.Frame, r["Type"]?.GetValue<string>() ?? "", policy).Length == 0)) return;
        foreach (var op in MatchingRecords(context, Pending).ToArray())
        {
            string role = op["role"]!.GetValue<string>();
            if (currentOperations.Contains(op["id"]!.GetValue<string>()) ||
                !proofs[role].StagesFor(op["scope"]).Any(s => s == stage || s == "*")) continue;
            // These adapters have stronger current-state recovery: fresh server
            // map targets, or current free tickets after exiting a battle.
            if (role == "mirror.end" || DailyCollectionProgress.RecoveryLocation(op) != null) continue;
            if (proofs[role].PreviewOwner?.Invoke(op, current) == true) continue;
            op["state"] = "superseded";
            op["reconciliation"] = new JsonObject { ["method"] = "replan_from_current_stage", ["stage"] = stage,
                ["at"] = driver.UtcTicks, ["actions"] = 0, ["original_success_claimed"] = false,
                ["saved_action_replayed"] = false, ["scene"] = current.Frame["Scene"]?.DeepClone() };
            Save(op);
            driver.Diagnostics.Event("interrupted_operation_replanned", new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = role, ["stage"] = stage });
        }
    }
    private static JsonObject FrameActor(JsonObject context) => new() { ["ProcessId"] = context["actor"]![0]!.DeepClone(), ["ProcessStartTicks"] = context["actor"]![1]!.DeepClone(), ["Instance"] = context["actor"]![2]!.DeepClone(), ["AccountKey"] = context["actor"]![3]!.DeepClone(), ["PlayerKey"] = context["actor"]![4]!.DeepClone() };
    private void Stop()
    {
        if (stopped())
            throw new StageHostException("stopped", "日常业务已停止，原记录保留。");
    }
    public JsonArray Events(JsonObject op)
    {
        string role = op["role"]!.GetValue<string>();
        var bySequence = new Dictionary<long, JsonObject>();
        var eventRoles = proofs[role].RolesFor(op["scope"]).ToHashSet(StringComparer.Ordinal);
        var native = eventRoles.SelectMany(r => driver.CollectEvents(r, DailyEvidence.Integer(op["at"]), op["confirmed_at"] == null ? null : DailyEvidence.Integer(op["confirmed_at"])));
        foreach (var node in (op["events"] as JsonArray ?? new JsonArray()).Concat(native))
        {
            var evt = node!.AsObject();
            if (eventRoles.Contains(evt["Role"]?.GetValue<string>() ?? "") && DailyEvidence.SameActor(evt["Frame"]!.AsObject(), op["before"]!["Frame"]!.AsObject()))
            {
                long sequence = DailyEvidence.Integer(evt["Sequence"]);
                if (bySequence.TryGetValue(sequence, out var prior) && !JsonNode.DeepEquals(prior, evt))
                    throw new InvalidDataException("Conflicting native event sequence");
                bySequence[sequence] = evt;
            }
        }
        return new JsonArray(bySequence.OrderBy(p => p.Key).Select(p => (JsonNode)p.Value.DeepClone()).ToArray());
    }
    public JsonObject Verify(JsonObject op, JsonArray events, JsonObject after) => proofs[op["role"]!.GetValue<string>()].Verify(op, events, after);
    public async Task PreviewAsync(JsonObject op, JsonObject context, JsonObject action)
    {
        if (op["state"]?.GetValue<string>() != "prepared")
            throw new StageHostException("pending", "原预览已经派发，不重复打开。");
        try
        {
            Stop();
            if (!JsonNode.DeepEquals((await driver.ObserveAsync()).Context, context))
                throw new StageHostException("identity", "预览前队列身份改变。");
            var input = action.DeepClone().AsObject();
            input["reason"] = "preview-business:" + op["id"]!.GetValue<string>() + "|" + (input["reason"]?.GetValue<string>() ?? "原生免费预览");
            op["preview_action"] = input.DeepClone();
            op["state"] = "previewing";
            Save(op);
            if (input["expect"]?.GetValue<string>() is not { Length: > 0 } destination || destination != op["action"]?["ui"]?.GetValue<string>())
                throw new StageHostException("protocol", "预览必须等待本次确认所用的原生窗口。");
            var sent = await driver.SendPreviewObservedAsync(input);
            if (sent["opening_frame"] is JsonObject opening) op["preview_opening_frame"] = opening.DeepClone();
            op["preview_frame"] = sent["after"]!.DeepClone();
            op["preview_command_id"] = sent["id"]!.DeepClone();
            op["state"] = "preview_ready";
            Save(op);
        }
        catch (Exception error)
        {
            if (error is DailyStepException { Command: not null } step)
                op["preview_command_id"] = step.Command["Id"]!.DeepClone();
            op["state"] = op["state"]?.GetValue<string>() == "prepared" || error is DailyStepException { Kind: "rejected" } ? "rejected" : "unknown_preview";
            op["error"] = error.Message;
            Save(op);
            if (op["state"]!.GetValue<string>() == "unknown_preview")
                throw new StageHostException("pending", error.Message);
            throw;
        }
    }
    public async Task<JsonObject> CommitAsync(JsonObject op, JsonObject context, double seconds = 60, Func<Task>? drive = null, Func<Task>? heartbeat = null)
    {
        WorkProgress?.Invoke(DailyData.O(("step", DailyWorkProgress.Operation(DailyData.S(op["role"]), op["scope"] as JsonObject ?? new()))));
        if (DailyManagementProof.IsFreeClaim(op["role"]!.GetValue<string>()))
            return await CommitFreeClaimAsync(op, context);
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 1800)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        string role = op["role"]!.GetValue<string>();
        if (op["state"]?.GetValue<string>() is not ("prepared" or "preview_ready"))
            throw new StageHostException("pending", "原业务已经提交，不重复提交。");
        try
        {
            Stop();
            var observed = await driver.ObserveAsync();
            if (!JsonNode.DeepEquals(observed.Context, context) || !DailyEvidence.SameActor(observed.Frame, op["before"]!["Frame"]!.AsObject()))
                throw new StageHostException("identity", "业务提交前身份改变。");
            if (op["state"]?.GetValue<string>() == "preview_ready" && proofs[role].PreviewOwner?.Invoke(op, observed) != true)
                throw new StageHostException("pending", "原确认弹窗已改变，未提交消耗。");
            // Persist intent before any consuming command; every uncertain exception remains pending.
            op["at"] = driver.UtcTicks;
            op["state"] = "dispatching";
            Save(op);
            var action = op["action"]!.DeepClone().AsObject();
            action["reason"] = "business:" + op["id"]!.GetValue<string>() + "|" + (action["reason"]?.GetValue<string>() ?? role);
            try
            {
                var sent = await driver.SendObservedAsync(action, op["preview_frame"] as JsonObject);
                op["command_id"] = sent["id"]!.DeepClone();
            }
            catch (DailyStepException e) when (e.Kind == "rejected")
            {
                op["state"] = "rejected";
                if (e.DefinitelyNotDispatched) op["confirmation_not_submitted"] = true;
                if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone();
                throw;
            }
            catch (DailyStepException e) { op["ui_error"] = e.Message; if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone(); }
            Save(op);
            if (drive != null)
                await drive();
            double end = clock() + seconds;
            string last = "No native response";
            while (clock() < end)
            {
                Stop();
                if (heartbeat != null)
                    await heartbeat();
                var events = Events(op);
                var after = await driver.EvidenceAsync(proofs[role].PrefixesFor(op["scope"]));
                op["events"] = events.DeepClone();
                op["last_observation"] = after.DeepClone();
                WorkObserved?.Invoke(op, events, after);
                if (DailyPickupRecovery.Rejection(op, events) is { } pickupRejection)
                {
                    op["state"] = "server_rejected";
                    op["rejection"] = pickupRejection;
                    op["confirmed_at"] = driver.UtcTicks;
                    Save(op);
                    throw new DailyPickupRejectedException(DailyPickupRecovery.TalentInactive(after));
                }
                try
                {
                    if (role is "missions.clear" or "pass.claim_all" or "event_rewards.1")
                    {
                        var rejection = DailyHomeProof.ResetRejection(op["before"]!.AsObject(), events);
                        if (rejection != null)
                        {
                            op["state"] = "server_rejected";
                            op["rejection"] = rejection;
                            Save(op);
                            throw new DailyMissionResetException(rejection);
                        }
                    }
                    var result = Verify(op, events, after);
                    op["result"] = result;
                    op["events"] = events.DeepClone();
                    op["after"] = after.DeepClone();
                    op["state"] = "completed";
                    op["confirmed_at"] = driver.UtcTicks;
                    op.Remove("verification_wait");
                    Save(op);
                    return op;
                }
                catch (InvalidDataException e) { last = e.Message; op["verification_wait"] = last; Save(op); }
                await delay(TimeSpan.FromMilliseconds(250));
            }
            throw new StageHostException("pending", last);
        }
        catch (Exception error)
        {
            if (op["state"]?.GetValue<string>() is not ("completed" or "rejected" or "server_rejected"))
                op["state"] = op["state"]?.GetValue<string>() == "preview_ready" ? "preview_ready" : op["state"]?.GetValue<string>() == "prepared" ? "rejected" : "unknown";
            op["error"] = error.Message;
            if (error is DailyStepException { Kind: "rejected", MayHaveDispatched: false } && op["command_id"] == null)
                op["confirmation_not_submitted"] = true;
            Save(op);
            if (op["confirmation_not_submitted"]?.GetValue<bool>() == true && !DailyTradePreview.IsConfirmation(op["action"]!.AsObject()))
                await CancelRejectedPreviewAsync(op, context);
            if (op["state"]?.GetValue<string>() == "unknown")
                throw new StageHostException("pending", error.Message);
            throw;
        }
    }
    // Close only a proven, unchanged, unsubmitted preview through its Cancel button.
    // This is cleanup, never evidence that the business succeeded or may be replayed.
    internal async Task CleanupRejectedPreviewsAsync(JsonObject context)
    {
        var frame = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(frame.Context, context)) throw new StageHostException("identity", "弹窗收尾期间队列身份改变。");
        if (!DailyNavigationDecision.Rows(frame.Frame).Any(r => r["Popup"]?.GetValue<bool>() == true && !DailyNavigationDecision.PassiveSurface(r["Type"]?.GetValue<string>() ?? ""))) return;
        foreach (var op in MatchingRecords(context, h => h["state"]?.GetValue<string>() == "rejected" && h["confirmation_not_submitted"]?.GetValue<bool>() == true && h["preview_cleanup"] == null, includeLegacy: false))
            if (await CancelRejectedPreviewAsync(op, context)) return;
    }
    private async Task<bool> CancelRejectedPreviewAsync(JsonObject op, JsonObject context)
    {
        if (stopped() || op["state"]?.GetValue<string>() != "rejected" || op["confirmation_not_submitted"]?.GetValue<bool>() != true
            || op["command_id"] != null || op["preview_cleanup"] != null || op["preview_frame"] is not JsonObject saved
            || !JsonNode.DeepEquals(op["cycle"], context["cycle"]) || !JsonNode.DeepEquals(op["server"], context["server"])) return false;
        try
        {
            var current = await driver.ObserveAsync();
            if (!JsonNode.DeepEquals(current.Context, context) || !DailyEvidence.SameActor(saved, current.Frame)
                || !JsonNode.DeepEquals(saved["Scene"], current.Frame["Scene"]) || Events(op).Count != 0) return false;
            string ui = op["action"]?["ui"]?.GetValue<string>() ?? "";
            var before = DailyNavigationDecision.Rows(saved).Where(r => r["Type"]?.GetValue<string>() == ui).ToArray();
            var after = DailyNavigationDecision.Rows(current.Frame).Where(r => r["Type"]?.GetValue<string>() == ui).ToArray();
            if (before.Length != 1 || after.Length != 1 || before[0]["Popup"]?.GetValue<bool>() != true
                || !JsonNode.DeepEquals(before[0], after[0]) || !DailyNavigationDecision.ReadyInput(after[0], false)
                || DailyNavigationDecision.Blockers(current.Frame, ui, DailyNavigationPolicy.Load()).Length != 0) return false;
            var cancel = (after[0]["Targets"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(t => t["Field"]?.GetValue<string>() == "_buttonCancel" && t["Enabled"]?.GetValue<bool>() == true && t["Route"]?.GetValue<string>() == "ui").ToArray();
            if (cancel.Length != 1) return false;
            var cleanup = new JsonObject { ["state"] = "prepared", ["at"] = driver.UtcTicks, ["business_replayed"] = false };
            op["preview_cleanup"] = cleanup;
            Save(op); // Durable, one-attempt cleanup; uncertain cancellation is never blindly repeated.
            var result = await driver.SendObservedAsync(new() { ["ui"] = ui, ["field"] = "_buttonCancel", ["target_id"] = cancel[0]["Id"]!.DeepClone(),
                ["absent"] = ui, ["reason"] = "取消本次未提交的确认，继续后续任务" }, current.Frame);
            cleanup["state"] = "closed";
            cleanup["command_id"] = result["id"]?.DeepClone();
            Save(op);
            driver.Diagnostics.Event("preview_cancelled", new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = op["role"]!.DeepClone(), ["business_replayed"] = false });
            return true;
        }
        catch (Exception error)
        {
            if (op["preview_cleanup"] is JsonObject cleanup)
            {
                cleanup["state"] = error is DailyStepException { Kind: "rejected", Submitted: false, Command: null } ? "rejected" : "unknown";
                cleanup["error"] = error.Message;
                if (error is DailyStepException step) cleanup["command_id"] = step.Command?["Id"]?.DeepClone();
                Save(op);
            }
            driver.Diagnostics.Event("preview_cleanup_blocked", new JsonObject { ["id"] = op["id"]!.DeepClone(), ["error"] = error.Message });
            return false;
        }
    }
    private async Task<JsonObject> CommitFreeClaimAsync(JsonObject op, JsonObject context)
    {
        string role = op["role"]!.GetValue<string>();
        if (op["state"]?.GetValue<string>() != "prepared")
            throw new StageHostException("pending", "此领取指令已派发，请重新读取游戏可领取状态。");
        Stop();
        var observed = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(observed.Context, context) || !DailyEvidence.SameActor(observed.Frame, op["before"]!["Frame"]!.AsObject()))
            throw new StageHostException("identity", "零成本领取前账号或场景身份改变。");
        op["state"] = "dispatching"; op["at"] = driver.UtcTicks; Save(op);
        try
        {
            var sent = await driver.SendObservedAsync(op["action"]!.AsObject());
            op["command_id"] = sent["id"]!.DeepClone();
        }
        catch (DailyStepException e)
        {
            if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone();
            op["ui_error"] = e.Message; Save(op);
            if (e.Kind == "rejected") { op["state"] = "rejected"; Save(op); throw; }
        }
        double end = clock() + 8;
        while (clock() < end)
        {
            Stop();
            var current = await driver.ObserveAsync();
            if (!JsonNode.DeepEquals(current.Context, context)) throw new StageHostException("identity", "领取期间账号或周期改变。");
            var after = await driver.EvidenceAsync(proofs[role].PrefixesFor(op["scope"]));
            if (!DailyEvidence.SameActor(after["Frame"]!.AsObject(), op["before"]!["Frame"]!.AsObject())) throw new StageHostException("identity", "领取观察身份改变。");
            var events = Events(op); op["events"] = events.DeepClone(); op["last_observation"] = after.DeepClone();
            try
            {
                op["result"] = Verify(op, events, after);
                op["confirmation"] = "native_response";
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
            {
                op["receipt_diagnostic"] = e.Message;
                var requested = role == DailyManagementProof.Role ? DailyManagementProof.Eligible(op["before"]!.AsObject()) : new[] { role };
                bool settled = requested.Length > 0 && requested.All(r =>
                {
                    var ui = DailyManagementProof.Categories.Single(c => c.Role == r).Ui;
                    return DailyEvidence.Reading(after, ui, "IsDisable()")?.GetValue<bool>() == false
                        && DailyEvidence.Reading(after, ui, "IsCanSettlement()")?.GetValue<bool>() == false;
                });
                if (!settled) { Save(op); await delay(TimeSpan.FromMilliseconds(200)); continue; }
                op["confirmation"] = "native_claim_state";
                op["result"] = new JsonObject { ["outcome"] = "no_longer_claimable", ["receipt_verified"] = false, ["cost"] = 0 };
            }
            op["state"] = "completed"; op["after"] = after.DeepClone(); op["confirmed_at"] = driver.UtcTicks; Save(op);
            return op["result"]!.AsObject();
        }
        op["state"] = "unconfirmed_free_claim";
        op["result"] = new JsonObject { ["state"] = "partial", ["reason"] = "free_claim_still_pending", ["cost"] = 0 };
        Save(op); return op["result"]!.AsObject();
    }
    public async Task<JsonObject> ReconcileAsync(JsonObject context)
    {
        Stop();
        var current = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(current.Context, context))
            throw new StageHostException("identity", "核对现场与原队列身份不一致。");
        var completed = new JsonArray();
        var unresolved = new JsonArray();
        var report = new JsonObject { ["completed"] = completed, ["unresolved"] = unresolved, ["actions"] = 0, ["engine"] = "dotnet-business-v1" };
        foreach (var op in MatchingRecords(context, Pending))
        {
            Stop();
            string role = op["role"]!.GetValue<string>();
            if (DailyTradeJournal.IsTrade(op))
            {
                if (report["trade_diagnostics"] is not JsonArray diagnostics) report["trade_diagnostics"] = diagnostics = new JsonArray();
                diagnostics.Add(new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = role, ["reason"] = "按游戏当前库存、供货与价格重新规划；旧回执只作诊断，不重放或宣称已成交。" });
                continue;
            }
            var entry = new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = role, ["stages"] = new JsonArray(proofs[role].StagesFor(op["scope"]).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()), ["recover_in_stage"] = false };
            if (DailyFieldTalentProof.Owns(op)) entry["talent_kind"] = op["scope"]!["kind"]!.DeepClone();
            entry["started_at"] = op["at"]?.DeepClone();
            if (op["state"]!.GetValue<string>() == "prepared")
            {
                op["state"] = "rejected";
                op["reconciliation"] = new JsonObject { ["method"] = "never_dispatched", ["actions"] = 0, ["at"] = driver.UtcTicks };
                Save(op);
                completed.Add(entry);
                continue;
            }
            if (op["state"]!.GetValue<string>() is "previewing" or "preview_ready" or "unknown_preview")
            {
                if (role == DailyFreeDrawProof.Role && DailyFreeDrawStage.AbandonedPreview(op, current, Events(op)))
                {
                    op["state"] = "superseded";
                    op["reconciliation"] = new JsonObject { ["method"] = "unsubmitted_free_preview_closed", ["at"] = driver.UtcTicks,
                        ["actions"] = 0, ["draw_completed"] = false, ["recheck_current_free_draws"] = true };
                    op.Remove("error");
                    Save(op);
                    entry["reason"] = "未提交的免费预览已关闭，按游戏当前免费次数重新检查。";
                    completed.Add(entry);
                    continue;
                }
                entry["reason"] = "免费预览尚未提交，需核对原弹窗所有权";
                entry["recover_in_stage"] = op["state"]!.GetValue<string>() == "preview_ready" && proofs[role].PreviewOwner?.Invoke(op, current) == true;
                unresolved.Add(entry);
                continue;
            }
            var events = Events(op);
            if (DailyFieldTalentProof.ConfirmedCountedReceipt(op, events) is { } confirmedTalent)
            {
                op["state"] = "completed";
                op["result"] = confirmedTalent;
                op["events"] = events.DeepClone();
                op["reconciliation"] = new JsonObject { ["method"] = "original_counted_talent_response", ["actions"] = 0,
                    ["at"] = driver.UtcTicks, ["recheck_current_targets"] = true };
                op.Remove("error"); op.Remove("recovery_error"); Save(op);
                entry["reason"] = "原天赋使用已由服务器确认，按当前地图剩余目标继续。";
                completed.Add(entry);
                continue;
            }
            string walkId = op["command_id"]?.GetValue<string>() ?? "";
            var walkReceipt = role == "mainline.pickup" && Guid.TryParseExact(walkId, "N", out _)
                ? DailyJson.TryRead<JsonObject>(Path.Combine(root, "live", "steps", walkId, "result.json"))?["receipt"] as JsonObject : null;
            if (DailyPickupRecovery.CanRetireUnstartedWalk(op, events, walkReceipt, current))
            {
                op["state"] = "superseded";
                op["reconciliation"] = new JsonObject { ["method"] = "original_walk_path_rejected_before_movement", ["actions"] = 0,
                    ["target_collected"] = false, ["recheck_current_targets"] = true, ["at"] = driver.UtcTicks };
                op.Remove("error"); op.Remove("recovery_error"); Save(op);
                entry["reason"] = "原移动在路径检查阶段已拒绝；保留未收集目标，按当前可达范围继续。";
                completed.Add(entry);
                continue;
            }
            if (DailyPickupRecovery.Rejection(op, events) is { } pickupRejection)
            {
                op["state"] = "server_rejected";
                op["rejection"] = pickupRejection;
                op["events"] = events.DeepClone();
                op["confirmed_at"] = driver.UtcTicks;
                op["reconciliation"] = new JsonObject { ["method"] = "explicit_pickup_rejection", ["actions"] = 0, ["at"] = driver.UtcTicks };
                Save(op);
                entry["reason"] = "原拾取已被游戏明确拒绝；保留失败记录，按当前地图重新检查。";
                completed.Add(entry);
                continue;
            }
            if (role == "mirror.end" && DailyMirror.CanRetireHistorical(op, current))
            {
                try
                {
                    var fresh = await driver.EvidenceAsync(["mirror"]);
                    if (DailyMirror.CanRetireHistorical(op, new DailyStageFrame(fresh["Frame"]!.AsObject(), context)))
                    {
                        var tickets = DailyMirror.Tickets(fresh);
                        DailyMirror.Debit(tickets, tickets, 0);
                        op["state"] = "superseded";
                        op["reconciliation"] = new JsonObject { ["method"] = "current_idle_mirror_budget", ["actions"] = 0,
                            ["at"] = driver.UtcTicks, ["battle_completed"] = false, ["current_tickets"] = tickets.DeepClone() };
                        Save(op);
                        entry["reason"] = "中断批次保留为诊断；当前已退出战斗，按现有免费次数重新规划。";
                        completed.Add(entry);
                        continue;
                    }
                }
                catch (InvalidDataException e) { op["historical_recovery_wait"] = e.Message; }
            }
            string last = "Original business proof unavailable";
            bool ok = false;
            bool Try(JsonObject candidate)
            {
                try
                {
                    var result = Verify(op, events, candidate);
                    op["state"] = "completed";
                    op["result"] = result;
                    op["events"] = events.DeepClone();
                    op["after"] = candidate.DeepClone();
                    op.Remove("error");
                    op["reconciliation"] = new JsonObject { ["method"] = "original_proof_and_native_evidence", ["actions"] = 0, ["at"] = driver.UtcTicks };
                    Save(op);
                    return true;
                }
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or NullReferenceException)
                {
                    // An incomplete old proof belongs to its owning stage. It must
                    // not crash queue preflight before current-state recovery runs.
                    // Preserve the failure; never convert it into confirmed success.
                    last = e is NullReferenceException
                        ? "旧操作回执字段不完整，结果尚未确认；对应环节将按游戏当前状态重新检查。"
                        : e.Message;
                    if (e is NullReferenceException) op["recovery_error_detail"] = e.ToString();
                    return false;
                }
            }
            foreach (string key in new[] { "after", "last_observation" })
                if (op[key] is JsonObject saved && Try(saved))
                {
                    ok = true;
                    break;
                }
            if (!ok && JsonNode.DeepEquals(op["cycle"], context["cycle"]) && DailyEvidence.SameActor(current.Frame, op["before"]!["Frame"]!.AsObject()))
            {
                try
                {
                    ok = Try(await driver.EvidenceAsync(proofs[role].PrefixesFor(op["scope"])));
                }
                catch (Exception e) when (e is StageHostException or DailyStepException or IOException) { last = e.Message; }
            }
            if (ok)
                completed.Add(entry);
            else if (DailyPickupRecovery.CanRetireWalk(op, events, current))
            {
                // Walking is not a resource purchase. Incidental pickups can produce
                // fully settled replies before reaching the selected target. Retire
                // the interrupted navigation; never mark that target collected.
                op["state"] = "superseded";
                op["events"] = events.DeepClone();
                op["reconciliation"] = new JsonObject { ["method"] = "settled_walk_reselect_native_targets", ["actions"] = 0,
                    ["at"] = driver.UtcTicks, ["target_collected"] = false, ["recheck_current_targets"] = true };
                op.Remove("error");
                op.Remove("recovery_error");
                Save(op);
                entry["reason"] = "原步行拾取回执已结束；按游戏当前未收集目标继续，不将原目标记为完成。";
                completed.Add(entry);
            }
            else
            {
                op["events"] = events.DeepClone();
                op["recovery_error"] = last;
                Save(op);
                entry["reason"] = last;
                entry["recover_in_stage"] = proofs[role].CanResume?.Invoke(op, current) == true;
                if (DailyCollectionProgress.RecoveryLocation(op) is { } location)
                {
                    entry["pack"] = location.Pack; entry["map"] = location.Map;
                    entry["reason"] = $"将自动读取卡带 {location.Pack} · 地图 {location.Map} 的当前收集状态，按剩余目标继续。";
                }
                if (DailyManagementProof.IsFreeClaim(role))
                {
                    entry["reason"] = "零成本领取回执仅用于诊断；本环节按游戏当前可领取状态继续。";
                    if (report["free_claim_diagnostics"] is not JsonArray diagnostics) report["free_claim_diagnostics"] = diagnostics = new JsonArray();
                    diagnostics.Add(entry);
                }
                else unresolved.Add(entry);
            }
        }
        return report;
    }
}

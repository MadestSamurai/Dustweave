using System.Diagnostics;
using System.Text.Json.Nodes;
namespace BD2Daily;

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
public sealed class DailyManagedBusiness
{
    private readonly Dictionary<string, string> recordPaths = new(StringComparer.Ordinal);
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
    public IEnumerable<JsonObject> Records(JsonObject context, string? role = null, bool includeLegacy = true)
    {
        var seen = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (string directory in includeLegacy ? new[] { "managed-business", "business" } : new[] { "managed-business" })
        {
            string path = Path.Combine(root, "live", directory);
            if (!Directory.Exists(path))
                continue;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                var op = DailyJson.TryRead<JsonObject>(file) ?? throw new InvalidDataException("Unreadable business record");
                string name = op["role"]?.GetValue<string>() ?? "";
                if (!proofs.ContainsKey(name) || role != null && name != role)
                    continue;
                DailyManagedReconciliation.ValidateRecord(file, op);
                string id = op["id"]!.GetValue<string>();
                if (seen.TryGetValue(id, out var prior))
                {
                    if (!JsonNode.DeepEquals(prior, op))
                        throw new StageHostException("pending", "发现同一操作的冲突记录，未覆盖或重复提交：" + id);
                    continue;
                }
                seen.Add(id, op);
                recordPaths[id] = file;
                if (JsonNode.DeepEquals(op["account"], context["actor"]![3]) && JsonNode.DeepEquals(op["player"], context["actor"]![4]) && JsonNode.DeepEquals(op["server"], context["server"]))
                    yield return op;
            }
        }
    }
    public void RequireResolved(JsonObject context, string role)
    {
        if (!DailyManagementProof.IsFreeClaim(role) && Records(context, role).Any(op => Pending(op) && !DailyTradeJournal.IsTrade(op)))
            throw new StageHostException("pending", "已有未确认的" + proofs[role].Stage + "操作；先核对原回执，不重复提交。");
    }
    public void Save(JsonObject op)
    {
        string id = op["id"]!.GetValue<string>(), path = recordPaths.GetValueOrDefault(id) ?? Path.Combine(root, "live", "managed-business", id + ".json");
        DailyManagedReconciliation.ValidateRecord(path, op);
        if (!proofs.ContainsKey(op["role"]!.GetValue<string>()))
            throw new InvalidDataException("Unknown managed business role");
        DailyJson.Write(path, op);
    }
    public JsonObject Create(JsonObject context, string role, JsonObject before, JsonObject scope, JsonObject action)
    {
        if (!DailyTradeJournal.IsTrade(role, scope)) RequireResolved(context, role);
        if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), FrameActor(context)))
            throw new StageHostException("identity", "业务证据与队列身份不一致。");
        if (!DailyManagementProof.IsFreeClaim(role) && (before["Taps"] is not JsonArray taps || proofs[role].RolesFor(scope).Any(required => !taps.Any(t => t?.GetValue<string>() == required))))
            throw new StageHostException("adapter", "Required native response observer unavailable: " + role);
        var op = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["role"] = role, ["stage"] = proofs[role].Stage, ["engine"] = "dotnet-business-v1", ["proof_version"] = 1, ["scope"] = scope.DeepClone(), ["before"] = before.DeepClone(), ["action"] = action.DeepClone(), ["roles"] = new JsonArray(proofs[role].RolesFor(scope).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()), ["account"] = context["actor"]![3]!.DeepClone(), ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = context["server"]!.DeepClone(), ["cycle"] = context["cycle"]!.DeepClone(), ["state"] = "prepared", ["at"] = driver.UtcTicks };
        Save(op);
        return op;
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
            catch (DailyStepException e) when (e.Kind == "rejected") { op["state"] = "rejected"; throw; }
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
            Save(op);
            if (op["state"]?.GetValue<string>() == "unknown")
                throw new StageHostException("pending", error.Message);
            throw;
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
        foreach (var op in Records(context).Where(Pending))
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
                entry["reason"] = "免费预览尚未提交，需核对原弹窗所有权";
                entry["recover_in_stage"] = op["state"]!.GetValue<string>() == "preview_ready" && proofs[role].PreviewOwner?.Invoke(op, current) == true;
                unresolved.Add(entry);
                continue;
            }
            var events = Events(op);
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
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { last = e.Message; return false; }
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
            else
            {
                op["events"] = events.DeepClone();
                op["recovery_error"] = last;
                Save(op);
                entry["reason"] = last;
                entry["recover_in_stage"] = proofs[role].CanResume?.Invoke(op, current) == true;
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










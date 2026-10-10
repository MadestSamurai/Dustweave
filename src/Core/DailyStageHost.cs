using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>One managed host owns navigation, transaction recovery, selected stages and private extensions.</summary>
public sealed class DailyStageHost : IDailyStageHost, IDailyStageProgressHost, IDailyWeeklyCompletionHost
{
    public event Action<string, JsonObject>? StageProgress;
    private readonly string root;
    private readonly DailyStageObservation observation; private readonly DailyStageNavigation navigation; private readonly DailyCommandDriver driver;
    private readonly DailyManagedBusiness business; private readonly DailyManagedBootstrap bootstrap; private readonly DailyManagedReconciliation reconciliation;
    private readonly DailyWorkflowRegistry workflows; private readonly IReadOnlyDictionary<string, IDailyManagedStage> managed; private readonly DailyTradeData? tradeData; private readonly DailyBusinessProof[] proofs;
    private readonly SemaphoreSlim serial = new(1, 1); private JsonObject? context; private bool closed;
    private JsonObject verifiedWeekly = new();
    public DailyStageHost(string root, DailyStageObservation observation, DailyStageNavigation navigation, DailyCommandDriver driver, DailyManagedBusiness business, DailyManagedBootstrap bootstrap, DailyManagedReconciliation reconciliation, DailyWorkflowRegistry workflows, IReadOnlyDictionary<string, IDailyManagedStage> managed, DailyBusinessProof[] proofs, DailyTradeData? tradeData = null)
    {
        this.root = root;
        this.observation = observation;
        this.navigation = navigation;
        this.driver = driver;
        navigation.DiagnosticObservation = frame => driver.Diagnostics.Observe(frame, "navigation");
        this.business = business;
        this.bootstrap = bootstrap;
        this.reconciliation = reconciliation;
        this.workflows = workflows;
        this.managed = managed;
        this.proofs = proofs;
        this.tradeData = tradeData;
    }
    private void Progress(string stage, JsonObject value) => StageProgress?.Invoke(stage, value);
    private JsonObject Bound() => context ?? throw new StageHostException("protocol", "日常队列尚未绑定身份。");
    private DailyWorkflow Workflow() => workflows.Create(Bound(), Relay);
    public async Task<JsonObject> SettleWeeklyAsync(IReadOnlyList<string> stages, JsonObject expected)
    {
        if (context == null || !JsonNode.DeepEquals(context, expected))
            return new();
        try
        {
            var evidence = await driver.EvidenceAsync(["weekly_npc.native", "mainline.reset"]);
            var result = DailyWeeklyCompletion.Inspect(evidence, stages);
            string week = S(DailyEvidence.Reading(evidence, "mainline.reset", "GetWeeklyResetTime().Ticks"));
            foreach (var part in result) part.Value!["week"] = week;
            foreach (var part in verifiedWeekly)
                if (stages.Contains(part.Key) && S(part.Value?["week"]) == week && result[part.Key] == null) result[part.Key] = Copy(part.Value);
            return result;
        }
        catch (Exception e) when (e is InvalidDataException or StageHostException or DailyStepException or IOException)
        {
            // Historical completion belongs to this bound queue even when stopping
            // prevents a new read. Never reconstruct it from UI progress counters.
            return new JsonObject(verifiedWeekly.Where(p => stages.Contains(p.Key)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Copy(p.Value))));
        }
    }
    private JsonArray UnknownPending()
    {
        var known = proofs.Select(p => p.Role).Concat(reconciliation.Roles).ToHashSet(StringComparer.Ordinal);
        var result = new JsonArray();
        foreach (string directory in new[] { "business", "managed-business" })
        {
            string path = Path.Combine(root, "live", directory);
            if (!Directory.Exists(path))
                continue;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                var op = DailyJson.TryRead<JsonObject>(file) ?? throw new InvalidDataException("未能读取核账记录：" + Path.GetFileName(file));
                DailyManagedReconciliation.ValidateRecord(file, op);
                if (known.Contains(S(op["role"])) || !DailyManagedBusiness.Pending(op) || !JsonNode.DeepEquals(op["account"], Bound()["actor"]![3]) || !JsonNode.DeepEquals(op["player"], Bound()["actor"]![4]) || !JsonNode.DeepEquals(op["server"], Bound()["server"]))
                    continue;
                result.Add(O(("id", op["id"]), ("role", op["role"]), ("stages", new JsonArray("*")), ("recover_in_stage", false), ("reason", "没有已注册的原操作核账规则；保留原记录，未重放请求")));
            }
        }
        return result;
    }
    private async Task<JsonObject> Reconcile()
    {
        var result = await reconciliation.RunAsync(Bound());
        var report = await business.ReconcileAsync(Bound());
        foreach (string key in new[] { "completed", "unresolved" })
            foreach (var row in report[key]!.AsArray())
                result[key]!.AsArray().Add(Copy(row));
        result["business"] = report;
        var talents = await driver.ReconcileFieldTalentsAsync();
        foreach (var row in talents["completed"]!.AsArray())
            result["completed"]!.AsArray().Add(Copy(row));
        result["field_talents"] = talents;
        foreach (var row in UnknownPending())
            result["unresolved"]!.AsArray().Add(Copy(row));
        return result;
    }
    public async Task<JsonObject> CallAsync(string operation, string? stage = null, JsonObject? arguments = null)
    {
        // Resume the workflow from current server state; never replay a consuming command.
        long? lastRefillBalance = null;
        while (true)
        {
            try { return await CallOnceAsync(operation, stage, arguments); }
            catch (StageHostException error) when (error.PotionLimit != null && operation is "execute" or "navigate")
            {
                var limit = error.PotionLimit;
                var settings = new DailyPreferenceStore(root).Read(S(Bound()["actor"]![3])).General;
                void Status(string state, string detail) => Progress(DailyTalentPotions.Stage, O(("state", state), ("detail", detail), ("parent", stage), ("limit", limit.Json())));
                try
                {
                    DailyTalentPotions.PurchaseCount(settings, limit.Available, limit.Required);
                    if (lastRefillBalance.HasValue && limit.Available >= lastRefillBalance.Value)
                        throw new InvalidDataException("补药后同一消耗仍受限，已停止重复购买；请核对补药目标与当前库存。");
                }
                catch (InvalidDataException reason)
                {
                    string message = "天赋药限制：" + limit.Message + "；" + reason.Message;
                    Status("blocked", message);
                    throw new StageHostException("adapter", message);
                }
                Status("running", "天赋药不足，前往广场核对周任务九折报价；补药后接续原任务。");
                try
                {
                    await serial.WaitAsync();
                    try
                    {
                        await business.CleanupRejectedPreviewsAsync(Bound());
                        var recovered = await navigation.RecoverAsync("trade", Bound(), error.Message, Relay);
                        if (!B(recovered["safe"])) throw new StageHostException("adapter", "耗药任务现场尚未关闭，未开始补药。");
                        var refill = await new DailyTradeExecution(Workflow(), new()).RefillPotions(limit.Required, settings);
                        lastRefillBalance = N(refill["balance"]);
                        Status("completed", $"已按九折补充 {N(refill["bought"])} 个天赋药，当前 {N(refill["balance"])}；继续原任务。");
                    }
                    finally { serial.Release(); }
                    // Reopen the owning stage, not the old preview or old action.
                    if (operation == "execute") await CallOnceAsync("navigate", stage);
                }
                catch (Exception failure)
                {
                    Status(failure is StageHostException { Kind: "pending" } or DailyStepException { MayHaveDispatched: true } ? "recovery_required" : "blocked", failure.Message);
                    if (failure is InvalidDataException) throw new StageHostException("adapter", failure.Message);
                    if (failure is DailyStepException step && step.Kind == "rejected" && !step.MayHaveDispatched)
                        throw new StageHostException(driver.PauseRequested ? "stopped" : "adapter", step.Message);
                    throw;
                }
            }
        }
    }
    private async Task<JsonObject> CallOnceAsync(string operation, string? stage = null, JsonObject? arguments = null)
    {
        await serial.WaitAsync();
        using var diagnostic = operation == "observe" ? null : driver.Diagnostics.Scope(operation + ":" + (stage ?? "queue"));
        var oldGuard = driver.SubmissionGuard;
        bool oldTrade = driver.TradeStageActive;
        try
        {
            if (closed)
                throw new StageHostException("transport", "日常执行组件已经关闭。");
            if (operation == "observe")
                return await observation.ObserveAsync();
            if (operation == "begin")
            {
                var expected = arguments?["context"]?.AsObject() ?? throw new StageHostException("protocol", "缺少队列身份");
                var started = await bootstrap.BeginAsync(expected);
                context = expected.DeepClone().AsObject();
                return started;
            }
            Bound();
            if (operation == "reconcile")
                return await Reconcile();
            if (stage == null || !DailyStageCatalog.Adapters.Contains(stage))
                throw new StageHostException("protocol", "未知日常环节。");
            if (stage == "trade" && operation is "navigate" or "execute")
                tradeData?.AssertReady();
            if (stage == "trade" && operation == "execute")
            {
                driver.TradeStageActive = true;
                driver.ConfigureTradeResume(null); // Replan trades from current game state, never replay an old plan.
                driver.SubmissionGuard = () => { try { tradeData?.AssertReady(); } catch (StageHostException e) { throw new DailyStepException("rejected", e.Message); } };
            }
            if (operation == "recover")
            {
                if (!JsonNode.DeepEquals(context, arguments?["context"]))
                    throw new StageHostException("identity", "恢复现场与原队列不同。");
                await business.CleanupRejectedPreviewsAsync(Bound());
                return await navigation.RecoverAsync(stage, Bound(), S(arguments?["error"]), Relay);
            }
            if (operation == "navigate")
            {
                await business.CleanupRejectedPreviewsAsync(Bound());
                return await navigation.EnterAsync(stage, Bound(), Relay, managed.TryGetValue(stage, out var owner) ? owner.CanResume : null);
            }
            if (operation == "execute")
            {
                await workflows.PrepareAsync(stage);
                if (DailyStageCatalog.IsWeeklyRoute(stage))
                    return await ExecuteWeekly(arguments?["stages"] is JsonArray selected ? selected.Select(S).ToArray() : [stage]);
                if (!managed.TryGetValue(stage, out var adapter))
                    throw new StageHostException("protocol", "未注册.NET环节：" + stage);
                var result = await adapter.ExecuteAsync(Bound(), Relay);
                if (stage == "trade" && B(result["daily_potion_budget_reached"]))
                    Progress(DailyTalentPotions.Stage, O(("state", "completed"), ("parent", stage), ("detail", "跑商已用完本轮允许的天赋药预算；保留量和补药设置已生效，需要继续加工可调整整体设置后补跑。")));
                if (stage == "trade" && tradeData != null)
                {
                    result["data_preflight"] = tradeData.Proof.DeepClone();
                    if (driver.TradeResumeProof != null)
                        result["unsubmitted_resume"] = driver.TradeResumeProof.DeepClone();
                }
                return result;
            }
            throw new StageHostException("protocol", "不支持的日常操作：" + operation);
        }
        catch (DailyStepException e) when (e.Kind == "rejected")
        {
            driver.Diagnostics.Failure("failure", O(("kind", e.Kind), ("message", e.Message), ("submitted", e.Submitted), ("may_have_dispatched", e.MayHaveDispatched), ("command", e.Command)));
            // Rejected means no native action dispatched (including an explicit rejected receipt).
            // A missing/changed button is a stage failure, not an uncertain consuming operation.
            // Pending transport and identity failures deliberately still pause the whole queue.
            throw new StageHostException(driver.PauseRequested ? "stopped" : "adapter", e.Message) { PotionLimit = e.PotionLimit };
        }
        catch (InvalidDataException e) { driver.Diagnostics.Failure("failure", O(("kind", "data"), ("message", e.Message))); throw new StageHostException("adapter", e.Message); }
        catch (Exception e) { driver.Diagnostics.Failure("failure", O(("kind", e.GetType().Name), ("message", e.Message))); throw; }
        finally { driver.SubmissionGuard = oldGuard; driver.TradeStageActive = oldTrade; if (stage == "trade" && operation == "execute") driver.ConfigureTradeResume(null); serial.Release(); }
    }
    private async Task<JsonObject> ExecuteWeekly(string[] names)
    {
        verifiedWeekly = new();
        var settled = await SettleWeeklyAsync(names, Bound());
        var remaining = names.Where(n => settled[n] == null).ToArray();
        if (settled["weekly_npc"] is JsonObject npc)
            Progress("weekly_npc", O(("detail", npc["detail"]), ("completed", npc["proof"]!["Completed"]), ("total", npc["proof"]!["Limit"]), ("active", false)));
        JsonObject parts;
        var route = new DailyWeeklyRoute(Workflow(), Progress);
        try
        {
            parts = remaining.Length == 0 ? new() : (await route.Run(remaining))["stages"]!.AsObject();
        }
        catch (Exception error) when (error is StageHostException { Kind: "adapter" } or DailyStepException { Kind: "deferred" })
        {
            var proof = await driver.TakeCollectionDeferralAsync();
            if (proof == null || !remaining.Contains("weekly_mainline"))
                throw;
            var recovery = await navigation.RecoverAsync("guild", Bound(), S(proof["detail"]), Relay);
            if (!B(recovery["safe"]))
                throw new StageHostException("adapter", "采集角色受卡带限制，返回主菜单尚未确认；进度保留。");
            parts = new();
            foreach (string name in remaining)
            {
                parts[name] = O(("state", "partial"), ("reason", name == "weekly_mainline" ? "collection_characters_pack_restricted" : "weekly_route_deferred"), ("detail", name == "weekly_mainline" ? S(proof["detail"]) : "本次周路线已保留，接续时核对剩余进度"), ("waiting_cartridge_access", true), ("proof", proof), ("recovery", recovery));
                Progress(name, O(("detail", parts[name]!["detail"]), ("active", false)));
            }
        }
        finally {
            verifiedWeekly = route.VerifiedResults();
            foreach (var p in settled) verifiedWeekly[p.Key] = Copy(p.Value);
        }
        foreach (var p in verifiedWeekly)
            parts[p.Key] = Copy(p.Value);
        return O(("state", parts.All(p => S(p.Value?["state"]) is "completed" or "skipped") ? "completed" : "partial"), ("stages", parts), ("engine", "dotnet-weekly-v2"));
    }
    private async Task<JsonObject> Relay(string operation, string? stage = null, JsonObject? arguments = null)
    {
        if (operation == "home_recovery")
            return await driver.HomeRecoveryAsync(S(arguments?["kind"]));
        if (operation == "navigation_step")
            return await driver.NavigationAsync(arguments?["action"]?.AsObject() ?? throw new StageHostException("protocol", "Missing navigation action"));
        if (operation == "guild_step" && stage == "guild")
            return await driver.GuildAsync(arguments?["action"]?.AsObject() ?? throw new StageHostException("protocol", "Missing guild action"), S(arguments?["guild_key"]));
        if (operation == "navigate" && stage == "daily_dispatch")
        {
            Require(await DailyTravel.Enter(Workflow(), 2, "square.pack", packType: 11), "没有可领取派遣的普通地图");
            return O(("state", "ready"), ("route", new JsonArray("square_field")));
        }
        if (operation == "execute" && stage != null && managed.TryGetValue(stage, out var adapter))
            return await adapter.ExecuteAsync(Bound(), Relay);
        throw new StageHostException("protocol", "未注册的内部.NET操作：" + operation + "/" + stage);
    }
    public async ValueTask DisposeAsync()
    {
        await serial.WaitAsync();
        try
        {
            closed = true;
        }
        finally { serial.Release(); }
    }
}


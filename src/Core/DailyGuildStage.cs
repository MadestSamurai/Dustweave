using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace BD2Daily;

public interface IDailyManagedStage
{
    bool CanResume(DailyStageFrame frame);
    Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay);
}
/// <summary>Managed guild workflow. Native passive receipts remain the only proof of attendance.</summary>
public sealed class DailyGuildStage : IDailyManagedStage
{
    private readonly string root;
    private readonly Func<Task<DailyStageFrame>> read;
    private readonly Func<DailySnapshot, GuildReceipt?> prior;
    private readonly Func<bool> stopped;
    private readonly Func<long> now;
    private readonly Func<double> clock;
    private readonly Func<TimeSpan, Task> delay;
    private readonly DailyNavigationPolicy policy;
    private readonly JsonObject recipe;
    public DailyGuildStage(string root, Func<Task<DailyStageFrame>> read, Func<bool> stopped,
        Func<DailySnapshot, GuildReceipt?>? prior = null, Func<long>? now = null, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, DailyNavigationPolicy? policy = null, JsonObject? recipe = null)
    {
        this.root = root;
        this.read = read;
        this.stopped = stopped;
        this.prior = prior ?? (s => new GuildStore(root).Prior(s));
        this.now = now ?? (() => DateTime.UtcNow.Ticks);
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.policy = policy ?? DailyNavigationPolicy.Load();
        this.recipe = recipe ?? LoadRecipe();
    }
    public static JsonObject LoadRecipe()
    {
        using var s = typeof(DailyGuildStage).Assembly.GetManifestResourceStream("BD2Daily.guild-flow.json") ?? throw new IOException("缺少公会规则。");
        return JsonNode.Parse(s)!.AsObject();
    }
    private static bool SameScope(GuildReceipt receipt, DailySnapshot daily) => receipt.AccountKey == daily.AccountKey && receipt.PlayerKey == daily.PlayerKey && receipt.ServerKey == daily.Guild.ServerKey && receipt.GuildKey == daily.Guild.GuildKey && receipt.CycleKey == daily.Guild.CycleKey;
    private void Validate(DailyStageFrame observed)
    {
        var daily = observed.Daily;
        var frame = observed.Frame;
        if (daily != null && (daily.FrameUtcTicks > now() || now() - daily.FrameUtcTicks > 3 * TimeSpan.TicksPerSecond))
            throw new StageHostException("identity", "Guild observer frame is stale");
        if (daily == null || daily.Guild == null || !daily.Guild.Supported || !daily.Guild.InGuild)
            throw new StageHostException("adapter", "Guild state unavailable or not a member");
        if (daily.Guild.ServerKey != observed.Context["server"]?.GetValue<string>() || daily.Guild.CycleKey != observed.Context["cycle"]?.GetValue<string>())
            throw new StageHostException("identity", "Guild server or cycle differs from the observed context");
        if (daily.ProcessId != frame["ProcessId"]?.GetValue<int>() || daily.ProcessStartTicks != frame["ProcessStartTicks"]?.GetValue<long>() || daily.AccountKey != frame["AccountKey"]?.GetValue<string>() || daily.PlayerKey != frame["PlayerKey"]?.GetValue<string>())
            throw new StageHostException("identity", "Guild observer identity is stale or changed");
    }
    internal static void ValidateConfirmed(GuildReceipt? receipt)
    {
        if (receipt?.State is not ("completed" or "checked_no_grant" or "confirmed_cleanup_pending"))
            return;
        string outcome = GuildPolicy.Outcome(receipt);
        if (outcome == "unknown" || receipt.State == "checked_no_grant" && outcome != "checked_no_grant" || receipt.State == "completed" && outcome != "completed")
            throw new StageHostException("pending", "公会回执的请求、响应或成员缓存证据不完整；不重复进入。");
    }
    public bool CanResume(DailyStageFrame observed)
    {
        if (observed.Daily?.Guild is not { Supported: true, InGuild: true })
            return false;
        var types = DailyNavigationDecision.Types(observed.Frame);
        if (!types.Overlaps(["GuildUI", "GuildJoinAttendancePopupUI"]))
            return false;
        Validate(observed);
        var receipt = prior(observed.Daily);
        if (receipt != null && (SameScope(receipt, observed.Daily) || receipt.State is "prepared" or "dispatching" or "waiting_response" or "response_received" or "unknown"))
            return true;
        CheckPriorAttempts(observed.Context, observed.Daily, null);
        return false;
    }
    public static JsonObject Decide(JsonObject frame, DailySnapshot daily, GuildReceipt? receipt, JsonObject recipe, DailyNavigationPolicy policy)
    {
        var types = DailyNavigationDecision.Types(frame);
        string outcome = receipt?.State ?? "none";
        if (outcome is "unknown" or "prepared" or "dispatching")
            return new()
            {
                ["pause"] = "Unresolved guild operation: " + outcome
            };
        if (outcome is "waiting_response" or "response_received")
            return new()
            {
                ["wait"] = "Waiting for native guild response"
            };
        if (outcome == "confirmed_cleanup_pending")
            outcome = "completed";
        foreach (var node in recipe["rules"]!.AsArray())
        {
            var rule = node!.AsObject();
            bool Contains(string key) => rule[key]?.AsArray().Any(n => types.Contains(n!.GetValue<string>())) ?? false;
            if (rule["has"]?.AsArray().Any(n => !types.Contains(n!.GetValue<string>())) == true || Contains("lacks"))
                continue;
            if (rule["outcome"] is JsonArray outcomes && !outcomes.Any(n => n!.GetValue<string>() == outcome))
                continue;
            if (rule["attendance_popup"]?.GetValue<bool>() == true && !daily.Guild.AttendancePopup)
                continue;
            string target = rule["action"]?["ui"]?.GetValue<string>() ?? "MenuUI";
            if (DailyNavigationDecision.Blockers(frame, target, policy).Length > 0)
                continue;
            return rule.DeepClone().AsObject();
        }
        return new()
        {
            ["pause"] = "No verified transition for: " + string.Join(", ", types.Order(StringComparer.Ordinal))
        };
    }
    private string DirectoryPath => Path.Combine(root, "live", "managed-guild");
    private void Save(JsonObject record) => DailyJson.Write(Path.Combine(DirectoryPath, record["id"]!.GetValue<string>() + ".json"), record);
    private void CheckPriorAttempts(JsonObject context, DailySnapshot daily, GuildReceipt? receipt)
    {
        if (!Directory.Exists(DirectoryPath))
            return;
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (!GuildStore.ValidId(id))
                throw new InvalidDataException("Invalid managed guild record ID");
            var row = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Managed guild record disappeared");
            if (row["schema"]?.GetValue<int>() != 1 || row["id"]?.GetValue<string>() != id || row["context"] is not JsonObject old || old["actor"] is not JsonArray a || a.Count != 5 || context["actor"] is not JsonArray b || row["entry_status"] is not JsonValue)
                throw new InvalidDataException("Invalid managed guild record");
            bool role = JsonNode.DeepEquals(a[3], b[3]) && JsonNode.DeepEquals(a[4], b[4]) && JsonNode.DeepEquals(old["server"], context["server"]);
            string status = row["entry_status"]!.GetValue<string>();
            if (status is not ("none" or "prepared" or "dispatching" or "unknown" or "rejected" or "confirmed"))
                throw new InvalidDataException("Invalid managed guild entry state");
            if (!role || status is not ("prepared" or "dispatching" or "unknown"))
                continue;
            if (receipt != null && (receipt.State is "completed" or "checked_no_grant" or "confirmed_cleanup_pending") && SameScope(receipt, daily) && JsonNode.DeepEquals(old["cycle"], context["cycle"]) && row["guild_key"]?.GetValue<string>() == daily.Guild.GuildKey)
            {
                row["entry_status"] = "confirmed";
                row["reconciled_guild_receipt"] = receipt.Id;
                Save(row);
                continue;
            }
            throw new StageHostException("pending", "存在未确认的公会进入意图，先核对原回执；不重复进入。");
        }
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        if (stopped())
            throw new StageHostException("stopped", "公会流程已停止，未提交新输入。");
        var initial = await read();
        if (!JsonNode.DeepEquals(initial.Context, context))
            throw new StageHostException("identity", "公会现场与原队列不一致。");
        Validate(initial);
        var existing = prior(initial.Daily!);
        ValidateConfirmed(existing);
        CheckPriorAttempts(context, initial.Daily!, existing);
        string id = Guid.NewGuid().ToString("N");
        var actions = new JsonArray();
        var record = new JsonObject { ["schema"] = 1, ["engine"] = "dotnet-guild-v1", ["version"] = DailyIdentity.Version, ["policy_fingerprint"] = policy.Fingerprint, ["recipe_fingerprint"] = DailyIdentity.Hash(recipe.ToJsonString()), ["before"] = initial.Frame.DeepClone(), ["id"] = id, ["context"] = context.DeepClone(), ["guild_key"] = initial.Daily!.Guild.GuildKey, ["state"] = "running", ["entry_status"] = "none", ["actions"] = actions, ["started"] = now() };
        Save(record);
        double end = clock() + policy.TotalSeconds;
        string? stable = null;
        double stableAt = clock();
        bool entry = false;
        int changed = 0;
        try
        {
            while (clock() < end)
            {
                if (stopped())
                    throw new StageHostException("stopped", "公会流程已停止，原业务回执保留。");
                var observed = await read();
                if (!JsonNode.DeepEquals(observed.Context, context))
                    throw new StageHostException("identity", "公会流程期间账号、连接或刷新周期变化。");
                Validate(observed);
                var frame = observed.Frame;
                var daily = observed.Daily!;
                if (daily.Guild.GuildKey != record["guild_key"]!.GetValue<string>())
                    throw new StageHostException("identity", "公会身份已变化，原请求不重放。");
                if (DailyNavigationDecision.Phase(frame) != "page")
                    throw new StageHostException("adapter", "公会流程不离开已有战斗。");
                var receipt = prior(daily);
                ValidateConfirmed(receipt);
                record["guild_receipt"] = receipt == null ? null : JsonSerializer.SerializeToNode(receipt, DailyJson.Options);
                if (receipt != null && receipt.State is "completed" or "checked_no_grant" or "confirmed_cleanup_pending")
                {
                    if (!SameScope(receipt, daily))
                        throw new StageHostException("identity", "公会回执不属于当前刷新周期。");
                    if (entry)
                        record["entry_status"] = "confirmed";
                }
                var notice = DailyNavigationDecision.Notice(frame, policy, out var noticeSurface);
                if (noticeSurface != null)
                {
                    if (notice == null)
                    {
                        await delay(TimeSpan.FromMilliseconds(200));
                        continue;
                    }
                    if (stopped())
                        throw new StageHostException("stopped", "公告处理前已停止。");
                    await relay("guild_step", "guild", new JsonObject { ["action"] = notice.DeepClone(), ["guild_key"] = record["guild_key"]?.DeepClone() });
                    stable = null;
                    continue;
                }
                // The native menu's rotating links must not delay entry or final
                // confirmed cleanup. Guild steps still revalidate the full token,
                // target and original attendance proof immediately before input.
                string pageMarker = DailyNavigationDecision.Types(frame).Contains("MenuUI") ? DailyHomeDecision.MenuSignature(frame, policy) : frame["UiToken"]?.GetValue<string>() ?? "";
                string marker = pageMarker + "|" + receipt?.State + "|" + daily.Guild.AttendancePopup;
                if (marker != stable)
                {
                    stable = marker;
                    stableAt = clock();
                }
                if (clock() - stableAt < policy.SettleSeconds)
                {
                    await delay(TimeSpan.FromMilliseconds(200));
                    continue;
                }
                var decision = Decide(frame, daily, receipt, recipe, policy);
                record["decision"] = decision.DeepClone();
                Save(record);
                if (decision["wait"] != null)
                {
                    if (receipt == null || now() - receipt.StartedUtcTicks > policy.RequestSeconds * TimeSpan.TicksPerSecond)
                        throw new StageHostException("pending", "Native response not confirmed; no retry");
                    await delay(TimeSpan.FromMilliseconds(200));
                    continue;
                }
                if (decision["pause"] is JsonValue pause)
                    throw new StageHostException(receipt?.State is "unknown" or "prepared" or "dispatching" ? "pending" : "adapter", pause.GetValue<string>());
                if (decision["finish"]?.GetValue<bool>() == true)
                {
                    record["state"] = "completed";
                    record["returned_home"] = true;
                    record["after"] = frame.DeepClone();
                    Save(record);
                    return new()
                    {
                        ["id"] = id,
                        ["state"] = "completed",
                        ["actions"] = actions.Count,
                        ["record"] = Path.Combine(DirectoryPath, id + ".json"),
                        ["guild_receipt"] = record["guild_receipt"]?.DeepClone()
                    };
                }
                var action = decision["action"]!.DeepClone().AsObject();
                bool entering = action["allow_guild"]?.GetValue<bool>() == true;
                if (entering)
                {
                    if (entry)
                        throw new StageHostException("pending", "已提交公会进入，等待原回执；不重复进入。");
                    string gate = GuildPolicy.Gate(daily, receipt, now());
                    if (gate.Length > 0)
                        throw new StageHostException("adapter", gate);
                    record["entry_status"] = "prepared";
                    Save(record);
                }
                if (stopped())
                {
                    if (entering)
                    {
                        record["entry_status"] = "rejected";
                        Save(record);
                    }
                    throw new StageHostException("stopped", "公会输入提交前已停止。");
                }
                var actionRecord = new JsonObject { ["rule"] = decision["id"]?.DeepClone(), ["intent"] = action.DeepClone() };
                actions.Add(actionRecord);
                if (entering)
                    record["entry_status"] = "dispatching";
                Save(record);
                try
                {
                    var done = await relay("guild_step", "guild", new JsonObject { ["action"] = action.DeepClone(), ["guild_key"] = record["guild_key"]?.DeepClone() });
                    actionRecord["receipt_id"] = done["id"]?.DeepClone();
                    if (entering)
                        entry = true;
                    changed = 0;
                }
                catch (StageHostException e) when (e.Kind is "guild_changed" or "guild_rejected")
                {
                    actionRecord["pre_dispatch_rejection"] = e.Message;
                    if (entering)
                        record["entry_status"] = "rejected";
                    Save(record);
                    if (++changed > 5)
                        throw new StageHostException("adapter", "公会页面持续变化，原输入未提交。");
                    stable = null;
                    await delay(TimeSpan.FromMilliseconds(200));
                    continue;
                }
                catch { if (entering) { entry = true; record["entry_status"] = "unknown"; } Save(record); throw; }
                Save(record);
                stableAt = clock();
            }
            throw new StageHostException(entry ? "pending" : "adapter", "Workflow time budget reached; no automatic restart");
        }
        catch (Exception error)
        {
            record["state"] = "paused";
            record["error"] = error.Message;
            bool uncertain = entry && record["entry_status"]?.GetValue<string>() != "confirmed";
            if (uncertain)
                record["entry_status"] = "unknown";
            Save(record);
            if (uncertain && error is StageHostException fault && fault.Kind == "adapter")
                throw new StageHostException("pending", error.Message);
            throw;
        }
    }
}


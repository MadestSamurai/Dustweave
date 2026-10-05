using System.Text.Json.Nodes;
namespace BD2Daily;

public sealed class StageHostException(string kind, string message) : Exception(message)
{
    public string Kind { get; } = kind;
}
public interface IDailyStageHost : IAsyncDisposable
{
    Task<JsonObject> CallAsync(string operation, string? stage = null, JsonObject? arguments = null);
}
public interface IDailyStageProgressHost
{
    event Action<string, JsonObject>? StageProgress;
}
public sealed record DailyQueueRequest(string Root, string Account, string Output, string? Resume = null, bool SyncCollection = false, IReadOnlyList<string>? Tasks = null, string? RetryOf = null);

/// <summary>Owns queue decisions and durable progress. Hosts execute one bounded operation only.</summary>
public sealed class DailyQueueEngine(IDailyStageHost host, Func<bool> paused, Func<DailyPreferences> preferences)
{
    private static readonly HashSet<string> Terminal = ["completed", "skipped", "partial", "blocked", "recovery_required"];
    private static string Text(JsonNode? n) => n?.GetValue<string>() ?? "";
    public static string RecordPath(string root, string id)
    {
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("队列标识无效。");
        return Path.Combine(root, "live", "queues", id, "result.json");
    }
    private static JsonObject Load(string path) => DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("队列记录不存在或无法读取。");
    private static void ValidateRecord(JsonObject record)
    {
        if (record["schema"]?.GetValue<int>() != 1 || record["context"] is not JsonObject || record["items"] is not JsonArray items)
            throw new InvalidDataException("不支持的队列记录格式。");
        var names = new HashSet<string>();
        foreach (var row in items)
        {
            if (row is not JsonObject item || !names.Add(Text(item["task"])) || Text(item["task"]).Length == 0 || !(Terminal.Contains(Text(item["state"])) || Text(item["state"]) is "pending" or "running"))
                throw new InvalidDataException("队列环节记录无效。");
        }
    }
    private static bool SameRoleCycle(JsonObject before, JsonObject now)
    {
        return before["actor"] is JsonArray a && now["actor"] is JsonArray b && a.Count == 5 && b.Count == 5
            && JsonNode.DeepEquals(a[3], b[3]) && JsonNode.DeepEquals(a[4], b[4])
            && JsonNode.DeepEquals(before["server"], now["server"]) && JsonNode.DeepEquals(before["cycle"], now["cycle"]);
    }
    private static JsonArray RetryItems(JsonObject old, IReadOnlyList<string> names, JsonObject context)
    {
        ValidateRecord(old);
        DailyWeeklyMission.RecheckLegacy(old);
        if (!SameRoleCycle(old["context"]!.AsObject(), context))
            throw new InvalidOperationException("补跑记录不是当前账号或已跨过日重置；请开始新队列读取游戏进度。");
        var rows = old["items"]!.AsArray();
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || wanted.Count != names.Count || names.Any(n => !rows.Any(i => Text(i!["task"]) == n && DailyQueueRetry.CanSelect(new QueueStage(Text(i["task"]), Text(i["state"]), "")))))
            throw new InvalidOperationException("补跑只能选择未完成环节；待核对的操作会先确认结果。");
        return new JsonArray(rows.Select(row => { var item = row!.DeepClone().AsObject(); if (wanted.Contains(Text(item["task"]))) return (JsonNode)new JsonObject { ["task"] = Text(item["task"]), ["state"] = Text(item["state"]) == "recovery_required" ? "recovery_required" : "pending" }; item["carried_forward"] = true; return item; }).ToArray());
    }
    private async Task<JsonObject> Observe(JsonObject? expected = null)
    {
        var state = await host.CallAsync("observe");
        if (expected != null && !JsonNode.DeepEquals(expected, state["context"]))
            throw new StageHostException("identity", "账号、角色、进程或重置周期已变化，请开始新队列读取游戏进度。");
        return state;
    }
    public async Task<JsonObject> RunAsync(DailyQueueRequest request)
    {
        if (!DailyProfiles.ValidKey(request.Account))
            throw new InvalidDataException("当前账号无效。");
        if (request.Resume != null && (request.Tasks != null || request.RetryOf != null || request.SyncCollection)
          || request.SyncCollection && (request.Tasks != null || request.RetryOf != null)
          || request.RetryOf != null && request.Tasks is not { Count: > 0 })
            throw new InvalidOperationException("接续、补跑与手动检查须分别执行。");
        var initial = await Observe();
        var context = initial["context"]!.AsObject();
        if (context["actor"] is not JsonArray actor || actor.Count != 5 || Text(actor[3]) != request.Account)
            throw new StageHostException("identity", "执行账号与当前游戏不一致。");
        var adapters = initial["adapters"]!.AsArray().Select(Text).ToHashSet(StringComparer.Ordinal);
        string id = request.Resume ?? Guid.NewGuid().ToString("N"), path = RecordPath(request.Root, id);
        JsonObject record;
        if (request.Resume != null)
        {
            record = Load(path);
            ValidateRecord(record);
            DailyWeeklyMission.RecheckLegacy(record, resume: true);
            if (!JsonNode.DeepEquals(context, record["context"]))
                throw new StageHostException("identity", "原队列的账号、进程或重置周期已变化，请开始新队列。");
            foreach (var node in record["items"]!.AsArray())
                if (Text(node!["state"]) == "running")
                {
                    node["state"] = "recovery_required";
                    node["error"] = "执行器上次中断，先核对原业务操作。";
                }
        }
        else
        {
            var names = request.SyncCollection ? new[] { "collection_sync" } : request.Tasks ?? DailyStageCatalog.Selectable.Select(s => s.Id).ToArray();
            if (names.Count == 0 || names.Distinct().Count() != names.Count || names.Any(n => !adapters.Contains(n)))
                throw new InvalidDataException("环节选择或执行组件不完整。");
            JsonObject? previous = request.RetryOf == null ? null : Load(RecordPath(request.Root, request.RetryOf));
            var items = previous == null ? new JsonArray(names.Select(n => (JsonNode)new JsonObject { ["task"] = n, ["state"] = "pending" }).ToArray()) : RetryItems(previous, names, context);
            record = new JsonObject { ["schema"] = 1, ["id"] = id, ["state"] = "pending", ["context"] = context.DeepClone(), ["created"] = DateTime.UtcNow.Ticks, ["items"] = items };
            if (previous != null)
            {
                record["previous_queue"] = Text(previous["id"]);
                record["selected_tasks"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
            }
        }
        DailyQueuePeriod.Remember(request.Root, DailyJson.TryRead<DailySnapshot>(Path.Combine(request.Root, "snapshot.json")));
        using (var document = System.Text.Json.JsonDocument.Parse(record.ToJsonString()))
        {
            var period = DailyQueuePeriod.Read(document.RootElement);
            long deadline = DailyQueuePeriod.Deadline(period, DailyQueuePeriod.Snapshot(request.Root, period?.Server ?? ""), DateTime.UtcNow.Ticks);
            if (deadline > 0)
                record["resetUtcTicks"] = deadline;
        }
        record["engine"] = "dotnet-v1";
        void Save()
        {
            record["updated"] = DateTime.UtcNow.Ticks;
            DailyJson.Write(path, record);
        }
        Save();
        DailyQueueHistory.Remember(request.Root, request.Account, path);
        DailyJson.Write(request.Output, new
        {
            account = request.Account,
            record = path,
            state = "preparing",
            engine = "dotnet-v1"
        });
        var itemsToRun = record["items"]!.AsArray().Select(i => i!.AsObject()).ToArray();
        var settings = preferences();
        settings.Validate();
        var owners = initial["owners"]!.AsArray().Select(Text).ToArray();
        string? priority = owners.Length == 1 && itemsToRun.Any(i => Text(i["task"]) == owners[0] && i["carried_forward"]?.GetValue<bool>() != true && Text(i["state"]) is "pending" or "running" or "recovery_required") && DailyStageCatalog.Enabled(owners[0], settings) ? owners[0] : null;
        HashSet<string> activeWeekly = [];
        void OnProgress(string stage, JsonObject progress)
        {
            if (!activeWeekly.Contains(stage))
                throw new StageHostException("protocol", "收到未选择环节的进度。");
            var row = itemsToRun.Single(i => Text(i["task"]) == stage);
            row["progress"] = progress.DeepClone();
            Save();
        }
        var progressHost = host as IDailyStageProgressHost;
        if (progressHost != null)
            progressHost.StageProgress += OnProgress;
        try
        {
            if (paused())
                throw new StageHostException("stopped", "已停止，尚未派发后续环节。");
            await host.CallAsync("begin", arguments: new JsonObject { ["context"] = context.DeepClone() });
            record["state"] = "running";
            record.Remove("error");
            Save();
            record["reconciliation"] = await host.CallAsync("reconcile");
            Save();
            var unresolved = record["reconciliation"]?["unresolved"]?.AsArray() ?? [];
            foreach (var item in itemsToRun)
            {
                bool blocked = unresolved.Any(row => row?["recover_in_stage"]?.GetValue<bool>() != true && (row?["stages"] is not JsonArray stages || stages.Any(s => Text(s) == "*" || Text(s) == Text(item["task"]))));
                if (Text(item["state"]) == "recovery_required" && blocked && Text(item["task"]) != priority)
                    item["error"] = "上次操作结果仍未确认，未重复执行；请查看核对记录。";
                if (Text(item["state"]) == "recovery_required" && (!blocked || Text(item["task"]) == priority))
                {
                    item["state"] = "pending";
                    item["recovered"] = true;
                    item.Remove("error");
                }
            }
            Save();
            foreach (var item in itemsToRun.OrderBy(i => Text(i["task"]) == priority ? 0 : 1))
            {
                if (item["carried_forward"]?.GetValue<bool>() == true || Terminal.Contains(Text(item["state"])))
                    continue;
                if (paused())
                    throw new StageHostException("stopped", "已停止后续操作。");
                await Observe(context);
                string stage = Text(item["task"]);
                if (!DailyStageCatalog.Enabled(stage, preferences()))
                {
                    item["state"] = "skipped";
                    item["result"] = new JsonObject { ["state"] = "skipped", ["reason"] = "disabled" };
                    item["finished"] = DateTime.UtcNow.Ticks;
                    Save();
                    continue;
                }
                if (!adapters.Contains(stage))
                {
                    item["state"] = "blocked";
                    item["error"] = "缺少日常执行适配器。";
                    Save();
                    continue;
                }
                if (DailyStageCatalog.IsWeeklyRoute(stage))
                {
                    var members = itemsToRun.Where(i => i["carried_forward"]?.GetValue<bool>() != true && !Terminal.Contains(Text(i["state"])) && DailyStageCatalog.IsWeeklyRoute(Text(i["task"])) && DailyStageCatalog.Enabled(Text(i["task"]), preferences())).ToArray();
                    activeWeekly = members.Select(i => Text(i["task"])).ToHashSet(StringComparer.Ordinal);
                    if (activeWeekly.Any(s => !adapters.Contains(s)))
                        throw new StageHostException("protocol", "周任务执行组件不完整。");
                    try
                    {
                        var nav = await host.CallAsync("navigate", stage);
                        await Observe(context);
                        if (paused())
                            throw new StageHostException("stopped", "已停止周任务。");
                        foreach (var row in members)
                        {
                            row["state"] = "running";
                            row["started"] = DateTime.UtcNow.Ticks;
                            row["navigation"] = nav.DeepClone();
                            row.Remove("error");
                            row.Remove("progress");
                        }
                        Save();
                        var result = await host.CallAsync("execute", stage, new JsonObject { ["stages"] = new JsonArray(members.Select(i => (JsonNode)JsonValue.Create(Text(i["task"]))!).ToArray()) });
                        var results = result["stages"] as JsonObject ?? throw new StageHostException("protocol", "合并周任务缺少分项结果。");
                        if (!results.Select(p => p.Key).ToHashSet().SetEquals(activeWeekly))
                            throw new StageHostException("protocol", "合并周任务结果与选择不一致。");
                        foreach (var row in members)
                        {
                            var part = results[Text(row["task"])] as JsonObject ?? throw new StageHostException("protocol", "周任务结果格式错误。");
                            if (!Terminal.Contains(Text(part["state"])))
                                throw new StageHostException("protocol", "周任务没有明确完成状态。");
                        }
                        foreach (var row in members)
                        {
                            var part = results[Text(row["task"])]!.AsObject();
                            row["state"] = Text(part["state"]);
                            row["result"] = part.DeepClone();
                            row["finished"] = DateTime.UtcNow.Ticks;
                        }
                        Save();
                    }
                    catch (StageHostException error) when (error.Kind is "adapter" or "pending" or "reconciled")
                    {
                        foreach (var row in members)
                        {
                            row["state"] = error.Kind == "pending" ? "recovery_required" : "blocked";
                            row["error"] = error.Message;
                            row["finished"] = DateTime.UtcNow.Ticks;
                        }
                        Save();
                        if (host is IDailyWeeklyCompletionHost completion)
                        {
                            var settled = await completion.SettleWeeklyAsync(members.Select(i => Text(i["task"])).ToArray(), context);
                            foreach (var row in members)
                                if (settled[Text(row["task"])] is JsonObject part && Text(part["state"]) == "completed")
                                {
                                    row["state"] = "completed";
                                    row["result"] = part.DeepClone();
                                    row.Remove("error");
                                }
                            Save();
                        }
                        await Observe(context);
                        var recovery = await host.CallAsync("recover", stage, new JsonObject { ["error"] = error.Message, ["context"] = context.DeepClone() });
                        foreach (var row in members)
                            row["recovery"] = recovery.DeepClone();
                        Save();
                        if (recovery["safe"]?.GetValue<bool>() != true)
                            throw new InvalidOperationException("周任务现场未能安全恢复：" + error.Message);
                    }
                    finally { activeWeekly.Clear(); }
                    if (paused())
                        throw new StageHostException("stopped", "已停止后续操作；周任务进度已保留。");
                    await Observe(context);
                    continue;
                }
                try
                {
                    item["navigation"] = await host.CallAsync("navigate", stage);
                    Save();
                }
                catch (StageHostException error) when (error.Kind == "adapter")
                {
                    if (paused())
                        throw;
                    await Observe(context);
                    JsonObject recovery;
                    try
                    {
                        recovery = await host.CallAsync("recover", stage, new JsonObject { ["error"] = error.Message, ["context"] = context.DeepClone() });
                    }
                    catch (StageHostException recoveryError) when (recoveryError.Kind == "adapter") { recovery = new JsonObject { ["safe"] = false, ["reason"] = recoveryError.Message }; }
                    item["state"] = "blocked";
                    item["phase"] = "navigation";
                    item["error"] = error.Message;
                    item["recovery"] = recovery;
                    item["finished"] = DateTime.UtcNow.Ticks;
                    Save();
                    if (recovery["safe"]?.GetValue<bool>() == true)
                        continue;
                    throw new InvalidOperationException("导航未能安全恢复：" + error.Message);
                }
                if (paused())
                    throw new StageHostException("stopped", "已在环节开始前停止。");
                await Observe(context);
                item["state"] = "running";
                item["started"] = DateTime.UtcNow.Ticks;
                item.Remove("error");
                Save();
                try
                {
                    JsonObject result;
                    try
                    {
                        result = await host.CallAsync("execute", stage);
                    }
                    catch (StageHostException error) when (error.Kind == "reconciled")
                    {
                        if (paused())
                            throw;
                        await Observe(context);
                        item["recovered_plan"] = true;
                        Save();
                        item["navigation"] = await host.CallAsync("navigate", stage);
                        Save();
                        result = await host.CallAsync("execute", stage); // At most one re-plan; never blindly resend a business request.
                    }
                    if (Text(result["state"]) is not ("completed" or "skipped" or "partial"))
                        throw new StageHostException("adapter", Text(result["error"]) is { Length: > 0 } e ? e : "环节尚未确认完成。");
                    item["state"] = Text(result["state"]);
                    item["result"] = result;
                }
                catch (StageHostException error) when (error.Kind is "adapter" or "pending" or "reconciled")
                {
                    item["state"] = error.Kind == "pending" ? "recovery_required" : "blocked";
                    item["error"] = error.Message;
                }
                item["finished"] = DateTime.UtcNow.Ticks;
                Save();
                if (paused())
                    throw new StageHostException("stopped", "已停止后续操作；原请求结果已保留。");
                await Observe(context);
            }
            record["state"] = itemsToRun.All(i => Text(i["state"]) is "completed" or "skipped") ? "completed" : "partial";
        }
        catch (Exception error)
        {
            record["state"] = "paused";
            record["error"] = error.Message;
            foreach (var item in itemsToRun.Where(i => Text(i["state"]) == "running"))
            {
                item["state"] = "recovery_required";
                item["error"] = "执行中断，须先核对原业务操作：" + error.Message;
            }
        }
        finally { if (progressHost != null) progressHost.StageProgress -= OnProgress; }
        Save();
        DailyQueueHistory.Remember(request.Root, request.Account, path);
        DailyJson.Write(request.Output, new
        {
            account = request.Account,
            record = path,
            state = Text(record["state"]),
            engine = "dotnet-v1"
        });
        return record;
    }
}

using System.Diagnostics;
using System.Text.Json;
namespace Dustweave;

public sealed record QueueStage(string Task, string State, string Detail, bool Carried = false, DateTimeOffset? FinishedAt = null, IReadOnlyList<QueueTaskDetail>? PendingTasks = null);
public sealed record QueueView(string State, string Message, string Record, IReadOnlyList<QueueStage> Stages, string Account = "", QueuePeriod? Period = null, bool Expired = false);
public sealed record QueuePlanRequest(string Account, IReadOnlyList<string> Tasks)
{
    public string[] Validate(string account, DailyPreferences preferences)
    {
        if (!DailyProfiles.ValidKey(account) || Account != account)
            throw new InvalidOperationException("本次勾选的账号已变化，请回到当前计划重新选择。");
        var wanted = Tasks.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0 || wanted.Count != Tasks.Count || wanted.Any(id => !DailyStageCatalog.Selectable.Any(s => s.Id == id && s.Enabled(preferences))))
            throw new InvalidOperationException("请勾选至少一个已开启环节；设置若已变化，请重新确认当前计划。");
        // Click/sort order must not change dependencies such as draws before recycling.
        return DailyStageCatalog.Selectable.Where(s => wanted.Contains(s.Id)).Select(s => s.Id).ToArray();
    }
}
public sealed record QueueRetryRequest(string Account, string Record, IReadOnlyList<string> Tasks);
public static class DailyQueueRetry
{
    public static bool CanSelect(QueueStage stage) => stage.State is "pending" or "partial" or "blocked" or "recovery_required";
    public static string[] Validate(QueueView view, string account, QueueRetryRequest request)
    {
        if (!DailyProfiles.ValidKey(account) || account != request.Account || view.Account != account || view.Record.Length == 0 || view.Record != request.Record)
            throw new InvalidOperationException("补跑记录与当前账号或队列不一致，请重新载入该账号的记录。");
        if (view.Expired)
            throw new InvalidOperationException("上次记录已跨过游戏每日重置，请开始日常读取新一天的进度。");
        var wanted = request.Tasks.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0 || wanted.Count != request.Tasks.Count || wanted.Any(t => !view.Stages.Any(s => s.Task == t && CanSelect(s))))
            throw new InvalidOperationException("请勾选未完成环节；待核对的环节会先确认上次结果，再继续未完成部分。");
        return view.Stages.Where(s => wanted.Contains(s.Task)).Select(s => s.Task).ToArray();
    }
}
public interface IDailyQueueExecutor
{
    Task<int> PrepareAsync(Action<string> report);
    Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null);
}
public sealed class PackagedDailyQueueExecutor(string directory) : IDailyQueueExecutor
{
    private async Task<int> Invoke(string file, IEnumerable<string> args, Action<string> report, string? dataRoot = null)
    {
        string root = dataRoot ?? DailyIdentity.DataRoot;
        string run = Guid.NewGuid().ToString("N");
        var start = DailyTools.StartInfo(directory, file);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var p = Process.Start(start) ?? throw new IOException("执行组件未启动。");
        async Task Drain(StreamReader reader)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
                if (line.Length > 0)
                    report(line);
        }
        var drains = Task.WhenAll(Drain(p.StandardOutput), Drain(p.StandardError));
        try
        {
            return await DailyHelperLifetime.WaitAsync(p, () =>
            {
                if (File.Exists(Path.Combine(root, "queue-stop")))
                    throw new OperationCanceledException("operator_stop");
                return Task.CompletedTask;
            }, TimeSpan.FromMinutes(3));
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            DailyJson.Write(Path.Combine(root, "connection-watchdog.json"), new
            {
                runId = run,
                processId = p.Id,
                reason = e is OperationCanceledException ? "operator_stop" : "connection_timeout",
                utc = DateTimeOffset.UtcNow,
                gameTerminated = false
            });
            if (e is OperationCanceledException)
                return 1;
            throw;
        }
        finally
        {
            if (!p.HasExited)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            }
            try
            {
                await drains.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException) { report("执行器输出管道未关闭，已保留现有日志。"); }
        }
    }
    public Task<int> PrepareAsync(Action<string> report) => Invoke("live", ["ready"], report);
    public async Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null)
    {
        var observation = DailyStageObservation.Attach(root, account);
        bool Stopped() => File.Exists(Path.Combine(root, "queue-stop"));
        var game = new DailyGameHost(root).Find() ?? throw new StageHostException("identity", "游戏已退出，没有建立执行连接。");
        using var driver = new DailyCommandDriver(root, new DailyPipeMailbox(root, game), observation.ReadFrameAsync, Stopped);
        var navigation = new DailyStageNavigation(observation.ReadFrameAsync, Stopped);
        var extension = DailyExtensionLoader.Load(DailyPlugin.Current);
        var proofs = DailyWorkflowRegistry.Proofs(extension);
        var business = new DailyManagedBusiness(root, driver, proofs, Stopped);
        DailyTradeData? tradeData = null;
        if (!syncCollection && (tasks == null || tasks.Contains("trade")) && DailyStageCatalog.Enabled("trade", new DailyPreferenceStore(root).Read(account)))
        {
            var before = await observation.ReadFrameAsync();
            tradeData = await DailyTradeData.PrepareAsync(directory, root, game, before, Stopped, report);
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(before.Context, (await observation.ReadFrameAsync()).Context))
                throw new StageHostException("identity", "跑商数据准备期间身份变化；没有开始交易。");
        }
        var workflows = new DailyWorkflowRegistry(root, directory, driver, business, navigation, proofs, Stopped, report, () => tradeData?.VerifiedCatalog, extension, () => DailyRuleData.PrepareAsync(directory, root, game, observation.ReadFrameAsync, Stopped, report), (name, source, groups) => DailyRuleData.PrepareSetAsync(directory, root, game, observation.ReadFrameAsync, Stopped, report, name, source, groups));
        var managed = workflows.Stages();
        managed["guild"] = new DailyGuildStage(root, observation.ReadFrameAsync, Stopped);
        managed["room"] = new DailyRoomStage(root, driver, Stopped);
        managed["mail"] = new DailyMailStage(root, driver, Stopped);
        managed["friendship"] = new DailyFriendshipStage(root, driver, business, navigation, Stopped);
        managed["free_draws"] = new DailyFreeDrawStage(driver, business, Stopped, prepare: () => DailyFreeDrawData.PrepareAsync(directory, root, game, observation.ReadFrameAsync, Stopped, report));
        foreach (string name in new[] { "management", "cafeteria_income", "life_helpers" })
            managed[name] = new DailyManagementStage(root, driver, business, Stopped, name);
        var reconciliation = new DailyManagedReconciliation(root, driver, ["room.info", "mail.collect"], Stopped);
        var bootstrap = new DailyManagedBootstrap(root, driver, observation.ReadFrameAsync, Stopped);
        await using var host = new DailyStageHost(root, observation, navigation, driver, business, bootstrap, reconciliation, workflows, managed, proofs, tradeData);
        var engine = new DailyQueueEngine(host, () => File.Exists(Path.Combine(root, "queue-stop")), () => new DailyPreferenceStore(root).Read(account));
        var record = await engine.RunAsync(new(root, account, output, resume, syncCollection, tasks, retryOf));
        report(".NET queue: " + record["state"]?.GetValue<string>());
        return record["state"]?.GetValue<string>() == "completed" ? 0 : 1;
    }

}
public sealed class DailyQueueSession(string root, IDailyQueueExecutor executor, Func<long>? utcTicks = null)
{
    private bool running, workerStarted;
    private volatile bool stopped;
    public bool IsRunning => running;
    public event Action<QueueView>? Progress;
    public string LastOutput => Path.Combine(root, "queue-ui.json");
    public void Stop()
    {
        if (!running)
            return;
        stopped = true;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "queue-stop"), "");

    }
    private static JsonElement ReadElement(string path)
    {
        using var doc = DailyJson.TryRead<JsonDocument>(path);
        return doc?.RootElement.Clone() ?? default;
    }
    public QueueView ReadView(string? selectedAccount = null)
    {
        var header = ReadElement(LastOutput);
        if (selectedAccount != null)
        {
            string selectedRecord = DailyQueueHistory.Resolve(root, selectedAccount);
            if (selectedRecord.Length == 0)
                return new("idle", "本账号尚无日常记录。", "", [], selectedAccount);
            header = JsonSerializer.SerializeToElement(new
            {
                account = selectedAccount,
                record = selectedRecord
            });
        }
        if (header.ValueKind != JsonValueKind.Object)
            return new("idle", "尚未运行日常。", "", []);
        string account = header.TryGetProperty("account", out var accountValue) ? accountValue.GetString() ?? "" : "";
        string record = header.TryGetProperty("record", out var p) ? p.GetString() ?? "" : "";
        // Output is local, but never follow an arbitrary record path into another directory.
        string allowed = Path.GetFullPath(Path.Combine(root, "live", "queues")) + Path.DirectorySeparatorChar;
        if (record.Length == 0 || !Path.GetFullPath(record).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            return new("preparing", "正在准备连接与队列…", "", []);
        var value = ReadElement(record);
        if (value.ValueKind != JsonValueKind.Object)
            return new("preparing", "正在进入日常菜单…", record, []);
        var normalized = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())!.AsObject();
        DailyWeeklyMission.RecheckLegacy(normalized);
        value = JsonSerializer.SerializeToElement(normalized);
        string state = value.GetProperty("state").GetString() ?? "";
        var stages = value.GetProperty("items").EnumerateArray().Select(i => new QueueStage(i.GetProperty("task").GetString() ?? "", i.GetProperty("state").GetString() ?? "",
          ReadStageDetail(i), i.TryGetProperty("carried_forward", out var carried) && carried.ValueKind == JsonValueKind.True, ReadFinishedAt(i), DailyPendingTasks.Read(i))).ToArray();
        var period = DailyQueuePeriod.Read(value);
        long now = utcTicks?.Invoke() ?? DateTime.UtcNow.Ticks;
        bool expired = DailyQueuePeriod.IsExpired(period, DailyQueuePeriod.Snapshot(root, period?.Server ?? ""), now);
        return new(state, value.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "", record, stages, account, period, expired);
    }
    private static string ReadStageDetail(JsonElement item)
    {
        static string Text(JsonElement element, string key) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        string error = Text(item, "error");
        if (!string.IsNullOrWhiteSpace(error)) return error;
        item.TryGetProperty("result", out var result);
        item.TryGetProperty("progress", out var progress);
        string state = Text(item, "state"), current = Text(progress, "detail");
        if (state is ("running" or "pending") && !string.IsNullOrWhiteSpace(current)) return current;
        string detail = Text(result, "detail");
        if (!string.IsNullOrWhiteSpace(detail)) return detail;
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("waiting_daily_reset", out var waiting)
            && waiting.ValueKind == JsonValueKind.True) return "吸收次数用完，次日接续未完成地图";
        string reason = Text(result, "reason");
        if (!string.IsNullOrWhiteSpace(reason)) return reason;
        // A final result must not display stale progress such as "preparing".
        return state is "completed" or "skipped" ? "" : current;
    }
    private static DateTimeOffset? ReadFinishedAt(JsonElement item)
    {
        // Durable per-stage UTC ticks, never the queue refresh time or the reader's clock.
        if (item.TryGetProperty("finished", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long ticks) && ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        return null;
    }
    private void MarkInterrupted(string message)
    {
        var view = ReadView();
        if (view.Record.Length == 0)
            return;
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(view.Record))!;
        if (node["state"]?.GetValue<string>() is "completed" or "partial")
            return;
        node["state"] = "paused";
        node["error"] = message;
        foreach (var item in node["items"]!.AsArray())
            if (item?["state"]?.GetValue<string>() == "running")
            {
                item["state"] = "recovery_required";
                item["error"] = "执行器已停止，需先核对原操作回执。";
            }
        DailyJson.Write(view.Record, node);
        Progress?.Invoke(ReadView());
    }
    public async Task<QueueView> RunAsync(string account, bool resume = false, bool syncCollection = false, QueueRetryRequest? retry = null, QueuePlanRequest? selection = null)
    {
        if (selection != null && (resume || syncCollection || retry != null))
            throw new InvalidOperationException("本次勾选、补跑、接续和检查须分别执行。");
        if (retry != null && (resume || syncCollection))
            throw new InvalidOperationException("补跑须单独执行。");
        if (resume && syncCollection)
            throw new InvalidOperationException("同步须独立执行。");
        if (running)
            throw new InvalidOperationException("日常队列正在运行。");
        if (!DailyProfiles.ValidKey(account))
            throw new InvalidDataException("请先确认当前账号。");
        var selectedTasks = selection != null ? selection.Validate(account, new DailyPreferenceStore(root).Read(account)) : retry == null ? null : DailyQueueRetry.Validate(ReadView(account), account, retry);
        string? id = null;
        if (resume)
        {
            if (ReadView(account).Expired)
                throw new InvalidOperationException("上次记录已跨过游戏每日重置，请开始日常读取新一天的进度。");
            var old = ReadView(account);
            if (old.Record.Length == 0 || old.Account != account)
                throw new InvalidOperationException("没有本账号可接续的队列。");
            id = Path.GetFileName(Path.GetDirectoryName(old.Record));
            if (id == null || id.Length != 32 || !id.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("接续记录无效。");
        }
        var previousOutput = ReadElement(LastOutput);
        running = true;
        stopped = false;
        workerStarted = false;
        File.Delete(Path.Combine(root, "queue-stop"));
        Directory.CreateDirectory(root);
        string log = Path.Combine(root, "queue-worker.log");
        using var stream = new StreamWriter(new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        string lastReport = "";
        var sync = new object();
        void Report(string line)
        {
            lock (sync)
            {
                lastReport = line;
                stream.WriteLine(line);
            }
        }
        Task<int>? execution = null;
        try
        {
            Progress?.Invoke(new("preparing", "正在准备日常连接…", "", []));
            int prepared = await executor.PrepareAsync(Report);
            if (stopped)
                return new("paused", "已停止，未开始日常操作。", "", []);
            if (prepared != 0)
                throw new InvalidOperationException("日常连接未就绪，请查看执行日志；未开始日常操作。");
            DailyJson.Write(LastOutput, new
            {
                state = "preparing",
                account,
                record = id != null ? Path.Combine(root, "live", "queues", id, "result.json") : retry?.Record ?? ""
            });
            workerStarted = true;
            // The workflow includes synchronous rule parsing and native optimization. Never
            // inherit the WPF synchronization context: the operator must retain Stop.
            var task = execution = Task.Run(() => executor.ExecuteAsync(root, account, LastOutput, id, Report, syncCollection, selectedTasks, retry == null ? null : Path.GetFileName(Path.GetDirectoryName(retry.Record))));
            while (!task.IsCompleted)
            {
                // Reassert stop after the worker's startup pause reset, closing that race.
                if (stopped)
                    Stop();
                try
                {
                    Progress?.Invoke(ReadView());
                }
                catch (Exception e) { Report("Progress read: " + e.Message); }
                await Task.Delay(300);
            }
            int code = await task;
            if (stopped)
                MarkInterrupted("已停止；下次启动先核对未完成操作。");
            var view = ReadView();
            Progress?.Invoke(view);
            if (code > 1)
                throw new InvalidOperationException(lastReport.Length > 0 ? "日常未继续：" + lastReport : "执行未能开始或需要处理异常，请查看执行日志。已完成的记录仍保留。");
            return view;
        }
        catch (Exception error)
        {
            if (workerStarted)
                MarkInterrupted(error.Message);
            throw;
        }
        finally
        {
            try
            {
                if (execution is { IsCompleted: false })
                {
                    Stop();
                    await execution.WaitAsync(TimeSpan.FromSeconds(30));
                }
            }
            finally
            {
                workerStarted = false;
                running = false;
                // A rejected startup must not erase the only selectable prior report.
                if (previousOutput.ValueKind == JsonValueKind.Object && ReadView().Stages.Count == 0)
                    DailyJson.Write(LastOutput, previousOutput);
            }
        }
    }
}

using System.Diagnostics;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>One explicit login coordinator may retry known native resource-download errors at most three times.</summary>
public static class DailyStartupRecovery
{
    public static string? NetworkError(JsonObject frame)
    {
        if (N(frame["BridgeVersion"]) < 31 || S(frame["Scene"]) is not ("Splash" or "ReGame"))
            return null;
        var popups = DailyNavigationDecision.Rows(frame).Where(u => B(u["Popup"]) && !DailyNavigationDecision.PassiveSurface(S(u["Type"]))).ToArray();
        if (popups.Length != 1 || S(popups[0]["Type"]) != "MessagePopupUI")
            return null;
        foreach (var text in popups[0]["Text"]?.AsArray() ?? new JsonArray())
        {
            var lines = S(text).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var codes = lines.Intersect(new[] { "BUNDLE_CATALOG_CHECK", "BUNDLE_COMMON" }).ToArray();
            if (lines.Contains("CLIENT_LOGIC_ERROR") && codes.Length == 1)
                return codes[0];
        }
        return null;
    }
    public static async Task<bool> Run(string root, GameInstance game, string account, Action<string> report, CancellationToken cancellation)
    {
        var mailbox = new DailyPipeMailbox(root, game);
        var host = new DailyGameHost(root);
        JsonObject Live()
        {
            if (host.Find() != game)
                throw new StageHostException("identity", "启动恢复期间游戏退出。");
            var bytes = mailbox.Read("live", "snapshot.json") ?? throw new IOException("启动观察尚未就绪");
            return JsonNode.Parse(bytes)!.AsObject();
        }
        DailySnapshot Daily() => DailyJson.TryRead<DailySnapshot>(Path.Combine(root, "snapshot.json")) ?? throw new IOException("缺少启动账号观察");
        var first = Daily();
        Require(first.AccountKey == account && StartupPolicy.Fresh(first, DateTime.UtcNow.Ticks), "启动恢复的账号身份无效");
        var frame = Live();
        if (NetworkError(frame) == null)
            return false;
        string owner = Guid.NewGuid().ToString("N"), permit = Path.Combine(root, "startup-permit.json"), path = Path.Combine(root, "live", "startup", $"network-{game.ProcessId}-{game.StartTicks}.json");
        var previous = DailyJson.TryRead<StartupPermit>(permit);
        Require(previous == null || string.IsNullOrEmpty(previous.Owner) || previous.ExpiresUtcTicks <= DateTime.UtcNow.Ticks, "另一个登录协调器正在运行");
        var record = DailyJson.TryRead<JsonObject>(path) ?? O(("state", "running"), ("retries", new JsonArray()), ("process", new JsonArray(first.ProcessId, first.ProcessStartTicks, first.InstanceId, account)));
        Require(record["pending_retry"] == null, "上次启动重试结果尚未确认，保留现场");
        var context = O(("actor", new JsonArray(Copy(frame["ProcessId"]), Copy(frame["ProcessStartTicks"]), Copy(frame["Instance"]), Copy(frame["AccountKey"]), Copy(frame["PlayerKey"]))), ("server", "startup"), ("cycle", game.StartTicks.ToString()));
        Task<DailyStageFrame> Read()
        {
            cancellation.ThrowIfCancellationRequested();
            var d = Daily();
            Require(d.ProcessId == first.ProcessId && d.ProcessStartTicks == first.ProcessStartTicks && d.InstanceId == first.InstanceId && d.AccountKey == account && StartupPolicy.Fresh(d, DateTime.UtcNow.Ticks), "启动恢复账号或进程改变");
            Require(string.IsNullOrEmpty(d.ErrorCode) && string.IsNullOrEmpty(d.LeaseOwner), "启动识别异常或已由其他协调器接管");
            var f = Live();
            Require(DailyEvidence.SameActor(f, frame) && DateTime.UtcNow.Ticks - N(f["AtUtcTicks"]) is >= 0 and <= 50000000, "执行桥身份改变或过期");
            return Task.FromResult(new DailyStageFrame(f, context, d));
        }
        using var driver = new DailyCommandDriver(root, mailbox, Read, () => cancellation.IsCancellationRequested);
        driver.Bind(context);
        driver.Acquire("live");
        driver.Acquire("daily");
        bool paused = mailbox.Read("live", "pause") != null;
        Require(mailbox.Read("live", "command.json") == null, "上次启动命令尚未完成");
        if (paused)
            mailbox.Delete("live", "pause");
        void Revoke()
        {
            var current = DailyJson.TryRead<StartupPermit>(permit);
            if (current?.Owner == owner)
                DailyJson.Write(permit, new StartupPermit());
        }
        try
        {
            long readySequence = 0;
            string? readyPlayer = null;
            var timer = Stopwatch.StartNew();
            string message = "";
            while (timer.Elapsed < TimeSpan.FromMinutes(30))
            {
                cancellation.ThrowIfCancellationRequested();
                if (host.Find() != game)
                    throw new StageHostException("identity", "启动恢复期间游戏退出");
                var daily = Daily();
                Require(daily.AccountKey == account && daily.InstanceId == first.InstanceId && daily.ProcessId == game.ProcessId && daily.ProcessStartTicks == game.StartTicks && StartupPolicy.Fresh(daily, DateTime.UtcNow.Ticks), "启动身份改变或过期");
                Require(string.IsNullOrEmpty(daily.ErrorCode) && string.IsNullOrEmpty(daily.LeaseOwner), "启动被其他操作接管或识别失败");
                if (mailbox.Read("live", "pause") != null)
                    throw new OperationCanceledException("启动恢复已暂停");
                if (daily.State == "identified" && !string.IsNullOrEmpty(daily.PlayerKey))
                {
                    Revoke();
                    if (readySequence > 0 && daily.Sequence > readySequence && daily.PlayerKey == readyPlayer)
                    {
                        record["state"] = "ready";
                        record["player"] = daily.PlayerName;
                        return true;
                    }
                    readySequence = daily.Sequence;
                    readyPlayer = daily.PlayerKey;
                    await Task.Delay(200, cancellation);
                    continue;
                }
                var current = Live();
                string? code = NetworkError(current);
                if (code != null)
                {
                    Revoke();
                    Require(record["retries"]!.AsArray().Count < 3, "资源下载三次重试仍失败");
                    record["pending_retry"] = O(("code", code), ("at", DateTime.UtcNow.Ticks), ("frame", current));
                    DailyJson.Write(path, record);
                    var sent = await driver.RetryStartupAsync();
                    record["retries"]!.AsArray().Add(O(("code", code), ("at", DateTime.UtcNow.Ticks), ("command", sent["id"])));
                    record.Remove("pending_retry");
                    DailyJson.Write(path, record);
                    // Wait for this exact popup to leave; no repeated confirmation of a stale frame.
                    double end = DailyWorkflow.Now + 25;
                    while (DailyWorkflow.Now < end)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var next = Live();
                        if (!JsonNode.DeepEquals(next["UiToken"], current["UiToken"]) || NetworkError(next) == null)
                            break;
                        await Task.Delay(150, cancellation);
                    }
                    Require(NetworkError(Live()) == null || !JsonNode.DeepEquals(Live()["UiToken"], current["UiToken"]), "资源错误窗口尚未推进，未重复点击");
                    continue;
                }
                Require(!DailyNavigationDecision.Rows(current).Any(u => B(u["Popup"]) && S(u["Type"]) != "DownloadPopupUI" && !DailyNavigationDecision.PassiveSurface(S(u["Type"]))), "未知启动弹窗，保留现场");
                DailyJson.Write(permit, new StartupPermit { Owner = owner, AccountKey = account, InstanceId = daily.InstanceId, ProcessId = game.ProcessId, ProcessStartTicks = game.StartTicks, ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks });
                string nextMessage = daily.Scene + " · " + daily.Startup?.Stage + " · " + daily.Startup?.Action;
                if (message != nextMessage)
                {
                    message = nextMessage;
                    report(message);
                }
                record["state"] = "waiting";
                record["updated"] = DateTime.UtcNow.Ticks;
                DailyJson.Write(path, record);
                await Task.Delay(350, cancellation);
            }
            throw new TimeoutException("启动恢复超时，进度已保留");
        }
        catch (Exception ex) { record["state"] = "paused"; record["error"] = ex.Message; throw; }
        finally { Revoke(); DailyJson.Write(path, record); if (paused) mailbox.Write("live", "pause", []); }
    }
}

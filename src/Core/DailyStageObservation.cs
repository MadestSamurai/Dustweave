using System.Diagnostics;
using System.Text.Json.Nodes;
using BD2.LocalIpc;
namespace Dustweave;

/// <summary>Read-only queue observation. Never opens a writer lease or sends gameplay.</summary>
public sealed class DailyStageObservation
{
    public const int BridgeVersion = DailyBridgeVersion.Current;
    public const string LiveEntries = "runtime.json|snapshot.json|error.json|attached.json|performance.json|command.json|pause|legacy-observation|evidence-config.json|observation-request.json|evidence.json|dice-lease.json|dispatch-recovery.json|events~*|receipts~*";
    public static IReadOnlyDictionary<string, string> OwnedBattles
    {
        get;
    } = new Dictionary<string, string>
    {
        ["mirror"] = "BattleUI_PVP",
        ["weekly_book"] = "BattleUI_TotalWar",
        ["event_battle"] = "BattleUI_EventBattle",
        ["tactics"] = "BattleUI_TarosTactics"
    };
    private readonly string account;
    private readonly GameInstance? expectedGame;
    private readonly Func<JsonObject?> live;
    private readonly Func<DailySnapshot?> daily;
    private readonly Func<bool> stopped;
    private readonly Func<long> now;
    private readonly Func<TimeSpan, Task> delay;
    private readonly TimeSpan timeout, interval;
    private readonly Action<string, JsonObject?, DailySnapshot?>? diagnostic;
    public DailyStageObservation(string account, Func<JsonObject?> live, Func<DailySnapshot?> daily,
        Func<bool>? stopped = null, Func<long>? now = null, TimeSpan? timeout = null, TimeSpan? interval = null, Func<TimeSpan, Task>? delay = null, GameInstance? expectedGame = null, Action<string, JsonObject?, DailySnapshot?>? diagnostic = null)
    {
        if (!DailyProfiles.ValidKey(account))
            throw new StageHostException("identity", "观察缺少有效的目标账号。");
        this.account = account;
        this.expectedGame = expectedGame;
        this.diagnostic = diagnostic;
        this.live = live;
        this.daily = daily;
        this.stopped = stopped ?? (() => false);
        this.now = now ?? (() => DateTime.UtcNow.Ticks);
        this.timeout = timeout ?? TimeSpan.FromSeconds(20);
        this.interval = interval ?? TimeSpan.FromMilliseconds(200);
        this.delay = delay ?? (t => Task.Delay(t));
    }
    public static DailyStageObservation Attach(string root, string account, Func<bool>? stopped = null)
    {
        var host = new DailyGameHost(root);
        var game = host.Find() ?? throw new StageHostException("identity", "游戏已退出，请重新连接日常工具。");
        DailyTransport.Refresh(root, game);
        string liveRoot = Path.Combine(root, "live");
        DesktopFiles.Configure(liveRoot, LiveEntries);
        DesktopFiles.Connect(liveRoot, game.ProcessId, game.StartTicks);
        JsonObject? ReadLive()
        {
            if (host.Find() != game)
                throw new StageHostException("identity", "游戏进程已变化，请开始新队列。");
            return DailyJson.TryRead<JsonObject>(Path.Combine(liveRoot, "snapshot.json"));
        }
        return new(account, ReadLive, () => DailyJson.TryRead<DailySnapshot>(Path.Combine(root, "snapshot.json")),
            stopped ?? (() => File.Exists(Path.Combine(root, "queue-stop"))), expectedGame: game,
            diagnostic: (error, frame, identity) => DailyJson.Write(Path.Combine(root, "live", "observation-failures", DateTime.UtcNow.Ticks + "-" + Guid.NewGuid().ToString("N") + ".json"), new { error, frame, identity, at = DateTime.UtcNow.Ticks }));
    }
    private static string Text(JsonObject frame, string name) => frame[name]?.GetValue<string>() ?? "";
    private static long Number(JsonObject frame, string name)
    {
        if (frame[name] is not JsonValue value)
            return 0;
        if (value.TryGetValue<long>(out long number))
            return number;
        if (value.TryGetValue<int>(out int small))
            return small;
        throw Fault("观察中的整数格式无效：" + name);
    }
    private static StageHostException Fault(string message) => new("identity", message);
    public async Task<JsonObject> ObserveAsync()
    {
        var observed = await ReadFrameAsync();
        var types = DailyNavigationDecision.Types(observed.Frame);
        return new JsonObject
        {
            ["context"] = observed.Context.DeepClone(),
            ["adapters"] = new JsonArray(DailyStageCatalog.Adapters.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["owners"] = new JsonArray(OwnedBattles.Where(p => types.Contains(p.Value)).Select(p => (JsonNode)JsonValue.Create(p.Key)!).ToArray())
        };
    }
    public async Task<DailyStageFrame> ReadFrameAsync()
    {
        var elapsed = Stopwatch.StartNew();
        (long Pid, long Start, string Instance)? connection = null;
        string? player = null;
        JsonObject? lastFrame = null; DailySnapshot? lastIdentity = null;
        string waitingReason = "未读到当前账号的最新主线程观察；保留现有进度，请重新连接。";
        void Diagnose(string error) { try { diagnostic?.Invoke(error, lastFrame, lastIdentity); } catch { } }
        while (true)
        {
            if (stopped())
                throw new StageHostException("stopped", "已停止观察，尚未派发后续环节。");
            try
            {
                var frame = live();
                var identity = daily();
                lastFrame = frame; lastIdentity = identity;
                long at = now();
                if (frame != null)
                {
                    if (Number(frame, "Protocol") != 1 || Number(frame, "BridgeVersion") != BridgeVersion)
                        throw Fault($"需要日常执行组件 LiveBridge{BridgeVersion}，请重新连接。");
                    long pid = Number(frame, "ProcessId"), start = Number(frame, "ProcessStartTicks");
                    string instance = Text(frame, "Instance");
                    if (expectedGame != null && (pid != expectedGame.ProcessId || start != expectedGame.StartTicks))
                        throw Fault("观察不属于当前游戏进程。");
                    if (pid <= 0 || start <= 0 || instance.Length == 0)
                        throw Fault("观察缺少当前游戏连接身份。");
                    var current = (pid, start, instance);
                    if (connection != null && connection.Value != current)
                        throw Fault("等待观察期间游戏连接已变化。");
                    connection = current;
                    string key = Text(frame, "AccountKey"), role = Text(frame, "PlayerKey"), error = Text(frame, "Error");
                    if (key.Length > 0 && key != account)
                        throw Fault("当前游戏账号与所选账号不一致。");
                    if (role.Length > 0)
                    {
                        if (!DailyProfiles.ValidKey(role))
                            throw Fault("角色观察身份无效。");
                        if (player != null && role != player)
                            throw Fault("等待观察期间角色已变化。");
                        player = role;
                    }
                    if (error.Length > 0 && error != "Waiting for fresh account identity")
                        throw Fault("主线程观察失败：" + error);
                    long ticks = Number(frame, "AtUtcTicks");
                    if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks || at - ticks < -TimeSpan.FromSeconds(2).Ticks)
                        throw Fault("主线程观察时间无效。");
                    if (identity != null)
                    {
                        if (identity.AccountKey == null || identity.PlayerKey == null || identity.Runtime == null || identity.State == null)
                            throw Fault("日常账号识别数据不完整。");
                        if (identity.ProcessId != pid || identity.ProcessStartTicks != start)
                            throw Fault("日常识别与执行组件的进程不一致。");
                        if (identity.AccountKey.Length > 0 && identity.AccountKey != account)
                            throw Fault("日常账号识别已变化。");
                        if (identity.PlayerKey.Length > 0 && role.Length > 0 && identity.PlayerKey != role)
                            throw Fault("日常角色识别与执行组件不一致。");
                        if (identity.Runtime != DailyIdentity.RuntimeName)
                            throw Fault("日常识别组件版本不一致。");
                        if (identity.State == "identified" && identity.AccountKey == key && identity.PlayerKey == role && error.Length == 0 && key == account && DailyProfiles.ValidKey(role)
                            && at - ticks >= 0 && at - ticks <= TimeSpan.FromSeconds(3).Ticks
                            && identity.FrameUtcTicks > 0 && at - identity.FrameUtcTicks >= 0 && at - identity.FrameUtcTicks <= TimeSpan.FromSeconds(3).Ticks)
                        {
                            var guild = identity.Guild;
                            if (string.IsNullOrEmpty(guild?.ServerKey) || string.IsNullOrEmpty(guild.CycleKey))
                                waitingReason = "等待当前服务器与每日重置周期恢复超时；未派发后续操作。";
                            else
                            {
                                if (frame["Surfaces"] is not JsonArray surfaces)
                                    throw Fault("观察缺少当前原生页面列表。");
                                var types = surfaces.Select(s => s?["Type"]?.GetValue<string>() ?? throw Fault("原生页面类型无效。")).ToHashSet(StringComparer.Ordinal);
                                var context = new JsonObject { ["actor"] = new JsonArray(pid, start, instance, key, role), ["server"] = guild.ServerKey, ["cycle"] = guild.CycleKey };
                                return new DailyStageFrame(frame.DeepClone().AsObject(), context, identity);
                            }
                        }
                    }
                }
            }
            catch (StageHostException e) { Diagnose(e.Message); throw; }
            catch (Exception e) when (e is InvalidOperationException or FormatException or System.Text.Json.JsonException) { Diagnose(e.Message); throw Fault("观察数据格式无效：" + e.Message); }
            if (elapsed.Elapsed >= timeout)
            {
                Diagnose(waitingReason);
                throw Fault(waitingReason);
            }
            await delay(interval);
        }
    }
}

public sealed record DailyStageFrame(JsonObject Frame, JsonObject Context, DailySnapshot? Daily = null);

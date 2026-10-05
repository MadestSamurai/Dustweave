using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Starts an identified managed queue and preserves unconfirmed receipts across upgrades.</summary>
public sealed class DailyManagedBootstrap
{
    private readonly string root; private readonly DailyCommandDriver driver; private readonly Func<Task<DailyStageFrame>> read; private readonly Func<bool> stopped; private bool begun;
    public DailyManagedBootstrap(string root, DailyCommandDriver driver, Func<Task<DailyStageFrame>> read, Func<bool> stopped)
    {
        this.root = root;
        this.driver = driver;
        this.read = read;
        this.stopped = stopped;
    }
    public async Task<JsonObject> BeginAsync(JsonObject context)
    {
        if (begun)
            throw new StageHostException("protocol", "队列已经开始，不重复初始化。");
        if (stopped())
            throw new StageHostException("stopped", "队列开始前已停止。");
        var observed = await read();
        if (!JsonNode.DeepEquals(observed.Context, context))
            throw new StageHostException("identity", "队列初始化现场与已观察身份不同。");
        if (observed.Frame["BridgeVersion"]?.GetValue<int>() != DailyStageObservation.BridgeVersion)
            throw new StageHostException("identity", "日常原生桥版本不匹配，未获取执行控制。");
        driver.EnsureBound(context);
        string id = Guid.NewGuid().ToString("N"), path = Path.Combine(root, "live", "queue-bootstrap", id, "result.json");
        var result = new JsonObject { ["id"] = id, ["context"] = context.DeepClone(), ["engine"] = "dotnet-bootstrap-v1", ["state"] = "prepared", ["actions"] = 0, ["commandTransport"] = "dotnet-driver-v1" };
        DailyJson.Write(path, result);
        try
        {
            var prior = await driver.HandleAsync(new()
            {
                ["operation"] = "read",
                ["channel"] = "live",
                ["name"] = "command.json"
            });
            if (prior["value"] != null)
                throw new StageHostException("pending", "取得控制前仍有原生命令，保留原输入和现场。");
            driver.Acquire("live");
            var outstanding = await driver.HandleAsync(new()
            {
                ["operation"] = "read",
                ["channel"] = "live",
                ["name"] = "command.json"
            });
            if (outstanding["value"] != null)
                throw new StageHostException("pending", "原生命令仍在队列中，保持暂停并核对原操作；不会清除或重放。");
            if (stopped())
                throw new StageHostException("stopped", "取得队列控制后已停止。");
            if (!JsonNode.DeepEquals((await driver.ObserveAsync()).Context, context))
                throw new StageHostException("identity", "队列取得控制后身份改变。");
            await driver.HandleAsync(new()
            {
                ["operation"] = "delete",
                ["channel"] = "live",
                ["name"] = "pause"
            });
            if (!JsonNode.DeepEquals((await driver.ObserveAsync()).Context, context))
                throw new StageHostException("identity", "解除暂停后身份改变。");
            begun = true;
            result["state"] = "ready";
            DailyJson.Write(path, result);
            return result;
        }
        catch (Exception error)
        {
            if (driver.HasControl)
                try
                {
                    await driver.HandleAsync(new()
                    {
                        ["operation"] = "write",
                        ["channel"] = "live",
                        ["name"] = "pause",
                        ["value"] = ""
                    });
                }
                catch (Exception e) when (e is IOException or StageHostException) { }
            result["state"] = "failed";
            result["error"] = error.Message;
            DailyJson.Write(path, result);
            throw;
        }
    }
    public bool NeedsLegacyReconciliation(JsonObject context, IEnumerable<string> excluded)
    {
        var roles = excluded.ToHashSet(StringComparer.Ordinal);
        string directory = Path.Combine(root, "live", "business");
        if (!Directory.Exists(directory))
            return false;
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable legacy business record");
            DailyManagedReconciliation.ValidateRecord(path, op);
            if (roles.Contains(op["role"]?.GetValue<string>() ?? "") || !DailyManagedBusiness.Pending(op))
                continue;
            if (JsonNode.DeepEquals(op["account"], context["actor"]![3]) && JsonNode.DeepEquals(op["player"], context["actor"]![4]) && JsonNode.DeepEquals(op["server"], context["server"]))
                return true;
        }
        return false;
    }
}



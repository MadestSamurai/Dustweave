using System.Diagnostics;
using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Visits the player's own room using original server response/cache proof and business journals.</summary>
public sealed class DailyRoomStage : IDailyManagedStage
{
    private readonly string root; private readonly DailyCommandDriver driver; private readonly Func<bool> stopped; private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay;
    private readonly JsonObject recipe;
    public DailyRoomStage(string root, DailyCommandDriver driver, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, JsonObject? recipe = null)
    {
        this.root = root;
        this.driver = driver;
        this.stopped = stopped;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.recipe = recipe ?? LoadRecipe();
    }
    public static JsonObject LoadRecipe()
    {
        using var s = typeof(DailyRoomStage).Assembly.GetManifestResourceStream("BD2Daily.room-flow.json") ?? throw new IOException("缺少小屋规则。");
        return JsonNode.Parse(s)!.AsObject();
    }
    private IEnumerable<JsonObject> Prior(JsonObject context)
    {
        string dir = Path.Combine(root, "live", "business");
        if (!Directory.Exists(dir))
            yield break;
        var a = context["actor"]!.AsArray();
        foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
            if (op["role"]?.GetValue<string>() != "room.info")
                continue;
            DailyManagedReconciliation.ValidateRecord(path, op);
            if (op["account"]?.GetValue<string>() != a[3]!.GetValue<string>() || op["player"]?.GetValue<string>() != a[4]!.GetValue<string>() || !JsonNode.DeepEquals(op["server"], context["server"]))
                continue;
            string id = op["id"]?.GetValue<string>() ?? "";
            if (!GuildStore.ValidId(id) || Path.GetFileNameWithoutExtension(path) != id)
                throw new InvalidDataException("Invalid room business ID");
            yield return op;
        }
    }
    public bool CanResume(DailyStageFrame frame) => DailyNavigationDecision.Types(frame.Frame).Contains("MyRoomUI") && Prior(frame.Context).Any(op => op["state"]?.GetValue<string>() is "prepared" or "dispatching" or "unknown" || op["state"]?.GetValue<string>() == "completed" && JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]));
    private async Task WaitTarget(string ui, string field)
    {
        double end = clock() + 40;
        while (clock() < end)
        {
            if (stopped())
                throw new StageHostException("stopped", "小屋操作前已停止");
            var f = (await driver.ObserveAsync()).Frame;
            var rows = DailyNavigationDecision.Rows(f).Where(r => r["Type"]?.GetValue<string>() == ui).ToArray();
            if (rows.Length == 1 && (rows[0]["InputReady"]?.GetValue<bool>() ?? true) && rows[0]["Targets"]!.AsArray().Any(t => t?["Field"]?.GetValue<string>() == field && t["Enabled"]?.GetValue<bool>() == true))
                return;
            await delay(TimeSpan.FromMilliseconds(200));
        }
        throw new StageHostException("adapter", "小屋页面输入尚未就绪，未提交新操作。");
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        if (stopped())
            throw new StageHostException("stopped", "小屋流程已停止");
        var observed = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(observed.Context, context))
            throw new StageHostException("identity", "小屋现场与队列不一致");
        var prior = Prior(context).ToArray();
        if (prior.Any(p => p["state"]?.GetValue<string>() is "prepared" or "dispatching" or "unknown"))
            throw new StageHostException("pending", "已有小屋操作待只读核账，不重复访问。");
        var completed = prior.FirstOrDefault(p => p["state"]?.GetValue<string>() == "completed" && JsonNode.DeepEquals(p["cycle"], context["cycle"]));
        string flowId = Guid.NewGuid().ToString("N"), flowPath = Path.Combine(root, "live", "workflows", flowId, "result.json");
        var steps = new JsonArray();
        var flow = new JsonObject { ["state"] = "running", ["engine"] = "dotnet-room-v1", ["version"] = DailyIdentity.Version, ["role"] = "room.info", ["before"] = observed.Frame.DeepClone(), ["steps"] = steps, ["recipe"] = recipe.DeepClone() };
        DailyJson.Write(flowPath, flow);
        JsonObject? op = null;
        try
        {
            if (completed != null)
            {
                // A completed row is reusable across reconnects, but it must still carry the original native proof.
                DailyEvidence.VerifyRoom(completed["before"]!.AsObject(), completed["events"]!.AsArray(), completed["after"]!.AsObject());
                flow["business"] = new JsonObject { ["state"] = "skipped_recorded", ["id"] = completed["id"]!.DeepClone(), ["actions"] = 0 };
                DailyJson.Write(flowPath, flow);
            }
            else
            {
                var action = recipe["action"]!.DeepClone().AsObject();
                await WaitTarget(action["ui"]!.GetValue<string>(), action["field"]!.GetValue<string>());
                var before = await driver.EvidenceAsync(["room"]);
                if (!before["Taps"]!.AsArray().Any(t => t?.GetValue<string>() == "room.info"))
                    throw new StageHostException("adapter", "Required room observer is not active");
                string id = Guid.NewGuid().ToString("N"), opPath = Path.Combine(root, "live", "business", id + ".json");
                op = new()
                {
                    ["id"] = id,
                    ["role"] = "room.info",
                    ["account"] = context["actor"]![3]!.DeepClone(),
                    ["player"] = context["actor"]![4]!.DeepClone(),
                    ["server"] = context["server"]!.DeepClone(),
                    ["cycle"] = context["cycle"]!.DeepClone(),
                    ["state"] = "prepared",
                    ["at"] = driver.UtcTicks,
                    ["before"] = before.DeepClone(),
                    ["action"] = action.DeepClone(),
                    ["roles"] = new JsonArray("room.info"),
                    ["day_cycle"] = context["cycle"]!.DeepClone(),
                    ["proof_version"] = 1,
                    ["stage"] = "room",
                    ["engine"] = "dotnet-room-v1"
                };
                DailyJson.Write(opPath, op);
                try
                {
                    if (stopped())
                        throw new StageHostException("stopped", "Stopped before room visit");
                    op["state"] = "dispatching";
                    DailyJson.Write(opPath, op);
                    action["reason"] = "business:" + id + "|" + action["reason"]!.GetValue<string>();
                    try
                    {
                        var done = await driver.SendObservedAsync(action);
                        op["command_id"] = done["id"]!.DeepClone();
                    }
                    catch (DailyStepException e) when (e.Kind == "rejected") { op["state"] = "rejected"; throw; }
                    catch (DailyStepException e) { op["ui_error"] = e.Message; if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone(); }
                    DailyJson.Write(opPath, op);
                    double end = clock() + 20;
                    while (clock() < end)
                    {
                        var events = driver.CollectEvents("room.info", DailyEvidence.Integer(op["at"]));
                        if (events.Any(e => e?["Kind"]?.GetValue<string>() == "response"))
                        {
                            var after = await driver.EvidenceAsync(["room"]);
                            op["last_observation"] = after.DeepClone();
                            var result = DailyEvidence.VerifyRoom(before, events, after);
                            op["events"] = events;
                            op["after"] = after;
                            op["result"] = result;
                            op["state"] = "completed";
                            DailyJson.Write(opPath, op);
                            break;
                        }
                        await delay(TimeSpan.FromMilliseconds(200));
                    }
                    if (op["state"]!.GetValue<string>() != "completed")
                        throw new StageHostException("pending", "No native room response; no retry");
                }
                catch (Exception error)
                {
                    if (op["state"]?.GetValue<string>() is not ("completed" or "rejected"))
                        op["state"] = op["state"]?.GetValue<string>() == "prepared" ? "rejected" : "unknown";
                    op["error"] = error.Message;
                    op["events"] = driver.CollectEvents("room.info", DailyEvidence.Integer(op["at"]));
                    DailyJson.Write(opPath, op);
                    if (op["state"]?.GetValue<string>() == "unknown")
                        throw new StageHostException("pending", error.Message);
                    throw;
                }
                flow["business"] = new JsonObject { ["state"] = op["state"]!.DeepClone(), ["id"] = op["id"]!.DeepClone(), ["result"] = op["result"]!.DeepClone() };
                DailyJson.Write(flowPath, flow);
            }
            var current = (await driver.ObserveAsync()).Frame;
            if (DailyNavigationDecision.Types(current).Contains("MyRoomUI"))
            {
                var back = recipe["return_action"]!.DeepClone().AsObject();
                back.Remove("if_present");
                await WaitTarget(back["ui"]!.GetValue<string>(), back["field"]!.GetValue<string>());
                steps.Add(await driver.SendObservedAsync(back));
            }
            var afterFrame = (await driver.ObserveAsync()).Frame;
            if (!DailyNavigationDecision.Types(afterFrame).Contains("MenuUI") || DailyNavigationDecision.Blockers(afterFrame, "MenuUI", DailyNavigationPolicy.Load()).Length > 0)
                throw new StageHostException("adapter", "小屋访问已核账，仍需完成页面收尾；不重新访问。");
            flow["state"] = "completed";
            flow["after"] = afterFrame.DeepClone();
            DailyJson.Write(flowPath, flow);
            return flow;
        }
        catch (Exception error) { flow["state"] = "paused"; flow["error"] = error.Message; DailyJson.Write(flowPath, flow); if (op?["state"]?.GetValue<string>() == "completed") throw new StageHostException("adapter", "小屋访问已确认，收尾未完成：" + error.Message); throw; }
    }
}


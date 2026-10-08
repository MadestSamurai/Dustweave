using Dustweave;
using System.Text.Json.Nodes;
static class QueueEngineCases
{
    private static readonly string Account = new('a', 64);
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            cases.Add(label);
        }
        string Root()
        {
            var r = Path.Combine(output, "engine-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(r);
            return r;
        }
        async Task<JsonObject> Run(FakeHost host, string root, string[]? tasks = null, string? resume = null, string? retry = null, DailyPreferences? prefs = null, Func<bool>? pause = null) => await new DailyQueueEngine(host, pause ?? (() => false), () => prefs ?? new DailyPreferences { Trade = new() { Enabled = true } }).RunAsync(new(root, Account, Path.Combine(root, "queue-ui.json"), resume, false, tasks, retry));
        var root = Root();
        var host = new FakeHost();
        var record = await Run(host, root, ["mail", "trade"]);
        Check(State(record) == "completed" && host.Executed.SequenceEqual(new[] { "mail", "trade" }), "native queue owns ordering and completion");
        Check(record["engine"]!.GetValue<string>() == "dotnet-v1" && File.Exists(DailyQueueEngine.RecordPath(root, record["id"]!.GetValue<string>())), "native queue persists engine ownership");
        host.Executed.Clear();
        await Run(host, root, resume: record["id"]!.GetValue<string>());
        Check(host.Executed.Count == 0, "native resume never repeats completed stages");
        host = new();
        var prefs = new DailyPreferences();
        prefs.Stages.Mail = false;
        prefs.Trade.Enabled = true;
        record = await Run(host, Root(), ["mail", "trade"], prefs: prefs);
        Check(host.Executed.SequenceEqual(new[] { "trade" }) && Stage(record, "mail") == "skipped", "disabled native stage does not navigate or execute");
        host = new();
        host.Fail["execute:mail"] = "pending";
        record = await Run(host, Root(), ["mail", "trade"]);
        Check(Stage(record, "mail") == "recovery_required" && Stage(record, "trade") == "completed", "uncertain settlement isolated from unrelated stage");
        host = new();
        host.Fail["navigate:mail"] = "adapter";
        host.Safe = true;
        record = await Run(host, Root(), ["mail", "trade"]);
        Check(Stage(record, "mail") == "blocked" && host.Executed.SequenceEqual(new[] { "trade" }) && State(record) == "partial", "safe navigation recovery continues without retrying failed stage");
        host = new();
        host.Fail["navigate:mail"] = "adapter";
        record = await Run(host, Root(), ["mail", "trade"]);
        Check(State(record) == "paused" && host.Executed.Count == 0 && Stage(record, "trade") == "pending", "unsafe navigation recovery stops queue");
        host = new();
        host.Fail["execute:mail"] = "transport";
        record = await Run(host, Root(), ["mail", "trade"]);
        Check(State(record) == "paused" && Stage(record, "mail") == "recovery_required" && Stage(record, "trade") == "pending", "broken transport preserves uncertainty and halts further input");
        host = new();
        host.Fail["execute:mail"] = "identity";
        record = await Run(host, Root(), ["mail", "trade"]);
        Check(State(record) == "paused" && host.Executed.SequenceEqual(new[] { "mail" }), "identity changed inside a stage cannot run next stage");
        host = new();
        host.Fail["execute:mail"] = "reconciled";
        host.FailOnce = true;
        record = await Run(host, Root(), ["mail"]);
        Check(State(record) == "completed" && host.Executed.Count == 2 && host.Calls.Count(x => x == "navigate:mail") == 2, "resolved business permits exactly one fresh plan");
        host = new();
        host.Fail["execute:mail"] = "reconciled";
        record = await Run(host, Root(), ["mail"]);
        Check(Stage(record, "mail") == "blocked" && host.Executed.Count == 2, "repeated replanning cannot loop");
        host = new();
        bool stop = false;
        host.After = (op, stage) => { if (op == "navigate") stop = true; };
        record = await Run(host, Root(), ["mail"], pause: () => stop);
        Check(State(record) == "paused" && host.Executed.Count == 0, "stop between navigation and execution dispatches nothing");
        host = new();
        root = Root();
        record = await Run(host, root, ["mail", "trade"], pause: () => true);
        Check(State(record) == "paused" && !host.Calls.Contains("begin:"), "stop before begin leaves worker paused");
        host = new(); root = Root();
        record = await Run(host, root, ["mail", "trade"], pause: () => true);
        string reconnectId = TextForTest(record["id"]);
        host.Context["actor"]![2] = "reloaded-observer";
        Check(DailyQueueEngine.CanReconnectPausedQueue(record, host.Context), "clean paused queue permits only observer identity renewal");
        var resumed = await Run(host, root, resume: reconnectId);
        Check(State(resumed) == "completed" && TextForTest(resumed["id"]) == reconnectId && resumed["connection_history"] is JsonArray { Count: 1 }, "clean same-game reconnect keeps original queue and records previous connection");
        Check(host.Calls.IndexOf("reconcile:") < host.Calls.IndexOf("execute:mail"), "observer reconnect still reconciles before gameplay");
        foreach (int field in new[] { 0, 1, 3, 4 })
        {
            var changed = host.Context.DeepClone().AsObject(); changed["actor"]![field] = "different";
            Check(!DailyQueueEngine.CanReconnectPausedQueue(record, changed), "reconnect rejects changed game or account field " + field);
        }
        foreach (string field in new[] { "server", "cycle" })
        {
            var changed = host.Context.DeepClone().AsObject(); changed[field] = "different";
            Check(!DailyQueueEngine.CanReconnectPausedQueue(record, changed), "reconnect rejects changed " + field);
        }
        foreach (string state in new[] { "running", "recovery_required" })
        {
            var uncertain = record.DeepClone().AsObject(); uncertain["items"]![0]!["state"] = state;
            Check(!DailyQueueEngine.CanReconnectPausedQueue(uncertain, host.Context), "reconnect cannot silently reuse " + state + " operation");
        }
        static string TextForTest(JsonNode? value) => value?.GetValue<string>() ?? "";
        // Import an old Python schema-1 interrupted record and scope recovery to its ledger.
        root = Root();
        string id = new('c', 32);
        var old = Old(id, new FakeHost().Context, "mail", "trade");
        old["items"]![0]!["state"] = "running";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new()
        {
            Unresolved = "mail"
        };
        record = await Run(host, root, resume: id);
        Check(Stage(record, "mail") == "recovery_required" && host.Executed.SequenceEqual(new[] { "trade" }), "old Python interrupted record requires scoped reconciliation");
        host = new();
        record = await Run(host, root, resume: id);
        Check(Stage(record, "mail") == "completed" && host.Executed.SequenceEqual(new[] { "mail" }), "resolved old operation resumes without repeating completed sibling");
        root = Root();
        old = Old(id, new FakeHost().Context, "square", "mail");
        old["items"]![0]!["state"] = "recovery_required";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new()
        {
            Unresolved = "square",
            RecoverInStage = true
        };
        record = await Run(host, root, resume: id);
        Check(Stage(record, "square") == "completed" && host.Executed.SequenceEqual(new[] { "square", "mail" }), "square navigation recovers against native state inside owning stage");
        root = Root();
        old = Old(id, new FakeHost().Context, "square", "mail");
        old["items"]![0]!["state"] = "recovery_required";
        old["items"]![1]!["state"] = "completed";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new()
        {
            Unresolved = "square",
            RecoverInStage = true
        };
        host.Context["actor"]![2] = "new-bridge";
        record = await Run(host, root, ["square"], retry: id);
        Check(Stage(record, "square") == "completed" && host.Executed.SequenceEqual(new[] { "square" }), "selected square recovery works across bridge updates without replaying mail");
        root = Root();
        old = Old(id, new FakeHost().Context, "event_rewards", "mail");
        old["items"]![0]!["state"] = "recovery_required";
        old["items"]![1]!["state"] = "completed";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new()
        {
            Unresolved = "event_rewards"
        };
        record = await Run(host, root, ["event_rewards"], retry: id);
        Check(Stage(record, "event_rewards") == "recovery_required" && host.Executed.Count == 0 && !host.Calls.Contains("navigate:event_rewards"), "selected uncertain stage reconciles without navigation or replay when unresolved");
        host = new();
        host.Context["actor"]![2] = "updated-bridge";
        record = await Run(host, root, ["event_rewards"], retry: id);
        Check(Stage(record, "event_rewards") == "completed" && host.Executed.SequenceEqual(new[] { "event_rewards" }) && host.Calls.IndexOf("reconcile:") < host.Calls.IndexOf("execute:event_rewards"), "selected reconciled stage continues across bridge upgrade without completed sibling");
        host = new()
        {
            Unresolved = "event_rewards",
            RecoverInStage = true
        };
        record = await Run(host, root, ["event_rewards"], retry: id);
        Check(Stage(record, "event_rewards") == "completed", "selected quiz can use registered owned continuation");
        root = Root();
        old = Old(id, new FakeHost().Context, "mail", "mirror");
        old["items"]![1]!["state"] = "running";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new()
        {
            Unresolved = "mirror",
            Owner = "mirror"
        };
        record = await Run(host, root, resume: id);
        Check(host.Executed.SequenceEqual(new[] { "mirror", "mail" }), "own unsettled battle settles before unrelated navigation");
        root = Root();
        old = Old(id, new FakeHost().Context, "mail", "trade");
        old["items"]![0]!["state"] = "completed";
        old["items"]![1]!["state"] = "blocked";
        DailyJson.Write(DailyQueueEngine.RecordPath(root, id), old);
        host = new();
        host.Context["actor"]![0] = 99;
        record = await Run(host, root, ["trade"], retry: id);
        Check(host.Executed.SequenceEqual(new[] { "trade" }) && record["items"]![0]!["carried_forward"]!.GetValue<bool>(), "selected retry permits new process and carries other evidence");
        Check(DailyJson.TryRead<JsonObject>(DailyQueueEngine.RecordPath(root, id))!["items"]![1]!["state"]!.GetValue<string>() == "blocked", "retry never rewrites source record");
        async Task Reject(FakeHost h, string r, string[]? names, string? resume, string? retry, string label)
        {
            bool rejected = false;
            try
            {
                await Run(h, r, names, resume, retry);
            }
            catch (Exception) { rejected = true; }
            Check(rejected && h.Executed.Count == 0 && !h.Calls.Contains("begin:"), label);
        }
        await Reject(new(), root, ["mail"], null, id, "completed stage cannot be replayed through retry API");
        host = new();
        host.Context["cycle"] = "next";
        await Reject(host, root, ["trade"], null, id, "retry cannot cross daily reset");
        host = new();
        host.Context["actor"]![4] = "another";
        await Reject(host, root, ["trade"], null, id, "retry cannot cross player identity");
        host = new();
        host.Context["actor"]![0] = 99;
        await Reject(host, root, null, id, null, "explicit resume cannot cross game process");
        await Reject(new(), Root(), ["missing"], null, null, "unknown stage rejected before unpause");
        await Reject(new(), root, ["trade", "trade"], null, id, "duplicate retry rejected before unpause");
        host = new();
        host.Context["actor"]![3] = new string('b', 64);
        await Reject(host, Root(), ["mail"], null, null, "wrong account rejected before journal or begin");
        host = new();
        root = Root();
        host.After = (op, stage) => { if (op == "execute") host.Context["cycle"] = "next"; };
        record = await Run(host, root, ["mail", "trade"]);
        Check(Stage(record, "mail") == "completed" && State(record) == "paused" && Stage(record, "trade") == "pending", "reset between stages preserves confirmed work and stops");
        host = new();
        record = await Run(host, Root(), ["weekly_steal"], prefs: new DailyPreferences { Weekly = new() { Steal = true } });
        Check(State(record) == "completed" && host.Executed.SequenceEqual(new[] { "weekly_steal" }), "independent theft runs without collection");
        host = new();
        record = await Run(host, Root(), prefs: new DailyPreferences());
        Check(!host.Executed.Contains("weekly_steal") && Stage(record, "weekly_steal") == "skipped", "default-off theft remains a selectable normal stage");
        Check(!record["items"]!.AsArray().Any(i => i!["task"]!.GetValue<string>() == "tactics"), "default queue omits unavailable optional tasks");
        var weekly = new[] { "weekly_mainline", "weekly_npc", "weekly_steal" };
        for (int mask = 1; mask < 8; mask++)
        {
            var chosen = weekly.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            root = Root();
            host = new();
            var chosenPrefs = new DailyPreferences { Weekly = new() { Mainline = true, Npc = true, Steal = true } };
            record = await Run(host, root, chosen, prefs: chosenPrefs);
            Check(State(record) == "completed" && host.Executed.Count == 1 && host.Calls.Count(x => x.StartsWith("navigate:")) == 1 && host.Members.SequenceEqual(chosen), "weekly selection shares one dispatch " + mask);
            Check(chosen.All(s => Stage(record, s) == "completed" && record["items"]!.AsArray().Single(i => i!["task"]!.GetValue<string>() == s)!["progress"]?["completed"]?.GetValue<int>() == 1), "weekly progress persists independently " + mask);
        }
        host = new()
        {
            PartialCollection = true
        };
        prefs = new()
        {
            Weekly = new()
            {
                Mainline = true,
                Npc = true,
                Steal = true
            }
        };
        record = await Run(host, Root(), weekly, prefs: prefs);
        Check(Stage(record, "weekly_mainline") == "partial" && Stage(record, "weekly_npc") == "completed" && Stage(record, "weekly_steal") == "completed", "quota does not overwrite other shared stage results");
        root = Root();
        host = new()
        {
            PartialCollection = true
        };
        record = await Run(host, root, weekly, prefs: prefs);
        host = new();
        record = await Run(host, root, ["weekly_mainline"], retry: record["id"]!.GetValue<string>(), prefs: prefs);
        Check(host.Members.SequenceEqual(new[] { "weekly_mainline" }) && record["items"]!.AsArray().Single(i => i!["task"]!.GetValue<string>() == "weekly_npc")!["carried_forward"]!.GetValue<bool>(), "weekly catchup does not repeat completed NPC/theft members");
        host = new();
        root = Root();
        record = await Run(host, root, weekly, prefs: prefs);
        host.Executed.Clear();
        host.Members.Clear();
        await Run(host, root, resume: record["id"]!.GetValue<string>(), prefs: prefs);
        Check(host.Executed.Count == 0, "completed shared route never repeats on resume");
        host = new();
        host.Fail["execute:weekly_mainline"] = "transport";
        record = await Run(host, Root(), weekly, prefs: prefs);
        Check(weekly.All(s => Stage(record, s) == "recovery_required"), "interrupted shared execution marks all selected rows uncertain");
        host = new()
        {
            Safe = true,
            SettledNpc = true
        };
        host.Fail["execute:weekly_mainline"] = "pending";
        record = await Run(host, Root(), weekly, prefs: prefs);
        Check(Stage(record, "weekly_npc") == "completed" && Stage(record, "weekly_mainline") == "recovery_required" && Stage(record, "weekly_steal") == "recovery_required", "shared native uncertainty preserves independently verified NPC completion and leaves other scopes pending");

        root = Root(); host = new();
        old = Old(id, host.Context, "weekly_sichuan", "mail");
        old["items"]![0]!["state"] = "skipped";
        old["items"]![0]!["result"] = new JsonObject { ["state"] = "skipped", ["reason"] = "weekly_minigame_complete" };
        old["items"]![1]!["state"] = "completed";
        var oldPath = DailyQueueEngine.RecordPath(root, id);
        DailyJson.Write(oldPath, old);
        var sourceText = File.ReadAllText(oldPath);
        var view = new DailyQueueSession(root, new PackagedDailyQueueExecutor(AppContext.BaseDirectory)).ReadView();
        DailyJson.Write(Path.Combine(root, "queue-ui.json"), new { account = Account, record = oldPath });
        view = new DailyQueueSession(root, new PackagedDailyQueueExecutor(AppContext.BaseDirectory)).ReadView();
        Check(view.Stages[0].State == "partial" && DailyQueueRetry.CanSelect(view.Stages[0]), "UI permits retry of legacy wrong minigame completion");
        record = await Run(host, root, ["weekly_sichuan"], retry: id);
        Check(host.Executed.SequenceEqual(new[] { "weekly_sichuan" }) && Stage(record, "mail") == "completed" && File.ReadAllText(oldPath) == sourceText, "retry engine accepts invalidated play mission without replaying siblings or overwriting history");
        host = new();
        record = await Run(host, root, resume: id);
        Check(host.Executed.SequenceEqual(new[] { "weekly_sichuan" }), "resume rechecks legacy wrong completion against game instead of carrying it forward");
        // A business call must observe a durable running record before issuing any action.
        host = new();
        root = Root();
        bool durable = false;
        host.After = (op, stage) => { if (op != "execute") return; var head = DailyJson.TryRead<JsonObject>(Path.Combine(root, "queue-ui.json"))!; var saved = DailyJson.TryRead<JsonObject>(head["record"]!.GetValue<string>())!; durable = Stage(saved, stage) == "running"; };
        await Run(host, root, ["mail"]);
        Check(durable, "running journal is flushed before executing business");
    }
    static string State(JsonObject r) => r["state"]!.GetValue<string>();
    static string Stage(JsonObject r, string task) => r["items"]!.AsArray().First(i => i!["task"]!.GetValue<string>() == task)!["state"]!.GetValue<string>();
    static JsonObject Old(string id, JsonObject context, params string[] tasks) => new() { ["schema"] = 1, ["id"] = id, ["context"] = context.DeepClone(), ["state"] = "paused", ["items"] = new JsonArray(tasks.Select(t => (JsonNode)new JsonObject { ["task"] = t, ["state"] = "pending" }).ToArray()) };
    private sealed class FakeHost : IDailyStageHost, IDailyStageProgressHost, IDailyWeeklyCompletionHost
    {
        public event Action<string, JsonObject>? StageProgress; public List<string> Members = []; public bool PartialCollection, SettledNpc;
        public Task<JsonObject> SettleWeeklyAsync(IReadOnlyList<string> stages, JsonObject context) => Task.FromResult(SettledNpc && stages.Contains("weekly_npc") ? new JsonObject { ["weekly_npc"] = new JsonObject { ["state"] = "completed", ["source"] = "TodayQuestInfoResponse" } } : new JsonObject());
        public JsonObject Context = JsonNode.Parse("{\"actor\":[1,2,\"fixture\",\"" + Account + "\",\"player\"],\"server\":\"fixture\",\"cycle\":\"today\"}")!.AsObject();
        public List<string> Executed = [], Calls = []; public Dictionary<string, string> Fail = new(); public bool Safe, FailOnce, RecoverInStage; public string? Unresolved, Owner; public Action<string, string>? After;
        public Task<JsonObject> CallAsync(string operation, string? stage = null, JsonObject? arguments = null)
        {
            string key = operation + ":" + stage;
            Calls.Add(key);
            if (operation == "execute")
                Executed.Add(stage!);
            After?.Invoke(operation, stage ?? "");
            if (Fail.TryGetValue(key, out var kind))
            {
                if (FailOnce)
                    Fail.Remove(key);
                throw new StageHostException(kind, "fixture: " + key);
            }
            if (operation == "execute" && arguments?["stages"] is JsonArray members)
            {
                var results = new JsonObject();
                foreach (var member in members)
                {
                    var name = member!.GetValue<string>();
                    Members.Add(name);
                    StageProgress?.Invoke(name, new JsonObject { ["detail"] = "共享地图 · 分项进度", ["completed"] = 1 });
                    results[name] = new JsonObject { ["state"] = PartialCollection && name == "weekly_mainline" ? "partial" : "completed", ["detail"] = name };
                }
                return Task.FromResult(new JsonObject { ["state"] = "completed", ["stages"] = results });
            }
            JsonObject result = operation switch
            {
                "observe" => new() { ["context"] = Context.DeepClone(), ["adapters"] = new JsonArray(DailyStageCatalog.Selectable.Select(s => (JsonNode)JsonValue.Create(s.Id)!).ToArray()), ["owners"] = Owner == null ? new JsonArray() : new JsonArray(Owner) },
                "reconcile" => new() { ["unresolved"] = Unresolved == null ? new JsonArray() : new JsonArray(new JsonObject { ["stages"] = new JsonArray(Unresolved), ["recover_in_stage"] = RecoverInStage }) },
                "recover" => new() { ["safe"] = Safe },
                _ => new() { ["state"] = "completed" }
            };
            return Task.FromResult(result);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

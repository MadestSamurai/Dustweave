using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Shared .NET business vocabulary. Missing required evidence is never converted into success.</summary>
public static class DailyData
{
    public static string S(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "";
    public static long N(JsonNode? value) => value == null ? 0 : value is JsonValue v && v.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : DailyEvidence.Integer(value);
    public static int I(JsonNode? value) => checked((int)N(value));
    public static bool B(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public static bool Flag(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out var b) ? b : N(value) != 0;
    public static JsonNode? Copy(JsonNode? value) => value?.DeepClone();
    public static JsonArray Array(IEnumerable<JsonNode?> values) => new(values.Select(Copy).ToArray());
    public static JsonObject O(params (string Key, object? Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (key, value) in fields)
            o[key] = value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value);
        return o;
    }
    public static JsonNode? R(JsonObject state, string id, string path) => DailyEvidence.Reading(state, id, path);
    public static JsonObject State(JsonObject state, string id, string path) => R(state, id, path)?.AsObject() ?? throw new InvalidDataException("Missing native state: " + id + "." + path);
    public static JsonObject[] Rows(JsonNode? node) => node is JsonArray a ? a.Select(n => n?.AsObject() ?? throw new InvalidDataException("Null native row")).ToArray() : throw new InvalidDataException("Missing native rows");
    public static JsonObject[] Cached(JsonObject state, string id, string path = "_items")
    {
        int count = I(R(state, id, "Count"));
        var raw = R(state, id, path) as JsonArray ?? throw new InvalidDataException("Missing cache: " + id);
        if (count < 0 || raw.Count < count || raw.Take(count).Any(n => n is not JsonObject))
            throw new InvalidDataException("Incomplete native cache: " + id);
        return raw.Take(count).Select(n => n!.AsObject()).ToArray();
    }
    public static IEnumerable<JsonObject> Readings(JsonObject evidence, string id) => Rows(evidence["Readings"]).Where(r => S(r["Id"]) == id && S(r["Error"]) == "").Select(DailyEvidence.Values);
    public static void Require(bool value, string error)
    {
        if (!value)
            throw new InvalidDataException(error);
    }
    public static void Identity(JsonObject before, JsonObject after)
    {
        Require(JsonNode.DeepEquals(before["Config"], after["Config"]), "Observation configuration changed");
        Require(DailyEvidence.SameActor(before["Frame"]!.AsObject(), after["Frame"]!.AsObject()), "Business account/process changed");
    }
    public static JsonObject? Response(string role, JsonArray events, JsonObject before, JsonObject after, bool optional = false, int requestEntries = 1)
    {
        Identity(before, after);
        var selected = Rows(events).Where(e => S(e["Role"]) == role).OrderBy(e => N(e["Sequence"])).ToArray();
        if (optional && selected.Length == 0)
            return null;
        var requests = selected.Where(e => S(e["Kind"]) == "request").ToArray();
        var replies = selected.Where(e => S(e["Kind"]) == "response").ToArray();
        Require(requests.Length == requestEntries && replies.Length == 1, "Native response count differs: " + role);
        Require(selected.All(e => DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())), "Native response actor differs");
        var reply = replies[0];
        Require(S(reply["Error"]) == "" && N(reply["ErrorCode"]) == 0 && B(reply["Accepted"]), "Native request rejected: " + role);
        Require(requests.Max(e => N(e["Sequence"])) < N(reply["Sequence"]), "Native request/response order differs");
        return DailyEvidence.Values(reply);
    }
    public static JsonObject[] Responses(string role, JsonArray events, JsonObject before, JsonObject after, int minimum = 1, int maximum = int.MaxValue, bool alternating = false)
    {
        Identity(before, after);
        var selected = Rows(events).Where(e => S(e["Role"]) == role).OrderBy(e => N(e["Sequence"])).ToArray();
        var requests = selected.Where(e => S(e["Kind"]) == "request").ToArray();
        var replies = selected.Where(e => S(e["Kind"]) == "response").ToArray();
        Require(requests.Length == replies.Length && replies.Length >= minimum && replies.Length <= maximum, "Native chain incomplete: " + role);
        Require(selected.All(e => DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())), "Mixed native response actor");
        for (int i = 0; i < replies.Length; i++)
        {
            Require(N(requests[i]["Sequence"]) < N(replies[i]["Sequence"]), "Native response precedes request");
            Require(S(replies[i]["Error"]) == "" && N(replies[i]["ErrorCode"]) == 0 && B(replies[i]["Accepted"]), "Native rejection: " + role);
        }
        if (alternating)
            Require(selected.Select(e => S(e["Kind"])).SequenceEqual(Enumerable.Range(0, replies.Length).SelectMany(_ => new[] { "request", "response" })), "Interleaved native requests");
        return replies.Select(DailyEvidence.Values).ToArray();
    }
}

/// <summary>One stage invocation owns a context, settings snapshot and short-lived server progress proof.</summary>
public sealed class DailyWorkflow
{
    private readonly DailyRuleData? ruleData;
    private readonly Func<string, string, IReadOnlyDictionary<string, string[]>, Task<DailyRuleData>>? prepareTables;
    private readonly List<DailyRuleData> extensionRules = [];
    public string Root
    {
        get;
    }
    public string Directory
    {
        get;
    }
    public JsonObject Context
    {
        get;
    }
    public DailyPreferences Settings
    {
        get;
    }
    public DailyCommandDriver Driver
    {
        get;
    }
    public DailyManagedBusiness Business
    {
        get;
    }
    public DailyStageNavigation Navigation
    {
        get;
    }
    public Func<string, string?, JsonObject?, Task<JsonObject>> Relay
    {
        get;
    }
    private readonly Dictionary<string, JsonObject[]> tables = new(StringComparer.Ordinal); private readonly Dictionary<string, Dictionary<long, JsonObject>> indexes = new(StringComparer.Ordinal); private readonly Func<bool> stopped; private readonly Dictionary<string, DailyBusinessProof> proofs; private readonly Action<string> report; private double refreshedAt = double.NegativeInfinity;
    public double Time => Driver.MonotonicTime;
    public async Task Delay(int milliseconds)
    {
        Check();
        await Driver.DelayAsync(TimeSpan.FromMilliseconds(milliseconds));
        Check();
    }
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public DailyWorkflow(string root, string directory, JsonObject context, DailyCommandDriver driver, DailyManagedBusiness business, DailyStageNavigation navigation, IEnumerable<DailyBusinessProof> proofs, Func<bool> stopped, Func<string, string?, JsonObject?, Task<JsonObject>> relay, Action<string>? report = null, DailyRuleData? ruleData = null, Func<string, string, IReadOnlyDictionary<string, string[]>, Task<DailyRuleData>>? prepareTables = null)
    {
        this.prepareTables = prepareTables;
        this.ruleData = ruleData;
        Root = root;
        Directory = directory;
        Context = context.DeepClone().AsObject();
        Driver = driver;
        Business = business;
        Navigation = navigation;
        this.proofs = proofs.ToDictionary(p => p.Role);
        this.stopped = stopped;
        Relay = relay;
        this.report = report ?? (_ => { });
        Settings = new DailyPreferenceStore(root).Read(DailyData.S(context["actor"]![3]));
        Settings.Validate();
    }
    public void Check()
    {
        ruleData?.AssertReady();
        foreach (var rules in extensionRules)
            rules.AssertReady();
        if (stopped())
            throw new StageHostException("stopped", "日常已停止，当前操作和核账记录已保留。");
    }
    public async Task<DailyRuleData> PrepareTables(string name, string source, IReadOnlyDictionary<string, string[]> groups)
    {
        await Observe();
        var result = await (prepareTables?.Invoke(name, source, groups) ?? throw new StageHostException("adapter", "Current host cannot prepare extension rule data"));
        extensionRules.Add(result);
        await Observe();
        return result;
    }
    public async Task<DailyStageFrame> Observe()
    {
        Check();
        var frame = await Driver.ObserveAsync();
        if (!JsonNode.DeepEquals(frame.Context, Context))
            throw new StageHostException("identity", "账号、进程、连接或周期已经变化。");
        return frame;
    }
    public async Task<JsonObject> Evidence(params string[] prefixes)
    {
        await Observe();
        return await Driver.EvidenceAsync(prefixes);
    }
    public async Task<JsonObject> Step(JsonObject action)
    {
        await Observe();
        return await Driver.SendObservedAsync(action);
    }
    public Task<JsonObject> Step(string ui, string? field = null, string? expect = null, string? absent = null, string? operation = null, int value = 0, string reason = "", bool back = false)
    {
        var a = new JsonObject { ["ui"] = ui, ["reason"] = reason };
        if (field != null)
            a["field"] = field;
        if (expect != null)
            a["expect"] = expect;
        if (absent != null)
            a["absent"] = absent;
        if (operation != null)
        {
            a["operation"] = operation;
            a["value"] = value;
        }
        if (back)
            a["back"] = true;
        return Step(a);
    }
    public async Task<bool> Has(string ui) => DailyNavigationDecision.Types((await Observe()).Frame).Contains(ui);
    public async Task Wait(Func<DailyStageFrame, bool> ready, double seconds = 25, string error = "页面没有推进")
    {
        using var diagnostic = Driver.Diagnostics.Scope("wait", DailyData.O(("reason", error), ("seconds", seconds)));
        double end = Time + seconds;
        while (Time < end)
        {
            var frame = await Observe();
            if (ready(frame))
                return;
            await Delay(150);
        }
        throw new StageHostException("adapter", error);
    }
    public async Task<JsonObject> WaitEvidence(string[] prefixes, Func<JsonObject, bool> ready, double seconds = 25, string error = "原生状态没有推进")
    {
        using var diagnostic = Driver.Diagnostics.Scope("wait", DailyData.O(("reason", error), ("seconds", seconds), ("prefixes", prefixes)));
        double end = Time + seconds;
        string last = "";
        JsonObject? lastEvidence = null;
        while (Time < end)
        {
            var e = await Evidence(prefixes);
            lastEvidence = e;
            try
            {
                if (ready(e))
                    return e;
            }
            catch (InvalidDataException ex) { last = ex.Message; }
            await Delay(150);
        }
        Driver.Diagnostics.Event("evidence_timeout", DailyData.O(("reason", error), ("prefixes", prefixes), ("last_error", last), ("last_evidence", lastEvidence)));
        throw new StageHostException("adapter", error + (last.Length > 0 ? "：" + last : ""));
    }
    public async Task Target(string ui, string field, double seconds = 25) => await Wait(f => DailyNavigationDecision.Rows(f.Frame).Any(r => DailyData.S(r["Type"]) == ui && DailyNavigationDecision.ReadyInput(r) && DailyData.Rows(r["Targets"]).Any(t => DailyData.S(t["Field"]) == field && DailyData.B(t["Enabled"]))), seconds, "页面按钮尚未就绪：" + ui + "." + field);
    public async Task<JsonObject> Pointer(string ui, Func<JsonObject, bool> predicate)
    {
        var f = await Observe();
        var matches = DailyNavigationDecision.Rows(f.Frame).Where(r => DailyData.S(r["Type"]) == ui && DailyNavigationDecision.ReadyInput(r)).SelectMany(r => DailyData.Rows(r["Targets"])).Where(t => DailyData.B(t["Enabled"]) && predicate(t)).ToArray();
        if (matches.Length != 1)
            throw new StageHostException("adapter", "原生目标不是唯一可用项：" + ui);
        return matches[0];
    }
    public async Task<JsonObject> Transact(string role, JsonObject scope, JsonObject action, double seconds = 40, Func<Task>? drive = null, Func<Task>? heartbeat = null)
    {
        await Observe();
        var p = proofs[role];
        var before = await Evidence(p.PrefixesFor(scope));
        var op = Business.Create(Context, role, before, scope, action);
        report("执行：" + p.Stage + " / " + role);
        await Business.CommitAsync(op, Context, seconds, drive, heartbeat);
        return op;
    }
    public Task Dismiss(string expected, bool wait = false) => Driver.DismissRewardAsync(expected, wait);
    public async Task Enter(string stage) => await Navigation.EnterAsync(stage, Context, Relay);
    public async Task Home(string stage)
    {
        var r = await Navigation.RecoverAsync(stage, Context, "准备下一环节", Relay);
        if (!DailyData.B(r["safe"]))
            throw new StageHostException("adapter", "当前页面暂不能安全退出：" + DailyData.S(r["reason"]));
    }
    public JsonObject Asset(string name)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("Invalid asset name");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "flows", name)))!.AsObject();
    }
    public JsonObject[] Table(string relative)
    {
        string path = ruleData?.PathFor(relative) ?? Path.Combine(Directory, "data", "daily", relative.Replace('/', Path.DirectorySeparatorChar));
        if (!tables.TryGetValue(relative, out var result))
        {
            result = DailyData.Rows(JsonNode.Parse(File.ReadAllText(path)));
            tables.Add(relative, result);
        }
        return result;
    }
    public Dictionary<long, JsonObject> Index(string relative)
    {
        if (!indexes.TryGetValue(relative, out var result))
        {
            result = Table(relative).ToDictionary(r => DailyData.N(r["id"]));
            indexes.Add(relative, result);
        }
        return result;
    }
    public async Task<JsonNode?> MailRead(string name, string channel = "live")
    {
        var r = await Driver.HandleAsync(DailyData.O(("operation", "read"), ("channel", channel), ("name", name)));
        return r["value"] == null ? null : JsonNode.Parse(Convert.FromBase64String(DailyData.S(r["value"])));
    }
    public Task<JsonObject> MailWrite(string name, JsonNode value, string channel = "live") => Driver.HandleAsync(DailyData.O(("operation", "write"), ("channel", channel), ("name", name), ("value", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value.ToJsonString())))));
    public Task<JsonObject> MailDelete(string name, string channel = "live") => Driver.HandleAsync(DailyData.O(("operation", "delete"), ("channel", channel), ("name", name)));
    public void Save(string name, object result)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("Invalid result name");
        DailyJson.Write(Path.Combine(Root, "live", name), result);
    }
    public static JsonObject Completed(JsonNode? result = null) => new() { ["state"] = "completed", ["engine"] = "dotnet-workflow-v1", ["result"] = result?.DeepClone() };
    public static JsonObject Skipped(string reason) => new() { ["state"] = "skipped", ["reason"] = reason, ["actions"] = 0, ["engine"] = "dotnet-workflow-v1" };
    public static JsonObject Partial(string reason) => new() { ["state"] = "partial", ["reason"] = reason, ["engine"] = "dotnet-workflow-v1" };
    public async Task Refresh(string? ui = null, bool force = false, bool dice = false)
    {
        await Observe();
        if (!dice && !force && Time - refreshedAt < 60)
            return;
        refreshedAt = double.NegativeInfinity;
        string[] roles = dice ? ["rewards.dice_query"] : ["missions.event_query", "missions.query"];
        var before = await Evidence("pass", "missions", "rewards");
        DailyData.Require(roles.All(role => before["Taps"]!.AsArray().Any(n => DailyData.S(n) == role)), "任务查询观察器未就绪");
        var frame = (await Observe()).Frame;
        string[] allowed = ["MenuUI", "PassUI", "MissionUI", "EventUI", "HuntOrAirwayUI", "GameFieldDefaultUI", "CafeteriaFieldDefaultUI", "AvatarLifeGameFieldDefaultUI", "AvatarFishingHarborUI", "FishingGameFieldDefaultUI", "AvatarFishingWorldMapUI", "TotalWarUI", "MyRoomUI", "MiniGameHubUI", "SichuanMainUI", "SichuanStagePopupUI", "SichuanBoardUI", "SichuanStageClearPopupUI"];
        ui ??= DailyNavigationDecision.Rows(frame).OrderByDescending(r => DailyData.N(r["Order"])).Where(r => allowed.Contains(DailyData.S(r["Type"])) && DailyNavigationDecision.ReadyInput(r) && DailyNavigationDecision.Blockers(frame, DailyData.S(r["Type"]), DailyNavigationPolicy.Load()).Length == 0).Select(r => DailyData.S(r["Type"])).FirstOrDefault();
        if (ui == null)
            throw new StageHostException("adapter", "没有可读取服务器任务进度的空闲页面。");
        long at = Driver.UtcTicks;
        await Step(ui, operation: dice ? "dice_query" : "reward_refresh", reason: "读取服务器的最新任务进度");
        double end = Time + 20;
        while (Time < end)
        {
            var after = await Evidence("pass", "missions", "rewards");
            var events = DailyData.Array(roles.SelectMany(role => Driver.CollectEvents(role, at)));
            try
            {
                var results = new JsonObject();
                foreach (string role in roles)
                    results[role] = DailyData.Response(role, events, before, after);
                if (DailyData.N(after["AtUtcTicks"]) <= DailyData.Rows(events).Where(e => DailyData.S(e["Kind"]) == "response").Max(e => DailyData.N(e["AtUtcTicks"])))
                    throw new InvalidDataException("Waiting for post-response cache");
                DailyJson.Write(Path.Combine(Root, "live", "reward-queries", at + ".json"), new
                {
                    at,
                    before,
                    after,
                    events,
                    result = results,
                    actions = 0,
                    engine = "dotnet-v1"
                });
                if (!dice)
                    refreshedAt = Time;
                return;
            }
            catch (InvalidDataException) { await Delay(150); }
        }
        throw new StageHostException("adapter", "服务器任务进度刷新没有完成，未使用旧数据制定计划。");
    }
}



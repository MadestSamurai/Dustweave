using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyMailProof
{
    public static HashSet<string> Ids(JsonObject state, string tab)
    {
        var rows = DailyEvidence.Reading(state, "mail." + tab, "$items")!.AsArray();
        if (DailyEvidence.Integer(DailyEvidence.Reading(state, "mail." + tab, "Count")) != rows.Count)
            throw new InvalidDataException("Mailbox truncated");
        return rows.Select(r => DailyEvidence.Integer(r!["InvenIndex"]).ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, string tab)
    {
        if (tab is not ("normal" or "cash"))
            throw new InvalidDataException("Invalid mailbox tab");
        var ids = Ids(before, tab);
        var remaining = Ids(after, tab);
        var selected = events.Select(e => e!.AsObject()).Where(e => e["Role"]?.GetValue<string>() == "mail.collect").ToArray();
        var requests = selected.Where(e => e["Kind"]?.GetValue<string>() == "request").ToArray();
        var responses = selected.Where(e => e["Kind"]?.GetValue<string>() == "response").ToArray();
        if (requests.Length == 0 || requests.Length != responses.Length)
            throw new InvalidDataException("Native response is still pending");
        if (!JsonNode.DeepEquals(before["Config"], after["Config"]) || !DailyEvidence.SameActor(before["Frame"]!.AsObject(), after["Frame"]!.AsObject()))
            throw new InvalidDataException("Observer or account changed");
        if (selected.Any(e => !DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())))
            throw new InvalidDataException("Mixed operation identity");
        if (responses.Any(e => e["Error"]?.GetValue<string>() != "" || DailyEvidence.Integer(e["ErrorCode"]) != 0 || e["Accepted"]?.GetValue<bool>() != true))
            throw new InvalidDataException("Native operation rejected");
        if (requests.Min(e => DailyEvidence.Integer(e["Sequence"])) >= responses.Min(e => DailyEvidence.Integer(e["Sequence"])))
            throw new InvalidDataException("Missing native request");
        foreach (var response in responses)
            DailyEvidence.Values(response);
        if (ids.Count == 0 || ids.Overlaps(remaining))
            throw new InvalidDataException("Claimed mail still in cache");
        long expected = tab == "cash" ? DailyEvidence.Integer(DailyEvidence.Reading(before, "mail.cash_total", "$self")) : ids.Count;
        if (responses.LongLength < (expected + 9) / 10)
            throw new InvalidDataException("Mail responses incomplete");
        return new()
        {
            ["tab"] = tab,
            ["loaded_claimed"] = ids.Count,
            ["responses"] = responses.Length,
            ["cache_matched"] = true
        };
    }
}
public sealed class DailyMailStage : IDailyManagedStage
{
    private const string TabPath = "ὭὯὫὫὮὡὠὮὤὬὨ";
    private readonly string root; private readonly DailyCommandDriver driver; private readonly Func<bool> stopped; private readonly Func<double> clock; private readonly Func<TimeSpan, Task> delay; private readonly DailyNavigationPolicy policy;
    public DailyMailStage(string root, DailyCommandDriver driver, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null)
    {
        this.root = root;
        this.driver = driver;
        this.stopped = stopped;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        policy = DailyNavigationPolicy.Load();
    }
    private IEnumerable<JsonObject> Pending(JsonObject context)
    {
        string dir = Path.Combine(root, "live", "business");
        if (!Directory.Exists(dir))
            yield break;
        var actor = context["actor"]!.AsArray();
        foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
            if (op["role"]?.GetValue<string>() != "mail.collect")
                continue;
            DailyManagedReconciliation.ValidateRecord(path, op);
            if (op["account"]?.GetValue<string>() != actor[3]!.GetValue<string>() || op["player"]?.GetValue<string>() != actor[4]!.GetValue<string>() || !JsonNode.DeepEquals(op["server"], context["server"]))
                continue;
            if (op["state"]?.GetValue<string>() is "prepared" or "dispatching" or "unknown")
                yield return op;
        }
    }
    public bool CanResume(DailyStageFrame frame) => DailyNavigationDecision.Types(frame.Frame).Contains("MailUI");
    private void Stop()
    {
        if (stopped())
            throw new StageHostException("stopped", "邮箱流程已停止，已确认记录保留。");
    }
    private async Task WaitTab(string tab)
    {
        double end = clock() + 25;
        while (clock() < end)
        {
            Stop();
            try
            {
                var evidence = await driver.EvidenceAsync(["mail"]);
                if (DailyEvidence.Reading(evidence, "mail.ui", TabPath)?.GetValue<string>().ToLowerInvariant() == tab)
                    return;
            }
            catch (InvalidDataException) { }
            await delay(TimeSpan.FromMilliseconds(250));
        }
        throw new StageHostException("adapter", "Native mailbox tab did not settle");
    }
    public async Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay)
    {
        Stop();
        var initial = await driver.ObserveAsync();
        if (!JsonNode.DeepEquals(initial.Context, context))
            throw new StageHostException("identity", "邮箱现场与原队列不一致。");
        if (Pending(context).Any())
            throw new StageHostException("pending", "已有邮箱领取待核账，不重复提交。");
        var operations = new JsonArray();
        try
        {
            if (DailyNavigationDecision.Types(initial.Frame).Contains("MailUI"))
                await driver.DismissRewardAsync("MailUI", false);
            else
                await driver.SendObservedAsync(new()
                {
                    ["ui"] = "MenuUI",
                    ["field"] = "_buttonMail",
                    ["expect"] = "MailUI",
                    ["reason"] = "收取邮箱物品"
                });
            for (int value = 0; value < 2; value++)
            {
                string tab = value == 0 ? "normal" : "cash";
                Stop();
                await driver.SendObservedAsync(new()
                {
                    ["ui"] = "MailUI",
                    ["operation"] = "mail_tab",
                    ["value"] = value,
                    ["reason"] = "检查另一类邮箱"
                });
                await WaitTab(tab);
                bool drained = false;
                for (int batch = 0; batch < 100; batch++)
                {
                    Stop();
                    var before = await driver.EvidenceAsync(["mail"]);
                    var ids = DailyMailProof.Ids(before, tab);
                    if (ids.Count == 0)
                    {
                        drained = true;
                        break;
                    }
                    if (before["Taps"] is not JsonArray taps || !taps.Any(t => t?.GetValue<string>() == "mail.collect"))
                        throw new StageHostException("adapter", "Required mail response observer unavailable");
                    if (Pending(context).Any())
                        throw new StageHostException("pending", "邮箱领取前仍有未确认事务。");
                    string id = Guid.NewGuid().ToString("N"), path = Path.Combine(root, "live", "business", id + ".json");
                    var action = new JsonObject { ["ui"] = "MailUI", ["field"] = "_objAllReceiveButton", ["reason"] = "一键领取邮箱物品" };
                    var op = new JsonObject { ["id"] = id, ["role"] = "mail.collect", ["scope"] = new JsonObject { ["tab"] = tab, ["instances"] = new JsonArray(ids.Order(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) }, ["account"] = context["actor"]![3]!.DeepClone(), ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = context["server"]!.DeepClone(), ["cycle"] = context["cycle"]!.DeepClone(), ["state"] = "prepared", ["at"] = driver.UtcTicks, ["before"] = before.DeepClone(), ["action"] = action.DeepClone(), ["roles"] = new JsonArray("mail.collect"), ["proof_version"] = 1, ["stage"] = "mail", ["engine"] = "dotnet-mail-v1" };
                    DailyJson.Write(path, op);
                    try
                    {
                        Stop();
                        op["state"] = "dispatching";
                        DailyJson.Write(path, op);
                        action["reason"] = "business:" + id + "|一键领取邮箱物品";
                        try
                        {
                            var done = await driver.SendObservedAsync(action);
                            op["command_id"] = done["id"]!.DeepClone();
                        }
                        catch (DailyStepException e) when (e.Kind == "rejected") { op["state"] = "rejected"; throw; }
                        catch (DailyStepException e) { op["ui_error"] = e.Message; if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone(); }
                        DailyJson.Write(path, op);
                        double end = clock() + 60;
                        string last = "No response";
                        while (clock() < end)
                        {
                            var events = driver.CollectEvents("mail.collect", DailyEvidence.Integer(op["at"]));
                            var after = await driver.EvidenceAsync(["mail"]);
                            op["events"] = events;
                            op["last_observation"] = after.DeepClone();
                            try
                            {
                                var result = DailyMailProof.Verify(before, events, after, tab);
                                op["result"] = result;
                                op["after"] = after;
                                op["state"] = "completed";
                                DailyJson.Write(path, op);
                                break;
                            }
                            catch (InvalidDataException e) { last = e.Message; op["verification_wait"] = last; DailyJson.Write(path, op); }
                            await delay(TimeSpan.FromMilliseconds(250));
                        }
                        if (op["state"]!.GetValue<string>() != "completed")
                            throw new StageHostException("pending", last);
                    }
                    catch (Exception error)
                    {
                        if (op["state"]?.GetValue<string>() is not ("completed" or "rejected"))
                            op["state"] = op["state"]?.GetValue<string>() == "prepared" ? "rejected" : "unknown";
                        op["error"] = error.Message;
                        DailyJson.Write(path, op);
                        if (op["state"]?.GetValue<string>() == "unknown")
                            throw new StageHostException("pending", error.Message);
                        throw;
                    }
                    var outcome = op["result"]!.DeepClone().AsObject();
                    outcome["id"] = id;
                    operations.Add(outcome);
                    try
                    {
                        await driver.DismissRewardAsync("MailUI", true);
                    }
                    catch (Exception error) { throw new StageHostException("adapter", "邮箱领取已确认，奖励展示待收尾；不重新领取：" + error.Message); }
                }
                if (!drained)
                    throw new StageHostException("adapter", "Mailbox batch limit reached; confirmed claims preserved");
            }
            await driver.DismissRewardAsync("MailUI", false);
            await driver.SendObservedAsync(new()
            {
                ["ui"] = "MailUI",
                ["back"] = true,
                ["absent"] = "MailUI",
                ["reason"] = "Confirmed operation UI cleanup"
            });
            var current = (await driver.ObserveAsync()).Frame;
            if (!DailyNavigationDecision.Types(current).Contains("MenuUI") || DailyNavigationDecision.Blockers(current, "MenuUI", policy).Length > 0)
                throw new StageHostException("adapter", "邮箱领取已核账，主菜单收尾未确认。");
            return new()
            {
                ["state"] = operations.Count > 0 ? "completed" : "skipped",
                ["reason"] = operations.Count > 0 ? "mailbox_collected" : "mailbox_empty",
                ["operations"] = operations,
                ["engine"] = "dotnet-mail-v1"
            };
        }
        catch (DailyStepException error) { throw new StageHostException(error.Kind == "rejected" ? "adapter" : error.Kind == "identity" ? "identity" : "pending", error.Message); }
    }
}
public sealed partial class DailyCommandDriver
{
    public Task DismissRewardAsync(string expected, bool waitForReward) => DismissRewardAsync([expected], waitForReward);
    public Task DismissRewardAsync(string[] expectedSurfaces, bool waitForReward) => DismissRewardAsync(expectedSurfaces, waitForReward, null);
    public async Task DismissRewardAsync(string[] expectedSurfaces, bool waitForReward, Func<Task<bool>>? destinationSettled)
    {
        double end = clock() + 40;
        bool seen = false;
        double readySince = -1;
        string readyToken = "";
        while (clock() < end)
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "Paused during reward cleanup");
            var frame = (await ReadBound()).Frame;
            string expected = expectedSurfaces.FirstOrDefault(t => DailyNavigationDecision.Types(frame).Contains(t)) ?? expectedSurfaces[0];
            if (await RecoverPresentationAsync(frame, expected))
                continue;
            var rows = DailyNavigationDecision.Rows(frame).Where(r => Text(r, "Type") == "RewardReceivePopupUI").ToArray();
            if (rows.Length == 0)
            {
                bool destinationReady = DailyNavigationDecision.Rows(frame).Any(r => expectedSurfaces.Contains(Text(r, "Type")) && (r["InputReady"]?.GetValue<bool>() ?? true) && DailyNavigationDecision.Blockers(frame, Text(r, "Type"), policy).Length == 0);
                // A verified transaction may resume after its result popup was already closed.
                // Stable native destination proves presentation cleanup, never a new reward claim.
                string token = Text(frame, "UiToken") + "|" + expected;
                if (destinationReady && (destinationSettled == null || await destinationSettled()))
                {
                    if (seen || !waitForReward)
                        return;
                    if (readySince < 0 || readyToken != token)
                    {
                        readySince = clock();
                        readyToken = token;
                    }
                    if (clock() - readySince >= 2)
                        return;
                }
                else
                    readySince = -1;
                await delay(TimeSpan.FromMilliseconds(100));
                continue;
            }
            readySince = -1;
            seen = true;
            if (rows.Length != 1 || DailyNavigationDecision.Blockers(frame, "RewardReceivePopupUI", policy).Length > 0)
                throw new StageHostException("adapter", "Reward result is covered by another popup");
            var evidence = await EvidenceAsync(["reward.presentation"]);
            bool ready = (rows[0]["InputReady"]?.GetValue<bool>() ?? true) && new[] { "ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ", "ὮὬὧὦὣὠὠὤὮὦὧ" }.All(p => DailyEvidence.Reading(evidence, "reward.presentation", p)?.GetValue<bool>() == true) && DailyEvidence.Reading(evidence, "reward.presentation", "ὦὡὮὫὧὠὮὡὭὭὬ")?.GetValue<bool>() == false;
            if (ready)
                await SendObservedAsync(new()
                {
                    ["ui"] = "RewardReceivePopupUI",
                    ["back"] = true,
                    ["absent"] = "RewardReceivePopupUI",
                    ["reason"] = "Close confirmed reward after native animation"
                });
            else
                await delay(TimeSpan.FromMilliseconds(100));
        }
        throw new StageHostException("adapter", "Reward presentation did not close; business receipt preserved");
    }
}

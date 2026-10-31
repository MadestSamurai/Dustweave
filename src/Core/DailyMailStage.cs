using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyMailProof
{
    // Native Define_ElementType: both regular and stacked PvP entry currency.
    // Inspect every attachment; a mixed reward is received as one indivisible mail.
    public static HashSet<string> ClaimableIds(JsonObject state, string tab, bool preserve)
    {
        var ids = Ids(state, tab);
        if (!preserve) return ids;
        foreach (var row in DailyEvidence.Reading(state, "mail." + tab, "$items")!.AsArray())
        {
            if (row?["ItemType"] is not JsonArray types || row["ItemId"] is not JsonArray items || row["ItemCount"] is not JsonArray counts ||
                types.Count != items.Count || types.Count != counts.Count)
                throw new InvalidDataException("mail_attachment_data_unavailable");
            if (types.Any(t => DailyEvidence.Integer(t) is 15 or 24))
                ids.Remove(DailyEvidence.Integer(row["InvenIndex"]).ToString(CultureInfo.InvariantCulture));
        }
        return ids;
    }
    public static HashSet<string> Ids(JsonObject state, string tab)
    {
        var rows = DailyEvidence.Reading(state, "mail." + tab, "$items")!.AsArray();
        if (DailyEvidence.Integer(DailyEvidence.Reading(state, "mail." + tab, "Count")) != rows.Count)
            throw new InvalidDataException("Mailbox truncated");
        return rows.Select(r => DailyEvidence.Integer(r!["InvenIndex"]).ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, string tab, HashSet<string>? claimed = null)
    {
        if (tab is not ("normal" or "cash"))
            throw new InvalidDataException("Invalid mailbox tab");
        var all = Ids(before, tab);
        var ids = claimed ?? all;
        if (!ids.IsSubsetOf(all)) throw new InvalidDataException("Claimed mail not in original snapshot");
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
        if (claimed != null && all.Except(ids).Any(id => !remaining.Contains(id)))
            throw new InvalidDataException("mail_preserved_items_changed");
        long expected = claimed == null && tab == "cash" ? DailyEvidence.Integer(DailyEvidence.Reading(before, "mail.cash_total", "$self")) : ids.Count;
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
    private readonly Func<bool> preserveBloodCocktail;
    public DailyMailStage(string root, DailyCommandDriver driver, Func<bool> stopped, Func<double>? clock = null, Func<TimeSpan, Task>? delay = null, Func<bool>? preserveBloodCocktail = null)
    {
        this.root = root;
        this.driver = driver;
        this.stopped = stopped;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        this.delay = delay ?? (t => Task.Delay(t));
        this.preserveBloodCocktail = preserveBloodCocktail ?? (() => false);
        policy = DailyNavigationPolicy.Load();
    }
    private async Task LoadFilteredMailbox(string tab, int value, JsonObject actor)
    {
        // The native list initially contains only 100 normal / 20 cash mails.
        // Load later pages before deciding that only protected rewards remain.
        for (int page = 0; page < 100; page++)
        {
            Stop();
            var before = await driver.EvidenceAsync(["mail"]);
            if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), actor)) throw new StageHostException("identity", "读取邮箱时游戏或账号发生变化。");
            var ids = DailyMailProof.Ids(before, tab);
            long total = DailyEvidence.Integer(DailyEvidence.Reading(before, "mail." + tab + "_total", "$self"));
            if (ids.Count >= total) return;
            if (total > 1000) throw new StageHostException("adapter", "mail_filtered_page_limit");
            long cursor = ids.Count == 0 ? 0 : tab == "normal" ? ids.Max(long.Parse) : ids.Min(long.Parse);
            await driver.SendObservedAsync(new() { ["ui"] = "MailUI", ["operation"] = "mail_page", ["value"] = value,
                ["items"] = new JsonArray(cursor), ["reason"] = "读取剩余邮件附件" });
            double end = clock() + 25;
            using var waiting = DailyObservationWindow.Begin(25, clock);
            bool progressed = false;
            while (clock() < end)
            {
                Stop();
                var after = await driver.EvidenceAsync(["mail"]);
                if (!DailyEvidence.SameActor(after["Frame"]!.AsObject(), actor)) throw new StageHostException("identity", "读取邮箱时游戏或账号发生变化。");
                if (DailyMailProof.Ids(after, tab).Count > ids.Count || DailyEvidence.Integer(DailyEvidence.Reading(after, "mail." + tab + "_total", "$self")) <= ids.Count)
                { progressed = true; break; }
                await delay(TimeSpan.FromMilliseconds(150));
            }
            if (!progressed) throw new StageHostException("adapter", "mail_filtered_page_unavailable");
        }
        throw new StageHostException("adapter", "mail_filtered_page_limit");
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
        using var waiting = DailyObservationWindow.Begin(25, clock);
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
        var interrupted = Pending(context).ToArray();
        var operations = new JsonArray();
        bool preserve = preserveBloodCocktail();
        var preserved = new HashSet<string>(StringComparer.Ordinal);
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
                if (preserve) await LoadFilteredMailbox(tab, value, initial.Frame);
                bool drained = false;
                for (int batch = 0; batch <= 100; batch++)
                {
                    Stop();
                    var before = await driver.EvidenceAsync(["mail"]);
                    var ids = DailyMailProof.Ids(before, tab);
                    var claimable = DailyMailProof.ClaimableIds(before, tab, preserve);
                    foreach (string held in ids.Except(claimable)) preserved.Add(tab + ":" + held);
                    var selected = preserve ? claimable.OrderBy(long.Parse).Take(10).ToHashSet(StringComparer.Ordinal) : ids;
                    if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), initial.Frame))
                        throw new StageHostException("identity", "读取邮箱时游戏或账号发生变化。");
                    // Fresh native mailbox IDs decide what is still claimable. Old
                    // incomplete receipts do not require a manual journal reset.
                    foreach (var prior in interrupted.Where(o => o["scope"]?["tab"]?.GetValue<string>() == tab && DailyManagedBusiness.Pending(o)))
                    {
                        prior["state"] = "superseded";
                        prior["reconciliation"] = new JsonObject { ["method"] = "current_mailbox_ids", ["at"] = driver.UtcTicks,
                            ["original_success_claimed"] = false, ["saved_action_replayed"] = false, ["remaining"] = ids.Count };
                        DailyJson.Write(Path.Combine(root, "live", "business", prior["id"]!.GetValue<string>() + ".json"), prior);
                    }
                    if (selected.Count == 0)
                    {
                        drained = true;
                        break;
                    }
                    if (batch == 100) break;
                    if (before["Taps"] is not JsonArray taps || !taps.Any(t => t?.GetValue<string>() == "mail.collect"))
                        throw new StageHostException("adapter", "Required mail response observer unavailable");
                    if (Pending(context).Any(o => !interrupted.Any(prior => JsonNode.DeepEquals(prior["id"], o["id"]))))
                        throw new StageHostException("pending", "邮箱领取前仍有未确认事务。");
                    string id = Guid.NewGuid().ToString("N"), path = Path.Combine(root, "live", "business", id + ".json");
                    var action = preserve
                        ? new JsonObject { ["ui"] = "MailUI", ["operation"] = "mail_collect_filtered", ["value"] = value,
                            ["items"] = new JsonArray(selected.OrderBy(long.Parse).Select(s => (JsonNode)JsonValue.Create(long.Parse(s))!).ToArray()), ["reason"] = "领取未保留的邮箱物品" }
                        : new JsonObject { ["ui"] = "MailUI", ["field"] = "_objAllReceiveButton", ["reason"] = "一键领取邮箱物品" };
                    var op = new JsonObject { ["id"] = id, ["role"] = "mail.collect", ["scope"] = new JsonObject { ["tab"] = tab, ["instances"] = new JsonArray(selected.Order(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) }, ["account"] = context["actor"]![3]!.DeepClone(), ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = context["server"]!.DeepClone(), ["cycle"] = context["cycle"]!.DeepClone(), ["state"] = "prepared", ["at"] = driver.UtcTicks, ["before"] = before.DeepClone(), ["action"] = action.DeepClone(), ["roles"] = new JsonArray("mail.collect"), ["proof_version"] = 1, ["stage"] = "mail", ["engine"] = "dotnet-mail-v1" };
                    DailyJson.Write(path, op);
                    try
                    {
                        Stop();
                        op["state"] = "dispatching";
                        DailyJson.Write(path, op);
                        action["reason"] = "business:" + id + "|" + action["reason"]!.GetValue<string>();
                        try
                        {
                            var done = await driver.SendObservedAsync(action);
                            op["command_id"] = done["id"]!.DeepClone();
                        }
                        catch (DailyStepException e) when (e.Kind == "rejected") { op["state"] = "rejected"; throw; }
                        catch (DailyStepException e) { op["ui_error"] = e.Message; if (e.Command != null) op["command_id"] = e.Command["Id"]!.DeepClone(); }
                        DailyJson.Write(path, op);
                        double end = clock() + 60;
                        using var waiting = DailyObservationWindow.Begin(60, clock);
                        string last = "No response";
                        while (clock() < end)
                        {
                            var events = driver.CollectEvents("mail.collect", DailyEvidence.Integer(op["at"]));
                            var after = await driver.EvidenceAsync(["mail"]);
                            op["events"] = events;
                            op["last_observation"] = after.DeepClone();
                            try
                            {
                                var result = DailyMailProof.Verify(before, events, after, tab, preserve ? selected : null);
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
                        if (error is StageHostException { HoldQueue: true } or StageHostException { Kind: "identity" or "stopped" } or DailyStepException { Kind: "identity" or "stopped" }) throw;
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
                    catch (StageHostException error) when (error.HoldQueue || error.Kind is "identity" or "stopped") { throw; }
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
                ["reason"] = preserved.Count > 0 ? operations.Count > 0 ? "mailbox_collected_with_preservation" : "mailbox_blood_cocktail_preserved" : operations.Count > 0 ? "mailbox_collected" : "mailbox_empty",
                ["preserved_mail_count"] = preserved.Count,
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
        using var waiting = DailyObservationWindow.Begin(40, clock);
        bool seen = false;
        double readySince = -1;
        string readyToken = "";
        string waitingFor = "Reward destination is not ready";
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
                // This helper also runs before a claim. An unready destination
                // must not imply that a reward was shown or a receipt exists.
                waitingFor = seen ? "Reward presentation closed but destination is not ready" : "Reward destination is not ready";
                bool destinationReady = DailyNavigationDecision.Rows(frame).Any(r => expectedSurfaces.Contains(Text(r, "Type")) && (r["InputReady"]?.GetValue<bool>() ?? true) && DailyNavigationDecision.Blockers(frame, Text(r, "Type"), policy).Length == 0);
                // A verified transaction may resume after its result popup was already closed.
                // Stable native destination proves presentation cleanup, never a new reward claim.
                string token = Text(frame, "UiToken") + "|" + expected;
                bool destinationStateReady = destinationReady && (destinationSettled == null || await destinationSettled());
                if (destinationReady && !destinationStateReady)
                    waitingFor = "Reward destination state has not refreshed";
                if (destinationStateReady)
                {
                    if (seen || !waitForReward)
                        return;
                    waitingFor = "Reward destination is still changing";
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
            waitingFor = "Reward presentation is still open";
            if (rows.Length != 1)
                throw new StageHostException("adapter", "Reward result is covered by another popup");
            if (DailyNavigationDecision.Blockers(frame, "RewardReceivePopupUI", policy).Length > 0)
            {
                // A response can open the reward before the submitting preview's
                // close animation finishes. Observe the handoff within this same
                // deadline; never dismiss the covering window or replay the claim.
                waitingFor = "Reward result is covered by another popup";
                await delay(TimeSpan.FromMilliseconds(100));
                continue;
            }
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
        throw new StageHostException("adapter", waitingFor);
    }
}

using System.Globalization;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Native field talent activation, evidence and read-only recovery.</summary>
public static class DailyFieldTalentProof
{
    public static JsonArray ResearchWindowEvents(JsonObject op, JsonArray events)
    {
        if (op["last_observation"] is not JsonObject last)
            return events.DeepClone().AsArray();
        long end = Numeric(last["AtUtcTicks"]), group = Numeric(op["scope"]!["group"]);
        var result = new JsonArray();
        JsonObject? request = null;
        foreach (var node in events.OrderBy(e => Numeric(e!["Sequence"])))
        {
            var e = node!.AsObject();
            if (Numeric(e["Frame"]!["AtUtcTicks"]) <= end)
            {
                result.Add(e.DeepClone());
                continue;
            }
            if (e["Kind"]?.GetValue<string>() == "request" && request == null)
            {
                request = e;
                continue;
            }
            bool other = false;
            if (e["Kind"]?.GetValue<string>() == "response" && request != null)
            {
                try
                {
                    var values = DailyEvidence.Values(e);
                    other = DailyEvidence.SameActor(request["Frame"]!.AsObject(), e["Frame"]!.AsObject()) && e["Error"]?.GetValue<string>() == "" && Numeric(e["ErrorCode"]) == 0 && e["Accepted"]?.GetValue<bool>() == true && values["IsSuccess"]?.GetValue<bool>() == true && Numeric(values["TalentSkillInfo"]?["groupId"]) != group;
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException) { other = false; }
            }
            if (request != null)
            {
                if (!other)
                    result.Add(request.DeepClone());
                request = null;
            }
            if (!other)
                result.Add(e.DeepClone());
        }
        if (request != null)
            result.Add(request.DeepClone());
        return result;
    }
    public static bool Owns(JsonObject op) => op["role"]?.GetValue<string>() == "dispatch.start" && op["action"]?["operation"]?.GetValue<string>() == "mainline_talent" && op["scope"]?["kind"] is JsonValue kind && kind.TryGetValue<int>(out int n) && n is 2 or 3 or 4 or 6 or 17 or 20;
    internal static long Numeric(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out string? text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
            return n;
        return DailyEvidence.Integer(node);
    }
    internal static JsonObject[] Rows(JsonObject e) => DailyEvidence.Reading(e, "mainline.talent_rows", "$self")!.AsArray().Select(r => r!.AsObject()).ToArray();
    internal static JsonObject? Count(JsonObject e, int group) => DailyEvidence.Reading(e, "mainline.talent_counts", "Values")!.AsArray().Select(r => r!.AsObject()).SingleOrDefault(r => Numeric(r["groupId"]) == group);
    public static string Gate(JsonObject e, int instance, int? group = null, int? kind = null)
    {
        var rows = Rows(e).Where(r => Numeric(r["Instance"]) == instance).ToArray();
        if (rows.Length != 1 || group != null && Numeric(rows[0]["Group"]) != group || kind != null && Numeric(rows[0]["Kind"]) != kind)
            throw new DailyStepException("rejected", "Talent selection changed; no replay");
        if (rows[0]["Gate"] is not JsonValue gate || !gate.TryGetValue<string>(out string? value))
            throw new StageHostException("adapter", "Native talent preflight unavailable; reconnect the current version");
        return value ?? throw new InvalidDataException("Missing native talent gate");
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        if (!Owns(op))
            throw new InvalidDataException("Unsupported field talent proof");
        var before = op["before"]!.AsObject();
        var frame = before["Frame"]!.AsObject();
        int kind = (int)Numeric(op["scope"]!["kind"]), group = (int)Numeric(op["scope"]!["group"]);
        if (!JsonNode.DeepEquals(before["Config"], after["Config"]) || !DailyEvidence.SameActor(frame, after["Frame"]!.AsObject()))
            throw new InvalidDataException("Talent observer or actor changed");
        var selected = events.Select(e => e!.AsObject()).Where(e => e["Role"]?.GetValue<string>() == "dispatch.start").ToArray();
        var requests = selected.Where(e => e["Kind"]?.GetValue<string>() == "request").ToArray();
        var responses = selected.Where(e => e["Kind"]?.GetValue<string>() == "response").ToArray();
        if (requests.Length != 1 || responses.Length != 1)
            throw new InvalidDataException("Expected exactly one talent request and response");
        var response = responses[0];
        if (selected.Any(e => !DailyEvidence.SameActor(e["Frame"]!.AsObject(), frame) || e["Error"]?.GetValue<string>() != "") || Numeric(requests[0]["Sequence"]) >= Numeric(response["Sequence"]) || Numeric(response["ErrorCode"]) != 0 || response["Accepted"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Talent response identity, order or status mismatch");
        var result = DailyEvidence.Values(response);
        if (result["IsSuccess"]?.GetValue<bool>() != true || Numeric(result["TalentSkillInfo"]?["groupId"]) != group)
            throw new InvalidDataException("Talent response belongs to a failed or different skill");
        if (kind is 3 or 4 or 20)
        {
            long old = Numeric(Count(before, group)?["useCount"] ?? JsonValue.Create(0)), updated = Numeric(Count(after, group)?["useCount"] ?? JsonValue.Create(0));
            if (updated != old + 1)
                throw new InvalidDataException("Talent usage did not advance exactly once");
        }
        return result;
    }
    // The legacy click returned synchronously with the same menu but no RPC.
    // Recovery is restricted to research: its long-lived effect must still have
    // been visible in the saved timeout observation if the cast had succeeded.
    // Never apply absence-of-events recovery to collection, suppression or summons.
    public static bool LegacyResearchRejected(JsonObject op, JsonObject receipt, JsonArray events)
    {
        try
        {
            if (!Owns(op) || Numeric(op["scope"]!["kind"]) != 6 || events.Count != 0 || receipt["State"]?.GetValue<string>() != "observed_after_dispatch" || receipt["Error"]?.GetValue<string>() != "" || receipt["MayHaveDispatched"]?.GetValue<bool>() != true)
                return false;
            var before = op["before"]!.AsObject();
            var after = op["last_observation"]!.AsObject();
            var frame = before["Frame"]!.AsObject();
            var command = receipt["Command"]!.AsObject();
            if (command["Id"]?.GetValue<string>() != op["command_id"]?.GetValue<string>() || command["Kind"]?.GetValue<string>() != "mainline_talent" || !JsonNode.DeepEquals(command["Value"], op["action"]!["value"]) || command["Reason"]?.GetValue<string>()?.StartsWith("business:" + op["id"]!.GetValue<string>() + "|", StringComparison.Ordinal) != true)
                return false;
            if (new[] { before, after }.Any(e => e["Error"]?.GetValue<string>() != "" || !(e["Taps"]?.AsArray().Any(t => t?.GetValue<string>() == "dispatch.start") ?? false)) || !JsonNode.DeepEquals(before["Config"], after["Config"]))
                return false;
            if (new[] { after["Frame"]!.AsObject(), receipt["Before"]!.AsObject(), receipt["After"]!.AsObject(), command }.Any(f => !DailyEvidence.SameActor(frame, f) || !JsonNode.DeepEquals(f["Scene"], frame["Scene"])))
                return false;
            if (new[] { receipt["Before"]!.AsObject(), receipt["After"]!.AsObject() }.Any(f => !DailyNavigationDecision.Rows(f).Any(s => s["Type"]?.GetValue<string>() == "QuickMenuUI" && JsonNode.DeepEquals(s["Id"], command["SurfaceId"]))))
                return false;
            if (DailyEvidence.Reading(before, "mainline.talent_state", "ὧὪὯὮὬὧὪὤὩὩὦ")?.GetValue<bool>() != true || DailyEvidence.Reading(after, "mainline.talent_state", "ὧὪὯὮὬὧὪὤὩὩὦ")?.GetValue<bool>() != false)
                return false;
            int group = (int)Numeric(op["scope"]!["group"]), instance = (int)Numeric(op["action"]!["value"]);
            var rows = Rows(before).Where(r => Numeric(r["Instance"]) == instance && Numeric(r["Group"]) == group).ToArray();
            if (rows.Length != 1)
                return false;
            var tableRows = before["Readings"]!.AsArray().Select(r => r!.AsObject()).Where(r => r["Id"]?.GetValue<string>() == "dispatch.talent" && Numeric(r["InstanceId"]) == instance).ToArray();
            if (tableRows.Length != 1 || tableRows[0]["Error"]?.GetValue<string>() != "")
                return false;
            var tableValue = tableRows[0]["Values"]!.AsArray().Select(v => v!.AsObject()).Single(v => v["Path"]?.GetValue<string>() == "ὣὡὪὭὤὨὭὣὪὧὩ");
            if (tableValue["Error"]?.GetValue<string>() != "")
                return false;
            var table = JsonNode.Parse(tableValue["Json"]!.GetValue<string>())!.AsObject();
            if (Numeric(table["groupId"]) != group || Numeric(table["classType"]) != 6)
                return false;
            double duration = table["valueList"]![1]!.GetValue<double>(), elapsed = (Numeric(after["AtUtcTicks"]) - Numeric(op["at"])) / (double)TimeSpan.TicksPerSecond;
            if (elapsed < 20 || duration < 60 || elapsed > duration - 10)
                return false;
            foreach (var e in new[] { before, after })
            {
                var research = DailyEvidence.Reading(e, "mainline.research", "$self")!.AsObject();
                if (research["Active"]?.GetValue<bool>() != false || research["Remaining"]!.GetValue<double>() > 0)
                    return false;
            }
            var later = Rows(after).Where(r => Numeric(r["Group"]) == group).ToArray();
            if (later.Length != 1 || later[0]["Cooldown"]!.GetValue<double>() > 0 || !JsonNode.DeepEquals(Count(before, group), Count(after, group)))
                return false;
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException or ArgumentException) { return false; }
    }
}

public sealed partial class DailyCommandDriver
{
    private static readonly string[] TalentPrefixes = ["mainline", "dispatch.talent"];
    private async Task<JsonObject> FieldTalentAsync(JsonObject action)
    {
        Active();
        ValidateAction(action);
        if (!HasControl)
            Acquire("live");
        var bound = (await ReadBound()).Frame;
        int instance = (int)Number(action, "value");
        string reason = Text(action, "reason"), opId = reason.StartsWith("business:", StringComparison.Ordinal) ? reason[9..].Split('|', 2)[0] : "";
        JsonObject? op = null;
        string? businessPath = null;
        if (opId.Length > 0)
        {
            if (!GuildStore.ValidId(opId))
                throw new StageHostException("protocol", "Invalid talent operation ID");
            businessPath = new[] { Path.Combine(root, "live", "managed-business", opId + ".json"), Path.Combine(root, "live", "business", opId + ".json") }.FirstOrDefault(File.Exists) ?? Path.Combine(root, "live", "managed-business", opId + ".json");
            op = DailyJson.TryRead<JsonObject>(businessPath) ?? throw new StageHostException("protocol", "Missing talent business intent");
            DailyManagedReconciliation.ValidateRecord(businessPath, op);
            if (!DailyFieldTalentProof.Owns(op) || op["state"]?.GetValue<string>() != "dispatching" || !DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), bound) || !JsonNode.DeepEquals(op["action"]!["value"], action["value"]) || !JsonNode.DeepEquals(op["server"], context!["server"]) || !JsonNode.DeepEquals(op["cycle"], context["cycle"]))
                throw new StageHostException("protocol", "Talent intent does not match the owned queue");
        }
        int? group = op == null ? null : (int)DailyFieldTalentProof.Numeric(op["scope"]!["group"]), kind = op == null ? null : (int)DailyFieldTalentProof.Numeric(op["scope"]!["kind"]);
        double end = clock() + 190;
        JsonObject? sent = null;
        int rejected = 0;
        var waits = new JsonArray();
        while (clock() < end)
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "Stopped before field talent dispatch");
            var frame = (await ReadBound()).Frame;
            if (!JsonNode.DeepEquals(frame["Scene"], bound["Scene"]))
                throw new DailyStepException("rejected", "Talent map changed; no dispatch");
            var menus = DailyNavigationDecision.Rows(frame).Where(s => Text(s, "Type") == "QuickMenuUI").ToArray();
            if (menus.Length != 1)
                throw new DailyStepException("rejected", "Talent menu changed; no dispatch");
            if (!DailyNavigationDecision.ReadyInput(menus[0]))
            {
                await delay(TimeSpan.FromMilliseconds(200));
                continue;
            }
            var evidence = await EvidenceAsync(TalentPrefixes);
            string gate = DailyFieldTalentProof.Gate(evidence, instance, group, kind);
            if (gate.Length > 0)
            {
                if (!gate.StartsWith("talent_wait:", StringComparison.Ordinal))
                    throw new DailyStepException("rejected", gate);
                if (waits.LastOrDefault()?["gate"]?.GetValue<string>() != gate)
                    waits.Add(new JsonObject { ["at"] = now(), ["gate"] = gate });
                await delay(TimeSpan.FromMilliseconds(200));
                continue;
            }
            try
            {
                sent = await SubmitRawAsync(action, bound);
                break;
            }
            catch (DailyStepException error) when (error.Kind == "rejected" && (error.Message.StartsWith("rejected: talent_wait:", StringComparison.Ordinal) || error.Message is "rejected: ui_not_ready" or "rejected: screen_changed") && ++rejected <= 3) { await delay(TimeSpan.FromMilliseconds(200)); }
        }
        if (sent == null)
            throw new DailyStepException("rejected", "Talent native gate did not clear within the bounded wait; no successful dispatch");
        string proofPath = Path.Combine(root, "live", "field-talents", sent["id"]!.GetValue<string>() + ".json");
        var log = new JsonObject { ["engine"] = "dotnet-field-talents-v1", ["transport"] = sent.DeepClone(), ["waits"] = waits, ["non_dispatch_retries"] = rejected };
        DailyJson.Write(proofPath, log);
        if (op == null)
            return sent; // Waypoint selection has its own later business operation.
        op["command_id"] = sent["id"]!.DeepClone();
        op["managed_engine"] = "dotnet-field-talents-v1";
        DailyJson.Write(businessPath!, op);
        double proofEnd = clock() + 25;
        JsonArray events = new();
        JsonObject? after = null;
        string last = "Native request not yet observed";
        try
        {
            while (clock() < proofEnd)
            {
                await ReadBound();
                events = CollectEvents("dispatch.start", DailyFieldTalentProof.Numeric(op["at"]));
                after = await EvidenceAsync(TalentPrefixes);
                try
                {
                    var proof = DailyFieldTalentProof.Verify(op, events, after);
                    log["state"] = "completed";
                    log["events"] = events.DeepClone();
                    log["after"] = after.DeepClone();
                    log["result"] = proof;
                    DailyJson.Write(proofPath, log);
                    op["state"] = "completed";
                    op["events"] = events.DeepClone();
                    op["after"] = after.DeepClone();
                    op["result"] = proof.DeepClone();
                    DailyJson.Write(businessPath!, op);
                    sent["business_proof"] = proof.DeepClone();
                    return sent;
                }
                catch (InvalidDataException error) { last = error.Message; }
                // At least one request means it may have consumed resources. Never replay.
                await delay(TimeSpan.FromMilliseconds(200));
            }
            throw new InvalidDataException(last);
        }
        catch (Exception error)
        {
            log["state"] = "unknown";
            log["error"] = error.Message;
            log["events"] = events;
            log["last_observation"] = after;
            DailyJson.Write(proofPath, log);
            op["state"] = "unknown";
            op["error"] = error.Message;
            op["events"] = log["events"]!.DeepClone();
            op["last_observation"] = log["last_observation"]?.DeepClone();
            DailyJson.Write(businessPath!, op);
            throw new DailyStepException("pending", "Talent result requires reconciliation; original command preserved: " + error.Message, true) { Command = sent["command"]!.DeepClone().AsObject() };
        }
    }
    public async Task<JsonObject> ReconcileFieldTalentsAsync()
    {
        await ReadBound();
        var completed = new JsonArray();
        var report = new JsonObject { ["completed"] = completed, ["unresolved"] = new JsonArray(), ["actions"] = 0, ["engine"] = "dotnet-field-talents-reconciliation-v1" };
        string directory = Path.Combine(root, "live", "business");
        if (!Directory.Exists(directory))
            return report;
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var op = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable business record");
            if (!DailyFieldTalentProof.Owns(op) || op["state"]?.GetValue<string>() is not ("dispatching" or "unknown"))
                continue;
            DailyManagedReconciliation.ValidateRecord(path, op);
            if (!JsonNode.DeepEquals(op["account"], context!["actor"]![3]) || !JsonNode.DeepEquals(op["player"], context["actor"]![4]) || !JsonNode.DeepEquals(op["server"], context["server"]) || !JsonNode.DeepEquals(op["cycle"], context["cycle"]))
                continue;
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "Stopped during talent read-only reconciliation");
            var original = op["before"]!["Frame"]!.AsObject();
            var events = CollectEvents("dispatch.start", DailyFieldTalentProof.Numeric(op["at"]));
            var merged = (op["events"] as JsonArray ?? new JsonArray()).Concat(events).Select(e => e!.AsObject()).Where(e => DailyEvidence.SameActor(e["Frame"]!.AsObject(), original)).GroupBy(e => DailyFieldTalentProof.Numeric(e["Sequence"])).Select(g => g.First()).OrderBy(e => DailyFieldTalentProof.Numeric(e["Sequence"]));
            events = new JsonArray(merged.Select(e => (JsonNode)e.DeepClone()).ToArray());
            string commandId = op["command_id"]?.GetValue<string>() ?? "";
            JsonObject? receipt = null;
            if (GuildStore.ValidId(commandId))
            {
                var raw = mailbox.Read("live", "receipts~" + commandId + ".json");
                receipt = raw == null ? null : JsonNode.Parse(raw)!.AsObject();
                receipt ??= DailyJson.TryRead<JsonObject>(Path.Combine(root, "live", "steps", commandId, "result.json"))?["receipt"]?.AsObject();
                receipt ??= DailyJson.TryRead<JsonObject>(Path.Combine(root, "live", "step-recovery", commandId + ".json"))?["receipt"]?.AsObject();
            }
            string? state = null;
            JsonObject? proof = null;
            // The unchanged planner may overwrite the shared record while returning
            // from its callback. Retain the independent managed proof across a crash.
            if (GuildStore.ValidId(commandId))
            {
                var managed = DailyJson.TryRead<JsonObject>(Path.Combine(root, "live", "field-talents", commandId + ".json"));
                var c = managed?["transport"]?["command"]?.AsObject();
                if (managed?["state"]?.GetValue<string>() == "completed" && c != null && c["Kind"]?.GetValue<string>() == "mainline_talent" && DailyEvidence.SameActor(c, original) && c["Id"]?.GetValue<string>() == commandId && c["Reason"]?.GetValue<string>()?.StartsWith("business:" + op["id"]!.GetValue<string>() + "|", StringComparison.Ordinal) == true && JsonNode.DeepEquals(c["Value"], op["action"]!["value"]))
                {
                    try
                    {
                        proof = DailyFieldTalentProof.Verify(op, managed["events"]!.AsArray(), managed["after"]!.AsObject());
                        state = "completed";
                        op["after"] = managed["after"]!.DeepClone();
                        events = managed["events"]!.DeepClone().AsArray();
                    }
                    catch (InvalidDataException) { }
                }
            }
            if (state == null)
                foreach (string key in new[] { "after", "last_observation" })
                    if (op[key] is JsonObject saved)
                    {
                        try
                        {
                            proof = DailyFieldTalentProof.Verify(op, events, saved);
                            state = "completed";
                            break;
                        }
                        catch (InvalidDataException) { }
                    }
            if (state == null && receipt != null && DailyFieldTalentProof.LegacyResearchRejected(op, receipt, DailyFieldTalentProof.ResearchWindowEvents(op, events)))
                state = "superseded";
            if (state == null)
                continue; // The compatibility reconciler keeps every unresolved role/scope visible.
            op["reconciliation"] = new JsonObject { ["previous_state"] = op["state"]!.DeepClone(), ["at"] = now(), ["method"] = state == "completed" ? "native_talent_response_and_usage" : "returned_click_no_request_and_inactive_long_research", ["actions"] = 0 };
            op["state"] = state;
            op["events"] = events;
            if (proof != null)
                op["result"] = proof;
            DailyJson.Write(path, op);
            completed.Add(new JsonObject { ["id"] = op["id"]!.DeepClone(), ["role"] = "dispatch.start", ["state"] = state, ["stages"] = new JsonArray("weekly_mainline") });
        }
        return report;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
static class FieldTalentCases
{
    static JsonObject Frame()
    {
        var f = CommandDriverCases.Frame();
        f["BridgeVersion"] = 104;
        f["Surfaces"]![0]!["Type"] = "QuickMenuUI";
        f["Surfaces"]![0]!["InputReady"] = true;
        return f;
    }
    static JsonObject Reading(string id, string path, JsonNode? value, int instance = 0) => new() { ["Id"] = id, ["InstanceId"] = instance, ["Error"] = "", ["Values"] = new JsonArray(new JsonObject { ["Path"] = path, ["Error"] = "", ["Json"] = value?.ToJsonString() ?? "null" }) };
    static JsonObject Evidence(string gate = "", bool busy = false, long at = 100000000) => new()
    {
        ["AtUtcTicks"] = at,
        ["Error"] = "",
        ["Config"] = "test",
        ["Frame"] = Frame(),
        ["Taps"] = new JsonArray("dispatch.start"),
        ["Readings"] = new JsonArray(
        Reading("mainline.talent_rows", "$self", new JsonArray(new JsonObject { ["Instance"] = 5, ["Group"] = 605, ["Kind"] = 6, ["Cooldown"] = 0, ["Gate"] = gate })),
        Reading("mainline.talent_counts", "Values", new JsonArray()), Reading("mainline.talent_state", "ὧὪὯὮὬὧὪὤὩὩὦ", JsonValue.Create(busy)),
        Reading("mainline.research", "$self", new JsonObject { ["Active"] = false, ["Remaining"] = 0 }),
        Reading("dispatch.talent", "ὣὡὪὭὤὨὭὣὪὧὩ", new JsonObject { ["groupId"] = 605, ["classType"] = 6, ["valueList"] = new JsonArray(9999, 180, 0) }, 5))
    };
    static JsonObject Action(string id) => new() { ["ui"] = "QuickMenuUI", ["operation"] = "mainline_talent", ["value"] = 5, ["reason"] = "business:" + id + "|test" };
    static JsonObject Op(string id) => new() { ["id"] = id, ["role"] = "dispatch.start", ["scope"] = new JsonObject { ["kind"] = 6, ["map"] = 143, ["group"] = 605 }, ["action"] = Action(id), ["before"] = Evidence(busy: true), ["at"] = 100000000, ["state"] = "dispatching", ["account"] = CommandDriverCases.Context()["actor"]![3]!.DeepClone(), ["player"] = CommandDriverCases.Context()["actor"]![4]!.DeepClone(), ["server"] = "test", ["cycle"] = "today" };
    static JsonObject Event(string kind, int sequence) => new() { ["Kind"] = kind, ["Role"] = "dispatch.start", ["Sequence"] = sequence, ["Frame"] = Frame(), ["Error"] = "", ["ErrorCode"] = 0, ["Accepted"] = kind == "response", ["Values"] = kind == "request" ? new JsonArray() : new JsonArray(new JsonObject { ["Path"] = "IsSuccess", ["Error"] = "", ["Json"] = "true" }, new JsonObject { ["Path"] = "TalentSkillInfo", ["Error"] = "", ["Json"] = "{\"groupId\":605}" }) };
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool v, string name)
        {
            if (!v)
                throw new Exception(name);
            cases.Add(name);
        }
        var op = Op(new string('c', 32));
        var command = DailyCommandDriver.BuildCommand(Frame(), Frame()["Surfaces"]![0]!.AsObject(), 0, Action(new string('c', 32)), 100000000, new string('d', 32), "business:" + new string('c', 32) + "|test");
        op["command_id"] = new string('d', 32);
        op["last_observation"] = Evidence(at: 350000000);
        var receipt = new JsonObject { ["Command"] = command, ["State"] = "observed_after_dispatch", ["MayHaveDispatched"] = true, ["Error"] = "", ["Before"] = Frame(), ["After"] = Frame() };
        Check(DailyFieldTalentProof.LegacyResearchRejected(op, receipt, new()), "legacy research busy-to-idle, long inactive effect and returned click prove non-cast without claiming success");
        var lateRequest = Event("request", 76);
        lateRequest["Frame"]!["AtUtcTicks"] = 1400000000;
        var lateResponse = Event("response", 78);
        lateResponse["Frame"]!["AtUtcTicks"] = 1410000000;
        lateResponse["Values"]![1]!["Json"] = "{\"groupId\":1815}";
        var lateEvents = new JsonArray(lateRequest, lateResponse);
        Check(DailyFieldTalentProof.ResearchWindowEvents(op, lateEvents).Count == 0, "later confirmed daily dispatch pair is not assigned to an earlier research click");
        lateEvents[1]!["Values"]![1]!["Json"] = "{\"groupId\":605}";
        Check(DailyFieldTalentProof.ResearchWindowEvents(op, lateEvents).Count == 2, "late response for original research remains unresolved evidence");
        lateEvents[1]!["Values"]![1]!["Json"] = "{\"groupId\":1815}";
        lateEvents[1]!["Accepted"] = false;
        Check(DailyFieldTalentProof.ResearchWindowEvents(op, lateEvents).Count == 2, "failed later dispatch cannot be discarded while reconciling research");
        Check(DailyFieldTalentProof.ResearchWindowEvents(op, new JsonArray(lateRequest.DeepClone())).Count == 1, "orphan later request prevents automatic research supersession");
        foreach (string condition in new[] { "request", "short-effect", "actor", "menu", "count", "config", "tap", "receipt", "active", "busy", "kind" })
        {
            var bad = op.DeepClone().AsObject();
            var r = receipt.DeepClone().AsObject();
            var events = new JsonArray();
            switch (condition)
            {
                case "request":
                    events.Add(Event("request", 1));
                    break;
                case "short-effect":
                    bad["before"]!["Readings"]![4]!["Values"]![0]!["Json"] = "{\"groupId\":605,\"classType\":6,\"valueList\":[9999,30,0]}";
                    break;
                case "actor":
                    r["After"]!["Instance"] = "other";
                    break;
                case "menu":
                    r["After"]!["Surfaces"]![0]!["Id"] = 99;
                    break;
                case "count":
                    bad["last_observation"]!["Readings"]![1]!["Values"]![0]!["Json"] = "[{\"groupId\":605,\"useCount\":1}]";
                    break;
                case "config":
                    bad["last_observation"]!["Config"] = "other";
                    break;
                case "tap":
                    bad["last_observation"]!["Taps"] = new JsonArray();
                    break;
                case "receipt":
                    r["Error"] = "unknown";
                    break;
                case "active":
                    bad["last_observation"]!["Readings"]![3]!["Values"]![0]!["Json"] = "{\"Active\":true,\"Remaining\":160}";
                    break;
                case "busy":
                    bad["last_observation"]!["Readings"]![2]!["Values"]![0]!["Json"] = "true";
                    break;
                case "kind":
                    bad["scope"]!["kind"] = 4;
                    break;
            }
            Check(!DailyFieldTalentProof.LegacyResearchRejected(bad, r, events), "legacy talent recovery refuses " + condition);
        }
        var proof = DailyFieldTalentProof.Verify(op, new JsonArray(Event("request", 1), Event("response", 2)), Evidence());
        Check(proof["IsSuccess"]!.GetValue<bool>(), "managed talent proof accepts one matching successful response");
        var weekly = Evidence();
        weekly["Readings"]!.AsArray().Add(Reading("weekly_npc.native", "$self", new JsonObject { ["State"] = "ready", ["Error"] = "", ["Limit"] = 3, ["Completed"] = 3, ["Remaining"] = 0, ["Week"] = 500 }));
        weekly["Readings"]!.AsArray().Add(Reading("mainline.reset", "GetWeeklyResetTime().Ticks", JsonValue.Create(500)));
        Check(DailyWeeklyCompletion.Inspect(weekly, ["weekly_npc"])["weekly_npc"]?["state"]?.GetValue<string>() == "completed", "server-confirmed NPC completion survives shared collection interruption");
        Check(DailyWeeklyCompletion.Inspect(weekly, ["weekly_mainline"]).Count == 0, "weekly settlement never promotes an unselected NPC stage");
        foreach (string condition in new[] { "cycle", "limit", "partial", "state", "error" })
        {
            var bad = weekly.DeepClone().AsObject();
            var native = DailyEvidence.Reading(bad, "weekly_npc.native", "$self")!.AsObject();
            switch (condition)
            {
                case "cycle":
                    native["Week"] = 501;
                    break;
                case "limit":
                    native["Limit"] = 0;
                    break;
                case "partial":
                    native["Completed"] = 2;
                    break;
                case "state":
                    native["State"] = "invalid";
                    break;
                case "error":
                    native["Error"] = "stale";
                    break;
            }
            bad["Readings"]![5]!["Values"]![0]!["Json"] = native.ToJsonString();
            Check(DailyWeeklyCompletion.Inspect(bad, ["weekly_npc"]).Count == 0, "weekly settlement refuses " + condition);
        }
        foreach (string condition in new[] { "duplicate", "failed", "wrong-group", "ordering", "actor", "config", "usage" })
        {
            var bad = op.DeepClone().AsObject();
            var events = new JsonArray(Event("request", 1), Event("response", 2));
            var after = Evidence();
            switch (condition)
            {
                case "duplicate":
                    events.Add(Event("request", 3));
                    break;
                case "failed":
                    events[1]!["Accepted"] = false;
                    break;
                case "wrong-group":
                    events[1]!["Values"]![1]!["Json"] = "{\"groupId\":625}";
                    break;
                case "ordering":
                    events[0]!["Sequence"] = 3;
                    break;
                case "actor":
                    after["Frame"]!["PlayerKey"] = "other";
                    break;
                case "config":
                    after["Config"] = "other";
                    break;
                case "usage":
                    bad["scope"]!["kind"] = 20;
                    break;
            }
            bool rejected = false;
            try
            {
                DailyFieldTalentProof.Verify(bad, events, after);
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "managed talent business proof refuses " + condition);
        }
        foreach (string scenario in new[] { "busy", "ui-wait", "race", "native-race", "ui-race", "late", "pending", "duplicate", "stop", "stop-after", "map", "identity", "disabled", "missing-gate" })
        {
            string path = Path.Combine(output, "field-talent-" + scenario), id = Guid.NewGuid().ToString("N");
            var box = new CommandDriverCases.Mailbox();
            double time = 0;
            bool stopped = false;
            int observations = 0;
            bool sent = false;
            DailyJson.Write(Path.Combine(path, "live", "business", id + ".json"), Op(id));
            box.AfterWrite = (_, name, bytes) =>
            {
                if (name != "observation-request.json")
                    return;
                var request = JsonNode.Parse(bytes)!.AsObject();
                observations++;
                var e = Evidence(scenario == "busy" && time < 1 ? "talent_wait:field_skill" : scenario == "stop" ? "talent_wait:animation" : scenario == "disabled" ? "talent_reject:catalyst_insufficient" : "", at: 100000000 + (long)(time * TimeSpan.TicksPerSecond));
                e["ObservationRequest"] = request["Id"]!.DeepClone();
                if (scenario == "missing-gate")
                    e["Readings"]![0]!["Values"]![0]!["Json"] = "[{\"Instance\":5,\"Group\":605,\"Kind\":6,\"Cooldown\":0}]";
                box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
            };
            box.AfterCommand = c =>
            {
                sent = true;
                if (scenario == "stop-after")
                    stopped = true;
                if (scenario is "pending" or "stop" or "stop-after" or "map" or "identity" or "disabled" or "missing-gate" or "late")
                    return;
                foreach (var e in new[] { Event("request", 1), Event("response", 2) })
                    box.Values["live:events~100000000-" + e["Sequence"]!.GetValue<int>() + ".json"] = JsonSerializer.SerializeToUtf8Bytes(e);
                if (scenario == "duplicate")
                    box.Values["live:events~100000000-3.json"] = JsonSerializer.SerializeToUtf8Bytes(Event("request", 3));
            };
            if (scenario is "race" or "native-race" or "ui-race")
            {
                box.State = "rejected";
                box.Dispatched = false;
                if (scenario == "race")
                    box.Error = "unclassified_native_rejection";
                if (scenario == "native-race")
                    box.Error = "talent_wait:animation";
                if (scenario == "ui-race")
                    box.Error = "ui_not_ready";
                box.AfterWrite = (_, name, bytes) => { if (name == "observation-request.json") { var r = JsonNode.Parse(bytes)!.AsObject(); var e = Evidence(at: 100000000 + (long)(time * TimeSpan.TicksPerSecond)); e["ObservationRequest"] = r["Id"]!.DeepClone(); box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e); } };
            }
            using var driver = new DailyCommandDriver(path, box, () => { var f = Frame(); var c = CommandDriverCases.Context(); if (scenario == "ui-wait" && time < 1) f["Surfaces"]![0]!["InputReady"] = false; if (observations > 0 && scenario == "map") f["Scene"] = "changed"; if (observations > 0 && scenario == "identity") c["cycle"] = "changed"; return Task.FromResult(new DailyStageFrame(f, c)); }, () => stopped, () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t =>
            {
                time += t.TotalSeconds;
                box.Values.Remove("live:observation-request.json");
                if (scenario == "stop")
                    stopped = true;
                if (scenario == "late" && sent && time >= 1)
                    foreach (var e in new[] { Event("request", 1), Event("response", 2) })
                        box.Values["live:events~100000000-" + e["Sequence"]!.GetValue<int>() + ".json"] = JsonSerializer.SerializeToUtf8Bytes(e);
                if (scenario is "native-race" or "ui-race" && box.Commands.Count > 0)
                {
                    box.State = "observed_after_dispatch";
                    box.Dispatched = true;
                }
                if (scenario == "race" && box.Commands.Count > 0)
                {
                    var receiptKey = box.Values.Keys.Single(k => k.StartsWith("live:receipts~"));
                    var r = JsonNode.Parse(box.Values[receiptKey])!.AsObject();
                    r["Error"] = "talent_wait:animation";
                    box.Values[receiptKey] = JsonSerializer.SerializeToUtf8Bytes(r);
                    box.State = "observed_after_dispatch";
                    box.Dispatched = true;
                }
                return Task.CompletedTask;
            });
            driver.Bind(CommandDriverCases.Context());
            string error = "";
            try
            {
                await driver.SubmitAsync(Action(id));
            }
            catch (DailyStepException e) { error = e.Kind; }
            catch (StageHostException e) { error = e.Kind; }
            if (scenario is "busy" or "ui-wait")
                Check(error == "" && sent && time >= 1 && box.Commands.Count == 1, "busy native talent " + scenario + " waits then issues one verified request");
            else if (scenario is "native-race" or "ui-race")
                Check(error == "" && box.Commands.Count == 2, "explicit undispatched native " + scenario + " permits exactly one new guarded attempt");
            else if (scenario == "late")
                Check(error == "" && box.Commands.Count == 1 && time >= 1, "delayed talent RPC is reconciled without a duplicate input");
            else if (scenario == "race")
                Check(error == "rejected" && box.Commands.Count == 1, "unclassified native rejection does not replay even when readiness changed");
            else if (scenario is "pending" or "duplicate" or "stop-after")
                Check(error == "pending" && box.Commands.Count == 1, "talent " + scenario + " preserves one original command without replay");
            else
                Check(error.Length > 0 && box.Commands.Count == 0, "talent preflight " + scenario + " submits no input");
            if (scenario == "busy")
            {
                string business = Path.Combine(path, "live", "business", id + ".json");
                var saved = DailyJson.TryRead<JsonObject>(business)!;
                Check(saved["state"]?.GetValue<string>() == "completed" && saved["after"] != null, "managed talent writes durable native proof before returning to the compatibility planner");
                saved["state"] = "unknown";
                saved.Remove("after");
                saved["events"] = new JsonArray();
                DailyJson.Write(business, saved);
                var report = await driver.ReconcileFieldTalentsAsync();
                Check(report["actions"]!.GetValue<int>() == 0 && report["completed"]!.AsArray().Count == 1 && DailyJson.TryRead<JsonObject>(business)!["state"]?.GetValue<string>() == "completed" && box.Commands.Count == 1, "managed talent recovers proof after planner overwrite or crash without replay");
            }
        }
    }
}

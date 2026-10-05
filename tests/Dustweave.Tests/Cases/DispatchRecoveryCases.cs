using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class DispatchRecoveryCases
{
    private const long Now = 1791011500000, End = 1791072000000;
    private static readonly long[] Slots = [2, 12, 21, 1022, 1031, 1033];
    private static JsonObject Timer(long id, long end, long now) => O(("Id", id), ("EndTime", end), ("ServerNowTime", now));
    private static JsonObject Wire(long id, bool text = true) => O(("id", id), ("endTime", text ? (object)End.ToString() : End), ("serverNowTime", text ? (object)Now.ToString() : Now));
    private static JsonObject Evidence(bool after, bool extra = true) => WorkflowCases.Evidence(
        WorkflowCases.Reading("dispatch.cache", ("Count", Slots.Length + (extra ? 1 : 0)), ("$items", Array(Slots.Select(id => Timer(id, after ? End : Now - 1000, after ? Now : Now - 2000)).Concat(extra ? [Timer(99, End + 1000, Now - 1000)] : [])))),
        WorkflowCases.Reading("dispatch.clock", ("UnixTimeStamp()", Now)),
        WorkflowCases.Reading("dispatch.global.totalwar", ("$self", !after)),
        WorkflowCases.Reading("dispatch.global.evilcastle", ("$self", !after)),
        WorkflowCases.Reading("dispatch.global.auto", ("$self", true)),
        WorkflowCases.Reading("dispatch.global.chain", ("Count", 0)),
        WorkflowCases.Reading("dispatch.global.wait", ("waitResetPackKeyWordSet", new JsonArray())),
        WorkflowCases.Reading("dispatch.global.collecting", ("$self", false)),
        WorkflowCases.Reading("dispatch.claim_ids", ("Count", Slots.Length), ("_items", Array(Slots.Select(x => JsonValue.Create(x))))));
    private static JsonArray Events(bool text = true) => new(
        WorkflowCases.Event("dispatch.reward", "request", 1),
        WorkflowCases.Event("dispatch.totalwar", "request", 2),
        WorkflowCases.Event("dispatch.evilcastle", "request", 3),
        WorkflowCases.Event("dispatch.reward", "response", 4, ("ItemInfo", new JsonArray(O(("type", 12), ("count", 480))))),
        WorkflowCases.Event("dispatch.totalwar", "response", 5, ("RewardInfoBundle", new JsonObject())),
        WorkflowCases.Event("dispatch.start", "request", 6),
        WorkflowCases.Event("dispatch.evilcastle", "response", 7, ("RewardInfoBundle", new JsonObject())),
        WorkflowCases.Event("dispatch.start", "response", 8, ("DispatchInfo", Array(Slots.Skip(3).Select(id => Wire(id, text)))), ("IsSuccess", true)),
        WorkflowCases.Event("dispatch.start", "request", 9),
        WorkflowCases.Event("dispatch.start", "response", 10, ("DispatchInfo", Array(Slots.Take(3).Select(id => Wire(id, text)))), ("IsSuccess", true)));
    private static void Set(JsonObject evidence, string id, string path, JsonNode value)
    {
        var reading = Rows(evidence["Readings"]).Single(r => S(r["Id"]) == id);
        Rows(reading["Values"]).Single(r => S(r["Path"]) == path)["Json"] = value.ToJsonString();
    }
    private static JsonArray Cache(JsonObject e) => R(e, "dispatch.cache", "$items")!.AsArray();
    private static void Add(JsonObject e, string id, params (string Key, object? Value)[] values) => e["Readings"]!.AsArray().Add(WorkflowCases.Reading(id, values));
    private static (JsonObject Before, JsonArray Events, JsonObject After) Character()
    {
        var before = Evidence(false, false); var after = Evidence(true, false);
        const string table = "ὦὢὧὧὥὥὫὡὧὣὪ";
        Add(before, "dispatch.ui", (table, O(("classType", 18), ("catalystValue", 10), ("valueList", Array(Slots.Select(x => JsonValue.Create(x)))))),
            ("ὡὯὫὤὯὡὪὨὪὪὨ", false), ("ὦὤὯὪὧὩὬὨὪὦὠ", false), ("_goMaskRoot.activeSelf", true),
            ("ὬὩὠὡὬὯὨὪὡὩὮ", Array(Slots.Select(x => JsonValue.Create(x)))), ("ὤὨὯὥὦὫὤὯὬὨὣ", "AvailableRewards"),
            ("_currencyUseButton.ὪὤὪὥὪὭὨὨὪὡὨ", true), ("ὢὧὭὧὡὯὦὧὥὩὮ.InvenIndex", 123));
        Add(before, "dispatch.currency", ("Catalyst", 1000));
        Add(after, "dispatch.currency", ("Catalyst", 1420));
        return (before, new JsonArray(WorkflowCases.Event("dispatch.reward", "request", 1),
            WorkflowCases.Event("dispatch.reward", "response", 2, ("ItemInfo", new JsonArray(O(("type", 12), ("count", 480))))),
            WorkflowCases.Event("dispatch.start", "request", 3),
            WorkflowCases.Event("dispatch.start", "response", 4, ("DispatchInfo", Array(Slots.Select(id => Wire(id)))), ("IsSuccess", true))), after);
    }
    public static async Task Run(string root, List<string> cases, string? captured = null)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); cases.Add(label); }
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, label);
        }
        foreach (bool text in new[] { true, false })
        {
            var proof = DailyDispatch.VerifyGlobal(Evidence(false), Events(text), Evidence(true));
            Check(Rows(R(Evidence(true), "dispatch.cache", "$items")).Length == 7 && proof["started"]!.AsArray().Select(N).SequenceEqual(Slots), "dispatch global validates both redispatch batches with " + (text ? "protobuf string int64" : "numeric int64"));
            Check(proof["claimed"]!.AsArray().Count == 6 && B(proof["totalwar_claimed"]) && B(proof["evilcastle_claimed"]), "dispatch verifies all six due slots and both extra reward branches " + text);
        }
        foreach (string field in new[] { "EndTime", "ServerNowTime" })
        {
            var after = Evidence(true); var cache = Cache(after); cache[0]![field] = N(cache[0]![field]) + 1;
            Set(after, "dispatch.cache", "$items", cache);
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), Events(), after), "dispatch still rejects an actual one-millisecond mismatch in " + field);
        }
        {
            var after = Evidence(true); var cache = Cache(after); cache.RemoveAt(0); Set(after, "dispatch.cache", "$items", cache); Set(after, "dispatch.cache", "Count", JsonValue.Create(cache.Count)!);
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), Events(), after), "dispatch missing one restarted slot cannot be marked complete");
        }
        {
            var after = Evidence(true); var cache = Cache(after); cache[^1]!["EndTime"] = End + 1001; Set(after, "dispatch.cache", "$items", cache);
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), Events(), after), "dispatch rejects changes to an unrelated active slot");
        }
        {
            var after = Evidence(true); Set(after, "dispatch.claim_ids", "Count", JsonValue.Create(3)!);
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), Events(), after), "dispatch partial reward collection cannot count as all six claims");
        }
        {
            var events = Events(); events.RemoveAt(events.Count - 1);
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), events, Evidence(true)), "dispatch incomplete second start response remains pending");
        }
        foreach (var wrong in new (string Field, JsonNode? Value)[] { ("id", JsonValue.Create(0)), ("endTime", null), ("serverNowTime", JsonValue.Create(0)), ("endTime", JsonValue.Create(Now.ToString())), ("serverNowTime", JsonValue.Create("invalid")) })
        {
            var events = Events(); var response = DailyEvidence.Values(events[7]!.AsObject()); var timers = response["DispatchInfo"]!.AsArray(); timers[0]![wrong.Field] = wrong.Value?.DeepClone();
            events[7]!["Values"]![0]!["Json"] = timers.ToJsonString();
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), events, Evidence(true)), "dispatch malformed wire " + wrong.Field + ":" + S(wrong.Value) + " is not coerced into success");
        }
        {
            var events = Events(); var timers = DailyEvidence.Values(events[7]!.AsObject())["DispatchInfo"]!.AsArray(); timers[1]!["id"] = timers[0]!["id"]!.DeepClone(); events[7]!["Values"]![0]!["Json"] = timers.ToJsonString();
            Reject(() => DailyDispatch.VerifyGlobal(Evidence(false), events, Evidence(true)), "dispatch duplicated returned slot is rejected");
        }
        var character = Character();
        Check(N(DailyDispatch.VerifyCharacter(character.Before, character.Events, character.After)["catalyst_spent"]) == 60, "single-character dispatch uses normalized wire timers and exact catalyst accounting");
        Set(character.After, "dispatch.currency", "Catalyst", JsonValue.Create(1419)!);
        Reject(() => DailyDispatch.VerifyCharacter(character.Before, character.Events, character.After), "normalizing timers never loosens catalyst debit checks");
        using (var f = new WorkflowHarness(Path.Combine(root, "dispatch-original-recovery"), DailyDispatch.Proofs().ToArray()))
        {
            var before = Evidence(false); before["Frame"] = f.Frame.DeepClone(); before["Taps"] = new JsonArray(DailyDispatch.Proofs().First().EventRoles.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
            var after = Evidence(true); after["Frame"] = f.Frame.DeepClone();
            var events = Events(); foreach (var e in events) e!["Frame"] = f.Frame.DeepClone();
            var op = f.Business.Create(f.Context, "dispatch.collect_all", before, DailyDispatch.Global(before), O(("ui", "GameFieldDefaultUI"), ("operation", "dispatch_collect_all")));
            op["state"] = "unknown"; op["error"] = "Global redispatch response/cache differs"; op["events"] = events.DeepClone(); op["last_observation"] = after.DeepClone(); f.Business.Save(op);
            var result = await f.Business.ReconcileAsync(f.Context);
            Check(result["completed"]!.AsArray().Count == 1 && result["unresolved"]!.AsArray().Count == 0 && f.Box.Commands.Count == 0, "old false dispatch failure reconciles original receipts with zero game input");
            var saved = f.Business.Records(f.Context).Single();
            Check(S(saved["state"]) == "completed" && saved["error"] == null && N(saved["reconciliation"]!["actions"]) == 0, "recovered dispatch proof persists without resending claims or catalyst costs");
            await f.Business.ReconcileAsync(f.Context);
            Check(f.Box.Commands.Count == 0 && f.Business.Records(f.Context).Count() == 1, "repeated dispatch reconciliation is idempotent");
        }
        if (captured != null)
        {
            var op = DailyJson.TryRead<JsonObject>(captured) ?? throw new Exception("Captured dispatch record unavailable");
            var proof = DailyDispatch.VerifyGlobal(op["before"]!.AsObject(), op["events"]!.AsArray(), op["last_observation"]!.AsObject());
            DailyJson.Write(Path.Combine(root, "captured-dispatch-proof.json"), proof);
            Check(proof["claimed"]!.AsArray().Count == 6 && proof["started"]!.AsArray().Count == 6 && B(proof["cache_matched"]), "actual October 3 failed dispatch record proves all six claims and restarts unchanged");
        }
    }
}

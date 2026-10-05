using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailyDispatch
{
    private const string Ui = "DispatchSelectUI", Ids = "ὬὩὠὡὬὯὨὪὡὩὮ", Character = "ὢὧὭὧὡὯὦὧὥὩὮ.InvenIndex", Table = "ὦὢὧὧὥὥὫὡὧὣὪ", StatePath = "ὤὨὯὥὦὫὤὯὬὨὣ", Claiming = "ὡὯὫὤὯὡὪὨὪὪὨ", Starting = "ὦὤὯὪὧὩὬὨὪὦὠ", Enough = "_currencyUseButton.ὪὤὪὥὪὭὨὨὪὡὨ", TalentCharacter = "ὯὠὭὬὭὢὣὨὠὬὢ", TalentKind = "ὬὩὬὡὯὮὨὣὩὦὡ";
    private static readonly string[] Prefixes = ["dispatch", "mirror.field", "mirror.input"];
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        yield return new("dispatch.collect_all", "daily_dispatch", Prefixes, (op, events, after) => VerifyGlobal(op["before"]!.AsObject(), events, after), ["dispatch.reward", "dispatch.start", "dispatch.totalwar", "dispatch.evilcastle"]);
        yield return new("dispatch.claim_and_start", "daily_dispatch", Prefixes, (op, events, after) => VerifyCharacter(op["before"]!.AsObject(), events, after), ["dispatch.reward", "dispatch.start"]);
    }
    public static Dictionary<long, JsonObject> Dispatches(JsonObject e)
    {
        var rows = Rows(R(e, "dispatch.cache", "$items"));
        Require(rows.Length == N(R(e, "dispatch.cache", "Count")) && rows.Select(r => N(r["Id"])).Distinct().Count() == rows.Length, "Incomplete dispatch cache");
        Require(rows.All(r => N(r["Id"]) > 0 && N(r["EndTime"]) > 0 && N(r["ServerNowTime"]) > 0), "Invalid dispatch timestamp");
        return rows.ToDictionary(r => N(r["Id"]));
    }
    public static JsonObject Global(JsonObject e)
    {
        long now = N(R(e, "dispatch.clock", "UnixTimeStamp()"));
        return O(("due", Dispatches(e).Where(p => N(p.Value["EndTime"]) <= now).Select(p => p.Key).Order().ToArray()), ("totalwar", R(e, "dispatch.global.totalwar", "$self")), ("evilcastle", R(e, "dispatch.global.evilcastle", "$self")), ("auto", R(e, "dispatch.global.auto", "$self")), ("busy", N(R(e, "dispatch.global.chain", "Count")) > 0 || R(e, "dispatch.global.wait", "waitResetPackKeyWordSet")!.AsArray().Any(n => S(n) == "DispatchAutoSupport") || B(R(e, "dispatch.global.collecting", "$self"))));
    }
    public static JsonObject Clear(JsonObject e)
    {
        var s = Global(e);
        Require(s["due"]!.AsArray().Count == 0 && !B(s["totalwar"]) && !B(s["evilcastle"]) && !B(s["busy"]), "Native global dispatch chain is incomplete");
        return s;
    }
    private static long[] ClaimIds(JsonObject e)
    {
        int count = I(R(e, "dispatch.claim_ids", "Count"));
        var rows = R(e, "dispatch.claim_ids", "_items")!.AsArray();
        Require(count >= 0 && rows.Count >= count && rows.Take(count).All(n => n != null), "Dispatch claim IDs truncated");
        return rows.Take(count).Select(N).Order().ToArray();
    }
    private static Dictionary<long, JsonObject> Normalize(IEnumerable<JsonObject> rows)
    {
        var result = new Dictionary<long, JsonObject>();
        foreach (var r in rows)
        {
            // Protobuf JSON renders int64 as strings; the live cache exposes CLR long numbers.
            // Normalize at the network boundary, then retain exact slot/timer equality.
            long id = N(r["id"]), end = N(r["endTime"]), now = N(r["serverNowTime"]);
            Require(id > 0 && now > 0 && end > now, "Invalid redispatch slot or timestamp");
            Require(result.TryAdd(id, O(("Id", id), ("EndTime", end), ("ServerNowTime", now))), "Duplicate redispatch slot");
        }
        return result;
    }
    public static JsonObject VerifyGlobal(JsonObject before, JsonArray events, JsonObject after)
    {
        var plan = Global(before);
        Clear(after);
        Require(!B(plan["busy"]), "Native dispatch chain was already active");
        var due = plan["due"]!.AsArray().Select(N).ToHashSet();
        var rewards = new JsonObject();
        foreach (var (kind, needed) in new[] { ("reward", due.Count > 0), ("totalwar", B(plan["totalwar"])), ("evilcastle", B(plan["evilcastle"])) })
        {
            var reply = Response("dispatch." + kind, events, before, after, !needed);
            Require(needed || reply == null, "Unexpected dispatch reward branch");
            if (reply != null)
                rewards[kind] = reply;
        }
        if (due.Count > 0)
            Require(ClaimIds(after).SequenceEqual(due.Order()), "Global claim did not cover all due slots");
        var started = Normalize(Responses("dispatch.start", events, before, after, 0).SelectMany(r => Rows(r["DispatchInfo"])));
        var old = Dispatches(before);
        var latest = Dispatches(after);
        Require(started.All(p => latest.TryGetValue(p.Key, out var v) && JsonNode.DeepEquals(v, p.Value) && N(v["EndTime"]) > N(v["ServerNowTime"])), "Global redispatch response/cache differs");
        var untouched = old.Keys.Except(due).ToHashSet();
        Require(untouched.All(i => latest.TryGetValue(i, out var v) && JsonNode.DeepEquals(v, old[i])) && latest.Keys.ToHashSet().SetEquals(untouched.Concat(started.Keys)), "Unexplained global dispatch cache change");
        return O(("claimed", due.Order().ToArray()), ("started", started.Keys.Order().ToArray()), ("claimed_ids_absent_from_new_timers", due.Except(latest.Keys).Order().ToArray()), ("totalwar_claimed", plan["totalwar"]), ("evilcastle_claimed", plan["evilcastle"]), ("rewards", rewards), ("cache_matched", true), ("global_clear", true));
    }
    public static JsonObject Preflight(JsonObject e)
    {
        var table = State(e, "dispatch.ui", Table);
        Require(N(table["classType"]) == 18, "Selected talent is not dispatch");
        Require(!B(R(e, "dispatch.ui", Claiming)) && !B(R(e, "dispatch.ui", Starting)) && B(R(e, "dispatch.ui", "_goMaskRoot.activeSelf")), "Native dispatch chain is active");
        var ids = R(e, "dispatch.ui", Ids)!.AsArray().Select(N).Where(i => i != 0).ToArray();
        Require(ids.Distinct().Count() == ids.Length, "Duplicate dispatch slots");
        long now = N(R(e, "dispatch.clock", "UnixTimeStamp()"));
        var cache = Dispatches(e);
        var due = ids.Where(i => cache.TryGetValue(i, out var r) && N(r["EndTime"]) <= now).Order().ToArray();
        var idle = ids.Where(i => !cache.ContainsKey(i)).Order().ToArray();
        int count = due.Length + idle.Length;
        long cost = checked(N(table["catalystValue"]) * count), catalyst = N(R(e, "dispatch.currency", "Catalyst"));
        Require(cost >= 0 && catalyst >= 0, "Invalid dispatch catalyst cost");
        string expected = due.Length > 0 ? "AvailableRewards" : idle.Length > 0 ? "NoRewards" : "AllDispatched";
        Require(S(R(e, "dispatch.ui", StatePath)) == expected, "Native dispatch eligibility differs");
        if (count > 0)
            Require(B(R(e, "dispatch.ui", Enough)) == (catalyst >= cost), "Native catalyst budget differs");
        return O(("character", R(e, "dispatch.ui", Character)), ("due", due), ("idle", idle), ("count", count), ("allowed_start", table["valueList"]!.AsArray().Select(N).Where(i => i != 0).Distinct().Order().ToArray()), ("cost", cost), ("enough", catalyst >= cost), ("server_time", now));
    }
    public static JsonObject VerifyCharacter(JsonObject before, JsonArray events, JsonObject after)
    {
        var plan = Preflight(before);
        var due = plan["due"]!.AsArray().Select(N).ToHashSet();
        var old = Dispatches(before);
        var latest = Dispatches(after);
        var reward = Response("dispatch.reward", events, before, after, due.Count == 0);
        Require(due.Count > 0 || reward == null, "Unexpected dispatch claim");
        if (due.Count > 0)
        {
            Require(reward?["ItemInfo"] is JsonArray a && a.Count > 0, "Missing dispatch rewards");
            Require(ClaimIds(after).SequenceEqual(due.Order()), "Dispatch claimed IDs differ");
        }
        long gained = reward?["ItemInfo"] is JsonArray items ? Rows(items).Where(r => N(r["type"]) == 12).Sum(r => N(r["count"])) : 0;
        bool enough = N(R(before, "dispatch.currency", "Catalyst")) + gained >= N(plan["cost"]);
        var start = Response("dispatch.start", events, before, after, !(N(plan["count"]) > 0 && enough));
        Require(enough || start == null, "Unexpected dispatch without catalyst");
        var normalized = Normalize(start == null ? [] : Rows(start["DispatchInfo"]));
        if (start != null)
        {
            Require(normalized.Count == N(plan["count"]) && normalized.Keys.All(i => plan["allowed_start"]!.AsArray().Select(N).Contains(i)), "Redispatch slot set differs");
            Require(normalized.All(p => N(p.Value["EndTime"]) > N(p.Value["ServerNowTime"]) && N(p.Value["ServerNowTime"]) >= N(plan["server_time"]) - 5000 && latest.TryGetValue(p.Key, out var v) && JsonNode.DeepEquals(v, p.Value)), "Redispatch timers/cache differ");
        }
        var untouched = old.Keys.Except(due).ToHashSet();
        Require(untouched.All(i => latest.TryGetValue(i, out var v) && JsonNode.DeepEquals(v, old[i])) && latest.Keys.ToHashSet().SetEquals(untouched.Concat(normalized.Keys)), "Unrelated dispatch changed");
        long debit = N(R(before, "dispatch.currency", "Catalyst")) + gained - N(R(after, "dispatch.currency", "Catalyst"));
        Require(debit == (start != null ? N(plan["cost"]) : 0), "Dispatch catalyst debit mismatch");
        return O(("claimed", due.Order().ToArray()), ("started", normalized.Keys.Order().ToArray()), ("catalyst_spent", debit), ("catalyst_received", gained), ("cache_matched", true), ("state", enough ? "completed" : "partial"), ("reason", enough ? "" : "insufficient_catalyst"));
    }
    private static async Task Page(DailyWorkflow w, string ui, bool settled = false)
    {
        await DailyTravel.Ready(w, ui, 35);
        await w.WaitEvidence(Prefixes, e => { string prefix = ui == "RewardReceivePopupUI" ? "reward.presentation" : "dispatch.presentation." + ui; bool ready = new[] { "ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ", "ὮὬὧὦὣὠὠὤὮὦὧ" }.All(p => B(R(e, prefix, p))) && !B(R(e, prefix, "ὦὡὮὫὧὠὮὡὭὭὬ")); if (ui == "QuickMenuUI") { ready &= !new[] { "ὧὭὨὢὥὩὫὯὠὫὭ", "ὦὣὩὣὧὯὧὠὢὣὢ", "ὠὩὠὩὫὪὥὧὢὫὭ" }.Any(p => B(R(e, "dispatch.quick", p))); const string root = "ὪὨὯὢὫὮὨὩὮὡὬ.ὯὧὨὨὡὭὡὭὧὥὧ."; ready &= !B(R(e, "dispatch.motion", root + "ὦὡὮὫὧὠὮὡὭὭὬ")) || B(R(e, "dispatch.motion", root + "ὯὨὤὪὤὢὠὨὨὤὣ")); } return ready && (!settled || !B(R(e, "dispatch.ui", Claiming)) && !B(R(e, "dispatch.ui", Starting)) && B(R(e, "dispatch.ui", "_goMaskRoot.activeSelf"))); }, 35, "派遣页面或自动续派动画尚未完成");
    }
    private static async Task Close(DailyWorkflow w, string ui)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!await w.Has(ui))
                return;
            await Page(w, ui, ui == Ui);
            await w.Step(ui, ui == "QuickMenuUI" ? "_buttonClose" : null, back: ui != "QuickMenuUI", reason: "关闭已结算的派遣展示");
            double end = w.Time + 3;
            while (w.Time < end)
            {
                if (!await w.Has(ui))
                    return;
                await w.Delay(200);
            }
        }
        throw new StageHostException("adapter", "派遣页面尚未关闭；没有重发领取或续派。");
    }
    private static async Task Field(DailyWorkflow w)
    {
        await DailyTravel.Ready(w, "GameFieldDefaultUI");
        await w.WaitEvidence(Prefixes, e => S(R(e, "mirror.input", "ὨὢὤὪὪὢὩὢὬὠὦ")) == "BMT_NONE" && double.Parse(S(R(e, "mirror.field", "_hideObjectCanvasGroup.alpha")), System.Globalization.CultureInfo.InvariantCulture) >= 1 && new[] { "_buttonMenu.interactable", "ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ" }.All(p => B(R(e, "mirror.field", p))), 30, "派遣后地图操作尚未就绪");
    }
    private static bool RecoveryEligible(JsonObject e)
    {
        try
        {
            return N(e["Frame"]?["BridgeVersion"]) >= 66 && N(R(e, "dispatch.spawn.field", "ὮὩὡὢὦὥὪὧὭὩὢ.Count")) > 0 && N(R(e, "dispatch.spawn.assets", "Count")) == 0 && new[] { "ὯὤὥὫὨὡὢὯὧὦὢ.Count", "ὮὫὮὤὡὭὦὩὢὩὫ.Count", "ὠὮὡὥὢὨὣὬὧὠὦ.Count" }.All(p => N(R(e, "dispatch.spawn.pending", p)) == 0);
        }
        catch (InvalidDataException) { return false; }
    }
    private static async Task<int> Messenger(DailyWorkflow w)
    {
        double started = w.Time, end = started + 25;
        bool recovered = false;
        while (w.Time < end)
        {
            var e = await w.Evidence(Prefixes);
            var ids = Rows(e["Readings"]).Where(r => S(r["Id"]) is "dispatch.global.field" or "dispatch.global.totalwar_field").Where(r => { var v = DailyEvidence.Values(r); return B(v["gameObject.activeInHierarchy"]) && !B(v["ὦὮὠὩὣὣὮὤὩὥὫ"]) && B(v["ὩὤὨὮὥὦὫὭὫὭὨ"]); }).Select(r => I(r["InstanceId"])).Order().ToArray();
            if (ids.Length > 0)
                return ids[0];
            if (!recovered && w.Time - started >= 3 && RecoveryEligible(e) && !B(Global(e)["busy"]))
            {
                recovered = true;
                string key = string.Join("|", w.Context["actor"]!.AsArray().Where((_, i) => i != 2).Select(S)) + "|" + N(R(e, "dispatch.clock", "UnixTimeStamp()")) / 86400000;
                string path = Path.Combine(w.Root, "live", "dispatch-recovery-attempts", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".json");
                Require(!File.Exists(path), "Messenger repair already attempted in this game/day");
                var record = O(("state", "prepared"), ("at", w.Driver.UtcTicks), ("before", e), ("global_state", Global(e)));
                DailyJson.Write(path, record);
                var command = await w.Step("GameFieldDefaultUI", operation: "dispatch_recover", reason: "恢复游戏失效的派遣信使加载标记");
                record["state"] = "dispatched";
                record["command"] = command["id"]!.DeepClone();
                DailyJson.Write(path, record);
                double reportUntil = w.Time + 12;
                bool acknowledged = false;
                try
                {
                    while (w.Time < reportUntil)
                    {
                        await w.Observe();
                        var native = await w.MailRead("dispatch-recovery.json") as JsonObject;
                        if (native != null && S(native["Command"]) == S(command["id"]) && S(native["State"]) != "watching")
                        {
                            record["state"] = native["State"]!.DeepClone();
                            record["native"] = native.DeepClone();
                            record["finished"] = w.Driver.UtcTicks;
                            DailyJson.Write(path, record);
                            Require(S(native["State"]) == "retry_requested", "Messenger repair aborted: " + S(native["Error"]));
                            acknowledged = true;
                            break;
                        }
                        await w.Delay(200);
                    }
                    Require(acknowledged, "Messenger repair completion unknown; no repeat");
                }
                catch (Exception error) { record["error"] = error.Message; record["finished"] = w.Driver.UtcTicks; DailyJson.Write(path, record); throw; }
                end = Math.Max(end, w.Time + 15);
            }
            await w.Delay(300);
        }
        throw new StageHostException("adapter", "派遣奖励仍未领取，但原生信使尚未出现；未将环节标为完成。");
    }
    private static async Task<JsonObject> ClaimCharacter(DailyWorkflow w)
    {
        await Page(w, Ui, true);
        var plan = Preflight(await w.Evidence(Prefixes));
        if (N(plan["count"]) == 0)
            return O(("state", "completed"), ("actions", 0), ("character", plan["character"]), ("reason", "all_dispatched"));
        if (plan["due"]!.AsArray().Count == 0 && !B(plan["enough"]))
            return O(("state", "partial"), ("actions", 0), ("character", plan["character"]), ("reason", "insufficient_catalyst"));
        var before = await w.Evidence(Prefixes);
        long started = w.Driver.UtcTicks;
        async Task Drive()
        {
            if (plan["due"]!.AsArray().Count == 0)
                return;
            double end = w.Time + 40;
            while (w.Time < end)
            {
                var after = await w.Evidence(Prefixes);
                var events = w.Driver.CollectEvents("dispatch.reward", started);
                try
                {
                    var reward = Response("dispatch.reward", events, before, after);
                    if (reward?["ItemInfo"] is JsonArray items && items.Count > 0 && ClaimIds(after).SequenceEqual(plan["due"]!.AsArray().Select(N).Order()))
                    {
                        if (await w.Has("RewardReceivePopupUI"))
                        {
                            await Page(w, "RewardReceivePopupUI");
                            await w.Step("RewardReceivePopupUI", back: true, absent: "RewardReceivePopupUI", reason: "领取已核账，让游戏继续自动派遣");
                        }
                        return;
                    }
                }
                catch (InvalidDataException) { }
                await w.Delay(150);
            }
            throw new StageHostException("pending", "派遣领取回执尚未完整，未重发。");
        }
        var op = await w.Transact("dispatch.claim_and_start", plan, O(("ui", Ui), ("field", "_autoDispatchExcuteBtn"), ("reason", "原生领取与自动续派")), 50, Drive);
        await Page(w, Ui, true);
        return O(("state", op["result"]!["state"]), ("actions", 1), ("character", plan["character"]), ("id", op["id"]), ("result", op["result"]));
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Tasks.Dispatch)
            return DailyWorkflow.Skipped("disabled");
        if (!await w.Has("GameFieldDefaultUI") && !await w.Has(Ui) && !await w.Has("QuickMenuUI"))
        {
            if (!await DailyTravel.Enter(w, 2, "square.pack", packType: 11))
                throw new StageHostException("adapter", "没有可领取派遣的普通地图。");
        }
        var initial = Global(await w.Evidence(Prefixes));
        Require(!B(initial["busy"]), "Native dispatch chain already active");
        var operations = new JsonArray();
        var results = new JsonArray();
        var visited = new HashSet<long>();
        var expected = new HashSet<long>();
        if (initial["due"]!.AsArray().Count > 0 || B(initial["totalwar"]) || B(initial["evilcastle"]))
        {
            if (await w.Has("MenuUI"))
                await w.Step("MenuUI", back: true, absent: "MenuUI");
            await Field(w);
            int target = await Messenger(w);
            var op = await w.Transact("dispatch.collect_all", initial, O(("ui", "GameFieldDefaultUI"), ("operation", "dispatch_collect_all"), ("value", target), ("reason", "一键领取全部派遣、会战及恶魔城收益")), 75);
            operations.Add(O(("id", op["id"]), ("result", op["result"])));
            await w.Dismiss("GameFieldDefaultUI");
        }
        Clear(await w.Evidence(Prefixes));
        if (await w.Has(Ui))
        {
            var r = await ClaimCharacter(w);
            results.Add(r);
            visited.Add(N(r["character"]));
            await Close(w, Ui);
        }
        for (int iteration = 0; iteration < 100; iteration++)
        {
            if (!await w.Has("QuickMenuUI"))
            {
                if (await w.Has("MenuUI"))
                    await w.Step("MenuUI", back: true, absent: "MenuUI");
                await Field(w);
                await w.Step(O(("ui", "GameFieldDefaultUI"), ("field", "_buttonFieldSkill"), ("dispatch_entry", true), ("expect", "QuickMenuUI"), ("reason", "核对全部已解锁派遣角色")));
            }
            await Page(w, "QuickMenuUI");
            var e = await w.Evidence(Prefixes);
            var rows = Readings(e, "dispatch.talent").ToArray();
            Require(rows.Length == N(R(e, "dispatch.quick", "TalentSkills.Count")) && rows.All(r => S(r[TalentKind]) == "Dispatch") && rows.Select(r => N(r[TalentCharacter])).Distinct().Count() == rows.Length, "Dispatch talent list incomplete");
            expected.UnionWith(rows.Select(r => N(r[TalentCharacter])));
            var row = rows.FirstOrDefault(r => !visited.Contains(N(r[TalentCharacter])));
            if (row == null)
                break;
            long id = N(row[TalentCharacter]);
            visited.Add(id);
            if (B(row["_objDisableImage.activeSelf"]))
            {
                results.Add(O(("state", "partial"), ("actions", 0), ("character", id), ("reason", "talent_unavailable")));
                continue;
            }
            string name = "/" + S(row["gameObject.name"]) + "/";
            var t = await w.Pointer("QuickMenuUI", t => S(t["Field"]).Contains(name, StringComparison.Ordinal) && S(t["Field"]).EndsWith("/Button - TalentSkill", StringComparison.Ordinal));
            await w.Step(O(("ui", "QuickMenuUI"), ("field", t["Field"]), ("target_id", t["Id"]), ("expect", Ui), ("reason", "核对该角色的全部派遣槽")));
            await w.WaitEvidence(Prefixes, x => N(R(x, "dispatch.ui", Character)) == id);
            results.Add(await ClaimCharacter(w));
            await Close(w, Ui);
            if (iteration == 99)
                throw new StageHostException("adapter", "派遣角色列表持续变化，保留已确认结果。");
        }
        foreach (string ui in new[] { Ui, "QuickMenuUI" })
            if (await w.Has(ui))
                await Close(w, ui);
        await Field(w);
        var final = await w.Evidence(Prefixes);
        var post = Clear(final);
        await w.Step("GameFieldDefaultUI", "_buttonMenu", expect: "MenuUI");
        var result = O(("state", expected.Count == 0 && results.Count == 0 ? "skipped" : expected.IsSubsetOf(visited) && Rows(results).All(r => S(r["state"]) == "completed") ? "completed" : "partial"), ("mode", "native_field_all"), ("engine", "dotnet-dispatch-v1"), ("global_operations", operations), ("characters", results), ("expected_characters", expected.Order().ToArray()), ("visited_characters", visited.Order().ToArray()), ("final_dispatches", Dispatches(final).Values.ToArray()), ("global_postcondition", post));
        w.Save("dispatch-latest.json", result);
        return result;
    }
}

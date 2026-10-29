using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyCollectionCatalog(JsonObject collection, JsonObject steal)
{
    public DailyCollectionCatalog(DailyWorkflow workflow) : this(workflow.Asset("collection-catalog.json"), workflow.Asset("steal-catalog.json")) { }
    public const int CoverageRevision = 3;
    private readonly Dictionary<long, JsonObject> cartridges = Rows(collection["cartridges"]).ToDictionary(r => N(r["id"]));
    private readonly Dictionary<long, JsonObject> steals = Rows(steal["packs"]).ToDictionary(r => N(r["id"]));
    public long[] SelectedMaps(long pack, WeeklyPreferences settings) => Maps(pack).Where(settings.IncludesCollectionMap).ToArray();
    public bool Supported(long pack) => pack is >= 1 and <= 19 || cartridges.ContainsKey(pack);
    public long[] Packs() => Enumerable.Range(1, 19).Select(id => (long)id)
        .Concat(cartridges.Where(p => (S(p.Value["kind"]) is "character" or "event") && p.Value["maps"] is JsonArray { Count: > 0 }).Select(p => p.Key).Order())
        .Distinct().ToArray();
    public long[] AvailablePacks(JsonObject evidence)
    {
        var owned = Rows(R(evidence, "mainline.owned_packs", "$items"));
        var definitions = Rows(R(evidence, "mainline.packs", "$items"));
        Require(owned.Length > 0 && owned.Length == N(R(evidence, "mainline.owned_packs", "Count")) &&
            definitions.Length > 0 && definitions.Length == N(R(evidence, "mainline.packs", "Count")), "collection_pack_list_incomplete");
        Require(owned.All(p => N(p["Key"]) > 0 && N(p["Key"]) == N(p["Value.Id"])), "collection_pack_list_incomplete");
        var ids = owned.Select(p => N(p["Value.Id"])).ToHashSet();
        var fields = definitions.Where(p => N(p["PackType"]) is 0 or 1 or 6).Select(p => N(p["Id"])).ToHashSet();
        var closed = UnavailableEventPacks(evidence);
        return Packs().Where(id => ids.Contains(id) && fields.Contains(id) && !closed.Contains(id)).ToArray();
    }
    public long[] UnavailableEventPacks(JsonObject evidence)
    {
        var scheduled = Rows(R(evidence, "mainline.packs", "$items"))
            .Where(p => N(p["PackType"]) == 6 && N(p["UseSchedule"]) == 1).Select(p => N(p["Id"]))
            .Intersect(Rows(R(evidence, "mainline.owned_packs", "$items")).Select(p => N(p["Value.Id"])))
            .Intersect(Packs()).ToArray();
        if (scheduled.Length == 0) return [];
        var access = Rows(R(evidence, "mainline.pack_access", "$self"));
        return scheduled.Where(id => !EventPackAvailable(access, id)).ToArray();
    }
    internal static bool EventPackAvailable(JsonObject[] access, long id)
    {
        var rows = access.Where(p => N(p["Id"]) == id).ToArray();
        Require(rows.Length == 1 && rows[0]["Available"] is JsonValue v && v.TryGetValue<bool>(out _), "活动卡带开放状态尚未读齐");
        return B(rows[0]["Available"]);
    }
    public HashSet<long> Transit(long pack) => cartridges[pack]["transitMaps"]!.AsArray().Select(N).ToHashSet();
    public long[] Maps(long pack) => pack switch { 6 => [601, 602, 606, 605, 608, 607], 11 => [1101, 1102, 1103], 14 => [141, 143, 142, 144], 15 => [151, 153, 154], 18 => [181, 186, 183], 1001 => [10011, 10014, 10015], 1002 => [10021, 10022, 10023], 1003 => [10031, 10032, 10033], _ => cartridges.TryGetValue(pack, out var c) ? c["maps"]!.AsArray().Select(N).ToArray() : pack is >= 1 and <= 19 ? new long[] { 1, 2, 3 }.Select(offset => (pack == 1 ? 0 : pack * 10) + offset).ToArray() : throw new InvalidDataException("Unsupported collection cartridge") };
    public long[] StealMaps(long pack)
    {
        Require(steals.TryGetValue(pack, out var p), "卡带尚无偷窃静态资料：" + pack);
        if (S(p!["status"]) is "empty" or "hidden")
            return [];
        Require(S(p["status"]) == "targets" && p["maps"] is JsonArray a && a.Count > 0, "Incomplete steal catalog");
        return p["maps"]!.AsArray().Select(N).ToArray();
    }
}
public sealed class DailyCollectionReadUnavailable(string message) : Exception(message);

public sealed class DailyCollectionProgress(DailyWorkflow workflow)
{
    private JsonObject? recentNetwork;
    private long recentInputRevision;
    private double recentAt = double.NegativeInfinity;
    private string recentScene = "";
    internal Func<Task>? PrepareRead { get; set; }
    internal bool OnlyCaptureRemains(JsonObject progress, long map)
    {
        if (progress["maps"]?[map.ToString()] is not JsonObject state || state["drops"] is not JsonArray { Count: 0 }
            || state["monsters"] is not JsonArray { Count: > 0 } remaining || state["story_locked"] is JsonArray { Count: > 0 }
            || recentNetwork == null) return false;
        var captures = Rows(recentNetwork["Targets"]).Where(t => N(t["Map"]) == map && N(t["Type"]) == 2).Select(t => N(t["Id"])).ToHashSet();
        return remaining.All(id => captures.Contains(N(id)));
    }
    public JsonObject[] PendingAbsorptions() => workflow.Business.MatchingRecords(workflow.Context,
        "dispatch.start", h => DailyManagedBusiness.Pending(h) && N(h["talent_kind"]) == 4)
        .Where(op => DailyFieldTalentProof.AbsorptionLocation(op) != null).ToArray();

    internal static (long Pack, long Map)? RecoveryLocation(JsonObject op) =>
        DailyFieldTalentProof.AbsorptionLocation(op) ?? DailyPickupRecovery.Location(op);
    internal JsonObject[] PendingMaps() => workflow.Business.MatchingRecords(workflow.Context,
        h => DailyManagedBusiness.Pending(h) && (S(h["role"]) == "mainline.pickup" || S(h["role"]) == "dispatch.start" && N(h["talent_kind"]) == 4))
        .Where(op => RecoveryLocation(op) != null).ToArray();

    private async Task RecoverAbsorptions(long pack, long[] maps, JsonObject network, JsonObject progress)
    {
        if (OnlySteal) return; // A theft-only projection deliberately omits drops.
        var pending = PendingMaps().Where(op => RecoveryLocation(op) is { } p
            && p.Pack == pack && maps.Contains(p.Map)).ToArray();
        if (pending.Length == 0) return;
        var current = await workflow.Evidence("mainline.map", "mainline.reset", "mainline.talent_counts");
        Require(N(DailyFieldRoute.Map(current)["packId"]) == pack
            && S(R(current, "mainline.reset", "GetWeeklyResetTime().Ticks")) == S(network["Week"]),
            "Collection recovery map or weekly cycle changed");
        var counts = DailyFieldRoute.Counts(current);
        Require(counts.All(c => N(c["groupId"]) > 0 && N(c["useCount"]) >= 0), "Current talent usage unavailable");
        foreach (var op in pending)
        {
            var p = RecoveryLocation(op)!.Value;
            op["state"] = "superseded"; // Old outcome stays unknown; fresh targets replace the old intent.
            op["reconciliation"] = O(("method", "fresh_map_server_progress"), ("at", workflow.Driver.UtcTicks),
                ("actions", 0), ("original_success_claimed", false), ("pack", pack), ("map", p.Map),
                ("query", network["Query"]), ("week", network["Week"]), ("remaining", progress["maps"]![p.Map.ToString()]),
                ("current_talent_counts", Array(counts)));
            op.Remove("error"); op.Remove("recovery_error");
            workflow.Business.Save(op);
            workflow.Driver.Diagnostics.Event("collection_map_recovered", O(("id", op["id"]), ("recovery", op["reconciliation"])));
        }
    }
    public bool Steal
    {
        get; set;
    }
    public bool OnlySteal
    {
        get; set;
    }
    public static JsonObject Classify(JsonObject data, long pack, long[] maps, bool steal = false, bool onlySteal = false)
    {
        Require(S(data["State"]) == "ready" && S(data["Error"]) == "" && N(data["Pack"]) == pack && data["Maps"]!.AsArray().Select(N).SequenceEqual(maps), "Collection response incomplete or for another cartridge");
        Require(N(data["Week"]) > 0 && N(data["Now"]) > 0, "Collection server clock missing");
        var result = new JsonObject(maps.Select(id => new KeyValuePair<string, JsonNode?>(id.ToString(), O(("drops", new JsonArray()), ("monsters", new JsonArray())))));
        var obtained = Rows(JsonNode.Parse(S(data["Rewards"])));
        var monsters = Rows(JsonNode.Parse(S(data["Monsters"])));
        var taken = obtained.Select(r => N(r["id"])).ToHashSet();
        var lookup = monsters.ToDictionary(r => N(r["monsterId"]));
        var seen = new HashSet<(long, long)>();
        foreach (var target in Rows(data["Drops"]))
        {
            long map = N(target["Map"]), id = N(target["Id"]);
            Require(result.ContainsKey(map.ToString()) && seen.Add((map, id)), "Invalid drop projection");
            if (!taken.Contains(id))
                result[map.ToString()]!["drops"]!.AsArray().Add(id);
        }
        seen.Clear();
        foreach (var target in Rows(data["Targets"]))
        {
            long map = N(target["Map"]), id = N(target["Id"]);
            Require(result.ContainsKey(map.ToString()) && seen.Add((map, id)), "Invalid monster projection");
            Require(lookup.TryGetValue(id, out var value) && N(value["groupId"]) == N(target["Group"]), "Server monster target missing");
            if (B(value!["activeFlag"]) && N(value["respawnTime"]) <= N(data["Now"]))
                result[map.ToString()]!["monsters"]!.AsArray().Add(id);
        }
        if (steal)
        {
            Require(B(data["StealReady"]), "NPC server cooldown not read");
            var records = Rows(JsonNode.Parse(S(data["Steals"])));
            var ends = records.ToDictionary(r => (N(r["npcId"]), N(r["groupId"])), r => N(r["endTime"]));
            foreach (var v in result)
                v.Value!["steals"] = new JsonArray();
            seen.Clear();
            foreach (var t in Rows(data["StealTargets"]))
            {
                long npc = N(t["Npc"]), group = N(t["Group"]), map = N(t["Map"]);
                Require(result.ContainsKey(map.ToString()) && npc > 0 && group > 0 && seen.Add((npc, group)), "Invalid steal projection");
                if (ends.GetValueOrDefault((npc, group)) <= N(data["Now"]))
                    result[map.ToString()]!["steals"]!.AsArray().Add(t.DeepClone());
            }
        }
        if (onlySteal)
        {
            Require(steal, "Steal-only requires server cooldown");
            foreach (var p in result)
            {
                p.Value!["drops"] = new JsonArray();
                p.Value["monsters"] = new JsonArray();
            }
        }
        var complete = new JsonObject(result.Where(p => p.Value!.AsObject().All(v => v.Value!.AsArray().Count == 0)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value!.DeepClone())));
        if (!onlySteal)
            foreach (var target in Rows(data["DeferredTargets"] ?? new JsonArray()))
            {
                string map = N(target["Map"]).ToString();
                Require(result.ContainsKey(map), "Deferred monster belongs to another map");
                result[map]!["story_locked"] ??= new JsonArray();
                result[map]!["story_locked"]!.AsArray().Add(Copy(target));
                complete.Remove(map);
            }
        return O(("source", "game_network"), ("weekly_reset", S(data["Week"])), ("maps", result), ("completed", complete));
    }
    private string PathFor(string week, long pack) => Path.Combine(workflow.Root, "live", "server-collection", S(workflow.Context["actor"]![3]), week, pack + ".json");
    public JsonObject? Cached(long pack, long[] maps, string week, bool missingAllowed = false)
    {
        Require(week.Length > 0 && week.All(char.IsDigit), "Invalid weekly scope");
        string path = PathFor(week, pack);
        if (!File.Exists(path))
        {
            if (missingAllowed)
                return null;
            throw new DailyCollectionSyncRequired("尚无本周服务器地图记录");
        }
        try
        {
            var stored = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Require(N(stored["schema"]) == 1 && S(stored["source"]) == "FieldObjectInfoResponse+MonsterInfoResponse" && JsonNode.DeepEquals(stored["account"], workflow.Context["actor"]![3]) && JsonNode.DeepEquals(stored["player"], workflow.Context["actor"]![4]) && S(stored["network"]!["Week"]) == week, "Collection cache identity/cycle mismatch");
            Require(stored["server"] == null || JsonNode.DeepEquals(stored["server"], workflow.Context["server"]), "Collection cache server mismatch");
            return DailyCollectionCache.Read(stored, pack, maps, Steal, OnlySteal, missingAllowed);
        }
        catch (Exception e) when (e is DailyCollectionSyncRequired or IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            workflow.Driver.Diagnostics.Event("collection_cache_refresh_required", O(("pack", pack), ("maps", maps), ("steal", Steal), ("only_steal", OnlySteal), ("reason", e.Message)));
            if (missingAllowed) return null; // Re-read this cartridge rather than require a manual global scan.
            throw new DailyCollectionSyncRequired("地图进度暂不可读，再次运行时会重新读取对应卡带：" + e.Message);
        }
    }
    public Task<JsonObject> Query(long pack, long[] maps) => Query(pack, maps, cache: true);
    public async Task<JsonObject> Query(long pack, long[] maps, bool cache)
    {
        // A query still uses the field's native input surface. Close only known
        // field overlays before dispatch; never send through a menu left by travel.
        if (PrepareRead != null) await PrepareRead();
        var observed = await workflow.Observe();
        var pending = PendingMaps();
        // Inner collection and outer route coordination often ask the identical
        // question consecutively. Reuse only this instance's fresh server reply,
        // with no intervening command, map change or unresolved collection. Never
        // substitute a disk checkpoint for an explicit recovery query.
        if (cache && recentNetwork != null && recentInputRevision == workflow.Driver.InputRevision
            && workflow.Time - recentAt is >= 0 and <= 2 && S(observed.Frame["Scene"]) == recentScene
            && N(recentNetwork["Pack"]) == pack && (!Steal || B(recentNetwork["StealReady"]))
            && recentNetwork["Maps"]!.AsArray().Select(N).SequenceEqual(maps)
            && !pending.Any(op => RecoveryLocation(op) is { } p && p.Pack == pack && maps.Contains(p.Map)))
            return Classify(recentNetwork, pack, maps, Steal, OnlySteal);
        // End stale movement before replacing its targets. Never replay its saved
        // Unity instance ID, including when an old walk crossed into another map.
        if (!OnlySteal && pending.Any(op => DailyPickupRecovery.Location(op) is { } p && p.Pack == pack && maps.Contains(p.Map)))
            await workflow.Step("GameFieldDefaultUI", operation: "mainline_cancel_nav");
        JsonObject? data = null;
        for (int readAttempt = 0; ; readAttempt++)
        {
            JsonObject command;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    command = await workflow.Step(O(("ui", "GameFieldDefaultUI"), ("operation", Steal ? "collection_query_steal" : "collection_query"), ("value", pack), ("items", maps), ("reason", "读取本卡带服务器收集进度")));
                    break;
                }
                catch (DailyStepException error) when (PrepareRead != null && error.PresentationChanged)
                {
                    // Only an explicitly unsent, read-only query may be reselected.
                    if (attempt >= 2) throw new DailyCollectionReadUnavailable("地图界面持续变化，本卡带暂缓读取，继续其他卡带。");
                    await PrepareRead();
                }
            }
            try
            {
                await workflow.WaitEvidence(["mainline.network"], e =>
                {
                    var f = e["Frame"]!.AsObject();
                    if (DailyNavigationDecision.Rows(f).Any(r => S(r["Type"]) == "MessagePopupUI" && S(r["NativeContext"]) == "request_timeout_ack_only"))
                        throw new StageHostException("adapter", "服务器收集进度未返回；未使用本地执行记录代替");
                    var n = State(e, "mainline.network", "$self");
                    if (S(n["Query"]) != S(command["id"])) return false;
                    if (S(n["State"]) == "failed") throw new StageHostException("adapter", S(n["Error"]));
                    if (S(n["State"]) != "ready") return false;
                    Classify(n, pack, maps, Steal, OnlySteal); data = n; return true;
                }, 35, "服务器收集进度未返回；未使用本地执行记录代替");
                break;
            }
            catch (StageHostException error) when (error.Kind == "adapter" && !error.HoldQueue)
            {
                var frame = (await workflow.Observe()).Frame;
                var action = DailyHomeDecision.BackgroundErrorAction(frame, DailyNavigationPolicy.Load());
                if (action != null && DailyNavigationDecision.Rows(frame).Any(r => S(r["Type"]) == "MessagePopupUI" && S(r["NativeContext"]) == "request_timeout_ack_only"))
                {
                    // The native handler verifies that OK has no retry/restart
                    // callback, and invalidates only this abandoned read. Never
                    // replay collection/absorption or treat a timeout as success.
                    await workflow.Step(action);
                    workflow.Driver.Diagnostics.Event("collection_query_timeout", O(("pack", pack), ("maps", maps), ("query", command["id"]), ("attempt", readAttempt + 1), ("retry", readAttempt < 2)));
                    if (readAttempt < 2)
                    {
                        if (PrepareRead != null) await PrepareRead();
                        continue;
                    }
                }
                throw new DailyCollectionReadUnavailable(error.Message);
            }
        }
        var result = Classify(data!, pack, maps, Steal, OnlySteal);
        await RecoverAbsorptions(pack, maps, data!, result);
        // `cache: false` forces a new server request; its confirmed response is
        // still useful after a restart. Merge it without losing unrelated maps.
        var snapshot = O(("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", workflow.Context["actor"]![3]), ("player", workflow.Context["actor"]![4]), ("server", workflow.Context["server"]), ("network", data), ("at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d));
        string path = PathFor(S(data!["Week"]), pack);
        lock (DailyCollectionCache.Gate)
        {
            var old = DailyJson.TryRead<JsonObject>(path);
            if (old != null && old["server"] == null) old["server"] = Copy(workflow.Context["server"]);
            try { snapshot = DailyCollectionCache.Merge(old, snapshot); }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
            {
                workflow.Driver.Diagnostics.Event("collection_cache_replaced_invalid", O(("pack", pack), ("reason", e.Message)));
                snapshot = DailyCollectionCache.Merge(null, snapshot);
            }
            DailyJson.Write(path, snapshot);
        }
        DailyJson.Write(Path.Combine(workflow.Root, "live", "mainline", S(workflow.Context["actor"]![3]), "network-" + pack + ".json"), O(("result", result), ("network", data), ("actor", workflow.Context["actor"]), ("at", workflow.Driver.UtcTicks)));
        recentNetwork = data!.DeepClone().AsObject();
        recentInputRevision = workflow.Driver.InputRevision;
        recentAt = workflow.Time;
        recentScene = S(observed.Frame["Scene"]);
        return result;
    }
}

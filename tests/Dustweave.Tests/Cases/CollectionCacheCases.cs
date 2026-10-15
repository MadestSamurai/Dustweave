using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class CollectionCacheCases
{
    public static void Run(string root, List<string> checks)
    {
        void Check(bool yes, string text) { if (!yes) throw new Exception(text); checks.Add(text); }
        JsonObject Snapshot(long[] maps, long now, bool complete = true, bool steal = true, double? written = null) => O(
            ("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", "account"), ("player", "player"), ("server", "server"), ("at", written ?? now),
            ("network", O(("State", "ready"), ("Error", ""), ("Pack", 1), ("Week", 500), ("Now", now), ("Maps", maps),
                ("Drops", Array(maps.Select(m => O(("Map", m), ("Id", m * 10))))),
                ("Rewards", complete ? Array(maps.Select(m => O(("id", m * 10)))).ToJsonString() : "[]"),
                ("Targets", new JsonArray()), ("Monsters", "[]"), ("StealReady", steal),
                ("StealTargets", steal ? Array(maps.Select(m => O(("Map", m), ("Npc", m), ("Group", 7)))) : new JsonArray()),
                ("Steals", steal ? Array(maps.Select(m => O(("npcId", m), ("groupId", 7), ("endTime", 400)))).ToJsonString() : "[]"))));
        var original = Snapshot([1, 2, 3], 100);
        string before = original.ToJsonString();
        var merged = DailyCollectionCache.Merge(original, Snapshot([2], 200, steal: false));
        Check(original.ToJsonString() == before, "collection merge never mutates source evidence");
        var read = DailyCollectionCache.Read(merged, 1, [1, 2, 3], true, false, true);
        Check(read["completed"]!.AsObject().Count == 3, "one-map collection read preserves other maps and prior theft cooldowns");
        var serialized = JsonNode.Parse(merged.ToJsonString())!.AsObject();
        Check(DailyCollectionCache.Read(serialized, 1, [3, 1], true, false, true)["completed"]!.AsObject().Count == 2,
            "merged map observations survive restart and requested map order changes");
        merged = DailyCollectionCache.Merge(merged, Snapshot([1, 2], 90, false, written: 9000));
        Check(DailyCollectionCache.Read(merged, 1, [1, 2, 3], true, false, true)["completed"]!.AsObject().Count == 3,
            "later export of an older unfinished observation cannot erase server-confirmed completion");
        merged = DailyCollectionCache.Merge(merged, Snapshot([2], 250, false, false));
        read = DailyCollectionCache.Read(merged, 1, [1, 2, 3], true, false, true);
        Check(read["completed"]!.AsObject().Count == 2 && read["maps"]!["2"]!["drops"]!.AsArray().Count == 1,
            "newer authoritative remaining targets update only their map without making completion artificially permanent");
        Check(DailyCollectionCache.Read(merged, 1, [2], true, true, true)["completed"]!.AsObject().Count == 1,
            "theft-only reading remains independent of unfinished collection");
        var noTheft = Snapshot([1, 2, 3], 100, steal: false);
        Check(DailyCollectionCache.Read(noTheft, 1, [1, 2, 3], false, false, true)["completed"]!.AsObject().Count == 3,
            "legacy collection-only records remain usable without theft observations");
        bool missing = false;
        try { DailyCollectionCache.Read(noTheft, 1, [1], true, true, true); } catch (DailyCollectionSyncRequired) { missing = true; }
        Check(missing, "missing theft data is never fabricated as completion");
        var locked = Snapshot([1, 2], 300);
        locked["network"]!["DeferredTargets"] = new JsonArray(O(("Map", 2), ("Id", 999)));
        Check(DailyCollectionCache.Read(locked, 1, [1], false, false, true)["completed"]!.AsObject().Count == 1,
            "story lock on another map does not invalidate an already completed map");
        bool refresh = false;
        try { DailyCollectionCache.Read(locked, 1, [2], false, false, true); } catch (DailyCollectionSyncRequired) { refresh = true; }
        Check(refresh, "story-locked map still requires fresh prerequisite observation");
        foreach (string key in new[] { "account", "player", "server", "week" })
        {
            var changed = Snapshot([2], 350, false, false);
            if (key == "week") changed["network"]!["Week"] = 501; else changed[key] = "other";
            var isolated = DailyCollectionCache.Merge(original, changed);
            Check(isolated["map_progress"]!.AsObject().Count == 1, "collection snapshots remain isolated by " + key);
        }
        var empty = Snapshot([], 360, false, false);
        Check(DailyCollectionCache.Read(DailyCollectionCache.Merge(merged, empty), 1, [1, 3], true, false, true)["completed"]!.AsObject().Count == 2,
            "an empty new observation cannot erase completed map observations");
        var unobserved = Snapshot([1], 370, false, false); unobserved["network"]!["State"] = "pending";
        bool rejected = false;
        try { DailyCollectionCache.Merge(original, unobserved); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && original.ToJsonString() == before, "unobserved pending map plan cannot overwrite confirmed server data");
    }
}

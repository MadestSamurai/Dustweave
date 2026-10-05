using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
static class WorkflowCases
{
    public static JsonObject Frame() => O(("ProcessId", 1), ("ProcessStartTicks", 2L), ("Instance", "i"), ("AccountKey", "a"), ("PlayerKey", "p"));
    public static JsonObject Reading(string id, params (string Key, object? Value)[] pairs) => O(("Id", id), ("Error", ""), ("Values", Array(pairs.Select(p => O(("Path", p.Key), ("Error", ""), ("Json", p.Value is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(p.Value)))))));
    public static JsonObject Evidence(params JsonObject[] readings) => O(("Config", "config"), ("Frame", Frame()), ("Readings", Array(readings)));
    public static JsonObject Event(string role, string kind, int seq, params (string Key, object? Value)[] values)
    {
        var row = Reading("unused", values);
        row.Remove("Id");
        row["Role"] = role;
        row["Kind"] = kind;
        row["Sequence"] = seq;
        row["Frame"] = Frame();
        row["Accepted"] = true;
        row["ErrorCode"] = 0;
        return row;
    }
    public static JsonObject Page() => O(("Ready", true), ("Kind", "EventExchangeUI"), ("TableId", 563), ("EventId", 28), ("Schedule", "{\"id\":1}"), ("Cache", "{}"), ("Claim", true), ("Free", false), ("AllowedCurrency", true), ("Cost", 1), ("Batch", 100), ("Balance", 210), ("Page", 2), ("Received", 15), ("Total", 195), ("Renew", true));
    public static JsonObject State(JsonObject page) => Evidence(Reading("rewards.native", ("$self", page)));
    public static void Run(List<string> cases)
    {
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);
            cases.Add(name);
        }
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try
            {
                action();
            }
            catch (Exception e) when (e is InvalidDataException or StageHostException or ArgumentException) { rejected = true; }
            Check(rejected, name);
        }
        JsonObject Change(JsonObject original, string key, object value)
        {
            var result = original.DeepClone().AsObject();
            result[key] = JsonSerializer.SerializeToNode(value);
            return result;
        }
        var p = Page();
        Check(DailyEventRewards.Action(p) == 4, "event exchange uses native maximum batch");
        Check(DailyEventRewards.Action(Change(p, "Received", 195)) == 5, "complete exchange page advances without partial exchange");
        foreach (var (key, value) in new (string, object)[] { ("Balance", 0), ("AllowedCurrency", false), ("Cost", 0), ("Batch", 0), ("Ready", false), ("Claim", false) })
            Check(DailyEventRewards.Action(Change(p, key, value)) == 0, "event spend blocked by " + key);
        var roulette = Change(p, "Kind", "MiniGameRouletteUI");
        roulette["Free"] = true;
        roulette["Cache"] = "{\"freeApCount\":1}";
        Check(DailyEventRewards.Action(roulette) == 2, "free roulette precedes token spending");
        roulette["Cache"] = "{}";
        roulette["Balance"] = 0;
        Check(DailyEventRewards.Action(roulette) == 0, "stale free roulette button cannot authorize spend");
        var catalog = O(("Catalog", new JsonArray("{\"id\":1,\"eventType\":7}", "{\"id\":2,\"eventType\":19}", "{\"id\":3,\"eventType\":4}", "{\"id\":4,\"eventType\":7,\"eventSubType\":1}", "{\"id\":5,\"eventType\":12}")));
        Check(DailyEventRewards.Select(catalog, new()).Select(r => N(r["id"])).SequenceEqual(new long[] { 3, 5, 2, 1 }), "event ordering excludes paid exchange variants");
        catalog["Catalog"]!.AsArray().Add("{\"id\":1,\"eventType\":7}");
        Reject(() => DailyEventRewards.Select(catalog, new()), "duplicate active event identity rejected");
        var before = State(p);
        var after = State(Change(p, "Balance", 110));
        var es = new JsonArray(Event("rewards.exchange", "request", 1), Event("rewards.exchange", "response", 2, ("ChangeExchangeRewardInfo", new JsonArray(O(("id", 1))))));
        Check(N(DailyEventRewards.Verify(before, es, after, 4)["spent"]) == 100, "exchange exact server debit accepted");
        Reject(() => DailyEventRewards.Verify(before, es, State(Change(p, "Balance", 111)), 4), "exchange off-by-one debit rejected");
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey" })
        {
            var bad = after.DeepClone().AsObject();
            bad["Frame"]![key] = key is "ProcessId" or "ProcessStartTicks" ? JsonValue.Create(999) : JsonValue.Create("changed");
            Reject(() => DailyEventRewards.Verify(before, es, bad, 4), "event scope rejects changed " + key);
        }
        Reject(() => DailyEventRewards.Verify(before, new JsonArray(es[0]!.DeepClone()), after, 4), "missing event response blocks completion");
        var badResponse = es.DeepClone().AsArray();
        badResponse[1]!["ErrorCode"] = 1;
        Reject(() => DailyEventRewards.Verify(before, badResponse, after, 4), "server rejected exchange does not complete");
        var reverse = es.DeepClone().AsArray();
        reverse[0]!["Sequence"] = 3;
        Reject(() => DailyEventRewards.Verify(before, reverse, after, 4), "response preceding request rejected");
        var cache = new Dictionary<(long Event, long Id), JsonObject> { { (28, 1), O(("IsComplete", true), ("Value", 1)) } };
        var balances = new Dictionary<string, long> { { "6:1", 100 } };
        var entry = O(("state", "completed"), ("kind", "EventMissionUI"), ("event_id", 28), ("cycle", "yesterday"), ("all_tabs_seen", true), ("missions", new JsonArray(O(("id", 1), ("required", 1), ("progress", 1), ("status", "claimed")))));
        Check(!DailyEventRewards.Needed(entry, "today", cache, balances), "finished one-time event remains skipped across reset only with server proof");
        cache.Clear();
        Check(DailyEventRewards.Needed(entry, "today", cache, balances), "absent server cache cannot prove one-time completion");
        var token = O(("state", "completed"), ("kind", "MiniGameRouletteUI"), ("cycle", "today"), ("currency_key", "6:1"), ("balance", 100), ("cost", 1));
        Check(DailyEventRewards.Needed(token, "today", cache, balances), "unchanged usable tokens must not be skipped");
        balances["6:1"] = 101;
        Check(DailyEventRewards.Needed(token, "today", cache, balances), "new token balance triggers event revisit");
        var dice = Change(p, "Kind", "MiniGameDiceUI");
        dice["Balance"] = 2;
        dice["Batch"] = 0;
        dice["Auto"] = false;
        Check(DailyEventRewards.Action(dice) == 6, "dice single token budget independent of batch count");
        var diceAfter = Change(dice, "Balance", 0);
        var throws = new JsonArray();
        for (int i = 0; i < 2; i++)
        {
            throws.Add(Event("rewards.dice", "request", i * 2 + 1));
            throws.Add(Event("rewards.dice", "response", i * 2 + 2, ("MiniGameBoardInfo", O(("eventScheduleId", 1))), ("MiniGameControllerInfo", new JsonArray(O(("value", 3))))));
        }
        Check(N(DailyMiniGames.VerifyDice(State(dice), throws, State(diceAfter))["spent"]) == 2, "dice validates complete native response chain and exact debit");
        Reject(() => DailyMiniGames.VerifyDice(State(dice), throws, State(Change(diceAfter, "Auto", true))), "dice active auto state never reported complete");
        Reject(() => DailyMiniGames.VerifyDice(State(dice), throws, State(Change(diceAfter, "Balance", 1))), "dice missing debit rejected");
        var guard = new DailyMiniGames.DiceGuard();
        guard.Observe(dice, new(), 0);
        Reject(() => guard.Observe(dice, new(), 11), "idle dice stops without another throw after missing receipt");
        guard = new();
        dice["Auto"] = true;
        for (int i = 0; i < 30; i++)
            guard.Observe(Change(dice, "Balance", 30 - i), new(), i * 30);
        Check(true, "legitimate dice progress can run beyond a fixed 45-second duration");
        Reject(() => guard.Observe(Change(dice, "Balance", 1), new(), 930), "stalled active dice auto loses renewal");
        foreach (int multiplier in new[] { 1, 7, 15, 40 })
        {
            int remaining = 40, sum = 0, batches = 0;
            while (remaining > 0)
            {
                int use = DailyMirror.Allocation(multiplier, remaining);
                sum += use;
                remaining -= use;
                batches++;
                Check(use > 0 && use <= multiplier, "mirror positive free-only batch " + multiplier + ":" + batches);
            }
            Check(sum == 40 && remaining == 0, "mirror spends all free tickets at multiplier " + multiplier);
        }
        DailyMirror.Debit(O(("free", 40), ("paid", 7)), O(("free", 25), ("paid", 7)), 15);
        Check(true, "mirror preserves paid ticket balance");
        Reject(() => DailyMirror.Debit(O(("free", 40), ("paid", 7)), O(("free", 25), ("paid", 6)), 15), "mirror rejects paid ticket consumption");
        Reject(() => DailyMirror.Allocation(41, 40), "mirror invalid multiplier rejected");
        var monster = O(("highest_level", 28), ("selected_level", 1), ("daily_damage", 0), ("level_record", 123), ("practice", false), ("quick_enabled", true));
        Check(S(DailyMonsterHunt.Plan(monster)["state"]) == "select_highest", "monster quick battle selects highest recorded level");
        monster["selected_level"] = 28;
        Check(S(DailyMonsterHunt.Plan(monster)["state"]) == "ready", "highest monster quick battle can proceed");
        monster["daily_damage"] = 123;
        Check(S(DailyMonsterHunt.Plan(monster)["state"]) == "skipped", "already applied monster daily record skips");
        monster["practice"] = true;
        Check(S(DailyMonsterHunt.Plan(monster)["state"]) == "blocked", "practice cannot satisfy daily reward");
        var levels = O(("Ready", true), ("Max", 6), ("Open", new[] { 1, 2, 3, 4 }), ("Cleared", new[] { 1, 3 }));
        Check(DailyWeeklySichuan.Choose(levels) == 2, "sichuan chooses first open uncleared regular level");
        levels["Max"] = 3;
        levels["Open"] = new JsonArray(1, 2, 3);
        levels["Cleared"] = new JsonArray(1, 2, 3);
        Check(DailyWeeklySichuan.Choose(levels) == 1, "all regular levels complete falls back to level one");
        levels["Open"] = new JsonArray(1, 2);
        levels["Cleared"] = new JsonArray(1, 2);
        Reject(() => DailyWeeklySichuan.Choose(levels), "locked unfinished level is not all-clear");
        var cmd = DailyWeeklyFishing.Command("owned", 7, 1_000_000_000);
        Check(DailyWeeklyFishing.Active(cmd, 1_000_000_000) && B(cmd["AutoApproach"]) && !B(cmd["AutoSell"]) && !B(cmd["AutoBait"]), "weekly fishing borrows navigation without selling fish or consuming bait");
        Check(!DailyWeeklyFishing.Active(cmd, 1_040_000_000), "fishing ownership expires after queue loss");
        var fish = O(("ProcessId", 7), ("CapturedUtcTicks", 1_000_000_000));
        Check(DailyWeeklyFishing.Fresh(fish, 7, 1_020_000_000), "fishing accepts fresh same-process heartbeat");
        Check(!DailyWeeklyFishing.Fresh(fish, 8, 1_020_000_000) && !DailyWeeklyFishing.Fresh(fish, 7, 1_040_000_000), "fishing rejects foreign process and stale heartbeat");
        var ranked = DailyWeeklyRooms.Rank([O(("ownerIndex", 10), ("myRoomLikeCount", 5)), O(("ownerIndex", 4), ("myRoomLikeCount", 1)), O(("ownerIndex", 2), ("myRoomLikeCount", 1))]);
        Check(ranked.Select(r => N(r["ownerIndex"])).SequenceEqual(new long[] { 2, 4, 10 }), "weekly likes prioritize lowest popularity with stable tie order");
        var missions = new[] { O(("id", 208), ("conditionType", 6), ("conditionSubType", 9), ("groupType", 1), ("conditionValue", 1), ("unlockQuestId", 0), ("unlockPackId", 0)) };
        var missionState = Evidence(Reading("missions.cache", ("Count", 1), ("_items", new JsonArray(O(("id", 208), ("value", 1), ("isComplete", false))))));
        Check(B(DailyWeeklyMission.Progress(missionState, 208, missions)["complete"]), "achieved unclaimed weekly craft is not repeated");
        missions[0]["conditionType"] = 99;
        Reject(() => DailyWeeklyMission.Progress(missionState, 208, missions), "weekly table definition change blocks guessed completion");
        Check(DailyHunting.CountStep(1, 10) == ("_objPlus10Button", 10), "native hunting count one-to-ten transition handled");
        Check(DailyHunting.CountStep(10, 15) == ("_objPlusButton", 11), "hunting does not overshoot remaining budget");
        var gear = O(("InvenIndex", "101"), ("LockFlag", false), ("KeepFlag", false), ("UseChar", 0));
        var ge = Evidence(Reading("policy.equipment", ("Count", 1), ("$items", new JsonArray(gear))));
        Check(DailyEquipment.Recyclable(ge, ["101"]).Count == 1, "recycle accepts exact unprotected instance");
        foreach (string key in new[] { "LockFlag", "KeepFlag", "UseChar" })
        {
            var g = gear.DeepClone().AsObject();
            g[key] = key == "UseChar" ? JsonValue.Create(1) : JsonValue.Create(true);
            Reject(() => DailyEquipment.Recyclable(Evidence(Reading("policy.equipment", ("Count", 1), ("$items", new JsonArray(g)))), ["101"]), "recycle rejects protected " + key);
        }
        Reject(() => DailyEquipment.Recyclable(ge, ["101", "101"]), "recycle duplicate instance rejected");
    }
}

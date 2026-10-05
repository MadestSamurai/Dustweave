using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class EventTradeBoundaryCases
{
    public static JsonObject Lobby() => O(("Category", "Battle"), ("Eligible", true), ("Event", 100), ("Group", 10), ("Latest", 2), ("FreeAp", 5),
        ("Stages", new JsonArray(Stage(1, true), Stage(2, false))));
    public static JsonObject Stage(int id, bool cleared) => O(("Id", id),
        ("Table", O(("id", id), ("groupId", 10), ("battleDeckId", 1000 + id), ("quickBattlePossible", 1), ("eventApCount", 1)).ToJsonString()),
        ("Deck", O(("id", 1000 + id), ("bonusRewardId", new JsonArray(1))).ToJsonString()),
        ("Progress", cleared ? O(("eventUid", 100), ("groupId", 10), ("id", id), ("battleChallengeIndex", new JsonArray(0))).ToJsonString() : "null"));
    public static void Run(List<string> checks, string? captured = null)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        void Reject(Action run, string name) { try { run(); } catch (InvalidDataException) { checks.Add(name); return; } throw new Exception(name); }
        var lobby = Lobby();
        var stage = Rows(lobby["Stages"])[1];
        foreach (JsonNode? value in new JsonNode?[] { null, JsonValue.Create("null"), new JsonObject(), JsonValue.Create("{}") })
        {
            stage["Progress"] = Copy(value);
            Check(DailyEventStageData.Progress(lobby, stage) == null, "Uncleared progress accepted: " + (value?.ToJsonString() ?? "null"));
            Check(N(DailyEventSweep.Highest(lobby)!["stage"]) == 1, "Sweep skips uncleared frontier");
        }
        stage.Remove("Progress");
        Reject(() => DailyEventSweep.Highest(lobby), "Missing collection field fails explicitly");
        foreach (var value in new JsonNode[] { new JsonArray(), JsonValue.Create("[]")!, JsonValue.Create("{broken")!, JsonValue.Create(1)! })
        {
            stage["Progress"] = Copy(value);
            Reject(() => DailyEventStageData.Progress(lobby, stage), "Malformed progress rejected: " + value.ToJsonString());
        }
        foreach (string field in new[] { "eventUid", "groupId", "id" })
        {
            var p = O(("eventUid", 100), ("groupId", 10), ("id", 2));
            p[field] = 999;
            stage["Progress"] = p;
            Reject(() => DailyEventStageData.Progress(lobby, stage), "Foreign progress rejected: " + field);
        }
        stage["Progress"] = null;
        foreach (string field in new[] { "Table", "Deck" })
        {
            var clone = stage.DeepClone().AsObject();
            clone[field] = "null";
            Reject(() => DailyEventStageData.Tables(lobby, clone), "Required static data remains strict: " + field);
        }
        var challenge = Lobby(); challenge["Category"] = "Challenge";
        challenge["Stages"] = new JsonArray(Stage(15, false));
        Check(DailyEventSweep.Challenge15(challenge) == null, "Uncleared challenge 15 never swept");

        JsonObject Op(string role, JsonNode? scope, string state = "completed") => O(("id", Guid.NewGuid().ToString("N")), ("role", role), ("state", state), ("scope", scope), ("account", "account"));
        var valid = Op("trade.buy", O(("trade_session", "session"), ("action", "favorites"))); valid["at"] = 2;
        var bargain = Op("dispatch.start", O(("trade_session", "session"), ("action", "bargain"), ("_proof", O(("kind", "bargain"))))); bargain["at"] = 1;
        var records = new List<JsonObject> { valid, bargain };
        foreach (string role in new[] { "cafeteria.regular_all", "cafeteria.event", "dispatch.claim" })
            foreach (JsonNode? scope in new JsonNode?[] { null, JsonValue.Create("regular"), new JsonArray(), O(("trade_session", "session")) })
                records.Add(Op(role, scope));
        records.Add(Op("dispatch.start", JsonValue.Create("legacy")));
        records.Add(Op("trade.sell", O(("trade_session", "different"), ("action", "sale:0"))));
        records.Add(Op("trade.buy", O(("trade_session", "session"), ("action", "partial:0")), "preview_ready"));
        if (captured != null) records.Add(DailyTradeCatalog.Read(captured));
        Check(records.Where(DailyTradeJournal.IsTrade).Count() == 4, "Role-first isolation excludes scalar restaurant journals and unrelated same-session scopes");
        Check(!DailyTradeJournal.IsTrade(Op("dispatch.start", JsonValue.Create("legacy"))), "Scalar legacy talent scope is not a trade");
        Check(DailyTradeJournal.IsTrade(Op("trade.buy", null)), "Unknown trade retained for diagnostic classification");
    }
}
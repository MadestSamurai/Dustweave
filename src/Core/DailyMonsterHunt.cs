using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyMonsterHunt
{
    private const string Selected = "ὢὧὮὣὤὬὯὭὢὯὡ", Practice = "ὠὡὤὯὮὭὡὦὡὡὤ", Matched = "ὤὢὯὤὦὥὥὤὥὧὤ", Schedule = "ὥὭὮὬὮὫὭὯὠὠὦ", Period = "ὣὪὧὨὤὡὭὫὡὮὫ";
    private static readonly string[] Fields = ["Season", "MonsterHuntId", "Level", "CurrentLevelHighestDamage", "DailyHighestDamage", "DailyRewardLevel", "DailyRewardDate"];
    public static DailyBusinessProof Proof() => new("monster.quick", "monster_hunt", ["monster"], (op, events, after) => Verify(op["before"]!.AsObject(), events, after));
    public static JsonObject Cache(JsonObject e)
    {
        var o = new JsonObject();
        foreach (string key in Fields)
            o[key] = R(e, "monster.cache", key)?.DeepClone();
        return o;
    }
    public static JsonObject Observed(JsonObject e)
    {
        var c = Cache(e);
        Require(B(R(e, "monster.ui", Matched)) && N(c["Season"]) == N(R(e, "monster.ui", Schedule + ".SeasonInfo.Season")) && N(c["MonsterHuntId"]) == N(R(e, "monster.ui", Schedule + ".MonsterHuntId")), "Monster Hunt cache belongs to another season");
        var quick=Rows(e["Frame"]?["Surfaces"]).Where(s=>S(s["Type"])=="MonsterHuntUI").SelectMany(s=>Rows(s["Targets"]))
            .Where(t=>S(t["Field"])=="_buttonQuickBattle._button").ToArray();
        return O(("highest_level", c["Level"]), ("selected_level", R(e, "monster.ui", Selected)), ("daily_damage", c["DailyHighestDamage"]), ("level_record", c["CurrentLevelHighestDamage"]), ("practice", R(e, "monster.ui", Practice)), ("quick_available", quick.Length>0), ("quick_enabled", quick.Any(t=>B(t["Enabled"]))), ("season", c["Season"]), ("monster", c["MonsterHuntId"]));
    }
    public static JsonObject Plan(JsonObject s)
    {
        foreach (string key in new[] { "highest_level", "selected_level", "daily_damage", "level_record", "practice" })
            if (s[key] == null)
                return O(("state", "blocked"), ("reason", "native_monster_state_missing"));
        if (N(s["highest_level"]) < 1 || N(s["selected_level"]) < 0 || N(s["daily_damage"]) < 0 || N(s["level_record"]) < 0)
            return O(("state", "blocked"), ("reason", "native_monster_state_invalid"));
        if (B(s["practice"]))
            return O(("state", "blocked"), ("reason", "practice_is_not_daily_reward"));
        if (N(s["level_record"]) <= 0)
            return O(("state", "blocked"), ("reason", "no_native_quick_battle_record"));
        if (N(s["daily_damage"]) >= N(s["level_record"]))
            return DailyWorkflow.Skipped("daily_record_already_applied");
        if (s["quick_available"] != null && !B(s["quick_available"]))
            return O(("state", "blocked"), ("reason", "native_quick_entry_removed"));
        if (N(s["selected_level"]) != N(s["highest_level"]))
            return O(("state", "select_highest"), ("level", s["highest_level"]));
        return O(("state", B(s["quick_enabled"]) ? "ready" : "blocked"), ("reason", B(s["quick_enabled"]) ? "" : "native_quick_button_unavailable"), ("level", s["highest_level"]));
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after)
    {
        var r = Response("monster.quick", events, before, after)!;
        var old = Cache(before);
        var current = Cache(after);
        var returned = r["MonsterHuntUserInfo"] as JsonObject ?? throw new InvalidDataException("Missing Monster Hunt settlement");
        foreach (string k in Fields)
            Require(N(returned[char.ToLowerInvariant(k[0]) + k[1..]]) == N(current[k]), "Monster Hunt response/cache differs: " + k);
        foreach (string k in new[] { "Season", "MonsterHuntId" })
            Require(N(old[k]) == N(current[k]), "Monster Hunt season changed");
        Require(N(current["Level"]) >= N(old["Level"]) && (N(current["Level"]) != N(old["Level"]) || N(current["DailyHighestDamage"]) >= N(old["CurrentLevelHighestDamage"])), "Highest record was not applied");
        Require(!JsonNode.DeepEquals(old, current), "Quick battle produced no progress");
        return O(("level", old["Level"]), ("after_level", current["Level"]), ("daily_damage", current["DailyHighestDamage"]), ("cache_matched", true), ("has_rewards", r["MonsterHuntRewardBundle"] != null || r["MonsterHuntDailyRewardBundle"] != null));
    }
    // Compatibility only: historical journals may still need their original proof.
    public static Task<JsonObject> Run(DailyWorkflow w) => Task.FromResult(DailyWorkflow.Skipped("feature_removed"));
}

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
        return O(("highest_level", c["Level"]), ("selected_level", R(e, "monster.ui", Selected)), ("daily_damage", c["DailyHighestDamage"]), ("level_record", c["CurrentLevelHighestDamage"]), ("practice", R(e, "monster.ui", Practice)), ("quick_enabled", R(e, "monster.ui", "_buttonQuickBattle._goEnable.activeSelf")), ("season", c["Season"]), ("monster", c["MonsterHuntId"]));
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
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.MonsterHunt.Enabled)
            return DailyWorkflow.Skipped("disabled");
        var e = await w.Evidence("monster");
        var seasons = Rows(R(e, "monster.schedules", "$self"));
        var results = new JsonArray();
        foreach (var season in seasons)
        {
            if (!B(season["Available"]) || S(season["Period"]) != "PlayPeriod")
            {
                results.Add(DailyWorkflow.Skipped("outside_monster_hunt_play_period"));
                continue;
            }
            await w.Home("monster_hunt");
            await w.Step("MenuUI", operation: "monster_open", value: B(season["Returned"]) ? 1 : 0, expect: "MonsterHuntUI", reason: B(season["Returned"]) ? "打开复刻魔兽" : "打开当期魔兽");
            await DailyTravel.Ready(w, "MonsterHuntUI");
            e = await w.WaitEvidence(["monster"], x => B(R(x, "monster.ui", Matched)) && N(R(x, "monster.cache", "Season")) == N(season["Season"]));
            var initial = Observed(e);
            Require(N(initial["season"]) == N(season["Season"]) && N(initial["monster"]) == N(season["Monster"]), "Monster schedule changed");
            JsonObject result;
            if (S(R(e, "monster.ui", Period)) != "PlayPeriod")
                result = DailyWorkflow.Skipped("outside_monster_hunt_play_period");
            else
            {
                int budget = checked((int)Math.Abs(N(initial["highest_level"]) - N(initial["selected_level"])));
                Require(budget <= 100, "Unexpected Monster Hunt level range");
                var state = initial;
                var decision = Plan(state);
                for (int i = 0; i <= budget && S(decision["state"]) == "select_highest"; i++)
                {
                    Require(new[] { "season", "monster", "highest_level" }.All(k => N(state[k]) == N(initial[k])), "Highest record changed during selection");
                    int direction = N(state["selected_level"]) < N(state["highest_level"]) ? 1 : -1;
                    long target = N(state["selected_level"]) + direction;
                    await w.Step("MonsterHuntUI", direction > 0 ? "_buttonNextLevel._button" : "_buttonPrevLevel._button", reason: "选择最高可快速战斗等级");
                    e = await w.WaitEvidence(["monster"], x => N(R(x, "monster.ui", Selected)) == target);
                    state = Observed(e);
                    decision = Plan(state);
                }
                if (S(decision["state"]) != "ready")
                {
                    result = decision;
                    if (S(result["state"]) is "blocked" or "select_highest")
                        result["state"] = "partial";
                }
                else
                {
                    await w.Step("MonsterHuntUI", "_buttonQuickBattle._button", expect: "MonsterHuntQuickPopupUI");
                    await DailyTravel.Ready(w, "MonsterHuntQuickPopupUI");
                    Require(JsonNode.DeepEquals(state, Observed(await w.Evidence("monster"))), "Monster eligibility changed before confirmation");
                    var op = await w.Transact("monster.quick", O(("level", state["highest_level"]), ("season", state["season"]), ("monster", state["monster"]), ("returned", season["Returned"])), O(("ui", "MonsterHuntQuickPopupUI"), ("field", "_buttonBattle"), ("reason", "使用最高原生记录完成魔兽快速战斗")), 45);
                    await w.Dismiss("MonsterHuntUI", B(op["result"]?["has_rewards"]));
                    result = DailyWorkflow.Completed(op["result"]);
                    result["operation"] = op["id"]!.DeepClone();
                }
            }
            result["returned"] = season["Returned"]!.DeepClone();
            result["season"] = season["Season"]!.DeepClone();
            results.Add(result);
            await w.Step("MonsterHuntUI", back: true, absent: "MonsterHuntUI");
            await w.Home("monster_hunt");
        }
        return O(("state", Rows(results).Any(r => S(r["state"]) == "partial") ? "partial" : Rows(results).Any(r => S(r["state"]) == "completed") ? "completed" : "skipped"), ("seasons", results), ("engine", "dotnet-monster-v1"));
    }
}

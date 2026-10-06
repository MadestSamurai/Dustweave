using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Resolve business goals against the current game's definitions, never a menu visit or a local success flag.</summary>
public static class DailyWeeklyMission
{
    public const string Equipment = "weekly_equipment", Book = "weekly_book", Likes = "weekly_room_likes", Fishing = "weekly_fishing", MiniGame = "weekly_sichuan";
    private static (int Type, int Subtype, int Required) Condition(string goal) => goal switch
    {
        Equipment => (6, 9, 1), Book => (280, 0, 1), Likes => (36, 0, 3), Fishing => (341, 0, 1), MiniGame => (343, 0, 1),
        _ => throw new ArgumentException("Unknown weekly goal: " + goal)
    };
    public static JsonObject Definition(string goal, JsonObject[] tables)
    {
        var c = Condition(goal);
        var matches = tables.Where(r => N(r["conditionType"]) == c.Type && N(r["conditionSubType"]) == c.Subtype && N(r["groupType"]) == 1 && N(r["conditionValue"]) == c.Required && N(r["unlockQuestId"]) == 0 && N(r["unlockPackId"]) == 0).ToArray();
        Require(matches.Length == 1 && N(matches[0]["id"]) > 0, "本周任务定义缺失或不唯一，未判定完成：" + goal);
        return matches[0];
    }
    public static JsonObject Progress(JsonObject evidence, string goal, JsonObject[] tables)
    {
        var table = Definition(goal, tables);
        long id = N(table["id"]), required = N(table["conditionValue"]);
        var rows = Cached(evidence, "missions.cache");
        Require(rows.Select(r => N(r["id"])).Distinct().Count() == rows.Length, "Duplicate weekly cache");
        var row = rows.SingleOrDefault(r => N(r["id"]) == id);
        long value = N(row?["value"]);
        Require(value >= 0, "Invalid weekly progress");
        bool claimed = B(row?["isComplete"]);
        return O(("id", id), ("goal", goal), ("condition_type", table["conditionType"]), ("condition_subtype", table["conditionSubType"]), ("progress", value), ("required", required), ("claimed", claimed), ("complete", claimed || value >= required), ("observed_at", evidence["AtUtcTicks"]), ("source", "game_mission_cache"));
    }
    // Persisted crafting operations carry an id. Verify its meaning against the same current definitions.
    public static JsonObject Progress(JsonObject evidence, long id, JsonObject[] tables)
    {
        var row = tables.SingleOrDefault(r => N(r["id"]) == id);
        Require(row != null, "Weekly mission definition missing: " + id);
        var goals = new[] { Equipment, Book, Likes, Fishing, MiniGame }.Where(g => { var c = Condition(g); return N(row!["conditionType"]) == c.Type && N(row["conditionSubType"]) == c.Subtype; }).ToArray();
        Require(goals.Length == 1, "Unknown weekly mission condition: " + id);
        string goal = goals[0];
        var definition = Definition(goal, tables);
        Require(N(definition["id"]) == id, "Weekly mission identity differs: " + id);
        return Progress(evidence, goal, tables);
    }
    public static async Task<JsonObject> Read(DailyWorkflow w, string goal) => Progress(await w.Evidence("missions.cache"), goal, w.Table("policy/MissionTable.json"));
    public static async Task<JsonObject> Confirm(DailyWorkflow w, string goal)
    {
        var tables = w.Table("policy/MissionTable.json");
        var e = await w.WaitEvidence(["missions.cache"], e => B(Progress(e, goal, tables)["complete"]), 15, "操作已执行，服务器周任务尚未确认；不会重复执行。");
        return Progress(e, goal, tables);
    }
    public static JsonObject Skipped(string reason, JsonObject proof)
    {
        Require(B(proof["complete"]), "Cannot skip an unfinished weekly mission");
        var result = DailyWorkflow.Skipped(reason);
        result["mission"] = proof.DeepClone();
        return result;
    }
    // Old builds checked mission 226 (opening the arcade menu), not playing a game.
    // Correct only that invalid conclusion; retain the original result for audit and re-read the game on retry.
    public static bool NeedsRecheck(JsonObject item)
    {
        if (S(item["task"]) != MiniGame || S(item["state"]) is not ("completed" or "skipped") || S(item["result"]?["reason"]) is "disabled" or "not_selected") return false;
        var proof = item["result"]?["mission"];
        return proof == null || !B(proof["complete"]) || !(S(proof["goal"]) == MiniGame && N(proof["condition_type"]) == 343 && N(proof["condition_subtype"]) == 0 && N(proof["required"]) == 1);
    }
    public static void RecheckLegacy(JsonObject record, bool resume = false)
    {
        foreach (var item in Rows(record["items"]).Where(NeedsRecheck))
        {
            item["state"] = resume ? "pending" : "partial";
            item["error"] = "旧版小游戏完成依据无效，可勾选补跑并重新读取游戏进度。";
            item.Remove("carried_forward");
        }
    }
}
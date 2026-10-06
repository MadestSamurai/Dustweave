using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Static tables are required; absent server progress means a stage has not been cleared.</summary>
public static class DailyEventStageData
{
    private static JsonObject? Object(JsonObject row, string field, bool nullable)
    {
        string label = $"活动关卡 {S(row["Id"])} 的 {field}";
        Require(row.ContainsKey(field), label + " 未采集");
        JsonNode? node = row[field];
        if (node is JsonValue value && value.TryGetValue<string>(out var json))
        {
            try { node = JsonNode.Parse(json); }
            catch (JsonException e) { throw new InvalidDataException(label + " 不是有效 JSON", e); }
        }
        if (node == null && nullable) return null;
        return node as JsonObject ?? throw new InvalidDataException(label + " 应为对象" + (nullable ? "或未通关空记录" : "，数据表缺失"));
    }
    public static JsonObject Required(JsonObject row, string field) => Object(row, field, false)!;
    public static JsonObject? Progress(JsonObject lobby, JsonObject row)
    {
        var progress = Object(row, "Progress", true);
        if (progress == null || progress.Count == 0) return null;
        Require(N(progress["eventUid"]) == N(lobby["Event"]) && N(progress["groupId"]) == N(lobby["Group"]) && N(progress["id"]) == N(row["Id"]),
            "活动通关记录不属于当前活动／分组／关卡");
        return progress;
    }
    public static (JsonObject Table, JsonObject Deck) Tables(JsonObject lobby, JsonObject row)
    {
        var table = Required(row, "Table");
        var deck = Required(row, "Deck");
        Require(N(table["id"]) == N(row["Id"]) && N(table["groupId"]) == N(lobby["Group"]) &&
            N(table["battleDeckId"]) > 0 && N(table["battleDeckId"]) == N(deck["id"]), "活动关卡与战斗表关联不一致");
        return (table, deck);
    }
}

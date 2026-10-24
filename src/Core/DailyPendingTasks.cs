using System.Text.Json;
namespace Dustweave;

public sealed record QueueTaskDetail(string Group, string Title, long? Progress, long? Required,
    string Status, string Reason = "", string Source = "");

// Presentation projection only. Reading a saved result must never dispatch game actions.
public static class DailyPendingTasks
{
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long? Number(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number) && number >= 0 ? number : null;
    private static HashSet<(long Table, long Event)>? ActiveEvents(JsonElement result)
    {
        if (!result.TryGetProperty("catalog", out var catalog) || catalog.ValueKind != JsonValueKind.Array) return null;
        var active = new HashSet<(long, long)>();
        foreach (var item in catalog.EnumerateArray())
        {
            try
            {
                using var parsed = item.ValueKind == JsonValueKind.String ? JsonDocument.Parse(item.GetString()!) : null;
                var row = parsed?.RootElement ?? item;
                if (Number(row, "id") is not long id || Number(row, "eventId") is not long eventId) return null;
                active.Add((id, eventId));
            }
            catch (JsonException) { return null; }
        }
        return active;
    }
    public static IReadOnlyList<QueueTaskDetail> Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("pending_tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array) return [];
        var output = new List<QueueTaskDetail>();
        var activeEvents = ActiveEvents(result);
        foreach (var row in tasks.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string state = Text(row, "status"), key = Text(row, "key"), group = Text(row, "group");
            if (state is "claimed" or "completed") continue;
            if (group.Length == 0) group = Number(row, "pass_id").HasValue || key.StartsWith("pass:", StringComparison.Ordinal) ? "pass"
                : key.StartsWith("MG_WEEKLY:", StringComparison.Ordinal) ? "weekly"
                : key.StartsWith("MG_DAILY:", StringComparison.Ordinal) ? "daily"
                : Number(row, "event_id").HasValue ? "event" : "other";
            if (group is not ("daily" or "weekly" or "pass" or "event")) group = "other";
            // Repair presentation of older reports using their own observed event
            // catalogue. Never rewrite the journal or infer completion from names.
            if (group == "event" && activeEvents != null && key.Split(':') is ["event", var table, _]
                && long.TryParse(table, out long tableId) && Number(row, "event_id") is long eventId
                && !activeEvents.Contains((tableId, eventId))) continue;
            string reason = Text(row, "detail");
            if (string.IsNullOrWhiteSpace(reason)) reason = Text(row, "reason");
            string source = Text(row, group == "pass" ? "pass_title" : "event_title");
            if (source.Length == 0 && group == "pass" && Number(row, "pass_id") is long passId
                && result.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Object
                && stages.TryGetProperty("passes", out var passes) && passes.ValueKind == JsonValueKind.Object
                && passes.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array)
                source = reports.EnumerateArray().Where(r => Number(r, "selected_pass_id") == passId)
                    .Select(r => Text(r, "pass_title")).FirstOrDefault(t => t.Length > 0) ?? "";
            output.Add(new(group, Text(row, "title"), Number(row, "progress"), Number(row, "required"),
                state.Length > 0 ? state : "pending", reason, source));
        }
        return output;
    }
}

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
    public static IReadOnlyList<QueueTaskDetail> Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("pending_tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array) return [];
        var output = new List<QueueTaskDetail>();
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

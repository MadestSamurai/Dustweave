using System.Text.Json;
using System.Text.Json.Nodes;
namespace Dustweave;

// Interpret semantic result fields only. Receipts, before/after observations and
// navigation commands can contain historical errors or reasons for taking action.
public static class DailyOutcomeExplanation
{
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";
    private static JsonElement Get(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) ? v : default;
    private static IEnumerable<JsonElement> Rows(JsonElement row) => row.ValueKind == JsonValueKind.Array ? row.EnumerateArray().Take(64) : [];
    private static string Direct(JsonElement result) => new[] { "error", "detail", "reason" }.Select(k => Text(result, k)).FirstOrDefault(s => s.Length > 0) ?? "";
    public static string Read(string stage, JsonElement result)
    {
        string detail = Text(result, "detail");
        if (detail.Length > 0) return detail;
        if (Get(result, "waiting_daily_reset").ValueKind == JsonValueKind.True) return "吸收次数用完，次日接续未完成地图";
        var lines = new List<string>();
        void Add(string text, string label = "") { if (text.Length > 0) lines.Add(label.Length == 0 ? text : label + "：" + text); }
        void Child(JsonElement row, string label = "") => Add(Direct(row), label);
        Add(Direct(result));
        switch (stage)
        {
            case "free_draws":
                foreach (var row in Rows(Get(result, "operations")))
                {
                    string reason = Direct(row);
                    if (reason.Length == 0 && Text(row, "state") == "no_free_draws") reason = "no_free_draws";
                    Add(reason, Text(row, "category") switch { "Costume" => "服装免费抽取", "Equipment" => "装备免费抽取", _ => "每日免费抽取" });
                }
                break;
            case "management":
                Child(Get(Get(result, "stages"), "income"), "经营收益");
                Child(Get(Get(result, "stages"), "guests"), "餐厅顾客与气泡");
                break;
            case "equipment": case "equipment_refine": case "equipment_recycle":
                var plan = Get(result, "plan");
                foreach (var value in Rows(Get(plan, "pending"))) if (value.ValueKind == JsonValueKind.String) Add(value.GetString() ?? "");
                foreach (var value in Rows(Get(plan, "reasons"))) if (value.ValueKind == JsonValueKind.String) Add(value.GetString() ?? "");
                foreach (var issue in Rows(Get(Get(result, "inspection"), "issues"))) Child(issue, "装备任务检查");
                if (lines.Count == 0 && Text(result, "state") == "skipped" && plan.ValueKind == JsonValueKind.Object)
                    Add(Rows(Get(plan, "retained")).Any() ? "equipment_protected" : "equipment_no_actions");
                break;
            case "daily_dispatch":
                foreach (var row in Rows(Get(result, "characters")))
                    Add(Direct(row) is { Length: > 0 } cause ? cause : Direct(Get(row, "result")), "派遣");
                if (lines.Count == 0 && Text(result, "state") == "skipped") Add("dispatch_no_available_characters");
                break;
            case "monster_hunt":
                foreach (var row in Rows(Get(result, "seasons"))) Child(row, "魔兽快速战斗");
                break;
        }
        foreach (var issue in Rows(Get(result, "issues"))) Child(issue);
        foreach (var location in Rows(Get(result, "deferred_locations"))) Child(location);
        return string.Join("\n", lines.Distinct(StringComparer.Ordinal));
    }
    public static void Complete(string stage, JsonObject result)
    {
        string state = DailyData.S(result["state"]);
        if (state is not ("completed" or "skipped" or "partial")) return;
        string detail = Read(stage, JsonSerializer.SerializeToElement(result));
        if (detail.Length == 0 && state is "skipped" or "partial")
        {
            detail = state == "skipped" ? "stage_skipped_without_reason" : "stage_partial_without_reason";
            result["explanation_missing"] = true;
        }
        if (detail.Length > 0) result["detail"] = detail;
    }
}

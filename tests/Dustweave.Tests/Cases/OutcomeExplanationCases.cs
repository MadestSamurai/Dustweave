using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;

static class OutcomeExplanationCases
{
    public static void Run(List<string> checks)
    {
        void Check(bool yes, string text) { if (!yes) throw new Exception(text); checks.Add(text); }
        string Read(string stage, string json) => DailyOutcomeExplanation.Read(stage, JsonSerializer.Deserialize<JsonElement>(json));
        string draws = Read("free_draws", """{"state":"skipped","operations":[{"category":"Costume","state":"no_free_draws"},{"category":"Equipment","state":"no_free_draws"}]}""");
        Check(draws.Contains("服装免费抽取：no_free_draws") && draws.Contains("装备免费抽取：no_free_draws"), "legacy free draws expose both category outcomes");
        string guests = Read("management", """{"state":"partial","stages":{"income":{"state":"completed"},"guests":{"state":"partial","reason":"idle_no_visible_bubble"}}} """);
        Check(guests == "餐厅顾客与气泡：idle_no_visible_bubble", "management explains the incomplete child instead of hiding it behind its aggregate state");
        Check(Read("daily_dispatch", """{"state":"partial","characters":[{"state":"partial","result":{"state":"partial","reason":"insufficient_catalyst"}}]}""") == "派遣：insufficient_catalyst",
            "dispatch propagates the nested settlement reason after claiming and starting");
        string equipment = Read("equipment", """{"state":"partial","plan":{"pending":["free_draw_receipt_unobserved","no_useful_refinement_candidate"]},"inspection":{"issues":[{"reason":"pass_not_ready"}]}}""");
        Check(equipment.Contains("free_draw_receipt_unobserved") && equipment.Contains("no_useful_refinement_candidate") && equipment.Contains("pass_not_ready"), "equipment explanation retains all independent pending causes");
        Check(Read("equipment", """{"state":"skipped","plan":{"retained":["synthetic-instance"]}}""") == "equipment_protected", "legacy retained equipment receives a protection explanation without exposing item instance IDs");
        Check(Read("mail", """{"state":"completed","result":{"before":{"error":"OLD_FAILURE"},"response":{"reason":"OLD_FAILURE"}},"operations":[{"reason":"CLICK_TO_START"}]}""") == "", "successful receipts and command intent never become outcome errors");
        Check(Read("management", """{"state":"partial","stages":{"income":4,"guests":null},"issues":[null,3,{}]}""") == "", "malformed optional child results do not break timeline loading");
        var source = JsonNode.Parse("""{"state":"skipped","operations":[{"category":"Equipment","state":"no_free_draws"}]}""")!.AsObject();
        DailyOutcomeExplanation.Complete("free_draws", source);
        Check(source["detail"]!.GetValue<string>().Contains("no_free_draws") && source["explanation_missing"] == null, "new durable queue results include projected explanations");
        DailyOutcomeExplanation.Complete("mail", source = new() { ["state"] = "partial" });
        Check(source["explanation_missing"]!.GetValue<bool>() && source["detail"]!.GetValue<string>() == "stage_partial_without_reason" && source["state"]!.GetValue<string>() == "partial", "missing producer explanation is explicit and does not change execution state");
        foreach (string reason in new[] { "no_free_draws", "free_draw_receipt_unobserved", "refinement_daily_limit_reached", "selected_refine_equipment_unavailable", "no_useful_refinement_candidate", "equipment_protected", "equipment_no_actions", "equipment_no_recycle_source", "equipment_no_refinement_task", "dispatch_no_available_characters", "idle_no_visible_bubble", "time_budget_reached", "stage_skipped_without_reason", "stage_partial_without_reason" })
            Check(DailyUserText.Describe(reason) != DailyUserText.Untranslated, "outcome code has actionable Chinese explanation: " + reason);
    }
}

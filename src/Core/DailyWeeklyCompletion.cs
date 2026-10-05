using System.Text.Json.Nodes;
namespace BD2Daily;

public interface IDailyWeeklyCompletionHost
{
    Task<JsonObject> SettleWeeklyAsync(IReadOnlyList<string> stages, JsonObject context);
}
public static class DailyWeeklyCompletion
{
    public static JsonObject Inspect(JsonObject evidence, IReadOnlyList<string> stages)
    {
        var result = new JsonObject();
        if (!stages.Contains("weekly_npc"))
            return result;
        var native = DailyEvidence.Reading(evidence, "weekly_npc.native", "$self")!.AsObject();
        long week = DailyEvidence.Integer(DailyEvidence.Reading(evidence, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        long limit = DailyEvidence.Integer(native["Limit"]), done = DailyEvidence.Integer(native["Completed"]);
        if (native["State"]?.GetValue<string>() != "ready" || native["Error"]?.GetValue<string>() != "" || limit < 1 || done != limit || DailyEvidence.Integer(native["Remaining"]) != 0 || DailyEvidence.Integer(native["Week"]) != week)
            return result;
        result["weekly_npc"] = new JsonObject { ["state"] = "completed", ["reason"] = "weekly_npc_verified", ["source"] = "TodayQuestInfoResponse", ["detail"] = $"周NPC任务 {done}/{limit}", ["engine"] = "dotnet-weekly-completion-v1", ["proof"] = native.DeepClone(), ["observation_at"] = evidence["AtUtcTicks"]!.DeepClone() };
        return result;
    }
}

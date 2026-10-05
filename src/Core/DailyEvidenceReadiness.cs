using System.Text.Json.Nodes;
namespace BD2Daily;

public sealed partial class DailyCommandDriver
{
    // Evidence is published before the UI snapshot. A known identity-sync frame is not a new actor.
    private static bool EvidenceFrameReady(JsonObject frame, JsonObject bound)
    {
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance" })
            if (frame[key] == null || !JsonNode.DeepEquals(frame[key], bound[key]))
                throw new StageHostException("identity", "证据所属游戏进程或连接已变化，已保留进度。");
        foreach (string key in new[] { "AccountKey", "PlayerKey" })
            if (Text(frame, key).Length > 0 && !JsonNode.DeepEquals(frame[key], bound[key]))
                throw new StageHostException("identity", "证据所属账号或角色已变化，已保留进度。");
        string error = Text(frame, "Error");
        if (error == "Waiting for fresh account identity") return false;
        if (error.Length > 0)
            throw new StageHostException("adapter", "证据观察失败：" + error);
        if (!DailyEvidence.SameActor(frame, bound))
            throw new StageHostException("adapter", "证据缺少账号身份，且未报告可恢复的切场状态。");
        return true;
    }
    private static JsonObject EvidenceFrameSummary(JsonObject frame)
    {
        var result = new JsonObject();
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey", "Scene", "AtUtcTicks", "Error" })
            result[key] = frame[key]?.DeepClone();
        return result;
    }
    private void SaveEvidenceObservation(string outcome, JsonObject expected, JsonObject? first, JsonObject? last, JsonObject request, double elapsed)
    {
        try
        {
            DailyJson.Write(Path.Combine(root, "live", "evidence-observations", now() + "-" + Guid.NewGuid().ToString("N") + ".json"),
                new { outcome, expected = EvidenceFrameSummary(expected), first, last, request, elapsedSeconds = elapsed, gameplayReplayed = false });
        }
        catch { /* Diagnostics must not mask identity/stop failures or interrupt recovered gameplay. */ }
    }
}

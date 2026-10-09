using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

// Mail is an input dependency of token activities, not only a final cleanup task.
// One initial refresh also recovers mail left by an interrupted previous run.
public sealed class DailyEventMailDependency(bool enabled)
{
    public bool Required { get; private set; } = enabled;
    public JsonArray Results { get; } = new();
    public void RewardClaimed() { if (enabled) Required = true; }
    public async Task Collect(Func<Task<JsonObject>> collect)
    {
        if (!Required) return;
        var result = await collect();
        Require(S(result["state"]) is "completed" or "skipped", "活动所需邮箱奖励尚未领取完成");
        Results.Add(result.DeepClone());
        Required = false;
    }
}

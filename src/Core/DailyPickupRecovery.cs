using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

// A failed, paired server reply is a terminal rejection, not an unknown reward.
// Missing replies, mixed success/failure and changed actors remain unresolved.
internal static class DailyPickupRecovery
{
    internal static JsonObject? Rejection(JsonObject op, JsonArray events)
    {
        if (S(op["role"]) != "mainline.pickup" || op["before"]?["Frame"] is not JsonObject actor) return null;
        try
        {
            var rows = Rows(events).Where(e => S(e["Role"]) is "mainline.pickup" or "mainline.pickup_list")
                .OrderBy(e => N(e["Sequence"])).ToArray();
            if (rows.Length == 0 || rows.Length % 2 != 0 || rows.Select(e => N(e["Sequence"])).Distinct().Count() != rows.Length) return null;
            if (rows.Any(e => !DailyEvidence.SameActor(e["Frame"]!.AsObject(), actor) || N(e["AtUtcTicks"]) < N(op["at"]) || S(e["Error"]) != "")) return null;
            for (int i = 0; i < rows.Length; i += 2)
            {
                var request = rows[i]; var reply = rows[i + 1];
                if (S(request["Kind"]) != "request" || S(reply["Kind"]) != "response" || S(request["Role"]) != S(reply["Role"]) ||
                    reply["Accepted"]?.GetValue<bool>() != false || N(reply["ErrorCode"]) <= 0) return null;
                var values = DailyEvidence.Values(reply);
                if (!values.ContainsKey("RewardInfoBundle") || values["RewardInfoBundle"] != null) return null;
            }
            return O(("method", "paired_server_rejection_without_rewards"), ("response_count", rows.Length / 2),
                ("codes", Array(rows.Where(e => S(e["Kind"]) == "response").Select(e => e["ErrorCode"]))), ("reward_claimed", false));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return null; }
    }
    internal static bool TalentInactive(JsonObject evidence) => evidence["Frame"] is JsonObject frame &&
        DailyNavigationDecision.Rows(frame).Any(r => S(r["NativeContext"]) == "talent_inactive_ack_only");
}

internal sealed class DailyPickupRejectedException(bool talentInactive) : Exception("游戏拒绝了拾取；未确认获得物品，重新检查探查与地图目标。")
{
    internal bool TalentInactive { get; } = talentInactive;
}

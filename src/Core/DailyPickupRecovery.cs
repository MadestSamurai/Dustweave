using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

// A failed, paired server reply is a terminal rejection, not an unknown reward.
// Missing replies, mixed success/failure and changed actors remain unresolved.
internal static class DailyPickupRecovery
{
    internal static (long Pack, long Map)? Location(JsonObject op)
    {
        if (S(op["role"]) != "mainline.pickup" || S(op["action"]?["operation"]) != "mainline_walk") return null;
        try
        {
            long map = N(op["scope"]?["map"]), pack = N(op["scope"]?["pack"]);
            if (op["before"] is not JsonObject before || N(DailyFieldRoute.Map(before)["id"]) != map) return null;
            long observedPack = N(DailyFieldRoute.Map(before)["packId"]);
            if (pack == 0) pack = observedPack;
            return map > 0 && pack > 0 && pack == observedPack ? (pack, map) : null;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return null; }
    }

    internal static bool PathRejected(DailyStepException error) => error.Kind == "rejected" && error.DefinitelyNotDispatched &&
        error.RejectionCode is "travel_unreachable:target_missing" or "travel_unreachable:target_off_navigation" or
            "travel_unreachable:path_incomplete" or "travel_unreachable:pickup_area_unreachable";

    // Upgrade recovery is deliberately tied to the audited v115 dispatch order.
    // An empty event list alone never proves that a consuming operation was unsent.
    internal static bool CanRetireUnstartedWalk(JsonObject op, JsonArray events, JsonObject? receipt, DailyStageFrame current)
    {
        if (S(op["role"]) != "mainline.pickup" || S(op["action"]?["operation"]) != "mainline_walk" || events.Count != 0 ||
            receipt?["Command"] is not JsonObject command || receipt["Before"] is not JsonObject frame ||
            op["before"]?["Frame"] is not JsonObject original || !MatchesAccount(op, current) ||
            !DailyEvidence.SameActor(frame, original) || !DailyEvidence.SameActor(command, original) ||
            S(command["Id"]) != S(op["command_id"]) || S(command["Id"]).Length != 32 || S(command["Kind"]) != "mainline_walk" ||
            S(command["Reason"]) != "business:" + S(op["id"]) + "|mainline.pickup" ||
            !JsonNode.DeepEquals(command["Value"], op["action"]?["value"]) || S(command["Scene"]) != S(original["Scene"]) ||
            N(frame["AtUtcTicks"]) < N(op["at"]) || N(frame["AtUtcTicks"]) > N(current.Frame["AtUtcTicks"])) return false;
        // These exact failures in v115 precede ClearQuestNav/SetMoveStart/SetMoveNav.
        string error = S(receipt["Error"]).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return N(frame["BridgeVersion"]) == 115 && S(receipt["State"]) == "unknown" &&
            receipt["MayHaveDispatched"] is JsonValue flag && flag.TryGetValue<bool>(out var marked) && marked &&
            new[] { "path_incomplete", "target_off_navigation", "player_off_navigation", "player_missing" }
                .Any(code => error == "System.InvalidOperationException: travel_unreachable:" + code);
    }

    private static bool MatchesAccount(JsonObject op, DailyStageFrame current)
    {
        if (op["before"]?["Frame"] is not JsonObject original || current.Context["actor"] is not JsonArray actor || actor.Count != 5) return false;
        string[] fields = ["ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey"];
        return !fields.Where((field, i) => actor[i] == null || !JsonNode.DeepEquals(current.Frame[field], actor[i])).Any() &&
            S(current.Frame["AccountKey"]).Length > 0 && S(current.Frame["PlayerKey"]).Length > 0 &&
            JsonNode.DeepEquals(op["account"], actor[3]) && JsonNode.DeepEquals(op["player"], actor[4]) &&
            JsonNode.DeepEquals(op["account"], original["AccountKey"]) && JsonNode.DeepEquals(op["player"], original["PlayerKey"]) &&
            op["server"] != null && JsonNode.DeepEquals(op["server"], current.Context["server"]);
    }

    internal static bool SettledWalk(JsonObject op, JsonArray events, JsonObject current)
    {
        return op["before"]?["Frame"] is JsonObject actor && DailyEvidence.SameActor(actor, current) &&
            DailyFieldRoute.FieldInputReady(current) && OriginalWalkSettled(op, events, current);
    }

    // Retiring old navigation is different from issuing movement in the current game.
    // Original replies keep their original observer identity. A replacement observer,
    // later login or different page need not reproduce a historical pickup snapshot.
    // This only releases the journal: it never confirms a target or replays an input.
    internal static bool CanRetireWalk(JsonObject op, JsonArray events, DailyStageFrame current)
    {
        if (op["before"]?["Frame"] is not JsonObject original || current.Context["actor"] is not JsonArray actor || actor.Count != 5)
            return false;
        string[] fields = ["ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey"];
        if (fields.Where((field, i) => actor[i] == null || !JsonNode.DeepEquals(current.Frame[field], actor[i])).Any() ||
            S(current.Frame["AccountKey"]).Length == 0 || S(current.Frame["PlayerKey"]).Length == 0 ||
            !JsonNode.DeepEquals(op["account"], actor[3]) || !JsonNode.DeepEquals(op["player"], actor[4]) ||
            !JsonNode.DeepEquals(op["account"], original["AccountKey"]) || !JsonNode.DeepEquals(op["player"], original["PlayerKey"]) ||
            op["server"] == null || !JsonNode.DeepEquals(op["server"], current.Context["server"])) return false;
        return OriginalWalkSettled(op, events, current.Frame);
    }

    private static bool OriginalWalkSettled(JsonObject op, JsonArray events, JsonObject current)
    {
        if (S(op["role"]) != "mainline.pickup" || S(op["action"]?["operation"]) != "mainline_walk" ||
            op["before"]?["Frame"] is not JsonObject actor) return false;
        try
        {
            var rows = Rows(events).OrderBy(e => N(e["Sequence"])).ToArray();
            if (rows.Length == 0 || rows.Length % 2 != 0 || rows.Select(e => N(e["Sequence"])).Distinct().Count() != rows.Length ||
                rows.Any(e => e["Frame"] is not JsonObject eventFrame || !DailyEvidence.SameActor(eventFrame, actor) || N(e["AtUtcTicks"]) < N(op["at"]) ||
                    N(e["AtUtcTicks"]) > N(current["AtUtcTicks"]) || S(e["Error"]) != "")) return false;
            for (int i = 0; i < rows.Length; i += 2)
            {
                var request = rows[i]; var reply = rows[i + 1];
                string role = S(request["Role"]);
                if (role is not ("mainline.pickup" or "mainline.pickup_list") || S(reply["Role"]) != role ||
                    S(request["Kind"]) != "request" || S(reply["Kind"]) != "response" || !B(reply["Accepted"]) ||
                    DailyEvidence.Integer(reply["ErrorCode"]) != 0 || N(reply["AtUtcTicks"]) < N(request["AtUtcTicks"]))
                    return false;
                if (!DailyEvidence.Values(reply).ContainsKey("RewardInfoBundle")) return false;
            }
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return false; }
    }

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

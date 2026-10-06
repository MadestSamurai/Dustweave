using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyFriendshipProof
{
    public const string Role = "friendship.counsel";
    public static JsonObject State(JsonObject evidence) => DailyEvidence.Reading(evidence, "friendship.native", "$self")!.AsObject();
    public static Dictionary<long, JsonObject> Candidates(JsonObject state)
    {
        var result = new Dictionary<long, JsonObject>();
        foreach (var node in state["Candidates"]!.AsArray())
        {
            var row = node!.AsObject();
            long id = DailyEvidence.Integer(row["Id"]);
            if (id <= 0 || DailyEvidence.Integer(row["Costume"]) <= 0 || DailyEvidence.Integer(row["Level"]) < 0 || DailyEvidence.Integer(row["Remaining"]) < 0 || row["Max"] is not JsonValue max || !max.TryGetValue<bool>(out _) || row["Quick"] is not JsonValue quick || !quick.TryGetValue<bool>(out _) || !result.TryAdd(id, row))
                throw new InvalidDataException("Invalid or duplicate counseling candidate");
        }
        return result;
    }
    public static JsonObject Choose(JsonObject state, bool quick = false)
    {
        if (state["Available"]?.GetValue<bool>() != true)
            return new()
            {
                ["state"] = "skipped",
                ["reason"] = "friendship_unavailable"
            };
        long free = DailyEvidence.Integer(state["Free"]), limit = DailyEvidence.Integer(state["DailyLimit"]);
        if (free < 0 || free > limit)
            throw new InvalidDataException("Invalid native counseling free quota");
        long remaining = Math.Max(0, Math.Min(3, limit) - (limit - free));
        if (remaining == 0)
            return new()
            {
                ["state"] = "skipped",
                ["reason"] = "daily_three_completed"
            };
        var rows = Candidates(state).Values.Where(r => r["Max"]!.GetValue<bool>() == false && DailyEvidence.Integer(r["Remaining"]) > 0).OrderBy(r => !(quick && r["Quick"]!.GetValue<bool>())).ThenBy(r => DailyEvidence.Integer(r["Level"])).ThenBy(r => DailyEvidence.Integer(r["Id"])).ToArray();
        return rows.Length == 0 ? new()
        {
            ["state"] = "partial",
            ["reason"] = "no_nonmax_counseling_target"
        } : new()
        {
            ["state"] = "ready",
            ["remaining"] = remaining,
            ["target"] = rows[0].DeepClone()
        };
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after)
    {
        var response = DailyNativeProof.Response(Role, before, events, after);
        var old = State(before);
        var fresh = State(after);
        if (Choose(old)["state"]?.GetValue<string>() != "ready" || fresh["Available"]?.GetValue<bool>() != true || DailyEvidence.Integer(fresh["DailyLimit"]) != DailyEvidence.Integer(old["DailyLimit"]) || DailyEvidence.Integer(fresh["Free"]) != DailyEvidence.Integer(old["Free"]) - 1)
            throw new InvalidDataException("Counseling free quota did not decrease exactly once");
        long selected = DailyEvidence.Integer(old["Selected"]);
        var prior = Candidates(old);
        var current = Candidates(fresh);
        if (!prior.TryGetValue(selected, out var target) || target["Max"]!.GetValue<bool>() || !current.TryGetValue(selected, out var next) || DailyEvidence.Integer(target["Remaining"]) <= 0 || DailyEvidence.Integer(next["Remaining"]) >= DailyEvidence.Integer(target["Remaining"]) || !JsonNode.DeepEquals(target["Costume"], next["Costume"]) || DailyEvidence.Integer(next["Level"]) < DailyEvidence.Integer(target["Level"]))
            throw new InvalidDataException("Counseling target/progress mismatch");
        return new()
        {
            ["friendship"] = selected,
            ["costume"] = target["Costume"]!.DeepClone(),
            ["free_remaining"] = fresh["Free"]!.DeepClone(),
            ["gained_exp"] = response["GainedExp"]?.DeepClone()
        };
    }
    public static bool OwnsPreview(JsonObject op, DailyStageFrame frame)
    {
        if (op["role"]?.GetValue<string>() != Role || op["scope"]?["quick"]?.GetValue<bool>() != true || op["state"]?.GetValue<string>() != "preview_ready" || op["preview_frame"] is not JsonObject saved || !JsonNode.DeepEquals(op["server"], frame.Context["server"]) || !JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]) || !DailyEvidence.SameActor(saved, frame.Frame) || !JsonNode.DeepEquals(saved["Scene"], frame.Frame["Scene"]) || !JsonNode.DeepEquals(saved["UiToken"], frame.Frame["UiToken"]))
            return false;
        var old = DailyNavigationDecision.Rows(saved).Where(r => r["Type"]?.GetValue<string>() == "FriendshipCounselingEnterPopupUI").ToArray();
        var current = DailyNavigationDecision.Rows(frame.Frame).Where(r => r["Type"]?.GetValue<string>() == "FriendshipCounselingEnterPopupUI").ToArray();
        return old.Length == 1 && current.Length == 1 && JsonNode.DeepEquals(old[0]["Id"], current[0]["Id"]) && DailyNavigationDecision.Blockers(frame.Frame, "FriendshipCounselingEnterPopupUI", DailyNavigationPolicy.Load()).Length == 0;
    }
    public static DailyBusinessProof Definition() => new(Role, "friendship", ["friendship", "reward.presentation"], (op, e, a) => Verify(op["before"]!.AsObject(), e, a), PreviewOwner: OwnsPreview);
}

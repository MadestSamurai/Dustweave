using System.Text.Json.Nodes;
namespace BD2Daily;

public static class DailyWeeklyNpcQueryProof
{
    public static JsonObject Verify(JsonObject before, JsonObject after, string query, int pack)
    {
        if (!DailyEvidence.SameActor(before["Frame"]!.AsObject(), after["Frame"]!.AsObject()) || !JsonNode.DeepEquals(before["Config"], after["Config"]))
            throw new InvalidDataException("NPC progress query observer changed");
        var state = DailyEvidence.Reading(after, "weekly_npc.native", "$self")!.AsObject();
        long week = DailyEvidence.Integer(DailyEvidence.Reading(before, "mainline.reset", "GetWeeklyResetTime().Ticks")), currentWeek = DailyEvidence.Integer(DailyEvidence.Reading(after, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        if (state["State"]?.GetValue<string>() != "ready" || state["Error"]?.GetValue<string>() != "" || state["Query"]?.GetValue<string>() != query || DailyEvidence.Integer(state["Pack"]) != pack || week != currentWeek || DailyEvidence.Integer(state["Week"]) != week)
            throw new InvalidDataException("NPC progress query is pending or belongs to a different request, cartridge or reset");
        long limit = DailyEvidence.Integer(state["Limit"]), done = DailyEvidence.Integer(state["Completed"]), remaining = DailyEvidence.Integer(state["Remaining"]);
        if (limit < 1 || done < 0 || done > limit || remaining < 0 || remaining > limit - done || state["ActionsSupported"] is not JsonValue supported || !supported.TryGetValue<bool>(out bool canAct))
            throw new InvalidDataException("Incomplete NPC progress query result");
        if (!canAct && state["CanAccept"]?.GetValue<bool>() != false)
            throw new InvalidDataException("Unsupported NPC cartridge must not expose accept permission");
        return new()
        {
            ["source"] = "TodayQuestInfoResponse",
            ["week"] = week,
            ["pack"] = pack,
            ["limit"] = limit,
            ["completed"] = done,
            ["remaining"] = remaining,
            ["actions_supported"] = canAct,
            ["query"] = query,
            ["actions"] = 0
        };
    }
}
public sealed partial class DailyCommandDriver
{
    private async Task<JsonObject> WeeklyNpcQueryAsync(JsonObject action)
    {
        Active();
        ValidateAction(action);
        if (!HasControl)
            Acquire("live");
        int pack = (int)Number(action, "value");
        if (pack <= 0 || Text(action, "ui") is not ("GameFieldDefaultUI" or "QuestBoardUI"))
            throw new DailyStepException("rejected", "NPC progress query requires the observed current field");
        string[] prefixes = ["weekly_npc.native", "mainline.reset"];
        var before = await EvidenceAsync(prefixes);
        var bound = (await ReadBound()).Frame;
        var sent = await SubmitRawAsync(action, bound);
        string path = Path.Combine(root, "live", "weekly-npc-queries", sent["id"]!.GetValue<string>() + ".json");
        double end = clock() + 45;
        JsonObject? after = null;
        string last = "Waiting for native NPC progress response";
        while (clock() < end)
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "Stopped during read-only NPC progress query");
            await ReadBound();
            after = await EvidenceAsync(prefixes);
            try
            {
                var proof = DailyWeeklyNpcQueryProof.Verify(before, after, sent["id"]!.GetValue<string>(), pack);
                DailyJson.Write(path, new JsonObject { ["state"] = "completed", ["engine"] = "dotnet-weekly-npc-query-v1", ["transport"] = sent.DeepClone(), ["before"] = before, ["after"] = after, ["result"] = proof });
                return sent;
            }
            catch (InvalidDataException error) { last = error.Message; }
            var native = DailyEvidence.Reading(after, "weekly_npc.native", "$self")?.AsObject();
            if (native?["Query"]?.GetValue<string>() == sent["id"]!.GetValue<string>() && native["State"]?.GetValue<string>() is "failed" or "invalid")
                break;
            await delay(TimeSpan.FromMilliseconds(200));
        }
        DailyJson.Write(path, new JsonObject { ["state"] = "unavailable", ["engine"] = "dotnet-weekly-npc-query-v1", ["transport"] = sent.DeepClone(), ["before"] = before, ["after"] = after, ["error"] = last, ["read_only"] = true });
        throw new StageHostException("adapter", "NPC progress query could not be verified; no NPC mutation submitted: " + last);
    }
}

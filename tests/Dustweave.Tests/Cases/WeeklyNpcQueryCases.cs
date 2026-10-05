using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Daily;
static class WeeklyNpcQueryCases
{
    static JsonObject Frame()
    {
        var f = CommandDriverCases.Frame();
        f["Surfaces"]![0]!["Type"] = "GameFieldDefaultUI";
        f["Surfaces"]![0]!["InputReady"] = true;
        return f;
    }
    static JsonObject State(string query, int pack, bool supported) => new() { ["State"] = "ready", ["Error"] = "", ["Query"] = query, ["Pack"] = pack, ["Week"] = 500, ["Limit"] = 3, ["Completed"] = 3, ["Remaining"] = 0, ["ActionsSupported"] = supported, ["CanAccept"] = false };
    static JsonObject Evidence(JsonObject state, long at = 100000000) => new()
    {
        ["AtUtcTicks"] = at,
        ["Error"] = "",
        ["Config"] = "test",
        ["Frame"] = Frame(),
        ["Readings"] = new JsonArray(
        new JsonObject { ["Id"] = "weekly_npc.native", ["Error"] = "", ["Values"] = new JsonArray(new JsonObject { ["Path"] = "$self", ["Error"] = "", ["Json"] = state.ToJsonString() }) },
        new JsonObject { ["Id"] = "mainline.reset", ["Error"] = "", ["Values"] = new JsonArray(new JsonObject { ["Path"] = "GetWeeklyResetTime().Ticks", ["Error"] = "", ["Json"] = "500" }) })
    };
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        foreach (int pack in new[] { 14, 1001, 2001 })
        {
            var before = Evidence(State("prior", pack, pack != 2001));
            var after = Evidence(State("query", pack, pack != 2001));
            var proof = DailyWeeklyNpcQueryProof.Verify(before, after, "query", pack);
            Check(proof["completed"]!.GetValue<long>() == 3 && proof["actions_supported"]!.GetValue<bool>() == (pack != 2001), "NPC global progress remains readable with truthful action support in cartridge " + pack);
        }
        foreach (string condition in new[] { "query", "pack", "cycle", "actor", "config", "permission", "count", "remaining", "missing-support", "failed" })
        {
            var before = Evidence(State("prior", 2001, false));
            var state = State("query", 2001, false);
            var after = Evidence(state);
            switch (condition)
            {
                case "query":
                    state["Query"] = "prior";
                    break;
                case "pack":
                    state["Pack"] = 14;
                    break;
                case "cycle":
                    state["Week"] = 501;
                    break;
                case "actor":
                    after["Frame"]!["Instance"] = "other";
                    break;
                case "config":
                    after["Config"] = "other";
                    break;
                case "permission":
                    state["CanAccept"] = true;
                    break;
                case "count":
                    state["Completed"] = 4;
                    break;
                case "remaining":
                    state["Remaining"] = 1;
                    break;
                case "missing-support":
                    state.Remove("ActionsSupported");
                    break;
                case "failed":
                    state["State"] = "failed";
                    break;
            }
            after["Readings"]![0]!["Values"]![0]!["Json"] = state.ToJsonString();
            bool rejected = false;
            try
            {
                DailyWeeklyNpcQueryProof.Verify(before, after, "query", 2001);
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "managed NPC progress query refuses " + condition);
        }
        string path = Path.Combine(output, "weekly-npc-readonly");
        var box = new CommandDriverCases.Mailbox();
        JsonObject native = State("prior", 2001, false);
        double time = 0;
        box.AfterWrite = (_, name, bytes) => { if (name != "observation-request.json") return; var request = JsonNode.Parse(bytes)!.AsObject(); var evidence = Evidence(native, 100000000 + (long)(time * TimeSpan.TicksPerSecond)); evidence["ObservationRequest"] = request["Id"]!.DeepClone(); box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence); };
        box.AfterCommand = c => { native = State(c["Id"]!.GetValue<string>(), 2001, false); box.Values.Remove("live:observation-request.json"); };
        using var driver = new DailyCommandDriver(path, box, () => Task.FromResult(new DailyStageFrame(Frame(), CommandDriverCases.Context())), () => false, () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t => { time += t.TotalSeconds; box.Values.Remove("live:observation-request.json"); return Task.CompletedTask; });
        driver.Bind(CommandDriverCases.Context());
        var result = await driver.SubmitAsync(new()
        {
            ["ui"] = "GameFieldDefaultUI",
            ["operation"] = "weekly_npc_query",
            ["value"] = 2001,
            ["reason"] = "Read global weekly progress in the already owned special field"
        });
        Check(box.Commands.Count == 1 && box.Commands[0]["Kind"]!.GetValue<string>() == "weekly_npc_query" && File.Exists(Path.Combine(path, "live", "weekly-npc-queries", result["id"]!.GetValue<string>() + ".json")), "special-cartridge NPC read uses one managed request and preserves native response proof without accepting a quest");
        var settled = DailyWeeklyCompletion.Inspect(Evidence(State("query", 2001, false)), ["weekly_mainline", "weekly_npc", "weekly_steal"]);
        Check(settled["weekly_npc"]?["state"]?.GetValue<string>() == "completed" && settled["weekly_mainline"] == null && settled["weekly_steal"] == null, "server-completed NPC independently settles while other route selections remain pending");
    }
}

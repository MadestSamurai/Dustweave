using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Daily;
static class CollectionReadinessCases
{
    static JsonObject Frame(string ui)
    {
        var f = CommandDriverCases.Frame();
        f["Surfaces"]![0]!["Type"] = ui;
        f["Surfaces"]![0]!["InputReady"] = true;
        return f;
    }
    static JsonObject Row(int group, bool restricted = true) => new() { ["Kind"] = group / 100, ["Group"] = group, ["Instance"] = group, ["Reason"] = restricted ? "character_pack_restricted" : "", ["Gate"] = restricted ? "talent_reject:character_unavailable" : "", ["PackRestricted"] = restricted };
    static JsonObject Evidence(string ui, JsonArray rows, long at = 100000000)
    {
        JsonObject Reading(string id, string path, JsonNode value) => new()
        {
            ["Id"] = id,
            ["Error"] = "",
            ["Values"] = new JsonArray(new JsonObject { ["Path"] = path, ["Error"] = "", ["Json"] = value.ToJsonString() })
        };
        return new()
        {
            ["AtUtcTicks"] = at,
            ["Error"] = "",
            ["Config"] = "fixture",
            ["Frame"] = Frame(ui),
            ["Readings"] = new JsonArray(
            Reading("mainline.map", "ὮὬὬὮὠὮὪὠὧὩὪ", new JsonObject { ["id"] = 20021, ["packId"] = 2002 }), Reading("mainline.reset", "GetWeeklyResetTime().Ticks", JsonValue.Create(500)), Reading("mainline.talent_rows", "$self", rows),
            Reading("weekly_npc.native", "$self", new JsonObject { ["State"] = "ready", ["Error"] = "", ["Limit"] = 3, ["Completed"] = 3, ["Remaining"] = 0, ["Week"] = 500 }))
        };
    }
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        foreach (int kind in new[] { 3, 4, 6, 20 })
        {
            var proof = DailyCollectionReadiness.Inspect(Evidence("GameFieldDefaultUI", new()), Evidence("QuickMenuUI", new(Row(kind * 100 + 5))), kind);
            Check(proof?["reason"]?.GetValue<string>() == "collection_characters_pack_restricted" && proof["actions"]!.GetValue<int>() == 0, "all native characters restricted defers collection kind " + kind + " without claiming collected targets");
        }
        foreach (string condition in new[] { "alternate", "dead", "catalyst", "quota", "unrelated", "missing", "actor", "scene", "week", "map", "config", "surface" })
        {
            var before = Evidence("GameFieldDefaultUI", new());
            var row = Row(605);
            var after = Evidence("QuickMenuUI", new(row.DeepClone()));
            bool invalid = false;
            switch (condition)
            {
                case "alternate":
                    after = Evidence("QuickMenuUI", new(row, Row(625, false)));
                    break;
                case "dead":
                    row["PackRestricted"] = false;
                    row["Reason"] = "character_dead";
                    break;
                case "catalyst":
                    row["PackRestricted"] = false;
                    row["Reason"] = "catalyst_insufficient";
                    break;
                case "quota":
                    row["PackRestricted"] = false;
                    row["Reason"] = "";
                    row["Gate"] = "talent_reject:row_disabled";
                    break;
                case "unrelated":
                    after = Evidence("QuickMenuUI", new(Row(415)));
                    break;
                case "missing":
                    row.Remove("PackRestricted");
                    invalid = true;
                    break;
                case "actor":
                    after["Frame"]!["AccountKey"] = "other";
                    invalid = true;
                    break;
                case "scene":
                    after["Frame"]!["Scene"] = "other";
                    invalid = true;
                    break;
                case "week":
                    after["Readings"]![1]!["Values"]![0]!["Json"] = "501";
                    invalid = true;
                    break;
                case "map":
                    after["Readings"]![0]!["Values"]![0]!["Json"] = "{\"id\":20022,\"packId\":2002}";
                    invalid = true;
                    break;
                case "config":
                    after["Config"] = "other";
                    invalid = true;
                    break;
                case "surface":
                    after["Frame"] = Frame("MenuUI");
                    invalid = true;
                    break;
            }
            if (condition is "dead" or "catalyst" or "quota" or "missing")
                after = Evidence("QuickMenuUI", new(row));
            bool rejected = false;
            JsonObject? proof = null;
            try
            {
                proof = DailyCollectionReadiness.Inspect(before, after, 6);
            }
            catch (Exception e) when (e is InvalidDataException or StageHostException) { rejected = true; }
            Check(invalid ? rejected : !rejected && proof == null, "cartridge restriction proof cannot hide " + condition);
        }
        foreach (bool restricted in new[] { false, true })
        {
            string path = Path.Combine(output, "collection-readiness-" + restricted);
            string ui = "GameFieldDefaultUI";
            double time = 0;
            bool wait = !restricted;
            var box = new CommandDriverCases.Mailbox { Error = "" };
            box.AfterCommand = c => { ui = c["Kind"]!.GetValue<string>() == "mainline_menu" ? "QuickMenuUI" : "GameFieldDefaultUI"; box.Values.Remove("live:observation-request.json"); };
            box.AfterWrite = (_, name, bytes) =>
            {
                if (name != "observation-request.json")
                    return;
                var request = JsonNode.Parse(bytes)!.AsObject();
                var row = Row(605, restricted);
                if (wait)
                    row["Gate"] = "talent_wait:field_skill";
                var e = Evidence(ui, new(row), 100000000 + (long)(time * TimeSpan.TicksPerSecond));
                e["ObservationRequest"] = request["Id"]!.DeepClone();
                box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
            };
            using var driver = new DailyCommandDriver(path, box, () => Task.FromResult(new DailyStageFrame(Frame(ui), CommandDriverCases.Context())), () => false, () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t => { time += t.TotalSeconds; wait = false; box.Values.Remove("live:observation-request.json"); return Task.CompletedTask; });
            driver.Bind(CommandDriverCases.Context());
            string kind = "";
            try
            {
                await driver.SubmitAsync(new()
                {
                    ["ui"] = "GameFieldDefaultUI",
                    ["operation"] = "mainline_menu",
                    ["value"] = 6,
                    ["expect"] = "QuickMenuUI",
                    ["reason"] = "collection"
                });
            }
            catch (DailyStepException e) { kind = e.Kind; }
            Check(restricted ? kind == "deferred" && box.Commands.Count == 2 : kind == "" && box.Commands.Count == 1 && time > 0, "managed collection menu " + (restricted ? "closes and defers restricted characters before any talent activation" : "waits out native field animation before legacy row selection"));
            Check(box.Commands.All(c => c["Kind"]!.GetValue<string>() != "mainline_talent"), "collection readiness never consumes a talent " + restricted);
            if (restricted)
            {
                var proof = await driver.TakeCollectionDeferralAsync();
                Check(proof != null && Directory.EnumerateFiles(Path.Combine(path, "live", "collection-deferrals")).Count() == 1 && await driver.TakeCollectionDeferralAsync() == null, "managed cartridge deferral is durable and can only be consumed once");
            }
        }
    }
}


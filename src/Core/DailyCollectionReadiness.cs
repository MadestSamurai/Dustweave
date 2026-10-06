using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyCollectionReadiness
{
    public static JsonObject? Inspect(JsonObject before, JsonObject after, int kind)
    {
        if (kind is not (3 or 4 or 6 or 20))
            return null;
        var first = before["Frame"]!.AsObject();
        var last = after["Frame"]!.AsObject();
        if (!DailyEvidence.SameActor(first, last) || !JsonNode.DeepEquals(first["Scene"], last["Scene"]) || !JsonNode.DeepEquals(before["Config"], after["Config"]))
            throw new InvalidDataException("Collection menu identity or configuration changed");
        if (!DailyNavigationDecision.Types(first).Contains("GameFieldDefaultUI") || !DailyNavigationDecision.Types(last).Contains("QuickMenuUI"))
            throw new InvalidDataException("Collection menu is no longer observed");
        const string mapPath = "ὮὬὬὮὠὮὪὠὧὩὪ";
        var map = DailyEvidence.Reading(before, "mainline.map", mapPath)!.AsObject();
        var current = DailyEvidence.Reading(after, "mainline.map", mapPath)!.AsObject();
        long week = DailyEvidence.Integer(DailyEvidence.Reading(before, "mainline.reset", "GetWeeklyResetTime().Ticks"));
        if (week <= 0 || week != DailyEvidence.Integer(DailyEvidence.Reading(after, "mainline.reset", "GetWeeklyResetTime().Ticks")) || !JsonNode.DeepEquals(map["id"], current["id"]) || !JsonNode.DeepEquals(map["packId"], current["packId"]))
            throw new InvalidDataException("Collection map or week changed");
        var rows = DailyEvidence.Reading(after, "mainline.talent_rows", "$self")!.AsArray().Where(r => DailyEvidence.Integer(r!["Kind"]) == kind).ToArray();
        if (rows.Length == 0)
            return null;
        if (rows.Any(r => r?["PackRestricted"] == null))
            throw new StageHostException("adapter", "Native collection restriction evidence is missing; reconnect the current version");
        if (rows.Any(r => r?["PackRestricted"]?.GetValue<bool>() != true || r["Reason"]?.GetValue<string>() != "character_pack_restricted"))
            return null;
        long pack = DailyEvidence.Integer(map["packId"]), id = DailyEvidence.Integer(map["id"]);
        if (pack <= 0 || id <= 0)
            throw new InvalidDataException("Collection field identity is missing");
        return new()
        {
            ["state"] = "partial",
            ["reason"] = "collection_characters_pack_restricted",
            ["detail"] = $"卡带 {pack} 地图 {id} 限制此天赋的所有角色；保留未收集目标，解除卡带限制后接续",
            ["pack"] = pack,
            ["map"] = id,
            ["weekly_reset"] = week,
            ["talent_kind"] = kind,
            ["actions"] = 0,
            ["engine"] = "dotnet-collection-readiness-v1",
            ["frame"] = last.DeepClone(),
            ["rows"] = new JsonArray(rows.Select(r => r!.DeepClone()).ToArray())
        };
    }
}

public sealed partial class DailyCommandDriver
{
    private JsonObject? collectionDeferral;
    private async Task<JsonObject> CollectionTalentMenuAsync(JsonObject action)
    {
        // Opening a menu never activates a talent. Own its readiness as well as
        // the later cast, so a transient native gate cannot become a false skip.
        var before = await EvidenceAsync(["mainline.map", "mainline.reset"]);
        var sent = await SubmitRawAsync(action, before["Frame"]!.AsObject());
        double started = clock(), end = started + 45, lastReport = double.NegativeInfinity;
        string lastGates = "";
        using var diagnostic = Diagnostics.Scope("collection_menu_readiness", DailyData.O(
            ("command_id", sent["id"]), ("kind", action["value"])));
        while (true)
        {
            var after = await EvidenceAsync(["mainline.map", "mainline.reset", "mainline.talent_rows"]);
            var proof = DailyCollectionReadiness.Inspect(before, after, checked((int)Number(action, "value")));
            if (proof != null)
            {
                proof["context"] = context!.DeepClone();
                proof["at"] = now();
                proof["menu_command"] = sent["command"]!.DeepClone();
                DailyJson.Write(Path.Combine(root, "live", "collection-deferrals", Text(sent, "id") + ".json"), proof);
                await SubmitRawAsync(new()
                {
                    ["ui"] = "QuickMenuUI",
                    ["back"] = true,
                    ["absent"] = "QuickMenuUI",
                    ["reason"] = "Close a collection menu whose characters are restricted by the current cartridge"
                }, after["Frame"]!.AsObject());
                collectionDeferral = proof;
                throw new DailyStepException("deferred", Text(proof, "detail"));
            }
            var rows = DailyEvidence.Reading(after, "mainline.talent_rows", "$self")!.AsArray().Where(r => DailyEvidence.Integer(r!["Kind"]) == Number(action, "value")).ToArray();
            bool transient = rows.Length == 0 || rows.Any(r => r?["Gate"]?.GetValue<string>() is "talent_wait:timeline" or "talent_wait:field_skill" or "talent_wait:animation" or "talent_wait:attempt_latch");
            if (!transient)
            {
                sent["native_readiness_seconds"] = clock() - started;
                Diagnostics.Event("collection_menu_ready", DailyData.O(("elapsed_seconds", clock() - started)));
                return sent;
            }
            string gates = string.Join(",", rows.Select(r => r?["Gate"]?.GetValue<string>() ?? "missing"));
            if (gates != lastGates || clock() - lastReport >= 5)
            {
                Diagnostics.Event("collection_menu_wait", DailyData.O(
                    ("elapsed_seconds", clock() - started), ("gates", gates), ("rows", rows.Length)));
                lastGates = gates; lastReport = clock();
            }
            if (clock() >= end)
                throw new DailyStepException("rejected", "Native talent menu did not settle; no talent activated");
            await delay(TimeSpan.FromMilliseconds(150));
        }
    }
    public async Task<JsonObject?> TakeCollectionDeferralAsync()
    {
        var proof = collectionDeferral;
        collectionDeferral = null;
        if (proof == null)
            return null;
        var current = await ReadBound();
        long age = now() - DailyEvidence.Integer(proof["at"]);
        if (stopped() || age < 0 || age > 30 * TimeSpan.TicksPerSecond || !JsonNode.DeepEquals(proof["context"], context) || !DailyEvidence.SameActor(proof["frame"]!.AsObject(), current.Frame) || !JsonNode.DeepEquals(proof["frame"]!["Scene"], current.Frame["Scene"]))
            return null;
        return proof.DeepClone().AsObject();
    }
}

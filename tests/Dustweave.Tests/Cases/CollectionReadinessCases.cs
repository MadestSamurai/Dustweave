using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
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
        var locked = new JsonObject { ["State"] = "ready", ["Error"] = "", ["Pack"] = 2001, ["Maps"] = new JsonArray(20011, 20012),
            ["Week"] = 100, ["Now"] = 50, ["Rewards"] = "[]", ["Monsters"] = "[]", ["Drops"] = new JsonArray(), ["Targets"] = new JsonArray(),
            ["DeferredTargets"] = new JsonArray(new JsonObject { ["Id"] = 1, ["Map"] = 20011, ["Group"] = 2 }), ["StealReady"] = true, ["Steals"] = "[]", ["StealTargets"] = new JsonArray() };
        var classified = DailyCollectionProgress.Classify(locked, 2001, [20011, 20012]);
        if (classified["completed"]!["20011"] != null || classified["completed"]!["20012"] == null || classified["maps"]!["20011"]!["story_locked"]!.AsArray().Count != 1)
            throw new Exception("Story locked targets became false completion or blocked unrelated maps");
        cases.Add("story gated monsters remain pending while unrelated maps can complete");
        var theft = DailyCollectionProgress.Classify(locked, 2001, [20011, 20012], true, true);
        if (theft["completed"]!.AsObject().Count != 2) throw new Exception("Story monster gates blocked independent theft");
        cases.Add("story monster gates do not block independent NPC theft");
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        using (var f = new WorkflowHarness(Path.Combine(output, "weekly-full-catalog"), []))
        {
            f.Workflow.Settings.Weekly.Mainline = true;
            f.Workflow.Settings.Weekly.Steal = true;
            f.Page("GameFieldDefaultUI");
            f.Readings = () => [
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 500L)),
                WorkflowCases.Reading("mainline.owned_packs", ("Count", 2), ("$items", new JsonArray(new JsonObject { ["Key"]=1, ["Value.Id"]=1 }, new JsonObject { ["Key"]=2, ["Value.Id"]=2 }))),
                WorkflowCases.Reading("mainline.packs", ("Count", 2), ("$items", new JsonArray(new JsonObject { ["Id"]=1, ["PackType"]=0 }, new JsonObject { ["Id"]=2, ["PackType"]=0 })))
            ];
            var counts = new Dictionary<string, long>();
            var route = new DailyWeeklyRoute(f.Workflow, (stage, progress) => {
                counts[stage] = DailyData.N(progress["total"]);
                if (counts.Count == 2) throw new OperationCanceledException("Synthetic pause before entering a cartridge");
            });
            try { await route.Run(["weekly_mainline", "weekly_steal"]); }
            catch (OperationCanceledException) { }
            var catalog = new DailyCollectionCatalog(f.Workflow);
            Check(counts["weekly_mainline"] == catalog.Maps(1).Length + catalog.Maps(2).Length && counts["weekly_steal"] == catalog.StealMaps(1).Length + catalog.StealMaps(2).Length,
                "weekly progress includes every available cartridge before entering the first one");
            Check(route.VerifiedResults().Count == 0 && f.Box.Commands.Count == 0, "known weekly targets cannot become completion proof without server progress");
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
        await ReusedScopeCase(output, cases);
        await FirstAppearanceCases(output, cases);
        await DelayedMenuCases(output, cases);
        await QueryTimeoutCases(output, cases);
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
    static async Task QueryTimeoutCases(string output, List<string> cases)
    {
        foreach (string scenario in new[] { "recovered", "persistent", "unknown", "late_reply" })
        {
            using var h = new WorkflowHarness(Path.Combine(output, "collection-query-timeout-" + scenario), []);
            h.Frame["Scene"] = "Map0018_003";
            h.Page("GameFieldDefaultUI");
            int reads = 0, acknowledgements = 0;
            double readyAt = 0;
            string first = "", current = "";
            JsonObject network = new();
            JsonObject Ready(string query) => new()
            {
                ["Query"] = query, ["State"] = "ready", ["Error"] = "", ["Pack"] = 18,
                ["Maps"] = new JsonArray(181), ["Week"] = 500, ["Now"] = 1000,
                ["Rewards"] = "[]", ["Monsters"] = "[]", ["Drops"] = new JsonArray(), ["Targets"] = new JsonArray()
            };
            h.Readings = () => [WorkflowCases.Reading("mainline.network", ("$self", network))];
            h.OnCommand = command =>
            {
                string kind = DailyData.S(command["Kind"]);
                if (kind == "collection_query")
                {
                    reads++; current = DailyData.S(command["Id"]); if (reads == 1) first = current;
                    network = new() { ["Query"] = current, ["State"] = "pending" };
                    if (reads == 1 || scenario == "persistent")
                    {
                        h.Page("MessagePopupUI", "_buttonOK");
                        h.Frame["Surfaces"]![0]!["NativeContext"] = scenario == "unknown" ? "" : "request_timeout_ack_only";
                        h.Frame["Surfaces"]![0]!["Text"] = new JsonArray("连线请求超时，请检查网络连接状态。");
                    }
                    else if (scenario == "late_reply") { network = Ready(first); readyAt = h.Time + 1; }
                    else network = Ready(current);
                }
                else if (kind == "background_error_ack")
                {
                    acknowledgements++; network["State"] = "failed"; network["Error"] = "Collection read timed out";
                    h.Page("GameFieldDefaultUI");
                }
                else throw new Exception("Read-only timeout recovery submitted unexpected input: " + kind);
            };
            h.OnDelay = () => { if (scenario == "late_reply" && reads == 2 && h.Time >= readyAt) network = Ready(current); };
            bool unavailable = false; JsonObject? result = null;
            try { result = await new DailyCollectionProgress(h.Workflow).Query(18, [181]); }
            catch (DailyCollectionReadUnavailable) { unavailable = true; }
            bool success = scenario is "recovered" or "late_reply";
            if (unavailable == success || (success && result?["completed"]?["181"] == null)
                || reads != (success ? 2 : scenario == "persistent" ? 3 : 1)
                || acknowledgements != (success ? 1 : scenario == "persistent" ? 3 : 0))
                throw new Exception("Collection query timeout recovery violated its bounded read/ack contract: " + scenario);
            if (scenario == "persistent" && DailyData.S(h.Frame["Surfaces"]![0]!["Type"]) != "GameFieldDefaultUI")
                throw new Exception("Exhausted read retries left the acknowledged timeout blocking the next cartridge");
            if (scenario == "late_reply" && h.Time < readyAt) throw new Exception("Late reply from old query completed the new one");
            cases.Add("collection timeout " + scenario + " preserves progress and only retries verified read-only queries");
        }
    }
    static async Task ReusedScopeCase(string output, List<string> cases)
    {
        string ui = "GameFieldDefaultUI";
        double time = 0;
        var requests = new List<JsonObject>();
        var box = new CommandDriverCases.Mailbox { Error = "" };
        void Publish(JsonObject request)
        {
            var row = Row(605, false);
            if (ui == "QuickMenuUI" && time < 1) row["Gate"] = "talent_wait:animation";
            var evidence = Evidence(ui, new(row), 100000000 + (long)(time * TimeSpan.TicksPerSecond));
            evidence["ObservationRequest"] = request["Id"]!.DeepClone();
            box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
        }
        box.AfterWrite = (_, name, bytes) =>
        {
            if (name != "observation-request.json") return;
            var request = JsonNode.Parse(bytes)!.AsObject(); requests.Add(request);
            if (ui == "GameFieldDefaultUI") Publish(request);
            // Leave the pre-open evidence in the mailbox until the next native tick.
        };
        box.AfterCommand = _ => ui = "QuickMenuUI";
        using var driver = new DailyCommandDriver(Path.Combine(output, "collection-reused-scope"), box,
            () => Task.FromResult(new DailyStageFrame(Frame(ui), CommandDriverCases.Context())), () => false,
            () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t =>
            {
                time += t.TotalSeconds;
                if (time >= .5 && box.Values.TryGetValue("live:observation-request.json", out var bytes))
                    Publish(JsonNode.Parse(bytes)!.AsObject());
                return Task.CompletedTask;
            });
        driver.Bind(CommandDriverCases.Context());
        await driver.SubmitAsync(new() { ["ui"] = "GameFieldDefaultUI", ["operation"] = "mainline_menu", ["value"] = 6, ["expect"] = "QuickMenuUI" });
        var after = await driver.EvidenceAsync(["mainline", "dispatch.talent"]);
        if (time < 1 || requests.Count != 2 || box.Commands.Count != 1
            || JsonNode.DeepEquals(requests[0]["Id"], requests[1]["Id"])
            || JsonNode.DeepEquals(requests[0]["Prefixes"], requests[1]["Prefixes"])
            || !DailyNavigationDecision.Types(after["Frame"]!.AsObject()).Contains("QuickMenuUI"))
            throw new Exception("Menu scope reused stale evidence or renewed again before row selection");
        cases.Add("menu opening renews evidence once and rejects the cached pre-open frame");
        cases.Add("menu animation and row selection reuse one scope without replaying the open command");
    }
    static async Task FirstAppearanceCases(string output, List<string> cases)
    {
        foreach (string scenario in new[] { "delayed", "passive-overlay", "input-animation", "never-opens", "closed", "replaced", "battle", "unknown-popup", "stop", "scene", "week" })
        {
            double time = 0;
            bool submitted = false, stopped = false;
            var box = new CommandDriverCases.Mailbox { Error = "" };
            string Ui() => !submitted || time < .6 || scenario == "never-opens" || scenario == "closed" && time >= 1.2
                ? "GameFieldDefaultUI" : scenario == "battle" ? "BattleUI" : "QuickMenuUI";
            JsonObject Current()
            {
                var frame = Frame(Ui());
                frame["AtUtcTicks"] = 100000000 + (long)(time * TimeSpan.TicksPerSecond);
                if (scenario == "input-animation" && Ui() == "QuickMenuUI" && time < 1.2) frame["Surfaces"]![0]!["InputReady"] = false;
                if (scenario == "replaced" && time >= 1.2) frame["Surfaces"]![0]!["Id"] = 2;
                if (scenario == "passive-overlay")
                    foreach (string type in new[] { "OverheadManageUI", "NoticeUI", "CurrencyManageUI" })
                        frame["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = type, ["Popup"] = true, ["InputReady"] = true });
                if (scenario == "unknown-popup" && time >= .3)
                    frame["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "UnknownPopupUI", ["Popup"] = true, ["InputReady"] = true });
                if (scenario == "scene" && time >= .3) frame["Scene"] = "other";
                return frame;
            }
            void Publish(JsonObject request)
            {
                // Nonempty stale rows must not count as a menu that has appeared.
                var row = Row(305, false);
                if (scenario is "closed" or "replaced") row["Gate"] = "talent_wait:animation";
                var e = Evidence(Ui(), new(row), 100000000 + (long)(time * TimeSpan.TicksPerSecond));
                e["Frame"] = Current();
                if (scenario == "week" && time >= .3) e["Readings"]![1]!["Values"]![0]!["Json"] = "501";
                e["ObservationRequest"] = request["Id"]!.DeepClone();
                box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
            }
            box.AfterCommand = _ => submitted = true;
            box.AfterWrite = (_, name, bytes) => { if (name == "observation-request.json") Publish(JsonNode.Parse(bytes)!.AsObject()); };
            string path = Path.Combine(output, "collection-first-appearance-" + scenario);
            using var driver = new DailyCommandDriver(path, box,
                () => Task.FromResult(new DailyStageFrame(Current(), CommandDriverCases.Context())), () => stopped,
                () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t =>
                {
                    time += t.TotalSeconds;
                    if (scenario == "stop" && time >= .3) stopped = true;
                    if (box.Values.TryGetValue("live:observation-request.json", out var bytes)) Publish(JsonNode.Parse(bytes)!.AsObject());
                    return Task.CompletedTask;
                });
            driver.Bind(CommandDriverCases.Context());
            JsonObject? result = null;
            Exception? failure = null;
            try { result = await driver.SendObservedAsync(new() { ["ui"] = "GameFieldDefaultUI", ["operation"] = "mainline_menu", ["value"] = 3, ["expect"] = "QuickMenuUI" }); }
            catch (Exception e) { failure = e; }
            bool ok = scenario switch
            {
                "delayed" or "passive-overlay" => failure == null && result?["state"]?.GetValue<string>() == "observed_expected_ui" && time >= .6,
                "input-animation" => failure == null && result?["state"]?.GetValue<string>() == "observed_expected_ui" && time >= 1.2,
                "never-opens" => failure is DailyStepException { Kind: "rejected", Submitted: true } && time >= 45 && time < 50,
                "closed" or "replaced" or "battle" or "unknown-popup" => failure is DailyStepException { Kind: "rejected", Submitted: true } && time < 2,
                "stop" => failure is StageHostException { Kind: "stopped" },
                _ => failure is InvalidDataException && time < 2
            };
            if (!ok) throw new Exception("collection first appearance " + scenario + ": " + failure);
            cases.Add("collection first appearance handles " + scenario);
            if (box.Commands.Count != 1 || box.Commands[0]["Kind"]?.GetValue<string>() != "mainline_menu")
                throw new Exception("collection first appearance repeated input: " + scenario);
            cases.Add("collection first appearance " + scenario + " never repeats opening or casts a talent");
            if (failure is DailyStepException or StageHostException { ObservationTimedOut: true })
            {
                var snapshot = Directory.GetFiles(Path.Combine(path, "live", "diagnostics", "failures"), "*.json")
                    .Select(file => JsonNode.Parse(File.ReadAllText(file))!.AsObject())
                    .Single(f => f["kind"]?.GetValue<string>() == "collection_menu_readiness_failed");
                var detail = snapshot["detail"]!;
                if (!JsonNode.DeepEquals(detail["command"], box.Commands[0]) || detail["receipt"]?["MayHaveDispatched"]?.GetValue<bool>() != true
                    || detail["before"]?["Frame"] == null || detail["after"]?["Frame"] == null)
                    throw new Exception("collection failure omitted dispatch and transition evidence: " + scenario);
                cases.Add("collection failure preserves the dispatched menu receipt and transition for " + scenario);
            }
        }
    }
    // Exercise the public observed-input path, not only SubmitAsync: the outer
    // deadline used to expire while the inner native animation wait succeeded.
    static async Task DelayedMenuCases(string output, List<string> cases)
    {
        foreach (string scenario in new[] { "slow", "resume", "never-ready", "stop", "account", "scene" })
        {
            string ui = "GameFieldDefaultUI";
            double time = 0;
            bool stopped = false;
            var context = CommandDriverCases.Context();
            JsonObject Current()
            {
                var frame = Frame(ui);
                frame["AtUtcTicks"] = 100000000 + (long)(time * TimeSpan.TicksPerSecond);
                if (scenario == "scene" && time >= 25) frame["Scene"] = "other";
                if (scenario == "account" && time >= 25)
                {
                    frame["AccountKey"] = new string('d', 64);
                    context["actor"]![3] = new string('d', 64);
                }
                return frame;
            }
            var box = new CommandDriverCases.Mailbox { Error = "" };
            box.AfterCommand = _ => { ui = "QuickMenuUI"; box.Values.Remove("live:observation-request.json"); };
            box.AfterWrite = (_, name, bytes) =>
            {
                if (name != "observation-request.json") return;
                var row = Row(2005, false);
                if (time < 36 || scenario == "never-ready") row["Gate"] = "talent_wait:animation";
                var e = Evidence(ui, new(row), 100000000 + (long)(time * TimeSpan.TicksPerSecond));
                e["Frame"] = Current();
                e["ObservationRequest"] = JsonNode.Parse(bytes)!["Id"]!.DeepClone();
                box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
            };
            using var driver = new DailyCommandDriver(Path.Combine(output, "collection-observed-" + scenario), box,
                () => Task.FromResult(new DailyStageFrame(Current(), context.DeepClone().AsObject())), () => stopped,
                () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t =>
                {
                    time += t.TotalSeconds;
                    if (scenario == "resume" && time < 1) time = 120;
                    if (scenario == "stop" && time >= 25) stopped = true;
                    box.Values.Remove("live:observation-request.json");
                    return Task.CompletedTask;
                });
            driver.Bind(CommandDriverCases.Context());
            JsonObject? result = null;
            Exception? failure = null;
            try { result = await driver.SendObservedAsync(new() { ["ui"] = "GameFieldDefaultUI", ["operation"] = "mainline_menu", ["value"] = 20, ["expect"] = "QuickMenuUI" }); }
            catch (Exception e) when (e is DailyStepException or StageHostException or InvalidDataException) { failure = e; }
            bool ok = scenario switch
            {
                "slow" or "resume" => failure == null && result?["state"]?.GetValue<string>() == "observed_expected_ui" && time >= 36,
                "never-ready" => failure is DailyStepException { Kind: "rejected" } && time >= 45 && time < 50,
                "stop" => failure is StageHostException { Kind: "stopped" },
                "account" => failure is DailyStepException { Kind: "identity" } or StageHostException { Kind: "identity" },
                _ => failure is InvalidDataException
            };
            if (!ok) throw new Exception("collection observed " + scenario + ": " + failure);
            cases.Add("collection observed input handles " + scenario + " independently of presentation deadline");
            if (box.Commands.Count != 1 || box.Commands[0]["Kind"]?.GetValue<string>() != "mainline_menu")
                throw new Exception("collection repeated input: " + scenario);
            cases.Add("collection " + scenario + " opens once and never activates a talent");
        }
    }
}

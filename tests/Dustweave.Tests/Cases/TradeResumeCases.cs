using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
static class TradeResumeCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        JsonObject Frame()
        {
            var frame = CommandDriverCases.Frame();
            frame["Surfaces"]![0]!["Type"] = "ShopUI";
            return frame;
        }
        JsonObject Read(string id, params (string Path, JsonNode Value)[] values) => new()
        {
            ["Id"] = id,
            ["Error"] = "",
            ["Values"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject { ["Path"] = v.Path, ["Error"] = "", ["Json"] = v.Value.ToJsonString() }).ToArray())
        };
        JsonObject Evidence(long gold = 1245, long potions = 167, long count = 30) => new()
        {
            ["Error"] = "",
            ["AtUtcTicks"] = 100000000,
            ["Frame"] = Frame(),
            ["Config"] = "spec",
            ["Readings"] = new JsonArray(
            Read("trade.currency", ("Gold", JsonValue.Create(gold)!), ("Catalyst", JsonValue.Create(potions)!)),
            Read("trade.inventory", ("Count", JsonValue.Create(2)!), ("$items", new JsonArray(new JsonObject { ["Key"] = "Food", ["Value.Count"] = 1, ["Value.Values"] = new JsonArray(new JsonObject { ["invenIndex"] = "stack", ["id"] = 1, ["type"] = 5, ["count"] = count }) }, new JsonObject { ["Key"] = "Other", ["Value.Count"] = 1, ["Value.Values"] = new JsonArray(new JsonObject { ["id"] = 2, ["type"] = 8, ["count"] = 3 }) }))),
            Read("trade.bargain", ("$self", JsonValue.Create(false)!)), Read("trade.native", ("$self", new JsonObject { ["Date"] = "2026-10-02", ["ServerTicks"] = 10, ["BargainActive"] = false, ["Offers"] = new JsonArray(new JsonObject { ["Shop"] = 1, ["Product"] = 2, ["Remaining"] = 100, ["Price"] = 24 }) })))
        };
        var context = CommandDriverCases.Context();
        context["cycle"] = "123";
        string account = context["actor"]![3]!.GetValue<string>();
        var catalog = new JsonObject { ["schema"] = 1, ["client"] = "client", ["database_sha256"] = "database" };
        var original = new JsonObject { ["account"] = account, ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = "test", ["cycle"] = "123", ["client"] = "client", ["database_sha256"] = "database" };
        JsonObject Job() => new()
        {
            ["id"] = new string('c', 32),
            ["at"] = 100,
            ["state"] = "stopped",
            ["phase"] = "purchase",
            ["operations"] = new JsonArray(),
            ["account"] = account,
            ["context"] = original.DeepClone(),
            ["catalog_hash"] = DailyTradeCatalog.Fingerprint(catalog),
            ["initial"] = DailyTradeResume.Wallet(Evidence(1000, 100, 20)),
            ["plan"] = new JsonObject { ["context"] = original.DeepClone(), ["catalog_hash"] = DailyTradeCatalog.Fingerprint(catalog), ["game_date"] = "2026-10-02" }
        };
        JsonObject Planning() => new()
        {
            ["context"] = original.DeepClone(),
            ["game_date"] = "2026-10-02",
            ["offers"] = new JsonArray(new JsonObject { ["shop"] = 1, ["product"] = 2, ["remaining"] = 100, ["price"] = 24 })
        };
        JsonObject Reward() => new()
        {
            ["id"] = new string('d', 32),
            ["account"] = account,
            ["player"] = context["actor"]![4]!.DeepClone(),
            ["server"] = "test",
            ["cycle"] = "123",
            ["at"] = 200,
            ["role"] = "mail.collect",
            ["state"] = "completed",
            ["before"] = Evidence(),
            ["after"] = Evidence(),
            ["events"] = new JsonArray(
            new JsonObject { ["Kind"] = "request", ["Role"] = "mail.collect", ["Sequence"] = 1, ["Frame"] = Frame() },
            new JsonObject { ["Kind"] = "response", ["Role"] = "mail.collect", ["Sequence"] = 2, ["Error"] = "", ["ErrorCode"] = 0, ["Accepted"] = true, ["Frame"] = Frame(), ["Values"] = new JsonArray(new JsonObject { ["Path"] = "RewardInfoBundle", ["Error"] = "", ["Json"] = "{\"itemInfo\":[{\"type\":4,\"count\":245},{\"type\":12,\"count\":67},{\"type\":5,\"id\":1,\"count\":10}],\"viewItemInfo\":[{\"type\":4,\"count\":245}]}" }) })
        };
        var result = DailyTradeResume.Evaluate(Job(), context, catalog, Evidence(), Planning(), [Reward()]);
        Check(result?["gold_gain"]!.GetValue<long>() == 245 && result["potions_gain"]!.GetValue<long>() == 67 && result["food_gains"]!["1"]!.GetValue<long>() == 10, "unsubmitted trade refresh requires exact native reward gains and excludes display duplicates");
        Check(DailyTradeResume.Evaluate(Job(), context, catalog, Evidence(1000, 100, 20), Planning(), []) == null, "unchanged original balance does not rewrite a transaction");
        Check(DailyTradeResume.MatchesInitial(Job(), Evidence(1000, 100, 20)) && !DailyTradeResume.MatchesInitial(Job(), Evidence()), "first shop comparison requests historical reward review only when the old initial balance actually differs");
        var menuEvidence = Evidence();
        menuEvidence["Frame"]!["Surfaces"]![0]!["Type"] = "MenuUI";
        Check(DailyTradeResume.Evaluate(Job(), context, catalog, menuEvidence, Planning(), [Reward()]) != null, "read-only unsubmitted review also works before the business host opens the shop from the native menu");
        foreach (string entrance in new[] { "GameFieldDefaultUI", "QuickMenuUI" })
        {
            var entry = Evidence();
            entry["Frame"]!["Surfaces"]![0]!["Type"] = entrance;
            Check(DailyTradeResume.Evaluate(Job(), context, catalog, entry, Planning(), [Reward()]) != null, "unsubmitted resource review accepts the managed trade navigator's already verified entrance: " + entrance);
        }
        var fieldWithHud = Evidence();
        fieldWithHud["Frame"]!["Surfaces"]![0]!["Type"] = "GameFieldDefaultUI";
        fieldWithHud["Frame"]!["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "OverheadManageUI", ["Popup"] = true, ["Order"] = 301 });
        Check(DailyTradeResume.Evaluate(Job(), context, catalog, fieldWithHud, Planning(), [Reward()]) != null, "shared background policy accepts the real native field HUD without confirming or dismissing it");
        var oldLedger = new JsonObject { ["account"] = account, ["at"] = 50, ["role"] = "management", ["scope"] = new JsonArray(1, 2), ["state"] = "completed" };
        Check(DailyTradeResume.Evaluate(Job(), context, catalog, Evidence(), Planning(), [oldLedger, Reward()]) != null, "historical unrelated array scopes do not masquerade as trade ownership or block proven rewards");
        var historical = Reward();
        foreach (var historic in new[] { historical["before"]!["Frame"]!, historical["after"]!["Frame"]!, historical["events"]![0]!["Frame"]!, historical["events"]![1]!["Frame"]! })
            historic["Instance"] = "previous-connection";
        Check(DailyTradeResume.Evaluate(Job(), context, catalog, Evidence(), Planning(), [historical]) != null, "completed reward receipts retain one historical connection within the same game process after a new queue reconnects");
        foreach (string scenario in new[] { "gold", "potions", "food", "no-reward", "pending", "receipt-cycle", "receipt-player", "receipt-error", "receipt-connection", "receipt-process", "duplicate-response", "display-only", "catalog", "cycle", "bargain", "stock", "price", "date", "planning-context", "planning-date", "truncated", "non-food", "protected", "other", "trade-receipt", "foreign-popup", "foreign-battle", "running", "negative-reward" })
        {
            var job = Job();
            var evidence = Evidence();
            var planning = Planning();
            var ledger = new List<JsonObject> { Reward() };
            var ctx = context.DeepClone().AsObject();
            switch (scenario)
            {
                case "gold":
                    evidence = Evidence(1244);
                    break;
                case "potions":
                    evidence = Evidence(potions: 166);
                    break;
                case "food":
                    evidence = Evidence(count: 29);
                    break;
                case "no-reward":
                    ledger.Clear();
                    break;
                case "pending":
                    ledger[0]["state"] = "unknown";
                    break;
                case "receipt-cycle":
                    ledger[0]["cycle"] = "124";
                    break;
                case "receipt-player":
                    ledger[0]["player"] = "another";
                    break;
                case "receipt-error":
                    ledger[0]["events"]![1]!["ErrorCode"] = 1;
                    break;
                case "duplicate-response":
                    ledger[0]["events"]!.AsArray().Add(ledger[0]["events"]![1]!.DeepClone());
                    break;
                case "display-only":
                    ledger[0]["events"]![1]!["Values"]![0]!["Json"] = "{\"viewItemInfo\":[{\"type\":4,\"count\":245}]}";
                    break;
                case "receipt-connection":
                    ledger[0]["after"]!["Frame"]!["Instance"] = "changed-during-response";
                    break;
                case "receipt-process":
                    ledger[0]["before"]!["Frame"]!["ProcessStartTicks"] = 11;
                    break;
                case "catalog":
                    job["catalog_hash"] = "different";
                    break;
                case "cycle":
                    ctx["cycle"] = "124";
                    break;
                case "bargain":
                    evidence["Readings"]![2]!["Values"]![0]!["Json"] = "true";
                    break;
                case "stock":
                    planning["offers"]![0]!["remaining"] = 99;
                    break;
                case "price":
                    planning["offers"]![0]!["price"] = 23;
                    break;
                case "date":
                    job["plan"]!["game_date"] = "2026-10-01";
                    break;
                case "planning-context":
                    planning["context"]!["cycle"] = "124";
                    break;
                case "planning-date":
                    planning["game_date"] = "2026-10-01";
                    break;
                case "truncated":
                    evidence["Readings"]![1]!["Values"]![0]!["Json"] = "3";
                    break;
                case "non-food":
                    evidence["Readings"]![1]!["Values"]![1]!["Json"] = evidence["Readings"]![1]!["Values"]![1]!["Json"]!.GetValue<string>().Replace("\"type\":5", "\"type\":9");
                    break;
                case "protected":
                    job["initial"]!["protected"]!["stack"] = new JsonObject { ["keepFlag"] = 1 };
                    break;
                case "other":
                    job["initial"]!["other"] = "different";
                    break;
                case "trade-receipt":
                    ledger.Add(new()
                    {
                        ["account"] = account,
                        ["role"] = "trade.buy",
                        ["at"] = 250,
                        ["state"] = "completed",
                        ["scope"] = new JsonObject { ["trade_session"] = job["id"]!.DeepClone() }
                    });
                    break;
                case "foreign-popup":
                    evidence["Frame"]!["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "MessagePopupUI", ["Popup"] = true });
                    break;
                case "running":
                    job["state"] = "running";
                    break;
                case "foreign-battle":
                    evidence["Frame"]!["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "BattleUI_PVE", ["Popup"] = false });
                    break;
                case "negative-reward":
                    ledger[0]["events"]![1]!["Values"]![0]!["Json"] = "{\"itemInfo\":[{\"type\":4,\"count\":-1}]}";
                    break;
            }
            bool refused = false;
            try
            {
                DailyTradeResume.Evaluate(job, ctx, catalog, evidence, planning, ledger);
            }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "unsubmitted trade preserves ambiguity: " + scenario);
        }
        foreach (string phase in new[] { "cooking", "sale", "audit" })
        {
            var job = Job();
            job["phase"] = phase;
            Check(DailyTradeResume.Evaluate(job, context, catalog, Evidence(), Planning(), [Reward()]) == null, "post-purchase trade never refreshes initial resources: " + phase);
        }
        var consumed = Job();
        consumed["operations"]!.AsArray().Add(new string('e', 32));
        Check(DailyTradeResume.Evaluate(consumed, context, catalog, Evidence(), Planning(), [Reward()]) == null, "confirmed progress retains its original strict wallet reconciliation");
        // Exercise the real managed driver: two fresh observations, no input, immutable history.
        string root = Path.Combine(output, "trade-resume-driver"), folder = Path.Combine(root, "trade", "executions", account, "123"), path = Path.Combine(folder, "execution.json");
        var originalJob = Job();
        DailyJson.Write(path, originalJob);
        DailyJson.Write(Path.Combine(folder, "planning-state.json"), Planning());
        DailyJson.Write(Path.Combine(root, "live", "business", new string('d', 32) + ".json"), Reward());
        var box = new CommandDriverCases.Mailbox();
        void Publish()
        {
            var request = box.Read("live", "observation-request.json");
            if (request == null)
                return;
            var evidence = Evidence();
            evidence["ObservationRequest"] = JsonNode.Parse(request)!["Id"]!.DeepClone();
            box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
        }
        box.AfterWrite = (_, _, _) => Publish();
        using var driver = new DailyCommandDriver(root, box, () => { Publish(); return Task.FromResult(new DailyStageFrame(Frame(), context)); }, () => false, () => 100000000);
        driver.Bind(context);
        driver.Acquire("live");
        var proof = await driver.PrepareTradeResumeAsync(catalog);
        var updated = DailyTradeCatalog.Read(path);
        Check(proof?["state"]!.GetValue<string>() == "completed" && box.Commands.Count == 0, "managed unsubmitted resume records proof without game inputs or resource operations");
        Check(JsonNode.DeepEquals(updated["plan"], originalJob["plan"]) && JsonNode.DeepEquals(updated["initial_original"], originalJob["initial"]) && JsonNode.DeepEquals(DailyTradeCatalog.Read(Path.Combine(Path.GetDirectoryName(updated["baseline_updates"]![0]!.GetValue<string>())!, "execution-before.json")), originalJob), "managed resume preserves the exact original transaction, plan and initial inventory in history");
        Check(await driver.PrepareTradeResumeAsync(catalog) == null && updated["baseline_updates"]!.AsArray().Count == 1, "repeating a reviewed unsubmitted resume does not duplicate gains or history");
        File.WriteAllText(Path.Combine(root, "live", "business", "unrelated-malformed.json"), "broken history");
        Check(await driver.PrepareTradeResumeAsync(catalog) == null && box.Commands.Count == 0, "unchanged first-shop baseline does not parse unrelated history again or submit any input");
        string scopeRoot = Path.Combine(output, "trade-resume-demand"), scopeFolder = Path.Combine(scopeRoot, "trade", "executions", account, "123"), scopePath = Path.Combine(scopeFolder, "execution.json");
        DailyJson.Write(scopePath, Job());
        DailyJson.Write(Path.Combine(scopeFolder, "planning-state.json"), Planning());
        DailyJson.Write(Path.Combine(scopeRoot, "live", "business", new string('d', 32) + ".json"), Reward());
        bool inShop = false;
        var scopedBox = new CommandDriverCases.Mailbox();
        JsonObject CurrentFrame()
        {
            var frame = Frame();
            if (!inShop)
                frame["Surfaces"]![0]!["Type"] = "GameFieldDefaultUI";
            return frame;
        }
        void ScopedPublish()
        {
            var raw = scopedBox.Read("live", "observation-request.json");
            if (raw == null)
                return;
            var e = Evidence();
            e["Frame"] = CurrentFrame();
            e["ObservationRequest"] = JsonNode.Parse(raw)!["Id"]!.DeepClone();
            scopedBox.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
        }
        scopedBox.AfterWrite = (_, _, _) => ScopedPublish();
        using var scopedDriver = new DailyCommandDriver(scopeRoot, scopedBox, () => { ScopedPublish(); return Task.FromResult(new DailyStageFrame(CurrentFrame(), context)); }, () => false, () => 100000000) { TradeStageActive = true };
        scopedDriver.Bind(context);
        scopedDriver.Acquire("live");
        scopedDriver.ConfigureTradeResume(catalog);
        await scopedDriver.DemandAsync(["trade", "square", "dispatch"]);
        Check(scopedDriver.TradeResumeProof == null && DailyTradeCatalog.Read(scopePath)["initial"]!["gold"]!.GetValue<long>() == 1000, "field evidence does not read absent native shop stock or refresh the original transaction");
        inShop = true;
        var resumedDemand = await scopedDriver.DemandAsync(["trade", "square", "dispatch"]);
        Check(scopedDriver.TradeResumeProof?["state"]!.GetValue<string>() == "completed" && DailyTradeCatalog.Read(scopePath)["initial"]!["gold"]!.GetValue<long>() == 1245 && scopedBox.Commands.Count == 0, "first native shop evidence demand completes managed unsubmitted review before the unchanged worker checks or spends");
        Check(resumedDemand["Prefixes"]!.AsArray().Select(p => p!.GetValue<string>()).SequenceEqual(new[] { "dispatch", "reward.presentation", "square", "trade" }) && JsonNode.Parse(scopedBox.Read("live", "observation-request.json")!)!["Id"]!.GetValue<string>() == resumedDemand["Id"]!.GetValue<string>(), "nested managed review restores the worker's complete requested evidence scope and matching demand ID");
        await scopedDriver.DemandAsync(["trade", "dispatch"]);
        Check(DailyTradeCatalog.Read(scopePath)["baseline_updates"]!.AsArray().Count == 1, "later shop evidence demands do not repeat the baseline review");
    }
}

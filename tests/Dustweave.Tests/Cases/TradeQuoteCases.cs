using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

internal static class TradeQuoteCases
{
    private const string ShopId = "ὬὥὫὥὮὤὭὥὪὥὣ", ShopMode = "ὯὣὡὫὨὫὡὧὩὬὠ", ShopNpc = "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ";
    private const string Product = "ὯὫὪὡὭὤὪὮὫὬὣ", Price = "ὣὡὢὩὥὥὨὯὩὥὯ", Limit = "ὠὥὬὠὨὧὡὩὨὤὯ", Kind = "ὢὭὩὠὪὯὮὧὩὣὧ";
    private static JsonObject Reading(string id, params (string Key, object? Value)[] fields) => O(("Id", id), ("Error", ""),
        ("Values", Array(fields.Select(f => O(("Path", f.Key), ("Error", ""), ("Json", O(("value", f.Value))["value"]!.ToJsonString()))))));
    private static JsonObject Catalog()
    {
        var skill = O(("values", new[] { 100, 60 }), ("potions", 10));
        var character = O(("kind", 16), ("skills", O(("5", skill))));
        return O(("schema", 1), ("client", "fixture"), ("database_sha256", "fixture"),
            ("items", new[] { O(("id", 1013), ("shop", 1), ("sale", 30), ("stack", 999), ("day", 1)) }),
            ("offers", new[] { O(("shop", 1), ("product", 2), ("item", 1013), ("base_price", 27), ("limit", 400)) }),
            ("recipes", new JsonArray()), ("characters", O(("10", character))));
    }
    private static DailyTradeQuoteObservation Sample(double time, int displayedPrice = 10, int displayedLimit = 400)
    {
        long ticks = new DateTime(2026, 10, 4, 7, 38, 0, DateTimeKind.Utc).Ticks + (long)(time * TimeSpan.TicksPerSecond), reset = new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc).Ticks;
        var frame = O(("BridgeVersion", DailyStageObservation.BridgeVersion), ("AccountKey", "account"), ("PlayerKey", "player"), ("ProcessId", 123), ("ProcessStartTicks", 456L),
            ("Instance", "fixture-connection"), ("Scene", "Map3009_003"), ("AtUtcTicks", ticks),
            ("Surfaces", new[] { O(("Type", "ShopUI"), ("Id", 7), ("InputReady", true), ("Popup", false), ("Order", 303)) }));
        var native = O(("ServerTicks", ticks), ("Date", "2026-10-04"), ("AvailableShops", new[] { 1 }), ("UnavailableShops", System.Array.Empty<int>()),
            ("BargainActive", true), ("BargainPercent", 60), ("Quotes", new JsonArray()),
            ("Offers", new[] {
                O(("Shop", 1), ("Product", 1), ("Item", 0), ("Type", 12), ("PriceType", 4), ("NoBargain", 1), ("BasePrice", 50), ("Rate", 100), ("ReputationDiscount", 10), ("Price", 45), ("Limit", 99999), ("Remaining", 99999)),
                O(("Shop", 1), ("Product", 2), ("Item", 1013), ("Type", 5), ("PriceType", 4), ("NoBargain", 0), ("BasePrice", 27), ("Rate", 100), ("ReputationDiscount", 10), ("Price", 10), ("Limit", 400), ("Remaining", 400)) }));
        var readings = new[] {
            Reading("trade.native", ("$self", native)), Reading("trade.bargain", ("$self", true)), Reading("trade.currency", ("Gold", 1000000), ("Catalyst", 1000)),
            Reading("trade.available_shops", ("$items", new[] { O(("Id", 1)) }), ("ὫὨὧὫὯὭὨὢὦὬὪ.Count", 1)),
            Reading("trade.inventory", ("Count", 1), ("$items", new[] { O(("Key", "Food"), ("Value.Count", 0), ("Value.Values", new JsonArray())) })),
            Reading("trade.characters", ("Count", 1), ("$items", new[] { O(("Id", 10), ("TalentLevel", 5), ("Temporary", false)) })),
            Reading("trade.recipes", ("Count", 0), ("$self", new JsonArray())),
            Reading("trade.shop_ui", (ShopId, 1), (ShopMode, "Buy"), (ShopNpc, 2), ("ὤὦὣὥὭὣὫὨὡὢὮ.Count", 1),
                ("$items", new[] { O((Product, 2), (Kind, 5), (Price, displayedPrice), (Limit, displayedLimit)) })) };
        var evidence = O(("Config", "quote-fixture"), ("Error", ""), ("AtUtcTicks", ticks), ("Frame", frame), ("Readings", readings));
        var daily = O(("Error", ""), ("AccountKey", "account"), ("PlayerKey", "player"), ("PlayerName", "fixture"), ("ProcessId", 123), ("ProcessStartTicks", 456L), ("FrameUtcTicks", ticks),
            ("Guild", O(("Error", ""), ("ClientMvid", "fixture"), ("ServerKey", "server"), ("ServerTicks", ticks), ("ResetTicks", reset), ("CycleKey", reset.ToString()))));
        return new(evidence, daily, ticks);
    }
    public static async Task Run(string output, List<string> cases, string? replay = null)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        double clock = 0; int reads = 0; var records = new List<JsonObject>();
        var catalog = Catalog();
        var correct = await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(Sample(clock)), () => clock,
            ms => { clock += ms / 1000d; return Task.CompletedTask; }, records.Add);
        Check(N(correct.State["offers"]![0]!["price"]) == 10 && records.Count == 0 && clock == 0, "already matching shop does not add waits or actions");
        var recovered = await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(Sample(clock, ++reads < 3 ? 24 : 10)), () => clock,
            ms => { clock += ms / 1000d; return Task.CompletedTask; }, records.Add);
        Check(reads == 3 && N(recovered.State["offers"]![0]!["price"]) == 10, "input-ready ordinary models wait for confirmed bargain prices");
        Check(records.Count == 1 && S(records[0]["state"]) == "ready" && N(records[0]["gameplay_actions"]) == 0 && N(records[0]["first"]!["detail"]!["displayed_price"]) == 24,
            "delayed quote records original mismatch without a second bargain or purchase");
        // Server reset is fixed during a wait; model timestamps may advance.
        clock = 0; reads = 0; records.Clear();
        var timeout = false;
        try { await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(Sample(0, 24)), () => clock,
            ms => { clock += ms / 1000d; return Task.CompletedTask; }, records.Add, seconds: 1); }
        catch (InvalidOperationException e) when (e.Message.Contains("报价刷新超时")) { timeout = true; }
        Check(timeout && clock >= 1 && clock < 1.5 && records.Count == 1 && S(records[0]["state"]) == "failed", "persistent disagreement is bounded and preserves the shop before generic navigation");
        foreach (string change in new[] { "player", "process", "surface", "scene", "merchant", "shop", "discount", "cycle", "client", "popup", "closed" })
        {
            clock = 0; reads = 0; records.Clear(); bool rejected = false;
            try
            {
                await DailyTradeQuoteReadiness.ReadAsync(catalog, () => {
                    var s = Sample(0, ++reads == 1 ? 24 : 10);
                    if (reads > 1) switch (change)
                    {
                        case "player": s.Evidence["Frame"]!["PlayerKey"] = "other"; s.Daily["PlayerKey"] = "other"; break;
                        case "process": s.Evidence["Frame"]!["ProcessStartTicks"] = 999; s.Daily["ProcessStartTicks"] = 999; break;
                        case "surface": s.Evidence["Frame"]!["Surfaces"]![0]!["Id"] = 8; break;
                        case "scene": s.Evidence["Frame"]!["Scene"] = "other"; break;
                        case "merchant": SetReading(s.Evidence, "trade.shop_ui", ShopNpc, JsonValue.Create(3)!); break;
                        case "shop": SetReading(s.Evidence, "trade.shop_ui", ShopId, JsonValue.Create(2)!); break;
                        case "discount": var n = State(s.Evidence, "trade.native", "$self"); n["BargainPercent"] = 50; SetReading(s.Evidence, "trade.native", "$self", n); break;
                        case "cycle": s.Daily["Guild"]!["CycleKey"] = "other"; break;
                        case "client": s.Daily["Guild"]!["ClientMvid"] = "other"; break;
                        case "popup": s.Evidence["Frame"]!["Surfaces"]!.AsArray().Add(O(("Type", "MessagePopupUI"), ("Popup", true), ("Order", 1000))); break;
                        case "closed": s.Evidence["Frame"]!["Surfaces"] = new JsonArray(); break;
                    }
                    return Task.FromResult(s);
                }, () => clock, ms => { clock += ms / 1000d; return Task.CompletedTask; }, records.Add);
            }
            catch (StageHostException e) when (e.Kind == "identity") { rejected = true; }
            Check(rejected && reads == 2 && records.Count == 1, "quote refresh preserves " + change + " change");
        }
        clock = 0; reads = 0; records.Clear(); bool stopped = false;
        try { await DailyTradeQuoteReadiness.ReadAsync(catalog, () => { reads++; return Task.FromResult(Sample(0, 24)); }, () => clock,
            _ => throw new StageHostException("stopped", "fixture stop"), records.Add); }
        catch (StageHostException e) when (e.Kind == "stopped") { stopped = true; }
        Check(stopped && reads == 1 && records.Count == 1, "manual stop during quote wait does not perform another read or action");
        clock = 0; records.Clear(); bool invalid = false;
        var bad = catalog.DeepClone().AsObject(); bad["offers"]![0]!["base_price"] = 28;
        try { await DailyTradeQuoteReadiness.ReadAsync(bad, () => Task.FromResult(Sample(0)), () => clock, _ => throw new Exception("must not retry rules"), records.Add); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid && records.Count == 1 && clock == 0 && records[0]["last"]?["evidence"] != null, "invalid native rules preserve first-read diagnostics without retrying as an animation");
        clock = 0; records.Clear(); bool nativeFailed = false;
        var unreadable = Sample(0);
        Rows(unreadable.Evidence["Readings"]).Single(r => S(r["Id"]) == "trade.native")["Error"] = "Shop supply is missing or expired: 8";
        try { await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(unreadable), () => clock,
            _ => throw new Exception("native failure must not loop"), records.Add); }
        catch (InvalidDataException e) { nativeFailed = DailyIssues.Classify(e).Code == "trade-read" && e.Message.Contains("expired: 8"); }
        Check(nativeFailed && records.Count == 1 && N(records[0]["waits"]) == 0 && records[0]["last"]?["evidence"] != null,
            "initial trade.native failure retains its cause and complete evidence without a transaction or retry");
        records.Clear(); bool originalError = false;
        try { await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(unreadable), () => clock, _ => Task.CompletedTask,
            _ => throw new IOException("diagnostic disk unavailable")); }
        catch (InvalidDataException e) { originalError = e.Message.Contains("expired: 8"); }
        Check(originalError, "diagnostic write failure never masks the native read failure");
        foreach (string missing in new[] { "row", "values", "path-error" })
        {
            var sample = Sample(0); records.Clear(); bool classified = false;
            var row = Rows(sample.Evidence["Readings"]).Single(r => S(r["Id"]) == "trade.native");
            if (missing == "row") sample.Evidence["Readings"]!.AsArray().Remove(row);
            if (missing == "values") row["Values"] = new JsonArray();
            if (missing == "path-error") row["Values"]![0]!["Error"] = "fixture native path failure";
            try { await DailyTradeQuoteReadiness.ReadAsync(catalog, () => Task.FromResult(sample), () => clock, _ => Task.CompletedTask, records.Add); }
            catch (InvalidDataException e) { classified = DailyIssues.Classify(e).Code == (missing == "row" ? "trade-configuration" : "trade-read"); }
            Check(classified && records.Count == 1, "incomplete native evidence is classified and retained: " + missing);
        }
        BD2Daily.Live.TradeStockRules.RequireFresh(8, null, 200L, 100L);
        Check(BD2Daily.Live.TradeStockRules.Remaining(400, false, null) == 400,
            "unvisited shop uses fresh common reset and the game's zero purchased count");
        BD2Daily.Live.TradeStockRules.RequireFresh(8, 200L, null, 100L);
        Check(BD2Daily.Live.TradeStockRules.Remaining(400, false, 400) == 0 && BD2Daily.Live.TradeStockRules.Remaining(400, false, 25) == 375,
            "existing sold-out and partial shop records keep their exact purchased amounts");
        foreach (var timers in new (long? Own, long? Common)[] { (null, null), (null, 100L), (99L, 200L) })
        {
            bool rejected = false;
            try { BD2Daily.Live.TradeStockRules.RequireFresh(8, timers.Own, timers.Common, 100L); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "missing or expired stock cannot be guessed from an unrelated fresh timer: " + timers);
        }
        await Connection(output, cases);
        if (replay != null) await Replay(replay, output, cases);
    }
    private static async Task Connection(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        string root = Path.Combine(output, "manual-trade-connection");
        Directory.CreateDirectory(root);
        var host = new Dustweave.Desktop.DemoEnvironment(root);
        int prepared = 0, captured = 0; bool installed = false;
        Task Prepare() { prepared++; installed = true; return Task.CompletedTask; }
        Task<int> Capture(GameInstance game, string account)
        {
            Check(installed && DailyToolControl.IsOccupied(root) && game == host.Game && account == host.CurrentKey,
                "manual shop capture prepares evidence and retains exclusive ownership before reading");
            return Task.FromResult(++captured);
        }
        File.WriteAllText(Path.Combine(root, "queue-stop"), "previous queue stopped");
        Check(await DailyTradeCaptureConnection.RunAsync(root, host, host.CurrentKey, Prepare, Capture) == 1 && prepared == 1,
            "cold shop capture works without a guild visit or prior daily queue");
        Check(File.ReadAllText(Path.Combine(root, "queue-stop")) == "previous queue stopped" && host.Calls.Count == 0,
            "manual capture neither resumes a stopped queue nor launches, logs in or changes game pages");
        installed = false;
        Check(await DailyTradeCaptureConnection.RunAsync(root, host, host.CurrentKey, Prepare, Capture) == 2 && prepared == 2,
            "repeat manual read reestablishes configuration after another feature changed it");
        bool blocked = false;
        using (DailyToolControl.Acquire(root))
        {
            try { await DailyTradeCaptureConnection.RunAsync(root, host, host.CurrentKey, Prepare, Capture); }
            catch (InvalidOperationException) { blocked = true; }
        }
        Check(blocked && prepared == 2 && captured == 2, "busy automation is never displaced by manual shop preparation");
        blocked = false;
        try { await DailyTradeCaptureConnection.RunAsync(root, host, host.Accounts[1].AccountKey, Prepare, Capture); }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked && prepared == 2, "wrong selected account is rejected before preparing or reading the shop");
        foreach (var change in new[] { "account", "process", "closed", "not-ready", "prepare-failed" })
        {
            var original = host.CurrentKey; var game = host.Game; blocked = false;
            try
            {
                await DailyTradeCaptureConnection.RunAsync(root, host, original, () =>
                {
                    if (change == "account") host.CurrentKey = host.Accounts[1].AccountKey;
                    if (change == "process") host.Game = game! with { StartTicks = game!.StartTicks + 1 };
                    if (change == "closed") host.Game = null;
                    if (change == "not-ready") host.Ready = false;
                    if (change == "prepare-failed") throw new TimeoutException("fixture preparation timeout");
                    return Task.CompletedTask;
                }, Capture);
            }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException) { blocked = true; }
            finally { host.CurrentKey = original; host.Game = game; host.Ready = true; }
            Check(blocked && captured == 2 && !DailyToolControl.IsOccupied(root), "preparation failure preserves capture and releases ownership: " + change);
        }
        blocked = false;
        string key = host.CurrentKey;
        try
        {
            await DailyTradeCaptureConnection.RunAsync(root, host, key, Prepare, (_, _) =>
            { host.CurrentKey = host.Accounts[1].AccountKey; return Task.FromResult("stale result"); });
        }
        catch (InvalidOperationException) { blocked = true; }
        finally { host.CurrentKey = key; }
        Check(blocked && !DailyToolControl.IsOccupied(root), "identity change during capture never exposes a previous account result");
        var reports = Directory.GetFiles(Path.Combine(root, "trade", "diagnostics"), "*.json").Select(DailyTradeCatalog.Read).ToArray();
        Check(reports.Any(r => S(r["phase"]) == "prepare" && S(r["error"]).Contains("fixture preparation timeout"))
            && reports.Any(r => S(r["phase"]) == "capture" && S(r["state"]) == "ready")
            && reports.All(r => S(r["transport"]) == "local-named-pipe" && N(r["gameplayActions"]) == 0),
            "capture diagnostics separate initialization failures from shop reads without gameplay");
    }
    private static void SetReading(JsonObject e, string id, string field, JsonNode value) => Rows(e["Readings"]).Single(r => S(r["Id"]) == id)["Values"]!.AsArray()
        .Single(v => S(v!["Path"]) == field)!["Json"] = value.ToJsonString();
    private static async Task Replay(string replay, string output, List<string> cases)
    {
        var original = DailyTradeCatalog.Read(Path.Combine(replay, "bargain-operation.json"));
        var e = original["after"]!.DeepClone().AsObject();
        var catalog = DailyTradeCatalog.Read(Path.Combine(replay, "planning-catalog.json"));
        var daily = DailyTradeCatalog.Read(Path.Combine(replay, "planning-state.identity.json"));
        var native = State(e, "trade.native", "$self");
        long at = N(e["AtUtcTicks"]);
        daily["FrameUtcTicks"] = at; daily["Guild"]!["ServerTicks"] = Copy(native["ServerTicks"]);
        foreach (string k in new[] { "AccountKey", "PlayerKey", "ProcessId", "ProcessStartTicks" }) daily[k] = Copy(e["Frame"]![k]);
        int reads = 0; double clock = 0; JsonObject? record = null;
        var result = await DailyTradeQuoteReadiness.ReadAsync(catalog, () => {
            var sample = e.DeepClone().AsObject();
            if (++reads > 1)
            {
                // Simulate refreshed product models using native quotes; later live UI text independently matched these prices.
                long shop = N(R(sample, "trade.shop_ui", ShopId));
                var offers = Rows(native["Offers"]).Where(r => N(r["Shop"]) == shop).ToDictionary(r => N(r["Product"]));
                var display = R(sample, "trade.shop_ui", "$items")!.AsArray();
                foreach (var row in Rows(display)) row[Price] = Copy(offers[N(row[Product])]["Price"]);
                SetReading(sample, "trade.shop_ui", "$items", display);
            }
            return Task.FromResult(new DailyTradeQuoteObservation(sample, daily.DeepClone().AsObject(), at));
        }, () => clock, ms => { clock += ms / 1000d; return Task.CompletedTask; }, r => record = r);
        if (reads != 2 || record == null || S(record["state"]) != "ready" || N(result.State["offers"]![0]!["price"]) != 10 || N(original["result"]!["potions_delta"]) != -10)
            throw new Exception("real bargain transition replay failed");
        DailyJson.Write(Path.Combine(output, "real-transition-replay.json"), O(("state", "passed"), ("reads", reads), ("quote_record", record), ("state_after", result.State), ("gameplay_actions", 0)));
        cases.Add("captured bargain response with ordinary 24 and discounted 10 recovers without replaying its confirmed 10-potion cost");
    }
}

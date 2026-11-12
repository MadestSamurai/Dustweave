using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TalentPotionCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action run, string name) { try { run(); } catch (Exception e) when (e is InvalidDataException or DailyPotionSupplyException) { cases.Add(name); return; } throw new Exception(name); }
        var p = new GeneralPreferences();
        Check(p.TalentPotionReserve == 0 && p.RefillTalentPotions && p.TalentPotionTarget == 5000 && !new TradePreferences().RefillCookingPotions, "fixed refill defaults on at 5000; cooking funding defaults off");
        Check(DailyTalentPotions.Check(100, 60, 40) == null, "consumption ending exactly at reserve allowed");
        Check(DailyTalentPotions.Check(100, 61, 40) != null, "one potion below reserve prevented");
        Check(DailyTalentPotions.Check(0, 0, 100) == null, "free actions allowed below reserve");
        Check(DailyTalentPotions.Check(0, 1, 0) != null, "default zero still detects exhausted potions");
        Check(DailyTalentPotions.Check(long.MaxValue, long.MaxValue, 1) != null, "large costs cannot overflow protection");
        p.RefillTalentPotions = false;
        Reject(() => DailyTalentPotions.PurchaseCount(p, 0, 1), "auto refill disabled prevents purchases");
        p = new() { RefillTalentPotions = true, TalentPotionReserve = 100, TalentPotionTarget = 1000 };
        Check(DailyTalentPotions.PurchaseCount(p, 80, 900) == 920, "refill restores target including reserve deficit");
        Reject(() => DailyTalentPotions.PurchaseCount(p, 80, 901), "batch exceeding configured target refused without repeated buys");
        var offer = O(("Type", 12), ("PriceType", 4), ("Price", 45), ("BasePrice", 50), ("Rate", 100), ("ReputationDiscount", 10), ("Remaining", 9999));
        var quote = O(("Offers", new[] { offer }));
        Check(N(DailyTalentPotions.DiscountedOffer(quote)["Price"]) == 45, "only actual weekly discounted potion quote accepted");
        foreach (var change in new (string, long)[] { ("Type", 5), ("PriceType", 1), ("ReputationDiscount", 0), ("Price", 50), ("Rate", 120), ("Remaining", 0) })
        {
            var invalid = quote.DeepClone().AsObject(); invalid["Offers"]![0]![change.Item1] = change.Item2;
            Reject(() => DailyTalentPotions.DiscountedOffer(invalid), "unsafe refill quote rejected: " + change.Item1);
        }
        var state = O(("potions", 80), ("potion_buy_limit", 9999), ("gold", 100000));
        var plan = DailyTalentPotions.PlanningState(state, p, quote);
        Check(N(plan["potions"]) == 0 && N(plan["potion_buy_limit"]) == 900 && N(state["potions"]) == 80, "ordinary planning preserves original target-limited fallback and excludes reserve");
        p.RefillTalentPotions = false;
        Check(N(DailyTalentPotions.PlanningState(state, p, quote)["potion_buy_limit"]) == 0, "trade cannot bypass global purchase switch");
        Check(N(DailyTalentPotions.PlanningState(state, p, quote, true)["potion_buy_limit"]) > 99999 && !B(DailyTalentPotions.PlanningState(state, p, quote, true)["can_bargain"]), "cooking funding is independent, supports native purchase batches, and cannot buy for bargaining alone");
        Check(N(DailyTalentPotions.PlanningState(state, p, new JsonObject(), true)["potion_buy_limit"]) == 0, "missing discount never becomes permission for full price pills");
        Check(DailyPotionSupply.CookingPolicy(p, 6000).TalentPotionTarget == 6100, "cooking funds actual need plus reserve independently of ordinary target");
        var funded = DailyTalentPotions.PlanningState(state, p, quote, true);
        Check(N(funded["potion_reserve_cost"]) == 900 && N(funded["gold"]) == 99100, "depleted reserve funding is included in upfront liquidity rather than charged after planning");
        var old = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject(); old["Schema"] = 16; old.Remove("General");
        old["Stages"]!["Guild"] = false;
        var migrated = DailyPreferences.Parse(old.ToJsonString());
        Check(migrated.Schema == 19 && !migrated.Stages.Guild && migrated.General.TalentPotionReserve == 0 && migrated.General.RefillTalentPotions, "old preference migration preserves tasks and adopts fixed refill");
        foreach (bool enabled in new[] { false, true })
        foreach (long target in new[] { 1000L, 8000L })
        {
            var prior = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
            prior["General"]!.AsObject().Remove("PotionSupplyRevision");
            prior["General"]!["RefillTalentPotions"] = enabled; prior["General"]!["TalentPotionTarget"] = target;
            var upgraded = DailyPreferences.Parse(prior.ToJsonString());
            Check(upgraded.General.RefillTalentPotions && upgraded.General.TalentPotionTarget == Math.Max(5000, target) && !upgraded.Trade.RefillCookingPotions, "one-time migration raises small targets and preserves larger targets: " + enabled + ":" + target);
            upgraded.General.RefillTalentPotions = false; upgraded.General.TalentPotionTarget = 700;
            var again = DailyPreferences.Parse(JsonSerializer.Serialize(upgraded));
            Check(!again.General.RefillTalentPotions && again.General.TalentPotionTarget == 700, "explicit opt-out and smaller target survive rereading: " + enabled + ":" + target);
        }
        var store = new DailyPreferenceStore(Path.Combine(output, "potion-settings"));
        string a = new('a', 64), b = new('b', 64); migrated.General = new() { TalentPotionReserve = 500, RefillTalentPotions = true, TalentPotionTarget = 2000 }; store.Save(a, migrated);
        Check(store.Read(a).General.TalentPotionTarget == 2000 && store.Read(b).General.TalentPotionReserve == 0, "potion policy isolated by account");
        Reject(() => new GeneralPreferences { TalentPotionReserve = 500, RefillTalentPotions = true, TalentPotionTarget = 500 }.Validate(), "target at reserve rejected");
        Check(DailyPotionLimit.Parse("potion_limit:100:61:40") == new DailyPotionLimit(100, 61, 40) && DailyPotionLimit.Parse("potion_limit:-1:2:3") == null, "native shortage receipt validated");
        Check(!DailyQueueRetry.CanSelect(new(DailyTalentPotions.Stage, "blocked", "")), "auxiliary refill cannot run independently of its daily task");
        using var h = new WorkflowHarness(Path.Combine(output, "potion-queue-gate"), []);
        h.Readings = () => [WorkflowCases.Reading("dispatch.currency", ("Catalyst", 100))];
        // Standalone/embedded tools have no delegate and never acquire the daily policy.
        await h.Driver.RequirePotionsAsync(9999);
        Check(h.Box.Commands.Count == 0, "tool session does not inherit daily potion policy");
        h.Driver.PotionPolicy = () => new() { TalentPotionReserve = 40 };
        await h.Driver.RequirePotionsAsync(60);
        DailyStepException? blocked = null;
        try { await h.Driver.RequirePotionsAsync(61); } catch (DailyStepException e) { blocked = e; }
        Check(blocked?.PotionLimit == new DailyPotionLimit(100,61,40) && !blocked.Submitted && !blocked.MayHaveDispatched && h.Box.Commands.Count == 0, "queue preflight shortage never dispatches or creates uncertain consumption");
        h.Page("DiscountPopupUI", "_objButtonOk");
        h.Box.State = "rejected"; h.Box.Dispatched = false; h.Box.Error = "potion_limit:100:61:40";
        blocked = null;
        try { await h.Driver.SendObservedAsync(O(("ui", "DiscountPopupUI"), ("field", "_objButtonOk"))); } catch (DailyStepException e) { blocked = e; }
        Check(blocked?.PotionLimit == new DailyPotionLimit(100,61,40) && blocked.Submitted && !blocked.MayHaveDispatched, "native pre-dispatch rejection retains shortage without uncertain replay");
        var sent = h.Box.Commands.Single();
        Check(B(sent["GuardTalentPotions"]) && N(sent["TalentPotionReserve"]) == 40, "daily reserve is carried to native final dispatch boundary");
        var proofs = DailyWorkflowRegistry.Proofs();
        Check(proofs.Single(x => x.Role == "trade.buy").StagesFor(O(("action", "queue_potion_refill"))).Contains("*"), "unsettled refill purchase blocks all consuming daily stages");
        await RefillBatches(output, Check);
        await RefillPreviewRecovery(output, Check);
        CookingBudget(Check);
    }

    private static void CookingBudget(Action<bool, string> check)
    {
        var catalog = O(("items", new[] { O(("id", 1), ("stack", 999)), O(("id", 2), ("stack", 999)), O(("id", 3), ("stack", 999)) }),
            ("recipes", new[] { O(("id", 10), ("output", 2), ("count", 1), ("potions", 3), ("materials", O(("1", 2)))), O(("id", 11), ("output", 3), ("count", 1), ("potions", 2), ("materials", O(("1", 1)))) }));
        var wallet = O(("items", O(("1", 20))), ("potions", 5), ("gold", 10000), ("protected", new JsonObject()));
        var all = DailyCustomCooking.SupplyPlan(catalog, [10, 11], wallet, 5, 0, 45, 999, [10, 11]);
        check(Rows(all["cooking"]).Length == 1 && N(all["required"]) == 30 && N(all["buy"]) == 30 && N(wallet["items"]!["1"]) == 20, "custom recipe order shares ingredients and keeps actual wallet immutable");
        var limited = DailyCustomCooking.SupplyPlan(catalog, [10, 11], wallet, 5, 9550, 45, 999, [10, 11]);
        check(N(limited["buy"]) == 9 && N(limited["cost"]) == 405, "custom cooking buys complete affordable recipe quantities while preserving gold");
        var locked = DailyCustomCooking.SupplyPlan(catalog, [10, 11], wallet, 5, 0, 45, 999, []);
        check(N(locked["buy"]) == 0, "locked recipes cannot reserve or buy pills");
        wallet["protected"] = O(("keep", O(("id", 1), ("count", 20))));
        check(N(DailyCustomCooking.SupplyPlan(catalog, [10], wallet, 5, 0, 45, 999, [10])["buy"]) == 0, "protected ingredients cannot fund cooking");
        wallet["protected"] = new JsonObject();
        wallet["storage"] = O(("slots", 1), ("used", 1), ("stacks", new[] { O(("id", 1), ("count", 20)) }));
        check(N(DailyCustomCooking.SupplyPlan(catalog, [10], wallet, 5, 0, 45, 999, [10])["buy"]) == 0, "full bag does not buy pills for unavailable output space");
        wallet["storage"]!["slots"] = 2;
        check(N(DailyCustomCooking.SupplyPlan(catalog, [11, 10], wallet, 5, 0, 45, 999, [10, 11])["required"]) == 40, "ordered custom recipes allocate shared slots only once");
    }

    private static async Task RefillPreviewRecovery(string output, Action<bool, string> check)
    {
        foreach (string scenario in new[] { "once", "twice", "exhausted", "gold", "discount", "stock", "balance", "cancelled", "identity", "pending", "later-batch" })
        {
            string root = Path.Combine(output, "potion-preview-" + scenario);
            long potions = 74817, gold = 5000000;
            int previews = 0, confirmations = 0, cancelled = 0;
            bool restored = false;
            var changed = new HashSet<string>();
            var policy = new GeneralPreferences { RefillTalentPotions = true, TalentPotionTarget = 100000, TalentPotionReserve = 1000 };
            using var h = new WorkflowHarness(root, DailyWorkflowRegistry.Proofs());
            DailyJson.Write(DailyTradePlan.SettingsPath(root, S(h.Context["actor"]![3])), O(("reserve_gold", 1000000)));
            h.Frame["Scene"] = "Map3009_001";
            h.Page("ShopUI", "_objBackButton");
            h.Readings = () => [
                WorkflowCases.Reading("trade.native", ("$self", O(("Offers", new[] { O(("Shop", 6), ("Product", 1), ("Type", 12), ("PriceType", 4),
                    ("BasePrice", 50), ("Rate", 100), ("Price", cancelled > 0 && scenario == "discount" ? 50 : 45),
                    ("ReputationDiscount", cancelled > 0 && scenario == "discount" ? 0 : 10),
                    ("Remaining", cancelled > 0 && scenario == "stock" ? 0 : scenario == "later-batch" ? 10000 : 99999)) })))),
                WorkflowCases.Reading("trade.currency", ("Gold", gold), ("Catalyst", potions)),
                WorkflowCases.Reading("dispatch.currency", ("Catalyst", potions)),
                WorkflowCases.Reading("trade.bargain", ("$self", false)),
                WorkflowCases.Reading("trade.inventory", ("Count", 1), ("$items", new JsonArray(O(("Key", "Food"), ("Value.Count", 0), ("Value.Values", new JsonArray()))))),
                WorkflowCases.Reading("trade.merchant", ("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", 7), ("$items", new JsonArray(O(("ὫὠὫὯὠὢὥὧὢὥὥ", "Shop"), ("ὭὩὪὯὯὬὧὠὠὬὠ", 1))))),
                WorkflowCases.Reading("trade.shop_ui", ("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", 7), ("ὯὣὡὫὨὫὡὧὩὬὠ", "Buy"))
            ];
            h.OnRead = () =>
            {
                int changes = scenario is "twice" or "later-batch" ? 2 : scenario == "exhausted" ? 3 : 1;
                if (restored || changed.Count >= changes || scenario == "later-batch" && confirmations == 0) return;
                var op = h.Business.Records(h.Context).FirstOrDefault(o => S(o["state"]) == "dispatching" && o["command_id"] == null && !changed.Contains(S(o["id"])));
                if (op == null) return;
                changed.Add(S(op["id"]));
                if (scenario == "identity") { h.Frame["AccountKey"] = new string('b', 64); return; }
                // Reproduce the live failure: same popup/product/quantity/price,
                // but its purchase label disappears after the ready preview.
                Rows(h.Frame["Surfaces"]).Single(r => S(r["Type"]) == "ShopPopupUI")["Text"] = new JsonArray("Talent Pill");
            };
            h.OnCommand = c =>
            {
                h.Time += .01;
                switch (S(c["Kind"]))
                {
                    case "trade_buy_preview":
                        previews++;
                        h.Page("ShopPopupUI");
                        h.Frame["Surfaces"]![0]!["Text"] = new JsonArray("Talent Pill", "Buy");
                        h.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "ShopUI"), ("InputReady", false)));
                        break;
                    case "trade_buy_confirm":
                        confirmations++;
                        long count = N(c["Items"]![2]);
                        check(potions + count <= policy.TalentPotionTarget && N(c["Items"]![3]) == 45, "replanned refill uses current deficit and discounted price: " + scenario);
                        h.Event("trade.buy", "request"); potions += count; gold -= count * 45;
                        if (scenario != "pending") h.Event("trade.buy", "response");
                        h.Page("ShopUI", "_objBackButton");
                        break;
                    case "back":
                        cancelled++; h.Page("ShopUI", "_objBackButton");
                        if (scenario == "gold") gold = 1100000;
                        if (scenario == "balance") potions += 1000;
                        if (scenario == "cancelled") h.Stopped = true;
                        break;
                    case "click": h.Page(S(c["UiToken"]) == "ShopUI" ? "QuickMenuUI" : "GameFieldDefaultUI", "_buttonClose"); break;
                    default: throw new Exception("Unexpected refill recovery action: " + S(c["Kind"]));
                }
            };
            Exception? failure = null;
            JsonObject? result = null;
            try { result = await new DailyTradeExecution(h.Workflow, new()).RefillPotions(0, policy); }
            catch (Exception error) { failure = error; }
            if (scenario is "once" or "twice" or "balance" or "later-batch")
            {
                if (failure != null) throw failure;
                check(potions == 100000 && confirmations == (scenario == "later-batch" ? 3 : 1) && previews == confirmations + cancelled,
                    "refill actually continues after preview changes without duplicate purchases: " + scenario);
                check(gold == 5000000 - (25183 - (scenario == "balance" ? 1000 : 0)) * 45 && N(result!["bought"]) == 25183 - (scenario == "balance" ? 1000 : 0),
                    "replanning retains exact cumulative cost and completed batches: " + scenario);
            }
            else if (scenario == "pending")
                check(failure != null && confirmations == 1 && previews == 2 && h.Business.Records(h.Context).Any(DailyManagedBusiness.Pending),
                    "lost purchase response keeps pending journal and never triggers another preview retry");
            else
            {
                check(failure != null && confirmations == 0, "no purchase after failed refill prerequisites or cancellation: " + scenario);
                if (scenario is "gold" or "discount" or "stock")
                    check(failure is DailyPotionSupplyException && previews == 1, "retry rechecks gold, discount and stock before another preview: " + scenario);
                if (scenario == "exhausted")
                {
                    check(failure is DailyPotionSupplyException { Code: "preview_unstable" } && previews == 3 && cancelled == 3 && !h.Business.Records(h.Context).Any(DailyManagedBusiness.Pending),
                        "three unsent cancelled previews stop bounded retry without an unresolved transaction");
                    restored = true;
                    h.Dispose(); // End the old synthetic owner before the restarted driver takes its lock.
                    using var restarted = new WorkflowHarness(root, DailyWorkflowRegistry.Proofs());
                    restarted.Frame["Scene"] = "Map3009_001";
                    restarted.Page("ShopUI", "_objBackButton");
                    restarted.Readings = h.Readings;
                    restarted.OnCommand = c =>
                    {
                        restarted.Time += .01;
                        switch (S(c["Kind"]))
                        {
                            case "trade_buy_preview":
                                restarted.Page("ShopPopupUI");
                                restarted.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "ShopUI"), ("InputReady", false)));
                                break;
                            case "trade_buy_confirm":
                                confirmations++;
                                restarted.Event("trade.buy", "request");
                                potions += N(c["Items"]![2]); gold -= N(c["Items"]![2]) * 45;
                                restarted.Event("trade.buy", "response"); restarted.Page("ShopUI", "_objBackButton");
                                break;
                            case "click": restarted.Page(S(c["UiToken"]) == "ShopUI" ? "QuickMenuUI" : "GameFieldDefaultUI", "_buttonClose"); break;
                            default: throw new Exception("Unexpected restarted refill action: " + S(c["Kind"]));
                        }
                    };
                    result = await new DailyTradeExecution(restarted.Workflow, new()).RefillPotions(0, policy);
                    check(potions == 100000 && confirmations == 1, "fresh driver and journal complete refill after persisted retry exhaustion");
                    restarted.Page("ShopUI", "_objBackButton");
                    result = await new DailyTradeExecution(restarted.Workflow, new()).RefillPotions(0, policy);
                    check(N(result["bought"]) == 0 && confirmations == 1, "repeated recovery at target never buys again");
                }
            }
        }
    }
    private static async Task RefillBatches(string output, Action<bool, string> check)
    {
        foreach (string scenario in new[] { "multiple", "single", "gold-reserve", "discount-lost", "stock-exhausted", "pending", "cancelled" })
        {
            using var h = new WorkflowHarness(Path.Combine(output, "potion-batches-" + scenario), DailyWorkflowRegistry.Proofs());
            // StageHost restores the original task's trade flag before invoking
            // the auxiliary refill; it does not own a normal optimizer session.
            h.Frame["Scene"] = "Map3009_001";
            h.Page("ShopUI", "_objBackButton");
            const long initial = 757972, initialGold = 37377739;
            long potions = initial, gold = initialGold;
            bool interrupted = false, restored = false;
            var quantities = new List<long>();
            var policy = new GeneralPreferences { RefillTalentPotions = true, TalentPotionReserve = 888888, TalentPotionTarget = 999999 };
            if (scenario == "single") { policy.TalentPotionReserve = initial; policy.TalentPotionTarget = initial + 99999; }
            DailyJson.Write(DailyTradePlan.SettingsPath(h.Root, S(h.Context["actor"]![3])), O(("reserve_gold", scenario == "gold-reserve" ? 30000000 : 1000000)));
            h.Readings = () =>
            {
                bool changed = quantities.Count > 0 && !restored;
                var offer = O(("Shop", 8), ("Product", 1), ("Item", 0), ("Type", 12), ("PriceType", 4),
                    ("Price", changed && scenario == "discount-lost" ? 50 : 45), ("BasePrice", 50), ("Rate", 100),
                    ("ReputationDiscount", changed && scenario == "discount-lost" ? 0 : 10),
                    ("Remaining", changed && scenario == "stock-exhausted" ? 0 : 99999));
                return [
                    WorkflowCases.Reading("trade.native", ("$self", O(("Offers", new[] { offer })))),
                    WorkflowCases.Reading("trade.currency", ("Gold", gold), ("Catalyst", potions)),
                    WorkflowCases.Reading("dispatch.currency", ("Catalyst", potions)),
                    WorkflowCases.Reading("trade.bargain", ("$self", false)),
                    WorkflowCases.Reading("trade.inventory", ("Count", 1), ("$items", new JsonArray(O(("Key", "Food"), ("Value.Count", 0), ("Value.Values", new JsonArray()))))),
                    WorkflowCases.Reading("trade.merchant", ("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", 7),
                        ("$items", new JsonArray(O(("ὫὠὫὯὠὢὥὧὢὥὥ", "Shop"), ("ὭὩὪὯὯὬὧὠὠὬὠ", 1))))),
                    WorkflowCases.Reading("trade.shop_ui", ("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", 7), ("ὯὣὡὫὨὫὡὧὩὬὠ", "Buy"))
                ];
            };
            h.OnRead = () =>
            {
                if (scenario == "cancelled" && !restored && quantities.Count == 1 && h.Business.Records(h.Context).Any(op => S(op["state"]) == "completed"))
                    h.Stopped = true;
            };
            h.OnCommand = c =>
            {
                h.Time += .01;
                switch (S(c["Kind"]))
                {
                    case "trade_buy_preview":
                        h.Page("ShopPopupUI");
                        h.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "ShopUI"), ("InputReady", false)));
                        break;
                    case "trade_buy_confirm":
                        long count = N(c["Items"]![2]);
                        check(count is > 0 and <= 99999 && N(c["Items"]![3]) == 45 && potions + count <= policy.TalentPotionTarget,
                            "refill respects native quantity, discount and target: " + scenario);
                        h.Event("trade.buy", "request");
                        quantities.Add(count); potions += count; gold -= count * 45;
                        if (scenario != "pending" || restored) h.Event("trade.buy", "response");
                        h.Page("ShopUI", "_objBackButton");
                        break;
                    case "click":
                        h.Page(S(c["UiToken"]) == "ShopUI" ? "QuickMenuUI" : "GameFieldDefaultUI", "_buttonClose");
                        break;
                    default: throw new Exception("Unexpected potion action: " + S(c["Kind"]));
                }
            };
            JsonObject? result = null;
            try { result = await new DailyTradeExecution(h.Workflow, new()).RefillPotions(99999, policy); }
            catch (Exception) when (scenario is not ("multiple" or "single")) { interrupted = true; }
            if (scenario == "gold-reserve")
            {
                check(interrupted && quantities.Count == 0 && gold == initialGold, "refill checks entire target cost against gold reserve before buying");
                continue;
            }
            if (scenario is not ("multiple" or "single"))
            {
                check(interrupted && quantities.SequenceEqual(new long[] { 99999 }), "interrupted refill stops after one batch: " + scenario);
                if (scenario == "pending")
                {
                    check(h.Business.Records(h.Context).Any(DailyManagedBusiness.Pending), "unconfirmed refill retains durable pending purchase");
                    var waiting = await h.Business.ReconcileAsync(h.Context);
                    check(waiting["trade_diagnostics"]!.AsArray().Count == 1 && quantities.Count == 1,
                        "queue reconciliation preserves the uncertain receipt without replaying it");
                    h.Event("trade.buy", "response");
                }
                restored = true; h.Stopped = false; h.Business.BeginAttempt();
                check((await h.Business.ReconcileAsync(h.Context))["unresolved"]!.AsArray().Count == 0, "restored refill can reconcile: " + scenario);
                await h.Business.ReplanInterruptedAsync(h.Context, "trade");
                result = await new DailyTradeExecution(h.Workflow, new()).RefillPotions(99999, policy);
            }
            check(potions == policy.TalentPotionTarget && gold == initialGold - (potions - initial) * 45,
                "refill reaches exact target with exact total cost: " + scenario);
            check(quantities.SequenceEqual(scenario == "single" ? new long[] { 99999 } : new long[] { 99999, 99999, 42029 }),
                "refill splits final remainder and never duplicates completed batches: " + scenario);
            var records = h.Business.Records(h.Context).ToArray();
            check(N(result!["balance"]) == potions && records.Count(op => S(op["state"]) == "completed") == quantities.Count - (scenario == "pending" ? 1 : 0)
                && records.All(op => S(op["state"]) == "completed" || scenario == "pending" && S(op["state"]) == "superseded"),
                "refill retains batch receipts without falsely confirming old uncertain purchases: " + scenario);
        }
    }
}

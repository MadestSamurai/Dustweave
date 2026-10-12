using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TalentPotionCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action run, string name) { try { run(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var p = new GeneralPreferences();
        Check(p.TalentPotionReserve == 0 && !p.RefillTalentPotions, "potion defaults preserve zero reserve and prohibit purchases");
        Check(DailyTalentPotions.Check(100, 60, 40) == null, "consumption ending exactly at reserve allowed");
        Check(DailyTalentPotions.Check(100, 61, 40) != null, "one potion below reserve prevented");
        Check(DailyTalentPotions.Check(0, 0, 100) == null, "free actions allowed below reserve");
        Check(DailyTalentPotions.Check(0, 1, 0) != null, "default zero still detects exhausted potions");
        Check(DailyTalentPotions.Check(long.MaxValue, long.MaxValue, 1) != null, "large costs cannot overflow protection");
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
        var state = O(("potions", 80), ("potion_buy_limit", 9999));
        var plan = DailyTalentPotions.PlanningState(state, p, quote);
        Check(N(plan["potions"]) == 0 && N(plan["potion_buy_limit"]) == 900 && N(state["potions"]) == 80, "planning excludes reserve without corrupting actual wallet");
        p.RefillTalentPotions = false;
        Check(N(DailyTalentPotions.PlanningState(state, p, quote)["potion_buy_limit"]) == 0, "trade cannot bypass global purchase switch");
        var old = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject(); old["Schema"] = 16; old.Remove("General");
        old["Stages"]!["Guild"] = false;
        var migrated = DailyPreferences.Parse(old.ToJsonString());
        Check(migrated.Schema == 18 && !migrated.Stages.Guild && migrated.General.TalentPotionReserve == 0 && !migrated.General.RefillTalentPotions, "schema migration leaves existing task choices and no-buy defaults intact");
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
    }
}

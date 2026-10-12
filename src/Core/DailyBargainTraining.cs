using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyBargainFault(string code, params object[] values) : Exception(code)
{
    public string Code => code;
    public object[] Values => values;
}

public sealed record DailyBargainPlan(DailyBargainCharacter Character, long Potions)
{
    public long Shortage => Math.Max(0, Character.Estimate - Potions);
    public bool CanStart => !Character.Full && Character.PerUse > 0 && Shortage == 0;
    public void Validate()
    {
        if (Character.Full) throw new DailyBargainFault(Character.Level >= Character.MaxLevel ? "bargain.maximum" : "bargain.upgrade");
        if (Character.PerUse <= 0) throw new DailyBargainFault("bargain.no_growth");
        if (Shortage > 0) throw new DailyBargainFault("bargain.shortage", Character.Estimate, Potions, Shortage);
    }
}

public sealed record DailyBargainProgress(DailyBargainCharacter Character, long Potions, int Rounds, long Spent);

// A manual tool, not a queue stage. Stop is deferred only while settling a
// confirmed round; a lost receipt is never treated as permission to bargain again.
public sealed class DailyBargainTraining(DailyWorkflow w, Func<bool> stopped, Action<string> report, Action<DailyBargainProgress> progress)
{
    const string ShopNpc = "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", MenuNpc = "ὬὣὨὤὯὪὪὮὠὥὨ.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ";
    long merchant;
    public bool Settling { get; private set; }
    Task<JsonObject> Evidence(params string[] more) => w.Evidence(new[] { DailyBargainProof.ReadId, "trade.currency" }.Concat(more).ToArray());
    Task Surface(string ui) => w.Wait(f => DailyNavigationDecision.Rows(f.Frame).Any(r => S(r["Type"]) == ui && DailyNavigationDecision.ReadyInput(r)), 25, "bargain.surface_timeout");
    void Merchant(JsonObject e) => Require(N(R(e, "trade.shop_ui", ShopNpc)) == merchant, "bargain.merchant_changed");
    async Task Menu()
    {
        if (!await w.Has("QuickMenuUI")) await w.Step("GameFieldDefaultUI", operation: "square_shop_interact", expect: "QuickMenuUI");
        await Surface("QuickMenuUI");
        Require(N(R(await Evidence("trade.quickmenu"), "trade.quickmenu", MenuNpc)) == merchant, "bargain.merchant_changed");
    }
    async Task Reconcile()
    {
        var types = DailyNavigationDecision.Types((await w.Observe()).Frame);
        foreach (var op in w.Business.Records(w.Context, DailyBargainProof.Role).Where(DailyManagedBusiness.Pending))
        {
            string state = S(op["state"]);
            if (state == "prepared" || state == "preview_ready" && !types.Contains("DiscountPopupUI"))
            {
                op["state"] = "rejected"; op["reconciliation"] = O(("method", "uncommitted_preview"), ("actions", 0)); w.Business.Save(op);
            }
            else if (state == "unknown")
            {
                try
                {
                    var after = await Evidence(); var events = w.Business.Events(op); var result = w.Business.Verify(op, events, after);
                    op["state"] = "completed"; op["result"] = result; op["after"] = after; op["events"] = events;
                    op["confirmed_at"] = w.Driver.UtcTicks; w.Business.Save(op);
                }
                catch (InvalidDataException) { /* Preserve the unresolved consuming attempt. */ }
            }
        }
        w.Business.RequireResolved(w.Context, DailyBargainProof.Role);
    }
    async Task CloseShop(bool success)
    {
        var before = await Evidence("trade.bargain");
        Require(B(R(before, "trade.bargain", "$self")) == success, "bargain.shop_result_changed");
        await w.Step("ShopUI", "_objBackButton");
        bool confirmed = false;
        double end = w.Time + 25;
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            var next = DailyTradeClose.Next(frame, success, confirmed);
            if (next == "confirm")
            {
                await w.Step("DiscountCancelPopupUI", "_objButtonOk", absent: "DiscountCancelPopupUI"); confirmed = true;
            }
            else if (next == "settling")
            {
                // A closed ShopUI can leave its last ShopOpenResponse discount flag
                // cached. Only verify our own exit, not a redundant normal shop open.
                var after = await Evidence();
                Identity(before, after);
                Require(!success || confirmed, "bargain.exit_unconfirmed");
                Require(JsonNode.DeepEquals(DailyBargainProof.State(before), DailyBargainProof.State(after)), "bargain.close_changed");
                return;
            }
            await w.Delay(100);
        }
        throw new DailyBargainFault("bargain.surface_timeout");
    }
    public async Task<string> Run(DailyBargainCharacter approved)
    {
        await Reconcile();
        var initial = await Evidence();
        var current = DailyBargainProof.Character(initial, approved.Character);
        approved.AssertSameTier(current);
        // A changed tier, increased cost or rolled-back XP requires a new preview.
        Require(current.Experience >= approved.Experience, "bargain.preview_changed");
        if (current.Full) return current.Level >= current.MaxLevel ? "bargain.maximum" : "bargain.upgrade";
        new DailyBargainPlan(current, N(DailyBargainProof.State(initial)["Catalyst"])).Validate();
        long budget = current.Estimate, spent = 0;
        int rounds = 0;
        progress(new(current, N(DailyBargainProof.State(initial)["Catalyst"]), rounds, spent));
        if (stopped()) return "bargain.stopped";
        report("bargain.travel");
        await new DailyTradeExecution(w, new JsonObject()).OpenShop();
        var shop = await Evidence("trade.shop_ui", "trade.bargain");
        Require(!B(R(shop, "trade.bargain", "$self")), "bargain.existing_discount");
        merchant = N(R(shop, "trade.shop_ui", ShopNpc));
        Require(merchant > 0, "bargain.merchant_changed");
        Settling = true;
        try { await CloseShop(false); } finally { Settling = false; }
        while (!stopped())
        {
            await Menu();
            JsonObject? row = null, before = null;
            double end = w.Time + 35;
            while (!stopped() && w.Time < end)
            {
                before = await Evidence("mainline.talent_rows");
                current = DailyBargainProof.Character(before, approved.Character); approved.AssertSameTier(current);
                if (current.Full) return "bargain.upgrade";
                var rows = Rows(R(before, "mainline.talent_rows", "$self")).Where(r => N(r["Kind"]) == 16 && N(r["Character"]) == approved.Character && N(r["Group"]) == approved.Group).ToArray();
                Require(rows.Length == 1, "bargain.character_unavailable");
                var candidate = rows[0];
                Require(S(candidate["Reason"]) == "", "bargain.character_unavailable");
                string gate = S(candidate["Gate"]);
                if (gate == "") { row = candidate; break; }
                Require(gate.StartsWith("talent_wait:", StringComparison.Ordinal), "bargain.character_unavailable");
                await w.Delay(100);
            }
            if (stopped()) break;
            Require(row != null && before != null, "bargain.surface_timeout");
            Require(spent + current.Cost <= budget, "bargain.budget_reached");
            Require(N(DailyBargainProof.State(before!)["Catalyst"]) >= current.Cost, "bargain.potions_changed");
            var op = w.Business.Create(w.Context, DailyBargainProof.Role, before!, O(("character", current.Character), ("npc", merchant), ("budget", budget)), O(("ui", "DiscountPopupUI"), ("field", "_objButtonOk")));
            Settling = true;
            try
            {
                await w.Business.PreviewAsync(op, w.Context, O(("ui", "QuickMenuUI"), ("operation", "trade_talent"), ("value", I(row!["Instance"])), ("expect", "DiscountPopupUI")));
                var preview = await Evidence("trade.discount_popup");
                Require(N(R(preview, "trade.discount_popup", "ὤὣὤὥὨὧὮὨὣὤὤ")) == current.Character && N(R(preview, "trade.discount_popup", "ὣὮὡὮὣὥὫὬὨὣὬ")) == merchant, "bargain.preview_changed");
                var skill = State(preview, "trade.discount_popup", "ὣὡὪὭὤὨὭὣὪὧὩ");
                Require(N(skill["id"]) == current.Skill && N(skill["classType"]) == 16 && N(skill["catalystValue"]) == current.Cost, "bargain.preview_changed");
                if (stopped())
                {
                    await w.Step("DiscountPopupUI", "_objButtonCancel", absent: "DiscountPopupUI");
                    op["state"] = "rejected"; w.Business.Save(op); break;
                }
                report("bargain.training");
                await w.Business.CommitAsync(op, w.Context, 45);
                rounds++; spent += N(op["result"]!["cost"]);
                current = DailyBargainProof.Character(op["after"]!.AsObject(), approved.Character);
                progress(new(current, N(DailyBargainProof.State(op["after"]!.AsObject())["Catalyst"]), rounds, spent));
                await Surface("ShopUI"); Merchant(await Evidence("trade.shop_ui"));
                await CloseShop(B(op["result"]!["success"]));
                if (current.Full) return "bargain.upgrade";
                if (N(op["result"]!["gain"]) == 0) return "bargain.no_growth";
            }
            finally { Settling = false; }
        }
        return "bargain.stopped";
    }
}

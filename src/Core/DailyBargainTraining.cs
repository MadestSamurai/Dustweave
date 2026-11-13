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

public sealed record DailyBargainProgress(DailyBargainCharacter Character, long Potions, int Rounds, long Spent)
{
    public DailyPracticeAchievement? Achievement {get;init;}
    public DailyPracticeAchievement[]? Achievements {get;init;}
}

// A manual tool, not a queue stage. Stop is deferred only while settling a
// confirmed round; a lost receipt is never treated as permission to bargain again.
public sealed class DailyBargainTraining(DailyWorkflow w, Func<bool> stopped, Action<string> report, Action<DailyBargainProgress> progress)
{
    const string ShopNpc = "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", MenuNpc = "ὬὣὨὤὯὪὪὮὠὥὨ.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ";
    long merchant;
    public bool Settling { get; private set; }
    public Action? Checkpoint { get; init; }
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
        var observed = await w.Observe();
        var types = DailyNavigationDecision.Types(observed.Frame);
        foreach (var op in w.Business.Records(w.Context, DailyBargainProof.Role).Where(DailyManagedBusiness.Pending))
        {
            string state = S(op["state"]);
            if (state == "preview_ready" && DailyBargainProof.Definition.PreviewOwner?.Invoke(op, observed) == true)
            {
                await w.Step("DiscountPopupUI", "_objButtonCancel", absent: "DiscountPopupUI");
                op["state"] = "rejected";
                op["reconciliation"] = O(("method", "cancel_uncommitted_preview"), ("actions", 1));
                w.Business.Save(op);
                observed = await w.Observe(); types = DailyNavigationDecision.Types(observed.Frame);
                continue;
            }
            if (state == "prepared" || state == "preview_ready" && !types.Contains("DiscountPopupUI") ||
                (state is "previewing" or "unknown_preview") && !types.Contains("DiscountPopupUI") &&
                DailyBargainProof.CanReplanAfterRestart(op, await Evidence(), observed.Frame))
            {
                op["state"] = "rejected"; op["reconciliation"] = O(("method", "uncommitted_preview"), ("actions", 0)); w.Business.Save(op);
            }
            else if (state is "unknown" or "dispatching")
            {
                var after = await Evidence();
                try
                {
                    var events = w.Business.Events(op); var result = w.Business.Verify(op, events, after);
                    op["state"] = "completed"; op["result"] = result; op["after"] = after; op["events"] = events;
                    op["confirmed_at"] = w.Driver.UtcTicks; w.Business.Save(op);
                }
                catch (InvalidDataException)
                {
                    // Full XP is not progress evidence in achievement mode.
                    // Adopt the cumulative count without claiming an old command succeeded.
                    bool advanced=false;
                    if(B(op["scope"]?["achievement"])){
                        try{
                            Identity(op["before"]!.AsObject(),after);
                            var a=DailyPracticeAchievement.From(op["before"]!.AsObject(),DailyBargainProof.ReadId,16);
                            advanced=a.Refresh(after,DailyBargainProof.ReadId).Current>a.Current;
                        }catch(Exception e)when(e is InvalidDataException or DailyBargainFault){}
                    }
                    if(advanced){
                        if(types.Contains("ShopUI")){
                            merchant=N(op["scope"]?["npc"]);var shop=await Evidence("trade.shop_ui","trade.bargain");Merchant(shop);
                            Settling=true;try{await CloseShop(B(R(shop,"trade.bargain","$self")));}finally{Settling=false;}
                        }
                        op["state"]="superseded";op["reconciliation"]=O(("method","current_achievement_progress"),("consuming_actions",0),("closed_shop",types.Contains("ShopUI")),("original_success_claimed",false),("saved_action_replayed",false),("current",after));w.Business.Save(op);
                        continue;
                    }
                    // A dead game's request cannot still be in flight in the newly
                    // authenticated process. Keep its outcome unknown, but plan from
                    // this process's actual XP/potions instead of replaying the action.
                    var current = await w.Observe();
                    if (!JsonNode.DeepEquals(current.Context, w.Context))
                        throw new StageHostException("identity", "bargain.account_changed");
                    if (!DailyBargainProof.CanReplanAfterRestart(op, after, current.Frame)) continue;
                    op["state"] = "superseded";
                    op["reconciliation"] = O(("method", "new_game_current_training_state"),
                        ("previous_state", state), ("actions", 0), ("original_success_claimed", false),
                        ("saved_action_replayed", false), ("at", w.Driver.UtcTicks), ("current", after));
                    w.Business.Save(op);
                }
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
                Require(new[]{"Characters","Catalyst","Gold"}.All(key=>JsonNode.DeepEquals(DailyBargainProof.State(before)[key],DailyBargainProof.State(after)[key])), "bargain.close_changed");
                return;
            }
            await w.Delay(100);
        }
        throw new DailyBargainFault("bargain.surface_timeout");
    }
    public async Task<string> Run(DailyBargainCharacter approved, DailyPracticeAchievement? achievement=null)
    {
        await Reconcile();
        var initial = await Evidence();
        var current = DailyBargainProof.Character(initial, approved.Character);
        approved.AssertSameTier(current);
        // A changed tier, increased cost or rolled-back XP requires a new preview.
        Require(current.Experience >= approved.Experience, "bargain.preview_changed");
        achievement=achievement?.Refresh(initial,DailyBargainProof.ReadId);
        if(achievement?.Remaining==0)return "practice.achievement_complete";
        if (achievement==null&&current.Full) return current.Level >= current.MaxLevel ? "bargain.maximum" : "bargain.upgrade";
        if(achievement==null)new DailyBargainPlan(current, N(DailyBargainProof.State(initial)["Catalyst"])).Validate();
        long budget = achievement==null?current.Estimate:checked(achievement.Remaining*current.Cost), spent = 0;
        int rounds = 0;
        progress(new(current, N(DailyBargainProof.State(initial)["Catalyst"]), rounds, spent){Achievement=achievement,Achievements=DailyPracticeAchievement.Read(DailyBargainProof.State(initial))});
        if (stopped()) return "bargain.stopped";
        report("bargain.travel");
        // Recovery may already have closed our discount and left this merchant's
        // talent menu open. Reuse it rather than reopening an unrelated shop.
        if(merchant>0&&await w.Has("QuickMenuUI"))await Menu();
        else{
            await new DailyTradeExecution(w, new JsonObject()).OpenShop();
            var shop = await Evidence("trade.shop_ui", "trade.bargain");
            Require(!B(R(shop, "trade.bargain", "$self")), "bargain.existing_discount");
            merchant = N(R(shop, "trade.shop_ui", ShopNpc));
            Require(merchant > 0, "bargain.merchant_changed");
            Settling = true;
            try { await CloseShop(false); } finally { Settling = false; }
        }
        while (!stopped())
        {
            Checkpoint?.Invoke();
            await Menu();
            JsonObject? row = null, before = null;
            double end = w.Time + 35;
            while (!stopped() && w.Time < end)
            {
                before = await Evidence("mainline.talent_rows");
                current = DailyBargainProof.Character(before, approved.Character); approved.AssertSameTier(current);
                if(achievement!=null){achievement=achievement.Refresh(before,DailyBargainProof.ReadId);if(achievement.Remaining==0)return "practice.achievement_complete";}
                else if (current.Full) return "bargain.upgrade";
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
            var op = w.Business.Create(w.Context, DailyBargainProof.Role, before!, O(("character", current.Character), ("npc", merchant), ("budget", budget),("achievement",achievement!=null),("achievementTarget",achievement?.Target??0)), O(("ui", "DiscountPopupUI"), ("field", "_objButtonOk")));
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
                await Surface("ShopUI"); Merchant(await Evidence("trade.shop_ui"));
                await CloseShop(B(op["result"]!["success"]));
                if(achievement!=null)achievement=await achievement.AfterUse(w,DailyBargainProof.ReadId);
                progress(new(current, N(DailyBargainProof.State(op["after"]!.AsObject())["Catalyst"]), rounds, spent){Achievement=achievement,Achievements=DailyPracticeAchievement.Read(DailyBargainProof.State(op["after"]!.AsObject()))});
                if(achievement?.Remaining==0)return "practice.achievement_complete";
                if (achievement==null&&current.Full) return "bargain.upgrade";
                if (achievement==null&&N(op["result"]!["gain"]) == 0) return "bargain.no_growth";
            }
            finally { Settling = false; }
        }
        return "bargain.stopped";
    }
}

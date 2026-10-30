using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed class DailyTradeExecution(DailyWorkflow w, JsonObject catalog)
{
    private const string NpcPath = "ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", MenuKind = "ὫὠὫὯὠὢὥὧὢὥὥ", ShopId = "ὭὩὪὯὯὬὧὠὠὬὠ", ShopNpc = "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", ShopMode = "ὯὣὡὫὨὫὡὧὩὬὠ";
    private Task<JsonObject> Evidence() => w.Evidence("trade", "dispatch");
    internal static long RequiredPotions(JsonObject plan, TradePreferences preferences) =>
        preferences.AutoCookAndSell ? N(plan["summary"]!["potions_used"]) : N(plan["bargain"]!["potions"]);
    private Task Surface(string name, double seconds = 30) => w.Wait(f => DailyNavigationDecision.Rows(f.Frame).Any(r => S(r["Type"]) == name && DailyNavigationDecision.ReadyInput(r)), seconds, "等待原生页面：" + name);
    private Task SettleShop() => w.Wait(f => !DailyNavigationDecision.Types(f.Frame).Overlaps(new[] { "ShopPopupUI", "BuyFavoritePopupUI", "RewardReceivePopupUI" }), 20, "成交已确认，等待原生弹窗结束");
    public static JsonObject Merchant(JsonObject e)
    {
        var merchants = Readings(e, "trade.merchant").Where(v => Rows(v["$items"] ?? new JsonArray()).Any(x => S(x[MenuKind]) == "Shop" && N(x[ShopId]) > 0 && N(x[ShopId]) is not (3001 or 3003 or 3010))).ToArray();
        Require(merchants.Length == 1, "广场商人缺失或不唯一");
        return merchants[0];
    }
    internal async Task Approach(long npc)
    {
        JsonObject? first = null, last = null;
        int attempts = 0, bookReturns = 0;
        async Task<JsonObject> Observe()
        {
            var f = (await w.Observe()).Frame;
            if (DailyTravel.SquareBookLobby(f))
            {
                if (bookReturns++ >= 2) throw new StageHostException("adapter", "广场入口反复打开末日之书；请离开入口后重试此环节。");
                await DailyTravel.LeaveSquareBookLobby(w, f);
                await DailyTravel.Ready(w, "GameFieldDefaultUI");
                // Recheck the same merchant before resuming only our travel command.
                var resumed = State(await w.Evidence("trade.navigation"), "trade.navigation", "$self");
                Require(N(resumed["Npc"]) == npc, "商人目标改变");
                if (attempts > 0) { await w.Step("GameFieldDefaultUI", operation: "square_shop_nav"); attempts++; }
                f = (await w.Observe()).Frame;
            }
            Require(S(f["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && DailyNavigationDecision.Blockers(f, "GameFieldDefaultUI", DailyNavigationPolicy.Load()).Length == 0, "商人导航被场景或弹窗中断");
            // Movement needs the merchant/path state, not the entire inventory,
            // recipes and dispatch tables on every navigation poll.
            var row = State(await w.Evidence("trade.navigation"), "trade.navigation", "$self");
            Require(N(row["Npc"]) == npc, "商人目标改变");
            return row;
        }
        async Task Stop()
        {
            var f = (await w.Observe()).Frame;
            if (S(f["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && DailyNavigationDecision.Types(f).Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(f, "GameFieldDefaultUI", DailyNavigationPolicy.Load()).Length == 0)
                await w.Step("GameFieldDefaultUI", operation: "square_cancel_nav");
        }
        try
        {
            first = last = await Observe();
            if (B(first["Near"])) return;
            if (!B(first["Reachable"]) || DailyRegionRouter.D(first["Distance"]) / DailyRouteAtlas.WalkSpeed > DailyQuickTravel.SameMapTransitionSeconds)
            {
                var quick = await w.Evidence("route.quick");
                var shortcut = DailyQuickTravel.Targets(quick).FirstOrDefault(t => N(t["Npc"]) == npc);
                if (shortcut != null && !B(shortcut["AtTarget"]) && await DailyQuickTravel.TryMove(w, npc, N(shortcut["Map"])))
                {
                    first = last = await Observe();
                    if (B(first["Near"])) return;
                }
            }
            if (w.Settings.General.FastTravel) await new DailyFieldRoute(w).PrepareWalking(DailyFieldRoute.TargetDistance(first));
            attempts = 1;
            await w.Step("GameFieldDefaultUI", operation: "square_shop_nav", reason:"使用 A* 前往广场商人");
            await DailySquareNavigation.Wait(w,"square_shop_nav",async()=> { last=await Observe(); return DailySquareNavigation.MerchantArrived((await w.Observe()).Frame,B(last["Near"])); });
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(w.Root, "live", "travel-diagnostics", "trade-" + w.Driver.UtcTicks + ".json"), O(("reason", e.Message), ("npc", npc), ("initial", first), ("last", last), ("attempts", attempts))); throw; }
        // A nearby merchant or a completed native shortcut has no walking
        // command to cancel. Keep cleanup for every attempted walk, including
        // a lost reply after the game may already have started moving.
        finally { if (attempts > 0) try { await Stop(); } catch (Exception e) { w.Save("trade-navigation-cleanup.json", O(("error", e.Message), ("at", w.Driver.UtcTicks))); } }
    }
    private async Task OpenMerchant(long expected)
    {
        if (await w.Has("ShopUI"))
            return;
        if (!await w.Has("QuickMenuUI"))
            await w.Step("GameFieldDefaultUI", "$pointer/Parent/InteractionInfo/Button - Int", expect: "QuickMenuUI");
        string? field = null;
        await w.WaitEvidence(["trade"], e => { if (N(R(e, "trade.quickmenu", "ὬὣὨὤὯὪὪὮὠὥὨ.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ")) != expected) throw new StageHostException("identity", "返回的商人与原交易不一致"); var matches = Readings(e, "trade.merchant_menu").Where(r => S(r["ὭὦὣὠὣὮὡὪὢὨὪ.ὫὠὫὯὠὢὥὧὢὥὥ"]) == "Shop").ToArray(); if (matches.Length != 1) return false; field = "$pointer/Parent/IntMenus/" + S(matches[0]["gameObject.name"]) + "/Image - menuBallon"; return true; }, 12, "商人原生商店菜单未就绪");
        await w.Target("QuickMenuUI", field!);
        await w.Step("QuickMenuUI", field, expect: "ShopUI");
    }
    public async Task OpenShop()
    {
        var f = (await w.Observe()).Frame;
        if (S(f["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && await w.Has("ShopUI"))
        {
            await SettleShop();
            var e = await Evidence();
            Require(N(R(e, "trade.shop_ui", ShopNpc)) == N(Merchant(e)[NpcPath]), "已打开商店不属于广场商人");
        }
        else
        {
            Require(await DailyTravel.Enter(w, 2, "square.pack", packType: 11), "广场卡带不可用");
            JsonObject? merchant = null;
            await w.WaitEvidence(["trade"], e => { merchant = Merchant(e); return S(e["Frame"]!["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal); }, 30, "广场商人尚未加载");
            long npc = N(merchant![NpcPath]);
            await Approach(npc);
            await w.Step("GameFieldDefaultUI", operation: "square_shop_interact", expect: "QuickMenuUI");
            await OpenMerchant(npc);
            await SettleShop();
        }
        await PrepareQuotePage();
        if (S(R(await Evidence(), "trade.shop_ui", ShopMode)) != "Buy")
            await w.Step("ShopUI", "_buyButton.root");
        await SettleShop();
    }
    internal async Task PrepareQuotePage(JsonObject? observed = null)
    {
        var e = observed ?? await Evidence();
        var frame = e["Frame"]!.AsObject();
        if (!DailyNavigationDecision.Types(frame).Contains("DiscountPopupUI")) return;
        // A native skill preview can arrive after the shop underneath it appears.
        // Cancel this unsubmitted preview, then plan from the live shop. Never
        // confirm it just to make a read succeed, or repeat a pending bargain.
        Require(S(frame["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) &&
            DailyNavigationDecision.Types(frame).Contains("ShopUI") &&
            N(R(e, "trade.shop_ui", ShopNpc)) == N(Merchant(e)[NpcPath]) &&
            N(State(e, "trade.discount_popup", DailyFieldRoute.SkillPath)["classType"]) == 16,
            "砍价预览与当前广场商人不一致，未操作。");
        if (w.Business.Records(w.Context, "dispatch.start").Any(op => S(op["scope"]?["_proof"]?["kind"]) == "bargain" && DailyManagedBusiness.Pending(op) && DailyWorkflowRegistry.Owned(op, new(frame, w.Context))))
            throw new StageHostException("pending", "上一笔砍价仍在确认，未重复使用天赋药。");
        if (DailyNavigationDecision.Blockers(frame, "DiscountPopupUI", DailyNavigationPolicy.Load()).Length != 0)
            throw new StageHostException("adapter", "砍价预览被其他窗口遮挡，请关闭额外窗口后继续跑商。");
        await w.Step("DiscountPopupUI", "_objButtonCancel", absent: "DiscountPopupUI", reason: "关闭未提交的砍价预览，先按当前库存计算采购计划");
        await PurchasePageReady();
    }
    internal Task PurchasePageReady() => w.Wait(f => DailyNavigationDecision.Rows(f.Frame).Any(r =>
        S(r["Type"]) == "ShopUI" && DailyNavigationDecision.ReadyInput(r, false)) &&
        DailyNavigationDecision.Blockers(f.Frame, "ShopUI", DailyNavigationPolicy.Load()).Length == 0,
        20, "等待砍价窗口关闭并返回商店；已确认的砍价不会重复执行");
    public async Task<JsonObject> Capture(string? output = null, bool recoverBargainPreview = false)
    {
        DailyTradeQuoteObservation? latest = null;
        var (state, proof) = await DailyTradeQuoteReadiness.ReadAsync(catalog, async () =>
        {
            var e = await Evidence();
            if (recoverBargainPreview && DailyNavigationDecision.Types(e["Frame"]!.AsObject()).Contains("DiscountPopupUI"))
            {
                await PrepareQuotePage(e);
                e = await Evidence();
            }
            var observed = await w.Observe();
            var daily = JsonSerializer.SerializeToNode(observed.Daily ?? throw new InvalidDataException("缺少当前账号快照"), DailyJson.Options)!.AsObject();
            return latest = new(e, daily, w.Driver.UtcTicks);
        }, () => w.Time, w.Delay, record => {
            string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(w.Root, "live", "trade-quote-reads");
            // Keep a small exportable explanation even when the full inventory
            // evidence exceeds the diagnostic archive's per-file size limit.
            DailyJson.Write(Path.Combine(directory, id + ".summary.json"), DailyTradeQuoteReadiness.Summary(record));
            DailyJson.Write(Path.Combine(directory, id + ".json"), record);
        });
        if (output != null)
        {
            DailyJson.Write(output, state);
            DailyJson.Write(Path.ChangeExtension(output, ".evidence.json"), latest!.Evidence);
            DailyJson.Write(Path.ChangeExtension(output, ".proof.json"), proof);
            DailyJson.Write(Path.ChangeExtension(output, ".identity.json"), latest.Daily);
        }
        return state;
    }

    private async Task Talent(int kind)
    {
        var e = await Evidence();
        var selected = Rows(e["Readings"]).Where(r => S(r["Id"]) == "dispatch.talent" && S(r["Error"]) == "").Select(r => (Row: r, Values: DailyEvidence.Values(r))).Where(x => N(x.Values[DailyFieldRoute.SkillPath]?["classType"]) == kind && !B(x.Values["_objDisableImage.activeSelf"])).OrderByDescending(x => N(x.Values[DailyFieldRoute.SkillPath]?["id"])).FirstOrDefault();
        Require(selected.Row != null, "没有可用的跑商天赋：" + kind);
        await w.Step("QuickMenuUI", operation: "trade_talent", value: I(selected.Row!["InstanceId"]));
    }
    private async Task<JsonObject> Bargain(JsonObject scope)
    {
        Require(!B(R(await Evidence(), "trade.bargain", "$self")), "砍价已开启，不重复消耗");
        if (await w.Has("ShopUI"))
            await w.Step("ShopUI", "_objBackButton", absent: "ShopUI");
        await Surface("QuickMenuUI");
        await Talent(16);
        await Surface("DiscountPopupUI");
        var skill = State(await Evidence(), "trade.discount_popup", DailyFieldRoute.SkillPath);
        await w.Driver.RequirePotionsAsync(N(skill["catalystValue"]));
        Require(N(skill["classType"]) == 16 && skill["valueList"]!.AsArray().Take(2).Select(N).SequenceEqual(new long[] { 100, 60 }) && N(skill["catalystValue"]) == 10, "预览不是必定60%砍价");
        scope["_proof"] = O(("kind", "bargain"), ("delta", O(("potions", -10))));
        var op = await w.Transact("dispatch.start", scope, O(("ui", "DiscountPopupUI"), ("field", "_objButtonOk")), 40);
        await PurchasePageReady();
        return op;
    }
    private async Task<JsonObject> PreviewCommit(string role, JsonObject scope, JsonObject preview, JsonObject confirm, double seconds = 40)
    {
        var op = w.Business.Create(w.Context, role, await Evidence(), scope, confirm);
        await w.Business.PreviewAsync(op, w.Context, preview);
        try { await w.Business.CommitAsync(op, w.Context, seconds); }
        catch (DailyStepException error) when (!error.Submitted && error.Kind=="rejected")
        {
            var current=(await w.Observe()).Frame;
            if(!DailyTradePreview.CanReplan(op,current,error))throw;
            await w.Step(S(confirm["ui"]),back:true,absent:S(confirm["ui"]),reason:"确认未发生交易，取消旧预览并按当前库存重新规划");
            op["state"]="superseded";op["recovery"]="unsent_preview_cancelled_replan_from_game";w.Business.Save(op);
            throw new DailyTradeReplanRequired("商品预览变化，已取消未提交预览并重新规划");
        }
        return op;
    }
    private async Task<JsonObject> Buy(JsonObject[] rows, bool favorite, JsonObject scope, bool existingFavoritesOnly = false)
    {
        long[] flat = rows.SelectMany(r => new[] { N(r["shop"]), N(r["product"]), N(r["count"]), N(r["price"]) }).ToArray();
        long cost = rows.Sum(r => N(r["count"]) * N(r["price"])), potions = 0;
        var delta = new JsonObject();
        foreach (var r in rows)
        {
            if (S(r["kind"]) == "potion")
                potions += N(r["count"]);
            else
            {
                string k = S(r["item"]);
                delta[k] = N(delta[k]) + N(r["count"]);
            }
        }
        string kind = favorite ? "favorite" : "buy", ui = favorite ? "BuyFavoritePopupUI" : "ShopPopupUI";
        scope["_proof"] = O(("kind", "buy"), ("delta", O(("gold", -cost), ("potions", potions), ("items", delta))), ("stock", rows.All(r => S(r["kind"]) != "potion") ? Array(rows) : new JsonArray()));
        var op = await PreviewCommit("trade.buy", scope, O(("ui", "ShopUI"), ("operation", "trade_" + kind + "_preview"), ("items", flat), ("value", cost), ("expect", ui), ("existing_favorites_only", existingFavoritesOnly)), O(("ui", ui), ("operation", "trade_" + kind + "_confirm"), ("items", flat), ("value", cost), ("existing_favorites_only", existingFavoritesOnly)));
        await w.Dismiss("ShopUI");
        await SettleShop();
        return op;
    }
    public async Task<JsonObject> RefillPotions(long required, GeneralPreferences policy)
    {
        // The quote exposes the native, current-cycle weekly-task discount. No cached
        // completion record, bargaining substitute or full-price fallback is allowed.
        await OpenShop();
        var e = await Evidence();
        var offer = DailyTalentPotions.DiscountedOffer(State(e, "trade.native", "$self"));
        long before = N(R(e, "trade.currency", "Catalyst"));
        long count = DailyTalentPotions.PurchaseCount(policy, before, required);
        Require(count <= N(offer["Remaining"]), "九折天赋药可购数量不足以补至目标，未购买；请调整补药目标。");
        long cost = checked(count * N(offer["Price"]));
        var tradeSettings = DailyTradeOptimizer.Preferences(DailyJson.TryRead<JsonObject>(DailyTradePlan.SettingsPath(w.Root, S(w.Context["actor"]![3]))));
        long reserveGold = N(tradeSettings["reserve_gold"]);
        Require(N(R(e, "trade.currency", "Gold")) - cost >= reserveGold, "金币不足，或购买会触及保留金币；未补药。");
        var op = await Buy([O(("kind", "potion"), ("shop", offer["Shop"]), ("product", offer["Product"]), ("count", count), ("price", offer["Price"]))], false, O(("action", "queue_potion_refill"), ("target", policy.TalentPotionTarget), ("reserve", policy.TalentPotionReserve)));
        var after = await Evidence();
        long balance = N(R(after, "trade.currency", "Catalyst"));
        Require(balance == before + count && DailyTalentPotions.Check(balance, required, policy.TalentPotionReserve) == null, "补药回执与库存不一致；保留记录，未重复购买。");
        await Close();
        return O(("bought", count), ("balance", balance), ("cost", cost), ("operation", op["id"]));
    }
    private async Task<JsonObject> Cook(JsonObject row, JsonObject scope, bool skipUnavailable = false)
    {
        if (await w.Has("CookingUI"))
            await w.Step("CookingUI", "_objBackButton", expect: "CookingSelectUI");
        await Surface("CookingSelectUI");
        await w.Step("CookingSelectUI", operation: "trade_cook_preview", value: I(row["recipe"]), expect: "CookingUI");
        var e = await Evidence();
        if (skipUnavailable && N(R(e, "trade.cooking_ui", "_craftMaterial.MaxCraftCount")) <= 0)
            return DailyWorkflow.Skipped("料理当前不可制作，未消耗材料");
        row = DailyTradeProof.Portion(row, Math.Min(N(row["count"]), N(R(e, "trade.cooking_ui", "_craftMaterial.MaxCraftCount"))));
        long packetLimit = N(R(e, "trade.cook_limit", "$self"));
        Require(packetLimit > 0, "Invalid native cooking packet limit");
        int packets = checked((int)((N(row["count"]) + packetLimit - 1) / packetLimit));
        scope["count"] = Copy(row["count"]);
        await w.Step("CookingUI", operation: "trade_cook_quantity", value: I(row["count"]));
        var delta = new JsonObject(row["materials"]!.AsObject().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(-N(p.Value)))));
        long outputCount = N(row["count"]) * N((skipUnavailable ? DailyCustomCooking.Recipes(catalog) : Rows(catalog["recipes"])).Single(r => N(r["id"]) == N(row["recipe"]))["count"]);
        delta[S(row["item"])] = outputCount;
        scope["_proof"] = O(("kind", "cook"), ("packets", packets), ("count", row["count"]), ("output_count", outputCount), ("item", row["item"]), ("delta", O(("potions", -N(row["potions"])), ("items", delta))));
        var op = w.Business.Create(w.Context, "trade.cook", await Evidence(), scope, O(("ui", "CookingUI"), ("operation", "trade_cook_confirm"), ("items", new[] { N(row["recipe"]), N(row["count"]) }), ("value", row["potions"])));
        int completed = 0;
        double progressAt = w.Time;
        async Task Heartbeat()
        {
            var now = await Evidence();
            var responses = DailyTradeProof.CookingResponses(op["before"]!.AsObject(), w.Business.Events(op), now, packets, false);
            if (responses.Length > completed)
            {
                completed = responses.Length;
                progressAt = w.Time;
                w.Save("trade-cooking-progress.json", O(("completed", completed), ("total", packets), ("at", w.Driver.UtcTicks)));
            }
            if (w.Time - progressAt >= 50)
                throw new StageHostException("pending", "整批料理没有继续结算，保留原回执，未重复制作");
        }
        await w.Business.CommitAsync(op, w.Context, Math.Min(1800, 50d * (packets + 1)), heartbeat: Heartbeat);
        await w.WaitEvidence(["trade"], state => !B(R(state, "trade.cooking_ui", "ὤὬὣὢὫὫὧὭὭὫὪ")), 35, "料理已核账，原生动画尚未结束");
        return op;
    }
    private async Task<JsonObject> Sell(JsonObject row, JsonObject scope)
    {
        long[] flat = [N(row["shop"]), N(row["item"]), N(row["instance"]), N(row["count"]), N(row["price"])];
        long value = N(row["count"]) * N(row["price"]);
        scope["_proof"] = O(("kind", "sell"), ("delta", O(("gold", value), ("items", O((S(row["item"]), -N(row["count"])))))));
        var op = await PreviewCommit("trade.sell", scope, O(("ui", "ShopUI"), ("operation", "trade_sell_preview"), ("items", flat), ("value", value), ("expect", "ShopPopupUI")), O(("ui", "ShopPopupUI"), ("operation", "trade_sell_confirm"), ("items", flat), ("value", value)));
        await w.Dismiss("ShopUI");
        await SettleShop();
        return op;
    }
    public async Task Close()
    {
        if (await w.Has("ShopUI"))
            await w.Step("ShopUI", "_objBackButton");
        if (await w.Has("CookingUI"))
            await w.Step("CookingUI", "_objBackButton", expect: "CookingSelectUI");
        if (await w.Has("CookingSelectUI"))
            await w.Step("CookingSelectUI", "_objBackButton", expect: "QuickMenuUI");
        if (await w.Has("QuickMenuUI"))
            await w.Step("QuickMenuUI", "_buttonClose", expect: "GameFieldDefaultUI");
        await w.Wait(f => DailyNavigationDecision.Types(f.Frame).Contains("GameFieldDefaultUI") && !DailyNavigationDecision.Types(f.Frame).Overlaps(new[] { "QuickMenuUI", "CookingUI", "CookingSelectUI", "ShopUI", "DiscountCancelPopupUI" }), 15, "已核账的交易菜单尚未关闭");
    }
    private async Task SettleForReplan()
    {
        // Do not interrupt the game's existing cooking batch, or confirm an old preview.
        if (await w.Has("CookingUI"))
        {
            await w.WaitEvidence(["trade"], e => R(e, "trade.cooking_ui", "ὤὬὣὢὫὫὧὭὭὫὪ") is JsonValue flag && flag.TryGetValue<bool>(out var busy) && !busy, 1800, "上一批料理仍在制作；完成后可按当前库存重算");
            await w.Step("CookingUI", "_objBackButton", expect: "CookingSelectUI");
        }
        if (await w.Has("CookingSelectUI")) await Close();
        foreach (string popup in new[] { "ShopPopupUI", "BuyFavoritePopupUI", "DiscountPopupUI" })
            if (await w.Has(popup)) await w.Step(popup, back: true, absent: popup, reason: "取消旧交易预览，按当前库存重算");
        if (await w.Has("RewardReceivePopupUI")) await w.Dismiss("ShopUI");
    }
    private static JsonObject Stable(JsonObject state)
    {
        var copy = state.DeepClone().AsObject();
        copy.Remove("captured_utc");
        copy.Remove("future_supply");
        return copy;
    }
    public async Task<JsonObject> Run()
    {
        try {
            for(int retry=0;;retry++){
                try { return await RunCore(); }
                catch(DailyTradeReplanRequired) when(retry<2) { }
            }
        }
        catch (Exception e)
        {
            w.Save("trade-failure.json", O(("error", e.Message), ("exception", e.ToString()), ("at", w.Driver.UtcTicks), ("context", w.Context)));
            throw;
        }
    }
    internal static JsonObject[] FavoritePurchases(JsonObject native)
    {
        var favorites = (native["Favorites"] as JsonArray ?? throw new InvalidDataException("商店收藏数据未就绪"))
            .Select(x => { var pair = x as JsonArray; Require(pair?.Count == 2, "商店收藏数据不完整"); return (N(pair![0]), N(pair[1])); }).ToHashSet();
        var available = (native["AvailableShops"] as JsonArray ?? throw new InvalidDataException("商店列表未就绪")).Select(N).ToHashSet();
        var offers = Rows(native["Offers"]).ToDictionary(x => (N(x["Shop"]), N(x["Product"])));
        var rows = new List<JsonObject>();
        foreach (var key in favorites.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            if (!available.Contains(key.Item1)) continue;
            Require(offers.TryGetValue(key, out var offer), "收藏商品暂时无法读取，未购买；请检查游戏内收藏列表");
            Require(N(offer!["Type"]) is 5 or 12 && N(offer["PriceType"]) == 4 && N(offer["Price"]) > 0 && N(offer["Remaining"]) >= 0, "收藏商品报价或币种不支持，未购买");
            if (N(offer["Remaining"]) == 0) continue;
            rows.Add(O(("shop", key.Item1), ("product", key.Item2), ("item", offer["Item"]), ("kind", N(offer["Type"]) == 12 ? "potion" : "food"), ("count", offer["Remaining"]), ("price", offer["Price"])));
        }
        return rows.ToArray();
    }
    private async Task<JsonObject> RunFavorites()
    {
        // Keep the purchase selection separate from the optimizer, but retain
        // the normal bargain and daily potion guard before reading final quotes.
        await SettleForReplan();
        await OpenShop();
        var evidence = await Evidence();
        var native = State(evidence, "trade.native", "$self");
        string session = Guid.NewGuid().ToString("N");
        var job = O(("id", session), ("state", "running"), ("phase", "purchase"), ("account", w.Context["actor"]![3]),
            ("context", O(("account", w.Context["actor"]![3]), ("player", w.Context["actor"]![4]), ("server", w.Context["server"]), ("cycle", w.Context["cycle"]))));
        using var closeSession = w.Driver.OwnTradeClose(job);
        async Task<JsonObject> Finish(string reason, long cost = 0, int products = 0)
        {
            // Called only with no purchase to submit or after Buy has reconciled
            // the purchase. An uncertain confirmation must never reach here.
            job["phase"] = "audit";
            await Close();
            job["phase"] = "cooking";
            var cooked = await CookFavorites(session);
            job["phase"] = "audit";
            return O(("state", products > 0 || cooked.Any(r => S(r?["state"]) == "completed") ? "completed" : "skipped"),
                ("reason", reason), ("gold_spent", cost), ("products", products), ("cooking", cooked), ("engine", "dotnet-trade-favorites-v2"));
        }
        var rows = FavoritePurchases(native);
        if (rows.Length == 0) return await Finish("收藏商品已购完、尚未解锁或未设置收藏；已检查自定义料理");
        if (!B(R(evidence, "trade.bargain", "$self")))
            await Bargain(O(("trade_session", session), ("action", "bargain")));
        // The pre-bargain quote can both overcharge and incorrectly reject an
        // affordable basket. Wait for the native discounted quote, then replan.
        await w.WaitEvidence(["trade"], e => B(R(e, "trade.bargain", "$self")) && B(State(e, "trade.native", "$self")["BargainActive"]), 12, "等待砍价后的商店报价");
        evidence = await Evidence();
        native = State(evidence, "trade.native", "$self");
        Require(B(native["BargainActive"]) && B(R(evidence, "trade.bargain", "$self")), "砍价状态已变化，请重新执行跑商");
        rows = FavoritePurchases(native);
        if (rows.Length == 0) return await Finish("收藏商品已购完、尚未解锁或未设置收藏；已检查自定义料理");
        long cost = rows.Aggregate(0L, (sum, r) => checked(sum + N(r["count"]) * N(r["price"])));
        if (cost > N(R(evidence, "trade.currency", "Gold")))
            return await Finish("金币不足以购买当前收藏商品；未购买，已按现有材料检查自定义料理");
        var scope = O(("trade_session", session), ("action", "existing-favorites"));
        var op = await Buy(rows, true, scope, existingFavoritesOnly: true);
        var result = await Finish("已砍价并购买收藏商品，按顺序检查自定义料理；未售卖，收藏保持不变", cost, rows.Length);
        result["operation"] = Copy(op["id"]);
        return result;
    }
    private async Task<JsonArray> CookFavorites(string session)
    {
        var results = new JsonArray();
        foreach (long id in w.Settings.Trade.CookingRecipes)
        {
            long made = 0;
            for (int batch = 0; batch < 100; batch++)
            {
                var evidence = await Evidence();
                var row = DailyCustomCooking.Next(catalog, id, DailyTradeResume.Wallet(evidence), w.Settings.General.TalentPotionReserve,
                    DailyCustomCooking.Available(catalog, evidence));
                if (row == null) break;
                if (!await w.Has("CookingUI") && !await w.Has("CookingSelectUI"))
                {
                    await w.Step("GameFieldDefaultUI", operation: "trade_menu", value: 7, expect: "QuickMenuUI");
                    await Talent(7);
                    await Surface("CookingSelectUI");
                }
                var op = await Cook(row, O(("trade_session", session), ("action", "custom-recipe:" + id + ":" + batch), ("recipe", id)), skipUnavailable: true);
                if (S(op["state"]) == "skipped") break;
                made += N(op["scope"]!["count"]);
            }
            var recipe = DailyCustomCooking.Recipes(catalog).SingleOrDefault(r => N(r["id"]) == id);
            string name = S(DailyCustomCooking.Items(catalog).SingleOrDefault(r => N(r["id"]) == N(recipe?["output"]))?["name"]);
            results.Add(O(("recipe", id), ("name", name), ("state", made > 0 ? "completed" : "skipped"), ("count", made),
                ("reason", made > 0 ? "按当前材料制作，成品已保留" : "配方或天赋未解锁、材料不足、库存已满或天赋药需保留")));
        }
        if (w.Settings.Trade.CookingRecipes.Length > 0) await Close();
        return results;
    }
    private async Task<JsonObject> RunCore()
    {
        if (!w.Settings.Trade.Enabled)
            return DailyWorkflow.Skipped("disabled");
        if (w.Settings.Trade.FavoritesOnly) return await RunFavorites();
        string account = S(w.Context["actor"]![3]), cycle = S(w.Context["cycle"]);
        Require(DailyProfiles.ValidKey(account) && cycle.Length > 0 && cycle.All(char.IsAsciiDigit), "Invalid trade account/cycle");
        string directory = Path.Combine(w.Root, "trade", "executions", account, cycle), path = Path.Combine(directory, "execution.json");
        Directory.CreateDirectory(directory);
        // Always plan from the game. Historical plans/receipts are diagnostic only.
        await SettleForReplan();
        await OpenShop();
        DailyTradeJournal.ArchivePlan(directory);
        JsonObject job;
        {
            string statePath = Path.Combine(directory, "planning-state.json");
            var state = await Capture(statePath, recoverBargainPreview: true);
            var supply = DailyTradeDropForecast.ForAccount(w, catalog, state);
            if (supply != null) state["future_supply"] = supply;
            DailyJson.Write(statePath, state);
            var settings = DailyTradeOptimizer.Preferences(DailyJson.TryRead<JsonObject>(DailyTradePlan.SettingsPath(w.Root, account)));
            DailyJson.Write(Path.Combine(directory, "planning-catalog.json"), catalog);
            DailyJson.Write(Path.Combine(directory, "planning-settings.json"), settings);
            var planningState = w.Driver.PotionPolicy == null ? state : DailyTalentPotions.PlanningState(state, w.Settings.General, State(await Evidence(), "trade.native", "$self"));
            planningState = planningState.DeepClone().AsObject();
            planningState["processing_enabled"] = w.Settings.Trade.AutoCookAndSell;
            var plan = await Task.Run(() => DailyTradePlan.Generate(catalog, planningState, settings, Path.Combine(directory, "plan.json"), account, w.Check));
            long requiredPotions = RequiredPotions(plan, w.Settings.Trade);
            // The optimizer still estimates future processing value for procurement.
            // Only fund the actions this run is allowed to perform.
            plan["execution"] = O(("auto_cook_and_sell", w.Settings.Trade.AutoCookAndSell),
                ("potions_required", requiredPotions), ("potions_to_buy", w.Settings.Trade.AutoCookAndSell
                    ? N(plan["summary"]!["potions_to_buy"]) : Math.Max(0, requiredPotions - N(planningState["potions"]))));
            long potionBudget = N(planningState["potions"]) + N(planningState["potion_buy_limit"]);
            plan["daily_potion_budget_reached"] = w.Settings.Trade.AutoCookAndSell && w.Driver.PotionPolicy != null &&
                requiredPotions >= potionBudget &&
                potionBudget < N(state["potions"]) + N(state["potion_buy_limit"]);
            await w.Observe();
            var e = await Evidence();
            var verifiedState = await Capture(recoverBargainPreview: true);
            Require(DailyTradeCatalog.Fingerprint(Stable(state)) == DailyTradeCatalog.Fingerprint(Stable(verifiedState)), "规划期间库存、供货或报价改变，请重新计算；未下单");
            job = O(("id", Guid.NewGuid().ToString("N")), ("state", "prepared"), ("account", account), ("context", state["context"]), ("catalog_hash", DailyTradeCatalog.Fingerprint(catalog)), ("npc", R(e, "trade.shop_ui", ShopNpc)), ("plan", plan), ("initial", DailyTradeResume.Wallet(e)), ("operations", new JsonArray()), ("phase", "purchase"), ("at", w.Driver.UtcTicks));
            DailyJson.Write(path, job);
        }
        Require(S(job["account"]) == account && S(job["catalog_hash"]) == DailyTradeCatalog.Fingerprint(catalog), "原交易账号或规则目录改变");
        foreach (var (key, value) in new[] { ("account", w.Context["actor"]![3]), ("player", w.Context["actor"]![4]), ("server", w.Context["server"]), ("cycle", w.Context["cycle"]) })
            Require(JsonNode.DeepEquals(job["context"]![key], value), "交易身份或供货周期改变");
        var operations = new List<JsonObject>();
        var planData = job["plan"]!.AsObject();
        void Record(JsonObject op)
        {
            operations.Add(op);
            job["operations"] = Array(operations.Select(o => o["id"]));
            DailyJson.Write(path, job);
        }
        bool Done(string key) => operations.Any(o => S(o["scope"]?["action"]) == key);
        JsonObject Scope(string key) => O(("trade_session", job["id"]), ("action", key));
        void Phase(string phase)
        {
            job["phase"] = phase;
            DailyJson.Write(path, job);
        }
        job["state"] = "running";
        DailyJson.Write(path, job);
        using var closeSession = w.Driver.OwnTradeClose(job);
        try
        {
            if (S(job["phase"]) == "purchase")
            {
                await w.Observe();
                long potionBuy = N(planData["execution"]!["potions_to_buy"]);
                if (potionBuy > 0 && !Done("potions"))
                {
                    if (w.Driver.PotionPolicy != null)
                    {
                        await w.Driver.RequirePotionsAsync(N(planData["execution"]!["potions_required"]));
                        throw new DailyTradeReplanRequired("天赋药余额变化，按当前库存重新规划。");
                    }
                    var n = State(await Evidence(), "trade.native", "$self");
                    Require(!B(n["BargainActive"]), "请先结束已有砍价以购买天赋药");
                    var offer = Rows(n["Offers"]).Where(r => N(r["Type"]) == 12).OrderBy(r => N(r["Price"])).First();
                    Record(await Buy([O(("kind", "potion"), ("shop", offer["Shop"]), ("product", offer["Product"]), ("count", potionBuy), ("price", offer["Price"]))], false, Scope("potions")));
                }
                if (B(planData["bargain"]!["start"]) && !B(R(await Evidence(), "trade.bargain", "$self")))
                {
                    Require(!Done("bargain"), "已执行砍价意外结束，保留剩余采购");
                    Record(await Bargain(Scope("bargain")));
                }
                string splitPath = Path.Combine(directory, "purchases-" + S(job["id"]) + ".json");
                if (!File.Exists(splitPath))
                    DailyJson.Write(splitPath, DailyTradeProof.Split(planData, await Capture(recoverBargainPreview: true)));
                var split = DailyTradeCatalog.Read(splitPath);
                var full = Rows(split["full"]);
                if (full.Length > 0 && !Done("favorites"))
                    Record(await Buy(full, true, Scope("favorites")));
                var partial = Rows(split["partial"]);
                for (int i = 0; i < partial.Length; i++)
                    if (!Done("partial:" + i))
                        Record(await Buy([partial[i]], false, Scope("partial:" + i)));
                Phase("cooking");
            }
            await ProcessStock(planData, job, directory, operations);
            await w.Observe();
            var final = await Evidence();
            job["final"] = DailyTradeResume.Wallet(final);
            job["state"] = "completed";
            job["completed_at"] = w.Driver.UtcTicks;
            job["gold_delta"] = N(job["final"]!["gold"]) - N(job["initial"]!["gold"]);
            job["potions_used"] = N(job["initial"]!["potions"]) - N(job["final"]!["potions"]);
            DailyJson.Write(path, job);
            DailyJson.Write(Path.Combine(directory, "final.evidence.json"), final);
            await Close();
            return O(("state", "completed"), ("reason", w.Settings.Trade.AutoCookAndSell ? "采购、烹饪及今日120%售卖已核账" : "采购已完成；自动料理与售卖未开启，食材与料理已保留"), ("record", path), ("gold_delta", job["gold_delta"]), ("potions_used", job["potions_used"]), ("operations", operations.Count), ("warnings", planData["warnings"]), ("daily_potion_budget_reached", planData["daily_potion_budget_reached"]), ("engine", "dotnet-trade-v1"));
        }
        catch (Exception e) { if (S(job["state"]) != "completed") job["state"] = "stopped"; job["error"] = e.Message; DailyJson.Write(path, job); throw; }
    }
    internal async Task ProcessStock(JsonObject planData, JsonObject job, string directory, List<JsonObject> operations)
    {
        string path = Path.Combine(directory, "execution.json");
        void Phase(string phase) { job["phase"] = phase; DailyJson.Write(path, job); }
        if (!w.Settings.Trade.AutoCookAndSell)
        {
            job["automatic_processing"] = "disabled";
            Phase("audit");
            return;
        }
        bool Done(string key) => operations.Any(o => S(o["scope"]?["action"]) == key);
        JsonObject Scope(string key) => O(("trade_session", job["id"]), ("action", key));
        void Record(JsonObject op)
        {
            operations.Add(op);
            job["operations"] = Array(operations.Select(o => o["id"]));
            DailyJson.Write(path, job);
        }
        if (S(job["phase"]) == "cooking")
        {
            await w.Observe();
            if (Rows(planData["cooking"]).Length > 0)
            {
                if (!await w.Has("CookingUI") && !await w.Has("CookingSelectUI"))
                {
                    await Close();
                    await w.Step("GameFieldDefaultUI", operation: "trade_menu", value: 7, expect: "QuickMenuUI");
                    await Talent(7);
                    await Surface("CookingSelectUI");
                }
                foreach (var row in DailyTradeProof.CookingOrder(planData, catalog))
                {
                    long completed = operations.Where(o => S(o["role"]) == "trade.cook" && N(o["scope"]?["recipe"]) == N(row["recipe"])).Sum(o => N(o["scope"]?["count"]));
                    while (completed < N(row["count"]))
                    {
                        var scope = Scope("recipe:" + S(row["recipe"]) + ":" + completed);
                        scope["recipe"] = Copy(row["recipe"]);
                        var op = await Cook(DailyTradeProof.Portion(row, N(row["count"]) - completed), scope);
                        Record(op);
                        completed += N(op["scope"]!["count"]);
                    }
                }
            }
            Phase("sale");
        }
        if (S(job["phase"]) == "sale")
        {
            await w.Observe();
            if (!await w.Has("ShopUI"))
            {
                await Close();
                await OpenMerchant(N(job["npc"]));
            }
            if (S(R(await Evidence(), "trade.shop_ui", ShopMode)) != "Sell")
                await w.Step("ShopUI", "_sellButton.root");
            string salePath = Path.Combine(directory, "sales-" + S(job["id"]) + ".json");
            if (!File.Exists(salePath))
                DailyJson.Write(salePath, DailyTradeProof.SaleStacks(planData, DailyTradeResume.Wallet(await Evidence()), catalog));
            var sales = Rows(JsonNode.Parse(File.ReadAllText(salePath)));
            for (int i = 0; i < sales.Length; i++)
                if (!Done("sale:" + i))
                    Record(await Sell(sales[i], Scope("sale:" + i)));
            Phase("audit");
        }
    }
}


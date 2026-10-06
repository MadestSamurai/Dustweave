using System.Text.Json;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public sealed class DailyTradeExecution(DailyWorkflow w, JsonObject catalog)
{
    private const string NpcPath = "ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", MenuKind = "ὫὠὫὯὠὢὥὧὢὥὥ", ShopId = "ὭὩὪὯὯὬὧὠὠὬὠ", ShopNpc = "ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", ShopMode = "ὯὣὡὫὨὫὡὧὩὬὠ";
    private Task<JsonObject> Evidence() => w.Evidence("trade", "dispatch");
    private Task Surface(string name, double seconds = 30) => w.Wait(f => DailyNavigationDecision.Rows(f.Frame).Any(r => S(r["Type"]) == name && DailyNavigationDecision.ReadyInput(r)), seconds, "等待原生页面：" + name);
    private Task SettleShop() => w.Wait(f => !DailyNavigationDecision.Types(f.Frame).Overlaps(new[] { "ShopPopupUI", "BuyFavoritePopupUI", "RewardReceivePopupUI" }), 20, "成交已确认，等待原生弹窗结束");
    public static JsonObject Merchant(JsonObject e)
    {
        var merchants = Readings(e, "trade.merchant").Where(v => Rows(v["$items"] ?? new JsonArray()).Any(x => S(x[MenuKind]) == "Shop" && N(x[ShopId]) > 0 && N(x[ShopId]) is not (3001 or 3003 or 3010))).ToArray();
        Require(merchants.Length == 1, "广场商人缺失或不唯一");
        return merchants[0];
    }
    private async Task Approach(long npc)
    {
        JsonObject? first = null, last = null;
        int attempts = 0;
        async Task<JsonObject> Observe()
        {
            var f = (await w.Observe()).Frame;
            Require(S(f["Scene"]).StartsWith("Map3009_", StringComparison.Ordinal) && DailyNavigationDecision.Blockers(f, "GameFieldDefaultUI", DailyNavigationPolicy.Load()).Length == 0, "商人导航被场景或弹窗中断");
            var row = State(await Evidence(), "trade.navigation", "$self");
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
            await w.Step("GameFieldDefaultUI", operation: "square_shop_nav", reason:"使用 A* 前往广场商人");
            attempts = 1;
            await DailySquareNavigation.Wait(w,"square_shop_nav",async()=> { last=await Observe(); return DailySquareNavigation.MerchantArrived((await w.Observe()).Frame,B(last["Near"])); });
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(w.Root, "live", "travel-diagnostics", "trade-" + w.Driver.UtcTicks + ".json"), O(("reason", e.Message), ("npc", npc), ("initial", first), ("last", last), ("attempts", attempts))); throw; }
        finally { try { await Stop(); } catch (Exception e) { w.Save("trade-navigation-cleanup.json", O(("error", e.Message), ("at", w.Driver.UtcTicks))); } }
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
        if (S(R(await Evidence(), "trade.shop_ui", ShopMode)) != "Buy")
            await w.Step("ShopUI", "_buyButton.root");
        await SettleShop();
    }
    public async Task<JsonObject> Capture(string? output = null)
    {
        DailyTradeQuoteObservation? latest = null;
        var (state, proof) = await DailyTradeQuoteReadiness.ReadAsync(catalog, async () =>
        {
            var e = await Evidence();
            var observed = await w.Observe();
            var daily = JsonSerializer.SerializeToNode(observed.Daily ?? throw new InvalidDataException("缺少当前账号快照"), DailyJson.Options)!.AsObject();
            return latest = new(e, daily, w.Driver.UtcTicks);
        }, () => w.Time, w.Delay, record => DailyJson.Write(Path.Combine(w.Root, "live", "trade-quote-reads", Guid.NewGuid().ToString("N") + ".json"), record));
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
        Require(N(skill["classType"]) == 16 && skill["valueList"]!.AsArray().Take(2).Select(N).SequenceEqual(new long[] { 100, 60 }) && N(skill["catalystValue"]) == 10, "预览不是必定60%砍价");
        scope["_proof"] = O(("kind", "bargain"), ("delta", O(("potions", -10))));
        var op = await w.Transact("dispatch.start", scope, O(("ui", "DiscountPopupUI"), ("field", "_objButtonOk")), 40);
        await Surface("ShopUI");
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
    private async Task<JsonObject> Buy(JsonObject[] rows, bool favorite, JsonObject scope)
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
        var op = await PreviewCommit("trade.buy", scope, O(("ui", "ShopUI"), ("operation", "trade_" + kind + "_preview"), ("items", flat), ("value", cost), ("expect", ui)), O(("ui", ui), ("operation", "trade_" + kind + "_confirm"), ("items", flat), ("value", cost)));
        await w.Dismiss("ShopUI");
        await SettleShop();
        return op;
    }
    private async Task<JsonObject> Cook(JsonObject row, JsonObject scope)
    {
        if (await w.Has("CookingUI"))
            await w.Step("CookingUI", "_objBackButton", expect: "CookingSelectUI");
        await Surface("CookingSelectUI");
        await w.Step("CookingSelectUI", operation: "trade_cook_preview", value: I(row["recipe"]), expect: "CookingUI");
        var e = await Evidence();
        row = DailyTradeProof.Portion(row, Math.Min(N(row["count"]), N(R(e, "trade.cooking_ui", "_craftMaterial.MaxCraftCount"))));
        long packetLimit = N(R(e, "trade.cook_limit", "$self"));
        Require(packetLimit > 0, "Invalid native cooking packet limit");
        int packets = checked((int)((N(row["count"]) + packetLimit - 1) / packetLimit));
        scope["count"] = Copy(row["count"]);
        await w.Step("CookingUI", operation: "trade_cook_quantity", value: I(row["count"]));
        var delta = new JsonObject(row["materials"]!.AsObject().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(-N(p.Value)))));
        long outputCount = N(row["count"]) * N(Rows(catalog["recipes"]).Single(r => N(r["id"]) == N(row["recipe"]))["count"]);
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
    private async Task<JsonObject> RunCore()
    {
        if (!w.Settings.Trade.Enabled)
            return DailyWorkflow.Skipped("disabled");
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
            var state = await Capture(statePath);
            var settings = DailyTradeOptimizer.Preferences(DailyJson.TryRead<JsonObject>(DailyTradePlan.SettingsPath(w.Root, account)));
            DailyJson.Write(Path.Combine(directory, "planning-catalog.json"), catalog);
            DailyJson.Write(Path.Combine(directory, "planning-settings.json"), settings);
            var plan = await Task.Run(() => DailyTradePlan.Generate(catalog, state, settings, Path.Combine(directory, "plan.json"), account, w.Check));
            await w.Observe();
            var e = await Evidence();
            var verifiedState = await Capture();
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
        try
        {
            if (S(job["phase"]) == "purchase")
            {
                await w.Observe();
                long potionBuy = N(planData["summary"]!["potions_to_buy"]);
                if (potionBuy > 0 && !Done("potions"))
                {
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
                    DailyJson.Write(splitPath, DailyTradeProof.Split(planData, await Capture()));
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
            return O(("state", "completed"), ("reason", "采购、烹饪及今日120%售卖已核账"), ("record", path), ("gold_delta", job["gold_delta"]), ("potions_used", job["potions_used"]), ("operations", operations.Count), ("warnings", planData["warnings"]), ("engine", "dotnet-trade-v1"));
        }
        catch (Exception e) { if (S(job["state"]) != "completed") job["state"] = "stopped"; job["error"] = e.Message; DailyJson.Write(path, job); throw; }
    }
}


using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TradeReplanCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var settings = new DailyPreferences();
        Check(!settings.Trade.FavoritesOnly,"favorites-only trading is off by default");
        settings.Trade.FavoritesOnly=true;
        Check(DailyPreferences.Parse(System.Text.Json.JsonSerializer.Serialize(settings)).Trade.FavoritesOnly,"favorites-only preference survives account serialization");
        JsonObject Offer(int shop,int product,int left=10)=>O(("Shop",shop),("Product",product),("Item",product+1000),("Type",5),("PriceType",4),("Price",20),("Remaining",left));
        var native=O(("AvailableShops",new[]{1,2}), ("Favorites",new[]{new[]{1,10},new[]{2,20},new[]{2,21},new[]{3,30}}),
            ("Offers",new JsonArray(Offer(1,10),Offer(1,11),Offer(2,20,7),Offer(2,21,0))));
        var favoriteRows=DailyTradeExecution.FavoritePurchases(native);
        Check(favoriteRows.Select(x=>N(x["product"])).SequenceEqual(new long[]{10,20})&&favoriteRows.Sum(x=>N(x["count"]))==17,
            "favorite purchase spans unlocked shops, excludes non-favorites, exhausted stock and locked chapters");
        native["Favorites"]=new JsonArray(new JsonArray(1,99));
        bool refused=false;try{DailyTradeExecution.FavoritePurchases(native);}catch(InvalidDataException){refused=true;}
        Check(refused,"missing favorite quote cannot silently become a different purchase");
        foreach (string scenario in new[] { "new", "active", "empty", "empty-active", "poor", "stale-plan", "potion-reserve", "failed", "late-quote", "purchase-pending" })
        using (var buy = new WorkflowHarness(Path.Combine(output,"favorites-flow-"+scenario), DailyWorkflowRegistry.Proofs(), configureIdentity: (_, context) => context["cycle"]="123"))
        {
            buy.Workflow.Settings.Trade.Enabled=true; buy.Workflow.Settings.Trade.FavoritesOnly=true;
            // Match the stage host: both native talent preview and managed shop
            // close must run, not the raw navigation-only test shortcut.
            buy.Driver.TradeStageActive=true;
            buy.Frame["Scene"]="Map3009_001";
            buy.Page("ShopUI", "_objBackButton");
            bool purchased=false, discounted=scenario is "active" or "empty-active", quoteReady=discounted;
            long gold=scenario=="poor"?150:250, potions=100;
            int bargains=0, closes=0;
            string oldJob=Path.Combine(buy.Root,"trade","executions",S(buy.Context["actor"]![3]),S(buy.Context["cycle"]),"execution.json");
            if(scenario=="stale-plan") { Directory.CreateDirectory(Path.GetDirectoryName(oldJob)!); File.WriteAllText(oldJob,"obsolete invalid plan"); }
            buy.Driver.PotionPolicy=()=>new() { TalentPotionReserve=scenario=="potion-reserve"?95:0 };
            JsonObject PricedOffer(int product) { var row=Offer(1,product,purchased||scenario is "empty" or "empty-active"?0:10); row["Price"]=quoteReady?20:50; return row; }
            var favorites = new JsonArray(new JsonArray(1,10));
            buy.Readings = () => [
                WorkflowCases.Reading("trade.native",("$self",O(("AvailableShops",new[]{1}), ("Favorites",favorites), ("BargainActive",quoteReady), ("Offers",new JsonArray(PricedOffer(10),PricedOffer(11)))))),
                WorkflowCases.Reading("trade.currency",("Gold",gold),("Catalyst",potions)),
                WorkflowCases.Reading("dispatch.currency",("Catalyst",potions)),
                WorkflowCases.Reading("trade.bargain",("$self",discounted)),
                WorkflowCases.Reading("mainline.talent_rows",("$self",new[]{O(("Kind",16),("Character",10),("Group",605),("Instance",0),("Gate",""),("Reason",""))})),
                WorkflowCases.Reading("dispatch.talent",(DailyFieldRoute.SkillPath,O(("id",6055),("classType",16))),("_objDisableImage.activeSelf",false)),
                WorkflowCases.Reading("trade.discount_popup",(DailyFieldRoute.SkillPath,O(("id",6055),("classType",16),("catalystValue",10),("valueList",new[]{100,60})))),
                WorkflowCases.Reading("trade.inventory",("Count",1),("$items",new JsonArray(O(("Key","Food"),("Value.Count",purchased?1:0),("Value.Values",purchased?new JsonArray(O(("id",1010),("type",5),("count",10),("keepFlag",0),("invenIndex","1"))):new JsonArray())))))
            ];
            // Supply merchant and shop properties exactly as the native adapter exposes them.
            var readings=buy.Readings;
            buy.Readings=()=>readings().Where(x=>S(x["Id"])!="trade.merchant").Concat(new[]{
                WorkflowCases.Reading("trade.merchant",("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ",7),("$items",new JsonArray(O(("ὫὠὫὯὠὢὥὧὢὥὥ","Shop"),("ὭὩὪὯὯὬὧὠὠὬὠ",1))))),
                WorkflowCases.Reading("trade.shop_ui",("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ",7),("ὯὣὡὫὨὫὡὧὩὬὠ","Buy"))}).ToArray();
            buy.OnCommand=c=>{
                buy.Time+=.01;
                switch(S(c["Kind"]))
                {
                    case "trade_talent": buy.Page("DiscountPopupUI","_objButtonOk");break;
                    case "trade_favorite_preview":
                        Check(B(c["ExistingFavoritesOnly"]),"favorite preview preserves the saved selection");
                        Check(discounted&&quoteReady&&N(c["Value"])==200,"favorites use fresh discounted prices, including when the original basket was unaffordable");
                        buy.Page("BuyFavoritePopupUI");buy.Frame["Surfaces"]!.AsArray().Add(O(("Id",2),("Type","ShopUI"),("InputReady",false)));break;
                    case "trade_favorite_confirm":
                        Check(B(c["ExistingFavoritesOnly"]),"confirmation revalidates the user's existing favorites");
                        buy.Event("trade.buy","request");purchased=true;gold-=200;
                        if(scenario!="purchase-pending")buy.Event("trade.buy","response");
                        buy.Page("ShopUI","_objBackButton");break;
                    case "click":
                        if(S(c["UiToken"])=="DiscountPopupUI") {
                            bargains++;buy.Event("dispatch.start","request");potions-=10;
                            discounted=scenario!="failed";quoteReady=discounted&&scenario!="late-quote";
                            buy.Event("dispatch.start","response",("IsSuccess",discounted));buy.Page("ShopUI","_objBackButton");
                        } else if(S(c["UiToken"])=="ShopUI") {
                            closes++;
                            if(discounted)buy.Page("DiscountCancelPopupUI","_objButtonOk");
                            else buy.Page("QuickMenuUI","_buttonClose");
                        } else if(S(c["UiToken"])=="DiscountCancelPopupUI")buy.Page("QuickMenuUI","_buttonClose");
                        else buy.Page("GameFieldDefaultUI");
                        break;
                    default: throw new Exception("Unexpected operation in favorites-only flow: "+S(c["Kind"]));
                }
            };
            buy.OnDelay=()=>{if(discounted&&scenario=="late-quote")quoteReady=true;};
            JsonObject? outcome=null;Exception? failure=null;
            try { outcome=await new DailyTradeExecution(buy.Workflow,new JsonObject()).Run(); } catch(Exception e) { failure=e; }
            if(scenario=="potion-reserve")
                Check(failure is DailyStepException { PotionLimit: not null }&&!purchased&&bargains==0&&potions==100,"favorites preserve the daily potion reserve before bargaining or buying");
            else if(scenario=="failed")
                Check(failure!=null&&!purchased&&bargains==1&&gold==250,"failed bargain never buys at full price or repeats consumption");
            else if(scenario is "empty" or "empty-active")
                Check(failure==null&&S(outcome?["state"])=="skipped"&&!purchased&&bargains==0&&potions==100&&closes==1,"empty favorites close without spending bargaining potions: "+scenario);
            else if(scenario=="poor")
                Check(failure==null&&S(outcome?["state"])=="skipped"&&!purchased&&bargains==1&&gold==150&&closes==2,"insufficient discounted basket closes its own bargain without a purchase");
            else if(scenario=="purchase-pending")
                Check(failure!=null&&purchased&&closes==1&&buy.Box.Commands.Count(c=>S(c["Kind"])=="trade_favorite_confirm")==1,"uncertain purchase is not repeated or dismissed as completed");
            else {
                if(failure!=null)throw failure;
                Check(S(outcome?["state"])=="completed"&&purchased&&gold==50&&bargains==(scenario=="active"?0:1)&&potions==(scenario=="active"?100:90),"favorites bargain once or reuse the active discount, then buy exactly once: "+scenario);
            }
            Check(!buy.Box.Commands.Any(c=>S(c["Kind"]).Contains("cook")||S(c["Kind"]).Contains("sell")),"favorites-only never cooks or sells: "+scenario);
            Check(scenario=="stale-plan"?File.ReadAllText(oldJob)=="obsolete invalid plan":!File.Exists(oldJob),"favorites do not require or overwrite a normal optimizer plan: "+scenario);
            if(scenario=="new")
            {
                discounted=quoteReady=false;
                buy.Page("ShopUI","_objBackButton");
                var again=await new DailyTradeExecution(buy.Workflow,new JsonObject()).Run();
                Check(S(again["state"])=="skipped"&&bargains==1&&gold==50,"second run reads exhausted favorites without another bargain or purchase");
            }
            buy.Page("ShopUI","_objBackButton");
            int beforeUnowned=buy.Box.Commands.Count; bool unowned=false;
            try { await buy.Driver.SubmitAsync(O(("ui","ShopUI"),("field","_objBackButton"))); }
            catch(DailyStepException e) when(e.Kind=="rejected") { unowned=true; }
            Check(unowned&&buy.Box.Commands.Count==beforeUnowned,"active trade ownership is released on success, skip or failure: "+scenario);
        }
        using var f = new WorkflowHarness(Path.Combine(output, "trade-current-state"), DailyWorkflowRegistry.Proofs());
        var before = WorkflowCases.Evidence();
        before["Frame"] = f.Frame.DeepClone(); before["Taps"] = new JsonArray("trade.buy", "dispatch.start");
        foreach (string role in new[] { "trade.buy", "dispatch.start" })
        {
            var old = f.Business.Create(f.Context, role, before, O(("trade_session", "old"), ("action", "purchase"), ("_proof", O(("kind", role == "dispatch.start" ? "bargain" : "buy")))), O(("ui", "ShopUI")));
            old["state"] = "unknown"; f.Business.Save(old);
        }
        var reconciled = await f.Business.ReconcileAsync(f.Context);
        Check(reconciled["unresolved"]!.AsArray().Count == 0 && reconciled["trade_diagnostics"]!.AsArray().Count == 2 && f.Box.Commands.Count == 0,
            "old unknown trades are diagnostic only, never replayed or declared completed");
        Check(f.Business.Records(f.Context).All(op => S(op["state"]) == "unknown"), "original uncertain outcomes preserved honestly");
        var next = f.Business.Create(f.Context, "trade.buy", before, O(("trade_session", "fresh"), ("action", "purchase")), O(("ui", "ShopUI")));
        Check(S(next["state"]) == "prepared" && S(next["scope"]!["trade_session"]) == "fresh", "fresh current-state plan can create its own purchase without recovering old receipt");
        f.Business.RequireResolved(f.Context, "dispatch.start");
        cases.Add("old bargain receipt cannot block an unrelated talent stage");

        string directory = Path.Combine(output, "trade-stale-plan");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "execution.json"), "{obsolete or interrupted json");
        File.WriteAllText(Path.Combine(directory, "purchases.json"), "old split");
        DailyTradeJournal.ArchivePlan(directory);
        var archives = Directory.GetDirectories(Path.Combine(directory, "history"));
        Check(archives.Length == 1 && File.ReadAllText(Path.Combine(archives[0], "execution.json")) == "{obsolete or interrupted json",
            "even unparseable old plan can be archived without blocking current-state planning");
        Check(File.ReadAllText(Path.Combine(archives[0], "purchases.json")) == "old split", "diagnostic archive preserves original plan bytes");
    }
}

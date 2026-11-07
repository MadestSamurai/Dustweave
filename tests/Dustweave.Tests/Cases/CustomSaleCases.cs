using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class CustomSaleCases
{
    static JsonObject Stack(long item, long instance, long count, bool locked = false, long expiry = 0) =>
        O(("id", item), ("type", 5), ("invenIndex", instance.ToString()), ("count", count), ("keepFlag", locked ? 1 : 0), ("expiryTime", expiry));
    static JsonObject Quote(long item, long shop = 1, long rate = 120, long price = 30) => O(("Item", item), ("Shop", shop), ("Rate", rate), ("Price", price));
    static JsonObject Catalog() => O(("cooking_recipes", new[] { O(("id", 900), ("output", 200)) }),
        ("cooking_items", new[] { O(("id", 100), ("name", "食材"), ("name_tw", "食材繁"), ("name_en", "Ingredient")),
            O(("id", 200), ("name", "料理"), ("name_tw", "料理繁"), ("name_en", "Dish")) }),
        ("items", new[] { O(("id", 100), ("name", "旧名称"), ("sale", 999)) }));

    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var catalog = Catalog();
        var choices = DailyCustomSales.Choices(catalog, "en-US");
        Check(choices.Length == 2 && choices.Single(c => c.Id == 200).Cooked && !choices.Single(c => c.Id == 100).Cooked
            && choices.Single(c => c.Id == 200).Name == "Dish", "custom sale catalog includes non-optimizer dishes, localized by output item rather than recipe id");
        Check(DailyCustomSales.Choices(catalog,"zh-TW").Single(c=>c.Id==100).Name=="食材繁", "custom sale catalog prefers translated current items over old optimizer names");
        var cookingOnly=catalog.DeepClone().AsObject(); cookingOnly.Remove("items");
        Check(DailyCustomSales.Choices(cookingOnly,"en-US").Length==2,"custom sale catalog does not require the optimizer's smaller item subset");
        var old = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject(); old["Trade"]!.AsObject().Remove("Sales");
        var preferences = DailyPreferences.Parse(old.ToJsonString());
        Check(preferences.Trade.Sales.Length == 0, "existing users never opt in to custom sales during upgrade");
        preferences.Trade.FavoritesOnly = true;
        preferences.Trade.Sales = [new(100, true, 50), new(200, false, 0)];
        Check(preferences.Trade.RequiresCatalog, "sale-only favorites require a catalog even without cooking recipes");
        var store = new DailyPreferenceStore(Path.Combine(output, "custom-sale-preferences"));
        store.Save(new string('a', 64), preferences);
        Check(store.Read(new string('a',64)).Trade.Sales.SequenceEqual(preferences.Trade.Sales) && store.Read(new string('b',64)).Trade.Sales.Length==0,
            "sale choices and reserves persist per account without enabling sales for other accounts");
        preferences.Trade.FavoritesOnly = false;
        Check(DailyPreferences.Parse(JsonSerializer.Serialize(preferences)).Trade.Sales.SequenceEqual(preferences.Trade.Sales), "changing trade mode preserves custom sale preferences");
        foreach (DailySalePreference[]? invalid in new DailySalePreference[]?[] { null, [null!], [new(0)], [new(100,true,-1)], [new(100,true,(long)int.MaxValue+1)], [new(100),new(100)] })
        {
            bool rejected=false; try { DailyCustomSales.Validate(invalid); } catch(InvalidDataException) { rejected=true; }
            Check(rejected, "malformed custom sale preferences are rejected before execution");
        }
        var wallet = O(("stacks", new JsonArray(Stack(100,1,20),Stack(100,2,100),Stack(100,3,30,true),Stack(100,4,999,false,123),Stack(200,5,10),Stack(999,6,99))));
        var native = O(("AvailableShops",new[]{1,2}), ("Quotes",new JsonArray(Quote(100),Quote(100,2,100,100),Quote(100,3,120,999),Quote(200,2,120,40),Quote(999))));
        JsonObject[] Plan(params DailySalePreference[] rules) => DailyCustomSales.Plan(catalog,rules,wallet,native);
        Check(Plan().Length==0 && Plan(new DailySalePreference(100,false),new(999,true)).Length==0,"unchecked and unknown items never become sale orders");
        var sales = Plan(new DailySalePreference(100,true,70),new(200,true));
        Check(sales.Length==3 && sales.Where(r=>N(r["item"])==100).Sum(r=>N(r["count"]))==80
            && sales.All(r=>N(r["instance"]) is not (3 or 4)) && sales.Where(r=>N(r["item"])==100).All(r=>N(r["price"])==30 && N(r["shop"])==1),
            "multiple stacks retain total reserve including locked stock; expired stacks, unavailable shops and non-120 quotes stay untouched");
        Check(sales.Where(r=>N(r["item"])==200).Sum(r=>N(r["value"]))==400,"dishes use the native live price rather than a catalog forecast");
        Check(Plan(new DailySalePreference(100,true,200)).Length==0 && Plan(new DailySalePreference(100,true,20)).Sum(r=>N(r["count"]))==120,
            "reserve above owned stock sells nothing; locked stock can satisfy the minimum without being sold");
        native["Quotes"]=new JsonArray(Quote(100,1,119),Quote(100,2,120,31));
        Check(Plan(new DailySalePreference(100,true,70)).All(r=>N(r["shop"])==2 && N(r["price"])==31),"highest eligible live quote is selected without using a lower rate");
        native["Quotes"]=new JsonArray(Quote(100,1,119));
        Check(Plan(new DailySalePreference(100,true)).Length==0,"no maximum-rate quote means keep inventory until another day");
        native["Quotes"]=new JsonArray(Quote(100));
        wallet["stacks"]=new JsonArray(Stack(100,2,50),Stack(100,3,30,true));
        Check(Plan(new DailySalePreference(100,true,70)).Sum(r=>N(r["count"]))==10,"replanning after stock changes retains the same total minimum");
        wallet["stacks"]=new JsonArray(Stack(100,2,50,true),Stack(100,3,30,true));
        Check(Plan(new DailySalePreference(100,true,0)).Length==0,"a newly locked stack is no longer eligible on the next pass");
        native.Remove("Quotes"); bool missing=false;
        try { Plan(new DailySalePreference(100,true)); } catch(InvalidDataException) { missing=true; }
        Check(missing,"incomplete quote evidence never authorizes selling");

        foreach (string scenario in new[]{"sale-only","low-price","quote-change","pending"})
        using (var h = new WorkflowHarness(Path.Combine(output,"custom-sales-"+scenario),DailyWorkflowRegistry.Proofs(),configureIdentity:(_,c)=>c["cycle"]="123"))
        {
            h.Workflow.Settings.Trade.Enabled=true; h.Workflow.Settings.Trade.FavoritesOnly=true;
            h.Workflow.Settings.Trade.Sales=[new(100,true,70),new(200,true)];
            h.Driver.TradeStageActive=true; h.Frame["Scene"]="Map3009_001";
            long ingredient=120, dish=10, gold=1000; int confirmations=0; string mode="Buy";
            void Shop()=>h.Page("ShopUI","_objBackButton","_sellButton.root","_buyButton.root");
            Shop();
            h.Readings=()=>[
                WorkflowCases.Reading("trade.native",("$self",O(("AvailableShops",new[]{1}), ("Favorites",new JsonArray()),("Offers",new JsonArray()),
                    ("Quotes",new JsonArray(Quote(100,rate:scenario=="low-price"?100:120),Quote(200,rate:scenario=="low-price" || scenario=="quote-change"&&confirmations>0?100:120)))))),
                WorkflowCases.Reading("trade.currency",("Gold",gold),("Catalyst",100)),
                WorkflowCases.Reading("trade.bargain",("$self",false)),
                WorkflowCases.Reading("trade.inventory",("Count",1),("$items",new JsonArray(O(("Key","Food"),("Value.Count",3),
                    ("Value.Values",new JsonArray(Stack(100,1,ingredient),Stack(100,2,30,true),Stack(200,3,dish))))))),
                WorkflowCases.Reading("trade.merchant",("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ",7),("$items",new JsonArray(O(("ὫὠὫὯὠὢὥὧὢὥὥ","Shop"),("ὭὩὪὯὯὬὧὠὠὬὠ",1))))),
                WorkflowCases.Reading("trade.shop_ui",("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ",7),("ὯὣὡὫὨὫὡὧὩὬὠ",mode))
            ];
            h.OnCommand=c=>{
                h.Time += .01;
                switch(S(c["Kind"])) {
                    case "trade_sell_preview":
                        Check(mode=="Sell","custom sale opens the sell tab before preview");
                        h.Page("ShopPopupUI"); h.Frame["Surfaces"]!.AsArray().Add(O(("Id",2),("Type","ShopUI"),("InputReady",false))); break;
                    case "trade_sell_confirm":
                        confirmations++; var flat=c["Items"]!.AsArray(); long item=N(flat[1]), count=N(flat[3]);
                        h.Event("trade.sell","request"); if(item==100)ingredient-=count;else dish-=count; gold+=count*N(flat[4]);
                        if(scenario!="pending" || confirmations>1)h.Event("trade.sell","response"); Shop(); break;
                    case "click":
                        if(S(c["UiToken"])=="ShopUI") {
                            long target=N(c["TargetId"]);
                            if(target==11) { mode="Sell"; Shop(); } else if(target==12) { mode="Buy"; Shop(); }
                            else h.Page("QuickMenuUI","_buttonClose");
                        } else h.Page("GameFieldDefaultUI"); break;
                    default: throw new Exception("Unexpected custom sale command: "+c.ToJsonString());
                }
            };
            JsonObject? result=null; Exception? error=null;
            try { result=await new DailyTradeExecution(h.Workflow,catalog).Run(); } catch(Exception e){error=e;}
            if(scenario=="pending") {
                Check(error!=null && confirmations==1 && ingredient==40,"uncertain sale confirmation is not submitted twice");
                Shop(); mode="Buy";
                await new DailyTradeExecution(h.Workflow,catalog).Run();
                Check(confirmations==2 && ingredient==40 && dish==0,"rerun uses remaining inventory instead of replaying the previous sale");
            } else {
                if(error!=null)throw new Exception(scenario,error);
                Check(ingredient==(scenario=="low-price"?120:40) && dish==(scenario=="sale-only"?0:10)
                    && confirmations==(scenario=="sale-only"?2:scenario=="quote-change"?1:0),"sale-only favorites execute selected surplus with bounded live repricing: "+scenario);
                Check(N(result?["gold_earned"])==gold-1000,"custom sale result reports verified proceeds: "+scenario);
            }
            Check(!h.Box.Commands.Any(c=>S(c["Kind"]).Contains("buy")||S(c["Kind"]).Contains("cook")||S(c["Kind"])=="trade_talent"),
                "empty purchase/cooking selections do not prevent sales or spend extra potions: "+scenario);
        }
    }
}

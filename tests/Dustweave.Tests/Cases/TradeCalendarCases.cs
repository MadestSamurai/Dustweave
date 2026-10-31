using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TradeCalendarCases
{
    public static Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string name) { if(!ok)throw new Exception(name); cases.Add(name); }
        JsonObject Item(long id,long price,int day=1)=>O(("id",id),("name","item"+id),("sale",price),("day",day),("shop",1),("stack",99999));
        JsonObject Recipe(long id,long item,long pills,params (string Key,object? Value)[] parts)=>O(("id",id),("output",item),("count",1),("potions",pills),("materials",O(parts)));
        JsonObject Catalog()=>O(("schema",1),("client","synthetic"),("database_sha256","synthetic"),
            ("items",new[]{Item(1,10,31),Item(2002,33,1)}),("recipes",new[]{Recipe(101,2002,1,("1",1))}),
            ("offers",new[]{O(("shop",1),("product",1),("item",1),("base_price",5),("limit",10))}));
        JsonObject State(JsonObject c,string date="2026-10-31",long gold=1000)=>O(("schema",1),
            ("context",O(("account","a"),("player","b"),("server","s"),("cycle","1"),("client",c["client"]),("database_sha256",c["database_sha256"]))),
            ("captured_utc","2026-10-31T00:00:00Z"),("game_date",date),("inventory_complete",true),("offers_complete",true),("recipes_complete",true),
            ("gold",gold),("potions",0),("potion_price",45),("potion_buy_limit",10000),("can_bargain",false),("bargain_active",false),
            ("items",new JsonObject()),("recipes",Array(Rows(c["recipes"]).Select(r=>r["id"]))),
            ("offers",Array(Rows(c["offers"]).Select(o=>O(("shop",o["shop"]),("product",o["product"]),("remaining",o["limit"]),("price",o["base_price"]),("bargain_price",N(o["base_price"])*40/100))))),
            ("today_quotes",new JsonArray()));
        JsonObject Plan(JsonObject c,JsonObject s,JsonObject? settings=null)=>DailyTradeOptimizer.Plan(c,s,settings,3);
        long Count(JsonObject p,string field,long item)=>Rows(p[field]).Where(r=>N(r["item"])==item).Sum(r=>N(r["count"]));
        var c=Catalog(); var s=State(c); s["items"]=O(("2002",100));
        string fingerprint=DailyTradeCatalog.Fingerprint(c);
        var p=Plan(c,s); DailyJson.Write(Path.Combine(output,"calendar-before.json"),p);
        Check(N(p["forecast"]?["announced_prices"])==1 && DailyTradeCatalog.Fingerprint(c)==fingerprint,"calendar forecasts new prices without changing observed catalog");
        Check(Count(p,"cooking",2002)==0 && N(p["summary"]?["potions_to_buy"])==0,"calendar delays potion spending before profitable sale date");
        Check(Rows(p["forecast"]?["calendar"]).Length==62 && Rows(p["sales"]).All(r=>B(r["today_quote_confirmed"])),"forecast future dates never become executable sale orders");
        s=State(c,"2026-11-01"); s["items"]=O(("2002",100)); s["today_quotes"]=new JsonArray(O(("item",2002),("shop",1),("rate",120),("price",33)));
        p=Plan(c,s); Check(Count(p,"sales",2002)==0 && Count(p,"holds",2002)==100,"prepared dishes retain price upside when funding does not require early sale");
        var tight=Catalog(); tight["offers"]![0]!["base_price"]=1; tight["items"]![0]!["day"]=2;
        var low=State(tight,"2026-11-01",0); low["items"]=O(("2002",1));
        low["today_quotes"]=new JsonArray(O(("item",2002),("shop",1),("rate",120),("price",33)));
        var liquid=Plan(tight,low);
        Check(Count(liquid,"sales",2002)==1,"limited capital can sell selected old-price food to fund more profitable later purchases");
        s=State(c,"2026-10-31",0); s["items"]=O(("1",5)); s["today_quotes"]=new JsonArray(O(("item",1),("shop",1),("rate",120),("price",10)));
        p=Plan(c,s); Check(N(p["summary"]?["cash_required"])==0 && Rows(p["purchases"]).Length==0,"zero cash cannot borrow from same-day or future sales");
        s=State(c,gold:100); p=Plan(c,s,O(("reserve_gold",90),("max_spend",5)));
        Check(N(p["summary"]?["cash_required"])<=5 && N(p["summary"]?["cash_remaining"])>=95,"calendar honors both reserve and per-run spend cap");
        s=State(c); s["processing_enabled"]=false; s["items"]=O(("1",20)); s["today_quotes"]=new JsonArray(O(("item",1),("shop",1),("rate",120),("price",10)));
        p=Plan(c,s); Check(Rows(p["cooking"]).Length==0 && Rows(p["sales"]).Length==0 && N(p["summary"]?["potions_used"])==0,"explicit later purchase-only choice performs no processing or potion spending");
        c=Catalog(); c["items"]![1]!["day"]=16; s=State(c,"2026-11-16"); s["items"]=O(("2002",100));
        s["today_quotes"]=new JsonArray(O(("item",2002),("shop",1),("rate",120),("price",33)));
        p=Plan(c,s); Check(Count(p,"sales",2002)==0 && Rows(p["warnings"]).Any(w=>S(w["code"])=="price_change_pending"),"maintenance boundary keeps changed dishes until actual price is confirmed");
        c["items"]![1]!["sale"]=66; s=State(c,"2026-11-16"); s["items"]=O(("1",10));
        s["today_quotes"]=new JsonArray(O(("item",2002),("shop",1),("rate",120),("price",66)));
        p=Plan(c,s); Check(Count(p,"cooking",2002)>0 && Count(p,"sales",2002)>0,"confirmed new prices immediately enable profitable current cooking and sales");
        Check(N(p["forecast"]?["announced_prices"])==0,"post-maintenance forecasts use captured prices rather than permanent hardcoded overrides");
        Check(N(p["summary"]?["potions_to_buy"])==Math.Max(0,N(p["summary"]?["potions_used"])-N(s["potions"])),"current pill purchases cover actual consumption without speculative hoarding");
        s["potions"]=10000; p=Plan(c,s);
        Check(N(p["summary"]?["potions_to_buy"])==0,"existing pills are used before buying any more");
        s["today_quotes"]![0]!["price"]=33; p=Plan(c,s); Check(Count(p,"sales",2002)==0,"mismatched current price never authorizes a sale");
        s=State(c,"2026-11-16"); s["items"]=O(("1",10)); s["protected_items"]=O(("1",10)); s["capacity_limits"]=O(("1",10));
        p=Plan(c,s); Check(Count(p,"purchases",1)==0 && Count(p,"cooking",2002)==0,"protected inventory and full capacity cannot be consumed or overfilled");
        c=Catalog(); c["offers"]![0]!["base_price"]=10; s=State(c);
        p=Plan(c,s,O(("include_break_even_resales",true)));
        Check(N(p["summary"]?["break_even_count"])>0,"achievement purchases remain available without sacrificing primary forecast value");
        s=State(c,"2026-11-16"); s["future_supply"]=O(("days",29),("database_sha256","synthetic"),("items",O(("1",2900))));
        s["offers"]![0]!["remaining"]=0; p=Plan(c,s);
        Check(Count(p,"cooking",2002)==0,"future expected drops never satisfy today's recipe ingredients");
        c=Catalog(); c["items"]![1]!["sale"]=999;
        Check(DailyTradePriceChange.ExpectedPrices(c,new(2026,10,31)).Count==0,"unrecognized live price is not overwritten by announced price");
        bool cancelled=false; try { PlanCancel(); }catch(OperationCanceledException){cancelled=true;}
        void PlanCancel()=>DailyTradeOptimizer.Plan(Catalog(),State(Catalog()),check:()=>throw new OperationCanceledException());
        Check(cancelled,"calendar cancellation propagates before any order generation");
        c=Catalog(); s=State(c); s["can_bargain"]=true; s["gold"]=1;
        p=Plan(c,s);
        Check(!B(p["bargain"]?["start"]) && N(p["summary"]?["cash_required"])<=1,"unaffordable bargaining falls back to a feasible normal mode without overspending");
        c=Catalog(); c["items"]!.AsArray().Add(Item(77,20,31)); c["items"]![2]!["shop"]=2;
        s=State(c); s["items"]=O(("77",50)); s["available_shops"]=new JsonArray(1);
        p=Plan(c,s);
        Check(!Rows(p["sales"]).Any(r=>N(r["item"])==77) && Rows(p["holds"]).Any(r=>N(r["item"])==77 && N(r["price"])==0),"unavailable merchants cannot fund current or future trading");
        c=Catalog(); c["items"]![0]!["stack"]=10; c["items"]![0]!["sale"]=20;
        c["offers"]![0]!["limit"]=30;
        s=State(c); s["recipes"]=new JsonArray(); s["items"]=O(("1",16));
        s["today_quotes"]=new JsonArray(O(("item",1),("shop",1),("rate",120),("price",20)));
        s["storage"]=O(("slots",10),("used",10),("stacks",new JsonArray(O(("id",1),("count",9)),O(("id",1),("count",7)))));
        p=Plan(c,s);
        Check(Count(p,"purchases",1)==4,"full bag fills both existing partial stacks even above one-stack total");
        s["storage"]!["slots"]=11; p=Plan(c,s);
        Check(Count(p,"purchases",1)==14,"one free slot permits one further stack plus existing headroom");
        c["items"]!.AsArray().Add(Item(2,30,31)); c["items"]![2]!["stack"]=10;
        c["offers"]!.AsArray().Add(O(("shop",1),("product",2),("item",2),("base_price",5),("limit",10)));
        s["offers"]!.AsArray().Add(O(("shop",1),("product",2),("remaining",10),("price",5),("bargain_price",2)));
        s["today_quotes"]!.AsArray().Add(O(("item",2),("shop",1),("rate",120),("price",30)));
        p=Plan(c,s);
        Check(Count(p,"purchases",2)==10 && Count(p,"purchases",1)==4,"two products compete for the same final slot by value rather than each claiming it");
        s["storage"]!["slots"]=10; s["today_quotes"]=new JsonArray(O(("item",1),("shop",1),("rate",120),("price",20)));
        p=Plan(c,s);
        Check(Count(p,"purchases",2)==0,"planned later sales cannot fund earlier purchase storage");
        var bag=new DailyTradeStorage(s["storage"]!.AsObject());
        Check(bag.Additional(1,10)==4 && bag.Additional(2,10)==0,"unknown non-food slots remain occupied");
        c=Catalog(); s=State(c); s["items"]=O(("1",300000)); s["potions"]=10000; s["potion_buy_limit"]=0;
        p=Plan(c,s);
        Check(Count(p,"cooking",2002)==0,"large multi-stack stock alone no longer forces early cooking");
        return Task.CompletedTask;
    }
}

using System.Text.Json.Nodes;
namespace Dustweave.Soul;

// Receding-horizon spending, not a value assigned to unspent currency.
// Coins only enable a feasible action. Battle outcomes never train this policy.
internal static class SoulShopPolicy
{
    static int N(JsonNode? n,string key)=>n?[key] is {} v?int.Parse(v.ToString()):0;
    static double Rate(JsonNode? n)=>n?["winRate"]?.GetValue<double>()??0;
    static (double Lower,double Upper) Bounds(JsonNode? battle)
    {
        int n=N(battle,"trials");if(n<=0)return(0,1);
        double p=Rate(battle),z=1.645,z2=z*z,den=1+z2/n;
        double center=(p+z2/(2*n))/den,spread=z*Math.Sqrt(p*(1-p)/n+z2/(4*n*n))/den;
        return(Math.Max(0,center-spread),Math.Min(1,center+spread));
    }
    internal static JsonObject Choose(JsonNode plan,JsonNode state)
    {
        var choice=plan["recommendation"]!;var battle=choice["battle"];
        int coins=N(state["play"],"coin"),price=N(state["progress"],"RefreshPrice"),cost=N(choice,"cost");
        var offers=state["shop"]!.AsArray().Where(n=>n?["isSoldOut"]?.GetValue<bool>()!=true&&N(n,"price")>0).ToArray();
        // Keep the buying power for at least one observed useful offer. This
        // is a stated affordability reference, not a predicted next shop.
        int buyingPower=offers.Where(n=>N(n,"type")==1).Select(n=>N(n,"price")).DefaultIfEmpty(offers.Select(n=>N(n,"price")).DefaultIfEmpty(1).Min()).Min();
        bool funded=price>0&&coins>=price+buyingPower;
        var owned=plan["ownedAlternative"]?["battle"];
        bool improvement=Bounds(battle).Lower>Bounds(owned).Upper;
        bool purchase=choice["path"]!.AsArray().Any(p=>p?["kind"]?.ToString() is "buy" or "upgrade");
        string action,reason;
        if(plan["status"]?.ToString()=="ready"){action="fight";reason="audited-current-plan";}
        else if(funded&&purchase&&cost>0&&coins-cost>=price+buyingPower&&improvement){action="secure";reason="preserve-audited-improvement-before-refresh";}
        else if(purchase&&improvement){action="fight";reason="preserve-affordable-measured-improvement";}
        else if(funded){action="refresh";reason="current-plan-weak-and-another-offer-is-affordable";}
        else {action="fight";reason="use-best-available-with-remaining-game-lives";}
        return new(){["action"]=action,["reason"]=reason,["coins"]=coins,["refreshPrice"]=price,["purchaseReference"]=buyingPower,
            ["currentWinRate"]=Rate(battle),["ownedWinRate"]=owned is null?null:JsonValue.Create(Rate(owned)),["planCost"]=cost,
            ["note"]="No fixed refresh-count cap, no terminal reward for unused coins, and no assumed future shop probability."};
    }
    internal static void Test()
    {
        var state=JsonNode.Parse("""{"play":{"coin":49},"progress":{"RefreshPrice":1},"shop":[{"type":1,"price":5}]}""")!;
        var plan=JsonNode.Parse("""{"status":"search_required","recommendation":{"cost":0,"battle":{"winRate":0.05859375,"trials":512},"path":[]},"ownedAlternative":{"battle":{"winRate":0.05859375,"trials":512}}}""")!;
        if(Choose(plan,state)["action"]!.ToString()!="refresh")throw new Exception("A weak board hoarded 49 coins instead of seeking an affordable improvement");
        // More than two refreshes are allowed; each accepted transaction must
        // still leave buying power. No battle result enters this recurrence.
        int count=0;while(Choose(plan,state)["action"]!.ToString()=="refresh"){state["play"]!["coin"]=49-++count;if(count>100)throw new Exception("Refresh spending did not terminate");}
        if(count<=2||N(state["play"],"coin")!=5)throw new Exception("Refresh stopped prematurely or consumed the purchase reserve");
        state["play"]!["coin"]=49;plan["recommendation"]!["cost"]=20;plan["recommendation"]!["battle"]!["winRate"]=.6;
        plan["recommendation"]!["path"]!.AsArray().Add(new JsonObject{["kind"]="buy"});
        if(Choose(plan,state)["action"]!.ToString()!="secure")throw new Exception("A measured affordable improvement was discarded before refreshing");
        plan["recommendation"]!["battle"]!["winRate"]=.06;
        if(Choose(plan,state)["action"]!.ToString()!="refresh")throw new Exception("Sampling noise authorized a speculative purchase");
        plan["status"]="ready";
        if(Choose(plan,state)["action"]!.ToString()!="fight")throw new Exception("A ready board was forced to spend its remaining coins");
        plan["status"]="search_required";state["play"]!["coin"]=1;
        if(Choose(plan,state)["action"]!.ToString()!="fight")throw new Exception("Insufficient funds discarded remaining lives");
        Console.WriteLine("Shop policy checks passed: low-win spending, repeated refreshes, buying power, measured improvements and remaining lives");
    }
}

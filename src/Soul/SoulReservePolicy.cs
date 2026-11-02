using System.Text.Json.Nodes;
namespace Dustweave.Soul;

// A failed complete board can still contain a useful component. Preserve a
// small, priced part of that evaluated candidate before discarding the shop.
// This authorizes a purchase only, never the failed layout or its upgrades.
internal static class SoulReservePolicy
{
    static int N(JsonNode? n,string key)=>n?[key] is {} value?int.Parse(value.ToString()):0;
    internal static JsonObject? Choose(JsonNode plan,JsonNode state,int allowance,int refreshPrice,Dictionary<int,JsonNode>? tables=null)
    {
        if(plan["status"]?.ToString()!="search_required"||allowance<=0||refreshPrice<=0)return null;
        NativePlanExecutor.CheckBinding(plan,state,allowUnready:true);
        if(plan["recommendation"]?["battle"]?["winRate"]?.GetValue<double>() is not >=.10)return null;
        tables??=JsonNode.Parse(File.ReadAllText(Path.Combine(SoulAssets.Tables,"IBItemTable.json")))!.AsArray().ToDictionary(n=>N(n,"id"),n=>n!);
        var packing=new SoulPacking(tables);
        int root=state["inventory"]!.AsArray().Min(n=>N(n,"inventoryIndex"));
        var core=NativeGrowthPolicy.Previous(state,root) is null?[]:state["growthRoute"]!["coreItemIds"]!.AsArray().Select(n=>int.Parse(n!.ToString())).ToHashSet();
        var held=state["inventory"]!.AsArray().Select(n=>N(n,"id")).ToHashSet();
        var offers=state["shop"]!.AsArray();
        var available=plan["recommendation"]!["path"]!.AsArray().OfType<JsonObject>()
            .Where(p=>p["kind"]?.ToString()=="buy"&&offers.Any(s=>N(s,"slot")==N(p,"slot")&&N(s,"id")==N(p,"itemId")&&N(s,"price")==N(p,"price")&&s?["isSoldOut"]?.GetValue<bool>()!=true)).ToArray();
        var floors=available.Where(p=>N(tables[N(p,"itemId")],"itemType")==0).ToArray();
        foreach(var unit in available
            .Where(p=>p["kind"]?.ToString()=="buy"&&!held.Contains(N(p,"itemId")))
            .Where(p=>N(tables[N(p,"itemId")],"itemType")==1)
            .OrderBy(p=>core.Contains(N(p,"itemId"))?0:1).ThenBy(p=>N(p,"price")).ThenBy(p=>N(p,"slot"))
        ){
            // Buy the actual affordable floor dependencies first, or do not
            // reserve the unit. No future shop, sale or upgrade is assumed.
            foreach(int subset in Enumerable.Range(0,1<<floors.Length).OrderBy(mask=>Enumerable.Range(0,floors.Length).Where(i=>(mask&(1<<i))!=0).Sum(i=>N(floors[i],"price")))){
                var bundle=new JsonArray(floors.Where((_,i)=>(subset&(1<<i))!=0).Select(p=>p.DeepClone()).Append(unit.DeepClone()).ToArray());
                int cost=bundle.Sum(p=>N(p,"price"));
                if(bundle.Any(p=>N(p,"price")<=0)||cost>allowance||N(state["play"],"coin")-cost-refreshPrice<10)continue;
                var witness=packing.PurchaseRoom(state,plan["recommendation"]!["layout"]!.AsArray(),bundle);
                if(witness is null)continue;
                return new JsonObject{["purchases"]=bundle,["cost"]=cost,["spaceWitness"]=witness};
            }
        }
        return null;
    }
    internal static void Test()
    {
        var tables=JsonNode.Parse("""[{"id":20001,"slotPositionX":[0,1],"slotPositionY":[0,0]},{"id":10036,"itemType":1,"slotPositionX":[0,1],"slotPositionY":[0,0]},{"id":10094,"itemType":1,"slotPositionX":[0,1],"slotPositionY":[0,0]}]""")!.AsArray().ToDictionary(n=>N(n,"id"),n=>n!);
        var state=JsonNode.Parse("""{"play":{"season":3,"dungeonId":14,"stageId":10,"coin":44},"inventory":[{"inventoryIndex":1,"id":10036,"level":0},{"inventoryIndex":2,"id":20001,"level":0}],"shop":[{"slot":0,"id":20001,"price":3},{"slot":1,"id":10094,"price":7,"type":1}]}""")!;
        var plan=JsonNode.Parse("""{"status":"search_required","initialState":{"season":3,"dungeonId":14,"stageId":10,"coin":44,"inventory":[{"index":1,"id":10036,"level":0},{"index":2,"id":20001,"level":0}],"shop":[{"slot":0,"id":20001,"price":3},{"slot":1,"id":10094,"price":7}]},"recommendation":{"cost":10,"battle":{"winRate":0.234375},"layout":[{"inventoryIndex":2,"itemId":20001,"position":0},{"inventoryIndex":102,"itemId":20001,"position":2},{"inventoryIndex":103,"itemId":10094,"position":2}],"path":[{"kind":"buy","slot":0,"itemId":20001,"price":3,"syntheticInventoryIndex":102},{"kind":"buy","slot":1,"itemId":10094,"price":7,"syntheticInventoryIndex":103}]}}""")!;
        var selected=Choose(plan,state,14,1,tables);
        if(selected?["purchases"]?.AsArray().Count!=2||N(selected["purchases"]![0],"itemId")!=20001||N(selected,"cost")!=10)throw new Exception("Floor dependency must be bought before reserved unit");
        if(Choose(plan,state,7,1,tables)!=null)throw new Exception("A unit was reserved without its required floor budget");
        plan["recommendation"]!["layout"]![1]!["position"]=1;
        if(Choose(plan,state,14,1,tables)!=null)throw new Exception("Overlapping floors accepted as purchase room");
        plan["recommendation"]!["layout"]![1]!["position"]=2;
        plan["recommendation"]!["battle"]!["winRate"]=0.0;
        if(Choose(plan,state,14,1,tables)!=null)throw new Exception("Reserved an unevidenced losing candidate");
        plan["recommendation"]!["battle"]!["winRate"]=.25;
        state["play"]!["coin"]=43;
        bool rejected=false;try{Choose(plan,state,14,1,tables);}catch(InvalidDataException){rejected=true;}
        if(!rejected)throw new Exception("A stale shop plan authorized a reservation");
        Console.WriteLine("Reserve checks passed: floor dependencies, complete budget, overlap, losing candidate, stale binding");
    }
}

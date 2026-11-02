using System.Text.Json.Nodes;

namespace Dustweave.Soul;

// Only used after the final native deck save, when every owned item must have
// exactly one view. Intermediate purchases intentionally defer board rebuilding.
internal static class SoulViewAudit
{
    internal static void RequireConsistent(JsonNode state)
    {
        var progress=state["progress"]!;
        var views=progress["Views"]!.AsArray();
        long Index(JsonNode? node)=>long.Parse(node!["InventoryIndex"]!.ToString());
        var owned=views.Where(v=>v?["Owner"]?.ToString()=="owned").ToArray();
        var wanted=state["inventory"]!.AsArray().Select(i=>long.Parse(i!["inventoryIndex"]!.ToString())).Order().ToArray();
        if(!owned.Select(Index).Order().SequenceEqual(wanted))throw new InvalidDataException("Owned item views differ from inventory after layout save.");
        foreach(var item in state["inventory"]!.AsArray()){
            var view=owned.Single(v=>Index(v)==long.Parse(item!["inventoryIndex"]!.ToString()))!;
            if(view["ItemId"]!.ToString()!=item!["id"]!.ToString()||view["Level"]!.ToString()!=item["level"]!.ToString())throw new InvalidDataException("An item view displays a stale item or level.");
        }
        var shop=views.Where(v=>v?["Owner"]?.ToString()=="shop").ToArray();
        if(progress["ShopReady"]?.GetValue<bool>()!=true||shop.Length!=state["shop"]!.AsArray().Count||shop.Select(v=>v!["ShopSlot"]!.ToString()).Distinct().Count()!=shop.Length)
            throw new InvalidDataException("Shop views are incomplete or duplicated.");
        if(views.Any(v=>v?["Owner"]?.ToString()=="other"&&v["State"]?.ToString()=="Shop"))throw new InvalidDataException("Orphaned shop views remain; battle was not started.");
        if(views.Where(v=>Index(v)>0).GroupBy(Index).Any(g=>g.Count()>1))throw new InvalidDataException("An owned item has overlapping views; battle was not started.");
    }
    internal static void Test()
    {
        var state=JsonNode.Parse("""{"inventory":[{"inventoryIndex":7,"id":10001,"level":0}],"shop":[{"slot":0}],"progress":{"ShopReady":true,"Views":[{"InventoryIndex":7,"ItemId":10001,"Level":0,"Owner":"owned","State":"Deck","ShopSlot":-1},{"InventoryIndex":-1,"Owner":"shop","State":"Shop","ShopSlot":0}]}}""")!;
        RequireConsistent(state);
        foreach(var change in new Action<JsonNode>[] {
            s=>s["progress"]!["Views"]!.AsArray().Add(s["progress"]!["Views"]![0]!.DeepClone()),
            s=>s["progress"]!["Views"]!.AsArray().Add(JsonNode.Parse("""{"InventoryIndex":-2,"Owner":"other","State":"Shop","ShopSlot":-1}""")),
            s=>s["progress"]!["ShopReady"]=false,
            s=>s["progress"]!["Views"]![0]!["Level"]=2,
            s=>s["inventory"]!.AsArray().Add(JsonNode.Parse("""{"inventoryIndex":8}"""))}){
            var invalid=state.DeepClone();change(invalid);bool rejected=false;
            try{RequireConsistent(invalid);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Incomplete/overlapping view state passed.");
        }
    }
}

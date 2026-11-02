using System.Text.Json.Nodes;
namespace Dustweave.Soul;

// A single continuous dungeon target guides optional bench purchases. The
// current board must already pass its own audit; speculative future ownership
// never enters a combat simulation or authorizes unaffordable upgrades.
internal static class NativeGrowthPolicy
{
    static string Root=>SoulAssets.Root;
    static int N(JsonNode? n,string k,int fallback=0)=>n?[k] is {} v?int.Parse(v.ToString()):fallback;
    internal static string? Previous(JsonNode state,int rootIndex)
    {
        var old=state["growthRoute"];var play=state["play"];
        return old is not null&&N(old,"dungeonId")==N(play,"dungeonId")&&N(old,"season")==N(play,"season")&&N(old,"rootInventoryIndex")==rootIndex?old["routeId"]?.GetValue<string>():null;
    }
    internal static void Append(JsonObject plan,JsonNode state,Dictionary<int,JsonNode> items,Dictionary<(int,int),JsonNode> units,string kernel,string tableHash)
    {
        if(plan["status"]?.GetValue<string>()!="ready"||!File.Exists(Path.Combine(Root,"campaign-library-active.json")))return;
        var data=JsonNode.Parse(File.ReadAllText(Path.Combine(Root,"campaign-library-active.json")))!;
        if(data["kernelSha256"]?.GetValue<string>()!=kernel||data["tableSha256"]?.GetValue<string>()!=tableHash||N(data,"season")!=N(state["play"],"season"))return;
        int dungeon=N(state["play"],"dungeonId"),wave=N(state["play"],"stageId"),season=N(state["play"],"season");
        var chapter=data["dungeons"]!.AsArray().SingleOrDefault(d=>N(d,"id")==dungeon);
        if(chapter is null||N(chapter,"waveCount")-wave<2)return;
        var sold=plan["recommendation"]!["path"]!.AsArray().Where(n=>n?["kind"]?.GetValue<string>()=="sell").Select(n=>N(n,"inventoryIndex")).ToHashSet();
        var held=state["inventory"]!.AsArray().Where(n=>!sold.Contains(N(n,"inventoryIndex"))).GroupBy(n=>N(n,"id")).ToDictionary(g=>g.Key,g=>g.Count());
        var caps=state["upgradeCapabilities"]!.AsArray().GroupBy(n=>N(n,"ItemId")).ToDictionary(g=>g.Key,g=>g.Max(n=>N(n,"MaximumLevel")));
        var offers=state["shop"]!.AsArray().Where(n=>n?["isSoldOut"]?.GetValue<bool>()!=true).Select(n=>n!).ToArray();
        int rootIndex=state["inventory"]!.AsArray().Min(n=>N(n,"inventoryIndex"));
        // Memory belongs to the captured decision state. Reading today's live
        // memory during replay leaks later purchases into an earlier decision.
        string? previous=Previous(state,rootIndex);
        var routes=chapter["routes"]!.AsArray().Where(r=>r is not null).Select(r=>r!).Where(r=>{
            var final=r["finalLayout"]!.AsArray();
            if(final.Count(p=>N(items[N(p,"itemId")],"itemType")==0)>8)return false;
            return final.Where(p=>N(items[N(p,"itemId")],"itemType")==1).All(p=>!caps.TryGetValue(N(p,"itemId"),out var cap)||N(p,"level")<=cap);
        }).ToArray();
        double Score(JsonNode route){
            var needed=route["coreItemIds"]!.AsArray().Select(n=>int.Parse(n!.ToString())).GroupBy(i=>i).ToDictionary(g=>g.Key,g=>g.Count());
            double owned=needed.Sum(g=>Math.Min(held.GetValueOrDefault(g.Key),g.Value)*(1+.1*N(items[g.Key],"itemGrade")));
            double available=needed.Sum(g=>Math.Min(offers.Count(o=>N(o,"id")==g.Key),Math.Max(0,g.Value-held.GetValueOrDefault(g.Key))));
            int floorCount=route["finalLayout"]!.AsArray().Count(p=>N(items[N(p,"itemId")],"itemType")==0);
            return 3*owned+1.5*available-.12*needed.Values.Sum()-.08*floorCount-.003*N(route,"finalCost")+(route["id"]?.GetValue<string>()==previous?.Trim()? .75:0);
        }
        var selected=routes.OrderByDescending(Score).FirstOrDefault();if(selected is null)return;
        var target=selected["coreItemIds"]!.AsArray().Select(n=>int.Parse(n!.ToString())).GroupBy(i=>i).ToDictionary(g=>g.Key,g=>g.Count());
        // Space and floor effects are investments too. Retaining units while
        // omitting their later floor coverage can make a route impossible to
        // assemble despite having enough coins and the correct characters.
        if(target.Any(g=>held.GetValueOrDefault(g.Key)>0))
            foreach(var group in selected["finalLayout"]!.AsArray().Where(p=>N(items[N(p,"itemId")],"itemType")==0).GroupBy(p=>N(p,"itemId")))target[group.Key]=group.Count();
        var choice=plan["recommendation"]!;var path=choice["path"]!.AsArray();int total=N(choice,"cost"),coin=N(state["play"],"coin"),reserve=Math.Clamp(coin/3,8,15);
        var counts=new Dictionary<int,int>(held);var taken=new HashSet<int>();
        foreach(var step in path.Where(n=>n?["kind"]?.GetValue<string>()=="buy")){
            int id=N(step,"itemId");counts[id]=counts.GetValueOrDefault(id)+1;taken.Add(N(step,"slot"));
        }
        var additions=new JsonArray();int synthetic=state["inventory"]!.AsArray().Max(n=>N(n,"inventoryIndex"))+100;
        var packing=new SoulPacking(items);var room=choice["layout"]!.DeepClone().AsArray();
        foreach(var offer in offers.OrderBy(o=>N(items[N(o,"id")],"itemType")==0?0:1).ThenBy(o=>N(o,"price"))){
            int id=N(offer,"id"),slot=N(offer,"slot"),price=N(offer,"price");
            if(taken.Contains(slot)||!target.ContainsKey(id)||counts.GetValueOrDefault(id)>=target[id]||coin-total-price<reserve||additions.Count>=2)continue;
            var step=new JsonObject{["kind"]="buy",["identity"]=$"shop:{slot}",["slot"]=slot,["itemId"]=id,["price"]=price,["cost"]=price,["syntheticInventoryIndex"]=synthetic+slot,["purpose"]="whole-dungeon-core-reserve"};
            var trial=new JsonArray(path.Select(p=>p!.DeepClone()).Append(step.DeepClone()).ToArray());
            if(N(items[id],"itemType")==0){
                ulong coverage=0;foreach(var p in room.Where(p=>N(items[N(p,"itemId")],"itemType")==0))coverage|=packing.Mask(p!);
                var position=packing.Shapes(id).Where(p=>(p.Mask&coverage)==0).OrderBy(p=>Math.Abs(p.Position%7-3)+Math.Abs(p.Position/7-3)).FirstOrDefault();
                if(position is null)continue;
                room.Add(new JsonObject{["inventoryIndex"]=synthetic+slot,["itemId"]=id,["position"]=position.Position,["rotation"]=position.Rotation});
            }else if(packing.PurchaseRoom(state,room,trial) is null)continue;
            path.Add(step);
            additions.Add(new JsonObject{["itemId"]=id,["price"]=price});counts[id]=counts.GetValueOrDefault(id)+1;total+=price;
        }
        choice["cost"]=total;choice["coinAfter"]=coin-total;choice["transactions"]=path.Count;
        // Persist only when a target actually overlaps this run. Empty targets
        // must not cause a random first route to become a permanent commitment.
        if(target.Any(g=>counts.GetValueOrDefault(g.Key)>0))choice["growthRoute"]=new JsonObject{
            ["season"]=season,["dungeonId"]=dungeon,["rootInventoryIndex"]=rootIndex,["routeId"]=selected["id"]!.DeepClone(),
            ["coreItemIds"]=selected["coreItemIds"]!.DeepClone(),["reservedPurchases"]=additions,["purpose"]="continuous-dungeon-growth-target",
            ["note"]="Future checkpoint items remain hypothetical until actually purchased and revalidated."};
    }
    internal static void Test()
    {
        var state=JsonNode.Parse("""{"play":{"dungeonId":15,"season":3},"growthRoute":{"dungeonId":15,"season":3,"rootInventoryIndex":21,"routeId":"target-a"}}""")!;
        if(Previous(state,21)!="target-a"||Previous(state,22) is not null)throw new InvalidDataException("Growth route crossed run identity");
        state["growthRoute"]!["dungeonId"]=14;
        if(Previous(state,21) is not null)throw new InvalidDataException("Growth route crossed dungeon");
        state.AsObject().Remove("growthRoute");
        if(Previous(state,21) is not null)throw new InvalidDataException("Replay invented a future route");
        Console.WriteLine("Growth memory checks passed: captured state, dungeon, run identity and missing history");
    }
}

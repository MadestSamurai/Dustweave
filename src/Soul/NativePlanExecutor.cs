using System.Text.Json.Nodes;
using Dustweave;
namespace Dustweave.Soul;

// Research executor: one identity-bound driver/lease for the complete plan.
// Each economic step still waits for its own accepted native response; an
// unknown outcome is never retried or inferred from a timeout.
internal static class NativePlanExecutor
{
    static long N(JsonNode? n)=>n is null?0:long.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
    static string[] Inventory(JsonNode state)=>state["inventory"]!.AsArray().Select(i=>$"{N(i!["inventoryIndex"])}/{N(i["id"])}/{N(i["level"])}").Order().ToArray();
    static string[] Offers(JsonArray offers,bool expected)=>offers.Select(s=>$"{N(s!["slot"])}/{N(s["id"])}/{N(s["price"])}/{(s[expected?"soldOut":"isSoldOut"]?.GetValue<bool>()??false)}").Order().ToArray();
    static JsonNode Read(string folder)=>JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!["state"]!;
    static JsonArray Guard(JsonNode state)=>new(N(state["play"]!["season"]),N(state["play"]!["dungeonId"]),N(state["play"]!["stageId"]),N(state["play"]!["coin"]));

    internal static void CheckSaleReceipt(JsonNode response,long index,long beforeCoins,long afterCoins)
    {
        if(response["Accepted"]?.GetValue<bool>()!=true||response["Role"]?.ToString()!="soul.IbItemSell")throw new InvalidDataException("Missing accepted sale receipt");
        var value=JsonNode.Parse(response["Values"]![0]!["Json"]!.GetValue<string>())!;
        if(N(value["invenIndex"])!=index||N(value["sellPrice"])<0||N(value["currentCoin"])!=afterCoins||beforeCoins+N(value["sellPrice"])!=afterCoins)
            throw new InvalidDataException("Sale receipt does not match the item and current coins");
    }

    internal static void CheckBinding(JsonNode plan,JsonNode state,bool allowUnready=false)
    {
        var status=plan["status"]?.GetValue<string>();
        bool bounded=status=="best_available"&&plan["decisionPolicy"]?.ToString()=="bounded-search-use-remaining-lives";
        if(status!="ready"&&!bounded&&!(allowUnready&&status=="search_required"))throw new InvalidDataException("Plan is not ready");
        var expected=plan["initialState"]??throw new InvalidDataException("Plan has no initial-state binding");
        foreach(var key in new[]{"season","dungeonId","stageId","coin"})
            if(N(expected[key])!=N(state["play"]![key]))throw new InvalidDataException("Run changed since planning: "+key);
        var items=expected["inventory"]!.AsArray().Select(i=>$"{N(i!["index"])}/{N(i["id"])}/{N(i["level"])}").Order().ToArray();
        if(!items.SequenceEqual(Inventory(state)))throw new InvalidDataException("Inventory changed since planning");
        if(!Offers(expected["shop"]!.AsArray(),true).SequenceEqual(Offers(state["shop"]!.AsArray(),false)))throw new InvalidDataException("Shop changed since planning");
        if(N(plan["recommendation"]!["cost"])>N(state["play"]!["coin"]))throw new InvalidDataException("Plan exceeds current coins");
        if(state["play"]!["isStageEnter"]?.GetValue<bool>()==true)throw new InvalidDataException("Battle already entered");
    }

    internal static void UseBestAvailable(JsonObject plan,JsonNode state)
    {
        CheckBinding(plan,state,allowUnready:true);
        if(N(state["play"]?["life"])<=0)throw new InvalidDataException("No remaining game life");
        if(plan["status"]?.ToString()=="ready")return;
        if(N(plan["recommendation"]?["battle"]?["trials"])<128||plan["recommendation"]?["layout"]?.AsArray().Count is not >0)
            throw new InvalidDataException("No audited legal candidate or no remaining game life");
        // Preserve the measured risk, including zero wins. A search target is
        // not permission to discard the game's remaining attempts.
        plan["status"]="best_available";
        plan["decisionPolicy"]="bounded-search-use-remaining-lives";
    }

    internal static List<JsonObject> Merge(JsonArray path)
    {
        var result=new List<JsonObject>();
        foreach(var node in path)
        {
            var next=node!.DeepClone().AsObject();var last=result.LastOrDefault();
            if(next["kind"]?.GetValue<string>()=="upgrade"&&last?["kind"]?.GetValue<string>()=="upgrade"&&N(next["inventoryIndex"])==N(last["inventoryIndex"])&&N(next["fromLevel"])==N(last["toLevel"]))
            {last["toLevel"]=next["toLevel"]!.DeepClone();last["price"]=N(last["price"])+N(next["price"]);last["cost"]=N(last["cost"])+N(next["cost"]);}
            else result.Add(next);
        }
        return result;
    }

    internal static async Task Run(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id,string planPath)
    {
        var plan=JsonNode.Parse(await File.ReadAllTextAsync(planPath))!;
        if(plan["solver"]?.ToString()=="dustweave-soul"){
            string kernel=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SoulAssets.Library)));
            if(plan["kernelSha256"]?.ToString()!=kernel)throw new InvalidDataException("Combat model changed since planning");
            if(plan["tableSha256"] is {} table&&table.ToString()!=SoulAssets.TableFingerprint(SoulAssets.Tables))throw new InvalidDataException("Game rule tables changed since planning");
        }
        var unitRows=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(SoulAssets.Tables,"IBUnitItemTable.json")))!.AsArray();
        var choice=plan["recommendation"]!;
        var encoded=NativeLayoutEncoder.Encode(choice["layout"]!.AsArray(),unitRows).AsArray();
        await SoulEvidence.Capture(mailbox,driver,folder,id+"-initial",continuation:async ()=>{
        var state=Read(folder);CheckBinding(plan,state);
        var steps=Merge(choice["path"]!.AsArray());
        if(steps.Sum(s=>N(s["price"]))!=N(choice["cost"]))throw new InvalidDataException("Plan cost mismatch");
        var map=new Dictionary<long,long>();int seq=0;
        async Task Send(string operation,int value,JsonArray items)
        {
            var action=new JsonObject{["ui"]="InventoryBattleStageUI",["operation"]=operation,["target_id"]=0,["value"]=value,["items"]=items,["timeout"]=20,["reason"]="soul-tool-continuous-plan"};
            await SoulEvidence.RunAction(mailbox,driver,folder,id+$"-step-{++seq:D2}",action);
            state=Read(folder);
        }
        var saleIndices=steps.Where(s=>s["kind"]?.GetValue<string>()=="sell").Select(s=>N(s["inventoryIndex"])).ToArray();
        if(saleIndices.Distinct().Count()!=saleIndices.Length||choice["layout"]!.AsArray().Any(p=>saleIndices.Contains(N(p!["inventoryIndex"]))))throw new InvalidDataException("Sold item is duplicated or required by final layout");
        if(state["deck"]!.AsArray().Any(p=>saleIndices.Contains(N(p!["inventoryIndex"]))))
        {
            // Unslot equipment temporarily too, so removing a unit cannot
            // leave an orphaned socket reference. All items remain owned.
            var inventory=state["inventory"]!.AsArray().ToDictionary(p=>N(p!["inventoryIndex"]),p=>p!);
            var trim=Guard(state);var keep=new List<string>();
            foreach(var p in state["deck"]!.AsArray())
            {
                long index=N(p!["inventoryIndex"]);
                if(saleIndices.Contains(index)||N(inventory[index]["type"])==2)continue;
                trim.Add(index);trim.Add(N(p["position"]));trim.Add(N(p["rotation"]));
                keep.Add($"{index}/{N(p["position"])}/{N(p["rotation"])}");
            }
            await Send("soul_save",0,trim);
            var actual=state["deck"]!.AsArray().Select(p=>$"{N(p!["inventoryIndex"])}/{N(p["position"])}/{N(p["rotation"])}");
            if(!keep.Order().SequenceEqual(actual.Order()))throw new InvalidDataException("Pre-sale deck removal was not confirmed");
        }
        foreach(var step in steps)
        {
            var before=state.DeepClone();var args=Guard(before);long price=N(step["price"]);
            bool sale=step["kind"]?.GetValue<string>()=="sell";
            if((sale?price>=0:price<0)||price>N(before["play"]!["coin"]))throw new InvalidDataException("Invalid transaction cost");
            if(step["kind"]!.GetValue<string>()=="buy")
            {
                var offer=before["shop"]!.AsArray().Single(s=>N(s!["slot"])==N(step["slot"]))!;
                if(N(offer["id"])!=N(step["itemId"])||N(offer["price"])!=price||offer["isSoldOut"]?.GetValue<bool>()==true)throw new InvalidDataException("Offer changed");
                args.Add(N(step["itemId"]));args.Add(N(offer["type"]));args.Add(price);
                await Send("soul_buy",(int)N(step["slot"]),args);
                var owned=before["inventory"]!.AsArray().Select(i=>N(i!["inventoryIndex"])).ToHashSet();
                var added=state["inventory"]!.AsArray().Where(i=>!owned.Contains(N(i!["inventoryIndex"]))).ToArray();
                if(added.Length!=1||N(added[0]!["id"])!=N(step["itemId"]))throw new InvalidDataException("Purchase receipt does not match inventory");
                map.Add(N(step["syntheticInventoryIndex"]),N(added[0]!["inventoryIndex"]));
            }
            else if(step["kind"]!.GetValue<string>()=="upgrade")
            {
                long index=N(step["inventoryIndex"]);if(map.TryGetValue(index,out var actual))index=actual;
                args.Add(index);args.Add(N(step["fromLevel"]));args.Add(price);args.Add(N(step["itemId"]));
                await Send("soul_upgrade",(int)(N(step["toLevel"])-N(step["fromLevel"])),args);
                var item=state["inventory"]!.AsArray().Single(i=>N(i!["inventoryIndex"])==index)!;
                if(N(item["level"])!=N(step["toLevel"])||N(item["id"])!=N(step["itemId"]))throw new InvalidDataException("Upgrade receipt does not match inventory");
            }
            else if(sale)
            {
                long index=N(step["inventoryIndex"]);
                var item=before["inventory"]!.AsArray().Single(i=>N(i!["inventoryIndex"])==index)!;
                if(N(item["id"])!=N(step["itemId"])||N(item["level"])!=N(step["level"]))throw new InvalidDataException("Sell item changed");
                args.Add(index);args.Add(N(item["id"]));args.Add(N(item["level"]));args.Add(-price);
                await Send("soul_sell",0,args);
                var remaining=state["inventory"]!.AsArray().Select(i=>N(i!["inventoryIndex"])).ToHashSet();
                var expected=before["inventory"]!.AsArray().Select(i=>N(i!["inventoryIndex"])).Where(i=>i!=index).ToHashSet();
                if(!remaining.SetEquals(expected))throw new InvalidDataException("Sell receipt does not match removed inventory");
                // The server rounds odd-price refunds differently from the
                // client's displayed estimate. Use its accepted item-bound
                // refund and refreshed wallet; a larger refund is not an error.
                var journal=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder,id+$"-step-{seq:D2}-action-journal.json")))!;
                CheckSaleReceipt(journal["response"]!,index,N(before["play"]!["coin"]),N(state["play"]!["coin"]));
            }
            else throw new InvalidDataException("Unreviewed economic action");
            foreach(var key in new[]{"season","dungeonId","stageId"})if(N(state["play"]![key])!=N(before["play"]![key]))throw new InvalidDataException("Run changed during transaction");
            if(!sale&&N(state["play"]!["coin"])!=N(before["play"]!["coin"])-price)throw new InvalidDataException("Accepted transaction coin mismatch");
            // Let the confirmed native storage drop/level effect be visible
            // before another action or the final board save replaces it.
            // This is presentation pacing, not another network or state poll.
            if(step["kind"]?.GetValue<string>() is "buy" or "upgrade")await Task.Delay(350);
        }
        var save=Guard(state);var wanted=new List<string>();
        foreach(var p in choice["layout"]!.AsArray())
        {
            long index=N(p!["inventoryIndex"]);var native=encoded.Single(e=>N(e!["inventoryIndex"])==index)!;
            if(map.TryGetValue(index,out var actual))index=actual;
            var item=state["inventory"]!.AsArray().Single(i=>N(i!["inventoryIndex"])==index)!;
            if(N(item["id"])!=N(p["itemId"])||N(item["level"])!=N(p["level"]))throw new InvalidDataException("Layout no longer matches inventory");
            save.Add(index);save.Add(N(native["position"]));save.Add(N(native["rotation"]));
            wanted.Add($"{index}/{N(native["position"])}/{N(native["rotation"])}");
        }
        await Send("soul_save",0,save);
        var saved=state["deck"]!.AsArray().Select(p=>$"{N(p!["inventoryIndex"])}/{N(p["position"])}/{N(p["rotation"])}");
        if(!wanted.Order().SequenceEqual(saved.Order()))throw new InvalidDataException("Saved deck differs from plan");
        SoulViewAudit.RequireConsistent(state);
        if(choice["growthRoute"] is JsonObject growth)
        {
            var committed=growth.DeepClone().AsObject();committed["status"]="native_confirmed";
            committed["journalPrefix"]=id;
            var pending=Path.Combine(folder,"active-growth-route.pending.json");
            await File.WriteAllTextAsync(pending,committed.ToJsonString());
            File.Move(pending,Path.Combine(folder,"active-growth-route.json"),true);
        }
        Console.WriteLine($"Plan applied with {seq} confirmed native transactions in one driver session");
        });
    }

    internal static void Test()
    {
        var receipt=JsonNode.Parse("""{"Accepted":true,"Role":"soul.IbItemSell","Values":[{"Json":"{\"invenIndex\":21159129,\"sellPrice\":3,\"currentCoin\":21}"}]}""")!;
        CheckSaleReceipt(receipt,21159129,18,21);
        foreach(var values in new[]{(Index:21159128L,Before:18L,After:21L),(Index:21159129L,Before:17L,After:21L),(Index:21159129L,Before:18L,After:20L)}){
            bool rejected=false;try{CheckSaleReceipt(receipt,values.Index,values.Before,values.After);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Unrelated or mismatched sale receipt accepted");
        }
        var path=JsonNode.Parse("[{\"kind\":\"upgrade\",\"inventoryIndex\":7,\"fromLevel\":0,\"toLevel\":1,\"price\":2,\"cost\":2},{\"kind\":\"upgrade\",\"inventoryIndex\":7,\"fromLevel\":1,\"toLevel\":2,\"price\":3,\"cost\":3},{\"kind\":\"upgrade\",\"inventoryIndex\":8,\"fromLevel\":0,\"toLevel\":1,\"price\":1,\"cost\":1}]")!.AsArray();
        var merged=Merge(path);if(merged.Count!=2||N(merged[0]["price"])!=5||N(merged[0]["toLevel"])!=2||N(path[0]!["price"])!=2)throw new Exception("Native upgrade merge failed");
        var state=JsonNode.Parse("""{"play":{"season":3,"dungeonId":13,"stageId":4,"coin":30},"inventory":[{"inventoryIndex":7,"id":10036,"level":1}],"shop":[{"slot":0,"id":10005,"price":2}]}""")!;
        var plan=JsonNode.Parse("""{"status":"ready","recommendation":{"cost":2},"initialState":{"season":3,"dungeonId":13,"stageId":4,"coin":30,"inventory":[{"index":7,"id":10036,"level":1}],"shop":[{"slot":0,"id":10005,"price":2,"soldOut":false}]}}""")!;
        CheckBinding(plan,state);
        foreach(var key in new[]{"season","dungeonId","stageId","coin"})
        {
            var changed=state.DeepClone();changed["play"]![key]=N(changed["play"]![key])+1;
            bool rejected=false;try{CheckBinding(plan,changed);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Stale plan accepted: "+key);
        }
        foreach(var mutation in new Action<JsonNode>[] {
            s=>s["inventory"]![0]!["level"]=2,
            s=>s["shop"]![0]!["isSoldOut"]=true,
            s=>s["shop"]![0]!["price"]=3,
            s=>s["play"]!["isStageEnter"]=true})
        {
            var changed=state.DeepClone();mutation(changed);bool rejected=false;
            try{CheckBinding(plan,changed);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Mutated plan precondition accepted");
        }
        var fallback=plan.DeepClone().AsObject();fallback["status"]="search_required";
        fallback["riskBudget"]=new JsonObject{["ready"]=false};
        fallback["recommendation"]!["battle"]=new JsonObject{["wins"]=0,["trials"]=512,["winRate"]=0.0};
        fallback["recommendation"]!["layout"]=new JsonArray(new JsonObject{["inventoryIndex"]=7,["itemId"]=10036,["position"]=0,["level"]=1});
        state["play"]!["life"]=5;
        UseBestAvailable(fallback,state);CheckBinding(fallback,state);
        if(fallback["status"]?.ToString()!="best_available"||fallback["riskBudget"]!["ready"]!.GetValue<bool>())throw new Exception("Remaining lives lost to the search target or risk was misreported");
        var exhausted=plan.DeepClone().AsObject();exhausted["status"]="search_required";state["play"]!["life"]=0;
        bool noLives=false;try{UseBestAvailable(exhausted,state);}catch(InvalidDataException){noLives=true;}
        if(!noLives)throw new Exception("An exhausted challenge attempted another battle");
        bool noReadyLives=false;try{UseBestAvailable(plan.AsObject(),state);}catch(InvalidDataException){noReadyLives=true;}
        if(!noReadyLives)throw new Exception("A reliable plan bypassed the game's exhausted lives");
        Console.WriteLine("Native plan checks passed: merge, preserved input, valid binding and 8 stale-state rejections");
    }
}

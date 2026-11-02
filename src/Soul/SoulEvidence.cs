using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
namespace Dustweave.Soul;

internal static class SoulEvidence
{
    internal static async Task Capture(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id,bool battle=false,JsonObject? action=null,Func<Task>? continuation=null,bool archiveRead=false)
    {
        var metadata=JsonNode.Parse(await File.ReadAllTextAsync(SoulAssets.MetadataPath))!.AsObject();
        var original=mailbox.Read("live","evidence-config.json");
        var empty=new JsonObject{["Mvid"]=metadata["mvid"]!.DeepClone(),["Reads"]=new JsonArray(),["Taps"]=new JsonArray(),["Aliases"]=new JsonArray()};
        var restore=original??JsonSerializer.SerializeToUtf8Bytes(empty);
        var config=JsonNode.Parse(restore)!.AsObject();
        if(config["Mvid"]?.GetValue<string>()!=metadata["mvid"]?.GetValue<string>())throw new InvalidOperationException("Client metadata identity mismatch");
        var reads=config["Reads"]!.AsArray();
        reads.Add(new JsonObject{["Id"]="soul.Capabilities",["Type"]="InventoryBattleManager",["Paths"]=new JsonArray("$self")});
        reads.Add(new JsonObject{["Id"]="soul.Progress",["Type"]="InventoryBattleManager",["Paths"]=new JsonArray("$self")});
        if(battle||action is not null||continuation is not null)foreach(var tap in SoulMetadata.Taps())config["Taps"]!.AsArray().Add(tap!.DeepClone());
        foreach(var owner in metadata["owners"]!.AsArray().OfType<JsonObject>())
        foreach(var property in owner["properties"]!.AsArray().OfType<JsonObject>())
        {
            var type=property["type"]!.GetValue<string>();
            var dto=new[]{"IbPlayDBInfo","IbItemDBInfo","IbShopItemDBInfo","IbDeckDBInfo"}.SingleOrDefault(t=>type.Contains("Proto.Net."+t));
            if(dto is null)continue;
            reads.Add(new JsonObject{["Id"]="soul."+dto,["Type"]=owner["type"]!.DeepClone(),["StaticMember"]=property["name"]!.DeepClone(),["Paths"]=new JsonArray("$self"),["CollectionPath"]="",["ItemPaths"]=new JsonArray(),["MaxItems"]=300});
        }
        await StoreConfiguration(folder,restore);
        bool archive=archiveRead||battle||action is not null||continuation is not null;
        JsonObject? lastObserved=null;
        try
        {
            mailbox.Write("live","evidence-config.json",JsonSerializer.SerializeToUtf8Bytes(config));
            for(var attempt=0;attempt<10;attempt++)
            {
                var observed=await driver.EvidenceAsync(new[]{"soul"});lastObserved=observed;
                if(observed["Readings"] is JsonArray rows && rows.Any(r=>r?["Id"]?.GetValue<string>()=="soul.IbPlayDBInfo"))
                {
                    await StoreObservation(observed,folder,id,archive);
                    // Full protobuf rows already exist in the snapshot. Repeating
                    // them on stdout can create another unbounded log copy.
                    if(battle)await RunBattle(mailbox,driver,folder,id);
                    if(action is not null)await RunAction(mailbox,driver,folder,id,action);
                    if(continuation is not null)await continuation();
                    return;
                }
                await Task.Delay(200);
            }
            throw new InvalidOperationException("Soul observer did not produce scoped evidence");
        }
        catch
        {
            // Freeze the last input held by this capture even if it could not be
            // parsed. Optional diagnostics must not hide the original failure.
            if(lastObserved is not null)StoreFailure(folder,id,lastObserved);
            throw;
        }
        finally { mailbox.Write("live","evidence-config.json",restore); }
    }
    internal static bool StoreFailure(string folder,string id,JsonObject observed)
    {
        try { DailyJson.Write(Path.Combine(folder,id+"-failed-soul.json"),observed);return true; }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return false;}
    }
    internal static Task StoreConfiguration(string folder,byte[] content)
    {
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        string path=Path.Combine(folder,"observer-"+hash+".json");
        if(!File.Exists(path))DailyJson.Write(path,JsonNode.Parse(content)!);
        return Task.CompletedTask;
    }
    internal static async Task StoreObservation(JsonObject observed,string folder,string id,bool archive)
    {
        DailyJson.Write(Path.Combine(folder,"soul-latest.json"),observed);
        await ExportState(observed,folder,id,archive);
    }
    private static async Task ExportState(JsonObject observed,string folder,string id,bool archive=true)
    {
        JsonNode Read(string type){var row=observed["Readings"]!.AsArray().Single(x=>x!["Id"]!.GetValue<string>()=="soul."+type)!;if(row["Error"]?.GetValue<string>()!="")throw new InvalidOperationException("Soul reading failed");return JsonNode.Parse(row["Values"]![0]!["Json"]!.GetValue<string>())!;}
        var items=Read("IbItemDBInfo").AsArray();var deck=Read("IbDeckDBInfo").AsArray();
        foreach(var row in items){row!["inventoryIndex"]=long.Parse(row["invenIndex"]!.GetValue<string>());row["level"]??=0;}
        foreach(var row in deck){row!["inventoryIndex"]=long.Parse(row["invenIndex"]!.GetValue<string>());row["rotation"]=row["rotationCount"]?.DeepClone()??JsonValue.Create(0);row["position"]??=0;}
        var play=Read("IbPlayDBInfo");
        foreach(var field in new[]{"season","dungeonId","stageId","coin","life","shopReloadCount"})play[field]??=0;
        play["isStageEnter"]??=false;
        var state=new JsonObject{["revision"]=observed["AtUtcTicks"]!.DeepClone(),["stateStatus"]="complete",["mandatoryBlockers"]=new JsonArray(),["play"]=play,["shop"]=Read("IbShopItemDBInfo"),["inventory"]=items,["deck"]=deck};
        state["upgradeCapabilities"]=Read("Capabilities");
        state["progress"]=Read("Progress");
        var routePath=Path.Combine(folder,"active-growth-route.json");
        // Capture strategy memory with native state so historical planners
        // never consult a later run's mutable route file.
        state["growthRoute"]=File.Exists(routePath)?JsonNode.Parse(await File.ReadAllTextAsync(routePath)):null;
        var payload=new JsonObject{["state"]=state,["evidence"]=archive?id+"-soul.json":"soul-latest.json"};
        if(archive){
            // Some post-action call sites only had the parsed state, leaving a
            // dangling evidence name. Every archival state now has its raw source.
            DailyJson.Write(Path.Combine(folder,id+"-soul.json"),observed);
            DailyJson.Write(Path.Combine(folder,id+"-state.json"),payload);
        }
        DailyJson.Write(Path.Combine(folder,"live-state-latest.json"),payload);
    }
    internal static JsonObject SaleSynchronization(JsonObject action,JsonNode response)
    {
        if(action["operation"]?.ToString()!="soul_sell"||action["items"] is not JsonArray items||items.Count!=8)
            throw new InvalidDataException("Sale synchronization requires the original item-bound action");
        if(response["Accepted"]?.GetValue<bool>()!=true||response["Role"]?.ToString()!="soul.IbItemSell")
            throw new InvalidDataException("Sale synchronization requires an accepted sale response");
        long index=long.Parse(items[4]!.ToString()),beforeCoins=long.Parse(items[3]!.ToString());
        var body=JsonNode.Parse(response["Values"]![0]!["Json"]!.GetValue<string>())!;
        long coins=body["currentCoin"]?.GetValue<long>()??0;
        if(index<=0||beforeCoins<0||beforeCoins>int.MaxValue||coins<0||coins>int.MaxValue)throw new InvalidDataException("Invalid native sale balance");
        NativePlanExecutor.CheckSaleReceipt(response,index,beforeCoins,coins);
        var sync=action.DeepClone().AsObject();sync["operation"]="soul_sale_sync";
        sync["items"]=new JsonArray(items.Take(4).Select(x=>x!.DeepClone()).Concat(new JsonNode[]{JsonValue.Create(index)!,JsonValue.Create(coins)!}).ToArray());
        return sync;
    }
    internal static async Task RunAction(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id,JsonObject action)
    {
        var kind=action["operation"]!.GetValue<string>();
        if(kind=="soul_views"){
            var viewReceipt=await SoulSubmit.Send(driver,action);
            await File.WriteAllTextAsync(Path.Combine(folder,id+"-view-reconcile-receipt.json"),viewReceipt.ToJsonString());
            var viewState=await driver.EvidenceAsync(new[]{"soul"});await ExportState(viewState,folder,id+"-after");return;
        }
        var role=kind switch{"soul_buy"=>"soul.IbShopBuy","soul_upgrade"=>"soul.IbItemUpgrade","soul_save"=>"soul.IbDeckSave","soul_refresh"=>"soul.IbShopRefresh","soul_enter" or "soul_confirm_enter"=>"soul.IbDungeonEnter","soul_sell"=>"soul.IbItemSell",_=>throw new InvalidOperationException("Unreviewed native action")};
        bool resuming=SoulEntryReceipt.IsResume(action);var resumedReads=new HashSet<string>();
        long start=DateTime.UtcNow.Ticks;
        var journal=new JsonObject{["status"]="dispatching",["atUtcTicks"]=start,["action"]=action.DeepClone()};
        string path=Path.Combine(folder,id+"-action-journal.json");
        await File.WriteAllTextAsync(path,journal.ToJsonString());
        var receipt=await SoulSubmit.Send(driver,action);
        await File.WriteAllTextAsync(Path.Combine(folder,id+"-receipt.json"),receipt.ToJsonString());
        var seen=new HashSet<string>();var until=DateTime.UtcNow.AddSeconds(35);bool accepted=false,refreshed=false;
        bool needsRead=kind is "soul_enter" or "soul_confirm_enter",entryConfirmed=kind=="soul_confirm_enter";
        while(DateTime.UtcNow<until)
        {
            if(kind=="soul_enter"&&!entryConfirmed&&!resuming){
                var frame=(await driver.ObserveAsync()).Frame;
                if(frame["Surfaces"]!.AsArray().Any(s=>s?["Type"]?.ToString()=="MessagePopupUI")){
                    var confirm=action.DeepClone().AsObject();confirm["operation"]="soul_confirm_enter";confirm["ui"]="MessagePopupUI";
                    await SoulSubmit.Send(driver,confirm);entryConfirmed=true;
                }
            }
            foreach(var name in mailbox.List("live","events~"))
            {
                if(!seen.Add(name))continue;var bytes=mailbox.Read("live",name);if(bytes is null)continue;
                var evt=JsonNode.Parse(bytes)!;
                if(evt["AtUtcTicks"]!.GetValue<long>()<start||evt["Role"]?.GetValue<string>()?.StartsWith("soul.")!=true)continue;
                await File.WriteAllBytesAsync(Path.Combine(folder,id+"-"+name.Replace('~','-')),bytes);
                Console.WriteLine("Native event: "+evt["Role"]+" "+evt["Kind"]+" accepted="+evt["Accepted"]);
                if(resuming&&evt["Kind"]?.ToString()=="response"&&SoulEntryReceipt.ResumeRoles.Contains(evt["Role"]?.ToString())){
                    if(evt["Accepted"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Existing-dungeon read rejected; no entry replayed");
                    resumedReads.Add(evt["Role"]!.ToString());
                }
                if(evt["Role"]?.GetValue<string>()==role&&evt["Kind"]?.GetValue<string>()=="response")
                {accepted=evt["Accepted"]?.GetValue<bool>()==true;journal["status"]=accepted?"server_accepted":"server_rejected";journal["response"]=evt.DeepClone();await File.WriteAllTextAsync(path,journal.ToJsonString());if(!accepted)throw new InvalidOperationException("Native request rejected; not retried");}
                if(evt["Role"]?.GetValue<string>()=="soul.IbShopInfo"&&evt["Kind"]?.GetValue<string>()=="response"&&evt["Accepted"]?.GetValue<bool>()==true)refreshed=true;
            }
            if(resuming&&SoulEntryReceipt.ResumeRoles.All(resumedReads.Contains)){
                var frame=(await driver.ObserveAsync()).Frame;
                bool visible=frame["Surfaces"]!.AsArray().Any(s=>s?["Type"]?.ToString()=="InventoryBattleStageUI");
                if(visible){
                    var observed=await driver.EvidenceAsync(new[]{"soul"});await ExportState(observed,folder,id+"-resume");
                    var current=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"live-state-latest.json")))!["state"]!;
                    if(SoulEntryReceipt.Confirmed(action,current,resumedReads,visible)){
                        journal["status"]="confirmed_existing_dungeon";journal["acceptedReads"]=new JsonArray(resumedReads.Order().Select(r=>(JsonNode)JsonValue.Create(r)!).ToArray());
                        await File.WriteAllTextAsync(path,journal.ToJsonString());return;
                    }
                }
            }
            if(accepted&&kind=="soul_sell"){
                var sync=SaleSynchronization(action,journal["response"]!);
                journal["status"]="server_accepted_syncing_wallet";await File.WriteAllTextAsync(path,journal.ToJsonString());
                var synced=await SoulSubmit.Send(driver,sync);
                await File.WriteAllTextAsync(Path.Combine(folder,id+"-wallet-receipt.json"),synced.ToJsonString());
            }
            if(accepted&&(!needsRead||refreshed)){var observed=await driver.EvidenceAsync(new[]{"soul"});await ExportState(observed,folder,id+"-after");journal["status"]=needsRead?"confirmed_and_refreshed":"confirmed_native_receipt";await File.WriteAllTextAsync(path,journal.ToJsonString());return;}
            await Task.Delay(200);
        }
        journal["status"]=accepted?"accepted_refresh_unconfirmed":"outcome_unknown";await File.WriteAllTextAsync(path,journal.ToJsonString());throw new TimeoutException("Native action confirmation incomplete; no retry");
    }
    internal static async Task RunObservedUi(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id,JsonObject action)
    {
        var role=action["expectProtocol"]?.GetValue<string>();
        if(role!="soul.IbDungeonGiveUp")throw new InvalidDataException("Unaudited UI protocol observation");
        long start=DateTime.UtcNow.Ticks;var before=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!["state"]!;
        if(before["play"]!["isStageEnter"]?.GetValue<bool>()==true)throw new InvalidDataException("Cannot abandon an entered battle");
        var expected=action["expectedRun"]??throw new InvalidDataException("Explicit reset identity required");
        foreach(var key in new[]{"season","dungeonId","stageId","coin","life"})if(!JsonNode.DeepEquals(expected[key],before["play"]![key]))throw new InvalidDataException("Reset run identity changed");
        var frame=(await driver.ObserveAsync()).Frame;
        var popup=frame["Surfaces"]!.AsArray().SingleOrDefault(s=>s?["Type"]?.GetValue<string>()=="MessagePopupUI");
        if(action["ui"]?.GetValue<string>()!="MessagePopupUI"||action["field"]?.GetValue<string>()!="_buttonOK"||popup is null)
            throw new InvalidDataException("Audited current-run give-up confirmation is not present");
        var dispatch=action.DeepClone().AsObject();dispatch.Remove("expectProtocol");dispatch.Remove("expectedRun");
        dispatch.Remove("field");dispatch["operation"]="soul_confirm_reset";dispatch["target_id"]=0;dispatch["value"]=0;
        dispatch["items"]=new JsonArray(new[]{"season","dungeonId","stageId","coin","life"}.Select(k=>expected[k]!.DeepClone()).ToArray());
        var receipt=await SoulSubmit.Send(driver,dispatch);
        await File.WriteAllTextAsync(Path.Combine(folder,id+"-receipt.json"),receipt.ToJsonString());
        var seen=new HashSet<string>();var until=DateTime.UtcNow.AddSeconds(30);
        while(DateTime.UtcNow<until){
            foreach(var name in mailbox.List("live","events~")){
                if(!seen.Add(name))continue;var bytes=mailbox.Read("live",name);if(bytes is null)continue;var evt=JsonNode.Parse(bytes)!;
                if(evt["AtUtcTicks"]!.GetValue<long>()<start||evt["Role"]?.GetValue<string>()!=role)continue;
                await File.WriteAllBytesAsync(Path.Combine(folder,id+"-"+name.Replace('~','-')),bytes);
                if(evt["Kind"]?.GetValue<string>()!="response")continue;
                if(evt["Accepted"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Native give-up rejected; do not repeat");
                var observed=await driver.EvidenceAsync(new[]{"soul"});await ExportState(observed,folder,id+"-after");
                var after=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!["state"]!;
                if(after["play"]!["dungeonId"]!.GetValue<int>()!=0||after["inventory"]!.AsArray().Count!=0||after["deck"]!.AsArray().Count!=0)
                    throw new InvalidOperationException("Accepted reset did not clear only the active run");
                if(before["progress"]!["ClearedDungeons"]!.ToJsonString()!=after["progress"]!["ClearedDungeons"]!.ToJsonString())throw new InvalidOperationException("Completed dungeon history changed");
                Console.WriteLine("Native give-up accepted; active run cleared and completed dungeon history preserved");return;
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException("Unknown give-up outcome; do not repeat");
    }
    private static async Task RunBattle(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id)
    {
        long start=DateTime.UtcNow.Ticks;
        var first=await driver.ObserveAsync();
        bool alreadyPrompt=first.Frame["Surfaces"]!.AsArray().Any(s=>s!["Type"]!.GetValue<string>()=="InventoryBattleBuyNoticePopupUI");
        var receipt=await SoulSubmit.Send(driver,new JsonObject{["ui"]=alreadyPrompt?"InventoryBattleBuyNoticePopupUI":"InventoryBattleStageUI",["field"]=alreadyPrompt?"_objOkBtn":"_objBattleStartButton",["reason"]="soul-tool-natural-battle",["timeout"]=20});
        await File.WriteAllTextAsync(Path.Combine(folder,id+"-battle-click.json"),receipt.ToJsonString());
        var seen=new HashSet<string>();var until=DateTime.UtcNow.AddSeconds(240);
        string previous="";bool confirmed=alreadyPrompt;
        while(DateTime.UtcNow<until)
        {
            var frame=await driver.ObserveAsync();
            var types=string.Join(",",frame.Frame["Surfaces"]!.AsArray().Select(s=>s!["Type"]!.GetValue<string>()));
            if(types!=previous){Console.WriteLine("Battle surfaces: "+types);previous=types;await File.WriteAllTextAsync(Path.Combine(folder,id+"-battle-frame-"+DateTime.UtcNow.Ticks+".json"),frame.Frame.ToJsonString());}
            if(!confirmed&&frame.Frame["Surfaces"]!.AsArray().Any(s=>s!["Type"]!.GetValue<string>()=="InventoryBattleBuyNoticePopupUI"))
            {
                confirmed=true;
                var confirm=await SoulSubmit.Send(driver,new JsonObject{["ui"]="InventoryBattleBuyNoticePopupUI",["field"]="_objOkBtn",["reason"]="soul-tool-authorized-no-shopping-baseline",["timeout"]=20});
                await File.WriteAllTextAsync(Path.Combine(folder,id+"-battle-confirm.json"),confirm.ToJsonString());
            }
            bool ended=false;
            foreach(var name in mailbox.List("live","events~"))
            {
                if(!seen.Add(name))continue;
                var bytes=mailbox.Read("live",name);if(bytes is null)continue;
                var evt=JsonNode.Parse(bytes)!.AsObject();
                if(evt["AtUtcTicks"]!.GetValue<long>()<start||evt["Role"]?.GetValue<string>()?.StartsWith("soul.")!=true)continue;
                await File.WriteAllBytesAsync(Path.Combine(folder,id+"-"+name.Replace('~','-')),bytes);
                Console.WriteLine("Soul event: "+evt["Role"]+" "+evt["Kind"]+" accepted="+evt["Accepted"]+" error="+evt["ErrorCode"]);
                if(evt["Role"]?.GetValue<string>()=="soul.IbStageEnd"&&evt["Kind"]?.GetValue<string>()=="response")
                {
                    if(evt["Accepted"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Server rejected battle settlement; preserve scene and do not replay");
                    await File.WriteAllTextAsync(Path.Combine(folder,"last-battle-response.json"),evt.ToJsonString());
                    ended=true;
                }
            }
            if(ended){await File.WriteAllTextAsync(Path.Combine(folder,"last-completed-battle.json"),new JsonObject{["prefix"]=id,["atUtc"]=DateTime.UtcNow.ToString("O")}.ToJsonString());Console.WriteLine("Natural battle ended; response exported.");return;}
            await Task.Delay(800);
        }
        throw new TimeoutException("Natural battle did not provide a terminal response; preserved scene, no retry");
    }
}

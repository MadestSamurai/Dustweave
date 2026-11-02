using System.Text.Json.Nodes;
namespace Dustweave.Soul;

internal static class SoulEntryReceipt
{
    internal static readonly string[] ResumeRoles=["soul.IbDeckInfo","soul.IbItemInfo","soul.IbShopInfo"];
    static int N(JsonNode? n)=>n is null?0:int.Parse(n.ToString());
    internal static bool IsResume(JsonNode action)=>action["operation"]?.ToString()=="soul_enter"&&action["items"] is JsonArray a&&a.Count==4&&N(a[1])>0&&N(a[1])==N(action["value"]);
    internal static bool EnteredBattle(JsonNode action,JsonNode state,JsonObject frame)
    {
        if(!IsResume(action))return false;
        bool result=frame["Surfaces"]!.AsArray().Any(s=>s?["Type"]?.ToString()=="InventoryBattleResultUI");
        int active=N(state["play"]?["dungeonId"]);
        // This only hands control to the observation/settlement path. It does
        // not authorize shopping or another Start, so idle-shop checks are not
        // applicable once the game has resumed an existing battle itself.
        return (SoulResume.InBattle(frame)||result)&&(active==N(action["items"]![1])||active==0&&result);
    }
    internal static bool Confirmed(JsonNode action,JsonNode state,ISet<string> accepted,bool stageVisible)
    {
        if(!IsResume(action)||!stageVisible||!ResumeRoles.All(accepted.Contains)||state["progress"]?["ShopReady"]?.GetValue<bool>()!=true)return false;
        var play=state["play"];var a=action["items"]!.AsArray();
        return N(play?["dungeonId"])==N(a[1])&&N(play?["stageId"])==N(a[2])&&N(play?["coin"])==N(a[3])&&play?["isStageEnter"]?.GetValue<bool>()!=true;
    }
    internal static void Test()
    {
        var action=JsonNode.Parse("""{"operation":"soul_enter","value":14,"items":[0,14,10,37]}""")!;
        var state=JsonNode.Parse("""{"play":{"dungeonId":14,"stageId":10,"coin":37,"isStageEnter":false},"progress":{"ShopReady":true}}""")!;
        var accepted=ResumeRoles.ToHashSet();
        var battle=new JsonObject{["Surfaces"]=new JsonArray(new JsonObject{["Type"]="InventoryBattleUI"})};
        if(!SoulResume.InBattle(battle)||!EnteredBattle(action,state,battle))throw new Exception("Existing Soul battle was mistaken for an idle shop");
        battle["Surfaces"]!.AsArray().Add(new JsonObject{["Type"]="InventoryBattleResultUI"});
        if(SoulResume.InBattle(battle)||!EnteredBattle(action,state,battle))throw new Exception("Result overlay waited forever for an already ended battle");
        var other=new JsonObject{["Surfaces"]=new JsonArray(new JsonObject{["Type"]="BattleUI_PVP"})};
        if(SoulResume.InBattle(other)||EnteredBattle(action,state,other))throw new Exception("An unrelated battle was adopted as Soul progress");
        if(!Confirmed(action,state,accepted,true))throw new Exception("Existing dungeon incorrectly requires a new-entry receipt");
        if(Confirmed(action,state,accepted,false))throw new Exception("Background reads counted as resumed stage");
        accepted.Remove("soul.IbItemInfo");if(Confirmed(action,state,accepted,true))throw new Exception("Incomplete resume reads accepted");accepted.Add("soul.IbItemInfo");
        foreach(var key in new[]{"dungeonId","stageId","coin"}){int old=N(state["play"]![key]);state["play"]![key]=old+1;if(Confirmed(action,state,accepted,true))throw new Exception("Changed resume state accepted: "+key);state["play"]![key]=old;}
        action["items"]![1]=0;if(IsResume(action)||Confirmed(action,state,accepted,true))throw new Exception("Fresh entry bypassed its consuming receipt");
        Console.WriteLine("Existing-dungeon resume checks passed: scoped reads, visible stage, exact progress and separate fresh entry");
    }
}

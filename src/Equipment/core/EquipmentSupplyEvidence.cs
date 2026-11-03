using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public static class EquipmentSupplyEvidence
{
 public static string Route(JsonNode r)=>S(r["table"]?["groupId"])+":"+S(r["table"]?["id"]);
 static IEnumerable<(string Path,JsonObject Journal)> Pending(JsonObject stock){
  if(!Directory.Exists(Data))yield break;
  foreach(string path in Directory.EnumerateFiles(Data,"*.json")){
   JsonObject journal;try{journal=Read(path);}catch(IOException){continue;}catch(System.Text.Json.JsonException){continue;}
   if(S(journal["pending"]?["role"])=="hunt.dispatch"&&S(journal["pending"]?["state"]) is "dispatching" or "unknown"&&S(journal["plan"]?["account"])==S(stock["account"])&&S(journal["plan"]?["player"])==S(stock["player"]))yield return(path,journal);
  }
 }
 public static bool HasPending(JsonObject stock)=>Pending(stock).Any();
 // Explicit user choice after a fresh inventory read and an idle native menu.
 // Preserve the original intent as evidence; never resend it or delete journals.
 public static void AbandonPending(JsonObject stock){foreach(var (path,journal) in Pending(stock)){
  journal["abandoned_supply"]=Clone(journal["pending"]);journal["pending"]=null;journal["state"]="superseded";journal["resolved_stock"]=Copy(stock);Write(path,journal);
 }}
 public static JsonObject Verify(JsonObject route,int count,long reserve,JsonObject before,JsonArray? packets,JsonObject after){
  if(S(before["account"])!=S(after["account"])||S(before["player"])!=S(after["player"]))throw new InvalidOperationException("Supply account changed");
  long cost=checked(N(route["ap"])*count);if(cost<=0||cost>EquipmentSupplyPlanner.AvailableRice(before,reserve))throw new InvalidOperationException("Supply rice budget mismatch");
  if(packets!=null&&(packets.Count!=1||packets[0]?["RewardInfoBundle"]==null))throw new InvalidOperationException("Sweep receipt is not complete yet");
  long free=N(before["rice"]?["free"])-N(after["rice"]?["free"]),bonus=N(before["rice"]?["bonus"])-N(after["rice"]?["bonus"]);
  if(free!=Math.Min(cost,N(before["rice"]?["free"]))||bonus!=cost-free)throw new InvalidOperationException("Sweep rice change is not confirmed yet");
  long gold=N(after["gold"])-N(before["gold"]),wood=N(after["materials"]?[EquipmentSupplyPlanner.Wood])-N(before["materials"]?[EquipmentSupplyPlanner.Wood]);
  if(gold<0||wood<0||N(after["potions"])!=N(before["potions"])||!Equal(before["equipment"],after["equipment"])||O(before["materials"]).Any(p=>N(after["materials"]?[p.Key])<N(p.Value))||O(before["protected"]).Any(p=>N(after["protected"]?[p.Key])<N(p.Value)))throw new InvalidOperationException("Other spending occurred during supply; refresh and review current inventory");
  // Receipts and actual balances establish settlement; projected amounts are
  // never substituted for a missing server result.
  if(packets==null&&gold==0&&wood==0)throw new InvalidOperationException("Sweep rewards are not observable yet");
  return new(){["route"]=Route(route),["sweeps"]=count,["rice"]=cost,["free_rice"]=free,["bonus_rice"]=bonus,["gold_gain"]=gold,["wood_gain"]=wood};
 }
 public static void Reconcile(JsonObject stock){
  if(!Directory.Exists(Data))return;
  foreach(string path in Directory.EnumerateFiles(Data,"*.json")){
   JsonObject journal;try{journal=Read(path);}catch(IOException){continue;}catch(System.Text.Json.JsonException){continue;}
   if(journal["pending"] is not JsonObject pending||S(pending["role"])!="hunt.dispatch"||S(pending["state"])=="rejected"||S(journal["plan"]?["account"])!=S(stock["account"]))continue;
   if(pending["action"] is not JsonObject action||action["supply_route"] is not JsonObject route||pending["before"] is not JsonObject observed||journal["operations"] is not JsonArray)continue;
   try{
    var before=EquipmentPlanner.Normalize(observed);
    var result=Verify(route,(int)N(action["supply_count"]),N(action["supply_reserve"]),before,null,stock);
    pending["result"]=result;pending["state"]="completed";journal["operations"]!.AsArray().Add(Select(pending,"id","role","state","at","result"));journal["expected"]=Copy(stock);journal["pending"]=null;Write(path,journal);
   }catch(InvalidOperationException){ /* Keep uncertain intent for explicit review. Never resend. */ }
  }
 }
}

using System.Globalization;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Expected drops belong only to future balances, never today's inventory.</summary>
public static class DailyTradeDropForecast
{
 public static Dictionary<long,long> Supply(JsonObject catalog,JsonObject state)
 {
  if(state["future_supply"]==null)return new();
  var f=state["future_supply"]!.AsObject();
  Require(N(f["days"])==29 && JsonNode.DeepEquals(f["database_sha256"],catalog["database_sha256"]),"Future drop rules do not match this trade model");
  var known=Rows(catalog["items"]).Select(x=>N(x["id"])).ToHashSet();
  var result=new Dictionary<long,long>();
  foreach(var p in f["items"]!.AsObject()){
   Require(long.TryParse(p.Key,out long id)&&known.Contains(id)&&p.Value?.GetValueKind()==System.Text.Json.JsonValueKind.Number&&long.TryParse(p.Value.ToJsonString(),NumberStyles.None,CultureInfo.InvariantCulture,out long count)&&count>=0&&count<=100_000_000,"Invalid future drop quantity");
   result[id]=N(p.Value);
  }
  return result;
 }

 public static JsonObject Project(JsonObject model,JsonObject catalog,IEnumerable<(long Pack,long Map,HashSet<long> Monsters)> maps)
 {
  Require(N(model["schema"])==1 && JsonNode.DeepEquals(model["database_sha256"],catalog["database_sha256"]),"Drop catalog differs from trade catalog");
  var rows=Rows(model["entries"]);var totals=new Dictionary<long,double>();
  // One weekly tour, at most 21 absorbed rooms a day. No daily re-collection of weekly drops.
  var selected=maps.DistinctBy(x=>(x.Pack,x.Map)).Take(147).ToArray();
  foreach(var map in selected)foreach(var r in rows.Where(r=>N(r["pack"])==map.Pack&&(S(r["kind"])=="pickup"?N(r["map"])==map.Map:map.Monsters.Contains(N(r["id"])))))
   foreach(var p in DailyTradeDrops.Mean(r["reward"]!.AsObject()))totals[p.Key]=totals.GetValueOrDefault(p.Key)+p.Value;
  return O(("days",29),("source","known_routes_static_expectation"),("database_sha256",catalog["database_sha256"]),
   ("maps",Array(selected.Select(m=>O(("pack",m.Pack),("map",m.Map))))),("weekly_mean",O(totals.Select(p=>(p.Key.ToString(),(object?)p.Value)).ToArray())),
   ("items",O(totals.Select(p=>(p.Key.ToString(),(object?)(long)Math.Floor(p.Value*29/7))).ToArray())),("executable",false));
 }

 public static HashSet<long> ActiveMonsters(JsonObject network)
 {
  var raw=network["Monsters"];
  var list=raw is JsonArray ? raw : string.IsNullOrWhiteSpace(S(raw)) ? new JsonArray() : JsonNode.Parse(S(raw));
  return Rows(list).Where(m=>B(m["activeFlag"])).Select(m=>N(m["monsterId"])).ToHashSet();
 }
 public static JsonObject? ForAccount(DailyWorkflow workflow,JsonObject catalog,JsonObject state)
 {
  if(!workflow.Settings.Weekly.Mainline)return null;
  try {
   string path=Path.Combine(DailyTools.PackageDirectory(workflow.Directory),"data","trade-drops.json");
   var model=DailyJson.TryRead<JsonObject>(path);
   if(model==null||!JsonNode.DeepEquals(model["database_sha256"],catalog["database_sha256"]))return null;
   string account=S(state["context"]!["account"]),player=S(state["context"]!["player"]);
   Require(DailyProfiles.ValidKey(account),"Invalid drop forecast identity");
   string directory=Path.Combine(workflow.Root,"live","server-collection",account);
   if(!Directory.Exists(directory))return null;
   var date=DateOnly.ParseExact(S(state["game_date"]),"yyyy-MM-dd",CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
   var route=new DailyCollectionCatalog(workflow);
   var packs=route.Packs();
   var maps=new List<(long Pack,long Map,HashSet<long> Monsters)>();
   var weeks=Directory.GetDirectories(directory).Where(p=>long.TryParse(Path.GetFileName(p),out long ticks)&&ticks>=date.AddDays(-14).Ticks&&ticks<=date.AddDays(8).Ticks).OrderDescending().ToArray();
   foreach(long pack in packs){
    var source=Rows(model["sources"]).SingleOrDefault(r=>N(r["pack"])==pack);
    if(source==null)continue;
    string data=DailyTradeData.Database();string db=Path.Combine(Path.GetDirectoryName(data)!,S(source["database"]));
    if(!File.Exists(db)||!string.Equals(DailyTradeCatalog.Hash(db),S(source["sha256"]),StringComparison.OrdinalIgnoreCase))continue;
    foreach(string week in weeks){
     var record=DailyJson.TryRead<JsonObject>(Path.Combine(week,pack+".json"));
     if(record==null||S(record["account"])!=account||S(record["player"])!=player||S(record["source"])!="FieldObjectInfoResponse+MonsterInfoResponse")continue;
     var network=record["network"]!.AsObject();
     if(S(network["State"])!="ready"||S(network["Error"])!=""||N(network["Pack"])!=pack)continue;
     var active=ActiveMonsters(network);
     foreach(long map in route.Maps(pack).Where(m=>network["Maps"]!.AsArray().Select(N).Contains(m)))
      maps.Add((pack,map,Rows(network["Targets"]).Where(t=>N(t["Map"])==map && active.Contains(N(t["Id"]))).Select(t=>N(t["Id"])).ToHashSet()));
     break;
    }
   }
   return maps.Count==0?null:Project(model,catalog,maps);
  }
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException or OverflowException){
   // Forecasts are optional. A stale optional source must not interrupt current-stock trading.
   workflow.Save("trade-drop-forecast-unavailable.json",O(("error",error.Message),("resources_spent",false)));return null;
  }
 }
}

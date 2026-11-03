extern alias equipment;
extern alias equipmentLive;
using System.Text.Json.Nodes;
using E=equipment::BD2Equipment.Core;
using MathReward=equipmentLive::BD2Equipment.Live.SupplyRewardMath;
using static equipment::BD2Equipment.Core.J;

static class EquipmentSupplyCases
{
 public static Task Run(string output,List<string> cases){
  void Check(bool yes,string label){if(!yes)throw new Exception(label);cases.Add("equipment supply: "+label);}
  void Reject(Action f,string label){try{f();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
  string? oldRoot=Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT");
  Environment.SetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT",Path.Combine(output,"equipment-supply"));
  var original=E.EquipmentPlanner.Catalog;
  void Catalog(JsonObject c)=>typeof(E.EquipmentPlanner).GetMethod("Activate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[c]);
  JsonObject Recipe(int id,string material,int used,int pills)=>new(){["id"]=id,["name"]="recipe"+id,["potions"]=pills,["materials"]=new JsonObject{[material]=used},["gold"]=new JsonObject{["7"]=10},["powder"]=new JsonObject{["7"]=10}};
  JsonObject Stock(long wood,long ore,long gold,long rice,long pills=15)=>new(){["account"]="supply-fixture",["player"]="player",["gold"]=gold,["potions"]=pills,["materials"]=new JsonObject{["501"]=wood,["201"]=ore},["protected"]=new JsonObject(),["equipment"]=new JsonArray(),["rice"]=new JsonObject{["free"]=Math.Min(4,rice),["bonus"]=Math.Max(0,rice-4)}};
  JsonObject Route(int id,long wood,long gold)=>new(){["available"]=true,["fixed"]=true,["ap"]=6,["wood"]=wood,["gold"]=gold,["table"]=new JsonObject{["groupId"]=1,["id"]=id}};
  JsonObject Settings(int woodReserve=0,int riceReserve=0)=>new(){["supply_enabled"]=true,["level"]=7,["count_limit"]=4,["wood_reserve"]=woodReserve,["rice_reserve"]=riceReserve,["supply_routes"]=new JsonArray(Route(1,5,0),Route(2,0,25))};
  try{
   // Current native semantics: guaranteed parent, one-choice wood child,
   // unrelated random extras. The parent ratio 90/10 does not weight wood.
   foreach(var (normal,boss,total) in new[]{(40,45,245),(60,67,367),(80,90,490)}){
    long wood= MathReward.Fixed(0,1,[10],[normal]);
    long extras=MathReward.Fixed(0,1,[90,1,5],[0,0,0]);
    Check(5*MathReward.Fixed(1,1,[90,10],[wood,extras])+MathReward.Fixed(1,1,[90,10],[(long)boss,0])==total,$"fixed wood {total}; extras do not reduce it");
   }
   Check(MathReward.Fixed(0,1,[1],[3500])*5+MathReward.Fixed(0,1,[1],[4500])==22000,"goblin base fixed 22000");
   Reject(()=>MathReward.Fixed(0,1,[50,50],[1,2]),"do not turn variable target rewards into a fixed quote");
   Catalog(new(){["recipes"]=new JsonArray(Recipe(1,"501",3,3),Recipe(2,"201",2,3))});
   // Independent integer enumeration across resource, reserve and cash boundaries.
   foreach(int wood in new[]{0,2,6})foreach(int ore in new[]{0,2,8})foreach(int gold in new[]{0,15,50})foreach(int rice in new[]{0,6,12,24})foreach(int floor in new[]{0,4}){
    var stock=Stock(wood,ore,gold,rice);var settings=Settings(floor,2);var p=E.EquipmentPlanner.Powder(stock,settings);E.EquipmentPlanner.Validate(p);
    int best=-1,minRice=int.MaxValue;
    for(int a=0;a<=4;a++)for(int b=0;a+b<=4;b++)for(int w=0;w<=4;w++)for(int g=0;g<=4;g++){
     int count=a+b,cost=6*(w+g);
     if(a*3>Math.Max(0,wood+w*5-floor)||b*2>ore||count*10>gold+g*25||cost>Math.Max(0,rice-2))continue;
     if(count>best||count==best&&cost<minRice){best=count;minRice=cost;}
    }
    Check(N(p["count"])==best&&N(p["supply"]?["rice"])==minRice,$"oracle {wood}/{ore}/{gold}/{rice}/reserve{floor}");
    if(p["supply"]!=null){var after=Copy(p["projected_stock"]!);after["rice"]!["free"]=Math.Max(0,N(stock["rice"]!["free"])-minRice);after["rice"]!["bonus"]=N(stock["rice"]!["bonus"])-Math.Max(0,minRice-N(stock["rice"]!["free"]));var actual=E.EquipmentSupplyPlanner.AfterSupply(p,after);Check(N(actual["count"])==best,"fixed-yield sweep meets approved craft count");}
   }
   var reserved=E.EquipmentPlanner.Powder(Stock(10,0,100,60),new(){["wood_reserve"]=4});Check(N(reserved["count"])==2,"wood reserve also applies without sweeps");
   var capped=Settings();capped["gold_budget"]=10;var limited=E.EquipmentPlanner.Powder(Stock(0,0,0,60),capped);Check(N(limited["count"])==1,"replenishment respects explicit crafting gold budget");
   var noQuotes=Settings();noQuotes["supply_routes"]=new JsonArray();Check(N(E.EquipmentPlanner.Powder(Stock(0,8,50,60),noQuotes)["count"])==4,"unavailable routes preserve existing-stock crafts");
   var missing=Stock(0,8,50,60);missing.Remove("rice");Reject(()=>E.EquipmentPlanner.Powder(missing,Settings()),"missing rice blocks only requested rice planning");Check(N(E.EquipmentPlanner.Powder(missing,new())["count"])==4,"missing rice does not block ordinary crafting");
   var before=Stock(0,0,10,10);var afterStock=Stock(5,0,10,4);afterStock["rice"]=new JsonObject{["free"]=0,["bonus"]=4};var receipt=new JsonArray(new JsonObject{["RewardInfoBundle"]=new JsonObject()});
   var proof=E.EquipmentSupplyEvidence.Verify(Route(1,5,0),1,4,before,receipt,afterStock);Check(N(proof["rice"])==6&&N(proof["wood_gain"])==5,"settles free-first plus stored rice once");
   Reject(()=>E.EquipmentSupplyEvidence.Verify(Route(1,5,0),1,5,before,receipt,afterStock),"reserve boundary rejects overspend");
   Reject(()=>E.EquipmentSupplyEvidence.Verify(Route(1,5,0),1,0,before,receipt,before),"unchanged inventory is not a consumed sweep");
   Check(N(E.EquipmentSupplyEvidence.Verify(Route(1,5,0),1,4,before,null,afterStock)["sweeps"])==1,"restart can confirm sweep from authoritative resource delta");
   afterStock["account"]="other";Reject(()=>E.EquipmentSupplyEvidence.Verify(Route(1,5,0),1,0,before,receipt,afterStock),"account change remains a hard boundary");
   var longSettings=Settings();longSettings["count_limit"]=50;var longPlan=E.EquipmentPlanner.Powder(Stock(0,0,0,300,150),longSettings);
   using var game=new FakeGame(longPlan["stock"]!.AsObject()){StopAfterFirstSweep=true};
   Reject(()=>E.EquipmentExecution.Execute(longPlan,S(longPlan["id"]),game),"stop after a settled sweep leaves progress resumable");
   Check(game.Sweeps==20&&game.Crafts==0,"stop prevents further sweeps and crafts");
   var resumed=E.EquipmentExecution.Resumable(game.Stock());Check(resumed!=null&&N(resumed["display"]?["supply"]?["rice"])==180,"resume excludes the 20 paid sweeps");
   File.Delete(Stop);game.StopAfterFirstSweep=false;
   var finished=E.EquipmentExecution.Execute(longPlan,S(longPlan["id"]),game);
   Check(S(finished["state"])=="completed"&&game.Sweeps==50&&game.Crafts==50,"resume finishes without repeating a paid batch");
   E.EquipmentExecution.Execute(longPlan,S(longPlan["id"]),game);Check(game.Sweeps==50&&game.Crafts==50,"completed plan retry does not repeat consumption");
   var pendingPath=Path.Combine(Data,"pending-sweep-fixture.json");Write(pendingPath,new JsonObject{["plan"]=Copy(longPlan),["pending"]=new JsonObject{["role"]="hunt.dispatch",["state"]="unknown"},["state"]="paused"});
   Check(E.EquipmentSupplyEvidence.HasPending(game.Stock()),"pending sweep is exposed for user recovery");
   var wrong=game.Stock();wrong["account"]="other";E.EquipmentSupplyEvidence.AbandonPending(wrong);Check(E.EquipmentSupplyEvidence.HasPending(game.Stock()),"recovery cannot modify another account");
   E.EquipmentSupplyEvidence.AbandonPending(game.Stock());E.EquipmentSupplyEvidence.AbandonPending(game.Stock());var saved=Read(pendingPath);
   Check(!E.EquipmentSupplyEvidence.HasPending(game.Stock())&&saved["abandoned_supply"]!=null&&S(saved["state"])=="superseded","explicit restart retains old evidence and is repeatable");
  }finally{Catalog(original);Environment.SetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT",oldRoot);}
  return Task.CompletedTask;
 }
 sealed class FakeGame(JsonObject initial):E.IEquipmentGame{
  JsonObject stock=Copy(initial);int count,level;JsonNode? recipe;
  public bool StopAfterFirstSweep;public int Sweeps,Crafts;
  public JsonObject Stock()=>Copy(stock);
  public IDisposable EnableActions()=>new Empty();public void Dispose(){}public void Cleanup(string destination){}
  public void RefinePreview(string instance)=>throw new NotSupportedException();
  public int PowderPreview(JsonNode r,int n,int l){recipe=r;count=n;level=l;return n;}
  public JsonObject SupplyPreview(JsonObject route,int n,long reserve)=>new(){["route"]=Copy(route),["count"]=n,["reserve"]=reserve};
  public JsonObject Transaction(JsonObject journal,string role,JsonObject action,Func<JsonObject,JsonArray,JsonObject,JsonObject> verify){
   var before=Copy(stock);JsonArray receipts;
   if(role=="hunt.dispatch"){
    int n=(int)N(action["count"]);var r=action["route"]!;long cost=n*N(r["ap"]),free=Math.Min(cost,N(stock["rice"]!["free"]));
    stock["rice"]!["free"]=N(stock["rice"]!["free"])-free;stock["rice"]!["bonus"]=N(stock["rice"]!["bonus"])-(cost-free);stock["gold"]=N(stock["gold"])+n*N(r["gold"]);stock["materials"]!["501"]=N(stock["materials"]!["501"])+n*N(r["wood"]);Sweeps+=n;
    receipts=new(new JsonObject{["RewardInfoBundle"]=new JsonObject()});
   }else{
    long gold=N(recipe!["gold"]![level.ToString()])*count;stock["gold"]=N(stock["gold"])-gold;stock["potions"]=N(stock["potions"])-N(recipe["potions"])*count;
    foreach(var p in O(recipe["materials"]))stock["materials"]![p.Key]=N(stock["materials"]![p.Key])-N(p.Value)*count;
    stock["materials"]!["10"]=N(stock["materials"]!["10"])+N(recipe["powder"]![level.ToString()])*count;Crafts+=count;
    receipts=new(new JsonObject{["UpgradeUsedGold"]=gold,["LackItemInfo"]=new JsonArray()});
   }
   var result=verify(before,receipts,Stock());journal["operations"]!.AsArray().Add(new JsonObject{["role"]=role,["state"]="completed",["result"]=Copy(result)});journal["expected"]=Stock();Write(Path.Combine(Data,S(journal["id"])+".json"),journal);
   if(StopAfterFirstSweep&&Sweeps>0)File.WriteAllText(Stop,"stop");return result;
  }
  sealed class Empty:IDisposable{public void Dispose(){}}
 }
}

using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public sealed partial class LiveEquipment
{
 const string HuntUi="HuntOrAirwayUI",HuntPopup="HuntDispatchPopupUI";
 JsonObject SupplyState()=>LiveEvidence.Reading(Evidence(Snapshot()),"equipment.supply","$state")!.AsObject();
 bool OpenSupply(int pack,int group){
  var support=SupplyState();if(S(support["error"])!="")throw new EquipmentSupplyError("equipment.supply.quoteError",S(support["error"]));
  if(Types(Snapshot()).Contains(HuntUi))Cleanup(HuntUi);else{Cleanup();Step("MenuUI","_goQuickHuntDispatchButton",expect:HuntUi);}
  Step(HuntUi,operation:"equipment_hunt_select",value:pack,items:new JsonArray(group));
  var selected=WaitRead("equipment.supply","$state",n=>N(n?["table"]?["packId"])==pack&&N(n?["table"]?["typeGroupId"])==group);
  if(S(LiveEvidence.Reading(selected,"equipment.supply","$state")?["state"])!="ABLE")return false;
  Step(HuntUi,"$pointer/SelectParent/Layout - Select/Object - LevelParent/Button - Hunt",expect:HuntPopup);
  return true;
 }
 public JsonArray SupplyQuotes(){
  using var actions=EnableActions();var quotes=new JsonArray();
  foreach(var destination in new[]{(9,0),(3004,1)}){
   if(!OpenSupply(destination.Item1,destination.Item2)){quotes.Add(new JsonObject{["available"]=false,["table"]=new JsonObject{["packId"]=destination.Item1,["typeGroupId"]=destination.Item2}});continue;}Step(HuntPopup,operation:"equipment_hunt_quote");
   var e=WaitRead("equipment.supply","$state",n=>n?["quote"] is JsonObject);var quote=LiveEvidence.Reading(e,"equipment.supply","$state")!["quote"]!.AsObject();
   if(S(quote["error"])!="")throw new EquipmentSupplyError("equipment.supply.quoteError",S(quote["error"]));quotes.Add(Copy(quote));
   Step(HuntPopup,back:true,absent:HuntPopup);
  }
  Cleanup();return quotes;
 }
 public JsonObject SupplyPreview(JsonObject route,int count,long reserve){
  var table=route["table"]!;if(!OpenSupply((int)N(table["packId"]),(int)N(table["typeGroupId"])))throw new EquipmentSupplyError("equipment.supply.routeUnavailable");
  Step(HuntPopup,operation:"equipment_hunt_options",value:count);
  var e=WaitRead("equipment.supply","$state",n=>N(n?["count"])==count&&!B(n?["freeOnly"]));var state=LiveEvidence.Reading(e,"equipment.supply","$state")!;
  foreach(string key in new[]{"groupId","id","packId","typeGroupId","apPerTime"})if(N(state["table"]?[key])!=N(table[key]))throw new EquipmentSupplyError("equipment.supply.routeChanged");
  long cost=checked(count*N(route["ap"]));if(cost>EquipmentSupplyPlanner.AvailableRice(Stock(),reserve))throw new EquipmentSupplyError("equipment.supply.reserveChanged");
  return new(){["ui"]=HuntPopup,["field"]="_buttonHuntDispatch._button",["reason"]="Equipment supply: confirmed fixed-count rice sweep",["supply_route"]=Copy(route),["supply_count"]=count,["supply_reserve"]=reserve};
 }
 public JsonObject ResolveSupply(JsonObject expected){
  using var actions=EnableActions();Cleanup();var stock=Stock();
  if(!Equal(stock,expected))throw new EquipmentSupplyError("equipment.supply.inventoryChanged");
  EquipmentSupplyEvidence.AbandonPending(stock);return new(){["stock"]=stock};
 }
}

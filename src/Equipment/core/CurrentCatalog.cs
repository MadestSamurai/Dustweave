using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;

// Builds prices and selectors from the connected client's current tables. No simulator data.
public static class CurrentCatalog
{
 public static (JsonObject Catalog,JsonObject Display) Build(JsonObject tables,string mvid)
 {
  JsonNode[] Rows(string name){var rows=A(tables[name]).ToArray();if(rows.Length==0)throw new InvalidDataException("Missing current table: "+name);return rows;}
  Dictionary<string,JsonNode> ById(string name)=>Rows(name).ToDictionary(r=>S(r["id"],"0"));
  void Need(bool yes,string reason){if(!yes)throw new InvalidDataException("Unsupported equipment data: "+reason);}
  var translations=new JsonObject();
  var equips=ById("EquipmentTable");var names=ById("NameTextTable");var texts=ById("LocalTextTable");
  JsonObject Names(JsonNode? id,Dictionary<string,JsonNode> dict){dict.TryGetValue(S(id,"0"),out var text);if(S(text?["textCn"])!=""&&S(text?["textEn"])!="")translations[S(text!["textCn"])]=S(text["textEn"]);return new(){["zh-CN"]=S(text?["textCn"],"#"+S(id)),["zh-TW"]=S(text?["textTw"]),["en-US"]=S(text?["textEn"]),["ko-KR"]=S(text?["text"]),["ja-JP"]=S(text?["textJp"])};}
  var rankValues=new JsonObject();foreach(var group in Rows("EquipmentRankTable").GroupBy(r=>S(r["groupId"]))) {
   var ranks=group.OrderBy(r=>N(r["id"])).ToArray();Need(ranks.Length==3&&ranks.Select(r=>N(r["id"])).SequenceEqual(new long[]{1,2,3})&&ranks.All(r=>A(r["rankValue"]).Count()==4),"rank groups");
   rankValues[group.Key]=Array(ranks.Select(r=>r["rankValue"]));
  }
  JsonObject Costs(JsonNode row,string prefix){var kinds=A(row[prefix+"ItemType"]).ToArray();var ids=A(row[prefix+"ItemId"]).ToArray();var counts=A(row[prefix+"ItemCount"]).ToArray();Need(kinds.Length>0&&kinds.Length==ids.Length&&ids.Length==counts.Length,"cost lengths");var result=new JsonObject{["gold"]=0,["powder"]=0};for(int i=0;i<kinds.Length;i++){Need(N(counts[i])>0,"positive cost");string key=N(kinds[i])==4&&N(ids[i])==0?"gold":N(kinds[i])==8&&N(ids[i])==10?"powder":throw new InvalidDataException("Unexpected equipment cost resource");result[key]=N(result[key])+N(counts[i]);}return result;}
  var refine=new JsonObject();foreach(var grade in Rows("EquipmentGradeTable")){var costs=Costs(grade,"rankSmelt");Need(N(costs["gold"])>0&&N(costs["powder"])>0,"refinement cost");refine[S(grade["id"])]=costs;}
  var equipment=new JsonObject();var displayEquipment=new JsonObject();
  foreach(var (id,g) in equips){Need(rankValues[S(g["rankGroupId"])]!=null&&refine[S(g["grade"])]!=null,"equipment references");var ns=Names(g["itemNameTextId"],names);equipment[id]=new JsonObject{["name"]=Clone(ns["zh-CN"]),["grade"]=Clone(g["grade"]),["rank_group"]=Clone(g["rankGroupId"])};displayEquipment[id]=new JsonObject{["slot"]=N(g["slotType"]),["quality"]=N(g["qualityType"]),["exclusive"]=N(g["privateUniqueCharId"]),["monster_hunt"]=N(g["isMonsterHunt"])!=0,["names"]=ns};}
  var boxes=ById("RandomBoxTable");var rewards=ById("RewardGroupTable");var growth=Rows("EquipmentGrowthTable").ToDictionary(r=>$"{S(r["groupId"])}:{N(r["id"])}");var expect=Rows("EquipmentExpectTable").ToDictionary(r=>$"{S(r["groupId"])}:{N(r["id"])}");var skills=Rows("TalentSkillTable");var recipes=new JsonArray();
  foreach(var r in Rows("EquipmentMakingTable")){
   if(N(r["resultItemType"])!=9||!boxes.TryGetValue(S(r["resultItemId"]),out var box)||!rewards.TryGetValue(S(box["rewardGroupId"]),out var reward))continue;
   var ids=A(reward["itemId"]).ToArray();var kinds=A(reward["itemType"]).ToArray();
   if(ids.Length==0||ids.Length!=kinds.Length||kinds.Any(k=>N(k)!=10)||ids.Any(i=>!equips.ContainsKey(S(i))||N(equips[S(i)]["grade"])!=1))continue;
   Need(N(r["resultItemCount"])==1&&N(reward["dropCount"])==1,"N recipe output count");
   var costs=skills.Where(s=>N(s["classType"])==9&&N(s["id"])==N(r["talentLevel"])).Select(s=>N(s["catalystValue"])).Distinct().ToArray();Need(costs.Length==1&&costs[0]>0,"crafting potion cost");
   var materials=new JsonObject();var mi=A(r["materialItemId"]).ToArray();var mt=A(r["materialItemType"]).ToArray();var mc=A(r["materialItemCount"]).ToArray();Need(mi.Length>0&&mi.Length==mt.Length&&mi.Length==mc.Length,"recipe materials");for(int i=0;i<mi.Length;i++){Need(N(mt[i])==8&&N(mc[i])>0,"recipe resource");materials[S(mi[i])]=N(materials[S(mi[i])])+N(mc[i]);}
   var gold=new JsonObject();var powder=new JsonObject();for(int level=1;level<=9;level++){
    var quotes=ids.Select(i=>{var gear=equips[S(i)];var e=expect[$"{S(gear["expect"])}:0"];var g=growth[$"{S(gear["growthGroupId"])}:{level}"];var income=Costs(g,"breakResult");Need(N(income["gold"])==0&&N(e["level"+level])>0,"crafting yield");return (Gold:N(e["level"+level]),Powder:N(income["powder"]));}).Distinct().ToArray();Need(quotes.Length==1,"random quality changes craft costs or powder yield");gold[level.ToString()]=quotes[0].Gold;powder[level.ToString()]=quotes[0].Powder;
   }
   recipes.Add(new JsonObject{["id"]=Clone(r["id"]),["name"]=Clone(Names(r["itemNameLocalTextId"],texts)["zh-CN"]),["potions"]=costs[0],["talent_level"]=Clone(r["talentLevel"]),["materials"]=materials,["gold"]=gold,["powder"]=powder});
  }
  Need(recipes.Count>0,"no normal equipment recipes");
  var resources=new JsonObject();foreach(var r in Rows("ResourceTable"))resources[S(r["id"])]=Clone(Names(r["itemNameTextId"],names)["zh-CN"]);
  var chars=new JsonObject();var charIds=new JsonObject();foreach(var c in Rows("CharTable")){string id=S(c["uniqueId"]);if(id.Length==0)id=S(c["uniqueCharId"]);Need(id.Length>0,"character identity");charIds[S(c["id"])]=id;chars[id]=Names(c["charNameTextId"]??c["nameTextId"],names);}
  var options=new JsonObject();foreach(var o in Rows("EquipmentOptionTable"))options[$"{S(o["groupId"])}:{S(o["id"])}"]=Copy(o);
  return (new JsonObject{["schema"]=1,["source"]=new JsonObject{["mode"]="connected_client",["clientMvid"]=mvid},["equipment"]=equipment,["rank_values"]=rankValues,["refine_cost"]=refine,["recipes"]=Array(A(recipes).OrderBy(r=>N(r["id"]))),["materials"]=resources},new JsonObject{["equipment"]=displayEquipment,["characters"]=chars,["character_ids"]=charIds,["options"]=options,["translations"]=translations});
 }
}

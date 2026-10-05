using System.Globalization;
using System.Text.Json.Nodes;
using static BD2Equipment.Core.J;
namespace BD2Equipment.Core;
public static class GearView
{
 static JsonObject Display=Resource("display-catalog.json");
 public static IReadOnlyDictionary<string,string> EnglishNames {get;private set;}=new Dictionary<string,string>();
 internal static void Activate(JsonObject display){Display=display;EnglishNames=O(display["translations"]).ToDictionary(p=>p.Key,p=>S(p.Value));}
 static readonly string[] Stats=["","生命值","生命%","物攻值","物攻%","魔攻值","魔攻%","物防","魔防","暴击率","暴伤","水伤","火伤","风伤","光伤","暗伤","水抗","火抗","风抗","属性伤害","属性抗性"];
 static readonly string[] Aliases=["","生命力 HP","生命力 HP","物理攻击 攻击力 ATK","物理攻击 攻击力 ATK","魔法攻击 魔法力 MATK","魔法攻击 魔法力 MATK","防御力 防御 物抗 DEF","魔法抵抗 魔抗 MRES","暴率 暴擊率 CRIT","暴击伤害 暴擊傷害 CDMG"];
 static readonly string[] Slots=["武器","护甲","头盔","饰品","手套"],Qualities=["I","II","III","IV"],Ranks=["—","C","B","A","S"];
 static string Stat(JsonNode? o){int id=(int)N(o?["id"]);return id>0&&id<Stats.Length?Stats[id]:id.ToString();}
 static decimal D(JsonNode? n)=>n==null?0:decimal.Parse(n.ToString(),CultureInfo.InvariantCulture);
 public static string Option(JsonNode? option,JsonNode data,bool scalable){
  int id=(int)N(option?["id"]);if(id==0)return "";string name=id<Stats.Length?Stats[id]:$"词条{id}";var table=Display["options"]![$"{S(option?["groupId"],"0")}:{id}"];if(table==null)return name+"（数值未读取）";
  decimal value=D(table["defaultValue"]);
  if(scalable){int level=(int)N(data["level"]);var levels=A(table["levelValue"]).ToArray();decimal growth=level>=0&&level<levels.Length?D(levels[level]):0;var ranks=A(data["rank"]).Take(3).ToArray();for(int i=0;i<ranks.Length;i++){int rank=(int)N(ranks[i]);var values=A(table["rankValue"+(i+1)]).ToArray();if(rank>0&&rank<=values.Length)growth+=D(values[rank-1]);}value+=D(table["growthValue"])*growth;}
  bool percent=id>=2&&id<=20&&id!=3&&id!=5;value=percent?decimal.Truncate(value*10000)/100:decimal.Truncate(value);return name+" "+value.ToString("0.################",CultureInfo.InvariantCulture)+(percent?"%":"");
 }
 public static JsonArray Build(JsonObject stock,JsonObject? details=null){
  details??=new();var raw=A(details["equipment"]).ToDictionary(r=>S(r["InvenIndex"]));var chars=A(details["characters"]).ToDictionary(c=>S(c["InvenIndex"]),c=>S(Display["character_ids"]![S(c["Id"])]));
  string Name(string id)=>id is "" or "0"?"":S(Display["characters"]?[id]?["zh-CN"],"角色"+id);
  var rows=new JsonArray();foreach(var gear in A(stock["equipment"])){
   string ident=S(gear["equipment_id"]),instance=S(gear["instance"]);var def=EquipmentPlanner.Catalog["equipment"]?[ident];if(def==null)throw new InvalidOperationException("Current equipment definition missing: "+ident);var meta=O(Display["equipment"]?[ident]);var item=raw.GetValueOrDefault(instance);var data=O(item?["BaseInfo"]);string charId=S(item?["UseChar"],"0"),uid=chars.GetValueOrDefault(charId,"");string owner=uid!=""?Name(uid):B(gear["equipped"])?"角色实例 "+charId:"未穿戴",exclusive=Name(S(meta["exclusive"],"0"));
   int grade=(int)N(def["grade"]);string rarity=(grade%10) switch{1=>"N",2=>"R",3=>"SR",4=>"UR",_=>throw new InvalidOperationException("装备稀有度未识别")};int q=(int)N(meta["quality"]);string quality=Qualities[q],slot=Slots[(int)N(meta["slot"])];var names=O(meta["names"]);string name=S(names["zh-CN"],S(def["name"]));long score=EquipmentPlanner.Rank(EquipmentPlanner.Catalog,gear);string ranks=string.Join(" / ",A(gear["ranks"]).Select(v=>Ranks[(int)N(v)]));
   var main=A(data["mainOption"]).ToArray();var subs=A(data["subOption"]).ToArray();var ex=O(data["privateOption"]);string mainText=string.Join(" · ",main.Select(o=>Option(o,data,true))),privateText=Option(ex,data,true),subText=string.Join(" · ",subs.Select(o=>Option(o,data,false)));if(mainText=="")mainText="未读取";if(subText=="")subText=N(gear["level"])<3?"未解锁":"未读取";
   var cost=EquipmentPlanner.Catalog["refine_cost"]![grade.ToString()]!;var state=new List<string>{owner};if(B(gear["locked"]))state.Add("已锁定");if(B(gear["kept"]))state.Add("保留中");
   var search=new List<string>{name};search.AddRange(names.Select(v=>S(v.Value)));search.AddRange([owner,exclusive]);search.AddRange(O(Display["characters"]?[uid]).Select(v=>S(v.Value)));search.AddRange(O(Display["characters"]?[S(meta["exclusive"],"0")]).Select(v=>S(v.Value)));search.AddRange([slot,rarity,quality,rarity+(q+1),exclusive!=""?"专武 專武 专属装备":"普通装备",instance,ranks,ranks.Replace(" / ",""),score+"级",mainText,privateText,subText]);search.AddRange(main.Concat(subs).Append(ex).Select(o=>(int)N(o["id"])).Select(id=>id<Aliases.Length?Aliases[id]:""));
   var row=Select(gear,"instance","locked","kept","equipped","level");row["name"]=$"{rarity} {name} {quality}";row["base_name"]=name;row["rarity"]=rarity;row["quality"]=quality;row["slot"]=slot;row["kind"]=exclusive!=""?"专属装备":B(meta["monster_hunt"])?"魔兽装备":"普通装备";row["owner"]=owner;row["exclusive"]=exclusive!=""?exclusive:"无";row["eligible"]=N(gear["level"])==9;row["score"]=score;row["ranks"]=ranks;row["main"]=mainText;row["private"]=privateText;row["sub"]=subText;row["main_stats"]=Array(main.Select(o=>(JsonNode?)JsonValue.Create(Stat(o))));row["sub_stats"]=Array(subs.Select(o=>(JsonNode?)JsonValue.Create(Stat(o))));row["cost"]=$"{N(cost["gold"])}金币 / {N(cost["powder"])}粉";row["state"]=string.Join(" · ",state);row["search"]=string.Join(" ",search);rows.Add(row);
  }return rows;
 }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using Google.Protobuf;
using Proto.Design.common;
using UnityEngine;

namespace BD2Equipment.Live {
 internal static class EquipmentSupplyNative {
  const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
  static string quote="null",request="";
  static object Get(object o,string name){for(var t=o.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(o);var p=t.GetProperty(name,Flags|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(o,null);}throw new InvalidOperationException("Missing hunt member: "+name);}
  static string Q(string value){using(var s=new MemoryStream()){new DataContractJsonSerializer(typeof(string)).WriteObject(s,value);return Encoding.UTF8.GetString(s.ToArray());}}
  static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
  internal static string Observe(){
   if(SupplyNames.Error.Length>0)return "{\"error\":"+Q(SupplyNames.Error)+"}";
   var menu=Bridge.Find(typeof(HuntOrAirwayUI)).Cast<HuntOrAirwayUI>().SingleOrDefault();var popup=Bridge.Find(typeof(HuntDispatchPopupUI)).Cast<HuntDispatchPopupUI>().SingleOrDefault();
   var table=popup!=null?(HuntDispatchTable)Get(popup,SupplyNames.PopupTable):menu!=null?(HuntDispatchTable)Get(menu,SupplyNames.SelectionTable):null;
   return "{\"quote\":"+quote+",\"table\":"+(table==null?"null":JsonFormatter.Default.Format(table))+",\"state\":"+Q(menu==null?"":Get(menu,SupplyNames.State).ToString())+",\"count\":"+(popup==null?0:Convert.ToInt32(Get(popup,SupplyNames.Count)))+",\"freeOnly\":"+(popup!=null&&(bool)Get(Get(popup,"_buttonFreeOnly"),"IsEnable")?"true":"false")+"}";
  }
  static T[] Pack<T>(string name,int pack){
   var method=typeof(RawDataManager).GetMethods().Single(m=>m.Name=="GetTableList"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==5&&m.GetParameters()[2].ParameterType==typeof(int));var p=method.GetParameters();
   return ((IEnumerable)method.MakeGenericMethod(typeof(T)).Invoke(NativeCatalog.Manager(),new object[]{Enum.Parse(p[0].ParameterType,"DB_PACK"),name,pack,string.Empty,Enum.Parse(p[4].ParameterType,"ASC")})).Cast<T>().ToArray();
  }
  // Decode only the requested resource. Guaranteed wood and gold are exact;
  // probabilistic extras do not turn a guaranteed target into an expectation.
  static long Yield(HuntDispatchTable table,int kind,int id){
   var monsters=Pack<Proto.Design.pack1.FieldMonsterTable>("FieldMonsterTable",table.PackId).ToDictionary(r=>r.Id);
   var decks=Pack<Proto.Design.pack1.BattleDeckTable>("BattleDeckTable",table.PackId).ToDictionary(r=>r.Id);
   var boxes=NativeCatalog.Table<RandomBoxTable>("RandomBoxTable").ToDictionary(r=>r.Id);var rewards=NativeCatalog.Table<RewardGroupTable>("RewardGroupTable").ToDictionary(r=>r.Id);
   Func<int,int,int,long> item=null;
   item=(type,key,depth)=>{
    if(type==kind&&key==id)return 1;
    if(type!=9)return 0;Need(depth<5,"Nested hunt reward table is too deep");var reward=rewards[boxes[key].RewardGroupId];
    Need(reward.ItemId.Count==reward.ItemType.Count&&reward.ItemId.Count==reward.ItemCount.Count,"Hunt reward columns differ");
    var amounts=new long[reward.ItemId.Count];for(int i=0;i<amounts.Length;i++)amounts[i]=checked(reward.ItemCount[i]*item(reward.ItemType[i],reward.ItemId[i],depth+1));
    return SupplyRewardMath.Fixed(reward.DropType,reward.DropCount,reward.Ratio.ToArray(),amounts);
   };
   Func<int,long> monster=key=>{var mob=monsters[key];Need(mob.BattleDeckId.Count>0,"Missing hunt battle deck");var deck=decks[mob.BattleDeckId[0]];long value=0;for(int i=0;i<deck.RewardType.Count;i++)value=checked(value+deck.RewardCount[i]*item(deck.RewardType[i],deck.RewardId[i],0));return value;};
   int[] normal;int boss;
   if(table.TypeGroupId==0){var ground=Pack<Proto.Design.pack1.HuntingGroundTable>("HuntingGroundTable",table.PackId).Single(r=>r.Difficulty==table.Difficulty);normal=ground.MonsterId.Where(i=>i>0).ToArray();boss=ground.BossId;}
   else{var ground=NativeCatalog.Table<SkyWayFieldTable>("SkyWayFieldTable").Single(r=>r.GroupId==table.TypeGroupId&&r.Id==table.TypeId);normal=ground.MonsterId.Where(i=>i>0).ToArray();boss=ground.BossId;}
   Need(normal.Length>0&&normal.Length==table.BattleCountMonster,"Hunt monster count differs from dispatch table");return checked(normal.Sum(monster)+(boss>0?monster(boss)*table.BattleCountBoss:0));
  }
  internal static void Execute(Command command,UIBase ui){
   Need(SupplyNames.Error.Length==0,SupplyNames.Error);
   if(command.Kind=="equipment_hunt_select"){
    quote="null";request="";
    Need(command.Value==9&&command.Items[0]==0||command.Value==3004&&command.Items[0]==1,"Unsupported supply destination");
    ((HuntOrAirwayUI)ui).SelectWorldMapPin(command.Value,(int)command.Items[0]);return;
   }
   var popup=(HuntDispatchPopupUI)ui;var table=(HuntDispatchTable)Get(popup,SupplyNames.PopupTable);
   Need(table.TypeGroupId==0&&table.PackId==9||table.TypeGroupId==1&&table.PackId==3004,"Supply route changed");
   if(command.Kind=="equipment_hunt_options"){
    var toggle=Get(popup,"_buttonFreeOnly");if((bool)Get(toggle,"IsEnable"))popup.OnClickUI((GameObject)Get(toggle,"_objectRoot"));
    // Native OnValueChanged is the same count callback used by its slider.
    popup.OnValueChanged(command.Value);
    Need(Convert.ToInt32(Get(popup,SupplyNames.Count))==command.Value,"Hunt count was not applied");return;
   }
   request=command.Id;quote="null";string bound=request;
   var loader=typeof(UIBase).Assembly.GetType(SupplyNames.LoaderType,true).GetMethod(SupplyNames.Loader,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
   loader.Invoke(null,new object[]{table.PackId,(Action)(()=>{
    if(request!=bound||popup==null)return;
    try{var current=(HuntDispatchTable)Get(popup,SupplyNames.PopupTable);Need(current.GroupId==table.GroupId&&current.Id==table.Id,"Hunt selection changed during quote");
     int bonus=table.TypeGroupId==1?Convert.ToInt32(Get(popup,SupplyNames.Bonus)):0;
     long gold=checked(Yield(table,4,0)*(100+bonus))/100,wood=table.TypeGroupId==0?Yield(table,8,501):0;
     quote="{\"request\":"+Q(bound)+",\"available\":true,\"fixed\":true,\"bonus\":"+bonus+",\"table\":"+JsonFormatter.Default.Format(table)+",\"ap\":"+table.ApPerTime+",\"wood\":"+wood+",\"gold\":"+gold+"}";
    }catch(Exception ex){quote="{\"request\":"+Q(bound)+",\"error\":"+Q(ex.GetBaseException().Message)+"}";}
   })});
  }
 }
}

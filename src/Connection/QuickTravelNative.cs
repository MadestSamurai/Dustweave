using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections.Generic;
using UnityEngine;
using gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class QuickTravelTarget {
  [DataMember] public int Npc,Map;
  [DataMember] public string Name="";
  [DataMember] public float X,Y,Z;
  [DataMember] public bool AtTarget;
 }
 [DataContract] internal sealed class QuickTravelState {
  [DataMember] public int Pack,Map,Npc;
  [DataMember] public string State="idle",Error="";
  [DataMember] public QuickTravelTarget[] Targets=new QuickTravelTarget[0];
 }
 // MiniMapAutoMoveItem's NPC/QuikAutoMove==1 path, not quest auto-walking.
 // Only native quick-list entries are eligible; no arbitrary coordinates or maps.
 internal static class QuickTravelNative {
  const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static Type objectType;
  static FieldInfo itemId,itemType;
  static MethodInfo destination,nearby;
  static void Resolve(){
   if(nearby!=null)return;
   objectType=typeof(GameFieldManager).GetMethod("MoveAutoMoveTarget").GetParameters()[1].ParameterType;
   itemId=typeof(MiniMapAutoMoveItem).GetFields(Members).Single(x=>x.FieldType==typeof(int));
   itemType=typeof(MiniMapAutoMoveItem).GetFields(Members).Single(x=>x.FieldType==objectType);
   destination=Find(typeof(int),typeof(int).MakeByRefType(),typeof(Vector3).MakeByRefType());
   nearby=Find(typeof(int),typeof(float));
  }
  static string state="idle";static int requestedNpc;static long generation;
  static MethodInfo Find(params Type[] parameters){return typeof(GameFieldManager).GetMethods(Members).Single(m=>m.ReturnType==typeof(bool)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters));}
  static GameObject Object(object owner,string name){return (GameObject)owner.GetType().GetField(name,Members).GetValue(owner);}
  internal static QuickTravelState Capture(){
   var result=new QuickTravelState{State=state,Npc=requestedNpc};
   try {
    // An optional shortcut changing in a later client must not break ordinary
    // field observations through a failed static type initializer.
    Resolve();
    var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var map=field.ὮὬὬὮὠὮὪὠὧὩὪ;
    if(map==null)return result;result.Pack=map.PackId;result.Map=map.Id;
    var ui=Bridge.Find(typeof(GameFieldDefaultUI)).Cast<GameFieldDefaultUI>().SingleOrDefault();
    if(ui==null)return result;
    var button=ui.GetComponentsInChildren<MiniMapAutoMoveQuickListButton>(true).SingleOrDefault();
    if(button==null||!Object(button,"_objRoot").activeInHierarchy||!Object(button,"_objEnable").activeSelf)return result;
    var raw=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
    var table=raw.GetMapUIObjectDTOs(map.PackId);
    var targets=new List<QuickTravelTarget>();
    foreach(var item in ui.GetComponentsInChildren<MiniMapAutoMoveItem>(true)){
     if(!item.gameObject.activeSelf||itemType.GetValue(item).ToString()!="NPC")continue;
     int npc=(int)itemId.GetValue(item);
     var native=table.FirstOrDefault(t=>t.Id==npc&&t.Type==Convert.ToInt32(Enum.Parse(objectType,"NPC"))&&t.QuikAutoMove==1);
     if(native==null||targets.Any(t=>t.Npc==npc))continue;
     object[] args={npc,0,Vector3.zero};
     if(!(bool)destination.Invoke(field,args))continue;
     var position=(Vector3)args[2];int mapId=(int)args[1];
     // Follow the game's own expanded interaction-area check: nearby quick travel
     // is a no-op, even when the normal interaction indicator is not lit yet.
     bool atTarget=mapId==map.Id&&(bool)nearby.Invoke(field,new object[]{npc,3f});
     var title=Object(item,"_goNormal").GetComponentInChildren<TMPro.TMP_Text>(true);
     targets.Add(new QuickTravelTarget{Npc=npc,Map=mapId,Name=title==null?"":title.text,X=position.x,Y=position.y,Z=position.z,AtTarget=atTarget});
    }
    result.Targets=targets.ToArray();
   }catch(Exception error){result.Error=error.GetType().Name+": "+error.Message;}
   return result;
  }
  internal static string Preflight(Command command){
   var snapshot=Capture();
   return snapshot.Targets.Any(x=>x.Npc==command.Value&&x.Map==command.Items[0]&&!x.AtTarget)?"":"quick_travel_unavailable";
  }
  internal static void Execute(Command command){
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);
   Bridge.Find(typeof(GameFieldDefaultUI)).Cast<GameFieldDefaultUI>().Single().SetActiveAutoMoveList(false);
   requestedNpc=command.Value;state="moving";long current=++generation;
   field.MoveMapNpcPosition(command.Value,()=>{if(current==generation)state="arrived";});
  }
 }
}

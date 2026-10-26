using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Proto.Design.common;
using gamfs;
using UnityEngine;
using UnityEngine.UI;
namespace BD2Daily.Live {
 [DataContract] internal sealed class DailyCraftRecipe {
  [DataMember] public int Id;
  [DataMember] public int[] Equipment;
 }
 internal static class EquipmentToolsNative {
  static Dictionary<int,DailyCraftRecipe> normalRecipes;
  static Dictionary<int,DailyCraftRecipe> NormalRecipes(){
   if(normalRecipes!=null)return normalRecipes;
   var data=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   var recipes=data.GetTableList<EquipmentMakingTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"EquipmentMakingTable");
   var boxes=data.GetTableList<RandomBoxTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"RandomBoxTable").ToDictionary(x=>x.Id);
   var rewards=data.GetTableList<RewardGroupTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"RewardGroupTable").ToDictionary(x=>x.Id);
   var equipment=data.GetTableList<EquipmentTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"EquipmentTable").ToDictionary(x=>x.Id);
   var result=new Dictionary<int,DailyCraftRecipe>();
   foreach(var recipe in recipes){
    RandomBoxTable box;RewardGroupTable reward;
    if(recipe.ResultItemType!=9||recipe.ResultItemCount!=1||!boxes.TryGetValue(recipe.ResultItemId,out box)||!rewards.TryGetValue(box.RewardGroupId,out reward))continue;
    if(reward.ItemId.Count==0||reward.ItemId.Count!=reward.ItemType.Count||reward.ItemType.Any(t=>t!=10)||reward.ItemId.Any(id=>!equipment.ContainsKey(id)||equipment[id].Grade!=1))continue;
    result.Add(recipe.Id,new DailyCraftRecipe{Id=recipe.Id,Equipment=reward.ItemId.Distinct().ToArray()});
   }
   return normalRecipes=result; // Client definitions are immutable; never cache account/material availability.
  }
  public static DailyCraftRecipe[] CraftRecipes(){
   var menu=UnityEngine.Object.FindObjectsOfType<EquipmentMakingSelectUI>().SingleOrDefault(ui=>ui.gameObject.activeInHierarchy);
   if(menu==null)return null;
   var recipes=NormalRecipes();
   return menu.EquipmentMakingDatas.Select(x=>x.ὫὮὩὬὤὫὮὨὢὯὬ.Id).Distinct().Where(recipes.ContainsKey).OrderBy(id=>id).Select(id=>recipes[id]).ToArray();
  }
  public static void PreviewCraft(EquipmentMakingSelectUI menu,int id){
   Require(NormalRecipes().ContainsKey(id),"Only N equipment recipes are supported");
   menu.OnEquipmentMakingUI(menu.EquipmentMakingDatas.Single(x=>x.ὫὮὩὬὤὫὮὨὢὯὬ.Id==id));
  }
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static object Get(object o,string n){var t=o.GetType();while(t!=null){var f=t.GetField(n,Flags);if(f!=null)return f.GetValue(o);var p=t.GetProperty(n,Flags);if(p!=null)return p.GetValue(o,null);t=t.BaseType;}throw new MissingFieldException(n);}
  static void Require(bool b,string message){if(!b)throw new InvalidOperationException(message);}
  public static bool Busy(){return UnityEngine.Object.FindObjectsOfType<EquipmentUpgradeUI>().Any(ui=>ui.gameObject.activeInHierarchy&&(bool)Get(ui,"ὢὮὫὪὩὨὥὥὬὪὪ"));}
  public static void Execute(Command c,UIBase ui){
   if(c.Kind=="equipment_refine_batch"){
    var upgrade=(EquipmentUpgradeUI)ui;
    var gear=Get(upgrade,"ὢὠὡὡὫὤὮὫὢὢὪ");var info=Get(gear,"BaseInfo");
    Require(Convert.ToInt64(Get(gear,"InvenIndex"))==c.Items[0]&&Convert.ToInt32(Get(info,"Level"))==9,"Selected +9 equipment changed");
    Require(Get(upgrade,"ὯὢὪὬὠὧὥὠὤὩὦ").ToString()=="SMELT"&&!(bool)Get(upgrade,"ὢὮὫὪὩὨὥὥὬὪὪ")&&(bool)Get(upgrade,"ὠὠὡὭὧὫὫὭὥὣὯ"),"Native refinement not ready");
    var model=Get(upgrade,"ὭὥὢὮὧὨὮὧὢὢὥ");
    var table=ὭὧὫὡὠὭὨὡὪὧὮ.ὦὦὤὡὭὡὭὮὨὭὩ(Convert.ToInt32(Get(model,"ὩὢὠὪὦὨὭὨὨὪὬ")));
    long gold=0,powder=0;
    for(int i=0;i<table.RankSmeltItemType.Count;i++){
     int type=table.RankSmeltItemType[i],id=table.RankSmeltItemId[i];
     long cost=(long)table.RankSmeltItemCount[i]*c.Items[1];
     Require(cost>0,"Unexpected refinement cost");
     if(type==4)gold+=cost;
     else {Require(((ὥὯὯὠὣὪὦὢὫὠὮ)type).ToString()=="Resource"&&id==10,"Unexpected refinement resource");powder+=cost;}
     Require(ὣὡὧὡὦὣὣὬὨὪὫ.ὧὮὢὬὢὬὠὥὡὡὬ((ὥὯὯὠὣὪὦὢὫὠὮ)type,id)>=cost,"Insufficient resources for approved batch");
    }
    Require(gold==c.Items[2]&&powder==c.Items[3],"Native cost differs from confirmed batch budget");
    // Positive target enters native sequence mode even for a tail batch of one.
    // The native driver splits a <=5000 batch into <=1000-request chunks.
    upgrade.StartAutoProgress((int)c.Items[1],true,c.Value);return;
   }
   if(c.Kind=="powder_recipe"){
    var menu=(EquipmentMakingSelectUI)ui;
    var r=menu.EquipmentMakingDatas.Single(x=>x.ὫὮὩὬὤὫὮὨὢὯὬ.Id==c.Value);
    Require(c.Value>=1&&c.Value<=10,"Only current N recipes are supported");
    menu.OnEquipmentMakingUI(r);return;
   }
   var popup=(EquipmentUpgradePopupUI)ui;
   Require((bool)Get(popup,"ὣὦὣὤὨὣὧὣὦὥὯ"),"Crafting configuration required, not inventory recycle");
   if(!(bool)Get(popup,"ὬὭὪὣὣὨὭὭὩὯὨ"))popup.OnClickUI((GameObject)Get(popup,"_autoBreakToggleButton"));
   var level=(Slider)Get(popup,"_upgradeSlider");var quantity=(Slider)Get(popup,"_goldSlider_auto");
   Require(c.Value>=level.minValue&&c.Value<=level.maxValue,"Enhancement outside native range");
   level.value=c.Value;
   Require(quantity.maxValue>=1&&quantity.maxValue<int.MaxValue,"Native crafting capacity unavailable");
   int count=(int)Math.Min(c.Items[0],(long)quantity.maxValue);
   Require(count>=quantity.minValue,"Quantity outside native range");
   quantity.value=count;
   Require((int)Get(popup,"ὬὠὬὡὫὤὮὨὦὤὥ")==c.Value&&(int)Get(popup,"ὧὤὯὭὨὨὦὥὡὦὫ")==count&&(bool)Get(popup,"ὬὭὪὣὣὨὭὭὩὯὨ"),"Native crafting preview mismatch");
  }
 }
}

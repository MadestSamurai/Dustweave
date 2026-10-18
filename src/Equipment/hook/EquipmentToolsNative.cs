using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace BD2Equipment.Live {
 internal static class EquipmentToolsNative {
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
    Require(NativeCatalog.IsNormalRecipe(c.Value),"Only current N recipes are supported");
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

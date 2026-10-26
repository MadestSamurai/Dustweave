using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace BD2Daily.Live {
 internal static class UiInteractionReadiness {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
  static object Field(object value,string name){
   for(var type=value.GetType();type!=null;type=type.BaseType){var field=type.GetField(name,Flags);if(field!=null)return field.GetValue(value);}
   return null;
  }
  // A scroll's origin item is an authoring template, not a bound mission/item.
  // Active clones can also still belong to the pool or be loading their data.
  internal static bool TargetReady(UIBase owner,GameObject target){
   var row=target.GetComponentInParent<LoopScrollItemBase>();
   if(row==null)return true;
   var scroll=Field(row,"_loopScrollRect") as LoopScrollRect;
   return scroll!=null&&scroll.scrollData!=null&&row!=scroll.scrollData._originItem&&
    !row.IsPooled&&row.LoadComplete&&ReferenceEquals(Field(row,"_uiBase"),owner);
  }
  internal static bool MissionReady(UIBase ui){
   if(!(ui is TarosTacticsMissionPopupUI))return true;
   try{
    if(!ui.CanCloseUI())return false;
    var scroll=Field(ui,"_scrollMission") as LoopScrollRect;
    var property=ui.GetType().GetProperties(Flags).SingleOrDefault(p=>typeof(IList).IsAssignableFrom(p.PropertyType)&&p.GetIndexParameters().Length==0);
    var data=property==null?null:property.GetValue(ui,null) as IList;
    // Init and every reward callback clear the cells, then AddItem after two
    // FixedUpdates. UIBase's input flag is already true during that gap.
    if(scroll==null||data==null||scroll.totalCount!=data.Count)return false;
    if(data.Count==0)return true;
    var dataType=property.PropertyType.GetGenericArguments().Single();
    var rows=scroll.GetComponentsInChildren<TarosTacticsMissionPopupUIScrollItem>(false)
     .Where(row=>row!=null&&row.gameObject.activeInHierarchy&&scroll.scrollData!=null&&row!=scroll.scrollData._originItem).ToArray();
    if(rows.Length==0)return false;
    foreach(var row in rows){
     if(!TargetReady(ui,row.gameObject))return false;
     var binding=row.GetType().GetFields(Flags).SingleOrDefault(f=>f.FieldType==dataType);
     var item=binding==null?null:binding.GetValue(row);
     if(item==null||!data.Cast<object>().Any(value=>ReferenceEquals(value,item)))return false;
    }
    return true;
   }catch(Exception){return false;}
  }
 }
}
